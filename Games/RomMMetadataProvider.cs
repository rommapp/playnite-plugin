using Playnite.SDK;
using Playnite.SDK.Models;
using RomM.Models.RomM.Rom;
using System;
using System.Collections.Generic;
using System.Linq;

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
            int romMId;
            if (!int.TryParse(game.GameId.Split(':')[0], out romMId))
            {
                _romM.Logger.Error($"{game.Name} GameID is malformed!");
                return null;
            }

            RomMRom romMGame = _romM.FetchRom(romMId.ToString());

            var preferedRatingsBoard = _romM.Playnite.ApplicationSettings.AgeRatingOrgPriority;
            var agerating = romMGame.Metadatum.Age_Ratings.Count > 0 ? new HashSet<MetadataProperty>(romMGame.Metadatum.Age_Ratings.Where(r => r.Split(':')[0] == preferedRatingsBoard.ToString()).Select(r => new MetadataNameProperty(r.ToString()))) : null;

            List<Link> gameLinks = new List<Link>();
            if (romMGame.SSId != null)
                gameLinks.Add(new Link("Screenscraper", $"https://www.screenscraper.fr/gameinfos.php?gameid={romMGame.SSId}"));
            if (romMGame.HasheousId != null)
                gameLinks.Add(new Link("Hasheous", $"https://hasheous.org/index.html?page=dataobjectdetail&type=game&id={romMGame.HasheousId}"));
            if (romMGame.RAId != null)
                gameLinks.Add(new Link("RetroAchievements", $"https://retroachievements.org/game/{romMGame.RAId}"));
            if (romMGame.HLTBId != null)
                gameLinks.Add(new Link("HowLongToBeat", $"https://howlongtobeat.com/game/{romMGame.HLTBId}"));

            var metadata = new GameMetadata
            {
                Name = romMGame.Name,
                Description = romMGame.Summary,

                Regions = new HashSet<MetadataProperty>(romMGame.Regions.Where(r => !string.IsNullOrEmpty(r)).Select(r => new MetadataNameProperty(r.ToString()))),
                Genres = new HashSet<MetadataProperty>(romMGame.Metadatum.Genres.Where(r => !string.IsNullOrEmpty(r)).Select(r => new MetadataNameProperty(r.ToString()))),
                AgeRatings = agerating,
                Series = new HashSet<MetadataProperty>(romMGame.Metadatum.Franchises.Where(r => !string.IsNullOrEmpty(r)).Select(r => new MetadataNameProperty(r.ToString()))),
                Features = new HashSet<MetadataProperty>(romMGame.Metadatum.Gamemodes.Where(r => !string.IsNullOrEmpty(r)).Select(r => new MetadataNameProperty(r.ToString()))),
                Categories = new HashSet<MetadataProperty>(romMGame.Metadatum.Collections.Where(r => !string.IsNullOrEmpty(r)).Select(r => new MetadataNameProperty(r.ToString()))),

                ReleaseDate = romMGame.Metadatum.Release_Date.HasValue ? new ReleaseDate(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(romMGame.Metadatum.Release_Date.Value).ToLocalTime()) : new ReleaseDate(),
                CommunityScore = (int?)romMGame.Metadatum.Average_Rating,

                CoverImage = !string.IsNullOrEmpty(romMGame.PathCoverL) ? new MetadataFile($"{_romM.Settings.RomMHost}{romMGame.PathCoverL}") : null,

                LastActivity = romMGame.RomUser.LastPlayed,
                UserScore = romMGame.RomUser.Rating * 10, //RomM-Rating is 1-10, Playnite 1-100, so it can unfortunately only by synced one direction without loosing decimals
                Links = gameLinks,
                
            };

            return metadata;
        }
    }
}
