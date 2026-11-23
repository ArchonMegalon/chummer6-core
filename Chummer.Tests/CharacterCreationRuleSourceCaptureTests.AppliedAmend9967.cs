// Private proposed regression. NOT COMPILED OR EXECUTED by this worker.
// Add to the EXISTING Chummer.Tests project only after aligning its source to
// the frozen/current runtime. The supplied old test worktree lacks these APIs.
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationRuleSourceCaptureTests
{
    [TestMethod]
    public void AppliedAmend9967_Ordered_sources_change_career_quote_and_survive_cold_replay()
    {
        string packet = Environment.GetEnvironmentVariable("CHUMMER_AMEND9967_FIXTURE")
            ?? throw new InvalidOperationException("Set CHUMMER_AMEND9967_FIXTURE to packet/fixture.");
        using var fixture = new Fixture(selected: true); // existing helper, not a new test host
        File.WriteAllText(fixture.Data("gear.xml"), "<chummer><gears/></chummer>");
        Copy9967(Path.Combine(packet, "baseline", "customdata"), Path.Combine(fixture.Root, "customdata"));
        var settings = XDocument.Load(fixture.Data("settings.xml"));
        var selected = settings.Root!.Element("settings")!.Element("setting")!.Element("customdatadirectorynames")!;
        selected.Elements().ElementAt(0).Element("order")!.Value = "1";
        selected.Elements().ElementAt(1).Element("order")!.Value = "2";
        selected.AddFirst(new XElement("customdatadirectoryname", new XElement("directoryname", "Seed9967"),
            new XElement("order", 0), new XElement("enabled", "True")));
        settings.Save(fixture.Data("settings.xml"));
        var character = XElement.Parse(fixture.Character);
        character.Element("customdatadirectorynames")!.AddFirst(new XElement("directoryname", "Seed9967"));
        character.Element("created")!.Value = "True";
        character.Add(new XElement("nuyen", "1000"));
        string original = character.ToString(SaveOptions.DisableFormatting);
        var saved = new WorkspaceStoredDocument(new("amend9967"), new WorkspaceDocument(original, "sr5"),
            1, 1, new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero));
        var baseline = Quote9967(saved, fixture.Resolver(), out _);
        Assert.AreEqual(10m, baseline.Quote.UnitCost);
        Assert.AreEqual(10m, baseline.Quote.TotalCost);
        Assert.AreEqual(990m, baseline.Quote.NuyenAfter);

        Copy9967(Path.Combine(packet, "amended", "customdata"), Path.Combine(fixture.Root, "customdata"));
        var amended = Quote9967(saved, fixture.Resolver(), out var snapshot);
        Assert.AreEqual(80m, amended.Quote.UnitCost);
        Assert.AreEqual(80m, amended.Quote.TotalCost);
        Assert.AreEqual(920m, amended.Quote.NuyenAfter);
        Assert.AreNotEqual(baseline.Command.Binding.RuleStateDigest, amended.Command.Binding.RuleStateDigest);
        Assert.AreEqual(original, saved.Document.Content, "Preview must not mutate the complete character.");

        Assert.IsTrue(CharacterCareerGearPurchaseTransaction.TryBuild(OwnerScope.LocalSingleUser, saved, snapshot,
            amended.Command with { ExplicitlyConfirmed = true }, out var built, out var receipt));
        Assert.IsNotNull(built);
        Assert.IsNotNull(receipt);
        Assert.IsTrue(CharacterCareerGearPurchaseTransaction.IsCoherent(receipt));
        Assert.AreEqual("920", XElement.Parse(built.Content).Element("nuyen")!.Value);
        // A proposal is not a leased store commit or native persistence proof.
        var proposedOriginal = XElement.Parse(built.Content);
        proposedOriginal.Element("gears")!.Remove();
        proposedOriginal.Element("expenses")!.Remove();
        proposedOriginal.Element("nuyen")!.Value = "1000";
        Assert.IsTrue(XNode.DeepEquals(character, proposedOriginal));

        ICharacterCreationRuleSourceCapture collector = fixture.Resolver();
        Assert.IsTrue(collector.TryCaptureChummer5aRuleSources(original, Chummer5aLegacyImportCompatibility.ProfileV2,
            out var capture, out _));
        Assert.IsNotNull(capture);
        Assert.IsTrue(capture.MatchesOriginalCharacter(original));
        byte[] archive = capture.ExportPrivateInputs();
        string ordered = JsonSerializer.Serialize(capture.OrderedCustomSources);
        Assert.IsTrue(capture.OrderedCustomSources.Count >= 7, "Seed plus six ordered contributors must be retained.");
        // Portable/versioned capture may legitimately rebind source identities.
        // Compare cold replay to the same capture, not to ambient resolver digests.
        var retainedQuote = Quote9967(saved, capture.CreateReplayResolver(), out _);
        Assert.AreEqual(80m, retainedQuote.Quote.TotalCost);

        // The control is a separate candidate. Never rewrite the archived order.
        selected.Elements().ElementAt(1).Element("order")!.Value = "2";
        selected.Elements().ElementAt(2).Element("order")!.Value = "1";
        settings.Save(fixture.Data("settings.xml"));
        var reverseCharacter = XElement.Parse(original);
        reverseCharacter.Element("customdatadirectorynames")!.ReplaceNodes(
            new XElement("directoryname", "Seed9967"), new XElement("directoryname", "Second"), new XElement("directoryname", "First"));
        var reverseSaved = saved with { Document = new WorkspaceDocument(reverseCharacter.ToString(SaveOptions.DisableFormatting), "sr5") };
        Assert.AreEqual(50m, Quote9967(reverseSaved, fixture.Resolver(), out _).Quote.TotalCost);

        Directory.Delete(fixture.Root, recursive: true);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(archive, capture.IntegrityDigest, out var restored));
        Assert.IsNotNull(restored);
        CollectionAssert.AreEqual(archive, restored.ExportPrivateInputs());
        Assert.AreEqual(ordered, JsonSerializer.Serialize(restored.OrderedCustomSources));
        Assert.AreEqual(capture.RequiredPackageSetDigest, restored.RequiredPackageSetDigest);
        Assert.IsTrue(restored.MatchesOriginalCharacter(original));
        var cold = Quote9967(saved, restored.CreateReplayResolver(), out _);
        Assert.AreEqual(80m, cold.Quote.TotalCost);
        Assert.AreEqual(JsonSerializer.Serialize(retainedQuote), JsonSerializer.Serialize(cold));
    }

    private static CharacterCareerGearPurchasePreview Quote9967(WorkspaceStoredDocument saved,
        ICharacterSourceDataResolver resolver, out CharacterCareerGearPurchaseSnapshot snapshot)
    {
        // Pure fixture discriminator, NOT a real runtime identity or account credential.
        string runtime = CharacterCareerGearPurchaseTransaction.Hash("amend9967-pure-fixture-only");
        Assert.IsTrue(CharacterCareerGearPurchaseTransaction.TryRead(OwnerScope.LocalSingleUser, saved,
            Guid.Parse(GearId), resolver, runtime, out var observed, out string error), error);
        snapshot = observed!;
        var request = new CharacterCareerGearPurchaseRequest(saved.Id,
            Guid.Parse("99670000-0000-0000-0000-000000000099"), Guid.Parse(GearId), 0, 1m,
            new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Unspecified));
        Assert.IsTrue(CharacterCareerGearPurchaseTransaction.TryPreview(snapshot, request, out var preview, out error), error);
        return preview!;
    }

    private static void Copy9967(string source, string target)
    {
        foreach (string path in Directory.EnumerateFiles(source, "*.xml", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(target, Path.GetRelativePath(source, path));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(path, destination, overwrite: true);
        }
    }
}
