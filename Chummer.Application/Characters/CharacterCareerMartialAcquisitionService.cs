using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterCareerMartialAcquisitionService(IWorkspaceStore store, ICharacterSourceDataResolver sources,
    IOwnerContextLeaseAccessor owners) : ICharacterCareerMartialAcquisitionService
{
    public CharacterCareerMartialAcquisitionListResult List(OwnerContextStamp owner, CharacterWorkspaceId id, long expectedRevision)
    {
        if (!owner.IsValid || !owners.TryAcquire(owner, out var lease)) return new(CharacterCareerMartialAcquisitionOutcome.Conflict);
        using (lease)
        {
            if (lease.Stamp != owner || store is not ICharacterCareerMartialAcquisitionAtomicCommitCapability { CareerMartialAcquisitionGuardsInstalled: true } atomic)
                return new(CharacterCareerMartialAcquisitionOutcome.Unavailable);
            return atomic.ListCareerMartialAcquisition(owners, lease, id, expectedRevision, sources);
        }
    }
    public CharacterCareerMartialAcquisitionPreviewResult Preview(OwnerContextStamp owner, CharacterCareerMartialAcquisitionRequest request)
    {
        if (!owner.IsValid || !CharacterCareerMartialAcquisitionTransaction.IsValidRequest(request)
            || !owners.TryAcquire(owner, out var lease)) return new(CharacterCareerMartialAcquisitionOutcome.Conflict);
        using (lease)
        {
            if (lease.Stamp != owner || store is not ICharacterCareerMartialAcquisitionAtomicCommitCapability { CareerMartialAcquisitionGuardsInstalled: true } atomic)
                return new(CharacterCareerMartialAcquisitionOutcome.Unavailable);
            return atomic.PreviewCareerMartialAcquisition(owners, lease, request, sources);
        }
    }
    public CharacterCareerMartialAcquisitionResult Commit(OwnerContextStamp owner, CharacterCareerMartialAcquisitionCommand command,
        CancellationToken cancellationToken = default) => Dispatch(owner, command, false, cancellationToken);
    public CharacterCareerMartialAcquisitionResult FinalizeDispatch(OwnerContextStamp owner, CharacterCareerMartialAcquisitionCommand originalCommand,
        CancellationToken cancellationToken = default) => Dispatch(owner, originalCommand, true, cancellationToken);
    private CharacterCareerMartialAcquisitionResult Dispatch(OwnerContextStamp owner, CharacterCareerMartialAcquisitionCommand command,
        bool retire, CancellationToken token)
    {
        // Lookup may recover an old applied result after restart; only a new write requires the original live stamp.
        if (!owner.IsValid || !CharacterCareerMartialAcquisitionTransaction.IsConfirmedCommand(command)) return new(CharacterCareerMartialAcquisitionOutcome.Conflict);
        if (!owners.TryAcquire(owner, out var lease)) return new(CharacterCareerMartialAcquisitionOutcome.Conflict);
        using (lease)
        {
            if (lease.Stamp != owner) return new(CharacterCareerMartialAcquisitionOutcome.Conflict);
            var read = Read(owner, lease, command.Request.WorkspaceId);
            if (!read.Success || read.Value is not { } saved) return MapReadFailure(read);
            var prior = LookupReceipt(owner, lease, saved, command.Request.OperationId,
                CharacterCareerMartialAcquisitionTransaction.CommandDigest(command));
            if (prior.Outcome != CharacterCareerMartialAcquisitionOutcome.NotFound) return prior;
            if (!(retire ? CharacterCareerMartialAcquisitionTransaction.SameOwner(owner, command) : CharacterCareerMartialAcquisitionTransaction.OwnerMatches(owner, command))) return new(CharacterCareerMartialAcquisitionOutcome.Conflict);
            if (store is not ICharacterCareerMartialAcquisitionAtomicCommitCapability atomic || !atomic.CareerMartialAcquisitionGuardsInstalled)
                return new(CharacterCareerMartialAcquisitionOutcome.Unavailable);
            try
            {
                var result = retire ? atomic.FinalizeCareerMartialAcquisitionDispatch(lease, command, owners, token)
                    : atomic.CommitCareerMartialAcquisition(lease, command, sources, owners, token);
                var after = Read(owner, lease, command.Request.WorkspaceId);
                if (!after.Success || after.Value is not { } observed) return new(CharacterCareerMartialAcquisitionOutcome.UnknownOutcome);
                var recovered = LookupReceipt(owner, lease, observed, command.Request.OperationId,
                    CharacterCareerMartialAcquisitionTransaction.CommandDigest(command));
                if (recovered.Outcome == CharacterCareerMartialAcquisitionOutcome.Replayed)
                    return recovered with { Outcome = result.Outcome == CharacterCareerMartialAcquisitionOutcome.Applied
                        ? CharacterCareerMartialAcquisitionOutcome.Applied : CharacterCareerMartialAcquisitionOutcome.Replayed };
                if (recovered.Outcome != CharacterCareerMartialAcquisitionOutcome.NotFound) return recovered;
                return result.Outcome is CharacterCareerMartialAcquisitionOutcome.Conflict or CharacterCareerMartialAcquisitionOutcome.Corrupt
                    or CharacterCareerMartialAcquisitionOutcome.IdempotencyConflict or CharacterCareerMartialAcquisitionOutcome.Canceled
                    ? result with { Receipt = null } : new(CharacterCareerMartialAcquisitionOutcome.UnknownOutcome);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            {
                // Adapter failures cannot prove non-application. Never resubmit an issued operation here.
                var after = Read(owner, lease, command.Request.WorkspaceId);
                if (after.Success && after.Value is { } observed)
                {
                    var result = LookupReceipt(owner, lease, observed, command.Request.OperationId,
                        CharacterCareerMartialAcquisitionTransaction.CommandDigest(command));
                    if (result.Receipt is not null) return result;
                }
                return new(CharacterCareerMartialAcquisitionOutcome.UnknownOutcome);
            }
        }
    }
    public CharacterCareerMartialAcquisitionResult Lookup(OwnerContextStamp owner, CharacterWorkspaceId id, Guid operationId, string digest)
    {
        if (!owner.IsValid || operationId == Guid.Empty || !CharacterCareerMartialAcquisitionTransaction.IsDigest(digest)
            || !owners.TryAcquire(owner, out var lease)) return new(CharacterCareerMartialAcquisitionOutcome.Conflict);
        using (lease)
        {
            if (lease.Stamp != owner) return new(CharacterCareerMartialAcquisitionOutcome.Conflict);
            var read = Read(owner, lease, id);
            return read.Success && read.Value is { } saved
                ? LookupReceipt(owner, lease, saved, operationId, digest) : MapReadFailure(read);
        }
    }
    private CharacterCareerMartialAcquisitionResult LookupReceipt(OwnerContextStamp owner, IOwnerContextLease lease,
        WorkspaceStoredDocument saved, Guid operationId, string digest)
    {
        var result = CharacterCareerMartialAcquisitionTransaction.Lookup(owner.Owner, saved, operationId, digest);
        if (result.Outcome != CharacterCareerMartialAcquisitionOutcome.IdempotencyConflict
            || store is not ICharacterCareerMartialAcquisitionAtomicCommitCapability { CareerMartialAcquisitionGuardsInstalled: true } atomic)
            return result;
        try
        {
            var recovered = atomic.RecoverRestoredCareerMartialAcquisitionReceipt(owners, lease, saved, operationId, digest);
            // A failed recovery must never turn a reserved operation into a fresh dispatch.
            return recovered.Outcome == CharacterCareerMartialAcquisitionOutcome.Replayed ? recovered : result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return result; }
    }
    private WorkspaceStoreReadResult Read(OwnerContextStamp owner, IOwnerContextLease lease, CharacterWorkspaceId id)
    {
        if (!CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id)) return new(WorkspaceOperationOutcome.Corrupt);
        try
        {
            var read = store is ICharacterCareerMartialAcquisitionAtomicCommitCapability atomic
                ? atomic.ReadCareerMartialAcquisitionWorkspace(owners, lease, id)
                : owner.Owner.IsLocalSingleUser ? store.Get(id) : store.Get(owner.Owner, id);
            return read.Success && (read.Value is null || read.Value.Id != id || read.Value.ContentRevision <= 0
                || read.Value.SavedRevision <= 0 || read.Value.SavedRevision > read.Value.ContentRevision)
                ? new(WorkspaceOperationOutcome.Corrupt) : read;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(WorkspaceOperationOutcome.Unavailable); }
    }
    public static CharacterCareerMartialAcquisitionResult MapReadFailure(WorkspaceStoreReadResult read)
        => new(read.Outcome switch { WorkspaceOperationOutcome.Missing => CharacterCareerMartialAcquisitionOutcome.Missing,
            WorkspaceOperationOutcome.Corrupt => CharacterCareerMartialAcquisitionOutcome.Corrupt,
            WorkspaceOperationOutcome.Conflict => CharacterCareerMartialAcquisitionOutcome.Conflict,
            _ => CharacterCareerMartialAcquisitionOutcome.Unavailable }, Error: read.Error);
}
