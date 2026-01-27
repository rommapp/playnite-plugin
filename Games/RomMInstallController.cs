using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using RomM.Downloads;            // IMPORTANT
using SharpCompress.Archives;     // doar dacă mai ai IsFileCompressed aici; altfel scoate
using SharpCompress.Common;

namespace RomM.Games
{
    internal class RomMInstallController : InstallController
    {
        protected readonly IRomM _romM;
        public ILogger Logger => LogManager.GetLogger();

        internal RomMInstallController(Game game, IRomM romM) : base(game)
        {
            Name = "Download";
            _romM = romM;
        }

        public override void Install(InstallActionArgs args)
        {
            var info = Game.GetRomMGameInfo();

            var dstPath = info.Mapping?.DestinationPathResolved
                ?? throw new Exception("Mapped emulator data cannot be found, try removing and re-adding.");

            // Paths (same as before)
            var installDir = Path.Combine(dstPath, Path.GetFileNameWithoutExtension(info.FileName));
            var downloadFilePath = info.HasMultipleFiles
                ? Path.Combine(installDir, info.FileName + ".zip")
                : Path.Combine(installDir, info.FileName);

            // Create request for queue
            var req = new DownloadRequest
            {
                GameId = Game.Id,
                GameName = Game.Name,

                DownloadUrl = info.DownloadUrl,
                InstallDir = installDir,
                GamePath = downloadFilePath,

                HasMultipleFiles = info.HasMultipleFiles,
                AutoExtract = info.Mapping != null && info.Mapping.AutoExtract,

                // Called by queue AFTER download/extract is done
                BuildRoms = () =>
                {
                    var roms = new List<GameRom>();

                    // If the downloaded file still exists and wasn't extracted -> single file ROM
                    if (File.Exists(downloadFilePath) && !(info.HasMultipleFiles || (info.Mapping.AutoExtract && IsFileCompressed(downloadFilePath))))
                    {
                        roms.Add(new GameRom(Game.Name, downloadFilePath));
                        return roms;
                    }

                    // Otherwise, we assume extracted files in installDir
                    var supported = GetEmulatorSupportedFileTypes(info);
                    var files = GetRomFiles(installDir, supported);

                    foreach (var f in files)
                    {
                        roms.Add(new GameRom(Game.Name, f));
                    }

                    return roms;
                },

                // Callbacks into Playnite install pipeline
                OnInstalled = installedArgs =>
                {
                    var game = _romM.Playnite.Database.Games[Game.Id];
                    game.IsInstalled = true;
                    _romM.Playnite.Database.Games.Update(game);

                    InvokeOnInstalled(installedArgs);
                },

                OnCanceled = () =>
                {
                    var game = _romM.Playnite.Database.Games[Game.Id];
                    game.IsInstalling = false;
                    game.IsInstalled = false;
                    _romM.Playnite.Database.Games.Update(game);

                    InvokeOnInstallationCancelled(new GameInstallationCancelledEventArgs());
                },

                OnFailed = ex =>
                {
                    _romM.Playnite.Notifications.Add(
                        Game.GameId,
                        $"Failed to download {Game.Name}.{Environment.NewLine}{Environment.NewLine}{ex}",
                        NotificationType.Error);

                    Game.IsInstalling = false;
                }
            };

            // Enqueue (non-blocking)
            _romM.DownloadQueueController.Enqueue(req);
        }

        private static string[] GetRomFiles(string installDir, List<string> supportedFileTypes)
        {
            if (installDir == null || installDir.Contains("../") || installDir.Contains(@"..\"))
            {
                throw new ArgumentException("Invalid file path");
            }

            if (supportedFileTypes == null || supportedFileTypes.Count == 0)
            {
                return Directory.GetFiles(installDir, "*", SearchOption.AllDirectories)
                    .Where(file => !file.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            return supportedFileTypes.SelectMany(fileType =>
            {
                if (fileType == null || fileType.Contains("../") || fileType.Contains(@"..\"))
                {
                    throw new ArgumentException("Invalid file path");
                }

                return Directory.GetFiles(installDir, "*." + fileType, SearchOption.AllDirectories)
                    .Where(file => !file.Contains("../") && !file.Contains(@"..\"));
            }).ToArray();
        }

        private static List<string> GetEmulatorSupportedFileTypes(RomMGameInfo info)
        {
            if (info.Mapping.EmulatorProfile is CustomEmulatorProfile)
            {
                var customProfile = info.Mapping.EmulatorProfile as CustomEmulatorProfile;
                return customProfile.ImageExtensions;
            }
            else if (info.Mapping.EmulatorProfile is BuiltInEmulatorProfile)
            {
                var builtInProfile = (info.Mapping.EmulatorProfile as BuiltInEmulatorProfile);
                return API.Instance.Emulation.Emulators
                    .FirstOrDefault(e => e.Id == info.Mapping.Emulator.BuiltInConfigId)?
                    .Profiles
                    .FirstOrDefault(p => p.Name == builtInProfile.Name)?
                    .ImageExtensions;
            }

            return null;
        }

        private static bool IsFileCompressed(string filePath)
        {
            // Exclude disk images which aren't handled by sharpcompress
            if (Path.GetExtension(filePath).Equals(".iso", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return ArchiveFactory.IsArchive(filePath, out var type);
        }
    }
}
