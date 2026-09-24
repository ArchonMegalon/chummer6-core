using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Whole-sequence skill spending. Free module levels remain separate
/// from purchases; this calculation cannot authorize a partial workspace write.</summary>
internal static partial class CharacterCreationLifeModuleSkillsRules
{
    internal static CharacterCreationLifeModuleSkillsQuoteResult Evaluate(string characterXml,
        CharacterCreationFoundationSequenceWritePlan effects, CharacterCreationLifeModuleMetatypeWritePlan racial,
        CharacterCreationLifeModuleTalentWritePlan talent, CharacterCreationLifeModuleAttributeQuote attributes,
        CharacterCreationKarmaSkillsSelection? selection, ICharacterSourceDataContext context)
    {
        try
        {
            if (!context.TryResolveCreationSkillsCatalog(out var catalog) || catalog is null
                || !context.TryResolveCreationLifeModuleSkillsPolicy(out var policy) || policy is null)
                return Failed(CharacterCreationSkillsBlockers.AuthorityUnavailable);
            var result = Quote(characterXml, effects, racial, talent, attributes, selection, catalog, policy);
            if (!context.TryResolveCreationSkillsCatalog(out var finalCatalog)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(catalog, finalCatalog)
                || !context.TryResolveCreationLifeModuleSkillsPolicy(out var finalPolicy)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(policy, finalPolicy))
                return Failed(CharacterCreationFoundationBlockers.SourceDigestConflict);
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Failed(CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);
        }
    }

    internal static CharacterCreationLifeModuleSkillsQuoteResult Quote(string characterXml,
        CharacterCreationFoundationSequenceWritePlan effects, CharacterCreationLifeModuleMetatypeWritePlan racial,
        CharacterCreationLifeModuleTalentWritePlan talent, CharacterCreationLifeModuleAttributeQuote attributes,
        CharacterCreationKarmaSkillsSelection? selection, CharacterCreationSkillsCatalog catalog,
        CharacterCreationKarmaSkillsPolicy policy)
    {
        try
        {
            if (!CharacterCreationSkillsCatalogAuthority.IsValid(catalog)
                || !ValidPolicy(policy) || policy.SettingsProfileId != catalog.SettingsProfileId
                || policy.RawProfileInputsDigest != catalog.RawProfileInputsDigest
                || attributes.Policy.SettingsProfileId != policy.SettingsProfileId
                || attributes.Policy.RawProfileInputsDigest != policy.RawProfileInputsDigest
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(attributes,
                    CharacterCreationLifeModuleAttributeRules.Evaluate(characterXml, effects, racial,
                        attributes.Policy, attributes.Attributes.Select(row => new CharacterCreationLifeModuleAttributePurchase(
                            row.AttributeId, row.KarmaLevels)).ToArray(), talent).Quote)
                || !CharacterCreationKarmaSkillsRules.TryFreeze(selection ?? new([], []), out var frozen)
                || frozen.TalentUnlock is not null && frozen.TalentUnlock != talent.Selection.SkillUnlock)
                return Failed(CharacterCreationSkillsBlockers.AuthorityUnavailable);
            frozen = frozen with { TalentUnlock = talent.Selection.SkillUnlock };
            XElement root = XDocument.Parse(characterXml).Root ?? throw new InvalidDataException();
            if (root.Elements("newskills").Elements().Any(container => container.Elements().Any())
                || root.Elements("skills").Any(container => container.HasElements))
                return Failed(CharacterCreationFoundationBlockers.PendingDraftConflict);
            if (!TryReadModifiers(effects, racial, talent, catalog, out var modifiers))
                return Failed(CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
            var activeById = catalog.ActiveSkills.ToDictionary(row => row.SourceSkillId, StringComparer.Ordinal);
            var sources = catalog.ActiveSkills.Concat(catalog.KnowledgeSkills).ToDictionary(row => (row.Kind, row.SourceSkillId));
            var groupsByName = catalog.SkillGroups.ToDictionary(row => row.Name, StringComparer.Ordinal);
            var groupsById = catalog.SkillGroups.ToDictionary(row => row.GroupId, StringComparer.Ordinal);
            var selected = new Dictionary<string, CharacterCreationKarmaSkillAllocation>(StringComparer.Ordinal);
            foreach (var allocation in frozen.Skills)
            {
                if (!sources.TryGetValue((allocation.Kind, allocation.SourceSkillId), out var source)
                    || !selected.TryAdd(SkillKey(allocation, source.IsExotic), allocation))
                    return Failed(CharacterCreationSkillsBlockers.AllocationInvalid);
            }
            var requestedGroups = frozen.Groups.ToDictionary(row => row.GroupId, row => row.KarmaLevels, StringComparer.Ordinal);
            if (requestedGroups.Keys.Any(id => !groupsById.ContainsKey(id))) return Failed(CharacterCreationSkillsBlockers.GroupInvalid);
            var rates = new[] { racial.Metatype.Movement.Walk, racial.Metatype.Movement.Run, racial.Metatype.Movement.Sprint };
            if (rates.Any(rate => rate.Ground < 0 || rate.Swim < 0 || rate.Fly < 0)) return Failed(CharacterCreationSkillsBlockers.AuthorityUnavailable);
            bool ground = !racial.Metatype.Movement.IsSpecial && rates.Any(rate => rate.Ground > 0);
            bool swim = !racial.Metatype.Movement.IsSpecial && rates.Any(rate => rate.Swim > 0);
            bool fly = !racial.Metatype.Movement.IsSpecial && rates.Any(rate => rate.Fly > 0);
            var allowed = catalog.ActiveSkills.Where(row => (!row.RequiresGroundMovement || ground)
                    && (!row.RequiresSwimMovement || swim) && (!row.RequiresFlyMovement || fly)
                    && (CharacterCreationSkillsAccessRules.IsOrdinary(row)
                        || modifiers.Unlocks.Any(filter => CharacterCreationSkillsAccessRules.Matches(row, filter))))
                .Select(row => row.SourceSkillId).ToHashSet(StringComparer.Ordinal);
            bool GroupEnabled(CharacterCreationSkillGroupCatalogEntry group) => group.MemberSkillSourceIds.Any(allowed.Contains)
                && !group.MemberSkillSourceIds.Any(id => modifiers.DisabledGroupCategories.Contains(activeById[id].Category));
            int Free(CharacterCreationSkillCatalogEntry source) => modifiers.SkillLevels.GetValueOrDefault(source.Name);
            int Personal(string id) => selected.GetValueOrDefault(CharacterCreationSkillKinds.Active + "/" + id)?.KarmaLevels ?? 0;
            bool HasSpec(string id) => selected.GetValueOrDefault(CharacterCreationSkillKinds.Active + "/" + id)?.SpecializationOptionId is not null;
            bool GroupUnbroken(CharacterCreationSkillGroupCatalogEntry group) => GroupEnabled(group)
                && (!policy.StrictSkillGroupsInCreateMode || group.MemberSkillSourceIds.All(id => Personal(id) + Free(activeById[id]) <= 0));
            int GroupFree(CharacterCreationSkillGroupCatalogEntry group) => modifiers.GroupLevels.GetValueOrDefault(group.Name);
            int GroupLevels(CharacterCreationSkillGroupCatalogEntry group) => !GroupEnabled(group) ? 0
                : Math.Min(checked(GroupFree(group) + (GroupUnbroken(group) ? requestedGroups.GetValueOrDefault(group.GroupId) : 0)), policy.MaxSkillGroupRatingCreate);
            int EffectiveGroup(CharacterCreationSkillCatalogEntry source) => source.SkillGroup is { } name
                && groupsByName.TryGetValue(name, out var group) ? GroupLevels(group) : 0;
            int Rating(string id) => Math.Min(checked(Personal(id) + Free(activeById[id]) + EffectiveGroup(activeById[id])), policy.MaxActiveSkillRatingCreate);
            // Include grants even without a paid allocation. Effective levels are
            // never copied into the paid allocation or charged a second time.
            foreach (var source in catalog.ActiveSkills.Where(row => Free(row) != 0 || EffectiveGroup(row) != 0))
                selected.TryAdd(CharacterCreationSkillKinds.Active + "/" + source.SourceSkillId, new(source.SourceSkillId, CharacterCreationSkillKinds.Active, 0));
            var blockers = new HashSet<string>(attributes.Blockers, StringComparer.Ordinal);
            if (!CharacterCreationKarmaSkillsRules.TryKnowledgePoints(policy.KnowledgePointsExpression,
                attributes.Attributes.ToDictionary(row => row.AttributeId, row => row.Current), out int expressionPoints))
                blockers.Add(CharacterCreationKarmaSkillsRules.KnowledgeExpressionUnresolved);
            int knowledgeTotal = checked(expressionPoints + Round(modifiers.KnowledgePoints));
            var skills = new List<CharacterCreationLifeModuleSkillValue>();
            var groups = new List<CharacterCreationLifeModuleSkillGroupValue>();
            decimal used = 0;
            int knowledgeUsed = 0, natives = 0;
            foreach (var group in catalog.SkillGroups)
            {
                int paid = requestedGroups.GetValueOrDefault(group.GroupId), free = GroupFree(group);
                if (paid == 0 && free == 0 && !group.MemberSkillSourceIds.Any(id => Personal(id) != 0 || Free(activeById[id]) != 0)) continue;
                var local = new HashSet<string>(StringComparer.Ordinal);
                bool enabled = GroupEnabled(group);
                if (paid > 0 && !enabled) local.Add(CharacterCreationSkillsBlockers.GroupInvalid);
                if (paid > 0 && (!GroupUnbroken(group) || policy.StrictSkillGroupsInCreateMode && group.MemberSkillSourceIds.Any(HasSpec)))
                    local.Add(CharacterCreationSkillsBlockers.GroupBroken);
                if (paid > 0 && (long)paid + free > policy.MaxSkillGroupRatingCreate) local.Add(CharacterCreationSkillsBlockers.RatingInvalid);
                int cost = 0;
                if (paid > 0 && enabled && GroupUnbroken(group))
                {
                    int upper = group.MemberSkillSourceIds.Where(allowed.Contains).Min(Rating);
                    if (!CharacterCreationSkillCostRules.TryGroup(upper - paid, upper, policy.KarmaNewSkillGroup, policy.KarmaImproveSkillGroup, out cost))
                        local.Add(CharacterCreationSkillsBlockers.AllocationInvalid);
                    cost = Math.Max(0, Round(cost * modifiers.Multiplier("SkillGroupCategoryKarmaCostMultiplier",
                        group.MemberSkillSourceIds.Select(id => activeById[id].Category).ToHashSet(StringComparer.Ordinal))));
                }
                bool broken = group.MemberSkillSourceIds.Where(allowed.Contains).Select(Rating).Distinct().Count() > 1
                    || policy.SpecializationsBreakSkillGroups && group.MemberSkillSourceIds.Where(allowed.Contains).Any(HasSpec);
                groups.Add(new(new(group.GroupId, paid), group.Name, free, GroupLevels(group), cost, enabled, broken, Ordered(local)));
                used += cost;
                blockers.UnionWith(local);
            }
            foreach (var allocation in selected.Values.OrderBy(row => row.Kind, StringComparer.Ordinal)
                .ThenBy(row => row.SourceSkillId, StringComparer.Ordinal).ThenBy(row => row.SpecializationOptionId, StringComparer.Ordinal))
            {
                var source = sources[(allocation.Kind, allocation.SourceSkillId)];
                bool knowledge = source.Kind == CharacterCreationSkillKinds.Knowledge;
                bool enabled = knowledge || allowed.Contains(source.SourceSkillId);
                int free = knowledge ? 0 : Free(source), groupLevels = knowledge ? 0 : EffectiveGroup(source);
                int cap = knowledge ? policy.MaxKnowledgeSkillRatingCreate : policy.MaxActiveSkillRatingCreate;
                int rating = checked(Math.Min(allocation.KnowledgePointLevels, cap)
                    + Math.Min(checked(allocation.KarmaLevels + free + groupLevels), cap));
                var local = new HashSet<string>(StringComparer.Ordinal);
                bool paid = allocation.KarmaLevels > 0 || allocation.KnowledgePointLevels > 0 || allocation.SpecializationOptionId is not null;
                if (paid && !enabled) local.Add(CharacterCreationSkillsBlockers.TalentAccessRequired);
                if (!knowledge && allocation.KnowledgePointLevels != 0) local.Add(CharacterCreationSkillsBlockers.AllocationInvalid);
                if (paid && (long)allocation.KnowledgePointLevels + allocation.KarmaLevels + free + groupLevels > cap)
                    local.Add(CharacterCreationSkillsBlockers.RatingInvalid);
                if (allocation.IsNativeLanguage)
                {
                    natives++;
                    if (!knowledge || !source.CanBeNativeLanguage || rating != 0 || allocation.SpecializationOptionId is not null)
                        local.Add(CharacterCreationSkillsBlockers.NativeLanguageInvalid);
                }
                int points = knowledge ? allocation.KnowledgePointLevels : 0;
                int specPrice = 0;
                if (allocation.SpecializationOptionId is { } specId)
                {
                    var spec = source.Specializations.SingleOrDefault(item => item.OptionId == specId);
                    if (spec is null || spec.Name.Contains('[', StringComparison.Ordinal) || rating <= 0 || !enabled
                        || groupLevels > 0 && policy.StrictSkillGroupsInCreateMode)
                        local.Add(CharacterCreationSkillsBlockers.SpecializationInvalid);
                    switch (allocation.SpecializationPayment)
                    {
                        case CharacterCreationKarmaSpecializationPayments.ExoticIdentity when source.IsExotic: break;
                        case CharacterCreationKarmaSpecializationPayments.Karma when !source.IsExotic:
                            specPrice = knowledge ? policy.KarmaKnowledgeSpecialization : policy.KarmaSpecialization;
                            break;
                        case CharacterCreationKarmaSpecializationPayments.KnowledgePoint when knowledge && !source.IsExotic:
                            if (allocation.KarmaLevels > 0 && allocation.KnowledgePointLevels == 0 && !policy.AllowPointBuySpecializationsOnKarmaSkills)
                                local.Add(CharacterCreationSkillsBlockers.SpecializationInvalid);
                            points = checked(points + 1);
                            break;
                        default: local.Add(CharacterCreationSkillsBlockers.SpecializationInvalid); break;
                    }
                }
                else if (allocation.SpecializationPayment is not null || source.IsExotic)
                    local.Add(CharacterCreationSkillsBlockers.SpecializationInvalid);
                if (groupLevels > 0 && policy.StrictSkillGroupsInCreateMode && allocation.KarmaLevels > 0)
                    local.Add(CharacterCreationSkillsBlockers.GroupBroken);
                decimal specAmount = specPrice * modifiers.Multiplier("SkillCategorySpecializationKarmaCostMultiplier", [source.Category]);
                int cost = 0, specCost = Round(specAmount);
                if (knowledge)
                {
                    if (!CharacterCreationSkillCostRules.TryKnowledge(allocation.KnowledgePointLevels, rating,
                        policy.KarmaNewKnowledgeSkill, policy.KarmaImproveKnowledgeSkill, out int baseCost))
                        local.Add(CharacterCreationSkillsBlockers.AllocationInvalid);
                    // KnowledgeSkill rounds the modified rank and spec sum once.
                    cost = Math.Max(0, Round(baseCost * modifiers.Multiplier("SkillCategoryKarmaCostMultiplier", [source.Category]) + specAmount));
                    points = Math.Max(0, Round(points * modifiers.Multiplier("SkillCategoryPointCostMultiplier", [source.Category])));
                }
                else
                {
                    var group = source.SkillGroup is { } name ? groupsByName.GetValueOrDefault(name) : null;
                    string[] members = group?.MemberSkillSourceIds.ToArray() ?? [];
                    string[] enabledMembers = members.Where(allowed.Contains).ToArray();
                    bool compensate = policy.CompensateSkillGroupKarmaDifference && members.Length > 0
                        && catalog.ActiveSkillSourceOrder.FirstOrDefault(enabledMembers.Contains) == source.SourceSkillId;
                    int? otherMinimum = enabledMembers.Where(id => id != source.SourceSkillId).Select(id => (int?)Rating(id)).Min();
                    int purchasedGroup = group is null || !GroupUnbroken(group) ? 0 : requestedGroups.GetValueOrDefault(group.GroupId);
                    int upper = purchasedGroup > 0 ? enabledMembers.Min(Rating) : 0;
                    // Charge exactly the personal purchase count, taking its top
                    // ranks outside the paid group interval. With unequal free
                    // grants that interval may lie below this skill's free base;
                    // subtracting it from a raw total would recharge module levels.
                    int aboveCount = Math.Min(allocation.KarmaLevels, Math.Max(0, rating - upper));
                    int remaining = allocation.KarmaLevels - aboveCount;
                    int below = remaining == 0 ? 0 : Interval(upper - purchasedGroup - remaining, upper - purchasedGroup);
                    int above = Interval(rating - aboveCount, rating);
                    cost = Math.Max(0, checked(below + above + specCost));
                    int Interval(int lower, int higher)
                    {
                        if (lower >= higher) return 0;
                        if (!CharacterCreationKarmaSkillsRules.TryActiveInterval(lower, higher, policy, compensate,
                            otherMinimum, enabledMembers.Length, out int subtotal)
                            || !CharacterCreationSkillCostRules.TryActive(lower, higher, policy.KarmaNewActiveSkill, policy.KarmaImproveActiveSkill, out int bare))
                        { local.Add(CharacterCreationSkillsBlockers.AllocationInvalid); return 0; }
                        return Math.Max(subtotal - bare, Round(subtotal * modifiers.Multiplier("SkillCategoryKarmaCostMultiplier", [source.Category])));
                    }
                }
                knowledgeUsed = checked(knowledgeUsed + points);
                used += cost;
                blockers.UnionWith(local);
                int moduleGroup = source.SkillGroup is { } groupName ? modifiers.GroupLevels.GetValueOrDefault(groupName) : 0;
                skills.Add(new(allocation, source.Name, source.SourceNodeDigest, free, moduleGroup, groupLevels,
                    allocation.IsNativeLanguage ? null : rating, cost, specCost, points, enabled, Ordered(local), source.SourceAnchorIds));
            }
            if (natives == 0) blockers.Add(CharacterCreationSkillsBlockers.NativeLanguageRequired);
            if (natives > 1) blockers.Add(CharacterCreationSkillsBlockers.NativeLanguageLimitExceeded);
            if (knowledgeUsed > knowledgeTotal) blockers.Add(CharacterCreationSkillsBlockers.KnowledgeBudgetExceeded);
            var quote = new CharacterCreationLifeModuleSkillsQuote(policy, catalog.CatalogDigest,
                effects.PlanDigest, racial.PlanDigest, talent.PlanDigest, attributes.QuoteDigest, frozen,
                allowed.Order(StringComparer.Ordinal).ToArray(), catalog.SkillGroups.Where(GroupEnabled).Select(row => row.GroupId).Order(StringComparer.Ordinal).ToArray(),
                skills.ToArray(), groups.ToArray(), knowledgeTotal, modifiers.KnowledgePoints, knowledgeUsed, natives, used, Ordered(blockers), string.Empty);
            return new(catalog, quote with { QuoteDigest = Digest(quote) }, quote.Blockers);
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException
            or InvalidDataException or KeyNotFoundException or OverflowException or FormatException)
        { return Failed(CharacterCreationSkillsBlockers.AllocationInvalid); }
    }

    private static bool ValidPolicy(CharacterCreationKarmaSkillsPolicy policy) =>
        policy is { Schema: CharacterCreationKarmaSkillsPolicy.LifeModulesSchemaV1, SourceAnchorIds.Count: > 0 }
        && new[] { policy.KarmaNewActiveSkill, policy.KarmaImproveActiveSkill, policy.KarmaNewKnowledgeSkill,
            policy.KarmaImproveKnowledgeSkill, policy.KarmaNewSkillGroup, policy.KarmaImproveSkillGroup,
            policy.KarmaSpecialization, policy.KarmaKnowledgeSpecialization, policy.MaxActiveSkillRatingCreate,
            policy.MaxKnowledgeSkillRatingCreate }.All(value => value >= 0)
        && policy.AuthorityDigest == CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(policy);
    private static string SkillKey(CharacterCreationKarmaSkillAllocation value, bool exotic) => value.Kind + "/" + value.SourceSkillId
        + (exotic ? "/" + value.SpecializationOptionId : string.Empty);
    private static int Round(decimal value) => checked((int)(value >= 0 ? decimal.Ceiling(value) : decimal.Floor(value)));
    private static string[] Ordered(HashSet<string> values) => values.Order(StringComparer.Ordinal).ToArray();
    private static string Digest<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
    private static CharacterCreationLifeModuleSkillsQuoteResult Failed(string blocker) => new(null, null, [blocker]);
}

internal sealed record CharacterCreationLifeModuleSkillsQuoteResult(CharacterCreationSkillsCatalog? Catalog,
    CharacterCreationLifeModuleSkillsQuote? Quote, IReadOnlyList<string> Blockers);
