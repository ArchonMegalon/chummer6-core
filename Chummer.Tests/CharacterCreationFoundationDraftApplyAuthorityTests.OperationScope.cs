using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Owners;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    public void Life_module_owner_operations_reuse_sources_only_within_the_admitted_call()
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = LifeCharacterProjectionFixture(directory, "mundane", withOrigin: true);
            var store = new FileWorkspaceStore(directory);
            var owner = new LocalOwnerContextAccessor();
            var resolver = new LifeOperationResolver(new FileSystemCharacterSourceDataResolver(CreateOverlays()));
            var service = new OwnerBoundCharacterCreationLifeModuleFinalizationService(store, owner,
                new XmlCharacterFileQueries(new CharacterFileService()), resolver, CreateCatalog());
            var stamp = owner.Capture();
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.Load(stamp, fixture.Request.Binding.WorkspaceId).Outcome);
            var preview = service.Preview(stamp, fixture.Request);
            Assert.AreEqual(fixture.Preview.PreviewDigest, preview.Value?.PreviewDigest);
            var command = LifeFinalizationCommand(fixture);
            var result = service.Confirm(stamp, command);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            Assert.AreEqual(fixture.Preview.FinalizationPlan!.ExpectedResultRawCharacterXmlDigest, result.Value!.RawCharacterXmlDigest);
            Assert.AreEqual(0, resolver.UnscopedCalls);
            Assert.HasCount(3, resolver.Scopes);
            Assert.IsTrue(resolver.Scopes.All(scope => scope.Disposed && scope.Contexts.Count == 1));
            Assert.IsTrue(resolver.Scopes[2].Calls > 1, "Both pre-write and post-flush evaluation must still resolve sources.");
            Assert.AreNotSame(resolver.Scopes[1].Contexts.Single(), resolver.Scopes[2].Contexts.Single());
            byte[] committed = File.ReadAllBytes(WorkspacePath(directory, fixture.Request.Binding.WorkspaceId));
            Assert.AreEqual(result.Value, service.Confirm(stamp, command).Value);
            Assert.HasCount(4, resolver.Scopes);
            Assert.IsTrue(resolver.Scopes[3].Disposed);
            Assert.AreEqual(0, resolver.Scopes[3].Calls, "Receipt replay is not a new rule evaluation.");
            CollectionAssert.AreEqual(committed, File.ReadAllBytes(WorkspacePath(directory, fixture.Request.Binding.WorkspaceId)));
            var reopened = new FileWorkspaceStore(directory).Get(fixture.Request.Binding.WorkspaceId).Value!;
            Assert.HasCount(1, reopened.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!);
            Assert.IsNotNull(reopened.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.State.LifeModuleDecisionAcceptances);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Life_module_source_scope_is_not_created_for_a_replaced_owner_lifetime()
    {
        string directory = CreateTempDirectory();
        try
        {
            var owner = new LocalOwnerContextAccessor();
            var replaced = new LocalOwnerContextAccessor();
            var resolver = new LifeOperationResolver(new FileSystemCharacterSourceDataResolver(CreateOverlays()));
            var service = new OwnerBoundCharacterCreationLifeModuleFinalizationService(new FileWorkspaceStore(directory), owner,
                new XmlCharacterFileQueries(new CharacterFileService()), resolver, CreateCatalog());
            Assert.IsNull(service.Load(replaced.Capture(), new("not-admitted")).Value);
            Assert.AreEqual(0, resolver.UnscopedCalls);
            Assert.IsEmpty(resolver.Scopes);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Life_module_source_scope_is_disposed_when_evaluation_throws()
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new Chummer.Contracts.Workspaces.CharacterWorkspaceId("life-scope-failure");
            var store = SeedJourney(directory, id);
            var owner = new LocalOwnerContextAccessor();
            var resolver = new LifeOperationResolver(new FileSystemCharacterSourceDataResolver(CreateOverlays())) { Fail = true };
            var service = new OwnerBoundCharacterCreationLifeModuleFinalizationService(store, owner,
                new XmlCharacterFileQueries(new CharacterFileService()), resolver, CreateCatalog());
            byte[] before = File.ReadAllBytes(WorkspacePath(directory, id));
            Assert.ThrowsExactly<ApplicationException>(() => service.Load(owner.Capture(), id));
            Assert.HasCount(1, resolver.Scopes);
            Assert.IsTrue(resolver.Scopes[0].Disposed);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));
            resolver.Fail = false;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, service.Load(owner.Capture(), id).Outcome);
            Assert.IsTrue(resolver.Scopes[1].Disposed, "A failed call must not retain its owner lease or source scope.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class LifeOperationResolver(FileSystemCharacterSourceDataResolver inner)
        : ICharacterSourceDataResolver, ICharacterSourceDataResolverOperationScopeFactory
    {
        internal List<LifeObservedScope> Scopes { get; } = [];
        internal int UnscopedCalls { get; private set; }
        internal bool Fail { get; set; }
        internal bool RejectSources { get; set; }
        public ICharacterSourceDataContext? TryCreateContext(string xml)
        { UnscopedCalls++; return inner.TryCreateContext(xml); }
        public ICharacterSourceDataResolverOperationScope CreateOperationScope()
        {
            var scope = new LifeObservedScope(inner.CreateOperationScope(), Fail, () => RejectSources);
            Scopes.Add(scope);
            return scope;
        }
    }

    private sealed class LifeObservedScope(ICharacterSourceDataResolverOperationScope inner, bool fail, Func<bool> rejected)
        : ICharacterSourceDataResolverOperationScope
    {
        internal bool Disposed { get; private set; }
        internal int Calls { get; private set; }
        internal HashSet<ICharacterSourceDataContext> Contexts { get; } = new(ReferenceEqualityComparer.Instance);
        public ICharacterSourceDataContext? TryCreateContext(string xml)
        {
            Assert.IsFalse(Disposed);
            Calls++;
            if (fail) throw new ApplicationException("Injected Life Modules source failure.");
            if (rejected()) return null;
            var context = inner.TryCreateContext(xml);
            if (context is not null) Contexts.Add(context);
            return context;
        }
        public void Dispose() { inner.Dispose(); Disposed = true; }
    }

    [TestMethod]
    public void Life_module_source_scope_still_rejects_source_loss_after_flush_without_a_write()
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = LifeCharacterProjectionFixture(directory, "mundane");
            var owner = new LocalOwnerContextAccessor();
            var resolver = new LifeOperationResolver(new FileSystemCharacterSourceDataResolver(CreateOverlays()));
            var store = new FileWorkspaceStore(directory, new LifeScopeFlushFault(resolver));
            var service = new OwnerBoundCharacterCreationLifeModuleFinalizationService(store, owner,
                new XmlCharacterFileQueries(new CharacterFileService()), resolver, CreateCatalog());
            var result = service.Confirm(owner.Capture(), LifeFinalizationCommand(fixture));
            Assert.IsTrue(resolver.RejectSources, "The first evaluation must reach the durable flush boundary.");
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
            Assert.HasCount(1, resolver.Scopes);
            Assert.IsTrue(resolver.Scopes[0].Disposed);
            CollectionAssert.AreEqual(fixture.Before, File.ReadAllBytes(WorkspacePath(directory, fixture.Request.Binding.WorkspaceId)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class LifeScopeFlushFault(LifeOperationResolver resolver) : IFileWorkspaceStoreFaultInjector
    {
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed) resolver.RejectSources = true;
        }
    }
}
