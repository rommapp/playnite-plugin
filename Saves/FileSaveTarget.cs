using System;
using System.IO;

namespace RomM.Saves
{
    /// <summary>
    /// A save that is a single file on disk -- RetroArch's .srm and anything else that keeps one
    /// blob per game. Uploaded as-is rather than packed, matching what argosy-launcher sends for
    /// the same platforms, so a save round-trips between the two clients untouched.
    /// </summary>
    internal sealed class FileSaveTarget : SaveTarget
    {
        private readonly string _path;

        /// <param name="writePath">Where a downloaded save is written when none exists locally.</param>
        /// <param name="existingPath">
        /// The file the emulator is actually using, when one was found. Everything -- hashing,
        /// upload, download -- goes to this in preference to <paramref name="writePath"/>: the
        /// emulator may keep its save somewhere the configured layout would not predict, and
        /// writing the "correct" path would leave the file it really reads untouched.
        /// </param>
        public FileSaveTarget(string emulatorTag, string writePath, string existingPath)
        {
            EmulatorTag = emulatorTag;
            _path = existingPath ?? writePath;
        }

        public override string EmulatorTag { get; }

        public override string Slot => "autosave";

        public override bool Exists => File.Exists(_path);

        public override string FileName => Path.GetFileName(_path);

        public override DateTime UpdatedAtUtc => new FileInfo(_path).LastWriteTimeUtc;

        public override long SizeBytes => new FileInfo(_path).Length;

        public override string ContentHash() => SaveFileHash.Md5HexFile(_path);

        public override PreparedUpload PrepareUpload()
        {
            return new PreparedUpload(_path, Path.GetFileName(_path), isTemporary: false);
        }

        public override void ApplyDownload(byte[] payload, DateTime? serverUpdatedAtUtc)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(_path, payload);

            if (serverUpdatedAtUtc.HasValue)
                File.SetLastWriteTimeUtc(_path, serverUpdatedAtUtc.Value.ToUniversalTime());
        }
    }
}
