using System.Text.Json;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationAttributesService
{
    /// <summary>
    /// Detached, read-only rules snapshot for local editing. Never a save lease:
    /// callers must retire it with its display and use fresh Preview/Confirm to save.
    /// </summary>
    public static DraftSession? CreateDraftSession(CharacterCreationAttributesState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var detached = JsonSerializer.Deserialize<CharacterCreationAttributesState>(
            JsonSerializer.SerializeToUtf8Bytes(state));
        if (detached is not { PrerequisiteDraft: not null }
            || !CharacterCreationCompletionReadiness.RulesReady(detached.CanEdit, detached.Blockers,
                CharacterCreationAttributesBlockers.PersistenceAuthorityRequired)
            || detached.Schema != CharacterCreationAttributesSchemas.SnapshotV1
            || !CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(detached.SnapshotDigest,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                    detached with { SnapshotDigest = string.Empty }))
            || detached.Binding.ContentRevision <= 0
            || detached.Binding.ContentRevision != detached.Binding.SavedRevision
            || detached.PrerequisiteDraft.DraftRevision != detached.Binding.PrerequisiteDraftRevision
            || !CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                detached.PrerequisiteDraft.DraftDigest, detached.Binding.PrerequisiteDraftDigest)
            || !CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                detached.PrerequisiteDraft.AuthorityDigest, detached.Binding.PrerequisiteAuthorityDigest))
            return null;
        return new DraftSession(detached);
    }

    public sealed class DraftSession
    {
        private readonly CharacterCreationAttributesState _state;
        internal DraftSession(CharacterCreationAttributesState state) => _state = state;

        // The admitted observation keeps its persistence blocker and CanEdit=false.
        // Only this store-free rule calculation excludes save readiness; it never
        // changes the observed draft, its identity, or live Preview/Confirm gates.
        private CharacterCreationAttributesState RulesState => _state.CanEdit ? _state
            : _state with { Blockers = [] };

        public CharacterCreationFoundationResult<CharacterCreationAttributesPreview> Preview(
            IReadOnlyList<CharacterCreationAttributeAllocation> allocations)
        {
            ArgumentNullException.ThrowIfNull(allocations);
            // A numeric probe or completion candidate is not a player choice.
            // Preserve the original intent without classifying its candidate deltas.
            return Detach(EvaluatePreparedState(RulesState, allocations.ToArray(),
                _state.PendingDraft?.PlayerChosenAttributeIds, validatePlayerChoiceChanges: false).Result);
        }

        /// <summary>
        /// Previews an explicit cumulative player-choice snapshot with the same
        /// admission rules as live Preview. The session remains read-only.
        /// </summary>
        public CharacterCreationFoundationResult<CharacterCreationAttributesPreview> Preview(
            IReadOnlyList<CharacterCreationAttributeAllocation> allocations,
            IReadOnlyList<string>? playerChosenAttributeIds)
        {
            ArgumentNullException.ThrowIfNull(allocations);
            return Detach(EvaluatePreparedState(RulesState, allocations.ToArray(),
                playerChosenAttributeIds?.ToArray()).Result);
        }

        private static CharacterCreationFoundationResult<CharacterCreationAttributesPreview> Detach(
            CharacterCreationFoundationResult<CharacterCreationAttributesPreview> result)
        {
            // No mutable array from the private snapshot escapes through a projection.
            return JsonSerializer.Deserialize<CharacterCreationFoundationResult<CharacterCreationAttributesPreview>>(
                JsonSerializer.SerializeToUtf8Bytes(result))!;
        }
    }
}
