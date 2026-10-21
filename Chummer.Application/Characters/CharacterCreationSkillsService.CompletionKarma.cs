using Chummer.Contracts.Characters;
using System.Xml;
using System.Xml.Linq;
using System.Text.Json;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationSkillsService
{
    /// <summary>Exact individual purchases layered over the unchanged point/grant
    /// preview. This prices intervals, not a second allocation of the point pools.
    /// Wallet admission belongs to the complete review, including all other costs.</summary>
    public static CharacterCreationFoundationResult<CharacterCreationAttributeSkillsPreview> PreviewCompletionPurchases(
        CharacterCreationSkillsCompletionInputs inputs,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributes,
        IReadOnlyList<CharacterCreationSkillAllocation> allocations,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups,
        IReadOnlyList<CharacterCreationSkillKarmaPurchase>? purchases,
        CancellationToken cancellationToken = default)
        => PreviewCompletionPurchases(inputs, attributes, allocations, groups, purchases, cancellationToken, captureExecution: false);

    internal static CharacterCreationFoundationResult<CharacterCreationAttributeSkillsPreview> PreviewCompletionPurchases(
        CharacterCreationSkillsCompletionInputs inputs,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributes,
        IReadOnlyList<CharacterCreationSkillAllocation> allocations,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups,
        IReadOnlyList<CharacterCreationSkillKarmaPurchase>? purchases,
        CancellationToken cancellationToken, bool captureExecution, CompleteSkillTrace? completeTrace = null)
        => inputs.SkillKarmaQualitySelections is not null
            ? PreviewCompositeSkillPurchases(inputs, attributes, allocations, groups, purchases, cancellationToken, captureExecution, completeTrace)
            : PreviewCompletionPurchasesCore(inputs, attributes, allocations, groups, purchases, null, cancellationToken, captureExecution, completeTrace);

    internal static CharacterCreationFoundationResult<CharacterCreationAttributeSkillsPreview> PreviewCompletionPurchasesCore(
        CharacterCreationSkillsCompletionInputs inputs,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributes,
        IReadOnlyList<CharacterCreationSkillAllocation> allocations,
        IReadOnlyList<CharacterCreationSkillGroupAllocation> groups,
        IReadOnlyList<CharacterCreationSkillKarmaPurchase>? purchases,
        IReadOnlyList<CharacterCreationKnowledgeCategoryEffect>? knowledgeEffects,
        CancellationToken cancellationToken, bool captureExecution = false, CompleteSkillTrace? completeTrace = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        cancellationToken.ThrowIfCancellationRequested();
        if (purchases is null)
        {
            if (completeTrace is null)
                return PreviewWithProposedAttributes(inputs, attributes, allocations, groups, cancellationToken);
            var point = PrepareCompletionPreview(inputs, attributes, cancellationToken);
            return point.Value is { } session
                ? session.Preview(allocations, groups, cancellationToken, completeTrace: completeTrace)
                : new(point.Outcome, null, point.Blockers);
        }
        inputs = JsonSerializer.Deserialize<CharacterCreationSkillsCompletionInputs>(JsonSerializer.SerializeToUtf8Bytes(inputs))!;
        purchases = purchases.ToArray();
        var prepared = PrepareCompletionPreview(inputs, attributes, cancellationToken);
        if (prepared.Value is null) return new(prepared.Outcome, null, prepared.Blockers);
        if (!IsCompletionPurchaseShapeValid(purchases))
            return Blocked<CharacterCreationAttributeSkillsPreview>(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillKarmaPurchaseBlockers.PurchaseInvalid);
        // Empty is explicit opt-in to the source policy, never an implicit source read.
        if (!IsCompletionKarmaPolicyValid(inputs.KarmaPurchasePolicy, inputs.Authority)
            || !IsCompletionCompensationOrderValid(inputs.KarmaCompensationSourceOrder, inputs.Authority))
            return Blocked<CharacterCreationAttributeSkillsPreview>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationSkillKarmaPurchaseBlockers.PolicyUnavailable);
        var baseline = prepared.Value.Preview(allocations, groups, cancellationToken, knowledgeEffects: knowledgeEffects, completeTrace: completeTrace);
        if (baseline.Value is { Blockers.Count: 0 } original && inputs.KarmaPurchasePolicy!.AllowSkillRegrouping.HasValue
            && (inputs.KarmaPurchasePolicy.CompensateSkillGroupKarmaDifference
                || original.Skills.Any(row => row.GroupRating > 0)
                || purchases.Any(purchase => purchase.Kind == CharacterCreationSkillKinds.Active
                    && original.SkillGroups.Any(group => group.MemberSkillSourceIds.Contains(purchase.SourceSkillId, StringComparer.Ordinal)))))
            baseline = prepared.Value.Preview(allocations, groups, cancellationToken, projectGroupMembers: true, knowledgeEffects: knowledgeEffects, completeTrace: completeTrace);
        if (baseline.Value is not { } pointPreview || baseline.Blockers.Count != 0)
            return purchases.Count == 0 ? baseline : new(baseline.Outcome, null, baseline.Blockers);
        return prepared.Value.PreviewPurchases(pointPreview, purchases.ToArray(), inputs.KarmaPurchasePolicy!,
            inputs.KarmaCompensationSourceOrder, cancellationToken, knowledgeEffects, captureExecution: captureExecution, completeTrace: completeTrace);
    }

    // Point legality is already authoritative, but the independent interval price
    // cannot claim support for quality-based skill discounts, caps or grants.
    // Talent unlocks are handled by the existing source-bound access policy.
    internal static bool HasUnsupportedCompletionSkillModifiers(IEnumerable<string> qualitySources, bool allowDiceOnlyGrants = false)
    {
        try
        {
            return qualitySources.Any(xml => XElement.Parse(xml).Elements().Where(node => node.Name.LocalName is "bonus" or "firstlevelbonus")
                .SelectMany(bonus => bonus.Descendants()).Any(effect => effect.Name.LocalName != "unlockskills"
                    && (effect.Name.LocalName.Contains("skill", StringComparison.OrdinalIgnoreCase)
                        || effect.Name.LocalName.Contains("specialization", StringComparison.OrdinalIgnoreCase))
                    && !(allowDiceOnlyGrants && CharacterCreationAwakenedLegacyProjector.IsDiceOnlySkillGrant(effect))));
        }
        catch (Exception error) when (error is XmlException or ArgumentException) { return true; }
    }

    internal static bool IsCompletionPurchaseShapeValid(IReadOnlyList<CharacterCreationSkillKarmaPurchase>? purchases)
        => purchases is null || purchases.Count <= CharacterCreationSkillKarmaPurchaseBlockers.MaximumPurchases
            && purchases.All(row => row is not null && !string.IsNullOrWhiteSpace(row.SourceSkillId)
                && row.Kind is CharacterCreationSkillKinds.Active or CharacterCreationSkillKinds.Knowledge
                && CharacterCreationSkillsDigest.IsCanonical(row.SourceNodeDigest) && row.Rating >= 1)
            && purchases.Select(row => (row.Kind, row.SourceSkillId)).Distinct().Count() == purchases.Count;

    internal static bool TryCaptureCompletionGroupOrderAuthority(CharacterCreationSkillsCompletionInputs inputs,
        out CharacterCreationSkillGroupOrderAuthority? authority)
    {
        authority = null;
        // No-order history preserves the original projection and deterministic IDs.
        if (inputs.KarmaCompensationSourceOrder is null) return true;
        if (inputs.KarmaPurchasePolicy is not { CompensateSkillGroupKarmaDifference: true, AllowSkillRegrouping: not null }
            || !IsCompletionKarmaPolicyValid(inputs.KarmaPurchasePolicy, inputs.Authority)
            || !IsCompletionCompensationOrderValid(inputs.KarmaCompensationSourceOrder, inputs.Authority)) return false;
        authority = new(CharacterCreationSkillGroupOrderAuthority.SchemaV1,
            CharacterCreationSkillsDigest.Compute(inputs.Authority), inputs.KarmaPurchasePolicy.AuthorityDigest,
            inputs.KarmaCompensationSourceOrder.ToArray());
        return true;
    }

    // Coverage observes the same immutable inputs used by PreviewCompletionPurchases.
    // Validate typed identities and require the entire group closure, including
    // unavailable members: the evaluator derives eligibility from talent/movement.
    // The original snapshot also binds the exact source-order payer evidence.
    // Selected intervals, modifiers and the shared wallet still require full review.
    internal static bool HasCompletionCompensationInputs(CharacterCreationSkillsCompletionInputs inputs)
        => inputs.KarmaPurchasePolicy is { CompensateSkillGroupKarmaDifference: true, AllowSkillRegrouping: not null }
            && inputs.KarmaCompensationSourceOrder is not null
            && IsValidCompletionInputs(inputs)
            && inputs.Authority.SkillGroups.All(group => group.MemberSkillSourceIds.SequenceEqual(
                inputs.Authority.ActiveSkills.Where(skill => skill.SkillGroup == group.Name)
                    .Select(skill => skill.SourceSkillId).Order(StringComparer.Ordinal), StringComparer.Ordinal))
            && TryCaptureCompletionGroupOrderAuthority(inputs, out var authority) && authority is not null;

    private static bool IsCompletionCompensationOrderValid(IReadOnlyList<string>? order,
        CharacterCreationSkillsAuthority authority)
        => order is null || order.Count == authority.ActiveSkills.Count
            && order.Order(StringComparer.Ordinal).SequenceEqual(
                authority.ActiveSkills.Select(row => row.SourceSkillId).Order(StringComparer.Ordinal), StringComparer.Ordinal);

    internal static bool IsCompletionKarmaPolicyValid(CharacterCreationKarmaSkillsPolicy? policy,
        CharacterCreationSkillsAuthority authority)
        => policy is { Schema: CharacterCreationKarmaSkillsPolicy.PriorityPurchaseSchemaV1, SourceAnchorIds.Count: > 0 }
            && policy.SettingsProfileId == authority.SettingsProfileId
            && policy.RawProfileInputsDigest == authority.RawProfileInputsDigest
            && policy.MaxActiveSkillRatingCreate == authority.MaxActiveSkillRatingCreate
            && policy.MaxKnowledgeSkillRatingCreate == authority.MaxKnowledgeSkillRatingCreate
            && policy.KnowledgePointsExpression == authority.KnowledgePointsExpression
            && policy.UsePointsOnBrokenGroups == authority.UsePointsOnBrokenGroups
            && policy.StrictSkillGroupsInCreateMode == authority.StrictSkillGroupsInCreateMode
            && policy.SpecializationsBreakSkillGroups == authority.SpecializationsBreakSkillGroups
            && new[] { policy.KarmaNewActiveSkill, policy.KarmaImproveActiveSkill, policy.KarmaNewKnowledgeSkill,
                policy.KarmaImproveKnowledgeSkill, policy.KarmaNewSkillGroup, policy.KarmaImproveSkillGroup,
                policy.KarmaSpecialization, policy.KarmaKnowledgeSpecialization }.All(cost => cost >= 0)
            && CharacterCreationSkillsDigest.EqualsFixedTime(policy.AuthorityDigest,
                CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(policy));
}
