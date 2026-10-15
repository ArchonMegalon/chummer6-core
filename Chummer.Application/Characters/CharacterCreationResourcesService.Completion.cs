using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationResourcesService
{
    /// <summary>
    /// Admit the real source, prerequisite, purchase cost and retained Resources
    /// history without requiring an invented intermediate Attributes draft.
    /// </summary>
    public CharacterCreationResourcesResult<CharacterCreationResourcesCompletionInputs> LoadCompletionInputs(
        CharacterCreationResourcesLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
            return ReadFailure<CharacterCreationResourcesCompletionInputs>(read);
        return LoadCompletionInputsFromObservation(workspace);
    }

    internal CharacterCreationResourcesResult<CharacterCreationResourcesCompletionInputs> LoadCompletionInputsFromObservation(
        WorkspaceStoredDocument workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        AuthorityContext context = BuildContext(workspace);
        string[] blockers = Normalize(context.Blockers.Concat(context.Budget.Blockers)
            .Concat(ValidateContinuationDraft(workspace)));
        if (blockers.Length != 0 || context.Prerequisite is null || context.PriorityOption is null)
            return new(CharacterCreationResourcesOutcomes.Blocked, null,
                blockers.Length == 0 ? [CharacterCreationResourcesBlockers.AuthorityUnavailable] : blockers);

        var inputs = new CharacterCreationResourcesCompletionInputs(
            CharacterCreationResourcesCompletionSchemas.InputsV1,
            context.Binding, context.Authority, context.Prerequisite, context.PriorityOption,
            context.PendingDraft, workspace.Document.AuxiliaryState.CharacterCreationResourcesReceipts ?? [],
            context.PurchaseAuthorityExact, context.KnownPurchaseCost, string.Empty)
        {
            LifestylesDraft = workspace.Document.AuxiliaryState.CharacterCreationLifestylesDraft,
            LifestyleReceipts = workspace.Document.AuxiliaryState.CharacterCreationLifestyleReceipts ?? []
        };
        // Validate retained purchases against the actual saved graph and current
        // source before carrying their original draft/ledger into a proposal.
        // TryLoad may project that real history; it never receives proposed data.
        if (!TryGetRetainedCompletionLifestyleCost(inputs, out decimal lifestyleCost)
            || inputs.LifestylesDraft is { } retained
                && (!CharacterCreationLifestylesDraftRules.TryLoad(workspace, _sourceData,
                        out var current, out _, out _)
                    || current is null
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                        retained.ProjectionAuthority, current.ProjectionAuthority)))
            return Blocked<CharacterCreationResourcesCompletionInputs>(CharacterCreationResourcesOutcomes.Blocked,
                CharacterCreationResourcesBlockers.PurchaseCostAuthorityRequired);
        try { inputs = inputs with { KnownPurchaseCost = checked(context.KnownPurchaseCost + lifestyleCost) }; }
        catch (OverflowException)
        {
            return Blocked<CharacterCreationResourcesCompletionInputs>(CharacterCreationResourcesOutcomes.Blocked,
                CharacterCreationResourcesBlockers.PurchaseCostAuthorityRequired);
        }
        inputs = inputs with
        {
            SnapshotDigest = CharacterCreationResourcesCompletionRules.ComputeInputsDigest(inputs)
        };
        return new(CharacterCreationResourcesOutcomes.Available, DetachCompletion(inputs), []);
    }

    /// <summary>
    /// Pure cumulative projection. Recompute the real upstream evaluations and
    /// price the full investment, never a saved-investment delta. No proposed
    /// draft, receipt or finalization contribution is issued by this operation.
    /// </summary>
    public static CharacterCreationResourcesResult<CharacterCreationResourcesCompletionProjection> PreviewCompletion(
        CharacterCreationResourcesCompletionInputs resourcesInputs,
        CharacterCreationMagicResonanceCompletionInputs magicInputs,
        CharacterCreationQualitiesCompletionInputs qualitiesInputs,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
        CharacterCreationMagicResonanceSelections magicSelections,
        IReadOnlyList<string> qualityOptionIds,
        int totalKarmaInvestment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourcesInputs);
        ArgumentNullException.ThrowIfNull(magicInputs);
        ArgumentNullException.ThrowIfNull(qualitiesInputs);
        ArgumentNullException.ThrowIfNull(attributeAllocations);
        ArgumentNullException.ThrowIfNull(magicSelections);
        ArgumentNullException.ThrowIfNull(qualityOptionIds);
        cancellationToken.ThrowIfCancellationRequested();
        resourcesInputs = DetachCompletion(resourcesInputs);
        magicInputs = DetachCompletion(magicInputs);
        qualitiesInputs = DetachCompletion(qualitiesInputs);
        attributeAllocations = attributeAllocations.ToArray();
        magicSelections = DetachCompletion(magicSelections);
        qualityOptionIds = qualityOptionIds.ToArray();
        if (!IsValidCompletionInputs(resourcesInputs))
            return Blocked<CharacterCreationResourcesCompletionProjection>(CharacterCreationResourcesOutcomes.Blocked,
                CharacterCreationResourcesBlockers.AuthorityUnavailable);
        if (!MatchesCompletionUpstream(resourcesInputs, magicInputs, qualitiesInputs))
            return Blocked<CharacterCreationResourcesCompletionProjection>(CharacterCreationResourcesOutcomes.Conflict,
                CharacterCreationResourcesCompletionSchemas.SnapshotConflict);

        var upstreamResult = CharacterCreationCompletionBudgetService.Preview(magicInputs, qualitiesInputs,
            attributeAllocations, magicSelections, qualityOptionIds, cancellationToken);
        if (upstreamResult.Value is not { AvailableKarmaBeforeResources: int available } upstream)
            return new(upstreamResult.Outcome == CharacterCreationFoundationOutcomes.Conflict
                    ? CharacterCreationResourcesOutcomes.Conflict : CharacterCreationResourcesOutcomes.Blocked, null,
                Normalize(upstreamResult.Blockers.Append(CharacterCreationResourcesCompletionSchemas.UpstreamBudgetUnavailable)));
        cancellationToken.ThrowIfCancellationRequested();
        var pricing = PriceSelectedCompletion(new(resourcesInputs.Authority, resourcesInputs.PriorityOption,
            available, totalKarmaInvestment, resourcesInputs.PurchaseAuthorityExact,
            resourcesInputs.KnownPurchaseCost, upstream.Blockers), cancellationToken);
        if (pricing.Value is not { } priced)
            return new(pricing.Outcome, null, pricing.Blockers);
        var projection = new CharacterCreationResourcesCompletionProjection(
            CharacterCreationResourcesCompletionSchemas.ProjectionV1, resourcesInputs.Binding,
            resourcesInputs.SnapshotDigest, upstream, priced.Options, priced.SelectedOption, priced.Budget,
            priced.RemainingCreationKarma, priced.Blockers, string.Empty);
        projection = projection with
        {
            ProposalDigest = CharacterCreationResourcesCompletionRules.ComputeProjectionDigest(projection)
        };
        cancellationToken.ThrowIfCancellationRequested();
        return new(projection.Blockers.Count == 0 ? CharacterCreationResourcesOutcomes.Available
            : CharacterCreationResourcesOutcomes.Blocked, DetachCompletion(projection), projection.Blockers.ToArray());
    }

    /// <summary>
    /// Shared pricing below the distinct live/historical admission envelopes.
    /// FullPurchaseCost is the complete independently repriced basket, not a
    /// saved-cost delta. This internal result cannot be confirmed or persisted.
    /// </summary>
    internal static CharacterCreationResourcesResult<SelectedCompletionResourcesPrice> PriceSelectedCompletion(
        SelectedCompletionResourcesPricingInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        input = DetachCompletion(input);
        if (input.Authority is null || input.PriorityOption is null || input.UpstreamBlockers is null
            || !CharacterCreationResourcesRules.IsValidAuthority(input.Authority)
            || !CharacterCreationResourcesRules.IsValidPriorityOption(input.PriorityOption)
            || input.FullPurchaseCost < 0m)
            return Blocked<SelectedCompletionResourcesPrice>(CharacterCreationResourcesOutcomes.Blocked,
                CharacterCreationResourcesBlockers.AuthorityUnavailable);
        var options = BuildOptions(input.Authority, input.PriorityOption, input.AvailableKarma);
        var selected = options.SingleOrDefault(option => option.KarmaInvestment == input.TotalKarmaInvestment);
        if (selected is null)
            return new(CharacterCreationResourcesOutcomes.Invalid, null,
                Normalize(input.UpstreamBlockers.Append(CharacterCreationResourcesBlockers.InvalidOption)));
        var blockers = new List<string>(input.UpstreamBlockers);
        blockers.AddRange(selected.Blockers);
        var budget = BuildBudget(input.Authority, input.PriorityOption, selected,
            input.PurchaseAuthorityExact, input.FullPurchaseCost, blockers);
        string[] normalized = Normalize(blockers);
        var result = new SelectedCompletionResourcesPrice(options, selected, budget,
            input.AvailableKarma - selected.KarmaInvestment, normalized);
        cancellationToken.ThrowIfCancellationRequested();
        return new(normalized.Length == 0 ? CharacterCreationResourcesOutcomes.Available
            : CharacterCreationResourcesOutcomes.Blocked, result, normalized);
    }

    /// <summary>
    /// Gear calls this only after validating the original inputs and pricing its
    /// full replacement basket with the real catalogue evaluator. The original
    /// purchase observation and receipts remain untouched. This is intentionally
    /// not a public raw-cost override for completion callers.
    /// </summary>
    internal static CharacterCreationResourcesCompletionProjection RepriceCompletionPurchases(
        CharacterCreationResourcesCompletionInputs inputs,
        CharacterCreationResourcesCompletionProjection projection,
        decimal sourcePricedGearCost)
    {
        if (!TryGetRetainedCompletionLifestyleCost(inputs, out decimal retainedCost))
            throw new ArgumentException("The retained Lifestyle purchase history is invalid.", nameof(inputs));
        return RepriceCompletionPurchases(inputs, projection, sourcePricedGearCost, retainedCost);
    }

    // Lifestyle completion supplies a full source-priced replacement list. Gear
    // alone uses the overload above, preserving the original Lifestyle spending.
    internal static CharacterCreationResourcesCompletionProjection RepriceCompletionPurchases(
        CharacterCreationResourcesCompletionInputs inputs,
        CharacterCreationResourcesCompletionProjection projection,
        decimal sourcePricedGearCost,
        decimal sourcePricedLifestyleCost)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentOutOfRangeException.ThrowIfNegative(sourcePricedGearCost);
        ArgumentOutOfRangeException.ThrowIfNegative(sourcePricedLifestyleCost);
        if (inputs.Binding != projection.Binding
            || !SameCompletionDigest(inputs.SnapshotDigest, projection.ResourcesSnapshotDigest))
            throw new ArgumentException("The funding projection must match the original Resources inputs.", nameof(projection));

        // The old purchase shortage is superseded, not ignored: price the exact
        // new basket and rebuild its finding, retaining every upstream blocker.
        var blockers = new List<string>(projection.UpstreamBudget.Blockers);
        blockers.AddRange(projection.SelectedOption.Blockers);
        CharacterCreationResourcesBudget budget = BuildBudget(inputs.Authority,
            inputs.PriorityOption, projection.SelectedOption, inputs.PurchaseAuthorityExact,
            checked(sourcePricedGearCost + sourcePricedLifestyleCost), blockers);
        var repriced = projection with { Budget = budget, Blockers = Normalize(blockers), ProposalDigest = string.Empty };
        return repriced with
        {
            ProposalDigest = CharacterCreationResourcesCompletionRules.ComputeProjectionDigest(repriced)
        };
    }

    private static bool IsValidCompletionInputs(CharacterCreationResourcesCompletionInputs inputs)
    {
        if (inputs.Schema != CharacterCreationResourcesCompletionSchemas.InputsV1
            || inputs.Binding is not { } binding || inputs.Authority is not { } authority
            || inputs.PrerequisiteDraft is not { } prerequisite || inputs.PriorityOption is not { } priority
            || inputs.Receipts is null || inputs.Receipts.Count >= CharacterCreationResourcesReceiptLedgerIntegrity.MaximumEntries
            || authority.Blockers is null || !CharacterCreationResourcesRules.IsValidAuthority(authority)
            || !CharacterCreationResourcesRules.IsValidPriorityOption(priority)
            || !inputs.PurchaseAuthorityExact || inputs.KnownPurchaseCost < 0m
            || !TryGetRetainedCompletionLifestyleCost(inputs, out decimal lifestyleCost)
            || inputs.KnownPurchaseCost < lifestyleCost
            || string.IsNullOrWhiteSpace(binding.WorkspaceId.Value)
            || binding.WorkspaceRevision != binding.ContentRevision
            || binding.ContentRevision <= 0 || binding.ContentRevision != binding.SavedRevision
            || !CharacterCreationResourcesRules.IsCanonicalDigest(binding.RawCharacterXmlDigest)
            || !IsCompletionAuxiliaryDigest(binding.AuxiliaryStateDigest)
            || binding.PrerequisiteDraftRevision != prerequisite.DraftRevision
            || !SameCompletionDigest(binding.PrerequisiteDraftDigest, prerequisite.DraftDigest)
            || !SameCompletionDigest(binding.AuthorityDigest, authority.AuthorityDigest)
            || !SameCompletionDigest(binding.SourceDigest, authority.SourceDigest)
            || !SameCompletionDigest(binding.RulesDigest, authority.RulesDigest)
            || !SameCompletionDigest(binding.RuntimeDigest, authority.RuntimeDigest)
            || authority.BuildMethod != prerequisite.BuildMethod
            || prerequisite.Assignments is null
            || !SameCompletionDigest(inputs.SnapshotDigest,
                CharacterCreationResourcesCompletionRules.ComputeInputsDigest(inputs)))
            return false;

        var assignments = prerequisite.Assignments.Where(assignment => assignment is not null
            && assignment.CategoryId == CharacterCreationPriorityCategoryIds.Resources).ToArray();
        if (assignments.Length != 1 || assignments[0].SourceId != priority.SourceId
            || assignments[0].Rank != priority.Rank
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(priority,
                authority.PriorityOptions.SingleOrDefault(option => option.SourceId == priority.SourceId)))
            return false;
        if (inputs.PendingDraft is { } draft
            && (draft.Budget?.Blockers is null || draft.FinalizationContribution?.SourceAnchorIds is null))
            return false;
        if (!CharacterCreationResourcesReceiptLedgerIntegrity.IsValidLedger(binding.WorkspaceId,
                binding.ContentRevision, inputs.PendingDraft, inputs.Receipts))
            return false;
        if (inputs.PendingDraft is not { } pending)
            return true;
        if (pending.PrerequisiteDraftRevision != binding.PrerequisiteDraftRevision
            || !SameCompletionDigest(pending.PrerequisiteDraftDigest, binding.PrerequisiteDraftDigest)
            || !SameCompletionDigest(pending.BaseRawCharacterXmlDigest, binding.RawCharacterXmlDigest)
            || !SameCompletionDigest(pending.AuthorityDigest, authority.AuthorityDigest)
            || !SameCompletionDigest(pending.SourceDigest, authority.SourceDigest)
            || !SameCompletionDigest(pending.RulesDigest, authority.RulesDigest)
            || !SameCompletionDigest(pending.RuntimeDigest, authority.RuntimeDigest))
            return false;

        // Price retained history against its actual source and recorded purchase
        // cost. The current Gear basket may legitimately have been saved later.
        var option = BuildOptions(authority, priority, int.MaxValue)
            .SingleOrDefault(item => item.OptionId == pending.SelectedOptionId);
        return option is not null && pending.KarmaInvestment == option.KarmaInvestment
            && pending.Budget.KnownPurchaseCost <= option.TotalStartingNuyen
            && pending.SourceAnchorIds.SequenceEqual(option.SourceAnchorIds, StringComparer.Ordinal)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(pending.Budget,
                BuildBudget(authority, priority, option, true, pending.Budget.KnownPurchaseCost, null))
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(pending.FinalizationContribution,
                BuildContribution(priority, option, binding.RawCharacterXmlDigest));
    }

    private static bool MatchesCompletionUpstream(CharacterCreationResourcesCompletionInputs resources,
        CharacterCreationMagicResonanceCompletionInputs magic,
        CharacterCreationQualitiesCompletionInputs qualities)
    {
        if (magic.Attributes?.Binding is null || qualities.Attributes?.Binding is not { } original
            || magic.Authority is null || qualities.Authority is null)
            return false;
        var binding = resources.Binding;
        return binding.WorkspaceId == original.WorkspaceId
            && binding.ContentRevision == original.ContentRevision && binding.SavedRevision == original.SavedRevision
            && SameCompletionDigest(binding.RawCharacterXmlDigest, original.RawCharacterXmlDigest)
            && SameCompletionDigest(binding.AuxiliaryStateDigest, original.AuxiliaryStateDigest)
            && binding.PrerequisiteDraftRevision == original.PrerequisiteDraftRevision
            && SameCompletionDigest(binding.PrerequisiteDraftDigest, original.PrerequisiteDraftDigest)
            && resources.Authority.SettingsProfileId == magic.Authority.SettingsProfileId
            && resources.Authority.SettingsProfileId == qualities.Authority.SettingsProfileId
            && SameCompletionDigest(resources.Authority.ProfileDigest, qualities.Authority.ProfileDigest)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                resources.PrerequisiteDraft, magic.PrerequisiteDraft)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                resources.PrerequisiteDraft, qualities.PrerequisiteDraft);
    }

    internal static bool TryGetRetainedCompletionLifestyleCost(
        CharacterCreationResourcesCompletionInputs inputs, out decimal cost)
    {
        cost = 0m;
        try
        {
            if (inputs?.Binding is not { } binding || inputs.Authority is not { } resources
                || inputs.LifestyleReceipts is not { } receipts
                || receipts.Count >= CharacterCreationLifestyleReceiptLedgerIntegrity.MaximumEntries
                || !CharacterCreationLifestyleReceiptLedgerIntegrity.IsValidLedger(
                    binding.WorkspaceId, binding.ContentRevision, receipts)) return false;
            if (inputs.LifestylesDraft is not { } draft) return receipts.Count == 0;
            if (!CharacterCreationLifestylesDraftRules.IsValidShape(binding.WorkspaceId, binding.ContentRevision, draft)
                || receipts.Count == 0
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    draft, receipts[^1].Receipt.WritePlan.PendingDraft)
                || draft.InputAuthority.PrerequisiteDraftDigest != binding.PrerequisiteDraftDigest
                || draft.InputAuthority.RawCharacterXmlDigest != binding.RawCharacterXmlDigest
                || draft.ProjectionAuthority.SettingsProfileId != resources.SettingsProfileId
                || draft.ProjectionAuthority.ProfileDigest != resources.ProfileDigest) return false;
            foreach (var selection in draft.Lifestyles)
            {
                if (!CharacterCreationLifestylesRules.TryProject(selection, draft.ProjectionAuthority,
                        out var projection, out _)) return false;
                cost = checked(cost + projection.Economics.TotalCost);
            }
            return cost >= 0m && cost == receipts[^1].Receipt.LifestyleCostAfter;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { cost = 0m; return false; }
    }

    private static bool SameCompletionDigest(string? left, string? right)
        => CharacterCreationResourcesRules.DigestsEqual(left, right);

    // WorkspaceDocumentAuxiliaryStateDigest deliberately uses bare lowercase
    // SHA-256, unlike the prefixed Resources source and receipt digests.
    private static bool IsCompletionAuxiliaryDigest(string? value)
        => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static T DetachCompletion<T>(T value)
        => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
}
