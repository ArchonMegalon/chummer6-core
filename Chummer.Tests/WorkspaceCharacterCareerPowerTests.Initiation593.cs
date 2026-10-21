using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

// Controller includes this file after additively composing terminal initiation592.
// The power adapter is composed in fix607; initiation remains separately owned.
public sealed partial class WorkspaceCharacterCareerPowerTests
{
    [TestMethod]
    [TestCategory("RequiresSharedMagic592")]
    public void Real_initiation_PP_grant_funds_a_later_power_with_both_receipts_and_one_karma_expense()
    {
        var root = XElement.Parse(Runner());
        root.Add(new XElement("initiationgrades"), new XElement("metamagics"),
            new XElement("nuyen", 0), new XElement("cyberwares"));
        root.Element("improvements")!.Add(NativeMagicEnableMarker607());
        using var f = new Fixture(character: root.ToString());
        var sources = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(CoreRoot(), CoreRoot(), null));
        var initiation = new OwnerBoundCharacterCareerInitiationService(f.Store, f.Owners,
            new CharacterCareerInitiationAuthorityResolver(sources));
        var first = f.Review(Command("Combat Sense", 6));
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(first).Outcome);
        var second = f.Review(Command("Rapid Healing", 6));
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(second).Outcome);
        Assert.AreEqual(0m, ReadMagic(f).RemainingPowerPoints);
        Assert.IsNull(f.Service.Review(f.Owners.Capture(), f.Id, Command("Kinesics", 4)));

        var source = XDocument.Load(System.IO.Path.Combine(CoreRoot(), "Chummer", "data", "metamagic.xml"))
            .Root!.Element("metamagics")!.Elements("metamagic").Single(row => row.Element("name")!.Value == "Power Point");
        var quote = initiation.Read(f.Owners.Capture(), f.Id,
            new CharacterCareerInitiationSelection(Guid.Parse(source.Element("id")!.Value), AcceptObligations: true));
        Assert.IsNotNull(quote);
        Assert.IsTrue(quote!.CanApply, string.Join(";", quote.Blockers));
        var purchase = new CharacterCareerInitiationPlan(Guid.NewGuid(), quote,
            new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Unspecified));
        Assert.AreEqual(CharacterCareerInitiationOutcome.Applied, initiation.Commit(f.Owners.Capture(), purchase).Outcome);
        var afterInitiation = f.Saved();
        Assert.AreEqual(1m, ReadMagic(f).RemainingPowerPoints);
        var target = ReadMagic(f).Choices.Single(c => c.Name == "Kinesics" && c.ChoiceLabel == "")
            .Targets.Single(t => t.Plan.Command.PaidRating == 4).Plan;
        Assert.AreEqual(7m, target.Quote.TotalPoints);
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(target).Outcome);
        var after = f.Saved();
        Assert.AreEqual(1, after.Document.AuxiliaryState.CharacterCareerInitiationTransactions!.Count);
        Assert.AreEqual(JsonSerializer.Serialize(afterInitiation.Document.AuxiliaryState.CharacterCareerInitiationTransactions),
            JsonSerializer.Serialize(after.Document.AuxiliaryState.CharacterCareerInitiationTransactions));
        Assert.AreEqual(3, after.Document.AuxiliaryState.CharacterCareerPowerTransactions!.Count);
        Assert.AreEqual(1, XElement.Parse(after.Document.Content).Element("expenses")!.Elements().Count());
        Assert.AreEqual((100 - quote.KarmaCost).ToString(), XElement.Parse(after.Document.Content).Element("karma")!.Value);
        Assert.AreEqual(0m, ReadMagic(f).RemainingPowerPoints);
        Assert.AreEqual(4m, Effect(after, "JudgeIntentionsDefense"));
        var coldStore = new FileWorkspaceStore(f.Path);
        var coldSources = new Sources { CatalogNames = f.Sources.CatalogNames };
        var cold = new OwnerBoundCharacterCareerPowerService(coldStore, f.Owners,
            new CampaignDenyingCareerPowerAuthorityResolver(coldSources));
        var coldInitiation = new OwnerBoundCharacterCareerInitiationService(coldStore, f.Owners,
            new CharacterCareerInitiationAuthorityResolver(new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(CoreRoot(), CoreRoot(), null))));
        Assert.AreEqual(CharacterCareerPowerOutcome.Replayed, cold.Commit(f.Owners.Capture(), target).Outcome);
        Assert.AreEqual(CharacterCareerInitiationOutcome.Replayed, coldInitiation.Commit(f.Owners.Capture(), purchase).Outcome);
        var coldRead = new OwnerBoundCharacterCareerMagicReader(coldStore, f.Owners, coldSources, cold)
            .Read(f.Owners.Capture(), f.Id);
        Assert.IsNotNull(coldRead);
        Assert.AreEqual(0m, coldRead!.RemainingPowerPoints);
        Assert.AreEqual(4, coldRead.OwnedPowers.Single(p => p.Name == "Kinesics").TotalRating);
        var recovered = f.Saved();
        Assert.AreEqual(after.Document.Content, recovered.Document.Content);
        Assert.AreEqual(after.Document.AuxiliaryStateDigest, recovered.Document.AuxiliaryStateDigest);
        Assert.AreEqual(after.ContentRevision, recovered.ContentRevision);
        Assert.AreEqual(after.SavedRevision, recovered.SavedRevision);
        Assert.AreEqual(target.Command.Identity.InternalId.ToString("D"),
            XElement.Parse(recovered.Document.Content).Element("powers")!.Elements("power")
                .Single(p => p.Element("name")!.Value == "Kinesics").Element("guid")!.Value);
    }
}
