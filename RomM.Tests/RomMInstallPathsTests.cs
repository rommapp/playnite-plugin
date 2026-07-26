using System;
using System.IO;
using RomM.Games;
using Xunit;

namespace RomM.Tests
{
    public class RomMInstallPathsTests
    {
        private const string Root = "GAMES_ROOT";

        [Fact]
        public void InstallDir_is_root_plus_filename_without_extension()
        {
            Assert.Equal(
                Path.Combine(Root, "Advance Wars (USA)"),
                RomMInstallPaths.InstallDir(Root, "Advance Wars (USA).gba"));
        }

        [Fact]
        public void GamePath_is_install_dir_plus_full_filename()
        {
            const string fileName = "Advance Wars (USA).gba";
            Assert.Equal(
                Path.Combine(Root, "Advance Wars (USA)", fileName),
                RomMInstallPaths.GamePath(Root, fileName));
        }

        [Fact]
        public void Handles_filename_without_extension()
        {
            Assert.Equal(Path.Combine(Root, "game"), RomMInstallPaths.InstallDir(Root, "game"));
            Assert.Equal(Path.Combine(Root, "game", "game"), RomMInstallPaths.GamePath(Root, "game"));
        }

        [Fact]
        public void Folder_name_pins_install_dir_to_the_rom_folder()
        {
            // Nested single file: folder is the ROM name (fs_name), file carries a region tag.
            const string folder = "All-Star Baseball '99";
            const string file = "All-Star Baseball '99 (Europe).zip";

            Assert.Equal(
                Path.Combine(Root, folder),
                RomMInstallPaths.InstallDir(Root, folder, file));
            Assert.Equal(
                Path.Combine(Root, folder, file),
                RomMInstallPaths.GamePath(Root, folder, file));
        }

        [Theory]
        [InlineData("/etc")]
        [InlineData(@"C:\Windows")]
        [InlineData(@"\Windows")]
        [InlineData("../../elsewhere")]
        [InlineData(@"folder\..\..\elsewhere")]
        public void Rejects_paths_that_escape_the_install_root(string hostile)
        {
            Assert.False(RomMInstallPaths.IsContained(hostile));
            Assert.Throws<ArgumentException>(() => RomMInstallPaths.InstallDir(Root, hostile, "game.gba"));
            Assert.Throws<ArgumentException>(() => RomMInstallPaths.GamePath(Root, "folder", hostile));
            Assert.Throws<ArgumentException>(() => RomMInstallPaths.GamePath(Root, hostile));
        }

        [Fact]
        public void ResolveWithin_keeps_archive_entries_under_the_install_dir()
        {
            var installDir = Path.Combine(Root, "Final Fantasy VII");

            Assert.Equal(
                Path.GetFullPath(Path.Combine(installDir, "Disc 1", "disc1.bin")),
                RomMInstallPaths.ResolveWithin(installDir, "Disc 1/disc1.bin"));
        }

        [Theory]
        [InlineData("../evil.exe")]
        [InlineData("Disc 1/../../evil.exe")]
        [InlineData("/etc/evil")]
        [InlineData(@"C:\Windows\evil.exe")]
        [InlineData("")]
        public void ResolveWithin_rejects_entries_outside_the_install_dir(string entryKey)
        {
            Assert.Throws<ArgumentException>(
                () => RomMInstallPaths.ResolveWithin(Path.Combine(Root, "Final Fantasy VII"), entryKey));
        }

        [Fact]
        public void Allows_nested_relative_file_paths()
        {
            Assert.True(RomMInstallPaths.IsContained(Path.Combine("Disc 1", "disc1.bin")));
            Assert.True(RomMInstallPaths.IsContained("Advance Wars (USA).gba"));
        }

        [Fact]
        public void Null_or_empty_folder_name_falls_back_to_filename_derived_dir()
        {
            const string file = "Advance Wars (USA).gba";

            Assert.Equal(
                RomMInstallPaths.InstallDir(Root, file),
                RomMInstallPaths.InstallDir(Root, null, file));
            Assert.Equal(
                RomMInstallPaths.GamePath(Root, file),
                RomMInstallPaths.GamePath(Root, "", file));
        }
    }
}
