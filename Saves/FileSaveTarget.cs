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
        private readonly string _writePath;
        private readonly string _readPath;

        /// <param name="writePath">Where a downloaded save is written when none exists locally.</param>
        /// <param name="existingPath">
        /// The file the emulator is actually using, when one was found. Downloads overwrite this in
        /// preference to <paramref name="writePath"/>: the emulator may keep its save somewhere the
        /// configured layout would not predict, and writing the "correct" path would leave the file
        /// it really reads untouched.
        /// </param>
        public FileSaveTarget(string emulatorTag, string writePath, string existingPath)
        {
            EmulatorTag = emulatorTag;
            _writePath = writePath;
            _readPath = existingPath ?? writePath;
        }

        public override string EmulatorTag { get; }

        public override string Slot => "autosave";

        public override bool Exists => File.Exists(_readPath);

        public override string FileName => Path.GetFileName(_readPath);

        public override DateTime UpdatedAtUtc => new FileInfo(_readPath).LastWriteTimeUtc;

        public override long SizeBytes => new FileInfo(_readPath).Length;

        public override string ContentHash() => SaveFileHash.Md5HexFile(_readPath);

        public override PreparedUpload PrepareUpload()
        {
            return new PreparedUpload(_readPath, Path.GetFileName(_readPath), isTemporary: false);
        }

        public override void ApplyDownload(byte[] payload, DateTime? serverUpdatedAtUtc)
        {
            var destination = File.Exists(_readPath) ? _readPath : _writePath;

            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(destination, payload);

            if (serverUpdatedAtUtc.HasValue)
                File.SetLastWriteTimeUtc(destination, serverUpdatedAtUtc.Value.ToUniversalTime());
        }
    }
}
