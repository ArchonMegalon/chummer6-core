using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    public void Life_module_resources_bind_explicit_funding_to_the_whole_review_without_writes()
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = SeedFullGraph(directory);
            var (_, baseline) = BuildFullGraph(fixture.Store, fixture.Id);
            var service = CreateService(fixture.Store);
            var prompt = baseline.ModuleSequence!.QualityLevels.Single().InstancePrompt!;
            var request = QualityInstanceRequest(service, fixture.Id, new Dictionary<string, string> { [prompt.PromptId] = "Renraku" })
                with { TalentSelection = new("mundane") };
            var missing = service.PreviewFinalization(request).Value!;
            Assert.IsNotNull(missing.ResourcesPolicy, string.Join(", ", missing.FinalizationBlocked));
            Assert.IsNull(missing.ResourcesQuote, "No implicit zero-funding decision.");
            CollectionAssert.Contains(missing.FinalizationBlocked.ToArray(), CharacterCreationLifeModuleResourcesQuote.SelectionRequired);
            var native = missing.SkillsCatalog!.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
            var selected = request with
            {
                SkillSelection = new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)], []),
                KarmaResourceInvestment = 1m
            };
            var preview = service.PreviewFinalization(selected).Value!;
            Assert.IsNotNull(preview.ResourcesQuote, string.Join(", ", preview.FinalizationBlocked));
            var quote = preview.ResourcesQuote;
            Assert.AreEqual(CharacterCreationKarmaResourcesPolicy.LifeModulesSchemaV1, quote.Policy.Schema);
            Assert.IsFalse(CharacterCreationKarmaResourcesRules.IsValidPolicy(quote.Policy), "A Life Modules funding quote cannot authorize Karma creation.");
            Assert.IsEmpty(quote.Blockers, string.Join(", ", quote.Blockers));
            Assert.AreEqual(2000m, quote.NuyenFromKarma);
            Assert.AreEqual(quote.TotalKarma - preview.EffectWriteSummary!.ModuleKarmaCost
                - preview.MetatypeWriteSummary!.MetatypeKarmaCost - preview.TalentWriteSummary!.KarmaCost
                - preview.AttributeQuote!.KarmaUsed - preview.SkillsQuote!.KarmaUsed
                - quote.QualityCosts.KarmaAdjustmentAfterTalent, quote.KarmaBeforeResources);
            Assert.AreEqual(quote.QualityCosts, preview.QualityCosts);
            Assert.AreEqual(-15, quote.QualityCosts.Lines.Single(row => row.Name == "SINner (Corporate Limited)").SourceKarma);
            Assert.AreEqual(-20m, quote.QualityCosts.FreeNegativeQualities, "Nationality and Arcology offsets both survive tier resolution.");
            Assert.AreEqual(5m, quote.QualityCosts.KarmaAdjustmentAfterTalent);
            Assert.AreEqual(quote.KarmaBeforeResources - 1m, quote.KarmaAfterResources);
            Assert.AreEqual(preview.SkillsQuote.QuoteDigest, quote.SkillsQuoteDigest);
            Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quote with { QuoteDigest = string.Empty }), quote.QuoteDigest);
            Assert.AreNotEqual(missing.PreviewDigest, preview.PreviewDigest);
            var omitted = service.ConfirmFinalization(new(selected.Binding, selected.DraftRevision, selected.DraftDigest, preview.PreviewDigest, true)
                { QualityInstanceValues = selected.QualityInstanceValues, TalentSelection = selected.TalentSelection, SkillSelection = selected.SkillSelection });
            CollectionAssert.Contains(omitted.Blockers.ToArray(), CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch);
            var exact = service.ConfirmFinalization(new(selected.Binding, selected.DraftRevision, selected.DraftDigest, preview.PreviewDigest, true)
                { QualityInstanceValues = selected.QualityInstanceValues, TalentSelection = selected.TalentSelection,
                    SkillSelection = selected.SkillSelection, KarmaResourceInvestment = selected.KarmaResourceInvestment });
            Assert.IsFalse(exact.Blockers.Contains(CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch));
            Assert.IsFalse(preview.CanApply);
            var reopened = CreateService(new FileWorkspaceStore(directory)).PreviewFinalization(selected).Value!;
            Assert.AreEqual(JsonSerializer.Serialize(quote), JsonSerializer.Serialize(reopened.ResourcesQuote));
            CollectionAssert.AreEqual(fixture.Before, File.ReadAllBytes(WorkspacePath(directory, fixture.Id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow("0", "0")]
    [DataRow("0.25", "500")]
    [DataRow("2", "4000")]
    public void Life_module_resources_preserve_fractional_funding_without_adding_starting_cash(string invested, string funded)
    {
        var fixture = SkillMathFixture();
        var policy = ResourcePolicy(fixture);
        var quote = QuoteLifeResources(fixture, policy, decimal.Parse(invested, System.Globalization.CultureInfo.InvariantCulture)).Quote!;
        Assert.IsNotNull(quote);
        Assert.AreEqual(decimal.Parse(funded, System.Globalization.CultureInfo.InvariantCulture), quote.NuyenFromKarma);
        Assert.AreEqual(quote.KarmaBeforeResources - quote.KarmaInvestment, quote.KarmaAfterResources);
    }

    [TestMethod]
    public void Life_module_resources_use_confirmed_attributes_house_expression_and_remaining_budget()
    {
        var fixture = SkillMathFixture(("SkillLevel", "Pistols", 2m));
        var policy = ResourcePolicy(fixture) with { FundingExpression = "{Karma} * 123 + {LOGUnaug} + {INT} + {PriorityNuyen}", MaximumKarmaInvestment = 1m };
        policy = policy with { AuthorityDigest = CharacterCreationKarmaResourcesRules.ComputePolicyDigest(policy) };
        var quote = QuoteLifeResources(fixture, policy, 2m).Quote!;
        Assert.IsNotNull(quote);
        Assert.AreEqual(246m + fixture.Attributes.Attributes.Where(row => row.AttributeId is "LOG" or "INT").Sum(row => row.Current), quote.NuyenFromKarma);
        CollectionAssert.Contains(quote.Blockers.ToArray(), CharacterCreationKarmaResourcesRules.InvestmentLimitExceeded);
        var depleted = QuoteLifeResources(fixture, policy, 1m, fixture.Racial.Metatype.KarmaCost).Quote!;
        Assert.AreEqual(0m, depleted.KarmaBeforeResources);
        Assert.AreEqual(-1m, depleted.KarmaAfterResources);
        CollectionAssert.Contains(depleted.Blockers.ToArray(), CharacterCreationAttributesBlockers.GlobalKarmaExceeded);
    }

    [TestMethod]
    [DataRow("negative")]
    [DataRow("overflow")]
    [DataRow("unknown-expression")]
    [DataRow("division-zero")]
    [DataRow("policy-digest")]
    [DataRow("wrong-schema")]
    [DataRow("wrong-profile")]
    [DataRow("skill-digest")]
    [DataRow("attribute-digest")]
    [DataRow("fractional-budget")]
    public void Life_module_resources_reject_invalid_or_mixed_inputs(string fault)
    {
        var fixture = SkillMathFixture();
        var policy = ResourcePolicy(fixture);
        decimal investment = fault switch { "negative" => -1m, "overflow" => decimal.MaxValue, _ => 1m };
        if (fault == "attribute-digest") fixture = fixture with { Attributes = fixture.Attributes with { QuoteDigest = "changed" } };
        if (fault == "policy-digest") policy = policy with { MaximumKarmaInvestment = 999 };
        if (fault is "unknown-expression" or "division-zero" or "wrong-schema" or "wrong-profile")
        {
            policy = fault switch
            {
                "unknown-expression" => policy with { FundingExpression = "{Unknown} * 100" },
                "division-zero" => policy with { FundingExpression = "{Karma} / 0" },
                "wrong-schema" => policy with { Schema = CharacterCreationKarmaResourcesPolicy.SchemaV1 },
                _ => policy with { SettingsProfileId = "different-profile" }
            };
            policy = policy with { AuthorityDigest = CharacterCreationKarmaResourcesRules.ComputePolicyDigest(policy) };
        }
        var native = fixture.Catalog.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
        // For the attribute tamper keep the original skill quote, proving its binding.
        var valid = fault == "attribute-digest" ? SkillMathFixture() : fixture;
        var skills = valid.Quote(new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)], [])).Quote!;
        if (fault == "skill-digest") skills = skills with { KarmaUsed = 999m };
        var result = CharacterCreationLifeModuleResourcesRules.Quote(fixture.Effects, fixture.Racial, fixture.Talent,
            fixture.Attributes, skills, policy, LifeQualityPolicy(fixture), fault == "fractional-budget" ? 999.5m : 1000m, investment);
        Assert.IsNull(result.Quote, fault);
        Assert.IsNotEmpty(result.Blockers, fault);
    }

    [TestMethod]
    public void Life_module_resources_recheck_policy_after_upstream_admission()
    {
        var fixture = SkillMathFixture();
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(fixture.Xml)!;
        var skills = CharacterCreationLifeModuleSkillsRules.Evaluate(fixture.Xml, fixture.Effects, fixture.Racial,
            fixture.Talent, fixture.Attributes, null, context).Quote!;
        Assert.IsNotNull(skills);
        var changing = new ResourceDriftContext(context);
        var result = CharacterCreationLifeModuleResourcesRules.Evaluate(fixture.Xml, fixture.Effects, fixture.Racial,
            fixture.Talent, fixture.Attributes, skills, 1000m, 1m, changing);
        Assert.AreEqual(2, changing.Reads, "Failure must come from the final policy reread, not an unrelated upstream mismatch.");
        Assert.IsNull(result.Quote);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationFoundationBlockers.SourceDigestConflict);
    }

    [TestMethod]
    public void Life_module_resources_do_not_trust_a_rehashed_skill_price()
    {
        var fixture = SkillMathFixture();
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(fixture.Xml)!;
        var skills = CharacterCreationLifeModuleSkillsRules.Evaluate(fixture.Xml, fixture.Effects, fixture.Racial,
            fixture.Talent, fixture.Attributes, null, context).Quote!;
        Assert.IsNotNull(skills);
        skills = skills with { KarmaUsed = 100m };
        skills = skills with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(skills with { QuoteDigest = string.Empty }) };
        var result = CharacterCreationLifeModuleResourcesRules.Evaluate(fixture.Xml, fixture.Effects, fixture.Racial,
            fixture.Talent, fixture.Attributes, skills, 1000m, 1m, context);
        Assert.IsNull(result.Quote);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationFoundationBlockers.SourceDigestConflict);
    }

    private static CharacterCreationKarmaResourcesPolicy ResourcePolicy(SkillMathBinding fixture)
    {
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(fixture.Xml)!;
        Assert.IsTrue(context.TryResolveCreationLifeModuleResourcesPolicy(out var policy));
        Assert.IsFalse(context.TryResolveCreationKarmaResourcesPolicy(out _));
        return policy!;
    }

    private static CharacterCreationLifeModuleResourcesQuoteResult QuoteLifeResources(SkillMathBinding fixture,
        CharacterCreationKarmaResourcesPolicy policy, decimal? investment, decimal total = 1000m)
    {
        var native = fixture.Catalog.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
        var skills = fixture.Quote(new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)], [])).Quote!;
        return CharacterCreationLifeModuleResourcesRules.Quote(fixture.Effects, fixture.Racial, fixture.Talent,
            fixture.Attributes, skills, policy, LifeQualityPolicy(fixture), total, investment);
    }

    private sealed class ResourceDriftContext(ICharacterSourceDataContext inner) : ICharacterSourceDataContext
    {
        private int _reads;
        internal int Reads => _reads;
        public bool TryResolveCyberwareGradeDeviceRating(string sourceId, string grade, out int rating)
            => inner.TryResolveCyberwareGradeDeviceRating(sourceId, grade, out rating);
        public bool TryResolveVehicleModBonuses(string sourceId, string grade, out CharacterVehicleModSourceBonuses bonuses)
            => inner.TryResolveVehicleModBonuses(sourceId, grade, out bonuses);
        public bool TryResolveCreationSkillsCatalog(out CharacterCreationSkillsCatalog? catalog)
            => inner.TryResolveCreationSkillsCatalog(out catalog);
        public bool TryResolveCreationLifeModuleSkillsPolicy(out CharacterCreationKarmaSkillsPolicy? policy)
            => inner.TryResolveCreationLifeModuleSkillsPolicy(out policy);
        public bool TryResolveCreationLifeModuleQualitiesPolicy(out CharacterCreationKarmaQualitiesPolicy? policy)
            => inner.TryResolveCreationLifeModuleQualitiesPolicy(out policy);
        public bool TryResolveCreationLifeModuleResourcesPolicy(out CharacterCreationKarmaResourcesPolicy? policy)
        {
            bool found = inner.TryResolveCreationLifeModuleResourcesPolicy(out policy);
            if (policy is not null && ++_reads > 1) policy = policy with { MaximumKarmaInvestment = 999m };
            return found;
        }
    }
}
