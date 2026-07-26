using System;
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

            if (!destination.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
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
    }
}
