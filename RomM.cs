using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using RomM.Downloads;
using RomM.Games;
using RomM.Models.RomM.Platform;
using RomM.Models.RomM.Rom;
using RomM.Settings;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
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
                HasSettings = true
            };

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

        private string CombineUrl(string baseUrl, string relativePath)
        {
            return $"{baseUrl?.TrimEnd('/')}/{relativePath?.TrimStart('/') ?? ""}";
        }

        internal IList<RomMPlatform> FetchPlatforms()
        {
            string apiPlatformsUrl = CombineUrl(Settings.RomMHost, "api/platforms");
            try
            {
                HttpResponseMessage response = HttpClientSingleton.Instance.GetAsync(apiPlatformsUrl).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

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
                if (mapping.Platform.IgdbId.ToString() == platformIgdbId)
                {
                    var gameName = rom.Name;

                    var game = Playnite.Database.Games.FirstOrDefault(g => g.Source.Name == SourceName.ToString() &&
                                                                           g.Platforms.Any(p => p.Name == mapping.Platform.Name) &&
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

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            base.OnApplicationStarted(args);

            Settings = new SettingsViewModel(this, this);
            HttpClientSingleton.ConfigureBasicAuth(Settings.RomMUsername, Settings.RomMPassword);
            Playnite.UriHandler.RegisterSource("romm", HandleRommUri);

            Playnite.Database.Games.ItemUpdated += OnItemUpdated;

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

        // Old-style overload (keeps older call sites working)
        public static Task<HttpResponseMessage> GetAsync(
            string url,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
        {
            return HttpClientSingleton.Instance.GetAsync(url, completionOption);
        }

        // New-style overload (used by DownloadQueueController)
        public static Task<HttpResponseMessage> GetAsync(
            string url,
            HttpCompletionOption completionOption,
            CancellationToken ct)
        {
            return HttpClientSingleton.Instance.GetAsync(url, completionOption, ct);
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

            if (string.IsNullOrEmpty(Settings.RomMHost) ||
                string.IsNullOrEmpty(Settings.RomMUsername) ||
                string.IsNullOrEmpty(Settings.RomMPassword))
            {
                Logger.Warn("RomM host, username or password is not set.");
                return new List<GameMetadata>();
            }

            IList<RomMPlatform> apiPlatforms = FetchPlatforms();
            List<GameMetadata> games = new List<GameMetadata>();
            IEnumerable<EmulatorMapping> enabledMappings = SettingsViewModel.Instance.Mappings?.Where(m => m.Enabled);

            if (enabledMappings == null || !enabledMappings.Any())
            {
                Logger.Warn("No emulators are configured or enabled in RomM settings. No games will be fetched.");
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

                string url = CombineUrl(Settings.RomMHost, "api/roms");
                RomMPlatform apiPlatform = apiPlatforms.FirstOrDefault(p => p.IgdbId == mapping.Platform.IgdbId);

                if (apiPlatform == null)
                {
                    Logger.Warn($"Platform {mapping.Platform.Name} with IGDB ID {mapping.Platform.IgdbId} not found in RomM, skipping.");
                    continue;
                }

                Logger.Debug($"Starting to fetch games for {apiPlatform.Name}.");

                const int pageSize = 72;
                int offset = 0;
                bool hasMoreData = true;
                var allRoms = new List<RomMRom>();
                var responseGameIDs = new HashSet<string>();

                while (hasMoreData)
                {
                    if (args.CancelToken.IsCancellationRequested)
                        break;

                    NameValueCollection queryParams = new NameValueCollection
                    {
                        { "limit", pageSize.ToString() },
                        { "offset", offset.ToString() },
                        { "platform_id", apiPlatform.Id.ToString() }, // singular
                        { "order_by", "name" },
                        { "order_dir", "asc" },
                    };

                    try
                    {
                        HttpResponseMessage response = GetAsyncWithParams(url, queryParams).GetAwaiter().GetResult();
                        response.EnsureSuccessStatusCode();

                        Logger.Debug($"Parsing response for {apiPlatform.Name} batch {offset / pageSize + 1}.");

                        Stream body = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                        List<RomMRom> roms;
                        using (StreamReader reader = new StreamReader(body))
                        {
                            var jsonResponse = JObject.Parse(reader.ReadToEnd());
                            roms = jsonResponse["items"].ToObject<List<RomMRom>>();
                        }

                        Logger.Debug($"Parsed {roms.Count} roms for batch {offset / pageSize + 1}.");
                        allRoms.AddRange(roms);

                        if (roms.Count < pageSize)
                        {
                            Logger.Debug($"Received less than {pageSize} roms for {apiPlatform.Name}, assuming no more games.");
                            hasMoreData = false;
                            break;
                        }

                        offset += pageSize;
                    }
                    catch (HttpRequestException e)
                    {
                        Logger.Error($"Request exception: {e.Message}");
                        hasMoreData = false;
                    }
                }

                try
                {
                    Logger.Debug($"Finished parsing response for {apiPlatform.Name}.");

                    var rootInstallDir = PlayniteApi.Paths.IsPortable
                        ? mapping.DestinationPathResolved.Replace(PlayniteApi.Paths.ApplicationPath, ExpandableVariables.PlayniteDirectory)
                        : mapping.DestinationPathResolved;

                    foreach (var item in allRoms)
                    {
                        if (args.CancelToken.IsCancellationRequested)
                            break;

                        var gameName = item.Name;

                        // Defensive: never allow path segments from server-provided filename
                        var fileName = Path.GetFileName(item.FileName);
                        if (string.IsNullOrWhiteSpace(fileName))
                        {
                            Logger.Warn($"Rom {item.Id} returned empty/invalid filename, skipping.");
                            continue;
                        }

                        var urlCover = item.UrlCover;
                        var gameInstallDir = Path.Combine(rootInstallDir, Path.GetFileNameWithoutExtension(fileName));
                        var pathToGame = Path.Combine(gameInstallDir, fileName);

                        var info = new RomMGameInfo
                        {
                            MappingId = mapping.MappingId,
                            FileName = fileName,
                            DownloadUrl = CombineUrl(Settings.RomMHost, $"api/roms/{item.Id}/content/{fileName}"),
                            HasMultipleFiles = item.HasMultipleFiles
                        };

                        var gameId = info.AsGameId();
                        responseGameIDs.Add(gameId);

                        if (Playnite.Database.Games.Any(g => g.GameId == gameId))
                        {
                            continue;
                        }

                        var gameNameWithTags =
                            $"{gameName}" +
                            $"{(item.Regions.Count > 0 ? $" ({string.Join(", ", item.Regions)})" : "")}" +
                            $"{(!string.IsNullOrEmpty(item.Revision) ? $" (Rev {item.Revision})" : "")}" +
                            $"{(item.Tags.Count > 0 ? $" ({string.Join(", ", item.Tags)})" : "")}";

                        games.Add(new GameMetadata
                        {
                            Source = SourceName,
                            Name = gameName,
                            Roms = new List<GameRom> { new GameRom(gameNameWithTags, pathToGame) },
                            InstallDirectory = gameInstallDir,
                            IsInstalled = File.Exists(pathToGame),
                            GameId = gameId,
                            Platforms = new HashSet<MetadataProperty> { new MetadataNameProperty(mapping.Platform.Name ?? "") },
                            Regions = new HashSet<MetadataProperty>(item.Regions.Where(r => !string.IsNullOrEmpty(r)).Select(r => new MetadataNameProperty(r.ToString()))),
                            InstallSize = item.FileSizeBytes,
                            Description = item.Summary,
                            CoverImage = !string.IsNullOrEmpty(urlCover) ? new MetadataFile(urlCover) : null,
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
                                    Path = CombineUrl(Settings.RomMHost, $"rom/{item.Id}"),
                                    IsPlayAction = false
                                }
                            }
                        });
                    }

                    Logger.Debug($"Finished adding new games for {apiPlatform.Name}");

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

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            if (DownloadsSidebar != null)
            {
                yield return DownloadsSidebar;
            }
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

        private void OnItemUpdated(object sender, ItemUpdatedEventArgs<Game> e)
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
            }
        }
    }
}
