using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static class CharacterCreationSkillsCatalogAuthority
{
    public static string ComputeDigest(CharacterCreationSkillsCatalog catalog)
        => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            catalog with { CatalogDigest = string.Empty });

    public static bool IsValid(CharacterCreationSkillsCatalog? catalog) => Validate(catalog, requireFullCatalog: true);

    // Retained calculation inputs are not a replacement for current source
    // authority. They may contain no active skills (e.g. native language only).
    internal static bool IsValidSubset(CharacterCreationSkillsCatalog? catalog) => Validate(catalog, requireFullCatalog: false);

    private static bool Validate(CharacterCreationSkillsCatalog? catalog, bool requireFullCatalog)
    {
        if (catalog is not { Schema: CharacterCreationSkillsCatalog.SchemaV1,
                ActiveSkills: not null, KnowledgeSkills: not null, SkillGroups: not null,
                SourceAnchorIds.Count: > 0 }
            || requireFullCatalog && (catalog.ActiveSkills.Count == 0 || catalog.KnowledgeSkills.Count == 0)
            || string.IsNullOrWhiteSpace(catalog.SettingsProfileId)
            || !CharacterCreationSkillsDigest.IsCanonical(catalog.RawProfileInputsDigest)
            || !CharacterCreationSkillsDigest.IsCanonical(catalog.SkillsInputsDigest)
            || !CharacterCreationSkillsDigest.IsCanonical(catalog.WeaponsInputsDigest)) return false;
        if (catalog.ActiveSkillSourceOrder is null
            || !catalog.ActiveSkillSourceOrder.Order(StringComparer.Ordinal).SequenceEqual(
                catalog.ActiveSkills.Where(skill => skill is not null).Select(skill => skill.SourceSkillId)
                    .Order(StringComparer.Ordinal), StringComparer.Ordinal)) return false;
        foreach (var (skills, kind) in new[]
        {
            (catalog.ActiveSkills, CharacterCreationSkillKinds.Active),
            (catalog.KnowledgeSkills, CharacterCreationSkillKinds.Knowledge)
        })
        {
            if (skills.Any(skill => skill is null || skill.Kind != kind
                || !Guid.TryParseExact(skill.SourceSkillId, "D", out var id) || id == Guid.Empty
                || id.ToString("D") != skill.SourceSkillId || string.IsNullOrWhiteSpace(skill.Name)
                || !CharacterCreationStandardPrioritySkillsRules.IsSupportedCategory(kind, skill.Category)
                || !CharacterCreationStandardPrioritySkillsRules.IsSupportedAttribute(skill.DefaultAttribute)
                || skill.Specializations is null || skill.SourceAnchorIds is not { Count: > 0 }
                || skill.Specializations.Any(option => option is null || string.IsNullOrWhiteSpace(option.Name)
                    || string.IsNullOrWhiteSpace(option.OptionId) || string.IsNullOrWhiteSpace(option.SourceAnchorId))
                || skill.Specializations.Select(option => option.OptionId).Distinct(StringComparer.Ordinal).Count() != skill.Specializations.Count
                || skill.Specializations.Select(option => option.Name).Distinct(StringComparer.Ordinal).Count() != skill.Specializations.Count
                || skill.CanBeNativeLanguage != CharacterCreationStandardPrioritySkillsRules.CanBeNativeLanguage(kind, skill.Category)
                || (kind == CharacterCreationSkillKinds.Knowledge && (skill.SkillGroup is not null || skill.IsExotic))
                || (skill.IsExotic && skill.SkillGroup is not null)
                || skill.SourceNodeDigest != CharacterCreationStandardPrioritySkillsRules.ComputeCatalogProjectionDigest(
                    catalog.SkillsInputsDigest, skill.SourceSkillId, skill.Kind, skill.Name, skill.Category,
                    skill.DefaultAttribute, skill.SkillGroup, skill.IsExotic, skill.Specializations, skill.SourceAnchorIds,
                    skill.CanDefault, skill.IgnoresSourceDisabled, skill.RequiresGroundMovement,
                    skill.RequiresSwimMovement, skill.RequiresFlyMovement, skill.CanBeNativeLanguage))
                || skills.Select(skill => skill.SourceSkillId).Distinct(StringComparer.Ordinal).Count() != skills.Count
                || skills.Select(skill => skill.Name).Distinct(StringComparer.Ordinal).Count() != skills.Count)
                return false;
        }
        var expectedGroups = catalog.ActiveSkills.Where(skill => !string.IsNullOrWhiteSpace(skill.SkillGroup))
            .GroupBy(skill => skill.SkillGroup!, StringComparer.Ordinal).ToArray();
        if (catalog.SkillGroups.Count != expectedGroups.Length
            || catalog.SkillGroups.Any(group => group is null)
            || catalog.SkillGroups.Select(group => group.Name).Distinct(StringComparer.Ordinal).Count() != expectedGroups.Length)
            return false;
        foreach (var expected in expectedGroups)
        {
            var group = catalog.SkillGroups.SingleOrDefault(item => item.Name == expected.Key);
            string[] members = expected.Select(item => item.SourceSkillId).Order(StringComparer.Ordinal).ToArray();
            string digest = CharacterCreationSkillsDigest.Compute(new
            {
                Schema = "chummer.sr5.creation-skill-group-source.v1",
                Name = expected.Key,
                MemberSkillSourceIds = members,
                EffectiveSkillsInputsDigest = catalog.SkillsInputsDigest
            });
            if (group is null || members.Length < 2 || group.GroupId != digest || group.GroupDigest != digest
                || group.MemberSkillSourceIds is null || !group.MemberSkillSourceIds.SequenceEqual(members, StringComparer.Ordinal)
                || group.SourceAnchorIds is not { Count: > 0 }) return false;
        }
        return catalog.CatalogDigest == ComputeDigest(catalog);
    }
}
