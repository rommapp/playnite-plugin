using Playnite.SDK.Models;
using RomM.Saves.Handlers;
using System.Collections.Generic;
using System.Linq;

namespace RomM.Saves
{
    internal class SaveEmulatorCandidate
    {
        /// <summary>Whether this candidate came from the platform mapping rather than the play action.</summary>
        public bool FromMapping { get; set; }

        public Emulator Emulator { get; set; }
        public EmulatorProfile Profile { get; set; }
    }

    /// <summary>
    /// The outcome of the pick. A null <see cref="Emulator"/> means no candidate named one at all;
    /// an emulator with a null <see cref="Handler"/> means one is set but no handler knows where it
    /// keeps its saves. The two need different advice, which is why they are distinguishable.
    /// </summary>
    internal class SaveEmulatorResolution
    {
        public Emulator Emulator { get; set; }
        public EmulatorProfile Profile { get; set; }
        public ISaveHandler Handler { get; set; }
        public bool FromMapping { get; set; }

        /// <summary>
        /// The mapping's emulator when it is supported but was passed over because the game launches
        /// another one. Set so the "unsupported" advice can point at it; never a sync target.
        /// </summary>
        public Emulator PassedOverMappingEmulator { get; set; }
    }

    /// <summary>
    /// Picks the emulator a game's saves belong to, from the candidates in preference order.
    ///
    /// The play action decides: it is what Playnite launches, so its emulator is the one that reads
    /// and writes the saves. Syncing another emulator's saves in its place would download where the
    /// launched emulator never reads and upload a save it never wrote, so a mapping naming a
    /// different emulator is never a fallback -- a stale action is fixed by the importer repointing
    /// it instead. The mapping only answers when the action names no emulator at all, or lends its
    /// profile to that same emulator. Kept free of Playnite lookups so the rules can be tested on
    /// their own.
    /// </summary>
    internal static class SaveEmulatorResolver
    {
        public static SaveEmulatorResolution Resolve(SaveHandlerRegistry handlers, IEnumerable<SaveEmulatorCandidate> candidates)
        {
            var known = (candidates ?? Enumerable.Empty<SaveEmulatorCandidate>())
                .Where(c => c != null && c.Emulator != null)
                .ToList();

            var launched = known.FirstOrDefault(c => !c.FromMapping)?.Emulator;
            Emulator passedOver = null;
            if (launched != null)
            {
                passedOver = known.FirstOrDefault(c => c.FromMapping && c.Emulator.Id != launched.Id
                                                       && handlers?.Find(c.Emulator) != null)?.Emulator;
                known = known.Where(c => c.Emulator.Id == launched.Id).ToList();
            }

            foreach (var candidate in known)
            {
                var handler = handlers?.Find(candidate.Emulator);
                if (handler == null)
                    continue;

                return new SaveEmulatorResolution
                {
                    Emulator = candidate.Emulator,
                    Profile = candidate.Profile ?? BorrowProfile(known, candidate),
                    Handler = handler,
                    FromMapping = candidate.FromMapping,
                };
            }

            // An emulator is set but unsupported. The first candidate is the one the user would go
            // looking for, so it is the one the message names.
            return new SaveEmulatorResolution
            {
                Emulator = known.FirstOrDefault()?.Emulator,
                PassedOverMappingEmulator = passedOver,
            };
        }

        /// <summary>
        /// A play action can name an emulator without naming a profile, and for RetroArch the
        /// profile is what identifies the core -- which is a folder in the save path when
        /// sort_savefiles_enable is on. Rather than resolve a path with the core folder missing,
        /// where a download would land somewhere RetroArch never reads, take the profile from
        /// another candidate for the same emulator.
        /// </summary>
        private static EmulatorProfile BorrowProfile(IEnumerable<SaveEmulatorCandidate> candidates, SaveEmulatorCandidate chosen)
        {
            return candidates
                .Where(c => c != chosen && c.Profile != null && c.Emulator.Id == chosen.Emulator.Id)
                .Select(c => c.Profile)
                .FirstOrDefault();
        }
    }
}
