using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    [DataRow("mundane")]
    [DataRow(MagicianTalentId)]
    public void Life_module_final_budget_binds_explicit_dice_and_reopens_without_mutating_the_draft(string talentId)
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = SeedFullGraph(directory);
            var (_, baseline) = BuildFullGraph(fixture.Store, fixture.Id);
            var service = CreateService(fixture.Store);
            var prompt = baseline.ModuleSequence!.QualityLevels.Single().InstancePrompt!;
            var request = QualityInstanceRequest(service, fixture.Id, new Dictionary<string, string> { [prompt.PromptId] = "Renraku" })
                with { TalentSelection = new(talentId), AttributePurchases = talentId == "mundane" ? [] : [new("MAG", 3)] };
            var first = service.PreviewFinalization(request).Value!;
            var native = first.SkillsCatalog!.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
            request = request with { SkillSelection = new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)], []),
                KarmaResourceInvestment = 0m, GearSelection = [], LifestyleSelection = [], ContactSelection = [],
                MagicSelection = new(null, null, [], [], []) };
            if (talentId != "mundane")
            {
                var chooser = service.PreviewFinalization(request).Value!;
                Assert.IsNotNull(chooser.MagicCatalog, string.Join(", ", chooser.FinalizationBlocked));
                request = request with { MagicSelection = new(LifeMagicOption(chooser.MagicCatalog, "tradition").Identity, null,
                    [], [LifeMagicOption(chooser.MagicCatalog, "spell").Identity], []) };
            }
            var missing = service.PreviewFinalization(request).Value!;
            Assert.IsNotNull(missing.CarryoverPolicy, string.Join(", ", missing.FinalizationBlocked));
            Assert.IsNotNull(missing.StartingCashSource);
            Assert.AreEqual("Street", missing.StartingCashSource.Name);
            Assert.IsNull(missing.FinalizationBudget);
            CollectionAssert.Contains(missing.FinalizationBlocked.ToArray(), CharacterCreationLifeModuleFinalizationBudgetQuote.DiceRequired);
            request = request with { StartingNuyenDiceTotal = 4 };
            var preview = service.PreviewFinalization(request).Value!;
            Assert.IsNotNull(preview.FinalizationBudget, string.Join(", ", preview.FinalizationBlocked));
            Assert.AreEqual(80m, preview.FinalizationBudget.CareerNuyen);
            Assert.AreEqual(0m, preview.ResourcesQuote!.NuyenFromKarma);
            Assert.AreEqual(0m, preview.LifestylesQuote!.Budget.Remaining);
            Assert.IsTrue(preview.CanApply, "The complete source-replayed runner has an atomic finalization path.");
            var confirm = new CharacterCreationFoundationFinalizationConfirmRequest(request.Binding, request.DraftRevision,
                request.DraftDigest, preview.PreviewDigest, true)
            {
                QualityInstanceValues = request.QualityInstanceValues, TalentSelection = request.TalentSelection,
                AttributePurchases = request.AttributePurchases,
                SkillSelection = request.SkillSelection, KarmaResourceInvestment = request.KarmaResourceInvestment,
                GearSelection = request.GearSelection, LifestyleSelection = request.LifestyleSelection,
                ContactSelection = request.ContactSelection, StartingNuyenDiceTotal = request.StartingNuyenDiceTotal,
                MagicSelection = request.MagicSelection
            };
            foreach (int? changedDice in new int?[] { null, 5 })
                CollectionAssert.Contains(service.ConfirmFinalization(confirm with { StartingNuyenDiceTotal = changedDice }).Blockers.ToArray(),
                    CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch);
            CollectionAssert.Contains(service.ConfirmFinalization(confirm with { MagicSelection = null }).Blockers.ToArray(),
                CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch);
            Assert.IsNotNull(preview.FinalizationPlan);
            var reopened = CreateService(new FileWorkspaceStore(directory)).PreviewFinalization(request).Value!;
            Assert.AreEqual(JsonSerializer.Serialize(preview.FinalizationBudget), JsonSerializer.Serialize(reopened.FinalizationBudget));
            Assert.AreEqual(preview.PreviewDigest, reopened.PreviewDigest);
            CollectionAssert.AreEqual(fixture.Before, File.ReadAllBytes(WorkspacePath(directory, fixture.Id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Life_module_final_budget_accounts_for_contacts_lifestyle_and_fractional_resource_investment()
    {
        var fixture = LifeFinalBudgetFixture(10.25m, "Low", [LifeContact(2, 2), LifeContact(2, 2), LifeContact(2, 2)]);
        var result = fixture.Evaluate(7);
        Assert.IsNotNull(result.Quote, string.Join(", ", result.Blockers));
        var quote = result.Quote;
        Assert.AreEqual(0.75m, quote.ResourceKarmaRoundingAdjustment);
        Assert.AreEqual((int)(fixture.Contacts.KarmaAfterContacts - 0.75m), quote.KarmaBeforeCarryover);
        Assert.IsTrue(fixture.Contacts.KarmaUsed > 0);
        Assert.AreEqual(7, quote.KarmaCarried);
        Assert.AreEqual(quote.KarmaBeforeCarryover - 7, quote.KarmaDiscarded);
        Assert.AreEqual(18500m, quote.NuyenBeforeCarryover);
        Assert.AreEqual(5000m, quote.NuyenCarried);
        Assert.AreEqual(13500m, quote.NuyenDiscarded);
        Assert.AreEqual(420m, quote.LifestyleStartingNuyen);
        Assert.AreEqual(5420m, quote.CareerNuyen);
        Assert.AreEqual(fixture.Lifestyles.QuoteDigest, quote.LifestylesQuoteDigest);
        Assert.AreEqual(fixture.Contacts.QuoteDigest, quote.ContactsQuoteDigest);
        Assert.AreEqual(HashContacts(quote with { QuoteDigest = string.Empty }), quote.QuoteDigest);
        Assert.IsTrue(quote.SourceAnchorIds.Contains("SR5:369"));
        var copy = JsonSerializer.Deserialize<CharacterCreationLifeModuleFinalizationBudgetQuote>(JsonSerializer.Serialize(quote));
        Assert.AreEqual(JsonSerializer.Serialize(quote), JsonSerializer.Serialize(copy));
    }

    [TestMethod]
    public void Life_module_final_budget_honors_profile_caps_without_changing_creation_money()
    {
        var fixture = LifeFinalBudgetFixture(1.25m);
        var current = fixture.Evaluate(4);
        Assert.IsNotNull(current.Quote, string.Join(", ", current.Blockers));
        var policy = current.Policy! with { MaximumKarma = 2, MaximumNuyen = 123.45m };
        policy = policy with { AuthorityDigest = CharacterCreationKarmaFinalizationBudgetRules.PolicyDigest(policy) };
        var result = CharacterCreationLifeModuleFinalizationBudgetRules.Quote(policy, current.StartingCashSource!,
            fixture.Base.Resources, fixture.Lifestyles, fixture.Contacts, fixture.Magic, 4).Quote!;
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.KarmaCarried);
        Assert.AreEqual(2500m, result.NuyenBeforeCarryover);
        Assert.AreEqual(123.45m, result.NuyenCarried);
        Assert.AreEqual(2376.55m, result.NuyenDiscarded);
        Assert.AreEqual(203.45m, result.CareerNuyen);
        Assert.AreEqual(2500m, fixture.Base.Resources.NuyenFromKarma);
        var zero = policy with { MaximumKarma = 0, MaximumNuyen = 0m };
        zero = zero with { AuthorityDigest = CharacterCreationKarmaFinalizationBudgetRules.PolicyDigest(zero) };
        var zeroQuote = CharacterCreationLifeModuleFinalizationBudgetRules.Quote(zero, current.StartingCashSource!,
            fixture.Base.Resources, fixture.Lifestyles, fixture.Contacts, fixture.Magic, 4).Quote!;
        Assert.IsNotNull(zeroQuote);
        Assert.AreEqual(0, zeroQuote.KarmaCarried);
        Assert.AreEqual(0m, zeroQuote.NuyenCarried);
        Assert.AreEqual(80m, zeroQuote.CareerNuyen);
        Assert.IsFalse(fixture.Base.Context.TryResolveCreationKarmaCarryoverPolicy(out _));
        Assert.IsFalse(fixture.Base.Context.TryResolveCreationKarmaDefaultStartingNuyen(out _));
        Assert.IsFalse(fixture.Base.Context.TryResolveCreationKarmaStartingNuyen(Guid.Parse(result.StartingCashSource.SourceId), out _));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(7)]
    [DataRow(int.MaxValue)]
    public void Life_module_final_budget_rejects_impossible_street_rolls(int dice)
    {
        var result = LifeFinalBudgetFixture().Evaluate(dice);
        Assert.IsNull(result.Quote);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationLifeModuleFinalizationBudgetQuote.BudgetInvalid);
    }

    [TestMethod]
    public void Life_module_final_budget_cannot_use_future_cash_to_buy_an_unfunded_lifestyle()
    {
        var fixture = LifeFinalBudgetFixture(0m, "Low");
        Assert.IsTrue(fixture.Lifestyles.Budget.Remaining < 0);
        Assert.IsNull(fixture.Evaluate(12).Quote);
    }

    [TestMethod]
    public void Life_module_final_budget_rejects_unowned_starting_cash_and_default_substitution()
    {
        var street = LifeFinalBudgetFixture();
        var low = LifeFinalBudgetFixture(2m, "Low");
        var streetSource = street.Evaluate(4).StartingCashSource!;
        var lowSource = low.Evaluate(7).StartingCashSource!;
        Assert.IsNotNull(streetSource);
        Assert.IsNotNull(lowSource);
        var policy = street.Evaluate(4).Policy!;
        Assert.IsNull(CharacterCreationLifeModuleFinalizationBudgetRules.Quote(policy, lowSource,
            street.Base.Resources, street.Lifestyles, street.Contacts, street.Magic, 7).Quote);
        Assert.IsNull(CharacterCreationLifeModuleFinalizationBudgetRules.Quote(policy, streetSource,
            low.Base.Resources, low.Lifestyles, low.Contacts, low.Magic, 4).Quote);
        Assert.IsFalse(street.Base.Context.TryResolveCreationLifeModuleStartingNuyen(Guid.Empty, out _));
        Assert.IsFalse(street.Base.Context.TryResolveCreationLifeModuleStartingNuyen(Guid.NewGuid(), out _));
    }

    [TestMethod]
    [DataRow("contacts")]
    [DataRow("lifestyles")]
    [DataRow("policy")]
    [DataRow("source")]
    [DataRow("magic")]
    public void Life_module_final_budget_rechecks_semantics_and_final_source_authority(string fault)
    {
        var fixture = LifeFinalBudgetFixture();
        if (fault == "contacts")
        {
            var contacts = fixture.Contacts with { KarmaAfterContacts = fixture.Contacts.KarmaAfterContacts + 1m };
            fixture = fixture with { Contacts = contacts with { QuoteDigest = HashContacts(contacts with { QuoteDigest = string.Empty }) } };
        }
        if (fault == "lifestyles")
        {
            var lifestyles = fixture.Lifestyles with { Budget = fixture.Lifestyles.Budget with { Remaining = 999999m } };
            fixture = fixture with { Lifestyles = lifestyles with { QuoteDigest = HashContacts(lifestyles with { QuoteDigest = string.Empty }) } };
        }
        var context = new FinalBudgetDriftContext(fixture.Base.Context, fault);
        var result = fixture.Evaluate(4, context);
        Assert.IsNull(result.Quote);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationFoundationBlockers.SourceDigestConflict);
        if (fault == "policy") Assert.AreEqual(2, context.PolicyReads);
        if (fault == "source") Assert.AreEqual(2, context.SourceReads);
        if (fault == "magic") Assert.AreEqual(3, context.MagicReads);
    }

    private static LifeFinalBudgetBinding LifeFinalBudgetFixture(decimal investment = 0m, string? lifestyle = null,
        IReadOnlyList<CharacterCreationKarmaContactSelection>? contacts = null)
    {
        var fixture = LifeLifestyleFixture(investment);
        var choice = lifestyle is null ? null : LifeLifestyle(fixture.Authority, lifestyle);
        var math = fixture.Math;
        var lifestyles = CharacterCreationLifeModuleLifestylesRules.Evaluate(math.Xml, math.Effects, math.Racial, math.Talent,
            math.Attributes, fixture.Skills, fixture.Resources, fixture.Gear, 1000m, choice is null ? [] : [choice],
            choice?.LifestyleId, fixture.Context).Quote!;
        var contactQuote = CharacterCreationLifeModuleContactsRules.Evaluate(math.Xml, math.Effects, math.Racial, math.Talent,
            math.Attributes, fixture.Skills, fixture.Resources, 1000m, contacts ?? [], fixture.Context).Quote!;
        Assert.IsNotNull(lifestyles);
        Assert.IsNotNull(contactQuote);
        var magic = CharacterCreationLifeModuleMagicRules.Evaluate(math.Xml, math.Effects, math.Racial, math.Talent,
            math.Attributes, fixture.Skills, fixture.Resources, contactQuote, 1000m, new(null, null, [], [], []), fixture.Context);
        Assert.IsNotNull(magic.Quote, string.Join(", ", magic.Blockers));
        return new(fixture, lifestyles, contactQuote, magic.Quote);
    }

    private sealed record LifeFinalBudgetBinding(LifeLifestyleBinding Base, CharacterCreationLifeModuleLifestylesQuote Lifestyles,
        CharacterCreationLifeModuleContactsQuote Contacts, CharacterCreationLifeModuleMagicQuote Magic)
    {
        internal CharacterCreationLifeModuleFinalizationBudgetResult Evaluate(int? dice, ICharacterSourceDataContext? context = null)
            => CharacterCreationLifeModuleFinalizationBudgetRules.Evaluate(Base.Math.Xml, Base.Math.Effects, Base.Math.Racial,
                Base.Math.Talent, Base.Math.Attributes, Base.Skills, Base.Resources, Base.Gear, Lifestyles, Contacts, Magic, 1000m, dice, context ?? Base.Context);
    }

    private sealed class FinalBudgetDriftContext(ICharacterSourceDataContext inner, string fault) : ICharacterSourceDataContext
    {
        internal int PolicyReads { get; private set; }
        internal int SourceReads { get; private set; }
        internal int MagicReads { get; private set; }
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
            => inner.TryResolveCreationLifeModuleResourcesPolicy(out policy);
        public bool TryResolveCreationGearAuthority(out CharacterCreationGearAuthority authority)
            => inner.TryResolveCreationGearAuthority(out authority);
        public bool TryResolveCreationLifestylesAuthority(out CharacterCreationLifestylesAuthority authority)
            => inner.TryResolveCreationLifestylesAuthority(out authority);
        public bool TryResolveCreationContactsPolicy(out CharacterCreationKarmaContactsPolicy? policy)
            => inner.TryResolveCreationContactsPolicy(out policy);
        public bool TryResolveCreationFoundationEffectSources(out CharacterCreationFoundationEffectSources? sources)
            => inner.TryResolveCreationFoundationEffectSources(out sources);
        public bool TryResolveCreationLifeModuleTalents(out CharacterCreationLifeModuleTalentCatalog? catalog)
            => inner.TryResolveCreationLifeModuleTalents(out catalog);
        public bool TryResolveCreationLifeModuleTalentSource(string id, out CharacterCreationTalentQualitySource? source)
            => inner.TryResolveCreationLifeModuleTalentSource(id, out source);
        public bool TryResolveCreationLifeModuleMagicCatalog(out CharacterCreationLifeModuleMagicCatalog? catalog)
        {
            bool found = inner.TryResolveCreationLifeModuleMagicCatalog(out catalog);
            if (++MagicReads == (fault == "magic-catalog" ? 2 : 3) && (fault is "magic" or "magic-catalog") && catalog is not null)
            {
                catalog = catalog with { CustomDataInputsDigest = "sha256:" + new string('0', 64) };
                catalog = catalog with { AuthorityDigest = CharacterCreationLifeModuleMagicRules.ComputeCatalogDigest(catalog) };
            }
            return found;
        }
        public bool TryResolveCreationCarryoverPolicy(out CharacterCreationKarmaCarryoverPolicy? policy)
        {
            bool found = inner.TryResolveCreationCarryoverPolicy(out policy);
            if (++PolicyReads == 2 && fault == "policy" && policy is not null)
            {
                policy = policy with { MaximumKarma = policy.MaximumKarma + 1 };
                policy = policy with { AuthorityDigest = CharacterCreationKarmaFinalizationBudgetRules.PolicyDigest(policy) };
            }
            return found;
        }
        public bool TryResolveCreationDefaultStartingNuyen(out CharacterCreationStartingNuyenSource? source)
        {
            bool found = inner.TryResolveCreationDefaultStartingNuyen(out source);
            if (++SourceReads == 2 && fault == "source" && source is not null)
                source = source with { Multiplier = source.Multiplier + 1m };
            return found;
        }
        public bool TryResolveCreationLifeModuleStartingNuyen(Guid id, out CharacterCreationStartingNuyenSource? source)
            => inner.TryResolveCreationLifeModuleStartingNuyen(id, out source);
    }
}
