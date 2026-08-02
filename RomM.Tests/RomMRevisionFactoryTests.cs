using System.Collections.Generic;
using System.IO;
using RomM.Games;
using RomM.Models.RomM.Rom;
using Xunit;

namespace RomM.Tests
{
    public class RomMRevisionFactoryTests
    {
        private const string Host = "https://romm.example.com";

        [Fact]
        public void SelectPrimaryFile_picks_shallowest_path()
        {
            var files = new List<RomMFile>
            {
                new RomMFile { FileName = "deep.gba", FullPath = "a/b/c/deep.gba" },
                new RomMFile { FileName = "shallow.gba", FullPath = "a/shallow.gba" },
            };

            Assert.Equal("shallow.gba", RomMRevisionFactory.SelectPrimaryFile(files).FileName);
        }

        [Fact]
        public void SelectPrimaryFile_null_when_empty_or_null()
        {
            Assert.Null(RomMRevisionFactory.SelectPrimaryFile(new List<RomMFile>()));
            Assert.Null(RomMRevisionFactory.SelectPrimaryFile(null));
        }

        [Fact]
        public void RelativeFilePath_keeps_path_below_the_rom_folder()
        {
            var file = new RomMFile { FileName = "disc1.bin", FullPath = "roms/ps1/Final Fantasy VII/Disc 1/disc1.bin" };

            Assert.Equal(Path.Combine("Disc 1", "disc1.bin"),
                RomMRevisionFactory.RelativeFilePath(file, "Final Fantasy VII"));
        }

        [Fact]
        public void RelativeFilePath_falls_back_to_leaf_name()
        {
            var file = new RomMFile { FileName = "disc1.bin", FullPath = "roms/ps1/Other Folder/disc1.bin" };

            Assert.Equal("disc1.bin", RomMRevisionFactory.RelativeFilePath(file, "Final Fantasy VII"));
            Assert.Equal("disc1.bin", RomMRevisionFactory.RelativeFilePath(file, null));
            Assert.Null(RomMRevisionFactory.RelativeFilePath(null, "Final Fantasy VII"));
        }

        [Fact]
        public void Single_file_with_id_uses_files_content_endpoint()
        {
            var rom = new RomMRom
            {
                Id = 32,
                HasMultipleFiles = false,
                Files = new List<RomMFile> { new RomMFile { Id = 7, FileName = "game.gba", FullPath = "game.gba" } },
            };

            var rev = RomMRevisionFactory.Build(rom, Host);

            Assert.NotNull(rev);
            Assert.False(rev.HasMultipleFiles);
            Assert.Equal("game.gba", rev.FileName);
            Assert.Equal(Host + "/api/roms/7/files/content/game.gba", rev.DownloadURL);
        }

        [Fact]
        public void Simple_single_file_has_no_folder_name()
        {
            var rom = new RomMRom
            {
                Id = 32,
                HasSimpleSingleFile = true,
                HasMultipleFiles = false,
                FileName = "game.gba",
                Files = new List<RomMFile> { new RomMFile { Id = 7, FileName = "game.gba", FullPath = "game.gba" } },
            };

            var rev = RomMRevisionFactory.Build(rom, Host);

            Assert.Null(rev.FolderName);
        }

        [Fact]
        public void Nested_single_file_folder_name_is_the_rom_folder()
        {
            var rom = new RomMRom
            {
                Id = 33,
                HasNestedSingleFile = true,
                HasMultipleFiles = false,
                FileName = "All-Star Baseball '99",
                Files = new List<RomMFile>
                {
                    new RomMFile { Id = 8, FileName = "All-Star Baseball '99 (Europe).zip", FullPath = "All-Star Baseball '99/All-Star Baseball '99 (Europe).zip" },
                },
            };

            var rev = RomMRevisionFactory.Build(rom, Host);

            // File is the real inner file; folder is fs_name (the ROM folder).
            Assert.Equal("All-Star Baseball '99 (Europe).zip", rev.FileName);
            Assert.Equal("All-Star Baseball '99", rev.FolderName);
        }

        [Fact]
        public void Multi_file_folder_name_is_the_rom_folder()
        {
            var rom = new RomMRom
            {
                Id = 40,
                HasMultipleFiles = true,
                FileName = "1080 TenEighty Snowboarding",
                Files = new List<RomMFile>(),
            };

            var rev = RomMRevisionFactory.Build(rom, Host);

            Assert.Equal("1080 TenEighty Snowboarding", rev.FolderName);
        }

        [Fact]
        public void Single_file_without_id_falls_back_to_rom_endpoint()
        {
            var rom = new RomMRom
            {
                Id = 32,
                HasMultipleFiles = false,
                Files = new List<RomMFile> { new RomMFile { Id = null, FileName = "game.gba", FullPath = "game.gba" } },
            };

            var rev = RomMRevisionFactory.Build(rom, Host);

            Assert.Equal(Host + "/api/roms/32/content/game.gba", rev.DownloadURL);
        }

        [Fact]
        public void Single_file_returns_null_when_no_files()
        {
            var rom = new RomMRom { Id = 32, HasMultipleFiles = false, Files = new List<RomMFile>() };

            Assert.Null(RomMRevisionFactory.Build(rom, Host));
        }

        [Fact]
        public void Multi_file_uses_rom_content_endpoint()
        {
            var rom = new RomMRom
            {
                Id = 40,
                HasMultipleFiles = true,
                FileName = "Game (Disc).zip",
                Files = new List<RomMFile>(),
            };

            var rev = RomMRevisionFactory.Build(rom, Host);

            Assert.True(rev.HasMultipleFiles);
            Assert.Equal("Game (Disc).zip", rev.FileName);
            Assert.Equal(Host + "/api/roms/40/content/Game (Disc).zip", rev.DownloadURL);
        }
    }
}
