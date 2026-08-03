using System.Collections.Generic;
using System.IO;
using RomM.Saves;
using Xunit;

namespace RomM.Tests
{
    public class RetroArchConfigTests
    {
        [Fact]
        public void Parse_strips_quotes_comments_and_whitespace()
        {
            var cfg = RetroArchConfig.Parse(string.Join("\n", new[]
            {
                "# a comment",
                "savefile_directory = \"C:\\saves\"",
                "sort_savefiles_enable = \"true\"",
                "   sort_savefiles_by_content_enable = false   ",
                "",
                "garbage line without separator",
            }));

            Assert.Equal("C:\\saves", cfg["savefile_directory"]);
            Assert.Equal("true", cfg["sort_savefiles_enable"]);
            Assert.Equal("false", cfg["sort_savefiles_by_content_enable"]);
            Assert.False(cfg.ContainsKey("garbage line without separator"));
        }

        [Fact]
        public void ResolveSaveFilePath_falls_back_to_content_directory_when_unset()
        {
            var content = Path.Combine("roms", "gba", "Game.gba");
            var cfg = new Dictionary<string, string>();

            var path = RetroArchConfig.ResolveSaveFilePath(cfg, content);

            Assert.Equal(Path.Combine("roms", "gba", "Game.srm"), path);
        }

        [Theory]
        [InlineData("")]
        [InlineData("default")]
        public void ResolveSaveFilePath_empty_or_default_uses_content_directory(string value)
        {
            var content = Path.Combine("roms", "Game.gba");
            var cfg = new Dictionary<string, string> { ["savefile_directory"] = value };

            var path = RetroArchConfig.ResolveSaveFilePath(cfg, content);

            Assert.Equal(Path.Combine("roms", "Game.srm"), path);
        }

        [Fact]
        public void ResolveSaveFilePath_uses_configured_directory()
        {
            var content = Path.Combine("roms", "Game.gba");
            var saveDir = Path.Combine("data", "saves");
            var cfg = new Dictionary<string, string> { ["savefile_directory"] = saveDir };

            var path = RetroArchConfig.ResolveSaveFilePath(cfg, content);

            Assert.Equal(Path.Combine(saveDir, "Game.srm"), path);
        }

        // "Content" here is the directory the ROM sits in, not the ROM itself — checked against
        // RetroArch, which puts a rom in roms\ under <saves>\roms\. See RetroArchConfigLayoutTests
        // for the full set of layouts.
        [Fact]
        public void ResolveSaveFilePath_sorts_by_content_when_enabled()
        {
            var content = Path.Combine("roms", "Game.gba");
            var saveDir = Path.Combine("data", "saves");
            var cfg = new Dictionary<string, string>
            {
                ["savefile_directory"] = saveDir,
                ["sort_savefiles_by_content_enable"] = "true",
            };

            var path = RetroArchConfig.ResolveSaveFilePath(cfg, content);

            Assert.Equal(Path.Combine(saveDir, "roms", "Game.srm"), path);
        }

        [Fact]
        public void ResolveSaveFilePath_sorts_by_core_only_when_core_known()
        {
            var content = Path.Combine("roms", "Game.gba");
            var saveDir = Path.Combine("data", "saves");
            var cfg = new Dictionary<string, string>
            {
                ["savefile_directory"] = saveDir,
                ["sort_savefiles_enable"] = "true",
            };

            // Unknown core -> no per-core subfolder (runtime search handles it instead).
            Assert.Equal(Path.Combine(saveDir, "Game.srm"),
                RetroArchConfig.ResolveSaveFilePath(cfg, content, coreName: null));

            // Known core -> per-core subfolder.
            Assert.Equal(Path.Combine(saveDir, "mgba_libretro", "Game.srm"),
                RetroArchConfig.ResolveSaveFilePath(cfg, content, coreName: "mgba_libretro"));
        }

        [Fact]
        public void ResolveSaveFilePath_expands_leading_colon_base_token()
        {
            var content = Path.Combine("roms", "Game.gba");
            var baseDir = Path.Combine("opt", "retroarch");
            var cfg = new Dictionary<string, string> { ["savefile_directory"] = ":\\saves" };

            var path = RetroArchConfig.ResolveSaveFilePath(cfg, content, retroArchBaseDir: baseDir);

            Assert.Equal(Path.Combine(baseDir, "saves", "Game.srm"), path);
        }

        [Fact]
        public void ResolveSaveFilePath_returns_null_without_content()
        {
            Assert.Null(RetroArchConfig.ResolveSaveFilePath(new Dictionary<string, string>(), null));
        }

        [Fact]
        public void ResolveSaveBaseDirectory_ignores_sorting_subfolders()
        {
            var content = Path.Combine("roms", "Game.gba");
            var saveDir = Path.Combine("data", "saves");
            var cfg = new Dictionary<string, string>
            {
                ["savefile_directory"] = saveDir,
                ["sort_savefiles_by_content_enable"] = "true",
            };

            Assert.Equal(saveDir, RetroArchConfig.ResolveSaveBaseDirectory(cfg, content));
        }
    }
}
