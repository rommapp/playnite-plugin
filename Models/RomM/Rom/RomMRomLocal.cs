using RomM.Settings;
using System;
using System.Collections.Generic;

namespace RomM.Models.RomM.Rom
{
    enum MainSibling
    {
        None = -1,
        Current = 0,
        Other = 1
    }

    public struct GameInstallInfo
    {
        public int Id { get; set; }
        public string FileName { get; set; }
        public bool HasMultipleFiles { get; set; }
        public string DownloadURL { get; set; }
        public EmulatorMapping Mapping { get; set; }
    }

    public struct RomMRevision
    {
        public int Id { get; set; }
        public string FileName { get; set; }
        public bool HasMultipleFiles { get; set; }
        public string DownloadURL { get; set; }
        public bool IsSelected { get; set; }
    }

    public class RomMRomLocal
    {
        public string Name { get; set; }
        public string SHA1 { get; set; }
        public Guid MappingID { get; set; }

        public List<RomMRevision> ROMVersions { get; set; }

    }
}
