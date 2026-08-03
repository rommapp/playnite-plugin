using Playnite.SDK.Models;
using RomM.Saves;
using Xunit;

namespace RomM.Tests
{
    public class RetroArchSaveHandlerTests
    {
        // Playnite names its built-in RetroArch profiles after the core, and RetroArch names the
        // per-core save folder the same way, so the profile name is the value we want verbatim.
        [Fact]
        public void Core_name_of_a_builtin_profile_is_its_name()
        {
            var profile = new BuiltInEmulatorProfile { Name = "mGBA" };

            Assert.Equal("mGBA", RetroArchSaveHandler.ResolveCoreName(profile));
        }

        // A custom profile only carries the core in its libretro argument, as a dll path.
        [Theory]
        [InlineData("-L \"cores\\mgba_libretro.dll\" \"{ImagePath}\"", "mgba")]
        [InlineData("-L cores\\snes9x_libretro.dll \"{ImagePath}\"", "snes9x")]
        [InlineData("-f -L \"D:\\RetroArch\\cores\\gambatte_libretro.dll\"", "gambatte")]
        public void Core_name_of_a_custom_profile_comes_from_the_libretro_argument(string args, string expected)
        {
            var profile = new CustomEmulatorProfile { Arguments = args };

            Assert.Equal(expected, RetroArchSaveHandler.ResolveCoreName(profile));
        }

        // No core name means the per-core folder is simply left out of the path, which is the
        // behaviour that existed before — not a reason to fail resolution.
        [Theory]
        [InlineData("\"{ImagePath}\"")]
        [InlineData("")]
        [InlineData(null)]
        public void Core_name_is_null_when_the_arguments_carry_none(string args)
        {
            Assert.Null(RetroArchSaveHandler.ResolveCoreName(new CustomEmulatorProfile { Arguments = args }));
        }

        [Fact]
        public void Core_name_is_null_without_a_profile()
        {
            Assert.Null(RetroArchSaveHandler.ResolveCoreName(null));
        }

        [Fact]
        public void Builtin_profile_without_a_name_yields_null()
        {
            Assert.Null(RetroArchSaveHandler.ResolveCoreName(new BuiltInEmulatorProfile { Name = "  " }));
        }
    }
}
