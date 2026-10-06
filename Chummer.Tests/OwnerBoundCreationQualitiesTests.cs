using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.Text.Json;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class OwnerBoundCreationQualitiesTests
{
    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority, false)]
    [DataRow(CharacterCreationBuildMethods.Priority, true)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, false)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, true)]
    public void Owner_only_quality_draft_saves_reopens_and_rejects_foreign_or_old_owners(string method, bool local)
    {
        using var fixture = ReadyContext.CreateUnprepared(method);
        string directory = Path.Combine(fixture.Directory, "quality-owner-only");
        var store = new FileWorkspaceStore(directory);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var ownerA = local ? OwnerScope.LocalSingleUser : new OwnerScope("qualities-owner-a");
        var ownerB = new OwnerScope("qualities-owner-b");
        owners.Set(ownerA);
        OwnerContextStamp original = owners.Capture();
        var codec = new Sr5WorkspaceCodec(fixture.Queries,
            new XmlCharacterSectionQueries(new CharacterSectionService(fixture.Resolver)),
            new XmlCharacterMetadataCommands(new CharacterFileService()));
        var bootstrap = new OwnerBoundCharacterCreationBootstrapService(
            new CharacterCreationBootstrapService(store, new RulesetWorkspaceCodecResolver([codec]),
                fixture.Queries, fixture.Resolver), owners);
        Assert.IsTrue(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(method, out var settings));
        var created = bootstrap.Create(original, new(CharacterCreationBootstrapSchemas.RequestV1,
            CharacterCreationBootstrapStages.AwaitingFoundationSelection, RulesetDefaults.Sr5,
            "Quality owner test", "Quality", method, settings));
        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, created.Outcome, string.Join(",", created.Blockers));
        var id = created.Value!.WorkspaceId;
        WorkspaceStoreReadResult Read(FileWorkspaceStore target) => local ? target.Get(id) : target.Get(ownerA, id);
        var source = Read(store).Value!.Document;
        Assert.IsTrue(store.CreateWorkspaceDocument(ownerB, id, new WorkspaceDocument(source.Content, source.RulesetId)).Success);
        var prerequisites = new OwnerBoundCharacterCreationPrerequisiteService(
            store, owners, fixture.Queries, fixture.Resolver);
        var state = prerequisites.Load(original, new(id)).Value!;
        var ranks = CharacterCreationPrerequisiteServiceTests.Assign("A", "E", "B", "C", "D");
        var heritage = state.Authority.Options.Single(x => x.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
            && x.Rank == "A").HeritageOptions.First(x => x.IsEnabled && x.MetatypeName == "Human"
                && x.MetavariantSourceId is null);
        var talent = state.Authority.Options.Single(x => x.CategoryId == CharacterCreationPriorityCategoryIds.Talent
            && x.Rank == "E").TalentOptions.First(x => x.IsEnabled
                && x.Value.Equals("Mundane", StringComparison.OrdinalIgnoreCase));
        var p = prerequisites.Preview(original, new(state.Binding, ranks)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId }).Value!;
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
            prerequisites.Confirm(original, new(p.Binding, ranks, p.PreviewDigest, true)
                { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId }).Outcome);
        var attributes = new OwnerBoundCharacterCreationAttributesService(store, owners, fixture.Resolver);
        var a = attributes.Preview(original, new(attributes.Load(original, new(id)).Value!.Binding, [])).Value!;
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
            attributes.Confirm(original, new(a.Binding, [], a.PreviewDigest, true)).Outcome);
        var before = Read(store).Value!;
        var other = store.Get(ownerB, id).Value!;
        Assert.AreEqual(local, store.Get(id).Success, "A legacy copy must not mask the linked-owner regression.");
        var old = new CharacterCreationQualitiesService(store, fixture.Resolver,
            new CharacterCreationPrerequisiteService(store, fixture.Queries, fixture.Resolver),
            new CharacterCreationAttributesService(store, fixture.Resolver));
        if (!local) Assert.IsNull(old.Load(new(id)).Value);
        var baselineResolver = new ObservedResolver(owners, fixture.Resolver);
        var baseline = new OwnerBoundCharacterCreationQualitiesService(store, owners, fixture.Queries, baselineResolver);
        var clock = Stopwatch.StartNew();
        long allocationStart = GC.GetAllocatedBytesForCurrentThread();
        var expectedLoad = baseline.Load(original, new(id));
        long baselineAllocation = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        double baselineMs = clock.Elapsed.TotalMilliseconds;
        int loadCalls = baselineResolver.Calls;
        var resolver = new ObservedOperationResolver(owners, fixture.Resolver);
        var service = new OwnerBoundCharacterCreationQualitiesService(store, owners, fixture.Queries, resolver);
        clock.Restart();
        allocationStart = GC.GetAllocatedBytesForCurrentThread();
        var loaded = service.Load(original, new(id));
        long scopedAllocation = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        double scopedMs = clock.Elapsed.TotalMilliseconds;
        Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(expectedLoad), JsonSerializer.Serialize(loaded),
            "All source authority, bindings, catalog entries, costs and blockers must remain exact.");
        Assert.IsTrue(owners.TryAcquire(original, out var freshLease));
        using (freshLease)
        using (var freshSources = ((ICharacterSourceDataResolverOperationScopeFactory)fixture.Resolver).CreateOperationScope())
        {
            var freshView = new OwnerBoundCreationWorkspaceStore(store, freshLease!, original, id);
            var freshPrerequisites = new CharacterCreationPrerequisiteService(freshView, fixture.Queries, freshSources);
            var freshAttributes = new CharacterCreationAttributesService(freshView, freshSources);
            var freshLoad = new CharacterCreationQualitiesService(freshView, freshSources,
                freshPrerequisites, freshAttributes).Load(new(id));
            Assert.AreEqual(JsonSerializer.Serialize(freshLoad), JsonSerializer.Serialize(loaded),
                "One validated read must preserve the full canonical result of repeated fresh reads.");
        }
        Assert.IsTrue(loadCalls > 1, "The actual baseline must repeat source-context construction.");
        Assert.HasCount(1, resolver.Scopes);
        Assert.AreEqual(loadCalls, resolver.Scopes[0].Calls, "Keep every context admission.");
        Assert.HasCount(1, resolver.Scopes[0].Contexts);
        Assert.IsTrue(resolver.Scopes[0].Disposed);
        var expectedPreview = baseline.Preview(original, new(loaded.Value.Binding, []));
        int previewCalls = baselineResolver.Calls - loadCalls;
        var preview = service.Preview(original, new(loaded.Value.Binding, [])).Value!;
        Assert.AreEqual(JsonSerializer.Serialize(expectedPreview.Value), JsonSerializer.Serialize(preview));
        Assert.HasCount(2, resolver.Scopes);
        Assert.AreEqual(previewCalls, resolver.Scopes[1].Calls);
        Assert.HasCount(1, resolver.Scopes[1].Contexts);
        Assert.AreNotSame(resolver.Scopes[0].Contexts[0], resolver.Scopes[1].Contexts[0],
            "A later operation must not retain an earlier context.");
        Assert.IsTrue(resolver.Scopes[1].Disposed);
        Assert.AreEqual(JsonSerializer.Serialize(before), JsonSerializer.Serialize(Read(store).Value),
            "Load and Preview must leave the complete stored observation unchanged.");
        Console.WriteLine($"qualities-operation-scope method={method} local={local} requests={loadCalls} "
            + $"baselineContexts={loadCalls} scopedContexts=1 baselineMs={baselineMs:F3} scopedMs={scopedMs:F3} "
            + $"baselineAllocatedBytes={baselineAllocation} scopedAllocatedBytes={scopedAllocation}; managed diagnostic only");
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        var command = new CharacterCreationQualitiesConfirmRequest(preview.Binding, [], preview.PreviewDigest,
            "owner-qualities", Guid.NewGuid(), true);
        foreach (var denied in new[] { default(OwnerContextStamp), original with { Owner = ownerB },
                     original with { AuthorityInstanceId = "foreign" } })
        {
            Assert.IsNull(service.Load(denied, new(id)).Value);
            Assert.IsNull(service.Preview(denied, new(preview.Binding, [])).Value);
            Assert.IsNull(service.Confirm(denied, command).Value);
        }
        Assert.HasCount(2, resolver.Scopes, "Reject the original owner before creating any source scope.");
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
            service.Confirm(original, command with { ExplicitlyConfirmed = false }).Outcome);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
            service.Confirm(original, command with { PreviewDigest = new string('f', 64) }).Outcome);
        owners.Set(ownerB);
        owners.Set(ownerA);
        Assert.IsNull(service.Confirm(original, command).Value);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, Read(store).Value!.Document.AuxiliaryStateDigest);
        var fresh = owners.Capture();
        var saved = service.Confirm(fresh, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, saved.Outcome, string.Join(",", saved.Blockers));
        var cold = new FileWorkspaceStore(directory);
        var reopened = new OwnerBoundCharacterCreationQualitiesService(cold, owners, fixture.Queries, resolver);
        Assert.IsNotNull(reopened.Load(fresh, new(id)).Value!.PendingDraft);
        Assert.AreEqual(saved.Value!.ReceiptDigest, reopened.Confirm(fresh, command).Value!.ReceiptDigest);
        var after = Read(cold).Value!;
        Assert.AreEqual(before.ContentRevision + 1, after.ContentRevision);
        Assert.AreEqual(after.ContentRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryState.CharacterCreationAttributesDraft!.DraftDigest,
            after.Document.AuxiliaryState.CharacterCreationAttributesDraft!.DraftDigest);
        Assert.AreEqual(1, after.Document.AuxiliaryState.CharacterCreationQualitiesReceipts!.Count);
        Assert.AreEqual(other.Document.AuxiliaryStateDigest, cold.Get(ownerB, id).Value!.Document.AuxiliaryStateDigest);
        Assert.AreEqual(other.ContentRevision, cold.Get(ownerB, id).Value!.ContentRevision);
        Assert.AreEqual(local, cold.Get(id).Success);
        Assert.IsTrue(resolver.Scopes.All(scope => scope.Disposed));
        Assert.IsTrue(resolver.Scopes.Where(scope => scope.Calls != 0).All(scope => scope.Contexts.Count == 1),
            "Confirm/reopen/replay must use at most one context for the unchanged XML in each operation.");
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Qualities_source_scope_and_owner_lease_close_after_failure(bool duringCreation)
    {
        using var fixture = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var resolver = new ObservedOperationResolver(owners, fixture.Resolver)
        { FailCreate = duringCreation, FailQuery = !duringCreation };
        var service = new OwnerBoundCharacterCreationQualitiesService(fixture.Store, owners, fixture.Queries, resolver);
        string before = JsonSerializer.Serialize(fixture.Store.Get(fixture.WorkspaceId));
        Assert.ThrowsExactly<SourceScopeTestException>(() => service.Load(owners.Capture(), new(fixture.WorkspaceId)));
        Assert.HasCount(duringCreation ? 0 : 1, resolver.Scopes);
        Assert.IsTrue(resolver.Scopes.All(scope => scope.Disposed));
        Assert.AreEqual(0, owners.ActiveLeases);
        Assert.AreEqual(before, JsonSerializer.Serialize(fixture.Store.Get(fixture.WorkspaceId)));
    }

    private sealed class SourceScopeTestException : Exception { }

    private sealed class ObservedResolver(CharacterCreationAttributesServiceTests.AllocationOwners owners,
        ICharacterSourceDataResolver inner) : ICharacterSourceDataResolver
    {
        public int Calls { get; private set; }
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            Assert.AreEqual(1, owners.ActiveLeases);
            Calls++;
            return inner.TryCreateContext(characterXml);
        }
    }

    private sealed class ObservedOperationResolver(CharacterCreationAttributesServiceTests.AllocationOwners owners,
        ICharacterSourceDataResolver inner) : ICharacterSourceDataResolver, ICharacterSourceDataResolverOperationScopeFactory
    {
        public List<ObservedScope> Scopes { get; } = [];
        public bool FailCreate { get; init; }
        public bool FailQuery { get; init; }
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
            => throw new AssertFailedException("Use the capable resolver's operation scope.");
        public ICharacterSourceDataResolverOperationScope CreateOperationScope()
        {
            Assert.AreEqual(1, owners.ActiveLeases);
            if (FailCreate) throw new SourceScopeTestException();
            var scope = new ObservedScope(owners,
                ((ICharacterSourceDataResolverOperationScopeFactory)inner).CreateOperationScope(), FailQuery);
            Scopes.Add(scope);
            return scope;
        }
    }

    private sealed class ObservedScope(CharacterCreationAttributesServiceTests.AllocationOwners owners,
        ICharacterSourceDataResolverOperationScope inner, bool failQuery) : ICharacterSourceDataResolverOperationScope
    {
        public int Calls { get; private set; }
        public List<ICharacterSourceDataContext> Contexts { get; } = [];
        public bool Disposed { get; private set; }
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            Assert.AreEqual(1, owners.ActiveLeases);
            Assert.IsFalse(Disposed);
            Calls++;
            if (failQuery) throw new SourceScopeTestException();
            var context = inner.TryCreateContext(characterXml);
            if (context is not null && !Contexts.Any(item => ReferenceEquals(item, context))) Contexts.Add(context);
            return context;
        }
        public void Dispose()
        {
            Assert.AreEqual(1, owners.ActiveLeases, "Dispose source state before the owner lease.");
            Assert.IsFalse(Disposed);
            inner.Dispose();
            Disposed = true;
        }
    }
}
