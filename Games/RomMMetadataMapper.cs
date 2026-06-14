using Playnite.SDK.Models;
using RomM.Models.RomM.Rom;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RomM.Games
{
    // Maps a RomM ROM payload to the GameMetadata fields shared by the library importer and the
    // metadata downloader. The importer layers identity/install fields (Source, GameId, Platforms,
    // GameActions, ...) on top of the returned object; the downloader returns it as-is.
    internal static class RomMMetadataMapper
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        public static GameMetadata BuildBaseMetadata(RomMRom rom, string romMHost, string preferredRatingsBoard)
        {
            var ageRatings = rom.Metadatum.Age_Ratings.Count > 0
                ? new HashSet<MetadataProperty>(rom.Metadatum.Age_Ratings
                    .Where(r => r.Split(':')[0] == preferredRatingsBoard)
                    .Select(r => new MetadataNameProperty(r)))
                : null;

            var links = new List<Link>();
            if (rom.SSId != null)
                links.Add(new Link("Screenscraper", $"https://www.screenscraper.fr/gameinfos.php?gameid={rom.SSId}"));
            if (rom.HasheousId != null)
                links.Add(new Link("Hasheous", $"https://hasheous.org/index.html?page=dataobjectdetail&type=game&id={rom.HasheousId}"));
            if (rom.RAId != null)
                links.Add(new Link("RetroAchievements", $"https://retroachievements.org/game/{rom.RAId}"));
            if (rom.HLTBId != null)
                links.Add(new Link("HowLongToBeat", $"https://howlongtobeat.com/game/{rom.HLTBId}"));

            return new GameMetadata
            {
                Name = rom.Name,
                Description = rom.Summary,
                Regions = ToNameSet(rom.Regions),
                Genres = ToNameSet(rom.Metadatum.Genres),
                AgeRatings = ageRatings,
                Series = ToNameSet(rom.Metadatum.Franchises),
                Features = ToNameSet(rom.Metadatum.Gamemodes),
                Categories = ToNameSet(rom.Metadatum.Collections),
                ReleaseDate = rom.Metadatum.Release_Date.HasValue
                    ? new ReleaseDate(UnixEpoch.AddMilliseconds(rom.Metadatum.Release_Date.Value).ToLocalTime())
                    : new ReleaseDate(),
                CommunityScore = (int?)rom.Metadatum.Average_Rating,
                CoverImage = !string.IsNullOrEmpty(rom.PathCoverL) ? new MetadataFile($"{romMHost}{rom.PathCoverL}") : null,
                // Game icon: Screenscraper wheel (logo) -> miximage -> cover image.
                Icon = ScreenscraperIcon(romMHost, rom.SSMetadata)
                    ?? ResolveImage(romMHost, rom.PathCoverL, rom.UrlCover),
                LastActivity = rom.RomUser.LastPlayed,
                // RomM rating is 1-10, Playnite 1-100, so it can only be synced one direction without losing decimals.
                UserScore = rom.RomUser.Rating * 10,
                Links = links,
            };
        }

        private static HashSet<MetadataProperty> ToNameSet(IEnumerable<string> values)
            => new HashSet<MetadataProperty>(values.Where(v => !string.IsNullOrEmpty(v)).Select(v => new MetadataNameProperty(v)));

        private static MetadataFile ScreenscraperIcon(string romMHost, RomMSSMetadata ss)
        {
            if (ss == null)
                return null;

            // Prefer the "wheel" (logo); fall back to the miximage. RomM-hosted path first, then external url.
            return ResolveImage(romMHost, ss.LogoPath, ss.LogoUrl)
                ?? ResolveImage(romMHost, ss.MiximagePath, ss.MiximageUrl);
        }

        private static MetadataFile ResolveImage(string romMHost, string path, string url)
        {
            if (!string.IsNullOrEmpty(path))
                return new MetadataFile($"{romMHost}{path}");
            if (!string.IsNullOrEmpty(url))
                return new MetadataFile(url);
            return null;
        }
    }
}
