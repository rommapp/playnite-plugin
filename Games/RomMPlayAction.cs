using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RomM.Games
{
    /// <summary>
    /// The emulator and profile the importer last wrote onto a game's play action, as recorded in
    /// the ROM's sidecar. An empty emulator id is what a sidecar written before the plugin kept
    /// this record yields.
    /// </summary>
    internal struct AppliedPlayAction
    {
        public readonly Guid EmulatorId;
        public readonly string ProfileId;

        public AppliedPlayAction(Guid emulatorId, string profileId)
        {
            EmulatorId = emulatorId;
            ProfileId = profileId;
        }
    }

    /// <summary>
    /// The emulator play action the importer writes onto every imported game, and the rules for
    /// deciding whether a later import may repoint it.
    ///
    /// The action is a snapshot of the emulator mapping taken at import time. Re-imports skip games
    /// that already exist, so a mapping later pointed at another emulator used to leave every game
    /// it had imported launching -- and syncing saves against -- the emulator it no longer uses. Refreshing the action closes that gap, but only while the action
    /// is still the one the plugin wrote: a user who repoints it themselves keeps their choice.
    /// </summary>
    internal static class RomMPlayAction
    {
        public static string NameFor(string emulatorName) => $"Play in {emulatorName}";

        public static GameAction Build(string emulatorName, Guid emulatorId, string emulatorProfileId) =>
            Apply(new GameAction(), emulatorName, emulatorId, emulatorProfileId);

        /// <summary>
        /// Writes the importer's play action onto an existing one, for repointing a game already in
        /// the library. Shares its body with <see cref="Build"/> so the fields the importer owns
        /// cannot drift from a freshly imported action; anything else on the action is left as it
        /// is, which is why <see cref="IsUnedited"/> refuses an action carrying argument overrides.
        /// </summary>
        public static GameAction Apply(GameAction action, string emulatorName, Guid emulatorId, string emulatorProfileId)
        {
            action.Name = NameFor(emulatorName);
            action.Type = GameActionType.Emulator;
            action.EmulatorId = emulatorId;
            action.EmulatorProfileId = emulatorProfileId;
            action.IsPlayAction = true;
            return action;
        }

        /// <summary>
        /// The emulator action Playnite launches the game with. The play action comes first; a game
        /// whose emulator action is not marked as one still tells us which emulator it runs on.
        /// </summary>
        public static GameAction Find(IEnumerable<GameAction> actions)
        {
            if (actions == null)
                return null;

            var emulatorActions = actions.Where(a => a != null && a.Type == GameActionType.Emulator).ToList();
            return emulatorActions.FirstOrDefault(a => a.IsPlayAction) ?? emulatorActions.FirstOrDefault();
        }

        /// <summary>Whether the action already launches the given emulator and profile.</summary>
        public static bool Matches(GameAction action, AppliedPlayAction target)
        {
            return action != null
                   && action.EmulatorId == target.EmulatorId
                   && SameProfile(action.EmulatorProfileId, target.ProfileId);
        }

        /// <summary>
        /// Whether the action is still the plugin's to repoint.
        ///
        /// <paramref name="applied"/> is what the plugin last wrote, recorded in the ROM's sidecar;
        /// if the action still carries it, nobody has touched it. Sidecars written before the plugin recorded that (every install
        /// that predates this) carry no applied emulator, and then the generated name is the only
        /// marker left: an action still called "Play in {the emulator it points at}" is one
        /// the importer wrote and the user has not renamed or repointed. That name costs a lookup
        /// of the action's emulator, so it is passed as a thunk and only resolved on that path.
        ///
        /// Either way the importer only ever writes a play action with no argument overrides, so an
        /// action the user demoted from being the play action, or gave arguments of its own, is
        /// theirs: repointing it would re-promote it beside their own play action, or launch the
        /// new emulator with arguments meant for the old one.
        /// </summary>
        public static bool IsUnedited(GameAction action, AppliedPlayAction applied, Func<string> actionEmulatorName)
        {
            if (action == null
                || !action.IsPlayAction
                || action.OverrideDefaultArgs
                || !string.IsNullOrEmpty(action.AdditionalArguments))
                return false;

            if (applied.EmulatorId != Guid.Empty)
                return Matches(action, applied);

            var name = actionEmulatorName?.Invoke();
            return !string.IsNullOrEmpty(name)
                   && string.Equals(action.Name, NameFor(name), StringComparison.Ordinal);
        }

        // Playnite writes an unset profile as either null or "", and the two mean the same thing.
        private static bool SameProfile(string left, string right) =>
            string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);
    }
}
