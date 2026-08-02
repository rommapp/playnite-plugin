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

    public class RomMRevision
    {
        public int Id { get; set; }
        public string FileName { get; set; }

        // The ROM's folder on the RomM filesystem (fs_name) for folder-based ROMs (nested single file
        // or multiple files). Null/empty for a "simple" single file that lives directly in the platform
        // folder. Install paths use this so they mirror RomM's on-disk layout instead of being derived
        // from the download file name (which can carry region tags / an extension the folder doesn't).
        public string FolderName { get; set; }

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
