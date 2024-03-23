
using Newtonsoft.Json.Linq;
using Playnite.SDK.Plugins;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net.Http;

namespace RomM.Settings
{
    public class RomMSearchContext : SearchContext
    {
        public RomMSearchContext()
        {
            Description = "Default search description";
            Label = "Default search";
            Hint = "Search hint goes here";
        }

        public override IEnumerable<SearchItem> GetSearchResults(GetSearchResultsArgs args)
        {
            if (args.CancelToken.IsCancellationRequested)
                    yield break;

            // Use args.SearchTerm to access search query
            string url = $"{SettingsViewModel.Instance.RomMHost}/api/roms";
            NameValueCollection queryParams = new NameValueCollection
            {
                { "size", "250" },
                { "search_term", args.SearchTerm },
                { "order_by", "name" },
                { "order_dir", "asc" }
            }; 
                
            try
            {
                // Make the request and get the response
                HttpResponseMessage response = RomM.GetAsyncWithParams(url, queryParams).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                // Assuming the response is in JSON format
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                JObject jsonObject = JObject.Parse(body);
                var items = jsonObject["items"].Children();

                foreach (var item in items)
                {

                }
            }
            catch (HttpRequestException e)
            {
                yield break;
            }
        }
    }
}
