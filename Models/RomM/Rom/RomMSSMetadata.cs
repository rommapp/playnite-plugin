using Newtonsoft.Json;

namespace RomM.Models.RomM.Rom
{
    // Screenscraper metadata/media on a rom. Only the media we consume (for the game icon) are
    // modelled here; Newtonsoft ignores the rest of the object.
    public class RomMSSMetadata
    {
        // Screenscraper "wheel" (clear logo). _path is served by RomM (relative), _url is external.
        [JsonProperty("logo_path")]
        public string LogoPath { get; set; }
        [JsonProperty("logo_url")]
        public string LogoUrl { get; set; }

        [JsonProperty("miximage_path")]
        public string MiximagePath { get; set; }
        [JsonProperty("miximage_url")]
        public string MiximageUrl { get; set; }
    }
}
