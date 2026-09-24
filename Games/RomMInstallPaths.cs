using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RomM.Games
{
    // Derives a ROM's install directory and playable path. These MUST come from the actual ROM file
    // name (what gets downloaded), not the display name: using the display name drops the extension
    // and can include characters that don't match the installed file, breaking IsInstalled detection
    // and the play path.
    //
    // For folder-based ROMs (nested single file / multiple files) a non-null folderName (fs_name)
    // pins the directory to the ROM's actual folder on the RomM filesystem, instead of deriving it
    // from the download file name — the file name can carry region tags and an extension that the
    // containing folder does not (e.g. file "Game (Europe).zip" inside folder "Game").
    internal static class RomMInstallPaths
    {
        // fs_name and file names come straight from the server, so they are untrusted. A rooted value
        // ("/tmp", @"C:\x", @"\x") makes Path.Combine discard rootInstallDir and ".." walks back out
        // of it — either would let the download and archive extraction write outside the configured
        // mapping. Nested relative paths (a primary file inside a subfolder) stay allowed.
        // Rooting is checked by hand rather than via Path.IsPathRooted so a Windows-rooted value is
        // still rejected when this runs on another platform (e.g. the test host).
        public static bool IsContained(string path)
            => string.IsNullOrEmpty(path)
               || (path[0] != '/'
                   && path[0] != '\\'
                   && path.IndexOf(':') < 0
                   && !path.Split('/', '\\').Any(segment => segment == ".."));

        private static string Contained(string path)
            => IsContained(path) ? path : throw new ArgumentException($"Path from RomM escapes the install root: {path}");

        // Resolves an untrusted relative path against a trusted root, throwing unless the result stays
        // inside it. Archive entry names are attacker-controlled too, so extraction resolves every
        // destination through here instead of handing raw keys to SharpCompress' ExtractFullPath.
        public static string ResolveWithin(string root, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                throw new ArgumentException("Archive entry has no name, refusing to extract it.");

            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var destination = Path.GetFullPath(Path.Combine(fullRoot, Contained(relativePath)));

            if (!IsInside(fullRoot, destination))
                throw new ArgumentException($"Path escapes the install directory: {relativePath}");

            return destination;
        }

        // <root>/<file name without extension>
        public static string InstallDir(string rootInstallDir, string fileName)
            => Path.Combine(rootInstallDir, Path.GetFileNameWithoutExtension(Contained(fileName)));

        // <root>/<folder name> when folderName is set, otherwise <root>/<file name without extension>.
        public static string InstallDir(string rootInstallDir, string folderName, string fileName)
            => string.IsNullOrEmpty(folderName)
                ? InstallDir(rootInstallDir, fileName)
                : Path.Combine(rootInstallDir, Contained(folderName));

        // <root>/<file name without extension>/<file name>
        public static string GamePath(string rootInstallDir, string fileName)
            => Path.Combine(InstallDir(rootInstallDir, fileName), Contained(fileName));

        // <install dir>/<file name>, using the folder-aware install dir.
        public static string GamePath(string rootInstallDir, string folderName, string fileName)
            => Path.Combine(InstallDir(rootInstallDir, folderName, fileName), Contained(fileName));

        // Whether an install lays a ROM's files straight into the mapping's folder, the way "install
        // flat" asks, or gives the game a folder of its own.
        //
        // A ROM fetched as a whole folder brings its own subfolders (patch/, dlc/) with it, so it
        // cannot go flat: extraction would scatter them across the platform folder, where the patch/
        // of every game would merge into a single one, and a flat uninstall -- which removes only the
        // files registered as the game's ROMs -- would leave them behind. Those ROMs keep a folder of
        // their own even when the mapping asks for flat; a single file still installs flat, which is
        // what the setting is for.
        //
        // ROMs RomM itself calls multi-file stay on the flat path they already take: moving them
        // would strand installations whose paths were recorded the old way.
        //
        // The importer and the install controller both ask here, so the path one computes cannot
        // drift from the other's -- they have to agree or IsInstalled detection stops matching.
        public static bool UsesFlatLayout(bool installFlat, bool downloadAsArchive, bool hasMultipleFiles)
            => installFlat && !(downloadAsArchive && !hasMultipleFiles);

        // Whether path sits strictly inside root, ignoring a trailing separator and case.
        //
        // Uninstall removes a directory only when it can show the directory belongs to one game, and
        // this is that proof. Two cases fail it, both of which must keep their files rather than lose
        // a folder: a game installed flat, whose install directory *is* the mapping's folder, shared
        // with every other game on the platform; and an install directory that no longer sits under
        // the mapping at all, because the destination was repointed after the game was installed --
        // there the stale path is the previous platform folder, and deleting it would take every ROM
        // in it. Asking how the game was actually installed also survives the setting being toggled
        // afterwards, which the mapping's own flag does not.
        public static bool IsInside(string root, string path)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path))
                return false;

            var normalizedRoot = NormalizeDirectory(root);
            var normalizedPath = NormalizeDirectory(path);

            return normalizedPath.Length > normalizedRoot.Length
                   && normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        // Whether some other game's install directory is installDir itself or lies inside it -- the
        // folder every flat-installed game on a platform shares, or one that a simple single file and
        // a same-named folder ROM both derive. IsInside alone cannot tell such a folder from a game's
        // own: a mapping repointed to the parent of its old flat folder ("D:\Roms\SNES" -> "D:\Roms")
        // puts that old platform folder inside the new destination, and deleting it would take every
        // ROM still in it.
        public static bool IsClaimedByAnother(string installDir, IEnumerable<string> otherInstallDirs)
        {
            if (string.IsNullOrEmpty(installDir) || otherInstallDirs == null)
                return false;

            var normalized = NormalizeDirectory(installDir);
            return otherInstallDirs.Any(other =>
                !string.IsNullOrEmpty(other)
                && (string.Equals(NormalizeDirectory(other), normalized, StringComparison.OrdinalIgnoreCase)
                    || IsInside(installDir, other)));
        }

        private static string NormalizeDirectory(string path)
        {
            try
            {
                path = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                // A path the filesystem will not resolve is compared as it arrived rather than
                // throwing: this only decides which uninstall branch runs.
            }

            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
