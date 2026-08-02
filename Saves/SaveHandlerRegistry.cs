using Playnite.SDK.Models;
using System.Collections.Generic;
using System.Linq;

namespace RomM.Saves
{
    /// <summary>
    /// Picks the handler that knows a given emulator. The single place that has to change when a
    /// platform is added, so no call site grows an emulator check.
    /// </summary>
    internal class SaveHandlerRegistry
    {
        private readonly IList<ISaveHandler> _handlers;

        public SaveHandlerRegistry()
            : this(new ISaveHandler[]
            {
                new RetroArchSaveHandler(),
            })
        {
        }

        public SaveHandlerRegistry(IEnumerable<ISaveHandler> handlers)
        {
            _handlers = handlers.ToList();
        }

        /// <summary>The handler for this emulator, or null when none of them recognises it.</summary>
        public ISaveHandler Find(Emulator emulator)
        {
            return emulator == null ? null : _handlers.FirstOrDefault(h => h.CanHandle(emulator));
        }

        /// <summary>Names of the emulators supported today, for user-facing messages.</summary>
        public IEnumerable<string> SupportedEmulatorTags => _handlers.Select(h => h.EmulatorTag);
    }
}
