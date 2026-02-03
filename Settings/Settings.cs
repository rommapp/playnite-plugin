using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace RomM.Settings
{
    public class SettingsViewModel : ObservableObject, ISettings
    {
        private readonly Plugin _plugin;

        private SettingsViewModel editingClone { get; set; }

        [JsonIgnore]
        internal readonly IPlayniteAPI PlayniteAPI;

        [JsonIgnore]
        internal readonly IRomM RomM;

        public static SettingsViewModel Instance { get; private set; }

        public bool ScanGamesInFullScreen { get; set; } = false;
        public bool NotifyOnInstallComplete { get; set; } = false;
        public bool KeepRomMSynced { get; set; } = false;
        public string RomMHost { get; set; } = "";
        public string RomMUsername { get; set; } = "";
        public string RomMPassword { get; set; } = "";
        public ObservableCollection<EmulatorMapping> Mappings { get; set; }

        public bool Use7z { get; set; } = false;
        public string PathTo7z { get; set; } = "";

        public SettingsViewModel()
        {
        }

        internal SettingsViewModel(Plugin plugin, IRomM romM)
        {
            RomM = romM;
            PlayniteAPI = plugin.PlayniteApi;
            Instance = this;
            _plugin = plugin;

            bool forceSave = false;
            var savedSettings = plugin.LoadPluginSettings<SettingsViewModel>();

            if (savedSettings == null) {
                forceSave = true;
            } else {
                ScanGamesInFullScreen = savedSettings.ScanGamesInFullScreen;
                NotifyOnInstallComplete = savedSettings.NotifyOnInstallComplete;
                RomMHost = savedSettings.RomMHost;
                RomMUsername = savedSettings.RomMUsername;
                RomMPassword = savedSettings.RomMPassword;
                Mappings = savedSettings.Mappings;
                KeepRomMSynced = savedSettings.KeepRomMSynced;
                Use7z = savedSettings.Use7z;
                PathTo7z = savedSettings.PathTo7z;
            }
            
            if (Mappings == null)
            {
                Mappings = new ObservableCollection<EmulatorMapping>();
            }

            var mappingsWithoutId = Mappings.Where(m => m.MappingId == default);
            if (mappingsWithoutId.Any())
            {
                mappingsWithoutId.ForEach(m => m.MappingId = Guid.NewGuid());
                forceSave = true;
            }

            if (forceSave)
            {
                SavePluginSettings(this);
            }
        }

        public void BeginEdit()
        {
            // Code executed when settings view is opened and user starts editing values.
            editingClone = JsonConvert.DeserializeObject<SettingsViewModel>(JsonConvert.SerializeObject(Instance));
        }

        public void CancelEdit()
        {
            // Code executed when user decides to cancel any changes made since BeginEdit was called.
            // This method should revert any changes made to Option1 and Option2.
            SavePluginSettings(editingClone);
        }

        public void EndEdit()
        {
            // Code executed when user decides to confirm changes made since BeginEdit was called.
            // This method should save settings made to Option1 and Option2.
            SavePluginSettings(this);
            HttpClientSingleton.ConfigureBasicAuth(this.RomMUsername, this.RomMPassword);
        }

        private void SavePluginSettings<SettingsViewModel>(SettingsViewModel settings)
        {
            var setDir = _plugin.GetPluginUserDataPath();
            var setFile = Path.Combine(setDir, "config.json");
            if (!Directory.Exists(setDir))
            {
                Directory.CreateDirectory(setDir);
            }

            var strConf = JsonConvert.SerializeObject(settings);
            File.WriteAllText(setFile, strConf);
        }

        public bool VerifySettings(out List<string> errors)
        {
            var mappingErrors = new List<string>();

            Mappings.Where(m => m.Enabled)?.ForEach(m =>
            {
                if (string.IsNullOrEmpty(m.DestinationPathResolved))
                {
                    mappingErrors.Add($"{m.MappingId}: No destination path specified.");
                }
                else if (!Directory.Exists(m.DestinationPathResolved))
                {
                    mappingErrors.Add($"{m.MappingId}: Destination path doesn't exist ({m.DestinationPathResolved}).");
                }
            });

            errors = mappingErrors;
            return errors.Count == 0;
        }
    }
}
