using System;
using Newtonsoft.Json;

namespace RomM.Models.RomM.Platform
{
    public class RomMPlatformFirmware
    {
        [JsonProperty("id")]
        public int Id { get; set; }

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

        [JsonProperty("full_path")]
        public string FullPath { get; set; }

        [JsonProperty("is_verified")]
        public bool IsVerified { get; set; }

        [JsonProperty("crc_hash")]
        public string CrcHash { get; set; }

        [JsonProperty("md5_hash")]
        public string Md5Hash { get; set; }

        [JsonProperty("sha1_hash")]
        public string Sha1Hash { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}
