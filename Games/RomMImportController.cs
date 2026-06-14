using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using RomM.Models.RomM.Collection;
using RomM.Models.RomM.Platform;
using RomM.Models.RomM.Rom;
using RomM.Settings;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace RomM.Games
{
    class RomMImportController
    {
        private readonly RomM _plugin;
        public ILogger Logger => LogManager.GetLogger();

        public RomMImportController(RomM plugin)
        {
            _plugin = plugin;
        }

        public List<Game> Import(LibraryImportGamesArgs args)
        {
            // Only block servers we can positively identify as older than 4.9. Dev builds report a
            // non-numeric version (e.g. "development"), which we assume is recent enough to import.
            // Pre-release suffixes (e.g. "4.9.0-beta") are stripped before parsing.
            string rawVersion = (_plugin.Settings.ServerVersion ?? string.Empty).Split('-', '+')[0];
            if (Version.TryParse(rawVersion, out Version versionParsed))
            {
                if (versionParsed.CompareTo(new Version(4, 9)) < 0)
                {
                    _plugin.Playnite.Notifications.Add(_plugin.Id.ToString(), "RomM Server 4.9 or later required to import ROMs!", NotificationType.Error);
                    return new List<Game>();
                }
            }
            else
            {
                Logger.Warn($"[Import Controller] Could not parse RomM server version \"{_plugin.Settings.ServerVersion}\"; assuming it's compatible (>= 4.9).");
            }

            IList<RomMPlatform> apiPlatforms = FetchPlatforms();
            List<Game> games = new List<Game>();
            IEnumerable<EmulatorMapping> enabledMappings = SettingsViewModel.Instance.Mappings?.Where(m => m.Enabled);
            string url = BuildROMUrl();

            if (enabledMappings == null || !enabledMappings.Any())
            {
                _plugin.Playnite.Notifications.Add(_plugin.Id.ToString(), "No emulators are configured or enabled in RomM settings. No games will be fetched.", NotificationType.Error);
                return games;
            }

            IList<RomMCollection> favoritCollections = _plugin.FetchFavorites();
            var favorites = favoritCollections.FirstOrDefault(c => c.IsFavorite)?.RomIds ?? new List<int>();

            // Pull ROM data for each enabled mapping and add the games to playnite
            foreach (var mapping in enabledMappings)
            {
                if (args.CancelToken.IsCancellationRequested)
                    break;

                // Check mapping has an Emulator, Profile & Platform assigned to it
                if (mapping.Emulator == null || mapping.EmulatorProfile == null || mapping.RomMPlatform == null || mapping.RomMPlatform.Id == -1)
                {
                    Logger.Warn($"[Import Controller] Emulator {mapping.MappingId} is misconfigured, skipping.");
                    continue;
                }

                RomMPlatform apiPlatform = apiPlatforms.FirstOrDefault(p => p.Id == mapping.RomMPlatformId);
                if (apiPlatform == null)
                {
                    _plugin.Playnite.Notifications.Add(_plugin.Id.ToString(), $"Platform {mapping.RomMPlatform.Name} with ID {mapping.RomMPlatformId} not found in RomM, skipping.", NotificationType.Error);
                    continue;
                }

                // Pull data from server
                // Could be made async, but when testing (4.7.0) found a performance degradation
                var romMROMs = DownloadROMData(args, url, apiPlatform);
                if(romMROMs == null)
                {
                    Logger.Warn($"[Import Controller] Failed to get ROMs for {apiPlatform.Name}.");
                    continue;
                }
                Logger.Info($"[Import Controller] Finished parsing response for {apiPlatform.Name}.");

                // Import games for the current mapping.
                // ProcessData mutates the Playnite database (ImportGame/Update/Remove), which is not
                // thread-safe, so each mapping is processed sequentially to avoid races/DependencySource
                // errors. The expensive network fetch above already ran synchronously.
                RomMImport newImport = new RomMImport(_plugin, args, mapping, romMROMs, favorites);
                games.AddRange(newImport.ProcessData());
            }

            return games;
        }

        private static async Task<HttpResponseMessage> GetAsyncWithParams(string baseUrl, NameValueCollection queryParams)
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

        private string BuildROMUrl()
        {
            string url = _plugin.CombineUrl(_plugin.Settings.RomMHost, "api/roms");

            if (_plugin.Settings.SkipMissingFiles)
            {
                url += "?missing=false&";
            }

            // Exclude genres from import
            string excludeGenresString = (_plugin.Settings.ExcludeGenres ?? string.Empty).Trim(' ');
            excludeGenresString = excludeGenresString.Trim(';');
            List<string> excludeGenres = excludeGenresString.Split(';').ToList();
            if (!string.IsNullOrEmpty(excludeGenresString))
            {
                // Add ? if it hasn't been added already
                if (!_plugin.Settings.SkipMissingFiles)
                {
                    url += "?";
                }

                foreach (var genre in excludeGenres)
                {
                    url += $"genres={HttpUtility.UrlEncode(genre)}&";
                }
            }
            return url.TrimEnd('&');
        }
        private IList<RomMPlatform> FetchPlatforms()
        {
            string apiPlatformsUrl = _plugin.CombineUrl(_plugin.Settings.RomMHost, "api/platforms");
            try
            {
                HttpResponseMessage response = HttpClientSingleton.Instance.GetAsync(apiPlatformsUrl).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonConvert.DeserializeObject<List<RomMPlatform>>(body);
            }
            catch (HttpRequestException e)
            {
                Logger.Error($"[Import Controller] Request exception: {e.Message}");
                return new List<RomMPlatform>();
            }
        }
        
        private List<RomMRom> DownloadROMData(LibraryImportGamesArgs args, string url, RomMPlatform platform)
        {
            Logger.Info($"[Import Controller] Starting to fetch games for {platform.Name}.");

            const int pageSize = 50;
            int offset = 0;
            bool hasMoreData = true;
            var romData = new List<RomMRom>();

            // Download data from RomM server
            while (hasMoreData)
            {
                if (args.CancelToken.IsCancellationRequested)
                    break;

                NameValueCollection queryParams = new NameValueCollection
                    {
                        { "platform_ids", platform.Id.ToString() },
                        { "genres_logic", "none" },
                        { "order_by", "name" },
                        { "order_dir", "asc" },
                        { "with_files", "true" },
                        { "with_siblings", "true" },
                        { "limit", pageSize.ToString() },
                        { "offset", offset.ToString() },
                    };

                try
                {
                    HttpResponseMessage response = GetAsyncWithParams(url, queryParams).GetAwaiter().GetResult();
                    response.EnsureSuccessStatusCode();

                    Logger.Info($"[Import Controller] Parsing response for {platform.Name} batch {offset / pageSize + 1}.");

                    Stream body = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                    List<RomMRom> roms;
                    using (StreamReader reader = new StreamReader(body))
                    {
                        var jsonResponse = JObject.Parse(reader.ReadToEnd());
                        roms = jsonResponse["items"].ToObject<List<RomMRom>>();
                    }

                    Logger.Info($"[Import Controller] Parsed {roms.Count} roms for batch {offset / pageSize + 1}.");
                    romData.AddRange(roms);

                    if (roms.Count < pageSize)
                    {
                        Logger.Info($"[Import Controller] Received less than {pageSize} roms for {platform.Name}, assuming no more games.");
                        hasMoreData = false;
                        break;
                    }

                    offset += pageSize;
                }
                catch (HttpRequestException e)
                {
                    Logger.Error($"[Import Controller] Request exception: {e.Message}");
                    hasMoreData = false;
                }
            }

            return romData;
        }
    }

}
