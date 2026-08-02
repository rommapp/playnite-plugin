using Playnite.SDK.Models;
using RomM.Saves;
using Xunit;

namespace RomM.Tests
{
    public class SaveHandlerRegistryTests
    {
        [Fact]
        public void Find_returns_the_retroarch_handler_for_a_builtin_retroarch_emulator()
        {
            var registry = new SaveHandlerRegistry();

            var handler = registry.Find(new Emulator { BuiltInConfigId = "retroarch" });

            Assert.NotNull(handler);
            Assert.Equal("retroarch", handler.EmulatorTag);
        }

        // Manually configured emulators carry no BuiltInConfigId, so the name is the next signal.
        [Theory]
        [InlineData("RetroArch")]
        [InlineData("retroarch (64-bit)")]
        [InlineData("My RETROARCH build")]
        public void Find_recognises_retroarch_by_name(string name)
        {
            var registry = new SaveHandlerRegistry();

            Assert.NotNull(registry.Find(new Emulator { Name = name }));
        }

        // An unknown emulator has to come back empty rather than fall through to a handler that
        // would resolve the wrong path and overwrite an unrelated save.
        [Fact]
        public void Find_returns_null_for_an_emulator_no_handler_knows()
        {
            var registry = new SaveHandlerRegistry();

            Assert.Null(registry.Find(new Emulator { Name = "PCSX2", BuiltInConfigId = "pcsx2" }));
        }

        [Fact]
        public void Find_returns_null_for_a_missing_emulator()
        {
            Assert.Null(new SaveHandlerRegistry().Find(null));
        }

        [Fact]
        public void Find_uses_the_handlers_it_was_given()
        {
            var registry = new SaveHandlerRegistry(new ISaveHandler[] { new StubHandler() });

            Assert.Equal("stub", registry.Find(new Emulator { Name = "anything" })?.EmulatorTag);
        }

        private class StubHandler : ISaveHandler
        {
            public string EmulatorTag => "stub";
            public bool CanHandle(Emulator emulator) => true;
            public SaveTarget ResolveTarget(SaveTargetRequest request) => null;
        }
    }
}
