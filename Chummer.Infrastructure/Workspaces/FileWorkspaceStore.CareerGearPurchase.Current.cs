using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Purchase = Chummer.Application.Characters.CharacterCareerGearPurchaseTransaction;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    private static void PurchaseOwner(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease)
    {
        if (!lease.Stamp.IsValid || owners.Capture() != lease.Stamp) throw new IOException("gear_owner_changed");
    }
    private string PurchaseCurrentBinding(string path, IOwnerContextLease lease)
        => Purchase.Hash(CharacterCreationFinalizationDigest.Compute(new {
            Runtime = CareerGearPurchaseRuntimeDigest, Owner = lease.Stamp,
            Record = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)),
            Slot = ReadContinuationSlotUnderLease(path), UpdatedUtc = File.GetLastWriteTimeUtc(path) }));

    private WorkspaceStoreReadResult ReadPurchaseUnderLease(string path, CharacterWorkspaceId id,
        IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        out IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> ledger, out GearCurrentEnvironment? current)
    {
        current = null; ledger = []; PurchaseOwner(owners, lease);
        var record = ReadPrivateRecord(path);
        if (!TryMaterializeRecord(record, out _, out _, out _, out bool migration) || migration
            || record.RetainedRuleSources is { CurrentEnvironment: null }) return UnavailableRead();
        return ReadGearWorkspaceUnderLease(lease.Stamp.Owner, id, path, owners, lease, lease.Stamp, out ledger, out current);
    }
    private ICharacterSourceDataResolver PurchaseSources(GearCurrentEnvironment? current, ICharacterSourceDataResolver ordinary)
        => current is null ? ordinary : current.Capture.CreateCurrentEnvironmentResolution(
            current.Execution.ExecutionProfile, current.Execution.ExecutionProfileFingerprint)?.Resolver
                ?? throw new IOException("gear_current_source_unavailable");
    private void ObservePurchase(string path, string binding, IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, GearCurrentEnvironment? current)
    {
        current?.ValidateFreeSpriteReadObservation(); PurchaseOwner(owners, lease);
        if (PurchaseCurrentBinding(path, lease) != binding) throw new IOException("gear_current_binding_changed");
    }

    internal CharacterCareerGearPurchaseCatalogResult QueryCurrentCareerGearPurchase(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, ICharacterSourceDataResolver sources, CharacterCareerGearPurchaseCatalogQuery query)
    {
        if (query is null || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(query.WorkspaceId))
            return new(CharacterCareerGearPurchaseOutcome.Conflict, null, null, ["gear_catalog_query_invalid"]);
        try
        {
            PurchaseOwner(owners, lease);
            string? path = TryGetPath(lease.Stamp.Owner, query.WorkspaceId);
            if (path is null || !File.Exists(path)) return new(CharacterCareerGearPurchaseOutcome.Missing, null, null, []);
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            string binding = PurchaseCurrentBinding(path, lease);
            var read = ReadPurchaseUnderLease(path, query.WorkspaceId, owners, lease, out _, out var environment);
            using var current = environment;
            if (!CareerGearPurchaseGuardsInstalled || !read.Success || read.Value is not { } saved
                || !Purchase.IsValidHistory(saved) || HasPendingGearEquipment(saved))
                return new(CharacterCareerGearPurchaseOutcome.Unavailable, null, null, ["gear_workspace_unavailable"]);
            if (saved.ContentRevision != query.ExpectedWorkspaceRevision)
                return new(CharacterCareerGearPurchaseOutcome.Conflict, null, saved.ContentRevision, ["gear_catalog_revision_conflict"]);
            var resolver = PurchaseSources(current, sources);
            using var scope = (resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            if (!Purchase.TryReadCatalog(lease.Stamp.Owner, saved, scope ?? resolver, binding, out var catalog, out string error))
                return new(CharacterCareerGearPurchaseOutcome.Unavailable, null, saved.ContentRevision, [error]);
            ObservePurchase(path, binding, owners, lease, current);
            return new(CharacterCareerGearPurchaseOutcome.Available, catalog, saved.ContentRevision, []);
        }
        catch (Exception ex) when (PurchaseExpected(ex))
        { return new(CharacterCareerGearPurchaseOutcome.Unavailable, null, null, ["gear_current_catalog_unavailable"]); }
    }

    internal CharacterCareerGearPurchasePreviewResult PreviewCurrentCareerGearPurchase(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, ICharacterSourceDataResolver sources, CharacterCareerGearPurchaseRequest request,
        CharacterCareerGearPurchaseCatalogSelection? selection)
    {
        if (!Purchase.IsValidRequest(request)) return new(CharacterCareerGearPurchaseOutcome.Conflict, Error: "gear_request_invalid");
        try
        {
            PurchaseOwner(owners, lease);
            string? path = TryGetPath(lease.Stamp.Owner, request.WorkspaceId);
            if (path is null || !File.Exists(path)) return new(CharacterCareerGearPurchaseOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            string binding = PurchaseCurrentBinding(path, lease);
            var read = ReadPurchaseUnderLease(path, request.WorkspaceId, owners, lease, out _, out var environment);
            using var current = environment;
            if (!CareerGearPurchaseGuardsInstalled || !read.Success || read.Value is not { } saved
                || !Purchase.IsValidHistory(saved) || HasPendingGearEquipment(saved))
                return new(CharacterCareerGearPurchaseOutcome.Unavailable, Error: "gear_workspace_unavailable");
            if (saved.Document.AuxiliaryState.CharacterCareerGearPurchaseReceipts?.Any(r => r.Command.Request.OperationId == request.OperationId) == true)
                return new(CharacterCareerGearPurchaseOutcome.IdempotencyConflict);
            var resolver = PurchaseSources(current, sources);
            using var scope = (resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            if (!Purchase.TryRead(lease.Stamp.Owner, saved, request.SourceId, scope ?? resolver, binding, out var snapshot, out string error))
                return new(CharacterCareerGearPurchaseOutcome.Unavailable, Error: error);
            if (selection is not null && (selection.SourceId != request.SourceId || selection.Binding != snapshot!.Binding))
                return new(CharacterCareerGearPurchaseOutcome.Conflict, Error: "gear_catalog_selection_stale");
            if (!Purchase.TryPreview(snapshot!, request, out var preview, out error))
                return new(CharacterCareerGearPurchaseOutcome.Unavailable, Error: error);
            if (!Purchase.TryBuild(lease.Stamp.Owner, saved, snapshot!, preview!.Command with { ExplicitlyConfirmed = true }, out var document, out var receipt))
                return new(CharacterCareerGearPurchaseOutcome.Unavailable, Error: "gear_purchase_unavailable");
            if (current is not null)
            {
                var after = saved with { Document = document!, ContentRevision = receipt!.CommittedWorkspaceRevision, SavedRevision = receipt.CommittedWorkspaceRevision };
                var admission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture, _retainedCharacterQueries!, after);
                if (admission.AfterCapture is null || admission.AfterExecution is null || admission.Blockers.Count != 0)
                    return new(CharacterCareerGearPurchaseOutcome.Unavailable, Error: "gear_proposed_environment_unavailable");
            }
            ObservePurchase(path, binding, owners, lease, current);
            return new(CharacterCareerGearPurchaseOutcome.Available, preview, saved.ContentRevision);
        }
        catch (Exception ex) when (PurchaseExpected(ex))
        { return new(CharacterCareerGearPurchaseOutcome.Unavailable, Error: "gear_current_preview_unavailable"); }
    }

    // History lookup never requires replaying the purchase or reopening a now-missing source.
    private CharacterCareerGearPurchaseResult LookupPurchaseUnderLease(string path, IOwnerContextLease lease,
        CharacterWorkspaceId id, Guid operation, string digest)
    {
        var record = ReadPrivateRecord(path);
        if (!TryMaterializeRecord(record, out var document, out long revision, out long savedRevision, out bool migration)
            || migration || record.LocalHistory is not { } local || !local.IsValid(revision))
            return new(CharacterCareerGearPurchaseOutcome.Corrupt, Error: "gear_history_invalid");
        var saved = new WorkspaceStoredDocument(id, document, revision, savedRevision,
            new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)) { LocalHistory = local };
        return Purchase.Lookup(lease.Stamp.Owner, saved, operation, digest);
    }
    internal CharacterCareerGearPurchaseResult LookupCurrentCareerGearPurchase(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id, Guid operationId, string digest)
    {
        if (!CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id) || operationId == Guid.Empty || !Purchase.IsDigest(digest))
            return new(CharacterCareerGearPurchaseOutcome.Conflict);
        try
        {
            PurchaseOwner(owners, lease);
            string? path = TryGetPath(lease.Stamp.Owner, id);
            if (path is null || !File.Exists(path)) return new(CharacterCareerGearPurchaseOutcome.Missing);
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            var result = LookupPurchaseUnderLease(path, lease, id, operationId, digest);
            PurchaseOwner(owners, lease); return result;
        }
        catch (Exception ex) when (PurchaseExpected(ex))
        { return new(CharacterCareerGearPurchaseOutcome.Unavailable, Error: "gear_receipt_observation_unavailable"); }
    }

    internal CharacterCareerGearPurchaseResult CommitCurrentCareerGearPurchase(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, ICharacterSourceDataResolver sources, CharacterCareerGearPurchaseCommand command, CancellationToken token)
    {
        if (!Purchase.IsConfirmedCommand(command) || command.Binding.OwnerId != lease.Stamp.Owner.NormalizedValue)
            return new(CharacterCareerGearPurchaseOutcome.Conflict, Error: "gear_confirmation_invalid");
        bool published = false;
        CharacterCareerGearPurchaseResult result;
        try { result = PublishCurrentPurchase(owners, lease, sources, command, token, out published); }
        catch (Exception ex) when (PurchaseExpected(ex)) { result = new(CharacterCareerGearPurchaseOutcome.UnknownOutcome); }
        if (result.Outcome is not (CharacterCareerGearPurchaseOutcome.Applied or CharacterCareerGearPurchaseOutcome.UnknownOutcome)) return result;
        var recovered = LookupCurrentCareerGearPurchase(owners, lease, command.Request.WorkspaceId, command.Request.OperationId, Purchase.CommandDigest(command));
        return recovered.Outcome == CharacterCareerGearPurchaseOutcome.Replayed
            ? recovered with { Outcome = published ? CharacterCareerGearPurchaseOutcome.Applied : CharacterCareerGearPurchaseOutcome.Replayed }
            : new(CharacterCareerGearPurchaseOutcome.UnknownOutcome, Error: "gear_commit_not_observed");
    }
    private CharacterCareerGearPurchaseResult PublishCurrentPurchase(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, ICharacterSourceDataResolver sources, CharacterCareerGearPurchaseCommand command,
        CancellationToken token, out bool published)
    {
        published = false; PurchaseOwner(owners, lease);
        string? path = TryGetPath(lease.Stamp.Owner, command.Request.WorkspaceId);
        if (path is null || !File.Exists(path)) return new(CharacterCareerGearPurchaseOutcome.Missing);
        using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        var prior = LookupPurchaseUnderLease(path, lease, command.Request.WorkspaceId, command.Request.OperationId, Purchase.CommandDigest(command));
        PurchaseOwner(owners, lease);
        if (prior.Outcome != CharacterCareerGearPurchaseOutcome.NotFound) return prior;
        if (token.IsCancellationRequested) return new(CharacterCareerGearPurchaseOutcome.Canceled);
        string binding = PurchaseCurrentBinding(path, lease);
        if (command.Binding.RuntimeDigest != binding) return new(CharacterCareerGearPurchaseOutcome.Conflict, Error: "gear_current_binding_conflict");
        var read = ReadPurchaseUnderLease(path, command.Request.WorkspaceId, owners, lease, out var ledger, out var environment);
        using var current = environment;
        if (!CareerGearPurchaseGuardsInstalled || !read.Success || read.Value is not { } saved || HasPendingGearEquipment(saved))
            return new(CharacterCareerGearPurchaseOutcome.Unavailable, Error: "gear_workspace_unavailable");
        var resolver = PurchaseSources(current, sources);
        using var scope = (resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        resolver = scope ?? resolver;
        if (!Purchase.TryRead(lease.Stamp.Owner, saved, command.Request.SourceId, resolver, binding, out var snapshot, out _)
            || !Purchase.TryBuild(lease.Stamp.Owner, saved, snapshot!, command, out var document, out var receipt))
            return new(CharacterCareerGearPurchaseOutcome.Conflict, Error: "gear_binding_conflict");
        // The generic transition guard forbids changing this dedicated ledger.
        // TryBuild has source-admitted the exact append; keep every sibling exact
        // and validate the complete resulting auxiliary shape under this lease.
        if (!IsValidAuxiliaryState(saved.Id, receipt!.CommittedWorkspaceRevision, document!.AuxiliaryState, current?.Recovery)
            || !CharacterCareerGearEquipmentTransaction.Same(saved.Document.AuxiliaryState,
                document.AuxiliaryState with { CharacterCareerGearPurchaseReceipts = saved.Document.AuxiliaryState.CharacterCareerGearPurchaseReceipts }))
            return new(CharacterCareerGearPurchaseOutcome.Corrupt, Error: "gear_auxiliary_transition_invalid");
        var after = saved with { Document = document, ContentRevision = receipt.CommittedWorkspaceRevision, SavedRevision = receipt.CommittedWorkspaceRevision };
        var record = BuildPersistedRecord(document, after.ContentRevision, after.SavedRevision, saved.LocalHistory!, ledger, saved.DelegatedGmHistorySegmentStarts);
        CharacterCurrentRuleEnvironmentReadmissionResult? readmission = null;
        if (current is not null)
        {
            readmission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture, _retainedCharacterQueries!, after, token);
            if (readmission.AfterCapture is not { } capture || readmission.AfterExecution is not { } execution || readmission.Blockers.Count != 0)
                return new(CharacterCareerGearPurchaseOutcome.Unavailable, Error: "gear_proposed_environment_unavailable");
            current.Recovery.RetainAdmittedCapture(new(capture, _retainedCharacterQueries!), capture.IntegrityDigest);
            var continuation = new WorkspaceContinuationSnapshot(lease.Stamp.Owner.NormalizedValue,
                new(saved.Id, document, after.LastUpdatedUtc, after.ContentRevision, after.SavedRevision), []);
            record = record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion,
                RetainedRuleSources = current.Record.RetainedRuleSources! with { References = current.Recovery.SnapshotReferences(),
                    CurrentEnvironment = WorkspacePrivateRuleSourceClosure.DescribeCurrentEnvironment(continuation,
                        capture, execution.ExecutionProfile, execution.ExecutionProfileFingerprint, []) } };
        }
        using var admission = new PurchaseWriteAdmission(this, path, record, current, after, readmission);
        WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () => {
            token.ThrowIfCancellationRequested(); PurchaseOwner(owners, lease);
            if (PurchaseCurrentBinding(path, lease) != binding
                || !Purchase.IsSourceCurrent(saved, resolver, command.Request.SourceId, command.Binding)) throw new IOException("gear_source_changed");
            admission.ValidateFinalFence(record);
        });
        published = true; return new(CharacterCareerGearPurchaseOutcome.Applied);
    }

    [ThreadStatic] private static PurchaseWriteAdmission? _purchaseWriteAdmission;
    private sealed class PurchaseWriteAdmission : IDisposable
    {
        private readonly PurchaseWriteAdmission? previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        internal GearCurrentEnvironment? Current { get; }
        private readonly string digest;
        private readonly WorkspaceStoredDocument after;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult? readmission;
        internal PurchaseWriteAdmission(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record,
            GearCurrentEnvironment? current, WorkspaceStoredDocument after, CharacterCurrentRuleEnvironmentReadmissionResult? readmission)
        {
            Store = store; Path = System.IO.Path.GetFullPath(path); Current = current; this.after = after; this.readmission = readmission;
            digest = CharacterCreationFinalizationDigest.Compute(record); previous = _purchaseWriteAdmission; _purchaseWriteAdmission = this;
        }
        internal void ValidateFinalFence(PersistedWorkspaceRecord record)
        {
            if (CharacterCreationFinalizationDigest.Compute(record) != digest) throw new IOException("gear_exact_record_changed");
            if (Current is null) return;
            Current.ValidateFinalFence();
            if (readmission?.AfterCapture is not { } capture || readmission.AfterExecution is not { } execution
                || !execution.Revalidate(after, capture, Store._retainedCharacterQueries!)) throw new IOException("gear_proposed_environment_changed");
        }
        public void Dispose() => _purchaseWriteAdmission = previous;
    }
    private PurchaseWriteAdmission? PurchaseAdmissionFor(string path)
        => _purchaseWriteAdmission is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, System.IO.Path.GetFullPath(path)) ? admission : null;
    private static bool PurchaseExpected(Exception ex) => ex is IOException or UnauthorizedAccessException or InvalidOperationException
        or ArgumentException or FormatException or OverflowException or System.Xml.XmlException or OperationCanceledException;
}
