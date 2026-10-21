using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
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
        if (!CharacterCreationCompositeBaselineReader.TryRead(workspace, out var composite))
            return Blocked<CharacterCreationResourcesCompletionInputs>(CharacterCreationResourcesOutcomes.Blocked,
                CharacterCreationCompositeBaselineReader.Invalid);
        AuthorityContext context = BuildContext(workspace);
        string[] blockers = Normalize(context.Blockers.Concat(context.Budget.Blockers)
            .Concat(ValidateContinuationDraft(workspace)).Where(blocker =>
                !(composite is not null && blocker == CharacterCreationResourcesBlockers.InsufficientNuyen)));
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
            LifestyleReceipts = workspace.Document.AuxiliaryState.CharacterCreationLifestyleReceipts ?? [],
            LoadoutDraft = workspace.Document.AuxiliaryState.CharacterCreationLoadoutDraft,
            LoadoutReceipts = workspace.Document.AuxiliaryState.CharacterCreationLoadoutReceipts
        };
        // Validate retained purchases against the actual saved graph and current
        // source before carrying their original draft/ledger into a proposal.
        // TryLoad may project that real history; it never receives proposed data.
        if (!TryGetRetainedCompletionLifestyleCost(inputs, out decimal lifestyleCost)
            || inputs.LifestylesDraft is { } retained
                && !(CharacterCreationLifestylesDraftRules.AdmitsRetainedCompletionHistory(workspace, _sourceData, retained)
                    || CharacterCreationLifestylesDraftRules.TryLoad(workspace, _sourceData,
                        out var current, out _, out _) && current is not null
                        && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
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
        return PreviewDetachedCompletion(resourcesInputs, magicInputs, qualitiesInputs, attributeAllocations,
            magicSelections, qualityOptionIds, totalKarmaInvestment, cancellationToken);
    }

    // Only the completion services' private, detached observations enter here.
    // The purchase planner retains this invariant funding result for one Plan;
    // every changed basket still passes through RepriceCompletionPurchases.
    internal static CharacterCreationResourcesResult<CharacterCreationResourcesCompletionProjection> PreviewDetachedCompletion(
        CharacterCreationResourcesCompletionInputs resourcesInputs,
        CharacterCreationMagicResonanceCompletionInputs magicInputs,
        CharacterCreationQualitiesCompletionInputs qualitiesInputs,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
        CharacterCreationMagicResonanceSelections magicSelections,
        IReadOnlyList<string> qualityOptionIds,
        int totalKarmaInvestment,
        CancellationToken cancellationToken,
        Action? upstreamBudgetEvaluated = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsValidCompletionInputs(resourcesInputs))
            return Blocked<CharacterCreationResourcesCompletionProjection>(CharacterCreationResourcesOutcomes.Blocked,
                CharacterCreationResourcesBlockers.AuthorityUnavailable);
        if (!MatchesCompletionUpstream(resourcesInputs, magicInputs, qualitiesInputs))
            return Blocked<CharacterCreationResourcesCompletionProjection>(CharacterCreationResourcesOutcomes.Conflict,
                CharacterCreationResourcesCompletionSchemas.SnapshotConflict);

        var upstreamResult = CharacterCreationCompletionBudgetService.Preview(magicInputs, qualitiesInputs,
            attributeAllocations, magicSelections, qualityOptionIds, cancellationToken);
        upstreamBudgetEvaluated?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        if (upstreamResult.Value is not { AvailableKarmaBeforeResources: int available } upstream)
            return new(upstreamResult.Outcome == CharacterCreationFoundationOutcomes.Conflict
                    ? CharacterCreationResourcesOutcomes.Conflict : CharacterCreationResourcesOutcomes.Blocked, null,
                Normalize(upstreamResult.Blockers.Append(CharacterCreationResourcesCompletionSchemas.UpstreamBudgetUnavailable)));
        cancellationToken.ThrowIfCancellationRequested();
        var pricing = PriceSelectedCompletion(new(resourcesInputs.Authority, resourcesInputs.PriorityOption,
            available, totalKarmaInvestment, resourcesInputs.PurchaseAuthorityExact,
            resourcesInputs.KnownPurchaseCost, upstream.Blockers), cancellationToken,
            resourcesInputs.PrerequisiteDraft.CreationKarmaTotal);
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
        SelectedCompletionResourcesPricingInput input, CancellationToken cancellationToken = default,
        int? creationKarmaTotal = null, bool includeOptions = true)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        input = DetachCompletion(input);
        if (input.Authority is null || input.PriorityOption is null || input.UpstreamBlockers is null
            || !CharacterCreationResourcesRules.IsValidAuthority(input.Authority)
            || !CharacterCreationResourcesRules.IsValidPriorityOption(input.PriorityOption)
            || input.FullPurchaseCost < 0m || input.AvailableKarma < 0)
            return Blocked<SelectedCompletionResourcesPrice>(CharacterCreationResourcesOutcomes.Blocked,
                CharacterCreationResourcesBlockers.AuthorityUnavailable);
        var selected = CreateAllocationOption(input.Authority, input.PriorityOption,
            input.AvailableKarma, input.TotalKarmaInvestment);
        if (selected is null)
            return new(CharacterCreationResourcesOutcomes.Invalid, null,
                Normalize(input.UpstreamBlockers.Append(CharacterCreationResourcesBlockers.InvalidOption)));
        // History validation only needs its recorded choice and must not enumerate
        // the Int32.MaxValue allowance sentinel. Execution paths retain the catalogue.
        CharacterCreationResourceAllocationOption[] options = includeOptions
            ? BuildOptions(input.Authority, input.PriorityOption, input.AvailableKarma, creationKarmaTotal, cancellationToken)
            : [selected];
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
        return RepriceCompletionPurchases(inputs, projection, sourcePricedGearCost, retainedCost,
            inputs.LoadoutDraft?.PurchaseCost ?? 0m);
    }

    // Lifestyle completion supplies a full source-priced replacement list. Gear
    // alone uses the overload above, preserving the original Lifestyle spending.
    internal static CharacterCreationResourcesCompletionProjection RepriceCompletionPurchases(
        CharacterCreationResourcesCompletionInputs inputs,
        CharacterCreationResourcesCompletionProjection projection,
        decimal sourcePricedGearCost,
        decimal sourcePricedLifestyleCost,
        decimal sourcePricedLoadoutCost = 0m)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentOutOfRangeException.ThrowIfNegative(sourcePricedGearCost);
        ArgumentOutOfRangeException.ThrowIfNegative(sourcePricedLifestyleCost);
        ArgumentOutOfRangeException.ThrowIfNegative(sourcePricedLoadoutCost);
        if (inputs.Binding != projection.Binding
            || !SameCompletionDigest(inputs.SnapshotDigest, projection.ResourcesSnapshotDigest))
            throw new ArgumentException("The funding projection must match the original Resources inputs.", nameof(projection));

        // The old purchase shortage is superseded, not ignored: price the exact
        // new basket and rebuild its finding, retaining every upstream blocker.
        var blockers = new List<string>(projection.UpstreamBudget.Blockers);
        blockers.AddRange(projection.SelectedOption.Blockers);
        CharacterCreationResourcesBudget budget = BuildBudget(inputs.Authority,
            inputs.PriorityOption, projection.SelectedOption, inputs.PurchaseAuthorityExact,
            checked(sourcePricedGearCost + sourcePricedLifestyleCost + sourcePricedLoadoutCost), blockers);
        var repriced = projection with { Budget = budget, Blockers = Normalize(blockers), ProposalDigest = string.Empty };
        return repriced with
        {
            ProposalDigest = CharacterCreationResourcesCompletionRules.ComputeProjectionDigest(repriced)
        };
    }

    /// <summary>
    /// Read the one saved funding choice against all admitted current purchases.
    /// This does not change its historical receipt or invent a new Karma choice.
    /// </summary>
    internal static CharacterCreationResourcesBudget? ReadSavedPurchaseBudget(
        CharacterCreationResourcesCompletionInputs inputs)
    {
        if (!IsValidCompletionInputs(inputs) || inputs.PendingDraft is not { } saved)
            return null;
        var option = FindAllocationOption(inputs.Authority, inputs.PriorityOption,
            int.MaxValue, saved.SelectedOptionId);
        return option is null ? null : BuildBudget(inputs.Authority, inputs.PriorityOption,
            option, inputs.PurchaseAuthorityExact, inputs.KnownPurchaseCost, null);
    }

    // Invocation-local evidence. Only successful canonical pricing can construct
    // this object; neither a receipt nor a copied projection can restore it.
    internal sealed class CompleteFundingExecution
    {
        private readonly string originalDigest;
        private readonly string authorityDigest;
        private readonly string upstreamDigest;
        private readonly SelectedCompletionResourcesPricingInput input;
        private readonly SelectedCompletionResourcesPrice price;
        private readonly CharacterCreationCompleteUpstreamCostsExecution costs;

        private CompleteFundingExecution(object original, object upstream,
            SelectedCompletionResourcesPricingInput input, SelectedCompletionResourcesPrice price,
            CharacterCreationCompleteAssessment authority, CharacterCreationCompleteUpstreamCostsExecution costs)
        {
            originalDigest = CharacterCreationFinalizationDigest.Compute(original);
            authorityDigest = CharacterCreationFinalizationDigest.Compute(authority);
            upstreamDigest = CharacterCreationFinalizationDigest.Compute(upstream);
            this.input = DetachCompletion(input);
            this.price = DetachCompletion(price);
            this.costs = costs;
            EvaluationDigest = CharacterCreationFinalizationDigest.Compute(new
                { Input = this.input, Result = this.price, Costs = costs.EvaluationDigest });
        }

        internal string EvaluationDigest { get; }
        internal bool TryGetCarryoverBudget995(CharacterCreationCompleteAssessment authority,
            out int remainingKarma, out decimal remainingNuyen, out decimal nuyenLimit)
        {
            remainingKarma = 0; remainingNuyen = 0; nuyenLimit = 0;
            if (!CanExecute(authority) || authorityDigest != CharacterCreationFinalizationDigest.Compute(authority)
                || price.Blockers.Count != 0 || !price.Budget.IsExact || price.Budget.Blockers.Count != 0
                || price.RemainingCreationKarma < 0 || price.Budget.RemainingNuyen < 0
                || price.Budget.Overspend != 0) return false;
            remainingKarma = price.RemainingCreationKarma;
            remainingNuyen = price.Budget.RemainingNuyen;
            nuyenLimit = price.Budget.CarryoverLimit;
            return true;
        }
        internal string CopySourceResult718() => JsonSerializer.Serialize(new
        {
            Operation = "PriceSelectedCompletion", Input = input, Result = price
        });
        internal string? LoadoutEvaluationDigest => costs.LoadoutEvaluationDigest;

        // The existing canonical execution owns these terms even at zero investment:
        // The canonical option constructor and bounded catalogue both use the rate
        // and ceiling; the wallet bounds display, never changes the profile rule.
        // A serialized ResourcesExecution718 is diagnostic data, never this capability.
        internal void BindSourceInvocation994(CharacterCreationRuleSourceScopeInvocation invocation,
            CharacterCreationCompleteAssessment authority, CharacterCreationCompletionReviewInputs inputs,
            CharacterCreationCompletionReviewRequest request)
        {
            var resources = inputs.Lifestyles.Gear.Resources;
            if (!CanExecute(authority) || authorityDigest != CharacterCreationFinalizationDigest.Compute(authority)
                || !CharacterCreationCompleteRuleSourceExecution.CanAttemptSourceScopes696(request)
                || authority.InputsSnapshotDigest != inputs.SnapshotDigest || request.InputsSnapshotDigest != inputs.SnapshotDigest
                || authority.RequestDigest != CharacterCreationFinalizationDigest.Compute(request)
                || authority.SelectionsDigest != CharacterCreationFinalizationDigest.Compute(request.Selections)
                || authority.RawCharacterXmlDigest != resources.Binding.RawCharacterXmlDigest
                || authority.PolicyDigest != inputs.EffectivePolicy?.PolicyDigest
                || invocation.PolicyDigest != authority.PolicyDigest || invocation.RequestDigest != authority.RequestDigest
                || inputs.RuleSourceExecutionSchema != request.RuleSourceExecutionSchema
                || invocation.CaptureDigest != inputs.RuleSourceCaptureDigest || invocation.CaptureDigest != request.RuleSourceCaptureDigest
                || !CharacterCreationFinalizationDigest.IsCanonical(invocation.CaptureDigest)
                || !CharacterCreationFinalizationDigest.IsCanonical(invocation.GraphDigest)
                || invocation.WorkspaceId718 is null || invocation.WorkspaceId718 != inputs.OriginalCharacter?.WorkspaceId
                || invocation.WorkspaceId718 != resources.Binding.WorkspaceId
                || !Same(input.Authority, resources.Authority) || !Same(input.PriorityOption, resources.PriorityOption)
                || !Same(invocation.CreationDraft718, resources.PrerequisiteDraft)
                || !Same(invocation.Attributes718, invocation.Skills718?.Attributes)
                || request.Selections.TotalKarmaInvestment != input.TotalKarmaInvestment
                || price.SelectedOption.KarmaInvestment != input.TotalKarmaInvestment
                || input.TotalKarmaInvestment < 0 || input.TotalKarmaInvestment > input.Authority.MaximumKarmaInvestment
                || price.Options.Where(option => option.KarmaInvestment == input.TotalKarmaInvestment).ToArray() is not [var selected]
                || !Same(selected, price.SelectedOption) || invocation.ResourcesExecution718 != CopySourceResult718()
                || !invocation.TryGetCompletedConsumer("profile", out _)) return;
            var policy = inputs.EffectivePolicy!;
            var source = inputs.Magic.Authority.MysticAdeptPowerPointPolicy;
            if (policy.SettingsProfileId != input.Authority.SettingsProfileId
                || policy.ProfileInputsDigest != input.Authority.ProfileDigest
                || !CharacterCreationCompleteProfileExecution.SupportsDomain(policy, source, CharacterCreationPolicyDomain.Profile)
                || !CharacterCreationPolicyConsumptionProjection.MatchesSource(policy, source)) return;
            var profile = XElement.Parse(source!.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
            if (profile.HasAttributes) return;
            var leaves = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string field in new[] { "chargenkarmatonuyenexpression", "nuyenmaxbp" })
            {
                var nodes = profile.Elements().Where(node => node.Name.LocalName == field).ToArray();
                if (nodes is not [var leaf] || leaf.Name != field || leaf.HasAttributes
                    || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)) continue;
                leaves.Add(field, FundingXmlDigest994(leaf));
            }
            if (leaves.Count != 0)
                fundingInvocations994.Add(invocation, new(this, FundingInvocationDigest994(invocation),
                    FundingXmlDigest994(profile), leaves));
        }

        internal static bool TryReadSource994(CharacterCreationRuleSourceScopeInvocation invocation,
            CharacterCreationResourcesAuthority authority, string profileXmlDigest, string field, string leafXmlDigest,
            out string? result, out string? executionDigest)
        {
            result = null; executionDigest = null;
            if (!fundingInvocations994.TryGetValue(invocation, out var bound)
                || !Same(bound.Completed.input.Authority, authority)
                || bound.ProfileXmlDigest != profileXmlDigest
                || !bound.Leaves.TryGetValue(field, out var leaf) || leaf != leafXmlDigest
                || bound.InvocationDigest != FundingInvocationDigest994(invocation)) return false;
            result = bound.Completed.CopySourceResult718();
            executionDigest = CharacterCreationFinalizationDigest.Compute(new
            {
                Operation = "canonical-funding-options-and-selection", bound.Completed.authorityDigest,
                bound.Completed.EvaluationDigest, bound.InvocationDigest, bound.ProfileXmlDigest,
                Authority = bound.Completed.input.Authority, Field = field, Leaf = leaf
            });
            return true;
        }

        private sealed record BoundFunding994(CompleteFundingExecution Completed, string InvocationDigest,
            string ProfileXmlDigest, IReadOnlyDictionary<string, string> Leaves);
        private static readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation, BoundFunding994>
            fundingInvocations994 = new();

        private static string FundingInvocationDigest994(CharacterCreationRuleSourceScopeInvocation invocation)
        {
            invocation.TryGetCompletedConsumer("profile", out var consumer);
            return CharacterCreationFinalizationDigest.Compute(new
            {
                invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
                Consumer = consumer, invocation.CompiledEffects, invocation.CreationDraft718, invocation.WorkspaceId718,
                invocation.Attributes718, invocation.AttributePolicy718, invocation.Skills718,
                invocation.ResourcesExecution718, invocation.LifestyleAuthority718, invocation.Lifestyles718,
                invocation.StartingLifestyleId718, invocation.SourceExecution832, invocation.CompiledMovement832,
                invocation.PhysicalAuthority886, invocation.PhysicalInitiative886
            });
        }

        private static string FundingXmlDigest994(XElement node)
            => CharacterCreationFinalizationDigest.Compute(node.ToString(SaveOptions.DisableFormatting));

        internal static CompleteFundingExecution? FromFull(CharacterCreationCompletionReviewInputs inputs,
            CharacterCreationCompletionReview review, CharacterCreationCompleteAssessment? authority,
            CancellationToken cancellationToken = default,
            CharacterCreationLifestylesService.CompleteGraphExecution? lifestyles = null,
            CharacterCreationCompleteRuleSourceExecution? ruleSourceExecution = null)
        {
            if (!CanExecute(authority) || review.Blockers.Count != 0 || review.Lifestyles is null
                || review.UpstreamBudget is null || review.Request.Selections.Gear is null
                || inputs.SnapshotDigest != review.Request.InputsSnapshotDigest
                || inputs.SnapshotDigest != CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs)
                || !CharacterCreationEffectivePolicyValidation.IsValid(inputs.EffectivePolicy!, inputs)
                || !Same(authority, CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, review.Request, ruleSourceExecution))) return null;
            var original = inputs.Lifestyles.Gear.Resources;
            var choices = review.Request.Selections;
            // Prepare the upstream allowance from original inputs, not from the
            // caller's scalar or the saved investment delta. The retained basket
            // may be unaffordable before the proposed replacement is priced.
            var funding = PreviewCompletion(original, inputs.Magic, inputs.Lifestyles.Qualities,
                choices.AttributeAllocations, choices.MagicSelections, choices.QualityOptionIds,
                choices.TotalKarmaInvestment, cancellationToken);
            if (funding.Value is not { UpstreamBudget: { AvailableKarmaBeforeResources: int available, Blockers.Count: 0 } upstream }
                || !Same(upstream, review.UpstreamBudget)) return null;
            var costs = CharacterCreationCompleteUpstreamCostsExecution.FromFull(inputs, review, authority!, cancellationToken, lifestyles, ruleSourceExecution);
            if (costs is null) return null;
            var pricing = FullInput(inputs, review, available, costs);
            var execution = Execute(original, inputs, upstream, pricing, authority!, costs, cancellationToken);
            return execution?.MatchesFull(inputs, review, authority!) == true ? execution : null;
        }

        internal static CompleteFundingExecution? FromSelected(CharacterCreationSelectedCompletionWitness witness,
            CharacterCreationCompletionReviewRequest request, CharacterCreationSelectedCompletionEvaluation result,
            CharacterCreationCompleteAssessment? authority, CancellationToken cancellationToken = default,
            CharacterCreationLifestylesService.CompleteGraphExecution? lifestyles = null)
        {
            if (!CanExecute(authority) || request.Selections.Gear is null
                || authority!.InputsSnapshotDigest != witness.ProvenanceInputsSnapshotDigest
                || authority.RequestDigest != CharacterCreationFinalizationDigest.Compute(request)
                || request.InputsSnapshotDigest != witness.ProvenanceInputsSnapshotDigest
                || authority.RawCharacterXmlDigest != witness.Skills.Binding.RawCharacterXmlDigest
                || authority.SelectedInputsDigest != witness.SelectedPolicyInputs?.InputsDigest
                || authority.PolicyDigest != witness.EffectivePolicy?.PolicyDigest
                || authority.SelectionsDigest != CharacterCreationFinalizationDigest.Compute(request.Selections)
                || authority.SettingsObservationDigest != CharacterCreationFinalizationDigest.Compute(witness.EffectivePolicy?.SettingObservations)
                || result.Qualities.Blockers.Count != 0 || !IsValidCompletionInputs(witness.Resources)) return null;
            // Rebuild the attribute/quality cost basis as well as Magic. A coherent
            // replacement lifestyle quote must not borrow caller-supplied effects.
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = CharacterCreationAttributesService.CreateDraftSession(witness.Skills.Attributes)
                ?.Preview(request.Selections.AttributeAllocations);
            if (attributes?.Value is not { } proposed
                || proposed.Blockers.Any(blocker => blocker != CharacterCreationAttributesBlockers.DraftDuplicate)
                || !Same(proposed, result.Skills.Attributes)
                || proposed.CreationKarmaBudget.Used is < 0 or > int.MaxValue
                || proposed.CreationKarmaBudget.Used != decimal.Truncate(proposed.CreationKarmaBudget.Used)
                || !CharacterCreationQualitiesRules.TryEvaluateSelectedAuthority(witness.Qualities,
                    request.Selections.QualityOptionIds, witness.Skills.PrerequisiteDraft.CreationKarmaTotal,
                    (int)proposed.CreationKarmaBudget.Used, out var qualities)
                || qualities is not { Blockers.Count: 0 } || !Same(qualities, result.Qualities)) return null;
            var magic = CharacterCreationMagicResonanceService.EvaluateSelectedMagic(witness.Magic,
                witness.Skills.PrerequisiteDraft, witness.Skills.Attributes.Attributes,
                result.Skills.Attributes.Attributes, cancellationToken);
            if (magic.Outcome != CharacterCreationFoundationOutcomes.Success || magic.Blockers.Count != 0
                || magic.Value is null || !Same(magic.Value, result.Magic)
                || !CharacterCreationPriorityKarmaBudget.TryEvaluate(result.Qualities.KarmaRemaining, 0,
                    magic.Value.Proposed.MysticAdeptPowerPoints, out int available)) return null;
            var costs = CharacterCreationCompleteUpstreamCostsExecution.FromSelected(witness, request, result, authority!, cancellationToken, lifestyles);
            if (costs is null) return null;
            var execution = Execute(witness.Resources, witness, SelectedUpstream(result), SelectedInput(witness, request, available, costs),
                authority!, costs, cancellationToken);
            return execution?.MatchesSelected(witness, request, result, authority!) == true ? execution : null;
        }

        private static CompleteFundingExecution? Execute(CharacterCreationResourcesCompletionInputs resources, object original, object upstream,
            SelectedCompletionResourcesPricingInput input, CharacterCreationCompleteAssessment authority,
            CharacterCreationCompleteUpstreamCostsExecution costs,
            CancellationToken cancellationToken)
        {
            if (!IsValidCompletionInputs(resources)) return null;
            var priced = PriceSelectedCompletion(input, cancellationToken, resources.PrerequisiteDraft.CreationKarmaTotal);
            return priced.Outcome == CharacterCreationResourcesOutcomes.Available && priced.Blockers.Count == 0
                && priced.Value is { Blockers.Count: 0 } value
                ? new(original, upstream, input, value, authority, costs) : null;
        }

        internal bool MatchesFull(CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReview review,
            CharacterCreationCompleteAssessment authority)
        {
            if (review.Blockers.Count != 0 || review.UpstreamBudget is not { AvailableKarmaBeforeResources: int available }
                || review.Lifestyles?.Gear.Resources is not { } actual) return false;
            var original = inputs.Lifestyles.Gear.Resources;
            return authority.RequestDigest == CharacterCreationFinalizationDigest.Compute(review.Request)
                && Same(actual.Binding, original.Binding) && actual.ResourcesSnapshotDigest == original.SnapshotDigest
                && Same(actual.UpstreamBudget, review.UpstreamBudget)
                && costs.MatchesFull(inputs, review, authority)
                && Matches(inputs, review.UpstreamBudget, FullInput(inputs, review, available, costs),
                    new(actual.Options, actual.SelectedOption, actual.Budget, actual.RemainingCreationKarma, actual.Blockers), authority);
        }

        internal bool MatchesSelected(CharacterCreationSelectedCompletionWitness witness,
            CharacterCreationCompletionReviewRequest request, CharacterCreationSelectedCompletionEvaluation result,
            CharacterCreationCompleteAssessment authority)
            => authority.RequestDigest == CharacterCreationFinalizationDigest.Compute(request)
                && CharacterCreationPriorityKarmaBudget.TryEvaluate(result.Qualities.KarmaRemaining, 0,
                    result.Magic.Proposed.MysticAdeptPowerPoints, out int available)
                && costs.MatchesSelected(witness, request, result, authority)
                && Matches(witness, SelectedUpstream(result), SelectedInput(witness, request, available, costs), result.Resources, authority);

        private bool Matches(object original, object upstream, SelectedCompletionResourcesPricingInput expected,
            SelectedCompletionResourcesPrice actual, CharacterCreationCompleteAssessment authority)
            => authorityDigest == CharacterCreationFinalizationDigest.Compute(authority)
                && originalDigest == CharacterCreationFinalizationDigest.Compute(original)
                && upstreamDigest == CharacterCreationFinalizationDigest.Compute(upstream)
                && Same(input, expected) && Same(price, actual);

        private static object SelectedUpstream(CharacterCreationSelectedCompletionEvaluation result)
            => new { result.Skills.Attributes, result.Qualities, result.Magic };

        private static SelectedCompletionResourcesPricingInput FullInput(CharacterCreationCompletionReviewInputs inputs,
            CharacterCreationCompletionReview review, int available, CharacterCreationCompleteUpstreamCostsExecution costs)
        {
            var original = inputs.Lifestyles.Gear.Resources;
            return new(original.Authority, original.PriorityOption, available, review.Request.Selections.TotalKarmaInvestment,
                original.PurchaseAuthorityExact, costs.FullPurchaseCost, review.UpstreamBudget!.Blockers);
        }

        private static SelectedCompletionResourcesPricingInput SelectedInput(CharacterCreationSelectedCompletionWitness witness,
            CharacterCreationCompletionReviewRequest request, int available, CharacterCreationCompleteUpstreamCostsExecution costs)
            => new(witness.Resources.Authority, witness.Resources.PriorityOption, available, request.Selections.TotalKarmaInvestment,
                witness.Resources.PurchaseAuthorityExact, costs.FullPurchaseCost, []);

        private static bool CanExecute(CharacterCreationCompleteAssessment? authority)
            => authority is { Schema: CharacterCreationCompleteAssessment.SchemaV4, Phase: CharacterCreationCompletePhase.Authority };

        private static bool Same<T>(T left, T right)
            => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
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
            || !TryGetRetainedCompletionLoadoutCost(inputs, out decimal loadoutCost)
            || !TryGetRetainedCompletionLifestyleCost(inputs, out decimal lifestyleCost)
            || inputs.KnownPurchaseCost < lifestyleCost + loadoutCost
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
        var option = FindAllocationOption(authority, priority, int.MaxValue, pending.SelectedOptionId);
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

    internal static bool TryGetRetainedCompletionLoadoutCost(CharacterCreationResourcesCompletionInputs inputs, out decimal cost)
    {
        cost = inputs.LoadoutDraft?.PurchaseCost ?? 0m;
        return CharacterCreationLoadoutReceiptLedgerIntegrity.IsValidLedger(inputs.Binding.WorkspaceId,
            inputs.Binding.ContentRevision, inputs.LoadoutDraft, inputs.LoadoutReceipts)
            && CharacterCreationLoadoutReceiptLedgerIntegrity.HasFunding(inputs.LoadoutDraft, inputs.PendingDraft, inputs.Receipts)
            && (inputs.LoadoutDraft is null || inputs.LoadoutDraft.RawCharacterXmlDigest == inputs.Binding.RawCharacterXmlDigest
                && inputs.LoadoutDraft.Authority.SettingsProfileId == inputs.Authority.SettingsProfileId
                && inputs.LoadoutDraft.Authority.ProfileDigest == inputs.Authority.ProfileDigest);
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
