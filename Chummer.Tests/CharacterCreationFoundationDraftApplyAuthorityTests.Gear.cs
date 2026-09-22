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
    public void Life_module_gear_binds_reviewed_purchases_and_reopens_without_mutating_the_runner()
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
            var native = missing.SkillsCatalog!.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
            request = request with
            {
                SkillSelection = new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)], []),
                KarmaResourceInvestment = 1m
            };
            missing = service.PreviewFinalization(request).Value!;
            Assert.IsNotNull(missing.GearAuthority, string.Join(", ", missing.FinalizationBlocked));
            Assert.IsNull(missing.GearQuote);
            CollectionAssert.Contains(missing.FinalizationBlocked.ToArray(), CharacterCreationLifeModuleGearQuote.SelectionRequired);
            var option = missing.GearAuthority.Options.First(row => row.IsSelectable && row.PackageCost is > 0 and < 1000);
            request = request with { GearSelection = [new(option.OptionId, 2)] };
            var preview = service.PreviewFinalization(request).Value!;
            Assert.IsNotNull(preview.GearQuote, string.Join(", ", preview.FinalizationBlocked));
            var quote = preview.GearQuote;
            Assert.IsEmpty(quote.Blockers, string.Join(", ", quote.Blockers));
            Assert.AreEqual(preview.ResourcesQuote!.QuoteDigest, quote.ResourcesQuoteDigest);
            Assert.AreEqual(missing.GearAuthority.AuthorityDigest, quote.Basis.AuthorityDigest);
            Assert.AreEqual(2000m, quote.Budget.TotalStartingNuyen);
            Assert.AreEqual(option.PackageCost * 2 / option.PackageQuantity, quote.Budget.BasketCost);
            Assert.AreEqual(2000m - quote.Budget.BasketCost, quote.Budget.RemainingNuyen);
            Assert.AreEqual(option.SourceNodeDigest, quote.Lines.Single().SourceNodeDigest);
            CollectionAssert.AreEqual(option.SourceAnchorIds.ToArray(), quote.Lines.Single().SourceAnchorIds.ToArray());
            Assert.IsTrue(CharacterCreationLegacySourceProjector.TryBuildGear(quote.Lines.Single(), quote.QuoteDigest, out _));
            Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quote with { QuoteDigest = string.Empty }), quote.QuoteDigest);
            var confirm = new CharacterCreationFoundationFinalizationConfirmRequest(request.Binding, request.DraftRevision,
                request.DraftDigest, preview.PreviewDigest, true)
            {
                QualityInstanceValues = request.QualityInstanceValues, TalentSelection = request.TalentSelection,
                SkillSelection = request.SkillSelection, KarmaResourceInvestment = request.KarmaResourceInvestment,
                GearSelection = request.GearSelection
            };
            var omitted = service.ConfirmFinalization(confirm with { GearSelection = null });
            CollectionAssert.Contains(omitted.Blockers.ToArray(), CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch);
            var exact = service.ConfirmFinalization(confirm);
            Assert.IsFalse(exact.Blockers.Contains(CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch));
            Assert.IsFalse(preview.CanApply, "Whole-character finalization is not authorized by a basket alone.");
            var reopened = CreateService(new FileWorkspaceStore(directory)).PreviewFinalization(request).Value!;
            Assert.AreEqual(JsonSerializer.Serialize(quote), JsonSerializer.Serialize(reopened.GearQuote));
            CollectionAssert.AreEqual(fixture.Before, File.ReadAllBytes(WorkspacePath(directory, fixture.Id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Life_module_gear_distinguishes_missing_empty_and_overspent_baskets()
    {
        var fixture = SkillMathFixture();
        var authority = LifeGearAuthority(fixture);
        var resources = QuoteLifeResources(fixture, ResourcePolicy(fixture), 0m).Quote!;
        var absent = CharacterCreationLifeModuleGearRules.Quote(authority, resources, null);
        Assert.IsNull(absent.Quote);
        CollectionAssert.Contains(absent.Blockers.ToArray(), CharacterCreationLifeModuleGearQuote.SelectionRequired);
        var empty = CharacterCreationLifeModuleGearRules.Quote(authority, resources, []);
        Assert.IsNotNull(empty.Quote);
        Assert.IsEmpty(empty.Blockers, string.Join(", ", empty.Blockers));
        Assert.AreEqual(0m, empty.Quote.Budget.BasketCost);
        Assert.IsTrue(empty.Quote.Budget.IsExact);
        var option = authority.Options.First(row => row.IsSelectable && row.PackageCost is > 0 and < 1000);
        var spent = CharacterCreationLifeModuleGearRules.Quote(authority, resources, [new(option.OptionId, 1)]);
        Assert.IsNotNull(spent.Quote);
        CollectionAssert.Contains(spent.Blockers.ToArray(), CharacterCreationGearBlockers.InsufficientFunds);
        Assert.AreEqual(option.PackageCost / option.PackageQuantity, spent.Quote.Budget.Overspend);
        Assert.AreEqual(0m, spent.Quote.Budget.RemainingNuyen);
        Assert.IsFalse(spent.Quote.Budget.IsExact);
        resources = resources with { Blockers = [CharacterCreationAttributesBlockers.GlobalKarmaExceeded] };
        resources = resources with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(resources with { QuoteDigest = string.Empty }) };
        var blocked = CharacterCreationLifeModuleGearRules.Quote(authority, resources, []);
        CollectionAssert.Contains(blocked.Blockers.ToArray(), CharacterCreationAttributesBlockers.GlobalKarmaExceeded);
        Assert.IsFalse(blocked.Quote!.Budget.IsExact);
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("zero")]
    [DataRow("negative")]
    [DataRow("unknown")]
    [DataRow("malformed-id")]
    [DataRow("disabled")]
    [DataRow("null-row")]
    public void Life_module_gear_rejects_invalid_and_disabled_selections(string fault)
    {
        var fixture = SkillMathFixture();
        var authority = LifeGearAuthority(fixture);
        var resources = QuoteLifeResources(fixture, ResourcePolicy(fixture), 1m).Quote!;
        var option = authority.Options.First(row => row.IsSelectable);
        CharacterCreationGearSelection[] basket = fault switch
        {
            "duplicate" => [new(option.OptionId, 1), new(option.OptionId, 1)],
            "zero" => [new(option.OptionId, 0)],
            "negative" => [new(option.OptionId, -1)],
            "unknown" => [new("gear:00000000-0000-0000-0000-000000000001", 1)],
            "malformed-id" => [new("gear:not-an-id", 1)],
            "disabled" => [new(authority.Options.First(row => !row.IsSelectable).OptionId, 1)],
            _ => [null!]
        };
        var result = CharacterCreationLifeModuleGearRules.Quote(authority, resources, basket);
        Assert.IsNotEmpty(result.Blockers, fault);
        Assert.IsTrue(result.Quote is null || !result.Quote.Budget.IsExact, fault);
        Assert.IsTrue(result.Quote is null || result.Quote.Lines.Count == 0, "Rejected rows must not become purchase lines.");
    }

    [TestMethod]
    [DataRow("null-authority")]
    [DataRow("unused-price")]
    [DataRow("source-xml")]
    [DataRow("profile")]
    [DataRow("funding")]
    public void Life_module_gear_rejects_unadmitted_catalog_and_funding_changes(string fault)
    {
        var fixture = SkillMathFixture();
        var authority = LifeGearAuthority(fixture);
        var resources = QuoteLifeResources(fixture, ResourcePolicy(fixture), 1m).Quote!;
        if (fault == "null-authority") authority = null!;
        if (fault == "profile") authority = authority with { ProfileDigest = "changed" };
        if (fault == "funding") resources = resources with { NuyenFromKarma = 999999m };
        if (fault is "unused-price" or "source-xml")
        {
            var options = authority.Options.ToArray();
            options[0] = fault == "unused-price" ? options[0] with { PackageCost = options[0].PackageCost + 1m }
                : options[0] with { SourceNodeXml = "<gear>changed</gear>" };
            if (fault == "source-xml") options[0] = options[0] with { OptionDigest = CharacterCreationGearRules.ComputeOptionDigest(options[0]) };
            authority = authority with { Options = options };
            authority = authority with { AuthorityDigest = CharacterCreationGearRules.ComputeAuthorityDigest(authority) };
        }
        var result = CharacterCreationLifeModuleGearRules.Quote(authority, resources, []);
        Assert.IsNull(result.Quote, fault);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationGearBlockers.AuthorityUnavailable);
    }

    [TestMethod]
    [DataRow("gear")]
    [DataRow("funding")]
    [DataRow("forged-budget")]
    [DataRow("forged-quality-cost")]
    [DataRow("quality-source")]
    [DataRow("existing-inventory")]
    public void Life_module_gear_rechecks_source_and_recalculates_funding_before_admission(string fault)
    {
        var fixture = SkillMathFixture();
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(fixture.Xml)!;
        var native = fixture.Catalog.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
        var skills = CharacterCreationLifeModuleSkillsRules.Evaluate(fixture.Xml, fixture.Effects, fixture.Racial,
            fixture.Talent, fixture.Attributes, new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)], []), context).Quote!;
        Assert.IsNotNull(skills);
        var funding = CharacterCreationLifeModuleResourcesRules.Evaluate(fixture.Xml, fixture.Effects, fixture.Racial,
            fixture.Talent, fixture.Attributes, skills, 1000m, 1m, context).Quote!;
        Assert.IsNotNull(funding);
        var xml = fixture.Xml;
        if (fault == "existing-inventory")
        {
            var document = XDocument.Parse(xml);
            document.Root!.Elements("gears").Remove();
            document.Root.Add(new XElement("gears", new XElement("gear", new XElement("name", "Do not overwrite"))));
            xml = document.ToString(SaveOptions.DisableFormatting);
        }
        if (fault == "forged-budget")
        {
            funding = funding with { NuyenFromKarma = 999999m };
            funding = funding with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(funding with { QuoteDigest = string.Empty }) };
        }
        if (fault == "forged-quality-cost")
        {
            var quality = funding.QualityCosts with { KarmaAdjustmentAfterTalent = -999m, QuoteDigest = string.Empty };
            quality = quality with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quality) };
            funding = funding with { QualityCosts = quality, KarmaBeforeResources = funding.KarmaBeforeResources + 999m,
                KarmaAfterResources = funding.KarmaAfterResources + 999m, QuoteDigest = string.Empty };
            funding = funding with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(funding) };
        }
        var changing = new GearDriftContext(context, fault);
        var result = CharacterCreationLifeModuleGearRules.Evaluate(xml, fixture.Effects, fixture.Racial,
            fixture.Talent, fixture.Attributes, skills, funding, 1000m, [], changing);
        if (fault == "gear") Assert.AreEqual(2, changing.GearReads);
        if (fault == "funding") Assert.AreEqual(3, changing.FundingReads, "The final resource reread must detect drift after basket projection.");
        if (fault == "quality-source") Assert.AreEqual(3, changing.QualityReads, "Quality policy must remain unchanged through basket projection.");
        Assert.IsNull(result.Quote, fault);
        CollectionAssert.Contains(result.Blockers.ToArray(), fault == "existing-inventory"
            ? CharacterCreationFoundationBlockers.PendingDraftConflict : CharacterCreationFoundationBlockers.SourceDigestConflict);
    }

    private static CharacterCreationGearAuthority LifeGearAuthority(SkillMathBinding fixture)
    {
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(fixture.Xml)!;
        Assert.IsTrue(context.TryResolveCreationGearAuthority(out var authority));
        return authority;
    }

    private sealed class GearDriftContext(ICharacterSourceDataContext inner, string fault) : ICharacterSourceDataContext
    {
        internal int GearReads { get; private set; }
        internal int FundingReads { get; private set; }
        internal int QualityReads { get; private set; }
        public bool TryResolveCyberwareGradeDeviceRating(string sourceId, string grade, out int rating)
            => inner.TryResolveCyberwareGradeDeviceRating(sourceId, grade, out rating);
        public bool TryResolveVehicleModBonuses(string sourceId, string grade, out CharacterVehicleModSourceBonuses bonuses)
            => inner.TryResolveVehicleModBonuses(sourceId, grade, out bonuses);
        public bool TryResolveCreationSkillsCatalog(out CharacterCreationSkillsCatalog? catalog)
            => inner.TryResolveCreationSkillsCatalog(out catalog);
        public bool TryResolveCreationLifeModuleSkillsPolicy(out CharacterCreationKarmaSkillsPolicy? policy)
            => inner.TryResolveCreationLifeModuleSkillsPolicy(out policy);
        public bool TryResolveCreationLifeModuleQualitiesPolicy(out CharacterCreationKarmaQualitiesPolicy? policy)
        {
            bool found = inner.TryResolveCreationLifeModuleQualitiesPolicy(out policy);
            if (++QualityReads == 3 && fault == "quality-source" && policy is not null)
                policy = SealQualityPolicy(policy with { Costs = new(3, false, false) });
            return found;
        }
        public bool TryResolveCreationLifeModuleResourcesPolicy(out CharacterCreationKarmaResourcesPolicy? policy)
        {
            bool found = inner.TryResolveCreationLifeModuleResourcesPolicy(out policy);
            FundingReads++;
            if (policy is not null && fault == "funding" && FundingReads == 3)
                policy = policy with { MaximumKarmaInvestment = 999m };
            return found;
        }
        public bool TryResolveCreationGearAuthority(out CharacterCreationGearAuthority authority)
        {
            bool found = inner.TryResolveCreationGearAuthority(out authority);
            GearReads++;
            if (fault == "gear" && GearReads == 2) authority = authority with { MaximumAvailability = 999 };
            return found;
        }
    }
}
