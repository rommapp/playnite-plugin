using Newtonsoft.Json;

namespace RomM.Models.RomM
{
    public class RomMUser
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("username")]
        public string Username { get; set; }
        [JsonProperty("email")]
        public string Email { get; set; }
        [JsonProperty("enabled")]
        public bool Enabled { get; set; }
        [JsonProperty("role")]
        public string Role { get; set; }
        [JsonProperty("avatar_path")]
        public string IconPath { get; set; }
        [JsonProperty("last_login")]
        public string LastLogin { get; set; }
        [JsonProperty("last_active")]
        public string LastActive { get; set; }
        [JsonProperty("ra_username")]
        public string RAUsername { get; set; }
    }
}
