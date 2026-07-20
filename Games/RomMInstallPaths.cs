using System.IO;

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
        // <root>/<file name without extension>
        public static string InstallDir(string rootInstallDir, string fileName)
            => Path.Combine(rootInstallDir, Path.GetFileNameWithoutExtension(fileName));

        // <root>/<folder name> when folderName is set, otherwise <root>/<file name without extension>.
        public static string InstallDir(string rootInstallDir, string folderName, string fileName)
            => string.IsNullOrEmpty(folderName)
                ? InstallDir(rootInstallDir, fileName)
                : Path.Combine(rootInstallDir, folderName);

        // <root>/<file name without extension>/<file name>
        public static string GamePath(string rootInstallDir, string fileName)
            => Path.Combine(InstallDir(rootInstallDir, fileName), fileName);

        // <install dir>/<file name>, using the folder-aware install dir.
        public static string GamePath(string rootInstallDir, string folderName, string fileName)
            => Path.Combine(InstallDir(rootInstallDir, folderName, fileName), fileName);
    }
}
