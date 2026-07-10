using RomM.Models.RomM.Platform;
using Xunit;

namespace RomM.Tests
{
    public class RomMPlatformTests
    {
        [Fact]
        public void Equal_when_names_match_regardless_of_id()
        {
            var a = new RomMPlatform { Name = "Game Boy Advance", Id = 1 };
            var b = new RomMPlatform { Name = "Game Boy Advance", Id = 2 };

            Assert.True(a.Equals(b));
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Not_equal_when_names_differ()
        {
            var a = new RomMPlatform { Name = "Game Boy Advance" };
            var b = new RomMPlatform { Name = "Super Nintendo" };

            Assert.False(a.Equals(b));
            Assert.True(a != b);
        }

        [Fact]
        public void Null_name_yields_zero_hashcode_and_equals_other_null_name()
        {
            var a = new RomMPlatform { Name = null };
            var b = new RomMPlatform { Name = null };

            Assert.Equal(0, a.GetHashCode());
            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Comparisons_with_null_are_safe()
        {
            var a = new RomMPlatform { Name = "Game Boy Advance" };

            Assert.False(a == null);
            Assert.True(a != null);
            Assert.False(a.Equals((object)null));
            Assert.False(a.Equals((RomMPlatform)null));
        }

        [Fact]
        public void PlayniteName_prefers_display_name_when_available()
        {
            var platform = new RomMPlatform
            {
                Name = "gba",
                DisplayName = "Game Boy Advance",
            };

            Assert.Equal("Game Boy Advance", platform.PlayniteName);
        }

        [Fact]
        public void PlayniteName_falls_back_to_name_when_display_name_missing()
        {
            var platform = new RomMPlatform
            {
                Name = "Nintendo DS",
                DisplayName = "",
            };

            Assert.Equal("Nintendo DS", platform.PlayniteName);
        }
    }
}
