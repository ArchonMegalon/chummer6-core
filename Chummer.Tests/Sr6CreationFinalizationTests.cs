using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Owners;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class Sr6CreationFoundationTests
{
    private static Sr6CreationFinalizationRequest FinalizationRequest(Fixture fixture, string method = "Priority")
    {
        var choice = KarmaSeed(method) with { Karma = new([], [], 0),
            Knowledge = new("German", [new(Guid.NewGuid(), "Seattle")], []), Contacts = new([]),
            Gear = new([]), Lifestyle = new("street", 1) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(choice)).Value);
        var review = fixture.Service.ReviewFinalization(fixture.Stamp, fixture.Binding);
        Assert.IsNotNull(review.Value, string.Join(",", review.Blockers));
        Assert.IsTrue(review.Value.CanFinalize, string.Join(",", review.Value.Blockers));
        return new(review.Value.Binding, review.Value.ReviewDigest, Guid.NewGuid(), true, true);
    }

    [TestMethod]
    [DataRow("Priority")]
    [DataRow("SumtoTen")]
    [DataRow("PointBuy")]
    public void Sr6_finalization_reviews_exact_loss_commits_once_and_reopens_as_created(string method)
    {
        using var fixture = new Fixture(method);
        var request = FinalizationRequest(fixture, method);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var review = fixture.Service.ReviewFinalization(fixture.Stamp, request.Binding).Value!;
        Assert.IsTrue(review.Losses.Any(row => row.DomainId == "karma" && row.Amount == 45));
        Assert.IsTrue(review.Losses.Any(row => row.DomainId == "attributes"));
        Assert.AreEqual(Sr6CreationFoundationIntegrity.Digest(before), Sr6CreationFoundationIntegrity.Digest(fixture.Store.Get(fixture.Id).Value!));
        Assert.IsNull(fixture.Service.ConfirmFinalization(fixture.Stamp, request with { AcceptUnspentLoss = false }).Value);
        Assert.IsNull(fixture.Service.ConfirmFinalization(fixture.Stamp, request with { ExplicitlyConfirmed = false }).Value);
        Assert.IsNull(fixture.Service.ConfirmFinalization(fixture.Stamp, request with { ReviewDigest = "sha256:" + new string('a', 64) }).Value);
        Assert.AreEqual(before.ContentRevision, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
        var result = fixture.Service.ConfirmFinalization(fixture.Stamp, request);
        Assert.IsNotNull(result.Value, string.Join(",", result.Blockers));
        Assert.IsFalse(result.Value.Replayed);
        Assert.AreEqual(before.ContentRevision + 1, result.Value.Receipt.ContentRevision);
        Assert.AreEqual(result.Value.Receipt.ContentRevision, result.Value.Receipt.SavedRevision);
        var coldStore = new FileWorkspaceStore(fixture.Directory);
        var cold = new Sr6CreationFoundationService(coldStore, fixture.Owner);
        var after = coldStore.Get(fixture.Id).Value!;
        Assert.AreEqual(review.Document.Content, after.Document.Content);
        Assert.AreEqual(result.Value.Receipt, cold.LoadFinalization(fixture.Stamp, fixture.Id).Value);
        Assert.IsNull(cold.Load(fixture.Stamp, fixture.Id).Value);
        Assert.IsNull(after.Document.AuxiliaryState.Sr6CreationFoundationDecisions);
        Assert.IsNull(after.Document.AuxiliaryState.CharacterCreationBootstrapBinding);
        var archive = after.Document.AuxiliaryState.Sr6CreationFinalizationArchive!;
        Assert.AreEqual(before.Document.Content, archive.OriginalDocument.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, WorkspaceDocumentAuxiliaryStateDigest.Compute(archive.OriginalState));
        var sections = new CharacterSectionService();
        Assert.IsTrue(sections.ParseProfile(after.Document.Content).Created);
        var root = XDocument.Parse(after.Document.Content).Root!;
        Assert.AreEqual("5", root.Element("karma")!.Value);
        Assert.AreEqual(review.Balances.ProjectedStartingNuyen, (decimal)root.Element("nuyen")!);
        Assert.AreEqual("6", root.Element("totaless")!.Value);
        Assert.AreEqual("finalized", root.Element("sr6creation")!.Attribute("stage")!.Value);
        Assert.IsNull(root.Element("sr6creationprojection"));
        Assert.AreEqual("0", root.Element("physicalcmfilled")!.Value);
        Assert.IsTrue(cold.ConfirmFinalization(fixture.Stamp, request).Value!.Replayed);
        Assert.IsNull(cold.ConfirmFinalization(fixture.Stamp, request with { OperationId = Guid.NewGuid() }).Value);
        Assert.IsNull(cold.ConfirmFinalization(fixture.Stamp, request with { AcceptUnspentLoss = false }).Value);
        Assert.AreEqual(Sr6CreationFoundationIntegrity.Digest(after), Sr6CreationFoundationIntegrity.Digest(coldStore.Get(fixture.Id).Value!));
        var imported = after with { LocalHistory = new WorkspaceLocalHistory(Guid.NewGuid().ToString("N"), after.ContentRevision, new string('1', 64)) };
        Assert.IsNull(Sr6CreationFinalizationRules.Lookup(imported, request)!.Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Sr6_finalization_recovers_ambiguous_replace_without_replaying_a_write(bool afterReplace)
    {
        using var fixture = new Fixture();
        var request = FinalizationRequest(fixture);
        var before = fixture.Store.Get(fixture.Id).Value!;
        fixture.Fault.Stage = afterReplace ? FileWorkspaceStoreFaultStage.AfterTargetReplaced : FileWorkspaceStoreFaultStage.AfterTempFileFlushed;
        var result = fixture.Service.ConfirmFinalization(fixture.Stamp, request);
        Assert.AreEqual(afterReplace, result.Value is not null);
        Assert.AreEqual(1, fixture.Fault.Calls);
        fixture.Fault.Stage = null;
        var cold = new Sr6CreationFoundationService(new FileWorkspaceStore(fixture.Directory), fixture.Owner);
        Assert.AreEqual(afterReplace, cold.LoadFinalization(fixture.Stamp, fixture.Id).Value is not null);
        Assert.AreEqual(before.ContentRevision + (afterReplace ? 1 : 0), fixture.Store.Get(fixture.Id).Value!.ContentRevision);
        if (afterReplace) Assert.IsTrue(cold.ConfirmFinalization(fixture.Stamp, request).Value!.Replayed);
        else Assert.AreEqual(Sr6CreationFoundationIntegrity.Digest(before), Sr6CreationFoundationIntegrity.Digest(fixture.Store.Get(fixture.Id).Value!));
    }

    [TestMethod]
    public void Sr6_finalization_rejects_changed_draft_missing_domains_and_missing_atomic_store()
    {
        using var fixture = new Fixture();
        var request = FinalizationRequest(fixture);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request()).Value);
        Assert.IsNull(fixture.Service.ConfirmFinalization(fixture.Stamp, request).Value);
        var review = fixture.Service.ReviewFinalization(fixture.Stamp, fixture.Binding).Value!;
        Assert.IsFalse(review.CanFinalize);
        CollectionAssert.Contains(review.Blockers.ToArray(), "attributes");
        Assert.AreEqual("false", XDocument.Parse(review.Document.Content).Root!.Element("created")!.Value);
        Assert.IsNull(fixture.Service.ConfirmFinalization(fixture.Stamp,
            new(review.Binding, review.ReviewDigest, Guid.NewGuid(), true, true)).Value);
        var unsupportedStore = new Sr6CreationFoundationService(new InMemoryWorkspaceStore(), fixture.Owner);
        CollectionAssert.Contains(unsupportedStore.ConfirmFinalization(fixture.Stamp, request).Blockers.ToArray(),
            Sr6CreationFoundationBlockers.PersistenceUnavailable);
    }

    [TestMethod]
    public void Sr6_finalization_preserves_archive_and_rejects_generic_injection_or_creation_reset()
    {
        using var fixture = new Fixture();
        var request = FinalizationRequest(fixture);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var prepared = Sr6CreationFinalizationRules.Prepare(before, request, out var replacement);
        Assert.IsNotNull(prepared.Value);
        Assert.IsFalse(fixture.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(fixture.Id, before.ContentRevision,
            before.Document.AuxiliaryStateDigest, replacement!).Success);
        Assert.IsNotNull(fixture.Service.ConfirmFinalization(fixture.Stamp, request).Value);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        var cleared = saved.Document with { State = saved.Document.State with { AuxiliaryState = WorkspaceDocumentAuxiliaryState.Empty } };
        Assert.IsFalse(fixture.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(fixture.Id, saved.ContentRevision,
            saved.Document.AuxiliaryStateDigest, cleared).Success);
        var root = XDocument.Parse(saved.Document.Content).Root!;
        root.Element("created")!.Value = "false";
        var reset = saved.Document with { State = saved.Document.State with { Payload = root.ToString(SaveOptions.DisableFormatting) } };
        Assert.IsFalse(fixture.Store.ReplaceWorkspaceDocumentAndCheckpoint(fixture.Id, saved.ContentRevision, reset).Success);
        root.Element("created")!.Value = "true";
        root.Element("name")!.Value = "Renamed after creation";
        var renamed = saved.Document with { State = saved.Document.State with { Payload = root.ToString(SaveOptions.DisableFormatting) } };
        Assert.IsTrue(fixture.Store.ReplaceWorkspaceDocumentAndCheckpoint(fixture.Id, saved.ContentRevision, renamed).Success);
        Assert.AreEqual(saved.Document.AuxiliaryStateDigest, fixture.Store.Get(fixture.Id).Value!.Document.AuxiliaryStateDigest);
        Assert.IsNotNull(fixture.Service.LoadFinalization(fixture.Stamp, fixture.Id).Value);
        Assert.IsTrue(fixture.Service.ConfirmFinalization(fixture.Stamp, request).Value!.Replayed);
    }

    [TestMethod]
    public async Task Sr6_finalization_concurrent_operations_commit_exactly_one_revision()
    {
        using var fixture = new Fixture();
        var first = FinalizationRequest(fixture);
        var second = first with { OperationId = Guid.NewGuid() };
        var results = await Task.WhenAll(Task.Run(() => fixture.Service.ConfirmFinalization(fixture.Stamp, first)),
            Task.Run(() => fixture.Service.ConfirmFinalization(fixture.Stamp, second)));
        Assert.AreEqual(1, results.Count(result => result.Value is not null));
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.AreEqual(first.Binding.ContentRevision + 1, saved.ContentRevision);
        Assert.IsNotNull(saved.Document.AuxiliaryState.Sr6CreationFinalizationArchive);
    }

    [TestMethod]
    public void Sr6_finalization_does_not_promote_unresolved_equipment_or_magical_tradition()
    {
        using var fixture = new Fixture();
        var seed = SpellSeed("Priority") with { Skills = new([]), Karma = new([], [], 0),
            Knowledge = new("German", [], []), Contacts = new([]), Gear = new([new(Guid.NewGuid(), "lined-coat", 1)]),
            Lifestyle = new("street", 1), Spells = new(["spell-heal"]) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed)).Value);
        var review = fixture.Service.ReviewFinalization(fixture.Stamp, fixture.Binding).Value!;
        CollectionAssert.Contains(review.Blockers.ToArray(), "magical-tradition");
        CollectionAssert.Contains(review.Blockers.ToArray(), "equipment-runtime-stats");
        CollectionAssert.Contains(review.Blockers.ToArray(), "spell-runtime-stats");
        var request = new Sr6CreationFinalizationRequest(review.Binding, review.ReviewDigest, Guid.NewGuid(), true, true);
        Assert.IsNull(fixture.Service.ConfirmFinalization(fixture.Stamp, request).Value);
        Assert.AreEqual(review.Binding.ContentRevision, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Sr6_finalization_rejects_foreign_expired_owner_and_malformed_archives()
    {
        using var owner = new RequestOwnerContextAccessor(new OwnerScope("sr6-final-a"));
        using var foreign = new RequestOwnerContextAccessor(new OwnerScope("sr6-final-b"));
        using var fixture = new Fixture(owner: owner);
        var request = FinalizationRequest(fixture);
        var stamp = fixture.Stamp;
        var other = new Sr6CreationFoundationService(fixture.Store, foreign);
        Assert.IsNull(other.ReviewFinalization(foreign.Capture(), request.Binding).Value);
        Assert.IsNull(other.ConfirmFinalization(foreign.Capture(), request).Value);
        Assert.IsNull(other.LoadFinalization(foreign.Capture(), fixture.Id).Value);
        Assert.IsNotNull(fixture.Service.ConfirmFinalization(stamp, request).Value);
        var saved = fixture.Store.Get(stamp.Owner, fixture.Id).Value!;
        var state = saved.Document.AuxiliaryState;
        var archive = state.Sr6CreationFinalizationArchive!;
        Assert.IsFalse(WorkspaceAuxiliaryStateIntegrity.IsValidShape(fixture.Id, saved.ContentRevision,
            state with { Sr6CreationFinalizationArchive = archive with { OriginalState = state } }));
        Assert.IsFalse(WorkspaceAuxiliaryStateIntegrity.IsValidShape(fixture.Id, saved.ContentRevision,
            state with { Sr6CreationFinalizationArchive = archive with { OriginalDocument = new("<character/>") } }));
        Assert.IsFalse(WorkspaceAuxiliaryStateIntegrity.IsValidShape(fixture.Id, saved.ContentRevision,
            state with { Sr6CreationFinalizationArchive = archive with { Receipt = archive.Receipt with { ContentRevision = 99 } } }));
        owner.Dispose();
        Assert.IsNull(fixture.Service.LoadFinalization(stamp, fixture.Id).Value);
        Assert.IsNull(fixture.Service.ConfirmFinalization(stamp, request).Value);
    }
}
