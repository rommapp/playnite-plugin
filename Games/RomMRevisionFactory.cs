using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RomM.Models.RomM.Rom;

namespace RomM.Games
{
    // Builds the per-ROM download descriptor (and selects the file to download). Pure so the
    // single-file vs multi-file endpoint logic is unit-testable without the plugin runtime.
    internal static class RomMRevisionFactory
    {
        // The file highest in the folder tree (fewest path separators); null when there are none.
        public static RomMFile SelectPrimaryFile(IList<RomMFile> files)
        {
            if (files == null || files.Count == 0)
                return null;

            if (files.Count > 1)
                return files.OrderBy(f => (f.FullPath ?? string.Empty).Count(c => c == '/')).FirstOrDefault();

            return files.FirstOrDefault();
        }

        // A file's path relative to the ROM folder (fs_name). Extraction preserves subdirectories, so
        // a file below another directory needs "sub/file.bin", not just "file.bin". Falls back to the
        // leaf name when the folder is not part of the full path.
        public static string RelativeFilePath(RomMFile file, string folderName)
        {
            if (file == null)
                return null;

            var segments = (file.FullPath ?? string.Empty).Split('/');
            var folderIndex = string.IsNullOrEmpty(folderName)
                ? -1
                : Array.FindLastIndex(segments, s => s.Equals(folderName, StringComparison.OrdinalIgnoreCase));

            return folderIndex >= 0 && folderIndex < segments.Length - 1
                ? string.Join(Path.DirectorySeparatorChar.ToString(), segments.Skip(folderIndex + 1))
                : file.FileName;
        }

        // Whether the ROM's whole folder has to be fetched instead of a single file.
        //
        // RomM reports a ROM whose folder holds extra content -- a Switch game with its update and
        // DLC in patch/ and dlc/ subfolders -- as a *nested single file*, because the game is the
        // only file at the folder's root. Trusting that flag alone downloads the game and silently
        // leaves the rest on the server, so a folder holding more than one file is fetched whole,
        // the way a ROM RomM itself calls multi-file already is.
        //
        // has_simple_single_file is excluded on purpose: there fs_name names the file itself rather
        // than a folder, so there is no folder to ask the server for.
        public static bool DownloadsWholeFolder(RomMRom rom)
            => rom.HasMultipleFiles
               || (rom.HasNestedSingleFile && (rom.Files?.Count ?? 0) > 1);

        // Returns null when a single-file ROM has no resolvable file. Single files use the 4.9
        // /files/content endpoint when a file id is present, falling back to the rom-level endpoint
        // (so we never emit "api/roms//files/content/..."); folder downloads take the whole archive.
        public static RomMRevision Build(RomMRom rom, string romMHost)
        {
            var revision = new RomMRevision
            {
                Id = rom.Id,
                // What RomM says the ROM is, kept as-is: it decides whether every extracted file is
                // a playable ROM (the discs of a multi-file game) or only the primary one.
                HasMultipleFiles = rom.HasMultipleFiles,
                DownloadAsArchive = DownloadsWholeFolder(rom),
                IsSelected = false
            };

            if (!revision.DownloadAsArchive)
            {
                var romfile = SelectPrimaryFile(rom.Files);
                if (romfile == null)
                    return null;

                revision.FileName = romfile.FileName;
                // The downloaded file is itself the one to launch.
                revision.PlayableFile = romfile.FileName;
                // A nested single file lives inside a folder named after the ROM (fs_name); a simple
                // single file sits directly in the platform folder and has no wrapping folder.
                revision.FolderName = rom.HasNestedSingleFile ? rom.FileName : null;
                revision.DownloadURL = romfile.Id.HasValue
                    ? RomMUrl.Combine(romMHost, $"api/roms/{romfile.Id}/files/content/{romfile.FileName}")
                    : RomMUrl.Combine(romMHost, $"api/roms/{rom.Id}/content/{romfile.FileName}");
            }
            else
            {
                revision.FileName = rom.FileName;
                // Folder-based ROMs are always stored in a folder named after the ROM (fs_name).
                revision.FolderName = rom.FileName;
                // FileName is the folder / archive base, which is not itself a real file, so the file
                // to launch is resolved here once and reused by the importer for the install path.
                revision.PlayableFile = RelativeFilePath(SelectPrimaryFile(rom.Files), rom.FileName);
                revision.DownloadURL = RomMUrl.Combine(romMHost, $"api/roms/{rom.Id}/content/{rom.FileName}");
            }

            return revision;
        }
    }
}
