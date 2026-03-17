using Newtonsoft.Json;
using Playnite.SDK.Models;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;
using RomM.Models.RomM.Platform;
using SharpCompress;

namespace RomM.Settings
{
    public class EmulatorMapping : ObservableObject
    {
        [JsonIgnore]
        private Guid _mappingId;
        [JsonIgnore]
        private string _mappingName = "";
        [JsonIgnore]
        private bool _enabled = true;
        [JsonIgnore]
        private bool _autoExtract = false;
        [JsonIgnore]
        private bool _useM3U = false;
        [JsonIgnore]
        private Emulator _emulator;
        [JsonIgnore]
        private Guid _emulatorId;
        [JsonIgnore]
        private EmulatorProfile _emulatorProfile;
        [JsonIgnore]
        private IEnumerable<EmulatorProfile> _availableProfiles;
        [JsonIgnore]
        public string _emulatorProfileId;
        [JsonIgnore]
        private RomMPlatform _emulatedPlatform = new RomMPlatform();
        [JsonIgnore]
        private IEnumerable<RomMPlatform> _availablePlatforms;
        [JsonIgnore]
        public int _romMPlatformId = -1;

        public EmulatorMapping(List<RomMPlatform> romMPlatforms)
        {
            MappingId = Guid.NewGuid();
            AvailablePlatforms = romMPlatforms;
        }

        public Guid MappingId 
        {
            get => _mappingId; 
            set
            {
                _mappingId = value;
                OnPropertyChanged();    
            }
        }

        [DefaultValue(true)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool Enabled 
        {
            get => _enabled;
            set
            {
                _enabled = value;
                OnPropertyChanged();
            }
        }

        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool AutoExtract
        {
            get => _autoExtract;
            set
            {
                _autoExtract = value;
                OnPropertyChanged();
            }
        }

        [DefaultValue(false)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public bool UseM3U
        {
            get => _useM3U;
            set
            {
                _useM3U = value;
                OnPropertyChanged();
            }
        }

        [JsonIgnore]
        public Emulator Emulator
        {
            get => _emulator;
            set 
            {
                _emulator = value;
                _emulatorId = value.Id;
                AvailableProfiles = Emulator?.SelectableProfiles;
                RomMPlatform = new RomMPlatform();
                MappingName = value.Name;
                OnPropertyChanged();
            }
        }
        public Guid EmulatorId
        {
            get => _emulatorId;
            set
            {
                _emulatorId = value;
                Emulator = SettingsViewModel.Instance.PlayniteAPI.Database.Emulators.FirstOrDefault(x => x.Id == _emulatorId);
                OnPropertyChanged();
            }
        }

        [JsonIgnore]
        public EmulatorProfile EmulatorProfile
        {
            get => _emulatorProfile;
            set 
            {
                _emulatorProfile = value;
                _emulatorProfileId = value.Id;
                MappingName += (" - " + value.Name); 
                OnPropertyChanged(); 
            }
        }
        public string EmulatorProfileId
        {
            get => _emulatorProfileId;
            set
            {
                _emulatorProfileId = value;
                EmulatorProfile = Emulator?.SelectableProfiles.FirstOrDefault(x => x.Id == _emulatorProfileId);
                OnPropertyChanged();
            }
        }

        [JsonIgnore]
        public Platform Platform
        {
            get => null;
            set 
            {
            }
        }
        [JsonIgnore]
        public string PlatformId
        {
            get => "";
            set
            {
            }
        }

        [JsonIgnore]
        public RomMPlatform RomMPlatform
        {
            get => _emulatedPlatform;
            set
            {
                _emulatedPlatform = value;
                _romMPlatformId = -1;
                if(value != null)
                {
                    _romMPlatformId = value.Id;
                    MappingName += (" - " + value.Name);
                }
                OnPropertyChanged();
            }
        }
        public int RomMPlatformId
        {
            get => _romMPlatformId;
            set
            {
                _romMPlatformId = value;
                OnPropertyChanged();
            }
        }

        [JsonIgnore]
        public string MappingName
        {
            get => _mappingName;
            set
            {
                _mappingName = value;
                OnPropertyChanged();
            }
        }

        public string DestinationPath { get; set; }

        [JsonIgnore]
        public static IEnumerable<Emulator> AvailableEmulators => SettingsViewModel.Instance.PlayniteAPI.Database.Emulators?.OrderBy(x => x.Name) ?? Enumerable.Empty<Emulator>();
        [JsonIgnore]
        public IEnumerable<EmulatorProfile> AvailableProfiles
        {
            get => _availableProfiles;
            set
            {
                _availableProfiles = value;
                OnPropertyChanged();
            }
        }    
        [JsonIgnore]
        public IEnumerable<RomMPlatform> AvailablePlatforms
        {
            get => _availablePlatforms;
            set
            {
                _availablePlatforms = value;
                OnPropertyChanged();
            }
        }

        [JsonIgnore]
        [XmlIgnore]
        public string DestinationPathResolved
        {
            get
            {
                var playnite = SettingsViewModel.Instance.PlayniteAPI;
                return playnite.Paths.IsPortable ? DestinationPath?.Replace(ExpandableVariables.PlayniteDirectory, playnite.Paths.ApplicationPath) : DestinationPath;
            }
        }

        [JsonIgnore]
        [XmlIgnore]
        public string EmulatorBasePath => Emulator?.InstallDir;

        [JsonIgnore]
        [XmlIgnore]
        public string EmulatorBasePathResolved
        {
            get
            {
                var playnite = SettingsViewModel.Instance.PlayniteAPI;
                var ret = Emulator?.InstallDir;
                if (playnite.Paths.IsPortable)
                {
                    ret = ret?.Replace(ExpandableVariables.PlayniteDirectory, playnite.Paths.ApplicationPath);
                }
                return ret;
            }
        }


        public IEnumerable<string> GetDescriptionLines()
        {
            yield return $"{nameof(_emulatorId)}: {_emulatorId}";
            yield return $"{nameof(Emulator)}*: {Emulator?.Name ?? "<Unknown>"}";
            yield return $"{nameof(EmulatorProfileId)}: {EmulatorProfileId ?? "<Unknown>"}";
            yield return $"{nameof(EmulatorProfile)}*: {EmulatorProfile?.Name ?? "<Unknown>"}";
            yield return $"{nameof(PlatformId)}: {PlatformId}";
            yield return $"{nameof(Platform)}*: {Platform?.Name ?? "<Unknown>"}";
            yield return $"{nameof(DestinationPath)}: {DestinationPath ?? "<Unknown>"}";
            yield return $"{nameof(DestinationPathResolved)}*: {DestinationPathResolved ?? "<Unknown>"}";
            yield return $"{nameof(EmulatorBasePathResolved)}*: {EmulatorBasePathResolved ?? "<Unknown>"}";
        }
    }
}
