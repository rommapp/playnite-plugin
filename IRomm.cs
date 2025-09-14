using Playnite.SDK;
using RomM.Controllers;
using RomM.ViewModels;

namespace RomM
{
	internal interface IRomM
	{
		ILogger Logger { get; }
		IPlayniteAPI Playnite { get; }
		SettingsViewModel Settings { get; }
		DownloadQueueController DownloadQueueController { get; }
		string GetPluginUserDataPath();
	}
}
