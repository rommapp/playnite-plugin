using Newtonsoft.Json;

namespace RomM.Models.RomM.Rom
{
    // Screenscraper metadata/media on a rom. Only the media we consume (for the game icon) are
    // modelled here; Newtonsoft ignores the rest of the object.
    public class RomMSSMetadata
    {
        // Composite image with consistent dimensions. _path is served by RomM (relative), _url is external.
        [JsonProperty("miximage_path")]
        public string MiximagePath { get; set; }
        [JsonProperty("miximage_url")]
        public string MiximageUrl { get; set; }
    }
}
