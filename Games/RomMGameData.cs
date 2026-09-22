using Newtonsoft.Json;
using Playnite.SDK;
using RomM.Models.RomM.Rom;
using System;
using System.IO;

namespace RomM.Games
{
    /// <summary>
    /// The per-ROM sidecar ("{sha1}.json" under the plugin's data folder): the download
    /// descriptors for every revision, the emulator mapping the ROM was imported under, and what
    /// the importer last wrote onto the game's play action.
    ///
    /// Install, uninstall, the version menu and save sync all need it, and each used to carry its
    /// own copy of the path building, the id parsing and the try/catch around a corrupt file.
    /// </summary>
    internal static class RomMGameData
    {
        public static string PathFor(string romDataPath, string sha1) =>
            Path.Combine(romDataPath ?? string.Empty, $"{sha1}.json");

        /// <summary>
        /// The sidecar for a game id, or null when the id is malformed, the file is missing or its
        /// contents cannot be read. What a missing sidecar means is the caller's business.
        /// </summary>
        public static RomMRomLocal Load(string romDataPath, string gameId, ILogger logger, string gameName = null)
        {
            if (!RomMGameId.TryParse(gameId, out int _, out string sha1))
            {
                logger?.Error($"{gameName ?? gameId} GameID is malformed!");
                return null;
            }

            return LoadBySha1(romDataPath, sha1, logger, out string _, gameName);
        }

        /// <param name="rawJson">
        /// The file's text exactly as read, or null when there was none. A caller about to rewrite
        /// the sidecar compares against this to decide whether anything changed, rather than
        /// re-serialising what it has just parsed once per ROM.
        /// </param>
        public static RomMRomLocal LoadBySha1(string romDataPath, string sha1, ILogger logger, out string rawJson, string gameName = null)
        {
            rawJson = null;

            var path = PathFor(romDataPath, sha1);
            if (!File.Exists(path))
                return null;

            try
            {
                var text = File.ReadAllText(path);
                var data = JsonConvert.DeserializeObject<RomMRomLocal>(text);
                rawJson = text;
                return data;
            }
            catch (Exception ex)
            {
                logger?.Error(ex, $"{gameName ?? sha1} ROM data file is corrupted!");
                return null;
            }
        }

        public static void Save(string romDataPath, string sha1, RomMRomLocal data) =>
            File.WriteAllText(PathFor(romDataPath, sha1), JsonConvert.SerializeObject(data));
    }
}
