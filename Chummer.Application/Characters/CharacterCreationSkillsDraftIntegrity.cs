using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static class CharacterCreationSkillsDraftIntegrity
{
    public static bool IsValidStateProjection(CharacterCreationSkillsState? state)
    {
        if (state is null
            || !string.Equals(state.Schema, CharacterCreationSkillsSchemas.SnapshotV1, StringComparison.Ordinal)
            || !IsValidAuthority(state.Authority)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(
                state.Binding.SkillsAuthorityDigest,
                state.Authority.AuthorityDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(
                state.Binding.RuntimeDigest,
                state.Authority.RuntimeDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(
                state.Binding.ContributionInputsDigest,
                CharacterCreationSkillsDigest.Compute(state.KnowledgePointContributions.ToArray()))
            || !CharacterCreationSkillsDigest.EqualsFixedTime(
                CharacterCreationSkillsDigest.Compute(state.KnowledgePointContributions.ToArray()),
                CharacterCreationSkillsDigest.Compute(state.Authority.KnowledgePointContributions.ToArray()))
            || state.IntuitionUnaugmented < 0
            || state.LogicUnaugmented < 0)
            return false;

        long knowledgeTotal = ((long)state.IntuitionUnaugmented + state.LogicUnaugmented) * 2L
                              + state.KnowledgePointContributions.Sum(item => (long)item.Points);
        if (knowledgeTotal is < 0 or > int.MaxValue
            || !BudgetMatches(state.ActiveSkillPointBudget, state.SelectedActiveSkillPoints,
                state.PendingDraft?.ActivePointUsed ?? 0)
            || !BudgetMatches(state.SkillGroupPointBudget, state.SelectedSkillGroupPoints,
                state.PendingDraft?.SkillGroupPointUsed ?? 0)
            || !BudgetMatches(state.KnowledgeSkillPointBudget, (int)knowledgeTotal,
                state.PendingDraft?.KnowledgePointUsed ?? 0))
            return false;

        if (state.PendingDraft is null)
            return state.Skills.Count == 0 && state.SkillGroups.Count == 0;
        if (state.PrerequisiteDraft is not { } prerequisite
            || state.AttributesDraft is not { } attributes
            || !IsStructurallyValidPending(
                state.PendingDraft,
                state.Binding.WorkspaceId,
                state.Binding.ContentRevision,
                state.Binding.RawCharacterXmlDigest,
                prerequisite,
                attributes,
                state.Authority,
                state.Binding.ContributionInputsDigest))
            return false;
        return CharacterCreationSkillsDigest.EqualsFixedTime(
                   CharacterCreationSkillsDigest.Compute(state.Skills.ToArray()),
                   CharacterCreationSkillsDigest.Compute(state.PendingDraft.Skills.ToArray()))
               && CharacterCreationSkillsDigest.EqualsFixedTime(
                   CharacterCreationSkillsDigest.Compute(state.SkillGroups.ToArray()),
                   CharacterCreationSkillsDigest.Compute(state.PendingDraft.SkillGroups.ToArray()))
               && state.PendingDraft.ActivePointTotal == state.SelectedActiveSkillPoints
               && state.PendingDraft.SkillGroupPointTotal == state.SelectedSkillGroupPoints
               && state.PendingDraft.KnowledgePointTotal == (int)knowledgeTotal;
    }

    private static bool BudgetMatches(CharacterCreationBudgetState budget, int total, int used) =>
        budget.IsExact
        && budget.Total == total
        && budget.Used == used
        && budget.Remaining == total - used;

    public static bool IsValidAuthority(CharacterCreationSkillsAuthority? authority)
    {
        if (authority is null
            || !string.Equals(authority.Schema, CharacterCreationSkillsSchemas.AuthorityV1, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(authority.SettingsProfileId)
            || !CharacterCreationSkillsDigest.IsCanonical(authority.EffectiveSkillsInputsDigest)
            || !CharacterCreationSkillsDigest.IsCanonical(authority.RawProfileInputsDigest)
            || !string.Equals(
                authority.KnowledgePointsExpression,
                CharacterCreationStandardPrioritySkillsRules.KnowledgePointsExpression,
                StringComparison.Ordinal)
            || authority.MaxActiveSkillRatingCreate != CharacterCreationStandardPrioritySkillsRules.MaximumRatingAtCreation
            || authority.MaxKnowledgeSkillRatingCreate != CharacterCreationStandardPrioritySkillsRules.MaximumRatingAtCreation
            || authority.MaxSkillGroupRatingCreate != CharacterCreationStandardPrioritySkillsRules.MaximumRatingAtCreation
            || authority.BaseNativeLanguageLimit != CharacterCreationStandardPrioritySkillsRules.BaseNativeLanguageCount
            || authority.UsePointsOnBrokenGroups
            || authority.StrictSkillGroupsInCreateMode
            || !authority.SpecializationsBreakSkillGroups
            || authority.ActiveSkills is null
            || authority.KnowledgeSkills is null
            || authority.SkillGroups is null
            || authority.KnowledgePointContributions is null
            || authority.SourceAnchorIds is not { Count: > 0 }
            || authority.Blockers is null
            || !authority.IsAuthoritative
            || authority.Blockers.Count != 0
            || !CharacterCreationSkillsDigest.IsCanonical(authority.RuntimeDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(
                authority.RuntimeDigest,
                CharacterCreationStandardPrioritySkillsRules.ComputeRuntimeDigest(
                    authority.UsePointsOnBrokenGroups,
                    authority.StrictSkillGroupsInCreateMode,
                    authority.SpecializationsBreakSkillGroups))
            || !CharacterCreationSkillsDigest.IsCanonical(authority.AuthorityDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(
                authority.AuthorityDigest,
                CharacterCreationSkillsDigest.Compute(authority with { AuthorityDigest = string.Empty })))
        {
            return false;
        }

        CharacterCreationSkillCatalogEntry[] catalog =
            [.. authority.ActiveSkills, .. authority.KnowledgeSkills];
        if (authority.ActiveSkills.Count == 0
            || authority.KnowledgeSkills.Count == 0
            || catalog.Select(skill => skill.SourceSkillId)
                .Distinct(StringComparer.Ordinal).Count() != catalog.Length
            || catalog.Select(skill => string.Concat(skill.Kind, "\0", skill.Name))
                .Distinct(StringComparer.Ordinal).Count() != catalog.Length
            || catalog.Any(skill => !IsValidCatalogEntry(
                skill,
                authority.EffectiveSkillsInputsDigest))
            || authority.ActiveSkills.Any(skill => !string.Equals(
                skill.Kind,
                CharacterCreationSkillKinds.Active,
                StringComparison.Ordinal))
            || authority.KnowledgeSkills.Any(skill => !string.Equals(
                skill.Kind,
                CharacterCreationSkillKinds.Knowledge,
                StringComparison.Ordinal)
                || skill.SkillGroup is not null
                || skill.IsExotic)
            || !IsCanonicallyOrdered(
                authority.ActiveSkills,
                authority.ActiveSkills.OrderBy(skill => skill.Name, StringComparer.Ordinal)
                    .ThenBy(skill => skill.SourceSkillId, StringComparer.Ordinal))
            || !IsCanonicallyOrdered(
                authority.KnowledgeSkills,
                authority.KnowledgeSkills.OrderBy(skill => skill.Name, StringComparer.Ordinal)
                    .ThenBy(skill => skill.SourceSkillId, StringComparer.Ordinal)))
        {
            return false;
        }

        var active = authority.ActiveSkills.ToDictionary(skill => skill.SourceSkillId, StringComparer.Ordinal);
        if (authority.SkillGroups.Select(group => group.GroupId)
                .Distinct(StringComparer.Ordinal).Count() != authority.SkillGroups.Count
            || authority.SkillGroups.Any(group => !IsValidGroup(group, active, authority.EffectiveSkillsInputsDigest))
            || !IsCanonicallyOrdered(
                authority.SkillGroups,
                authority.SkillGroups.OrderBy(group => group.Name, StringComparer.Ordinal)
                    .ThenBy(group => group.GroupId, StringComparer.Ordinal)))
        {
            return false;
        }

        string[] groupedNames = authority.ActiveSkills
            .Where(skill => skill.SkillGroup is not null)
            .Select(skill => skill.SkillGroup!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!groupedNames.SequenceEqual(
                authority.SkillGroups.Select(group => group.Name).OrderBy(name => name, StringComparer.Ordinal),
                StringComparer.Ordinal)
            || authority.KnowledgePointContributions.Select(item => item.ContributionId)
                .Distinct(StringComparer.Ordinal).Count() != authority.KnowledgePointContributions.Count
            || authority.KnowledgePointContributions.Any(item =>
                !IsCanonicalLabel(item.ContributionId)
                || item.Points < 0
                || !CharacterCreationSkillsDigest.IsCanonical(item.SourceCharacterXmlDigest)
                || !CharacterCreationSkillsDigest.IsCanonical(item.SourceDigest)
                || item.SourceAnchorIds is not { Count: > 0 }
                || !IsCanonicalStringSet(item.SourceAnchorIds)
                || !CharacterCreationSkillsDigest.EqualsFixedTime(
                    item.SourceDigest,
                    CharacterCreationSkillsDigest.Compute(new
                    {
                        Schema = "chummer.sr5.creation-knowledge-point-contribution.v1",
                        item.ContributionId,
                        item.Points,
                        item.SourceCharacterXmlDigest,
                        SourceAnchorIds = item.SourceAnchorIds.ToArray()
                    })))
            || !IsCanonicallyOrdered(
                authority.KnowledgePointContributions,
                authority.KnowledgePointContributions.OrderBy(item => item.ContributionId, StringComparer.Ordinal))
            || !IsCanonicalStringSet(authority.SourceAnchorIds))
        {
            return false;
        }
        return true;
    }

    public static string ComputeDigest(CharacterCreationSkillsDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return CharacterCreationSkillsDigest.Compute(draft with { DraftDigest = string.Empty });
    }

    public static bool IsStructurallyValidPending(
        CharacterCreationSkillsDraft? draft,
        CharacterWorkspaceId workspaceId,
        long persistedContentRevision,
        string rawCharacterXmlDigest,
        CharacterCreationPrerequisiteDraft prerequisite,
        CharacterCreationAttributesDraft attributes,
        CharacterCreationSkillsAuthority authority,
        string contributionInputsDigest)
        => draft is not null
           && string.Equals(draft.Schema, CharacterCreationSkillsSchemas.DraftV1, StringComparison.Ordinal)
           && draft.WorkspaceId == workspaceId
           && draft.DraftRevision > 0
           && draft.BaseContentRevision > 0
           && draft.BaseContentRevision < persistedContentRevision
           && CharacterCreationSkillsDigest.EqualsFixedTime(draft.BaseRawCharacterXmlDigest, rawCharacterXmlDigest)
           && draft.PrerequisiteDraftRevision == prerequisite.DraftRevision
           && CharacterCreationSkillsDigest.EqualsFixedTime(draft.PrerequisiteDraftDigest, prerequisite.DraftDigest)
           && CharacterCreationSkillsDigest.EqualsFixedTime(draft.PrerequisiteAuthorityDigest, prerequisite.AuthorityDigest)
           && draft.AttributesDraftRevision == attributes.DraftRevision
           && CharacterCreationSkillsDigest.EqualsFixedTime(draft.AttributesDraftDigest, attributes.DraftDigest)
           && CharacterCreationSkillsDigest.EqualsFixedTime(draft.SkillsAuthorityDigest, authority.AuthorityDigest)
           && CharacterCreationSkillsDigest.EqualsFixedTime(draft.RuntimeDigest, authority.RuntimeDigest)
           && CharacterCreationSkillsDigest.EqualsFixedTime(draft.ContributionInputsDigest, contributionInputsDigest)
           && draft.ActivePointTotal >= 0
           && draft.ActivePointUsed >= 0
           && draft.ActivePointUsed <= draft.ActivePointTotal
           && draft.SkillGroupPointTotal >= 0
           && draft.SkillGroupPointUsed >= 0
           && draft.SkillGroupPointUsed <= draft.SkillGroupPointTotal
           && draft.KnowledgePointTotal >= 0
           && draft.KnowledgePointUsed >= 0
           && draft.KnowledgePointUsed <= draft.KnowledgePointTotal
           && draft.KnowledgePointOverflowToActive >= 0
           && draft.Allocations is not null
           && draft.GroupAllocations is not null
           && draft.Skills is not null
           && draft.SkillGroups is not null
           && draft.KnowledgePointContributions is not null
           && draft.SourceAnchorIds is { Count: > 0 }
           && !draft.CharacterEffectsApplied
           && CharacterCreationSkillsDigest.IsCanonical(draft.LastIdempotencyKeyDigest)
           && CharacterCreationSkillsDigest.IsCanonical(draft.LastPreviewDigest)
           && CharacterCreationSkillsDigest.IsCanonical(draft.LastCommandDigest)
           && CharacterCreationSkillsDigest.IsCanonical(draft.DraftDigest)
           && CharacterCreationSkillsDigest.EqualsFixedTime(draft.DraftDigest, ComputeDigest(draft));

    public static bool HasSameLogicalPayload(
        CharacterCreationSkillsDraft left,
        CharacterCreationSkillsDraft right)
        => CharacterCreationSkillsDigest.EqualsFixedTime(
            ComputeDigest(left with
            {
                DraftRevision = 0,
                BaseContentRevision = 0,
                LastIdempotencyKeyDigest = string.Empty,
                LastPreviewDigest = string.Empty,
                LastCommandDigest = string.Empty,
                DraftDigest = string.Empty
            }),
            ComputeDigest(right with
            {
                DraftRevision = 0,
                BaseContentRevision = 0,
                LastIdempotencyKeyDigest = string.Empty,
                LastPreviewDigest = string.Empty,
                LastCommandDigest = string.Empty,
                DraftDigest = string.Empty
            }));

    public static bool IsValidReceiptLedger(
        IReadOnlyList<CharacterCreationSkillsReceipt>? receipts,
        CharacterWorkspaceId workspaceId,
        long persistedContentRevision)
    {
        if (receipts is null)
            return true;
        if (receipts.Count == 0
            || receipts[0].DraftRevision != 1
            || receipts.Any(receipt => !CharacterCreationSkillsDigest.IsValidReceipt(
                receipt,
                workspaceId,
                persistedContentRevision))
            || receipts.Select(receipt => receipt.IdempotencyKeyDigest)
                .Distinct(StringComparer.Ordinal).Count() != receipts.Count)
        {
            return false;
        }
        if (!CharacterCreationSkillsDigest.EqualsFixedTime(
                receipts[0].PreviousReceiptDigest,
                CharacterCreationSkillsDigest.ReceiptLedgerRootDigest))
            return false;
        return receipts.Zip(receipts.Skip(1)).All(pair =>
            pair.First.ContentRevision < pair.Second.ContentRevision
            && pair.Second.PreviousContentRevision == pair.First.ContentRevision
            && pair.First.DraftRevision < long.MaxValue
            && pair.Second.DraftRevision == pair.First.DraftRevision + 1
            && CharacterCreationSkillsDigest.EqualsFixedTime(
                pair.Second.PreviousReceiptDigest,
                pair.First.ReceiptDigest));
    }

    private static bool IsValidCatalogEntry(
        CharacterCreationSkillCatalogEntry skill,
        string effectiveSkillsInputsDigest)
        => Guid.TryParseExact(skill.SourceSkillId, "D", out Guid sourceId)
           && sourceId != Guid.Empty
           && string.Equals(skill.SourceSkillId, sourceId.ToString("D"), StringComparison.Ordinal)
           && IsCanonicalLabel(skill.Name)
           && IsCanonicalLabel(skill.Category)
           && IsCanonicalLabel(skill.DefaultAttribute)
           && (skill.SkillGroup is null || IsCanonicalLabel(skill.SkillGroup))
           && CharacterCreationStandardPrioritySkillsRules.IsSupportedCategory(skill.Kind, skill.Category)
           && CharacterCreationStandardPrioritySkillsRules.IsSupportedAttribute(skill.DefaultAttribute)
           && CharacterCreationSkillsDigest.IsCanonical(skill.SourceNodeDigest)
           && skill.Specializations is not null
           && skill.Specializations.Select(option => option.OptionId)
               .Distinct(StringComparer.Ordinal).Count() == skill.Specializations.Count
           && skill.Specializations.Select(option => option.Name)
               .Distinct(StringComparer.Ordinal).Count() == skill.Specializations.Count
           && skill.Specializations.All(option =>
               IsCanonicalLabel(option.OptionId)
               && IsCanonicalLabel(option.Name)
               && IsCanonicalLabel(option.SourceAnchorId))
           && IsCanonicallyOrdered(
               skill.Specializations,
               skill.Specializations.OrderBy(option => option.Name, StringComparer.Ordinal)
                   .ThenBy(option => option.OptionId, StringComparer.Ordinal))
           && skill.SourceAnchorIds is { Count: > 0 }
           && IsCanonicalStringSet(skill.SourceAnchorIds)
           && CharacterCreationSkillsDigest.EqualsFixedTime(
               skill.SourceNodeDigest,
               CharacterCreationStandardPrioritySkillsRules.ComputeCatalogProjectionDigest(
                   effectiveSkillsInputsDigest,
                   skill.SourceSkillId,
                   skill.Kind,
                   skill.Name,
                   skill.Category,
                   skill.DefaultAttribute,
                   skill.SkillGroup,
                   skill.IsExotic,
                   skill.Specializations,
                   skill.SourceAnchorIds,
                   skill.CanDefault,
                   skill.IgnoresSourceDisabled));

    private static bool IsValidGroup(
        CharacterCreationSkillGroupCatalogEntry group,
        IReadOnlyDictionary<string, CharacterCreationSkillCatalogEntry> active,
        string effectiveSkillsInputsDigest)
    {
        if (!IsCanonicalLabel(group.Name)
            || group.MemberSkillSourceIds is not { Count: >= 2 }
            || !IsCanonicalStringSet(group.MemberSkillSourceIds)
            || group.SourceAnchorIds is not { Count: > 0 }
            || !IsCanonicalStringSet(group.SourceAnchorIds)
            || !CharacterCreationSkillsDigest.IsCanonical(group.GroupId)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(group.GroupId, group.GroupDigest)
            || group.MemberSkillSourceIds.Any(id =>
                !active.TryGetValue(id, out CharacterCreationSkillCatalogEntry? skill)
                || !string.Equals(skill.SkillGroup, group.Name, StringComparison.Ordinal)))
        {
            return false;
        }
        string expected = CharacterCreationSkillsDigest.Compute(new
        {
            Schema = "chummer.sr5.creation-skill-group-source.v1",
            Name = group.Name,
            MemberSkillSourceIds = group.MemberSkillSourceIds.ToArray(),
            EffectiveSkillsInputsDigest = effectiveSkillsInputsDigest
        });
        return CharacterCreationSkillsDigest.EqualsFixedTime(expected, group.GroupDigest);
    }

    private static bool IsCanonicalStringSet(IReadOnlyList<string> values) =>
        values.All(IsCanonicalLabel)
        && values.SequenceEqual(
            values.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal),
            StringComparer.Ordinal);

    private static bool IsCanonicalLabel(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && value.Length <= 512
        && !value.Any(char.IsControl);

    private static bool IsCanonicallyOrdered<T>(
        IReadOnlyList<T> actual,
        IEnumerable<T> expected) => actual.SequenceEqual(expected);
}
