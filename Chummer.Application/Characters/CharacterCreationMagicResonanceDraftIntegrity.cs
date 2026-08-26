using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static class CharacterCreationMagicResonanceDraftIntegrity
{
    public static string ComputeDigest(CharacterCreationMagicResonanceDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return CharacterCreationMagicResonanceDigest.Compute(draft with { DraftDigest = string.Empty });
    }

    public static bool IsValidAuthority(CharacterCreationMagicResonanceAuthority? authority)
    {
        if (authority is null
            || !string.Equals(authority.Schema, CharacterCreationMagicResonanceSchemas.AuthorityV1, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(authority.SettingsProfileId)
            || !CharacterCreationMagicResonanceDigest.IsCanonical(authority.PrerequisiteAuthorityDigest)
            || !CharacterCreationMagicResonanceDigest.IsCanonical(authority.SourceInputsDigest)
            || !CharacterCreationMagicResonanceDigest.IsCanonical(authority.CustomDataInputsDigest)
            || !CharacterCreationMagicResonanceDigest.IsCanonical(authority.GmPolicyDigest)
            || !CharacterCreationMagicResonanceDigest.IsCanonical(authority.RuntimeDigest)
            || authority.Talents is null
            || authority.Metatypes is null
            || authority.Traditions is null
            || authority.Streams is null
            || authority.AdeptPowers is null
            || authority.Spells is null
            || authority.ComplexForms is null
            || authority.SourceAnchorIds is not { Count: > 0 }
            || authority.Blockers is null
            || !authority.IsAuthoritative
            || authority.Blockers.Count != 0
            || !CharacterCreationMagicResonanceDigest.IsCanonical(authority.AuthorityDigest)
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                authority.AuthorityDigest,
                CharacterCreationMagicResonanceDigest.Compute(authority with { AuthorityDigest = string.Empty })))
            return false;

        if (!IsCanonicalSet(authority.SourceAnchorIds)
            || authority.Talents.Count == 0
            || authority.Metatypes.Count == 0
            || !IsCanonicallyOrdered(authority.Talents,
                authority.Talents.OrderBy(item => item.Rank, StringComparer.Ordinal)
                    .ThenBy(item => item.Identity.TalentSelectionId, StringComparer.Ordinal))
            || !IsCanonicallyOrdered(authority.Metatypes,
                authority.Metatypes.OrderBy(item => item.MetatypeName, StringComparer.Ordinal)
                    .ThenBy(item => item.MetatypeSourceId, StringComparer.Ordinal)))
            return false;

        if (authority.Talents.Select(item => item.Identity)
                .Distinct().Count() != authority.Talents.Count
            || authority.Talents.Any(item => !IsValidTalent(item))
            || authority.Metatypes.Select(item => item.MetatypeSourceId)
                .Distinct(StringComparer.Ordinal).Count() != authority.Metatypes.Count
            || authority.Metatypes.Any(item => !IsGuid(item.MetatypeSourceId)
                || !IsLabel(item.MetatypeName)
                || !IsLabel(item.MetatypeCategory)
                || !CharacterCreationMagicResonanceDigest.IsCanonical(item.SourceNodeDigest)
                || !IsCanonicalSet(item.SourceAnchorIds)))
            return false;

        return IsValidCatalog(authority.Traditions, CharacterCreationMagicResonanceKinds.Tradition)
               && IsValidCatalog(authority.Streams, CharacterCreationMagicResonanceKinds.Stream)
               && IsValidCatalog(authority.AdeptPowers, CharacterCreationMagicResonanceKinds.AdeptPower)
               && IsValidCatalog(authority.Spells, CharacterCreationMagicResonanceKinds.Spell)
               && IsValidCatalog(authority.ComplexForms, CharacterCreationMagicResonanceKinds.ComplexForm);
    }

    public static bool IsStructurallyValidPending(
        CharacterCreationMagicResonanceDraft? draft,
        CharacterWorkspaceId workspaceId,
        long persistedContentRevision,
        string rawCharacterXmlDigest,
        CharacterCreationPrerequisiteDraft prerequisite,
        CharacterCreationAttributesDraft attributes,
        CharacterCreationMagicResonanceAuthority authority)
        => draft is not null
           && string.Equals(draft.Schema, CharacterCreationMagicResonanceSchemas.DraftV1, StringComparison.Ordinal)
           && draft.WorkspaceId == workspaceId
           && draft.DraftRevision > 0
           && draft.BaseContentRevision > 0
           && draft.BaseContentRevision < persistedContentRevision
           && CharacterCreationMagicResonanceDigest.EqualsFixedTime(draft.BaseRawCharacterXmlDigest, rawCharacterXmlDigest)
           && draft.PrerequisiteDraftRevision == prerequisite.DraftRevision
           && CharacterCreationMagicResonanceDigest.EqualsFixedTime(draft.PrerequisiteDraftDigest, prerequisite.DraftDigest)
           && CharacterCreationMagicResonanceDigest.EqualsFixedTime(draft.PrerequisiteAuthorityDigest, prerequisite.AuthorityDigest)
           && draft.AttributesDraftRevision == attributes.DraftRevision
           && CharacterCreationMagicResonanceDigest.EqualsFixedTime(draft.AttributesDraftDigest, attributes.DraftDigest)
           && CharacterCreationMagicResonanceDigest.EqualsFixedTime(draft.AuthorityDigest, authority.AuthorityDigest)
           && CharacterCreationMagicResonanceDigest.EqualsFixedTime(draft.SourceInputsDigest, authority.SourceInputsDigest)
           && CharacterCreationMagicResonanceDigest.EqualsFixedTime(draft.CustomDataInputsDigest, authority.CustomDataInputsDigest)
           && CharacterCreationMagicResonanceDigest.EqualsFixedTime(draft.GmPolicyDigest, authority.GmPolicyDigest)
           && CharacterCreationMagicResonanceDigest.EqualsFixedTime(draft.RuntimeDigest, authority.RuntimeDigest)
           && authority.Talents.Count(item => item.Identity == draft.TalentIdentity
               && string.Equals(item.Kind, draft.TalentKind, StringComparison.Ordinal)
               && item.Magic == draft.AssignedMagic
               && item.Resonance == draft.AssignedResonance
               && item.Depth == draft.AssignedDepth) == 1
           && draft.AssignedMagic >= 0
           && draft.AssignedResonance >= 0
           && draft.AssignedDepth >= 0
           && draft.Selections is not null
           && HasCanonicalSelections(draft.Selections)
           && IsValidBudget(draft.TraditionBudget)
           && IsValidBudget(draft.StreamBudget)
           && IsValidBudget(draft.AdeptPowerPointBudget)
           && IsValidBudget(draft.SpellBudget)
           && IsValidBudget(draft.ComplexFormBudget)
           && IsCanonicalSet(draft.SourceAnchorIds)
           && !draft.CharacterEffectsApplied
           && CharacterCreationMagicResonanceDigest.IsCanonical(draft.LastIdempotencyKeyDigest)
           && CharacterCreationMagicResonanceDigest.IsCanonical(draft.LastPreviewDigest)
           && CharacterCreationMagicResonanceDigest.IsCanonical(draft.LastCommandDigest)
           && CharacterCreationMagicResonanceDigest.IsCanonical(draft.DraftDigest)
           && CharacterCreationMagicResonanceDigest.EqualsFixedTime(draft.DraftDigest, ComputeDigest(draft));

    public static bool HasSameLogicalPayload(
        CharacterCreationMagicResonanceDraft left,
        CharacterCreationMagicResonanceDraft right)
        => CharacterCreationMagicResonanceDigest.EqualsFixedTime(
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
        IReadOnlyList<CharacterCreationMagicResonanceReceipt>? receipts,
        CharacterWorkspaceId workspaceId,
        long persistedContentRevision)
    {
        if (receipts is null)
            return true;
        if (receipts.Count == 0
            || receipts[0].DraftRevision != 1
            || receipts.Any(receipt => !CharacterCreationMagicResonanceDigest.IsValidReceipt(
                receipt, workspaceId, persistedContentRevision))
            || receipts.Select(receipt => receipt.IdempotencyKeyDigest)
                .Distinct(StringComparer.Ordinal).Count() != receipts.Count
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                receipts[0].PreviousReceiptDigest,
                CharacterCreationMagicResonanceDigest.ReceiptLedgerRootDigest))
            return false;
        return receipts.Zip(receipts.Skip(1)).All(pair =>
            pair.First.ContentRevision < pair.Second.ContentRevision
            // Other typed auxiliary lanes may advance the workspace between two
            // Magic/Resonance commands. Bind the exact later CAS revision without
            // requiring this lane's receipts to be globally adjacent.
            && pair.Second.PreviousContentRevision >= pair.First.ContentRevision
            && pair.First.DraftRevision < long.MaxValue
            && pair.Second.DraftRevision == pair.First.DraftRevision + 1
            && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                pair.Second.PreviousReceiptDigest, pair.First.ReceiptDigest));
    }

    private static bool IsValidTalent(CharacterCreationMagicResonanceTalentOption item) =>
        IsGuid(item.Identity.PrioritySourceId)
        && IsLabel(item.Identity.TalentSelectionId)
        && IsLabel(item.Identity.TalentValue)
        && IsLabel(item.Rank)
        && IsLabel(item.Name)
        && item.Kind is CharacterCreationMagicResonanceKinds.Mundane
            or CharacterCreationMagicResonanceKinds.Adept
            or CharacterCreationMagicResonanceKinds.Magician
            or CharacterCreationMagicResonanceKinds.MysticAdept
            or CharacterCreationMagicResonanceKinds.AspectedMagician
            or CharacterCreationMagicResonanceKinds.Technomancer
            or CharacterCreationMagicResonanceKinds.ArtificialIntelligence
            or CharacterCreationMagicResonanceKinds.Unsupported
        && item.Magic >= 0
        && item.Resonance >= 0
        && item.Depth >= 0
        && item.SpellBudget >= 0
        && item.ComplexFormBudget >= 0
        && item.AdeptPowerPointBudget >= 0m
        && CharacterCreationMagicResonanceDigest.IsCanonical(item.SourceNodeDigest)
        && IsCanonicalSet(item.SourceAnchorIds)
        && item.Blockers is not null
        && item.IsEnabled == (item.Blockers.Count == 0);

    private static bool IsValidCatalog(
        IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> values,
        string kind)
        => values.Select(item => item.Identity).Distinct().Count() == values.Count
           && IsCanonicallyOrdered(values,
               values.OrderBy(item => item.Name, StringComparer.Ordinal)
                   .ThenBy(item => item.Identity.SourceId, StringComparer.Ordinal))
           && values.All(item => string.Equals(item.Schema, CharacterCreationMagicResonanceSchemas.CatalogOptionV1, StringComparison.Ordinal)
               && string.Equals(item.Identity.Kind, kind, StringComparison.Ordinal)
               && IsGuid(item.Identity.SourceId)
               && IsLabel(item.Name)
               && IsLabel(item.Category)
               && item.PointCost >= 0m
               && item.MaximumLevels >= 1
               && IsLabel(item.SourceBook)
               && IsLabel(item.Page)
               && CharacterCreationMagicResonanceDigest.IsCanonical(item.SourceNodeDigest)
               && IsCanonicalSet(item.SourceAnchorIds)
               && item.Blockers is not null
               && item.IsEnabled == (item.Blockers.Count == 0));

    private static bool IsValidBudget(CharacterCreationMagicResonanceBudgetState budget) =>
        budget is not null
        && IsLabel(budget.Kind)
        && budget.Total >= 0m
        && budget.Used >= 0m
        && budget.Remaining >= 0m
        && budget.Used <= budget.Total
        && budget.Remaining == budget.Total - budget.Used
        && budget.Blockers is not null;

    private static bool HasCanonicalSelections(CharacterCreationMagicResonanceSelections selections) =>
        selections.AdeptPowers is not null
        && selections.Spells is not null
        && selections.ComplexForms is not null
        && selections.AdeptPowers.SequenceEqual(selections.AdeptPowers
            .OrderBy(item => item.Identity.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Identity.SourceId, StringComparer.Ordinal)
            .ThenBy(item => item.Levels))
        && selections.Spells.SequenceEqual(selections.Spells
            .OrderBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.SourceId, StringComparer.Ordinal))
        && selections.ComplexForms.SequenceEqual(selections.ComplexForms
            .OrderBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.SourceId, StringComparer.Ordinal));

    private static bool IsGuid(string? value) =>
        Guid.TryParseExact(value, "D", out Guid id)
        && id != Guid.Empty
        && string.Equals(value, id.ToString("D"), StringComparison.Ordinal);

    private static bool IsCanonicalSet(IReadOnlyList<string> values) =>
        values.Count > 0
        && values.All(IsLabel)
        && values.SequenceEqual(values.Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal);

    private static bool IsLabel(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && value.Length <= 512
        && !value.Any(char.IsControl);

    private static bool IsCanonicallyOrdered<T>(IReadOnlyList<T> actual, IEnumerable<T> expected) =>
        actual.SequenceEqual(expected);
}
