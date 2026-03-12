using Playnite.SDK;
using Playnite.SDK.Models;
using RomM.Models.RomM.Rom;

namespace RomM
{
	internal interface IRomM
	{
        ILogger Logger { get; }
		IPlayniteAPI Playnite { get; }
		Settings.SettingsViewModel Settings { get; }
        MetadataProperty Source { get; }
        Downloads.DownloadQueueController DownloadQueueController { get; }
        string GetPluginUserDataPath();
        RomMRom FetchRom(string romId);

    }
}