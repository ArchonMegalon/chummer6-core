using Chummer.Application.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.LifeModules;

public sealed partial class CharacterCreationFoundationLifeModuleDecisionAuthority
{
    internal LifeModuleDecisionAuthorityResult<LifeModuleDecisionAvailabilitySnapshot> LoadAvailability(
        LifeModuleDecisionAvailabilityRequest request)
    {
        if (!TryWorkspaceId(request.WorkspaceId, out var id)
            || request.WorkspaceRevision < 1 || request.SavedRevision < 0
            || string.IsNullOrWhiteSpace(request.TurnId)
            || !LifeModuleDecisionAcceptanceIntegrity.IsDigest(request.DecisionDigest))
            return Invalid<LifeModuleDecisionAvailabilitySnapshot>();
        var before = _workspaceStore.Get(id);
        if (!before.Success || before.Value is not { } workspace)
            return FromRead<LifeModuleDecisionAvailabilitySnapshot>(before);
        var loaded = Load(request.WorkspaceId);
        if (loaded.Value is not { } current)
            return new(loaded.Outcome, null, loaded.Blockers);
        if (workspace.ContentRevision != request.WorkspaceRevision || workspace.SavedRevision != request.SavedRevision
            || current.WorkspaceRevision != request.WorkspaceRevision || current.TurnId != request.TurnId
            || !FixedEquals(current.DecisionDigest, request.DecisionDigest))
            return Blocked<LifeModuleDecisionAvailabilitySnapshot>(LifeModuleOriginDossierOutcomes.Conflict,
                LifeModuleOriginDossierBlockers.DecisionStale);
        if (current.IsTerminal || current.StageOrder < LifeModuleJourneyStageOrders.FormativeYears
            || _foundation is not CharacterCreationFoundationService foundation)
            return Missing<LifeModuleDecisionAvailabilitySnapshot>();
        var journey = foundation.ProjectJourney(workspace);
        if (journey.Value is not { } state || journey.Blockers.Count != 0
            || !TryFoundationDigest(state.Binding.RawCharacterXmlDigest, out var content)
            || !TryFoundationDigest(state.Binding.SourceDigest, out var source)
            || !FixedEquals(content, current.ContentDigest) || !FixedEquals(source, current.SourceDigest))
            return Invalid<LifeModuleDecisionAvailabilitySnapshot>();

        // The same evaluator that rejects confirmation classifies these options.
        // Unknown, malformed and otherwise blocked paths are not opportunities.
        var candidates = BuildModuleCandidates(foundation, workspace, state, includeBudgetExcluded: true);
        var legal = current.LegalChoices.Where(choice => choice.ChoiceId != FinishSelectionChoiceId)
            .Select(choice => choice.ChoiceId).Order(StringComparer.Ordinal).ToArray();
        if (!legal.SequenceEqual(candidates.Where(candidate => candidate.Choice.IsLegal)
                .Select(candidate => candidate.Choice.ChoiceId).Order(StringComparer.Ordinal)))
            return Invalid<LifeModuleDecisionAvailabilitySnapshot>();
        var options = candidates.Select(candidate => new LifeModuleOptionAvailability(
            candidate.Choice.ChoiceId, candidate.Choice.Label,
            candidate.Choice.IsLegal ? LifeModuleOptionAvailabilityStates.Available
                : LifeModuleOptionAvailabilityStates.BudgetExcluded,
            Array.AsReadOnly(candidate.Choice.SourceAnchorIds.ToArray()))).ToArray();

        var reread = _workspaceStore.Get(id);
        if (!reread.Success || reread.Value is not { } after
            || after.ContentRevision != workspace.ContentRevision || after.SavedRevision != workspace.SavedRevision
            || after.Document.Content != workspace.Document.Content || after.Document.RulesetId != workspace.Document.RulesetId
            || after.Document.Format != workspace.Document.Format || after.Document.SchemaVersion != workspace.Document.SchemaVersion
            || after.Document.PayloadKind != workspace.Document.PayloadKind
            || WorkspaceDocumentAuxiliaryStateDigest.Compute(after.Document.AuxiliaryState)
                != WorkspaceDocumentAuxiliaryStateDigest.Compute(workspace.Document.AuxiliaryState))
            return Blocked<LifeModuleDecisionAvailabilitySnapshot>(LifeModuleOriginDossierOutcomes.Conflict,
                LifeModuleOriginDossierBlockers.WorkspaceStale);
        return Success(new LifeModuleDecisionAvailabilitySnapshot(request, content, source,
            current.RulesDigest, current.RuntimeDigest, Array.AsReadOnly(options)));
    }
}
