using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RomM.Models.RomM
{
    struct ServerInfo
    {
        [JsonProperty("VERSION")]
        public string Version { get; set; }
        [JsonProperty("SHOW_SETUP_WIZARD")]
        public bool ShowSetupWizard { get; set; }
    }

    class RomMHeartbeat
    {
        [JsonProperty("SYSTEM")]
        public ServerInfo SystemInfo { get; set; } 
    }
}
