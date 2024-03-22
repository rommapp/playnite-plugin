using Playnite.SDK.Models;
using RomM.Games;

internal static class GameInfoBaseExtensions
{
    static public RomMGameInfo GetRomMGameInfo(this Game game)
    {
        return RomMGameInfo.FromGame<RomMGameInfo>(game);
    }

    static public RomMGameInfo GetRomMGameInfo(this GameMetadata game)
    {
        return RomMGameInfo.FromGameMetadata<RomMGameInfo>(game);
    }
}
