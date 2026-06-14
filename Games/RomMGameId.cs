namespace RomM.Games
{
    /// <summary>
    /// RomM game ids are stored as "&lt;romMId&gt;:&lt;sha1&gt;". This centralises parsing so the
    /// many call sites don't each re-implement the split/guard logic. Legacy protobuf ids
    /// ("!0...") and any other malformed value return false.
    /// </summary>
    internal static class RomMGameId
    {
        public static bool TryParse(string gameId, out int romMId, out string sha1)
        {
            romMId = -1;
            sha1 = null;

            if (string.IsNullOrEmpty(gameId))
                return false;

            var parts = gameId.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out romMId))
                return false;

            sha1 = parts[1];
            return true;
        }
    }
}
