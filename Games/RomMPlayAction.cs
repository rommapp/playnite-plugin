using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RomM.Games
{
    /// <summary>
    /// The emulator and profile the importer last wrote onto a game's play action, as recorded in
    /// the ROM's sidecar. <see cref="Unknown"/> is what a sidecar written before the plugin kept
    /// this record yields.
    /// </summary>
    internal struct AppliedPlayAction
    {
        public static readonly AppliedPlayAction Unknown = new AppliedPlayAction(Guid.Empty, null);

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

        public static GameAction Build(string emulatorName, Guid emulatorId, string emulatorProfileId)
        {
            return new GameAction
            {
                Name = NameFor(emulatorName),
                Type = GameActionType.Emulator,
                EmulatorId = emulatorId,
                EmulatorProfileId = emulatorProfileId,
                IsPlayAction = true,
            };
        }

        /// <summary>
        /// The emulator action Playnite launches the game with. The play action comes first; a game
        /// whose emulator action is not marked as one still tells us which emulator it runs on.
        /// </summary>
        public static GameAction Find(IEnumerable<GameAction> actions)
        {
            if (actions == null)
                return null;

            var list = actions as IList<GameAction> ?? actions.ToList();
            return list.FirstOrDefault(a => a != null && a.IsPlayAction && a.Type == GameActionType.Emulator)
                   ?? list.FirstOrDefault(a => a != null && a.Type == GameActionType.Emulator);
        }

        /// <summary>Whether the action already launches the given emulator and profile.</summary>
        public static bool Matches(GameAction action, Guid emulatorId, string emulatorProfileId)
        {
            return action != null
                   && action.EmulatorId == emulatorId
                   && SameProfile(action.EmulatorProfileId, emulatorProfileId);
        }

        /// <summary>
        /// Whether the action is still the plugin's to repoint.
        ///
        /// <paramref name="appliedEmulatorId"/> / <paramref name="appliedProfileId"/> are what the
        /// plugin last wrote, recorded in the ROM's sidecar; if the action still carries them,
        /// nobody has touched it. Sidecars written before the plugin recorded that (every install
        /// that predates this) carry no applied emulator, and then the generated name is the only
        /// marker left: an action still called "Play in &lt;the emulator it points at&gt;" is one
        /// the importer wrote and the user has not renamed or repointed.
        /// </summary>
        public static bool IsUnedited(GameAction action, Guid appliedEmulatorId, string appliedProfileId, string actionEmulatorName)
        {
            if (action == null)
                return false;

            if (appliedEmulatorId != Guid.Empty)
                return Matches(action, appliedEmulatorId, appliedProfileId);

            return !string.IsNullOrEmpty(actionEmulatorName)
                   && string.Equals(action.Name, NameFor(actionEmulatorName), StringComparison.Ordinal);
        }

        // Playnite writes an unset profile as either null or "", and the two mean the same thing.
        private static bool SameProfile(string left, string right)
        {
            if (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right))
                return true;

            return string.Equals(left, right, StringComparison.Ordinal);
        }
    }
}
