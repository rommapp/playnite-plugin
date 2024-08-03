using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace RomM.Models.RomM.Platform
{
public class RomMPlatform
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonProperty("fs_slug")]
        public string FsSlug { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("rom_count")]
        public int RomCount { get; set; }

        [JsonProperty("igdb_id")]
        public ulong? IgdbId { get; set; }

        [JsonProperty("sgdb_id")]
        public object SgdbId { get; set; }

        [JsonProperty("moby_id")]
        public object MobyId { get; set; }

        [JsonProperty("logo_path")]
        public string LogoPath { get; set; }

        [JsonProperty("firmware")]
        public List<RomMPlatformFirmware> Firmware { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }


}