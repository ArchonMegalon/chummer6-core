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
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidRequest(request)) return Reject(CharacterCreationFoundationOutcomes.Invalid,
            CharacterCreationCompletionPlanBlockers.RequestInvalid);
        inputs = Detach(inputs);
        request = Detach(request);
        if (!TryOriginalSelections(inputs, out var original))
            return Reject(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationCompletionReviewSchemas.AuthorityUnavailable);

        // This reuses whole-graph source/observation admission even when an
        // unfinished original character understandably has readiness blockers.
        var admission = CharacterCreationCompletionReviewService.Review(inputs,
            new(request.InputsSnapshotDigest, original), cancellationToken);
        if (admission.Value is null) return new(admission.Outcome, null, admission.Blockers);
        if (!MatchesOriginalRequests(inputs, request))
            return Reject(CharacterCreationFoundationOutcomes.Conflict, CharacterCreationCompletionPlanBlockers.SnapshotConflict);
        if (!Same(inputs.Magic.Selections, request.Magic.ManualSelections))
            return Reject(CharacterCreationFoundationOutcomes.Invalid, CharacterCreationCompletionPlanBlockers.ManualMagicMismatch);

        var choices = original with
        {
            QualityOptionIds = request.QualityOptionIds,
            Contacts = request.Contacts,
            TotalKarmaInvestment = request.TotalKarmaInvestment,
            StartingLifestyleId = request.StartingLifestyleId,
            ChangeableAttributeIds = request.Attributes.PermittedAttributeOrder,
            ChangeableSkillTargets = request.Skills.PermittedTargetOrder
        };
        var preservation = CharacterCreationCompletionPreservationRules.Check(inputs, choices);
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

            // Contact costs depend on the proposed CHA and Qualities. Price them
            // before PP, and reserve them plus the FULL explicit investment.
            // The admitted Priority Skills path spends points, not Karma; the
            // final whole review independently prices the supported domains.
            var contactResult = CharacterCreationContactsService.PreviewCompletion(inputs.Contacts,
                choices.AttributeAllocations, choices.QualityOptionIds, choices.Contacts, cancellationToken);
            if (contactResult.Outcome == CharacterCreationContactOutcomes.Conflict)
                return new(CharacterCreationFoundationOutcomes.Conflict, null, contactResult.Blockers);
            findings.AddRange(contactResult.Blockers);
            int? reserve = null;
            if (contactResult.Value is { Blockers.Count: 0, AdditionalKarmaCost: int contactCost } && contactCost >= 0)
            {
                try { reserve = checked(request.KarmaToReserve + contactCost + request.TotalKarmaInvestment); }
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
                        inputs.Loadout, purchaseRequest, cancellationToken);
                    if (Terminal(purchaseResult.Outcome)) return new(purchaseResult.Outcome, null, purchaseResult.Blockers);
                    findings.AddRange(purchaseResult.Blockers);
                    purchases = purchaseResult.Value;
                    if (purchases is not null)
                        choices = choices with { Gear = purchases.Gear, Lifestyles = purchases.Lifestyles,
                            StartingLifestyleId = purchases.StartingLifestyleId, Loadout = purchases.Loadout };
                }
            }
        }

        var final = CharacterCreationCompletionReviewService.Review(inputs,
            new(request.InputsSnapshotDigest, choices), cancellationToken);
        if (final.Value is not { } review) return new(final.Outcome, null, final.Blockers);
        if (request.AllocateRemainingAttributeKarma && attributes is not null
            && skills is not null && magic is not null && purchases is not null)
        {
            var karmaResult = AllocateAttributeKarma(inputs, request, attributes, choices, review, cancellationToken);
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
        selections = new(inputs.Skills.Attributes.Attributes.Select(row => new CharacterCreationAttributeAllocation(
                row.AttributeId, row.PriorityPointsSpent, row.KarmaLevels)).ToArray(),
            inputs.Skills.Allocations, inputs.Skills.GroupAllocations, inputs.Magic.Selections,
            inputs.Lifestyles.Qualities.SelectedOptionIds, inputs.Lifestyles.Gear.Resources.PendingDraft?.KarmaInvestment ?? 0,
            inputs.Lifestyles.Gear.State.PendingDraft?.Lines.Select(row => new CharacterCreationGearSelection(row.OptionId, row.Quantity)).ToArray() ?? [],
            inputs.Contacts.Contacts, inputs.Lifestyles.Lifestyles, null) { Loadout = inputs.Loadout?.OriginalSelections };
        return true;
    }

    private static bool ValidRequest(CharacterCreationCompletionPlanRequest request)
        => request is { Attributes: { PermittedAttributeOrder: not null }, Skills: { PermittedTargetOrder: not null },
                Magic: { ManualSelections: not null }, QualityOptionIds: not null, Contacts: not null, Purchases: not null }
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
