using System.Security.Cryptography;
using System.Text;

namespace RomM.Games
{
    // Some newer platforms don't get a server-side hash, so we synthesise a stable one from the rom id
    // plus its base file name. Pure and deterministic so the game still gets a stable "romMId:sha1" id.
    internal static class RomMHash
    {
        public static string FallbackSha1Hex(int romId, string fileNameNoExt)
        {
            var toHash = $"{romId}{fileNameNoExt}";

            using (var sha1 = new SHA1Managed())
            {
                return ToHex(sha1.ComputeHash(Encoding.UTF8.GetBytes(toHash)));
            }
        }

        /// <summary>Lower-case hex, the spelling every hash this plugin exchanges is written in.</summary>
        public static string ToHex(byte[] hash)
        {
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                sb.Append(b.ToString("x2"));

            return sb.ToString();
        }
    }
}
