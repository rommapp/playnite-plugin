using System;
using Newtonsoft.Json;

namespace RomM.Models.RomM.Save
{
    /// <summary>
    /// Payload for POST /api/devices. The server stores an arbitrary client string and a sync mode
    /// (see KNOWN_DEVICES / SyncMode in romm). The Playnite plugin registers itself as the "playnite"
    /// client using API sync mode. allow_existing lets us re-register idempotently per RomM account.
    /// </summary>
    public class RomMDeviceCreate
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("platform")]
        public string Platform { get; set; }

        [JsonProperty("client")]
        public string Client { get; set; }

        [JsonProperty("client_version")]
        public string ClientVersion { get; set; }

        // SyncMode is a StrEnum on the server: "api" | "file_transfer" | "push_pull".
        [JsonProperty("sync_mode")]
        public string SyncMode { get; set; } = "api";

        [JsonProperty("allow_existing")]
        public bool AllowExisting { get; set; } = true;
    }

    /// <summary>Response from POST /api/devices. We only persist <see cref="DeviceId"/>.</summary>
    public class RomMDeviceCreateResponse
    {
        [JsonProperty("device_id")]
        public string DeviceId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}
