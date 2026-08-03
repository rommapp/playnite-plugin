using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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
                var saveRoot = RetroArchConfig.ResolveSaveBaseDirectory(cfg, request.ContentPath, baseDir);

                // With sort_savefiles_enable the save sits in a folder named after the running core.
                // Resolving without that name only matters once a download has to create the file:
                // it would land beside the core folders instead of inside the right one, where
                // RetroArch never looks, and the game would start over on a save that is present.
                var coreName = MatchExistingCoreFolder(saveRoot, ResolveCoreName(request.Profile));

                var expectedPath = RetroArchConfig.ResolveSaveFilePath(cfg, request.ContentPath, coreName, baseDir);
                if (string.IsNullOrEmpty(expectedPath))
                    return null;

                // The configured path is where RetroArch *would* write. When nothing is there, the
                // save may still exist under a subfolder we did not model, so fall back to
                // searching for it by ROM name before assuming there is none.
                var existing = File.Exists(expectedPath)
                    ? expectedPath
                    : FindExistingSave(saveRoot, Path.GetFileNameWithoutExtension(request.ContentPath));

                return new FileSaveTarget(EmulatorTag, expectedPath, existing);
            }
            catch (Exception ex)
            {
                request.Logger?.Error(ex, $"[SaveSync] Failed to resolve RetroArch save path for {request.Game?.Name}.");
                return null;
            }
        }

        /// <summary>
        /// The core a profile runs, as far as it can be told from Playnite. Built-in RetroArch
        /// profiles are named after the core ("mGBA"); a custom profile carries it in the libretro
        /// argument (`-L "cores\mgba_libretro.dll"`). Null when neither yields anything, which
        /// leaves the per-core folder out of the path exactly as before.
        /// </summary>
        internal static string ResolveCoreName(EmulatorProfile profile)
        {
            var builtIn = profile as BuiltInEmulatorProfile;
            if (builtIn != null)
                return string.IsNullOrWhiteSpace(builtIn.Name) ? null : builtIn.Name.Trim();

            var custom = profile as CustomEmulatorProfile;
            if (custom != null)
                return CoreFromArguments(custom.Arguments);

            return null;
        }

        private static readonly Regex LibretroArgument =
            new Regex(@"-L\s+""?(?<path>[^""\s]+)""?", RegexOptions.IgnoreCase);

        private static string CoreFromArguments(string arguments)
        {
            if (string.IsNullOrEmpty(arguments))
                return null;

            var match = LibretroArgument.Match(arguments);
            if (!match.Success)
                return null;

            var name = Path.GetFileNameWithoutExtension(match.Groups["path"].Value);
            if (string.IsNullOrEmpty(name))
                return null;

            if (name.EndsWith("_libretro", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - "_libretro".Length);

            return name.Length == 0 ? null : name;
        }

        /// <summary>
        /// RetroArch names the folder after the core's own display name, which is not always how
        /// Playnite spells it — a profile can yield "mgba" where the folder on disk is "mGBA".
        /// Where a matching folder already exists its spelling wins, so a download joins the saves
        /// RetroArch is already writing instead of creating a near-duplicate beside them.
        /// </summary>
        private static string MatchExistingCoreFolder(string saveRoot, string coreName)
        {
            if (string.IsNullOrEmpty(coreName) || string.IsNullOrEmpty(saveRoot) || !Directory.Exists(saveRoot))
                return coreName;

            try
            {
                var match = Directory.EnumerateDirectories(saveRoot)
                    .Select(Path.GetFileName)
                    .FirstOrDefault(n => string.Equals(n, coreName, StringComparison.OrdinalIgnoreCase));

                return match ?? coreName;
            }
            catch
            {
                return coreName;
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
