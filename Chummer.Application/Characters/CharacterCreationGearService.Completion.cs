using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationGearService
{
    /// <summary>
    /// Admit the real catalogue and history. Only a genuinely initial absent
    /// Resources draft is new; stale or unsupported retained history stays closed.
    /// </summary>
    public CharacterCreationGearResult<CharacterCreationGearCompletionInputs> LoadCompletionInputs(
        CharacterCreationGearLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
            return ReadFailure<CharacterCreationGearCompletionInputs>(read);
        return LoadCompletionInputsFromObservation(workspace);
    }

    internal CharacterCreationGearResult<CharacterCreationGearCompletionInputs> LoadCompletionInputsFromObservation(
        WorkspaceStoredDocument workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        AuthorityContext context = BuildContext(workspace);
        var receipts = workspace.Document.AuxiliaryState.CharacterCreationGearReceipts ?? [];
        bool initialResources = context.ResourcesDraft is null && context.PendingDraft is null
            && receipts.Count == 0 && context.Binding.ResourcesDraftRevision == 0
            && context.Binding.ResourcesDraftDigest == string.Empty;
        string[] blockers = Normalize(context.Blockers.Where(blocker =>
            !(initialResources && blocker == CharacterCreationGearBlockers.ResourcesDraftRequired)));
        if (blockers.Length != 0)
            return new(CharacterCreationGearOutcomes.Blocked, null, blockers);

        // Both readers inspect this actual observed document, not an invented
        // workspace or a later read that might already have changed revision.
        var resources = new CharacterCreationResourcesService(_workspaceStore, _sourceData)
            .LoadCompletionInputsFromObservation(workspace);
        if (resources.Value is not { } resourceInputs)
            return new(CharacterCreationGearOutcomes.Blocked, null,
                Normalize(resources.Blockers.Append(CharacterCreationGearCompletionSchemas.UpstreamBudgetUnavailable)));
        var inputs = new CharacterCreationGearCompletionInputs(CharacterCreationGearCompletionSchemas.InputsV1,
            CreateState(context), resourceInputs, receipts, string.Empty);
        inputs = inputs with { SnapshotDigest = CharacterCreationGearCompletionRules.ComputeInputsDigest(inputs) };
        if (!IsValidCompletionInputs(inputs))
            return Blocked<CharacterCreationGearCompletionInputs>(CharacterCreationGearOutcomes.Blocked,
                CharacterCreationGearBlockers.AuthorityUnavailable);
        return new(CharacterCreationGearOutcomes.Available, DetachCompletion(inputs), []);
    }

    /// <summary>
    /// Recompute upstream Karma/funding, then price the full proposed basket.
    /// Existing Gear is replaced in the projection, never charged a second time.
    /// This operation cannot persist, issue receipts or authorize finalization.
    /// </summary>
    public static CharacterCreationGearResult<CharacterCreationGearCompletionProjection> PreviewCompletion(
        CharacterCreationGearCompletionInputs inputs,
        CharacterCreationMagicResonanceCompletionInputs magicInputs,
        CharacterCreationQualitiesCompletionInputs qualitiesInputs,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
        CharacterCreationMagicResonanceSelections magicSelections,
        IReadOnlyList<string> qualityOptionIds,
        int totalKarmaInvestment,
        IReadOnlyList<CharacterCreationGearSelection> basket,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(magicInputs);
        ArgumentNullException.ThrowIfNull(qualitiesInputs);
        ArgumentNullException.ThrowIfNull(attributeAllocations);
        ArgumentNullException.ThrowIfNull(magicSelections);
        ArgumentNullException.ThrowIfNull(qualityOptionIds);
        ArgumentNullException.ThrowIfNull(basket);
        cancellationToken.ThrowIfCancellationRequested();
        inputs = DetachCompletion(inputs);
        basket = basket.ToArray();
        if (!IsValidCompletionInputs(inputs))
            return Blocked<CharacterCreationGearCompletionProjection>(CharacterCreationGearOutcomes.Blocked,
                CharacterCreationGearBlockers.AuthorityUnavailable);

        var resources = CharacterCreationResourcesService.PreviewCompletion(inputs.Resources,
            magicInputs, qualitiesInputs, attributeAllocations, magicSelections, qualityOptionIds,
            totalKarmaInvestment, cancellationToken);
        if (resources.Value is not { } funding)
            return new(resources.Outcome == CharacterCreationResourcesOutcomes.Conflict
                    ? CharacterCreationGearOutcomes.Conflict
                    : resources.Outcome == CharacterCreationResourcesOutcomes.Invalid
                        ? CharacterCreationGearOutcomes.Invalid : CharacterCreationGearOutcomes.Blocked,
                null, Normalize(resources.Blockers.Append(CharacterCreationGearCompletionSchemas.UpstreamBudgetUnavailable)));

        cancellationToken.ThrowIfCancellationRequested();
        CharacterCreationGearRules.TryProjectBasket(basket, inputs.State.Authority,
            funding.Budget.TotalStartingNuyen, out var lines, out var budget, out var basketBlockers);
        // Only a complete source-priced basket may supersede the original
        // purchase observation. Invalid/partial baskets remain blocked without
        // manufacturing an exact replacement cost for Resources.
        if (basketBlockers.All(blocker => blocker == CharacterCreationGearBlockers.InsufficientFunds))
            funding = CharacterCreationResourcesService.RepriceCompletionPurchases(
                inputs.Resources, funding, budget.BasketCost);
        string[] blockers = Normalize(funding.Blockers.Concat(basketBlockers));
        string[] anchors = lines.SelectMany(line => line.SourceAnchorIds)
            .Concat(CharacterCreationGearSourceAnchors.All).Distinct(StringComparer.Ordinal)
            .OrderBy(anchor => anchor, StringComparer.Ordinal).ToArray();
        var projection = new CharacterCreationGearCompletionProjection(
            CharacterCreationGearCompletionSchemas.ProjectionV1, inputs.State.Binding,
            inputs.SnapshotDigest, funding, lines, budget, anchors, blockers, string.Empty);
        projection = projection with
        {
            ProposalDigest = CharacterCreationGearCompletionRules.ComputeProjectionDigest(projection)
        };
        cancellationToken.ThrowIfCancellationRequested();
        return new(blockers.Length == 0 ? CharacterCreationGearOutcomes.Available : CharacterCreationGearOutcomes.Blocked,
            DetachCompletion(projection), blockers);
    }

    private static bool IsValidCompletionInputs(CharacterCreationGearCompletionInputs inputs)
    {
        if (inputs.Schema != CharacterCreationGearCompletionSchemas.InputsV1
            || inputs.State is not { Binding: { } binding, Authority: { } authority, Budget: { } budget } state
            || inputs.Resources is not { Binding: { } resourcesBinding, Authority: { } resourcesAuthority } resources
            || state.Blockers is null || budget.Blockers is null || inputs.Receipts is null
            || inputs.Receipts.Count >= CharacterCreationGearReceiptLedgerIntegrity.MaximumEntries
            || resources.Receipts is null || resources.Receipts.Any(entry => entry?.Receipt is null)
            || resourcesAuthority.Blockers is null
            || authority.Options is null || authority.Blockers is null || authority.SourceAnchorIds is null
            || authority.Options.Any(option => option is null || option.Blockers is null || option.SourceAnchorIds is null)
            || !CharacterCreationGearRules.IsValidAuthority(authority)
            || !CharacterCreationResourcesRules.IsValidAuthority(resourcesAuthority)
            || state.Schema != CharacterCreationGearSchemas.StateV1 || state.StepId != CharacterCreationWizardStepIds.Resources
            || string.IsNullOrWhiteSpace(binding.WorkspaceId.Value)
            || binding.WorkspaceRevision != binding.ContentRevision
            || binding.ContentRevision <= 0 || binding.ContentRevision != binding.SavedRevision
            || !CharacterCreationGearRules.IsCanonicalDigest(binding.RawCharacterXmlDigest)
            // Workspace auxiliary digests deliberately use bare SHA-256 hex,
            // unlike the domain-level "sha256:" prefixed Gear digests.
            || binding.AuxiliaryStateDigest is not { Length: 64 }
            || binding.AuxiliaryStateDigest.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || !SameCompletionDigest(binding.AuthorityDigest, authority.AuthorityDigest)
            || !SameCompletionDigest(binding.SourceDigest, authority.SourceDigest)
            || !SameCompletionDigest(binding.RulesDigest, authority.RulesDigest)
            || !SameCompletionDigest(binding.RuntimeDigest, authority.RuntimeDigest)
            || !SameCompletionDigest(state.SnapshotDigest, CharacterCreationGearRules.ComputeStateDigest(state))
            || !SameCompletionDigest(inputs.SnapshotDigest, CharacterCreationGearCompletionRules.ComputeInputsDigest(inputs))
            || binding.WorkspaceId != resourcesBinding.WorkspaceId
            || binding.ContentRevision != resourcesBinding.ContentRevision || binding.SavedRevision != resourcesBinding.SavedRevision
            || !SameCompletionDigest(binding.RawCharacterXmlDigest, resourcesBinding.RawCharacterXmlDigest)
            || !SameCompletionDigest(binding.AuxiliaryStateDigest, resourcesBinding.AuxiliaryStateDigest)
            || authority.SettingsProfileId != resourcesAuthority.SettingsProfileId
            || !SameCompletionDigest(authority.ProfileDigest, resourcesAuthority.ProfileDigest)
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(state.ResourcesDraft, resources.PendingDraft)
            || binding.ResourcesDraftRevision != (resources.PendingDraft?.DraftRevision ?? 0)
            || binding.ResourcesDraftDigest != (resources.PendingDraft?.DraftDigest ?? string.Empty)
            || !resources.PurchaseAuthorityExact
            || !CharacterCreationResourcesService.TryGetRetainedCompletionLifestyleCost(resources, out decimal lifestyleCost)
            || !CharacterCreationResourcesService.TryGetRetainedCompletionLoadoutCost(resources, out decimal loadoutCost)
            || resources.KnownPurchaseCost - lifestyleCost - loadoutCost != (state.PendingDraft?.Budget?.BasketCost ?? 0m))
            return false;

        if (state.ResourcesDraft is { } savedResources && (savedResources.Budget?.Blockers is null
                || savedResources.FinalizationContribution?.SourceAnchorIds is null)
            || state.PendingDraft is { } savedGear && (savedGear.Budget?.Blockers is null
                || savedGear.FinalizationContribution?.SourceAnchorIds is null
                || savedGear.Lines is null || savedGear.Lines.Any(line => line is null || line.SourceAnchorIds is null)))
            return false;
        bool initial = state.ResourcesDraft is null && state.PendingDraft is null && inputs.Receipts.Count == 0;
        string[] expectedBlockers = initial ? [CharacterCreationGearBlockers.ResourcesDraftRequired] : [];
        if (!state.Blockers.SequenceEqual(expectedBlockers, StringComparer.Ordinal)
            || state.CanEdit != (expectedBlockers.Length == 0)
            || !CharacterCreationGearReceiptLedgerIntegrity.IsValidLedger(binding.WorkspaceId,
                binding.ContentRevision, state.PendingDraft, inputs.Receipts))
            return false;
        decimal total = state.ResourcesDraft?.Budget?.TotalStartingNuyen ?? 0m;
        decimal cost = state.PendingDraft?.Budget?.BasketCost ?? 0m;
        var expectedBudget = new CharacterCreationGearBudget(total, cost, Math.Max(0m, total - cost),
            Math.Max(0m, cost - total), expectedBlockers.Length == 0, expectedBlockers);
        if (!CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(budget, expectedBudget))
            return false;
        if (state.PendingDraft is not { } pending)
            return initial || state.ResourcesDraft is not null;
        if (pending.Lines is null || pending.Lines.Any(line => line is null || line.SourceAnchorIds is null)
            || pending.Budget?.Blockers is null || pending.FinalizationContribution?.SourceAnchorIds is null
            || state.ResourcesDraft is null
            || !SameCompletionDigest(pending.BaseRawCharacterXmlDigest, binding.RawCharacterXmlDigest)
            || !SameCompletionDigest(pending.AuthorityDigest, authority.AuthorityDigest)
            || !SameCompletionDigest(pending.SourceDigest, authority.SourceDigest)
            || !SameCompletionDigest(pending.RulesDigest, authority.RulesDigest)
            || !SameCompletionDigest(pending.RuntimeDigest, authority.RuntimeDigest)
            || !TryResolvePendingResourcesTotal(pending, state.ResourcesDraft, resources.Receipts, out decimal historicalTotal))
            return false;
        return PendingMatchesCurrentAuthority(pending, authority, historicalTotal);
    }

    private static bool SameCompletionDigest(string? left, string? right)
        => CharacterCreationGearRules.DigestsEqual(left, right);

    private static T DetachCompletion<T>(T value)
        => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
}
