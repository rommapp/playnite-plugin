using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace RomM.Models.RomM.Rom
{
    public class RomMRom
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("igdb_id")]
        public int? IgdbId { get; set; }

        [JsonProperty("sgdb_id")]
        public object SgdbId { get; set; }

        [JsonProperty("moby_id")]
        public object MobyId { get; set; }

        [JsonProperty("platform_id")]
        public int PlatformId { get; set; }

        [JsonProperty("platform_slug")]
        public string PlatformSlug { get; set; }

        [JsonProperty("platform_name")]
        public string PlatformName { get; set; }

        [JsonProperty("file_name")]
        public string FileName { get; set; }

        [JsonProperty("file_name_no_tags")]
        public string FileNameNoTags { get; set; }

        [JsonProperty("file_name_no_ext")]
        public string FileNameNoExt { get; set; }

        [JsonProperty("file_extension")]
        public string FileExtension { get; set; }

        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        [JsonProperty("file_size_bytes")]
        public ulong FileSizeBytes { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonProperty("summary")]
        public string Summary { get; set; }

        [JsonProperty("first_release_date")]
        public long? FirstReleaseDate { get; set; }

        [JsonProperty("alternative_names")]
        public List<string> AlternativeNames { get; set; }

        [JsonProperty("genres")]
        public List<string> Genres { get; set; }

        [JsonProperty("franchises")]
        public List<string> Franchises { get; set; }

        [JsonProperty("collections")]
        public List<string> Collections { get; set; }

        [JsonProperty("companies")]
        public List<string> Companies { get; set; }

        [JsonProperty("game_modes")]
        public List<string> GameModes { get; set; }

        [JsonProperty("igdb_metadata")]
        public RomMIgdbMetadata IgdbMetadata { get; set; }

        [JsonProperty("path_cover_s")]
        public string PathCoverS { get; set; }

        [JsonProperty("path_cover_l")]
        public string PathCoverL { get; set; }

        [JsonProperty("has_cover")]
        public bool HasCover { get; set; }

        [JsonProperty("url_cover")]
        public string UrlCover { get; set; }

        [JsonProperty("revision")]
        public string Revision { get; set; }

        [JsonProperty("regions")]
        public List<string> Regions { get; set; }

        [JsonProperty("languages")]
        public List<string> Languages { get; set; }

        [JsonProperty("tags")]
        public List<string> Tags { get; set; }

        [JsonProperty("multi")]
        public bool Multi { get; set; }

        [JsonProperty("files")]
        public List<object> Files { get; set; }

        [JsonProperty("full_path")]
        public string FullPath { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonProperty("rom_user")]
        public object RomUser { get; set; }

        [JsonProperty("sort_comparator")]
        public string SortComparator { get; set; }
    }
}
