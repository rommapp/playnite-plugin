using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System.IO;
using System.Windows;

namespace RomM.Games
{
    internal class RomMUninstallController : UninstallController
    {
        private readonly IRomM _romM;

        internal RomMUninstallController(Game game, IRomM romM) : base(game)
        {
            Name = "Uninstall";
            _romM = romM;
        }

        public override void Uninstall(UninstallActionArgs args)
        {
            if (new DirectoryInfo(Game.InstallDirectory).Exists)
            {
                Directory.Delete(Game.InstallDirectory, true);
            }
            else
            {
                _romM.Playnite.Dialogs.ShowMessage($"\"{Game.Name}\" folder could not be found. Marking as uninstalled.", "Game not found", MessageBoxButton.OK);
            }
            Game.Roms.Clear();
            InvokeOnUninstalled(new GameUninstalledEventArgs());
        }
    }
}
