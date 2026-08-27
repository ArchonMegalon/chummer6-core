using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.LifeModules;

/// <summary>
/// Adapter seam over the existing Life Module decision ledger/command. Lookup
/// is read-only and makes retries idempotent after the command has advanced the
/// workspace. Accept is the sole allowed mechanics write.
/// </summary>
public interface ILifeModuleDecisionAuthority
{
    LifeModuleDecisionAuthorityResult<LifeModuleDecisionAuthorityStep> Load(string workspaceId);

    LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> FindAcceptance(
        string workspaceId,
        string idempotencyKeyDigest);

    LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> Accept(
        LifeModuleDecisionAcceptanceCommand command);
}

/// <summary>
/// Deterministic, provider-free projection of the live SR5 Life Module
/// decision ledger into the canonical Origin Dossier turn/chapter stream.
/// </summary>
public sealed class LifeModuleOriginDossierService
{
    private const int MaxChoices = 4_096;
    private const int MaxFacts = 65_536;
    private const int MaxChapters = 16_384;
    private const string Sr5RulesetId = "sr5";

    private readonly ILifeModuleDecisionAuthority _authority;

    public LifeModuleOriginDossierService(ILifeModuleDecisionAuthority authority)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
    }

    public static string TurnLedgerRootDigest { get; } = ComputeTextDigest(
        "chummer.origin_dossier.turn-ledger.root.v1");

    public static string EmptyPlayerLayerDigest { get; } = ComputeTextDigest(
        "chummer.origin_dossier.player-layer.empty.v1");

    public static string EmptyProviderLayerDigest { get; } = ComputeTextDigest(
        "chummer.origin_dossier.provider-layer.empty.v1");

    public LifeModuleOriginDossierResult<OriginStoryArcSeed> Project(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
            return Blocked<OriginStoryArcSeed>(
                LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.AuthorityInvalid);

        LifeModuleDecisionAuthorityResult<LifeModuleDecisionAuthorityStep> loaded =
            _authority.Load(workspaceId);
        if (!IsAuthoritySuccess(loaded.Outcome) || loaded.Value is not { } step)
            return FromAuthority<LifeModuleDecisionAuthorityStep, OriginStoryArcSeed>(loaded);
        if (!TryCreateTurn(step, out LifeModuleNarrativeTurnSeed? turn)
            || turn is null
            || turn.AcceptedDecisionIds.Count != 0
            || turn.CanonicalFacts.Count != 0
            || !DigestsEqual(turn.PreviousTurnDigest, TurnLedgerRootDigest))
        {
            return Blocked<OriginStoryArcSeed>(
                LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.AuthorityInvalid);
        }

        OriginStoryArcSeed projection = CreateProjection(turn, [], step.MechanicsSnapshotDigest);
        return new(LifeModuleOriginDossierOutcomes.Success, projection, []);
    }

    /// <summary>
    /// Rebinds a persisted, user-owned story projection to the current decision
    /// authority after a process restart. The persisted chapters remain the
    /// timeline source, while the live authority must reproduce the exact
    /// current turn and mechanics bindings before the projection is returned.
    /// </summary>
    public LifeModuleOriginDossierResult<OriginStoryArcSeed> Resume(
        OriginStoryArcSeed persisted)
    {
        ArgumentNullException.ThrowIfNull(persisted);
        if (!TryValidateProjection(persisted))
            return Blocked<OriginStoryArcSeed>(
                LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.ProjectionInvalid);

        LifeModuleDecisionAuthorityResult<LifeModuleDecisionAuthorityStep> loaded =
            _authority.Load(persisted.CurrentTurn.WorkspaceId);
        if (!IsAuthoritySuccess(loaded.Outcome) || loaded.Value is not { } freshStep)
            return FromAuthority<LifeModuleDecisionAuthorityStep, OriginStoryArcSeed>(loaded);
        if (!TryCreateTurn(freshStep, out LifeModuleNarrativeTurnSeed? freshTurn)
            || freshTurn is null)
        {
            return Blocked<OriginStoryArcSeed>(
                LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.AuthorityInvalid);
        }

        string? staleBlocker = FindStaleBlocker(persisted.CurrentTurn, freshTurn);
        if (staleBlocker is null
            && !DigestsEqual(
                persisted.CanonicalLayer.MechanicsSnapshotDigest,
                freshStep.MechanicsSnapshotDigest))
        {
            staleBlocker = LifeModuleOriginDossierBlockers.DecisionStale;
        }
        if (staleBlocker is not null)
            return Blocked<OriginStoryArcSeed>(
                LifeModuleOriginDossierOutcomes.Conflict,
                staleBlocker);

        return new(
            LifeModuleOriginDossierOutcomes.Success,
            persisted,
            []);
    }

    public LifeModuleOriginDossierResult<LifeModuleOriginDossierAdvance> Accept(
        OriginStoryArcSeed current,
        string choiceId,
        string idempotencyKey,
        bool explicitlyAccepted)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!explicitlyAccepted)
            return Blocked<LifeModuleOriginDossierAdvance>(
                LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.ExplicitAcceptanceRequired);
        if (!IsValidIdempotencyKey(idempotencyKey))
            return Blocked<LifeModuleOriginDossierAdvance>(
                LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.IdempotencyKeyInvalid);
        if (!TryValidateProjection(current))
            return Blocked<LifeModuleOriginDossierAdvance>(
                LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.ProjectionInvalid);

        LifeModuleNarrativeChoiceSeed? choice = current.CurrentTurn.LegalChoices.SingleOrDefault(
            candidate => string.Equals(candidate.ChoiceId, choiceId, StringComparison.Ordinal));
        if (choice is null || !choice.IsLegal || choice.Blockers.Count != 0)
            return Blocked<LifeModuleOriginDossierAdvance>(
                LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.IllegalChoice);

        string idempotencyKeyDigest = ComputeTextDigest(idempotencyKey);
        LifeModuleDecisionAcceptanceCommand command = CreateCommand(
            current,
            choice,
            idempotencyKey,
            idempotencyKeyDigest);
        LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> lookup =
            _authority.FindAcceptance(current.CurrentTurn.WorkspaceId, idempotencyKeyDigest);
        if (IsAuthoritySuccess(lookup.Outcome) && lookup.Value is { } replay)
        {
            if (!string.Equals(replay.Receipt?.ChoiceId, command.ChoiceId, StringComparison.Ordinal)
                || !DigestsEqual(
                    replay.Receipt?.DecisionCommandDigest ?? string.Empty,
                    command.DecisionCommandDigest)
                || !DigestsEqual(
                    replay.Receipt?.IdempotencyKeyDigest ?? string.Empty,
                    command.IdempotencyKeyDigest))
            {
                return Blocked<LifeModuleOriginDossierAdvance>(
                    LifeModuleOriginDossierOutcomes.Conflict,
                    LifeModuleOriginDossierBlockers.IdempotencyConflict);
            }
            return CreateAdvance(current, choice, command, replay);
        }
        if (!string.Equals(
                lookup.Outcome,
                LifeModuleOriginDossierOutcomes.Missing,
                StringComparison.Ordinal))
        {
            return FromAuthority<LifeModuleDecisionAcceptance, LifeModuleOriginDossierAdvance>(lookup);
        }

        LifeModuleDecisionAuthorityResult<LifeModuleDecisionAuthorityStep> loaded =
            _authority.Load(current.CurrentTurn.WorkspaceId);
        if (!IsAuthoritySuccess(loaded.Outcome) || loaded.Value is not { } freshStep)
            return FromAuthority<LifeModuleDecisionAuthorityStep, LifeModuleOriginDossierAdvance>(loaded);
        if (!TryCreateTurn(freshStep, out LifeModuleNarrativeTurnSeed? freshTurn)
            || freshTurn is null)
        {
            return Blocked<LifeModuleOriginDossierAdvance>(
                LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.AuthorityInvalid);
        }

        string? staleBlocker = FindStaleBlocker(current.CurrentTurn, freshTurn);
        if (staleBlocker is null
            && !DigestsEqual(
                current.CanonicalLayer.MechanicsSnapshotDigest,
                freshStep.MechanicsSnapshotDigest))
        {
            staleBlocker = LifeModuleOriginDossierBlockers.DecisionStale;
        }
        if (staleBlocker is not null)
            return Blocked<LifeModuleOriginDossierAdvance>(
                LifeModuleOriginDossierOutcomes.Conflict,
                staleBlocker);

        LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> accepted =
            _authority.Accept(command);
        if (!IsAuthoritySuccess(accepted.Outcome) || accepted.Value is not { } acceptance)
            return FromAuthority<LifeModuleDecisionAcceptance, LifeModuleOriginDossierAdvance>(accepted);
        return CreateAdvance(current, choice, command, acceptance);
    }

    private static LifeModuleOriginDossierResult<LifeModuleOriginDossierAdvance> CreateAdvance(
        OriginStoryArcSeed current,
        LifeModuleNarrativeChoiceSeed choice,
        LifeModuleDecisionAcceptanceCommand command,
        LifeModuleDecisionAcceptance acceptance)
    {
        if (!TryValidateAcceptance(current, choice, command, acceptance)
            || !TryCreateTurn(acceptance.NextStep, out LifeModuleNarrativeTurnSeed? nextTurn)
            || nextTurn is null)
        {
            return Blocked<LifeModuleOriginDossierAdvance>(
                LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.AuthorityInvalid);
        }

        OriginNarrativeChapterProjection chapter = CreateChapter(
            current,
            choice,
            acceptance.Receipt);
        OriginNarrativeChapterProjection[] chapters =
            [.. current.VisibleChapters, chapter];
        OriginStoryArcSeed projection = CreateProjection(
            nextTurn,
            chapters,
            acceptance.Receipt.MechanicsSnapshotDigest);
        return new(
            LifeModuleOriginDossierOutcomes.Success,
            new LifeModuleOriginDossierAdvance(projection, acceptance.Receipt),
            []);
    }

    private static bool TryValidateAcceptance(
        OriginStoryArcSeed current,
        LifeModuleNarrativeChoiceSeed choice,
        LifeModuleDecisionAcceptanceCommand command,
        LifeModuleDecisionAcceptance acceptance)
    {
        LifeModuleAcceptedDecisionReceipt? receipt = acceptance.Receipt;
        LifeModuleDecisionAuthorityStep? next = acceptance.NextStep;
        if (receipt is null
            || next is null
            || next.AcceptedDecisionIds is null
            || next.CanonicalFacts is null
            || !string.Equals(
                receipt.Schema,
                OriginDossierSchemas.AcceptedDecisionReceiptV1,
                StringComparison.Ordinal)
            || !IsCanonicalDigest(receipt.ReceiptDigest)
            || string.IsNullOrWhiteSpace(receipt.DecisionId)
            || !string.Equals(receipt.ChoiceId, choice.ChoiceId, StringComparison.Ordinal)
            || !DigestsEqual(receipt.DecisionCommandDigest, command.DecisionCommandDigest)
            || !DigestsEqual(receipt.IdempotencyKeyDigest, command.IdempotencyKeyDigest)
            || receipt.PreviousWorkspaceRevision != command.WorkspaceRevision
            || receipt.WorkspaceRevision <= receipt.PreviousWorkspaceRevision
            || !DigestsEqual(receipt.PreviousContentDigest, command.ExpectedContentDigest)
            || !DigestsEqual(receipt.SourceDigest, command.ExpectedSourceDigest)
            || !DigestsEqual(receipt.RulesDigest, command.ExpectedRulesDigest)
            || !DigestsEqual(receipt.RuntimeDigest, command.ExpectedRuntimeDigest)
            || !DigestsEqual(receipt.PreviousDecisionDigest, command.ExpectedDecisionDigest)
            || !DigestsEqual(
                receipt.PreviousMechanicsSnapshotDigest,
                command.ExpectedMechanicsSnapshotDigest)
            || !IsCanonicalDigest(receipt.AcceptedDecisionGraphDigest)
            || !IsCanonicalDigest(receipt.MechanicsSnapshotDigest)
            || string.IsNullOrWhiteSpace(receipt.ConsequenceMarkdown)
            || receipt.CanonicalFacts is null
            || receipt.CanonicalFacts.Count > MaxFacts)
        {
            return false;
        }

        string[] expectedAcceptedIds =
            [.. current.CurrentTurn.AcceptedDecisionIds, receipt.DecisionId];
        if (!expectedAcceptedIds.SequenceEqual(next.AcceptedDecisionIds, StringComparer.Ordinal)
            || next.AcceptedDecisionIds.Distinct(StringComparer.Ordinal).Count()
               != next.AcceptedDecisionIds.Count
            || !string.Equals(next.RulesetId, current.CurrentTurn.RulesetId, StringComparison.Ordinal)
            || !string.Equals(next.WorkspaceId, current.CurrentTurn.WorkspaceId, StringComparison.Ordinal)
            || next.WorkspaceRevision != receipt.WorkspaceRevision
            || !string.Equals(next.OwnerId, current.CurrentTurn.OwnerId, StringComparison.Ordinal)
            || !string.Equals(next.RunnerId, current.CurrentTurn.RunnerId, StringComparison.Ordinal)
            || !string.Equals(next.RunnerDisplayName, current.CurrentTurn.RunnerDisplayName, StringComparison.Ordinal)
            || !string.Equals(next.Locale, current.CurrentTurn.Locale, StringComparison.Ordinal)
            || !string.Equals(next.JourneyId, current.CurrentTurn.JourneyId, StringComparison.Ordinal)
            || current.CurrentTurn.TurnSequence == int.MaxValue
            || next.TurnSequence != current.CurrentTurn.TurnSequence + 1
            || next.StageOrder < current.CurrentTurn.StageOrder
            || !DigestsEqual(next.PreviousTurnDigest, current.CurrentTurn.SeedDigest)
            || !DigestsEqual(next.DecisionGraphDigest, receipt.AcceptedDecisionGraphDigest)
            || !DigestsEqual(next.ContentDigest, receipt.ContentDigest)
            || !DigestsEqual(next.SourceDigest, receipt.SourceDigest)
            || !DigestsEqual(next.RulesDigest, receipt.RulesDigest)
            || !DigestsEqual(next.RuntimeDigest, receipt.RuntimeDigest)
            || !DigestsEqual(next.MechanicsSnapshotDigest, receipt.MechanicsSnapshotDigest))
        {
            return false;
        }

        OriginCanonicalNarrativeFact[] expectedFacts = current.CurrentTurn.CanonicalFacts
            .Concat(receipt.CanonicalFacts.Select(SealFact))
            .OrderBy(static fact => fact.FactId, StringComparer.Ordinal)
            .ThenBy(static fact => fact.FactDigest, StringComparer.Ordinal)
            .ToArray();
        OriginCanonicalNarrativeFact[] nextFacts = next.CanonicalFacts
            .Select(SealFact)
            .OrderBy(static fact => fact.FactId, StringComparer.Ordinal)
            .ThenBy(static fact => fact.FactDigest, StringComparer.Ordinal)
            .ToArray();
        return expectedFacts.Select(static fact => fact.FactDigest)
            .SequenceEqual(nextFacts.Select(static fact => fact.FactDigest), StringComparer.Ordinal);
    }

    private static string? FindStaleBlocker(
        LifeModuleNarrativeTurnSeed current,
        LifeModuleNarrativeTurnSeed fresh)
    {
        if (!string.Equals(current.WorkspaceId, fresh.WorkspaceId, StringComparison.Ordinal)
            || current.WorkspaceRevision != fresh.WorkspaceRevision
            || !DigestsEqual(current.ContentDigest, fresh.ContentDigest))
            return LifeModuleOriginDossierBlockers.WorkspaceStale;
        if (!DigestsEqual(current.SourceDigest, fresh.SourceDigest))
            return LifeModuleOriginDossierBlockers.SourceStale;
        if (!DigestsEqual(current.RulesDigest, fresh.RulesDigest))
            return LifeModuleOriginDossierBlockers.RulesStale;
        if (!DigestsEqual(current.RuntimeDigest, fresh.RuntimeDigest))
            return LifeModuleOriginDossierBlockers.RuntimeStale;
        if (!DigestsEqual(current.DecisionGraphDigest, fresh.DecisionGraphDigest)
            || !DigestsEqual(current.DecisionDigest, fresh.DecisionDigest)
            || !DigestsEqual(current.SeedDigest, fresh.SeedDigest))
            return LifeModuleOriginDossierBlockers.DecisionStale;
        return null;
    }

    private static LifeModuleDecisionAcceptanceCommand CreateCommand(
        OriginStoryArcSeed current,
        LifeModuleNarrativeChoiceSeed choice,
        string idempotencyKey,
        string idempotencyKeyDigest)
        => new(
            OriginDossierSchemas.DecisionAcceptanceCommandV1,
            current.CurrentTurn.WorkspaceId,
            current.CurrentTurn.WorkspaceRevision,
            choice.ChoiceId,
            choice.DecisionCommandDigest,
            current.CurrentTurn.ContentDigest,
            current.CurrentTurn.SourceDigest,
            current.CurrentTurn.RulesDigest,
            current.CurrentTurn.RuntimeDigest,
            current.CurrentTurn.DecisionGraphDigest,
            current.CurrentTurn.DecisionDigest,
            current.CanonicalLayer.MechanicsSnapshotDigest,
            current.CurrentTurn.SeedDigest,
            idempotencyKey,
            idempotencyKeyDigest);

    private static OriginNarrativeChapterProjection CreateChapter(
        OriginStoryArcSeed current,
        LifeModuleNarrativeChoiceSeed choice,
        LifeModuleAcceptedDecisionReceipt receipt)
    {
        int sequence = current.VisibleChapters.Count + 1;
        OriginCanonicalNarrativeFact[] facts = receipt.CanonicalFacts
            .Select(SealFact)
            .OrderBy(static fact => fact.FactId, StringComparer.Ordinal)
            .ThenBy(static fact => fact.FactDigest, StringComparer.Ordinal)
            .ToArray();
        string canonicalLayerDigest = ComputeDigest(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("acceptedDecisionGraphDigest", receipt.AcceptedDecisionGraphDigest);
            writer.WriteString("decisionId", receipt.DecisionId);
            WriteStringArray(writer, "factDigests", facts.Select(static fact => fact.FactDigest));
            writer.WriteString("mechanicsSnapshotDigest", receipt.MechanicsSnapshotDigest);
            writer.WriteEndObject();
        });
        string chapterId = ComputeDigest(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("decisionId", receipt.DecisionId);
            writer.WriteNumber("sequence", sequence);
            writer.WriteString("turnSeedDigest", current.CurrentTurn.SeedDigest);
            writer.WriteEndObject();
        });
        string markdown = JoinMarkdown(
            current.CurrentTurn.VisibleStoryMarkdown,
            $"**{choice.Label}**",
            receipt.ConsequenceMarkdown);
        var chapter = new OriginNarrativeChapterProjection(
            chapterId,
            sequence,
            choice.Label,
            markdown,
            receipt.DecisionId,
            canonicalLayerDigest,
            EmptyPlayerLayerDigest,
            EmptyProviderLayerDigest,
            string.Empty);
        return chapter with { ChapterDigest = ComputeChapterDigest(chapter) };
    }

    private static OriginStoryArcSeed CreateProjection(
        LifeModuleNarrativeTurnSeed turn,
        IReadOnlyList<OriginNarrativeChapterProjection> chapters,
        string mechanicsSnapshotDigest)
    {
        string[] chapterDigests = chapters.Select(static chapter => chapter.ChapterDigest).ToArray();
        var canonical = new OriginCanonicalNarrativeLayer(
            turn.RulesetId,
            turn.AcceptedDecisionIds,
            turn.CanonicalFacts,
            chapterDigests,
            turn.DecisionGraphDigest,
            mechanicsSnapshotDigest,
            string.Empty);
        canonical = canonical with { LayerDigest = ComputeCanonicalLayerDigest(canonical) };
        string arcSeedId = ComputeArcSeedId(turn);
        var seed = new OriginStoryArcSeed(
            OriginDossierSchemas.StoryArcSeedV1,
            arcSeedId,
            turn,
            canonical,
            chapters.ToArray(),
            turn.CanonicalFacts.Select(static fact => fact.FactId).ToArray(),
            turn.LegalChoices.Select(static choice => choice.ChoiceId).ToArray(),
            [],
            string.Empty);
        return seed with { SeedDigest = ComputeArcSeedDigest(seed) };
    }

    private static bool TryCreateTurn(
        LifeModuleDecisionAuthorityStep step,
        out LifeModuleNarrativeTurnSeed? turn)
    {
        turn = null;
        if (step is null
            || !string.Equals(
                step.Schema,
                OriginDossierSchemas.DecisionAuthorityStepV1,
                StringComparison.Ordinal)
            || !string.Equals(step.RulesetId, Sr5RulesetId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(step.WorkspaceId)
            || step.WorkspaceRevision <= 0
            || string.IsNullOrWhiteSpace(step.OwnerId)
            || string.IsNullOrWhiteSpace(step.RunnerId)
            || string.IsNullOrWhiteSpace(step.RunnerDisplayName)
            || string.IsNullOrWhiteSpace(step.Locale)
            || string.IsNullOrWhiteSpace(step.JourneyId)
            || string.IsNullOrWhiteSpace(step.StageId)
            || step.StageOrder <= 0
            || string.IsNullOrWhiteSpace(step.TurnId)
            || step.TurnSequence <= 0
            || string.IsNullOrWhiteSpace(step.DecisionLeadInMarkdown)
            || string.IsNullOrWhiteSpace(step.DecisionPrompt)
            || step.LegalChoices is null
            || step.LegalChoices.Count == 0
            || step.LegalChoices.Count > MaxChoices
            || step.LegalChoices.Any(static choice => choice is null)
            || step.CanonicalFacts is null
            || step.CanonicalFacts.Count > MaxFacts
            || step.CanonicalFacts.Any(static fact => fact is null)
            || step.AcceptedDecisionIds is null
            || step.AcceptedDecisionIds.Count > MaxChapters
            || !IsCanonicalDigest(step.PreviousTurnDigest)
            || !IsCanonicalDigest(step.DecisionGraphDigest)
            || !IsCanonicalDigest(step.DecisionDigest)
            || !IsCanonicalDigest(step.ContentDigest)
            || !IsCanonicalDigest(step.SourceDigest)
            || !IsCanonicalDigest(step.RulesDigest)
            || !IsCanonicalDigest(step.RuntimeDigest)
            || !IsCanonicalDigest(step.MechanicsSnapshotDigest))
        {
            return false;
        }

        LifeModuleNarrativeChoiceSeed[] choices = step.LegalChoices
            .Select(ProjectChoice)
            .OrderBy(static choice => choice.ChoiceId, StringComparer.Ordinal)
            .ToArray();
        if (choices.Any(static choice => !TryValidateChoice(choice))
            || choices.Select(static choice => choice.ChoiceId)
                .Distinct(StringComparer.Ordinal).Count() != choices.Length
            || choices.Any(static choice => !IsCanonicalDigest(choice.DecisionCommandDigest)))
        {
            return false;
        }

        OriginCanonicalNarrativeFact[] facts = step.CanonicalFacts
            .Select(SealFact)
            .OrderBy(static fact => fact.FactId, StringComparer.Ordinal)
            .ThenBy(static fact => fact.FactDigest, StringComparer.Ordinal)
            .ToArray();
        if (facts.Any(static fact => !IsValidFact(fact))
            || facts.Select(static fact => fact.FactId)
                .Distinct(StringComparer.Ordinal).Count() != facts.Length
            || facts.Any(fact => !step.AcceptedDecisionIds.Contains(
                fact.AcceptedDecisionId,
                StringComparer.Ordinal))
            || step.AcceptedDecisionIds.Any(string.IsNullOrWhiteSpace)
            || step.AcceptedDecisionIds.Distinct(StringComparer.Ordinal).Count()
                != step.AcceptedDecisionIds.Count)
        {
            return false;
        }

        var candidate = new LifeModuleNarrativeTurnSeed(
            OriginDossierSchemas.NarrativeTurnSeedV1,
            step.RulesetId,
            step.WorkspaceId,
            step.WorkspaceRevision,
            step.OwnerId,
            step.RunnerId,
            step.RunnerDisplayName,
            step.Locale,
            step.JourneyId,
            step.StageId,
            step.StageOrder,
            step.TurnId,
            step.TurnSequence,
            JoinMarkdown(step.DecisionLeadInMarkdown, step.DecisionPrompt),
            step.DecisionPrompt.Trim(),
            choices,
            facts,
            step.AcceptedDecisionIds.ToArray(),
            step.PreviousTurnDigest,
            step.DecisionGraphDigest,
            step.DecisionDigest,
            step.ContentDigest,
            step.SourceDigest,
            step.RulesDigest,
            step.RuntimeDigest,
            string.Empty);
        turn = candidate with { SeedDigest = ComputeTurnSeedDigest(candidate) };
        return true;
    }

    private static LifeModuleNarrativeChoiceSeed ProjectChoice(
        LifeModuleDecisionAuthorityChoice authority)
    {
        LifeModuleMechanicsPreview preview = SealPreview(authority.MechanicsPreview);
        string[] anchors = NormalizeStrings(authority.SourceAnchorIds);
        string[] blockers = NormalizeStrings(authority.Blockers);
        var choice = new LifeModuleNarrativeChoiceSeed(
            authority.ChoiceId?.Trim() ?? string.Empty,
            authority.Label?.Trim() ?? string.Empty,
            authority.Source?.Trim() ?? string.Empty,
            authority.PageReference?.Trim() ?? string.Empty,
            authority.DecisionCommandDigest ?? string.Empty,
            preview,
            preview.PreviewDigest,
            anchors,
            blockers,
            authority.IsLegal,
            string.Empty);
        return choice with { ChoiceDigest = ComputeChoiceDigest(choice) };
    }

    private static LifeModuleMechanicsPreview SealPreview(LifeModuleMechanicsPreview preview)
    {
        LifeModuleMechanicsPreviewItem[] items = (preview?.Items ?? [])
            .Select(SealPreviewItem)
            .OrderBy(static item => item.EffectId, StringComparer.Ordinal)
            .ThenBy(static item => item.ItemDigest, StringComparer.Ordinal)
            .ToArray();
        var sealedPreview = new LifeModuleMechanicsPreview(
            preview?.KarmaCost ?? 0,
            preview?.KarmaRaw?.Trim() ?? string.Empty,
            preview?.KarmaIsExact ?? false,
            items,
            NormalizeStrings(preview?.PendingFollowUpIds),
            NormalizeStrings(preview?.SourceAnchorIds),
            string.Empty);
        return sealedPreview with
        {
            PreviewDigest = ComputeMechanicsPreviewDigest(sealedPreview)
        };
    }

    private static LifeModuleMechanicsPreviewItem SealPreviewItem(
        LifeModuleMechanicsPreviewItem item)
    {
        var sealedItem = new LifeModuleMechanicsPreviewItem(
            item?.EffectId?.Trim() ?? string.Empty,
            item?.Domain?.Trim() ?? string.Empty,
            item?.TargetId?.Trim() ?? string.Empty,
            item?.BeforeValue?.Trim() ?? string.Empty,
            item?.AfterValue?.Trim() ?? string.Empty,
            item?.BudgetDelta ?? 0,
            NormalizeStrings(item?.SourceAnchorIds),
            string.Empty);
        return sealedItem with { ItemDigest = ComputeMechanicsPreviewItemDigest(sealedItem) };
    }

    private static OriginCanonicalNarrativeFact SealFact(OriginCanonicalNarrativeFact fact)
    {
        var sealedFact = new OriginCanonicalNarrativeFact(
            fact?.FactId?.Trim() ?? string.Empty,
            fact?.FactKind?.Trim() ?? string.Empty,
            fact?.LocalizedSummary?.Trim() ?? string.Empty,
            fact?.AcceptedDecisionId?.Trim() ?? string.Empty,
            NormalizeStrings(fact?.SourceAnchorIds),
            string.Empty);
        return sealedFact with { FactDigest = ComputeFactDigest(sealedFact) };
    }

    private static bool TryValidateProjection(OriginStoryArcSeed seed)
    {
        if (!string.Equals(seed.Schema, OriginDossierSchemas.StoryArcSeedV1, StringComparison.Ordinal)
            || seed.CurrentTurn is null
            || seed.CanonicalLayer is null
            || seed.CanonicalLayer.AcceptedDecisionIds is null
            || seed.CanonicalLayer.Facts is null
            || seed.CanonicalLayer.Facts.Any(static fact => fact is null)
            || seed.CanonicalLayer.ChapterProjectionDigests is null
            || seed.VisibleChapters is null
            || seed.VisibleChapters.Count > MaxChapters
            || seed.AllowedCanonicalFactIds is null
            || seed.AllowedChoiceIds is null
            || seed.ToneTags is null
            || seed.ToneTags.Count != 0
            || !TryValidateTurn(seed.CurrentTurn)
            || !string.Equals(seed.ArcSeedId, ComputeArcSeedId(seed.CurrentTurn), StringComparison.Ordinal)
            || !string.Equals(seed.CanonicalLayer.RulesetId, seed.CurrentTurn.RulesetId, StringComparison.Ordinal)
            || !IsCanonicalDigest(seed.CanonicalLayer.MechanicsSnapshotDigest))
            return false;
        for (int index = 0; index < seed.VisibleChapters.Count; index++)
        {
            OriginNarrativeChapterProjection chapter = seed.VisibleChapters[index];
            if (chapter is null
                || chapter.Sequence != index + 1
                || !DigestsEqual(chapter.PlayerLayerDigest, EmptyPlayerLayerDigest)
                || !DigestsEqual(chapter.ProviderLayerDigest, EmptyProviderLayerDigest)
                || !DigestsEqual(chapter.ChapterDigest, ComputeChapterDigest(chapter)))
            {
                return false;
            }
        }

        if (seed.CurrentTurn.AcceptedDecisionIds.Count != seed.VisibleChapters.Count
            || !seed.CurrentTurn.AcceptedDecisionIds.SequenceEqual(
                seed.VisibleChapters.Select(static chapter => chapter.ThroughAcceptedDecisionId),
                StringComparer.Ordinal)
            || !seed.CanonicalLayer.AcceptedDecisionIds.SequenceEqual(
                seed.CurrentTurn.AcceptedDecisionIds,
                StringComparer.Ordinal)
            || !seed.CanonicalLayer.Facts.Select(static fact => fact.FactDigest).SequenceEqual(
                seed.CurrentTurn.CanonicalFacts.Select(static fact => fact.FactDigest),
                StringComparer.Ordinal)
            || !seed.CanonicalLayer.ChapterProjectionDigests.SequenceEqual(
                seed.VisibleChapters.Select(static chapter => chapter.ChapterDigest),
                StringComparer.Ordinal)
            || !DigestsEqual(seed.CanonicalLayer.DecisionGraphDigest, seed.CurrentTurn.DecisionGraphDigest)
            || !DigestsEqual(seed.CanonicalLayer.LayerDigest, ComputeCanonicalLayerDigest(seed.CanonicalLayer))
            || !seed.AllowedCanonicalFactIds.SequenceEqual(
                seed.CurrentTurn.CanonicalFacts.Select(static fact => fact.FactId),
                StringComparer.Ordinal)
            || !seed.AllowedChoiceIds.SequenceEqual(
                seed.CurrentTurn.LegalChoices.Select(static choice => choice.ChoiceId),
                StringComparer.Ordinal)
            || !DigestsEqual(seed.SeedDigest, ComputeArcSeedDigest(seed)))
        {
            return false;
        }
        return true;
    }

    private static bool TryValidateTurn(LifeModuleNarrativeTurnSeed turn)
    {
        if (!string.Equals(turn.Schema, OriginDossierSchemas.NarrativeTurnSeedV1, StringComparison.Ordinal)
            || !string.Equals(turn.RulesetId, Sr5RulesetId, StringComparison.Ordinal)
            || turn.LegalChoices is null
            || turn.CanonicalFacts is null
            || turn.AcceptedDecisionIds is null
            || turn.LegalChoices.Count == 0
            || turn.LegalChoices.Count > MaxChoices
            || turn.CanonicalFacts.Count > MaxFacts
            || turn.AcceptedDecisionIds.Count > MaxChapters
            || string.IsNullOrWhiteSpace(turn.WorkspaceId)
            || turn.WorkspaceRevision <= 0
            || string.IsNullOrWhiteSpace(turn.OwnerId)
            || string.IsNullOrWhiteSpace(turn.RunnerId)
            || string.IsNullOrWhiteSpace(turn.RunnerDisplayName)
            || string.IsNullOrWhiteSpace(turn.Locale)
            || string.IsNullOrWhiteSpace(turn.JourneyId)
            || string.IsNullOrWhiteSpace(turn.StageId)
            || turn.StageOrder <= 0
            || string.IsNullOrWhiteSpace(turn.TurnId)
            || turn.TurnSequence <= 0
            || string.IsNullOrWhiteSpace(turn.VisibleStoryMarkdown)
            || string.IsNullOrWhiteSpace(turn.DecisionPrompt)
            || turn.LegalChoices.Any(static choice => !TryValidateChoice(choice))
            || turn.LegalChoices.Select(static choice => choice.ChoiceId)
                .Distinct(StringComparer.Ordinal).Count() != turn.LegalChoices.Count
            || turn.CanonicalFacts.Any(static fact => !IsValidFact(fact))
            || turn.CanonicalFacts.Select(static fact => fact.FactId)
                .Distinct(StringComparer.Ordinal).Count() != turn.CanonicalFacts.Count
            || turn.CanonicalFacts.Any(fact => !turn.AcceptedDecisionIds.Contains(
                fact.AcceptedDecisionId,
                StringComparer.Ordinal))
            || turn.AcceptedDecisionIds.Any(string.IsNullOrWhiteSpace)
            || turn.AcceptedDecisionIds.Distinct(StringComparer.Ordinal).Count()
                != turn.AcceptedDecisionIds.Count
            || !IsCanonicalDigest(turn.PreviousTurnDigest)
            || !IsCanonicalDigest(turn.DecisionGraphDigest)
            || !IsCanonicalDigest(turn.DecisionDigest)
            || !IsCanonicalDigest(turn.ContentDigest)
            || !IsCanonicalDigest(turn.SourceDigest)
            || !IsCanonicalDigest(turn.RulesDigest)
            || !IsCanonicalDigest(turn.RuntimeDigest)
            || !DigestsEqual(turn.SeedDigest, ComputeTurnSeedDigest(turn)))
        {
            return false;
        }
        return true;
    }

    private static bool TryValidateChoice(LifeModuleNarrativeChoiceSeed choice)
        => choice is not null
           && !string.IsNullOrWhiteSpace(choice.ChoiceId)
           && !string.IsNullOrWhiteSpace(choice.Label)
           && !string.IsNullOrWhiteSpace(choice.Source)
           && !string.IsNullOrWhiteSpace(choice.DecisionCommandDigest)
           && IsCanonicalDigest(choice.DecisionCommandDigest)
           && choice.MechanicsPreview is not null
           && choice.MechanicsPreview.Items is not null
           && choice.MechanicsPreview.PendingFollowUpIds is not null
           && choice.MechanicsPreview.SourceAnchorIds is not null
           && choice.MechanicsPreview.SourceAnchorIds.Count != 0
           && choice.SourceAnchorIds is not null
           && choice.SourceAnchorIds.Count != 0
           && choice.Blockers is not null
           && choice.Blockers.Count == 0
           && choice.IsLegal
           && DigestsEqual(choice.MechanicsPreviewDigest, choice.MechanicsPreview.PreviewDigest)
           && DigestsEqual(
               choice.MechanicsPreview.PreviewDigest,
               ComputeMechanicsPreviewDigest(choice.MechanicsPreview))
           && choice.MechanicsPreview.Items.All(static item =>
               item is not null
               && DigestsEqual(item.ItemDigest, ComputeMechanicsPreviewItemDigest(item)))
           && DigestsEqual(choice.ChoiceDigest, ComputeChoiceDigest(choice));

    private static string ComputeArcSeedId(LifeModuleNarrativeTurnSeed turn)
        => ComputeDigest(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("journeyId", turn.JourneyId);
            writer.WriteString("rulesetId", turn.RulesetId);
            writer.WriteString("runnerId", turn.RunnerId);
            writer.WriteString("workspaceId", turn.WorkspaceId);
            writer.WriteEndObject();
        });

    private static bool IsValidFact(OriginCanonicalNarrativeFact fact)
        => fact is not null
           && !string.IsNullOrWhiteSpace(fact.FactId)
           && !string.IsNullOrWhiteSpace(fact.FactKind)
           && !string.IsNullOrWhiteSpace(fact.LocalizedSummary)
           && !string.IsNullOrWhiteSpace(fact.AcceptedDecisionId)
           && fact.SourceAnchorIds is not null
           && fact.SourceAnchorIds.Count != 0
           && DigestsEqual(fact.FactDigest, ComputeFactDigest(fact));

    private static string ComputeMechanicsPreviewItemDigest(LifeModuleMechanicsPreviewItem item)
        => ComputeDigest(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("budgetDelta", item.BudgetDelta);
            writer.WriteString("domain", item.Domain);
            writer.WriteString("effectId", item.EffectId);
            writer.WriteString("targetId", item.TargetId);
            writer.WriteString("beforeValue", item.BeforeValue);
            writer.WriteString("afterValue", item.AfterValue);
            WriteStringArray(writer, "sourceAnchorIds", item.SourceAnchorIds);
            writer.WriteEndObject();
        });

    private static string ComputeMechanicsPreviewDigest(LifeModuleMechanicsPreview preview)
        => ComputeDigest(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("karmaCost", preview.KarmaCost);
            writer.WriteString("karmaRaw", preview.KarmaRaw);
            writer.WriteBoolean("karmaIsExact", preview.KarmaIsExact);
            WriteStringArray(writer, "itemDigests", preview.Items.Select(static item => item.ItemDigest));
            WriteStringArray(writer, "pendingFollowUpIds", preview.PendingFollowUpIds);
            WriteStringArray(writer, "sourceAnchorIds", preview.SourceAnchorIds);
            writer.WriteEndObject();
        });

    private static string ComputeChoiceDigest(LifeModuleNarrativeChoiceSeed choice)
        => ComputeDigest(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("choiceId", choice.ChoiceId);
            writer.WriteString("label", choice.Label);
            writer.WriteString("source", choice.Source);
            writer.WriteString("pageReference", choice.PageReference);
            writer.WriteString("decisionCommandDigest", choice.DecisionCommandDigest);
            writer.WriteString("mechanicsPreviewDigest", choice.MechanicsPreviewDigest);
            WriteStringArray(writer, "sourceAnchorIds", choice.SourceAnchorIds);
            WriteStringArray(writer, "blockers", choice.Blockers);
            writer.WriteBoolean("isLegal", choice.IsLegal);
            writer.WriteEndObject();
        });

    private static string ComputeFactDigest(OriginCanonicalNarrativeFact fact)
        => ComputeDigest(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("factId", fact.FactId);
            writer.WriteString("factKind", fact.FactKind);
            writer.WriteString("localizedSummary", fact.LocalizedSummary);
            writer.WriteString("acceptedDecisionId", fact.AcceptedDecisionId);
            WriteStringArray(writer, "sourceAnchorIds", fact.SourceAnchorIds);
            writer.WriteEndObject();
        });

    private static string ComputeTurnSeedDigest(LifeModuleNarrativeTurnSeed turn)
        => ComputeDigest(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("schema", turn.Schema);
            writer.WriteString("rulesetId", turn.RulesetId);
            writer.WriteString("workspaceId", turn.WorkspaceId);
            writer.WriteNumber("workspaceRevision", turn.WorkspaceRevision);
            writer.WriteString("ownerId", turn.OwnerId);
            writer.WriteString("runnerId", turn.RunnerId);
            writer.WriteString("runnerDisplayName", turn.RunnerDisplayName);
            writer.WriteString("locale", turn.Locale);
            writer.WriteString("journeyId", turn.JourneyId);
            writer.WriteString("stageId", turn.StageId);
            writer.WriteNumber("stageOrder", turn.StageOrder);
            writer.WriteString("turnId", turn.TurnId);
            writer.WriteNumber("turnSequence", turn.TurnSequence);
            writer.WriteString("visibleStoryMarkdown", turn.VisibleStoryMarkdown);
            writer.WriteString("decisionPrompt", turn.DecisionPrompt);
            WriteStringArray(writer, "choiceDigests", turn.LegalChoices.Select(static choice => choice.ChoiceDigest));
            WriteStringArray(writer, "factDigests", turn.CanonicalFacts.Select(static fact => fact.FactDigest));
            WriteStringArray(writer, "acceptedDecisionIds", turn.AcceptedDecisionIds);
            writer.WriteString("previousTurnDigest", turn.PreviousTurnDigest);
            writer.WriteString("decisionGraphDigest", turn.DecisionGraphDigest);
            writer.WriteString("decisionDigest", turn.DecisionDigest);
            writer.WriteString("contentDigest", turn.ContentDigest);
            writer.WriteString("sourceDigest", turn.SourceDigest);
            writer.WriteString("rulesDigest", turn.RulesDigest);
            writer.WriteString("runtimeDigest", turn.RuntimeDigest);
            writer.WriteEndObject();
        });

    private static string ComputeChapterDigest(OriginNarrativeChapterProjection chapter)
        => ComputeDigest(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("chapterId", chapter.ChapterId);
            writer.WriteNumber("sequence", chapter.Sequence);
            writer.WriteString("title", chapter.Title);
            writer.WriteString("visibleMarkdown", chapter.VisibleMarkdown);
            writer.WriteString("throughAcceptedDecisionId", chapter.ThroughAcceptedDecisionId);
            writer.WriteString("canonicalLayerDigest", chapter.CanonicalLayerDigest);
            writer.WriteString("playerLayerDigest", chapter.PlayerLayerDigest);
            writer.WriteString("providerLayerDigest", chapter.ProviderLayerDigest);
            writer.WriteEndObject();
        });

    private static string ComputeCanonicalLayerDigest(OriginCanonicalNarrativeLayer layer)
        => ComputeDigest(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("rulesetId", layer.RulesetId);
            WriteStringArray(writer, "acceptedDecisionIds", layer.AcceptedDecisionIds);
            WriteStringArray(writer, "factDigests", layer.Facts.Select(static fact => fact.FactDigest));
            WriteStringArray(writer, "chapterProjectionDigests", layer.ChapterProjectionDigests);
            writer.WriteString("decisionGraphDigest", layer.DecisionGraphDigest);
            writer.WriteString("mechanicsSnapshotDigest", layer.MechanicsSnapshotDigest);
            writer.WriteEndObject();
        });

    private static string ComputeArcSeedDigest(OriginStoryArcSeed seed)
        => ComputeDigest(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("schema", seed.Schema);
            writer.WriteString("arcSeedId", seed.ArcSeedId);
            writer.WriteString("currentTurnDigest", seed.CurrentTurn.SeedDigest);
            writer.WriteString("canonicalLayerDigest", seed.CanonicalLayer.LayerDigest);
            WriteStringArray(writer, "chapterDigests", seed.VisibleChapters.Select(static chapter => chapter.ChapterDigest));
            WriteStringArray(writer, "allowedCanonicalFactIds", seed.AllowedCanonicalFactIds);
            WriteStringArray(writer, "allowedChoiceIds", seed.AllowedChoiceIds);
            WriteStringArray(writer, "toneTags", seed.ToneTags);
            writer.WriteEndObject();
        });

    private static string[] NormalizeStrings(IEnumerable<string>? values)
        => values?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray()
           ?? [];

    private static string JoinMarkdown(params string[] parts)
        => string.Join(
            "\n\n",
            parts.Where(static part => !string.IsNullOrWhiteSpace(part))
                .Select(static part => part.Trim()));

    private static bool IsValidIdempotencyKey(string key)
        => !string.IsNullOrWhiteSpace(key)
           && key.Length <= 200
           && string.Equals(key, key.Trim(), StringComparison.Ordinal);

    private static bool IsAuthoritySuccess(string outcome)
        => string.Equals(outcome, LifeModuleOriginDossierOutcomes.Success, StringComparison.Ordinal);

    private static bool IsCanonicalDigest(string digest)
        => digest is { Length: 64 }
           && digest.All(static character =>
               character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool DigestsEqual(string left, string right)
        => IsCanonicalDigest(left)
           && IsCanonicalDigest(right)
           && CryptographicOperations.FixedTimeEquals(
               Encoding.ASCII.GetBytes(left),
               Encoding.ASCII.GetBytes(right));

    private static string ComputeTextDigest(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string ComputeDigest(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
            writer.Flush();
        }
        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    private static void WriteStringArray(
        Utf8JsonWriter writer,
        string propertyName,
        IEnumerable<string> values)
    {
        writer.WriteStartArray(propertyName);
        foreach (string value in values)
            writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static LifeModuleOriginDossierResult<TTarget> FromAuthority<TSource, TTarget>(
        LifeModuleDecisionAuthorityResult<TSource> result)
        => new(
            string.IsNullOrWhiteSpace(result.Outcome) || IsAuthoritySuccess(result.Outcome)
                ? LifeModuleOriginDossierOutcomes.Invalid
                : result.Outcome,
            default,
            result.Blockers?.Count > 0
                ? result.Blockers.ToArray()
                : [LifeModuleOriginDossierBlockers.AuthorityInvalid]);

    private static LifeModuleOriginDossierResult<T> Blocked<T>(
        string outcome,
        string blocker)
        => new(outcome, default, [blocker]);
}
