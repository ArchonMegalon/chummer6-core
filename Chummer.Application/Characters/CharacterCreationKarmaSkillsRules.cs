using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Karma foundation allocation before additional quality/improvement domains.
/// Sources and confirmed attributes are supplied by Core; no Priority points,
/// free talent ranks or imported-character modifiers are inferred here.
/// </summary>
public static class CharacterCreationKarmaSkillsRules
{
    public const string KnowledgeExpressionUnresolved = "creation-karma-skills-knowledge-expression-unresolved";
    public const string KarmaBudgetExceeded = "creation-karma-skills-budget-exceeded";
    public const int MaximumSkillAllocations = 256;
    public const int MaximumGroupAllocations = 64;

    public static bool TryFreeze(CharacterCreationKarmaSkillsSelection? selection,
        [NotNullWhen(true)] out CharacterCreationKarmaSkillsSelection? frozen)
    {
        frozen = null;
        if (selection?.Skills is null || selection.Groups is null
            || selection.Skills.Count > MaximumSkillAllocations || selection.Groups.Count > MaximumGroupAllocations)
            return false;
        try
        {
            var skills = selection.Skills.Take(MaximumSkillAllocations + 1).ToArray();
            var groups = selection.Groups.Take(MaximumGroupAllocations + 1).ToArray();
            if (skills.Length > MaximumSkillAllocations || groups.Length > MaximumGroupAllocations
                || skills.Any(item => item is null || item.KarmaLevels < 0 || item.KnowledgePointLevels < 0
                    || item.Kind is not (CharacterCreationSkillKinds.Active or CharacterCreationSkillKinds.Knowledge)
                    || string.IsNullOrWhiteSpace(item.SourceSkillId))
                || groups.Any(item => item is null || item.KarmaLevels < 0 || string.IsNullOrWhiteSpace(item.GroupId))
                || groups.Select(item => item.GroupId).Distinct(StringComparer.Ordinal).Count() != groups.Length)
                return false;
            frozen = new(skills.OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.SourceSkillId, StringComparer.Ordinal)
                .ThenBy(item => item.SpecializationOptionId, StringComparer.Ordinal).ToArray(),
                groups.OrderBy(item => item.GroupId, StringComparer.Ordinal).ToArray(), selection.TalentUnlock);
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return false; }
    }

    public static CharacterCreationKarmaSkillsQuote? Evaluate(CharacterCreationSkillsCatalog catalog,
        CharacterCreationKarmaSkillsPolicy policy, CharacterCreationKarmaTalentCatalog talents,
        CharacterCreationMetatypeOptionProjection metatype, string talentId,
        CharacterCreationKarmaAttributesQuote attributes, int karmaAvailable,
        CharacterCreationKarmaSkillsSelection selection)
    {
        if (catalog is null || !TryFreeze(selection, out var frozen) || karmaAvailable < 0 || !IsPolicyValid(policy)
            || policy.SettingsProfileId != catalog.SettingsProfileId || policy.RawProfileInputsDigest != catalog.RawProfileInputsDigest
            || attributes?.Policy is null || attributes.Policy.SettingsProfileId != policy.SettingsProfileId
            || attributes.Policy.RawProfileInputsDigest != policy.RawProfileInputsDigest) return null;
        var access = CharacterCreationKarmaSkillAccessRules.Evaluate(catalog, talents, metatype, talentId, frozen.TalentUnlock);
        if (access is null || !CharacterCreationKarmaAttributesRules.IsValid(attributes, metatype,
            talents.Options.SingleOrDefault(option => option.OptionId == talentId), attributes.Allocations)) return null;
        var blockers = new HashSet<string>(access.Blockers, StringComparer.Ordinal);
        if (!TryKnowledgePoints(policy.KnowledgePointsExpression, attributes, out int knowledgeTotal))
            blockers.Add(KnowledgeExpressionUnresolved);
        var sourceSkills = catalog.ActiveSkills.Concat(catalog.KnowledgeSkills)
            .ToDictionary(skill => (skill.Kind, skill.SourceSkillId));
        var selected = new Dictionary<string, CharacterCreationKarmaSkillAllocation>(StringComparer.Ordinal);
        foreach (var allocation in frozen.Skills)
        {
            if (!sourceSkills.TryGetValue((allocation.Kind, allocation.SourceSkillId), out var source)) return null;
            string key = Key(allocation, source.IsExotic);
            if (!selected.TryAdd(key, allocation)) return null;
        }
        var groupRanks = frozen.Groups.ToDictionary(item => item.GroupId, item => item.KarmaLevels, StringComparer.Ordinal);
        if (groupRanks.Keys.Any(id => catalog.SkillGroups.All(group => group.GroupId != id))) return null;
        var activeById = catalog.ActiveSkills.ToDictionary(skill => skill.SourceSkillId, StringComparer.Ordinal);
        var groupsByName = catalog.SkillGroups.ToDictionary(group => group.Name, StringComparer.Ordinal);
        var allowed = access.AllowedActiveSkillSourceIds.ToHashSet(StringComparer.Ordinal);
        int Personal(string id) => selected.GetValueOrDefault(CharacterCreationSkillKinds.Active + "/" + id)?.KarmaLevels ?? 0;
        int GroupRanks(CharacterCreationSkillCatalogEntry skill) => skill.SkillGroup is { } name
            && groupsByName.TryGetValue(name, out var group) ? groupRanks.GetValueOrDefault(group.GroupId) : 0;
        int Rating(string id) => checked(Personal(id) + GroupRanks(activeById[id]));
        bool HasSpec(string id) => selected.GetValueOrDefault(CharacterCreationSkillKinds.Active + "/" + id)?.SpecializationOptionId is not null;
        var projectedGroups = new List<CharacterCreationKarmaSkillGroupProjection>();
        var projectedSkills = new List<CharacterCreationKarmaSkillProjection>();
        decimal used = 0, knowledgeUsed = 0;
        int natives = 0;
        try
        {
            foreach (var group in catalog.SkillGroups)
            {
                int ranks = groupRanks.GetValueOrDefault(group.GroupId);
                if (!groupRanks.ContainsKey(group.GroupId) && group.MemberSkillSourceIds.All(id => Personal(id) == 0)) continue;
                string[] enabled = group.MemberSkillSourceIds.Where(allowed.Contains).ToArray();
                var local = new HashSet<string>(StringComparer.Ordinal);
                if (ranks > policy.MaxSkillGroupRatingCreate) local.Add(CharacterCreationSkillsBlockers.RatingInvalid);
                if (ranks > 0 && enabled.Length == 0) local.Add(CharacterCreationSkillsBlockers.GroupInvalid);
                if (ranks > 0 && policy.StrictSkillGroupsInCreateMode
                    && group.MemberSkillSourceIds.Any(id => Personal(id) > 0 || HasSpec(id)))
                    local.Add(CharacterCreationSkillsBlockers.GroupBroken);
                int groupCost = 0;
                if (enabled.Length > 0 && ranks > 0)
                {
                    int upper = enabled.Min(Rating);
                    if (!CharacterCreationSkillCostRules.TryGroup(upper - ranks, upper,
                        policy.KarmaNewSkillGroup, policy.KarmaImproveSkillGroup, out groupCost))
                        local.Add(CharacterCreationSkillsBlockers.AllocationInvalid);
                }
                bool broken = enabled.Length > 1 && (enabled.Select(Rating).Distinct().Count() > 1
                    || policy.SpecializationsBreakSkillGroups && enabled.Any(HasSpec));
                projectedGroups.Add(new(new(group.GroupId, ranks), group.Name, groupCost, broken, Ordered(local)));
                used += groupCost;
                blockers.UnionWith(local);
                if (ranks > 0)
                    foreach (string id in group.MemberSkillSourceIds)
                        selected.TryAdd(CharacterCreationSkillKinds.Active + "/" + id,
                            new(id, CharacterCreationSkillKinds.Active, 0));
            }

            foreach (var allocation in selected.Values.OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.SourceSkillId, StringComparer.Ordinal).ThenBy(item => item.SpecializationOptionId, StringComparer.Ordinal))
            {
                var source = sourceSkills[(allocation.Kind, allocation.SourceSkillId)];
                bool knowledge = source.Kind == CharacterCreationSkillKinds.Knowledge;
                int groupLevel = knowledge ? 0 : GroupRanks(source);
                int rating = checked(allocation.KnowledgePointLevels + allocation.KarmaLevels + groupLevel);
                var local = new HashSet<string>(StringComparer.Ordinal);
                bool enabled = knowledge || allowed.Contains(source.SourceSkillId);
                bool explicitSpend = allocation.KarmaLevels > 0 || allocation.KnowledgePointLevels > 0 || allocation.SpecializationOptionId is not null;
                if (!enabled && explicitSpend) local.Add(CharacterCreationSkillsBlockers.TalentAccessRequired);
                if (!knowledge && allocation.KnowledgePointLevels != 0) local.Add(CharacterCreationSkillsBlockers.AllocationInvalid);
                if (rating > (knowledge ? policy.MaxKnowledgeSkillRatingCreate : policy.MaxActiveSkillRatingCreate))
                    local.Add(CharacterCreationSkillsBlockers.RatingInvalid);
                int points = knowledge ? allocation.KnowledgePointLevels : 0;
                int specCost = 0, cost = 0;
                if (allocation.IsNativeLanguage)
                {
                    natives++;
                    if (!knowledge || !source.CanBeNativeLanguage || rating != 0 || allocation.SpecializationOptionId is not null)
                        local.Add(CharacterCreationSkillsBlockers.NativeLanguageInvalid);
                }
                if (allocation.SpecializationOptionId is { } specId)
                {
                    var spec = source.Specializations.SingleOrDefault(option => option.OptionId == specId);
                    if (spec is null || spec.Name.Contains('[', StringComparison.Ordinal) || rating <= 0 || !enabled
                        || groupLevel > 0 && policy.StrictSkillGroupsInCreateMode)
                        local.Add(CharacterCreationSkillsBlockers.SpecializationInvalid);
                    switch (allocation.SpecializationPayment)
                    {
                        case CharacterCreationKarmaSpecializationPayments.ExoticIdentity when source.IsExotic:
                            break;
                        case CharacterCreationKarmaSpecializationPayments.Karma when !source.IsExotic:
                            specCost = knowledge ? policy.KarmaKnowledgeSpecialization : policy.KarmaSpecialization;
                            break;
                        case CharacterCreationKarmaSpecializationPayments.KnowledgePoint when knowledge && !source.IsExotic:
                            if (allocation.KarmaLevels > 0 && allocation.KnowledgePointLevels == 0
                                && !policy.AllowPointBuySpecializationsOnKarmaSkills)
                                local.Add(CharacterCreationSkillsBlockers.SpecializationInvalid);
                            points = checked(points + 1);
                            break;
                        default: local.Add(CharacterCreationSkillsBlockers.SpecializationInvalid); break;
                    }
                }
                else if (allocation.SpecializationPayment is not null || source.IsExotic)
                    local.Add(CharacterCreationSkillsBlockers.SpecializationInvalid);
                if (groupLevel > 0 && policy.StrictSkillGroupsInCreateMode && allocation.KarmaLevels > 0)
                    local.Add(CharacterCreationSkillsBlockers.GroupBroken);

                if (knowledge)
                {
                    if (!CharacterCreationSkillCostRules.TryKnowledge(allocation.KnowledgePointLevels, rating,
                        policy.KarmaNewKnowledgeSkill, policy.KarmaImproveKnowledgeSkill, out cost))
                        local.Add(CharacterCreationSkillsBlockers.AllocationInvalid);
                }
                else if (rating > 0)
                {
                    CharacterCreationSkillGroupCatalogEntry? group = source.SkillGroup is { } name
                        ? groupsByName.GetValueOrDefault(name) : null;
                    string[] members = group?.MemberSkillSourceIds.ToArray() ?? [];
                    string[] enabledMembers = members.Where(allowed.Contains).ToArray();
                    bool compensate = policy.CompensateSkillGroupKarmaDifference && members.Length > 0
                        && catalog.ActiveSkillSourceOrder.FirstOrDefault(id => enabledMembers.Contains(id, StringComparer.Ordinal)) == source.SourceSkillId;
                    int? otherMinimum = enabledMembers.Where(id => id != source.SourceSkillId).Select(id => (int?)Rating(id)).Min();
                    if (groupLevel == 0)
                    {
                        if (!TryActiveInterval(0, rating, policy, compensate, otherMinimum, enabledMembers.Length, out cost))
                            local.Add(CharacterCreationSkillsBlockers.AllocationInvalid);
                    }
                    else
                    {
                        // Skill.CurrentKarmaCost uses all canonical members for
                        // this split, whereas SkillGroup uses enabled members.
                        int groupUpper = members.Min(Rating);
                        if (!TryActiveInterval(0, groupUpper - groupLevel, policy, compensate, otherMinimum, enabledMembers.Length, out int below)
                            || !TryActiveInterval(groupUpper, rating, policy, compensate, otherMinimum, enabledMembers.Length, out int above))
                            local.Add(CharacterCreationSkillsBlockers.AllocationInvalid);
                        else cost = checked(below + above);
                    }
                }
                cost = Math.Max(0, checked(cost + specCost));
                knowledgeUsed += points;
                used += cost;
                blockers.UnionWith(local);
                projectedSkills.Add(new(allocation, source.SourceNodeDigest, source.Name,
                    allocation.IsNativeLanguage ? null : rating, groupLevel, points, cost, specCost, enabled, Ordered(local)));
            }
        }
        catch (OverflowException) { return null; }
        if (natives == 0) blockers.Add(CharacterCreationSkillsBlockers.NativeLanguageRequired);
        if (natives > 1) blockers.Add(CharacterCreationSkillsBlockers.NativeLanguageLimitExceeded);
        if (knowledgeUsed > knowledgeTotal) blockers.Add(CharacterCreationSkillsBlockers.KnowledgeBudgetExceeded);
        if (used > karmaAvailable) blockers.Add(KarmaBudgetExceeded);
        var quote = new CharacterCreationKarmaSkillsQuote(CharacterCreationKarmaSkillsQuote.SchemaV1,
            policy, catalog.CatalogDigest, attributes.QuoteDigest, access, frozen,
            projectedSkills.ToArray(), projectedGroups.OrderBy(group => group.Allocation.GroupId, StringComparer.Ordinal).ToArray(),
            knowledgeTotal, knowledgeUsed, natives, karmaAvailable, used, Ordered(blockers), string.Empty);
        return quote with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quote) };
    }

    private static string Key(CharacterCreationKarmaSkillAllocation allocation, bool exotic) =>
        allocation.Kind + "/" + allocation.SourceSkillId + (exotic ? "/" + allocation.SpecializationOptionId : string.Empty);

    private static string[] Ordered(HashSet<string> blockers) => blockers.Order(StringComparer.Ordinal).ToArray();

    private static bool IsPolicyValid(CharacterCreationKarmaSkillsPolicy? policy) =>
        policy is { Schema: CharacterCreationKarmaSkillsPolicy.SchemaV1, SourceAnchorIds.Count: > 0 }
        && new[] { policy.KarmaNewActiveSkill, policy.KarmaImproveActiveSkill, policy.KarmaNewKnowledgeSkill,
            policy.KarmaImproveKnowledgeSkill, policy.KarmaNewSkillGroup, policy.KarmaImproveSkillGroup,
            policy.KarmaSpecialization, policy.KarmaKnowledgeSpecialization, policy.MaxActiveSkillRatingCreate,
            policy.MaxKnowledgeSkillRatingCreate }.All(value => value >= 0)
        && policy.AuthorityDigest == CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(policy);

    private static bool TryKnowledgePoints(string expression, CharacterCreationKarmaAttributesQuote attributes, out int points)
    {
        points = 0;
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 2048) return false;
        foreach (var attribute in attributes.Attributes)
        {
            string value = attribute.Current.ToString(CultureInfo.InvariantCulture);
            expression = expression.Replace("{" + attribute.AttributeId + "Unaug}", value, StringComparison.Ordinal)
                .Replace("{" + attribute.AttributeId + "}", value, StringComparison.Ordinal);
        }
        expression = expression.Replace(" div ", " / ", StringComparison.Ordinal);
        int depth = 0;
        foreach (char character in expression)
        {
            if (character == '(' && ++depth > 32) return false;
            if (character == ')' && --depth < 0) return false;
            if (!char.IsWhiteSpace(character) && character is not (>= '0' and <= '9')
                && character is not ('(' or ')' or '+' or '-' or '*' or '/' or '.')) return false;
        }
        if (depth != 0 || !CharacterGearQuantityRules.TryEvaluateCostExpression(expression, 0, out decimal result)
            || result > int.MaxValue) return false;
        points = (int)decimal.Ceiling(result); // Legacy StandardRound rounds away from zero.
        return true;
    }

    private static bool TryActiveInterval(int lower, int upper, CharacterCreationKarmaSkillsPolicy policy,
        bool compensate, int? otherMinimum, int memberCount, out int cost)
    {
        cost = 0;
        if (lower >= upper) return true;
        if (!CharacterCreationSkillCostRules.TryActive(lower, upper, policy.KarmaNewActiveSkill,
            policy.KarmaImproveActiveSkill, out cost)) return false;
        if (!compensate || otherMinimum is null || otherMinimum <= lower) return true;
        try
        {
            long ceiling = Math.Min(otherMinimum.Value, upper);
            // Preserve Skill.RangeCost's house-rule formula, not a blanket
            // replacement of all individual ranks with a new group purchase.
            long levels = lower == 0 ? (ceiling - 1) * ceiling / 2
                : (ceiling * (ceiling + 1) - (long)lower * (lower + 1L)) / 2;
            long group = checked(levels * policy.KarmaImproveSkillGroup + (lower == 0 ? policy.KarmaNewSkillGroup : 0));
            long individual = checked(levels * policy.KarmaImproveActiveSkill + (lower == 0 ? policy.KarmaNewActiveSkill : 0));
            cost = checked((int)(cost + group - checked(memberCount * individual)));
            return true;
        }
        catch (OverflowException) { cost = 0; return false; }
    }
}
