using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces.TypedWizard;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CareerAttributeAdvanceSavedRulesTests
{
    private static readonly CharacterWorkspaceId WorkspaceId = new("saved-edge-repair");
    private static readonly Guid ExpenseId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [TestMethod]
    public void Saved_burn_repair_reopens_with_unchanged_attribute_karma_and_rejects_replay()
    {
        string xml = SavedCharacter();
        CharacterCareerAttributeAdvanceQuote quote = Find(Project(xml), "EDG");
        Assert.AreEqual(2, quote.EffectiveValue);
        Assert.AreEqual(3, quote.TargetValue);
        Assert.AreEqual(15, quote.KarmaCost);
        Assert.IsTrue(quote.RepairsBurnedEdge);
        CareerAttributeAdvanceRequest request = Request(quote);

        CareerAttributeAdvanceMutationResult result = CareerAttributeAdvanceMutation.Apply(
            xml, request, null, new SavedProfileResolver());
        XElement root = XElement.Parse(result.Xml);
        Assert.AreEqual("0", Attribute(root, "EDG").Element("karma")!.Value);
        Assert.AreEqual("2", Attribute(root, "EDG").Element("base")!.Value);
        Assert.AreEqual("3", Attribute(root, "EDG").Element("totalvalue")!.Value);
        Assert.AreEqual("85", root.Element("karma")!.Value);
        Assert.IsFalse(root.Element("improvements")!.Elements().Any());
        Assert.AreEqual("preserve me", root.Element("customstate")!.Value);
        Assert.AreEqual(1, root.Element("expenses")!.Elements("expense").Count());

        CareerAttributeAdvanceEditorState reopened = Project(result.Xml, 8);
        Assert.AreEqual(3, Find(reopened, "EDG").EffectiveValue);
        Assert.AreEqual(0, Find(reopened, "EDG").BurnedEdgePoints);
        Assert.AreEqual(result.Receipt, reopened.RecoverableReceipts.Single());
        Assert.AreEqual(0, reopened.OmittedReceiptCount);
        Assert.ThrowsExactly<InvalidOperationException>(() => CareerAttributeAdvanceMutation.Apply(
            result.Xml, request, null, new SavedProfileResolver()));
        // A fresh quote still cannot reuse the persisted expense identity.
        Assert.ThrowsExactly<InvalidOperationException>(() => CareerAttributeAdvanceMutation.Apply(
            result.Xml, Request(Find(reopened, "EDG")) with { ExpectedContentRevision = 8 },
            null, new SavedProfileResolver()));
    }

    [TestMethod]
    public void Saved_normal_advance_preserves_unrelated_burn_authority_and_edge_row()
    {
        string xml = SavedCharacter();
        XElement before = XElement.Parse(xml);
        CharacterCareerAttributeAdvanceQuote quote = Find(Project(xml), "BOD");
        CareerAttributeAdvanceMutationResult result = CareerAttributeAdvanceMutation.Apply(
            xml, Request(quote), null, new SavedProfileResolver());
        XElement after = XElement.Parse(result.Xml);
        Assert.IsTrue(XNode.DeepEquals(before.Element("improvements"), after.Element("improvements")));
        Assert.IsTrue(XNode.DeepEquals(Attribute(before, "EDG"), Attribute(after, "EDG")));
        Assert.AreEqual("5", Attribute(after, "BOD").Element("totalvalue")!.Value);
        Assert.AreEqual("75", after.Element("karma")!.Value);
        Assert.AreEqual("preserve me", after.Element("customstate")!.Value);
        CareerAttributeAdvanceEditorState reopened = Project(result.Xml, 8);
        Assert.AreEqual(1, Find(reopened, "EDG").BurnedEdgePoints);
        Assert.AreEqual(result.Receipt, reopened.RecoverableReceipts.Single());
    }

    [TestMethod]
    public void Saved_clamped_repair_keeps_exact_projected_target_without_adding_karma_points()
    {
        // Canonical RawMinimum clamps max(1 - 2, 0) and max(1 - 1, 0) to zero.
        string xml = SavedCharacter(burned: 2);
        CharacterCareerAttributeAdvanceQuote quote = Find(Project(xml), "EDG");
        Assert.AreEqual(2, quote.TargetValue);
        Assert.AreEqual(15, quote.KarmaCost);
        CareerAttributeAdvanceMutationResult result = CareerAttributeAdvanceMutation.Apply(
            xml, Request(quote), null, new SavedProfileResolver());
        CharacterCareerAttributeAdvanceQuote reopened = Find(Project(result.Xml, 8), "EDG");
        Assert.AreEqual(2, reopened.EffectiveValue);
        Assert.AreEqual(1, reopened.BurnedEdgePoints);
        Assert.AreEqual(0, reopened.KarmaPoints);
        Assert.AreEqual(3, reopened.TargetValue);
    }

    [TestMethod]
    public void Saved_untrusted_burn_or_total_cannot_supply_a_quote_or_apply_a_reviewed_repair()
    {
        string xml = SavedCharacter();
        CareerAttributeAdvanceRequest request = Request(Find(Project(xml), "EDG"));
        foreach (Action<XElement> corrupt in new Action<XElement>[]
        {
            root => Attribute(root, "EDG").SetElementValue("totalvalue", 3),
            root => root.Element("improvements")!.Element("improvement")!.SetElementValue("custom", true),
            root => root.Element("improvements")!.Element("improvement")!.SetElementValue("max", 1)
        })
        {
            XElement root = XElement.Parse(xml);
            corrupt(root);
            string changed = root.ToString(SaveOptions.DisableFormatting);
            Assert.IsFalse(Project(changed).Attributes.Any(q => q.Identity.Abbreviation == "EDG"));
            Assert.ThrowsExactly<InvalidOperationException>(() => CareerAttributeAdvanceMutation.Apply(
                changed, request, null, new SavedProfileResolver()));
        }
        Assert.ThrowsExactly<InvalidOperationException>(() => CareerAttributeAdvanceEditorProjector.Project(
            xml, WorkspaceId, 7, null));
    }

    private static CareerAttributeAdvanceEditorState Project(string xml, long revision = 7)
        => CareerAttributeAdvanceEditorProjector.Project(xml, WorkspaceId, revision, null, new SavedProfileResolver());

    private static CharacterCareerAttributeAdvanceQuote Find(CareerAttributeAdvanceEditorState state, string name)
        => state.Attributes.Single(q => q.Identity.Abbreviation == name);

    private static CareerAttributeAdvanceRequest Request(CharacterCareerAttributeAdvanceQuote quote)
        => new(WorkspaceId, 7, quote, quote.LogicalRevision, quote.SourceRevision, quote.RuleDigest,
            true, ExpenseId, new DateTime(2081, 5, 12, 14, 30, 0));

    private static XElement Attribute(XElement root, string name)
        => root.Element("attributes")!.Elements("attribute").Single(a => a.Element("name")!.Value == name);

    private static string SavedCharacter(int burned = 1)
        => new XElement("character",
            new XElement("created", "1"), new XElement("settings", "saved-sr5"),
            new XElement("karma", "100"), new XElement("magenabled", "0"),
            new XElement("resenabled", "0"), new XElement("adept", "0"),
            new XElement("magician", "0"), new XElement("critter", "0"),
            new XElement("metatypecategory", "Metahuman"),
            new XElement("attributes", Row("BOD", 2, 1, 4), Row("EDG", 2, 0, 2)),
            new XElement("improvements", new XElement("improvement",
                new XElement("improvedname", "EDG"), new XElement("min", -burned),
                new XElement("max", 0), new XElement("aug", 0), new XElement("augmax", 0),
                new XElement("val", 0), new XElement("rating", 1),
                new XElement("improvementttype", "Attribute"),
                new XElement("improvementsource", "BurnedEdge"),
                new XElement("condition", "career"), new XElement("enabled", "1"),
                new XElement("custom", "0"), new XElement("addtorating", "0"))),
            new XElement("expenses"), new XElement("customstate", "preserve me"))
            .ToString(SaveOptions.DisableFormatting);

    private static XElement Row(string name, int basePoints, int karma, int total)
        => new("attribute", new XElement("name", name), new XElement("metatypemin", 1),
            new XElement("metatypemax", 6), new XElement("metatypeaugmax", 9),
            new XElement("base", basePoints), new XElement("karma", karma),
            new XElement("totalvalue", total));

    private sealed class SavedProfileResolver : ICharacterSourceDataResolver, ICharacterSourceDataContext
    {
        public ICharacterSourceDataContext? TryCreateContext(string characterXml) => this;
        public bool TryResolveCareerSettingsProfile(out string settingsXml, out string authorityState)
        {
            settingsXml = "<settings><id>saved-sr5</id><karmacost><karmaattribute>5</karmaattribute></karmacost>"
                + "<unclampattributeminimum>0</unclampattributeminimum></settings>";
            authorityState = "saved-sr5-profile:v1";
            return true;
        }
        public bool TryResolveCyberwareGradeDeviceRating(string gradeName, string improvementSource, out int deviceRating)
        { deviceRating = 0; return false; }
        public bool TryResolveVehicleModBonuses(string sourceId, string name, out CharacterVehicleModSourceBonuses bonuses)
        { bonuses = CharacterVehicleModSourceBonuses.Empty; return false; }
    }
}
