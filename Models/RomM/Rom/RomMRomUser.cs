using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace RomM.Models.RomM.Rom
{
    public class RomMRomUser
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("user_id")]
        public int UserId { get; set; }

        [JsonProperty("last_played")]
        public DateTime? LastPlayed { get; set; }

        [JsonProperty("backlogged")]
        public bool Backlogged { get; set; }

        [JsonProperty("now_playing")]
        public bool NowPlaying { get; set; }

        [JsonProperty("rating")]
        public int Rating { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        public static readonly Dictionary<string, string> CompletionStatusMap = new Dictionary<string, string>
        {
            { "never_playing", "Abandoned" },
            { "retired", "Played" },
            { "incomplete", "On Hold" },
            { "finished", "Beaten" },
            { "completed_100", "Completed" },
            { "backlogged", "Plan to Play" },
            { "now_playing", "Playing" },
            { "not_played", "Not Played" }
        };
    }
}