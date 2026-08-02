using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using RomM.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ProtoBuf;
using RomM.Models.RomM.Rom;

namespace RomM.Games
{
    // Plugin-coupled half of RomMGameInfo: resolving the emulator mapping and constructing the
    // install/uninstall controllers. Kept out of RomMGameInfo.cs so the serialization half stays
    // unit-testable without the Playnite/plugin runtime.
    internal partial class RomMGameInfo
    {
        public EmulatorMapping Mapping
        {
            get
            {
                return Settings.SettingsViewModel.Instance.Mappings.FirstOrDefault(m => m.MappingId == MappingId);
            }
        }

        public InstallController GetInstallController(Game game, RomM romm, GameInstallInfo GameData) => new RomMInstallController(game, romm, GameData);

        public UninstallController GetUninstallController(Game game, RomM romm, EmulatorMapping mapping) => new RomMUninstallController(game, romm, mapping);

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
