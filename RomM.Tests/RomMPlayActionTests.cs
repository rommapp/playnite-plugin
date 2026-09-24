using Playnite.SDK.Models;
using RomM.Games;
using System;
using System.Collections.Generic;
using Xunit;

namespace RomM.Tests
{
    public class RomMPlayActionTests
    {
        private static readonly Guid Mapped = Guid.NewGuid();
        private static readonly Guid Other = Guid.NewGuid();

        private static AppliedPlayAction Applied(Guid emulatorId, string profileId) =>
            new AppliedPlayAction(emulatorId, profileId);

        [Fact]
        public void Build_produces_the_importers_play_action()
        {
            var action = RomMPlayAction.Build("RetroArch", Mapped, "profile-1");

            Assert.Equal("Play in RetroArch", action.Name);
            Assert.Equal(GameActionType.Emulator, action.Type);
            Assert.Equal(Mapped, action.EmulatorId);
            Assert.Equal("profile-1", action.EmulatorProfileId);
            Assert.True(action.IsPlayAction);
        }

        // Repointing an existing action has to leave it identical to a freshly imported one, which
        // is why both go through the same writer.
        [Fact]
        public void Apply_rewrites_an_existing_action_into_the_importers_shape()
        {
            var action = new GameAction { Name = "Play in Dolphin", Type = GameActionType.URL, EmulatorId = Other };

            RomMPlayAction.Apply(action, "RetroArch", Mapped, "profile-1");

            Assert.Equal("Play in RetroArch", action.Name);
            Assert.Equal(GameActionType.Emulator, action.Type);
            Assert.Equal(Mapped, action.EmulatorId);
            Assert.Equal("profile-1", action.EmulatorProfileId);
            Assert.True(action.IsPlayAction);
        }

        [Fact]
        public void Find_prefers_the_emulator_action_marked_as_the_play_action()
        {
            var play = new GameAction { Type = GameActionType.Emulator, IsPlayAction = true, Name = "play" };

            var found = RomMPlayAction.Find(new List<GameAction>
            {
                new GameAction { Type = GameActionType.URL, Name = "View in RomM" },
                new GameAction { Type = GameActionType.Emulator, Name = "secondary" },
                play,
            });

            Assert.Same(play, found);
        }

        // An emulator action that isn't flagged as the play action still tells us which emulator
        // the game runs on, which is all save sync needs.
        [Fact]
        public void Find_falls_back_to_any_emulator_action()
        {
            var action = new GameAction { Type = GameActionType.Emulator, Name = "secondary" };

            Assert.Same(action, RomMPlayAction.Find(new[]
            {
                new GameAction { Type = GameActionType.URL, IsPlayAction = true },
                action,
            }));
        }

        [Fact]
        public void Find_returns_null_without_an_emulator_action()
        {
            Assert.Null(RomMPlayAction.Find(new[] { new GameAction { Type = GameActionType.URL } }));
            Assert.Null(RomMPlayAction.Find(null));
        }

        // Playnite writes an unset profile as null or "" depending on where it came from.
        [Theory]
        [InlineData(null, "")]
        [InlineData("", null)]
        [InlineData("p", "p")]
        public void Matches_treats_an_unset_profile_the_same_either_way(string actionProfile, string mappedProfile)
        {
            var action = new GameAction { EmulatorId = Mapped, EmulatorProfileId = actionProfile };

            Assert.True(RomMPlayAction.Matches(action, Applied(Mapped, mappedProfile)));
        }

        [Fact]
        public void Matches_is_false_for_another_emulator_or_profile()
        {
            var action = new GameAction { EmulatorId = Mapped, EmulatorProfileId = "p" };

            Assert.False(RomMPlayAction.Matches(action, Applied(Other, "p")));
            Assert.False(RomMPlayAction.Matches(action, Applied(Mapped, "q")));
            Assert.False(RomMPlayAction.Matches(null, Applied(Mapped, "p")));
        }

        [Fact]
        public void An_action_still_carrying_what_the_plugin_applied_is_unedited()
        {
            var action = new GameAction { Name = "anything", IsPlayAction = true, EmulatorId = Mapped, EmulatorProfileId = "p" };

            Assert.True(RomMPlayAction.IsUnedited(action, Applied(Mapped, "p"), () => "Dolphin"));
        }

        // Once the user has repointed the action, the recorded applied emulator no longer matches
        // and the action is theirs -- a later mapping change must not take it back.
        [Fact]
        public void An_action_repointed_by_the_user_is_not_unedited()
        {
            var action = new GameAction { Name = "Play in Dolphin", IsPlayAction = true, EmulatorId = Other, EmulatorProfileId = "q" };

            Assert.False(RomMPlayAction.IsUnedited(action, Applied(Mapped, "p"), () => "Dolphin"));
        }

        // Sidecars from before the plugin recorded what it applied: the generated name is the only
        // marker that the importer wrote the action and nobody has touched it since.
        [Fact]
        public void Without_a_recorded_emulator_the_generated_name_marks_the_action_as_the_plugins()
        {
            var action = new GameAction { Name = "Play in Dolphin", IsPlayAction = true, EmulatorId = Other };

            Assert.True(RomMPlayAction.IsUnedited(action, Applied(Guid.Empty, null), () => "Dolphin"));
        }

        [Theory]
        [InlineData("Play with Dolphin", "Dolphin")]
        [InlineData("Play in Dolphin", "RetroArch")]
        [InlineData("Play in Dolphin", null)]
        public void Without_a_recorded_emulator_anything_but_the_generated_name_is_left_alone(string name, string emulatorName)
        {
            var action = new GameAction { Name = name, IsPlayAction = true, EmulatorId = Other };

            Assert.False(RomMPlayAction.IsUnedited(action, Applied(Guid.Empty, null), () => emulatorName));
        }

        // The emulator name costs a database lookup, so it is only resolved on the legacy path that
        // actually needs it.
        [Fact]
        public void The_emulator_name_is_not_resolved_when_the_sidecar_records_what_we_applied()
        {
            var action = new GameAction { Name = "Play in Dolphin", IsPlayAction = true, EmulatorId = Mapped, EmulatorProfileId = "p" };
            var resolved = false;

            RomMPlayAction.IsUnedited(action, Applied(Mapped, "p"), () => { resolved = true; return "Dolphin"; });

            Assert.False(resolved);
        }

        // The importer only writes a play action. One the user demoted is theirs: repointing it
        // would mark it as a play action again beside the one they chose.
        [Fact]
        public void An_action_no_longer_marked_as_the_play_action_is_not_unedited()
        {
            var action = new GameAction { Name = "Play in Dolphin", IsPlayAction = false, EmulatorId = Mapped, EmulatorProfileId = "p" };

            Assert.False(RomMPlayAction.IsUnedited(action, Applied(Mapped, "p"), () => "Dolphin"));
            Assert.False(RomMPlayAction.IsUnedited(action, Applied(Guid.Empty, null), () => "Dolphin"));
        }

        // Repointing keeps every field the importer does not own, so arguments the user added for
        // one emulator would be passed to the next.
        [Fact]
        public void An_action_with_argument_overrides_is_not_unedited()
        {
            var additional = new GameAction { IsPlayAction = true, EmulatorId = Mapped, EmulatorProfileId = "p", AdditionalArguments = "--fullscreen" };
            var overridden = new GameAction { IsPlayAction = true, EmulatorId = Mapped, EmulatorProfileId = "p", OverrideDefaultArgs = true };

            Assert.False(RomMPlayAction.IsUnedited(additional, Applied(Mapped, "p"), () => "Dolphin"));
            Assert.False(RomMPlayAction.IsUnedited(overridden, Applied(Mapped, "p"), () => "Dolphin"));
        }

        [Fact]
        public void A_missing_action_is_never_unedited()
        {
            Assert.False(RomMPlayAction.IsUnedited(null, Applied(Mapped, "p"), () => "RetroArch"));
        }
    }
}
