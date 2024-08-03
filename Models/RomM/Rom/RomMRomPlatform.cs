using Newtonsoft.Json;

namespace RomM.Models.RomM.Rom
{
    public class RomMRomPlatform
    {
        [JsonProperty("igdb_id")]
        public int? IgdbId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }
}