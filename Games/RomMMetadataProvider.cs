using Playnite.SDK;
using Playnite.SDK.Models;
using RomM.Models.RomM.Rom;

namespace RomM.Games
{
    public class RomMMetadataProvider : LibraryMetadataProvider
    {
        private readonly IRomM _romM;
        public RomMMetadataProvider(RomM romM)
        {
            _romM = romM;
        }

        public override GameMetadata GetMetadata(Game game)
        {
            if (!RomMGameId.TryParse(game.GameId, out int romMId, out string _))
            {
                _romM.Logger.Error($"[Metadata] {game.Name} GameID is malformed!");
                return null;
            }

            RomMRom romMGame = _romM.FetchRom(romMId.ToString());
            if(romMGame == null)
            {
                _romM.Logger.Error($"[Metadata] {game.Name} failed to get game!");
                return null;
            }

            // RomM 4.9+ can omit these fields; normalise so the metadata mapping never null-derefs.
            romMGame.Normalize();

            return RomMMetadataMapper.BuildBaseMetadata(romMGame, _romM.Settings.RomMHost, _romM.Playnite.ApplicationSettings.AgeRatingOrgPriority.ToString());
        }
    }
}
