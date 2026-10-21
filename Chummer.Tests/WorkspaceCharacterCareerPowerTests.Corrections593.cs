using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class WorkspaceCharacterCareerPowerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Cache_hit_validates_each_owned_quality_identity_and_implemented_state(bool disabled)
    {
        var root = XElement.Parse(Runner());
        var adept = Quality("Adept");
        var counterfeit = new XElement(adept);
        counterfeit.SetElementValue("guid", Guid.NewGuid());
        if (disabled) counterfeit.SetElementValue("implemented", false);
        else counterfeit.SetElementValue("name", "The Warrior's Way");
        root.Element("qualities")!.Add(adept, counterfeit);
        using var f = new Fixture(character: root.ToString());
        var before = f.Saved();
        var review = f.Service.ReviewDetailed(f.Owners.Capture(), f.Id,
            Command("Combat Sense", 1) with { ApplyWayDiscount = true });
        Assert.IsNull(review.Plan);
        Assert.AreEqual(disabled ? "power_grant_not_implemented" : "power_grant_identity_invalid", review.Blocker);
        Assert.AreEqual(before.Document.Content, f.Saved().Document.Content);
        Assert.AreEqual(before.ContentRevision, f.Saved().ContentRevision);
    }

    [TestMethod]
    public void Attribute_raise_rejects_native_delta_truncation_and_keeps_legal_plus_four()
    {
        using var f = new Fixture();
        var command = Command("Improved Physical Attribute", 5);
        command = command with { Identity = command.Identity with { Extra = "BOD" } };
        var review = f.Service.ReviewDetailed(f.Owners.Capture(), f.Id, command);
        Assert.IsNull(review.Plan);
        Assert.AreEqual("power_attribute_cap_unavailable", review.Blocker);
        var legal = f.Review(command with { PaidRating = 4 });
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(legal).Outcome);
        var body = XElement.Parse(f.Saved().Document.Content).Element("attributes")!.Elements()
            .Single(a => a.Element("name")!.Value == "BOD");
        Assert.AreEqual("7", body.Element("totalvalue")!.Value);
        Assert.AreEqual(1, f.Saved().Document.AuxiliaryState.CharacterCareerPowerTransactions!.Count);
    }

    [TestMethod]
    [DataRow("AttributeMaxClamp")]
    [DataRow("ReplaceAttribute")]
    public void Unknown_native_attribute_modifier_semantics_refuse_the_entire_purchase(string type)
    {
        var root = XElement.Parse(Runner());
        root.Element("improvements")!.Add(new XElement("improvement",
            new XElement("enabled", true), new XElement("improvedname", "BOD"),
            new XElement("improvementttype", type), new XElement("improvementsource", "Quality"),
            new XElement("sourcename", Guid.NewGuid())));
        using var f = new Fixture(character: root.ToString());
        var command = Command("Improved Physical Attribute", 1);
        command = command with { Identity = command.Identity with { Extra = "BOD" } };
        Assert.IsNull(f.Service.Review(f.Owners.Capture(), f.Id, command));
        Assert.AreEqual(1L, f.Saved().ContentRevision);
    }

    [TestMethod]
    public void Free_plus_paid_cap_refuses_discarded_paid_levels_without_changing_calculator567()
    {
        // Calculator semantics deliberately still describe the native total cap.
        Assert.IsTrue(CharacterAdeptPowerPointRules.TryCalculateCost(new(6, true, 6, 6, .5m, 0, 0, 1, 0), out var cost));
        Assert.AreEqual(6, cost!.TotalRating);
        Assert.AreEqual(6, cost.PaidRating);
        using var f = new Fixture(character: MentorRunner());
        var command = Command("Rapid Healing", 1);
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(f.Review(command)).Outcome);
        var before = f.Saved();
        var rejected = f.Service.ReviewDetailed(f.Owners.Capture(), f.Id,
            command with { TransactionId = Guid.NewGuid(), PaidRating = 6 });
        Assert.IsNull(rejected.Plan);
        Assert.AreEqual("power_paid_levels_discarded", rejected.Blocker);
        Assert.AreEqual(before.Document.Content, f.Saved().Document.Content);
        var legal = f.Review(command with { TransactionId = Guid.NewGuid(), PaidRating = 5 });
        Assert.AreEqual(6, legal.Quote.Proposed.TotalRating);
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(legal).Outcome);
        Assert.AreEqual(6m, Effect(f.Saved(), "PhysicalCMRecovery"));
    }

    [TestMethod]
    public void Saved_maximum_definition_drift_does_not_authorize_a_raise()
    {
        using var f = new Fixture();
        int cap = 1;
        f.Sources.AmendPower = row => { if (row.Element("name")!.Value == "Kinesics") row.SetElementValue("maxlevels", cap); };
        var command = Command("Kinesics", 1);
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(f.Review(command)).Outcome);
        var before = f.Saved();
        cap = 6;
        var rejected = f.Service.ReviewDetailed(f.Owners.Capture(), f.Id,
            command with { TransactionId = Guid.NewGuid(), PaidRating = 2 });
        Assert.IsNull(rejected.Plan);
        Assert.AreEqual("power_saved_definition_drift", rejected.Blocker);
        Assert.AreEqual(before.Document.Content, f.Saved().Document.Content);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Saved_maximum_alias_is_supported_singly_and_rejected_when_ambiguous(bool both)
    {
        using var seed = new Fixture();
        var command = Command("Kinesics", 1);
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, seed.Commit(seed.Review(command)).Outcome);
        var root = XElement.Parse(seed.Saved().Document.Content);
        var row = root.Element("powers")!.Element("power")!;
        row.Add(new XElement("maxlevel", row.Element("maxlevels")!.Value));
        if (!both) row.Element("maxlevels")!.Remove();
        using var f = new Fixture(character: root.ToString());
        var review = f.Service.ReviewDetailed(f.Owners.Capture(), f.Id,
            command with { TransactionId = Guid.NewGuid(), PaidRating = 2 });
        if (both)
        {
            Assert.IsNull(review.Plan);
            Assert.AreEqual("power_maximum_alias_ambiguous", review.Blocker);
        }
        else Assert.IsNotNull(review.Plan);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Read_emits_evaluated_effects_and_exact_bound_legal_plans_without_writes(bool linked)
    {
        using var f = new Fixture(linked);
        // AllocationOwners deliberately refuses recursive acquisition. Every
        // source read and target review must stay under the original admission.
        f.Sources.OnCatalog = () => Assert.AreEqual(1, f.Owners.ActiveLeases);
        var before = f.Saved();
        var read = ReadMagic(f);
        Assert.AreEqual(0, f.Owners.ActiveLeases);
        Assert.IsTrue(read.Applicable);
        Assert.IsTrue(read.AllowsOwnerManagedTransactions);
        Assert.AreEqual(6m, read.RemainingPowerPoints);
        Assert.AreEqual(before.Document.Content, f.Saved().Document.Content);
        Assert.AreEqual(before.ContentRevision, f.Saved().ContentRevision);
        var plans = read.Choices.SelectMany(c => c.Targets).ToArray();
        Assert.IsTrue(plans.Length > 0);
        Assert.AreEqual(plans.Length, plans.Select(t => t.Plan.Command.TransactionId).Distinct().Count());
        Assert.AreEqual(plans.Length, plans.Select(t => t.Plan.Command.Identity.InternalId).Distinct().Count());
        Assert.IsTrue(plans.All(t => t.Plan.Command.ReadBinding == read.Binding));
        var target = read.Choices.Single(c => c.Name == "Combat Sense" && c.ChoiceLabel == "")
            .Targets.Single(t => t.Plan.Command.PaidRating == 1);
        Assert.AreEqual(1m, target.Effects.Single(e => e.Kind == "dodge").Value);
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(target.Plan).Outcome);
        var next = ReadMagic(f);
        Assert.AreEqual(5.5m, next.RemainingPowerPoints);
        Assert.AreEqual(1, next.OwnedPowers.Single().TotalRating);
        Assert.AreEqual(1m, next.OwnedPowers.Single().Effects.Single().Value);
        Assert.AreEqual(CharacterCareerPowerOutcome.Replayed, f.Service.Lookup(f.Owners.Capture(), target.Plan).Outcome);
        Assert.AreEqual(CharacterCareerPowerOutcome.Conflict,
            f.Commit(plans.First(t => t.Plan.Command.TransactionId != target.Plan.Command.TransactionId).Plan).Outcome);
        var raised = next.Choices.Single(c => c.Name == "Combat Sense" && c.ChoiceLabel == "")
            .Targets.Single(t => t.Plan.Command.PaidRating == 2);
        Assert.AreEqual(target.Plan.Command.Identity, raised.Plan.Command.Identity);
        Assert.AreEqual(2m, raised.Effects.Single(e => e.Kind == "dodge").Value);
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(raised.Plan).Outcome);
        Assert.AreEqual(2m, Effect(f.Saved(), "Dodge"));
        Assert.AreEqual(0, f.Owners.ActiveLeases);
    }

    [TestMethod]
    public void Read_binding_cannot_cross_owner_ABA_even_with_a_new_current_stamp()
    {
        using var f = new Fixture(linked: true);
        var stamp = f.Owners.Capture();
        var target = ReadMagic(f).Choices.First().Targets.First().Plan;
        f.Owners.Set(new OwnerScope("owner-B"));
        f.Owners.Set(f.Owner);
        var reader = MagicReader(f);
        Assert.IsNull(reader.Read(stamp, f.Id));
        Assert.AreEqual(CharacterCareerPowerOutcome.Conflict, f.Commit(target).Outcome);
        var changed = target with { Command = target.Command with {
            ReadBinding = target.Command.ReadBinding! with { CharacterDigest = new string('a', 64) } } };
        Assert.AreEqual(CharacterCareerPowerOutcome.Conflict, f.Commit(changed).Outcome);
        Assert.AreEqual(1L, f.Saved().ContentRevision);
    }

    [TestMethod]
    public void Read_source_drift_and_observed_ABA_never_publish_mixed_target_plans()
    {
        using var f = new Fixture();
        f.Sources.OnCatalog = () =>
        {
            if (f.Sources.CatalogReads == 2) f.Sources.Generation = 1;
            if (f.Sources.CatalogReads == 8) f.Sources.Generation = 0;
        };
        Assert.IsNull(MagicReader(f).Read(f.Owners.Capture(), f.Id));
        Assert.AreEqual(1L, f.Saved().ContentRevision);
        f.Sources.OnCatalog = null;
        var target = ReadMagic(f).Choices.First().Targets.First().Plan;
        f.Sources.Generation++;
        Assert.AreEqual(CharacterCareerPowerOutcome.Conflict, f.Commit(target).Outcome);
        Assert.AreEqual(1L, f.Saved().ContentRevision);
    }

    [TestMethod]
    [DataRow("<campaign/>")]
    [DataRow("<plugin><futureCampaign state='unknown'/></plugin>")]
    [DataRow("<metadata><unrecognizedPolicy/></metadata>")]
    [DataRow("<plugin campaignMode='false'/>")]
    public void Campaign_and_unknown_governance_are_denied_in_read_and_final_authority(string metadata)
    {
        var root = XElement.Parse(Runner()); root.Add(XElement.Parse(metadata));
        using var f = new Fixture(character: root.ToString());
        var read = ReadMagic(f);
        Assert.IsFalse(read.AllowsOwnerManagedTransactions);
        Assert.AreEqual(0, read.Choices.Count);
        var command = Command("Combat Sense", 1);
        Assert.IsNull(f.Service.Review(f.Owners.Capture(), f.Id, command));
        // A host cannot recover permissive semantics by registering the old resolver.
        var naked = new OwnerManagedCareerPowerAuthorityResolver(f.Sources);
        Assert.IsNull(naked.Open(f.Owner, f.Saved(), command, f.Store.CareerPowerRuntimeFingerprint));
        Assert.AreEqual(1L, f.Saved().ContentRevision);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Unsupported_owned_power_remains_visible_with_unknown_rating_and_pool(bool sourceMissing)
    {
        using var f = new Fixture();
        var command = Command("Kinesics", 1);
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(f.Review(command)).Outcome);
        var before = f.Saved();
        if (sourceMissing) f.Sources.Forbidden = command.Identity.SourceId.ToString("D");
        else f.Sources.AmendPower = row =>
        {
            if (row.Element("name")!.Value == "Kinesics")
                row.Element("bonus")!.Add(new XElement("uncompiledEffect", "Rating"));
        };
        var read = ReadMagic(f);
        Assert.AreEqual(1, read.OwnedPowers.Count);
        Assert.AreEqual("Kinesics", read.OwnedPowers[0].Name);
        Assert.AreEqual(command.Identity.SourceId, read.OwnedPowers[0].SourceId);
        Assert.IsNull(read.OwnedPowers[0].TotalRating);
        Assert.IsTrue(read.OwnedPowers[0].Blockers.Count > 0);
        Assert.IsNull(read.RemainingPowerPoints);
        Assert.AreEqual(0, read.Choices.Count);
        Assert.AreEqual(before.Document.Content, f.Saved().Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, f.Saved().Document.AuxiliaryStateDigest);
    }

    [TestMethod]
    public void New_null_read_binding_keeps_historical_command_serialization()
    {
        var command = Command("Combat Sense", 1);
        Assert.IsFalse(JsonSerializer.Serialize(command).Contains("ReadBinding", StringComparison.Ordinal));
        var legacy = new { command.TransactionId, command.Identity, command.PaidRating, command.ApplyWayDiscount };
        Assert.AreEqual(JsonSerializer.Serialize(legacy), JsonSerializer.Serialize(command));
    }

    [TestMethod]
    public void Career_magic_DI_replaces_naked_authority_and_resolves_real_reader()
    {
        using var f = new Fixture();
        var services = new ServiceCollection();
        services.AddSingleton<Chummer.Application.Workspaces.IWorkspaceStore>(f.Store);
        services.AddSingleton<Chummer.Application.Owners.IOwnerContextAccessor>(f.Owners);
        services.AddSingleton<ICharacterSourceDataResolver>(f.Sources);
        services.AddSingleton<ICharacterCareerPowerAuthorityResolver, OwnerManagedCareerPowerAuthorityResolver>();
        services.AddCharacterCareerMagic();
        using var provider = services.BuildServiceProvider();
        Assert.IsInstanceOfType<CampaignDenyingCareerPowerAuthorityResolver>(provider.GetRequiredService<ICharacterCareerPowerAuthorityResolver>());
        Assert.IsInstanceOfType<OwnerBoundCharacterCareerMagicReader>(provider.GetRequiredService<ICharacterCareerMagicReader>());
    }

    private static OwnerBoundCharacterCareerMagicReader MagicReader(Fixture f)
    {
        f.Sources.CatalogNames = new HashSet<string> { "Combat Sense", "Kinesics", "Improved Physical Attribute", "Rapid Healing" };
        return new(f.Store, f.Owners, f.Sources, f.Service);
    }
    private static CharacterCareerMagicRead ReadMagic(Fixture f)
    {
        var read = MagicReader(f).Read(f.Owners.Capture(), f.Id);
        Assert.IsNotNull(read, "The saved fixture must remain readable.");
        return read!;
    }
}
