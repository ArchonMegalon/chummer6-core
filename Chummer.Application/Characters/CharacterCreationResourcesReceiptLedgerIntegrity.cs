using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Persistence-boundary proof for the append-only Resources draft lane. Resources
/// confirmation is deliberately draft-only, so the canonical character document
/// must remain byte-identical until the eventual single creation finalizer runs.
/// </summary>
public static class CharacterCreationResourcesReceiptLedgerIntegrity
{
    public const int MaximumEntries = 4096;

    public static bool IsValidLedger(
        CharacterWorkspaceId workspaceId,
        long currentContentRevision,
        CharacterCreationResourcesDraft? draft,
        IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry>? entries)
    {
        entries ??= [];
        if (string.IsNullOrWhiteSpace(workspaceId.Value)
            || currentContentRevision <= 0
            || entries.Count > MaximumEntries
            || (entries.Count == 0) != (draft is null))
            return false;
        if (draft is null)
            return true;
        if (!IsValidDraft(workspaceId, currentContentRevision, draft))
            return false;

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string previousDigest = CharacterCreationResourcesRules.ReceiptLedgerRootDigest;
        long previousContentRevision = 0;
        long previousDraftRevision = 0;
        foreach (CharacterCreationResourcesReceiptLedgerEntry? entry in entries)
        {
            if (!IsValidEntry(workspaceId, currentContentRevision, entry)
                || !keys.Add(entry!.IdempotencyKeyDigest)
                || !ids.Add(entry.Receipt.ReceiptId)
                || entry.Receipt.PreviousWorkspaceRevision < previousContentRevision
                || entry.Receipt.DraftRevision <= previousDraftRevision
                || !CharacterCreationResourcesRules.DigestsEqual(
                    entry.Receipt.PreviousReceiptDigest,
                    previousDigest))
                return false;
            previousContentRevision = entry.Receipt.WorkspaceRevision;
            previousDraftRevision = entry.Receipt.DraftRevision;
            previousDigest = entry.Receipt.ReceiptDigest;
        }
        CharacterCreationResourcesReceipt latest = entries[^1].Receipt;
        return latest.DraftRevision == draft.DraftRevision
               && CharacterCreationResourcesRules.DigestsEqual(latest.DraftDigest, draft.DraftDigest)
               && CharacterCreationResourcesRules.DigestsEqual(
                   latest.RawCharacterXmlDigest,
                   draft.BaseRawCharacterXmlDigest)
               && CharacterCreationResourcesRules.DigestsEqual(
                   latest.PrerequisiteDraftDigest,
                   draft.PrerequisiteDraftDigest)
               && CharacterCreationResourcesRules.DigestsEqual(
                   latest.AuthorityDigest,
                   draft.AuthorityDigest)
               && CharacterCreationResourcesRules.DigestsEqual(
                   latest.SourceDigest,
                   draft.SourceDigest)
               && CharacterCreationResourcesRules.DigestsEqual(
                   latest.RulesDigest,
                   draft.RulesDigest)
               && CharacterCreationResourcesRules.DigestsEqual(
                   latest.RuntimeDigest,
                   draft.RuntimeDigest)
               && string.Equals(latest.OptionId, draft.SelectedOptionId, StringComparison.Ordinal)
               && latest.KarmaInvestment == draft.KarmaInvestment
               && latest.TotalStartingNuyen == draft.Budget.TotalStartingNuyen
               && latest.RemainingNuyen == draft.Budget.RemainingNuyen
               && CharacterCreationResourcesRules.DigestsEqual(
                   latest.IdempotencyKeyDigest,
                   draft.LastIdempotencyKeyDigest)
               && CharacterCreationResourcesRules.DigestsEqual(
                   latest.PreviewDigest,
                   draft.LastPreviewDigest)
               && CharacterCreationResourcesRules.DigestsEqual(
                   latest.CommandDigest,
                   draft.LastCommandDigest);
    }

    public static bool IsValidAppendTransition(
        CharacterWorkspaceId workspaceId,
        long previousContentRevision,
        long previousSavedRevision,
        long nextContentRevision,
        CharacterCreationResourcesDraft? currentDraft,
        IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry>? currentEntries,
        CharacterCreationResourcesDraft? replacementDraft,
        IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry>? replacementEntries,
        WorkspaceDocument currentDocument,
        WorkspaceDocument replacementDocument)
    {
        currentEntries ??= [];
        replacementEntries ??= [];
        if (nextContentRevision != previousContentRevision + 1
            || replacementEntries.Count != currentEntries.Count + 1
            || replacementEntries.Count > MaximumEntries
            || replacementDraft is null
            || replacementDraft.BaseContentRevision != previousContentRevision
            || replacementDraft.DraftRevision != (currentDraft?.DraftRevision ?? 0) + 1
            || !IsValidLedger(
                workspaceId,
                nextContentRevision,
                replacementDraft,
                replacementEntries))
            return false;
        for (int index = 0; index < currentEntries.Count; index++)
        {
            if (!CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    currentEntries[index],
                    replacementEntries[index]))
                return false;
        }
        CharacterCreationResourcesReceipt receipt = replacementEntries[^1].Receipt;
        if (receipt.PreviousWorkspaceRevision != previousContentRevision
            || receipt.WorkspaceRevision != nextContentRevision
            || receipt.PreviousSavedRevision != previousSavedRevision
            || receipt.SavedRevision != nextContentRevision
            || !CharacterCreationResourcesRules.DigestsEqual(
                receipt.RawCharacterXmlDigest,
                replacementDraft.BaseRawCharacterXmlDigest))
            return false;
        return string.Equals(currentDocument.Content, replacementDocument.Content, StringComparison.Ordinal)
               && currentDocument.Format == replacementDocument.Format
               && string.Equals(currentDocument.RulesetId, replacementDocument.RulesetId, StringComparison.Ordinal)
               && currentDocument.SchemaVersion == replacementDocument.SchemaVersion
               && string.Equals(currentDocument.PayloadKind, replacementDocument.PayloadKind, StringComparison.Ordinal);
    }

    private static bool IsValidDraft(
        CharacterWorkspaceId workspaceId,
        long currentContentRevision,
        CharacterCreationResourcesDraft draft) =>
        string.Equals(draft.Schema, CharacterCreationResourcesSchemas.DraftV1, StringComparison.Ordinal)
        && draft.WorkspaceId == workspaceId
        && draft.DraftRevision > 0
        && draft.BaseContentRevision > 0
        && draft.BaseContentRevision < currentContentRevision
        && CharacterCreationResourcesRules.IsCanonicalDigest(draft.BaseRawCharacterXmlDigest)
        && draft.PrerequisiteDraftRevision > 0
        && CharacterCreationResourcesRules.IsCanonicalDigest(draft.PrerequisiteDraftDigest)
        && CharacterCreationResourcesRules.IsCanonicalDigest(draft.AuthorityDigest)
        && CharacterCreationResourcesRules.IsCanonicalDigest(draft.SourceDigest)
        && CharacterCreationResourcesRules.IsCanonicalDigest(draft.RulesDigest)
        && CharacterCreationResourcesRules.IsCanonicalDigest(draft.RuntimeDigest)
        && !string.IsNullOrWhiteSpace(draft.SelectedOptionId)
        && draft.KarmaInvestment >= 0
        && draft.Budget is not null
        && draft.Budget.KarmaInvestment == draft.KarmaInvestment
        && draft.Budget.TotalStartingNuyen >= 0m
        && draft.Budget.KnownPurchaseCost >= 0m
        && draft.Budget.RemainingNuyen >= 0m
        && draft.Budget.Overspend == 0m
        && draft.Budget.IsExact
        && draft.Budget.Blockers.Count == 0
        && draft.SourceAnchorIds is { Count: > 0 }
        && draft.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor))
        && draft.FinalizationContribution is not null
        && draft.SourceAnchorIds.SequenceEqual(
            draft.FinalizationContribution.SourceAnchorIds,
            StringComparer.Ordinal)
        && !draft.CharacterEffectsApplied
        && CharacterCreationResourcesRules.IsCanonicalDigest(draft.LastIdempotencyKeyDigest)
        && CharacterCreationResourcesRules.IsCanonicalDigest(draft.LastPreviewDigest)
        && CharacterCreationResourcesRules.IsCanonicalDigest(draft.LastCommandDigest)
        && IsValidContribution(draft.FinalizationContribution, draft)
        && CharacterCreationResourcesRules.IsCanonicalDigest(draft.DraftDigest)
        && CharacterCreationResourcesRules.DigestsEqual(
            draft.DraftDigest,
            CharacterCreationResourcesRules.ComputeDraftDigest(draft));

    private static bool IsValidContribution(
        CharacterCreationResourcesFinalizationContribution contribution,
        CharacterCreationResourcesDraft draft) =>
        contribution is not null
        && string.Equals(
            contribution.Schema,
            CharacterCreationResourcesSchemas.ContributionV1,
            StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(contribution.PriorityRank)
        && Guid.TryParseExact(contribution.PrioritySourceId, "D", out Guid sourceId)
        && sourceId != Guid.Empty
        && contribution.StartingNuyen == draft.Budget.PriorityNuyen
        && contribution.NuyenKarma == draft.KarmaInvestment
        && CharacterCreationResourcesRules.DigestsEqual(
            contribution.ExpectedRawCharacterXmlDigest,
            draft.BaseRawCharacterXmlDigest)
        && contribution.SourceAnchorIds is { Count: > 0 }
        && CharacterCreationResourcesRules.IsCanonicalDigest(contribution.ContributionDigest)
        && CharacterCreationResourcesRules.DigestsEqual(
            contribution.ContributionDigest,
            CharacterCreationResourcesRules.ComputeContributionDigest(contribution));

    private static bool IsValidEntry(
        CharacterWorkspaceId workspaceId,
        long currentContentRevision,
        CharacterCreationResourcesReceiptLedgerEntry? entry)
    {
        if (entry?.Receipt is not CharacterCreationResourcesReceipt receipt
            || receipt.WorkspaceId != workspaceId
            || receipt.WorkspaceRevision > currentContentRevision
            || receipt.PreviousWorkspaceRevision <= 0
            || receipt.WorkspaceRevision != receipt.PreviousWorkspaceRevision + 1
            || receipt.PreviousSavedRevision < 0
            || receipt.PreviousSavedRevision > receipt.PreviousWorkspaceRevision
            || receipt.SavedRevision != receipt.WorkspaceRevision
            || !string.Equals(receipt.Schema, CharacterCreationResourcesSchemas.ReceiptV1, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(receipt.ReceiptId)
            || receipt.ReceiptId.Length != 43
            || !receipt.ReceiptId.StartsWith("creation-resources-", StringComparison.Ordinal)
            || !CharacterCreationResourcesRules.IsCanonicalDigest(entry.IdempotencyKeyDigest)
            || !CharacterCreationResourcesRules.IsCanonicalDigest(entry.CommandDigest)
            || !CharacterCreationResourcesRules.DigestsEqual(entry.IdempotencyKeyDigest, receipt.IdempotencyKeyDigest)
            || !CharacterCreationResourcesRules.DigestsEqual(entry.CommandDigest, receipt.CommandDigest)
            || !CharacterCreationResourcesRules.IsCanonicalDigest(receipt.RawCharacterXmlDigest)
            || !CharacterCreationResourcesRules.IsCanonicalDigest(receipt.PrerequisiteDraftDigest)
            || !CharacterCreationResourcesRules.IsCanonicalDigest(receipt.AuthorityDigest)
            || !CharacterCreationResourcesRules.IsCanonicalDigest(receipt.SourceDigest)
            || !CharacterCreationResourcesRules.IsCanonicalDigest(receipt.RulesDigest)
            || !CharacterCreationResourcesRules.IsCanonicalDigest(receipt.RuntimeDigest)
            || string.IsNullOrWhiteSpace(receipt.OptionId)
            || receipt.KarmaInvestment < 0
            || receipt.TotalStartingNuyen < 0m
            || receipt.RemainingNuyen < 0m
            || receipt.DraftRevision <= 0
            || !CharacterCreationResourcesRules.IsCanonicalDigest(receipt.DraftDigest)
            || !CharacterCreationResourcesRules.IsCanonicalDigest(receipt.PreviewDigest)
            || !CharacterCreationResourcesRules.IsCanonicalDigest(receipt.PreviousReceiptDigest)
            || receipt.CharacterDocumentChanged
            || !CharacterCreationResourcesRules.IsCanonicalDigest(receipt.ReceiptDigest)
            || !CharacterCreationResourcesRules.DigestsEqual(
                receipt.ReceiptDigest,
                CharacterCreationResourcesRules.ComputeReceiptDigest(receipt)))
            return false;
        string expectedId = "creation-resources-" + entry.CommandDigest["sha256:".Length..][..24];
        return string.Equals(receipt.ReceiptId, expectedId, StringComparison.Ordinal);
    }
}
