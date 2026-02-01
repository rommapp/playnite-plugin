using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using RomM.Downloads;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;

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

            // If RomM indicates multiple files, we download as an archive name (zip) into the install folder.
            // Otherwise we download the single ROM file.
            var downloadFilePath = info.HasMultipleFiles
                ? Path.Combine(installDir, info.FileName + ".zip")
                : Path.Combine(installDir, info.FileName);

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
                    // (AutoExtract might be false, or file isn't an archive, etc.)
                    var wouldExtract =
                        info.HasMultipleFiles ||
                        (info.Mapping != null && info.Mapping.AutoExtract && IsFileCompressed(downloadFilePath));

                    if (File.Exists(downloadFilePath) && !wouldExtract)
                    {
                        roms.Add(new GameRom(Game.Name, downloadFilePath));
                        return roms;
                    }

                    // Otherwise, we assume extracted files are in installDir
                    var supported = GetEmulatorSupportedFileTypes(info);
                    var actualRomFiles = GetRomFiles(installDir, supported);

                    // Prefer .m3u if requested
                    var useM3u = info.Mapping != null && info.Mapping.UseM3u;
                    if (useM3u)
                    {
                        var m3uFile = actualRomFiles.FirstOrDefault(m =>
                            m.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase));

                    Logger.Debug($"Downloading {Game.Name} to {gamePath}...");
                    Directory.CreateDirectory(installDir);

                    // Stream the file directly to disk
                    using (var fileStream = new FileStream(gamePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    using (var httpStream = await response.Content.ReadAsStreamAsync())
                    {
                        byte[] buffer = new byte[65536];
                        int bytesRead;

                        while ((bytesRead = await httpStream.ReadAsync(buffer, 0, buffer.Length, _watcherToken.Token)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead, _watcherToken.Token);
                        }
                    }

                    Logger.Debug($"Download of {Game.Name} complete.");

                    // Always extract top-level file of multi-file archives
                    if (info.HasMultipleFiles || (info.Mapping.AutoExtract && IsFileCompressed(gamePath)))
                    {
                        Logger.Debug($"Extracting {Game.Name} to {installDir}...");
                        // Extract the archive to the install directory
                        ExtractArchive(gamePath, installDir);

                        // Delete the compressed file
                        File.Delete(gamePath);

                        // Extract nested archives if auto-extract is enabled
                        if (info.HasMultipleFiles && info.Mapping.AutoExtract)
                        {
                            ExtractNestedArchives(installDir);
                        }

                        Logger.Debug($"Extraction of {Game.Name} complete.");

                        List<string> supportedFileTypes = GetEmulatorSupportedFileTypes(info);
                        string[] actualRomFiles = GetRomFiles(installDir, supportedFileTypes);

                        var m3uFile = info.Mapping.UseM3u
                            ? Directory.EnumerateFiles(installDir, "*", SearchOption.AllDirectories)
                                .FirstOrDefault(f => f.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase))
                            : null;

                        if (!string.IsNullOrEmpty(m3uFile))
                        {
                            roms.Add(new GameRom(Game.Name, m3uFile));
                            return roms;
                        }
                    }

                    // Otherwise add all rom files except m3u (we don’t want duplicates)
                    foreach (var f in actualRomFiles.Where(f => !f.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase)))
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
            // NOTE: this traversal check is weak; containment checks should be done via GetFullPath
            // against a trusted root. Keeping your existing checks as-is for now.
            if (installDir == null || installDir.Contains("../") || installDir.Contains(@"..\"))
            {
                throw new ArgumentException("Invalid file path");
            }

            if (supportedFileTypes == null || supportedFileTypes.Count == 0)
            {
                return Directory.GetFiles(installDir, "*", SearchOption.AllDirectories)
                    .ToArray();
            }

            return supportedFileTypes.SelectMany(fileType =>
            {
                if (fileType == null || fileType.Contains("../") || fileType.Contains(@"..\"))
                {
                    throw new ArgumentException("Invalid file path");
                }

                return Directory.GetFiles(installDir, "*." + fileType, SearchOption.AllDirectories);
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

            if (_romM.Settings.Use7z && (!string.IsNullOrEmpty(_romM.Settings.PathTo7z) && _romM.Settings.PathTo7z.EndsWith("7z.exe", StringComparison.OrdinalIgnoreCase)))
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = _romM.Settings.PathTo7z,
                    Arguments = $"x \"{gamePath}\" -o\"{installDir}\" -y",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    if(process.ExitCode != 0)
                    {
                        _romM.Playnite.Notifications.Add(new NotificationMessage("RomM-Archive-Failed", $"Failed to extract {gamePath}", NotificationType.Error));
                        throw new Exception($"7z extraction failed for {gamePath} with exit code {process.ExitCode}.");
                    }
                }
            }
            else
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
    }
}
