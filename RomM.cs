using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Web;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Specialized;
using System.IO;
using System.Reflection;
using System.Linq;
using Newtonsoft.Json;
using RomM.Settings;
using Playnite.SDK.Events;
using RomM.Games;
using RomM.Models.RomM.Platform;
using RomM.Models.RomM.Rom;


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

        public static HttpClient Instance
        {
            get { return httpClient; }
        }
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

        // Implementing Client adds ability to open it via special menu in playnite.
        public override LibraryClient Client { get; } = new RomMClient();

        public RomM(IPlayniteAPI api) : base(api)
        {
            Playnite = api;
            Properties = new LibraryPluginProperties
            {
                HasSettings = true
            };
        }

        internal IList<RomMPlatform> FetchPlatforms()
        {
            string apiPlatformsUrl = $"{Settings.RomMHost}/api/platforms";
            try
            {
                // Make the request and get the response
                HttpResponseMessage response = HttpClientSingleton.Instance.GetAsync(apiPlatformsUrl).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                // Assuming the response is in JSON format
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonConvert.DeserializeObject<List<RomMPlatform>>(body);
            }
            catch (HttpRequestException e)
            {
                Logger.Error($"Request exception: {e.Message}");
                return new List<RomMPlatform>();
            }
        }

        internal RomMRom FetchRom(string romId)
        {
            string romUrl = $"{Settings.RomMHost}/api/roms/{romId}";
            try
            {
                // Fetch the rom info from RomM
                HttpResponseMessage response = HttpClientSingleton.Instance.GetAsync(romUrl).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                // Assuming the response is in JSON format
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

            string romUrl = $"{Settings.RomMHost}/api/roms/{romId}";
            RomMRom rom = FetchRom(romId);

            if (rom == null)
            {
                Logger.Warn($"Game {romId} not found in RomM.");
                return;
            }

            foreach (var mapping in SettingsViewModel.Instance.Mappings?.Where(m => m.Enabled))
            {
                if (mapping.Platform.IgdbId.ToString() == platformIgdbId)
                {
                    var gameName = rom.Name;

                    var game = Playnite.Database.Games.FirstOrDefault(g => g.Source.Name == SourceName.ToString() &&
                                                                           g.Platforms.Any(p => p.Name == mapping.Platform.Name) &&
                                                                           g.Name == gameName);

                    if (game == null)
                    {
                        Logger.Warn($"Game {gameName} not found in Playnite database.");
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

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            base.OnApplicationStarted(args);
            Settings = new SettingsViewModel(this, this);
            HttpClientSingleton.ConfigureBasicAuth(Settings.RomMUsername, Settings.RomMPassword);
            Playnite.UriHandler.RegisterSource("romm", HandleRommUri);
        }

        public static async Task<HttpResponseMessage> GetAsync(string baseUrl, HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
        {
            return await HttpClientSingleton.Instance.GetAsync(baseUrl, completionOption);
        }

        public static async Task<HttpResponseMessage> GetAsyncWithParams(string baseUrl, NameValueCollection queryParams)
        {
            var uriBuilder = new UriBuilder(baseUrl);
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);

            foreach (string key in queryParams)
            {
                query[key] = queryParams[key];
            }

            uriBuilder.Query = query.ToString();

            return await HttpClientSingleton.Instance.GetAsync(uriBuilder.Uri);
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            if (Playnite.ApplicationInfo.Mode == ApplicationMode.Fullscreen && !Settings.ScanGamesInFullScreen)
            {
                return new List<GameMetadata>();
            }

            // Return early if host, username or password is not set
            if (string.IsNullOrEmpty(Settings.RomMHost) || string.IsNullOrEmpty(Settings.RomMUsername) || string.IsNullOrEmpty(Settings.RomMPassword))
            {
                Logger.Warn("RomM host, username or password is not set.");
                return new List<GameMetadata>();
            }

            IList<RomMPlatform> apiPlatforms = FetchPlatforms();
            List<GameMetadata> games = new List<GameMetadata>();
            IEnumerable<EmulatorMapping> enabledMappings = SettingsViewModel.Instance.Mappings?.Where(m => m.Enabled);

            if (enabledMappings == null)
            {
                Logger.Warn("No enabled mappings found.");
                return games;
            }

            foreach (var mapping in enabledMappings)
            {
                if (args.CancelToken.IsCancellationRequested)
                    break;

                if (mapping.Emulator == null)
                {
                    Logger.Warn($"Emulator {mapping.EmulatorId} not found, skipping.");
                    continue;
                }

                if (mapping.EmulatorProfile == null)
                {
                    Logger.Warn($"Emulator profile {mapping.EmulatorProfileId} for emulator {mapping.EmulatorId} not found, skipping.");
                    continue;
                }

                if (mapping.Platform == null)
                {
                    Logger.Warn($"Platform {mapping.PlatformId} not found, skipping.");
                    continue;
                }

                string url = $"{Settings.RomMHost}/api/roms";
                RomMPlatform apiPlatform = apiPlatforms.FirstOrDefault(p => p.IgdbId == mapping.Platform.IgdbId);

                if (apiPlatform == null)
                {
                    Logger.Warn($"Platform {mapping.Platform.Name} with IGDB ID {mapping.Platform.IgdbId} not found in RomM, skipping.");
                    continue;
                }

                Logger.Debug($"Starting to fetch games for {apiPlatform.Name}.");

                NameValueCollection queryParams = new NameValueCollection
                {
                    { "limit", "2500" },
                    { "offset", "0" },
                    { "platform_id", apiPlatform.Id.ToString() },
                    { "order_by", "name" },
                    { "order_dir", "asc" },
                };

                var responseGameIDs = new HashSet<string>();
                try
                {
                    // Make the request and get the response
                    HttpResponseMessage response = GetAsyncWithParams(url, queryParams).GetAwaiter().GetResult();
                    response.EnsureSuccessStatusCode();

                    Logger.Debug($"Starting to parse response for {apiPlatform.Name}.");

                    // Assuming the response is in JSON format
                    Stream body = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                    List<RomMRom> roms;
                    using (StreamReader reader = new StreamReader(body))
                    using (JsonTextReader jsonReader = new JsonTextReader(reader))
                    {
                        roms = JsonSerializerSingleton.Instance.Deserialize<List<RomMRom>>(jsonReader);
                    }

                    Logger.Debug($"Finished parsing response for {apiPlatform.Name}.");

                    var rootInstallDir = PlayniteApi.Paths.IsPortable
                        ? mapping.DestinationPathResolved.Replace(PlayniteApi.Paths.ApplicationPath, ExpandableVariables.PlayniteDirectory)
                        : mapping.DestinationPathResolved;

                    // Return a GameMetadata for each item in the response
                    foreach (var item in roms)
                    {
                        var gameName = item.Name;
                        var fileName = item.FileName;
                        var urlCover = item.UrlCover;
                        var gameInstallDir = Path.Combine(rootInstallDir, Path.GetFileNameWithoutExtension(fileName));
                        var pathToGame = Path.Combine(gameInstallDir, fileName);

                        var info = new RomMGameInfo
                        {
                            MappingId = mapping.MappingId,
                            FileName = fileName,
                            DownloadUrl = $"{Settings.RomMHost}/api/roms/{item.Id}/content/{fileName}",
                            IsMulti = item.Multi
                        };
                        var gameId = info.AsGameId();
                        responseGameIDs.Add(gameId);

                        // Check if the game is already installed
                        if (Playnite.Database.Games.Any(g => g.GameId == gameId))
                        {
                            continue;
                        }

                        var gameNameWithTags  = $"{gameName}{(item.Regions.Count > 0 ? $" ({string.Join(", ", item.Regions)})" : "")}{(!string.IsNullOrEmpty(item.Revision) ? $" (Rev {item.Revision})" : "")}{(item.Tags.Count > 0 ? $" ({string.Join(", ", item.Tags)})" : "")}";

                        // Add newly found game
                        games.Add(new GameMetadata
                        {
                            Source = SourceName,
                            Name = gameNameWithTags,
                            Roms = new List<GameRom> { new GameRom(gameNameWithTags, pathToGame) },
                            InstallDirectory = gameInstallDir,
                            IsInstalled = File.Exists(pathToGame),
                            GameId = gameId,
                            Platforms = new HashSet<MetadataProperty> { new MetadataNameProperty(mapping.Platform.Name ?? "") },
                            Regions = new HashSet<MetadataProperty>(item.Regions.Where(r => !string.IsNullOrEmpty(r)).Select(r => new MetadataNameProperty(r.ToString()))),
                            InstallSize = item.FileSizeBytes,
                            Description = item.Summary,
                            Icon = !string.IsNullOrEmpty(urlCover) ? new MetadataFile(urlCover) : null,
                            GameActions = new List<GameAction>
                            {
                                new GameAction
                                {
                                    Name = $"Play in {mapping.Emulator.Name}",
                                    Type = GameActionType.Emulator,
                                    EmulatorId = mapping.EmulatorId,
                                    EmulatorProfileId = mapping.EmulatorProfileId,
                                    IsPlayAction = true,
                                },
                                new GameAction
                                {
                                    Type = GameActionType.URL,
                                    Name = "View in RomM",
                                    Path = $"{Settings.RomMHost}/rom/{item.Id}",
                                    IsPlayAction = false
                                }
                            }
                        });
                    }

                    Logger.Debug($"Finished adding new games for {apiPlatform.Name}");

                    // Find games in the database that are not in the response
                    var gamesInDatabase = Playnite.Database.Games.Where(g =>
                        g.Source != null && g.Source.Name == SourceName.ToString() &&
                        g.Platforms != null && g.Platforms.Any(p => p.Name == mapping.Platform.Name)
                    );

                    Logger.Debug($"Starting to remove not found games for {apiPlatform.Name}.");

                    foreach (var game in gamesInDatabase)
                    {
                        if (args.CancelToken.IsCancellationRequested)
                            break;

                        if (responseGameIDs.Contains(game.GameId))
                        {
                            continue;
                        }

                        // Remove from the playnite database
                        Playnite.Database.Games.Remove(game.Id);
                    }

                    Logger.Debug($"Finished removing not found games for {apiPlatform.Name}");
                }
                catch (HttpRequestException e)
                {
                    Logger.Error($"Request exception: {e.Message}");
                    return games;
                }
            }

            return games;
        }


        public override ISettings GetSettings(bool firstRunSettings)
        {
            return Settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new SettingsView();
        }

        public override IEnumerable<InstallController> GetInstallActions(GetInstallActionsArgs args)
        {
            if (args.Game.PluginId == Id)
            {
                yield return args.Game.GetRomMGameInfo().GetInstallController(args.Game, this);
            }
        }

        public override IEnumerable<UninstallController> GetUninstallActions(GetUninstallActionsArgs args)
        {
            if (args.Game.PluginId == Id)
            {
                yield return args.Game.GetRomMGameInfo().GetUninstallController(args.Game, this);
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
    }
}

