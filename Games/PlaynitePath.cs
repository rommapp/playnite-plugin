using Playnite.SDK;

namespace RomM.Games
{
    /// <summary>
    /// A portable Playnite stores paths with the "{PlayniteDir}" token, which no filesystem call
    /// resolves. One place decides what that expands to, so a second copy of the rule cannot drift
    /// in behind it -- <see cref="RomM.Settings.EmulatorMapping"/> and the save handlers both ask
    /// here.
    /// </summary>
    internal static class PlaynitePath
    {
        public static string Resolve(IPlayniteAPI playnite, string path)
        {
            if (string.IsNullOrEmpty(path) || playnite?.Paths?.IsPortable != true)
                return path;

            return path.Replace(ExpandableVariables.PlayniteDirectory, playnite.Paths.ApplicationPath);
        }
    }
}
