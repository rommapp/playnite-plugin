using Playnite.SDK.Models;
using RomM.Saves;
using RomM.Saves.Handlers;
using System;
using System.Collections.Generic;
using Xunit;

namespace RomM.Tests
{
    public class SaveEmulatorResolverTests
    {
        private static readonly SaveHandlerRegistry Handlers = new SaveHandlerRegistry();

        private static Emulator RetroArch(Guid? id = null) =>
            new Emulator { Id = id ?? Guid.NewGuid(), Name = "RetroArch" };

        private static Emulator Dolphin() =>
            new Emulator { Id = Guid.NewGuid(), Name = "Dolphin" };

        private static SaveEmulatorCandidate Candidate(SaveEmulatorSource source, Emulator emulator, EmulatorProfile profile = null) =>
            new SaveEmulatorCandidate { Source = source, Emulator = emulator, Profile = profile };

        // The action a user repointed at another supported emulator is their choice, and outranks
        // whatever the platform mapping says.
        [Fact]
        public void The_play_action_wins_when_its_emulator_is_supported()
        {
            var fromAction = RetroArch();

            var resolution = SaveEmulatorResolver.Resolve(Handlers, new[]
            {
                Candidate(SaveEmulatorSource.PlayAction, fromAction),
                Candidate(SaveEmulatorSource.Mapping, RetroArch()),
            });

            Assert.Same(fromAction, resolution.Emulator);
            Assert.Equal(SaveEmulatorSource.PlayAction, resolution.Source);
            Assert.Equal(SaveEmulatorProblem.None, resolution.Problem);
            Assert.NotNull(resolution.Handler);
        }

        // The reported bug: the action is a snapshot from import, so a mapping later repointed at
        // RetroArch has to be consulted rather than the sync giving up on the stale emulator.
        [Fact]
        public void The_mapping_is_used_when_the_play_actions_emulator_is_unsupported()
        {
            var fromMapping = RetroArch();

            var resolution = SaveEmulatorResolver.Resolve(Handlers, new[]
            {
                Candidate(SaveEmulatorSource.PlayAction, Dolphin()),
                Candidate(SaveEmulatorSource.Mapping, fromMapping),
            });

            Assert.Same(fromMapping, resolution.Emulator);
            Assert.Equal(SaveEmulatorSource.Mapping, resolution.Source);
            Assert.Equal(SaveEmulatorProblem.None, resolution.Problem);
        }

        // Reported as unsupported rather than as "no emulator": the two need different advice, and
        // the emulator named is the one the user would go looking for.
        [Fact]
        public void No_supported_candidate_reports_the_first_emulator_as_unsupported()
        {
            var resolution = SaveEmulatorResolver.Resolve(Handlers, new[]
            {
                Candidate(SaveEmulatorSource.PlayAction, Dolphin()),
                Candidate(SaveEmulatorSource.Mapping, new Emulator { Id = Guid.NewGuid(), Name = "PCSX2" }),
            });

            Assert.Equal(SaveEmulatorProblem.Unsupported, resolution.Problem);
            Assert.Equal("Dolphin", resolution.UnsupportedEmulatorName);
            Assert.Null(resolution.Handler);
        }

        [Fact]
        public void Candidates_without_an_emulator_are_reported_as_none_set()
        {
            var resolution = SaveEmulatorResolver.Resolve(Handlers, new[]
            {
                Candidate(SaveEmulatorSource.PlayAction, null),
                Candidate(SaveEmulatorSource.Mapping, null),
                null,
            });

            Assert.Equal(SaveEmulatorProblem.NoEmulator, resolution.Problem);
            Assert.Null(resolution.Emulator);
        }

        [Fact]
        public void No_candidates_at_all_are_reported_as_none_set()
        {
            Assert.Equal(SaveEmulatorProblem.NoEmulator,
                SaveEmulatorResolver.Resolve(Handlers, null).Problem);
        }

        // An action can name an emulator without naming a profile. For RetroArch the profile is the
        // core, and the core is a folder in the save path, so the mapping's profile for that same
        // emulator is better than none.
        [Fact]
        public void A_profile_is_borrowed_from_another_candidate_for_the_same_emulator()
        {
            var id = Guid.NewGuid();
            var profile = new BuiltInEmulatorProfile { Name = "mGBA" };

            var resolution = SaveEmulatorResolver.Resolve(Handlers, new[]
            {
                Candidate(SaveEmulatorSource.PlayAction, RetroArch(id)),
                Candidate(SaveEmulatorSource.Mapping, RetroArch(id), profile),
            });

            Assert.Equal(SaveEmulatorSource.PlayAction, resolution.Source);
            Assert.Same(profile, resolution.Profile);
        }

        // A profile belongs to the emulator it was defined on; borrowing across emulators would
        // name a core that emulator never runs.
        [Fact]
        public void A_profile_is_not_borrowed_from_a_different_emulator()
        {
            var resolution = SaveEmulatorResolver.Resolve(Handlers, new[]
            {
                Candidate(SaveEmulatorSource.PlayAction, RetroArch()),
                Candidate(SaveEmulatorSource.Mapping, RetroArch(), new BuiltInEmulatorProfile { Name = "mGBA" }),
            });

            Assert.Null(resolution.Profile);
        }

        [Fact]
        public void The_candidates_own_profile_is_kept()
        {
            var id = Guid.NewGuid();
            var own = new BuiltInEmulatorProfile { Name = "Dolphin - GC/Wii" };

            var resolution = SaveEmulatorResolver.Resolve(Handlers, new List<SaveEmulatorCandidate>
            {
                Candidate(SaveEmulatorSource.PlayAction, RetroArch(id), own),
                Candidate(SaveEmulatorSource.Mapping, RetroArch(id), new BuiltInEmulatorProfile { Name = "mGBA" }),
            });

            Assert.Same(own, resolution.Profile);
        }
    }
}
