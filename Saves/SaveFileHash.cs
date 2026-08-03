using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;

namespace RomM.Saves
{
    /// <summary>
    /// Content hashes RomM uses to compare saves during negotiate. There are two schemes, and
    /// picking the wrong one makes the server misclassify a save:
    ///
    ///   * A plain save file (RetroArch's .srm) hashes as MD5 over its raw bytes.
    ///     See <see cref="Md5HexFile"/>.
    ///   * An archive hashes over its *entries*, not its bytes. The server's _compute_zip_hash
    ///     MD5s each entry's content, pairs that digest with the entry name, sorts the pairs by
    ///     name, joins them as "name:hash" separated by '\n', and MD5s that string.
    ///     See <see cref="ZipHexFile"/>.
    ///
    /// Raw-byte MD5 over an archive can never agree with the server, because zip bytes vary with
    /// entry order, compression settings and timestamps while the content does not. Folder-based
    /// platforms (PS2 memory cards, Switch, PSP, GameCube) upload archives, so they need the
    /// second scheme; using the first would report a conflict on every sync for saves that are in
    /// fact identical.
    ///
    /// Cross-checked against argosy-launcher's SaveArchiver.calculateZipHash so both clients agree
    /// on what "unchanged" means. SaveFileHashTests pins its published vector.
    /// </summary>
    public static class SaveFileHash
    {
        public static string Md5Hex(Stream stream)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static string Md5HexFile(string path)
        {
            using (var fs = File.OpenRead(path))
                return Md5Hex(fs);
        }

        /// <summary>
        /// Hashes an archive the way the server does: per-entry content digests keyed by entry
        /// name, independent of how the archive happens to be laid out. Directory entries carry no
        /// content and are skipped.
        /// </summary>
        public static string ZipHexFile(string path)
        {
            var entries = new List<KeyValuePair<string, string>>();

            using (var archive = ZipArchive.Open(path))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory)
                        continue;

                    using (var stream = entry.OpenEntryStream())
                        entries.Add(new KeyValuePair<string, string>(NormalizeEntryName(entry.Key), Md5Hex(stream)));
                }
            }

            return Combine(entries);
        }

        /// <summary>
        /// The digest <see cref="ZipHexFile"/> would produce for this folder, without building the
        /// archive first. Entry names are rooted at the folder's own name so they match what an
        /// upload writes, which lets negotiate report local state without a temp file.
        /// </summary>
        public static string FolderAsZipHex(string folder)
        {
            return FoldersAsZipHex(new[] { folder });
        }

        /// <summary>
        /// Same as <see cref="FolderAsZipHex"/> for a save whose unit spans several sibling folders
        /// (a PS2 game owning multiple card entries, a PSP game's profile and system data).
        /// </summary>
        public static string FoldersAsZipHex(IEnumerable<string> folders)
        {
            var entries = new List<KeyValuePair<string, string>>();

            foreach (var folder in folders)
            {
                var root = new DirectoryInfo(folder);
                if (!root.Exists)
                    continue;

                foreach (var file in root.GetFiles("*", SearchOption.AllDirectories))
                {
                    var relative = file.FullName.Substring(root.FullName.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    entries.Add(new KeyValuePair<string, string>(
                        NormalizeEntryName(root.Name + "/" + relative),
                        Md5HexFile(file.FullName)));
                }
            }

            return Combine(entries);
        }

        /// <summary>
        /// Zip entry names are '/'-separated by specification. Windows paths are not, so a name
        /// derived from the filesystem has to be normalised here or the digest silently diverges
        /// from what every other client computes for the very same save.
        /// </summary>
        private static string NormalizeEntryName(string name)
        {
            return string.IsNullOrEmpty(name) ? name : name.Replace('\\', '/');
        }

        private static string Combine(List<KeyValuePair<string, string>> entries)
        {
            // Ordinal, not culture-aware: the server and the other clients order by raw code unit,
            // and a culture-sensitive comparison would reorder names under some locales.
            entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

            var sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                    sb.Append('\n');
                sb.Append(entries[i].Key).Append(':').Append(entries[i].Value);
            }

            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString())))
                return Md5Hex(ms);
        }
    }
}
