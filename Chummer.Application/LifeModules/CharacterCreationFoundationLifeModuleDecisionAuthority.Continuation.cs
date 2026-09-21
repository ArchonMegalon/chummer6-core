using System.Globalization;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.LifeModules;

public sealed partial class CharacterCreationFoundationLifeModuleDecisionAuthority
{
    internal static LifeModuleDecisionAuthorityStep? BuildContinuationStep(
        CharacterCreationFoundationService foundation, WorkspaceStoredDocument workspace,
        LifeModuleDecisionAuthorityStep seed)
    {
        var loaded = foundation.ProjectJourney(workspace);
        if (loaded.Value is not { } state || loaded.Blockers.Count != 0
            || !TryFoundationDigest(state.Binding.RawCharacterXmlDigest, out string content)
            || !TryFoundationDigest(state.Binding.SourceDigest, out string source)
            || !FixedEquals(source, seed.SourceDigest) || !FixedEquals(content, seed.ContentDigest))
            return null;
        var candidates = BuildModuleCandidates(foundation, workspace, state);
        string graph = Digest(new
        {
            state.DraftDigest, state.CurrentStageOrder, seed.AcceptedDecisionIds,
            Choices = candidates.Select(candidate => candidate.Choice.DecisionCommandDigest).ToArray()
        });
        bool halted = candidates.Length == 0;
        return seed with
        {
            WorkspaceRevision = workspace.ContentRevision,
            StageId = $"life-module-stage-{state.CurrentStageOrder}",
            StageOrder = state.CurrentStageOrder,
            DecisionLeadInMarkdown = StageLeadIn(seed.Locale, state.CurrentStageOrder),
            DecisionPrompt = halted ? PendingInputPrompt(seed.Locale) : Prompt(seed.Locale),
            LegalChoices = candidates.Select(candidate => candidate.Choice).ToArray(),
            IsTerminal = halted,
            DecisionGraphDigest = graph,
            DecisionDigest = Digest(new { graph, state.Binding, state.DraftRevision, state.DraftDigest }),
            MechanicsSnapshotDigest = Digest(new { state.DraftDigest, state.DraftRevision, state.Budget })
        };
    }

    private LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> AcceptModule(
        WorkspaceStoredDocument workspace, LifeModuleDecisionAcceptanceCommand command)
    {
        if (_foundation is not CharacterCreationFoundationService foundation)
            return Invalid<LifeModuleDecisionAcceptance>();
        var loaded = Load(workspace.Id.Value);
        if (loaded.Value is not { IsTerminal: false } current || !CommandMatchesStep(command, current))
            return Blocked<LifeModuleDecisionAcceptance>(LifeModuleOriginDossierOutcomes.Conflict,
                LifeModuleOriginDossierBlockers.DecisionStale);
        var journey = foundation.ProjectJourney(workspace);
        if (journey.Value is not { } state)
            return FromFoundation<CharacterCreationLifeModuleJourneyState, LifeModuleDecisionAcceptance>(journey);
        var candidate = BuildModuleCandidates(foundation, workspace, state).SingleOrDefault(item =>
            item.Choice.ChoiceId == command.ChoiceId
            && FixedEquals(item.Choice.DecisionCommandDigest, command.DecisionCommandDigest));
        if (candidate is null)
            return Invalid<LifeModuleDecisionAcceptance>();
        var projected = foundation.ProjectModule(workspace, candidate.Preview.Request with
        {
            FollowUpValues = command.InputResolution?.Values ?? candidate.Preview.Request.FollowUpValues
        }, state);
        if (projected.Value is not { CanConfirm: true } preview
            || !MatchesModuleInputs(current, candidate.Choice, preview, command.InputResolution))
            return Invalid<LifeModuleDecisionAcceptance>();
        var confirmed = foundation.ConfirmModule(new(preview.Request,
            preview.PreviewDigest, ExplicitlyConfirmed: true)
        {
            OriginDecisionCommand = command,
            OriginDecisionStep = current
        });
        return confirmed.Value?.OriginDecisionAcceptance is { } acceptance ? Success(acceptance)
            : FromFoundation<CharacterCreationLifeModuleApplyReceipt, LifeModuleDecisionAcceptance>(confirmed);
    }

    internal static LifeModuleDecisionAcceptance? CreateModuleAcceptance(
        CharacterCreationFoundationService foundation, WorkspaceStoredDocument workspace,
        CharacterCreationFoundationDraftLedger proposed, CharacterCreationLifeModulePreview preview,
        LifeModuleDecisionAcceptanceCommand? command, LifeModuleDecisionAuthorityStep? current)
    {
        var ledger = workspace.Document.AuxiliaryState.LifeModuleDecisionAcceptances;
        if (command is null || current is null || current.IsTerminal || current.TurnSequence == int.MaxValue
            || ledger is not { Count: > 0 }
            || !LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(workspace.Id, workspace.ContentRevision, ledger)
            || !SameStep(current, ledger[^1].NextStep) || !CommandMatchesStep(command, current)
            || current.WorkspaceRevision != workspace.ContentRevision
            || ledger.Any(item => FixedEquals(item.Receipt.IdempotencyKeyDigest, command.IdempotencyKeyDigest)))
            return null;
        var fresh = BuildContinuationStep(foundation, workspace, current);
        if (fresh is null || !SameStep(current, fresh))
            return null;
        string choiceId = ModuleChoiceId(preview);
        var choice = current.LegalChoices.SingleOrDefault(item => item.ChoiceId == choiceId);
        if (choice is null || choice.ChoiceId != command.ChoiceId
            || !FixedEquals(choice.DecisionCommandDigest, command.DecisionCommandDigest)
            || !MatchesModuleInputs(current, choice, preview, command.InputResolution))
            return null;
        string decisionId = Digest(new { Kind = "sr5-life-module", command.WorkspaceId,
            command.ChoiceId, command.DecisionCommandDigest,
            InputResolutionDigest = command.InputResolution?.ResolutionDigest });
        var fact = new OriginCanonicalNarrativeFact(
            $"life-module:{preview.Entry.Selection.ModuleId}:{proposed.AdditionalModules!.Count}",
            "accepted-life-module", choice.Label, decisionId, preview.Entry.SourceAnchorIds, string.Empty);
        fact = fact with { FactDigest = Digest(fact with { FactDigest = string.Empty }) };
        var acceptedFacts = new List<OriginCanonicalNarrativeFact> { fact };
        foreach (var prompt in choice.FollowUps ?? [])
        {
            if (command.InputResolution is null || !command.InputResolution.Values.TryGetValue(prompt.PromptId, out var answer)
                || string.IsNullOrWhiteSpace(answer))
                continue;
            var answerFact = new OriginCanonicalNarrativeFact(fact.FactId + ":answer:" + Digest(prompt.PromptId),
                "accepted-life-module-answer", prompt.Label + ": " + answer, decisionId,
                prompt.SourceAnchorIds, string.Empty);
            acceptedFacts.Add(answerFact with { FactDigest = Digest(answerFact) });
        }
        string consequence = string.IsNullOrWhiteSpace(preview.Entry.StoryTemplate)
            ? choice.Label : preview.Entry.StoryTemplate.Trim();
        var seed = current with
        {
            WorkspaceRevision = workspace.ContentRevision + 1,
            TurnSequence = current.TurnSequence + 1,
            TurnId = $"{JourneyId}:{workspace.Id.Value}:{current.TurnSequence + 1}",
            PreviousTurnDigest = command.ExpectedTurnSeedDigest,
            AcceptedDecisionIds = [.. current.AcceptedDecisionIds, decisionId],
            CanonicalFacts = [.. current.CanonicalFacts, .. acceptedFacts]
        };
        var next = BuildContinuationStep(foundation, ProjectCommittedDraft(workspace, proposed), seed);
        if (next is null)
            return null;
        var receipt = new LifeModuleAcceptedDecisionReceipt(
            OriginDossierSchemas.AcceptedDecisionReceiptV1, decisionId, choice.ChoiceId,
            command.DecisionCommandDigest, command.IdempotencyKeyDigest, workspace.ContentRevision,
            next.WorkspaceRevision, current.ContentDigest, next.ContentDigest, next.SourceDigest,
            next.RulesDigest, next.RuntimeDigest, current.DecisionDigest, current.MechanicsSnapshotDigest,
            next.DecisionGraphDigest, next.MechanicsSnapshotDigest, consequence, acceptedFacts, string.Empty)
        { InputResolutionDigest = command.InputResolution?.ResolutionDigest };
        return LifeModuleOriginDossierService.SealAcceptanceChapter(current, receipt, next);
    }

    private static ModuleCandidate[] BuildModuleCandidates(CharacterCreationFoundationService foundation,
        WorkspaceStoredDocument workspace, CharacterCreationLifeModuleJourneyState state)
    {
        var result = new List<ModuleCandidate>();
        foreach (var module in state.Options)
        {
            IEnumerable<LifeModuleVersionProjectionDto?> versions = module.Versions;
            if (module.Versions.Count == 0)
                versions = [null];
            foreach (var version in versions)
            {
                var prompts = module.FollowUps.Concat(version?.FollowUps ?? []).ToArray();
                if (!LifeModuleDecisionInputIntegrity.ValidForms(prompts.Length == 0 ? null : prompts))
                    continue;
                var request = new CharacterCreationLifeModulePreviewRequest(state.Binding,
                    state.DraftRevision, state.DraftDigest, new(module.ModuleId, version?.VersionId));
                var projected = foundation.ProjectModule(workspace, request, state);
                // A form may be offered before its player answers exist. Only
                // missing answers are deferred; every other rule blocker stays
                // closed and actual confirmation always revalidates the inputs.
                if (projected.Value is not { } preview || projected.Blockers.Any(blocker =>
                        blocker != CharacterCreationFoundationBlockers.LifeModuleFollowUpRequired))
                    continue;
                var entry = preview.Entry;
                var mechanics = ModuleMechanics(preview) with
                { PendingFollowUpIds = prompts.Where(prompt => prompt.IsRequired).Select(prompt => prompt.PromptId).ToArray() };
                var choice = new LifeModuleDecisionAuthorityChoice(ModuleChoiceId(preview),
                    version is null ? module.Name : $"{module.Name} · {version.Label}",
                    version?.Source ?? module.Source, version?.PageReference ?? module.PageReference,
                    ModuleCommandDigest(preview), mechanics, entry.SourceAnchorIds, [], true)
                { FollowUps = prompts.Length == 0 ? null : prompts };
                result.Add(new(preview, choice));
            }
        }
        return result.OrderBy(candidate => candidate.Choice.ChoiceId, StringComparer.Ordinal).ToArray();
    }

    private static string ModuleChoiceId(CharacterCreationLifeModulePreview preview)
        => Digest(new { preview.Entry.StageOrder, preview.Entry.Selection });

    private static string ModuleCommandDigest(CharacterCreationLifeModulePreview preview)
        => Digest(new { preview.Request, preview.PreviewDigest });

    private static LifeModuleMechanicsPreview ModuleMechanics(CharacterCreationLifeModulePreview preview)
        => new(preview.Entry.KarmaCost, preview.Entry.KarmaCost.ToString(CultureInfo.InvariantCulture), true,
            preview.Entry.ProjectedEffects.Select(effect => new LifeModuleMechanicsPreviewItem(
                effect.EffectId, effect.Domain, effect.TargetId, effect.BeforeValue ?? string.Empty,
                effect.AfterValue ?? string.Empty, effect.BudgetDelta, effect.SourceAnchorIds, string.Empty)).ToArray(),
            [], preview.Entry.SourceAnchorIds, string.Empty);

    private static LifeModuleDecisionInputResolution ResolveModuleInputs(LifeModuleDecisionAuthorityStep current,
        LifeModuleDecisionAuthorityChoice choice, CharacterCreationLifeModulePreview preview)
        => LifeModuleDecisionInputIntegrity.Seal(new(current.WorkspaceId, current.WorkspaceRevision,
            choice.ChoiceId, current.DecisionDigest, choice.DecisionCommandDigest,
            preview.Entry.FollowUpValues, preview.PreviewDigest, ModuleMechanics(preview), string.Empty));

    private static bool MatchesModuleInputs(LifeModuleDecisionAuthorityStep current,
        LifeModuleDecisionAuthorityChoice choice, CharacterCreationLifeModulePreview preview,
        LifeModuleDecisionInputResolution? resolution)
        => choice.FollowUps is { Count: > 0 }
            ? LifeModuleDecisionInputIntegrity.Matches(resolution, current.WorkspaceId, current.WorkspaceRevision,
                choice.ChoiceId, current.DecisionDigest, choice.DecisionCommandDigest)
              && ResolveModuleInputs(current, choice, preview).ResolutionDigest == resolution!.ResolutionDigest
            : resolution is null && FixedEquals(choice.DecisionCommandDigest, ModuleCommandDigest(preview));

    public LifeModuleDecisionAuthorityResult<LifeModuleDecisionInputResolution> ResolveInputs(
        LifeModuleDecisionInputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_foundation is not CharacterCreationFoundationService foundation
            || !TryWorkspaceId(request.WorkspaceId, out var id)
            || !LifeModuleDecisionInputIntegrity.TryNormalize(request.Values, out var values))
            return Invalid<LifeModuleDecisionInputResolution>();
        var loaded = Load(request.WorkspaceId);
        if (loaded.Value is not { IsTerminal: false } current
            || current.WorkspaceRevision != request.WorkspaceRevision || current.DecisionDigest != request.DecisionDigest)
            return Blocked<LifeModuleDecisionInputResolution>(LifeModuleOriginDossierOutcomes.Conflict,
                LifeModuleOriginDossierBlockers.DecisionStale);
        var read = _workspaceStore.Get(id);
        if (read.Value is not { } workspace || workspace.ContentRevision != request.WorkspaceRevision)
            return Blocked<LifeModuleDecisionInputResolution>(LifeModuleOriginDossierOutcomes.Conflict,
                LifeModuleOriginDossierBlockers.WorkspaceStale);
        if (workspace.Document.AuxiliaryState.LifeModuleDecisionAcceptances is not { Count: > 0 })
        {
            var initial = foundation.Load(new(id));
            if (initial.Value is not { } initialState)
                return FromFoundation<CharacterCreationFoundationState, LifeModuleDecisionInputResolution>(initial);
            var initialCandidate = BuildCandidates(initialState).SingleOrDefault(item =>
                item.Choice.ChoiceId == request.ChoiceId && item.Choice.DecisionCommandDigest == request.DecisionCommandDigest);
            return initialCandidate?.Choice.FollowUps is { Count: > 0 }
                ? ResolveFoundationInputs(current, initialState, initialCandidate, values)
                : Invalid<LifeModuleDecisionInputResolution>();
        }
        var journey = foundation.ProjectJourney(workspace);
        if (journey.Value is not { } state)
            return FromFoundation<CharacterCreationLifeModuleJourneyState, LifeModuleDecisionInputResolution>(journey);
        var candidate = BuildModuleCandidates(foundation, workspace, state).SingleOrDefault(item =>
            item.Choice.ChoiceId == request.ChoiceId && item.Choice.DecisionCommandDigest == request.DecisionCommandDigest);
        if (candidate?.Choice.FollowUps is not { Count: > 0 })
            return Invalid<LifeModuleDecisionInputResolution>();
        var projected = foundation.ProjectModule(workspace, candidate.Preview.Request with { FollowUpValues = values }, state);
        if (projected.Value is not { CanConfirm: true } preview || projected.Blockers.Count != 0)
            return FromFoundation<CharacterCreationLifeModulePreview, LifeModuleDecisionInputResolution>(projected);
        return Success(ResolveModuleInputs(current, candidate.Choice, preview));
    }

    private static bool SameStep(LifeModuleDecisionAuthorityStep left, LifeModuleDecisionAuthorityStep right)
        => FixedEquals(Digest(left), Digest(right));

    private static WorkspaceStoredDocument ProjectCommittedDraft(WorkspaceStoredDocument workspace,
        CharacterCreationFoundationDraftLedger proposed)
        => workspace with
        {
            ContentRevision = workspace.ContentRevision + 1,
            SavedRevision = workspace.ContentRevision + 1,
            Document = workspace.Document with
            {
                State = workspace.Document.State with
                {
                    AuxiliaryState = workspace.Document.AuxiliaryState with { CharacterCreationFoundationDraft = proposed }
                }
            }
        };

    private static string StageLeadIn(string locale, int stage) => PrimaryLanguage(locale) switch
    {
        "de" => $"Deine bisherigen Entscheidungen sind gespeichert. Wähle den nächsten Abschnitt deiner Vorgeschichte (Phase {stage}).",
        "es" => $"Tus decisiones anteriores están guardadas. Elige el siguiente capítulo de tu pasado (etapa {stage}).",
        _ => $"Your earlier decisions are saved. Choose the next part of your background (stage {stage})."
    };

    private static string PendingInputPrompt(string locale) => PrimaryLanguage(locale) switch
    {
        "de" => "Dieser Entwurf ist gespeichert. Weitere Auswahlfragen oder die Finalisierung sind noch offen; der Runner ist nicht fertig.",
        "es" => "Este borrador está guardado. Quedan decisiones o la finalización; el runner aún no está terminado.",
        _ => "This draft is saved. Further choices or finalization remain; the runner is not finished."
    };

    private sealed record ModuleCandidate(CharacterCreationLifeModulePreview Preview,
        LifeModuleDecisionAuthorityChoice Choice);
}
