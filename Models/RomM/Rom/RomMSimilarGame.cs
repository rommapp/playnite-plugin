using Newtonsoft.Json;

namespace RomM.Models.RomM.Rom
{
    public class RomMSimilarGame
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("cover_url")]
        public string CoverUrl { get; set; }
    }
}