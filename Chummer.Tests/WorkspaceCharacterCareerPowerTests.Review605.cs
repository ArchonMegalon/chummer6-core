using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class WorkspaceCharacterCareerPowerTests
{
    [TestMethod]
    public void Persisted_delegation_without_campaign_XML_or_auxiliary_denies_local_power_admission()
    {
        using var f = new Fixture(linked: true);
        var committed = f.Review(Command("Kinesics", 1));
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(committed).Outcome);
        var pending = f.Review(Command("Combat Sense", 1));
        var beforeDelegation = f.Saved();
        DelegateNotesAndCheckpoint607(f);
        var saved = f.Saved();
        Assert.IsTrue(saved.HasDelegatedGmHistory);
        Assert.AreEqual(beforeDelegation.Document.AuxiliaryStateDigest, saved.Document.AuxiliaryStateDigest);
        Assert.IsFalse(XElement.Parse(saved.Document.Content).Descendants()
            .Any(e => e.Name.LocalName.Contains("campaign", StringComparison.OrdinalIgnoreCase)));

        var read = ReadMagic(f);
        Assert.IsFalse(read.AllowsOwnerManagedTransactions);
        Assert.AreEqual(0, read.Choices.Count);
        var command = Command("Combat Sense", 1);
        var review = f.Service.ReviewDetailed(f.Owners.Capture(), f.Id, command);
        Assert.IsNull(review.Plan);
        Assert.AreEqual("power_campaign_authority_unavailable", review.Blocker);
        var naked = new OwnerManagedCareerPowerAuthorityResolver(f.Sources);
        Assert.IsNull(naked.Open(f.Owner, saved, command, f.Store.CareerPowerRuntimeFingerprint));

        // The final store boundary checks its validated ledger, before opening authority.
        var denied = f.Store.CommitCareerPower(f.Owner, pending, f.Authority);
        Assert.AreEqual(CharacterCareerPowerOutcome.Conflict, denied.Outcome);
        Assert.AreEqual("power_campaign_authority_unavailable", denied.Error);
        Assert.AreEqual(saved.ContentRevision, denied.CurrentRevision);
        Assert.AreEqual(CharacterCareerPowerOutcome.Conflict, f.Commit(pending).Outcome);
        AssertUnchanged607(saved, f.Saved());

        // Existing paid operations remain recoverable, even after governance/source changes.
        f.Sources.Unavailable = true;
        Assert.AreEqual(CharacterCareerPowerOutcome.Replayed, f.Commit(committed).Outcome);
        Assert.AreEqual(CharacterCareerPowerOutcome.Replayed,
            f.Store.CommitCareerPower(f.Owner, committed, f.Authority).Outcome);
        AssertUnchanged607(saved, f.Saved());
    }

    [TestMethod]
    public void Stripping_read_binding_cannot_cross_owner_ABA_or_build_a_new_transaction()
    {
        using var f = new Fixture(linked: true);
        var plan = f.Review(Command("Combat Sense", 1));
        var stripped = plan with { Command = plan.Command with { ReadBinding = null } };
        var before = f.Saved();
        f.Owners.Set(new OwnerScope("owner-B"));
        f.Owners.Set(f.Owner);
        Assert.AreEqual(CharacterCareerPowerOutcome.Conflict, f.Commit(plan).Outcome);
        var rejected = f.Commit(stripped);
        Assert.AreEqual(CharacterCareerPowerOutcome.Conflict, rejected.Outcome);
        Assert.AreEqual("power_read_owner_changed", rejected.Error);

        // Null remains a valid historical plan shape, but cannot construct a fresh receipt.
        Assert.IsTrue(CharacterCareerPowerTransaction.IsValidPlan(stripped));
        using var admission = f.Authority.Open(f.Owner, before, stripped.Command,
            f.Store.CareerPowerRuntimeFingerprint);
        Assert.IsNotNull(admission);
        Assert.IsFalse(CharacterCareerPowerTransaction.TryBuild(f.Owner, before, stripped, admission!,
            f.Store.CareerPowerRuntimeFingerprint, out var replacement, out var entry));
        Assert.IsNull(replacement);
        Assert.IsNull(entry);
        Assert.AreEqual(CharacterCareerPowerOutcome.Conflict,
            f.Store.CommitCareerPower(f.Owner, stripped, f.Authority).Outcome);
        AssertUnchanged607(before, f.Saved());
    }

    [TestMethod]
    public void Historical_null_binding_receipt_retries_before_binding_and_delegation_admission()
    {
        using var f = new Fixture(linked: true);
        var bound = f.Review(Command("Combat Sense", 1));
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(bound).Outcome);
        var historical = bound with { Command = bound.Command with { ReadBinding = null } };
        var receipt = f.Saved().Document.AuxiliaryState.CharacterCareerPowerTransactions!.Single()
            with { Plan = historical };
        receipt = receipt with { EntryDigest = CharacterCareerPowerTransaction.EntryDigest(receipt) };

        // Materialize the pre-binding receipt format in the existing real file-store fixture.
        // No modern null-binding command is executed to manufacture historical authority.
        string path = Directory.GetFiles(f.Path, f.Id.Value + ".json", SearchOption.AllDirectories).Single();
        var record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var auxiliary = record["AuxiliaryState"]!.AsObject();
        var ledger = auxiliary.Single(p => p.Key.Equals(
            nameof(WorkspaceDocumentAuxiliaryState.CharacterCareerPowerTransactions),
            StringComparison.OrdinalIgnoreCase)).Value!.AsArray();
        ledger[0] = JsonSerializer.SerializeToNode(receipt);
        Assert.IsFalse(ledger[0]!.ToJsonString().Contains("\"ReadBinding\"", StringComparison.Ordinal));
        File.WriteAllText(path, record.ToJsonString());

        var historicalSaved = f.Saved(); // Cold read validates the actual serialized ledger.
        Assert.IsTrue(CharacterCareerPowerTransaction.IsValidHistory(historicalSaved));
        DelegateNotesAndCheckpoint607(f);
        var before = f.Saved();
        Assert.IsTrue(before.HasDelegatedGmHistory);
        f.Owners.Set(new OwnerScope("owner-B"));
        f.Owners.Set(f.Owner);
        f.Sources.Unavailable = true;
        var coldStore = new FileWorkspaceStore(f.Path);
        var cold = new OwnerBoundCharacterCareerPowerService(coldStore, f.Owners, f.Authority);
        Assert.AreEqual(CharacterCareerPowerOutcome.Replayed,
            cold.Lookup(f.Owners.Capture(), historical).Outcome);
        Assert.AreEqual(CharacterCareerPowerOutcome.Replayed,
            cold.Commit(f.Owners.Capture(), historical).Outcome);
        Assert.AreEqual(CharacterCareerPowerOutcome.Replayed,
            coldStore.CommitCareerPower(f.Owner, historical, f.Authority).Outcome);
        Assert.AreEqual(CharacterCareerPowerOutcome.IdempotencyConflict,
            cold.Commit(f.Owners.Capture(), historical with {
                Command = historical.Command with { ApplyWayDiscount = true } }).Outcome);
        AssertUnchanged607(before, f.Saved());
        Assert.AreEqual(1, f.Saved().Document.AuxiliaryState.CharacterCareerPowerTransactions!.Count);
    }

    [TestMethod]
    public void Imported_Human_augmented_maximum_eleven_cannot_authorize_rating_five()
    {
        var root = XElement.Parse(Runner());
        var body = root.Element("attributes")!.Elements()
            .Single(a => a.Element("name")!.Value == "BOD");
        body.SetElementValue("metatypeaugmax", 11); // This is the only changed saved input.
        using var f = new Fixture(character: root.ToString());
        var before = f.Saved();
        var command = Command("Improved Physical Attribute", 5);
        command = command with { Identity = command.Identity with { Extra = "BOD" } };
        var review = f.Service.ReviewDetailed(f.Owners.Capture(), f.Id, command);
        Assert.IsNull(review.Plan);
        Assert.AreEqual("power_attribute_cap_unavailable", review.Blocker);
        var read = ReadMagic(f);
        Assert.IsFalse(read.Choices.SelectMany(c => c.Targets).Any(t =>
            t.Plan.Command.Identity.SourceId == command.Identity.SourceId
            && t.Plan.Command.Identity.Extra == "BOD" && t.Plan.Command.PaidRating == 5));
        AssertUnchanged607(before, f.Saved());

        // The source-backed Human 6/10 bounds still admit the ordinary +4 delta.
        using var lawful = new Fixture();
        var legal = lawful.Review(command with { PaidRating = 4 });
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, lawful.Commit(legal).Outcome);
        var savedBody = XElement.Parse(lawful.Saved().Document.Content).Element("attributes")!.Elements()
            .Single(a => a.Element("name")!.Value == "BOD");
        Assert.AreEqual("7", savedBody.Element("totalvalue")!.Value);
        Assert.AreEqual("10", savedBody.Element("metatypeaugmax")!.Value);
    }

    [TestMethod]
    public void Shared_Magic_native_enable_marker_and_initiation_disable_keep_owned_PP_spendable()
    {
        var root = XElement.Parse(Runner());
        root.Add(new XElement("initiationdisabled", true));
        var marker = NativeMagicEnableMarker607();
        root.Element("improvements")!.Add(new XElement(marker));
        using var f = new Fixture(character: root.ToString());
        var read = ReadMagic(f);
        Assert.AreEqual(6m, read.RemainingPowerPoints);
        var plan = f.Review(Command("Combat Sense", 1));
        Assert.AreEqual(CharacterCareerPowerOutcome.Applied, f.Commit(plan).Outcome);
        Assert.AreEqual(5.5m, ReadMagic(f).RemainingPowerPoints);
        Assert.IsTrue(XElement.Parse(f.Saved().Document.Content).Element("improvements")!.Elements()
            .Any(e => XNode.DeepEquals(e, marker)));
    }

    private static XElement NativeMagicEnableMarker607()
    {
        var native = XElement.Load(System.IO.Path.Combine(CoreRoot(), "Chummer.Tests", "TestFiles",
            "Munin_Career.chum5"));
        return new XElement(native.Element("improvements")!.Elements().Single(e =>
            e.Element("unique")?.Value == "enableattribute" && e.Element("improvedname")?.Value == "MAG"));
    }

    private static void DelegateNotesAndCheckpoint607(Fixture f)
    {
        var saved = f.Saved();
        const string notes = "GM updated notes";
        var root = XElement.Parse(saved.Document.Content);
        root.SetElementValue("notes", notes);
        string key = CharacterCareerPowerTransaction.Hash("delegated-key607");
        string command = CharacterCareerPowerTransaction.Hash("delegated-command607");
        const string delegation = "delegation607", authority = "authority607";
        string receiptId = "gm-edit-" + CharacterCareerPowerTransaction.Hash(
            string.Join("\n", command, delegation, authority, key))[..24];
        var receipt = new DelegatedGmCharacterEditAuditReceipt(
            DelegatedGmCharacterEditContract.Name, receiptId, "campaign607", delegation,
            "campaign-owner607", f.Owner.NormalizedValue, authority, 1, "gm607",
            DelegatedGmCharacterEditContract.GameMasterRole, f.Owner.NormalizedValue, f.Id,
            "Update notes", key, command, saved.ContentRevision, saved.ContentRevision + 1,
            DateTimeOffset.UtcNow,
            ImmutableArray.Create(new DelegatedGmCharacterEditAuditOperation(
                DelegatedGmCharacterPatchOperationKind.Replace, DelegatedGmCharacterEditContract.ProfileNotesPath,
                CharacterCareerPowerTransaction.Hash(notes), notes.Length)));
        var entry = new DelegatedGmCharacterEditLedgerEntry(key, command, receipt);
        Assert.IsTrue(DelegatedGmCharacterEditLedgerValidator.IsValidForCommit(
            f.Owner, f.Id, saved.ContentRevision, entry));
        var document = saved.Document with { State = saved.Document.State with { Payload = root.ToString() } };
        var result = f.Store.ApplyDelegatedGmCharacterEdit(f.Owner, f.Id, saved.ContentRevision, document, entry);
        Assert.AreEqual(DelegatedGmCharacterEditStoreOutcome.Applied, result.Outcome, result.Error);
        Assert.IsTrue(f.Store.SaveCheckpoint(f.Owner, f.Id, saved.ContentRevision + 1).Success);
    }

    private static void AssertUnchanged607(WorkspaceStoredDocument before, WorkspaceStoredDocument after)
    {
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
    }
}
