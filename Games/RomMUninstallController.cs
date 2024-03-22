using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System.IO;
using System.Linq;
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
            var gameImagePathResolved = Game.Roms.First().Path.Replace(ExpandableVariables.PlayniteDirectory, _romM.Playnite.Paths.ApplicationPath);
            if (new FileInfo(gameImagePathResolved).Exists)
            {
                File.Delete(gameImagePathResolved);
            }
            else
            {
                _romM.Playnite.Dialogs.ShowMessage($"\"{Game.Name}\" does not appear to be installed. Marking as uninstalled.", "Game not installed", MessageBoxButton.OK);
            }
            Game.Roms.Clear();
            InvokeOnUninstalled(new GameUninstalledEventArgs());
        }
    }
}
