using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.LifeModules;

public sealed partial class LifeModuleOriginDossierService
{
    // Called inside the existing atomic Foundation commit. Never write the
    // chapter separately after mechanics; a lost phone checkpoint is recoverable.
    internal static LifeModuleDecisionAcceptance? SealAcceptanceChapter(
        LifeModuleDecisionAuthorityStep before,
        LifeModuleAcceptedDecisionReceipt receipt,
        LifeModuleDecisionAuthorityStep next)
    {
        if (!TryCreateTurn(before, out var turn) || turn is null)
            return null;
        var choice = turn.LegalChoices.SingleOrDefault(item => item.ChoiceId == receipt.ChoiceId);
        if (choice is null)
            return null;
        var chapter = CreateChapter(turn, choice, receipt);
        receipt = receipt with { ChapterDigest = chapter.ChapterDigest };
        receipt = receipt with
        { ReceiptDigest = LifeModuleDecisionAcceptanceIntegrity.ComputeReceiptDigest(receipt) };
        return new(receipt, next) { Chapter = chapter };
    }

    private LifeModuleOriginDossierResult<OriginStoryArcSeed> RecoverProjection(
        LifeModuleDecisionAuthorityStep step, LifeModuleNarrativeTurnSeed turn)
    {
        if (_authority is not ILifeModuleDecisionHistoryAuthority history)
            return Blocked<OriginStoryArcSeed>(LifeModuleOriginDossierOutcomes.Missing,
                "life-module-origin-history-unavailable");
        var loaded = history.LoadHistory(step.WorkspaceId);
        if (!IsAuthoritySuccess(loaded.Outcome))
            return FromAuthority<IReadOnlyList<LifeModuleDecisionAcceptance>, OriginStoryArcSeed>(loaded);
        if (loaded.Value is not { Count: > 0 } entries)
            return Blocked<OriginStoryArcSeed>(LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.AuthorityInvalid);
        if (!LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(
                new CharacterWorkspaceId(step.WorkspaceId), step.WorkspaceRevision, entries)
            || !DigestsEqual(LifeModuleDecisionAcceptanceIntegrity.ComputeCanonicalDigest(entries[^1].NextStep),
                LifeModuleDecisionAcceptanceIntegrity.ComputeCanonicalDigest(step)))
            return Blocked<OriginStoryArcSeed>(LifeModuleOriginDossierOutcomes.Conflict,
                LifeModuleOriginDossierBlockers.WorkspaceStale);
        // Historical receipts without chapters remain valid receipts, but do
        // not authorize inventing/re-rendering the exact text the user once saw.
        if (entries.Any(entry => entry.Chapter is null))
            return Blocked<OriginStoryArcSeed>(LifeModuleOriginDossierOutcomes.Missing,
                "life-module-origin-history-unavailable");
        var projection = CreateProjection(turn, entries.Select(entry => entry.Chapter!).ToArray(),
            step.MechanicsSnapshotDigest);
        return TryValidateProjection(projection)
            ? new(LifeModuleOriginDossierOutcomes.Success, projection, [])
            : Blocked<OriginStoryArcSeed>(LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.ProjectionInvalid);
    }

    internal static bool ValidateStoredChapter(LifeModuleDecisionAcceptance acceptance, int sequence)
    {
        var chapter = acceptance.Chapter;
        var receipt = acceptance.Receipt;
        if (chapter is null)
            return receipt.ChapterDigest is null; // preserve historical receipts
        return !string.IsNullOrWhiteSpace(chapter.Title)
            && !string.IsNullOrWhiteSpace(chapter.VisibleMarkdown)
            && chapter.Sequence == sequence
            && chapter.ThroughAcceptedDecisionId == receipt.DecisionId
            && DigestsEqual(chapter.ChapterId, ChapterId(sequence, receipt.DecisionId,
                acceptance.NextStep.PreviousTurnDigest))
            && DigestsEqual(chapter.CanonicalLayerDigest, ChapterCanonicalLayer(receipt))
            && DigestsEqual(chapter.PlayerLayerDigest, EmptyPlayerLayerDigest)
            && DigestsEqual(chapter.ProviderLayerDigest, EmptyProviderLayerDigest)
            && DigestsEqual(chapter.ChapterDigest, ComputeChapterDigest(chapter))
            && DigestsEqual(receipt.ChapterDigest ?? string.Empty, chapter.ChapterDigest);
    }

    private static bool StoredChapterMatches(LifeModuleDecisionAcceptance acceptance,
        OriginNarrativeChapterProjection expected)
        => acceptance.Chapter is null && acceptance.Receipt.ChapterDigest is null
           || acceptance.Chapter is { } stored
           && ValidateStoredChapter(acceptance, expected.Sequence)
           && DigestsEqual(stored.ChapterDigest, expected.ChapterDigest);

    private static string ChapterCanonicalLayer(LifeModuleAcceptedDecisionReceipt receipt)
    {
        var facts = receipt.CanonicalFacts.Select(SealFact)
            .OrderBy(static fact => fact.FactId, StringComparer.Ordinal)
            .ThenBy(static fact => fact.FactDigest, StringComparer.Ordinal).ToArray();
        return ComputeDigest(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("acceptedDecisionGraphDigest", receipt.AcceptedDecisionGraphDigest);
            writer.WriteString("decisionId", receipt.DecisionId);
            WriteStringArray(writer, "factDigests", facts.Select(static fact => fact.FactDigest));
            writer.WriteString("mechanicsSnapshotDigest", receipt.MechanicsSnapshotDigest);
            writer.WriteEndObject();
        });
    }

    private static string ChapterId(int sequence, string decisionId, string turnSeedDigest)
        => ComputeDigest(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("decisionId", decisionId);
            writer.WriteNumber("sequence", sequence);
            writer.WriteString("turnSeedDigest", turnSeedDigest);
            writer.WriteEndObject();
        });
}
