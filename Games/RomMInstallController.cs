using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace RomM.Games
{
    internal class RomMInstallController : InstallController
    {
        protected readonly IRomM _romM;
        protected CancellationTokenSource _watcherToken;
        public ILogger Logger => LogManager.GetLogger();

        internal RomMInstallController(Game game, IRomM romM) : base(game)
        {
            Name = "Install";
            _romM = romM;
        }

        public override void Dispose()
        {
            _watcherToken?.Cancel();
            base.Dispose();
        }

        public override void Install(InstallActionArgs args)
        {
            var info = Game.GetRomMGameInfo();

            var dstPath = info.Mapping?.DestinationPathResolved ??
                throw new Exception("Mapped emulator data cannot be found, try removing and re-adding.");

            _watcherToken = new CancellationTokenSource();

            Task.Run(async () =>
            {
                try
                {
                    var destination = new DirectoryInfo(dstPath);

                    // Fetch file from content endpoint
                    HttpResponseMessage response = await RomM.GetAsync(info.DownloadUrl);
                    var body = await response.Content.ReadAsByteArrayAsync();

                    var installDir = dstPath;
                    var gamePath = Path.Combine(dstPath, info.FileName);

                    if (_romM.Playnite.ApplicationInfo.IsPortable)
                    {
                        installDir = installDir.Replace(_romM.Playnite.Paths.ApplicationPath, ExpandableVariables.PlayniteDirectory);
                        gamePath = gamePath.Replace(_romM.Playnite.Paths.ApplicationPath, ExpandableVariables.PlayniteDirectory);
                    }

                    // Write the file to disk
                    File.WriteAllBytes(gamePath, body);

                    // Extract compressed files
                    if (IsFileCompressed(gamePath))
                    {
                        ExtractArchive(gamePath, installDir);

                        // Delete the compressed file
                        File.Delete(gamePath);

                        // Update the game path to the extracted game
                        gamePath = Path.Combine(dstPath, Path.GetFileNameWithoutExtension(info.FileName));

                        // If the gamePath is a directory, we need to find the actual game file
                        if (Directory.Exists(gamePath))
                        {
                            gamePath = Path.Combine(gamePath, Directory.GetFiles(gamePath, "*", SearchOption.AllDirectories)[0]);
                        }

                        // Update the game's installation status
                        var game = _romM.Playnite.Database.Games[Game.Id];
                        game.IsInstalled = true;
                        _romM.Playnite.Database.Games.Update(game);
                    }

                    InvokeOnInstalled(new GameInstalledEventArgs(new GameInstallationData()
                    {
                        InstallDirectory = installDir,
                        Roms = new List<GameRom>() { new GameRom(Game.Name, gamePath) },
                    }));
                }
                catch (Exception ex)
                {
                    _romM.Playnite.Notifications.Add(Game.GameId, $"Failed to download {Game.Name}.{Environment.NewLine}{Environment.NewLine}{ex}", NotificationType.Error);
                    Game.IsInstalling = false;
                    throw;
                }
            });
        }

        private static bool IsFileCompressed(string filePath)
        {
            try
            {
                // Try to open the file as an archive
                using (var archive = ArchiveFactory.Open(filePath))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private void ExtractArchive(string gamePath, string installDir)
        {
            using (var archive = ArchiveFactory.Open(gamePath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (!entry.IsDirectory)
                    {
                        entry.WriteToDirectory(installDir, new ExtractionOptions
                        {
                            ExtractFullPath = true,
                            Overwrite = true
                        });
                    }
                }
            }
        }
    }
}
