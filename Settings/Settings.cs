using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using Playnite.SDK.Plugins;
using RomM.Models.RomM;
using RomM.Models.RomM.Platform;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Windows.Data;
using System.Windows.Media.Imaging;

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

        #region Backing Variables
        [JsonIgnore]
        private string _romMHost = "";
        [JsonIgnore]
        private string _romMServerVersion = "---";
        [JsonIgnore]
        private string _romMClientToken = "";
        [JsonIgnore]
        private bool _useBasicAuth = true;
        [JsonIgnore]
        private string _romMUsername = "";
        [JsonIgnore]
        private string _romMPassword = "";

        [JsonIgnore]
        private string _defaultprofilepath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"profile.png");
        [JsonIgnore]
        private string _profilepath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"profile.png");
        [JsonIgnore]
        private string _romMUser = "----";
        [JsonIgnore]
        private string _profileType = "----";
        [JsonIgnore]
        private bool _connectionFailed = false;
        [JsonIgnore]
        private bool _platformSynced = false;
        [JsonIgnore]
        private bool _platformSyncFailed = false;

        [JsonIgnore]
        private string _excludeGenres = "";
        [JsonIgnore]
        private string _7zPath = "";

        [JsonIgnore]
        private List<RomMPlatform> _romMPlatforms = new List<RomMPlatform>();
        #endregion

        [JsonIgnore]
        public bool ConnectionFailed
        {
            get => _connectionFailed;
            set
            {
                _connectionFailed = value;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public bool PlatformSynced
        {
            get => _platformSynced;
            set
            {
                _platformSynced = value;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public bool PlatformSyncFailed
        {
            get => _platformSyncFailed;
            set
            {
                _platformSyncFailed = value;
                OnPropertyChanged();
            }
        }

        public string RomMHost
        {
            get => _romMHost;
            set
            {
                if(value.Length == 0)
                {
                    _romMHost = "";
                }
                else
                {
                    _romMHost = value.Last() == '/' ? value.Substring(0, value.Length - 1) : value;
                }
                OnPropertyChanged();
            }
        }
        public string RomMClientToken
        {
            get => _romMClientToken;
            set
            {
                _romMClientToken = value;
                OnPropertyChanged();
            }
        }
        public bool UseBasicAuth
        {
            get => _useBasicAuth;
            set
            {
                _useBasicAuth = value;
                OnPropertyChanged();
            }
        }
        public string RomMUsername
        {
            get => _romMUsername;
            set
            {
                _romMUsername = value;
                OnPropertyChanged();
            }
        }
        public string RomMPassword
        {
            get => _romMPassword;
            set
            {
                _romMPassword = value;
                OnPropertyChanged();
            }
        }
        public string RomMUser
        {
            get => _romMUser;
            set
            {
                _romMUser = value;
                OnPropertyChanged();
            }
        }
        public string ClientTokenURL
        {
            get => $"{RomMHost}/client-api-tokens";
            set { }
        }

        public string ServerVersion
        {
            get => _romMServerVersion;
            set
            {
                _romMServerVersion = value;
                OnPropertyChanged();
            }
        }
        public string ProfilePath 
        { 
            get => _profilepath; 
            set
            {
                _profilepath = value;
                OnPropertyChanged();
            }
        }
        public string RomMProfileType
        {
            get => _profileType;
            set
            {
                _profileType = value;
                OnPropertyChanged();
            }
        }
 
        public bool ScanGamesInFullScreen { get; set; } = false;
        public bool NotifyOnInstallComplete { get; set; } = false;
        public bool KeepRomMSynced { get; set; } = false;
        public bool Use7z { get; set; } = false;
        public string PathTo7z
        {
            get => _7zPath;
            set
            {
                _7zPath = value;
                OnPropertyChanged();
            }
        } 
        public bool MergeRevisions { get; set; } = false;
        public bool KeepDeletedGames { get; set; } = false;
        public string ExcludeGenres
        {
            get => _excludeGenres;
            set
            {
                _excludeGenres = value;
                OnPropertyChanged();
            }
        }
        public bool SkipMissingFiles { get; set; } = false;

        public ObservableCollection<EmulatorMapping> Mappings { get; set; }

        public List<RomMPlatform> RomMPlatforms
        {
            get => _romMPlatforms;
            set
            {
                if(value != null)
                {
                    _romMPlatforms = value;
                    foreach (var mapping in Mappings)
                    {
                        mapping.AvailablePlatforms = value;
                        if(mapping.RomMPlatformId != -1)
                        {
                            mapping.RomMPlatform = value.Find(x => x.Id == mapping.RomMPlatformId);
                        }
                    }
                    OnPropertyChanged();
                }
            }
        }

        public SettingsViewModel(){}

        internal SettingsViewModel(Plugin plugin, IRomM romM)
        {
            RomM = romM;
            PlayniteAPI = plugin.PlayniteApi;
            Instance = this;
            _plugin = plugin;

            bool forceSave = false;
            var savedSettings = plugin.LoadPluginSettings<SettingsViewModel>();

            if (savedSettings == null) 
            {
                forceSave = true;
            } 
            else 
            {  
                RomMHost = savedSettings.RomMHost;
                RomMClientToken = savedSettings.RomMClientToken;
                RomMUsername = savedSettings.RomMUsername;
                RomMPassword = savedSettings.RomMPassword;
                UseBasicAuth = savedSettings.UseBasicAuth;

                Mappings = savedSettings.Mappings;
                RomMPlatforms = savedSettings.RomMPlatforms;

                KeepRomMSynced = savedSettings.KeepRomMSynced;
                ScanGamesInFullScreen = savedSettings.ScanGamesInFullScreen;
                NotifyOnInstallComplete = savedSettings.NotifyOnInstallComplete;
                Use7z = savedSettings.Use7z;
                PathTo7z = savedSettings.PathTo7z;
                MergeRevisions = savedSettings.MergeRevisions;
                KeepDeletedGames = savedSettings.KeepDeletedGames;
                ExcludeGenres = savedSettings.ExcludeGenres;     
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

        public bool TestConnection()
        {
            try
            {
                if(string.IsNullOrEmpty(RomMHost))
                {
                    throw new ArgumentException("host not set!");
                }

                if(UseBasicAuth)
                {
                    if(string.IsNullOrEmpty(RomMUsername) || string.IsNullOrEmpty(RomMPassword))
                    {
                        throw new ArgumentException("username or password not set!");
                    }

                    HttpClientSingleton.ConfigureBasicAuth(RomMUsername, RomMPassword);
                }
                else
                {
                    if (string.IsNullOrEmpty(RomMClientToken))
                    {
                        throw new ArgumentException("client token not set!");
                    }

                    HttpClientSingleton.Instance.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", RomMClientToken);
                }

                // Check server is present
                HttpResponseMessage response = HttpClientSingleton.Instance.GetAsync($"{RomMHost}/api/heartbeat", HttpCompletionOption.ResponseContentRead, new System.Threading.CancellationToken()).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                Stream body = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();

                using (StreamReader reader = new StreamReader(body))
                {
                    var jsonResponse = JObject.Parse(reader.ReadToEnd());
                    ServerInfo info = jsonResponse["SYSTEM"].ToObject<ServerInfo>();

                    ServerVersion = info.Version;
                }

                // Get user info
                response = HttpClientSingleton.Instance.GetAsync($"{RomMHost}/api/users/me", System.Net.Http.HttpCompletionOption.ResponseContentRead, new System.Threading.CancellationToken()).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                body = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                RomMUser userinfo;

                using (StreamReader reader = new StreamReader(body))
                {
                    var jsonResponse = JObject.Parse(reader.ReadToEnd());
                    userinfo = jsonResponse.ToObject<RomMUser>();
                }

                if (!string.IsNullOrEmpty(userinfo.IconPath))
                {
                    response = HttpClientSingleton.Instance.GetAsync($"{RomMHost}/api/raw/assets/{userinfo.IconPath}", System.Net.Http.HttpCompletionOption.ResponseContentRead, new System.Threading.CancellationToken()).GetAwaiter().GetResult();
                    response.EnsureSuccessStatusCode();
                    var imagebytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    File.WriteAllBytes($"{PlayniteAPI.Paths.ExtensionsDataPath}\\{RomM.Id.ToString()}\\avatar.png", imagebytes);
                    ProfilePath = $"{PlayniteAPI.Paths.ExtensionsDataPath}\\{RomM.Id.ToString()}\\avatar.png";
                }
                else
                {
                    ProfilePath = _defaultprofilepath;
                }

                RomMProfileType = userinfo.Role;
                RomMUser = userinfo.Username;
                ConnectionFailed = false;
            }
            catch (Exception ex)
            {
                ConnectionFailed = true;
                ProfilePath = _defaultprofilepath;
                RomMUser = "----";
                RomMProfileType = "----";
                ServerVersion = "---";
                LogManager.GetLogger().Error($"Failed to read response! {ex}");
                PlayniteAPI.Notifications.Add(new NotificationMessage(RomM.Id.ToString(), $"RomM - Failed to poll server: {ex.Message}", NotificationType.Error));
                return false;
            }

            return true;
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

	// Used to load profile image into cache so it can be changed while the application is running
    public class ImageCacheConverter : IValueConverter
    {
        public object Convert(object value, Type targetType,
            object parameter, System.Globalization.CultureInfo culture)
        {

            var path = (string)value;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();

            return image;

        }

        public object ConvertBack(object value, Type targetType,
            object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException("Not implemented.");
        }
    }
}
