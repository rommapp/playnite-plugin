using System;
using System.IO;

namespace RomM.Saves
{
    /// <summary>
    /// A game's local save, as the sync service needs to see it: something that may or may not
    /// exist, reports a content hash and a modification time, can be packaged for upload, and can
    /// be replaced from a downloaded blob.
    ///
    /// Deliberately says nothing about whether that is one file or a directory tree. RetroArch's
    /// SRAM is a single .srm; a PS2 memory card entry, a Switch title's save folder and a
    /// GameCube game's .gci set are several files that travel as one archive. Both shapes have to
    /// answer the same questions for negotiate to work, and only the implementation differs --
    /// including which of the two hashing schemes in <see cref="SaveFileHash"/> applies.
    /// </summary>
    internal abstract class SaveTarget
    {
        /// <summary>
        /// The emulator tag the server files this save under. Metadata rather than a filter --
        /// RomM keys saves by rom id, so a save uploaded under one tag is still offered to a
        /// client using another.
        /// </summary>
        public abstract string EmulatorTag { get; }

        /// <summary>
        /// The slot the save is filed under. Not optional in practice: `sync/negotiate` only
        /// considers saves that carry one, so a slot-less upload lands on the server correctly and
        /// is then invisible to every device, including the one that wrote it. Other RomM clients
        /// use "autosave" for a game's live save regardless of platform, and matching that is what
        /// keeps the same save reconcilable across them.
        /// </summary>
        public abstract string Slot { get; }

        /// <summary>Whether there is anything locally to report or upload yet.</summary>
        public abstract bool Exists { get; }

        /// <summary>Name reported to the server, and the name the upload is filed under.</summary>
        public abstract string FileName { get; }

        public abstract DateTime UpdatedAtUtc { get; }

        public abstract long SizeBytes { get; }

        /// <summary>
        /// The digest the server compares against. Implementations must pick the scheme that
        /// matches what they upload: raw bytes for a single file, per-entry for an archive.
        /// </summary>
        public abstract string ContentHash();

        /// <summary>
        /// Produces the bytes to send. Callers dispose the result, which cleans up any temporary
        /// archive built along the way.
        /// </summary>
        public abstract PreparedUpload PrepareUpload();

        /// <summary>
        /// Replaces the local save with a downloaded payload. <paramref name="serverUpdatedAtUtc"/>
        /// is applied to the result where possible so the next negotiate sees the two sides as
        /// in sync rather than as a fresh local edit.
        /// </summary>
        public abstract void ApplyDownload(byte[] payload, DateTime? serverUpdatedAtUtc);
    }

    /// <summary>
    /// A file ready to be uploaded. <see cref="IsTemporary"/> marks archives built on the fly, so
    /// a single-file save is sent straight from disk without being copied.
    /// </summary>
    internal sealed class PreparedUpload : IDisposable
    {
        public PreparedUpload(string filePath, string fileName, bool isTemporary)
        {
            FilePath = filePath;
            FileName = fileName;
            IsTemporary = isTemporary;
        }

        public string FilePath { get; }
        public string FileName { get; }
        public bool IsTemporary { get; }

        public void Dispose()
        {
            if (!IsTemporary)
                return;

            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch
            {
                // A leftover temp file is not worth failing a sync over.
            }
        }
    }
}
