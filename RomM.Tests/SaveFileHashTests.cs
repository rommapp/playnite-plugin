using System.IO;
using System.Text;
using RomM.Saves;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers;
using Xunit;

namespace RomM.Tests
{
    public class SaveFileHashTests
    {
        // The server hashes saves with MD5 (hex). These are the canonical MD5 digests; if the client
        // produced anything else, negotiate could never detect identical files.
        [Fact]
        public void Md5Hex_of_empty_stream_matches_known_digest()
        {
            using (var stream = new MemoryStream())
            {
                Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", SaveFileHash.Md5Hex(stream));
            }
        }

        [Fact]
        public void Md5Hex_of_abc_matches_known_digest()
        {
            using (var stream = new MemoryStream(Encoding.ASCII.GetBytes("abc")))
            {
                Assert.Equal("900150983cd24fb0d6963f7d28e17f72", SaveFileHash.Md5Hex(stream));
            }
        }

        [Fact]
        public void Md5HexFile_reads_and_hashes_file()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "abc");
                Assert.Equal("900150983cd24fb0d6963f7d28e17f72", SaveFileHash.Md5HexFile(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        // Archives are hashed by their entries, not their bytes. This vector is the one
        // argosy-launcher pins in SaveArchiverHashParityTest against the server's
        // _compute_zip_hash; drift here means Playnite and Argosy would disagree about whether a
        // save changed, and negotiate would report conflicts for identical data.
        [Fact]
        public void ZipHexFile_matches_server_compute_zip_hash_vector()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".zip");
            try
            {
                WriteZip(path,
                    new ZipEntrySpec("a.sav", new byte[] { 0x00, 0x01, 0x02 }),
                    new ZipEntrySpec("b.sav", new byte[] { 0xFF, 0xFE }));

                Assert.Equal("fe72f8d850245659647bd6b5f3577a7a", SaveFileHash.ZipHexFile(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        // The digest keys on entry names, so the order they were written in must not matter --
        // otherwise two clients zipping the same save in a different order would disagree.
        [Fact]
        public void ZipHexFile_is_independent_of_entry_order()
        {
            var forward = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".zip");
            var reverse = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".zip");
            try
            {
                WriteZip(forward,
                    new ZipEntrySpec("a.sav", new byte[] { 0x00, 0x01, 0x02 }),
                    new ZipEntrySpec("b.sav", new byte[] { 0xFF, 0xFE }));
                WriteZip(reverse,
                    new ZipEntrySpec("b.sav", new byte[] { 0xFF, 0xFE }),
                    new ZipEntrySpec("a.sav", new byte[] { 0x00, 0x01, 0x02 }));

                Assert.Equal(SaveFileHash.ZipHexFile(forward), SaveFileHash.ZipHexFile(reverse));
            }
            finally
            {
                File.Delete(forward);
                File.Delete(reverse);
            }
        }

        // Reporting local state during negotiate hashes the folder directly rather than building a
        // throwaway archive, so the two paths have to agree -- including the '/' separator, which
        // a Windows path would otherwise contribute as '\'.
        [Fact]
        public void FolderAsZipHex_matches_the_archive_it_would_produce()
        {
            var work = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var folder = Path.Combine(work, "BESCES-53326nico");
            var zipPath = Path.Combine(work, "bundle.zip");
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, "nested"));
                File.WriteAllBytes(Path.Combine(folder, "icon.sys"), new byte[] { 0x01, 0x02 });
                File.WriteAllBytes(Path.Combine(folder, "nested", "data.bin"), new byte[] { 0x03 });

                WriteZip(zipPath,
                    new ZipEntrySpec("BESCES-53326nico/icon.sys", new byte[] { 0x01, 0x02 }),
                    new ZipEntrySpec("BESCES-53326nico/nested/data.bin", new byte[] { 0x03 }));

                Assert.Equal(SaveFileHash.ZipHexFile(zipPath), SaveFileHash.FolderAsZipHex(folder));
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        private class ZipEntrySpec
        {
            public ZipEntrySpec(string name, byte[] content)
            {
                Name = name;
                Content = content;
            }

            public string Name { get; }
            public byte[] Content { get; }
        }

        private static void WriteZip(string path, params ZipEntrySpec[] entries)
        {
            using (var archive = ZipArchive.Create())
            {
                foreach (var entry in entries)
                    archive.AddEntry(entry.Name, new MemoryStream(entry.Content), closeStream: true);

                using (var stream = File.OpenWrite(path))
                    archive.SaveTo(stream, new WriterOptions(CompressionType.Deflate));
            }
        }
    }
}
