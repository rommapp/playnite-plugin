using Playnite.SDK;
using Playnite.SDK.Models;

namespace RomM.Saves
{
    /// <summary>
    /// Knows where one emulator keeps its saves. Handlers own everything platform-specific --
    /// reading the emulator's own configuration, working out the path, deciding whether the save
    /// is a file or a directory -- so <see cref="SaveSyncService"/> can stay about negotiate,
    /// upload and download.
    ///
    /// Adding an emulator is a new handler plus one line in <see cref="SaveHandlerRegistry"/>.
    /// </summary>
    internal interface ISaveHandler
    {
        /// <summary>The tag saves are filed under on the server, e.g. "retroarch".</summary>
        string EmulatorTag { get; }

        /// <summary>Whether this handler is the one that knows the given emulator.</summary>
        bool CanHandle(Emulator emulator);

        /// <summary>
        /// Locates the game's save, or null when this handler cannot work out a path -- an
        /// unreadable configuration, a layout it does not recognise. Returning null skips the game
        /// rather than failing the sync.
        /// </summary>
        SaveTarget ResolveTarget(SaveTargetRequest request);
    }

    /// <summary>
    /// Everything a handler needs to locate a save, gathered by the service so handlers stay free
    /// of Playnite lookups and stay unit-testable.
    /// </summary>
    internal class SaveTargetRequest
    {
        public Game Game { get; set; }

        public Emulator Emulator { get; set; }

        /// <summary>
        /// The profile the game launches with, when one is set. Carries the detail that decides
        /// where some emulators file a save — for RetroArch, which core is running.
        /// </summary>
        public EmulatorProfile Profile { get; set; }

        /// <summary>The ROM's path with Playnite's variables already expanded.</summary>
        public string ContentPath { get; set; }

        public ILogger Logger { get; set; }
    }
}
