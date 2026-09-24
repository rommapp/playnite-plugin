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

        [Theory]
        // A folder fetched whole for a ROM RomM still calls single-file keeps a folder of its own:
        // its patch/ and dlc/ must not be scattered across the folder every game on the platform
        // shares, where a flat uninstall would then leave them behind.
        [InlineData(true, true, false, false)]
        // A single file installs flat, which is what the setting is for.
        [InlineData(true, false, false, true)]
        // ROMs RomM itself calls multi-file stay on the flat path they already took.
        [InlineData(true, true, true, true)]
        // Nothing goes flat when the mapping does not ask for it.
        [InlineData(false, true, false, false)]
        [InlineData(false, false, false, false)]
        [InlineData(false, true, true, false)]
        public void Flat_layout_makes_an_exception_for_ROMs_fetched_as_a_folder(
            bool installFlat, bool downloadAsArchive, bool hasMultipleFiles, bool expected)
            => Assert.Equal(
                expected,
                RomMInstallPaths.UsesFlatLayout(installFlat, downloadAsArchive, hasMultipleFiles));

        [Fact]
        public void A_directory_under_the_destination_belongs_to_the_game()
        {
            var destination = Path.Combine(Path.GetTempPath(), "roms", "switch");

            Assert.True(RomMInstallPaths.IsInside(destination, Path.Combine(destination, "Sample Game")));
            Assert.True(RomMInstallPaths.IsInside(
                destination + Path.DirectorySeparatorChar, Path.Combine(destination, "Sample Game")));
            Assert.True(RomMInstallPaths.IsInside(
                destination.ToUpperInvariant(), Path.Combine(destination.ToLowerInvariant(), "Sample Game")));
        }

        [Fact]
        public void The_destination_itself_is_not_inside_it()
        {
            // A flat install lives in the folder every game on the platform shares, so uninstall has
            // to remove its files and leave the folder alone.
            var destination = Path.Combine(Path.GetTempPath(), "roms", "switch");

            Assert.False(RomMInstallPaths.IsInside(destination, destination));
            Assert.False(RomMInstallPaths.IsInside(destination, destination + Path.DirectorySeparatorChar));
        }

        [Fact]
        public void A_stale_install_dir_is_not_inside_the_current_destination()
        {
            // The mapping was repointed after the game was installed: the recorded path is the
            // previous platform folder, and deleting that would take every ROM still in it.
            var previous = Path.Combine(Path.GetTempPath(), "previous", "switch");
            var current = Path.Combine(Path.GetTempPath(), "current", "switch");

            Assert.False(RomMInstallPaths.IsInside(current, previous));
            Assert.False(RomMInstallPaths.IsInside(current, Path.Combine(previous, "Sample Game")));
        }

        [Theory]
        [InlineData(null, "anything")]
        [InlineData("anything", null)]
        [InlineData("", "anything")]
        [InlineData("anything", "")]
        public void IsInside_is_false_when_a_path_is_missing(string root, string path)
            => Assert.False(RomMInstallPaths.IsInside(root, path));

        [Fact]
        public void An_old_flat_folder_under_a_repointed_parent_is_shared()
        {
            // The mapping moved from roms/snes (flat) to its parent roms: the old platform folder now
            // sits inside the destination, but every other flat SNES game still records it as theirs.
            var parent = Path.Combine(Path.GetTempPath(), "roms");
            var oldFlat = Path.Combine(parent, "snes");

            Assert.True(RomMInstallPaths.IsInside(parent, oldFlat));
            Assert.True(RomMInstallPaths.IsClaimedByAnother(oldFlat, new[] { oldFlat + Path.DirectorySeparatorChar }));
        }

        [Fact]
        public void A_folder_holding_another_games_install_is_shared()
        {
            var dir = Path.Combine(Path.GetTempPath(), "roms", "snes");

            Assert.True(RomMInstallPaths.IsClaimedByAnother(dir, new[] { Path.Combine(dir, "Other Game") }));
        }

        [Fact]
        public void A_games_own_folder_is_not_shared()
        {
            var root = Path.Combine(Path.GetTempPath(), "roms", "snes");
            var own = Path.Combine(root, "Sample Game");

            Assert.False(RomMInstallPaths.IsClaimedByAnother(own, new[] { root, Path.Combine(root, "Other Game"), null, "" }));
        }
    }
}
