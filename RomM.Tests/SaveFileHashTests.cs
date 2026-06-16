using System.IO;
using System.Text;
using RomM.Saves;
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
    }
}
