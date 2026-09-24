using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Owners;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class Sr6CreationFoundationTests
{
    [TestMethod]
    [DataRow("Priority")]
    [DataRow("SumtoTen")]
    [DataRow("PointBuy")]
    public void Character_projection_materializes_saved_choices_without_writing_or_finalizing(string method)
    {
        using var fixture = new Fixture(method);
        Guid language = Guid.NewGuid(), topic = Guid.NewGuid(), contact = Guid.NewGuid(), gear = Guid.NewGuid();
        var selection = KarmaSeed(method) with
        {
            Attributes = Spend(Spend(EmptyAttributes(), "Body", 1, 0), "Logic", 1, 0),
            Skills = new([new("Athletics", 1, ["Climbing"])]),
            Knowledge = new("German", [new(topic, "Seattle")], [new(language, "English", "basic")]),
            Karma = new([new("Body", 1)], [new("Athletics", 1), new("ExoticWeapons", 1, "Whip")], 5)
            { Knowledge = new([], [new(language, "English", "expert")]) },
            Contacts = new([new(contact, "Café", "Fixer", 1, 1)]),
            Gear = new([new(gear, "lined-coat", 2)]), Lifestyle = new("low", 1)
        };
        var request = fixture.Request(selection);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        string bytes = Sr6CreationFoundationIntegrity.Digest(saved);
        var result = fixture.Service.ProjectCharacter(fixture.Stamp, fixture.Binding);
        Assert.IsNotNull(result.Value, string.Join(",", result.Blockers));
        var projection = result.Value;
        var root = XDocument.Parse(projection.Document.Content).Root!;
        Assert.AreEqual("false", root.Element("created")!.Value);
        Assert.AreEqual("Human", root.Element("metatype")!.Value);
        Assert.AreEqual("SR6", root.Element("gameedition")!.Value);
        Assert.IsNull(root.Element(CharacterCreationBootstrapXml.MarkerElement));
        Assert.AreEqual("materialized-draft", root.Element("sr6creationprojection")!.Attribute("stage")!.Value);
        Assert.AreEqual(request.PreviewDigest, root.Element("sr6creationprojection")!.Element("foundationpreviewdigest")!.Value);
        Assert.AreEqual(fixture.Binding, projection.Binding);
        CollectionAssert.Contains(projection.IncompleteDomains.ToArray(), "finalization-transaction");
        CollectionAssert.DoesNotContain(projection.IncompleteDomains.ToArray(), "equipment-runtime-stats");
        var body = root.Element("attributes")!.Elements("attribute").Single(row => row.Element("name")!.Value == "BOD");
        Assert.AreEqual("2", body.Element("base")!.Value);
        Assert.AreEqual("1", body.Element("karma")!.Value);
        Assert.AreEqual("3", body.Element("totalvalue")!.Value);
        var sections = new CharacterSectionService();
        var parsedBody = sections.ParseAttributes(projection.Document.Content).Attributes.Single(row => row.Name == "BOD");
        Assert.AreEqual(2, parsedBody.BaseValue);
        Assert.AreEqual(1, parsedBody.KarmaValue);
        Assert.AreEqual(3, parsedBody.TotalValue);
        Assert.IsFalse(parsedBody.Created);
        Assert.IsFalse(parsedBody.CanCareerUpgrade);
        Assert.AreEqual(10, sections.ParseConditionMonitor(projection.Document.Content).PhysicalTrack);
        Assert.AreEqual(0, sections.ParseConditionMonitor(projection.Document.Content).PhysicalOverflow,
            "Overflow capacity is not current overflow damage.");
        if (method == "PointBuy")
        {
            Assert.IsNull(root.Element("prioritymetatype"));
            Assert.IsNotNull(root.Element("sr6creationprojection")!.Element("characterpoints"));
        }
        else Assert.AreEqual("D", root.Element("prioritymetatype")!.Value);
        var skills = root.Element("newskills")!.Element("skills")!.Elements("skill").ToArray();
        var athletics = skills.Single(row => row.Element("name")!.Value == "Athletics");
        Assert.AreEqual("1", athletics.Element("base")!.Value);
        Assert.AreEqual("1", athletics.Element("karma")!.Value);
        Assert.AreEqual("2", athletics.Descendants("sr6dicebonus").Single().Value);
        Assert.AreEqual("0", skills.Single(row => row.Element("name")!.Value == "ExoticWeapons")
            .Descendants("sr6dicebonus").Single().Value, "Exotic weapon proficiency is not a +2 specialty.");
        Assert.IsFalse(skills.Any(row => row.Element("name")!.Value == "Sorcery"));
        var english = skills.Single(row => row.Element("guid")!.Value == language.ToString("D"));
        Assert.AreEqual("expert", english.Element("sr6level")!.Value);
        Assert.AreEqual("3", english.Element("sr6comprehensionbonus")!.Value);
        var native = skills.Single(row => row.Element("name")!.Value == "German");
        Assert.AreEqual("Native", native.Element("sr6level")!.Value);
        Assert.IsNull(native.Element("sr6comprehensionbonus"));
        Assert.AreEqual("true", skills.Single(row => row.Element("guid")!.Value == topic.ToString("D")).Element("sr6unrated")!.Value);
        var friend = root.Element("contacts")!.Element("contact")!;
        Assert.AreEqual(contact.ToString("D"), friend.Element("guid")!.Value);
        Assert.AreEqual("Café", friend.Element("name")!.Value);
        Assert.AreEqual("true", friend.Element("sr6gmreviewrequired")!.Value);
        var coat = root.Element("armors")!.Element("armor")!;
        Assert.AreEqual(gear.ToString("D"), coat.Element("guid")!.Value);
        Assert.AreEqual("2", coat.Element("qty")!.Value);
        Assert.AreEqual("1800", coat.Element("sr6totalcost")!.Value);
        Assert.AreEqual("false", coat.Element("equipped")!.Value);
        Assert.AreEqual("true", coat.Element("sr6runtimestatsavailable")!.Value);
        Assert.AreEqual("3", coat.Element("sr6equipmentprofile")!.Element("armor")!.Element("defenseratingbonus")!.Value);
        Assert.IsNull(coat.Element("armor"), "Do not emit SR5 armor/soak values for SR6 Defense Rating.");
        Assert.AreEqual("2000", root.Element("lifestyles")!.Element("lifestyle")!.Element("cost")!.Value);
        var balances = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!.Balances!;
        Assert.AreEqual(balances.RemainingKarma, (int)root.Element("karma")!);
        Assert.AreEqual(balances.RemainingNuyen, (decimal)root.Element("nuyen")!);
        Assert.IsTrue(balances.RemainingNuyen > balances.ProjectedStartingNuyen, "Projection must not discard excess starting cash.");
        Assert.AreEqual("sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(projection.Document.Content))), projection.DocumentDigest);
        Assert.AreEqual(bytes, Sr6CreationFoundationIntegrity.Digest(fixture.Store.Get(fixture.Id).Value!));
        var cold = new Sr6CreationFoundationService(new FileWorkspaceStore(fixture.Directory), fixture.Owner);
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var reopened = cold.ProjectCharacter(fixture.Stamp, fixture.Binding).Value!;
            Assert.AreEqual(projection.Document, reopened.Document);
            Assert.AreEqual(projection.DocumentDigest, reopened.DocumentDigest);
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
        Assert.AreEqual(bytes, Sr6CreationFoundationIntegrity.Digest(fixture.Store.Get(fixture.Id).Value!));
    }

    [TestMethod]
    public void Character_projection_preserves_power_costs_quality_levels_and_unresolved_effects()
    {
        using var fixture = new Fixture("PointBuy");
        var selection = PowerSeed("PointBuy") with { Qualities = new(["built-tough-1", "combat-paralysis"]),
            AdeptPowers = new([new("improved-reflexes", 1), new("improved-attribute-reaction", 1), new("mystic-armor", 1)]) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(selection)).Value);
        var projection = fixture.Service.ProjectCharacter(fixture.Stamp, fixture.Binding).Value!;
        var root = XDocument.Parse(projection.Document.Content).Root!;
        var power = root.Element("powers")!.Elements("power").Single(row => row.Element("sr6id")!.Value == "mystic-armor");
        Assert.AreEqual("0.25", power.Element("pointsperlevel")!.Value);
        Assert.AreEqual("0.25", power.Element("sr6totalpoints")!.Value);
        var quality = root.Element("qualities")!.Elements("quality").Single(row => row.Element("sr6id")!.Value == "built-tough-1");
        Assert.AreEqual("1", quality.Element("sr6rating")!.Attribute("total")!.Value);
        Assert.AreEqual("0", quality.Element("sr6rating")!.Attribute("innate")!.Value);
        var derived = root.Element("sr6creationprojection")!.Element("derived")!;
        var value = derived.Elements("stat").Single(row => row.Attribute("id")!.Value == "initiative-base").Element("value")!;
        Assert.AreEqual("true", value.Attribute("unresolved")!.Value);
        Assert.AreEqual(string.Empty, value.Value, "An unresolved reaction effect must not become zero.");
        CollectionAssert.Contains(projection.IncompleteDomains.ToArray(), "passive-effect-conflict");
        Assert.AreEqual("1", root.Element("attributes")!.Elements("attribute")
            .Single(row => row.Element("name")!.Value == "REA").Element("totalvalue")!.Value);
    }

    [TestMethod]
    public void Character_projection_keeps_spell_and_form_source_identity_without_invented_runtime_stats()
    {
        using var magician = new Fixture();
        Assert.IsNotNull(magician.Service.Confirm(magician.Stamp, magician.Request(SpellSeed("Priority") with
            { Spells = new(["spell-heal", "ritual-ward"]) })).Value);
        var magical = magician.Service.ProjectCharacter(magician.Stamp, magician.Binding).Value!;
        var spells = XDocument.Parse(magical.Document.Content).Root!.Element("spells")!.Elements("spell").ToArray();
        Assert.AreEqual(2, spells.Length);
        Assert.IsTrue(spells.All(row => row.Element("source")!.Value.StartsWith("sr6_core_de_2024:p", StringComparison.Ordinal)));
        Assert.IsTrue(spells.All(row => row.Element("drain") is null));
        CollectionAssert.Contains(magical.IncompleteDomains.ToArray(), "magical-tradition");
        CollectionAssert.Contains(magical.IncompleteDomains.ToArray(), "spell-runtime-stats");
        using var techno = new Fixture();
        Assert.IsNotNull(techno.Service.Confirm(techno.Stamp, techno.Request(FormSeed("Priority") with
            { ComplexForms = new([new("emulate-autosoft-targeting", "Ares Alpha"), new("emulate-autosoft-targeting", "HK-227")]) })).Value);
        var matrix = techno.Service.ProjectCharacter(techno.Stamp, techno.Binding).Value!;
        var forms = XDocument.Parse(matrix.Document.Content).Root!.Element("complexforms")!.Elements("complexform").ToArray();
        Assert.AreEqual(2, forms.Length);
        Assert.AreNotEqual(forms[0].Element("guid")!.Value, forms[1].Element("guid")!.Value);
        Assert.IsTrue(forms.All(row => row.Element("sr6gmreviewrequired")!.Value == "true"));
        CollectionAssert.Contains(matrix.IncompleteDomains.ToArray(), "complex-form-runtime-stats");
        CollectionAssert.DoesNotContain(matrix.IncompleteDomains.ToArray(), "magical-tradition");
    }

    [TestMethod]
    public void Character_projection_rejects_missing_stale_foreign_and_expired_authority_without_writes()
    {
        using var owner = new RequestOwnerContextAccessor(new OwnerScope("sr6-project-a"));
        using var other = new RequestOwnerContextAccessor(new OwnerScope("sr6-project-b"));
        using var fixture = new Fixture(owner: owner);
        var stamp = fixture.Stamp;
        var originalBinding = fixture.Binding;
        Assert.IsNull(fixture.Service.ProjectCharacter(stamp, originalBinding).Value);
        Assert.IsNotNull(fixture.Service.Confirm(stamp, fixture.Request()).Value);
        var current = fixture.Binding;
        Assert.IsNull(fixture.Service.ProjectCharacter(stamp, originalBinding).Value);
        Assert.IsNull(fixture.Service.ProjectCharacter(other.Capture(), current).Value);
        Assert.IsNull(new Sr6CreationFoundationService(fixture.Store, other).ProjectCharacter(other.Capture(), current).Value);
        var saved = fixture.Store.Get(stamp.Owner, fixture.Id).Value!;
        Assert.IsNotNull(fixture.Service.ProjectCharacter(stamp, current).Value);
        Assert.IsNull(fixture.Service.ProjectCharacter(stamp, current with { AuthorityDigest = "sha256:" + new string('0', 64) }).Value);
        owner.Dispose();
        Assert.IsNull(fixture.Service.ProjectCharacter(stamp, current).Value);
        Assert.AreEqual(Sr6CreationFoundationIntegrity.Digest(saved),
            Sr6CreationFoundationIntegrity.Digest(fixture.Store.Get(stamp.Owner, fixture.Id).Value!));
    }

    [TestMethod]
    public void Character_projection_revalidates_redigested_history_instead_of_trusting_the_saved_preview()
    {
        using var fixture = new Fixture("PointBuy");
        var selection = SpellSeed("PointBuy") with { Spells = new(["spell-heal"]) };
        var saved = fixture.Store.Get(fixture.Id).Value!;
        var request = fixture.Request(selection);
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(saved, request, out var candidate, out var decision));
        var quote = decision.Preview with { Spells = decision.Preview.Spells! with { CharacterPointCost = 0 } };
        quote = quote with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(quote) };
        var forged = decision with { Preview = quote, Command = request with { PreviewDigest = quote.PreviewDigest } };
        forged = forged with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(forged) };
        var state = candidate.AuxiliaryState with { Sr6CreationFoundationDecisions = [forged] };
        Assert.IsTrue(Sr6CreationFoundationIntegrity.IsValidLedger(fixture.Id, 2, state));
        Assert.IsNull(Sr6CreationCharacterProjector.Project(saved with { ContentRevision = 2, SavedRevision = 2,
            Document = candidate with { State = candidate.State with { AuxiliaryState = state } } }).Value);
        Assert.AreEqual(1L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }
}
