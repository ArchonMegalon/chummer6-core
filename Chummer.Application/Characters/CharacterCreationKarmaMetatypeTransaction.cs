using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Intrinsic history checks and deterministic preparation, not storage authorization.</summary>
public static class CharacterCreationKarmaMetatypeTransaction
{
    public const int MaximumDecisions = 128;
    private const int MaximumDecisionBytes = 128 * 1024;

    public static bool IsConfirmed(CharacterCreationKarmaMetatypeConfirmRequest? request)
        => request is { ExplicitlyConfirmed: true, Binding: { } binding }
            && request.OperationId != Guid.Empty
            && Guid.TryParseExact(request.MetatypeOptionId, "D", out var optionId)
            && optionId != Guid.Empty && optionId.ToString("D") == request.MetatypeOptionId
            && (request.TalentOptionId is null || CharacterCreationKarmaTalentAuthority.IsOptionId(request.TalentOptionId))
            && (binding.TalentAuthorityDigest is null || Digest(binding.TalentAuthorityDigest))
            && (request.TalentOptionId is null || Digest(binding.TalentAuthorityDigest))
            && (binding.AttributePolicyDigest is null || Digest(binding.AttributePolicyDigest))
            && (request.AttributeAllocations is null || request.TalentOptionId is not null
                && Digest(binding.AttributePolicyDigest)
                && CharacterCreationKarmaAttributesRules.IsAllocationShape(request.AttributeAllocations))
            && (binding.SkillsPolicyDigest is null || Digest(binding.SkillsPolicyDigest))
            && (binding.SkillsCatalogDigest is null || Digest(binding.SkillsCatalogDigest))
            && (request.SkillsSelection is null || request.AttributeAllocations is not null
                && Digest(binding.SkillsPolicyDigest) && Digest(binding.SkillsCatalogDigest)
                && CharacterCreationKarmaSkillsRules.TryFreeze(request.SkillsSelection, out _))
            && (binding.ResourcesPolicyDigest is null || Digest(binding.ResourcesPolicyDigest))
            && (request.ResourceKarmaInvestment is null || request.ResourceKarmaInvestment >= 0
                && request.AttributeAllocations is not null && Digest(binding.ResourcesPolicyDigest))
            && (binding.QualitiesPolicyDigest is null || Digest(binding.QualitiesPolicyDigest))
            && (binding.QualitiesCatalogDigest is null || Digest(binding.QualitiesCatalogDigest))
            && (request.QualityOptionIds is null || request.AttributeAllocations is not null
                && Digest(binding.QualitiesPolicyDigest) && Digest(binding.QualitiesCatalogDigest)
                && CharacterCreationKarmaQualitiesRules.TryFreeze(request.QualityOptionIds, out _))
            && !string.IsNullOrWhiteSpace(binding.WorkspaceId.Value)
            && binding.ContentRevision is > 0 and < long.MaxValue
            && (binding.SavedRevision == binding.ContentRevision
                || (binding.ContentRevision == CharacterCreationBootstrapRevisions.InitialContentRevision
                    && binding.SavedRevision == CharacterCreationBootstrapRevisions.InitialSavedRevision))
            && CharacterCareerReputationTransaction.IsDigest(binding.AuxiliaryStateDigest)
            && Digest(binding.RawCharacterXmlDigest) && Digest(binding.BootstrapBindingDigest)
            && Digest(binding.SourceProfileDigest) && Digest(binding.MetatypeAuthorityDigest)
            && Digest(request.QuoteDigest);

    public static string DecisionDigest(CharacterCreationKarmaMetatypeDecision decision)
        => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            decision with { DecisionDigest = string.Empty });

    public static bool IsValidLedger(CharacterWorkspaceId id, long revision,
        WorkspaceDocumentAuxiliaryState state)
    {
        var ledger = state.CharacterCreationKarmaMetatypeDecisions;
        if (ledger is null) return true;
        if (ledger.Count is < 1 or > MaximumDecisions
            || state.CharacterCreationBootstrapBinding is not { BuildMethod: CharacterCreationBuildMethods.Karma } bootstrap)
            return false;
        try
        {
            var seen = new HashSet<Guid>();
            long previousRevision = 0;
            for (int index = 0; index < ledger.Count; index++)
            {
                var decision = ledger[index];
                if (decision is null || decision.Schema != CharacterCreationKarmaMetatypeSchemas.DecisionV1
                    || !IsConfirmed(decision.Command) || decision.Quote is not { CanSelect: true } quote
                    || index > 0 && (ledger[index - 1].Quote.Talent is not null && quote.Talent is null
                        || ledger[index - 1].Quote.Attributes is not null && quote.Attributes is null
                        || ledger[index - 1].Quote.Skills is not null && quote.Skills is null
                        || ledger[index - 1].Quote.Resources is not null && quote.Resources is null
                        || ledger[index - 1].Quote.Qualities is not null && quote.Qualities is null)
                    || quote.Schema != CharacterCreationKarmaMetatypeSchemas.QuoteV1
                    || quote.Binding != decision.Command.Binding || quote.Binding.WorkspaceId != id
                    || quote.Metatype is not { IsEnabled: true, KarmaCost: >= 0 } metatype
                    || metatype.Blockers is not { Count: 0 } || quote.Blockers is not { Count: 0 }
                    || metatype.SourceAnchorIds is not { Count: > 0 } || quote.SourceAnchorIds is not { Count: > 0 }
                    || decision.Command.MetatypeOptionId != metatype.OptionId
                    || decision.Command.QuoteDigest != quote.QuoteDigest || !Digest(quote.SnapshotDigest)
                    || quote.QuoteDigest != CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                        quote with { QuoteDigest = string.Empty })
                    || quote.KarmaBudget is not { IsExact: true, Total: >= 0, Remaining: >= 0 } budget
                    || budget.Total > int.MaxValue || decimal.Truncate(budget.Total) != budget.Total
                    || budget.BudgetId != CharacterCreationBudgetIds.Karma || budget.Unit != "karma"
                    || !ValidTalent(quote.Talent, decision.Command.TalentOptionId)
                    || budget.Blockers is not { Count: 0 }
                    || !CharacterCreationKarmaAttributesRules.IsValid(quote.Attributes, metatype, quote.Talent,
                        decision.Command.AttributeAllocations)
                    || quote.Attributes is { } attributes && (attributes.Policy.AuthorityDigest != quote.Binding.AttributePolicyDigest
                        || attributes.Policy.RawProfileInputsDigest != quote.Binding.SourceProfileDigest)
                    || !CharacterCreationKarmaQualitiesRules.IsValid(quote.Qualities, metatype, quote.Talent,
                        decision.Command.QualityOptionIds)
                    || quote.Qualities is { } qualities && (qualities.Policy.AuthorityDigest != quote.Binding.QualitiesPolicyDigest
                        || qualities.CatalogDigest != quote.Binding.QualitiesCatalogDigest
                        || qualities.Policy.RawProfileInputsDigest != quote.Binding.SourceProfileDigest)
                    || !CharacterCreationKarmaSkillsRules.IsValid(quote.Skills, metatype, quote.Talent, quote.Attributes,
                        decision.Command.SkillsSelection)
                    || quote.Skills is { } skills && (skills.Policy.AuthorityDigest != quote.Binding.SkillsPolicyDigest
                        || skills.CatalogDigest != quote.Binding.SkillsCatalogDigest
                        || skills.Policy.RawProfileInputsDigest != quote.Binding.SourceProfileDigest
                        || skills.KarmaAvailable != budget.Total - metatype.KarmaCost - (quote.Talent?.KarmaCost ?? 0)
                            - (quote.Attributes?.KarmaUsed ?? 0) - (quote.Qualities?.Costs.NetKarmaSpent ?? 0))
                    || !CharacterCreationKarmaResourcesRules.IsValid(quote.Resources, quote.Attributes,
                        decision.Command.ResourceKarmaInvestment, budget.Total - metatype.KarmaCost
                            - (quote.Talent?.KarmaCost ?? 0) - (quote.Attributes?.KarmaUsed ?? 0)
                            - (quote.Qualities?.Costs.NetKarmaSpent ?? 0) - (quote.Skills?.KarmaUsed ?? 0))
                    || quote.Resources is { } resources && (resources.Policy.AuthorityDigest != quote.Binding.ResourcesPolicyDigest
                        || resources.Policy.RawProfileInputsDigest != quote.Binding.SourceProfileDigest)
                    || budget.Used != (decimal)metatype.KarmaCost + (quote.Talent?.KarmaCost ?? 0)
                        + (quote.Attributes?.KarmaUsed ?? 0) + (quote.Qualities?.Costs.NetKarmaSpent ?? 0)
                        + (quote.Skills?.KarmaUsed ?? 0) + (quote.Resources?.KarmaInvestment ?? 0)
                    || budget.Total - budget.Used != budget.Remaining
                    || decision.DraftRevision != index + 1
                    || decision.CommittedContentRevision != quote.Binding.ContentRevision + 1
                    || decision.CommittedContentRevision > revision
                    || quote.Binding.ContentRevision < previousRevision
                    || quote.Binding.BootstrapBindingDigest != bootstrap.BindingDigest
                    || quote.Binding.RawCharacterXmlDigest != bootstrap.RawCharacterXmlDigest
                    || quote.Binding.SourceProfileDigest != bootstrap.RawProfileInputsDigest
                    || quote.Binding.MetatypeAuthorityDigest != bootstrap.MetatypeAuthorityDigest
                    || !seen.Add(decision.Command.OperationId)
                    || decision.DecisionDigest != DecisionDigest(decision)
                    || JsonSerializer.SerializeToUtf8Bytes(decision).Length > MaximumDecisionBytes)
                    return false;
                var before = new WorkspaceDocumentAuxiliaryState(CharacterCreationBootstrapBinding: bootstrap,
                    CharacterCreationKarmaMetatypeDecisions: index == 0 ? null : ledger.Take(index).ToArray());
                if (quote.Binding.AuxiliaryStateDigest != WorkspaceDocumentAuxiliaryStateDigest.Compute(before))
                    return false;
                previousRevision = decision.CommittedContentRevision;
            }
            return true;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    public static bool IsValidHistory(WorkspaceStoredDocument workspace)
    {
        var ledger = workspace.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions;
        return IsValidLedger(workspace.Id, workspace.ContentRevision, workspace.Document.AuxiliaryState)
            && (ledger is null || (workspace.SavedRevision >= ledger[^1].CommittedContentRevision
                && ledger[^1].Quote.Binding.RawCharacterXmlDigest ==
                    CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(workspace.Document.Content)));
    }

    public static CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit>? Lookup(
        WorkspaceStoredDocument workspace, CharacterCreationKarmaMetatypeConfirmRequest request)
    {
        if (!IsValidHistory(workspace)) return Blocked(CharacterCreationKarmaMetatypeBlockers.HistoryInvalid);
        var decision = workspace.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions?
            .SingleOrDefault(item => item.Command.OperationId == request.OperationId);
        if (decision is null) return null;
        return CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(decision.Command, request)
            && workspace.CanReplayReceipt(decision.CommittedContentRevision)
            ? new(CharacterCreationFoundationOutcomes.Success, new(decision, true), [])
            : Blocked(CharacterCreationKarmaMetatypeBlockers.IdempotencyConflict);
    }

    public static bool TryBuild(OwnerScope owner, WorkspaceStoredDocument workspace,
        ICharacterSourceDataResolver sourceResolver, CharacterCreationKarmaMetatypeConfirmRequest request,
        out WorkspaceDocument? replacement, out CharacterCreationKarmaMetatypeDecision? decision)
    {
        replacement = null;
        decision = null;
        if (!IsConfirmed(request) || !IsValidHistory(workspace) || Lookup(workspace, request) is not null)
            return false;
        var history = workspace.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions;
        if (history is { Count: >= MaximumDecisions }) return false;
        // Isolated view prevents a nested store read/lease or caller-provided quote.
        var view = new WorkspaceContinuationReadView(owner, workspace);
        var result = new CharacterCreationKarmaMetatypeService(view, sourceResolver)
            .Preview(request.Binding, request.MetatypeOptionId, request.TalentOptionId, request.AttributeAllocations,
                request.SkillsSelection, request.ResourceKarmaInvestment, request.QualityOptionIds);
        if (result.Value is not { CanSelect: true } quote || quote.QuoteDigest != request.QuoteDigest)
            return false;
        var prepared = new CharacterCreationKarmaMetatypeDecision(
            CharacterCreationKarmaMetatypeSchemas.DecisionV1, request, quote,
            (history?.Count ?? 0) + 1, workspace.ContentRevision + 1, string.Empty);
        prepared = prepared with { DecisionDigest = DecisionDigest(prepared) };
        var auxiliary = workspace.Document.AuxiliaryState with
        {
            CharacterCreationKarmaMetatypeDecisions = (history ?? []).Append(prepared).ToArray()
        };
        if (!IsValidLedger(workspace.Id, prepared.CommittedContentRevision, auxiliary)) return false;
        replacement = workspace.Document with { State = workspace.Document.State with { AuxiliaryState = auxiliary } };
        decision = prepared;
        return true;
    }

    public static CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> Blocked(string blocker)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);

    public static bool TryFreezeRequest(CharacterCreationKarmaMetatypeConfirmRequest request,
        out CharacterCreationKarmaMetatypeConfirmRequest frozen)
    {
        frozen = request;
        try
        {
            if (!IsConfirmed(request)) return false;
            // No caller-owned collection may change the command while storage waits
            // for a lease, writes its temporary file, or recovers a committed result.
            CharacterCreationKarmaSkillsSelection? skills = null;
            if (request.SkillsSelection is not null && !CharacterCreationKarmaSkillsRules.TryFreeze(request.SkillsSelection, out skills))
                return false;
            string[]? qualities = null;
            if (request.QualityOptionIds is not null
                && !CharacterCreationKarmaQualitiesRules.TryFreeze(request.QualityOptionIds, out qualities)) return false;
            frozen = request with { AttributeAllocations = request.AttributeAllocations?.Take(14).ToArray(),
                SkillsSelection = skills, QualityOptionIds = qualities };
            return IsConfirmed(frozen);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool Digest(string? value) => CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(value);

    private static bool ValidTalent(CharacterCreationKarmaTalentOption? talent, string? id)
    {
        if (talent is null) return id is null;
        if (id != talent.OptionId || talent is not { IsEnabled: true, KarmaCost: >= 0, Blockers.Count: 0,
            SourceAnchorIds.Count: > 0 } || talent.SourceNodeXml is null
            || talent.SourceNodeDigest != CharacterCreationQualitiesRules.ComputeSourceNodeDigest(talent.SourceNodeXml))
            return false;
        if (id == CharacterCreationKarmaTalentCatalog.MundaneOptionId)
            return talent.KarmaCost == 0 && talent.EnabledAttribute is null && talent.SourceNodeXml.Length == 0;
        return talent.EnabledAttribute is "MAG" or "RES" && talent.SourceNodeXml.Length is > 0 and <= 32 * 1024;
    }
}
