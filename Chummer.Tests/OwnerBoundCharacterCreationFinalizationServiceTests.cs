using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
[DoNotParallelize]
public sealed class OwnerBoundCharacterCreationFinalizationServiceTests
{
    private static readonly OwnerScope AccountA = new("finalization-account-a");
    private static readonly OwnerScope AccountB = new("finalization-account-b");
    private static ReadyContext s_source = null!;

    [ClassInitialize]
    public static void Initialize(TestContext _)
    {
        // One genuine, nonempty, complete awakened draft graph. Each test gets
        // independent private storage; no test may mutate this source fixture.
        s_source = ReadyContext.Create(true, includeNonEmptyPurchases: true,
            talentValue: "Mystic Adept", mysticPowerPoints: 2);
    }

    [ClassCleanup]
    public static void Cleanup() => s_source?.Dispose();

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, true)]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true, true)]
    [DataRow(true, true, true)]
    [DataRow(false, false, true)]
    [DataRow(true, false, true)]
    public void Overview_read_preserves_each_domain_and_reuses_only_admitted_observations(
        bool linked, bool includePriorityDrafts, bool includeFoundation = false)
    {
        using var fixture = new Fixture(linked ? AccountA : OwnerScope.LocalSingleUser);
        var before = fixture.CaptureAllPartitions();
        var stamp = fixture.Owner.Capture();
        var baselineStore = new ScopedAtomicStore(fixture);
        var baselineResolver = new ObservedOperationResolver(fixture, s_source.Resolver);
        var expectedFoundation = includeFoundation
            ? new OwnerBoundCharacterCreationLifeModuleFinalizationService(
                baselineStore, fixture.Owner, s_source.Queries, baselineResolver, OverviewCatalog())
                .Load(stamp, fixture.Id) : null;
        var expected = new CharacterCreationOverviewRead(
            new OwnerBoundCharacterCreationContactsService(
                new CharacterCreationContactsService(baselineStore, baselineResolver), fixture.Owner)
                .Load(stamp, new(fixture.Id)),
            includePriorityDrafts ? new OwnerBoundCharacterCreationQualitiesService(
                baselineStore, fixture.Owner, s_source.Queries, baselineResolver).Load(stamp, new(fixture.Id)) : null,
            includePriorityDrafts ? new OwnerBoundCharacterCreationMagicResonanceService(
                baselineStore, fixture.Owner, baselineResolver).Load(stamp, new(fixture.Id)) : null,
            new OwnerBoundCharacterCreationLifestylesReader(baselineStore, fixture.Owner, baselineResolver)
                .Load(stamp, new(fixture.Id)),
            new OwnerBoundCharacterCreationFinalizationService(
                baselineStore, fixture.Owner, s_source.Queries, baselineResolver).Load(stamp, new(fixture.Id)))
            { Foundation = expectedFoundation };
        Assert.IsNotNull(expected.Contacts.Value);
        Assert.IsTrue(expected.Contacts.Value.CanEdit);
        Assert.IsNotNull(expected.Finalization.Value);
        Assert.IsTrue(expected.Finalization.Value.Steps.All(step => step.IsComplete));
        int queries = baselineResolver.Scopes.Sum(scope => scope.Calls);

        var store = new ScopedAtomicStore(fixture);
        var resolver = new ObservedOperationResolver(fixture, s_source.Resolver);
        IOwnerBoundCharacterCreationOverviewReader service = includeFoundation
            ? new OwnerBoundCharacterCreationLifeModuleFinalizationService(
                store, fixture.Owner, s_source.Queries, resolver, OverviewCatalog())
            : new OwnerBoundCharacterCreationFinalizationService(store, fixture.Owner, s_source.Queries, resolver);
        var actual = service.LoadOverview(stamp, fixture.Id, includePriorityDrafts);
        Assert.IsNotNull(actual);
        AssertJsonEquals(expected, actual);
        Assert.AreEqual(1, store.Reads, "One complete validated workspace observation, including local history.");
        Assert.AreEqual(linked ? 0 : 1, store.LocalReads);
        Assert.HasCount(1, resolver.Scopes);
        var first = resolver.Scopes[0];
        Assert.AreEqual(queries, first.Calls, "No live source admission was skipped.");
        Assert.HasCount(1, first.UniqueContexts);
        Assert.IsTrue(first.Disposed);

        AssertJsonEquals(expected, service.LoadOverview(stamp, fixture.Id, includePriorityDrafts)!);
        Assert.AreEqual(2, store.Reads, "A new public read must observe storage again.");
        Assert.HasCount(2, resolver.Scopes);
        Assert.HasCount(1, resolver.Scopes[1].UniqueContexts);
        Assert.AreNotSame(first.UniqueContexts[0], resolver.Scopes[1].UniqueContexts[0]);
        Assert.IsTrue(resolver.Scopes[1].Disposed);
        Assert.AreEqual(0, resolver.UnscopedCalls);
        Assert.AreEqual(0, store.Commits);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
        fixture.AssertPartitionsUnchanged(before);
    }

    private static XmlLifeModulesCatalogService OverviewCatalog()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null)
        {
            string path = Path.Combine(root.FullName, "Chummer", "data", "lifemodules.xml");
            if (File.Exists(path)) return new(path);
            root = root.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the canonical Life Modules catalog.");
    }

    [TestMethod]
    [DataRow("owner-B")]
    [DataRow("owner-ABA")]
    [DataRow("foreign-issuer")]
    public void Overview_read_rejects_stale_owner_before_sources_or_storage(string denial)
    {
        using var fixture = new Fixture(AccountA);
        var original = fixture.Owner.Capture();
        if (denial == "foreign-issuer") original = new TestOwner(AccountA).Capture();
        else
        {
            fixture.Owner.Transition(AccountB);
            if (denial == "owner-ABA") fixture.Owner.Transition(AccountA);
        }
        var before = fixture.CaptureAllPartitions();
        var store = new ScopedAtomicStore(fixture);
        var resolver = new ObservedOperationResolver(fixture, s_source.Resolver);
        var service = new OwnerBoundCharacterCreationFinalizationService(store, fixture.Owner, s_source.Queries, resolver);
        Assert.IsNull(service.LoadOverview(original, fixture.Id, true));
        var foundation = new OwnerBoundCharacterCreationLifeModuleFinalizationService(
            store, fixture.Owner, s_source.Queries, resolver, OverviewCatalog());
        Assert.IsNull(foundation.LoadOverview(original, fixture.Id, false));
        Assert.IsEmpty(resolver.Scopes);
        Assert.AreEqual(0, store.Reads);
        Assert.AreEqual(0, store.Commits);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
        fixture.AssertPartitionsUnchanged(before);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Overview_read_unwinds_source_scope_and_owner_lease_on_failure(bool duringCreation)
    {
        using var fixture = new Fixture(AccountA);
        var before = fixture.CaptureAllPartitions();
        var store = new ScopedAtomicStore(fixture);
        var resolver = new ObservedOperationResolver(fixture, s_source.Resolver)
            { FailCreate = duringCreation, FailQuery = !duringCreation };
        var service = new OwnerBoundCharacterCreationFinalizationService(store, fixture.Owner, s_source.Queries, resolver);
        Assert.ThrowsExactly<OperationSourceTestException>(() => service.LoadOverview(fixture.Owner.Capture(), fixture.Id, true));
        Assert.AreEqual(1, resolver.InjectedFailures);
        Assert.HasCount(duringCreation ? 0 : 1, resolver.Scopes);
        Assert.IsTrue(resolver.Scopes.All(scope => scope.Disposed));
        Assert.AreEqual(0, store.Commits);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
        fixture.AssertPartitionsUnchanged(before);
    }

    [TestMethod]
    public void Overview_read_does_not_turn_failed_source_readmission_into_ready_state()
    {
        using var fixture = new Fixture(AccountA);
        var before = fixture.CaptureAllPartitions();
        var store = new ScopedAtomicStore(fixture);
        var resolver = new ObservedOperationResolver(fixture, s_source.Resolver) { DenyRepeatedContext = true };
        var service = new OwnerBoundCharacterCreationFinalizationService(store, fixture.Owner, s_source.Queries, resolver);
        var actual = service.LoadOverview(fixture.Owner.Capture(), fixture.Id, true);
        Assert.IsNotNull(actual);
        Assert.IsNotNull(actual.Contacts.Value);
        Assert.IsFalse(actual.Contacts.Value.CanEdit);
        Assert.IsTrue(actual.Finalization.Value is null || !actual.Finalization.Value.Steps.All(step => step.IsComplete));
        Assert.HasCount(1, resolver.Scopes);
        Assert.IsTrue(resolver.Scopes[0].Calls > 1);
        Assert.IsTrue(resolver.Scopes[0].Disposed);
        Assert.AreEqual(0, store.Commits);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
        fixture.AssertPartitionsUnchanged(before);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Contacts_pending_read_shares_sources_only_inside_admitted_operation(bool linked)
    {
        using var fixture = new Fixture(linked ? AccountA : OwnerScope.LocalSingleUser);
        var before = fixture.CaptureAllPartitions();
        var stamp = fixture.Owner.Capture();
        var baselineStore = new ScopedAtomicStore(fixture);
        var baselineResolver = new ObservedResolver(fixture, s_source.Resolver);
        var baseline = new OwnerBoundCharacterCreationContactsService(
            new CharacterCreationContactsService(baselineStore, baselineResolver), fixture.Owner);
        var expected = baseline.Load(stamp, new(fixture.Id));
        Assert.IsNotNull(expected.Value);
        Assert.IsTrue(expected.Value.CanEdit, JsonSerializer.Serialize(expected));
        int expectedQueries = baselineResolver.Calls;
        Assert.IsTrue(expectedQueries > 1, "Exercise bootstrap and contact source admission.");

        var store = new ScopedAtomicStore(fixture);
        var resolver = new ObservedOperationResolver(fixture, s_source.Resolver);
        var service = new OwnerBoundCharacterCreationContactsService(
            new CharacterCreationContactsService(store, resolver), fixture.Owner);
        var actual = service.Load(stamp, new(fixture.Id));
        AssertJsonEquals(expected, actual);
        Assert.AreEqual(baselineStore.Reads, store.Reads);
        Assert.HasCount(1, resolver.Scopes);
        var first = resolver.Scopes[0];
        Assert.AreEqual(expectedQueries, first.Calls, "Every live source admission remains required.");
        Assert.HasCount(1, first.UniqueContexts);
        Assert.IsTrue(first.Disposed);
        AssertJsonEquals(expected, service.Load(stamp, new(fixture.Id)));
        Assert.HasCount(2, resolver.Scopes);
        Assert.HasCount(1, resolver.Scopes[1].UniqueContexts);
        Assert.AreNotSame(first.UniqueContexts[0], resolver.Scopes[1].UniqueContexts[0]);
        Assert.IsTrue(resolver.Scopes[1].Disposed);
        Assert.AreEqual(0, resolver.UnscopedCalls);
        Assert.AreEqual(0, store.Commits);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
        fixture.AssertPartitionsUnchanged(before);
    }

    [TestMethod]
    [DataRow("owner-B")]
    [DataRow("owner-ABA")]
    [DataRow("foreign-issuer")]
    public void Contacts_pending_scope_requires_original_owner_admission(string denial)
    {
        using var fixture = new Fixture(AccountA);
        var original = fixture.Owner.Capture();
        if (denial == "foreign-issuer") original = new TestOwner(AccountA).Capture();
        else
        {
            fixture.Owner.Transition(AccountB);
            if (denial == "owner-ABA") fixture.Owner.Transition(AccountA);
        }
        var before = fixture.CaptureAllPartitions();
        var store = new ScopedAtomicStore(fixture);
        var resolver = new ObservedOperationResolver(fixture, s_source.Resolver);
        var service = new OwnerBoundCharacterCreationContactsService(
            new CharacterCreationContactsService(store, resolver), fixture.Owner);
        var result = service.Load(original, new(fixture.Id));
        Assert.AreEqual(CharacterCreationContactOutcomes.Unavailable, result.Outcome);
        Assert.IsNull(result.Value);
        Assert.IsEmpty(resolver.Scopes);
        Assert.AreEqual(0, resolver.UnscopedCalls);
        Assert.AreEqual(0, store.Reads);
        Assert.AreEqual(0, store.Commits);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
        fixture.AssertPartitionsUnchanged(before);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Contacts_pending_scope_and_owner_lease_close_on_source_failure(bool duringCreation)
    {
        using var fixture = new Fixture(AccountA);
        var before = fixture.CaptureAllPartitions();
        var store = new ScopedAtomicStore(fixture);
        var resolver = new ObservedOperationResolver(fixture, s_source.Resolver)
        { FailCreate = duringCreation, FailQuery = !duringCreation };
        var service = new OwnerBoundCharacterCreationContactsService(
            new CharacterCreationContactsService(store, resolver), fixture.Owner);
        Assert.ThrowsExactly<OperationSourceTestException>(() => service.Load(fixture.Owner.Capture(), new(fixture.Id)));
        Assert.AreEqual(1, resolver.InjectedFailures);
        Assert.HasCount(duringCreation ? 0 : 1, resolver.Scopes);
        Assert.IsTrue(resolver.Scopes.All(scope => scope.Disposed));
        Assert.AreEqual(0, store.Commits);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
        fixture.AssertPartitionsUnchanged(before);
    }

    [TestMethod]
    public void Contacts_pending_scope_rejects_failed_repeated_source_admission()
    {
        using var fixture = new Fixture(AccountA);
        var before = fixture.CaptureAllPartitions();
        var store = new ScopedAtomicStore(fixture);
        var resolver = new ObservedOperationResolver(fixture, s_source.Resolver) { DenyRepeatedContext = true };
        var service = new OwnerBoundCharacterCreationContactsService(
            new CharacterCreationContactsService(store, resolver), fixture.Owner);
        var result = service.Load(fixture.Owner.Capture(), new(fixture.Id));
        Assert.IsNotNull(result.Value);
        Assert.IsFalse(result.Value.CanEdit);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationContactsBlockers.BudgetAuthorityRequired);
        Assert.HasCount(1, resolver.Scopes);
        Assert.AreEqual(2, resolver.Scopes[0].Calls);
        Assert.IsTrue(resolver.Scopes[0].Disposed);
        Assert.AreEqual(0, store.Commits);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
        fixture.AssertPartitionsUnchanged(before);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Contacts_pending_scope_preserves_confirm_cold_reopen_and_receipt_replay(bool linked)
    {
        using var fixture = new Fixture(linked ? AccountA : OwnerScope.LocalSingleUser);
        var otherPartitions = fixture.CaptureOtherPartitions();
        var before = fixture.Read(fixture.Owner.Current);
        var stamp = fixture.Owner.Capture();
        var resolver = new ObservedOperationResolver(fixture, s_source.Resolver);
        var service = new OwnerBoundCharacterCreationContactsService(
            new CharacterCreationContactsService(fixture.Store, resolver), fixture.Owner);
        var loaded = service.Load(stamp, new(fixture.Id)).Value!;
        Assert.IsNotNull(loaded.NewContactTemplate);
        var edit = new CharacterCreationContactEdit(Guid.Parse("b78b9335-ac53-431c-8ad9-b1f3b08e20de"),
            loaded.NewContactTemplate.Identity with { Name = "Source-scope test contact" },
            Connection: 1, Loyalty: 1) { ChangeKind = CharacterCreationContactChangeKind.Add };
        var preview = service.Preview(stamp, new(loaded.Binding, edit));
        Assert.IsNotNull(preview.Value, JsonSerializer.Serialize(preview));
        var command = new CharacterCreationContactConfirmRequest(loaded.Binding, edit,
            preview.Value.PreviewDigest, "contacts-source-scope", ExplicitlyConfirmed: true);
        var applied = service.Confirm(stamp, command);
        Assert.AreEqual(CharacterCreationContactOutcomes.Applied, applied.Outcome, JsonSerializer.Serialize(applied));
        Assert.IsNotNull(applied.Value);
        var cold = new OwnerBoundCharacterCreationContactsService(
            new CharacterCreationContactsService(new FileWorkspaceStore(fixture.Root), resolver), fixture.Owner);
        var reopened = cold.Load(stamp, new(fixture.Id));
        Assert.IsNotNull(reopened.Value);
        Assert.IsTrue(reopened.Value.Contacts.Any(contact => contact.ContactId == edit.ContactId));
        var replay = cold.Confirm(stamp, command);
        Assert.AreEqual(CharacterCreationContactOutcomes.Replayed, replay.Outcome, JsonSerializer.Serialize(replay));
        AssertJsonEquals(applied.Value, replay.Value);
        var after = fixture.Read(fixture.Owner.Current);
        Assert.AreEqual(before.Document.Content, after.Document.Content, "Pending contacts change typed draft state only.");
        Assert.AreEqual(before.ContentRevision + 1, after.ContentRevision);
        Assert.AreEqual(after.ContentRevision, after.SavedRevision);
        Assert.IsTrue(resolver.Scopes.All(scope => scope.Disposed));
        Assert.AreEqual(0, resolver.UnscopedCalls);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
        fixture.AssertOtherPartitionsUnchanged(otherPartitions);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Magic_operation_scope_preserves_full_state_preview_and_fresh_admission(bool linked)
    {
        using var fixture = new Fixture(linked ? AccountA : OwnerScope.LocalSingleUser);
        var before = fixture.CaptureAllPartitions();
        var stamp = fixture.Owner.Capture();
        var baselineStore = new ScopedAtomicStore(fixture);
        var baselineResolver = new ObservedResolver(fixture, s_source.Resolver);
        var baseline = new OwnerBoundCharacterCreationMagicResonanceService(
            baselineStore, fixture.Owner, baselineResolver);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long allocationStart = GC.GetAllocatedBytesForCurrentThread();
        var expected = baseline.Load(stamp, new(fixture.Id));
        long baselineAllocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        double baselineMs = clock.Elapsed.TotalMilliseconds;
        Assert.IsNotNull(expected.Value);
        Assert.IsTrue(expected.Value.CanEdit, JsonSerializer.Serialize(expected));
        Assert.IsNotNull(expected.Value.PendingDraft);
        int baselineCalls = baselineResolver.Calls;

        var store = new ScopedAtomicStore(fixture);
        var resolver = new ObservedOperationResolver(fixture, s_source.Resolver);
        var service = new OwnerBoundCharacterCreationMagicResonanceService(store, fixture.Owner, resolver);
        clock.Restart();
        allocationStart = GC.GetAllocatedBytesForCurrentThread();
        var actual = service.Load(stamp, new(fixture.Id));
        long scopedAllocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        double scopedMs = clock.Elapsed.TotalMilliseconds;
        AssertJsonEquals(expected, actual); // Every catalog row, budget, binding and digest.
        Assert.AreEqual(baselineStore.Reads, store.Reads, "Keep all workspace/domain validation.");
        Assert.AreEqual(1, store.Reads, "The nested Attributes projection shares this single store observation.");
        Assert.IsTrue(fixture.Owner.TryAcquire(stamp, out var freshLease));
        using (freshLease)
        using (var freshSources = ((ICharacterSourceDataResolverOperationScopeFactory)s_source.Resolver).CreateOperationScope())
        {
            var freshStore = new ScopedAtomicStore(fixture);
            var freshView = new OwnerBoundCreationWorkspaceStore(freshStore, freshLease!, stamp, fixture.Id);
            var freshLoad = new CharacterCreationMagicResonanceService(freshView, freshSources).Load(new(fixture.Id));
            AssertJsonEquals(freshLoad, actual);
            Assert.AreEqual(2, freshStore.Reads, "The reference graph must still exercise repeated reads.");
        }
        var loadScope = resolver.Scopes.Single();
        Assert.AreEqual(baselineCalls, loadScope.Calls);
        Assert.IsTrue(baselineCalls > 1, "Exercise the nested Attributes context request.");
        Assert.HasCount(1, loadScope.UniqueContexts);
        Assert.IsTrue(loadScope.Disposed);

        // A duplicate saved selection must retain its existing rejection, not
        // become confirmable as a side effect of resolver reuse.
        var previewRequest = new CharacterCreationMagicResonancePreviewRequest(
            expected.Value.Binding, expected.Value.PendingDraft.Selections);
        AssertJsonEquals(baseline.Preview(stamp, previewRequest), service.Preview(stamp, previewRequest));
        AssertJsonEquals(baseline.LoadReReview(stamp, new(fixture.Id)), service.LoadReReview(stamp, new(fixture.Id)));
        AssertJsonEquals(expected, service.Load(stamp, new(fixture.Id)));
        Assert.HasCount(4, resolver.Scopes);
        Assert.IsTrue(resolver.Scopes.All(scope => scope.Disposed));
        Assert.HasCount(1, resolver.Scopes[3].UniqueContexts);
        Assert.AreNotSame(loadScope.UniqueContexts[0], resolver.Scopes[3].UniqueContexts[0],
            "Never retain source context between operations.");
        Assert.AreEqual(0, resolver.UnscopedCalls);
        Assert.AreEqual(0, store.Commits);
        fixture.AssertPartitionsUnchanged(before);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
        Console.WriteLine($"magic-operation-scope requests={baselineCalls} scopedContexts=1 "
            + $"baselineMs={baselineMs:F3} scopedMs={scopedMs:F3} "
            + $"baselineAllocatedBytes={baselineAllocated} scopedAllocatedBytes={scopedAllocated}; "
            + "managed diagnostic only");
    }

    [TestMethod]
    [DataRow("owner-B")]
    [DataRow("owner-ABA")]
    [DataRow("foreign-issuer")]
    public void Magic_operation_scope_is_not_created_before_owner_admission(string denial)
    {
        using var fixture = new Fixture(AccountA);
        var original = fixture.Owner.Capture();
        if (denial == "foreign-issuer") original = new TestOwner(AccountA).Capture();
        else
        {
            fixture.Owner.Transition(AccountB);
            if (denial == "owner-ABA") fixture.Owner.Transition(AccountA);
        }
        var before = fixture.CaptureAllPartitions();
        var store = new ScopedAtomicStore(fixture);
        var resolver = new ObservedOperationResolver(fixture, s_source.Resolver);
        var service = new OwnerBoundCharacterCreationMagicResonanceService(store, fixture.Owner, resolver);
        var result = service.Load(original, new(fixture.Id));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        Assert.IsNull(result.Value);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision);
        Assert.IsEmpty(resolver.Scopes);
        Assert.AreEqual(0, resolver.UnscopedCalls);
        Assert.AreEqual(0, store.Reads);
        Assert.AreEqual(0, store.Commits);
        fixture.AssertPartitionsUnchanged(before);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Magic_operation_scope_and_owner_lease_are_disposed_on_failure(bool duringCreation)
    {
        using var fixture = new Fixture(AccountA);
        var before = fixture.CaptureAllPartitions();
        var store = new ScopedAtomicStore(fixture);
        var resolver = new ObservedOperationResolver(fixture, s_source.Resolver)
        {
            FailCreate = duringCreation,
            FailQuery = !duringCreation
        };
        var service = new OwnerBoundCharacterCreationMagicResonanceService(store, fixture.Owner, resolver);
        Assert.ThrowsExactly<OperationSourceTestException>(() => service.Load(fixture.Owner.Capture(), new(fixture.Id)));
        Assert.AreEqual(1, resolver.InjectedFailures);
        Assert.HasCount(duringCreation ? 0 : 1, resolver.Scopes);
        Assert.IsTrue(resolver.Scopes.All(scope => scope.Disposed));
        Assert.AreEqual(0, store.Commits);
        fixture.AssertPartitionsUnchanged(before);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Magic_operation_scope_preserves_confirm_cold_reopen_and_replay(bool linked)
    {
        var owner = linked ? AccountA : OwnerScope.LocalSingleUser;
        using var baselineFixture = new Fixture(owner);
        using var fixture = new Fixture(owner);
        var unchanged = fixture.CaptureOtherPartitions();
        var before = fixture.Read(owner);
        var baselineStore = new ScopedAtomicStore(baselineFixture);
        var baseline = new OwnerBoundCharacterCreationMagicResonanceService(baselineStore,
            baselineFixture.Owner, new ObservedResolver(baselineFixture, s_source.Resolver));
        var store = new ScopedAtomicStore(fixture);
        var resolver = new ObservedOperationResolver(fixture, s_source.Resolver);
        var service = new OwnerBoundCharacterCreationMagicResonanceService(store, fixture.Owner, resolver);
        var stamp = fixture.Owner.Capture();
        var baselineStamp = baselineFixture.Owner.Capture();
        var loaded = service.Load(stamp, new(fixture.Id));
        AssertJsonEquals(baseline.Load(baselineStamp, new(fixture.Id)), loaded);
        Assert.IsNotNull(loaded.Value);
        Assert.IsNotNull(loaded.Value.PendingDraft);
        var saved = loaded.Value.PendingDraft.Selections;
        Assert.IsNotEmpty(saved.Spells);
        var replacement = loaded.Value.Authority.Spells.First(option => option.IsEnabled
            && !saved.Spells.Contains(option.Identity)).Identity;
        var selections = saved with { Spells = saved.Spells.Skip(1).Append(replacement).ToArray() };
        var preview = service.Preview(stamp, new(loaded.Value.Binding, selections));
        AssertJsonEquals(baseline.Preview(baselineStamp, new(loaded.Value.Binding, selections)), preview);
        Assert.IsNotNull(preview.Value);
        Assert.IsTrue(preview.Value.CanConfirm, JsonSerializer.Serialize(preview));
        var command = new CharacterCreationMagicResonanceConfirmRequest(loaded.Value.Binding,
            selections, preview.Value.PreviewDigest, "magic-scope-replace-spell", true);
        var confirmed = service.Confirm(stamp, command);
        AssertJsonEquals(baseline.Confirm(baselineStamp, command), confirmed);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome);
        Assert.IsNotNull(confirmed.Value);
        Assert.AreEqual(1, store.Commits);
        Assert.AreEqual(baselineStore.Reads, store.Reads);
        var after = fixture.Read(owner);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.ContentRevision + 1, after.ContentRevision);
        Assert.AreEqual(after.ContentRevision, after.SavedRevision);
        AssertJsonEquals(baselineFixture.Read(owner).Document.AuxiliaryState, after.Document.AuxiliaryState);
        // The existing domain canonicalizes selection order in the reviewed preview.
        AssertJsonEquals(preview.Value.Selections,
            after.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft!.Selections);
        Assert.HasCount(3, resolver.Scopes);
        Assert.IsTrue(resolver.Scopes.All(scope => scope.Disposed && scope.UniqueContexts.Count == 1));

        var coldStore = new ScopedAtomicStore(fixture); // Every read opens a new FileWorkspaceStore.
        var coldResolver = new ObservedOperationResolver(fixture, s_source.Resolver);
        var cold = new OwnerBoundCharacterCreationMagicResonanceService(coldStore, fixture.Owner, coldResolver);
        var reopened = cold.Load(stamp, new(fixture.Id));
        Assert.IsNotNull(reopened.Value);
        Assert.IsTrue(reopened.Value.CanEdit, JsonSerializer.Serialize(reopened));
        Assert.AreEqual(confirmed.Value.DraftDigest, reopened.Value.PendingDraft!.DraftDigest);
        var committedBytes = fixture.CapturePartition(owner);
        AssertJsonEquals(confirmed, cold.Confirm(stamp, command));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict,
            cold.Confirm(stamp, command with { PreviewDigest = "changed-preview" }).Outcome);
        Assert.AreEqual(0, coldStore.Commits);
        Assert.AreEqual(committedBytes, fixture.CapturePartition(owner));
        Assert.IsTrue(coldResolver.Scopes.All(scope => scope.Disposed));
        Assert.AreEqual(0, coldResolver.Scopes[1].Calls, "Replay only reads the durable receipt.");
        Assert.AreEqual(0, resolver.UnscopedCalls + coldResolver.UnscopedCalls);
        fixture.AssertOtherPartitionsUnchanged(unchanged);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void Full_canonical_graph_finalizes_only_the_admitted_partition_and_replays_cold(bool linked, bool scoped)
    {
        using var fixture = new Fixture(linked ? AccountA : OwnerScope.LocalSingleUser);
        var before = fixture.Read(fixture.Owner.Current);
        var unchanged = fixture.CaptureOtherPartitions();
        var observed = new ScopedAtomicStore(fixture);
        var scopedResolver = scoped ? new ObservedOperationResolver(fixture, s_source.Resolver) : null;
        var service = scopedResolver is null ? Service(fixture, observed)
            : new OwnerBoundCharacterCreationFinalizationService(observed, fixture.Owner,
                s_source.Queries, scopedResolver);
        var stamp = fixture.Owner.Capture();
        var command = Review(service, stamp, fixture.Id);
        Assert.AreEqual(0, observed.Commits);
        AssertRecordEquals(before, fixture.Read(stamp.Owner));

        var result = service.Confirm(stamp, command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, result.Outcome, Describe(result));
        Assert.IsNotNull(result.Value);
        Assert.IsEmpty(result.Blockers);
        Assert.AreEqual(1, observed.Commits);
        Assert.IsGreaterThanOrEqualTo(12, observed.Reads,
            "Finalization must evaluate its complete graph and reread its receipt through the scoped view.");
        Assert.AreEqual(linked ? 0 : observed.Reads, observed.LocalReads);
        var after = fixture.Read(stamp.Owner);
        Assert.AreEqual(before.ContentRevision + 1, after.ContentRevision);
        Assert.AreEqual(after.ContentRevision, after.SavedRevision);
        Assert.AreEqual(before.LocalHistory, after.LocalHistory);
        CollectionAssert.AreEqual(before.DelegatedGmHistorySegmentStarts.ToArray(),
            after.DelegatedGmHistorySegmentStarts.ToArray());
        Assert.IsTrue(s_source.Queries.ParseSummary(new(after.Document.Content)).Created);
        Assert.IsNotNull(after.Document.AuxiliaryState.CharacterCreationFinalizationArchive);
        AssertJsonEquals(before.Document.AuxiliaryState,
            after.Document.AuxiliaryState.CharacterCreationFinalizationArchive.State);
        Assert.HasCount(1, after.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!);
        Assert.AreEqual(result.Value, after.Document.AuxiliaryState.CharacterCreationFinalizationReceipts![0].Receipt);
        Assert.IsTrue(after.CanReplayReceipt(result.Value.ContentRevision));
        fixture.AssertOtherPartitionsUnchanged(unchanged);
        if (scopedResolver is not null)
        {
            Assert.HasCount(3, scopedResolver.Scopes, "Load, Review and Confirm must own distinct scopes.");
            Assert.IsTrue(scopedResolver.Scopes.All(scope => scope.Disposed));
            Assert.IsTrue(scopedResolver.Scopes.All(scope => scope.UniqueContexts.Count == 1));
            Assert.AreEqual(0, scopedResolver.UnscopedCalls);
            var scopedLookup = service.LookupReceipt(stamp, new(fixture.Id, command.IdempotencyKey));
            Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, scopedLookup.Outcome, Describe(scopedLookup));
            Assert.AreEqual(result.Value, scopedLookup.Value);
            Assert.HasCount(4, scopedResolver.Scopes);
            var lookupScope = scopedResolver.Scopes[3];
            Assert.IsTrue(lookupScope.Disposed);
            Assert.AreEqual(0, lookupScope.Calls);
            Assert.IsEmpty(lookupScope.UniqueContexts, "Receipt lookup must not reconstruct source authority.");
        }

        var committedBytes = fixture.CapturePartition(stamp.Owner);
        var coldService = Service(fixture, new ScopedAtomicStore(fixture));
        var replay = coldService.Confirm(stamp, command);
        var lookup = coldService.LookupReceipt(stamp, new(fixture.Id, command.IdempotencyKey));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, replay.Outcome, Describe(replay));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, lookup.Outcome, Describe(lookup));
        Assert.AreEqual(result.Value, replay.Value);
        Assert.AreEqual(result.Value, lookup.Value);
        Assert.AreEqual(committedBytes, fixture.CapturePartition(stamp.Owner));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Conflict,
            coldService.Confirm(stamp, command with { PlanDigest = "different-plan" }).Outcome);
        Assert.AreEqual(committedBytes, fixture.CapturePartition(stamp.Owner));
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Scoped_actual_full_finalization_matches_unscoped_all_steps_and_digests(bool linked)
    {
        using var fixture = new Fixture(linked ? AccountA : OwnerScope.LocalSingleUser);
        var before = fixture.CaptureAllPartitions();
        var stamp = fixture.Owner.Capture();
        var baselineStore = new ScopedAtomicStore(fixture);
        var baselineResolver = new ObservedResolver(fixture, s_source.Resolver);
        var baseline = new OwnerBoundCharacterCreationFinalizationService(baselineStore,
            fixture.Owner, s_source.Queries, baselineResolver);
        var baselineClock = System.Diagnostics.Stopwatch.StartNew();
        long baselineAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        var expected = baseline.Load(stamp, new(fixture.Id));
        long baselineAllocated = GC.GetAllocatedBytesForCurrentThread() - baselineAllocationStart;
        baselineClock.Stop();
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Available, expected.Outcome, Describe(expected));
        Assert.IsNotNull(expected.Value);
        Assert.HasCount(7, expected.Value.Steps);
        Assert.IsTrue(expected.Value.Steps.All(step => step.IsComplete));
        int baselineLoadCalls = baselineResolver.Calls;
        int baselineLoadReads = baselineStore.Reads;

        var scopedStore = new ScopedAtomicStore(fixture);
        var resolver = new ObservedOperationResolver(fixture, s_source.Resolver);
        var service = new OwnerBoundCharacterCreationFinalizationService(scopedStore,
            fixture.Owner, s_source.Queries, resolver);
        var scopedClock = System.Diagnostics.Stopwatch.StartNew();
        long scopedAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        var actual = service.Load(stamp, new(fixture.Id));
        long scopedAllocated = GC.GetAllocatedBytesForCurrentThread() - scopedAllocationStart;
        scopedClock.Stop();
        int scopedLoadReads = scopedStore.Reads;
        AssertJsonEquals(expected, actual); // Includes every step, binding and snapshot digest.
        Assert.AreEqual(baselineLoadReads, scopedStore.Reads, "No domain/workspace validation may disappear.");
        // Compare against the full graph with read reuse disabled, not just
        // another adapter which may enable the same optimization.
        Assert.IsTrue(fixture.Owner.TryAcquire(stamp, out var freshLease));
        using (freshLease)
        using (var freshSources = ((ICharacterSourceDataResolverOperationScopeFactory)s_source.Resolver).CreateOperationScope())
        {
            var freshStore = new ScopedAtomicStore(fixture);
            var freshView = new OwnerBoundCreationWorkspaceStore(freshStore, freshLease!, stamp, fixture.Id);
            var prerequisites = new CharacterCreationPrerequisiteService(freshView, s_source.Queries, freshSources);
            var attributes = new CharacterCreationAttributesService(freshView, freshSources);
            var reference = new CharacterCreationFinalizationService(freshView, s_source.Queries,
                prerequisites, attributes,
                new CharacterCreationSkillsService(freshView, freshSources),
                new CharacterCreationQualitiesService(freshView, freshSources, prerequisites, attributes),
                new CharacterCreationMagicResonanceService(freshView, freshSources),
                new CharacterCreationResourcesService(freshView, freshSources),
                new CharacterCreationGearService(freshView, freshSources), freshSources).Load(new(fixture.Id));
            AssertJsonEquals(reference, actual);
            Assert.IsGreaterThan(1, freshStore.Reads, "The reference must exercise repeated validated store reads.");
            Console.WriteLine($"finalization-read-observation referenceReads={freshStore.Reads} loadReads={scopedLoadReads}");
        }
        Assert.AreEqual(1, scopedLoadReads,
            "All seven read-only evaluators must share one complete validated workspace observation.");
        var loadScope = resolver.Scopes.Single();
        Assert.AreEqual(baselineLoadCalls, loadScope.Calls);
        Assert.IsTrue(baselineLoadCalls > 1, "The baseline must actually exercise repeated context construction.");
        Assert.HasCount(1, loadScope.UniqueContexts);
        Assert.IsTrue(loadScope.Disposed);

        var expectedReview = baseline.Review(stamp, new(expected.Value.Binding));
        var actualReview = service.Review(stamp, new(actual.Value!.Binding));
        AssertJsonEquals(expectedReview, actualReview); // Includes complete plan and preview digest.
        Assert.IsGreaterThan(1, scopedStore.Reads - scopedLoadReads,
            "Review must still read current storage independently for its nested evaluators.");
        Assert.HasCount(2, resolver.Scopes);
        var reviewScope = resolver.Scopes[1];
        Assert.HasCount(1, reviewScope.UniqueContexts);
        Assert.AreNotSame(loadScope.UniqueContexts[0], reviewScope.UniqueContexts[0]);
        Assert.IsTrue(reviewScope.Disposed);
        Assert.AreEqual(baselineStore.Reads, scopedStore.Reads);
        Assert.AreEqual(0, scopedStore.Commits);
        Assert.AreEqual(0, resolver.UnscopedCalls);
        fixture.AssertPartitionsUnchanged(before);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
        Console.WriteLine($"finalization-operation-scope domains=7 requests={loadScope.Calls} "
            + $"baselineContexts={baselineLoadCalls} scopedContexts={loadScope.UniqueContexts.Count} "
            + $"baselineLoadReads={baselineLoadReads} scopedLoadReads={scopedLoadReads} "
            + $"baselineLoadMs={baselineClock.Elapsed.TotalMilliseconds:F3} "
            + $"scopedLoadMs={scopedClock.Elapsed.TotalMilliseconds:F3} "
            + $"baselineLoadAllocatedBytes={baselineAllocated} scopedLoadAllocatedBytes={scopedAllocated}; "
            + "managed diagnostic only");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Finalization_load_observation_cannot_survive_the_public_call(bool linked)
    {
        using var fixture = new Fixture(linked ? AccountA : OwnerScope.LocalSingleUser);
        var before = fixture.CaptureAllPartitions();
        var stamp = fixture.Owner.Capture();
        var store = new ScopedAtomicStore(fixture);
        var service = Service(fixture, store);
        var loaded = service.Load(stamp, new(fixture.Id));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Available, loaded.Outcome, Describe(loaded));
        Assert.IsNotNull(loaded.Value);
        Assert.AreEqual(1, store.Reads);

        // Losing storage after a successful read may not return cached readiness
        // on another Load or admit a Review against the previous binding.
        store.ReadUnavailable = true;
        var missingLoad = service.Load(stamp, new(fixture.Id));
        var missingReview = service.Review(stamp, new(loaded.Value.Binding));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.NotFound, missingLoad.Outcome);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.NotFound, missingReview.Outcome);
        Assert.IsNull(missingLoad.Value);
        Assert.IsNull(missingReview.Value);
        CollectionAssert.Contains(missingLoad.Blockers.ToArray(), CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        CollectionAssert.Contains(missingReview.Blockers.ToArray(), CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        Assert.AreEqual(3, store.Reads);
        store.ReadUnavailable = false;
        AssertJsonEquals(loaded, service.Load(stamp, new(fixture.Id)));
        Assert.AreEqual(4, store.Reads);
        Assert.AreEqual(0, store.Commits);
        fixture.AssertPartitionsUnchanged(before);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
    }

    [TestMethod]
    [DataRow("owner-B")]
    [DataRow("owner-ABA")]
    [DataRow("foreign-issuer")]
    public void Operation_scope_is_not_created_before_original_owner_admission(string denial)
    {
        using var fixture = new Fixture(AccountA);
        var original = fixture.Owner.Capture();
        if (denial == "foreign-issuer") original = new TestOwner(AccountA).Capture();
        else
        {
            fixture.Owner.Transition(AccountB);
            if (denial == "owner-ABA") fixture.Owner.Transition(AccountA);
        }
        var before = fixture.CaptureAllPartitions();
        var store = new ScopedAtomicStore(fixture);
        var resolver = new ObservedOperationResolver(fixture, s_source.Resolver);
        var service = new OwnerBoundCharacterCreationFinalizationService(store, fixture.Owner,
            s_source.Queries, resolver);
        AssertUnavailable(service.Load(original, new(fixture.Id)));
        Assert.IsEmpty(resolver.Scopes);
        Assert.AreEqual(0, resolver.UnscopedCalls);
        Assert.AreEqual(0, store.Reads);
        Assert.AreEqual(0, store.Commits);
        fixture.AssertPartitionsUnchanged(before);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Operation_scope_and_owner_lease_are_disposed_on_injected_failure(bool duringCreation)
    {
        using var fixture = new Fixture(AccountA);
        var before = fixture.CaptureAllPartitions();
        var store = new ScopedAtomicStore(fixture);
        var resolver = new ObservedOperationResolver(fixture, s_source.Resolver)
        {
            FailCreate = duringCreation,
            FailQuery = !duringCreation
        };
        var service = new OwnerBoundCharacterCreationFinalizationService(store, fixture.Owner,
            s_source.Queries, resolver);
        Assert.ThrowsExactly<OperationSourceTestException>(() => service.Load(fixture.Owner.Capture(), new(fixture.Id)));
        Assert.AreEqual(1, resolver.InjectedFailures, "The intended scope boundary must have been reached.");
        Assert.HasCount(duringCreation ? 0 : 1, resolver.Scopes);
        Assert.IsTrue(resolver.Scopes.All(scope => scope.Disposed));
        Assert.AreEqual(0, store.Commits);
        fixture.AssertPartitionsUnchanged(before);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
    }

    [TestMethod]
    [DataRow("foreign-issuer")]
    [DataRow("switched-owner")]
    [DataRow("owner-ABA")]
    [DataRow("unknown")]
    public void All_four_methods_reject_invalid_original_admission_before_any_store_read(string denial)
    {
        using var fixture = new Fixture(AccountA);
        var observed = new ScopedAtomicStore(fixture);
        var service = Service(fixture, observed);
        var original = fixture.Owner.Capture();
        var command = Review(service, original, fixture.Id);
        OwnerContextStamp rejected = original;
        if (denial == "foreign-issuer") rejected = new TestOwner(AccountA).Capture();
        else if (denial == "switched-owner") fixture.Owner.Transition(AccountB);
        else if (denial == "owner-ABA")
        {
            fixture.Owner.Transition(AccountB);
            fixture.Owner.Transition(AccountA);
        }
        else rejected = default;
        int reads = observed.Reads;
        var before = fixture.CaptureAllPartitions();
        AssertUnavailable(service.Load(rejected, new(fixture.Id)));
        AssertUnavailable(service.Review(rejected, new(command.Binding)));
        AssertUnavailable(service.Confirm(rejected, command));
        AssertUnavailable(service.LookupReceipt(rejected, new(fixture.Id, command.IdempotencyKey)));
        Assert.AreEqual(reads, observed.Reads);
        Assert.AreEqual(0, observed.Commits);
        fixture.AssertPartitionsUnchanged(before);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
    }

    [TestMethod]
    public void Missing_lease_capability_and_spoofed_local_identity_never_fall_back_to_local_storage()
    {
        using var fixture = new Fixture(AccountA);
        var observed = new ScopedAtomicStore(fixture);
        var command = Review(Service(fixture, observed), fixture.Owner.Capture(), fixture.Id);
        int reads = observed.Reads;
        var currentOnly = new OwnerBoundCharacterCreationFinalizationService(observed,
            new CurrentOnlyOwner(AccountA), s_source.Queries, s_source.Resolver);
        AssertUnavailable(currentOnly.Load(fixture.Owner.Capture(), new(fixture.Id)));
        AssertUnavailable(currentOnly.Confirm(fixture.Owner.Capture(), command));

        var spoof = new TestOwner(new OwnerScope(OwnerScope.LocalSingleUser.Value));
        var spoofed = new OwnerBoundCharacterCreationFinalizationService(observed,
            spoof, s_source.Queries, s_source.Resolver);
        AssertUnavailable(spoofed.Load(spoof.Capture(), new(fixture.Id)));
        AssertUnavailable(spoofed.Confirm(spoof.Capture(), command));
        Assert.AreEqual(reads, observed.Reads);
        Assert.AreEqual(0, observed.Commits);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Missing_scoped_atomic_capability_cannot_be_replaced_by_a_local_only_capability(bool localOnly)
    {
        using var fixture = new Fixture(AccountA);
        var command = Review(Service(fixture, new ScopedAtomicStore(fixture)), fixture.Owner.Capture(), fixture.Id);
        ObservedStore store = localOnly ? new LocalAtomicStore(fixture) : new ObservedStore(fixture);
        var before = fixture.CaptureAllPartitions();
        var result = Service(fixture, store).Confirm(fixture.Owner.Capture(), command);
        Assert.IsNull(result.Value);
        Assert.AreNotEqual(CharacterCreationFinalizationOutcomes.Applied, result.Outcome);
        Assert.AreNotEqual(CharacterCreationFinalizationOutcomes.Replayed, result.Outcome);
        Assert.IsNotEmpty(result.Blockers);
        Assert.AreEqual(0, store.LocalReads);
        fixture.AssertPartitionsUnchanged(before);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
    }

    [TestMethod]
    public void Missing_workspace_returns_owner_confined_NotFound_without_local_fallback()
    {
        using var fixture = new Fixture(AccountA);
        var observed = new ScopedAtomicStore(fixture);
        var service = Service(fixture, observed);
        var result = service.Load(fixture.Owner.Capture(), new(new("absent-workspace")));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.NotFound, result.Outcome);
        Assert.IsNull(result.Value);
        Assert.AreEqual(1, observed.Reads);
        Assert.AreEqual(0, observed.LocalReads);
        Assert.AreEqual(0, observed.Commits);
    }

    [TestMethod]
    public void Known_commit_keeps_receipt_if_reopen_is_unavailable_and_lookup_remains_owner_bound()
    {
        using var fixture = new Fixture(AccountA);
        var observed = new ScopedAtomicStore(fixture) { FailReadsAfterCommit = true };
        var service = Service(fixture, observed);
        var original = fixture.Owner.Capture();
        var command = Review(service, original, fixture.Id);
        var result = service.Confirm(original, command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, result.Outcome, Describe(result));
        Assert.IsNotNull(result.Value);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationFinalizationBlockers.PostCommitReopenRequired);
        Assert.AreEqual(1, observed.Commits);
        var before = fixture.CaptureAllPartitions();
        fixture.Owner.Transition(AccountB);
        int reads = observed.Reads;
        AssertUnavailable(service.LookupReceipt(original, new(fixture.Id, command.IdempotencyKey)));
        Assert.AreEqual(reads, observed.Reads);
        fixture.AssertPartitionsUnchanged(before);
        fixture.Owner.Transition(AccountA);
        var current = fixture.Owner.Capture();
        Assert.AreNotEqual(original, current);
        var recovered = Service(fixture, new ScopedAtomicStore(fixture))
            .LookupReceipt(current, new(fixture.Id, command.IdempotencyKey));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, recovered.Outcome, Describe(recovered));
        Assert.AreEqual(result.Value, recovered.Value);
        fixture.AssertPartitionsUnchanged(before);
    }

    [TestMethod]
    public void Commit_that_reports_unavailable_recovers_only_its_scoped_locally_committed_receipt()
    {
        using var fixture = new Fixture(AccountA);
        var observed = new ScopedAtomicStore(fixture) { ReportUnavailableAfterCommit = true };
        var service = Service(fixture, observed);
        var stamp = fixture.Owner.Capture();
        var command = Review(service, stamp, fixture.Id);
        var result = service.Confirm(stamp, command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, result.Outcome, Describe(result));
        Assert.IsNotNull(result.Value);
        Assert.AreEqual(1, observed.Commits);
        Assert.AreEqual(result.Value, fixture.Read(AccountA).Document.AuxiliaryState
            .CharacterCreationFinalizationReceipts!.Single().Receipt);
        Assert.AreEqual(0, observed.LocalReads);
    }

    [TestMethod]
    public void Actual_same_owner_continuation_restore_does_not_promote_imported_finalization_receipts()
    {
        using var fixture = new Fixture(AccountA);
        var service = Service(fixture, new ScopedAtomicStore(fixture));
        var stamp = fixture.Owner.Capture();
        var command = Review(service, stamp, fixture.Id);
        var committed = service.Confirm(stamp, command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, committed.Outcome, Describe(committed));
        Assert.IsNotNull(committed.Value);
        var exported = new WorkspaceContinuationExportService(fixture.Store, fixture.Owner).Export(stamp, fixture.Id);
        Assert.IsTrue(exported.Success, exported.Error);
        Assert.IsNotNull(exported.Value);
        Assert.AreEqual(AccountA.NormalizedValue, exported.Value.Snapshot.OwnerId);

        string targetDirectory = Directory.CreateTempSubdirectory("chummer-finalization-import-").FullName;
        try
        {
            var target = new FileWorkspaceStore(targetDirectory);
            const int maximumBytes = 16 * 1024 * 1024;
            var restore = new WorkspaceContinuationRestoreService(target, fixture.Owner,
                s_source.Resolver, s_source.Queries, Catalog(), maximumBytes);
            using var review = restore.Review(stamp, WorkspaceContinuationCodec.Encode(exported.Value, maximumBytes));
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Available, review.Result.Outcome,
                JsonSerializer.Serialize(review.Result));
            var restored = restore.Confirm(review, explicitlyConfirmed: true);
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, restored.Outcome, JsonSerializer.Serialize(restored));
            var coldStore = new FileWorkspaceStore(targetDirectory);
            var current = coldStore.Get(AccountA, fixture.Id).Value!;
            Assert.IsNotNull(current.LocalHistory);
            Assert.AreEqual(current.ContentRevision, current.LocalHistory.ImportedThroughRevision);
            Assert.AreEqual(exported.Value.SnapshotDigest, current.LocalHistory.ImportedSnapshotDigest);
            Assert.IsFalse(current.CanReplayReceipt(committed.Value.ContentRevision));
            AssertJsonEquals(fixture.Read(AccountA).Document, current.Document);
            CollectionAssert.AreEqual(fixture.Read(AccountA).DelegatedGmHistorySegmentStarts.ToArray(),
                current.DelegatedGmHistorySegmentStarts.ToArray());
            var importedService = new OwnerBoundCharacterCreationFinalizationService(coldStore, fixture.Owner,
                s_source.Queries, s_source.Resolver);
            var before = JsonSerializer.Serialize(current);
            var replay = importedService.Confirm(stamp, command);
            var lookup = importedService.LookupReceipt(stamp, new(fixture.Id, command.IdempotencyKey));
            Assert.AreEqual(CharacterCreationFinalizationOutcomes.Conflict, replay.Outcome, Describe(replay));
            Assert.AreEqual(CharacterCreationFinalizationOutcomes.Conflict, lookup.Outcome, Describe(lookup));
            Assert.IsNull(replay.Value);
            Assert.IsNull(lookup.Value);
            Assert.AreEqual(before, JsonSerializer.Serialize(new FileWorkspaceStore(targetDirectory).Get(AccountA, fixture.Id).Value));
        }
        finally { Directory.Delete(targetDirectory, recursive: true); }
    }

    private static OwnerBoundCharacterCreationFinalizationService Service(Fixture fixture, ObservedStore store)
        => new(store, fixture.Owner, s_source.Queries, new ObservedResolver(fixture, s_source.Resolver));

    private static CharacterCreationFinalizationConfirmRequest Review(
        IOwnerBoundCharacterCreationFinalizationService service, OwnerContextStamp owner, CharacterWorkspaceId id)
    {
        var loaded = service.Load(owner, new(id));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Available, loaded.Outcome, Describe(loaded));
        Assert.IsNotNull(loaded.Value);
        Assert.HasCount(7, loaded.Value.Steps);
        Assert.IsTrue(loaded.Value.Steps.All(step => step.IsComplete), Describe(loaded));
        var source = loaded.Value.StartingCashSource!;
        var choice = new CharacterCreationStartingCashChoice(source.AuthorityDigest, source.Dice);
        var reviewed = service.Review(owner, new(loaded.Value.Binding) { StartingCash = choice });
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Available, reviewed.Outcome, Describe(reviewed));
        Assert.IsNotNull(reviewed.Value);
        Assert.IsNotNull(reviewed.Value.Plan);
        Assert.IsTrue(reviewed.Value.CanConfirm, Describe(reviewed));
        return new(loaded.Value.Binding, reviewed.Value.PreviewDigest, reviewed.Value.Plan.PlanDigest,
            "owner-bound-finalization", ExplicitlyConfirmed: true) { StartingCash = choice };
    }

    private static void AssertUnavailable<T>(CharacterCreationFinalizationResult<T> result) where T : class
    {
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Unavailable, result.Outcome, Describe(result));
        Assert.IsNull(result.Value);
    }

    private static string Describe<T>(CharacterCreationFinalizationResult<T> result) where T : class
        => JsonSerializer.Serialize(result);
    private static void AssertJsonEquals<T>(T expected, T actual)
        => Assert.IsTrue(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expected),
            JsonSerializer.SerializeToElement(actual)));
    private static void AssertRecordEquals(WorkspaceStoredDocument expected, WorkspaceStoredDocument actual)
        => AssertJsonEquals(expected, actual);

    private static XmlLifeModulesCatalogService Catalog()
    {
        for (DirectoryInfo? path = new(AppContext.BaseDirectory); path is not null; path = path.Parent)
        {
            string candidate = Path.Combine(path.FullName, "Chummer", "data", "lifemodules.xml");
            if (File.Exists(candidate)) return new(candidate);
        }
        throw new DirectoryNotFoundException("Canonical Core Life Modules catalog was not staged.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Dictionary<OwnerScope, string> _paths = [];
        public string Root { get; } = Directory.CreateTempSubdirectory("chummer-finalization-owner-").FullName;
        public FileWorkspaceStore Store { get; }
        public TestOwner Owner { get; }
        public CharacterWorkspaceId Id => s_source.WorkspaceId;

        public Fixture(OwnerScope owner)
        {
            Store = new(Root);
            Owner = new(owner);
            foreach (var partition in new[] { OwnerScope.LocalSingleUser, AccountA, AccountB })
            {
                string[] oldPaths = Directory.GetFiles(Root, Id.Value + ".json", SearchOption.AllDirectories);
                var placeholder = new WorkspaceDocument(
                    "<character><name>Unready shadow</name><metatype>Human</metatype><created>False</created></character>", "sr5");
                var created = partition.IsLocalSingleUser
                    ? Store.CreateWorkspaceDocument(Id, placeholder)
                    : Store.CreateWorkspaceDocument(partition, Id, placeholder);
                Assert.IsTrue(created.Success, created.Error);
                string recordPath = Directory.GetFiles(Root, Id.Value + ".json", SearchOption.AllDirectories)
                    .Except(oldPaths, StringComparer.Ordinal).Single();
                _paths.Add(partition, recordPath);
                if (partition != owner) continue;

                // TEST FIXTURE STORAGE, not authenticated restore or owner migration.
                // The destination was created through its real scoped FileStore API.
                // Install exact bytes from a genuinely committed ReadyContext; never
                // relabel a portable owner/digest or invent rule-valid draft objects.
                string sourcePath = Path.Combine(s_source.Directory, "workspaces", Id.Value + ".json");
                File.Copy(sourcePath, recordPath, overwrite: true);
                File.SetLastWriteTimeUtc(recordPath, File.GetLastWriteTimeUtc(sourcePath));
                CollectionAssert.AreEqual(File.ReadAllBytes(sourcePath), File.ReadAllBytes(recordPath));
                AssertRecordEquals(s_source.Store.Get(Id).Value!, Read(partition));
            }
            Assert.IsNull(Read(owner == AccountA ? AccountB : AccountA)
                .Document.AuxiliaryState.CharacterCreationPrerequisiteDraft);
        }

        public WorkspaceStoredDocument Read(OwnerScope owner)
        {
            var cold = new FileWorkspaceStore(Root);
            var result = owner.IsLocalSingleUser ? cold.Get(Id) : cold.Get(owner, Id);
            Assert.IsTrue(result.Success, result.Error);
            Assert.IsNotNull(result.Value);
            return result.Value;
        }

        public (string Bytes, DateTime Timestamp) CapturePartition(OwnerScope owner)
            => (Convert.ToBase64String(File.ReadAllBytes(_paths[owner])), File.GetLastWriteTimeUtc(_paths[owner]));
        public Dictionary<OwnerScope, (string Bytes, DateTime Timestamp)> CaptureAllPartitions()
            => _paths.Keys.ToDictionary(owner => owner, CapturePartition);
        public Dictionary<OwnerScope, (string Bytes, DateTime Timestamp)> CaptureOtherPartitions()
            => _paths.Keys.Where(owner => owner != Owner.Current).ToDictionary(owner => owner, CapturePartition);
        public void AssertOtherPartitionsUnchanged(Dictionary<OwnerScope, (string Bytes, DateTime Timestamp)> before)
            => AssertPartitionsUnchanged(before);
        public void AssertPartitionsUnchanged(Dictionary<OwnerScope, (string Bytes, DateTime Timestamp)> before)
        {
            foreach (var entry in before) Assert.AreEqual(entry.Value, CapturePartition(entry.Key), entry.Key.Value);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class ObservedResolver(Fixture fixture, ICharacterSourceDataResolver inner) : ICharacterSourceDataResolver
    {
        public int Calls { get; private set; }
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            Calls++;
            fixture.Owner.AssertLeaseHeld();
            Assert.AreEqual(s_source.Store.Get(s_source.WorkspaceId).Value!.Document.Content, characterXml);
            return inner.TryCreateContext(characterXml);
        }
    }

    private sealed class OperationSourceTestException : Exception { }

    private sealed class ObservedOperationResolver(Fixture fixture, ICharacterSourceDataResolver inner)
        : ICharacterSourceDataResolver, ICharacterSourceDataResolverOperationScopeFactory
    {
        public List<ObservedOperationScope> Scopes { get; } = [];
        public int UnscopedCalls { get; private set; }
        public bool FailCreate { get; init; }
        public bool FailQuery { get; init; }
        public bool DenyRepeatedContext { get; init; }
        public int InjectedFailures { get; private set; }
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            UnscopedCalls++;
            throw new AssertFailedException("A capable resolver must use its operation scope.");
        }
        public ICharacterSourceDataResolverOperationScope CreateOperationScope()
        {
            fixture.Owner.AssertLeaseHeld();
            if (FailCreate)
            {
                InjectedFailures++;
                throw new OperationSourceTestException();
            }
            Assert.IsInstanceOfType<ICharacterSourceDataResolverOperationScopeFactory>(inner);
            var scope = new ObservedOperationScope(fixture,
                ((ICharacterSourceDataResolverOperationScopeFactory)inner).CreateOperationScope(), () =>
                {
                    if (!FailQuery) return;
                    InjectedFailures++;
                    throw new OperationSourceTestException();
                }, DenyRepeatedContext);
            Scopes.Add(scope);
            return scope;
        }
    }

    private sealed class ObservedOperationScope(Fixture fixture,
        ICharacterSourceDataResolverOperationScope inner, Action beforeQuery, bool denyRepeatedContext = false)
        : ICharacterSourceDataResolverOperationScope
    {
        public int Calls { get; private set; }
        public List<ICharacterSourceDataContext> UniqueContexts { get; } = [];
        public bool Disposed { get; private set; }
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            fixture.Owner.AssertLeaseHeld();
            Assert.IsFalse(Disposed);
            Calls++;
            // Match the baseline observer's real store read and exact-XML
            // assertion, so elapsed/allocation diagnostics do not compare
            // different fixture instrumentation costs.
            Assert.AreEqual(s_source.Store.Get(s_source.WorkspaceId).Value!.Document.Content, characterXml);
            beforeQuery();
            if (denyRepeatedContext && Calls > 1) return null;
            var context = inner.TryCreateContext(characterXml);
            if (context is not null && !UniqueContexts.Any(item => ReferenceEquals(item, context)))
                UniqueContexts.Add(context);
            return context;
        }
        public void Dispose()
        {
            fixture.Owner.AssertLeaseHeld();
            Assert.IsFalse(Disposed, "The operation must be closed once, before its lease.");
            inner.Dispose();
            Disposed = true;
        }
    }

    private class ObservedStore(Fixture fixture) : IWorkspaceStore
    {
        protected Fixture Fixture { get; } = fixture;
        public int Reads { get; private set; }
        public int LocalReads { get; private set; }
        public bool ReadUnavailable { get; set; }
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
        {
            LocalReads++;
            return Read(OwnerScope.LocalSingleUser, id);
        }
        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id) => Read(owner, id);
        private WorkspaceStoreReadResult Read(OwnerScope owner, CharacterWorkspaceId id)
        {
            Fixture.Owner.AssertLeaseHeld();
            Assert.AreEqual(Fixture.Owner.Current, owner, "Every nested evaluator must use the admitted partition.");
            Reads++;
            return ReadUnavailable ? new(WorkspaceOperationOutcome.Unavailable)
                : owner.IsLocalSingleUser ? new FileWorkspaceStore(Fixture.Root).Get(id)
                : new FileWorkspaceStore(Fixture.Root).Get(owner, id);
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

    private class LocalAtomicStore(Fixture fixture) : ObservedStore(fixture), IWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        public bool SupportsWorkspaceAuxiliaryStateAtomicCommit => true;
        public int Commits { get; protected set; }
        public bool FailReadsAfterCommit { get; init; }
        public bool ReportUnavailableAfterCommit { get; init; }
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            CharacterWorkspaceId id, long revision, string digest, WorkspaceDocument document)
            => Commit(OwnerScope.LocalSingleUser, id, revision, digest, document);
        protected WorkspaceStoreMutationResult Commit(OwnerScope owner, CharacterWorkspaceId id,
            long revision, string digest, WorkspaceDocument document)
        {
            Fixture.Owner.AssertLeaseHeld();
            Assert.AreEqual(Fixture.Owner.Current, owner);
            var result = owner.IsLocalSingleUser
                ? Fixture.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(id, revision, digest, document)
                : Fixture.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(owner, id, revision, digest, document);
            if (result.Success)
            {
                Commits++;
                ReadUnavailable = FailReadsAfterCommit;
                if (ReportUnavailableAfterCommit) return new(WorkspaceOperationOutcome.Unavailable);
            }
            return result;
        }
    }

    private sealed class ScopedAtomicStore(Fixture fixture) : LocalAtomicStore(fixture), IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        public bool SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit => true;
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            OwnerScope owner, CharacterWorkspaceId id, long revision, string digest, WorkspaceDocument document)
            => Commit(owner, id, revision, digest, document);
    }

    private sealed class CurrentOnlyOwner(OwnerScope owner) : IOwnerContextAccessor
    {
        public OwnerScope Current => owner;
    }

    private sealed class TestOwner(OwnerScope initial) : IOwnerContextLeaseAccessor
    {
        private readonly object _gate = new();
        private readonly string _issuer = Guid.NewGuid().ToString("N");
        private OwnerScope _owner = initial;
        private long _revision;
        public int ActiveLeases { get; private set; }
        public OwnerScope Current { get { lock (_gate) return _owner; } }
        public OwnerContextStamp Capture() { lock (_gate) return new(_owner, _issuer, _revision); }
        public void AssertLeaseHeld()
        {
            Assert.IsTrue(Monitor.IsEntered(_gate), "Owner admission must stay on the calling thread.");
            Assert.AreEqual(1, ActiveLeases, "Admission must cover source reads, atomic commit and receipt lookup.");
        }
        public void Transition(OwnerScope owner)
        {
            lock (_gate) { Assert.AreEqual(0, ActiveLeases); _owner = owner; _revision++; }
        }
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            Monitor.Enter(_gate);
            Assert.AreEqual(0, ActiveLeases, "The finalizer must not reacquire a nested lease.");
            if (!expected.IsValid || expected != new OwnerContextStamp(_owner, _issuer, _revision))
            {
                Monitor.Exit(_gate);
                lease = null;
                return false;
            }
            ActiveLeases++;
            lease = new Lease(this, expected);
            return true;
        }
        private sealed class Lease(TestOwner owner, OwnerContextStamp stamp) : IOwnerContextLease
        {
            private bool _disposed;
            public OwnerContextStamp Stamp => !_disposed ? stamp : throw new ObjectDisposedException(nameof(Lease));
            public void Dispose()
            {
                if (_disposed) return;
                owner.AssertLeaseHeld();
                _disposed = true;
                owner.ActiveLeases--;
                Monitor.Exit(owner._gate);
            }
        }
    }
}
