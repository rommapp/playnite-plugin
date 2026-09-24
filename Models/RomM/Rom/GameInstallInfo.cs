using RomM.Settings;

namespace RomM.Models.RomM.Rom
{
    // Carries an EmulatorMapping (a Settings type), so it lives in its own file to keep the rest of
    // RomMRomLocal.cs free of the RomM.Settings dependency and unit-testable.
    public struct GameInstallInfo
    {
        public int Id { get; set; }
        public string FileName { get; set; }
        public string FolderName { get; set; }
        public bool HasMultipleFiles { get; set; }
        public bool DownloadAsArchive { get; set; }
        public string PlayableFile { get; set; }
        public string DownloadURL { get; set; }
        public EmulatorMapping Mapping { get; set; }

        public static GameInstallInfo From(RomMRevision revision, EmulatorMapping mapping)
            => new GameInstallInfo
            {
                Id = revision.Id,
                FileName = revision.FileName,
                FolderName = revision.FolderName,
                HasMultipleFiles = revision.HasMultipleFiles,
                DownloadAsArchive = revision.DownloadAsArchive,
                PlayableFile = revision.PlayableFile,
                DownloadURL = revision.DownloadURL,
                Mapping = mapping
            };
    }
}
