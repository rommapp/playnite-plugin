using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace RomM.Models.RomM.Save
{
    /// <summary>
    /// Per-device sync state attached to a save. Returned by the server (see PR #3479) so the
    /// client can tell which devices already hold a copy and which one is current.
    /// </summary>
    public class RomMDeviceSync
    {
        [JsonProperty("device_id")]
        public string DeviceId { get; set; }

        [JsonProperty("device_name")]
        public string DeviceName { get; set; }

        [JsonProperty("last_synced_at")]
        public DateTime? LastSyncedAt { get; set; }

        [JsonProperty("is_untracked")]
        public bool IsUntracked { get; set; }

        [JsonProperty("is_current")]
        public bool IsCurrent { get; set; }
    }

    /// <summary>
    /// Subset of the server's SaveSchema that the plugin needs. The server returns many more
    /// fields (file paths, screenshot, tags) which we deliberately ignore.
    /// </summary>
    public class RomMSave
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("rom_id")]
        public int RomId { get; set; }

        [JsonProperty("file_name")]
        public string FileName { get; set; }

        [JsonProperty("file_size_bytes")]
        public long FileSizeBytes { get; set; }

        [JsonProperty("download_path")]
        public string DownloadPath { get; set; }

        [JsonProperty("emulator")]
        public string Emulator { get; set; }

        [JsonProperty("slot")]
        public string Slot { get; set; }

        [JsonProperty("content_hash")]
        public string ContentHash { get; set; }

        [JsonProperty("origin_device_id")]
        public string OriginDeviceId { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonProperty("device_syncs")]
        public List<RomMDeviceSync> DeviceSyncs { get; set; } = new List<RomMDeviceSync>();
    }
}
