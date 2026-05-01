using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using RomM.Games;
using RomM.Downloads;
using RomM.VersionSelector;
using RomM.Models.RomM.Collection;
using RomM.Models.RomM.Rom;
using RomM.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace RomM
{
    public static class HttpClientSingleton
    {
        private static readonly HttpClient httpClient = new HttpClient();

        static HttpClientSingleton()
        {
            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public static void ConfigureBasicAuth(string username, string password)
        {
            var base64Credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64Credentials);
        }
        public static void ConfigureAPIAuth(string apiToken)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        }

        public static HttpClient Instance => httpClient;
    }

    public static class JsonSerializerSingleton
    {
        public static JsonSerializer Instance { get; } = new JsonSerializer();
    }

    public class RomM : LibraryPlugin, IRomM
    {
        private const string s_pluginName = "RomM";

        internal static readonly string Icon = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"icon.png");
        internal static readonly Guid PluginId = Guid.Parse("9700aa21-447d-41b4-a989-acd38f407d9f");
        internal static readonly MetadataNameProperty SourceName = new MetadataNameProperty(s_pluginName);

        public override Guid Id { get; } = PluginId;
        public override string Name { get; } = s_pluginName;
        public override string LibraryIcon { get; } = Icon;

        public ILogger Logger => LogManager.GetLogger();
        public IPlayniteAPI Playnite { get; private set; }
        public SettingsViewModel Settings { get; private set; }     
        public string ROMDataPath { get; private set; }
        public MetadataProperty Source { get; private set; }

        public DownloadQueueController DownloadQueueController { get; private set; }
        internal RomMDownloadsSidebarItem DownloadsSidebar { get; private set; }
        private readonly DownloadQueueViewModel downloadsVm;
        
        // Implementing Client adds ability to open it via special menu in playnite
        public override LibraryClient Client { get; } = new RomMClient();

        public RomM(IPlayniteAPI api) : base(api)
        {
            Playnite = api;
            Properties = new LibraryPluginProperties
            {
                HasSettings = true,
                HasCustomizedGameImport = true,
            };
            ROMDataPath = $"{Playnite.Paths.ExtensionsDataPath}\\{Id}\\Games\\";

            // Initialise the download queue
            downloadsVm = new DownloadQueueViewModel();

            // Limit to 10 concurrent downloads for the moment
            DownloadQueueController = new DownloadQueueController(Playnite, downloadsVm, maxConcurrent: 10);

            // Initialise the sidebar only in desktop mode
            if (API.Instance.ApplicationInfo.Mode == ApplicationMode.Desktop)
            {
                DownloadsSidebar = new RomMDownloadsSidebarItem(this);
            }
        }


    #region Helper functions
        public string CombineUrl(string baseUrl, string relativePath)
        {
            return $"{baseUrl?.TrimEnd('/')}/{relativePath?.TrimStart('/') ?? ""}";
        }

        public RomMRom FetchRom(string romId)
        {
            string romUrl = CombineUrl(Settings.RomMHost, $"api/roms/{romId}");
            try
            {
                HttpResponseMessage response = HttpClientSingleton.Instance.GetAsync(romUrl).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonConvert.DeserializeObject<RomMRom>(body);
            }
            catch (HttpRequestException e)
            {
                Logger.Error($"Request exception: {e.Message}");
                return null;
            }
        }

        // Playnite url is in the format playnite://romm/<action>/<platform_igdb_id>/<rom_id>
        internal void HandleRommUri(PlayniteUriEventArgs args)
        {
            var action = args.Arguments[0];
            var platformIgdbId = args.Arguments[1];
            var romId = args.Arguments[2];

            Logger.Debug($"Received Playnite URI: {action}/{platformIgdbId}/{romId}");

            RomMRom rom = FetchRom(romId);

            if (rom == null)
            {
                Logger.Warn($"Game {romId} not found in RomM.");
                return;
            }

            foreach (var mapping in SettingsViewModel.Instance.Mappings?.Where(m => m.Enabled))
            {
                if (mapping.RomMPlatform.IgdbId.ToString() == platformIgdbId)
                {
                    var gameName = rom.Name;

                    var game = Playnite.Database.Games.FirstOrDefault(g => g.Source.Name == SourceName.ToString() &&
                                                                           g.Platforms.Any(p => p.Name == mapping.RomMPlatform.Name) &&
                                                                           g.Name == gameName);

                    if (game == null)
                    {
                        Logger.Warn($"Game {gameName} not found in Playnite database.");
                        return;
                    }

                    PlayniteApi.MainView.SwitchToLibraryView();
                    PlayniteApi.MainView.SelectGame(game.Id);

                    switch (action)
                    {
                        case "view":
                            // We always open the game in the webview
                            return;
                        case "play":
                            PlayniteApi.StartGame(game.Id);
                            break;
                    }
                }
            }
        }

        // New-style overload (used by DownloadQueueController)
        public static Task<HttpResponseMessage> GetAsync(string url, HttpCompletionOption completionOption, CancellationToken ct)
        {
            return HttpClientSingleton.Instance.GetAsync(url, completionOption, ct);
        }
    #endregion

    #region Playnite functions
        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            base.OnApplicationStarted(args);

            if (!Directory.Exists($"{ROMDataPath}"))
                Directory.CreateDirectory($"{ROMDataPath}");

            Settings = new SettingsViewModel(this, this);

            if (Settings.UseBasicAuth && !string.IsNullOrEmpty(Settings.RomMUsername) && !string.IsNullOrEmpty(Settings.RomMPassword))
            {
                HttpClientSingleton.ConfigureBasicAuth(Settings.RomMUsername, Settings.RomMPassword);
            }
            else if(SettingsViewModel.ApiTokenPattern.IsMatch(Settings.RomMClientToken))
            {
                HttpClientSingleton.ConfigureAPIAuth(Settings.RomMClientToken);
            }
            
            Playnite.UriHandler.RegisterSource("romm", HandleRommUri);
            Source = SourceName;

            // Portable path fix: expand "{PlayniteDir}" to absolute paths in DB on startup
            if (Playnite.Paths.IsPortable)
            {
                using (PlayniteApi.Database.BufferedUpdate())
                {
                    var games = PlayniteApi.Database.Games.Where(g =>
                        g.PluginId == Id &&
                        g.InstallDirectory != null &&
                        g.InstallDirectory.Contains(ExpandableVariables.PlayniteDirectory));

                    foreach (var game in games)
                    {
                        game.InstallDirectory = PlayniteApi.ExpandGameVariables(game, game.InstallDirectory);

                        if (game.Roms != null && game.Roms.Count > 0)
                        {
                            var roms = game.Roms.Where(r => r.Path.Contains(ExpandableVariables.PlayniteDirectory));
                            foreach (var rom in roms)
                            {
                                rom.Path = PlayniteApi.ExpandGameVariables(game, rom.Path);
                            }
                        }

                        PlayniteApi.Database.Games.Update(game);
                    }
                }
            }

            Playnite.Database.Games.ItemUpdated += OnItemUpdated;

            PlayniteApi.Database.Games.ItemCollectionChanged += (_, argus) =>
            {
                // Remove json file if game is removed from playnite
                if (argus.RemovedItems.Count > 0)
                {
                    foreach (var item in argus.RemovedItems)
                    {
                        if (item.PluginId == PluginId)
                        {
                            if (item.GameId.Contains(':'))
                            {
                                if (File.Exists($"{ROMDataPath}{item.GameId.Split(':')[1]}.json"))
                                {
                                    File.Delete($"{ROMDataPath}{item.GameId.Split(':')[1]}.json");
                                }
                            }
                            else
                            {
                                Logger.Error($"Game {item.Name} id is malformed!");
                            }
                        }
                    }
                }
            };

        }
        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            base.OnApplicationStopped(args);

            Playnite.Database.Games.ItemUpdated -= OnItemUpdated;

            // Portable path fix: restore "{PlayniteDir}" tokens before exiting
            if (Playnite.Paths.IsPortable)
            {
                using (PlayniteApi.Database.BufferedUpdate())
                {
                    var games = PlayniteApi.Database.Games.Where(g =>
                        g.PluginId == Id &&
                        g.InstallDirectory != null &&
                        g.InstallDirectory.StartsWith(PlayniteApi.Paths.ApplicationPath));

                    foreach (var game in games)
                    {
                        game.InstallDirectory = game.InstallDirectory.Replace(
                            PlayniteApi.Paths.ApplicationPath,
                            ExpandableVariables.PlayniteDirectory);

                        if (game.Roms != null && game.Roms.Count > 0)
                        {
                            foreach (var rom in game.Roms)
                            {
                                rom.Path = rom.Path.Replace(
                                    PlayniteApi.Paths.ApplicationPath,
                                    ExpandableVariables.PlayniteDirectory);
                            }
                        }

                        PlayniteApi.Database.Games.Update(game);
                    }
                }
            }
        }

        public override IEnumerable<Game> ImportGames(LibraryImportGamesArgs args)
        {
            if (Playnite.ApplicationInfo.Mode == ApplicationMode.Fullscreen && !Settings.ScanGamesInFullScreen)
            {
                return new List<Game>();
            }

            if(!Settings.TestConnection())
            {
                return new List<Game>();
            }

            return new RomMImportController(this).Import(args);
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return Settings;
        }
        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new SettingsView();
        }

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            if (DownloadsSidebar != null)
            {
                yield return DownloadsSidebar;
            }
        }
        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            List<GameMenuItem> gameMenuItems = new List<GameMenuItem>();

            if (args.Games.First().PluginId == PluginId)
            {

                if (Settings.MergeRevisions && File.Exists($"{ROMDataPath}{args.Games.First().GameId.Split(':')[1]}.json") && args.Games.First().IsInstalled)
                {
                    try
                    {
                        string json = File.ReadAllText($"{ROMDataPath}{args.Games.First().GameId.Split(':')[1]}.json");
                        var gameData = JsonConvert.DeserializeObject<RomMRomLocal>(json);
                        if(gameData.ROMVersions.Count > 1)
                        {
                            gameMenuItems.Add(new GameMenuItem
                            {
                                //MenuSection = "@",
                                Description = "Switch ROM Version!",
                                Action = (gameMenuItem) =>
                                {
                                    Playnite.InstallGame(args.Games.First().Id);
                                }
                            });
                        }
                    }
                    catch (Exception)
                    {
                        Logger.Error($"{args.Games.First().Name} GameID is malformed or json file is corrupted!");
                    } 
                }
            }
            return gameMenuItems;
        }

        public override IEnumerable<InstallController> GetInstallActions(GetInstallActionsArgs args)
        {
            if (args.Game.PluginId == Id)
            {
                string gameID = args.Game.GameId;
                GameInstallInfo romData = new GameInstallInfo();
                RomMRomLocal gameData = new RomMRomLocal();

                if (args.Game.GameId.StartsWith("!0"))
                {
                    PlayniteApi.Notifications.Add(new NotificationMessage(PluginId.ToString(), "Old ID detected run update game library before installing!", NotificationType.Error));
                    romData.Id = (int)InstallStatus.Cancelled;
                }
                else
                {
                    // Pull game file from RomM data directory
                    int romMId;
                    string romMSHA1 = gameID.Split(':')[1];
                    if (!int.TryParse(args.Game.GameId.Split(':')[0], out romMId) || !File.Exists($"{ROMDataPath}{romMSHA1}.json"))
                    {
                        Logger.Error($"{args.Game.Name} GameID is malformed!");
                        romData.Id = (int)InstallStatus.Cancelled;
                        yield return new RomMInstallController(args.Game, this, romData);
                    }

                    try
                    {
                        string json = File.ReadAllText($"{ROMDataPath}{romMSHA1}.json");
                        gameData = JsonConvert.DeserializeObject<RomMRomLocal>(json);
                    }
                    catch (Exception)
                    {
                        Logger.Error($"{args.Game.Name} GameID is malformed or {romMSHA1} json file is corrupted!");
                        romData.Id = (int)InstallStatus.Cancelled;
                    }

                    if (romData.Id == (int)InstallStatus.Cancelled)
                        yield return new RomMInstallController(args.Game, this, romData);

                    // Set ROM data to base ROM
                    romData = new GameInstallInfo
                    {
                        Id = gameData.ROMVersions[0].Id,
                        FileName = gameData.ROMVersions[0].FileName,
                        HasMultipleFiles = gameData.ROMVersions[0].HasMultipleFiles,
                        DownloadURL = gameData.ROMVersions[0].DownloadURL,
                        Mapping = Settings.Mappings.FirstOrDefault(x => x.MappingId == gameData.MappingID)
                    };

                    // If Siblings are avaiable prompt user with version selection
                    if (Settings.MergeRevisions && gameData.ROMVersions?.Count > 1)
                    {

                        RomMVersionSelector VersionSelectorControl = new RomMVersionSelector(gameData.ROMVersions);
                        var window = Playnite.Dialogs.CreateWindow(new WindowCreationOptions
                        {
                            ShowMinimizeButton = false,
                            ShowMaximizeButton = false,
                            ShowCloseButton = false,
                        });

                        window.Height = 215;
                        window.Width = 600;

                        window.Title = "Select Version to install!";
                        window.ShowInTaskbar = false;
                        window.ResizeMode = ResizeMode.NoResize;
                        window.Owner = API.Instance.Dialogs.GetCurrentAppWindow();
                        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                        window.Content = VersionSelectorControl;

                        window.ShowDialog();

                        if (VersionSelectorControl.Cancelled)
                        {
                            romData.Id = (int)InstallStatus.Cancelled;
                        }
                        else
                        {
                            // Uninstall old ROM before installing new one
                            if (args.Game.IsInstalled)
                            {
                                Playnite.UninstallGame(args.Game.Id);

                                args.Game.IsInstalling = true;
                                Playnite.Database.Games.Update(args.Game);
                            }


                            var selectedrevision = VersionSelectorControl.RomVersions.First(x => x.IsSelected);
                            romData.Id = selectedrevision.Id;
                            romData.FileName = selectedrevision.FileName;
                            romData.HasMultipleFiles = selectedrevision.HasMultipleFiles;
                            romData.DownloadURL = selectedrevision.DownloadURL;
                            
                            gameData.ROMVersions = VersionSelectorControl.RomVersions.ToList();

                            File.WriteAllText($"{ROMDataPath}{romMSHA1}.json", JsonConvert.SerializeObject(gameData));
                        }
                    }
                }

                yield return new RomMInstallController(args.Game, this, romData);
            }
        }
        public override IEnumerable<UninstallController> GetUninstallActions(GetUninstallActionsArgs args)
        {
            if (args.Game.PluginId == Id)
            {
                yield return new RomMUninstallController(args.Game, this);
            }
        }
        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
            base.OnGameInstalled(args);

            if (args.Game.PluginId == PluginId && Settings.NotifyOnInstallComplete)
            {
                Playnite.Notifications.Add(args.Game.GameId, $"Download of \"{args.Game.Name}\" is complete", NotificationType.Info);
            }
        }

        public override LibraryMetadataProvider GetMetadataDownloader()
        {
            return new RomMMetadataProvider(this);
        }
        #endregion

    #region RomM Status Syncing
        public IList<RomMCollection> FetchFavorites()
        {
            string apiFavoriteUrl = CombineUrl(Settings.RomMHost, "api/collections");
            try
            {
                // Make the request and get the response
                HttpResponseMessage response = HttpClientSingleton.Instance.GetAsync(apiFavoriteUrl).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                // Assuming the response is in JSON format
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonConvert.DeserializeObject<List<RomMCollection>>(body);
            }
            catch (HttpRequestException e)
            {
                Logger.Error($"Request exception: {e.Message}");
                return new List<RomMCollection>();
            }
        }
        internal RomMCollection CreateFavorites()
        {
            string apiCollectionUrl = CombineUrl(Settings.RomMHost, "api/collections?is_favorite=true&is_public=false");
            try
            {
                var formData = new MultipartFormDataContent();
                formData.Add(new StringContent("Favorites"), "name");

                HttpResponseMessage postResponse = HttpClientSingleton.Instance.PostAsync(apiCollectionUrl, formData).GetAwaiter().GetResult();
                postResponse.EnsureSuccessStatusCode();

                string body = postResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonConvert.DeserializeObject<RomMCollection>(body);
            }
            catch (HttpRequestException e)
            {
                Logger.Error($"Request exception: {e.Message}");
                return null;
            }
        }
        internal void UpdateFavorites(RomMCollection favoriteCollection, List<int> romIds)
        {
            if (favoriteCollection == null)
            {
                Logger.Error($"Can't update favorites, collection is null");
                return;
            }

            string apiCollectionUrl = CombineUrl(Settings.RomMHost, "api/collections");
            try
            {
                var formData = new MultipartFormDataContent();
                formData.Add(new StringContent(JsonConvert.SerializeObject(romIds)), "rom_ids");
                HttpResponseMessage putResponse = HttpClientSingleton.Instance.PutAsync($"{apiCollectionUrl}/{favoriteCollection.Id}", formData).GetAwaiter().GetResult();
                putResponse.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException e)
            {
                Logger.Error($"Request exception: {e.Message}");
            }
        }

        private void OnItemUpdated(object sender, ItemUpdatedEventArgs<Game> e)
        {
            Task.Run(async () =>
            {
                foreach (var update in e.UpdatedItems)
                {
                    var oldGame = update.OldData;
                    var newGame = update.NewData;

                    // Ignore non-RomM games
                    if (newGame.PluginId != Id)
                    {
                        continue;
                    }

                    // This is the cancel signal
                    if (oldGame.IsInstalling && !newGame.IsInstalling)
                    {
                        DownloadQueueController?.Cancel(newGame.Id);
                    }
                
                    if (Settings.KeepRomMSynced == true)
                    {
                        int romMId;                    
                        if(!int.TryParse(newGame.GameId.Split(':')[0], out romMId))
                        {
                            Logger.Error($"{newGame.Name} GameID is malformed!");
                        }

                        if (oldGame.Favorite != newGame.Favorite)
                        {
                            Logger.Info($"Favorites changed for {romMId}.");
                            try
                            {
                                IList<RomMCollection> favoriteCollections = FetchFavorites();
                                var favoriteCollection = favoriteCollections.FirstOrDefault(c => c.IsFavorite) ?? CreateFavorites();

                                var romIds = favoriteCollection?.RomIds ?? new List<int>();
                                if (newGame.Favorite == false)
                                {
                                    romIds.Remove(romMId);
                                }
                                else
                                {
                                    romIds.Add(romMId);
                                }

                                UpdateFavorites(favoriteCollection, romIds);
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, "RomM Favorite Sync Failed");
                            }
                        }
                        
                        if (oldGame.CompletionStatus != newGame.CompletionStatus)
                        {
                            try
                            {
                                // This would be easier if status would be merged: https://github.com/rommapp/romm/issues/2971
                                // For now we check if it is either "playing" or "plan to play" and set the booleans, otherwise we set the status
                                // If this issue is accepted and fixed, we can just reverse the CompletionStatusMap dictionary
                                if (newGame.CompletionStatus == null) continue;
                                var status = newGame.CompletionStatus.Name;

                                var updatePayload = new
                                {
                                    data = new
                                    {
                                        backlogged = status == "Plan to Play",
                                        now_playing = status == "Playing",
                                        status = RomMRomUser.CompletionStatusMap.FirstOrDefault((kv) => kv.Value == status && kv.Value != "Playing" && kv.Value != "Plan to Play" && kv.Value != "Not Played").Key
                                    }
                                };
                                string apiRomMRomUserProps = CombineUrl(Settings.RomMHost, $"api/roms/{romMId}/props");
                                HttpResponseMessage response = HttpClientSingleton.Instance.PutAsync(apiRomMRomUserProps, new StringContent(JsonConvert.SerializeObject(updatePayload), Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
                                response.EnsureSuccessStatusCode();
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex, $"RomM Status Sync Failed for {romMId}");
                            }
                        }
                    }
                }
            });
        }
    #endregion
    }
}