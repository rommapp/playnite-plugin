using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using RomM.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ProtoBuf;
using Playnite.SDK;

namespace RomM.Games
{
    [ProtoContract]
    internal class RomMGameInfo
    {
        [ProtoMember(1)]
        public Guid MappingId { get; set; }

        [ProtoMember(2)]
        public string DownloadUrl { get; set; }

        [ProtoMember(3)]
        public string FileName { get; set; }

        [ProtoMember(4)]
        public bool IsMulti { get; set; }

        public EmulatorMapping Mapping
        {
            get
            {
                return Settings.SettingsViewModel.Instance.Mappings.FirstOrDefault(m => m.MappingId == MappingId);
            }
        }

        public string AsGameId()
        {
            using (var ms = new MemoryStream())
            {
                Serializer.Serialize(ms, this);
                return $"!0{Convert.ToBase64String(ms.ToArray())}";
            }
        }

        public static T FromGame<T>(Game game) where T : RomMGameInfo
        {
            return FromGameIdString<T>(game.GameId);
        }

        public static T FromGameMetadata<T>(GameMetadata game) where T : RomMGameInfo
        {
            return FromGameIdString<T>(game.GameId);
        }

        private static T FromGameIdString<T>(string gameId) where T : RomMGameInfo
        {
            Debug.Assert(gameId != null, "GameId is null");
            Debug.Assert(gameId.Length > 0, "GameId is empty");
            Debug.Assert(gameId[0] == '!', "GameId is not in expected format. (Legacy game that didn't get converted?)");
            Debug.Assert(gameId.Length > 2, $"GameId is too short ({gameId.Length} chars)");
            Debug.Assert(gameId[1] == '0', $"GameId is marked as being serialized ProtoBuf, but of invalid version. (Expected 0, got {gameId[1]})");

            var gameInfoStr = Convert.FromBase64String(gameId.Substring(2));
            using (var ms = new MemoryStream(gameInfoStr))
            {
                return Serializer.Deserialize<T>(ms);
            }
        }

        public InstallController GetInstallController(Game game, RomM romm) => new RomMInstallController(game, romm);

        public UninstallController GetUninstallController(Game game, RomM romm) => new RomMUninstallController(game, romm);

        protected IEnumerable<string> GetDescriptionLines()
        {
            yield return $"{nameof(DownloadUrl)} : {DownloadUrl}";
        }

        public string ToDescriptiveString(Game g)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Game: {g.Name}");
            sb.AppendLine($"Type: {GetType()}");
            sb.AppendLine($"{nameof(MappingId)}: {MappingId}");

            GetDescriptionLines().ForEach(l => sb.AppendLine(l));

            var mapping = Mapping;
            if (mapping != null)
            {
                sb.AppendLine();
                sb.AppendLine("Mapping Info:");
                mapping.GetDescriptionLines().ForEach(l => sb.AppendLine($"    {l}"));
            }

            return sb.ToString();
        }
    }
}
