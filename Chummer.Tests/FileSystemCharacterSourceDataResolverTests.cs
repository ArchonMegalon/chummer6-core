#nullable enable annotations

using System;
using System.IO;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class FileSystemCharacterSourceDataResolverTests
{
    private const string SettingsId = "223a11ff-80e0-428b-89a9-6ef1c243b8b6";
    private const string CanonicalLifeModuleSettingsId = "8a31af6d-7137-4284-872b-7d8087e156c6";
    private const string CanonicalSumToTenSettingsId = "3509a807-68ee-4c18-b7d5-b130313b4b77";
    private const string CanonicalImprovedSumToTenSettingsId = "2ef9b098-4cd2-4c2b-8f3d-76164e3f4f8e";
    private const string CanonicalStreetScumSettingsId = "4c34a8ed-2888-410c-afda-024475fa3c76";
    private const string CanonicalPrioritiesDigest =
        "sha256:4b41936b90fdd84a00b060585542eed8eb4d2045eeda1940c1c8a95af3eb91d1";
    private const string CanonicalMetatypesDigest =
        "sha256:ccee5dfabb8d0e193aa980e9905822a0f94fb9bb8093c162f5b694a974946425";
    private const string VehicleModId = "f89a112e-600a-4278-8731-9b14cf3737c9";

    [TestMethod]
    public void Canonical_priority_profile_projects_digest_bound_rank_and_creation_karma_authority()
    {
        string coreRoot = FindCoreRoot();
        ICharacterSourceDataContext context = CreateContext(
            coreRoot,
            $"<character><settings>{SettingsId}</settings></character>")!;

        Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority authority));
        Assert.IsTrue(authority.IsAuthoritative, string.Join(",", authority.Blockers));
        Assert.AreEqual(CharacterCreationBuildMethods.Priority, authority.BuildMethod);
        Assert.AreEqual(25, authority.CreationKarmaTotal);
        CollectionAssert.AreEqual(
            new[] { "A", "B", "C", "D", "E" },
            authority.PriorityArray.ToArray());
        Assert.AreEqual("Standard", authority.PriorityTable);
        Assert.AreEqual(10, authority.SumToTenTarget);
        Assert.AreEqual(CanonicalPrioritiesDigest, authority.RawPrioritiesXmlDigest);
        Assert.AreEqual(CanonicalMetatypesDigest, authority.RawMetatypesXmlDigest);
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            authority.SelectedCustomDataInputsDigest));
        Assert.AreEqual(1, authority.MaxNumberMaxAttributesCreate);
        Assert.AreEqual(5, authority.KarmaAttribute);
        Assert.IsFalse(authority.AlternateMetatypeAttributeKarma);
        Assert.IsFalse(authority.ReverseAttributePriorityOrder);
        Assert.HasCount(25, authority.Options);
        CharacterCreationPriorityOptionProjection attributesA = authority.Options.Single(option =>
            option.CategoryId == CharacterCreationPriorityCategoryIds.Attributes
            && option.Rank == "A");
        Assert.AreEqual(24, attributesA.BaseNormalAttributePoints);
        CharacterCreationPriorityOptionProjection heritageE = authority.Options.Single(option =>
            option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
            && option.Rank == "E");
        CharacterCreationPriorityHeritageOptionProjection human = heritageE.HeritageOptions.Single(option =>
            option.MetatypeName == "Human" && option.MetavariantName is null);
        Assert.IsTrue(human.IsEnabled, string.Join(",", human.Blockers));
        Assert.AreEqual(1, human.SpecialAttributePoints);
        Assert.IsFalse(human.HalvesNormalAttributePoints);
        Assert.HasCount(13, human.Attributes);
        CharacterCreationPriorityOptionProjection talentE = authority.Options.Single(option =>
            option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
            && option.Rank == "E");
        Assert.IsTrue(talentE.TalentOptions.Single(option => option.Value == "Mundane").IsEnabled);
        Assert.IsFalse(talentE.TalentOptions.Single(option => option.Value == "A.I.").IsEnabled);
        CharacterCreationPriorityHeritageOptionProjection halved = authority.Options
            .Where(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage)
            .SelectMany(option => option.HeritageOptions)
            .First(option => option.HalvesNormalAttributePoints);
        Assert.IsFalse(halved.IsEnabled);
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            halved.MetatypeSourceNodeDigest));
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
            authority.AuthorityDigest,
            CharacterCreationPrerequisiteAuthorityDigest.Compute(authority)));

        ICharacterSourceDataContext duplicateRanks = CreateContext(
            coreRoot,
            $"<character><settings>{CanonicalStreetScumSettingsId}</settings></character>")!;
        Assert.IsTrue(duplicateRanks.TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority streetScum));
        Assert.IsTrue(streetScum.IsAuthoritative, string.Join(",", streetScum.Blockers));
        CollectionAssert.AreEqual(
            new[] { "B", "C", "D", "E", "E" },
            streetScum.PriorityArray.ToArray());
        Assert.HasCount(20, streetScum.Options);
    }

    [TestMethod]
    public void Canonical_talent_grants_project_exact_active_skill_and_group_choice_authority()
    {
        string coreRoot = FindCoreRoot();
        ICharacterSourceDataContext context = CreateContext(
            coreRoot,
            $"<character><settings>{SettingsId}</settings></character>")!;

        Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority authority));
        Assert.IsTrue(authority.IsAuthoritative, string.Join(",", authority.Blockers));
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            authority.RawSkillsXmlDigest));
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            authority.EffectiveSkillsInputsDigest));

        CharacterCreationPriorityTalentOptionProjection magician = authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && option.Rank == "A")
            .TalentOptions.Single(option => option.Value == "Magician");
        CharacterCreationTalentActiveSkillGrantProjection magicGrant = magician.ActiveSkillGrant!;
        Assert.AreEqual(2, magicGrant.Quantity);
        Assert.AreEqual(5, magicGrant.BaseRating);
        Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Magic, magicGrant.SkillType);
        Assert.IsTrue(magicGrant.IsSupported, string.Join(",", magicGrant.Blockers));
        Assert.AreNotEqual(0, magicGrant.Options.Count);
        Assert.IsTrue(magicGrant.Options.All(option => option.Category is
            "Magical Active" or "Pseudo-Magical Active"));
        Assert.IsTrue(magicGrant.Options.Any(option => option.Category == "Pseudo-Magical Active"));
        Assert.IsTrue(magicGrant.Options.All(option => Guid.TryParseExact(
            option.SourceId,
            "D",
            out _)));
        Assert.IsTrue(magicGrant.Options.All(option =>
            CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                option.SkillsSourceDigest,
                authority.EffectiveSkillsInputsDigest)));

        CharacterCreationPriorityTalentOptionProjection technomancer = authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && option.Rank == "A")
            .TalentOptions.Single(option => option.Value == "Technomancer");
        CharacterCreationTalentActiveSkillGrantProjection resonanceGrant =
            technomancer.ActiveSkillGrant!;
        Assert.IsTrue(resonanceGrant.Options.All(option => option.Category == "Resonance Active"
            || option.SkillGroup is "Cracking" or "Electronics"));
        Assert.IsTrue(resonanceGrant.Options.Any(option => option.Category != "Resonance Active"
            && (option.SkillGroup is "Cracking" or "Electronics")));
        Assert.AreEqual(3, resonanceGrant.Quantity);

        CharacterCreationPriorityTalentOptionProjection artificialIntelligence = authority.Options
            .Single(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                              && option.Rank == "B")
            .TalentOptions.Single(option => option.Value == "A.I.");
        CharacterCreationTalentActiveSkillGrantProjection matrixGrant =
            artificialIntelligence.ActiveSkillGrant!;
        Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Matrix, matrixGrant.SkillType);
        Assert.IsTrue(matrixGrant.IsSupported, string.Join(",", matrixGrant.Blockers));
        Assert.IsTrue(matrixGrant.Options.Count > 0);
        Assert.IsTrue(matrixGrant.Options.All(option => option.SkillGroup is
            "Cracking" or "Electronics"));

        CharacterCreationPriorityTalentOptionProjection adept = authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && option.Rank == "B")
            .TalentOptions.Single(option => option.Value == "Adept");
        CharacterCreationTalentActiveSkillChoiceProjection[] exoticOptions = adept.ActiveSkillGrant!
            .Options.Where(option => option.IsExotic).ToArray();
        Assert.IsTrue(exoticOptions.Length > 0);
        Assert.IsTrue(exoticOptions.All(option => !option.IsEnabled
            && option.Blockers.SequenceEqual(
                [CharacterCreationPrerequisiteBlockers
                    .TalentExoticSkillSpecializationRequired],
                StringComparer.Ordinal)));

        CharacterCreationPriorityTalentOptionProjection explorer = authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && option.Rank == "C")
            .TalentOptions.Single(option => option.Value == "Explorer");
        CharacterCreationTalentActiveSkillGrantProjection specificGrant =
            explorer.ActiveSkillGrant!;
        Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Specific, specificGrant.SkillType);
        Assert.IsTrue(specificGrant.IsSupported, string.Join(",", specificGrant.Blockers));
        CollectionAssert.AreEqual(
            new[] { "Arcana", "Assensing", "Astral Combat" },
            specificGrant.SpecificSkillChoiceNames.ToArray());
        CollectionAssert.AreEqual(
            specificGrant.SpecificSkillChoiceNames.ToArray(),
            specificGrant.Options.Select(option => option.CanonicalName).ToArray());

        CharacterCreationPriorityTalentOptionProjection adeptXPath = authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && option.Rank == "C")
            .TalentOptions.Single(option => option.Value == "Adept");
        CharacterCreationTalentActiveSkillGrantProjection xpathGrant =
            adeptXPath.ActiveSkillGrant!;
        Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.XPath, xpathGrant.SkillType);
        Assert.AreEqual(
            CharacterCreationTalentSkillGrantTypes.PinnedXPathPredicate,
            xpathGrant.SkillTypeQuery);
        Assert.IsTrue(xpathGrant.IsSupported, string.Join(",", xpathGrant.Blockers));
        Assert.IsTrue(xpathGrant.Options.Count > 0);
        Assert.IsTrue(xpathGrant.Options.All(option => option.Attribute is not ("RES" or "DEP")
            && (option.Category != "Magical Active" || string.IsNullOrEmpty(option.SkillGroup))));

        CharacterCreationPriorityTalentOptionProjection aspected = authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && option.Rank == "B")
            .TalentOptions.Single(option => option.Value == "Aspected Magician");
        CharacterCreationTalentSkillGroupGrantProjection groupGrant = aspected.SkillGroupGrant!;
        Assert.AreEqual(1, groupGrant.Quantity);
        Assert.AreEqual(4, groupGrant.BaseRating);
        Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Grouped, groupGrant.SkillGroupType);
        Assert.AreEqual(string.Empty, groupGrant.CompatibilityMarker);
        Assert.IsTrue(groupGrant.IsSupported, string.Join(",", groupGrant.Blockers));
        CollectionAssert.AreEqual(
            new[] { "Conjuring", "Enchanting", "Sorcery" },
            groupGrant.Options.Select(option => option.CanonicalName).ToArray());
        CollectionAssert.AreEqual(
            new[] { "Conjuring", "Enchanting", "Sorcery" },
            groupGrant.RequestedGroupNames.ToArray());
        Assert.IsTrue(groupGrant.Options.All(option =>
            option.SelectionId.StartsWith("skill-group:", StringComparison.Ordinal)
            && option.MemberSkillSourceIds.Count > 0
            && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(option.GroupDigest)));

        CharacterCreationPriorityTalentOptionProjection aspectedD = authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && option.Rank == "D")
            .TalentOptions.Single(option => option.Value == "Aspected Magician");
        Assert.AreEqual(0, aspectedD.SkillGroupGrant!.BaseRating);
        Assert.IsFalse(aspectedD.IsEnabled,
            "Grant authority must not make the remaining unsupported Talent ledgers writable.");
    }

    [TestMethod]
    public void Canonical_talent_corpus_projects_every_child_with_exact_branch_and_option_order()
    {
        string coreRoot = FindCoreRoot();
        ICharacterSourceDataContext context = CreateContext(
            coreRoot,
            $"<character><settings>{SettingsId}</settings></character>")!;
        Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority authority));
        Assert.IsTrue(authority.IsAuthoritative, string.Join(",", authority.Blockers));

        XDocument priorities = XDocument.Load(Path.Combine(coreRoot, "Chummer", "data",
            "priorities.xml"));
        XDocument skillsDocument = XDocument.Load(Path.Combine(coreRoot, "Chummer", "data",
            "skills.xml"));
        (string Id, string Name, string Attribute, string Category, string? Group, bool Exotic)[]
            skills = skillsDocument.Root!.Element("skills")!.Elements("skill")
                .Select(skill =>
                {
                    string[] names = skill.Elements("name").Select(node => node.Value)
                        .Distinct(StringComparer.Ordinal).ToArray();
                    Assert.HasCount(1, names);
                    string? group = skill.Element("skillgroup")?.Value;
                    if (string.IsNullOrEmpty(group))
                        group = null;
                    return (
                        Id: skill.Element("id")!.Value,
                        Name: names[0],
                        Attribute: skill.Element("attribute")?.Value ?? string.Empty,
                        Category: skill.Element("category")!.Value,
                        Group: group,
                        Exotic: bool.TryParse(skill.Element("exotic")?.Value, out bool exotic)
                                && exotic);
                })
                .ToArray();
        (string Name, string[] Members)[] groups = skillsDocument.Root!.Element("skillgroups")!
            .Elements("name")
            .Select(node => (
                Name: node.Value,
                Members: skills.Where(skill => string.Equals(
                        skill.Group,
                        node.Value,
                        StringComparison.Ordinal))
                    .Select(skill => skill.Id)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray()))
            .Where(group => group.Members.Length > 0)
            .OrderBy(group => group.Name, StringComparer.Ordinal)
            .ToArray();

        XElement[] rawRows = priorities.Root!.Element("priorities")!.Elements("priority")
            .Where(row => string.Equals(
                row.Element("category")?.Value,
                "Talent",
                StringComparison.Ordinal))
            .ToArray();
        CharacterCreationPriorityOptionProjection[] projectedRows = authority.Options
            .Where(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Talent)
            .ToArray();
        Assert.AreEqual(rawRows.Length, projectedRows.Length,
            "Every current-corpus Talent priority row must be represented.");

        foreach (XElement rawRow in rawRows)
        {
            string sourceId = rawRow.Element("id")!.Value;
            CharacterCreationPriorityOptionProjection projectedRow = projectedRows.Single(option =>
                option.SourceId == sourceId);
            XElement[] rawTalents = rawRow.Element("talents")!.Elements("talent").ToArray();
            Assert.AreEqual(rawTalents.Length, projectedRow.TalentOptions.Count);
            for (int index = 0; index < rawTalents.Length; index++)
            {
                XElement rawTalent = rawTalents[index];
                CharacterCreationPriorityTalentOptionProjection projected =
                    projectedRow.TalentOptions[index];
                Assert.AreEqual(rawTalent.Element("name")!.Value, projected.Name);
                Assert.AreEqual(rawTalent.Element("value")!.Value, projected.Value);

                XElement? quantityNode = rawTalent.Element("skillqty")
                                         ?? rawTalent.Element("skillgroupqty");
                bool hasPrompt = int.TryParse(quantityNode?.Value, out int sourceQuantity)
                                 && sourceQuantity > 0;
                if (!hasPrompt)
                {
                    Assert.IsNull(projected.ActiveSkillGrant);
                    Assert.IsNull(projected.SkillGroupGrant);
                    continue;
                }
                XElement? typeNode = rawTalent.Element("skilltype")
                                     ?? rawTalent.Element("skillgrouptype");
                string rawSkillType = typeNode?.Value ?? string.Empty;
                string skillType = CharacterCreationTalentSkillGrantTypes
                    .NormalizeLegacySelectorType(rawSkillType);
                string selectorTypeSource = rawTalent.Element("skilltype") is not null
                    ? CharacterCreationTalentGrantSelectorTypeSources.SkillType
                    : rawTalent.Element("skillgrouptype") is not null
                        ? CharacterCreationTalentGrantSelectorTypeSources.SkillGroupType
                        : CharacterCreationTalentGrantSelectorTypeSources.Missing;
                string query = typeNode?.Attribute("xpath")?.Value ?? string.Empty;
                bool groupPicker = string.IsNullOrEmpty(query)
                                   && (skillType is
                                       CharacterCreationTalentSkillGrantTypes.Grouped
                                       or CharacterCreationTalentSkillGrantTypes.Choices);
                bool usesSkillValue = int.TryParse(
                                          rawTalent.Element("skillval")?.Value,
                                          out int effectiveRating)
                                      && effectiveRating >= 0;
                if (!usesSkillValue)
                {
                    Assert.IsTrue(int.TryParse(
                        rawTalent.Element("skillgroupval")?.Value,
                        out effectiveRating));
                    Assert.IsTrue(effectiveRating >= 0);
                }
                string improvementKind = usesSkillValue
                    ? CharacterCreationTalentGrantImprovementKinds.SkillBase
                    : CharacterCreationTalentGrantImprovementKinds.SkillGroupBase;
                if (!groupPicker)
                {

                    CharacterCreationTalentActiveSkillGrantProjection grant =
                        projected.ActiveSkillGrant!;
                    Assert.IsNull(projected.SkillGroupGrant);
                    int quantity = Math.Min(
                        sourceQuantity,
                        CharacterCreationTalentSkillGrantTypes.MaximumPromptSlots);
                    Assert.AreEqual(quantity, grant.Quantity);
                    Assert.AreEqual(effectiveRating, grant.BaseRating);
                    Assert.AreEqual(improvementKind, grant.ImprovementKind);
                    Assert.AreEqual(skillType, grant.SkillType);
                    Assert.AreEqual(rawSkillType, grant.RawSelectorType);
                    Assert.AreEqual(selectorTypeSource, grant.SelectorTypeSource);
                    Assert.AreEqual(query, grant.SkillTypeQuery);
                    string[] specificNames = skillType ==
                                             CharacterCreationTalentSkillGrantTypes.Specific
                        ? rawTalent.Element("skillchoices")?.Elements("skill")
                            .Select(node => node.Value).ToArray() ?? []
                        : [];
                    CollectionAssert.AreEqual(
                        specificNames,
                        grant.SpecificSkillChoiceNames.ToArray());

                    IEnumerable<(string Id, string Name, string Attribute, string Category,
                        string? Group, bool Exotic)> candidates = skillType switch
                    {
                        CharacterCreationTalentSkillGrantTypes.Active
                            or CharacterCreationTalentSkillGrantTypes.Default => skills,
                        CharacterCreationTalentSkillGrantTypes.Magic => skills.Where(skill =>
                            skill.Category is "Magical Active" or "Pseudo-Magical Active"),
                        CharacterCreationTalentSkillGrantTypes.Resonance => skills.Where(skill =>
                            skill.Category == "Resonance Active"
                            || skill.Group is "Cracking" or "Electronics"),
                        CharacterCreationTalentSkillGrantTypes.Matrix => skills.Where(skill =>
                            skill.Group is "Cracking" or "Electronics"),
                        CharacterCreationTalentSkillGrantTypes.Specific when specificNames.Length == 0
                            => skills,
                        CharacterCreationTalentSkillGrantTypes.Specific => specificNames.Select(name =>
                            skills.Single(skill => skill.Name == name)),
                        CharacterCreationTalentSkillGrantTypes.XPath when string.Equals(
                            query,
                            CharacterCreationTalentSkillGrantTypes.PinnedXPathPredicate,
                            StringComparison.Ordinal) => skills.Where(skill =>
                            skill.Attribute is not ("RES" or "DEP")
                            && (skill.Category != "Magical Active"
                                || string.IsNullOrEmpty(skill.Group))),
                        _ => []
                    };
                    (string Id, string Name, string Attribute, string Category, string? Group,
                        bool Exotic)[] expected = skillType ==
                                                  CharacterCreationTalentSkillGrantTypes.Specific
                                                  && specificNames.Length > 0
                        ? candidates.ToArray()
                        : candidates.OrderBy(skill => skill.Name, StringComparer.Ordinal)
                            .ThenBy(skill => skill.Id, StringComparer.Ordinal)
                            .ToArray();
                    CollectionAssert.AreEqual(
                        expected.Select(skill => skill.Id).ToArray(),
                        grant.Options.Select(option => option.SelectionId).ToArray(),
                        $"Active option identity/order drift for {projected.Name}.");
                    for (int optionIndex = 0; optionIndex < expected.Length; optionIndex++)
                    {
                        Assert.AreEqual(expected[optionIndex].Name,
                            grant.Options[optionIndex].CanonicalName);
                        Assert.AreEqual(expected[optionIndex].Attribute,
                            grant.Options[optionIndex].Attribute);
                        Assert.AreEqual(expected[optionIndex].Category,
                            grant.Options[optionIndex].Category);
                        Assert.AreEqual(expected[optionIndex].Group,
                            grant.Options[optionIndex].SkillGroup);
                        Assert.AreEqual(expected[optionIndex].Exotic,
                            grant.Options[optionIndex].IsExotic);
                        Assert.AreEqual(!expected[optionIndex].Exotic,
                            grant.Options[optionIndex].IsEnabled);
                    }
                    bool supportedSkillType = skillType switch
                    {
                        CharacterCreationTalentSkillGrantTypes.Active
                            or CharacterCreationTalentSkillGrantTypes.Default
                            or CharacterCreationTalentSkillGrantTypes.Magic
                            or CharacterCreationTalentSkillGrantTypes.Resonance
                            or CharacterCreationTalentSkillGrantTypes.Matrix
                            or CharacterCreationTalentSkillGrantTypes.Specific => true,
                        CharacterCreationTalentSkillGrantTypes.XPath => string.Equals(
                            query,
                            CharacterCreationTalentSkillGrantTypes.PinnedXPathPredicate,
                            StringComparison.Ordinal),
                        _ => false
                    };
                    Assert.AreEqual(
                        supportedSkillType
                        && expected.Count(skill => !skill.Exotic) >= quantity,
                        grant.IsSupported,
                        $"Active support-state drift for {projected.Name}.");
                    continue;
                }

                CharacterCreationTalentSkillGroupGrantProjection groupGrant =
                    projected.SkillGroupGrant!;
                Assert.IsNull(projected.ActiveSkillGrant);
                int groupQuantity = Math.Min(
                    sourceQuantity,
                    CharacterCreationTalentSkillGrantTypes.MaximumPromptSlots);
                Assert.AreEqual(groupQuantity, groupGrant.Quantity);
                Assert.AreEqual(effectiveRating, groupGrant.BaseRating);
                Assert.AreEqual(improvementKind, groupGrant.ImprovementKind);
                Assert.AreEqual(rawSkillType, groupGrant.RawSelectorType);
                Assert.AreEqual(selectorTypeSource, groupGrant.SelectorTypeSource);
                string groupType = skillType;
                Assert.AreEqual(groupType, groupGrant.SkillGroupType);
                Assert.AreEqual(
                    groupType == CharacterCreationTalentSkillGrantTypes.Choices
                        ? CharacterCreationTalentSkillGrantTypes.GroupChoiceAliasCompatibility
                        : string.Empty,
                    groupGrant.CompatibilityMarker);
                string[] requestedNames = rawTalent.Element("skillgroupchoices")?
                    .Elements("skillgroup").Select(node => node.Value).ToArray() ?? [];
                CollectionAssert.AreEqual(requestedNames, groupGrant.RequestedGroupNames.ToArray());
                bool supportedGroupType = groupType is
                    CharacterCreationTalentSkillGrantTypes.Grouped
                    or CharacterCreationTalentSkillGrantTypes.Choices;
                (string Name, string[] Members)[] expectedGroups = requestedNames.Length == 0
                                                                   && supportedGroupType
                    ? groups
                    : requestedNames.Select(name => groups.Single(group => group.Name == name))
                        .ToArray();
                CollectionAssert.AreEqual(
                    expectedGroups.Select(group => CharacterCreationTalentGrantAuthorityDigest
                            .ComputeSkillGroupSelectionId(
                                CharacterCreationTalentGrantAuthorityDigest.ComputeSkillGroup(
                                    authority.EffectiveSkillsInputsDigest,
                                    group.Name,
                                    group.Members)))
                        .ToArray(),
                    groupGrant.Options.Select(option => option.SelectionId).ToArray(),
                    $"Group option identity/order drift for {projected.Name}.");
                for (int groupIndex = 0; groupIndex < expectedGroups.Length; groupIndex++)
                {
                    Assert.AreEqual(expectedGroups[groupIndex].Name,
                        groupGrant.Options[groupIndex].CanonicalName);
                    CollectionAssert.AreEqual(
                        expectedGroups[groupIndex].Members,
                        groupGrant.Options[groupIndex].MemberSkillSourceIds.ToArray());
                }
                Assert.AreEqual(
                    supportedGroupType && expectedGroups.Length >= groupQuantity,
                    groupGrant.IsSupported,
                    $"Group support-state drift for {projected.Name}.");
            }
        }
    }

    [TestMethod]
    public void Canonical_sum_to_ten_and_improved_profiles_project_exact_weights_and_targets()
    {
        string coreRoot = FindCoreRoot();
        ICharacterSourceDataContext standard = CreateContext(
            coreRoot,
            $"<character><settings>{CanonicalSumToTenSettingsId}</settings></character>")!;
        Assert.IsTrue(standard.TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority standardAuthority));
        Assert.IsTrue(standardAuthority.IsAuthoritative,
            string.Join(",", standardAuthority.Blockers));
        Assert.AreEqual(10, standardAuthority.SumToTenTarget);
        Assert.AreEqual(4, standardAuthority.RankWeights.Single(weight => weight.Rank == "A").Value);
        Assert.AreEqual(3, standardAuthority.RankWeights.Single(weight => weight.Rank == "B").Value);

        ICharacterSourceDataContext improved = CreateContext(
            coreRoot,
            $"<character><settings>{CanonicalImprovedSumToTenSettingsId}</settings>"
            + "<customdatadirectorynames><directoryname>Sum-to-Ten Improved</directoryname>"
            + "</customdatadirectorynames></character>")!;
        Assert.IsTrue(improved.TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority improvedAuthority));
        Assert.IsTrue(improvedAuthority.IsAuthoritative,
            string.Join(",", improvedAuthority.Blockers));
        Assert.AreEqual(14, improvedAuthority.SumToTenTarget);
        Assert.AreEqual(7, improvedAuthority.RankWeights.Single(weight => weight.Rank == "A").Value);
        Assert.AreEqual(4, improvedAuthority.RankWeights.Single(weight => weight.Rank == "B").Value);
        Assert.AreNotEqual(
            standardAuthority.SelectedPriorityCustomDataInputsDigest,
            improvedAuthority.SelectedPriorityCustomDataInputsDigest);
    }

    [TestMethod]
    public void Priority_authority_detects_source_drift_and_rejects_row_mutating_custom_data()
    {
        string root = CreateTempDirectory();
        try
        {
            const string customSetting =
                "<customdatadirectoryname><directoryname>Unsafe Priority</directoryname>"
                + "<order>0</order><enabled>True</enabled></customdatadirectoryname>";
            WriteBaseContent(
                root,
                customSetting,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            string customRoot = Path.Combine(root, "customdata", "Unsafe Priority");
            Directory.CreateDirectory(customRoot);
            File.WriteAllText(
                Path.Combine(customRoot, "amend_priorities.xml"),
                "<chummer><priorities amendoperation=\"replace\" /></chummer>");
            ICharacterSourceDataContext context = CreateContext(
                root,
                CharacterXml(
                    "<customdatadirectorynames><directoryname>Unsafe Priority</directoryname>"
                    + "</customdatadirectorynames>"))!;

            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority unsupported));
            Assert.IsFalse(unsupported.IsAuthoritative);
            CollectionAssert.Contains(
                unsupported.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.PriorityCustomDataUnsupported);

            File.Delete(Path.Combine(customRoot, "amend_priorities.xml"));
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority drifted));
            Assert.IsFalse(drifted.IsAuthoritative);
            CollectionAssert.Contains(
                drifted.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.PrioritiesSourceDrift);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Talent_skill_authority_detects_effective_skills_source_drift()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority initial));
            Assert.IsTrue(initial.IsAuthoritative, string.Join(",", initial.Blockers));

            File.AppendAllText(Path.Combine(root, "data", "skills.xml"), "\n");
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority drifted));
            Assert.IsFalse(drifted.IsAuthoritative);
            CollectionAssert.Contains(
                drifted.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.SkillsSourceDrift);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Talent_skill_authority_projects_case_insensitive_current_and_legacy_choice_rules()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            const string pinnedXPath =
                "not(attribute = 'RES' or attribute = 'DEP') and "
                + "(not(category = 'Magical Active') or skillgroup = '' or not(skillgroup))";
            string talentXml =
                "<talents>"
                + TalentGrant("Mixed", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype>MaGiC</skilltype><skillgroupchoices><skillgroup>Sorcery"
                    + "</skillgroup></skillgroupchoices><skillgroupqty>1</skillgroupqty>"
                    + "<skillgroupval>2</skillgroupval><skillgrouptype>grouped</skillgrouptype>")
                + TalentGrant("Hybrid Active Type", "<skilltype>magic</skilltype>"
                    + "<skillgroupqty>1</skillgroupqty><skillgroupval>2</skillgroupval>"
                    + "<skillgrouptype>grouped</skillgrouptype>")
                + TalentGrant("Hybrid Active Value", "<skillval>3</skillval>"
                    + "<skillgroupqty>1</skillgroupqty><skillgroupval>2</skillgroupval>"
                    + "<skillgrouptype>grouped</skillgrouptype>"
                    + "<skillgroupchoices><skillgroup>Sorcery</skillgroup></skillgroupchoices>")
                + TalentGrant("Hybrid Skill Choices", "<skillchoices><skill>Arcana</skill>"
                    + "</skillchoices><skillgroupqty>1</skillgroupqty>"
                    + "<skillgroupval>2</skillgroupval><skillgrouptype>grouped</skillgrouptype>"
                    + "<skillgroupchoices><skillgroup>Sorcery</skillgroup></skillgroupchoices>")
                + TalentGrant("Hybrid Active Quantity", "<skillqty>1</skillqty>"
                    + "<skillgroupqty>2</skillgroupqty><skillgroupval>2</skillgroupval>"
                    + "<skillgrouptype>grouped</skillgrouptype>"
                    + "<skillgroupchoices><skillgroup>Sorcery</skillgroup></skillgroupchoices>")
                + TalentGrant("Hybrid Invalid Active Value", "<skillqty>1</skillqty>"
                    + "<skillval>not-a-number</skillval><skillgroupval>5</skillgroupval>"
                    + "<skilltype>magic</skilltype>")
                + TalentGrant("Resonance", "<skillqty>3</skillqty><skillval>2</skillval>"
                    + "<skilltype>ReSoNaNcE</skilltype>")
                + TalentGrant("Matrix", "<skillqty>2</skillqty><skillval>2</skillval>"
                    + "<skilltype>MaTrIx</skilltype>")
                + TalentGrant("Specific", "<skillchoices><skill>Arcana</skill>"
                    + "<skill>Assensing</skill></skillchoices><skillqty>2</skillqty>"
                    + "<skillval>3</skillval><skilltype>SpEcIfIc</skilltype>")
                + TalentGrant("Empty Specific", "<skillchoices/><skillqty>1</skillqty>"
                    + "<skillval>3</skillval><skilltype>specific</skilltype>")
                + TalentGrant("XPath", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + $"<skilltype xpath=\"{pinnedXPath}\">XpAtH</skilltype>")
                + TalentGrant("Unknown XPath", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype xpath=\"category = 'Combat Active'\">xpath</skilltype>")
                + TalentGrant("Default", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype>DeFaUlT</skilltype>")
                + TalentGrant("Missing Type", "<skillchoices><skill>Arcana</skill></skillchoices>"
                    + "<skillqty>1</skillqty><skillval>2</skillval>")
                + TalentGrant("Clamped Active", "<skillqty>4</skillqty><skillval>2</skillval>"
                    + "<skilltype>active</skilltype>")
                + TalentGrant("Unknown Active", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype>unknown</skilltype>")
                + TalentGrant("Empty Type", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype></skilltype>")
                + TalentGrant("Whitespace Type", "<skillqty>1</skillqty><skillval>2</skillval>"
                    + "<skilltype> </skilltype>")
                + string.Concat(new[]
                {
                    (Name: "Zero Active", Quantity: "0"),
                    (Name: "Negative Active", Quantity: "-1"),
                    (Name: "Empty Active", Quantity: string.Empty),
                    (Name: "Unparseable Active", Quantity: "not-a-number")
                }.Select(item => TalentGrant(
                    item.Name,
                    $"<skillqty>{item.Quantity}</skillqty><skillval>2</skillval>"
                    + "<skilltype>active</skilltype><skillgroupchoices><skillgroup>Sorcery"
                    + "</skillgroup></skillgroupchoices><skillgroupqty>1</skillgroupqty>"
                    + "<skillgroupval>2</skillgroupval><skillgrouptype>grouped</skillgrouptype>")))
                + TalentGrant("Legacy Choices", "<skillgroupchoices><skillgroup>Sorcery"
                    + "</skillgroup></skillgroupchoices><skillgroupqty>1</skillgroupqty>"
                    + "<skillgroupval>2</skillgroupval><skillgrouptype>ChOiCeS</skillgrouptype>")
                + TalentGrant("Grouped", "<skillgroupchoices><skillgroup>Sorcery"
                    + "</skillgroup></skillgroupchoices><skillgroupqty>1</skillgroupqty>"
                    + "<skillgroupval>2</skillgroupval><skillgrouptype>GrOuPeD</skillgrouptype>")
                + TalentGrant("Empty Grouped", "<skillgroupchoices/><skillgroupqty>1"
                    + "</skillgroupqty><skillgroupval>2</skillgroupval>"
                    + "<skillgrouptype>grouped</skillgrouptype>")
                + TalentGrant("Clamped Grouped", "<skillgroupchoices/><skillgroupqty>4"
                    + "</skillgroupqty><skillgroupval>2</skillgroupval>"
                    + "<skillgrouptype>grouped</skillgrouptype>")
                + string.Concat(new[]
                {
                    (Name: "Zero Group", Quantity: "0"),
                    (Name: "Negative Group", Quantity: "-1"),
                    (Name: "Unparseable Group", Quantity: "not-a-number")
                }.Select(item => TalentGrant(
                    item.Name,
                    $"<skillgroupqty>{item.Quantity}</skillgroupqty>"
                    + "<skillgroupval>2</skillgroupval><skillgrouptype>grouped</skillgrouptype>")))
                + TalentGrant("Unknown Group", "<skillgroupchoices><skillgroup>Sorcery"
                    + "</skillgroup></skillgroupchoices><skillgroupqty>1</skillgroupqty>"
                    + "<skillgroupval>2</skillgroupval><skillgrouptype>unknown</skillgrouptype>")
                + "</talents>";
            WritePriorityFixture(root, talentXml);
            File.WriteAllText(
                Path.Combine(root, "data", "skills.xml"),
                "<chummer><skillgroups><name>Sorcery</name><name>Cracking</name>"
                + "<name>Electronics</name></skillgroups><skills>"
                + Skill("11111111-1111-1111-1111-111111111111", "Spellcasting", "MAG",
                    "Magical Active", "Sorcery", "DISABLED")
                + Skill("22222222-2222-2222-2222-222222222222", "Arcana", "LOG",
                    "Pseudo-Magical Active", null, "DISABLED")
                + Skill("33333333-3333-3333-3333-333333333333", "Assensing", "INT",
                    "Magical Active", null, "SR5")
                + Skill("44444444-4444-4444-4444-444444444444", "Cybercombat", "LOG",
                    "Technical Active", "Cracking", "DISABLED")
                + Skill("55555555-5555-5555-5555-555555555555", "Computer", "LOG",
                    "Technical Active", "Electronics", "DISABLED")
                + Skill("66666666-6666-6666-6666-666666666666", "Compiling", "RES",
                    "Resonance Active", null, "SR5")
                + Skill("77777777-7777-7777-7777-777777777777", "Pistols", "AGI",
                    "Combat Active", null, "SR5")
                + "</skills></chummer>");

            ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority authority));
            Assert.IsTrue(authority.IsAuthoritative, string.Join(",", authority.Blockers));
            CharacterCreationPriorityTalentOptionProjection[] talents = authority.Options
                .First(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Talent)
                .TalentOptions.ToArray();

            CharacterCreationPriorityTalentOptionProjection mixed = talents.Single(talent =>
                talent.Value == "Mixed");
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Magic,
                mixed.ActiveSkillGrant!.SkillType);
            Assert.IsNull(mixed.SkillGroupGrant, "Active skill fields take branch precedence.");
            Assert.IsTrue(mixed.ActiveSkillGrant.Options.Any(option =>
                option.CanonicalName == "Arcana"), "Talent prompts do not apply BookXPath.");

            CharacterCreationPriorityTalentOptionProjection hybridActiveType = talents.Single(
                talent => talent.Value == "Hybrid Active Type");
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Magic,
                hybridActiveType.ActiveSkillGrant!.SkillType);
            Assert.AreEqual(CharacterCreationTalentGrantImprovementKinds.SkillGroupBase,
                hybridActiveType.ActiveSkillGrant.ImprovementKind,
                "The active selector retains the group-value improvement kind.");
            Assert.IsNull(hybridActiveType.SkillGroupGrant);

            CharacterCreationTalentSkillGroupGrantProjection hybridActiveValue = talents.Single(
                talent => talent.Value == "Hybrid Active Value").SkillGroupGrant!;
            Assert.AreEqual(3, hybridActiveValue.BaseRating,
                "skillval takes precedence over skillgroupval in the one effective lane.");
            Assert.AreEqual(CharacterCreationTalentGrantImprovementKinds.SkillBase,
                hybridActiveValue.ImprovementKind,
                "The group selector retains the active-value improvement kind.");

            CharacterCreationPriorityTalentOptionProjection hybridSkillChoices = talents.Single(
                talent => talent.Value == "Hybrid Skill Choices");
            Assert.IsNull(hybridSkillChoices.ActiveSkillGrant);
            CollectionAssert.AreEqual(
                new[] { "Sorcery" },
                hybridSkillChoices.SkillGroupGrant!.RequestedGroupNames.ToArray());

            CharacterCreationTalentSkillGroupGrantProjection hybridActiveQuantity = talents.Single(
                talent => talent.Value == "Hybrid Active Quantity").SkillGroupGrant!;
            Assert.AreEqual(1, hybridActiveQuantity.Quantity,
                "skillqty takes precedence over skillgroupqty in the one effective lane.");

            CharacterCreationTalentActiveSkillGrantProjection hybridInvalidActiveValue =
                talents.Single(talent => talent.Value == "Hybrid Invalid Active Value")
                    .ActiveSkillGrant!;
            Assert.AreEqual(5, hybridInvalidActiveValue.BaseRating);
            Assert.AreEqual(CharacterCreationTalentGrantImprovementKinds.SkillGroupBase,
                hybridInvalidActiveValue.ImprovementKind,
                "An unparsable skillval falls back to skillgroupval for persisted authority.");

            CharacterCreationTalentActiveSkillGrantProjection resonance = talents.Single(talent =>
                talent.Value == "Resonance").ActiveSkillGrant!;
            Assert.IsTrue(resonance.IsSupported, string.Join(",", resonance.Blockers));
            CollectionAssert.AreEquivalent(
                new[] { "Cybercombat", "Computer", "Compiling" },
                resonance.Options.Select(option => option.CanonicalName).ToArray());

            CharacterCreationTalentActiveSkillGrantProjection matrix = talents.Single(talent =>
                talent.Value == "Matrix").ActiveSkillGrant!;
            Assert.IsTrue(matrix.IsSupported, string.Join(",", matrix.Blockers));
            Assert.IsTrue(matrix.Options.All(option => option.SkillGroup is
                "Cracking" or "Electronics"));

            CharacterCreationTalentActiveSkillGrantProjection specific = talents.Single(talent =>
                talent.Value == "Specific").ActiveSkillGrant!;
            Assert.IsTrue(specific.IsSupported, string.Join(",", specific.Blockers));
            CollectionAssert.AreEqual(
                new[] { "Arcana", "Assensing" },
                specific.Options.Select(option => option.CanonicalName).ToArray());

            CharacterCreationTalentActiveSkillGrantProjection emptySpecific = talents.Single(talent =>
                talent.Value == "Empty Specific").ActiveSkillGrant!;
            Assert.IsTrue(emptySpecific.IsSupported, string.Join(",", emptySpecific.Blockers));
            Assert.IsEmpty(emptySpecific.SpecificSkillChoiceNames);
            CollectionAssert.AreEqual(
                emptySpecific.Options.OrderBy(
                        option => option.CanonicalName,
                        StringComparer.Ordinal)
                    .ThenBy(option => option.SourceId, StringComparer.Ordinal)
                    .Select(option => option.SourceId)
                    .ToArray(),
                emptySpecific.Options.Select(option => option.SourceId).ToArray());
            Assert.AreEqual(7, emptySpecific.Options.Count);

            CharacterCreationTalentActiveSkillGrantProjection xpath = talents.Single(talent =>
                talent.Value == "XPath").ActiveSkillGrant!;
            Assert.IsTrue(xpath.IsSupported, string.Join(",", xpath.Blockers));
            Assert.AreEqual(pinnedXPath, xpath.SkillTypeQuery);
            Assert.IsFalse(xpath.Options.Any(option => option.CanonicalName is
                "Spellcasting" or "Compiling"));

            CharacterCreationTalentActiveSkillGrantProjection unknownXPath = talents.Single(talent =>
                talent.Value == "Unknown XPath").ActiveSkillGrant!;
            Assert.IsFalse(unknownXPath.IsSupported);
            Assert.IsEmpty(unknownXPath.Options);
            CollectionAssert.Contains(
                unknownXPath.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.TalentSkillGrantAuthorityUnsupported);

            CharacterCreationTalentActiveSkillGrantProjection defaultGrant = talents.Single(talent =>
                talent.Value == "Default").ActiveSkillGrant!;
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Default, defaultGrant.SkillType);
            Assert.IsTrue(defaultGrant.IsSupported, string.Join(",", defaultGrant.Blockers));

            CharacterCreationTalentActiveSkillGrantProjection missingType = talents.Single(talent =>
                talent.Value == "Missing Type").ActiveSkillGrant!;
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Default, missingType.SkillType);
            Assert.IsTrue(missingType.IsSupported, string.Join(",", missingType.Blockers));
            Assert.IsEmpty(missingType.SpecificSkillChoiceNames);
            Assert.AreEqual(emptySpecific.Options.Count, missingType.Options.Count);

            CharacterCreationTalentActiveSkillGrantProjection clampedActive = talents.Single(talent =>
                talent.Value == "Clamped Active").ActiveSkillGrant!;
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.MaximumPromptSlots,
                clampedActive.Quantity);
            Assert.IsTrue(clampedActive.IsSupported, string.Join(",", clampedActive.Blockers));

            CharacterCreationTalentActiveSkillGrantProjection unknownActive = talents.Single(talent =>
                talent.Value == "Unknown Active").ActiveSkillGrant!;
            Assert.IsTrue(unknownActive.IsSupported, string.Join(",", unknownActive.Blockers));
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Default,
                unknownActive.SkillType);
            Assert.AreEqual("unknown", unknownActive.RawSelectorType);
            Assert.AreEqual(CharacterCreationTalentGrantSelectorTypeSources.SkillType,
                unknownActive.SelectorTypeSource);
            Assert.AreEqual(emptySpecific.Options.Count, unknownActive.Options.Count);
            foreach (string value in new[] { "Empty Type", "Whitespace Type" })
            {
                CharacterCreationTalentActiveSkillGrantProjection legacyDefault = talents.Single(
                    talent => talent.Value == value).ActiveSkillGrant!;
                Assert.IsTrue(legacyDefault.IsSupported,
                    $"{value}: {string.Join(",", legacyDefault.Blockers)}");
                Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Default,
                    legacyDefault.SkillType);
                Assert.AreEqual(CharacterCreationTalentGrantSelectorTypeSources.SkillType,
                    legacyDefault.SelectorTypeSource);
                Assert.AreEqual(value == "Whitespace Type" ? " " : string.Empty,
                    legacyDefault.RawSelectorType);
            }
            foreach (string value in new[]
                     {
                         "Zero Active", "Negative Active", "Empty Active",
                         "Unparseable Active"
                     })
            {
                CharacterCreationPriorityTalentOptionProjection noPrompt = talents.Single(talent =>
                    talent.Value == value);
                Assert.IsNull(noPrompt.ActiveSkillGrant);
                Assert.IsNull(noPrompt.SkillGroupGrant,
                    "An active branch with no prompts still takes precedence over group fields.");
            }

            CharacterCreationTalentSkillGroupGrantProjection legacy = talents.Single(talent =>
                talent.Value == "Legacy Choices").SkillGroupGrant!;
            Assert.IsTrue(legacy.IsSupported, string.Join(",", legacy.Blockers));
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Choices, legacy.SkillGroupType);
            Assert.AreEqual(
                CharacterCreationTalentSkillGrantTypes.GroupChoiceAliasCompatibility,
                legacy.CompatibilityMarker);
            CollectionAssert.Contains(
                legacy.SourceAnchorIds.ToList(),
                $"compatibility:{legacy.CompatibilityMarker}");

            CharacterCreationTalentSkillGroupGrantProjection grouped = talents.Single(talent =>
                talent.Value == "Grouped").SkillGroupGrant!;
            Assert.IsTrue(grouped.IsSupported, string.Join(",", grouped.Blockers));
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Grouped, grouped.SkillGroupType);
            Assert.AreEqual(string.Empty, grouped.CompatibilityMarker);
            CollectionAssert.Contains(
                grouped.Options.Single().MemberSkillSourceIds.ToList(),
                "11111111-1111-1111-1111-111111111111");

            CharacterCreationTalentSkillGroupGrantProjection emptyGrouped = talents.Single(talent =>
                talent.Value == "Empty Grouped").SkillGroupGrant!;
            Assert.IsTrue(emptyGrouped.IsSupported, string.Join(",", emptyGrouped.Blockers));
            Assert.IsEmpty(emptyGrouped.RequestedGroupNames);
            CollectionAssert.AreEqual(
                new[] { "Cracking", "Electronics", "Sorcery" },
                emptyGrouped.Options.Select(option => option.CanonicalName).ToArray());

            CharacterCreationTalentSkillGroupGrantProjection clampedGrouped = talents.Single(talent =>
                talent.Value == "Clamped Grouped").SkillGroupGrant!;
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.MaximumPromptSlots,
                clampedGrouped.Quantity);
            Assert.IsTrue(clampedGrouped.IsSupported, string.Join(",", clampedGrouped.Blockers));
            foreach (string value in new[]
                     {
                         "Zero Group", "Negative Group", "Unparseable Group"
                     })
            {
                Assert.IsNull(talents.Single(talent => talent.Value == value).SkillGroupGrant);
            }
            CharacterCreationPriorityTalentOptionProjection unknownGroup = talents.Single(talent =>
                talent.Value == "Unknown Group");
            Assert.IsNull(unknownGroup.SkillGroupGrant);
            Assert.IsTrue(unknownGroup.ActiveSkillGrant!.IsSupported,
                string.Join(",", unknownGroup.ActiveSkillGrant.Blockers));
            Assert.AreEqual(CharacterCreationTalentSkillGrantTypes.Default,
                unknownGroup.ActiveSkillGrant.SkillType);
            Assert.AreEqual(CharacterCreationTalentGrantImprovementKinds.SkillGroupBase,
                unknownGroup.ActiveSkillGrant.ImprovementKind);
            Assert.AreEqual(CharacterCreationTalentGrantSelectorTypeSources.SkillGroupType,
                unknownGroup.ActiveSkillGrant.SelectorTypeSource);
            Assert.AreEqual("unknown", unknownGroup.ActiveSkillGrant.RawSelectorType);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Talent_skill_authority_fails_closed_on_distinct_ordinary_skill_name_collision()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(
                root,
                "<talents>" + TalentGrant(
                    "Active",
                    "<skillqty>1</skillqty><skillval>2</skillval><skilltype>active</skilltype>")
                + "</talents>");
            File.WriteAllText(
                Path.Combine(root, "data", "skills.xml"),
                "<chummer><skillgroups/><skills>"
                + Skill("11111111-1111-1111-1111-111111111111", "Duplicate", "LOG",
                    "Technical Active", null, "SR5")
                + Skill("22222222-2222-2222-2222-222222222222", "Duplicate", "INT",
                    "Technical Active", null, "SR5")
                + "</skills></chummer>");

            ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority authority));
            Assert.IsFalse(authority.IsAuthoritative);
            CollectionAssert.Contains(
                authority.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.TalentSkillGrantAuthorityUnsupported);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Priority_authority_projects_nonzero_heritage_karma_from_effective_source()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            string prioritiesPath = Path.Combine(root, "data", "priorities.xml");
            File.WriteAllText(
                prioritiesPath,
                File.ReadAllText(prioritiesPath).Replace(
                    "<karma>0</karma>",
                    "<karma>7</karma>",
                    StringComparison.Ordinal));

            ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority authority));
            Assert.IsTrue(authority.IsAuthoritative, string.Join(",", authority.Blockers));
            CharacterCreationPriorityHeritageOptionProjection human = authority.Options.Single(option =>
                    option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
                    && option.Rank == "A")
                .HeritageOptions.Single(option => option.MetatypeName == "Human");
            Assert.AreEqual(7, human.KarmaCost);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Priority_authority_rejects_metatype_custom_data_and_detects_its_digest_drift()
    {
        string root = CreateTempDirectory();
        try
        {
            const string directoryName = "Unsafe Metatypes";
            const string customSetting =
                "<customdatadirectoryname><directoryname>Unsafe Metatypes</directoryname>"
                + "<order>0</order><enabled>True</enabled></customdatadirectoryname>";
            WriteBaseContent(
                root,
                customSetting,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            WritePriorityFixture(root);
            string customRoot = Path.Combine(root, "customdata", directoryName);
            Directory.CreateDirectory(customRoot);
            string amendmentPath = Path.Combine(customRoot, "amend_metatypes.xml");
            File.WriteAllText(
                amendmentPath,
                "<chummer><metatypes><metatype><name>Human</name>"
                + "<karma amendoperation=\"replace\">1</karma></metatype></metatypes></chummer>");
            ICharacterSourceDataContext context = CreateContext(
                root,
                CharacterXml(
                    "<customdatadirectorynames><directoryname>Unsafe Metatypes</directoryname>"
                    + "</customdatadirectorynames>"))!;

            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority unsupported));
            Assert.IsFalse(unsupported.IsAuthoritative);
            Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                unsupported.SelectedCustomDataInputsDigest));
            CollectionAssert.Contains(
                unsupported.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.MetatypeCustomDataUnsupported);

            File.AppendAllText(amendmentPath, "\n");
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority drifted));
            Assert.IsFalse(drifted.IsAuthoritative);
            CollectionAssert.Contains(
                drifted.Blockers.ToList(),
                CharacterCreationPrerequisiteBlockers.CustomDataDrift);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Priority_projection_fails_closed_on_ambiguous_rows_missing_attributes_and_namespaces()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints>"
                + "<priorityarray></priorityarray><prioritytable>Standard</prioritytable>"
                + "<sumtoten>10</sumtoten>");
            string path = Path.Combine(root, "data", "priorities.xml");

            WritePriorityFixture(root);
            ICharacterSourceDataContext defaultArray = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(defaultArray.TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority defaultArrayAuthority));
            Assert.IsTrue(defaultArrayAuthority.IsAuthoritative,
                string.Join(",", defaultArrayAuthority.Blockers));
            CollectionAssert.AreEqual(
                new[] { "A", "B", "C", "D", "E" },
                defaultArrayAuthority.PriorityArray.ToArray());
            string canonical = File.ReadAllText(path);
            File.WriteAllText(
                path,
                canonical.Replace(
                    "</priorities>",
                    "<priority><id>10000000-0000-0000-0000-000000000001</id>"
                    + "<name>duplicate</name><value>A</value><category>Heritage</category>"
                    + "</priority></priorities>",
                    StringComparison.Ordinal));
            AssertPriorityBlocker(root, CharacterCreationPrerequisiteBlockers.PriorityRowsInvalid);

            WritePriorityFixture(root);
            File.WriteAllText(
                path,
                File.ReadAllText(path).Replace(
                    "<attributes>24</attributes>",
                    string.Empty,
                    StringComparison.Ordinal));
            AssertPriorityBlocker(root, CharacterCreationPrerequisiteBlockers.PriorityRowsInvalid);

            WritePriorityFixture(root);
            File.WriteAllText(
                path,
                File.ReadAllText(path).Replace(
                    "<chummer>",
                    "<chummer xmlns=\"urn:unsupported\">",
                    StringComparison.Ordinal));
            AssertPriorityBlocker(
                root,
                CharacterCreationPrerequisiteBlockers.PriorityCategoriesInvalid);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Canonical_life_module_profile_exposes_exact_750_karma_authority()
    {
        string coreRoot = FindCoreRoot();
        ICharacterSourceDataContext context = CreateContext(
            coreRoot,
            $"<character><settings>{CanonicalLifeModuleSettingsId}</settings></character>")!;

        Assert.IsNotNull(context);
        Assert.IsTrue(context.TryResolveCreationSourceProfile(
            out CharacterCreationSourceProfileAuthority authority));
        Assert.AreEqual(CharacterCreationBuildMethods.LifeModules, authority.BuildMethod);
        Assert.AreEqual(750, authority.BuildPoints);
        Assert.IsTrue(authority.LifeModuleBudgetIsExact);
        Assert.IsEmpty(authority.BudgetBlockers);
        CollectionAssert.Contains(authority.EnabledSourcebooks.ToList(), "RF");
        CollectionAssert.Contains(authority.EnabledSourcebooks.ToList(), "SR5");
    }

    [TestMethod]
    public void Creation_budget_profile_rejects_missing_duplicate_mismatched_and_negative_fields()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(root, string.Empty, "");
            CharacterCreationSourceProfileAuthority missing = ResolveCreationProfile(root);
            Assert.IsFalse(missing.LifeModuleBudgetIsExact);
            CollectionAssert.Contains(
                missing.BudgetBlockers.ToList(),
                CharacterCreationFoundationBlockers.LifeModuleBudgetProfileBuildMethodInvalid);
            CollectionAssert.Contains(
                missing.BudgetBlockers.ToList(),
                CharacterCreationFoundationBlockers.LifeModuleBudgetProfileBuildPointsInvalid);

            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>LifeModule</buildmethod><buildmethod>LifeModule</buildmethod>"
                + "<buildpoints>750</buildpoints><buildpoints>750</buildpoints>");
            CharacterCreationSourceProfileAuthority duplicate = ResolveCreationProfile(root);
            Assert.IsFalse(duplicate.LifeModuleBudgetIsExact);
            CollectionAssert.Contains(
                duplicate.BudgetBlockers.ToList(),
                CharacterCreationFoundationBlockers.LifeModuleBudgetProfileBuildMethodInvalid);
            CollectionAssert.Contains(
                duplicate.BudgetBlockers.ToList(),
                CharacterCreationFoundationBlockers.LifeModuleBudgetProfileBuildPointsInvalid);

            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>Priority</buildmethod><buildpoints>750</buildpoints>");
            CharacterCreationSourceProfileAuthority mismatch = ResolveCreationProfile(root);
            Assert.IsFalse(mismatch.LifeModuleBudgetIsExact);
            CollectionAssert.Contains(
                mismatch.BudgetBlockers.ToList(),
                CharacterCreationFoundationBlockers.LifeModuleBudgetProfileBuildMethodMismatch);

            WriteBaseContent(
                root,
                string.Empty,
                "<buildmethod>LifeModule</buildmethod><buildpoints>-1</buildpoints>");
            CharacterCreationSourceProfileAuthority negative = ResolveCreationProfile(root);
            Assert.IsFalse(negative.LifeModuleBudgetIsExact);
            CollectionAssert.Contains(
                negative.BudgetBlockers.ToList(),
                CharacterCreationFoundationBlockers.LifeModuleBudgetProfileBuildPointsInvalid);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Creation_source_profile_comes_from_saved_settings_and_binds_raw_profile_inputs()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(root, customDataSetting: string.Empty);
            ICharacterSourceDataContext first = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(first.TryResolveCreationSourceProfile(
                out CharacterCreationSourceProfileAuthority firstAuthority));
            CollectionAssert.AreEqual(
                new[] { "SG", "SR5" },
                firstAuthority.EnabledSourcebooks.ToArray());

            string settingsPath = Path.Combine(root, "data", "settings.xml");
            File.AppendAllText(settingsPath, "\n");
            ICharacterSourceDataContext second = CreateContext(root, CharacterXml())!;
            Assert.IsTrue(second.TryResolveCreationSourceProfile(
                out CharacterCreationSourceProfileAuthority secondAuthority));

            Assert.AreEqual(SettingsId, firstAuthority.SettingsProfileId);
            Assert.AreNotEqual(
                firstAuthority.RawProfileInputsDigest,
                secondAuthority.RawProfileInputsDigest,
                "Changing raw settings.xml bytes must change the profile authority digest.");
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Context_resolves_base_grade_and_vehicle_mod_source_values()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(root, customDataSetting: string.Empty);
            ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;

            Assert.IsNotNull(context);
            Assert.IsTrue(context.TryIsBookEnabled("sg", out bool streetGrimoireEnabled));
            Assert.IsTrue(streetGrimoireEnabled);
            Assert.IsTrue(context.TryIsBookEnabled("FA", out bool forbiddenArcanaEnabled));
            Assert.IsFalse(forbiddenArcanaEnabled);
            Assert.IsFalse(context.TryIsBookEnabled(string.Empty, out _));
            Assert.IsTrue(context.TryResolveCyberwareGradeDeviceRating("Standard", "Cyberware", out int rating));
            Assert.AreEqual(4, rating);
            Assert.IsTrue(context.TryResolveCyberwareGradeDeviceRating("Alphaware", "Cyberware", out int fallbackRating));
            Assert.AreEqual(3, fallbackRating);
            Assert.IsTrue(context.TryResolveMaxNuyenDecimals(out int maximumNuyenDecimals));
            Assert.AreEqual(3, maximumNuyenDecimals);
            Assert.IsTrue(context.TryResolveGroupMembershipKarmaCosts(out int joinCost, out int leaveCost));
            Assert.AreEqual(5, joinCost);
            Assert.AreEqual(1, leaveCost);
            Assert.IsTrue(context.TryResolveKarmaNuyenExchangeRates(
                out decimal workingForPeopleRate,
                out decimal workingForManRate));
            Assert.AreEqual(1_500m, workingForPeopleRate);
            Assert.AreEqual(2_000m, workingForManRate);

            Assert.IsTrue(context.TryResolveVehicleModBonuses(
                VehicleModId,
                "Gyro-Stabilization",
                out CharacterVehicleModSourceBonuses bonuses));
            Assert.AreEqual("Rating + 1", bonuses.BodyExpression);
            Assert.AreEqual("2", bonuses.DeviceRatingExpression);
            Assert.AreEqual("3", bonuses.MatrixConditionExpression);
            Assert.AreEqual("1", bonuses.WirelessBodyExpression);
            Assert.AreEqual("4", bonuses.WirelessDeviceRatingExpression);
            Assert.AreEqual("5", bonuses.WirelessMatrixConditionExpression);

            Assert.IsTrue(context.TryResolveVehicleModBonuses(
                Guid.NewGuid().ToString("D"),
                "Removed Source Item",
                out CharacterVehicleModSourceBonuses missing));
            Assert.AreEqual(CharacterVehicleModSourceBonuses.Empty, missing);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Context_applies_highest_priority_governed_overlay()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(root, customDataSetting: string.Empty);
            string amendsRoot = Path.Combine(root, "amends");
            WriteOverlay(amendsRoot, "low", priority: 10, deviceRating: 6);
            WriteOverlay(amendsRoot, "high", priority: 20, deviceRating: 8);

            ICharacterSourceDataContext context = CreateContext(root, CharacterXml(), amendsRoot)!;

            Assert.IsNotNull(context);
            Assert.IsTrue(context.TryResolveCyberwareGradeDeviceRating("Standard", "Cyberware", out int rating));
            Assert.AreEqual(8, rating);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Context_applies_selected_legacy_custom_data_in_profile_order()
    {
        string root = CreateTempDirectory();
        try
        {
            const string customId = "4b3a4c48-d2af-4e46-9d27-9f06eab83c0c";
            WriteBaseContent(
                root,
                $"<customdatadirectoryname><directoryname>{customId}&gt;1.0</directoryname><order>0</order><enabled>True</enabled></customdatadirectoryname>");
            string customRoot = Path.Combine(root, "customdata", "My Rules");
            Directory.CreateDirectory(customRoot);
            File.WriteAllText(
                Path.Combine(customRoot, "manifest.xml"),
                $"<manifest><guid>{customId}</guid><version>2.0.0</version></manifest>");
            File.WriteAllText(
                Path.Combine(customRoot, "amend_cyberware.xml"),
                "<chummer><grades><grade><name>Standard</name><devicerating>7</devicerating></grade></grades></chummer>");
            File.WriteAllText(
                Path.Combine(customRoot, "amend_vehicles.xml"),
                $"<chummer><mods><mod><id>{VehicleModId}</id><bonus><body>Rating + 2</body><devicerating>6</devicerating><matrixcmbonus>7</matrixcmbonus></bonus></mod></mods></chummer>");

            ICharacterSourceDataContext context = CreateContext(
                root,
                CharacterXml("<customdatadirectorynames><directoryname>My Rules</directoryname></customdatadirectorynames>"))!;

            Assert.IsNotNull(context);
            Assert.IsTrue(context.TryResolveCyberwareGradeDeviceRating("Standard", "Cyberware", out int rating));
            Assert.AreEqual(7, rating);
            Assert.IsTrue(context.TryResolveVehicleModBonuses(
                VehicleModId,
                "Gyro-Stabilization",
                out CharacterVehicleModSourceBonuses bonuses));
            Assert.AreEqual("Rating + 2", bonuses.BodyExpression);
            Assert.AreEqual("6", bonuses.DeviceRatingExpression);
            Assert.AreEqual("7", bonuses.MatrixConditionExpression);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Context_applies_same_phase_custom_files_in_alphabetical_order()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                "<customdatadirectoryname><directoryname>Ordered Rules</directoryname><order>0</order><enabled>True</enabled></customdatadirectoryname>");
            string customRoot = Path.Combine(root, "customdata", "Ordered Rules");
            Directory.CreateDirectory(customRoot);
            File.WriteAllText(
                Path.Combine(customRoot, "amend_z_cyberware.xml"),
                "<chummer><grades><grade><name>Standard</name><devicerating>9</devicerating></grade></grades></chummer>");
            File.WriteAllText(
                Path.Combine(customRoot, "amend_a_cyberware.xml"),
                "<chummer><grades><grade><name>Standard</name><devicerating>6</devicerating></grade></grades></chummer>");

            ICharacterSourceDataContext context = CreateContext(
                root,
                CharacterXml("<customdatadirectorynames><directoryname>Ordered Rules</directoryname></customdatadirectorynames>"))!;

            Assert.IsNotNull(context);
            Assert.IsTrue(context.TryResolveCyberwareGradeDeviceRating("Standard", "Cyberware", out int rating));
            Assert.AreEqual(9, rating);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Spirit_catalog_applies_selected_custom_additions_and_amendments_exactly()
    {
        string root = CreateTempDirectory();
        try
        {
            const string customId = "5b3a4c48-d2af-4e46-9d27-9f06eab83c0c";
            const string fireId = "a1111111-1111-1111-1111-111111111111";
            const string airId = "a2222222-2222-2222-2222-222222222222";
            const string waterId = "a3333333-3333-3333-3333-333333333333";
            WriteBaseContent(
                root,
                $"<customdatadirectoryname><directoryname>{customId}&gt;1.0</directoryname><order>0</order><enabled>True</enabled></customdatadirectoryname>");
            File.WriteAllText(
                Path.Combine(root, "data", "traditions.xml"),
                $"<chummer><spirits><spirit><id>{fireId}</id><name>Spirit of Fire</name></spirit><spirit><id>{airId}</id><name>Spirit of Air</name></spirit></spirits></chummer>");

            string customRoot = Path.Combine(root, "customdata", "Spirit Rules");
            Directory.CreateDirectory(customRoot);
            File.WriteAllText(
                Path.Combine(customRoot, "manifest.xml"),
                $"<manifest><guid>{customId}</guid><version>2.0.0</version></manifest>");
            File.WriteAllText(
                Path.Combine(customRoot, "custom_traditions.xml"),
                $"<chummer><spirits><spirit><id>{waterId}</id><name>Spirit of Water</name></spirit></spirits></chummer>");
            File.WriteAllText(
                Path.Combine(customRoot, "amend_traditions.xml"),
                $"<chummer><spirits><spirit><id>{airId}</id><name amendoperation=\"REPLACE\">Spirit of Storm</name></spirit></spirits></chummer>");

            ICharacterSourceDataContext context = CreateContext(
                root,
                CharacterXml("<customdatadirectorynames><directoryname>Spirit Rules</directoryname></customdatadirectorynames>"))!;

            Assert.IsNotNull(context);
            Assert.IsTrue(context.TryResolveSpiritCatalogNames("Spirit", out IReadOnlyList<string> names));
            CollectionAssert.AreEqual(
                new[] { "Spirit of Fire", "Spirit of Storm", "Spirit of Water" },
                names.ToArray());
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Context_rejects_saved_custom_directory_mismatch_and_unknown_settings()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(root, customDataSetting: string.Empty);

            Assert.IsNull(CreateContext(
                root,
                CharacterXml("<customdatadirectorynames><directoryname>Unexpected Rules</directoryname></customdatadirectorynames>")));
            Assert.IsNull(CreateContext(
                root,
                $"<character><settings>{Guid.NewGuid():D}</settings></character>"));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Targeted_unsupported_amend_operation_fails_closed()
    {
        string root = CreateTempDirectory();
        try
        {
            WriteBaseContent(
                root,
                "<customdatadirectoryname><directoryname>Unsafe Rules</directoryname><order>0</order><enabled>True</enabled></customdatadirectoryname>");
            string customRoot = Path.Combine(root, "customdata", "Unsafe Rules");
            Directory.CreateDirectory(customRoot);
            File.WriteAllText(
                Path.Combine(customRoot, "amend_cyberware.xml"),
                "<chummer><grades><grade amendoperation=\"multiply\"><name>Standard</name><devicerating>9</devicerating></grade></grades></chummer>");
            ICharacterSourceDataContext context = CreateContext(
                root,
                CharacterXml("<customdatadirectorynames><directoryname>Unsafe Rules</directoryname></customdatadirectorynames>"))!;

            Assert.IsNotNull(context);
            Assert.IsFalse(context.TryResolveCyberwareGradeDeviceRating("Standard", "Cyberware", out _));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private static ICharacterSourceDataContext? CreateContext(
        string root,
        string characterXml,
        string? amendsRoot = null)
    {
        var overlays = new FileSystemContentOverlayCatalogService(root, root, amendsRoot);
        var resolver = new FileSystemCharacterSourceDataResolver(overlays);
        return resolver.TryCreateContext(characterXml);
    }

    private static string CharacterXml(string extra = "")
        => $"<character><settings>{SettingsId}</settings>{extra}</character>";

    private static CharacterCreationSourceProfileAuthority ResolveCreationProfile(string root)
    {
        ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;
        Assert.IsNotNull(context);
        Assert.IsTrue(context.TryResolveCreationSourceProfile(
            out CharacterCreationSourceProfileAuthority authority));
        return authority;
    }

    private static void WriteBaseContent(
        string root,
        string customDataSetting,
        string? buildAuthorityXml = null)
    {
        buildAuthorityXml ??=
            "<buildmethod>LifeModule</buildmethod><buildpoints>750</buildpoints>";
        string data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(
            Path.Combine(data, "settings.xml"),
            $"<chummer><settings><setting><id>{SettingsId}</id><nuyenformat>#,0.###</nuyenformat><karmajoingroup>5</karmajoingroup><karmaleavegroup>1</karmaleavegroup><nuyenperbpwftp>1500</nuyenperbpwftp><nuyenperbpwftm>2000</nuyenperbpwftm><books><book>SR5</book><book>SG</book></books><customdatadirectorynames>{customDataSetting}</customdatadirectorynames>{buildAuthorityXml}<alternatemetatypeattributekarma>False</alternatemetatypeattributekarma><reverseattributepriorityorder>False</reverseattributepriorityorder><karmacost><karmaattribute>5</karmaattribute></karmacost></setting></settings></chummer>");
        File.WriteAllText(
            Path.Combine(data, "metatypes.xml"),
            "<chummer><metatypes><metatype><id>a53d885d-a4a4-443d-b6a6-b0a55b0a96c7</id>"
            + "<name>Human</name><category>Metahuman</category><karma>0</karma>"
            + "<bodmin>1</bodmin><bodmax>6</bodmax><bodaug>10</bodaug>"
            + "<agimin>1</agimin><agimax>6</agimax><agiaug>10</agiaug>"
            + "<reamin>1</reamin><reamax>6</reamax><reaaug>10</reaaug>"
            + "<strmin>1</strmin><strmax>6</strmax><straug>10</straug>"
            + "<chamin>1</chamin><chamax>6</chamax><chaaug>10</chaaug>"
            + "<intmin>1</intmin><intmax>6</intmax><intaug>10</intaug>"
            + "<logmin>1</logmin><logmax>6</logmax><logaug>10</logaug>"
            + "<wilmin>1</wilmin><wilmax>6</wilmax><wilaug>10</wilaug>"
            + "<edgmin>2</edgmin><edgmax>7</edgmax><edgaug>7</edgaug>"
            + "<magmin>1</magmin><magmax>6</magmax><magaug>6</magaug>"
            + "<resmin>1</resmin><resmax>6</resmax><resaug>6</resaug>"
            + "<essmin>0</essmin><essmax>6</essmax><essaug>6</essaug>"
            + "<depmin>0</depmin><depmax>0</depmax><depaug>0</depaug>"
            + "<bonus/><source>SR5</source></metatype></metatypes></chummer>");
        File.WriteAllText(
            Path.Combine(data, "skills.xml"),
            "<chummer><skillgroups><name>Sorcery</name></skillgroups><skills><skill>"
            + "<id>40c72109-8924-45ca-a4d7-255b75e6a6b0</id><name>Spellcasting</name>"
            + "<category>Magical Active</category><skillgroup>Sorcery</skillgroup>"
            + "<source>SR5</source></skill></skills></chummer>");
        File.WriteAllText(
            Path.Combine(data, "cyberware.xml"),
            "<chummer><grades><grade><name>Standard</name><devicerating>4</devicerating></grade><grade><name>Alphaware</name></grade></grades></chummer>");
        File.WriteAllText(
            Path.Combine(data, "bioware.xml"),
            "<chummer><grades><grade><name>Standard</name><devicerating>2</devicerating></grade></grades></chummer>");
        File.WriteAllText(
            Path.Combine(data, "vehicles.xml"),
            $"<chummer><mods><mod><id>{VehicleModId}</id><name>Gyro-Stabilization</name><bonus><body>Rating + 1</body><devicerating>2</devicerating><matrixcmbonus>3</matrixcmbonus></bonus><wirelessbonus><body>1</body><devicerating>4</devicerating><matrixcmbonus>5</matrixcmbonus></wirelessbonus></mod></mods></chummer>");
    }

    private static void WriteOverlay(string amendsRoot, string id, int priority, int deviceRating)
    {
        string packRoot = Path.Combine(amendsRoot, id);
        string data = Path.Combine(packRoot, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(
            Path.Combine(packRoot, "manifest.json"),
            $"{{\"id\":\"{id}\",\"priority\":{priority},\"enabled\":true,\"mode\":\"merge-catalog\"}}");
        File.WriteAllText(
            Path.Combine(data, "cyberware.fragment.xml"),
            $"<chummer><grades><grade><name>Standard</name><devicerating>{deviceRating}</devicerating></grade></grades></chummer>");
    }

    private static string TalentGrant(string name, string grantXml) =>
        $"<talent><name>{name}</name><value>{name}</value>{grantXml}</talent>";

    private static string Skill(
        string id,
        string name,
        string attribute,
        string category,
        string? group,
        string source) =>
        $"<skill><id>{id}</id><name>{name}</name><attribute>{attribute}</attribute>"
        + $"<category>{category}</category>"
        + (group is null ? "<skillgroup/>" : $"<skillgroup>{group}</skillgroup>")
        + $"<source>{source}</source></skill>";

    private static void WritePriorityFixture(string root, string? talentXml = null)
    {
        talentXml ??=
            "<talents><talent><name>Mundane</name><value>Mundane</value>"
            + "<forbidden><oneof><metatype>A.I.</metatype></oneof></forbidden>"
            + "</talent></talents>";
        string[] categories = ["Heritage", "Talent", "Attributes", "Skills", "Resources"];
        string[] ranks = ["A", "B", "C", "D", "E"];
        Dictionary<string, int> attributePoints = new(StringComparer.Ordinal)
        {
            ["A"] = 24,
            ["B"] = 20,
            ["C"] = 16,
            ["D"] = 14,
            ["E"] = 12
        };
        int sequence = 1;
        string rows = string.Concat(categories.SelectMany(category => ranks.Select(rank =>
        {
            string attributes = category == "Attributes"
                ? $"<attributes>{attributePoints[rank]}</attributes>"
                : category == "Heritage"
                    ? "<metatypes><metatype><name>Human</name><value>1</value><karma>0</karma></metatype></metatypes>"
                    : category == "Talent"
                        ? talentXml
                        : string.Empty;
            string id = $"00000000-0000-0000-0000-{sequence++:000000000000}";
            return $"<priority><id>{id}</id><name>{category}-{rank}</name><value>{rank}</value>"
                   + $"<category>{category}</category>{attributes}</priority>";
        })));
        File.WriteAllText(
            Path.Combine(root, "data", "priorities.xml"),
            "<chummer><categories><category>Heritage</category><category>Talent</category>"
            + "<category>Attributes</category><category>Skills</category><category>Resources</category>"
            + "</categories><priortysumtotenvalues><A>4</A><B>3</B><C>2</C><D>1</D><E>0</E>"
            + $"</priortysumtotenvalues><priorities>{rows}</priorities></chummer>");
    }

    private static void AssertPriorityBlocker(string root, string blocker)
    {
        ICharacterSourceDataContext context = CreateContext(root, CharacterXml())!;
        Assert.IsNotNull(context);
        Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority authority));
        Assert.IsFalse(authority.IsAuthoritative);
        CollectionAssert.Contains(authority.Blockers.ToList(), blocker);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"chummer-source-data-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindCoreRoot()
    {
        DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Chummer", "data", "settings.xml")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate canonical Chummer/data/settings.xml.");
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
