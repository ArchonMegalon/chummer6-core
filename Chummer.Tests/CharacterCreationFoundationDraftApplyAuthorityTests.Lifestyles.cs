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
    public void Life_module_lifestyles_share_gear_funding_and_keep_the_review_read_only()
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
            var first = service.PreviewFinalization(request).Value!;
            var native = first.SkillsCatalog!.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
            request = request with
            {
                SkillSelection = new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)], []),
                KarmaResourceInvestment = 2m, GearSelection = []
            };
            var missing = service.PreviewFinalization(request).Value!;
            Assert.IsNotNull(missing.LifestylesAuthority, string.Join(", ", missing.FinalizationBlocked));
            Assert.IsNull(missing.LifestylesQuote);
            CollectionAssert.Contains(missing.FinalizationBlocked.ToArray(), CharacterCreationLifeModuleLifestylesQuote.SelectionRequired);
            var low = LifeLifestyle(missing.LifestylesAuthority, "Low");
            var item = missing.GearAuthority!.Options.First(row => row.IsSelectable && row.PackageCost is > 0 and < 1000);
            request = request with { GearSelection = [new(item.OptionId, item.PackageQuantity)],
                LifestyleSelection = [low], StartingLifestyleId = low.LifestyleId };
            var preview = service.PreviewFinalization(request).Value!;
            Assert.IsNotNull(preview.LifestylesQuote, string.Join(", ", preview.FinalizationBlocked));
            var quote = preview.LifestylesQuote;
            Assert.IsEmpty(quote.Blockers, string.Join(", ", quote.Blockers));
            Assert.AreEqual(2000m, quote.LifestyleNuyenUsed);
            Assert.AreEqual(4000m, quote.Budget.Total);
            Assert.AreEqual(2000m + item.PackageCost, quote.Budget.Used);
            Assert.AreEqual(2000m - item.PackageCost, quote.Budget.Remaining);
            Assert.AreEqual(preview.GearQuote!.QuoteDigest, quote.GearQuoteDigest);
            Assert.AreEqual(preview.ResourcesQuote!.QuoteDigest, quote.ResourcesQuoteDigest);
            Assert.AreEqual(low.LifestyleId, quote.StartingLifestyleId);
            Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quote with { QuoteDigest = string.Empty }), quote.QuoteDigest);
            var confirm = new CharacterCreationFoundationFinalizationConfirmRequest(request.Binding, request.DraftRevision,
                request.DraftDigest, preview.PreviewDigest, true)
            {
                QualityInstanceValues = request.QualityInstanceValues, TalentSelection = request.TalentSelection,
                SkillSelection = request.SkillSelection, KarmaResourceInvestment = request.KarmaResourceInvestment,
                GearSelection = request.GearSelection, LifestyleSelection = request.LifestyleSelection,
                StartingLifestyleId = request.StartingLifestyleId
            };
            var omitted = service.ConfirmFinalization(confirm with { StartingLifestyleId = null });
            CollectionAssert.Contains(omitted.Blockers.ToArray(), CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch);
            var exact = service.ConfirmFinalization(confirm);
            Assert.IsFalse(exact.Blockers.Contains(CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch));
            Assert.IsFalse(preview.CanApply);
            var reopened = CreateService(new FileWorkspaceStore(directory)).PreviewFinalization(request).Value!;
            Assert.AreEqual(JsonSerializer.Serialize(quote), JsonSerializer.Serialize(reopened.LifestylesQuote));
            CollectionAssert.AreEqual(fixture.Before, File.ReadAllBytes(WorkspacePath(directory, fixture.Id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Life_module_lifestyles_do_not_fund_purchases_with_future_cash_or_an_unowned_tier()
    {
        var fixture = LifeLifestyleFixture(0m);
        var absent = fixture.Quote(null, null);
        Assert.IsNull(absent.Quote);
        CollectionAssert.Contains(absent.Blockers.ToArray(), CharacterCreationLifeModuleLifestylesQuote.SelectionRequired);
        var empty = fixture.Quote([], null);
        Assert.IsNotNull(empty.Quote);
        Assert.IsEmpty(empty.Blockers);
        Assert.AreEqual(0m, empty.Quote.Budget.Remaining);
        Assert.IsEmpty(empty.Quote.Lines);
        var low = LifeLifestyle(fixture.Authority, "Low");
        var unpaid = fixture.Quote([low], low.LifestyleId);
        Assert.IsNotNull(unpaid.Quote);
        Assert.AreEqual(-2000m, unpaid.Quote.Budget.Remaining);
        Assert.AreEqual(2000m, unpaid.Quote.Budget.Overspend);
        CollectionAssert.Contains(unpaid.Blockers.ToArray(), CharacterCreationLifestylesBlockers.InsufficientFunds);
        foreach (var result in new[] { fixture.Quote([], low.LifestyleId), fixture.Quote([low], null), fixture.Quote([low], Guid.NewGuid()) })
            CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationLifeModuleLifestylesQuote.StartingLifestyleRequired);
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("zero-months")]
    [DataRow("unknown")]
    [DataRow("null-row")]
    [DataRow("catalog")]
    [DataRow("funding")]
    [DataRow("gear")]
    public void Life_module_lifestyles_reject_invalid_selections_and_changed_bindings(string fault)
    {
        var fixture = LifeLifestyleFixture();
        var low = LifeLifestyle(fixture.Authority, "Low");
        CharacterCreationLifestyleConfiguration[] selections = fault switch
        {
            "duplicate" => [low, low],
            "zero-months" => [low with { Increments = 0 }],
            "unknown" => [low with { BaseLifestyleOptionId = "lifestyle:00000000-0000-0000-0000-000000000001" }],
            "null-row" => [null!],
            _ => [low]
        };
        if (fault == "catalog")
        {
            var options = fixture.Authority.LifestyleOptions.ToArray();
            options[0] = options[0] with { BaseCost = options[0].BaseCost + 1m };
            var changed = fixture.Authority with { LifestyleOptions = options };
            changed = changed with { AuthorityDigest = CharacterCreationLifestylesRules.ComputeAuthorityDigest(changed) };
            fixture = fixture with { Authority = changed };
        }
        if (fault == "funding") fixture = fixture with { Resources = fixture.Resources with { NuyenFromKarma = 999999m } };
        if (fault == "gear") fixture = fixture with { Gear = fixture.Gear with { ResourcesQuoteDigest = "changed" } };
        var result = fixture.Quote(selections, low.LifestyleId);
        Assert.IsNull(result.Quote, fault);
        Assert.IsNotEmpty(result.Blockers, fault);
    }

    [TestMethod]
    public void Life_module_lifestyles_apply_pending_trust_fund_and_racial_costs_without_dropping_effects()
    {
        var fixture = LifeLifestyleFixture();
        var math = fixture.Math;
        var racial = math.Racial with { ImprovementXml = [.. math.Racial.ImprovementXml,
            CharacterCreationAwakenedLegacyProjector.Improvement("LifestyleCost", "", "metatype", "Metatype", 50)
                .ToString(System.Xml.Linq.SaveOptions.DisableFormatting)], PlanDigest = string.Empty };
        racial = racial with { PlanDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(racial) };
        var talent = math.Talent with { MetatypePlanDigest = racial.PlanDigest, PlanDigest = string.Empty };
        talent = talent with { PlanDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(talent) };
        Assert.IsTrue(CharacterCreationLifeModuleLifestylesRules.TryEffectiveAuthority(fixture.Authority,
            math.Effects, racial, talent, out var effective));
        Assert.AreEqual(50m, effective.MetatypeCostPercent);
        var low = LifeLifestyle(effective, "Low");
        Assert.IsTrue(CharacterCreationLifestylesRules.TryProject(low, effective, out var projection, out _));
        Assert.AreEqual(3000m, projection.Economics.TotalCost);

        math = SkillMathFixture(("TrustFund", "", 1m));
        Assert.IsTrue(CharacterCreationLifeModuleLifestylesRules.TryEffectiveAuthority(fixture.Authority,
            math.Effects, math.Racial, math.Talent, out effective));
        Assert.AreEqual(1, effective.TrustFundLevel);
        var medium = LifeLifestyle(effective, "Medium") with { TrustFund = true };
        Assert.IsTrue(CharacterCreationLifestylesRules.TryProject(medium, effective, out projection, out _));
        Assert.IsTrue(projection.Economics.CoveredByTrustFund);
        Assert.AreEqual(0m, projection.Economics.TotalCost);

        math = fixture.Math;
        var (unsupported, unsupportedRacial) = SealAttributeMathPlans(math.Effects with
        {
            ImprovementXml = [CharacterCreationAwakenedLegacyProjector.Improvement("BasicLifestyleCost", "", "quality", "Quality", 50)
                .ToString(System.Xml.Linq.SaveOptions.DisableFormatting)]
        }, math.Racial);
        var unsupportedTalent = math.Talent with { EffectPlanDigest = unsupported.PlanDigest,
            MetatypePlanDigest = unsupportedRacial.PlanDigest, PlanDigest = string.Empty };
        unsupportedTalent = unsupportedTalent with { PlanDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(unsupportedTalent) };
        Assert.IsFalse(CharacterCreationLifeModuleLifestylesRules.TryEffectiveAuthority(fixture.Authority,
            unsupported, unsupportedRacial, unsupportedTalent, out _), "Unsupported precedence cannot silently disappear.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Life_module_lifestyles_readmit_gear_and_detect_source_drift(bool forgeGear)
    {
        var fixture = LifeLifestyleFixture();
        var math = fixture.Math;
        var gear = fixture.Gear;
        if (forgeGear)
        {
            gear = gear with { Budget = gear.Budget with { BasketCost = 1m, RemainingNuyen = 3999m } };
            gear = gear with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(gear with { QuoteDigest = string.Empty }) };
        }
        var changing = new LifestyleDriftContext(fixture.Context);
        var result = CharacterCreationLifeModuleLifestylesRules.Evaluate(math.Xml, math.Effects, math.Racial,
            math.Talent, math.Attributes, fixture.Skills, fixture.Resources, gear, 1000m, [], null,
            forgeGear ? fixture.Context : changing);
        if (!forgeGear) Assert.AreEqual(2, changing.Reads);
        Assert.IsNull(result.Quote);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationFoundationBlockers.SourceDigestConflict);
    }

    private static CharacterCreationLifestyleConfiguration LifeLifestyle(CharacterCreationLifestylesAuthority authority, string name)
    {
        var option = authority.LifestyleOptions.Single(row => row.Name == name);
        Assert.IsTrue(option.IsSelectable, string.Join(", ", option.Blockers));
        return new(Guid.NewGuid(), option.OptionId, name + " home", CharacterCreationLifestyleStyleIds.Standard,
            option.DefaultIncrementId, 1, 100m, 0, false, false, 0, 0, 0, 0, "", "", "", []);
    }

    private static LifeLifestyleBinding LifeLifestyleFixture(decimal investment = 2m)
    {
        var math = SkillMathFixture();
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(math.Xml)!;
        var native = math.Catalog.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
        var skills = CharacterCreationLifeModuleSkillsRules.Evaluate(math.Xml, math.Effects, math.Racial, math.Talent,
            math.Attributes, new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)], []), context).Quote!;
        var resources = CharacterCreationLifeModuleResourcesRules.Evaluate(math.Xml, math.Effects, math.Racial,
            math.Talent, math.Attributes, skills, 1000m, investment, context).Quote!;
        Assert.IsNotNull(resources);
        var gear = CharacterCreationLifeModuleGearRules.Evaluate(math.Xml, math.Effects, math.Racial, math.Talent,
            math.Attributes, skills, resources, 1000m, [], context).Quote!;
        Assert.IsNotNull(gear);
        Assert.IsTrue(context.TryResolveCreationLifestylesAuthority(out var authority));
        return new(math, context, skills, resources, gear, authority);
    }

    private sealed record LifeLifestyleBinding(SkillMathBinding Math, ICharacterSourceDataContext Context,
        CharacterCreationLifeModuleSkillsQuote Skills, CharacterCreationLifeModuleResourcesQuote Resources,
        CharacterCreationLifeModuleGearQuote Gear, CharacterCreationLifestylesAuthority Authority)
    {
        internal CharacterCreationLifeModuleLifestylesQuoteResult Quote(
            IReadOnlyList<CharacterCreationLifestyleConfiguration>? choices, Guid? starting)
            => CharacterCreationLifeModuleLifestylesRules.Quote(Authority, Authority.AuthorityDigest, Resources, Gear, choices, starting);
    }

    private sealed class LifestyleDriftContext(ICharacterSourceDataContext inner) : ICharacterSourceDataContext
    {
        internal int Reads { get; private set; }
        public bool TryResolveCyberwareGradeDeviceRating(string sourceId, string grade, out int rating)
            => inner.TryResolveCyberwareGradeDeviceRating(sourceId, grade, out rating);
        public bool TryResolveVehicleModBonuses(string sourceId, string grade, out CharacterVehicleModSourceBonuses bonuses)
            => inner.TryResolveVehicleModBonuses(sourceId, grade, out bonuses);
        public bool TryResolveCreationSkillsCatalog(out CharacterCreationSkillsCatalog? catalog)
            => inner.TryResolveCreationSkillsCatalog(out catalog);
        public bool TryResolveCreationLifeModuleSkillsPolicy(out CharacterCreationKarmaSkillsPolicy? policy)
            => inner.TryResolveCreationLifeModuleSkillsPolicy(out policy);
        public bool TryResolveCreationLifeModuleResourcesPolicy(out CharacterCreationKarmaResourcesPolicy? policy)
            => inner.TryResolveCreationLifeModuleResourcesPolicy(out policy);
        public bool TryResolveCreationGearAuthority(out CharacterCreationGearAuthority authority)
            => inner.TryResolveCreationGearAuthority(out authority);
        public bool TryResolveCreationLifestylesAuthority(out CharacterCreationLifestylesAuthority authority)
        {
            bool found = inner.TryResolveCreationLifestylesAuthority(out authority);
            if (++Reads == 2) authority = authority with { MetatypeCostPercent = 999m };
            return found;
        }
    }
}
