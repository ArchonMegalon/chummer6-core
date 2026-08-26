using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.DependencyInjection;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceCharacterAfterRunSettlementWorkspaceTests
{
    private static readonly CharacterWorkspaceId WorkspaceId = new("after-run-workspace-1");
    private static readonly Guid TransactionId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");

    [TestMethod]
    public void Di_preserves_host_projection_source_and_default_source_fails_closed()
    {
        var store = new AuxiliaryPreservingWorkspaceStore(Document());
        var hostSource = new EchoProposalSource(Projection());
        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceStore>(store);
        services.AddSingleton<ICharacterAfterRunSettlementProposalProjectionSource>(
            hostSource);

        services.AddCharacterAfterRunSettlementPersistence();

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.AreSame(
            hostSource,
            provider.GetRequiredService<
                ICharacterAfterRunSettlementProposalProjectionSource>());
        Assert.IsInstanceOfType<WorkspaceCharacterAfterRunSettlementWorkspace>(
            provider.GetRequiredService<ICharacterAfterRunSettlementWorkspace>());
        Assert.IsInstanceOfType<CharacterAfterRunSettlementService>(
            provider.GetRequiredService<ICharacterAfterRunSettlementService>());

        var unavailable =
            new UnavailableCharacterAfterRunSettlementProposalProjectionSource();
        var workspace = new WorkspaceCharacterAfterRunSettlementWorkspace(
            store,
            unavailable);
        CharacterAfterRunSettlementWorkspaceReadResult read = workspace.Read(
            WorkspaceId,
            Projection().Identity);
        Assert.AreEqual(
            CharacterAfterRunSettlementWorkspaceOutcome.Unavailable,
            read.Outcome);
        Assert.IsNotNull(read.Error);
    }

    [TestMethod]
    public void File_workspace_applies_reopens_and_replays_before_stale_revision()
    {
        string stateDirectory = TemporaryDirectory();
        try
        {
            var store = SeedFileStore(stateDirectory);
            var service = Service(store, new EchoProposalSource(Projection()));
            CharacterAfterRunSettlementCommand command = Command(service);

            CharacterAfterRunSettlementResult applied = service.Settle(command);

            Assert.AreEqual(CharacterAfterRunSettlementServiceOutcome.Applied, applied.Outcome);
            Assert.AreEqual(2L, applied.CurrentWorkspaceRevision);
            Assert.AreEqual(19, applied.Receipt!.KarmaAfter);
            WorkspaceStoredDocument saved = store.Get(WorkspaceId).Value!;
            Assert.AreEqual(2L, saved.ContentRevision);
            Assert.AreEqual(2L, saved.SavedRevision);
            XDocument xml = XDocument.Parse(saved.Document.Content);
            XElement root = xml.Root!;
            Assert.AreEqual("12", root.Element("streetcred")?.Value);
            Assert.AreEqual("5", root.Element("notoriety")?.Value);
            Assert.AreEqual("7", root.Element("publicawareness")?.Value);
            Assert.AreEqual("19", root.Element("karma")?.Value);
            Assert.AreEqual(2, root.Element("contacts")?.Elements("contact").Count());
            Assert.AreEqual(1, root.Element("expenses")?.Elements("expense").Count());
            Assert.AreEqual(0, root.Elements("chummerafterrunsettlementledger").Count());
            Assert.AreEqual(
                1,
                saved.Document.AuxiliaryState.CharacterAfterRunSettlementReceipts?.Count);

            var reopenedStore = new FileWorkspaceStore(stateDirectory);
            var reopened = Service(
                reopenedStore,
                new UnavailableCharacterAfterRunSettlementProposalProjectionSource());
            CharacterAfterRunSettlementResult replay = reopened.Settle(command);
            Assert.AreEqual(CharacterAfterRunSettlementServiceOutcome.Replayed, replay.Outcome);
            Assert.AreEqual(
                applied.Receipt!.ReceiptDigest,
                replay.Receipt!.ReceiptDigest);
            CollectionAssert.AreEqual(
                applied.Receipt.AddedContacts.ToArray(),
                replay.Receipt.AddedContacts.ToArray());

            CharacterAfterRunSettlementResult collision = reopened.Settle(command with
            {
                ExplicitlyConfirmed = false
            });
            Assert.AreEqual(
                CharacterAfterRunSettlementServiceOutcome.IdempotencyConflict,
                collision.Outcome);
            Assert.AreEqual(2L, reopenedStore.Get(WorkspaceId).Value!.ContentRevision);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Unknown_write_is_recovered_from_the_durable_reopened_ledger()
    {
        string stateDirectory = TemporaryDirectory();
        try
        {
            FileWorkspaceStore durable = SeedFileStore(stateDirectory);
            var throwing = new ThrowAfterSuccessfulCheckpointStore(durable);
            var service = Service(throwing, new EchoProposalSource(Projection()));
            CharacterAfterRunSettlementCommand command = Command(service);

            CharacterAfterRunSettlementResult result = service.Settle(command);

            Assert.AreEqual(CharacterAfterRunSettlementServiceOutcome.Replayed, result.Outcome);
            Assert.IsNotNull(result.Receipt);
            Assert.AreEqual(1, throwing.CommittedWriteCount);
            Assert.AreEqual(2L, new FileWorkspaceStore(stateDirectory)
                .Get(WorkspaceId).Value!.ContentRevision);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Tampered_persisted_receipt_is_rejected_on_reopen()
    {
        var store = new AuxiliaryPreservingWorkspaceStore(Document());
        ICharacterAfterRunSettlementService service = Service(
            store,
            new EchoProposalSource(Projection()));
        CharacterAfterRunSettlementCommand command = Command(service);
        Assert.AreEqual(
            CharacterAfterRunSettlementServiceOutcome.Applied,
            service.Settle(command).Outcome);
        store.TamperAppliedResultDigest();

        var reopened = new WorkspaceCharacterAfterRunSettlementWorkspace(
            store,
            new UnavailableCharacterAfterRunSettlementProposalProjectionSource());
        CharacterAfterRunSettlementWorkspaceLookupResult lookup = reopened.Lookup(
            WorkspaceId,
            TransactionId,
            command.CommandDigest());
        Assert.AreEqual(CharacterAfterRunSettlementWorkspaceOutcome.Corrupt, lookup.Outcome);
        Assert.IsNull(lookup.Receipt);
    }

    [TestMethod]
    public void Atomic_replacement_preserves_quality_and_magic_auxiliary_lanes()
    {
        WorkspaceDocumentAuxiliaryState auxiliary = new(
            CharacterCreationMagicResonanceReceipts:
                Array.Empty<CharacterCreationMagicResonanceReceipt>(),
            CharacterCreationQualitiesReceipts:
                Array.Empty<CharacterCreationQualitiesDraftReceipt>());
        WorkspaceDocument initial = Document(auxiliary);
        var store = new AuxiliaryPreservingWorkspaceStore(initial);
        var service = Service(store, new EchoProposalSource(Projection()));
        CharacterAfterRunSettlementCommand command = Command(service);
        string beforeDigest = initial.AuxiliaryStateDigest;

        CharacterAfterRunSettlementResult applied = service.Settle(command);

        Assert.AreEqual(CharacterAfterRunSettlementServiceOutcome.Applied, applied.Outcome);
        Assert.IsTrue(store.SawAtomicCheckpoint);
        WorkspaceDocumentAuxiliaryState siblingState =
            store.Current.Document.AuxiliaryState with
            {
                CharacterAfterRunSettlementReceipts = null
            };
        Assert.AreEqual(
            beforeDigest,
            WorkspaceDocumentAuxiliaryStateDigest.Compute(siblingState));
        Assert.AreNotEqual(beforeDigest, store.Current.Document.AuxiliaryStateDigest);
        Assert.IsNotNull(
            store.Current.Document.AuxiliaryState.CharacterCreationQualitiesReceipts);
        Assert.IsNotNull(
            store.Current.Document.AuxiliaryState.CharacterCreationMagicResonanceReceipts);
    }

    [TestMethod]
    public void File_store_rejects_a_direct_quality_lane_write_through_the_governed_cas()
    {
        string stateDirectory = TemporaryDirectory();
        try
        {
            FileWorkspaceStore store = SeedFileStore(stateDirectory);
            WorkspaceStoredDocument saved = store.Get(WorkspaceId).Value!;
            WorkspaceDocument forged = new(
                saved.Document.State with
                {
                    AuxiliaryState = saved.Document.AuxiliaryState with
                    {
                        CharacterCreationQualitiesReceipts =
                            Array.Empty<CharacterCreationQualitiesDraftReceipt>()
                    }
                },
                saved.Document.Format);

            WorkspaceStoreMutationResult result =
                store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    WorkspaceId,
                    saved.ContentRevision,
                    saved.Document.AuxiliaryStateDigest,
                    forged);

            Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, result.Outcome);
            Assert.AreEqual(1L, store.Get(WorkspaceId).Value!.ContentRevision);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    private static ICharacterAfterRunSettlementService Service(
        IWorkspaceStore store,
        ICharacterAfterRunSettlementProposalProjectionSource source)
        => new CharacterAfterRunSettlementService(
            new WorkspaceCharacterAfterRunSettlementWorkspace(store, source));

    private static CharacterAfterRunSettlementCommand Command(
        ICharacterAfterRunSettlementService service)
    {
        CharacterAfterRunSettlementQuoteBinding binding = service.Quote(
            new CharacterAfterRunSettlementQuoteRequest(
                WorkspaceId,
                Projection().Identity)).Binding!;
        return new CharacterAfterRunSettlementCommand(
            CharacterAfterRunSettlementServiceSchemas.CommandV1,
            binding.WorkspaceId,
            binding.WorkspaceRevision,
            binding.Identity,
            binding.Quote.SourceDigest,
            binding.Quote.CustomDataDigest,
            binding.Quote.GmPolicyDigest,
            binding.Quote.RuntimeDigest,
            binding.Quote.LogicalDigest,
            binding.BindingDigest,
            TransactionId,
            ExplicitlyConfirmed: true);
    }

    private static FileWorkspaceStore SeedFileStore(string stateDirectory)
    {
        var store = new FileWorkspaceStore(stateDirectory);
        Assert.IsTrue(store.CreateWorkspaceDocument(WorkspaceId, Document()).Success);
        Assert.IsTrue(store.SaveCheckpoint(WorkspaceId, 1).Success);
        return store;
    }

    private static WorkspaceDocument Document(
        WorkspaceDocumentAuxiliaryState? auxiliary = null)
        => new(
            new WorkspaceDocumentState(
                CharacterAfterRunSettlementRules.RulesetId,
                1,
                "workspace",
                """
                <character>
                  <created>True</created>
                  <karma>30</karma>
                  <streetcred>10</streetcred>
                  <notoriety>4</notoriety>
                  <publicawareness>6</publicawareness>
                  <contacts />
                  <expenses />
                </character>
                """)
            {
                AuxiliaryState = auxiliary ?? WorkspaceDocumentAuxiliaryState.Empty
            },
            WorkspaceDocumentFormat.NativeXml);

    private static CharacterAfterRunSettlementProposalProjection Projection()
    {
        CharacterAfterRunSettlementInput input =
            CharacterAfterRunSettlementRulesTests.Input();
        return new CharacterAfterRunSettlementProposalProjection(
            input.Identity,
            input.TargetOwnedByCharacter,
            input.ProjectionIsExact,
            input.RunCompleted,
            input.ExpectedGmActorId,
            input.ExpectedOwnerActorId,
            input.CurrentHeat,
            input.HeatDelta,
            input.StreetCredDelta,
            input.NotorietyDelta,
            input.PublicAwarenessDelta,
            input.Settings,
            input.ContactProposals,
            input.GmReview,
            input.OwnerReview,
            input.RawSourceState,
            input.RawCustomDataState,
            input.RawGmPolicyState,
            input.RawRuntimeState);
    }

    private static string TemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "chummer-after-run-workspace-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class EchoProposalSource :
        ICharacterAfterRunSettlementProposalProjectionSource
    {
        private readonly CharacterAfterRunSettlementProposalProjection _projection;

        public EchoProposalSource(
            CharacterAfterRunSettlementProposalProjection projection)
        {
            _projection = projection;
        }

        public CharacterAfterRunSettlementProposalProjectionResult Read(
            CharacterAfterRunSettlementProposalProjectionRequest request)
            => new(
                CharacterAfterRunSettlementProposalProjectionOutcome.Available,
                request.WorkspaceId,
                request.WorkspaceRevision,
                request.CharacterProjectionDigest,
                _projection);
    }

    private class DelegatingWorkspaceStore :
        IWorkspaceStore,
        IWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        protected DelegatingWorkspaceStore(IWorkspaceStore inner)
        {
            Inner = inner;
        }

        protected IWorkspaceStore Inner { get; }

        public bool SupportsWorkspaceAuxiliaryStateAtomicCommit =>
            Inner is IWorkspaceAuxiliaryStateAtomicCommitCapability
            { SupportsWorkspaceAuxiliaryStateAtomicCommit: true };

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document)
            => Inner.CreateWorkspaceDocument(document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            OwnerScope owner,
            WorkspaceDocument document)
            => Inner.CreateWorkspaceDocument(owner, document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            CharacterWorkspaceId id,
            WorkspaceDocument document)
            => Inner.CreateWorkspaceDocument(id, document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            OwnerScope owner,
            CharacterWorkspaceId id,
            WorkspaceDocument document)
            => Inner.CreateWorkspaceDocument(owner, id, document);

        public IReadOnlyList<WorkspaceStoreEntry> List() => Inner.List();

        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => Inner.List(owner);

        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id) => Inner.Get(id);

        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id)
            => Inner.Get(owner, id);

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document)
            => Inner.ReplaceWorkspaceDocument(id, expectedContentRevision, document);

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document)
            => Inner.ReplaceWorkspaceDocument(owner, id, expectedContentRevision, document);

        public virtual WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document)
            => Inner.ReplaceWorkspaceDocumentAndCheckpoint(
                id,
                expectedContentRevision,
                document);

        public virtual WorkspaceStoreMutationResult
            ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                CharacterWorkspaceId id,
                long expectedContentRevision,
                string expectedAuxiliaryStateDigest,
                WorkspaceDocument document)
            => Inner.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                id,
                expectedContentRevision,
                expectedAuxiliaryStateDigest,
                document);

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndCheckpoint(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document)
            => Inner.ReplaceWorkspaceDocumentAndCheckpoint(
                owner,
                id,
                expectedContentRevision,
                document);

        public WorkspaceStoreMutationResult SaveCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => Inner.SaveCheckpoint(id, expectedContentRevision);

        public WorkspaceStoreMutationResult SaveCheckpoint(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => Inner.SaveCheckpoint(owner, id, expectedContentRevision);

        public WorkspaceStoreMutationResult Delete(
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => Inner.Delete(id, expectedContentRevision);

        public WorkspaceStoreMutationResult Delete(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => Inner.Delete(owner, id, expectedContentRevision);
    }

    private sealed class ThrowAfterSuccessfulCheckpointStore : DelegatingWorkspaceStore
    {
        public ThrowAfterSuccessfulCheckpointStore(IWorkspaceStore inner)
            : base(inner)
        {
        }

        public int CommittedWriteCount { get; private set; }

        public override WorkspaceStoreMutationResult
            ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            string expectedAuxiliaryStateDigest,
            WorkspaceDocument document)
        {
            WorkspaceStoreMutationResult result =
                base.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                id,
                expectedContentRevision,
                expectedAuxiliaryStateDigest,
                document);
            if (result.Success)
            {
                CommittedWriteCount++;
                throw new IOException("simulated_ack_loss_after_durable_write");
            }
            return result;
        }
    }

    private sealed class AuxiliaryPreservingWorkspaceStore :
        IWorkspaceStore,
        IWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        public AuxiliaryPreservingWorkspaceStore(WorkspaceDocument document)
        {
            Current = new WorkspaceStoredDocument(
                WorkspaceId,
                document,
                1,
                1,
                DateTimeOffset.UtcNow);
        }

        public WorkspaceStoredDocument Current { get; private set; }

        public bool SawAtomicCheckpoint { get; private set; }

        public bool SupportsWorkspaceAuxiliaryStateAtomicCommit => true;

        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
            => id == WorkspaceId
                ? new WorkspaceStoreReadResult(WorkspaceOperationOutcome.Success, Current)
                : new WorkspaceStoreReadResult(WorkspaceOperationOutcome.Missing);

        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id)
            => Get(id);

        public WorkspaceStoreMutationResult
            ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            string expectedAuxiliaryStateDigest,
            WorkspaceDocument document)
        {
            if (id != WorkspaceId)
            {
                return new WorkspaceStoreMutationResult(WorkspaceOperationOutcome.Missing);
            }
            if (expectedContentRevision != Current.ContentRevision)
            {
                return new WorkspaceStoreMutationResult(WorkspaceOperationOutcome.Conflict);
            }
            if (expectedAuxiliaryStateDigest != Current.Document.AuxiliaryStateDigest)
            {
                return new WorkspaceStoreMutationResult(
                    WorkspaceOperationOutcome.Conflict,
                    Error: "stale_auxiliary_state");
            }
            SawAtomicCheckpoint = true;
            long revision = Current.ContentRevision + 1;
            Current = new WorkspaceStoredDocument(
                WorkspaceId,
                document,
                revision,
                revision,
                DateTimeOffset.UtcNow);
            return new WorkspaceStoreMutationResult(
                WorkspaceOperationOutcome.Success,
                new WorkspaceStoreEntry(
                    WorkspaceId,
                    Current.LastUpdatedUtc,
                    revision,
                    revision));
        }

        public void TamperAppliedResultDigest()
        {
            CharacterAfterRunSettlementReceiptLedgerEntry[] ledger =
                Current.Document.AuxiliaryState.CharacterAfterRunSettlementReceipts!
                    .ToArray();
            ledger[0] = ledger[0] with { AppliedResultDigest = new string('0', 64) };
            WorkspaceDocument tampered = new(
                Current.Document.State with
                {
                    AuxiliaryState = Current.Document.AuxiliaryState with
                    {
                        CharacterAfterRunSettlementReceipts = ledger
                    }
                },
                Current.Document.Format);
            Current = Current with { Document = tampered };
        }

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document)
            => Unsupported();

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            OwnerScope owner,
            WorkspaceDocument document)
            => Unsupported();

        public IReadOnlyList<WorkspaceStoreEntry> List() => [];

        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => [];

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document)
            => Unsupported();

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document)
            => Unsupported();

        public WorkspaceStoreMutationResult SaveCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => Unsupported();

        public WorkspaceStoreMutationResult SaveCheckpoint(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => Unsupported();

        public WorkspaceStoreMutationResult Delete(
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => Unsupported();

        public WorkspaceStoreMutationResult Delete(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => Unsupported();

        private static WorkspaceStoreMutationResult Unsupported()
            => new(WorkspaceOperationOutcome.Unavailable);
    }
}

internal static class CharacterAfterRunSettlementCommandTestExtensions
{
    public static string CommandDigest(this CharacterAfterRunSettlementCommand command)
    {
        Assert.IsTrue(CharacterAfterRunSettlementServiceIntegrity.TryComputeCommandDigest(
            command,
            out string digest));
        return digest;
    }
}
