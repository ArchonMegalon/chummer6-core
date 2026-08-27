using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static class CharacterCreationFinalizationReceiptLedgerIntegrity
{
    public static bool IsValidLedger(
        CharacterWorkspaceId workspaceId,
        long persistedContentRevision,
        IReadOnlyList<CharacterCreationFinalizationReceiptLedgerEntry>? ledger)
    {
        if (ledger is null)
            return true;
        if (ledger.Count != 1)
            return false;
        CharacterCreationFinalizationReceiptLedgerEntry entry = ledger[0];
        CharacterCreationFinalizationReceipt receipt = entry.Receipt;
        return receipt is not null
               && string.Equals(receipt.Schema, CharacterCreationFinalizationSchemas.ReceiptV1,
                   StringComparison.Ordinal)
               && receipt.WorkspaceId == workspaceId
               && receipt.PreviousContentRevision > 0
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
            || replacementLedger is not { Count: 1 }
            || nextContentRevision != previousContentRevision + 1
            || !IsValidLedger(workspaceId, nextContentRevision, replacementLedger))
            return false;

        WorkspaceDocumentAuxiliaryState expectedAuxiliary = new(
            CharacterCreationFinalizationReceipts: replacementLedger);
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
        if (!CharacterCreationFinalizationProjector.TryProject(
                current,
                out string expectedXml,
                out CharacterCreationFinalizationDelta[] deltas,
                out string[] sourceAnchorIds,
                out decimal karmaRemaining,
                out decimal startingNuyen,
                out decimal nuyenRemaining,
                out _)
            || !string.Equals(expectedXml, replacementDocument.Content, StringComparison.Ordinal))
            return false;

        CharacterCreationFinalizationReceipt receipt = replacementLedger[0].Receipt;
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
            string.Empty);
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
        string expectedCommandDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = "chummer.sr5.creation-finalization.command.v1",
            Binding = binding,
            PreviewDigest = review.PreviewDigest,
            PlanDigest = plan.PlanDigest,
            ExplicitlyConfirmed = true
        });
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
            or CharacterCreationBuildMethods.LifeModules;

    private static string ComputeDigest(this WorkspaceDocumentAuxiliaryState state) =>
        WorkspaceDocumentAuxiliaryStateDigest.Compute(state);
}
