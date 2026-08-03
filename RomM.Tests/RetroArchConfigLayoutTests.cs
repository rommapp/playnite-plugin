using System.Collections.Generic;
using RomM.Saves;
using Xunit;

namespace RomM.Tests
{
    /// <summary>
    /// Save layouts checked against RetroArch itself rather than against the option names, which
    /// are misleading in two places: sorting "by content" keys on the ROM's folder rather than the
    /// ROM, and it wraps around the per-core folder instead of nesting inside it.
    /// </summary>
    public class RetroArchConfigLayoutTests
    {
        private const string Rom = @"D:\Retrogames\gba\Advance Wars.gba";

        private static Dictionary<string, string> Cfg(params string[] pairs)
        {
            var cfg = new Dictionary<string, string> { { "savefile_directory", @"D:\RetroArch\saves" } };
            for (int i = 0; i < pairs.Length; i += 2)
                cfg[pairs[i]] = pairs[i + 1];
            return cfg;
        }

        [Fact]
        public void Plain_layout_puts_the_save_in_the_configured_directory()
        {
            Assert.Equal(@"D:\RetroArch\saves\Advance Wars.srm",
                RetroArchConfig.ResolveSaveFilePath(Cfg(), Rom));
        }

        [Fact]
        public void Sorting_by_core_adds_the_core_folder()
        {
            Assert.Equal(@"D:\RetroArch\saves\mGBA\Advance Wars.srm",
                RetroArchConfig.ResolveSaveFilePath(Cfg("sort_savefiles_enable", "true"), Rom, "mGBA"));
        }

        // Verified against RetroArch: the folder is named after the ROM's parent directory ("gba"),
        // not after the ROM.
        [Fact]
        public void Sorting_by_content_uses_the_roms_parent_folder_name()
        {
            Assert.Equal(@"D:\RetroArch\saves\gba\Advance Wars.srm",
                RetroArchConfig.ResolveSaveFilePath(Cfg("sort_savefiles_by_content_enable", "true"), Rom));
        }

        // …and the core folder sits inside the content folder, not the other way round.
        [Fact]
        public void Content_sorting_wraps_around_the_core_folder()
        {
            var cfg = Cfg("sort_savefiles_by_content_enable", "true", "sort_savefiles_enable", "true");

            Assert.Equal(@"D:\RetroArch\saves\gba\mGBA\Advance Wars.srm",
                RetroArchConfig.ResolveSaveFilePath(cfg, Rom, "mGBA"));
        }

        // savefiles_in_content_dir wins over a configured savefile_directory rather than only
        // filling in when it is empty.
        [Fact]
        public void Saves_in_content_dir_override_the_configured_directory()
        {
            Assert.Equal(@"D:\Retrogames\gba\Advance Wars.srm",
                RetroArchConfig.ResolveSaveFilePath(Cfg("savefiles_in_content_dir", "true"), Rom));
        }

        [Fact]
        public void Saves_in_content_dir_still_take_the_core_folder()
        {
            var cfg = Cfg("savefiles_in_content_dir", "true", "sort_savefiles_enable", "true");

            Assert.Equal(@"D:\Retrogames\gba\mGBA\Advance Wars.srm",
                RetroArchConfig.ResolveSaveFilePath(cfg, Rom, "mGBA"));
        }

        [Fact]
        public void Base_directory_follows_saves_in_content_dir_for_the_recursive_search()
        {
            Assert.Equal(@"D:\Retrogames\gba",
                RetroArchConfig.ResolveSaveBaseDirectory(Cfg("savefiles_in_content_dir", "true"), Rom));
        }

        [Fact]
        public void An_empty_savefile_directory_falls_back_to_the_content_directory()
        {
            var cfg = new Dictionary<string, string> { { "savefile_directory", "default" } };

            Assert.Equal(@"D:\Retrogames\gba\Advance Wars.srm",
                RetroArchConfig.ResolveSaveFilePath(cfg, Rom));
        }
    }
}
