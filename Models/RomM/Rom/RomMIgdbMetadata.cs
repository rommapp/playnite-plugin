using System.Collections.Generic;
using Newtonsoft.Json;

namespace RomM.Models.RomM.Rom
{
    public class AgeRating
    {
        [JsonProperty("rating")]
        public string Rating { get; set; }

        [JsonProperty("category")]
        public string RatingsBoard { get; set; }

        [JsonProperty("rating_cover_url")]
        public string RatingsCover { get; set; }
    }

    public class RomMIgdbMetadata
    {
        [JsonProperty("total_rating")]
        public string TotalRating { get; set; }

        [JsonProperty("aggregated_rating")]
        public string AggregatedRating { get; set; }

        [JsonProperty("first_release_date")]
        public long? FirstReleaseDate { get; set; }

        [JsonProperty("genres")]
        public List<string> Genres { get; set; }

        [JsonProperty("franchises")]
        public List<string> Franchises { get; set; }

        [JsonProperty("alternative_names")]
        public List<string> AlternativeNames { get; set; }

        [JsonProperty("collections")]
        public List<string> Collections { get; set; }

        [JsonProperty("companies")]
        public List<string> Companies { get; set; }

        [JsonProperty("game_modes")]
        public List<string> GameModes { get; set; }

        [JsonProperty("age_ratings")]
        public List<AgeRating> AgeRatings { get; set; }

        [JsonProperty("platforms")]
        public List<RomMRomPlatform> Platforms { get; set; }

        [JsonProperty("expansions")]
        public List<RomMExpansion> Expansions { get; set; }

        [JsonProperty("dlcs")]
        public List<RomMDLC> Dlcs { get; set; }

        [JsonProperty("remasters")]
        public List<RomMRemaster> Remasters { get; set; }

        [JsonProperty("remakes")]
        public List<RomMRemake> Remakes { get; set; }

        [JsonProperty("expanded_games")]
        public List<RomMExpandedGame> ExpandedGames { get; set; }

        [JsonProperty("ports")]
        public List<RomMPort> Ports { get; set; }

        [JsonProperty("similar_games")]
        public List<RomMSimilarGame> SimilarGames { get; set; }
    }
}
