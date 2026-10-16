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
    {
        ArgumentNullException.ThrowIfNull(inputs);
        cancellationToken.ThrowIfCancellationRequested();
        if (purchases is null)
            return PreviewWithProposedAttributes(inputs, attributes, allocations, groups, cancellationToken);
        inputs = JsonSerializer.Deserialize<CharacterCreationSkillsCompletionInputs>(JsonSerializer.SerializeToUtf8Bytes(inputs))!;
        purchases = purchases.ToArray();
        var prepared = PrepareCompletionPreview(inputs, attributes, cancellationToken);
        if (prepared.Value is null) return new(prepared.Outcome, null, prepared.Blockers);
        var baseline = prepared.Value.Preview(allocations, groups, cancellationToken);
        if (!IsCompletionPurchaseShapeValid(purchases))
            return Blocked<CharacterCreationAttributeSkillsPreview>(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillKarmaPurchaseBlockers.PurchaseInvalid);
        // Empty is explicit opt-in to the source policy, never an implicit source read.
        if (!IsCompletionKarmaPolicyValid(inputs.KarmaPurchasePolicy, inputs.Authority))
            return Blocked<CharacterCreationAttributeSkillsPreview>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationSkillKarmaPurchaseBlockers.PolicyUnavailable);
        if (baseline.Value is not { } pointPreview || baseline.Blockers.Count != 0)
            return purchases.Count == 0 ? baseline : new(baseline.Outcome, null, baseline.Blockers);
        return prepared.Value.PreviewPurchases(pointPreview, purchases.ToArray(), inputs.KarmaPurchasePolicy!, cancellationToken);
    }

    // Point legality is already authoritative, but the independent interval price
    // cannot claim support for quality-based skill discounts, caps or grants.
    // Talent unlocks are handled by the existing source-bound access policy.
    internal static bool HasUnsupportedCompletionSkillModifiers(IEnumerable<string> qualitySources)
    {
        try
        {
            return qualitySources.Any(xml => XElement.Parse(xml).Elements("bonus")
                .SelectMany(bonus => bonus.Descendants()).Any(effect => effect.Name.LocalName != "unlockskills"
                    && (effect.Name.LocalName.Contains("skill", StringComparison.OrdinalIgnoreCase)
                        || effect.Name.LocalName.Contains("specialization", StringComparison.OrdinalIgnoreCase))));
        }
        catch (Exception error) when (error is XmlException or ArgumentException) { return true; }
    }

    internal static bool IsCompletionPurchaseShapeValid(IReadOnlyList<CharacterCreationSkillKarmaPurchase>? purchases)
        => purchases is null || purchases.Count <= CharacterCreationSkillKarmaPurchaseBlockers.MaximumPurchases
            && purchases.All(row => row is not null && !string.IsNullOrWhiteSpace(row.SourceSkillId)
                && row.Kind is CharacterCreationSkillKinds.Active or CharacterCreationSkillKinds.Knowledge
                && CharacterCreationSkillsDigest.IsCanonical(row.SourceNodeDigest) && row.Rating >= 1)
            && purchases.Select(row => (row.Kind, row.SourceSkillId)).Distinct().Count() == purchases.Count;

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
