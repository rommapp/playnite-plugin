using Playnite.SDK;
using Playnite.SDK.Models;
using RomM.Models.RomM.Rom;
using System;

namespace RomM
{
	internal interface IRomM
	{
        ILogger Logger { get; }
		IPlayniteAPI Playnite { get; }
        Guid Id { get; }

        Settings.SettingsViewModel Settings { get; }
        MetadataProperty Source { get; }
        Downloads.DownloadQueueController DownloadQueueController { get; }
        string GetPluginUserDataPath();
        RomMRom FetchRom(string romId);

        /// <summary>
        /// The emulator mapping a game was imported under, from its ROM sidecar, or null when the
        /// sidecar or the mapping is gone. Unlike the game's play action this follows the mapping
        /// as the user edits it, because every import rewrites the sidecar.
        /// </summary>
        Settings.EmulatorMapping MappingFor(Game game);

    }
}