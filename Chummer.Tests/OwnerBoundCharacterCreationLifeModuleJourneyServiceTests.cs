using System.Diagnostics.CodeAnalysis;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

// Reuse the real catalog, nationality checkpoint and Journey request helpers.
public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    private static readonly OwnerScope JourneyAccountA = new("journey-account-a");
    private static readonly OwnerScope JourneyAccountB = new("journey-account-b");

    [TestMethod]
    public void Owner_bound_journey_checkpoints_all_stages_repeat_and_finish_only_in_original_partition()
    {
        using var fixture = new OwnerJourneyFixture();
        var others = fixture.Capture(JourneyAccountB, OwnerScope.LocalSingleUser);
        var stamp = fixture.Owner.Capture();
        var initial = fixture.Read(JourneyAccountA);
        string xml = initial.Document.Content;
        long revision = initial.ContentRevision;
        decimal used = 55;
        string[] modules = [FormativeArcologyId, TeenCorporateId, SkipEducationId, BountyHunterId, BountyHunterId];
        decimal[] costs = [40, 50, 0, 100, 100];
        var store = new JourneyScopedStore(fixture);
        var resolver = new JourneyOperationResolver(fixture);
        IOwnerBoundCharacterCreationLifeModuleJourneyService service = fixture.Service(store, resolver);
        for (int i = 0; i < modules.Length; i++)
        {
            var state = OwnerJourneyState(service, stamp, fixture.Id);
            Assert.AreEqual(Math.Min(2 + i, 5), state.CurrentStageOrder);
            Assert.AreEqual(used, state.Budget.Used);
            var request = JourneyRequest(state, modules[i]);
            var preview = JourneySuccess(service.PreviewModule(stamp, request));
            Assert.AreEqual(costs[i], preview.Entry.KarmaCost);
            Assert.AreEqual(used + costs[i], preview.BudgetAfter.Used);
            var receipt = JourneySuccess(service.ConfirmModule(stamp, new(request, preview.PreviewDigest, true)));
            used += costs[i];
            revision++;
            Assert.AreEqual(revision - 1, receipt.PreviousContentRevision);
            Assert.AreEqual(revision, receipt.ContentRevision);
            Assert.AreEqual(revision, receipt.SavedRevision);
            Assert.IsFalse(receipt.CharacterEffectsApplied);
            Assert.IsNull(receipt.OriginDecisionAcceptance);
            Assert.AreEqual(1, store.Commits, "Each module must use the owner-scoped atomic capability.");
            var persisted = fixture.Read(JourneyAccountA);
            Assert.AreEqual(xml, persisted.Document.Content);
            Assert.AreEqual(revision, persisted.ContentRevision);
            Assert.AreEqual(revision, persisted.SavedRevision);
            var draft = persisted.Document.AuxiliaryState.CharacterCreationFoundationDraft!;
            Assert.AreEqual(receipt.DraftRevision, draft.DraftRevision);
            Assert.AreEqual(receipt.DraftDigest, draft.DraftDigest);
            Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(draft), draft.DraftDigest);
            Assert.AreEqual(initial.Document.AuxiliaryState.CharacterCreationFoundationDraft!.Selection, draft.Selection);
            Assert.IsNull(persisted.Document.AuxiliaryState.LifeModuleDecisionAcceptances,
                "Journey confirmations without Origin commands must not fabricate book history.");
            Assert.AreEqual(0, fixture.Owner.ActiveLeases);
            fixture.AssertUnchanged(others);

            // Every iteration reconstructs the store and facade before reopening.
            store = new JourneyScopedStore(fixture);
            service = fixture.Service(store, resolver);
            var reopened = OwnerJourneyState(service, stamp, fixture.Id);
            Assert.AreEqual(i + 1, reopened.AdditionalModules.Count);
            Assert.AreEqual(used, reopened.Budget.Used);
            Assert.AreEqual(750m - used, reopened.Budget.Remaining);
            Assert.AreEqual(receipt.DraftDigest, reopened.DraftDigest);
        }

        var ready = OwnerJourneyState(service, stamp, fixture.Id);
        Assert.IsTrue(ready.CanFinishSelection);
        var finishRequest = new CharacterCreationLifeModuleFinishRequest(ready.Binding, ready.DraftRevision, ready.DraftDigest);
        var finish = JourneySuccess(service.PreviewFinishSelection(stamp, finishRequest));
        Assert.IsTrue(finish.CanConfirm);
        var beforeFinish = fixture.Capture(JourneyAccountA);
        var unconfirmed = service.ConfirmFinishSelection(stamp, new(finishRequest, finish.PreviewDigest, false));
        CollectionAssert.Contains(unconfirmed.Blockers.ToList(), CharacterCreationFoundationBlockers.ExplicitConfirmationRequired);
        fixture.AssertUnchanged(beforeFinish);
        var receiptFinish = JourneySuccess(service.ConfirmFinishSelection(stamp, new(finishRequest, finish.PreviewDigest, true)));
        Assert.AreEqual(revision, receiptFinish.PreviousContentRevision);
        Assert.AreEqual(revision + 1, receiptFinish.ContentRevision);
        Assert.AreEqual(receiptFinish.ContentRevision, receiptFinish.SavedRevision);
        Assert.IsFalse(receiptFinish.CharacterEffectsApplied);
        Assert.IsNull(receiptFinish.OriginDecisionAcceptance);
        Assert.AreEqual(1, store.Commits, "Finish must use the owner-scoped atomic capability.");
        var finishedDocument = fixture.Read(JourneyAccountA);
        Assert.AreEqual(xml, finishedDocument.Document.Content);
        Assert.AreEqual(receiptFinish.ContentRevision, finishedDocument.ContentRevision);
        Assert.AreEqual(receiptFinish.SavedRevision, finishedDocument.SavedRevision);
        var finishedDraft = finishedDocument.Document.AuxiliaryState.CharacterCreationFoundationDraft!;
        Assert.IsTrue(finishedDraft.ModuleSelectionFinished);
        Assert.AreEqual(receiptFinish.DraftRevision, finishedDraft.DraftRevision);
        Assert.AreEqual(receiptFinish.DraftDigest, finishedDraft.DraftDigest);
        Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(finishedDraft), finishedDraft.DraftDigest);
        Assert.IsNull(finishedDocument.Document.AuxiliaryState.LifeModuleDecisionAcceptances);
        var cold = OwnerJourneyState(fixture.Service(new JourneyScopedStore(fixture), resolver), stamp, fixture.Id);
        Assert.IsTrue(cold.SelectionFinished);
        Assert.IsFalse(cold.CanFinishSelection);
        Assert.HasCount(modules.Length, cold.AdditionalModules);
        Assert.AreEqual(used, cold.Budget.Used);
        Assert.AreEqual(receiptFinish.DraftDigest, cold.DraftDigest);
        fixture.AssertUnchanged(others);
        Assert.IsTrue(resolver.Scopes.All(scope => scope.Disposed));
        Assert.IsTrue(resolver.Scopes.Any(scope => scope.Calls > 0));
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
    }

    [TestMethod]
    [DataRow("foreign-issuer")]
    [DataRow("stale-owner")]
    [DataRow("owner-ABA")]
    [DataRow("invalid")]
    [DataRow("spoofed-local")]
    [DataRow("missing-lease")]
    [DataRow("null-lease")]
    [DataRow("mismatched-lease")]
    public void Owner_bound_journey_rejects_original_admission_before_storage_or_source_work(string denial)
    {
        using var fixture = new OwnerJourneyFixture();
        var store = new JourneyScopedStore(fixture);
        var resolver = new JourneyOperationResolver(fixture);
        var service = fixture.Service(store, resolver);
        var original = fixture.Owner.Capture();
        var request = JourneyRequest(OwnerJourneyState(service, original, fixture.Id), FormativeArcologyId);
        var preview = JourneySuccess(service.PreviewModule(original, request));
        IOwnerContextAccessor accessor = fixture.Owner;
        OwnerContextStamp rejected = original;
        JourneyUnusableOwner? unusable = null;
        switch (denial)
        {
            case "foreign-issuer": rejected = new AvailabilityOwner(JourneyAccountA).Capture(); break;
            case "stale-owner": fixture.Owner.Transition(JourneyAccountB); break;
            case "owner-ABA":
                fixture.Owner.Transition(JourneyAccountB);
                fixture.Owner.Transition(JourneyAccountA);
                break;
            case "invalid": rejected = default; break;
            case "spoofed-local":
                var spoofed = new AvailabilityOwner(new OwnerScope(OwnerScope.LocalSingleUser.Value));
                accessor = spoofed;
                rejected = spoofed.Capture();
                break;
            case "missing-lease": accessor = new JourneyCurrentOnlyOwner(); break;
            default:
                unusable = new JourneyUnusableOwner(denial == "null-lease");
                accessor = unusable;
                break;
        }
        service = fixture.Service(store, resolver, accessor);
        int reads = store.Reads, scopes = resolver.Scopes.Count;
        var before = fixture.CaptureAll();
        var finishRequest = new CharacterCreationLifeModuleFinishRequest(request.Binding, request.DraftRevision, request.DraftDigest);
        JourneyAdmissionDenied(service.LoadJourney(rejected, new(fixture.Id)));
        JourneyAdmissionDenied(service.PreviewModule(rejected, request));
        JourneyAdmissionDenied(service.ConfirmModule(rejected, new(request, preview.PreviewDigest, true)));
        JourneyAdmissionDenied(service.PreviewFinishSelection(rejected, finishRequest));
        JourneyAdmissionDenied(service.ConfirmFinishSelection(rejected, new(finishRequest, "unused", true)));
        Assert.AreEqual(reads, store.Reads);
        Assert.AreEqual(0, store.Commits);
        Assert.AreEqual(scopes, resolver.Scopes.Count);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
        if (unusable is not null)
        {
            Assert.AreEqual(5, unusable.Attempts);
            Assert.IsTrue(unusable.Leases.All(lease => lease.Disposed));
        }
        fixture.AssertUnchanged(before);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Owner_bound_journey_missing_workspace_never_uses_matching_local_or_other_owner(bool differentId)
    {
        using var fixture = new OwnerJourneyFixture();
        var store = new JourneyScopedStore(fixture);
        var resolver = new JourneyOperationResolver(fixture);
        var service = fixture.Service(store, resolver);
        var stamp = fixture.Owner.Capture();
        var request = JourneyRequest(OwnerJourneyState(service, stamp, fixture.Id), FormativeArcologyId);
        var preview = JourneySuccess(service.PreviewModule(stamp, request));
        var missing = fixture.Id;
        if (differentId)
        {
            missing = new("local-only-journey");
            SeedJourney(fixture.Root, missing);
            request = request with { Binding = request.Binding with { WorkspaceId = missing } };
            store.ExpectedId = missing;
        }
        else
            Assert.IsTrue(fixture.Store.Delete(JourneyAccountA, fixture.Id, fixture.Read(JourneyAccountA).ContentRevision).Success);
        var others = fixture.Capture(JourneyAccountB, OwnerScope.LocalSingleUser);
        string localOnly = differentId ? File.ReadAllText(WorkspacePath(fixture.Root, missing)) : string.Empty;
        int calls = resolver.Scopes.Sum(scope => scope.Calls);
        var finish = new CharacterCreationLifeModuleFinishRequest(request.Binding, request.DraftRevision, request.DraftDigest);
        Assert.IsNull(service.LoadJourney(stamp, new(missing)).Value);
        Assert.IsNull(service.PreviewModule(stamp, request).Value);
        Assert.IsNull(service.ConfirmModule(stamp, new(request, preview.PreviewDigest, true)).Value);
        Assert.IsNull(service.PreviewFinishSelection(stamp, finish).Value);
        Assert.IsNull(service.ConfirmFinishSelection(stamp, new(finish, "unused", true)).Value);
        Assert.AreEqual(calls, resolver.Scopes.Sum(scope => scope.Calls));
        Assert.AreEqual(0, store.Commits);
        Assert.IsTrue(resolver.Scopes.All(scope => scope.Disposed));
        fixture.AssertUnchanged(others);
        if (differentId) Assert.AreEqual(localOnly, File.ReadAllText(WorkspacePath(fixture.Root, missing)));
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
    }

    [TestMethod]
    public void Owner_bound_journey_requires_owner_atomic_capability_for_module_and_finish()
    {
        using var fixture = new OwnerJourneyFixture();
        var stamp = fixture.Owner.Capture();
        var resolver = new JourneyOperationResolver(fixture);
        var scoped = fixture.Service(new JourneyScopedStore(fixture), resolver);
        var legacyStore = new JourneyLocalAtomicStore(fixture);
        var legacy = fixture.Service(legacyStore, resolver);
        var request = JourneyRequest(OwnerJourneyState(scoped, stamp, fixture.Id), FormativeArcologyId);
        var preview = JourneySuccess(scoped.PreviewModule(stamp, request));
        var before = fixture.CaptureAll();
        var denied = legacy.ConfirmModule(stamp, new(request, preview.PreviewDigest, true));
        CollectionAssert.Contains(denied.Blockers.ToList(), CharacterCreationFoundationBlockers.WizardStatePersistenceAuthorityRequired);
        fixture.AssertUnchanged(before);
        foreach (string module in new[] { FormativeArcologyId, TeenCorporateId, SkipEducationId, BountyHunterId })
            OwnerJourneyAppend(scoped, stamp, fixture.Id, module);
        var state = OwnerJourneyState(scoped, stamp, fixture.Id);
        Assert.IsTrue(state.CanFinishSelection);
        var finishRequest = new CharacterCreationLifeModuleFinishRequest(state.Binding, state.DraftRevision, state.DraftDigest);
        var finish = JourneySuccess(scoped.PreviewFinishSelection(stamp, finishRequest));
        before = fixture.CaptureAll();
        var deniedFinish = legacy.ConfirmFinishSelection(stamp, new(finishRequest, finish.PreviewDigest, true));
        CollectionAssert.Contains(deniedFinish.Blockers.ToList(), CharacterCreationFoundationBlockers.WizardStatePersistenceAuthorityRequired);
        fixture.AssertUnchanged(before);
        Assert.AreEqual(0, legacyStore.Commits);
        Assert.IsTrue(resolver.Scopes.All(scope => scope.Disposed));
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
    }

    [TestMethod]
    public void Owner_bound_journey_delegates_confirmation_and_stale_binding_guards_without_writes()
    {
        using var fixture = new OwnerJourneyFixture();
        var store = new JourneyScopedStore(fixture);
        var resolver = new JourneyOperationResolver(fixture);
        var service = fixture.Service(store, resolver);
        var stamp = fixture.Owner.Capture();
        var request = JourneyRequest(OwnerJourneyState(service, stamp, fixture.Id), FormativeArcologyId);
        var preview = JourneySuccess(service.PreviewModule(stamp, request));
        var before = fixture.CaptureAll();
        var notConfirmed = service.ConfirmModule(stamp, new(request, preview.PreviewDigest, false));
        CollectionAssert.Contains(notConfirmed.Blockers.ToList(), CharacterCreationFoundationBlockers.ExplicitConfirmationRequired);
        var staleSource = request with { Binding = request.Binding with { SourceDigest = new string('0', 64) } };
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, service.PreviewModule(stamp, staleSource).Outcome);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict,
            service.ConfirmModule(stamp, new(staleSource, preview.PreviewDigest, true)).Outcome);
        var staleDraft = request with { DraftDigest = new string('0', 64) };
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, service.PreviewModule(stamp, staleDraft).Outcome);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict,
            service.ConfirmModule(stamp, new(request, new string('0', 64), true)).Outcome);
        Assert.AreEqual(0, store.Commits);
        fixture.AssertUnchanged(before);
        JourneySuccess(service.ConfirmModule(stamp, new(request, preview.PreviewDigest, true)));
        before = fixture.CaptureAll();
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict,
            service.ConfirmModule(stamp, new(request, preview.PreviewDigest, true)).Outcome);
        fixture.AssertUnchanged(before);
        foreach (string module in new[] { TeenCorporateId, SkipEducationId, BountyHunterId })
            OwnerJourneyAppend(service, stamp, fixture.Id, module);
        var state = OwnerJourneyState(service, stamp, fixture.Id);
        var finishRequest = new CharacterCreationLifeModuleFinishRequest(state.Binding, state.DraftRevision, state.DraftDigest);
        var finish = JourneySuccess(service.PreviewFinishSelection(stamp, finishRequest));
        before = fixture.CaptureAll();
        var forged = finishRequest with { Binding = finishRequest.Binding with { SourceDigest = new string('0', 64) } };
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, service.PreviewFinishSelection(stamp, forged).Outcome);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict,
            service.ConfirmFinishSelection(stamp, new(forged, finish.PreviewDigest, true)).Outcome);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict,
            service.ConfirmFinishSelection(stamp, new(finishRequest, new string('0', 64), true)).Outcome);
        fixture.AssertUnchanged(before);
        JourneySuccess(service.ConfirmFinishSelection(stamp, new(finishRequest, finish.PreviewDigest, true)));
        before = fixture.CaptureAll();
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
            service.ConfirmFinishSelection(stamp, new(finishRequest, finish.PreviewDigest, true)).Outcome);
        fixture.AssertUnchanged(before);
        Assert.IsTrue(resolver.Scopes.All(scope => scope.Disposed));
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Owner_bound_journey_unwinds_source_scope_and_owner_lease_on_failure(bool duringCreation)
    {
        using var fixture = new OwnerJourneyFixture();
        var store = new JourneyScopedStore(fixture);
        var resolver = new JourneyOperationResolver(fixture) { FailCreate = duringCreation, FailQuery = !duringCreation };
        var before = fixture.CaptureAll();
        var service = fixture.Service(store, resolver);
        Assert.ThrowsExactly<JourneySourceException>(() => service.LoadJourney(fixture.Owner.Capture(), new(fixture.Id)));
        Assert.HasCount(duringCreation ? 0 : 1, resolver.Scopes);
        Assert.IsTrue(resolver.Scopes.All(scope => scope.Disposed));
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
        Assert.AreEqual(0, store.Commits);
        fixture.AssertUnchanged(before);
    }

    private static T JourneySuccess<T>(CharacterCreationFoundationResult<T> result) where T : class
    {
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(", ", result.Blockers));
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static void JourneyAdmissionDenied<T>(CharacterCreationFoundationResult<T> result) where T : class
    {
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        Assert.IsNull(result.Value);
        CollectionAssert.Contains(result.Blockers.ToList(), CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
    }

    private static CharacterCreationLifeModuleJourneyState OwnerJourneyState(
        IOwnerBoundCharacterCreationLifeModuleJourneyService service, OwnerContextStamp stamp, CharacterWorkspaceId id)
        => JourneySuccess(service.LoadJourney(stamp, new(id)));

    private static void OwnerJourneyAppend(IOwnerBoundCharacterCreationLifeModuleJourneyService service,
        OwnerContextStamp stamp, CharacterWorkspaceId id, string module)
    {
        var request = JourneyRequest(OwnerJourneyState(service, stamp, id), module);
        var preview = JourneySuccess(service.PreviewModule(stamp, request));
        JourneySuccess(service.ConfirmModule(stamp, new(request, preview.PreviewDigest, true)));
    }

    private sealed class OwnerJourneyFixture : IDisposable
    {
        private readonly Dictionary<OwnerScope, string> _paths = [];
        public string Root { get; } = Directory.CreateTempSubdirectory("chummer-journey-owner-").FullName;
        public CharacterWorkspaceId Id { get; } = new("owner-bound-journey");
        public FileWorkspaceStore Store { get; }
        public AvailabilityOwner Owner { get; } = new(JourneyAccountA);

        public OwnerJourneyFixture()
        {
            string sourceRoot = Path.Combine(Root, "seed");
            var source = SeedJourney(sourceRoot, Id);
            Store = new(Root);
            foreach (var owner in new[] { JourneyAccountA, JourneyAccountB, OwnerScope.LocalSingleUser })
            {
                var previous = Directory.GetFiles(Root, Id.Value + ".json", SearchOption.AllDirectories);
                var placeholder = new WorkspaceDocument(CharacterXml("Human"), RulesetDefaults.Sr5);
                var created = owner.IsLocalSingleUser ? Store.CreateWorkspaceDocument(Id, placeholder)
                    : Store.CreateWorkspaceDocument(owner, Id, placeholder);
                Assert.IsTrue(created.Success, created.Error);
                string path = Directory.GetFiles(Root, Id.Value + ".json", SearchOption.AllDirectories)
                    .Except(previous, StringComparer.Ordinal).Single();
                _paths.Add(owner, path);
                if (owner != JourneyAccountA) continue;
                // Test-only installation of genuine committed bytes in a partition
                // created through the real owner-scoped API, as in finalizer fixtures.
                File.Copy(WorkspacePath(sourceRoot, Id), path, overwrite: true);
                CollectionAssert.AreEqual(File.ReadAllBytes(WorkspacePath(sourceRoot, Id)), File.ReadAllBytes(path));
                Assert.AreEqual(source.Get(Id).Value!.Document.AuxiliaryStateDigest,
                    Read(owner).Document.AuxiliaryStateDigest);
            }
        }

        public WorkspaceStoredDocument Read(OwnerScope owner) => (owner.IsLocalSingleUser
            ? new FileWorkspaceStore(Root).Get(Id) : new FileWorkspaceStore(Root).Get(owner, Id)).Value!;
        public IOwnerBoundCharacterCreationLifeModuleJourneyService Service(IWorkspaceStore store,
            ICharacterSourceDataResolver resolver, IOwnerContextAccessor? owner = null)
            => new OwnerBoundCharacterCreationLifeModuleJourneyService(store, owner ?? Owner,
                new XmlCharacterFileQueries(new CharacterFileService()), resolver, CreateCatalog());
        public Dictionary<OwnerScope, (string Bytes, DateTime Timestamp)> Capture(params OwnerScope[] owners)
            => owners.ToDictionary(owner => owner, owner => (Convert.ToBase64String(File.ReadAllBytes(_paths[owner])),
                File.GetLastWriteTimeUtc(_paths[owner])));
        public Dictionary<OwnerScope, (string Bytes, DateTime Timestamp)> CaptureAll() => Capture(_paths.Keys.ToArray());
        public void AssertUnchanged(Dictionary<OwnerScope, (string Bytes, DateTime Timestamp)> before)
        {
            foreach (var entry in before) Assert.AreEqual(entry.Value, Capture(entry.Key)[entry.Key], entry.Key.Value);
        }
        public void AssertLeaseHeld() => Assert.AreEqual(1, Owner.ActiveLeases);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private class JourneyReadStore(OwnerJourneyFixture fixture) : IWorkspaceStore
    {
        protected OwnerJourneyFixture Fixture { get; } = fixture;
        public CharacterWorkspaceId ExpectedId { get; set; } = fixture.Id;
        public int Reads { get; private set; }
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id) => throw new AssertFailedException("Unexpected local read.");
        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id)
        {
            Fixture.AssertLeaseHeld();
            Assert.AreEqual(JourneyAccountA, owner);
            Assert.AreEqual(ExpectedId, id);
            Reads++;
            return new FileWorkspaceStore(Fixture.Root).Get(owner, id);
        }
        public IReadOnlyList<WorkspaceStoreEntry> List() => throw new AssertFailedException("Unexpected inventory.");
        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => throw new AssertFailedException("Unexpected inventory.");
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document) => Unexpected();
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document) => Unexpected();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(CharacterWorkspaceId id, long revision, WorkspaceDocument document) => Unexpected();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(OwnerScope owner, CharacterWorkspaceId id, long revision, WorkspaceDocument document) => Unexpected();
        public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long revision) => Unexpected();
        public WorkspaceStoreMutationResult SaveCheckpoint(OwnerScope owner, CharacterWorkspaceId id, long revision) => Unexpected();
        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long revision) => Unexpected();
        public WorkspaceStoreMutationResult Delete(OwnerScope owner, CharacterWorkspaceId id, long revision) => Unexpected();
        protected static WorkspaceStoreMutationResult Unexpected() => throw new AssertFailedException("Unexpected mutation lane.");
    }

    private class JourneyLocalAtomicStore(OwnerJourneyFixture fixture) : JourneyReadStore(fixture), IWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        public bool SupportsWorkspaceAuxiliaryStateAtomicCommit => true;
        public int Commits { get; protected set; }
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            CharacterWorkspaceId id, long revision, string digest, WorkspaceDocument document) => Unexpected();
    }

    private sealed class JourneyScopedStore(OwnerJourneyFixture fixture) : JourneyLocalAtomicStore(fixture), IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        public bool SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit => true;
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            OwnerScope owner, CharacterWorkspaceId id, long revision, string digest, WorkspaceDocument document)
        {
            Fixture.AssertLeaseHeld();
            Assert.AreEqual(JourneyAccountA, owner);
            Assert.AreEqual(ExpectedId, id);
            var current = new FileWorkspaceStore(Fixture.Root).Get(owner, id).Value!;
            Assert.AreEqual(current.ContentRevision, revision);
            Assert.AreEqual(current.Document.AuxiliaryStateDigest, digest);
            Assert.AreEqual(current.Document.Content, document.Content);
            var result = new FileWorkspaceStore(Fixture.Root).ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                owner, id, revision, digest, document);
            if (result.Success) Commits++;
            return result;
        }
    }

    private sealed class JourneySourceException : Exception { }

    private sealed class JourneyOperationResolver(OwnerJourneyFixture fixture)
        : ICharacterSourceDataResolver, ICharacterSourceDataResolverOperationScopeFactory
    {
        private readonly FileSystemCharacterSourceDataResolver _inner = new(CreateOverlays());
        public List<JourneyOperationScope> Scopes { get; } = [];
        public bool FailCreate { get; init; }
        public bool FailQuery { get; init; }
        public ICharacterSourceDataContext? TryCreateContext(string xml)
            => throw new AssertFailedException("A capable resolver must use its operation scope.");
        public ICharacterSourceDataResolverOperationScope CreateOperationScope()
        {
            fixture.AssertLeaseHeld();
            if (FailCreate) throw new JourneySourceException();
            var scope = new JourneyOperationScope(fixture, _inner.CreateOperationScope(), FailQuery);
            Scopes.Add(scope);
            return scope;
        }
    }

    private sealed class JourneyOperationScope(OwnerJourneyFixture fixture,
        ICharacterSourceDataResolverOperationScope inner, bool failQuery) : ICharacterSourceDataResolverOperationScope
    {
        public bool Disposed { get; private set; }
        public int Calls { get; private set; }
        public ICharacterSourceDataContext? TryCreateContext(string xml)
        {
            fixture.AssertLeaseHeld();
            Assert.IsFalse(Disposed);
            Calls++;
            if (failQuery) throw new JourneySourceException();
            Assert.AreEqual(CharacterXml("Human"), xml, "Journey must keep the original character XML.");
            return inner.TryCreateContext(xml);
        }
        public void Dispose()
        {
            fixture.AssertLeaseHeld();
            Assert.IsFalse(Disposed, "The scope must close once, before its owner lease.");
            inner.Dispose();
            Disposed = true;
        }
    }

    private sealed class JourneyCurrentOnlyOwner : IOwnerContextAccessor
    {
        public OwnerScope Current => JourneyAccountA;
    }

    private sealed class JourneyUnusableOwner(bool nullLease) : IOwnerContextLeaseAccessor
    {
        public OwnerScope Current => JourneyAccountA;
        public OwnerContextStamp Capture() => throw new AssertFailedException("The facade must admit the original stamp.");
        public int Attempts { get; private set; }
        public List<JourneyMismatchedLease> Leases { get; } = [];
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            Attempts++;
            if (nullLease) { lease = null; return false; }
            var wrong = new JourneyMismatchedLease();
            Leases.Add(wrong);
            lease = wrong;
            return true;
        }
    }

    private sealed class JourneyMismatchedLease : IOwnerContextLease
    {
        public OwnerContextStamp Stamp => default;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
