using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Joins the independently validated Priority talent plan to the current Skills
/// catalog. Grants are free base ratings, not more spendable Priority points.
/// This join does not authorize a prerequisite draft or mutate a character.
/// </summary>
internal sealed record CharacterCreationTalentSkillGrants(
    IReadOnlyDictionary<string, CharacterCreationTalentActiveSkillGrantPlanEntry> Skills,
    IReadOnlyDictionary<string, CharacterCreationTalentSkillGroupGrantPlanEntry> Groups)
{
    internal static bool TryResolve(CharacterCreationPrerequisiteDraft? prerequisite,
        CharacterCreationSkillsAuthority authority, out CharacterCreationTalentSkillGrants grants)
        => TryResolve(prerequisite, authority.ActiveSkills, authority.SkillGroups,
            authority.EffectiveSkillsInputsDigest, authority.MaxActiveSkillRatingCreate,
            authority.MaxSkillGroupRatingCreate, out grants);

    internal static bool TryResolveCatalog3563(CharacterCreationPrerequisiteDraft prerequisite,
        CharacterCreationSkillsCatalog catalog, int maximumRating, out CharacterCreationTalentSkillGrants grants)
        => TryResolve(prerequisite, catalog.ActiveSkills, catalog.SkillGroups,
            catalog.SkillsInputsDigest, maximumRating, maximumRating, out grants);

    private static bool TryResolve(CharacterCreationPrerequisiteDraft? prerequisite,
        IReadOnlyList<CharacterCreationSkillCatalogEntry> activeSkills,
        IReadOnlyList<CharacterCreationSkillGroupCatalogEntry> skillGroups,
        string skillsDigest, int maximumActiveRating, int maximumGroupRating,
        out CharacterCreationTalentSkillGrants grants)
    {
        grants = new(new Dictionary<string, CharacterCreationTalentActiveSkillGrantPlanEntry>(StringComparer.Ordinal),
            new Dictionary<string, CharacterCreationTalentSkillGroupGrantPlanEntry>(StringComparer.Ordinal));
        if (prerequisite?.TalentSelection?.GrantPlan is not { } plan) return true;
        return TryResolvePlan5693(plan, activeSkills, skillGroups, skillsDigest,
            maximumActiveRating, maximumGroupRating, out grants, prerequisite);
    }

    // The caller must obtain this plan from the existing source-backed planner.
    // A public/serialized plan is never an imported selection capability.
    internal static bool TryResolveImportedPlan5693(CharacterCreationTalentGrantPlanContribution? plan,
        CharacterCreationSkillsCatalog catalog, int maximumRating, out CharacterCreationTalentSkillGrants grants)
        => TryResolvePlan5693(plan, catalog.ActiveSkills, catalog.SkillGroups, catalog.SkillsInputsDigest,
            maximumRating, maximumRating, out grants);

    private static bool TryResolvePlan5693(CharacterCreationTalentGrantPlanContribution? plan,
        IReadOnlyList<CharacterCreationSkillCatalogEntry> activeSkills,
        IReadOnlyList<CharacterCreationSkillGroupCatalogEntry> skillGroups,
        string skillsDigest, int maximumActiveRating, int maximumGroupRating,
        out CharacterCreationTalentSkillGrants grants, CharacterCreationPrerequisiteDraft? prerequisite = null)
    {
        var skills = new Dictionary<string, CharacterCreationTalentActiveSkillGrantPlanEntry>(StringComparer.Ordinal);
        var groups = new Dictionary<string, CharacterCreationTalentSkillGroupGrantPlanEntry>(StringComparer.Ordinal);
        grants = new(skills, groups);
        if (plan is null)
            return true;
        if (plan.Schema != CharacterCreationPrerequisiteSchemas.TalentGrantPlanV1
            || plan.ActiveSkills is null || plan.SkillGroups is null || plan.SourceAnchorIds is not { Count: > 0 }
            || plan.ActiveSkills.Count + plan.SkillGroups.Count is < 1 or > CharacterCreationTalentSkillGrantTypes.MaximumPromptSlots
            || prerequisite is not null && !CharacterCreationSkillsDigest.EqualsFixedTime(prerequisite.DraftDigest,
                CharacterCreationPrerequisiteDraftIntegrity.ComputeDigest(prerequisite))
            || plan.ActiveSkills.Count != 0 && plan.SkillGroups.Count != 0
            || !CharacterCreationSkillsDigest.EqualsFixedTime(plan.PlanDigest,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(plan with { PlanDigest = string.Empty })))
            return false;
        foreach (var grant in plan.ActiveSkills)
        {
            if (grant is null) return false;
            var matches = activeSkills.Where(item => item.SourceSkillId == grant.SourceId).Take(2).ToArray();
            if (matches.Length != 1 || grant.TargetKind != "active-skill"
                || grant.ImprovementKind != CharacterCreationTalentGrantImprovementKinds.SkillBase
                || grant.BaseRating < 1 || grant.BaseRating > maximumActiveRating
                || grant.CanonicalName != matches[0].Name || grant.Category != matches[0].Category
                || grant.SkillGroup != matches[0].SkillGroup || matches[0].IsExotic
                || grant.SourceAnchorIds is not { Count: > 0 }
                || !CharacterCreationSkillsDigest.EqualsFixedTime(grant.SkillsSourceDigest, skillsDigest)
                || !skills.TryAdd(grant.SourceId, grant))
                return false;
        }
        var selectedGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var grant in plan.SkillGroups)
        {
            if (grant is null) return false;
            var matches = skillGroups.Where(item => item.Name == grant.CanonicalName).Take(2).ToArray();
            if (matches.Length != 1 || grant.TargetKind != "skill-group"
                || grant.ImprovementKind != CharacterCreationTalentGrantImprovementKinds.SkillGroupBase
                || grant.BaseRating < 0 || grant.BaseRating > maximumGroupRating
                || grant.MemberSkillSourceIds is not { Count: > 0 }
                || grant.SourceAnchorIds is not { Count: > 0 }
                || !grant.MemberSkillSourceIds.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(
                    matches[0].MemberSkillSourceIds.OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal)
                || !CharacterCreationSkillsDigest.EqualsFixedTime(grant.SkillsSourceDigest, skillsDigest)
                || !selectedGroups.Add(matches[0].GroupId))
                return false;
            // Zero-rated entries remain in the validated Priority choice plan,
            // but do not create skill rows or consume/grant any skill points.
            if (grant.BaseRating > 0)
                groups.Add(matches[0].GroupId, grant);
        }
        return true;
    }

    internal bool IsValidProjection(IReadOnlyList<CharacterCreationSkillProjection> skills,
        IReadOnlyList<CharacterCreationSkillGroupProjection> groups)
    {
        if (skills.Any(item => item is null || item.SourceAnchorIds is null)
            || groups.Any(item => item is null || item.SourceAnchorIds is null))
            return false;
        if (Skills.Any(grant => skills.Count(item => item.Kind == CharacterCreationSkillKinds.Active
                && item.SourceSkillId == grant.Key) != 1)
            || Groups.Any(grant => groups.Count(item => item.GroupId == grant.Key) != 1))
            return false;
        foreach (var skill in skills)
        {
            var grant = skill.Kind == CharacterCreationSkillKinds.Active ? Skills.GetValueOrDefault(skill.SourceSkillId) : null;
            if (skill.GrantedRating != (grant?.BaseRating ?? 0)
                || grant is not null && (skill.IsNativeLanguage || skill.Rating < grant.BaseRating
                    || skill.EffectiveRating != skill.Rating
                    || skill.PointCost != skill.Rating - grant.BaseRating + (skill.SpecializationName is null ? 0 : 1)
                    || !grant.SourceAnchorIds.All(skill.SourceAnchorIds.Contains)))
                return false;
        }
        foreach (var group in groups)
        {
            var grant = Groups.GetValueOrDefault(group.GroupId);
            if (group.GrantedRating != (grant?.BaseRating ?? 0)
                || grant is not null && (group.Rating < grant.BaseRating
                    || group.PointCost != group.Rating - grant.BaseRating
                    || !grant.SourceAnchorIds.All(group.SourceAnchorIds.Contains)))
                return false;
        }
        return true;
    }

    internal CharacterCreationSkillProjection[] InitialSkills(CharacterCreationSkillsAuthority authority) =>
        Skills.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
        {
            var source = authority.ActiveSkills.Single(item => item.SourceSkillId == pair.Key);
            return new CharacterCreationSkillProjection(source.SourceSkillId, source.Kind, source.Name,
                source.Category, source.DefaultAttribute, source.SkillGroup, pair.Value.BaseRating,
                pair.Value.BaseRating, 0, null, null, false, true, [],
                Anchors(source.SourceAnchorIds, pair.Value.SourceAnchorIds)) { GrantedRating = pair.Value.BaseRating };
        }).ToArray();

    internal CharacterCreationSkillGroupProjection[] InitialGroups(CharacterCreationSkillsAuthority authority) =>
        Groups.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
        {
            var source = authority.SkillGroups.Single(item => item.GroupId == pair.Key);
            return new CharacterCreationSkillGroupProjection(source.GroupId, source.Name, pair.Value.BaseRating,
                0, source.MemberSkillSourceIds, true, [], Anchors(source.SourceAnchorIds, pair.Value.SourceAnchorIds))
                { GrantedRating = pair.Value.BaseRating };
        }).ToArray();

    internal static string[] Anchors(IReadOnlyList<string> source, IReadOnlyList<string>? grant) =>
        source.Concat(grant ?? []).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
}
