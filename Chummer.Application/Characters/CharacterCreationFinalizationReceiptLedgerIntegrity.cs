using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static class CharacterCreationFinalizationReceiptLedgerIntegrity
{
    /// <summary>
    /// Archive the complete consumed Creation graph, including each step's
    /// confirmation history. Active draft/receipt validators stay unchanged;
    /// their exact original graph is validated at its original revision.
    /// </summary>
    internal static WorkspaceDocumentAuxiliaryState ConsumeDrafts(
        WorkspaceDocumentAuxiliaryState current,
        IReadOnlyList<CharacterCreationFinalizationReceiptLedgerEntry> finalizationReceipts,
        CharacterCreationFinalizationStartingCash? startingCash = null) => new(
            CharacterCreationFinalizationReceipts: finalizationReceipts,
            CharacterCreationFinalizationArchive: new(current) { StartingCash = startingCash });

    public static bool IsValidArchive(
        CharacterWorkspaceId workspaceId,
        long currentContentRevision,
        CharacterCreationFinalizationArchive archive,
        IReadOnlyList<CharacterCreationFinalizationReceiptLedgerEntry>? receipts)
    {
        ArgumentNullException.ThrowIfNull(archive);
        return archive.State is not null
               && archive.State.CharacterCreationFinalizationArchive is null
               && archive.State.CharacterCreationFinalizationReceipts is null
               && receipts is { Count: 1 }
               && IsValidLedger(workspaceId, currentContentRevision, receipts)
               && CharacterCreationKarmaFinalizationTransaction.IsValidArchive(workspaceId, archive, receipts[0].Receipt)
               && IsValidStartingCashArchive(archive, receipts[0].Receipt)
               && string.Equals(archive.State.ComputeDigest(),
                   receipts[0].Receipt.PreviousAuxiliaryStateDigest, StringComparison.Ordinal);
    }

    private static bool IsValidStartingCashArchive(CharacterCreationFinalizationArchive archive,
        CharacterCreationFinalizationReceipt receipt)
    {
        if (archive.StartingCash is not { } cash)
            return receipt.StartingCash is null && receipt.StartingCashAuthorityDigest is null;
        var bootstrap = archive.State.CharacterCreationBootstrapBinding;
        return receipt.BuildMethod is CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen
            && bootstrap is not null && CharacterCreationFinalizationStartingCashRules.IsValid(cash)
            && cash.Source.SettingsProfileId == bootstrap.SettingsProfileId
            && cash.Source.RawProfileInputsDigest == bootstrap.RawProfileInputsDigest
            && cash.Source.SettingsProfileId == receipt.CarryoverPolicy?.SettingsProfileId
            && cash.Source.RawProfileInputsDigest == receipt.CarryoverPolicy.RawProfileInputsDigest
            && cash.Choice == receipt.StartingCash && cash.AuthorityDigest == receipt.StartingCashAuthorityDigest;
    }

    /// <summary>Read-only receipt recovery. Never use historical drafts to evaluate a new mutation.</summary>
    internal static bool TryReadReceiptHistory(WorkspaceStoredDocument workspace,
        out WorkspaceDocumentAuxiliaryState history, out long historyRevision)
    {
        history = workspace.Document.AuxiliaryState;
        historyRevision = workspace.ContentRevision;
        if (history.CharacterCreationFinalizationArchive is not { } archive)
            return true;
        if (!IsValidArchive(workspace.Id, workspace.ContentRevision, archive,
                history.CharacterCreationFinalizationReceipts))
            return false;
        historyRevision = history.CharacterCreationFinalizationReceipts![0].Receipt.PreviousContentRevision;
        history = archive.State;
        return true;
    }

    public static bool IsValidLedger(
        CharacterWorkspaceId workspaceId,
        long persistedContentRevision,
        IReadOnlyList<CharacterCreationFinalizationReceiptLedgerEntry>? ledger)
    {
        if (ledger is null)
            return true;
        if (ledger.Count != 1)
            return false;
        CharacterCreationFinalizationReceiptLedgerEntry? entry = ledger[0];
        if (entry?.Receipt is null)
            return false;
        CharacterCreationFinalizationReceipt receipt = entry.Receipt;
        return receipt is not null
               && string.Equals(receipt.Schema, CharacterCreationFinalizationSchemas.ReceiptV1,
                   StringComparison.Ordinal)
               && receipt.WorkspaceId == workspaceId
               && receipt.PreviousContentRevision is > 0 and < long.MaxValue
               && receipt.ContentRevision == receipt.PreviousContentRevision + 1
               && receipt.ContentRevision <= persistedContentRevision
               && receipt.SavedRevision == receipt.ContentRevision
               && receipt.PreviousSavedRevision >= 0
               && receipt.PreviousSavedRevision <= receipt.PreviousContentRevision
               && receipt.CharacterCreated
               && receipt.RequiresFreshCareerReopen
               && CharacterCreationFinalizationDigest.IsCanonical(entry.IdempotencyKeyDigest)
               && CharacterCreationFinalizationDigest.IsCanonical(entry.CommandDigest)
               && CharacterCreationFinalizationDigest.EqualsFixedTime(
                   entry.IdempotencyKeyDigest, receipt.IdempotencyKeyDigest)
               && CharacterCreationFinalizationDigest.EqualsFixedTime(
                   entry.CommandDigest, receipt.CommandDigest)
               && CharacterCreationFinalizationDigest.IsCanonical(receipt.PreviousRawCharacterXmlDigest)
               && CharacterCreationFinalizationDigest.IsCanonical(receipt.RawCharacterXmlDigest)
               && receipt.PreviousAuxiliaryStateDigest is { Length: 64 }
               && receipt.PreviousAuxiliaryStateDigest.All(static character =>
                   character is >= '0' and <= '9' or >= 'a' and <= 'f')
               && CharacterCreationFinalizationDigest.IsCanonical(receipt.AuthorityDigest)
               && CharacterCreationFinalizationDigest.IsCanonical(receipt.PreviewDigest)
               && CharacterCreationFinalizationDigest.IsCanonical(receipt.PlanDigest)
               && CharacterCreationFinalizationBuildMethodIsKnown(receipt.BuildMethod)
               && (receipt.CarryoverPolicy is null
                   || CharacterCreationKarmaFinalizationBudgetRules.IsValidPolicy(receipt.CarryoverPolicy))
               && (receipt.StartingCash is null ? receipt.StartingCashAuthorityDigest is null
                   : receipt.StartingCash.DiceTotal >= 0
                       && CharacterCreationFinalizationDigest.IsCanonical(receipt.StartingCash.SourceAuthorityDigest)
                       && CharacterCreationFinalizationDigest.IsCanonical(receipt.StartingCashAuthorityDigest))
               && CharacterCreationFinalizationDigest.EqualsFixedTime(
                   receipt.PreviousReceiptDigest,
                   CharacterCreationFinalizationDigest.ReceiptLedgerRootDigest)
               && CharacterCreationFinalizationDigest.IsCanonical(receipt.ReceiptDigest)
               && CharacterCreationFinalizationDigest.EqualsFixedTime(
                   receipt.ReceiptDigest,
                   CharacterCreationFinalizationDigest.ComputeReceiptDigest(receipt));
    }

    public static bool IsValidTransition(
        CharacterWorkspaceId workspaceId,
        long previousContentRevision,
        long previousSavedRevision,
        long nextContentRevision,
        WorkspaceDocument currentDocument,
        WorkspaceDocument replacementDocument)
    {
        IReadOnlyList<CharacterCreationFinalizationReceiptLedgerEntry>? currentLedger =
            currentDocument.AuxiliaryState.CharacterCreationFinalizationReceipts;
        IReadOnlyList<CharacterCreationFinalizationReceiptLedgerEntry>? replacementLedger =
            replacementDocument.AuxiliaryState.CharacterCreationFinalizationReceipts;
        if (currentLedger is not null
            || currentDocument.AuxiliaryState.CharacterCreationFinalizationArchive is not null
            || replacementLedger is not { Count: 1 }
            || nextContentRevision != previousContentRevision + 1
            || !IsValidLedger(workspaceId, nextContentRevision, replacementLedger))
            return false;

        var startingCash = replacementDocument.AuxiliaryState.CharacterCreationFinalizationArchive?.StartingCash;
        WorkspaceDocumentAuxiliaryState expectedAuxiliary = ConsumeDrafts(
            currentDocument.AuxiliaryState, replacementLedger, startingCash);
        if (!string.Equals(
                expectedAuxiliary.ComputeDigest(),
                replacementDocument.AuxiliaryStateDigest,
                StringComparison.Ordinal))
            return false;

        var current = new WorkspaceStoredDocument(
            workspaceId,
            currentDocument,
            previousContentRevision,
            previousSavedRevision,
            DateTimeOffset.UnixEpoch);
        CharacterCreationFinalizationReceipt receipt = replacementLedger[0].Receipt;
        if (startingCash is null || !IsValidStartingCashArchive(
                replacementDocument.AuxiliaryState.CharacterCreationFinalizationArchive!, receipt)) return false;
        if (!CharacterCreationFinalizationProjector.TryProject(
                current,
                out string expectedXml,
                out CharacterCreationFinalizationDelta[] deltas,
                out string[] sourceAnchorIds,
                out decimal karmaRemaining,
                out decimal startingNuyen,
                out decimal nuyenRemaining,
                out _, receipt.CarryoverPolicy, startingCash)
            || !string.Equals(expectedXml, replacementDocument.Content, StringComparison.Ordinal))
            return false;

        var binding = new CharacterCreationFinalizationBinding(
            workspaceId,
            previousContentRevision,
            previousSavedRevision,
            CharacterCreationFinalizationProjector.ComputeRawCharacterXmlDigest(currentDocument.Content),
            currentDocument.AuxiliaryStateDigest,
            receipt.BuildMethod,
            receipt.AuthorityDigest);
        var planCandidate = new CharacterCreationFinalizationPlan(
            CharacterCreationFinalizationSchemas.PlanV1,
            binding,
            deltas,
            karmaRemaining,
            startingNuyen,
            nuyenRemaining,
            sourceAnchorIds,
            CharacterCreationFinalizationProjector.ComputeRawCharacterXmlDigest(expectedXml),
            string.Empty)
        {
            CarryoverPolicy = receipt.CarryoverPolicy,
            StartingCash = startingCash.Choice,
            StartingCashAuthorityDigest = startingCash.AuthorityDigest
        };
        CharacterCreationFinalizationPlan plan = planCandidate with
        {
            PlanDigest = CharacterCreationFinalizationDigest.Compute(
                planCandidate with { PlanDigest = string.Empty })
        };
        var reviewCandidate = new CharacterCreationFinalizationReview(
            CharacterCreationFinalizationSchemas.ReviewV1,
            binding,
            plan,
            deltas,
            Blockers: [],
            sourceAnchorIds,
            RequiresExplicitConfirmation: true,
            CanConfirm: true,
            PreviewDigest: string.Empty);
        CharacterCreationFinalizationReview review = reviewCandidate with
        {
            PreviewDigest = CharacterCreationFinalizationDigest.Compute(
                reviewCandidate with { PreviewDigest = string.Empty })
        };
        string expectedCommandDigest = CharacterCreationFinalizationService.ComputeCommandDigest(new(
            binding, review.PreviewDigest, plan.PlanDigest, "transition-validation", true) { StartingCash = startingCash.Choice });
        string expectedReceiptId = CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = CharacterCreationFinalizationSchemas.ReceiptV1,
            Id = workspaceId,
            idempotencyDigest = receipt.IdempotencyKeyDigest,
            commandDigest = expectedCommandDigest
        });
        return receipt.PreviousContentRevision == previousContentRevision
               && receipt.ContentRevision == nextContentRevision
               && receipt.PreviousSavedRevision == previousSavedRevision
               && receipt.SavedRevision == nextContentRevision
               && CharacterCreationFinalizationDigest.EqualsFixedTime(
                   receipt.PreviousRawCharacterXmlDigest,
                   CharacterCreationFinalizationProjector.ComputeRawCharacterXmlDigest(
                       currentDocument.Content))
               && CharacterCreationFinalizationDigest.EqualsFixedTime(
                   receipt.RawCharacterXmlDigest,
                   CharacterCreationFinalizationProjector.ComputeRawCharacterXmlDigest(
                       replacementDocument.Content))
               && string.Equals(
                   receipt.PreviousAuxiliaryStateDigest,
                   currentDocument.AuxiliaryStateDigest,
                   StringComparison.Ordinal)
               && CharacterCreationFinalizationDigest.EqualsFixedTime(
                   receipt.PlanDigest,
                   plan.PlanDigest)
               && CharacterCreationFinalizationDigest.EqualsFixedTime(
                   receipt.PreviewDigest,
                   review.PreviewDigest)
               && CharacterCreationFinalizationDigest.EqualsFixedTime(
                   receipt.CommandDigest,
                   expectedCommandDigest)
               && CharacterCreationFinalizationDigest.EqualsFixedTime(
                   receipt.ReceiptId,
                   expectedReceiptId);
    }

    public static CharacterCreationFinalizationReceiptLedgerEntry? Find(
        IReadOnlyList<CharacterCreationFinalizationReceiptLedgerEntry>? ledger,
        string idempotencyKeyDigest) => ledger?.SingleOrDefault(entry =>
        CharacterCreationFinalizationDigest.EqualsFixedTime(
            entry.IdempotencyKeyDigest,
            idempotencyKeyDigest));

    private static bool CharacterCreationFinalizationBuildMethodIsKnown(string buildMethod) =>
        buildMethod is CharacterCreationBuildMethods.Priority
            or CharacterCreationBuildMethods.SumToTen
            or CharacterCreationBuildMethods.Karma
            or CharacterCreationBuildMethods.LifeModules;

    private static string ComputeDigest(this WorkspaceDocumentAuxiliaryState state) =>
        WorkspaceDocumentAuxiliaryStateDigest.Compute(state);
}
