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

        // True when the download is the ROM's whole folder as one archive instead of a single file.
        // Deliberately separate from HasMultipleFiles: RomM reports a Switch game that has an update
        // or DLC beside it as a *single* file -- the game is the only file at the folder's root -- yet
        // the folder still has to be fetched whole. Keeping the two apart is what lets a genuine
        // multi-file ROM go on exposing every extracted file as a playable disc while these do not.
        // Absent from sidecars written before this existed, where it reads back as false.
        public bool DownloadAsArchive { get; set; }

        // The file to launch, relative to the ROM folder: the file inside the extracted folder for an
        // archive download, the downloaded file itself otherwise. Null in pre-existing sidecars.
        public string PlayableFile { get; set; }

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
