using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace RomM.Models.RomM.Rom
{
    public class metadatum
    {
        [JsonProperty("rom_id")]
        public int Id { get; set; }

        [JsonProperty("genres")]
        public List<string> Genres { get; set; }

        [JsonProperty("franchises")]
        public List<string> Franchises { get; set; }

        [JsonProperty("collections")]
        public List<string> Collections { get; set; }

        [JsonProperty("companies")]
        public List<string> Companies { get; set; }

        [JsonProperty("game_modes")]
        public List<string> Gamemodes { get; set; }

        [JsonProperty("age_ratings")]
        public List<string> Age_Ratings { get; set; }

        [JsonProperty("first_release_date")]
        public long? Release_Date { get; set; }

        [JsonProperty("average_rating")]
        public float? Average_Rating { get; set; }

    }

    public class RomMFile
    {
        [JsonProperty("file_name")]
        public string FileName { get; set; }

        [JsonProperty("file_size_bytes")]
        public long? FileSize { get; set; }

        [JsonProperty("full_path")]
        public string FullPath { get; set; }
    }

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

        [JsonProperty("fs_name")]
        public string FileName { get; set; }

        [JsonProperty("fs_name_no_tags")]
        public string FileNameNoTags { get; set; }

        [JsonProperty("fs_name_no_ext")]
        public string FileNameNoExt { get; set; }

        [JsonProperty("fs_extension")]
        public string FileExtension { get; set; }

        [JsonProperty("fs_path")]
        public string FilePath { get; set; }

        [JsonProperty("fs_size_bytes")]
        public ulong FileSizeBytes { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonProperty("summary")]
        public string Summary { get; set; }

        [JsonProperty("first_release_date")]
        public long? FirstReleaseDate { get; set; }

        [JsonProperty("metadatum")]
        public metadatum Metadatum { get; set; }

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

        [JsonProperty("path_cover_small")]
        public string PathCoverS { get; set; }

        [JsonProperty("path_cover_large")]
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

        [JsonProperty("has_simple_single_file")]
        public bool HasSimpleSingleFile { get; set; }

        [JsonProperty("has_nested_single_file")]
        public bool HasNestedSingleFile { get; set; }
        
        [JsonProperty("has_multiple_files")]
        public bool HasMultipleFiles { get; set; }

        [JsonProperty("files")]
        public List<RomMFile> Files { get; set; }

        [JsonProperty("full_path")]
        public string FullPath { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonProperty("rom_user")]
        public RomMRomUser RomUser { get; set; }

        [JsonProperty("sort_comparator")]
        public string SortComparator { get; set; }
    }
}
