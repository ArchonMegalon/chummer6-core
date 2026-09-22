using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    public void Life_module_skills_preview_source_grants_and_explicit_language_without_writes()
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = SeedFullGraph(directory);
            var (effectResult, baseline) = BuildFullGraph(fixture.Store, fixture.Id);
            var service = CreateService(fixture.Store);
            var prompt = baseline.ModuleSequence!.QualityLevels.Single().InstancePrompt!;
            var request = QualityInstanceRequest(service, fixture.Id, new Dictionary<string, string> { [prompt.PromptId] = "Renraku" })
                with { TalentSelection = new("mundane") };
            var preview = service.PreviewFinalization(request).Value!;
            Assert.IsNotNull(preview.SkillsQuote, string.Join(", ", preview.FinalizationBlocked));
            Assert.IsNotNull(preview.SkillsCatalog);
            Assert.AreEqual(CharacterCreationKarmaSkillsPolicy.LifeModulesSchemaV1, preview.SkillsQuote.Policy.Schema);
            Assert.AreEqual(0m, preview.SkillsQuote.KarmaUsed, "Module Karma already pays for its grants.");
            CollectionAssert.Contains(preview.SkillsQuote.Blockers.ToArray(), CharacterCreationSkillsBlockers.NativeLanguageRequired);
            var grants = effectResult.Plan!.ImprovementXml.Select(XElement.Parse).ToArray();
            foreach (var row in preview.SkillsQuote.Skills)
            {
                int free = grants.Where(item => item.Element("improvementttype")?.Value == "SkillLevel"
                    && item.Element("improvedname")?.Value == row.Name).Sum(item => int.Parse(item.Element("val")!.Value, CultureInfo.InvariantCulture));
                Assert.AreEqual(free, row.ModuleLevels);
                Assert.AreEqual(0, row.Allocation.KarmaLevels);
                Assert.AreEqual(0, row.KarmaCost);
                Assert.IsNotEmpty(row.SourceAnchorIds);
            }
            var native = preview.SkillsCatalog.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
            var selected = request with { SkillSelection = new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)], []) };
            var chosen = service.PreviewFinalization(selected).Value!;
            Assert.IsNotNull(chosen.SkillsQuote);
            Assert.IsEmpty(chosen.SkillsQuote.Blockers, string.Join(", ", chosen.SkillsQuote.Blockers));
            Assert.AreEqual(1, chosen.SkillsQuote.NativeLanguagesUsed);
            Assert.AreNotEqual(preview.PreviewDigest, chosen.PreviewDigest);
            Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(chosen.SkillsQuote with { QuoteDigest = string.Empty }), chosen.SkillsQuote.QuoteDigest);
            var omitted = service.ConfirmFinalization(new(selected.Binding, selected.DraftRevision, selected.DraftDigest, chosen.PreviewDigest, true)
                { QualityInstanceValues = selected.QualityInstanceValues, TalentSelection = selected.TalentSelection });
            CollectionAssert.Contains(omitted.Blockers.ToArray(), CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch);
            var exact = service.ConfirmFinalization(new(selected.Binding, selected.DraftRevision, selected.DraftDigest, chosen.PreviewDigest, true)
                { QualityInstanceValues = selected.QualityInstanceValues, TalentSelection = selected.TalentSelection, SkillSelection = selected.SkillSelection });
            Assert.IsFalse(exact.Blockers.Contains(CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch));
            Assert.IsFalse(chosen.CanApply, "Remaining finalization domains must not be bypassed.");
            var reopened = CreateService(new FileWorkspaceStore(directory)).PreviewFinalization(selected).Value!;
            Assert.AreEqual(JsonSerializer.Serialize(chosen.SkillsQuote), JsonSerializer.Serialize(reopened.SkillsQuote));
            CollectionAssert.AreEqual(fixture.Before, File.ReadAllBytes(WorkspacePath(directory, fixture.Id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow(0, 0, 3, 0)]
    [DataRow(1, 0, 4, 8)]
    [DataRow(0, 1, 4, 0)]
    [DataRow(1, 1, 5, 10)]
    public void Life_module_skills_free_individual_and_group_levels_are_not_charged_twice(
        int personal, int groupPurchase, int expectedRating, int expectedCost)
    {
        var fixture = SkillMathFixture(("SkillLevel", "Pistols", 2m), ("SkillGroupLevel", "Firearms", 1m));
        var source = fixture.Catalog.ActiveSkills.Single(row => row.Name == "Pistols");
        var group = fixture.Catalog.SkillGroups.Single(row => row.Name == "Firearms");
        var quote = fixture.Quote(new([new(source.SourceSkillId, source.Kind, personal)], [new(group.GroupId, groupPurchase)]));
        Assert.IsNotNull(quote.Quote, string.Join(", ", quote.Blockers));
        var row = quote.Quote.Skills.Single(item => item.Name == "Pistols");
        Assert.AreEqual(expectedRating, row.Rating);
        Assert.AreEqual(expectedCost, row.KarmaCost);
        Assert.AreEqual(2, row.ModuleLevels);
        Assert.AreEqual(1, row.ModuleGroupLevels);
        var groupRow = quote.Quote.Groups.Single(item => item.Name == "Firearms");
        Assert.AreEqual(groupPurchase == 0 ? 0 : 10, groupRow.KarmaCost);
        Assert.AreEqual((decimal)(expectedCost + groupRow.KarmaCost), quote.Quote.KarmaUsed);
        CollectionAssert.AreEqual(new[] { CharacterCreationSkillsBlockers.NativeLanguageRequired }, quote.Blockers.ToArray());
    }

    [TestMethod]
    public void Life_module_skills_knowledge_pool_rounds_grants_once_and_uses_attribute_values()
    {
        var fixture = SkillMathFixture(("FreeKnowledgeSkills", "", 0.2m), ("FreeKnowledgeSkills", "", 0.3m));
        var policy = fixture.Policy with { KnowledgePointsExpression = "{LOG} + {INT}" };
        policy = policy with { AuthorityDigest = CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(policy) };
        var native = fixture.Catalog.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
        var skill = fixture.Catalog.KnowledgeSkills.First(row => !row.CanBeNativeLanguage);
        var result = fixture.Quote(new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true),
            new(skill.SourceSkillId, skill.Kind, 1, KnowledgePointLevels: 2)], []), policy);
        Assert.IsNotNull(result.Quote, string.Join(", ", result.Blockers));
        Assert.IsEmpty(result.Blockers);
        Assert.AreEqual(0.5m, result.Quote.ModuleKnowledgePoints);
        Assert.AreEqual(fixture.Attributes.Attributes.Where(row => row.AttributeId is "LOG" or "INT").Sum(row => row.Current) + 1,
            result.Quote.KnowledgePointsTotal);
        Assert.AreEqual(2, result.Quote.KnowledgePointsUsed);
        Assert.AreEqual(3 * policy.KarmaImproveKnowledgeSkill, result.Quote.Skills.Single(row => row.Name == skill.Name).KarmaCost);
    }

    [TestMethod]
    public void Life_module_skills_respect_disabled_groups_and_category_price_modifiers()
    {
        var fixture = SkillMathFixture(("SkillLevel", "Pistols", 2m), ("SkillGroupLevel", "Firearms", 1m),
            ("SkillGroupCategoryDisable", "Combat Active", 0m), ("SkillCategoryKarmaCostMultiplier", "Combat Active", 150m));
        var source = fixture.Catalog.ActiveSkills.Single(row => row.Name == "Pistols");
        var group = fixture.Catalog.SkillGroups.Single(row => row.Name == "Firearms");
        var result = fixture.Quote(new([new(source.SourceSkillId, source.Kind, 1)], [new(group.GroupId, 1)]));
        Assert.IsNotNull(result.Quote, string.Join(", ", result.Blockers));
        var row = result.Quote.Skills.Single(item => item.Name == source.Name);
        Assert.AreEqual(3, row.Rating);
        Assert.AreEqual(0, row.GroupLevels);
        Assert.AreEqual(9, row.KarmaCost);
        Assert.IsFalse(result.Quote.Groups.Single(item => item.Name == group.Name).IsEnabled);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationSkillsBlockers.GroupInvalid);
    }

    [TestMethod]
    [DataRow("policy-schema")]
    [DataRow("policy-digest")]
    [DataRow("catalog-digest")]
    [DataRow("attribute-digest")]
    [DataRow("effect-digest")]
    [DataRow("duplicate")]
    [DataRow("unknown")]
    [DataRow("overflow")]
    [DataRow("talent-unlock")]
    public void Life_module_skills_reject_changed_authority_or_invalid_allocations(string fault)
    {
        var fixture = SkillMathFixture(("SkillLevel", "Pistols", 2m));
        var skill = fixture.Catalog.ActiveSkills.Single(row => row.Name == "Pistols");
        CharacterCreationKarmaSkillsSelection selection = new([], []);
        if (fault == "policy-schema")
        {
            var policy = fixture.Policy with { Schema = CharacterCreationKarmaSkillsPolicy.SchemaV1 };
            fixture = fixture with { Policy = policy with { AuthorityDigest = CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(policy) } };
        }
        if (fault == "policy-digest") fixture = fixture with { Policy = fixture.Policy with { KarmaNewActiveSkill = 999 } };
        if (fault == "catalog-digest") fixture = fixture with { Catalog = fixture.Catalog with { CatalogDigest = "changed" } };
        if (fault == "attribute-digest") fixture = fixture with { Attributes = fixture.Attributes with { QuoteDigest = "changed" } };
        if (fault == "effect-digest") fixture = fixture with { Effects = fixture.Effects with { ImprovementXml = [] } };
        if (fault == "duplicate") selection = new([new(skill.SourceSkillId, skill.Kind, 1), new(skill.SourceSkillId, skill.Kind, 1)], []);
        if (fault == "unknown") selection = new([new(Guid.NewGuid().ToString("D"), skill.Kind, 1)], []);
        if (fault == "overflow") selection = new([new(skill.SourceSkillId, skill.Kind, int.MaxValue)], []);
        if (fault == "talent-unlock") selection = new([], [], "Sorcery");
        var result = fixture.Quote(selection);
        Assert.IsNull(result.Quote, fault);
        Assert.IsNotEmpty(result.Blockers, fault);
    }

    [TestMethod]
    public void Life_module_skills_caps_free_levels_but_rejects_paid_overflow_and_strict_broken_groups()
    {
        var fixture = SkillMathFixture(("SkillLevel", "Pistols", 30m));
        var skill = fixture.Catalog.ActiveSkills.Single(row => row.Name == "Pistols");
        var free = fixture.Quote(null).Quote!;
        Assert.AreEqual(fixture.Policy.MaxActiveSkillRatingCreate, free.Skills.Single().Rating);
        Assert.AreEqual(0m, free.KarmaUsed);
        var bought = fixture.Quote(new([new(skill.SourceSkillId, skill.Kind, 1)], []));
        CollectionAssert.Contains(bought.Blockers.ToArray(), CharacterCreationSkillsBlockers.RatingInvalid);
        var group = fixture.Catalog.SkillGroups.Single(row => row.Name == "Firearms");
        var strict = fixture.Policy with { StrictSkillGroupsInCreateMode = true };
        strict = strict with { AuthorityDigest = CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(strict) };
        var broken = fixture.Quote(new([], [new(group.GroupId, 1)]), strict);
        CollectionAssert.Contains(broken.Blockers.ToArray(), CharacterCreationSkillsBlockers.GroupBroken);
        Assert.AreEqual(0, broken.Quote!.Groups.Single(row => row.Name == group.Name).EffectiveLevels);
    }

    [TestMethod]
    public void Life_module_skills_specialization_cost_is_included_once_and_keeps_exact_source_identity()
    {
        var fixture = SkillMathFixture(("SkillLevel", "Pistols", 2m),
            ("SkillCategorySpecializationKarmaCostMultiplier", "Combat Active", 125m));
        var skill = fixture.Catalog.ActiveSkills.Single(row => row.Name == "Pistols");
        var spec = skill.Specializations.First(row => !row.Name.Contains('[', StringComparison.Ordinal));
        var selection = new CharacterCreationKarmaSkillsSelection([new(skill.SourceSkillId, skill.Kind, 0,
            SpecializationOptionId: spec.OptionId, SpecializationPayment: CharacterCreationKarmaSpecializationPayments.Karma)], []);
        var result = fixture.Quote(selection).Quote!;
        var value = result.Skills.Single();
        int expected = (int)decimal.Ceiling(fixture.Policy.KarmaSpecialization * 1.25m);
        Assert.AreEqual(expected, value.KarmaCost);
        Assert.AreEqual(expected, value.SpecializationKarmaCost);
        Assert.AreEqual((decimal)expected, result.KarmaUsed);
        var invalid = fixture.Quote(selection with { Skills = [selection.Skills.Single() with { SpecializationOptionId = "unknown-spec" }] });
        CollectionAssert.Contains(invalid.Blockers.ToArray(), CharacterCreationSkillsBlockers.SpecializationInvalid);
    }

    [TestMethod]
    [DataRow("catalog")]
    [DataRow("policy")]
    public void Life_module_skills_reject_source_drift_during_evaluation(string changing)
    {
        var fixture = SkillMathFixture(("SkillLevel", "Pistols", 2m));
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(fixture.Xml)!;
        var result = CharacterCreationLifeModuleSkillsRules.Evaluate(fixture.Xml, fixture.Effects, fixture.Racial,
            fixture.Talent, fixture.Attributes, null, new SkillDriftContext(context, changing));
        Assert.IsNull(result.Quote);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationFoundationBlockers.SourceDigestConflict);
    }

    private sealed class SkillDriftContext(ICharacterSourceDataContext inner, string changing) : ICharacterSourceDataContext
    {
        private int _catalogReads;
        private int _policyReads;
        public bool TryResolveCyberwareGradeDeviceRating(string sourceId, string grade, out int rating)
            => inner.TryResolveCyberwareGradeDeviceRating(sourceId, grade, out rating);
        public bool TryResolveVehicleModBonuses(string sourceId, string grade, out CharacterVehicleModSourceBonuses bonuses)
            => inner.TryResolveVehicleModBonuses(sourceId, grade, out bonuses);
        public bool TryResolveCreationSkillsCatalog(out CharacterCreationSkillsCatalog? catalog)
        {
            bool found = inner.TryResolveCreationSkillsCatalog(out catalog);
            if (catalog is not null && changing == "catalog" && ++_catalogReads > 1)
                catalog = catalog with { CatalogDigest = "changed" };
            return found;
        }
        public bool TryResolveCreationLifeModuleSkillsPolicy(out CharacterCreationKarmaSkillsPolicy? policy)
        {
            bool found = inner.TryResolveCreationLifeModuleSkillsPolicy(out policy);
            if (policy is not null && changing == "policy" && ++_policyReads > 1)
                policy = policy with { KarmaNewActiveSkill = 999 };
            return found;
        }
    }

    private static SkillMathBinding SkillMathFixture(params (string Type, string Name, decimal Value)[] grants)
    {
        var basis = AttributeMathFixture(0, "CHA");
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(basis.Xml)!;
        Assert.IsTrue(context.TryResolveCreationSkillsCatalog(out var catalog));
        Assert.IsTrue(context.TryResolveCreationLifeModuleSkillsPolicy(out var policy));
        Assert.IsFalse(context.TryResolveCreationKarmaSkillsPolicy(out _));
        Assert.IsTrue(context.TryResolveCreationLifeModuleTalents(out var talents));
        var effects = basis.Effects with { ImprovementXml = grants.Select(grant =>
        {
            var xml = CharacterCreationAwakenedLegacyProjector.Improvement(grant.Type, grant.Name, "math-only", "Quality");
            xml.Element("val")!.Value = grant.Value.ToString(CultureInfo.InvariantCulture);
            return xml.ToString(SaveOptions.DisableFormatting);
        }).ToArray() };
        var (bound, racial) = SealAttributeMathPlans(effects, basis.Racial);
        var talent = new CharacterCreationLifeModuleTalentWritePlan("life-module-talent-contribution/v1", bound.PlanDigest,
            racial.PlanDigest, talents!, new("mundane"), talents!.Options.Single(row => row.OptionId == "mundane"),
            null, [], [], [], [], string.Empty);
        talent = talent with { PlanDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(talent) };
        var attributes = CharacterCreationLifeModuleAttributeRules.Evaluate(basis.Xml, bound, racial, basis.Policy, [], talent);
        Assert.IsNotNull(attributes.Quote, string.Join(", ", attributes.Blockers));
        // Arithmetic fixtures deliberately choose the ordinary optional group policy.
        policy = policy! with { StrictSkillGroupsInCreateMode = false, CompensateSkillGroupKarmaDifference = false };
        policy = policy with { AuthorityDigest = CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(policy) };
        return new(basis.Xml, bound, racial, talent, attributes.Quote, catalog!, policy);
    }

    private sealed record SkillMathBinding(string Xml, CharacterCreationFoundationSequenceWritePlan Effects,
        CharacterCreationLifeModuleMetatypeWritePlan Racial, CharacterCreationLifeModuleTalentWritePlan Talent,
        CharacterCreationLifeModuleAttributeQuote Attributes, CharacterCreationSkillsCatalog Catalog, CharacterCreationKarmaSkillsPolicy Policy)
    {
        internal CharacterCreationLifeModuleSkillsQuoteResult Quote(CharacterCreationKarmaSkillsSelection? selection,
            CharacterCreationKarmaSkillsPolicy? policy = null) => CharacterCreationLifeModuleSkillsRules.Quote(Xml, Effects,
                Racial, Talent, Attributes, selection, Catalog, policy ?? Policy);
    }
}
