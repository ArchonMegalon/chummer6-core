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
    public void Life_module_contacts_bind_explicit_selection_and_reopen_without_writes()
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = SeedFullGraph(directory);
            var (_, baseline) = BuildFullGraph(fixture.Store, fixture.Id);
            var service = CreateService(fixture.Store);
            var prompt = baseline.ModuleSequence!.QualityLevels.Single().InstancePrompt!;
            var request = QualityInstanceRequest(service, fixture.Id, new Dictionary<string, string> { [prompt.PromptId] = "Renraku" })
                with { TalentSelection = new("mundane"), KarmaResourceInvestment = 0m };
            var missing = service.PreviewFinalization(request).Value!;
            Assert.IsNotNull(missing.ContactsPolicy, string.Join(", ", missing.FinalizationBlocked));
            Assert.IsNull(missing.ContactsQuote);
            CollectionAssert.Contains(missing.FinalizationBlocked.ToArray(), CharacterCreationLifeModuleContactsQuote.SelectionRequired);
            var native = missing.SkillsCatalog!.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
            var selected = request with { SkillSelection = new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)], []),
                ContactSelection = [LifeContact(2, 2), LifeContact(3, 1) with { IsGroup = true }] };
            var preview = service.PreviewFinalization(selected).Value!;
            var quote = preview.ContactsQuote;
            Assert.IsNotNull(quote, string.Join(", ", preview.FinalizationBlocked));
            Assert.IsEmpty(quote.Blockers);
            Assert.AreEqual(preview.AttributeQuote!.Attributes.Single(row => row.AttributeId == "CHA").Current * 3, quote.ContactBudget.Total);
            Assert.AreEqual(4, quote.ContactBudget.Used);
            Assert.AreEqual(4, quote.GroupContactKarma);
            Assert.AreEqual(4, quote.AdditionalQualityKarma);
            Assert.AreEqual(quote.ContactBudget.Overspend + 4, quote.KarmaUsed);
            Assert.AreEqual(preview.ResourcesQuote!.KarmaAfterResources - quote.KarmaUsed, quote.KarmaAfterContacts);
            Assert.AreEqual(preview.ResourcesQuote.QuoteDigest, quote.ResourcesQuoteDigest);
            Assert.AreEqual(preview.QualityCosts!.QuoteDigest, quote.QualityCostsQuoteDigest);
            Assert.AreEqual(HashContacts(quote with { QuoteDigest = string.Empty }), quote.QuoteDigest);
            var omitted = service.ConfirmFinalization(new(selected.Binding, selected.DraftRevision, selected.DraftDigest, preview.PreviewDigest, true)
            { TalentSelection = selected.TalentSelection, QualityInstanceValues = selected.QualityInstanceValues,
                SkillSelection = selected.SkillSelection, KarmaResourceInvestment = selected.KarmaResourceInvestment });
            CollectionAssert.Contains(omitted.Blockers.ToArray(), CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch);
            var exact = service.ConfirmFinalization(new(selected.Binding, selected.DraftRevision, selected.DraftDigest, preview.PreviewDigest, true)
            { TalentSelection = selected.TalentSelection, QualityInstanceValues = selected.QualityInstanceValues,
                SkillSelection = selected.SkillSelection, KarmaResourceInvestment = selected.KarmaResourceInvestment,
                ContactSelection = selected.ContactSelection });
            Assert.IsFalse(exact.Blockers.Contains(CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch));
            Assert.IsFalse(preview.CanApply);
            var reopened = CreateService(new FileWorkspaceStore(directory)).PreviewFinalization(selected).Value!;
            Assert.AreEqual(JsonSerializer.Serialize(quote), JsonSerializer.Serialize(reopened.ContactsQuote));
            CollectionAssert.AreEqual(fixture.Before, File.ReadAllBytes(WorkspacePath(directory, fixture.Id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Life_module_contacts_share_attribute_allowance_and_remaining_karma()
    {
        var fixture = LifeContactFixture();
        Assert.IsFalse(fixture.Context.TryResolveCreationKarmaContactsPolicy(out _));
        Assert.IsTrue(CharacterCreationKarmaContactsRules.IsValidPolicy(fixture.Policy), "The shared expression policy is not a Karma foundation.");
        var choices = new[] { LifeContact(2, 2), LifeContact(2, 2), LifeContact(2, 2) };
        var quote = fixture.Quote(choices).Quote!;
        Assert.IsNotNull(quote);
        Assert.AreEqual(12, quote.ContactBudget.Used);
        Assert.AreEqual(Math.Max(0, 12 - quote.ContactBudget.Total), quote.KarmaUsed);
        Assert.AreEqual(fixture.Resources.KarmaAfterResources - quote.KarmaUsed, quote.KarmaAfterContacts);
        var policy = fixture.Policy with { ContactPointsExpression = "({CHAUnaug} + {LOG}) * 0.1", GroupContactKarmaMultiplier = 5 };
        policy = policy with { AuthorityDigest = CharacterCreationKarmaContactsRules.PolicyDigest(policy) };
        var custom = (fixture with { Policy = policy }).Quote(choices).Quote!;
        Assert.AreEqual((int)decimal.Ceiling(fixture.Math.Attributes.Attributes.Where(row => row.AttributeId is "CHA" or "LOG")
            .Sum(row => row.Current) * 0.1m), custom.ContactBudget.Total);
        Assert.AreEqual(custom.ContactBudget.Overspend, custom.KarmaUsed, "KarmaContact multiplies groups, not ordinary contact overspend.");
        var poor = LifeContactFixture(remaining: 1m).Quote(choices).Quote!;
        Assert.IsTrue(poor.KarmaAfterContacts < 0);
        CollectionAssert.Contains(poor.Blockers.ToArray(), CharacterCreationAttributesBlockers.GlobalKarmaExceeded);
    }

    [TestMethod]
    public void Life_module_contacts_keep_free_family_blackmail_and_group_choices_explicit()
    {
        var fixture = LifeContactFixture();
        var contact = LifeContact(2, 2);
        var free = fixture.Quote([contact with { Free = true }]).Quote!;
        Assert.AreEqual(0, free.KarmaUsed);
        Assert.AreEqual(0, free.ContactBudget.Used);
        Assert.IsTrue(free.Lines.Single().Selection.Free);
        var family = fixture.Quote([contact with { Family = true, Blackmail = true }]).Quote!;
        Assert.AreEqual(7, family.Lines.Single().PointCost);
        var group = fixture.Quote([LifeContact(3, 1) with { IsGroup = true }]).Quote!;
        Assert.AreEqual(0, group.ContactBudget.Used);
        Assert.AreEqual(4, group.GroupContactKarma);
        Assert.AreEqual(4, group.KarmaUsed);
        Assert.IsNull(fixture.Quote(null).Quote);
        var empty = fixture.Quote([]).Quote!;
        Assert.AreEqual(0, empty.KarmaUsed);
        Assert.IsEmpty(empty.Blockers);
        Assert.AreEqual(fixture.Resources.KarmaAfterResources, empty.KarmaAfterContacts);
    }

    [TestMethod]
    public void Life_module_contacts_group_costs_compose_with_free_quality_offsets_before_excess()
    {
        var math = SkillMathFixture(("FreePositiveQualities", "", 2m));
        var plans = QualityCostPlans(math, [QualityCostRow("Positive tier", 24, "QualityLevelImprovement")], [], [], 0);
        var qualityPolicy = SealQualityPolicy(plans.Policy with { Costs = new(1, true, false) });
        var attributes = CharacterCreationLifeModuleAttributeRules.Evaluate(math.Xml, plans.Effects, plans.Racial,
            math.Attributes.Policy, [], plans.Talent).Quote!;
        var native = math.Catalog.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
        var skills = CharacterCreationLifeModuleSkillsRules.Quote(math.Xml, plans.Effects, plans.Racial, plans.Talent,
            attributes, new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)], []), math.Catalog, math.Policy).Quote!;
        var resources = CharacterCreationLifeModuleResourcesRules.Quote(plans.Effects, plans.Racial, plans.Talent, attributes,
            skills, ResourcePolicy(math), qualityPolicy, 1000m, 0m).Quote!;
        Assert.IsNotNull(resources);
        Assert.AreEqual(22, resources.QualityCosts.Costs.PositiveKarmaSpent);
        var policy = LifeContactFixture().Policy with { GroupContactKarmaMultiplier = 2 };
        policy = policy with { AuthorityDigest = CharacterCreationKarmaContactsRules.PolicyDigest(policy) };
        var quote = CharacterCreationLifeModuleContactsRules.Quote(plans.Effects, plans.Racial, plans.Talent,
            attributes, resources, policy, [LifeContact(3, 1) with { IsGroup = true }]).Quote!;
        Assert.IsNotNull(quote);
        Assert.AreEqual(8, quote.GroupContactKarma); // Four points at KarmaContact two.
        Assert.AreEqual(35, quote.CombinedQualityCosts.PositiveKarmaSpent); // 24-2+8=30 plus five excess.
        Assert.AreEqual(13, quote.AdditionalQualityKarma);
        Assert.AreEqual(13, quote.KarmaUsed);
        CollectionAssert.Contains(quote.Blockers.ToArray(), CharacterCreationQualitiesBlockers.PositiveLimitExceeded);
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("empty-id")]
    [DataRow("connection")]
    [DataRow("loyalty")]
    [DataRow("group-loyalty")]
    [DataRow("identity")]
    [DataRow("control")]
    [DataRow("too-many")]
    public void Life_module_contacts_reject_invalid_choices_without_clamping(string fault)
    {
        var fixture = LifeContactFixture();
        var contact = LifeContact(2, 2);
        contact = fault switch
        {
            "empty-id" => contact with { ContactId = Guid.Empty },
            "connection" => contact with { Connection = 8 },
            "loyalty" => contact with { Loyalty = 7 },
            "group-loyalty" => contact with { IsGroup = true },
            "identity" => contact with { Identity = contact.Identity with { Name = "  Space " } },
            "control" => contact with { Identity = contact.Identity with { Notes = "\0" } },
            _ => contact
        };
        var choices = fault == "too-many" ? Enumerable.Range(0, 65).Select(_ => LifeContact(1, 1)).ToArray()
            : fault == "duplicate" ? [contact, contact] : new[] { contact };
        var result = fixture.Quote(choices);
        Assert.IsNull(result.Quote, fault);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationContactsBlockers.ContactInvalid);
    }

    [TestMethod]
    [DataRow("policy")]
    [DataRow("profile")]
    [DataRow("expression")]
    [DataRow("attributes")]
    [DataRow("resources")]
    [DataRow("qualities")]
    public void Life_module_contacts_reject_changed_cost_authorities(string fault)
    {
        var fixture = LifeContactFixture();
        if (fault == "policy") fixture = fixture with { Policy = fixture.Policy with { GroupContactKarmaMultiplier = 999 } };
        if (fault is "profile" or "expression")
        {
            var policy = fault == "profile" ? fixture.Policy with { SettingsProfileId = "different" }
                : fixture.Policy with { ContactPointsExpression = "{Unknown}" };
            fixture = fixture with { Policy = policy with { AuthorityDigest = CharacterCreationKarmaContactsRules.PolicyDigest(policy) } };
        }
        if (fault == "attributes") fixture = fixture with { Math = fixture.Math with { Attributes = fixture.Math.Attributes with { QuoteDigest = "changed" } } };
        if (fault == "resources") fixture = fixture with { Resources = fixture.Resources with { KarmaAfterResources = 9999m } };
        if (fault == "qualities")
        {
            var costs = fixture.Resources.QualityCosts with { KarmaAdjustmentAfterTalent = -999m, QuoteDigest = string.Empty };
            costs = costs with { QuoteDigest = HashContacts(costs) };
            var resources = fixture.Resources with { QualityCosts = costs, QuoteDigest = string.Empty };
            fixture = fixture with { Resources = resources with { QuoteDigest = HashContacts(resources) } };
        }
        Assert.IsNull(fixture.Quote([]).Quote, fault);
    }

    [TestMethod]
    [DataRow("policy")]
    [DataRow("quality")]
    [DataRow("forged-funding")]
    [DataRow("existing-contacts")]
    public void Life_module_contacts_recheck_sources_funding_and_existing_inventory(string fault)
    {
        var fixture = LifeContactFixture();
        var resources = fixture.Resources;
        if (fault == "forged-funding")
        {
            resources = resources with { KarmaAfterResources = 9999m, QuoteDigest = string.Empty };
            resources = resources with { QuoteDigest = HashContacts(resources) };
        }
        string xml = fixture.Math.Xml;
        if (fault == "existing-contacts")
        {
            var root = XDocument.Parse(xml).Root!;
            root.Elements("contacts").Remove();
            root.Add(new XElement("contacts", new XElement("contact", new XElement("name", "Keep existing"))));
            xml = root.ToString(SaveOptions.DisableFormatting);
        }
        var changing = new LifeContactDriftContext(fixture.Context, fault);
        var result = CharacterCreationLifeModuleContactsRules.Evaluate(xml, fixture.Math.Effects, fixture.Math.Racial,
            fixture.Math.Talent, fixture.Math.Attributes, fixture.Skills, resources, resources.TotalKarma, [], changing);
        Assert.IsNull(result.Quote);
        if (fault == "policy") Assert.AreEqual(2, changing.ContactReads);
        if (fault == "quality") Assert.AreEqual(3, changing.QualityReads);
        CollectionAssert.Contains(result.Blockers.ToArray(), fault == "existing-contacts"
            ? CharacterCreationFoundationBlockers.PendingDraftConflict : CharacterCreationFoundationBlockers.SourceDigestConflict);
    }

    private static CharacterCreationKarmaContactSelection LifeContact(int connection, int loyalty)
        => new(Guid.NewGuid(), new("Ana", "Fixer", "Seattle", "Kontakt — español", "", "", "", "", "", "", "", "", ""),
            connection, loyalty);
    private static string HashContacts<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);

    private static LifeContactBinding LifeContactFixture(decimal? remaining = null)
    {
        var math = SkillMathFixture();
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(math.Xml)!;
        var native = math.Catalog.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
        var skills = CharacterCreationLifeModuleSkillsRules.Evaluate(math.Xml, math.Effects, math.Racial, math.Talent,
            math.Attributes, new([new(native.SourceSkillId, native.Kind, 0, IsNativeLanguage: true)], []), context).Quote!;
        var resources = CharacterCreationLifeModuleResourcesRules.Evaluate(math.Xml, math.Effects, math.Racial, math.Talent,
            math.Attributes, skills, 1000m, 0m, context).Quote!;
        Assert.IsNotNull(resources);
        if (remaining.HasValue)
            resources = CharacterCreationLifeModuleResourcesRules.Evaluate(math.Xml, math.Effects, math.Racial, math.Talent,
                math.Attributes, skills, resources.TotalKarma - resources.KarmaAfterResources + remaining.Value, 0m, context).Quote!;
        Assert.IsNotNull(resources);
        Assert.IsTrue(context.TryResolveCreationContactsPolicy(out var policy));
        return new(math, context, skills, resources, policy!);
    }

    private sealed record LifeContactBinding(SkillMathBinding Math, ICharacterSourceDataContext Context,
        CharacterCreationLifeModuleSkillsQuote Skills, CharacterCreationLifeModuleResourcesQuote Resources,
        CharacterCreationKarmaContactsPolicy Policy)
    {
        internal CharacterCreationLifeModuleContactsQuoteResult Quote(IReadOnlyList<CharacterCreationKarmaContactSelection>? selections)
            => CharacterCreationLifeModuleContactsRules.Quote(Math.Effects, Math.Racial, Math.Talent, Math.Attributes, Resources, Policy, selections);
    }

    private sealed class LifeContactDriftContext(ICharacterSourceDataContext inner, string fault) : ICharacterSourceDataContext
    {
        internal int ContactReads { get; private set; }
        internal int QualityReads { get; private set; }
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
        public bool TryResolveCreationLifeModuleQualitiesPolicy(out CharacterCreationKarmaQualitiesPolicy? policy)
        {
            bool found = inner.TryResolveCreationLifeModuleQualitiesPolicy(out policy);
            if (++QualityReads == 3 && fault == "quality" && policy is not null)
                policy = SealQualityPolicy(policy with { Costs = new(3, false, false) });
            return found;
        }
        public bool TryResolveCreationContactsPolicy(out CharacterCreationKarmaContactsPolicy? policy)
        {
            bool found = inner.TryResolveCreationContactsPolicy(out policy);
            if (++ContactReads == 2 && fault == "policy" && policy is not null)
                policy = policy with { GroupContactKarmaMultiplier = 999 };
            return found;
        }
    }
}
