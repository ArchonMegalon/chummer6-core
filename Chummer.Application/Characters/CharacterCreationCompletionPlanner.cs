using System.Text.Json;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Pure proposal composition. Original observations are never rewritten
/// as intermediate drafts; the final review re-evaluates the complete proposal.</summary>
public static partial class CharacterCreationCompletionPlanner
{
    public static CharacterCreationFoundationResult<CharacterCreationCompletionPlan> Plan(
        CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionPlanRequest request,
        CancellationToken cancellationToken = default)
        => PlanCore(inputs, request, cancellationToken, null);

    /// <summary>Plan against an explicitly restored private source execution.
    /// The context is not serialized, inferred from hashes or owner/save authority.
    /// Every original and candidate review independently validates its binding.</summary>
    public static CharacterCreationFoundationResult<CharacterCreationCompletionPlan> PlanWithExecution(
        CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionPlanRequest request,
        CharacterCreationRuleSourceExecutionContext? ruleSources,
        CancellationToken cancellationToken = default, CharacterCreationCampaignExecutionContext? campaign = null)
        => PlanCore(inputs, request, cancellationToken, ruleSources, campaign);

    private static CharacterCreationFoundationResult<CharacterCreationCompletionPlan> PlanCore(
        CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionPlanRequest request,
        CancellationToken cancellationToken, CharacterCreationRuleSourceExecutionContext? ruleSources,
        CharacterCreationCampaignExecutionContext? campaign = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidRequest(request)) return Reject(CharacterCreationFoundationOutcomes.Invalid,
            CharacterCreationCompletionPlanBlockers.RequestInvalid);
        if (!CharacterCreationSkillCompletionPermissionRules.TrySnapshotAuthorityCatalogs(inputs.Skills?.Authority,
                out var authority, cancellationToken))
            return Reject(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationCompletionReviewSchemas.AuthorityUnavailable);
        inputs = inputs with { Skills = inputs.Skills! with { Authority = authority } };
        if (!CharacterCreationSkillCompletionPlanner.TrySnapshotRequest(authority, request.Skills, out var skillsRequest, cancellationToken))
            return Reject(CharacterCreationFoundationOutcomes.Invalid, CharacterCreationSkillCompletionBlockers.RequestInvalid);
        request = request with { Skills = skillsRequest };
        inputs = Detach(inputs);
        request = Detach(request);
        if (!TryOriginalSelections(inputs, out var original))
            return Reject(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationCompletionReviewSchemas.AuthorityUnavailable);

        // This reuses whole-graph source/observation admission even when an
        // unfinished original character understandably has readiness blockers.
        var originalRequest = ReviewRequest(request, original);
        var fixedBlockers = CompleteCandidateBlockers(inputs, originalRequest, cancellationToken, campaign, ruleSources);
        if (fixedBlockers.Count != 0)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, fixedBlockers);
        var admission = CharacterCreationCompletionReviewService.Review(inputs, originalRequest, cancellationToken, ruleSources, campaign);
        if (admission.Value is null) return new(admission.Outcome, null, admission.Blockers);
        return PlanAdmitted(inputs, request, original, cancellationToken, ruleSources, campaign);
    }

    private static CharacterCreationFoundationResult<CharacterCreationCompletionPlan> PlanAdmitted(
        CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionPlanRequest request,
        CharacterCreationCompletionSelections original,
        CancellationToken cancellationToken, CharacterCreationRuleSourceExecutionContext? ruleSources,
        CharacterCreationCampaignExecutionContext? campaign = null)
    {
        if (!MatchesOriginalRequests(inputs, request))
            return Reject(CharacterCreationFoundationOutcomes.Conflict, CharacterCreationCompletionPlanBlockers.SnapshotConflict);
        if (!Same(original.MagicSelections, request.Magic.ManualSelections))
            return Reject(CharacterCreationFoundationOutcomes.Invalid, CharacterCreationCompletionPlanBlockers.ManualMagicMismatch);

        var choices = original with
        {
            QualityOptionIds = request.QualityOptionIds,
            Contacts = request.Contacts,
            TotalKarmaInvestment = request.TotalKarmaInvestment,
            StartingLifestyleId = request.StartingLifestyleId,
            ChangeableAttributeIds = request.Attributes.PermittedAttributeOrder,
            ChangeableSkillTargets = request.Skills.PermittedTargetOrder,
            SkillKarmaPurchases = request.SkillKarmaPurchases ?? original.SkillKarmaPurchases,
            KarmaToReserve = request.SkillKarmaPurchases is null && original.SkillKarmaPurchases is null ? 0 : request.KarmaToReserve
        };
        var preservation = CharacterCreationCompletionPreservationRules.Check(inputs, choices, cancellationToken);
        if (preservation.Count != 0) return new(CharacterCreationFoundationOutcomes.Blocked, null, preservation);
        if (!request.PermitNegativeQualityAdditions && request.QualityOptionIds.Except(original.QualityOptionIds)
                .Any(id => inputs.Lifestyles.Qualities.Authority.Options.Any(option => option.OptionId == id
                    && option.Type == CharacterCreationQualityType.Negative)))
            return Reject(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationCompletionPlanBlockers.NegativeQualityPermissionRequired);

        // Opt-in automatic spending cannot override recorded player choices or
        // manufacture provenance for dense legacy/imported allocations. Apply
        // this to the Priority pass too, before it can change the same IDs.
        if (request.AllocateRemainingAttributeKarma
            && !HasAutomaticAttributePermissions(inputs.Skills.Attributes, request.Attributes.PermittedAttributeOrder))
            return Reject(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationAttributeCompletionBlockers.AutomaticPermissionUnavailable);

        var findings = new List<string>();
        var attributeResult = CharacterCreationAttributeCompletionPlanner.Plan(inputs.Skills.Attributes,
            request.Attributes, cancellationToken);
        if (Terminal(attributeResult.Outcome)) return new(attributeResult.Outcome, null, attributeResult.Blockers);
        findings.AddRange(attributeResult.Blockers);
        var attributes = attributeResult.Value;
        CharacterCreationSkillCompletionPlan? skills = null;
        CharacterCreationMagicCompletionPlan? magic = null;
        CharacterCreationPurchaseCompletionPlan? purchases = null;
        if (attributes is not null)
        {
            choices = choices with { AttributeAllocations = attributes.Allocations };
            var skillResult = CharacterCreationSkillCompletionPlanner.Plan(inputs.Skills, choices.AttributeAllocations,
                request.Skills, cancellationToken);
            if (Terminal(skillResult.Outcome)) return new(skillResult.Outcome, null, skillResult.Blockers);
            findings.AddRange(skillResult.Blockers);
            skills = skillResult.Value;
            if (skills is not null)
                choices = choices with { SkillAllocations = skills.Allocations, GroupAllocations = skills.GroupAllocations };

            // Price explicit skill purchases and contacts before Power Points;
            // reserve them plus the full investment and the shared final reserve.
            int? skillKarmaCost = 0;
            if (choices.SkillKarmaPurchases is not null)
            {
                var quote = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills,
                    choices.AttributeAllocations, choices.SkillAllocations, choices.GroupAllocations,
                    choices.SkillKarmaPurchases, cancellationToken);
                if (Terminal(quote.Outcome)) return new(quote.Outcome, null, quote.Blockers);
                findings.AddRange(quote.Blockers);
                skillKarmaCost = quote.Value?.Skills.Sum(row => row.KarmaCost);
                if (skills is not null && quote.Value is not null)
                {
                    skills = skills with { Preview = quote.Value, HasChanges = skills.HasChanges || choices.SkillKarmaPurchases.Count > 0,
                        PlanDigest = string.Empty };
                    skills = skills with { PlanDigest = CharacterCreationSkillsDigest.Compute(skills) };
                }
            }
            var contactResult = CharacterCreationContactsService.PreviewCompletion(inputs.Contacts,
                choices.AttributeAllocations, choices.QualityOptionIds, choices.Contacts, cancellationToken);
            if (contactResult.Outcome == CharacterCreationContactOutcomes.Conflict)
                return new(CharacterCreationFoundationOutcomes.Conflict, null, contactResult.Blockers);
            findings.AddRange(contactResult.Blockers);
            int? reserve = null;
            if (contactResult.Value is { Blockers.Count: 0, AdditionalKarmaCost: int contactCost } && contactCost >= 0
                && skillKarmaCost is >= 0)
            {
                try { reserve = checked(request.KarmaToReserve + contactCost + request.TotalKarmaInvestment + skillKarmaCost.Value); }
                catch (OverflowException) { /* unavailable is not a zero reserve */ }
            }
            if (reserve is null) findings.Add(CharacterCreationCompletionPlanBlockers.KarmaReserveUnavailable);
            else
            {
                var magicResult = CharacterCreationMagicCompletionPlanner.Plan(inputs.Magic, inputs.Lifestyles.Qualities,
                    choices.AttributeAllocations, choices.QualityOptionIds,
                    request.Magic with { KarmaToReserve = Math.Max(request.Magic.KarmaToReserve, reserve.Value) }, cancellationToken);
                if (Terminal(magicResult.Outcome)) return new(magicResult.Outcome, null, magicResult.Blockers);
                findings.AddRange(magicResult.Blockers);
                magic = magicResult.Value;
                if (magic is not null)
                {
                    choices = choices with { MagicSelections = magic.Selections };
                    var purchaseRequest = new CharacterCreationPurchaseCompletionRequest(inputs.Lifestyles.SnapshotDigest,
                        inputs.Magic.SnapshotDigest, choices.AttributeAllocations, choices.MagicSelections,
                        choices.QualityOptionIds, choices.TotalKarmaInvestment, request.Purchases,
                        request.StartingLifestyleId, request.NuyenToReserve, request.MaximumAdditionalPurchaseSpend,
                        request.MaximumPurchaseEvaluations) { LoadoutSnapshotDigest = inputs.Loadout?.SnapshotDigest };
                    var purchaseResult = CharacterCreationPurchaseCompletionPlanner.Plan(inputs.Lifestyles, inputs.Magic,
                        inputs.Loadout, purchaseRequest, true, null, cancellationToken, original);
                    if (Terminal(purchaseResult.Outcome)) return new(purchaseResult.Outcome, null, purchaseResult.Blockers);
                    findings.AddRange(purchaseResult.Blockers);
                    purchases = purchaseResult.Value;
                    if (purchases is not null)
                        choices = choices with { Gear = purchases.Gear, Lifestyles = purchases.Lifestyles,
                            StartingLifestyleId = purchases.StartingLifestyleId, Loadout = purchases.Loadout,
                            AmmunitionAssignments = purchases.AmmunitionAssignments };
                }
            }
        }

        var final = CharacterCreationCompletionReviewService.Review(inputs,
            ReviewRequest(request, choices), cancellationToken, ruleSources, campaign);
        if (final.Value is not { } review) return new(final.Outcome, null, final.Blockers);
        if (request.AllocateRemainingAttributeKarma && attributes is not null
            && skills is not null && magic is not null && purchases is not null)
        {
            var karmaResult = AllocateAttributeKarma(inputs, request, attributes, choices, review, cancellationToken, ruleSources, campaign);
            if (karmaResult.Value is not { } allocation) return new(karmaResult.Outcome, null, karmaResult.Blockers);
            attributes = allocation.Attributes;
            choices = allocation.Selections;
            review = allocation.Review;
            // A Karma level can satisfy a Priority floor that was previously
            // capped. Keep other domain findings and all unmet goals visible.
            findings.RemoveAll(value => value == CharacterCreationAttributeCompletionBlockers.TargetsUnfulfilled);
            findings.AddRange(attributes.Blockers);
            if (allocation.Changed)
            {
                var previousSkillBlockers = skills.Blockers;
                RefreshDependentPlans(choices, review, ref skills, ref magic, ref purchases);
                findings.RemoveAll(value => previousSkillBlockers.Contains(value, StringComparer.Ordinal));
                findings.AddRange(skills!.Blockers);
            }
        }
        findings.AddRange(review.Blockers);
        if (request.KarmaToReserve > 0 && (review.Balances.RemainingCreationKarma is not int karma || karma < request.KarmaToReserve))
            findings.Add(CharacterCreationCompletionPlanBlockers.KarmaReserveRequired);
        if (request.NuyenToReserve > 0 && (review.Balances.RemainingNuyen is not decimal nuyen || nuyen < request.NuyenToReserve))
            findings.Add(CharacterCreationCompletionPlanBlockers.NuyenReserveRequired);
        string[] blockers = findings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var plan = new CharacterCreationCompletionPlan(request, choices, attributes, skills, magic, purchases,
            review, blockers, string.Empty);
        plan = plan with { PlanDigest = CharacterCreationCompletionPlanRules.ComputePlanDigest(plan) };
        cancellationToken.ThrowIfCancellationRequested();
        return new(blockers.Length == 0 ? CharacterCreationFoundationOutcomes.Success : CharacterCreationFoundationOutcomes.Blocked,
            Detach(plan), blockers);
    }

    private static CharacterCreationCompletionReviewRequest ReviewRequest(
        CharacterCreationCompletionPlanRequest request, CharacterCreationCompletionSelections selections)
        => new(request.InputsSnapshotDigest, selections)
        {
            CampaignAuthority = request.CampaignAuthority,
            PolicyRequirement = request.PolicyRequirement,
            EffectivePolicyDigest = request.EffectivePolicyDigest,
            PolicyConsumptionSchema = request.PolicyConsumptionSchema,
            CompleteAssessmentSchema = request.CompleteAssessmentSchema,
            MaximumAttributesDefaultExecutionSchema = request.MaximumAttributesDefaultExecutionSchema,
            AdditionalConsumerExecutionSchema = request.AdditionalConsumerExecutionSchema,
            ProfileSettingsExecutionSchema = request.ProfileSettingsExecutionSchema,
            OriginalCharacterExecutionSchema = request.OriginalCharacterExecutionSchema,
            RuleSourceExecutionSchema = request.RuleSourceExecutionSchema,
            RuleSourceCaptureDigest = request.RuleSourceCaptureDigest,
            SourcebookSelection = request.SourcebookSelection
        };

    // A non-null diagnostic review is not candidate readiness. Recompute fixed
    // Complete authority from the detached admitted inputs before any search.
    // Historical unpinned Complete still encounters its original review refusal.
    private static IReadOnlyList<string> CompleteCandidateBlockers(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request,
        CancellationToken cancellationToken, CharacterCreationCampaignExecutionContext? campaign = null,
        CharacterCreationRuleSourceExecutionContext? ruleSources = null)
    {
        if (request.PolicyRequirement != CharacterCreationPolicyRequirement.Complete
            || request.CompleteAssessmentSchema is null) return [];
        // The new sourcebook opt-in needs its private capture even at fixed admission.
        // Historical requests retain their previous assessment inputs.
        var authority = CharacterCreationCompleteAssessmentService.AssessAuthority(inputs, request, cancellationToken, campaign,
            request.SourcebookSelection is null ? null : ruleSources);
        if (authority.Outcome == CharacterCreationFoundationOutcomes.Success
            && authority.Value?.MayRequestCompleteCandidate == true) return [];
        return authority.Blockers.Count != 0 ? authority.Blockers
            : [CharacterCreationCompleteAssessment.FixedAuthorityUnavailable];
    }

    private static bool MatchesOriginalRequests(CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionPlanRequest request)
        => request.Attributes.Binding == inputs.Skills.Attributes.Binding
            && Digest(request.Attributes.SnapshotDigest, inputs.Skills.Attributes.SnapshotDigest)
            && request.Skills.Binding == inputs.Skills.Binding
            && Digest(request.Skills.AttributesSnapshotDigest, inputs.Skills.Attributes.SnapshotDigest)
            && Digest(request.Skills.SkillsSnapshotDigest, inputs.Skills.SnapshotDigest)
            && Digest(request.Magic.MagicSnapshotDigest, inputs.Magic.SnapshotDigest)
            && Digest(request.Magic.QualitiesSnapshotDigest, inputs.Lifestyles.Qualities.SnapshotDigest);

    private static bool TryOriginalSelections(CharacterCreationCompletionReviewInputs inputs,
        out CharacterCreationCompletionSelections selections)
    {
        selections = null!;
        if (inputs.Skills is not { Attributes: { Attributes: not null }, Allocations: not null, GroupAllocations: not null }
            || inputs.Skills.Attributes.Attributes.Any(row => row is null)
            || inputs.Magic?.Selections is null
            || inputs.Lifestyles is not { Qualities: { SelectedOptionIds: not null }, Lifestyles: not null,
                Gear: { State: not null, Resources: not null } }
            || inputs.Lifestyles.Gear.State.PendingDraft is { } gear && (gear.Lines is null || gear.Lines.Any(row => row is null))
            || inputs.Contacts?.Contacts is null) return false;
        // A previously applied Finish proposal is durable selection history,
        // not a collection of ordinary stage drafts. The admitted composite
        // baseline includes later manual edits; rebuilding from PendingDraft
        // alone would silently lose purchases and fail preservation on re-Finish.
        if (inputs.Skills.CompositeBaseline is { } baseline)
        {
            selections = baseline.Selections with
            {
                AttributeAllocations = inputs.Skills.Attributes.Attributes.Select(row =>
                    new CharacterCreationAttributeAllocation(row.AttributeId, row.PriorityPointsSpent, row.KarmaLevels)).ToArray(),
                SkillAllocations = inputs.Skills.Allocations,
                GroupAllocations = inputs.Skills.GroupAllocations,
                SkillKarmaPurchases = baseline.Selections.SkillKarmaPurchases
                    ?? (inputs.Skills.KarmaPurchasePolicy is null ? null : [])
            };
            return true;
        }
        selections = new(inputs.Skills.Attributes.Attributes.Select(row => new CharacterCreationAttributeAllocation(
                row.AttributeId, row.PriorityPointsSpent, row.KarmaLevels)).ToArray(),
            inputs.Skills.Allocations, inputs.Skills.GroupAllocations, inputs.Magic.Selections,
            inputs.Lifestyles.Qualities.SelectedOptionIds, inputs.Lifestyles.Gear.Resources.PendingDraft?.KarmaInvestment ?? 0,
            inputs.Lifestyles.Gear.State.PendingDraft?.Lines.Select(row => new CharacterCreationGearSelection(row.OptionId, row.Quantity)).ToArray() ?? [],
            inputs.Contacts.Contacts, inputs.Lifestyles.Lifestyles, null) { Loadout = inputs.Loadout?.OriginalSelections,
                SkillKarmaPurchases = inputs.Skills.KarmaPurchasePolicy is null ? null : [] };
        return true;
    }

    private static bool ValidRequest(CharacterCreationCompletionPlanRequest request)
        => request is { Attributes: { PermittedAttributeOrder: not null }, Skills: { PermittedTargetOrder: not null },
                Magic: { ManualSelections: not null }, QualityOptionIds: not null, Contacts: not null, Purchases: not null }
            && CharacterCreationSkillsService.IsCompletionPurchaseShapeValid(request.SkillKarmaPurchases)
            && request.Attributes.MaximumAdditionalKarmaSpend >= 0
            && (request.Attributes.KarmaTargets is null || request.Attributes.KarmaTargets.Count <= 13
                && request.Attributes.KarmaTargets.All(target => target is not null && target.TargetUnaugmentedRating >= 0))
            && request.QualityOptionIds.Count <= 65_536 && request.QualityOptionIds.All(id => !string.IsNullOrWhiteSpace(id))
            && request.Contacts.Count <= CharacterCreationKarmaContactsRules.MaximumSelections && request.Contacts.All(row => row is not null)
            && request.Purchases.Count <= CharacterCreationPurchaseCompletionPlanner.MaximumTargets && request.Purchases.All(row => row is not null)
            && request.TotalKarmaInvestment >= 0 && request.KarmaToReserve >= 0 && request.Magic.KarmaToReserve >= 0
            && request.NuyenToReserve >= 0 && request.MaximumAdditionalPurchaseSpend >= 0
            && request.MaximumPurchaseEvaluations is >= 1 and <= CharacterCreationPurchaseCompletionPlanner.MaximumEvaluations
            && request.StartingLifestyleId != Guid.Empty;
    private static bool Terminal(string outcome) => outcome is CharacterCreationFoundationOutcomes.Invalid or CharacterCreationFoundationOutcomes.Conflict;
    private static bool Digest(string? left, string? right) => CharacterCreationSkillsDigest.EqualsFixedTime(left, right);
    private static bool Same<T>(T left, T right) => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
    private static T Detach<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
    private static CharacterCreationFoundationResult<CharacterCreationCompletionPlan> Reject(string outcome, string blocker) => new(outcome, null, [blocker]);
}
