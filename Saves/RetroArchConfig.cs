using System;
using System.Collections.Generic;
using System.IO;

namespace RomM.Saves
{
    /// <summary>
    /// Parses a retroarch.cfg and resolves where RetroArch writes a game's battery save (.srm).
    /// Pure, Playnite-free logic so it can be unit-tested; the filesystem/emulator lookups live in
    /// <see cref="SaveSyncService"/>.
    ///
    /// RetroArch save path rules (battery / SRAM):
    ///   base = savefile_directory (empty / "default" -> the content's own directory)
    ///   + per-core subfolder      when sort_savefiles_enable = true
    ///   + per-content subfolder   when sort_savefiles_by_content_enable = true
    ///   file = &lt;content base name&gt;.srm
    /// </summary>
    public static class RetroArchConfig
    {
        /// <summary>RetroArch battery-save extension. RetroArch always writes SRAM here regardless of core.</summary>
        public const string SaveExtension = ".srm";

        /// <summary>
        /// Parses retroarch.cfg text into a key/value map. Lines are "key = value"; values may be
        /// wrapped in double quotes and lines starting with '#' are comments.
        /// </summary>
        public static Dictionary<string, string> Parse(string cfgText)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(cfgText))
                return result;

            foreach (var rawLine in cfgText.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();

                // Strip a single pair of surrounding double quotes.
                if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                    value = value.Substring(1, value.Length - 2);

                if (key.Length > 0)
                    result[key] = value;
            }

            return result;
        }

        /// <summary>
        /// Resolves the expected .srm path for <paramref name="contentFilePath"/> given the parsed
        /// config. <paramref name="coreName"/> may be null when unknown (the per-core subfolder is
        /// then skipped; <see cref="SaveSyncService"/> falls back to a recursive search at runtime).
        /// <paramref name="retroArchBaseDir"/> expands RetroArch's leading ':' base-directory token.
        /// Returns null when no content path is available.
        /// </summary>
        public static string ResolveSaveFilePath(
            IDictionary<string, string> cfg,
            string contentFilePath,
            string coreName = null,
            string retroArchBaseDir = null)
        {
            if (string.IsNullOrEmpty(contentFilePath))
                return null;

            var contentDir = Path.GetDirectoryName(contentFilePath);
            var contentName = Path.GetFileNameWithoutExtension(contentFilePath);

            var saveDir = ExpandPath(GetValue(cfg, "savefile_directory"), retroArchBaseDir);
            if (string.IsNullOrEmpty(saveDir))
                saveDir = contentDir;

            if (GetBool(cfg, "sort_savefiles_enable") && !string.IsNullOrEmpty(coreName))
                saveDir = Path.Combine(saveDir, coreName);

            if (GetBool(cfg, "sort_savefiles_by_content_enable"))
                saveDir = Path.Combine(saveDir, contentName);

            return Path.Combine(saveDir, contentName + SaveExtension);
        }

        /// <summary>
        /// Resolves the configured save base directory (without any per-core / per-content sorting),
        /// used as the root for a recursive ".srm" search when the exact path doesn't exist. Falls
        /// back to the content directory when savefile_directory is unset.
        /// </summary>
        public static string ResolveSaveBaseDirectory(
            IDictionary<string, string> cfg,
            string contentFilePath,
            string retroArchBaseDir = null)
        {
            var saveDir = ExpandPath(GetValue(cfg, "savefile_directory"), retroArchBaseDir);
            if (string.IsNullOrEmpty(saveDir) && !string.IsNullOrEmpty(contentFilePath))
                saveDir = Path.GetDirectoryName(contentFilePath);

            return saveDir;
        }

        private static string GetValue(IDictionary<string, string> cfg, string key)
        {
            return cfg != null && cfg.TryGetValue(key, out var v) ? v : null;
        }

        private static bool GetBool(IDictionary<string, string> cfg, string key)
        {
            var v = GetValue(cfg, key);
            return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Normalises a RetroArch directory value: empty / "default" -> null (use content dir),
        /// a leading ':' is RetroArch's base-directory token, and environment variables are expanded.
        /// </summary>
        private static string ExpandPath(string raw, string retroArchBaseDir)
        {
            if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "default", StringComparison.OrdinalIgnoreCase))
                return null;

            raw = Environment.ExpandEnvironmentVariables(raw);

            if (raw.Length > 0 && raw[0] == ':')
            {
                var rest = raw.Substring(1).TrimStart('\\', '/');
                if (!string.IsNullOrEmpty(retroArchBaseDir))
                    return Path.Combine(retroArchBaseDir, rest);
                return rest;
            }

            return raw;
        }
    }
}
