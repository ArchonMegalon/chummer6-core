using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Verifies the append-only creation Gear draft lane. Every accepted transition keeps
/// the character payload byte-identical; only the auxiliary finalization contribution
/// and its receipt chain may advance.
/// </summary>
public static class CharacterCreationGearReceiptLedgerIntegrity
{
    public const int MaximumEntries = 4096;

    public static bool IsValidLedger(
        CharacterWorkspaceId workspaceId,
        long currentContentRevision,
        CharacterCreationGearDraft? draft,
        IReadOnlyList<CharacterCreationGearReceiptLedgerEntry>? entries)
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
        string previousDigest = CharacterCreationGearRules.ReceiptLedgerRootDigest;
        long previousRevision = 0;
        long previousDraftRevision = 0;
        foreach (CharacterCreationGearReceiptLedgerEntry? entry in entries)
        {
            if (!IsValidEntry(workspaceId, currentContentRevision, entry)
                || !keys.Add(entry!.IdempotencyKeyDigest)
                || !ids.Add(entry.Receipt.ReceiptId)
                || entry.Receipt.PreviousWorkspaceRevision < previousRevision
                || entry.Receipt.DraftRevision <= previousDraftRevision
                || !CharacterCreationGearRules.DigestsEqual(
                    entry.Receipt.PreviousReceiptDigest,
                    previousDigest))
                return false;
            previousRevision = entry.Receipt.WorkspaceRevision;
            previousDraftRevision = entry.Receipt.DraftRevision;
            previousDigest = entry.Receipt.ReceiptDigest;
        }

        CharacterCreationGearReceipt latest = entries[^1].Receipt;
        return latest.DraftRevision == draft.DraftRevision
               && CharacterCreationGearRules.DigestsEqual(latest.DraftDigest, draft.DraftDigest)
               && CharacterCreationGearRules.DigestsEqual(
                   latest.RawCharacterXmlDigest,
                   draft.BaseRawCharacterXmlDigest)
               && latest.ResourcesDraftRevision == draft.ResourcesDraftRevision
               && CharacterCreationGearRules.DigestsEqual(
                   latest.ResourcesDraftDigest,
                   draft.ResourcesDraftDigest)
               && CharacterCreationGearRules.DigestsEqual(
                   latest.AuthorityDigest,
                   draft.AuthorityDigest)
               && CharacterCreationGearRules.DigestsEqual(latest.SourceDigest, draft.SourceDigest)
               && CharacterCreationGearRules.DigestsEqual(latest.RulesDigest, draft.RulesDigest)
               && CharacterCreationGearRules.DigestsEqual(latest.RuntimeDigest, draft.RuntimeDigest)
               && latest.LineCount == draft.Lines.Count
               && latest.BasketCost == draft.Budget.BasketCost
               && latest.RemainingNuyen == draft.Budget.RemainingNuyen
               && CharacterCreationGearRules.DigestsEqual(
                   latest.IdempotencyKeyDigest,
                   draft.LastIdempotencyKeyDigest)
               && CharacterCreationGearRules.DigestsEqual(latest.PreviewDigest, draft.LastPreviewDigest)
               && CharacterCreationGearRules.DigestsEqual(latest.CommandDigest, draft.LastCommandDigest);
    }

    public static bool IsValidAppendTransition(
        CharacterWorkspaceId workspaceId,
        long previousContentRevision,
        long previousSavedRevision,
        long nextContentRevision,
        CharacterCreationGearDraft? currentDraft,
        IReadOnlyList<CharacterCreationGearReceiptLedgerEntry>? currentEntries,
        CharacterCreationGearDraft? replacementDraft,
        IReadOnlyList<CharacterCreationGearReceiptLedgerEntry>? replacementEntries,
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
            || !IsValidLedger(workspaceId, nextContentRevision, replacementDraft, replacementEntries))
            return false;
        for (int index = 0; index < currentEntries.Count; index++)
        {
            if (!CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    currentEntries[index], replacementEntries[index]))
                return false;
        }
        CharacterCreationGearReceipt receipt = replacementEntries[^1].Receipt;
        return receipt.PreviousWorkspaceRevision == previousContentRevision
               && receipt.WorkspaceRevision == nextContentRevision
               && receipt.PreviousSavedRevision == previousSavedRevision
               && receipt.SavedRevision == nextContentRevision
               && !receipt.CharacterDocumentChanged
               && string.Equals(currentDocument.Content, replacementDocument.Content, StringComparison.Ordinal)
               && currentDocument.Format == replacementDocument.Format
               && string.Equals(currentDocument.RulesetId, replacementDocument.RulesetId, StringComparison.Ordinal)
               && currentDocument.SchemaVersion == replacementDocument.SchemaVersion
               && string.Equals(currentDocument.PayloadKind, replacementDocument.PayloadKind, StringComparison.Ordinal);
    }

    private static bool IsValidDraft(
        CharacterWorkspaceId workspaceId,
        long currentContentRevision,
        CharacterCreationGearDraft draft) =>
        string.Equals(draft.Schema, CharacterCreationGearSchemas.DraftV1, StringComparison.Ordinal)
        && draft.WorkspaceId == workspaceId
        && draft.DraftRevision > 0
        && draft.BaseContentRevision > 0
        && draft.BaseContentRevision < currentContentRevision
        && CharacterCreationGearRules.IsCanonicalDigest(draft.BaseRawCharacterXmlDigest)
        && draft.ResourcesDraftRevision > 0
        && CharacterCreationGearRules.IsCanonicalDigest(draft.ResourcesDraftDigest)
        && CharacterCreationGearRules.IsCanonicalDigest(draft.AuthorityDigest)
        && CharacterCreationGearRules.IsCanonicalDigest(draft.SourceDigest)
        && CharacterCreationGearRules.IsCanonicalDigest(draft.RulesDigest)
        && CharacterCreationGearRules.IsCanonicalDigest(draft.RuntimeDigest)
        && draft.Lines is { Count: <= 4096 }
        && draft.Lines.All(IsValidLine)
        && draft.Lines.Select(item => item.OptionId).Distinct(StringComparer.Ordinal).Count()
           == draft.Lines.Count
        && draft.Budget is { IsExact: true, Overspend: 0m }
        && draft.Budget.Blockers.Count == 0
        && TryComputeBasketCost(draft.Lines, out decimal basketCost)
        && draft.Budget.BasketCost == basketCost
        && draft.Budget.RemainingNuyen
           == draft.Budget.TotalStartingNuyen - draft.Budget.BasketCost
        && draft.FinalizationContribution is not null
        && IsValidContribution(draft.FinalizationContribution, draft)
        && !draft.CharacterEffectsApplied
        && CharacterCreationGearRules.IsCanonicalDigest(draft.LastIdempotencyKeyDigest)
        && CharacterCreationGearRules.IsCanonicalDigest(draft.LastPreviewDigest)
        && CharacterCreationGearRules.IsCanonicalDigest(draft.LastCommandDigest)
        && CharacterCreationGearRules.IsCanonicalDigest(draft.DraftDigest)
        && CharacterCreationGearRules.DigestsEqual(
            draft.DraftDigest,
            CharacterCreationGearRules.ComputeDraftDigest(draft));

    private static bool IsValidLine(CharacterCreationGearLine line) =>
        line is not null
        && !string.IsNullOrWhiteSpace(line.OptionId)
        && line.SourceId != Guid.Empty
        && line.Quantity > 0
        && line.PackageQuantity > 0
        && line.PackageCost >= 0m
        && TryComputeLineCost(line, out decimal expectedCost)
        && line.TotalCost == expectedCost
        && line.Availability >= 0
        && line.SourceAnchorIds is { Count: > 0 }
        && line.SourceNodeXml is
            { Length: > 0 and <= CharacterCreationGearRules.MaximumSourceNodeLength }
        && CharacterCreationGearRules.IsCanonicalDigest(line.SourceNodeDigest)
        && CharacterCreationGearRules.DigestsEqual(
            line.SourceNodeDigest,
            CharacterCreationGearRules.ComputeSourceNodeDigest(line.SourceNodeXml))
        && CharacterCreationGearRules.IsCanonicalDigest(line.LineDigest)
        && CharacterCreationGearRules.DigestsEqual(
            line.LineDigest,
            CharacterCreationGearRules.ComputeLineDigest(line));

    private static bool IsValidContribution(
        CharacterCreationGearFinalizationContribution contribution,
        CharacterCreationGearDraft draft) =>
        string.Equals(
            contribution.Schema,
            CharacterCreationGearSchemas.ContributionV1,
            StringComparison.Ordinal)
        && CharacterCreationGearRules.DigestsEqual(
            contribution.ExpectedRawCharacterXmlDigest,
            draft.BaseRawCharacterXmlDigest)
        && contribution.ResourcesDraftRevision == draft.ResourcesDraftRevision
        && CharacterCreationGearRules.DigestsEqual(
            contribution.ResourcesDraftDigest,
            draft.ResourcesDraftDigest)
        && contribution.Lines.Select(item => item.LineDigest)
            .SequenceEqual(draft.Lines.Select(item => item.LineDigest), StringComparer.Ordinal)
        && contribution.TotalCost == draft.Budget.BasketCost
        && contribution.SourceAnchorIds is { Count: > 0 }
        && CharacterCreationGearRules.IsCanonicalDigest(contribution.ContributionDigest)
        && CharacterCreationGearRules.DigestsEqual(
            contribution.ContributionDigest,
            CharacterCreationGearRules.ComputeContributionDigest(contribution));

    private static bool IsValidEntry(
        CharacterWorkspaceId workspaceId,
        long currentContentRevision,
        CharacterCreationGearReceiptLedgerEntry? entry)
    {
        if (entry?.Receipt is not CharacterCreationGearReceipt receipt
            || receipt.WorkspaceId != workspaceId
            || receipt.WorkspaceRevision > currentContentRevision
            || receipt.PreviousWorkspaceRevision <= 0
            || receipt.WorkspaceRevision != receipt.PreviousWorkspaceRevision + 1
            || receipt.PreviousSavedRevision < 0
            || receipt.PreviousSavedRevision > receipt.PreviousWorkspaceRevision
            || receipt.SavedRevision != receipt.WorkspaceRevision
            || !string.Equals(receipt.Schema, CharacterCreationGearSchemas.ReceiptV1, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(receipt.ReceiptId)
            || !receipt.ReceiptId.StartsWith("creation-gear-", StringComparison.Ordinal)
            || receipt.ReceiptId.Length != 38
            || !CharacterCreationGearRules.IsCanonicalDigest(entry.IdempotencyKeyDigest)
            || !CharacterCreationGearRules.IsCanonicalDigest(entry.CommandDigest)
            || !CharacterCreationGearRules.DigestsEqual(entry.IdempotencyKeyDigest, receipt.IdempotencyKeyDigest)
            || !CharacterCreationGearRules.DigestsEqual(entry.CommandDigest, receipt.CommandDigest)
            || !CharacterCreationGearRules.IsCanonicalDigest(receipt.RawCharacterXmlDigest)
            || receipt.ResourcesDraftRevision <= 0
            || !CharacterCreationGearRules.IsCanonicalDigest(receipt.ResourcesDraftDigest)
            || !CharacterCreationGearRules.IsCanonicalDigest(receipt.AuthorityDigest)
            || !CharacterCreationGearRules.IsCanonicalDigest(receipt.SourceDigest)
            || !CharacterCreationGearRules.IsCanonicalDigest(receipt.RulesDigest)
            || !CharacterCreationGearRules.IsCanonicalDigest(receipt.RuntimeDigest)
            || receipt.LineCount < 0
            || receipt.BasketCost < 0m
            || receipt.RemainingNuyen < 0m
            || receipt.DraftRevision <= 0
            || !CharacterCreationGearRules.IsCanonicalDigest(receipt.DraftDigest)
            || !CharacterCreationGearRules.IsCanonicalDigest(receipt.PreviewDigest)
            || !CharacterCreationGearRules.IsCanonicalDigest(receipt.PreviousReceiptDigest)
            || receipt.CharacterDocumentChanged
            || !CharacterCreationGearRules.IsCanonicalDigest(receipt.ReceiptDigest))
            return false;
        return CharacterCreationGearRules.DigestsEqual(
            receipt.ReceiptDigest,
            CharacterCreationGearRules.ComputeReceiptDigest(receipt));
    }

    private static bool TryComputeBasketCost(
        IReadOnlyList<CharacterCreationGearLine> lines,
        out decimal total)
    {
        total = 0m;
        try
        {
            foreach (CharacterCreationGearLine line in lines)
                total = checked(total + line.TotalCost);
            return true;
        }
        catch (OverflowException)
        {
            total = 0m;
            return false;
        }
    }

    private static bool TryComputeLineCost(
        CharacterCreationGearLine line,
        out decimal total)
    {
        total = 0m;
        try
        {
            total = checked(line.PackageCost * line.Quantity / line.PackageQuantity);
            return true;
        }
        catch (OverflowException)
        {
            total = 0m;
            return false;
        }
    }
}
