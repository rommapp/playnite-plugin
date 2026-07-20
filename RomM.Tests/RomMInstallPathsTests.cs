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
