using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using RomM.Settings;
using System.IO;
using System.Windows;

namespace RomM.Games
{
    internal class RomMUninstallController : UninstallController
    {
        private readonly IRomM _romM;
        private EmulatorMapping _mapping;

        internal RomMUninstallController(Game game, IRomM romM, EmulatorMapping mapping) : base(game)
        {
            Name = "Uninstall";
            _romM = romM;
            _mapping = mapping;
        }

        public override void Uninstall(UninstallActionArgs args)
        {
            // A portable Playnite records these with the "{PlayniteDir}" token, which no filesystem
            // call resolves.
            var installDir = PlaynitePath.Resolve(_romM.Playnite, Game.InstallDirectory);

            // Whether the game owns its install directory, read from how it was actually installed
            // rather than from what the mapping says now -- the setting can be toggled after a game is
            // installed, and a ROM fetched as a whole folder keeps a folder of its own even when the
            // mapping asks for flat. Anything else shares its directory with other games, or no longer
            // sits under the mapping, and only the files registered as its ROMs may be removed.
            if (RomMInstallPaths.IsInside(_mapping.DestinationPathResolved, installDir))
            {
                if (new DirectoryInfo(installDir).Exists)
                {
                    Directory.Delete(installDir, true);
                }
                else
                {
                    _romM.Playnite.Dialogs.ShowMessage($"\"{Game.Name}\" folder could not be found. Marking as uninstalled.", "Game not found", MessageBoxButton.OK);
                }
            }
            else
            {
                foreach (var RomFile in Game.Roms)
                {
                    var romPath = PlaynitePath.Resolve(_romM.Playnite, RomFile.Path);
                    if(File.Exists(romPath))
                        File.Delete(romPath);
                }
            }

            Game.Roms.Clear();
            InvokeOnUninstalled(new GameUninstalledEventArgs());
        }
    }
}
