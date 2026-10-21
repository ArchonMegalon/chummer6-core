using System.Text.Json;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationSkillsService
{
    /// <summary>
    /// Evaluate explicit hypothetical choices without saving attributes, replacing
    /// historical skill drafts, or creating receipts. Requires valid original
    /// attributes and skills snapshots from the same saved workspace revision.
    /// Planning before the first saved Attributes draft needs separate authority
    /// preparation; this entry point never invents that missing baseline.
    /// </summary>
    public static CharacterCreationFoundationResult<CharacterCreationAttributeSkillsPreview>
        PreviewWithProposedAttributes(
            CharacterCreationAttributesState attributesState,
            CharacterCreationSkillsState skillsState,
            IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
            IReadOnlyList<CharacterCreationSkillAllocation> skillAllocations,
            IReadOnlyList<CharacterCreationSkillGroupAllocation> groupAllocations,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skillAllocations);
        ArgumentNullException.ThrowIfNull(groupAllocations);
        var prepared = PrepareCompletionPreview(attributesState, skillsState, attributeAllocations, cancellationToken);
        return prepared.Value is { } session
            ? session.Preview(skillAllocations, groupAllocations, cancellationToken)
            : new(prepared.Outcome, null, prepared.Blockers);
    }

    /// <summary>
    /// Plan from admitted original inputs even before Attributes is saved.
    /// This is a detached historical projection, not permission to apply it.
    /// </summary>
    public static CharacterCreationFoundationResult<CharacterCreationAttributeSkillsPreview>
        PreviewWithProposedAttributes(
            CharacterCreationSkillsCompletionInputs inputs,
            IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
            IReadOnlyList<CharacterCreationSkillAllocation> skillAllocations,
            IReadOnlyList<CharacterCreationSkillGroupAllocation> groupAllocations,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skillAllocations);
        ArgumentNullException.ThrowIfNull(groupAllocations);
        var prepared = PrepareCompletionPreview(inputs, attributeAllocations, cancellationToken);
        return prepared.Value is { } session
            ? session.Preview(skillAllocations, groupAllocations, cancellationToken)
            : new(prepared.Outcome, null, prepared.Blockers);
    }

    internal static CharacterCreationFoundationResult<CompletionPreviewSession> PrepareCompletionPreview(
        CharacterCreationSkillsCompletionInputs inputs,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(attributeAllocations);
        cancellationToken.ThrowIfCancellationRequested();
        inputs = JsonSerializer.Deserialize<CharacterCreationSkillsCompletionInputs>(
            JsonSerializer.SerializeToUtf8Bytes(inputs))!;
        attributeAllocations = attributeAllocations.ToArray();
        if (!IsValidCompletionInputs(inputs))
            return Blocked<CompletionPreviewSession>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationSkillsBlockers.AuthorityUnavailable);
        var attributesSession = CharacterCreationAttributesService.CreateDraftSession(inputs.Attributes);
        if (attributesSession is null)
            return Blocked<CompletionPreviewSession>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationAttributesBlockers.AuthorityUnavailable);
        cancellationToken.ThrowIfCancellationRequested();
        var attributesResult = attributesSession.Preview(attributeAllocations);
        if (attributesResult.Value is not { } attributes
            || attributes.Blockers.Any(blocker => blocker != CharacterCreationAttributesBlockers.DraftDuplicate))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, attributesResult.Blockers);
        return new(CharacterCreationFoundationOutcomes.Success,
            new CompletionPreviewSession(inputs.Attributes.SnapshotDigest, inputs.SnapshotDigest,
                inputs.Binding, inputs.Authority, inputs.PrerequisiteDraft,
                inputs.SelectedActiveSkillPoints, inputs.SelectedSkillGroupPoints,
                inputs.MovementCapability, inputs.KnowledgePointContributions, attributes), []);
    }

    private static bool IsValidCompletionInputs(CharacterCreationSkillsCompletionInputs inputs)
    {
        if (inputs.Schema != CharacterCreationSkillsCompletionSchemas.InputsV1
            || inputs.Binding is null || inputs.Attributes?.Binding is null
            || inputs.PrerequisiteDraft?.Assignments is null
            || inputs.Allocations is null || inputs.GroupAllocations is null
            || inputs.KnowledgePointContributions is null
            || !CharacterCreationSkillsDigest.EqualsFixedTime(inputs.SnapshotDigest,
                CharacterCreationSkillsDigest.Compute(inputs with { SnapshotDigest = string.Empty }))
            || !CharacterCreationSkillsDraftIntegrity.IsValidAuthority(inputs.Authority)
            || !CharacterCreationSkillsAccessRules.MatchesPrerequisite(inputs.Authority, inputs.PrerequisiteDraft)
            || !MatchesCompletionAttributes(inputs.Binding, inputs.PrerequisiteDraft, inputs.Attributes)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(inputs.Binding.SkillsAuthorityDigest, inputs.Authority.AuthorityDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(inputs.Binding.RuntimeDigest, inputs.Authority.RuntimeDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(inputs.Binding.ContributionInputsDigest,
                CharacterCreationSkillsDigest.Compute(inputs.KnowledgePointContributions))
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                inputs.KnowledgePointContributions, inputs.Authority.KnowledgePointContributions)
            || inputs.KnowledgePointContributions.Any(contribution =>
                !CharacterCreationSkillsDigest.EqualsFixedTime(contribution.SourceCharacterXmlDigest, inputs.Binding.RawCharacterXmlDigest))
            || inputs.MovementCapability != ResolveMovementCapability(inputs.PrerequisiteDraft.HeritageSelection?.Movement)
            || inputs.PrerequisiteDraft.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
            || inputs.PrerequisiteDraft.PriorityTable != "Standard")
            return false;

        var assignments = inputs.PrerequisiteDraft.Assignments.Where(item =>
            item.CategoryId == CharacterCreationPriorityCategoryIds.Skills).Take(2).ToArray();
        if (assignments.Length != 1
            || !CharacterCreationStandardPrioritySkillsRules.TryGetBudget(assignments[0].Rank, out int active, out int groups)
            || active != inputs.SelectedActiveSkillPoints || groups != inputs.SelectedSkillGroupPoints)
            return false;

        if (inputs.CompositeBaseline is { } composite)
            return inputs.SavedSkillsState is null
                && CharacterCreationFinalizationDigest.IsCanonical(composite.BaselineDigest)
                && composite.ContentRevision > 0 && composite.ContentRevision <= inputs.Binding.ContentRevision
                && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    composite, inputs.Attributes.CompositeBaseline)
                && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    composite.Selections.SkillAllocations, inputs.Allocations)
                && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    composite.Selections.GroupAllocations, inputs.GroupAllocations)
                && (composite.Selections.SkillKarmaPurchases is null || inputs.KarmaPurchasePolicy is not null);

        if (inputs.Attributes.CompositeBaseline is not null) return false;
        if (inputs.SavedSkillsState is { } saved)
            return saved.PendingDraft is { } draft && CreateDraftSession(saved) is not null
                && saved.Binding == inputs.Binding
                && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(saved.Authority, inputs.Authority)
                && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(saved.PrerequisiteDraft, inputs.PrerequisiteDraft)
                && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(saved.AttributesDraft, inputs.Attributes.PendingDraft)
                && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(draft.Allocations, inputs.Allocations)
                && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(draft.GroupAllocations, inputs.GroupAllocations);

        if (!CharacterCreationTalentSkillGrants.TryResolve(inputs.PrerequisiteDraft, inputs.Authority, out var grants))
            return false;
        var initialSkills = grants.InitialSkills(inputs.Authority).Select(skill => new CharacterCreationSkillAllocation(
            skill.SourceSkillId, skill.Kind, skill.Rating, skill.SpecializationOptionId, skill.IsNativeLanguage)).ToArray();
        var initialGroups = grants.InitialGroups(inputs.Authority).Select(group =>
            new CharacterCreationSkillGroupAllocation(group.GroupId, group.Rating)).ToArray();
        return CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(initialSkills, inputs.Allocations)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(initialGroups, inputs.GroupAllocations);
    }

    // The bounded planner validates/clones the original source snapshots once,
    // not once per tentative point. This session has no workspace/write path.
    internal static CharacterCreationFoundationResult<CompletionPreviewSession> PrepareCompletionPreview(
        CharacterCreationAttributesState attributesState,
        CharacterCreationSkillsState skillsState,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attributesState);
        ArgumentNullException.ThrowIfNull(skillsState);
        ArgumentNullException.ThrowIfNull(attributeAllocations);
        cancellationToken.ThrowIfCancellationRequested();

        // IReadOnlyList may wrap caller-owned arrays. All validation and evaluation
        // below use the same private copies, including authority/source anchors.
        attributesState = JsonSerializer.Deserialize<CharacterCreationAttributesState>(
            JsonSerializer.SerializeToUtf8Bytes(attributesState))!;
        skillsState = JsonSerializer.Deserialize<CharacterCreationSkillsState>(
            JsonSerializer.SerializeToUtf8Bytes(skillsState))!;
        attributeAllocations = attributeAllocations.ToArray();

        var attributesSession = CharacterCreationAttributesService.CreateDraftSession(attributesState);
        if (attributesSession is null)
            return Blocked<CompletionPreviewSession>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationAttributesBlockers.AuthorityUnavailable);
        if (CreateDraftSession(skillsState) is null)
            return Blocked<CompletionPreviewSession>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationSkillsBlockers.AuthorityUnavailable);

        CharacterCreationAttributesBinding attributesBinding = attributesState.Binding;
        CharacterCreationSkillsBinding skillsBinding = skillsState.Binding;
        if (attributesBinding.WorkspaceId != skillsBinding.WorkspaceId
            || attributesBinding.ContentRevision != skillsBinding.ContentRevision
            || attributesBinding.SavedRevision != skillsBinding.SavedRevision
            || !CharacterCreationSkillsDigest.EqualsFixedTime(
                attributesBinding.RawCharacterXmlDigest, skillsBinding.RawCharacterXmlDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(
                attributesBinding.AuxiliaryStateDigest, skillsBinding.AuxiliaryStateDigest)
            || attributesBinding.PrerequisiteDraftRevision != skillsBinding.PrerequisiteDraftRevision
            || !CharacterCreationSkillsDigest.EqualsFixedTime(
                attributesBinding.PrerequisiteDraftDigest, skillsBinding.PrerequisiteDraftDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(
                attributesBinding.PrerequisiteAuthorityDigest, skillsBinding.PrerequisiteAuthorityDigest)
            || attributesState.PendingDraft is null
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                attributesState.PendingDraft, skillsState.AttributesDraft)
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                attributesState.PrerequisiteDraft, skillsState.PrerequisiteDraft))
            return Blocked<CompletionPreviewSession>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationAttributeSkillsPreviewSchemas.SnapshotConflict);

        cancellationToken.ThrowIfCancellationRequested();
        var attributesResult = attributesSession.Preview(attributeAllocations);
        if (attributesResult.Value is not { } attributes
            || attributes.Blockers.Any(blocker => blocker != CharacterCreationAttributesBlockers.DraftDuplicate))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, attributesResult.Blockers);

        return new(CharacterCreationFoundationOutcomes.Success,
            new CompletionPreviewSession(attributesState.SnapshotDigest, skillsState.SnapshotDigest,
                skillsState.Binding, skillsState.Authority, skillsState.PrerequisiteDraft!,
                skillsState.SelectedActiveSkillPoints, skillsState.SelectedSkillGroupPoints,
                skillsState.MovementCapability, skillsState.KnowledgePointContributions, attributes), []);
    }

    internal sealed class CompletionPreviewSession(
        string attributesSnapshotDigest,
        string skillsSnapshotDigest,
        CharacterCreationSkillsBinding binding,
        CharacterCreationSkillsAuthority authority,
        CharacterCreationPrerequisiteDraft prerequisite,
        int selectedActiveSkillPoints,
        int selectedSkillGroupPoints,
        CharacterCreationMovementCapability movement,
        IReadOnlyList<CharacterCreationKnowledgePointContribution> contributions,
        CharacterCreationAttributesPreview attributes)
    {
        internal CharacterCreationFoundationResult<CharacterCreationAttributeSkillsPreview> PreviewPurchases(
            CharacterCreationAttributeSkillsPreview baseline,
            IReadOnlyList<CharacterCreationSkillKarmaPurchase> purchases,
            CharacterCreationKarmaSkillsPolicy policy, IReadOnlyList<string>? compensationOrder,
            CancellationToken cancellationToken, IReadOnlyList<CharacterCreationKnowledgeCategoryEffect>? knowledgeEffects = null,
            IReadOnlyList<CharacterCreationSkillKarmaEffect>? karmaEffects = null, bool captureExecution = false,
            CompleteSkillTrace? completeTrace = null)
        {
            bool projectGroups = policy.AllowSkillRegrouping.HasValue
                && (policy.CompensateSkillGroupKarmaDifference || baseline.Skills.Any(row => row.GroupRating > 0));
            if (purchases.Count == 0 && !projectGroups && karmaEffects is not { Count: > 0 })
            {
                completeTrace?.FinishPurchases(policy, purchases, baseline);
                completeTrace?.FinishCompensationPolicy1015(policy, purchases, baseline, projectGroups);
                return new(CharacterCreationFoundationOutcomes.Success, baseline, []);
            }
            var blockers = new List<string>();
            var skillCosts972 = completeTrace is null ? null : new Dictionary<string, int>(StringComparer.Ordinal);
            var rows = baseline.Skills.ToDictionary(row => (row.Kind, row.SourceSkillId));
            var final = rows.ToDictionary(pair => pair.Key, pair => new CharacterCreationSkillAllocation(
                pair.Value.SourceSkillId, pair.Value.Kind, pair.Value.Rating,
                pair.Value.SpecializationOptionId, pair.Value.IsNativeLanguage));
            var costs = new Dictionary<(string Kind, string Id), (int Levels, int Cost, bool Specialization)>();
            var knowledgePrices = new Dictionary<(string Kind, string Id), CharacterCreationKnowledgeCategoryPrice>();
            var modifierPrices = new Dictionary<(string Kind, string Id), CharacterCreationSkillKarmaPrice>();
            var executionPrices = captureExecution
                ? new Dictionary<(string Kind, string Id), CharacterCreationSkillKarmaPrice>() : null;
            var compensationAnchors = new Dictionary<(string Kind, string Id), IReadOnlyList<string>>();
            try
            {
                foreach (var purchase in purchases)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var key = (purchase.Kind, purchase.SourceSkillId);
                    var source = (purchase.Kind == CharacterCreationSkillKinds.Active ? authority.ActiveSkills : authority.KnowledgeSkills)
                        .SingleOrDefault(row => row.SourceSkillId == purchase.SourceSkillId);
                    if (source is null || !CharacterCreationSkillsDigest.EqualsFixedTime(source.SourceNodeDigest, purchase.SourceNodeDigest))
                    { blockers.Add(CharacterCreationSkillKarmaPurchaseBlockers.SourceConflict); continue; }
                    rows.TryGetValue(key, out var original);
                    int lower = original?.Rating ?? 0;
                    if (original?.IsNativeLanguage == true || purchase.Rating < lower
                        || purchase.Rating == lower && purchase.SpecializationOptionId is null
                        || purchase.SpecializationOptionId is not null && original?.SpecializationOptionId is not null)
                    { blockers.Add(CharacterCreationSkillKarmaPurchaseBlockers.PurchaseInvalid); continue; }
                    // Historical inputs did not retain the group persistence policy.
                    // Do not reinterpret an old total as a new personal rank count.
                    if (purchase.Kind == CharacterCreationSkillKinds.Active && source.SkillGroup is not null
                        && !projectGroups
                        && baseline.SkillGroups.Any(group => group.Rating > 0
                            && group.MemberSkillSourceIds.Contains(source.SourceSkillId, StringComparer.Ordinal)))
                    { blockers.Add(policy.CompensateSkillGroupKarmaDifference
                        ? CharacterCreationSkillKarmaPurchaseBlockers.GroupCompensationUnsupported
                        : CharacterCreationSkillsBlockers.GroupBroken); continue; }
                    int cost;
                    bool priced = purchase.Kind == CharacterCreationSkillKinds.Active
                        ? CharacterCreationSkillCostRules.TryActive(lower, purchase.Rating,
                            policy.KarmaNewActiveSkill, policy.KarmaImproveActiveSkill, out cost)
                        : CharacterCreationSkillCostRules.TryKnowledge(lower, purchase.Rating,
                            policy.KarmaNewKnowledgeSkill, policy.KarmaImproveKnowledgeSkill, out cost);
                    if (!priced) { blockers.Add(CharacterCreationSkillKarmaPurchaseBlockers.PurchaseInvalid); continue; }
                    ObserveSkillInterval972(skillCosts972, purchase.Kind, lower, purchase.Rating, policy);
                    bool specialization = purchase.SpecializationOptionId is not null;
                    if (specialization) cost = checked(cost + (purchase.Kind == CharacterCreationSkillKinds.Active
                        ? policy.KarmaSpecialization : policy.KarmaKnowledgeSpecialization));
                    if (specialization) ObserveSkillSpecialization972(skillCosts972, purchase.Kind, policy);
                    if (purchase.Kind == CharacterCreationSkillKinds.Knowledge && knowledgeEffects is { Count: > 0 })
                    {
                        int basePoints = lower - (original?.GrantedRating ?? 0) - (original?.GroupRating ?? 0);
                        if (!CharacterCreationKnowledgeCategoryRules.TryPrice(source.Category, lower, purchase.Rating,
                                basePoints, original?.SpecializationOptionId is null ? 0 : 1, specialization ? 1 : 0,
                                policy.KarmaNewKnowledgeSkill, policy.KarmaImproveKnowledgeSkill,
                                policy.KarmaKnowledgeSpecialization, knowledgeEffects, out var knowledgePrice))
                        { blockers.Add(CharacterCreationSkillKarmaPurchaseBlockers.ModifiersUnsupported); continue; }
                        cost = knowledgePrice!.KarmaCost;
                        knowledgePrices.Add(key, knowledgePrice);
                    }
                    costs.Add(key, (checked(purchase.Rating - lower), cost, specialization));
                    final[key] = new(purchase.SourceSkillId, purchase.Kind, purchase.Rating,
                        purchase.SpecializationOptionId ?? original?.SpecializationOptionId, false);
                }
                if (blockers.Count != 0) return new(CharacterCreationFoundationOutcomes.Blocked, null, Normalize(blockers));
                // Reuse all current source/talent/movement/grant/cap/group/spec rules.
                // Only this temporary legality evaluation has an unbounded point pool;
                // the returned budgets and PointCost remain the original paid points.
                var evaluated = EvaluateAllocations(authority, prerequisite, int.MaxValue, selectedSkillGroupPoints,
                    attributes.Attributes,
                    movement, final.Values.ToArray(), baseline.SkillGroups.Select(row =>
                        new CharacterCreationSkillGroupAllocation(row.GroupId, row.Rating)).ToArray(), blockers,
                    projectGroupMembers: projectGroups,
                    personalKarmaLevels: costs.ToDictionary(pair => pair.Key, pair => pair.Value.Levels),
                    karmaSpecializations: costs.Where(pair => pair.Value.Specialization).Select(pair => pair.Key).ToHashSet(),
                    allowPointBuySpecializationsOnKarmaSkills: policy.AllowPointBuySpecializationsOnKarmaSkills,
                    knowledgeEffects: knowledgeEffects, completeTrace: completeTrace);
                if (blockers.Count != 0) return new(CharacterCreationFoundationOutcomes.Blocked, null, Normalize(blockers));
                // All final ratings must be known before pricing compensation. The
                // native Karma implementation owns the formula and its per-skill
                // nonnegative clamp; purchase enumeration must not change the price.
                // Price every active interval only after final membership/ratings are known.
                // Compensation is part of the raw interval, before all modifiers.
                {
                    foreach (var purchase in purchases.Where(row => row.Kind == CharacterCreationSkillKinds.Active))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var key = (purchase.Kind, purchase.SourceSkillId);
                        var group = authority.SkillGroups.SingleOrDefault(row =>
                            row.MemberSkillSourceIds.Contains(purchase.SourceSkillId, StringComparer.Ordinal));
                        var enabled = (group?.MemberSkillSourceIds ?? []).Where(id =>
                            CharacterCreationSkillsAccessRules.IsSkillAvailable(authority, id)
                            && IsMovementAvailable(authority.ActiveSkills.Single(row => row.SourceSkillId == id), movement))
                            .ToArray();
                        rows.TryGetValue(key, out var original);
                        int lower = original?.Rating ?? 0;
                        int? otherMinimum = enabled.Where(id => id != purchase.SourceSkillId)
                            .Select(id => (int?)(final.GetValueOrDefault((CharacterCreationSkillKinds.Active, id))?.Rating ?? 0)).Min();
                        // Ordinary historical shapes never needed an order witness.
                        // A potentially compensating interval may not guess its payer.
                        bool compensate = policy.CompensateSkillGroupKarmaDifference
                            && purchase.Rating > lower && otherMinimum is not null && otherMinimum > lower;
                        if (compensate && compensationOrder is null)
                            return Blocked<CharacterCreationAttributeSkillsPreview>(CharacterCreationFoundationOutcomes.Blocked,
                                CharacterCreationSkillKarmaPurchaseBlockers.GroupCompensationUnsupported);
                        compensate = compensate && compensationOrder!.FirstOrDefault(id => enabled.Contains(id, StringComparer.Ordinal)) == purchase.SourceSkillId;
                        var source = authority.ActiveSkills.Single(row => row.SourceSkillId == purchase.SourceSkillId);
                        var paid = costs[key];
                        if (!CharacterCreationKarmaSkillsRules.TryPriceActive(source, lower, purchase.Rating, policy,
                                compensate, otherMinimum, enabled.Length, paid.Specialization ? 1 : 0, karmaEffects ?? [], out var price,
                                captureExecution))
                            return Blocked<CharacterCreationAttributeSkillsPreview>(CharacterCreationFoundationOutcomes.Blocked,
                                CharacterCreationSkillKarmaPurchaseBlockers.PurchaseInvalid);
                        ObserveSkillCompensation972(skillCosts972, lower, purchase.Rating, compensate, otherMinimum, policy);
                        if (compensate) compensationAnchors[key] = group!.SourceAnchorIds.Concat(enabled.SelectMany(id =>
                            authority.ActiveSkills.Single(row => row.SourceSkillId == id).SourceAnchorIds)).ToArray();
                        costs[key] = (paid.Levels, price!.KarmaCost, paid.Specialization);
                        if (executionPrices is not null) executionPrices[key] = price;
                        if (price.Effects.Any(e => !e.ImprovementType.StartsWith("SkillGroup", StringComparison.Ordinal))) modifierPrices[key] = price;
                    }
                }
                // Include point-only knowledge rows: native zero-width additive
                // semantics are observable even when no rank was bought with Karma.
                foreach (var source in authority.KnowledgeSkills)
                {
                    var key = (source.Kind, source.SourceSkillId);
                    rows.TryGetValue(key, out var original);
                    if (original?.IsNativeLanguage == true || !final.TryGetValue(key, out var allocation)
                        || karmaEffects is not { Count: > 0 }) continue;
                    var target = CharacterCreationLegacySourceProjector.Target(source);
                    if (!karmaEffects.Any(e => e.Targets.Contains(target))) continue;
                    if (!costs.ContainsKey(key) && !karmaEffects.Any(e => e.Targets.Contains(target)
                        && !CharacterCreationKnowledgeCategoryRules.IsSupportedType(e.ImprovementType))) continue;
                    int lower = original?.Rating ?? 0, upper = allocation.Rating ?? 0;
                    costs.TryGetValue(key, out var paid);
                    if (!CharacterCreationSkillCostRules.TryKnowledge(lower, upper,
                            policy.KarmaNewKnowledgeSkill, policy.KarmaImproveKnowledgeSkill, out int raw)
                        || !CharacterCreationSkillKarmaModifierRules.TryPrice(target, lower, upper, raw, 0,
                            paid.Specialization ? 1 : 0, policy.KarmaKnowledgeSpecialization, karmaEffects, out var price))
                        return Blocked<CharacterCreationAttributeSkillsPreview>(CharacterCreationFoundationOutcomes.Blocked,
                            CharacterCreationSkillKarmaPurchaseBlockers.ModifiersUnsupported);
                    ObserveSkillInterval972(skillCosts972, source.Kind, lower, upper, policy);
                    if (paid.Specialization) ObserveSkillSpecialization972(skillCosts972, source.Kind, policy);
                    costs[key] = (paid.Levels, price!.KarmaCost, paid.Specialization);
                    if (price.Effects.Any(e => !CharacterCreationKnowledgeCategoryRules.IsSupportedType(e.ImprovementType)))
                        modifierPrices[key] = price;
                }
                var skills = evaluated.Skills.Select(row =>
                {
                    if (!costs.TryGetValue((row.Kind, row.SourceSkillId), out var paid)) return row;
                    rows.TryGetValue((row.Kind, row.SourceSkillId), out var original);
                    // paid.Cost is the complete interval-plus-specialization quote,
                    // including any charge already present on the point-only baseline.
                    return row with { PointCost = original?.PointCost ?? 0, KarmaLevels = paid.Levels,
                        KarmaCost = paid.Cost, KarmaSpecialization = paid.Specialization,
                        KnowledgeCategoryPrice = knowledgePrices.GetValueOrDefault((row.Kind, row.SourceSkillId)) ?? row.KnowledgeCategoryPrice,
                        SkillKarmaPrice = executionPrices?.GetValueOrDefault((row.Kind, row.SourceSkillId))
                            ?? modifierPrices.GetValueOrDefault((row.Kind, row.SourceSkillId)),

                        SourceAnchorIds = row.SourceAnchorIds.Concat(policy.SourceAnchorIds)
                            .Concat(compensationAnchors.GetValueOrDefault((row.Kind, row.SourceSkillId)) ?? [])
                            .Concat(knowledgePrices.GetValueOrDefault((row.Kind, row.SourceSkillId))?.Effects.SelectMany(effect => effect.SourceAnchorIds) ?? [])
                            .Concat(modifierPrices.GetValueOrDefault((row.Kind, row.SourceSkillId))?.Effects.SelectMany(effect => effect.SourceAnchorIds) ?? [])
                            .Concat(paid.Specialization ? new[] { (row.Kind == CharacterCreationSkillKinds.Active
                                ? authority.ActiveSkills : authority.KnowledgeSkills).Single(source => source.SourceSkillId == row.SourceSkillId)
                                .Specializations.Single(spec => spec.OptionId == row.SpecializationOptionId).SourceAnchorId } : [])
                            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
                }).ToArray();
                _ = skills.Sum(row => row.KarmaCost); // Reject overflow before exposing an exact quote.
                var groups = projectGroups
                    ? baseline.SkillGroups.Select(group =>
                    {
                        var members = group.MemberSkillSourceIds.Where(id =>
                            CharacterCreationSkillsAccessRules.IsSkillAvailable(authority, id)
                            && IsMovementAvailable(authority.ActiveSkills.Single(row => row.SourceSkillId == id), movement))
                            .Select(id => skills.Single(row => row.Kind == CharacterCreationSkillKinds.Active && row.SourceSkillId == id)).ToArray();
                        // SkillGroup.UpdateIsBroken does not latch during creation.
                        // At Finish the career initial state is the final enabled set,
                        // not a transient purchase enumeration or intermediate review.
                        bool broken = members.Length > 1 && (members.Select(row => row.EffectiveRating).Distinct().Count() > 1
                            || policy.SpecializationsBreakSkillGroups && members.Any(row => row.SpecializationOptionId is not null));
                        return group with { IsBroken = broken, AllowSkillRegrouping = policy.AllowSkillRegrouping,
                            SourceAnchorIds = group.SourceAnchorIds.Concat(policy.SourceAnchorIds)
                                .Concat(members.SelectMany(row => row.SourceAnchorIds))
                                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
                    }).ToArray() : baseline.SkillGroups;
                var result = baseline with { Skills = skills, SkillGroups = groups, ProposalDigest = string.Empty };
                result = result with { ProposalDigest = CharacterCreationSkillsDigest.Compute(result) };
                cancellationToken.ThrowIfCancellationRequested();
                completeTrace?.FinishPurchases(policy, purchases, result);
                completeTrace?.FinishSkillCosts972(policy, purchases, result, skillCosts972!);
                completeTrace?.FinishCompensationPolicy1015(policy, purchases, result, projectGroups);
                return new(CharacterCreationFoundationOutcomes.Success, result, []);
            }
            catch (OverflowException)
            {
                return Blocked<CharacterCreationAttributeSkillsPreview>(CharacterCreationFoundationOutcomes.Blocked,
                    CharacterCreationSkillKarmaPurchaseBlockers.PurchaseInvalid);
            }
        }

        internal CharacterCreationFoundationResult<CharacterCreationAttributeSkillsPreview> Preview(
            IReadOnlyList<CharacterCreationSkillAllocation> skillAllocations,
            IReadOnlyList<CharacterCreationSkillGroupAllocation> groupAllocations,
            CancellationToken cancellationToken, bool projectGroupMembers = false,
            IReadOnlyList<CharacterCreationKnowledgeCategoryEffect>? knowledgeEffects = null, CompleteSkillTrace? completeTrace = null)
        {
            ArgumentNullException.ThrowIfNull(skillAllocations);
            ArgumentNullException.ThrowIfNull(groupAllocations);
            cancellationToken.ThrowIfCancellationRequested();

            // Do not rebind historical Skills to a pretend saved attribute draft.
            // Core retains the source, talent, movement, grant and overflow rules.
            var blockers = new List<string>();
            var projected = EvaluateAllocations(
                authority,
                prerequisite,
                selectedActiveSkillPoints,
                selectedSkillGroupPoints,
                attributes.Attributes,
                movement,
                skillAllocations.ToArray(),
                groupAllocations.ToArray(),
                blockers, projectGroupMembers: projectGroupMembers, knowledgeEffects: knowledgeEffects, completeTrace: completeTrace);
            cancellationToken.ThrowIfCancellationRequested();
            string[] normalized = Normalize(blockers);
            var preview = new CharacterCreationAttributeSkillsPreview(
                CharacterCreationAttributeSkillsPreviewSchemas.PreviewV1,
                attributesSnapshotDigest,
                skillsSnapshotDigest,
                attributes,
                binding,
                projected.Skills,
                projected.Groups,
                contributions,
                projected.ActiveBudget,
                projected.GroupBudget,
                projected.KnowledgeBudget,
                projected.KnowledgeOverflow,
                normalized,
                ProposalDigest: string.Empty);
            preview = preview with { ProposalDigest = CharacterCreationSkillsDigest.Compute(preview) };
            // Source anchors/group members and the attribute projection may refer
            // to the private snapshot. A returned proposal must not mutate it.
            preview = JsonSerializer.Deserialize<CharacterCreationAttributeSkillsPreview>(
                JsonSerializer.SerializeToUtf8Bytes(preview))!;
            return new(normalized.Length == 0 ? CharacterCreationFoundationOutcomes.Success
                : CharacterCreationFoundationOutcomes.Blocked, preview, normalized);
        }
    }
}
