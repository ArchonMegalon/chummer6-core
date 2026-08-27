using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Persistence-boundary proof for the append-only SR5 Priority qualities draft lane.
/// Qualities confirmation is draft-only: the canonical character payload must remain
/// byte-identical until the whole-build finalizer commits it.
/// </summary>
public static class CharacterCreationQualitiesReceiptLedgerIntegrity
{
    public const int MaximumEntries = 16_384;

    public static bool IsValidLedger(
        CharacterWorkspaceId workspaceId,
        long currentContentRevision,
        CharacterCreationQualitiesDraft? draft,
        IReadOnlyList<CharacterCreationQualitiesDraftReceipt>? entries)
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

        var transactionIds = new HashSet<Guid>();
        var idempotencyKeys = new HashSet<string>(StringComparer.Ordinal);
        string previousDigest = CharacterCreationQualitiesRules.ReceiptLedgerRootDigest;
        long previousContentRevision = 0;
        foreach (CharacterCreationQualitiesDraftReceipt? entry in entries)
        {
            if (!CharacterCreationQualitiesRules.IsStructurallyValidReceipt(
                    entry,
                    workspaceId,
                    currentContentRevision)
                || !transactionIds.Add(entry!.TransactionId)
                || !idempotencyKeys.Add(entry.IdempotencyKeyDigest)
                || entry.PreviousContentRevision < previousContentRevision
                || !CharacterCreationQualitiesRules.DigestsEqual(
                    entry.PreviousReceiptDigest,
                    previousDigest))
                return false;
            previousContentRevision = entry.ContentRevision;
            previousDigest = entry.ReceiptDigest;
        }

        CharacterCreationQualitiesDraftReceipt latest = entries[^1];
        return CharacterCreationQualitiesRules.DigestsEqual(latest.DraftDigest, draft.DraftDigest)
               && CharacterCreationQualitiesRules.DigestsEqual(
                   latest.AuthorityDigest,
                   draft.AuthorityDigest)
               && CharacterCreationQualitiesRules.DigestsEqual(
                   latest.RuntimeDigest,
                   draft.RuntimeDigest)
               && CharacterCreationQualitiesRules.DigestsEqual(
                   latest.IdempotencyKeyDigest,
                   draft.LastIdempotencyKeyDigest)
               && CharacterCreationQualitiesRules.DigestsEqual(
                   latest.PreviewDigest,
                   draft.LastPreviewDigest)
               && CharacterCreationQualitiesRules.DigestsEqual(
                   latest.CommandDigest,
                   draft.LastCommandDigest);
    }

    public static bool IsValidAppendTransition(
        CharacterWorkspaceId workspaceId,
        long previousContentRevision,
        long previousSavedRevision,
        long nextContentRevision,
        CharacterCreationQualitiesDraft? currentDraft,
        IReadOnlyList<CharacterCreationQualitiesDraftReceipt>? currentEntries,
        CharacterCreationQualitiesDraft? replacementDraft,
        IReadOnlyList<CharacterCreationQualitiesDraftReceipt>? replacementEntries,
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

        CharacterCreationQualitiesDraftReceipt receipt = replacementEntries[^1];
        if (receipt.PreviousContentRevision != previousContentRevision
            || receipt.ContentRevision != nextContentRevision
            || receipt.PreviousSavedRevision != previousSavedRevision
            || receipt.SavedRevision != nextContentRevision
            || receipt.CharacterDocumentChanged)
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
        CharacterCreationQualitiesDraft draft) =>
        string.Equals(draft.Schema, CharacterCreationQualitiesSchemas.DraftV1, StringComparison.Ordinal)
        && draft.WorkspaceId == workspaceId
        && draft.DraftRevision > 0
        && draft.BaseContentRevision > 0
        && draft.BaseContentRevision < currentContentRevision
        && CharacterCreationQualitiesRules.IsCanonicalDigest(draft.BaseRawCharacterXmlDigest)
        && draft.PrerequisiteDraftRevision > 0
        && CharacterCreationQualitiesRules.IsCanonicalDigest(draft.PrerequisiteDraftDigest)
        && draft.AttributesDraftRevision > 0
        && CharacterCreationQualitiesRules.IsCanonicalDigest(draft.AttributesDraftDigest)
        && CharacterCreationQualitiesRules.IsCanonicalDigest(draft.AuthorityDigest)
        && CharacterCreationQualitiesRules.IsCanonicalDigest(draft.RuntimeDigest)
        && draft.SelectedOptionIds is not null
        && draft.Selections is not null
        && draft.SelectedOptionIds.Count == draft.Selections.Count
        && draft.SelectedOptionIds.Distinct(StringComparer.Ordinal).Count()
            == draft.SelectedOptionIds.Count
        && draft.Selections.All(static selection =>
            selection.SourceId != Guid.Empty
            && !string.IsNullOrWhiteSpace(selection.OptionId)
            && !string.IsNullOrWhiteSpace(selection.SelectionKey)
            && !string.IsNullOrWhiteSpace(selection.Name)
            && selection.Rating > 0
            && CharacterCreationQualitiesRules.IsCanonicalDigest(selection.OptionDigest))
        && draft.PositiveKarmaUsed >= 0
        && draft.NegativeKarmaUsed >= 0
        && draft.KarmaRemaining >= 0
        && draft.SourceAnchorIds is not null
        && draft.SourceAnchorIds.All(static anchor => !string.IsNullOrWhiteSpace(anchor))
        && !draft.CharacterEffectsApplied
        && CharacterCreationQualitiesRules.IsCanonicalDigest(draft.LastIdempotencyKeyDigest)
        && CharacterCreationQualitiesRules.IsCanonicalDigest(draft.LastPreviewDigest)
        && CharacterCreationQualitiesRules.IsCanonicalDigest(draft.LastCommandDigest)
        && CharacterCreationQualitiesRules.IsCanonicalDigest(draft.DraftDigest)
        && CharacterCreationQualitiesRules.DigestsEqual(
            draft.DraftDigest,
            CharacterCreationQualitiesRules.ComputeDraftDigest(draft));
}
