using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Core-owned skill availability. The catalog remains complete; the
/// selected source quality's unlockskills effects determine purchasable rows.
/// Legacy oracle: SkillsSection.SkillFilter, not Talent display names.</summary>
public static class CharacterCreationSkillsAccessRules
{
    public static bool IsSkillAvailable(CharacterCreationSkillsAuthority authority, string sourceId)
    {
        var skill = authority.ActiveSkills.SingleOrDefault(item => item.SourceSkillId == sourceId);
        return skill is not null && (authority.TalentAccess is { } access
            ? access.AllowedActiveSkillSourceIds.Contains(sourceId, StringComparer.Ordinal)
            : IsOrdinary(skill));
    }

    public static bool IsGroupAvailable(CharacterCreationSkillsAuthority authority, string groupId)
    {
        var group = authority.SkillGroups.SingleOrDefault(item => item.GroupId == groupId);
        return group is not null && (authority.TalentAccess is { } access
            ? access.AllowedSkillGroupIds.Contains(groupId, StringComparer.Ordinal)
            : group.MemberSkillSourceIds.Any(id => IsSkillAvailable(authority, id)));
    }

    internal static bool TryBind(CharacterCreationPrerequisiteDraft prerequisite,
        ICharacterSourceDataContext context, CharacterCreationSkillsAuthority original,
        out CharacterCreationSkillsAuthority authority)
    {
        authority = original;
        if (original.TalentAccess is not null
            || prerequisite.TalentSelection?.GrantedQualities is not { } references) return false;
        if (references.Count == 0) return true;
        if (!CharacterCreationTalentQualityGrants.TryResolveTalent(prerequisite, context,
                out var magic, out var talent)) return false;
        string[] groups = SelectedGroups(prerequisite);
        if (!TryProject(original, talent, groups, out var activeIds, out var groupIds)) return false;
        var access = new CharacterCreationTalentSkillAccess(CharacterCreationSkillsSchemas.TalentAccessV1,
            prerequisite.DraftDigest, magic.RuntimeDigest,
            talent, groups, activeIds, groupIds, string.Empty);
        access = access with { AccessDigest = Digest(access) };
        authority = original with
        {
            TalentAccess = access,
            SourceAnchorIds = original.SourceAnchorIds.Concat(talent.SourceAnchorIds)
                .Concat(talent.GrantedQualitySources!.SelectMany(item => item.SourceAnchorIds))
                .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray(),
            AuthorityDigest = string.Empty
        };
        authority = authority with
        {
            AuthorityDigest = CharacterCreationSkillsDigest.Compute(authority with { AuthorityDigest = string.Empty })
        };
        return true;
    }

    internal static bool IsValid(CharacterCreationSkillsAuthority authority)
    {
        if (authority.TalentAccess is not { } access) return true;
        return access.Schema == CharacterCreationSkillsSchemas.TalentAccessV1
            && CharacterCreationSkillsDigest.IsCanonical(access.PrerequisiteDraftDigest)
            && CharacterCreationSkillsDigest.IsCanonical(access.TalentRuntimeDigest)
            && access.AllowedActiveSkillSourceIds is not null && access.AllowedSkillGroupIds is not null
            && TryProject(authority, access.Talent, access.SelectedGroupNames, out var skills, out var groups)
            && skills.SequenceEqual(access.AllowedActiveSkillSourceIds, StringComparer.Ordinal)
            && groups.SequenceEqual(access.AllowedSkillGroupIds, StringComparer.Ordinal)
            && access.Talent.SourceAnchorIds.Concat(access.Talent.GrantedQualitySources!
                    .SelectMany(item => item.SourceAnchorIds)).All(authority.SourceAnchorIds.Contains)
            && CharacterCreationSkillsDigest.EqualsFixedTime(access.AccessDigest, Digest(access));
    }

    internal static bool MatchesPrerequisite(CharacterCreationSkillsAuthority authority,
        CharacterCreationPrerequisiteDraft? prerequisite)
    {
        if (authority.TalentAccess is not { } access)
            return prerequisite?.TalentSelection?.GrantedQualities is not { Count: > 0 };
        if (prerequisite?.TalentSelection is not { GrantedQualities: not null } selected
            || !IsValid(authority)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(access.PrerequisiteDraftDigest, prerequisite.DraftDigest)
            || !SelectedGroups(prerequisite).SequenceEqual(access.SelectedGroupNames, StringComparer.Ordinal)) return false;
        var assignments = prerequisite.Assignments.Where(item =>
            item.CategoryId == CharacterCreationPriorityCategoryIds.Talent).Take(2).ToArray();
        return assignments.Length == 1
            && access.Talent.Identity.PrioritySourceId == assignments[0].SourceId
            && access.Talent.Rank == assignments[0].Rank
            && access.Talent.Identity.TalentSelectionId == selected.SelectionId
            && access.Talent.Identity.TalentValue == selected.Value
            && CharacterCreationSkillsDigest.EqualsFixedTime(access.Talent.SourceNodeDigest, selected.PriorityChildNodeDigest)
            && selected.GrantedQualities.SequenceEqual(access.Talent.GrantedQualitySources!
                .Select(item => item.Reference), StringComparer.Ordinal);
    }

    internal static string Digest(CharacterCreationTalentSkillAccess access) =>
        CharacterCreationSkillsDigest.Compute(access with { AccessDigest = string.Empty });

    private static string[] SelectedGroups(CharacterCreationPrerequisiteDraft prerequisite) =>
        (prerequisite.TalentSelection?.GrantPlan?.SkillGroups ?? [])
            .Select(item => item.CanonicalName).OrderBy(item => item, StringComparer.Ordinal).ToArray();

    private static bool TryProject(CharacterCreationSkillsAuthority authority,
        CharacterCreationMagicResonanceTalentOption? talent, IReadOnlyList<string>? selectedGroups,
        out string[] activeIds, out string[] groupIds)
    {
        activeIds = []; groupIds = [];
        if (talent?.Identity is null || talent.SourceAnchorIds is null || selectedGroups is null
            || selectedGroups.Count > CharacterCreationTalentSkillGrantTypes.MaximumPromptSlots
            || selectedGroups.Any(string.IsNullOrWhiteSpace)
            || !selectedGroups.SequenceEqual(selectedGroups.Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal), StringComparer.Ordinal)
            || !talent.IsEnabled || !CharacterCreationMagicResonanceDraftIntegrity.IsValidTalent(talent)
            || !CharacterCreationTalentQualitySourceRules.MatchesTalent(talent.CanonicalSourceXml, talent.GrantedQualitySources))
            return false;
        var filters = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var source in talent.GrantedQualitySources!)
            {
                var definition = XElement.Parse(source.CanonicalSourceXml);
                var bonuses = definition.Elements("bonus").Take(2).ToArray();
                if (bonuses.Length > 1) return false;
                if (bonuses.Length == 0) continue;
                foreach (var effect in bonuses[0].Elements("unlockskills"))
                {
                    if (!TryChooseUnlock(effect, selectedGroups, source.ForcedSelection, out string choice)) return false;
                    filters.Add(choice);
                }
            }
        }
        catch (XmlException) { return false; }
        activeIds = authority.ActiveSkills.Where(skill => IsOrdinary(skill)
                || filters.Any(filter => Matches(skill, filter)))
            .Select(item => item.SourceSkillId).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        var allowed = activeIds.ToHashSet(StringComparer.Ordinal);
        // A group exists for the runner when at least one of its catalog members
        // is available. Canonical membership is retained; disabled members do
        // not break it (the same rule as movement-disabled skills).
        groupIds = authority.SkillGroups.Where(group => group.MemberSkillSourceIds.Any(allowed.Contains))
            .Select(item => item.GroupId).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        return true;
    }

    internal static bool TryChooseUnlock(XElement effect, IReadOnlyList<string> selectedGroups,
        string forced, out string choice)
    {
        choice = string.Empty;
        if (effect.HasAttributes || effect.HasElements) return false;
        string[] choices = effect.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (choices.Length == 0 || choices.Distinct(StringComparer.Ordinal).Count() != choices.Length
            || choices.Any(item => item is not ("Magician" or "Adept" or "Aware" or "Explorer"
                or "Technomancer" or "Sorcery" or "Conjuring" or "Enchanting" or "Spellcasting"))) return false;
        choice = choices.Length == 1 ? choices[0] : selectedGroups.Count == 1 ? selectedGroups[0] : string.Empty;
        return choices.Contains(choice, StringComparer.Ordinal) && (forced.Length == 0 || forced == choice);
    }

    private static bool IsOrdinary(CharacterCreationSkillCatalogEntry skill) =>
        skill.Category is not ("Magical Active" or "Resonance Active");

    private static bool Matches(CharacterCreationSkillCatalogEntry skill, string filter) => filter switch
    {
        "Magician" => skill.Category == "Magical Active",
        "Technomancer" => skill.Category == "Resonance Active",
        "Sorcery" or "Conjuring" or "Enchanting" => skill.Category == "Magical Active"
            && (string.IsNullOrEmpty(skill.SkillGroup) || skill.SkillGroup == filter),
        "Adept" or "Aware" or "Explorer" => skill.Category == "Magical Active" && string.IsNullOrEmpty(skill.SkillGroup),
        "Spellcasting" => skill.Category == "Magical Active" && skill.Name == "Spellcasting",
        _ => false
    };
}
