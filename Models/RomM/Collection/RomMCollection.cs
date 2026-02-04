using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace RomM.Models.RomM.Collection
{
    public class RomMCollection
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("rom_ids")]
        public List<int> RomIds { get; set; }

        [JsonProperty("is_favorite")]
        public bool IsFavorite { get; set; }
    }
}