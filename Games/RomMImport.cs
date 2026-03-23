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
        Dictionary<string, Guid> _completionStatusMap;
        List<int> _favourites;

        public RomMImport(RomM plugin, LibraryImportGamesArgs args, EmulatorMapping mapping, List<RomMRom> roms, List<int> favourites)
        {
            _plugin = plugin;
            _args = args;
            _mapping = mapping;
            _ROMs = roms;
            _completionStatusMap = plugin.Playnite.Database.CompletionStatuses.ToDictionary(cs => cs.Name, cs => cs.Id);
            _favourites = favourites;
        }

        private RomMFile DetermineFile(RomMRom ROM)
        {
            if(ROM.Files.Count == 0)
                return null;

            if(ROM.Files.Count > 1)
            {
                List<string> fullpaths = new List<string>();
                foreach (var file in ROM.Files)
                {
                    fullpaths.Add(file.FullPath);
                }

                fullpaths = fullpaths.OrderBy(x => x.Count(c => c == '/')).ToList();
                return ROM.Files.Where(x => x.FullPath == fullpaths[0]).FirstOrDefault();
            }

            return ROM.Files.FirstOrDefault();
        }

        // Main library import functions
        public List<Game> ProcessData()
        {
            var games = new List<Game>();
            List<string> ImportedGamesIDs = new List<string>();
            _plugin.PlayniteApi.Database.Platforms.Add(_mapping.RomMPlatform.Name);

            foreach (var ROM in _ROMs)
            {
                if (_args.CancelToken.IsCancellationRequested)
                    break;

                // Some newer platforms don't get a hash value so we will compromise with this
                if (string.IsNullOrEmpty(ROM.SHA1))
                {
                    var tohash = $"{ROM.Name}{ROM.FileSizeBytes}";

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

                // Skip game import if the ROM is apart of the exclusion list
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

                // Migrate old RomMGameInfo id to new romMId:SHA1 id
                string gameID = $"{ROM.Id}:{ROM.SHA1}";
                UpdatedOldGameID(ROM);

                // Merging revisions
                if (_plugin.Settings.MergeRevisions && ROM.Siblings?.Count > 0)
                {
                    if (CheckForMainSibling(ROM) == MainSibling.Other)
                    {
                        var siblinggame = _plugin.Playnite.Database.Games.FirstOrDefault(x => x.GameId == gameID);
                        if(siblinggame != null)
                        {
                            _plugin.Playnite.Database.Games.Remove(siblinggame);
                        }  
                        continue;
                    }
                        
                    if (ROM.Processed)
                    {
                        var siblinggame = _plugin.Playnite.Database.Games.FirstOrDefault(x => x.GameId == gameID);
                        if (siblinggame != null)
                        {
                            _plugin.Playnite.Database.Games.Remove(siblinggame);
                        }
                        continue;
                    }
                        
                }

                // Save Game ROM data to file
                SaveGameData(ROM);
                
                // Skip full import if ROM has already been imported 
                Guid statusID = new Guid();
                var game = _plugin.Playnite.Database.Games.FirstOrDefault(g => g.GameId == gameID);
                if (game != null)
                {
                    // Sync user data
                    if(_plugin.Settings.KeepRomMSynced)
                    {
                        statusID = DetermineCompletionStatus(ROM);

                        game.Favorite = _favourites.Exists(f => f == ROM.Id);

                        if (statusID != Guid.Empty)
                        {
                            game.CompletionStatusId = statusID;
                        }
                        _plugin.Playnite.Database.Games.Update(game);
                    }

                    ImportedGamesIDs.Add(gameID);
                    continue;
                }

                // If keep deleted games is enabled and a deleted game gets re-added back to the server under a new romMId, Update playnite entry
                if(_plugin.Settings.KeepDeletedGames)
                {
                    if(UpdatedDeletedGame(ROM))
                    {
                        ImportedGamesIDs.Add(gameID);
                        continue;
                    }
                }

                var importedGame = ImportGame(ROM, statusID);
                if (importedGame != null)
                {
                    games.Add(importedGame); 
                    ImportedGamesIDs.Add(gameID);
                }
                else
                {
                    _plugin.Logger.Error($"[Importer] Failed to import RomM GameID: {ROM.Id}");
                    continue;
                }
            }
            _plugin.Logger.Info($"[Importer] Finished adding new games for {_mapping.RomMPlatform.Name}");

            if (!_plugin.Settings.KeepDeletedGames)
            {
                RemoveMissingGames(ImportedGamesIDs);
            }

            return games;
        }
        private Game ImportGame(RomMRom ROM, Guid StatusID)
        {
            var rootInstallDir = _plugin.Playnite.Paths.IsPortable
                        ? _mapping.DestinationPathResolved.Replace(_plugin.Playnite.Paths.ApplicationPath, ExpandableVariables.PlayniteDirectory)
                        : _mapping.DestinationPathResolved;
            var gameInstallDir = Path.Combine(rootInstallDir, Path.GetFileNameWithoutExtension(ROM.Name));
            var pathToGame = Path.Combine(gameInstallDir, ROM.Name);

            var gameNameWithTags =
                        $"{ROM.Name}" +
                        $"{(ROM.Regions.Count > 0 ? $" ({string.Join(", ", ROM.Regions)})" : "")}" +
                        $"{(!string.IsNullOrEmpty(ROM.Revision) ? $" (Rev {ROM.Revision})" : "")}" +
                        $"{(ROM.Tags.Count > 0 ? $" ({string.Join(", ", ROM.Tags)})" : "")}";

            var preferedRatingsBoard = _plugin.Playnite.ApplicationSettings.AgeRatingOrgPriority;
            var agerating = ROM.Metadatum.Age_Ratings.Count > 0 ? new HashSet<MetadataProperty>(ROM.Metadatum.Age_Ratings.Where(r => r.Split(':')[0] == preferedRatingsBoard.ToString()).Select(r => new MetadataNameProperty(r.ToString()))) : null;

            var status = _plugin.Playnite.Database.CompletionStatuses.Get(StatusID);
            var completionStatusProperty = status != null ? new MetadataNameProperty(status.Name) : null;

            List<Link> gameLinks = new List<Link>();
            if(ROM.SSId != null)
                gameLinks.Add(new Link("Screenscraper", $"https://www.screenscraper.fr/gameinfos.php?gameid={ROM.SSId}"));
            if (ROM.HasheousId != null)
                gameLinks.Add(new Link("Hasheous", $"https://hasheous.org/index.html?page=dataobjectdetail&type=game&id={ROM.HasheousId}"));
            if (ROM.RAId != null)
                gameLinks.Add(new Link("RetroAchievements", $"https://retroachievements.org/game/{ROM.RAId}"));
            if (ROM.HLTBId != null)
                gameLinks.Add(new Link("HowLongToBeat", $"https://howlongtobeat.com/game/{ROM.HLTBId}"));

            var metadata = new GameMetadata
            {
                Source = _plugin.Source,
                GameId = $"{ROM.Id}:{ROM.SHA1}",

                Name = ROM.Name,
                Description = ROM.Summary,

                Platforms = new HashSet<MetadataProperty> { new MetadataNameProperty(_mapping.RomMPlatform.Name ?? "") },
                Regions = new HashSet<MetadataProperty>(ROM.Regions.Where(r => !string.IsNullOrEmpty(r)).Select(r => new MetadataNameProperty(r.ToString()))),
                Genres = new HashSet<MetadataProperty>(ROM.Metadatum.Genres.Where(r => !string.IsNullOrEmpty(r)).Select(r => new MetadataNameProperty(r.ToString()))),
                AgeRatings = agerating,
                Series = new HashSet<MetadataProperty>(ROM.Metadatum.Franchises.Where(r => !string.IsNullOrEmpty(r)).Select(r => new MetadataNameProperty(r.ToString()))),
                Features = new HashSet<MetadataProperty>(ROM.Metadatum.Gamemodes.Where(r => !string.IsNullOrEmpty(r)).Select(r => new MetadataNameProperty(r.ToString()))),
                Categories = new HashSet<MetadataProperty>(ROM.Metadatum.Collections.Where(r => !string.IsNullOrEmpty(r)).Select(r => new MetadataNameProperty(r.ToString()))),

                ReleaseDate = ROM.Metadatum.Release_Date.HasValue ? new ReleaseDate(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ROM.Metadatum.Release_Date.Value).ToLocalTime()) : new ReleaseDate(),
                CommunityScore = (int?)ROM.Metadatum.Average_Rating,

                CoverImage = !string.IsNullOrEmpty(ROM.PathCoverL) ? new MetadataFile($"{_plugin.Settings.RomMHost}{ROM.PathCoverL}") : null,

                Favorite = _favourites.Exists(f => f == ROM.Id),
                LastActivity = ROM.RomUser.LastPlayed,
                UserScore = ROM.RomUser.Rating * 10, //RomM-Rating is 1-10, Playnite 1-100, so it can unfortunately only by synced one direction without loosing decimals
                CompletionStatus = completionStatusProperty,
                Links = gameLinks,
                Roms = new List<GameRom> { new GameRom(gameNameWithTags, pathToGame) },
                InstallDirectory = gameInstallDir,
                IsInstalled = File.Exists(pathToGame),
                InstallSize = ROM.FileSizeBytes,
                GameActions = new List<GameAction>
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
                            }
            };

            // Import new game
            Game game = _plugin.Playnite.Database.ImportGame(metadata, _plugin);

            if (ROM.HasManual)
            {
                game.Manual = $"{_plugin.Settings.RomMHost}/assets/romm/resources/{ROM.ManualPath}";
            }

            return game;
        }
        private void RemoveMissingGames(List<string> ImportedGames)
        {
            var gamesInDatabase = _plugin.Playnite.Database.Games.Where(g =>
                        g.Source != null && g.Source.Name == _plugin.Source.ToString() &&
                        g.Platforms != null && g.Platforms.Any(p => p.Name == _mapping.RomMPlatform.Name)
                    );

            _plugin.Logger.Info($"[Importer] Starting to remove not found games for {_mapping.RomMPlatform.Name}.");

            foreach (var game in gamesInDatabase)
            {
                if (_args.CancelToken.IsCancellationRequested)
                    break;

                if (ImportedGames.Contains(game.GameId))
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
            var filename = ROM.HasMultipleFiles ? Path.GetFileName(ROM.FileName) : Path.GetFileName(ROM.Files.Where(f => f.FullPath.Count(c => c == '/') <= 3).FirstOrDefault().FileName);
            if (string.IsNullOrWhiteSpace(filename))
            {
                _plugin.Logger.Warn($"[Importer] Rom {ROM.Id} returned empty/invalid filename, skipping updating game id.");
                return false;
            }
            var info = new RomMGameInfo
            {
                MappingId = _mapping.MappingId,
                FileName = filename,
                DownloadUrl = _plugin.CombineUrl(_plugin.Settings.RomMHost, $"api/roms/{ROM.Id}/content/{filename}"),
                HasMultipleFiles = ROM.HasMultipleFiles
            };

            // Check to see if a game already exists with
            var oldgame = _plugin.Playnite.Database.Games.FirstOrDefault(g => g.GameId == info.AsGameId());
            if (oldgame != null)
            {
                oldgame.GameId = $"{ROM.Id}:{ROM.SHA1}";
                oldgame.PlatformIds = new List<Guid> { _plugin.Playnite.Database.Platforms.First(x => x.Name == _mapping.RomMPlatform.Name).Id };
                _plugin.Playnite.Database.Games.Update(oldgame);
                return true;
            }
            else
            {
                return false;
            }
        }
        private bool UpdatedDeletedGame(RomMRom ROM)
        {
            // Check to see if a game already exists with an old romMId
            var oldgame = _plugin.Playnite.Database.Games.FirstOrDefault(g => g.PluginId == _plugin.Id && g.GameId.Split(':')[1] == ROM.SHA1);
            if (oldgame != null)
            {
                oldgame.GameId = $"{ROM.Id}:{ROM.SHA1}";
                _plugin.Playnite.Database.Games.Update(oldgame);
                return true;
            }
            else
            {
                return false;
            }
        }

        private MainSibling CheckForMainSibling(RomMRom ROM)
        {
            //Check to see if ROM is the main sibling
            if (ROM.RomUser.IsMainSibling)
                return MainSibling.Current;

            //Find if there is a main sibling
            foreach (var sibling in ROM.Siblings)
            {
                var siblingROM = _ROMs.Find(x => x.Id == sibling.Id);

                if (siblingROM.RomUser.IsMainSibling)
                {
                    return MainSibling.Other;
                }
            }

            return MainSibling.None;
        }
        private void SaveGameData(RomMRom ROM)
        {
            string[] versionBreakdown = _plugin.Settings.ServerVersion.Split('.');
            float versionParsed = float.Parse(versionBreakdown[0]) + (float.Parse(versionBreakdown[1]) / 100);

            RomMRomLocal toSave = new RomMRomLocal();

            // Save base ROM data
            toSave.Id = ROM.Id;
            toSave.Name = ROM.Name;
            toSave.SHA1 = ROM.SHA1;
            toSave.HasMultipleFiles = ROM.HasMultipleFiles;
            if(!ROM.HasMultipleFiles)
            {
                var romfile = DetermineFile(ROM);
                if (romfile == null)
                {
                    _plugin.Logger.Error("[Importer] Unable to save ROM data as there is no rom file!");
                    return;
                }

                toSave.FileName = romfile.FileName;
                toSave.DownloadURL = versionParsed <= 4.7 ?
                                           _plugin.CombineUrl(_plugin.Settings.RomMHost, $"api/romsfiles/{romfile.Id}/content/{romfile.FileName}") :
                                           _plugin.CombineUrl(_plugin.Settings.RomMHost, $"api/roms/{romfile.Id}/files/content/{romfile.FileName}");
            }
            else
            {
                toSave.FileName = ROM.FileName;
                toSave.DownloadURL = _plugin.CombineUrl(_plugin.Settings.RomMHost, $"api/roms/{ROM.Id}/content/{ROM.FileName}");
            } 
            toSave.IsSelected = false;
            toSave.MappingID = _mapping.MappingId;

            // Save sibling data
            if (_plugin.Settings.MergeRevisions && ROM.Siblings?.Count > 0)
            {
                toSave.Siblings = new List<RomMSavedSibing>();

                foreach (var sibling in ROM.Siblings)
                {
                    var siblingROM = _ROMs.Find(x => x.Id == sibling.Id);
                    if(siblingROM != null)
                    {
                        RomMSavedSibing saveSibling = new RomMSavedSibing();

                        saveSibling.Id = siblingROM.Id;
                        saveSibling.HasMultipleFiles = siblingROM.HasMultipleFiles;
                        if (!siblingROM.HasMultipleFiles)
                        {
                            var romfile = DetermineFile(siblingROM);
                            if (romfile == null)
                            {
                                _plugin.Logger.Error("[Importer] Unable to save sibling ROM data as there is no rom file!");
                                continue;
                            }

                            saveSibling.FileName = romfile.FileName;
                            toSave.DownloadURL = versionParsed <= 4.7 ?
                                           _plugin.CombineUrl(_plugin.Settings.RomMHost, $"api/romsfiles/{romfile.Id}/content/{romfile.FileName}") :
                                           _plugin.CombineUrl(_plugin.Settings.RomMHost, $"api/roms/{romfile.Id}/files/content/{romfile.FileName}");
                        }
                        else
                        {
                            saveSibling.FileName = siblingROM.FileName;
                            saveSibling.DownloadURL = _plugin.CombineUrl(_plugin.Settings.RomMHost, $"api/roms/{siblingROM.Id}/content/{siblingROM.FileName}");
                        }          
                        saveSibling.IsSelected = false;
                        _ROMs.First(x => x.Id == sibling.Id).Processed = true;

                        toSave.Siblings.Add(saveSibling);
                    }
                }
            }

            // Write data to file
            string json = JsonConvert.SerializeObject(toSave);
            File.WriteAllText($"{_plugin.ROMDataPath}{ROM.SHA1}.json", json);

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
                completionStatus = RomMRomUser.CompletionStatusMap[ROM.RomUser.Status ?? "not_played"];
            }

            _completionStatusMap.TryGetValue(completionStatus, out var statusId);

            var status = _plugin.Playnite.Database.CompletionStatuses.Get(statusId);
            var completionStatusProperty = status != null ? new MetadataNameProperty(status.Name) : null;

            return statusId;
        }    
    }
}
