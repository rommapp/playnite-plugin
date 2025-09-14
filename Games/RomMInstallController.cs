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
using System.Linq;

namespace RomM.Games
{
    internal class RomMInstallController : InstallController
    {
        protected readonly IRomM _romM;
        protected CancellationTokenSource _watcherToken;
        public ILogger Logger => LogManager.GetLogger();

        internal RomMInstallController(Game game, IRomM romM) : base(game)
        {
            Name = "Download";
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

            // Add to download queue (we'll get total bytes later)
            _romM.DownloadQueueController.AddDownload(Game.Name, 0);

            // Start the installation process asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    // Set downloading status in queue
                    _romM.DownloadQueueController.SetDownloading(Game.Name);

                    // Fetch file from content endpoint
                    HttpResponseMessage response = await RomM.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    List<GameRom> roms = new List<GameRom>();
                    string installDir = Path.Combine(dstPath, Path.GetFileNameWithoutExtension(info.FileName));
                    string gamePath = Path.Combine(installDir, info.FileName);
                    if (info.IsMulti)
                    {
                        // File name for multi-file archives is the folder name, so we append .zip
                        gamePath = Path.Combine(installDir, info.FileName + ".zip");
                    }

                    if (_romM.Playnite.ApplicationInfo.IsPortable)
                    {
                        installDir = installDir.Replace(_romM.Playnite.Paths.ApplicationPath, ExpandableVariables.PlayniteDirectory);
                        gamePath = gamePath.Replace(_romM.Playnite.Paths.ApplicationPath, ExpandableVariables.PlayniteDirectory);
                    }

                    Logger.Debug($"Downloading {Game.Name} to {gamePath}...");
                    Directory.CreateDirectory(installDir);

                    // Download with progress reporting
                    await DownloadWithProgress(response, gamePath);

                    Logger.Debug($"Download of {Game.Name} complete.");

                    // Always extract top-level file of multi-file archives
                    if (info.IsMulti || (info.Mapping.AutoExtract && IsFileCompressed(gamePath)))
                    {
                        Logger.Debug($"Extracting {Game.Name} to {installDir}...");
                        
                        // Set extracting status in queue (handles all UI updates)
                        _romM.DownloadQueueController.SetExtracting(Game.Name);

                        // Extract the archive to the install directory
                        ExtractArchiveWithProgress(gamePath, installDir);

                        // Delete the compressed file
                        File.Delete(gamePath);

                        // Extract nested archives if auto-extract is enabled
                        if (info.IsMulti && info.Mapping.AutoExtract)
                        {
                            ExtractNestedArchivesWithProgress(installDir);
                        }

                        Logger.Debug($"Extraction of {Game.Name} complete.");

                        List<string> supportedFileTypes = GetEmulatorSupportedFileTypes(info);
                        string[] actualRomFiles = GetRomFiles(installDir, supportedFileTypes);

                        foreach (var romFile in actualRomFiles) {
                            roms.Add(new GameRom(Game.Name, romFile));
                        }
                    } else {
                        // Add the single ROM file to the list
                        roms.Add(new GameRom(Game.Name, gamePath));
                    }

                    // Update the game's installation status
                    var game = _romM.Playnite.Database.Games[Game.Id];
                    game.IsInstalled = true;
                    _romM.Playnite.Database.Games.Update(game);

                    // Mark as completed in download queue
                    _romM.DownloadQueueController.SetCompleted(Game.Name);

                    InvokeOnInstalled(new GameInstalledEventArgs(new GameInstallationData()
                    {
                        InstallDirectory = installDir,
                        Roms = roms,
                    }));
                }
                catch (Exception ex)
                {
                    _romM.Playnite.Notifications.Add(Game.GameId, $"Failed to download {Game.Name}.{Environment.NewLine}{Environment.NewLine}{ex}", NotificationType.Error);
                    Game.IsInstalling = false;
                    
                    // Mark as error in download queue
                    _romM.DownloadQueueController.SetError(Game.Name, ex.Message);
                    
                    throw;
                }
            });
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
            else
            {
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
            if (gamePath == null || gamePath.Contains("../") || gamePath.Contains(@"..\"))
            {
                throw new ArgumentException("Invalid game path");
            }

            if (installDir == null || installDir.Contains("../") || installDir.Contains(@"..\"))
            {
                throw new ArgumentException("Invalid install directory path");
            }

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

        void ExtractNestedArchives(string directoryPath)
        {
            if (directoryPath == null || directoryPath.Contains("../") || directoryPath.Contains(@"..\"))
            {
                throw new ArgumentException("Invalid file path");
            }

            foreach (var file in Directory.GetFiles(directoryPath))
            {   
                if (IsFileCompressed(file))
                {
                    ExtractArchive(file, directoryPath);
                    File.Delete(file);
                }
            }
        }

        private async Task DownloadWithProgress(HttpResponseMessage response, string filePath)
        {
            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;

            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
            using (var httpStream = await response.Content.ReadAsStreamAsync())
            {
                byte[] buffer = new byte[65536];
                int bytesRead;

                while ((bytesRead = await httpStream.ReadAsync(buffer, 0, buffer.Length, _watcherToken.Token)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, _watcherToken.Token);
                    downloadedBytes += bytesRead;

                    if (totalBytes > 0)
                    {
                        var progressValue = (int)((double)downloadedBytes / totalBytes * 100);
                        
                        // DownloadQueueController handles all detailed progress UI updates
                        _romM.DownloadQueueController.UpdateDownloadProgress(Game.Name, progressValue, downloadedBytes, totalBytes);
                    }
                }
            }
        }

        private void ExtractArchiveWithProgress(string gamePath, string installDir)
        {
            if (gamePath == null || gamePath.Contains("../") || gamePath.Contains(@"..\"))
            {
                throw new ArgumentException("Invalid game path");
            }

            if (installDir == null || installDir.Contains("../") || installDir.Contains(@"..\"))
            {
                throw new ArgumentException("Invalid install directory path");
            }

            using (var archive = ArchiveFactory.Open(gamePath))
            {
                var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
                var totalEntries = entries.Count;
                var processedEntries = 0;

                foreach (var entry in entries)
                {
                    _watcherToken.Token.ThrowIfCancellationRequested();

                    entry.WriteToDirectory(installDir, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });

                    processedEntries++;
                    var progressValue = (int)((double)processedEntries / totalEntries * 100);
                    
                    // DownloadQueueController handles all detailed progress UI updates
                    _romM.DownloadQueueController.UpdateExtractionProgress(Game.Name, progressValue);
                }
            }
        }

        private void ExtractNestedArchivesWithProgress(string directoryPath)
        {
            if (directoryPath == null || directoryPath.Contains("../") || directoryPath.Contains(@"..\"))
            {
                throw new ArgumentException("Invalid file path");
            }

            var compressedFiles = Directory.GetFiles(directoryPath).Where(IsFileCompressed).ToList();
            var totalFiles = compressedFiles.Count;
            var processedFiles = 0;

            foreach (var file in compressedFiles)
            {
                _watcherToken.Token.ThrowIfCancellationRequested();

                ExtractArchive(file, directoryPath);
                File.Delete(file);

                processedFiles++;
                var progressValue = (int)((double)processedFiles / totalFiles * 100);
                
                // DownloadQueueController handles all detailed progress UI updates
                _romM.DownloadQueueController.UpdateExtractionProgress(Game.Name, progressValue);
            }
        }
    }
}
