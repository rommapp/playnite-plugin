﻿using Playnite.SDK;
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
using Newtonsoft.Json.Linq;
using RomM.Settings;
using Playnite.SDK.Events;
using RomM.Games;


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

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            base.OnApplicationStarted(args);
            Settings = new SettingsViewModel(this, this);
            HttpClientSingleton.ConfigureBasicAuth(Settings.RomMUsername, Settings.RomMPassword);
            this.Searches = new List<SearchSupport> { new SearchSupport("romm", "RomM", new RomMSearchContext()) };
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
                yield break;
            }

            // Return early if host, username or password is not set
            if (string.IsNullOrEmpty(Settings.RomMHost) || string.IsNullOrEmpty(Settings.RomMUsername) || string.IsNullOrEmpty(Settings.RomMPassword))
            {
                Logger.Warn("RomM host, username or password is not set.");
                yield break;
            }

            string apiPlatformsUrl = $"{Settings.RomMHost}/api/platforms";
            JArray apiPlatforms = new JArray();
            List<GameMetadata> games = new List<GameMetadata>();

            try
            {
                // Make the request and get the response
                HttpResponseMessage response = HttpClientSingleton.Instance.GetAsync(apiPlatformsUrl).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                // Assuming the response is in JSON format
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                apiPlatforms = JArray.Parse(body);
            }
            catch (HttpRequestException e)
            {
                Logger.Error($"Request exception: {e.Message}");
                yield break;
            }

            foreach (var mapping in SettingsViewModel.Instance.Mappings?.Where(m => m.Enabled))
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
                NameValueCollection queryParams = new NameValueCollection
                {
                    { "size", "10000" },
                    { "platform_id", apiPlatforms.FirstOrDefault(p => p["igdb_id"].ToObject<ulong>() == mapping.Platform.IgdbId)["id"].ToString() }
                };

                var responseGameIDs = new List<string>();

                try
                {
                    // Make the request and get the response
                    HttpResponseMessage response = GetAsyncWithParams(url, queryParams).GetAwaiter().GetResult();
                    response.EnsureSuccessStatusCode();

                    // Assuming the response is in JSON format
                    string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    JObject jsonObject = JObject.Parse(body);
                    var items = jsonObject["items"].Children();
                    var installDir = PlayniteApi.Paths.IsPortable ? mapping.DestinationPathResolved.Replace(PlayniteApi.Paths.ApplicationPath, ExpandableVariables.PlayniteDirectory) : mapping.DestinationPathResolved;

                    // Return a GameMetadata for each item in the response
                    foreach (var item in items)
                    {
                        var gameName = item["name"].ToString();
                        var fileName = item["file_name"].ToString();
                        var pathToGame = Path.Combine(installDir, fileName);

                        var info = new RomMGameInfo()
                        {
                            MappingId = mapping.MappingId,
                            FileName = fileName,
                            DownloadUrl = $"{Settings.RomMHost}/api/roms/{item["id"].ToObject<int>()}/content/{fileName}",
                            IsMulti = item["multi"].ToObject<bool>()
                        };
                        var gameId = info.AsGameId();
                        responseGameIDs.Add(gameId);

                        // Check if the game is already installed
                        if (Playnite.Database.Games.Where(g => g.GameId == gameId).Any())
                        {
                            continue;
                        }

                        // Add newly found game
                        games.Add(new GameMetadata
                        {
                            Source = RomM.SourceName,
                            Name = gameName,
                            Roms = new List<GameRom>() { new GameRom(gameName, pathToGame) },
                            InstallDirectory = installDir,
                            IsInstalled = File.Exists(pathToGame),
                            GameId = gameId,
                            Platforms = new HashSet<MetadataProperty>() { new MetadataNameProperty(mapping.Platform.Name) },
                            Regions = new HashSet<MetadataProperty>(item["regions"].Select(r => new MetadataNameProperty(r.ToString()))),
                            InstallSize = (ulong)item["file_size_bytes"],
                            Description = item["summary"].ToString(),
                            GameActions = new List<GameAction>
                            {
                                new GameAction()
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
                                    Path = $"{Settings.RomMHost}/rom/{item["id"].ToObject<int>()}",
                                    IsPlayAction = false
                                }
                            }
                        });
                    }

                    // Find games in the database that are not in the response
                    var gamesInDatabase = Playnite.Database.Games.Where(g => g.Source.Name == RomM.SourceName.ToString() && g.Platforms.Where(p => p.Name == mapping.Platform.Name).Any());
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
                }
                catch (HttpRequestException e)
                {
                    Logger.Error($"Request exception: {e.Message}");
                    yield break;
                }

                foreach (var game in games)
                {
                    yield return game;
                }
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
    }
}
