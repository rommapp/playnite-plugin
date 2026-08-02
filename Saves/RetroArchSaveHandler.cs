using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RomM.Saves
{
    /// <summary>
    /// RetroArch's battery saves. The location comes out of retroarch.cfg rather than being fixed,
    /// so the file is found by reading the emulator's own configuration and then, if that path is
    /// empty, by looking for the save the emulator already wrote.
    ///
    /// The save is named after the ROM, not after any platform identifier, which is why this
    /// handler needs no title id and works for every core.
    /// </summary>
    internal sealed class RetroArchSaveHandler : ISaveHandler
    {
        public string EmulatorTag => "retroarch";

        public bool CanHandle(Emulator emulator)
        {
            if (emulator == null)
                return false;

            if (string.Equals(emulator.BuiltInConfigId, "retroarch", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(emulator.Name) &&
                emulator.Name.IndexOf("retroarch", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (!string.IsNullOrEmpty(emulator.InstallDir) &&
                File.Exists(Path.Combine(emulator.InstallDir, "retroarch.exe")))
                return true;

            return false;
        }

        public SaveTarget ResolveTarget(SaveTargetRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.ContentPath))
                    return null;

                var cfgPath = FindConfig(request.Emulator);
                var cfg = cfgPath != null
                    ? RetroArchConfig.Parse(File.ReadAllText(cfgPath))
                    : new Dictionary<string, string>();

                var baseDir = request.Emulator.InstallDir;

                var expectedPath = RetroArchConfig.ResolveSaveFilePath(cfg, request.ContentPath, null, baseDir);
                if (string.IsNullOrEmpty(expectedPath))
                    return null;

                // The configured path is where RetroArch *would* write. When nothing is there, the
                // save may still exist under a per-core or per-content subfolder we did not model,
                // so fall back to searching for it by ROM name before assuming there is none.
                var existing = File.Exists(expectedPath)
                    ? expectedPath
                    : FindExistingSave(
                        RetroArchConfig.ResolveSaveBaseDirectory(cfg, request.ContentPath, baseDir),
                        Path.GetFileNameWithoutExtension(request.ContentPath));

                return new FileSaveTarget(EmulatorTag, expectedPath, existing);
            }
            catch (Exception ex)
            {
                request.Logger?.Error(ex, $"[SaveSync] Failed to resolve RetroArch save path for {request.Game?.Name}.");
                return null;
            }
        }

        private static string FindConfig(Emulator emulator)
        {
            if (!string.IsNullOrEmpty(emulator.InstallDir))
            {
                var inInstall = Path.Combine(emulator.InstallDir, "retroarch.cfg");
                if (File.Exists(inInstall))
                    return inInstall;
            }

            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RetroArch", "retroarch.cfg");
            return File.Exists(appData) ? appData : null;
        }

        private static string FindExistingSave(string baseDir, string contentName)
        {
            if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir) || string.IsNullOrEmpty(contentName))
                return null;

            try
            {
                return Directory
                    .EnumerateFiles(baseDir, contentName + RetroArchConfig.SaveExtension, SearchOption.AllDirectories)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }
    }
}
