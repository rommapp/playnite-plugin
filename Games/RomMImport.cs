using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using RomM.Models.RomM.Rom;
using RomM.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RomM.Games
{
    internal class RomMImport
    {
        private readonly RomM _plugin;
        LibraryImportGamesArgs _args;
        EmulatorMapping _mapping;
        List<RomMRom> _ROMs;
        Dictionary<int, RomMRom> _romById;
        // Snapshots of this plugin's games, indexed so the per-ROM lookups are O(1) instead of a
        // full Database.Games scan each. Kept in sync as games are migrated/removed during import.
        Dictionary<string, Game> _existingGames;       // keyed by new-format "romMId:sha1" GameId
        Dictionary<string, Game> _existingGamesBySha1; // keyed by sha1
        Dictionary<string, Game> _legacyGames;         // unmigrated "!0..." games, keyed by GameId
        Dictionary<string, Guid> _completionStatusMap;
        List<int> _favourites;

        public RomMImport(RomM plugin, LibraryImportGamesArgs args, EmulatorMapping mapping, List<RomMRom> roms, List<int> favourites)
        {
            _plugin = plugin;
            _args = args;
            _mapping = mapping;
            _ROMs = roms;

            // Index ROMs by id once so sibling lookups are O(1) instead of List.Find per sibling per ROM.
            _romById = new Dictionary<int, RomMRom>();
            foreach (var rom in roms)
                _romById[rom.Id] = rom;

            // Index the existing library once so the per-ROM "already imported?" / migration lookups
            // don't each scan the whole Games collection.
            _existingGames = new Dictionary<string, Game>();
            _existingGamesBySha1 = new Dictionary<string, Game>();
            _legacyGames = new Dictionary<string, Game>();
            foreach (var g in plugin.Playnite.Database.Games)
            {
                if (g.PluginId != plugin.Id || g.GameId == null)
                    continue;

                if (RomMGameId.TryParse(g.GameId, out int _, out string sha1))
                {
                    _existingGames[g.GameId] = g;
                    _existingGamesBySha1[sha1] = g;
                }
                else
                {
                    _legacyGames[g.GameId] = g;
                }
            }

            _completionStatusMap = plugin.Playnite.Database.CompletionStatuses.ToDictionary(cs => cs.Name, cs => cs.Id);
            _favourites = favourites;
        }

        private RomMFile DetermineFile(RomMRom ROM)
        {
            if(ROM.Files.Count == 0)
                return null;

            if(ROM.Files.Count > 1)
            {
                // Return the file that sits highest in the folder tree (fewest path separators).
                return ROM.Files.OrderBy(f => (f.FullPath ?? string.Empty).Count(c => c == '/')).FirstOrDefault();
            }

            return ROM.Files.FirstOrDefault();
        }

        // Builds the install descriptor for a single ROM (base or sibling). Returns null when a
        // single-file ROM has no resolvable file. Single files use the 4.9 /files/content endpoint,
        // multi-file ROMs download the whole rom as an archive.
        private RomMRevision BuildRevision(RomMRom rom)
        {
            var revision = new RomMRevision
            {
                Id = rom.Id,
                HasMultipleFiles = rom.HasMultipleFiles,
                IsSelected = false
            };

            if (!rom.HasMultipleFiles)
            {
                var romfile = DetermineFile(rom);
                if (romfile == null)
                    return null;

                revision.FileName = romfile.FileName;
                // The 4.9 single-file endpoint needs the file id; fall back to the rom-level endpoint
                // when the payload doesn't include one so we never emit "api/roms//files/content/...".
                revision.DownloadURL = romfile.Id.HasValue
                    ? _plugin.CombineUrl(_plugin.Settings.RomMHost, $"api/roms/{romfile.Id}/files/content/{romfile.FileName}")
                    : _plugin.CombineUrl(_plugin.Settings.RomMHost, $"api/roms/{rom.Id}/content/{romfile.FileName}");
            }
            else
            {
                revision.FileName = rom.FileName;
                revision.DownloadURL = _plugin.CombineUrl(_plugin.Settings.RomMHost, $"api/roms/{rom.Id}/content/{rom.FileName}");
            }

            return revision;
        }

        // Main library import functions
        public List<Game> ProcessData()
        {
            var games = new List<Game>();
            var importedGameIds = new HashSet<string>();
            _plugin.PlayniteApi.Database.Platforms.Add(_mapping.RomMPlatform.Name);

            // Batch the add/update/remove writes so Playnite raises a single update pass instead of
            // per-item churn for the whole platform.
            using (_plugin.Playnite.Database.BufferedUpdate())
            {
                foreach (var ROM in _ROMs)
                {
                    if (_args.CancelToken.IsCancellationRequested)
                        break;

                    // RomM 4.9+ can omit these fields entirely; normalise so the rest of the importer
                    // never has to null-check (mirrors the hardening done in #107).
                    ROM.Normalize();

                    // Some newer platforms don't get a hash value so we will compromise with this
                    if (string.IsNullOrEmpty(ROM.SHA1))
                    {
                        var tohash = $"{ROM.Id}{ROM.FileNameNoExt}";

                        using (SHA1Managed sha1 = new SHA1Managed())
                        {
                            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(tohash));
                            var sb = new StringBuilder(hash.Length * 2);

                            foreach (byte b in hash)
                            {
                                sb.Append(b.ToString("x2"));
                            }

                            ROM.SHA1 = sb.ToString();
                        }
                    }

                    // Skip game import if the ROM is part of the exclusion list
                    if (_plugin.Playnite.Database.ImportExclusions[Playnite.ImportExclusionItem.GetId($"{ROM.Id}:{ROM.SHA1}", _plugin.Id)] != null)
                    {
                        _plugin.Logger.Warn($"[Importer] Excluding {ROM.Name} from import.");
                        continue;
                    }

                    // Skip if ROM has no filename
                    if (string.IsNullOrEmpty(ROM.FileName))
                    {
                        _plugin.Playnite.Notifications.Add(new NotificationMessage(_plugin.Id.ToString(), $"Filename for ROM ID: {ROM.Id} doesn't exist!\nDoes ROM exist on the servers filesystem?", NotificationType.Error));
                        continue;
                    }

                    // Fail-safe incase none of these are set to true
                    if (!ROM.HasSimpleSingleFile & !ROM.HasNestedSingleFile & !ROM.HasMultipleFiles)
                        ROM.HasMultipleFiles = true;

                    // Migrate old RomMGameInfo id to new romMId:SHA1 id (no-op once everything's migrated)
                    string gameID = $"{ROM.Id}:{ROM.SHA1}";
                    UpdatedOldGameID(ROM);

                    // Merging revisions: drop the games for siblings we're folding into the main entry.
                    if (_plugin.Settings.MergeRevisions && ROM.Siblings?.Count > 0)
                    {
                        if (CheckForMainSibling(ROM) == MainSibling.Other || ROM.Processed)
                        {
                            RemoveExistingGame(gameID);
                            continue;
                        }
                    }

                    // Save Game ROM data to file
                    SaveGameData(ROM);

                    // Skip full import if ROM has already been imported
                    Guid statusID = Guid.Empty;
                    if (_existingGames.TryGetValue(gameID, out var existingGame))
                    {
                        // Sync user data
                        if (_plugin.Settings.KeepRomMSynced)
                        {
                            statusID = DetermineCompletionStatus(ROM);
                            existingGame.Favorite = _favourites.Exists(f => f == ROM.Id);
                            if (statusID != Guid.Empty)
                                existingGame.CompletionStatusId = statusID;

                            // This is our own write of the server's values; don't let OnItemUpdated echo it back.
                            _plugin.SuppressSync(existingGame.Id);
                            _plugin.Playnite.Database.Games.Update(existingGame);
                        }

                        importedGameIds.Add(gameID);
                        continue;
                    }

                    // If keep deleted games is enabled and a deleted game gets re-added back to the
                    // server under a new romMId, update the existing playnite entry instead.
                    if (_plugin.Settings.KeepDeletedGames && UpdatedDeletedGame(ROM))
                    {
                        importedGameIds.Add(gameID);
                        continue;
                    }

                    var importedGame = ImportGame(ROM, statusID);
                    if (importedGame != null)
                    {
                        games.Add(importedGame);
                        importedGameIds.Add(gameID);
                    }
                    else
                    {
                        _plugin.Logger.Error($"[Importer] Failed to import RomM GameID: {ROM.Id}");
                    }
                }

                _plugin.Logger.Info($"[Importer] Finished adding new games for {_mapping.RomMPlatform.Name}");

                if (!_plugin.Settings.KeepDeletedGames)
                {
                    RemoveMissingGames(importedGameIds);
                }
            }

            return games;
        }

        private Game ImportGame(RomMRom ROM, Guid StatusID)
        {
            var rootInstallDir = _plugin.Playnite.Paths.IsPortable
                        ? _mapping.DestinationPathResolved.Replace(_plugin.Playnite.Paths.ApplicationPath, ExpandableVariables.PlayniteDirectory)
                        : _mapping.DestinationPathResolved;

            // Paths must be derived from the actual ROM file (what RomMInstallController downloads),
            // not the display Name. Using Name drops the extension and can include characters that
            // don't match the installed file, breaking IsInstalled detection and the play path.
            var baseRevision = BuildRevision(ROM);
            var fileName = !string.IsNullOrEmpty(baseRevision?.FileName) ? baseRevision.FileName : ROM.Name;
            var gameInstallDir = Path.Combine(rootInstallDir, Path.GetFileNameWithoutExtension(fileName));
            var pathToGame = Path.Combine(gameInstallDir, fileName);

            var status = _plugin.Playnite.Database.CompletionStatuses.Get(StatusID);
            var completionStatusProperty = status != null ? new MetadataNameProperty(status.Name) : null;

            var metadata = RomMMetadataMapper.BuildBaseMetadata(ROM, _plugin.Settings.RomMHost, _plugin.Playnite.ApplicationSettings.AgeRatingOrgPriority.ToString());

            metadata.Source = _plugin.Source;
            metadata.GameId = $"{ROM.Id}:{ROM.SHA1}";
            metadata.Platforms = new HashSet<MetadataProperty> { new MetadataNameProperty(_mapping.RomMPlatform.Name ?? "") };
            metadata.Favorite = _favourites.Exists(f => f == ROM.Id);
            metadata.CompletionStatus = completionStatusProperty;
            metadata.Roms = new List<GameRom> { new GameRom(ROM.FileNameNoExt, pathToGame) };
            metadata.InstallDirectory = gameInstallDir;
            metadata.IsInstalled = File.Exists(pathToGame);
            metadata.InstallSize = ROM.FileSizeBytes;
            metadata.GameActions = new List<GameAction>
            {
                new GameAction
                {
                    Name = $"Play in {_mapping.Emulator.Name}",
                    Type = GameActionType.Emulator,
                    EmulatorId = _mapping.EmulatorId,
                    EmulatorProfileId = _mapping.EmulatorProfileId,
                    IsPlayAction = true,
                },
                new GameAction
                {
                    Type = GameActionType.URL,
                    Name = "View in RomM",
                    Path = _plugin.CombineUrl(_plugin.Settings.RomMHost, $"rom/{ROM.Id}"),
                    IsPlayAction = false
                }
            };

            // Import new game
            Game game = _plugin.Playnite.Database.ImportGame(metadata, _plugin);

            if (ROM.HasManual && !string.IsNullOrEmpty(ROM.ManualPath))
            {
                game.Manual = $"{_plugin.Settings.RomMHost}/assets/romm/resources/{ROM.ManualPath}";
                // ImportGame returns a persisted entity; the manual is set afterwards so it has to be saved explicitly.
                _plugin.Playnite.Database.Games.Update(game);
            }

            return game;
        }

        // Removes the playnite game for the given id (if present) and keeps the indexes consistent.
        private void RemoveExistingGame(string gameID)
        {
            if (_existingGames.TryGetValue(gameID, out var existing))
            {
                _plugin.Playnite.Database.Games.Remove(existing.Id);
                _existingGames.Remove(gameID);
                if (RomMGameId.TryParse(gameID, out int _, out string sha1))
                    _existingGamesBySha1.Remove(sha1);
            }
        }

        private void RemoveMissingGames(HashSet<string> importedGameIds)
        {
            // Snapshot before mutating; removing while enumerating the live query is unsafe.
            var gamesInDatabase = _plugin.Playnite.Database.Games.Where(g =>
                        g.Source != null && g.Source.Name == _plugin.Source.ToString() &&
                        g.Platforms != null && g.Platforms.Any(p => p.Name == _mapping.RomMPlatform.Name)
                    ).ToList();

            _plugin.Logger.Info($"[Importer] Starting to remove not found games for {_mapping.RomMPlatform.Name}.");

            foreach (var game in gamesInDatabase)
            {
                if (_args.CancelToken.IsCancellationRequested)
                    break;

                if (importedGameIds.Contains(game.GameId))
                {
                    continue;
                }

                _plugin.Playnite.Database.Games.Remove(game.Id);
                _plugin.Logger.Info($"[Importer] Removing {game.Name} - {game.Id} for {_mapping.RomMPlatform.Name}");
            }

            _plugin.Logger.Info($"[Importer] Finished removing not found games for {_mapping.RomMPlatform.Name}");
        }

        private bool UpdatedOldGameID(RomMRom ROM)
        {
            // Nothing to migrate once every legacy "!0..." game is gone (the common case), so skip the
            // protobuf id reconstruction entirely.
            if (_legacyGames.Count == 0)
                return false;

            if (ROM.Files.Count == 0)
            {
                _plugin.Logger.Warn($"[Importer] Rom {ROM.Id} has no files, skipping check for updating game id.");
                return false;
            }

            var singleRomFile = ROM.Files.Where(f => (f.FullPath ?? string.Empty).Count(c => c == '/') <= 3).FirstOrDefault();
            var filename = ROM.HasMultipleFiles ? Path.GetFileName(ROM.FileName) : Path.GetFileName(singleRomFile?.FileName);
            if (string.IsNullOrWhiteSpace(filename))
            {
                _plugin.Logger.Warn($"[Importer] Rom {ROM.Id} returned empty/invalid filename, skipping check for updating game id.");
                return false;
            }
            var info = new RomMGameInfo
            {
                MappingId = _mapping.MappingId,
                FileName = filename,
                DownloadUrl = _plugin.CombineUrl(_plugin.Settings.RomMHost, $"api/roms/{ROM.Id}/content/{filename}"),
                HasMultipleFiles = ROM.HasMultipleFiles
            };

            var legacyId = info.AsGameId();
            if (_legacyGames.TryGetValue(legacyId, out var oldgame))
            {
                var newId = $"{ROM.Id}:{ROM.SHA1}";
                oldgame.GameId = newId;
                oldgame.PlatformIds = new List<Guid> { _plugin.Playnite.Database.Platforms.First(x => x.Name == _mapping.RomMPlatform.Name).Id };
                _plugin.Playnite.Database.Games.Update(oldgame);

                // Keep indexes consistent so the already-imported check finds the just-migrated game.
                _legacyGames.Remove(legacyId);
                _existingGames[newId] = oldgame;
                _existingGamesBySha1[ROM.SHA1] = oldgame;
                return true;
            }

            return false;
        }

        private bool UpdatedDeletedGame(RomMRom ROM)
        {
            // A game with the same SHA1 but a different romMId means RomM deleted and re-added it; adopt
            // the existing entry under the new id.
            if (_existingGamesBySha1.TryGetValue(ROM.SHA1, out var oldgame))
            {
                var oldId = oldgame.GameId;
                var newId = $"{ROM.Id}:{ROM.SHA1}";
                oldgame.GameId = newId;
                _plugin.Playnite.Database.Games.Update(oldgame);

                _existingGames.Remove(oldId);
                _existingGames[newId] = oldgame;
                return true;
            }

            return false;
        }

        private MainSibling CheckForMainSibling(RomMRom ROM)
        {
            //Check to see if ROM is the main sibling
            if (ROM.RomUser.IsMainSibling)
                return MainSibling.Current;

            //Find if there is a main sibling
            foreach (var sibling in ROM.Siblings)
            {
                // The sibling may live on a different page and not be present in this batch.
                if (_romById.TryGetValue(sibling.Id, out var siblingROM) && siblingROM.RomUser != null && siblingROM.RomUser.IsMainSibling)
                {
                    return MainSibling.Other;
                }
            }

            return MainSibling.None;
        }

        private void SaveGameData(RomMRom ROM)
        {
            RomMRomLocal toSave = new RomMRomLocal
            {
                Name = ROM.Name,
                SHA1 = ROM.SHA1,
                MappingID = _mapping.MappingId,
                ROMVersions = new List<RomMRevision>()
            };

            // Save base ROM data
            var baseROM = BuildRevision(ROM);
            if (baseROM == null)
            {
                _plugin.Logger.Error("[Importer] Unable to save ROM data as there is no rom file!");
                return;
            }
            toSave.ROMVersions.Add(baseROM);

            // Save sibling data
            if (_plugin.Settings.MergeRevisions && ROM.Siblings?.Count > 0)
            {
                foreach (var sibling in ROM.Siblings)
                {
                    if (!_romById.TryGetValue(sibling.Id, out var siblingROM))
                        continue;

                    var saveSibling = BuildRevision(siblingROM);
                    if (saveSibling == null)
                    {
                        _plugin.Logger.Error("[Importer] Unable to save sibling ROM data as there is no rom file!");
                        continue;
                    }

                    siblingROM.Processed = true;
                    toSave.ROMVersions.Add(saveSibling);
                }
            }

            // Carry over the user's previously selected version and only rewrite when something changed.
            string sidecarPath = $"{_plugin.ROMDataPath}{ROM.SHA1}.json";
            string existingJson = null;
            if (File.Exists(sidecarPath))
            {
                try
                {
                    existingJson = File.ReadAllText(sidecarPath);
                    var localROM = JsonConvert.DeserializeObject<RomMRomLocal>(existingJson);
                    foreach (var revision in localROM?.ROMVersions ?? new List<RomMRevision>())
                    {
                        var matchedRevision = toSave.ROMVersions.FirstOrDefault(x => x.Id == revision.Id);
                        if (matchedRevision != null)
                            matchedRevision.IsSelected = revision.IsSelected;
                    }
                }
                catch (Exception)
                {
                    _plugin.Logger.Error($"{ROM.Name} GameID is malformed or {ROM.SHA1} json file is corrupted!");
                }
            }

            string json = JsonConvert.SerializeObject(toSave);
            if (json != existingJson)
                File.WriteAllText(sidecarPath, json);
        }

        private Guid DetermineCompletionStatus(RomMRom ROM)
        {
            string completionStatus;
            // Determine status in Playnite. Backlogged and "now playing" take precedent over the status options
            if (ROM.RomUser.Backlogged || ROM.RomUser.NowPlaying)
            {
                completionStatus = ROM.RomUser.NowPlaying ? RomMRomUser.CompletionStatusMap["now_playing"] : RomMRomUser.CompletionStatusMap["backlogged"];
            }
            else
            {
                // RomM may report a status value we don't map; fall back instead of throwing KeyNotFoundException.
                var romMStatus = ROM.RomUser.Status ?? "not_played";
                if (!RomMRomUser.CompletionStatusMap.TryGetValue(romMStatus, out completionStatus))
                {
                    completionStatus = RomMRomUser.CompletionStatusMap["not_played"];
                }
            }

            _completionStatusMap.TryGetValue(completionStatus, out var statusId);
            return statusId;
        }
    }
}
