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
            if(_mapping.InstallFlat)
            {
                foreach (var RomFile in Game.Roms)
                {
                    if(File.Exists(RomFile.Path))
                        File.Delete(RomFile.Path);
                }
            }
            else
            {
                if (new DirectoryInfo(Game.InstallDirectory).Exists)
                {
                    Directory.Delete(Game.InstallDirectory, true);
                }
                else
                {
                    _romM.Playnite.Dialogs.ShowMessage($"\"{Game.Name}\" folder could not be found. Marking as uninstalled.", "Game not found", MessageBoxButton.OK);
                }
            }

                Game.Roms.Clear();
            InvokeOnUninstalled(new GameUninstalledEventArgs());
        }
    }
}
