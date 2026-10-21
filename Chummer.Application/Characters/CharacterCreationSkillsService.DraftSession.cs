using System.Text.Json;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationSkillsService
{
    /// <summary>
    /// Capture one validated, detached rules snapshot for responsive local editing.
    /// This session has no store, resolver, owner lease or write capability. Its
    /// results describe that snapshot only, never current save authorization.
    /// Callers must retire it when the issuing display/owner/revision changes and
    /// use the ordinary fresh Preview/Confirm path for durable changes.
    /// </summary>
    public static DraftSession? CreateDraftSession(CharacterCreationSkillsState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        // IReadOnlyList does not guarantee immutable caller storage. Keep a
        // private deep snapshot; no caller-owned arrays may enter the cache.
        var detached = JsonSerializer.Deserialize<CharacterCreationSkillsState>(
            JsonSerializer.SerializeToUtf8Bytes(state));
        if (detached is not { PrerequisiteDraft: not null, AttributesDraft: not null }
            || !CharacterCreationCompletionReadiness.RulesReady(detached.CanEdit, detached.Blockers,
                CharacterCreationSkillsBlockers.PersistenceAuthorityRequired)
            || (detached.CompositeInputs is null ? !CharacterCreationSkillsDraftIntegrity.IsValidStateProjection(detached)
                : !IsValidCompositeSkillsState(detached))
            || !CharacterCreationSkillsDigest.EqualsFixedTime(detached.SnapshotDigest,
                CharacterCreationSkillsDigest.Compute(detached with { SnapshotDigest = string.Empty }))
            || detached.Binding.ContentRevision <= 0
            || detached.Binding.ContentRevision != detached.Binding.SavedRevision
            || detached.PrerequisiteDraft.DraftRevision != detached.Binding.PrerequisiteDraftRevision
            || detached.AttributesDraft.DraftRevision != detached.Binding.AttributesDraftRevision
            || !CharacterCreationSkillsDigest.EqualsFixedTime(detached.PrerequisiteDraft.DraftDigest, detached.Binding.PrerequisiteDraftDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(detached.AttributesDraft.DraftDigest, detached.Binding.AttributesDraftDigest))
            return null;
        return new DraftSession(detached);
    }

    public sealed class DraftSession
    {
        private readonly CharacterCreationSkillsState _state;
        internal DraftSession(CharacterCreationSkillsState state) => _state = state;

        /// <summary>Recalculate dependent budgets with Core's existing evaluator, without I/O.</summary>
        public CharacterCreationFoundationResult<CharacterCreationSkillsPreview> Preview(
            IReadOnlyList<CharacterCreationSkillAllocation> allocations,
            IReadOnlyList<CharacterCreationSkillGroupAllocation> groups)
        {
            ArgumentNullException.ThrowIfNull(allocations);
            ArgumentNullException.ThrowIfNull(groups);
            var result = EvaluatePreparedState(_state,
                new(_state.Binding, allocations.ToArray(), groups.ToArray()), []).Result;
            // Projected group membership/source anchors must not expose arrays
            // retained by the private rules snapshot either.
            return JsonSerializer.Deserialize<CharacterCreationFoundationResult<CharacterCreationSkillsPreview>>(
                JsonSerializer.SerializeToUtf8Bytes(result))!;
        }
    }
}
