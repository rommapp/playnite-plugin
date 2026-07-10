using Newtonsoft.Json;
using System;

namespace RomM.Models.RomM
{
    public class RomMRegisterDevice
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("platform")]
        public string Platform { get; set; }

        [JsonProperty("client")]
        public string Client { get; set; }

        [JsonProperty("client_version")]
        public string ClientVersion { get; set; }

        [JsonProperty("mac_address")]
        public string MACAddress { get; set; }

        [JsonProperty("hostname")]
        public string HostName { get; set; }

    }

    public class RomMRegisterDeviceResponse
    {
        [JsonProperty("device_id")]
        public string DeviceID { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }
    }

    public class RomMDevice
    {
        [JsonProperty("id")]
        public string ID { get; set; }

        [JsonProperty("user_id")]
        public int UserID { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("platform")]
        public string Platform { get; set; }

        [JsonProperty("client")]
        public string Client { get; set; }

        [JsonProperty("client_version")]
        public string ClientVersion { get; set; }

        [JsonProperty("ip_address")]
        public string IPAddress { get; set; }

        [JsonProperty("mac_address")]
        public string MACAddress { get; set; }

        [JsonProperty("hostname")]
        public string Hostname { get; set; }

        [JsonProperty("sync_mode")]
        public string SyncMode { get; set; }

        [JsonProperty("sync_enabled")]
        public bool SyncEnabled { get; set; }

        [JsonProperty("last_seen")]
        public DateTime LastSeen { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}
