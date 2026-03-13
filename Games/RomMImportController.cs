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
using System.Diagnostics;
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
            IList<RomMPlatform> apiPlatforms = FetchPlatforms();
            List<Task<List<Game>>> tasks = new List<Task<List<Game>>>();
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
                if (mapping.Emulator == null || mapping.EmulatorProfile == null || mapping.Platform == null)
                {
                    Logger.Warn($"Emulator {mapping.EmulatorId} is misconfigured, skipping.");
                    continue;
                }

                RomMPlatform apiPlatform = apiPlatforms.FirstOrDefault(p => p.IgdbId == mapping.Platform.IgdbId);
                if (apiPlatform == null)
                {
                    _plugin.Playnite.Notifications.Add(_plugin.Id.ToString(), $"Platform {mapping.Platform.Name} with IGDB ID {mapping.Platform.IgdbId} not found in RomM, skipping.", NotificationType.Error);
                    continue;
                }

                // Pull data from server
                // Could be made async, but when testing (4.7.0) found a performance degradation
                var romMROMs = DownloadROMData(args, url, apiPlatform);
                if(romMROMs == null)
                {
                    Logger.Debug($"Failed to get ROMs for {apiPlatform.Name}.");
                    continue;
                }
                Logger.Debug($"Finished parsing response for {apiPlatform.Name}.");

                // Import games for current mapping 
                tasks.Add(Task<List<Game>>.Factory.StartNew(() =>
                {
                    RomMImport newImport = new RomMImport(_plugin, args, mapping, romMROMs, favorites);
                    return newImport.ProcessData();
                }));

            }

            Task.WhenAll(tasks).Wait();
            
            foreach (var task in tasks)
            {
                games.AddRange(task.Result);
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
                url += "?missing=false";
            }

            // Exclude genres from import
            List<string> excludeGenres = _plugin.Settings.ExcludeGenres.Split(';').ToList();
            if (!string.IsNullOrEmpty(_plugin.Settings.ExcludeGenres))
            {
                // Add ? if it hasn't been added already
                if (!_plugin.Settings.SkipMissingFiles)
                {
                    url += "?";
                }

                if (excludeGenres.Count > 0)
                {
                    foreach (var genre in excludeGenres)
                    {
                        url += $"genres={HttpUtility.UrlEncode(genre)}&";
                    }
                }
                else
                {
                    url += $"genres={HttpUtility.UrlEncode(_plugin.Settings.ExcludeGenres)}";
                }
            }

            return url;
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
                Logger.Error($"Request exception: {e.Message}");
                return new List<RomMPlatform>();
            }
        }
        
        private List<RomMRom> DownloadROMData(LibraryImportGamesArgs args, string url, RomMPlatform platform)
        {
            Logger.Debug($"Starting to fetch games for {platform.Name}.");

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
                        { "limit", pageSize.ToString() },
                        { "offset", offset.ToString() },
                    };

                try
                {
                    HttpResponseMessage response = GetAsyncWithParams(url, queryParams).GetAwaiter().GetResult();
                    response.EnsureSuccessStatusCode();

                    Logger.Debug($"Parsing response for {platform.Name} batch {offset / pageSize + 1}.");

                    Stream body = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                    List<RomMRom> roms;
                    using (StreamReader reader = new StreamReader(body))
                    {
                        var jsonResponse = JObject.Parse(reader.ReadToEnd());
                        roms = jsonResponse["items"].ToObject<List<RomMRom>>();
                    }

                    Logger.Debug($"Parsed {roms.Count} roms for batch {offset / pageSize + 1}.");
                    romData.AddRange(roms);

                    if (roms.Count < pageSize)
                    {
                        Logger.Debug($"Received less than {pageSize} roms for {platform.Name}, assuming no more games.");
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

            return romData;
        }
    }

}
