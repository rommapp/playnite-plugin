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
        public bool IsSelected { get; set; }
        public EmulatorMapping Mapping { get; set; }
    }

    public struct RomMSavedSibing
    {
        public int Id { get; set; }
        public string FileName { get; set; }
        public bool HasMultipleFiles { get; set; }
        public string DownloadURL { get; set; }
        public bool IsSelected { get; set; }
    }

    public class RomMRomLocal
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string SHA1 { get; set; }
        public string FileName { get; set; }
        public bool HasMultipleFiles { get; set; }
        public string DownloadURL { get; set; }
        public bool IsSelected { get; set; }
        public Guid MappingID { get; set; }

        public List<RomMSavedSibing> Siblings { get; set; }

    }
}
