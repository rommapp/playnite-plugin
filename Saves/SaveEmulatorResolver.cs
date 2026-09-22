using Playnite.SDK.Models;
using RomM.Saves.Handlers;
using System.Collections.Generic;
using System.Linq;

namespace RomM.Saves
{
    /// <summary>Where a candidate emulator came from, for logging and messages.</summary>
    internal enum SaveEmulatorSource
    {
        None = 0,
        PlayAction = 1,
        Mapping = 2,
    }

    /// <summary>Why no emulator could be used, when none could.</summary>
    internal enum SaveEmulatorProblem
    {
        None = 0,

        /// <summary>Neither the play action nor the mapping names an emulator.</summary>
        NoEmulator = 1,

        /// <summary>An emulator is set, but no handler knows where it keeps saves.</summary>
        Unsupported = 2,
    }

    internal class SaveEmulatorCandidate
    {
        public SaveEmulatorSource Source { get; set; }
        public Emulator Emulator { get; set; }
        public EmulatorProfile Profile { get; set; }
    }

    internal class SaveEmulatorResolution
    {
        public Emulator Emulator { get; set; }
        public EmulatorProfile Profile { get; set; }
        public ISaveHandler Handler { get; set; }
        public SaveEmulatorSource Source { get; set; }
        public SaveEmulatorProblem Problem { get; set; }

        /// <summary>The emulator that is set but unsupported, for the message shown to the user.</summary>
        public string UnsupportedEmulatorName { get; set; }
    }

    /// <summary>
    /// Picks the emulator a game's saves belong to, from the candidates in preference order.
    ///
    /// The play action leads: a user who repoints it at another emulator should have their saves
    /// follow it. But the action is only a snapshot of the mapping taken at import, so when it
    /// names an emulator no handler covers, the platform's own emulator mapping -- which the
    /// settings screen presents as the thing that decides this -- gets its turn before sync gives
    /// up. Kept free of Playnite lookups so the preference order can be tested on its own.
    /// </summary>
    internal static class SaveEmulatorResolver
    {
        public static SaveEmulatorResolution Resolve(SaveHandlerRegistry handlers, IEnumerable<SaveEmulatorCandidate> candidates)
        {
            var known = (candidates ?? Enumerable.Empty<SaveEmulatorCandidate>())
                .Where(c => c != null && c.Emulator != null)
                .ToList();

            if (known.Count == 0)
                return new SaveEmulatorResolution { Problem = SaveEmulatorProblem.NoEmulator };

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
                    Source = candidate.Source,
                };
            }

            return new SaveEmulatorResolution
            {
                Problem = SaveEmulatorProblem.Unsupported,
                UnsupportedEmulatorName = known[0].Emulator.Name,
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
