using System;
using System.IO;
using System.Text;
using RomM.Saves;
using Xunit;

namespace RomM.Tests
{
    public class FileSaveTargetTests : IDisposable
    {
        private readonly string _dir;

        public FileSaveTargetTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string Path_(string name) => Path.Combine(_dir, name);

        [Fact]
        public void Reports_absent_when_no_save_has_been_written_yet()
        {
            var target = new FileSaveTarget("retroarch", Path_("game.srm"), null);

            Assert.False(target.Exists);
        }

        [Fact]
        public void Reports_the_file_it_found_rather_than_the_configured_path()
        {
            var existing = Path_("sorted-by-core.srm");
            File.WriteAllText(existing, "abc");

            var target = new FileSaveTarget("retroarch", Path_("game.srm"), existing);

            Assert.True(target.Exists);
            Assert.Equal("sorted-by-core.srm", target.FileName);
            Assert.Equal("900150983cd24fb0d6963f7d28e17f72", target.ContentHash());
        }

        // A single file goes to the server untouched -- no archive, nothing to clean up afterwards.
        [Fact]
        public void Uploads_the_file_in_place_without_a_temporary_copy()
        {
            var path = Path_("game.srm");
            File.WriteAllText(path, "abc");

            var target = new FileSaveTarget("retroarch", path, null);
            using (var prepared = target.PrepareUpload())
            {
                Assert.False(prepared.IsTemporary);
                Assert.Equal(path, prepared.FilePath);
                Assert.Equal("game.srm", prepared.FileName);
            }

            Assert.True(File.Exists(path));
        }

        [Fact]
        public void Creates_the_directory_when_downloading_a_first_save()
        {
            var path = Path.Combine(_dir, "saves", "nested", "game.srm");
            var target = new FileSaveTarget("retroarch", path, null);

            target.ApplyDownload(Encoding.ASCII.GetBytes("abc"), null);

            Assert.Equal("abc", File.ReadAllText(path));
        }

        // The emulator may keep its save somewhere the configured layout would not predict. A
        // download has to land on the file it actually reads, or the game silently keeps loading
        // the old data.
        [Fact]
        public void Overwrites_the_discovered_file_rather_than_the_configured_path()
        {
            var configured = Path_("game.srm");
            var existing = Path_("sorted-by-core.srm");
            File.WriteAllText(existing, "old");

            var target = new FileSaveTarget("retroarch", configured, existing);
            target.ApplyDownload(Encoding.ASCII.GetBytes("new"), null);

            Assert.Equal("new", File.ReadAllText(existing));
            Assert.False(File.Exists(configured));
        }

        // Negotiate compares timestamps, so a download that leaves "now" on the file would look
        // like a local edit on the next sync and bounce straight back up.
        [Fact]
        public void Stamps_the_downloaded_file_with_the_server_time()
        {
            var path = Path_("game.srm");
            var serverTime = new DateTime(2024, 5, 17, 9, 30, 0, DateTimeKind.Utc);

            var target = new FileSaveTarget("retroarch", path, null);
            target.ApplyDownload(Encoding.ASCII.GetBytes("abc"), serverTime);

            Assert.Equal(serverTime, target.UpdatedAtUtc);
        }
    }
}
