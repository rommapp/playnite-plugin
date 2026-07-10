using Newtonsoft.Json;

namespace RomM.Models.RomM
{
    public class RomMPairDevice
    {
        [JsonProperty("device_code")]
        public string DeviceCode { get; set; }

        [JsonProperty("user_code")]
        public string UserCode { get; set; }

        [JsonProperty("verification_path")]
        public string VerificationPath { get; set; }

        [JsonProperty("verification_path_complete")]
        public string VerificationPathComplete { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; } = 600;

        [JsonProperty("interval")]
        public int Interval { get; set; } = 5;
    }

    public class RomMPairDeviceResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("device_id")]
        public string DeviceID { get; set; }

    }
}
