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
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RomM.Settings
{
    public class SettingsViewModel : ObservableObject, ISettings
    {
        private readonly Plugin _plugin;
        private SettingsViewModel editingClone { get; set; }
        [JsonIgnore] internal readonly IPlayniteAPI PlayniteAPI;
        [JsonIgnore] internal readonly IRomM RomM;
        public static SettingsViewModel Instance { get; private set; }

        #region Backing Variables

        [JsonIgnore] private string _romMHost = "";
        [JsonIgnore] private string _romMServerVersion = "---";
        [JsonIgnore] private string _romMClientToken = "";
        [JsonIgnore] private bool _useBasicAuth = true;
        [JsonIgnore] private string _romMUsername = "";
        [JsonIgnore] private string _romMPassword = "";

        [JsonIgnore] private string _defaultprofilepath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"profile.png");
        [JsonIgnore] private string _profilepath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"profile.png");
        [JsonIgnore] private string _romMUser = "----";
        [JsonIgnore] private string _profileType = "----";

        [JsonIgnore] private string _excludeGenres = "";
        [JsonIgnore] private string _7zPath = "";

        [JsonIgnore] private List<RomMPlatform> _romMPlatforms = new List<RomMPlatform>();

        [JsonIgnore] private bool _notify = false;
        [JsonIgnore] private string _notifyText = "";
        [JsonIgnore] private string _notifyIcon = "";
        [JsonIgnore] private Color _notfiyColour = Colors.DarkSlateGray;
        [JsonIgnore] private Brush _notfiyTextColour = new SolidColorBrush(Colors.LightGray);

        #endregion

        #region Notifcation Bar
        [JsonIgnore]
        public bool Notify
        {
            get => _notify;
            set
            {
                _notify = value;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public string NotifyText
        {
            get => _notifyText;
            set
            {
                _notifyText = value;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public string NotifyIcon
        {
            get => _notifyIcon;
            set
            {
                _notifyIcon = value;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public Color NotfiyColour
        {
            get => _notfiyColour;
            set
            {
                _notfiyColour = value;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public Brush NotfiyTextColour
        {
            get => _notfiyTextColour;
            set
            {
                _notfiyTextColour = value;
                OnPropertyChanged();
            }
        }
        public void UpdateNotifcationBar(string Message, bool IsError = false)
        {
            if (IsError)
            {
                NotfiyColour = (Color)ColorConverter.ConvertFromString("#730000");
                NotfiyTextColour = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ff6b6b"));
                NotifyIcon = $" \uE730";
                NotifyText = $"      {Message}";
                Notify = true;
            }
            else
            {
                NotfiyColour = (Color)ColorConverter.ConvertFromString("#035900");
                NotfiyTextColour = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#91ff8e"));
                NotifyIcon = $" \uE73E";
                NotifyText = $"      {Message}";
                Notify = true;
            }
        }

        #endregion

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
                    _romMHost = value.TrimEnd('/');
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
        public static readonly Regex ApiTokenPattern = new Regex(@"^rmm_[0-9a-f]{64}$", RegexOptions.Compiled);

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

        [JsonIgnore] public string ClientTokenURL
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
                    OnPropertyChanged();

                    foreach (var mapping in Mappings)
                    {
                        mapping.AvailablePlatforms = value;
                    }              
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

                RomMUser = savedSettings.RomMUser;
                RomMProfileType = savedSettings.RomMProfileType;
                ProfilePath = savedSettings.ProfilePath;
                ServerVersion = savedSettings.ServerVersion;

                // ----- These need to stay in this order -----
                Mappings = savedSettings.Mappings;
                RomMPlatforms = savedSettings.RomMPlatforms;
                // --------------------------------------------

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

        public bool TestConnection(bool UpdateNotificationBar = false)
        {
            Notify = false;

            try
            {
                if(string.IsNullOrEmpty(RomMHost))
                {
                    throw new ArgumentException("Host not set!");
                }
                if(!Uri.IsWellFormedUriString(RomMHost, UriKind.RelativeOrAbsolute))
                {
                    throw new ArgumentException("Host is not a valid URL!");
                }

                if(UseBasicAuth)
                {
                    if(string.IsNullOrEmpty(RomMUsername) || string.IsNullOrEmpty(RomMPassword))
                    {
                        throw new ArgumentException("Username/Password not set!");
                    }

                    HttpClientSingleton.ConfigureBasicAuth(RomMUsername, RomMPassword);
                }
                else
                {
                    if (string.IsNullOrEmpty(RomMClientToken))
                    {
                        throw new ArgumentException("Client token not set!");
                    }

                    if(!ApiTokenPattern.IsMatch(RomMClientToken))
                    {
                        throw new ArgumentException("Client token format invaild!");
                    }

                    HttpClientSingleton.ConfigureAPIAuth(RomMClientToken);
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
                if(UpdateNotificationBar)
                    UpdateNotifcationBar("Authenticated!");
            }
            catch (Exception ex)
            {
                Notify = true;
                ProfilePath = _defaultprofilepath;
                RomMUser = "----";
                RomMProfileType = "----";
                ServerVersion = "---";
                LogManager.GetLogger().Error($"Failed to read response! {ex}");

                if (UpdateNotificationBar)
                    UpdateNotifcationBar($"Authentication failed: {ex.Message}", true);

                PlayniteAPI.Notifications.Add(new NotificationMessage($"RomMPlugin.Authentication.Failed.{ex.Message}", $"RomM - Authentication failed: {ex.Message}", NotificationType.Error));
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
            if (UseBasicAuth)
            {
                HttpClientSingleton.ConfigureBasicAuth(RomMUsername, RomMPassword);
            }
            else
            {
                HttpClientSingleton.ConfigureAPIAuth(RomMClientToken);
            }

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
                    UpdateNotifcationBar($"{m.MappingId}: No destination path specified.", true);
                }
                else if (!Directory.Exists(m.DestinationPathResolved))
                {
                    mappingErrors.Add($"{m.MappingId}: Destination path doesn't exist ({m.DestinationPathResolved}).");
                    UpdateNotifcationBar($"{m.MappingId}: Destination path doesn't exist ({m.DestinationPathResolved}).", true);
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
