using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;

namespace RomM.Downloads
{
    public class DownloadRequest
    {
        public Guid GameId { get; set; }
        public string GameName { get; set; }
        public string DownloadUrl { get; set; }
        public string InstallDir { get; set; }      // folder where to extract/install
        public string GamePath { get; set; }        // full path to the downloaded file on disk
        public bool HasMultipleFiles { get; set; }  // whether archive contains multiple top-level files
        public bool AutoExtract { get; set; } = true;

        /// Optional function used after extraction to build rom list for Playnite
        public Func<List<GameRom>> BuildRoms { get; set; }

        // Callbacks
        public Action<GameInstalledEventArgs> OnInstalled { get; set; }
        public Action OnCanceled { get; set; }
        public Action<Exception> OnFailed { get; set; }
    }
}
