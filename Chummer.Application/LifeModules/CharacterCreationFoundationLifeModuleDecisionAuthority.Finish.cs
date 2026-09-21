using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.LifeModules;

public sealed partial class CharacterCreationFoundationLifeModuleDecisionAuthority
{
    private const string FinishSelectionChoiceId = "finish-life-module-selection";
    private const string FinishedSelectionStageId = CharacterCreationLifeModuleStageIds.SelectionFinished;

    private static LifeModuleDecisionAuthorityChoice FinishChoice(
        CharacterCreationLifeModuleFinishPreview preview, string locale)
        => new(FinishSelectionChoiceId, FinishLabel(locale), "Life Modules", string.Empty,
            Digest(new { preview.Request, preview.PreviewDigest }),
            new(0, "0", true,
                [new("module-selection-finished", "creation-stage", FinishedSelectionStageId, "false", "true", 0,
                    preview.SourceAnchorIds, string.Empty)], [], preview.SourceAnchorIds, string.Empty),
            preview.SourceAnchorIds, [], true);

    private LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> AcceptFinishSelection(
        CharacterCreationFoundationService foundation, WorkspaceStoredDocument workspace,
        LifeModuleDecisionAuthorityStep current, LifeModuleDecisionAcceptanceCommand command)
    {
        var journey = foundation.ProjectJourney(workspace);
        if (journey.Value is not { } state)
            return FromFoundation<CharacterCreationLifeModuleJourneyState, LifeModuleDecisionAcceptance>(journey);
        var preview = CharacterCreationFoundationService.ProjectFinishSelection(workspace, state);
        if (!preview.CanConfirm || command.InputResolution is not null
            || !FixedEquals(command.DecisionCommandDigest, FinishChoice(preview, current.Locale).DecisionCommandDigest))
            return Invalid<LifeModuleDecisionAcceptance>();
        var confirmed = foundation.ConfirmFinishSelection(new(preview.Request, preview.PreviewDigest, true)
        { OriginDecisionCommand = command, OriginDecisionStep = current });
        return confirmed.Value?.OriginDecisionAcceptance is { } acceptance ? Success(acceptance)
            : FromFoundation<CharacterCreationLifeModuleFinishReceipt, LifeModuleDecisionAcceptance>(confirmed);
    }

    internal static LifeModuleDecisionAcceptance? CreateFinishAcceptance(
        CharacterCreationFoundationService foundation, WorkspaceStoredDocument workspace,
        CharacterCreationFoundationDraftLedger proposed, CharacterCreationLifeModuleFinishPreview preview,
        LifeModuleDecisionAcceptanceCommand? command, LifeModuleDecisionAuthorityStep? current)
    {
        var ledger = workspace.Document.AuxiliaryState.LifeModuleDecisionAcceptances;
        if (command is null || current is null || current.IsTerminal || current.TurnSequence == int.MaxValue
            || command.InputResolution is not null || !preview.CanConfirm || !proposed.ModuleSelectionFinished
            || ledger is not { Count: > 0 }
            || !LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(workspace.Id, workspace.ContentRevision, ledger)
            || !SameStep(current, ledger[^1].NextStep) || !CommandMatchesStep(command, current)
            || current.WorkspaceRevision != workspace.ContentRevision
            || ledger.Any(item => FixedEquals(item.Receipt.IdempotencyKeyDigest, command.IdempotencyKeyDigest)))
            return null;
        var fresh = BuildContinuationStep(foundation, workspace, current);
        var choice = FinishChoice(preview, current.Locale);
        if (fresh is null || !SameStep(current, fresh) || command.ChoiceId != FinishSelectionChoiceId
            || !FixedEquals(choice.DecisionCommandDigest, command.DecisionCommandDigest))
            return null;
        string decisionId = Digest(new { Kind = FinishedSelectionStageId, command.WorkspaceId,
            command.ChoiceId, command.DecisionCommandDigest });
        string consequence = FinishedSelectionText(current.Locale);
        var fact = new OriginCanonicalNarrativeFact(FinishedSelectionStageId, "accepted-module-selection-finish",
            consequence, decisionId, preview.SourceAnchorIds, string.Empty);
        fact = fact with { FactDigest = Digest(fact) };
        var seed = current with
        {
            WorkspaceRevision = workspace.ContentRevision + 1,
            TurnSequence = current.TurnSequence + 1,
            TurnId = $"{JourneyId}:{workspace.Id.Value}:{current.TurnSequence + 1}",
            PreviousTurnDigest = command.ExpectedTurnSeedDigest,
            AcceptedDecisionIds = [.. current.AcceptedDecisionIds, decisionId],
            CanonicalFacts = [.. current.CanonicalFacts, fact]
        };
        var next = BuildContinuationStep(foundation, ProjectCommittedDraft(workspace, proposed), seed);
        if (next is not { IsTerminal: true, StageId: FinishedSelectionStageId })
            return null;
        var receipt = new LifeModuleAcceptedDecisionReceipt(OriginDossierSchemas.AcceptedDecisionReceiptV1,
            decisionId, choice.ChoiceId, command.DecisionCommandDigest, command.IdempotencyKeyDigest,
            workspace.ContentRevision, next.WorkspaceRevision, current.ContentDigest, next.ContentDigest,
            next.SourceDigest, next.RulesDigest, next.RuntimeDigest, current.DecisionDigest, current.MechanicsSnapshotDigest,
            next.DecisionGraphDigest, next.MechanicsSnapshotDigest, consequence, [fact], string.Empty);
        return LifeModuleOriginDossierService.SealAcceptanceChapter(current, receipt, next);
    }

    private static string FinishLabel(string locale) => PrimaryLanguage(locale) switch
    {
        "de" => "Modulauswahl beenden",
        "es" => "Finalizar selección de módulos",
        _ => "Finish module selection"
    };

    private static string FinishedSelectionText(string locale) => PrimaryLanguage(locale) switch
    {
        "de" => "Deine Modulauswahl ist gespeichert. Das Buch bleibt erhalten. Die Regelwirkungen und der Übergang in die Karriere müssen noch geprüft und ausdrücklich bestätigt werden.",
        "es" => "Tu selección de módulos está guardada y el libro se conserva. Los efectos de reglas y el paso a la carrera aún deben revisarse y confirmarse expresamente.",
        _ => "Your module selection is saved and the book is retained. Rule effects and the transition to Career still need review and explicit confirmation."
    };
}
