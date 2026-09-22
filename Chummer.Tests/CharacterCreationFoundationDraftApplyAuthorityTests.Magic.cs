using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    [DataRow("mundane", "mundane", 0)]
    [DataRow(MagicianTalentId, "magician", 5)]
    [DataRow(AdeptTalentId, "adept", 0)]
    [DataRow(MysticTalentId, "mystic-adept", 10)]
    [DataRow(TechnomancerTalentId, "technomancer", 4)]
    public void Life_module_magic_prices_typed_purchases_without_priority_grants(string talent, string kind, int karma)
    {
        var fixture = LifeMagicFixture(talent);
        Assert.IsTrue(CharacterCreationLifeModuleMagicRules.IsValidCatalog(fixture.Catalog));
        Assert.IsFalse(fixture.Context.TryResolveCreationKarmaMagicCatalog(out _));
        Assert.IsFalse(CharacterCreationKarmaMagicRules.IsValidPolicy(fixture.Catalog.Policy));
        Assert.AreEqual(CharacterCreationKarmaMagicPolicy.LifeModulesSchemaV1, fixture.Catalog.Policy.Schema);
        var selection = new CharacterCreationMagicResonanceSelections(
            kind is "magician" or "mystic-adept" ? LifeMagicOption(fixture.Catalog, "tradition").Identity : null,
            kind == "technomancer" ? LifeMagicOption(fixture.Catalog, "stream").Identity : null,
            kind is "adept" or "mystic-adept" ? [new(LifeMagicOption(fixture.Catalog, "adept-power", row => row.PointCost <= 1m).Identity, 1)] : [],
            kind is "magician" or "mystic-adept" ? [LifeMagicOption(fixture.Catalog, "spell").Identity] : [],
            kind == "technomancer" ? [LifeMagicOption(fixture.Catalog, "complex-form").Identity] : [])
            { MysticAdeptPowerPoints = kind == "mystic-adept" ? 1 : 0 };
        var result = fixture.Evaluate(selection);
        Assert.IsNotNull(result.Quote, string.Join(", ", result.Blockers));
        Assert.IsEmpty(result.Blockers, string.Join(", ", result.Blockers));
        var quote = result.Quote;
        Assert.AreEqual(kind, quote.TalentKind);
        if (kind == "magician")
        {
            Assert.HasCount(1, fixture.Catalog.Talents.SkillUnlockChoices[talent]);
            Assert.IsNull(fixture.Math.Talent.Selection.SkillUnlock);
            Assert.IsTrue(quote.Access.AllowsSpells, "The automatic Magician unlock is not an aspected choice.");
        }
        Assert.AreEqual(karma, quote.Cost.TotalKarma);
        Assert.AreEqual(fixture.Contacts.KarmaAfterContacts - karma, quote.KarmaAfterMagic);
        Assert.AreEqual(fixture.Contacts.QuoteDigest, quote.ContactsQuoteDigest);
        Assert.AreEqual(fixture.Resources.QuoteDigest, quote.ResourcesQuoteDigest);
        Assert.AreEqual(fixture.Catalog.AuthorityDigest, quote.SourceAuthorityDigest);
        Assert.IsTrue(quote.ProjectionCatalog.Catalogs.Sum(row => row.Options.Count) < fixture.Catalog.Catalogs.Sum(row => row.Options.Count));
        if (kind == "mystic-adept") Assert.AreEqual(0, quote.Cost.MysticPowerPoints!.ExchangedSpellSlots);
        Assert.AreEqual(HashContacts(quote with { QuoteDigest = string.Empty }), quote.QuoteDigest);
        Assert.AreEqual(JsonSerializer.Serialize(quote), JsonSerializer.Serialize(fixture.Evaluate(selection).Quote));
    }

    [TestMethod]
    public void Life_module_magic_requires_explicit_selection_and_the_correct_tradition_or_stream()
    {
        var fixture = LifeMagicFixture(MagicianTalentId);
        CollectionAssert.Contains(fixture.Evaluate(null).Blockers.ToArray(), CharacterCreationLifeModuleMagicQuote.SelectionRequired);
        CollectionAssert.Contains(fixture.Evaluate(new(null, null, [], [], [])).Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.TraditionRequired);
        var selection = new CharacterCreationMagicResonanceSelections(LifeMagicOption(fixture.Catalog, "tradition").Identity, null,
            [], [LifeMagicOption(fixture.Catalog, "spell").Identity], []);
        var wrong = fixture.Evaluate(selection with { ComplexForms = [LifeMagicOption(fixture.Catalog, "complex-form").Identity] });
        CollectionAssert.Contains(wrong.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.ComplexFormSelectionNotAllowed);
        Assert.IsNull(fixture.Evaluate(selection with { Spells = [selection.Spells[0], selection.Spells[0]] }).Quote);
        Assert.IsNull(fixture.Evaluate(selection with { Spells = [new("spell", Guid.NewGuid().ToString("D"))] }).Quote);
        var techno = LifeMagicFixture(TechnomancerTalentId);
        CollectionAssert.Contains(techno.Evaluate(new(null, null, [], [], [])).Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.StreamRequired);
        var mundane = LifeMagicFixture();
        Assert.IsEmpty(mundane.Evaluate(new(null, null, [], [], [])).Blockers);
        CollectionAssert.Contains(mundane.Evaluate(selection).Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.SpellSelectionNotAllowed);
    }

    [TestMethod]
    [DataRow("Sorcery", true)]
    [DataRow("Conjuring", false)]
    [DataRow("Enchanting", false)]
    public void Life_module_magic_honors_the_explicit_aspected_skill_choice(string unlock, bool allowed)
    {
        var fixture = LifeMagicFixture(AspectedTalentId, unlock);
        var result = fixture.Evaluate(new(LifeMagicOption(fixture.Catalog, "tradition").Identity, null,
            [], [LifeMagicOption(fixture.Catalog, "spell").Identity], []));
        Assert.IsNotNull(result.Quote, string.Join(", ", result.Blockers));
        Assert.AreEqual("aspected-magician", result.Quote.TalentKind);
        Assert.AreEqual(allowed, result.Quote.Access.AllowsSpells);
        Assert.AreEqual(allowed, result.Blockers.Count == 0);
    }

    [TestMethod]
    public void Life_module_magic_caps_spells_and_adept_power_levels_by_actual_magic()
    {
        var fixture = LifeMagicFixture(MagicianTalentId);
        int magic = fixture.Math.Attributes.Attributes.Single(row => row.AttributeId == "MAG").Current;
        var spells = fixture.Catalog.Catalogs.Single(row => row.Kind == "spell").Options
            .Where(row => row.Category != "Rituals" && CharacterCreationMagicResonanceFinalizationRules.TryProjectOption(row, 1, out _))
            .Take(magic * 2 + 1).Select(row => row.Identity).ToArray();
        Assert.AreEqual(magic * 2 + 1, spells.Length);
        CollectionAssert.Contains(fixture.Evaluate(new(LifeMagicOption(fixture.Catalog, "tradition").Identity, null, [], spells, [])).Blockers.ToArray(),
            CharacterCreationMagicResonanceBlockers.SpellBudgetExceeded);
        var adept = LifeMagicFixture(AdeptTalentId);
        int adeptMagic = adept.Math.Attributes.Attributes.Single(row => row.AttributeId == "MAG").Current;
        var power = LifeMagicOption(adept.Catalog, "adept-power", row => row.MaximumLevels > adeptMagic
            && row.PointCost * (adeptMagic + 1) <= adeptMagic);
        var over = adept.Evaluate(new(null, null, [new(power.Identity, adeptMagic + 1)], [], []));
        Assert.IsNotNull(over.Quote);
        Assert.IsTrue(over.Quote.PowerPointsUsed <= over.Quote.PowerPointsTotal);
        CollectionAssert.Contains(over.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.OptionInvalid);
    }

    [TestMethod]
    public void Life_module_magic_spending_precedes_carryover_and_never_changes_shopping_funds()
    {
        var fixture = LifeMagicFixture(MagicianTalentId);
        var selected = fixture.Evaluate(new(LifeMagicOption(fixture.Catalog, "tradition").Identity, null,
            [], [LifeMagicOption(fixture.Catalog, "spell").Identity], [])).Quote!;
        Assert.IsNotNull(selected);
        var math = fixture.Math;
        var gear = CharacterCreationLifeModuleGearRules.Evaluate(math.Xml, math.Effects, math.Racial, math.Talent,
            math.Attributes, fixture.Skills, fixture.Resources, 1000m, [], fixture.Context).Quote!;
        var lifestyles = CharacterCreationLifeModuleLifestylesRules.Evaluate(math.Xml, math.Effects, math.Racial, math.Talent,
            math.Attributes, fixture.Skills, fixture.Resources, gear, 1000m, [], null, fixture.Context).Quote!;
        var final = CharacterCreationLifeModuleFinalizationBudgetRules.Evaluate(math.Xml, math.Effects, math.Racial, math.Talent,
            math.Attributes, fixture.Skills, fixture.Resources, gear, lifestyles, fixture.Contacts, selected, 1000m, 4, fixture.Context);
        Assert.IsNotNull(final.Quote, string.Join(", ", final.Blockers));
        Assert.AreEqual((int)fixture.Contacts.KarmaAfterContacts - 5, final.Quote.KarmaBeforeCarryover);
        Assert.AreEqual(selected.QuoteDigest, final.Quote.MagicQuoteDigest);
        Assert.AreEqual(0m, fixture.Resources.NuyenFromKarma);
        Assert.AreEqual(80m, final.Quote.CareerNuyen);
        var forged = selected with { Cost = selected.Cost with { TotalKarma = 0 }, KarmaAfterMagic = selected.KarmaBeforeMagic };
        forged = forged with { QuoteDigest = HashContacts(forged with { QuoteDigest = string.Empty }) };
        Assert.IsNull(CharacterCreationLifeModuleFinalizationBudgetRules.Evaluate(math.Xml, math.Effects, math.Racial, math.Talent,
            math.Attributes, fixture.Skills, fixture.Resources, gear, lifestyles, fixture.Contacts, forged, 1000m, 4, fixture.Context).Quote);
    }

    [TestMethod]
    [DataRow("contacts")]
    [DataRow("resources")]
    [DataRow("magic-catalog")]
    [DataRow("existing-spells")]
    public void Life_module_magic_rejects_forged_budgets_source_drift_and_existing_inventory(string fault)
    {
        var fixture = LifeMagicFixture(MagicianTalentId);
        if (fault == "contacts")
        {
            var changed = fixture.Contacts with { KarmaAfterContacts = fixture.Contacts.KarmaAfterContacts + 10m };
            fixture = fixture with { Contacts = changed with { QuoteDigest = HashContacts(changed with { QuoteDigest = string.Empty }) } };
        }
        if (fault == "resources")
        {
            var changed = fixture.Resources with { KarmaAfterResources = fixture.Resources.KarmaAfterResources + 10m };
            fixture = fixture with { Resources = changed with { QuoteDigest = HashContacts(changed with { QuoteDigest = string.Empty }) } };
        }
        if (fault == "existing-spells")
        {
            var root = XElement.Parse(fixture.Math.Xml);
            root.Add(new XElement("spells", new XElement("spell", new XElement("name", "preserve existing spell"))));
            fixture = fixture with { Math = fixture.Math with { Xml = root.ToString(SaveOptions.DisableFormatting) } };
        }
        var context = new FinalBudgetDriftContext(fixture.Context, fault);
        var result = fixture.Evaluate(new(LifeMagicOption(fixture.Catalog, "tradition").Identity, null, [], [], []), context);
        Assert.IsNull(result.Quote);
        CollectionAssert.Contains(result.Blockers.ToArray(), fault == "existing-spells"
            ? CharacterCreationFoundationBlockers.PendingDraftConflict : CharacterCreationFoundationBlockers.SourceDigestConflict);
        if (fault == "magic-catalog") Assert.AreEqual(2, context.MagicReads);
    }

    [TestMethod]
    public void Life_module_magic_uses_profile_prices_and_blocks_karma_overdraw()
    {
        var fixture = LifeMagicFixture(MagicianTalentId, remaining: 1m);
        var result = fixture.Evaluate(new(LifeMagicOption(fixture.Catalog, "tradition").Identity, null,
            [], [LifeMagicOption(fixture.Catalog, "spell").Identity], []));
        Assert.IsNotNull(result.Quote, string.Join(", ", result.Blockers));
        Assert.AreEqual(-4m, result.Quote.KarmaAfterMagic);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationAttributesBlockers.GlobalKarmaExceeded);
        var source = XElement.Parse(fixture.Catalog.Policy.CanonicalSourceXml);
        source.Element("karmacost")!.SetElementValue("karmaspell", "7");
        source.Element("karmacost")!.SetElementValue("karmanewcomplexform", "11");
        source.Element("karmacost")!.SetElementValue("karmamysadpp", "9");
        source.SetElementValue("priorityspellsasadeptpowers", "True");
        Assert.IsTrue(CharacterCreationKarmaMagicRules.TryCreateLifeModulePolicy(fixture.Catalog.SettingsProfileId,
            fixture.Catalog.Policy.SettingsInputsDigest, source.ToString(SaveOptions.DisableFormatting), out var policy));
        Assert.IsTrue(CharacterCreationKarmaMagicRules.TryCalculateCost(policy, "mystic-adept", 3, 2, 0, 2, true, out var cost));
        Assert.AreEqual(32, cost!.TotalKarma);
        Assert.AreEqual(0, cost.MysticPowerPoints!.ExchangedSpellSlots);
        Assert.IsFalse(CharacterCreationKarmaMagicRules.TryCalculateCost(policy, "mystic-adept", 3, 2, 0, 2, out _));
        Assert.IsFalse(CharacterCreationKarmaMagicRules.TryCalculateCost(policy, "mystic-adept", 3, 2, 0, 4, true, out _));
    }

    private static CharacterCreationMagicResonanceCatalogOption LifeMagicOption(CharacterCreationLifeModuleMagicCatalog catalog,
        string kind, Func<CharacterCreationMagicResonanceCatalogOption, bool>? predicate = null)
        => catalog.Catalogs.Single(row => row.Kind == kind).Options.First(row => (predicate is null || predicate(row))
            && CharacterCreationMagicResonanceFinalizationRules.TryProjectOption(row, 1, out _));

    private static SkillMathBinding LifeSourceBoundTalentMath(string talentId = "mundane", string? unlock = null)
    {
        var basis = SkillMathFixture();
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(basis.Xml)!;
        Assert.IsTrue(context.TryResolveCreationFoundationEffectSources(out var sources));
        Assert.IsTrue(sources!.TryCreateAuthorities(out _, out _, out _, out string sourceDigest));
        var (effects, racial) = SealAttributeMathPlans(basis.Effects with { SourceContextDigest = sourceDigest }, basis.Racial);
        var result = CharacterCreationLifeModuleTalentWritePlanner.Build(basis.Xml, effects, racial, new(talentId, unlock), context);
        Assert.IsNotNull(result.Plan, string.Join(", ", result.Blockers));
        var talent = result.Plan;
        var attributes = CharacterCreationLifeModuleAttributeRules.Evaluate(basis.Xml, effects, racial, basis.Attributes.Policy,
            talent.Talent.EnabledAttribute is { } attribute ? [new(attribute, 3)] : [], talent);
        Assert.IsNotNull(attributes.Quote, string.Join(", ", attributes.Blockers));
        return basis with { Effects = effects, Racial = racial, Talent = talent, Attributes = attributes.Quote };
    }

    private static LifeMagicBinding LifeMagicFixture(string talent = "mundane", string? unlock = null, decimal? remaining = null)
    {
        var math = LifeSourceBoundTalentMath(talent, unlock);
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(math.Xml)!;
        var native = math.Catalog.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
        var skills = CharacterCreationLifeModuleSkillsRules.Evaluate(math.Xml, math.Effects, math.Racial, math.Talent, math.Attributes,
            new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)], []), context).Quote!;
        Assert.IsNotNull(skills);
        var resources = CharacterCreationLifeModuleResourcesRules.Evaluate(math.Xml, math.Effects, math.Racial, math.Talent,
            math.Attributes, skills, 1000m, 0m, context).Quote!;
        Assert.IsNotNull(resources);
        decimal total = 1000m;
        if (remaining is { } available)
        {
            total = resources.TotalKarma - resources.KarmaAfterResources + available;
            resources = CharacterCreationLifeModuleResourcesRules.Evaluate(math.Xml, math.Effects, math.Racial, math.Talent,
                math.Attributes, skills, total, 0m, context).Quote!;
            Assert.IsNotNull(resources);
        }
        var contacts = CharacterCreationLifeModuleContactsRules.Evaluate(math.Xml, math.Effects, math.Racial, math.Talent,
            math.Attributes, skills, resources, total, [], context).Quote!;
        Assert.IsNotNull(contacts);
        Assert.IsTrue(context.TryResolveCreationLifeModuleMagicCatalog(out var catalog));
        return new(math, context, skills, resources, contacts, catalog!, total);
    }

    private sealed record LifeMagicBinding(SkillMathBinding Math, ICharacterSourceDataContext Context,
        CharacterCreationLifeModuleSkillsQuote Skills, CharacterCreationLifeModuleResourcesQuote Resources,
        CharacterCreationLifeModuleContactsQuote Contacts, CharacterCreationLifeModuleMagicCatalog Catalog, decimal TotalKarma)
    {
        internal CharacterCreationLifeModuleMagicResult Evaluate(CharacterCreationMagicResonanceSelections? selections, ICharacterSourceDataContext? context = null)
            => CharacterCreationLifeModuleMagicRules.Evaluate(Math.Xml, Math.Effects, Math.Racial, Math.Talent,
                Math.Attributes, Skills, Resources, Contacts, TotalKarma, selections, context ?? Context);
    }
}
