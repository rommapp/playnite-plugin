using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RomM.Saves
{
    /// <summary>
    /// Computes the content hash RomM uses to compare saves. The server hashes save files with
    /// MD5 (hex digest of the raw bytes), so the client must match that exactly or negotiate will
    /// never report "no_op" / will misclassify identical files. See romm assets_handler.
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
    }
}
