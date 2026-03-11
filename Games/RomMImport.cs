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

        List<MetadataPlugin> _metadataPlugins = new List<MetadataPlugin>();

        public RomMImport(RomM plugin, LibraryImportGamesArgs args, EmulatorMapping mapping, List<RomMRom> roms, List<int> favourites)
        {
            _plugin = plugin;
            _args = args;
            _mapping = mapping;
            _ROMs = roms;
            _completionStatusMap = plugin.Playnite.Database.CompletionStatuses.ToDictionary(cs => cs.Name, cs => cs.Id);
            _favourites = favourites;

            foreach (var addons in plugin.Playnite.Addons.Plugins)
            {
                try
                {
                    var metadataPlugin = (MetadataPlugin)addons;
                    _metadataPlugins.Add(metadataPlugin);
                }
                catch (Exception)
                {
                }
            }
        }

        // Helper functions
        private string CombineUrl(string baseUrl, string relativePath)
        {
            return $"{baseUrl?.TrimEnd('/')}/{relativePath?.TrimStart('/') ?? ""}";
        }

        // Main library import functions

        public List<Game> ProcessData()
        {
            var games = new List<Game>();
            List<string> ImportedGamesIDs = new List<string>();

            foreach (var ROM in _ROMs)
            {
                if (_args.CancelToken.IsCancellationRequested)
                    break;

                // Skip game import if the ROM is apart of the exclusion list
                if(_plugin.Playnite.Database.ImportExclusions[Playnite.ImportExclusionItem.GetId($"{ROM.Id}:{ROM.SHA1}", _plugin.Id)] != null)
                {
                    _plugin.Logger.Warn($"Excluding {ROM.Name} from import.");
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

                // Merging revisions
                if (_plugin.Settings.MergeRevisions && ROM.Siblings?.Count > 0)
                {
                    if (CheckForMainSibling(ROM) == MainSibling.Other)
                        continue;

                    if (ROM.Processed)
                        continue;
                }

                // Save Game ROM data to file
                SaveGameData(ROM);
                
                // Skip full import if ROM has already been imported
                string gameID = $"{ROM.Id}:{ROM.SHA1}";
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

                var importedGame = ImportGame(ROM, statusID);

                if (importedGame != null)
                {
                    games.Add(importedGame); 
                    ImportedGamesIDs.Add(gameID);
                }
                else
                {
                    _plugin.Logger.Error($"Failed to import RomM GameID: {ROM.Id}");
                    continue;
                }
            }
            _plugin.Logger.Debug($"Finished adding new games for {_mapping.Platform.Name}");

           
            RemoveMissingGames(ImportedGamesIDs);


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
                Source = _plugin.SourceName,
                GameId = $"{ROM.Id}:{ROM.SHA1}",

                Name = ROM.Name,
                Description = ROM.Summary,

                Platforms = new HashSet<MetadataProperty> { new MetadataNameProperty(_mapping.Platform.Name ?? "") },
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
                                    Path = CombineUrl(_plugin.Settings.RomMHost, $"rom/{ROM.Id}"),
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

            // NOTE: Due to the switch from GetGames to ImportGames metadata plugins don't run.
            // This is currently commented out as the user should be given the option to run metadata scans with library import
            // and a more sophisticated system need implementing to import that data
            //Pull metadata from plugins
            //game = PluginMetaData(game);
            //_plugin.Playnite.Database.Games.Update(game);

            return game;
        }
        private void RemoveMissingGames(List<string> ImportedGames)
        {
            var gamesInDatabase = _plugin.Playnite.Database.Games.Where(g =>
                        g.Source != null && g.Source.Name == _plugin.SourceName.ToString() &&
                        g.Platforms != null && g.Platforms.Any(p => p.Name == _mapping.Platform.Name)
                    );

            _plugin.Logger.Debug($"Starting to remove not found games for {_mapping.Platform.Name}.");

            foreach (var game in gamesInDatabase)
            {
                if (_args.CancelToken.IsCancellationRequested)
                    break;

                if (ImportedGames.Contains(game.GameId))
                {
                    continue;
                }

                _plugin.Playnite.Database.Games.Remove(game.Id);
            }

            _plugin.Logger.Debug($"Finished removing not found games for {_mapping.Platform.Name}");
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
            RomMRomLocal toSave = new RomMRomLocal();

            if (_plugin.Settings.MergeRevisions && ROM.Siblings?.Count > 0)
            {
                // Check to see if game is already installed
                string GameID = $"{ROM.Id}:{ROM.SHA1}";
                var game = _plugin.Playnite.Database.Games.FirstOrDefault(x => x.GameId == GameID);

                if(game != null && game.IsInstalled)
                {
                    try
                    {
                        // Load Game data from file 
                        string olddata = File.ReadAllText($"{_plugin.ROMDataPath}{ROM.SHA1}.json");
                        toSave = JsonConvert.DeserializeObject<RomMRomLocal>(olddata);

                        List<RomMSavedSibing> toSaveSiblings = toSave.Siblings;

                        // Remove all sibling data so any ROMs still linked to Game will be added back
                        toSave.Siblings.Clear();

                        // Check to see if sibling still exists
                        foreach (var sibling in ROM.Siblings)
                        {
                            var siblingItem = _ROMs.Find(x => x.Id == sibling.Id);

                            if (toSaveSiblings.Exists(x => x.Id == sibling.Id))
                            {
                                // Add sibling back to file
                                toSave.Siblings.Add(toSaveSiblings.Find(x => x.Id == sibling.Id));
                            }
                            else
                            {
                                // Add new sibling
                                var newSibling = new RomMSavedSibing();

                                if (string.IsNullOrEmpty(siblingItem.FileName))
                                {
                                    _plugin.Playnite.Notifications.Add(new NotificationMessage(_plugin.Id.ToString(), $"Filename for ROM ID: {siblingItem.Id} doesn't exist!\nDoes ROM exist on the servers filesystem?", NotificationType.Error));
                                    continue;
                                }

                                newSibling.Id = sibling.Id;
                                newSibling.FileName = ROM.HasMultipleFiles ? Path.GetFileName(siblingItem.FileName) : Path.GetFileName(siblingItem.Files.First().FileName);
                                newSibling.DownloadURL = CombineUrl(_plugin.Settings.RomMHost, $"api/roms/{newSibling.Id}/content/{newSibling.FileName}");
                                newSibling.HasMultipleFiles = ROM.HasMultipleFiles;
                                newSibling.IsSelected = false;
                                newSibling.Mapping = _mapping;

                                _ROMs.Find(x => x.Id == siblingItem.Id).Processed = true;
                                toSave.Siblings.Add(newSibling);
                            }
                        }

                        // Write old and new data back to file
                        olddata = JsonConvert.SerializeObject(toSave);
                        File.WriteAllText($"{_plugin.Playnite.Paths.ExtensionsDataPath}\\{_plugin.Id}\\Games\\{ROM.SHA1}.json", olddata);
                        return;
                    }
                    catch { }
                }
                else // Game hasn't been import or isnt installed
                {
                    // Save base ROM data
                    toSave.Id = ROM.Id;
                    toSave.Name = ROM.Name;
                    toSave.SHA1 = ROM.SHA1;
                    toSave.FileName = ROM.HasMultipleFiles ? Path.GetFileName(ROM.FileName) : Path.GetFileName(ROM.Files.First().FileName);
                    toSave.DownloadURL = CombineUrl(_plugin.Settings.RomMHost, $"api/roms/{toSave.Id}/content/{toSave.FileName}");
                    toSave.HasMultipleFiles = ROM.HasMultipleFiles;
                    toSave.IsSelected = false;
                    toSave.Mapping = _mapping;

                    toSave.Siblings = new List<RomMSavedSibing>();

                    // Save sibling data
                    foreach (var sibling in ROM.Siblings)
                    {
                        var siblingItem = _ROMs.Find(x => x.Id == sibling.Id);

                        if (string.IsNullOrEmpty(siblingItem.FileName))
                        {
                            _plugin.Playnite.Notifications.Add(new NotificationMessage(_plugin.Id.ToString(), $"Filename for ROM ID: {siblingItem.Id} doesn't exist!\nDoes ROM exist on the servers filesystem?", NotificationType.Error));
                            continue;
                        }

                        RomMSavedSibing saveSibling = new RomMSavedSibing();
                        saveSibling.Id = siblingItem.Id;
                        saveSibling.FileName = ROM.HasMultipleFiles ? Path.GetFileName(ROM.FileName) : Path.GetFileName(ROM.Files.First().FileName);
                        saveSibling.DownloadURL = CombineUrl(_plugin.Settings.RomMHost, $"api/roms/{saveSibling.Id}/content/{saveSibling.FileName}");
                        saveSibling.HasMultipleFiles = ROM.HasMultipleFiles;
                        saveSibling.IsSelected = false;
                        saveSibling.Mapping = _mapping;

                        _ROMs.Find(x => x.Id == siblingItem.Id).Processed = true;

                        toSave.Siblings.Add(saveSibling);
                    }
                }

            }
            else //Merge Revisons not enabled or no siblings present
            {
                // Save base ROM data
                toSave.Id = ROM.Id;
                toSave.Name = ROM.Name;
                toSave.SHA1 = ROM.SHA1;
                toSave.FileName = ROM.HasMultipleFiles ? Path.GetFileName(ROM.FileName) : Path.GetFileName(ROM.Files.First().FileName);
                toSave.DownloadURL = CombineUrl(_plugin.Settings.RomMHost, $"api/roms/{toSave.Id}/content/{toSave.FileName}");
                toSave.HasMultipleFiles = ROM.HasMultipleFiles;
                toSave.IsSelected = false;
                toSave.Mapping = _mapping;

                toSave.Siblings = new List<RomMSavedSibing>();
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

        

        private Game PluginMetaData(Game Game)
        {
            foreach (var plugin in _metadataPlugins)
            {
                try
                {
                    var pluginProvider = plugin.GetMetadataProvider(new MetadataRequestOptions(Game, true));

                    try
                    {
                        if (pluginProvider.AvailableFields.Any(x => x == MetadataField.Icon))
                        {
                            Game.Icon = string.IsNullOrEmpty(Game.Icon) ? pluginProvider.GetIcon(new GetMetadataFieldArgs()).Path : Game.Icon;
                        }
                    }
                    catch{}
                    try
                    {
                        if (pluginProvider.AvailableFields.Any(x => x == MetadataField.BackgroundImage))
                        {
                            Game.BackgroundImage = string.IsNullOrEmpty(Game.BackgroundImage) ? pluginProvider.GetBackgroundImage(new GetMetadataFieldArgs()).Path : Game.BackgroundImage;
                        }
                    }
                    catch{}
                    try
                    {
                        if (pluginProvider.AvailableFields.Any(x => x == MetadataField.CoverImage))
                        {
                            Game.CoverImage = string.IsNullOrEmpty(Game.CoverImage) ? pluginProvider.GetCoverImage(new GetMetadataFieldArgs()).Path : Game.CoverImage;
                        }
                    }
                    catch{}
                }
                catch
                {
                    
                }

               
            } 

            return Game;
        }      
    }
}
