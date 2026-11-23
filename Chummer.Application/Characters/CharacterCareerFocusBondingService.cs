using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterCareerFocusBondingService(IWorkspaceStore store, ICharacterSourceDataResolver sources,
    IOwnerContextLeaseAccessor owners, Chummer.Application.Session.DurableSessionPlayService? play = null) : ICharacterCareerFocusBondingService
{
    public CharacterCareerFocusBondingListResult List(OwnerContextStamp owner, CharacterWorkspaceId id, long expectedRevision)
    {
        if (!owner.IsValid || !owners.TryAcquire(owner, out var lease)) return new(CharacterCareerFocusBondingOutcome.Conflict);
        using (lease)
        {
            if (lease.Stamp != owner || store is not ICharacterCareerFocusBondingAtomicCommitCapability { CareerFocusBondingGuardsInstalled: true })
                return new(CharacterCareerFocusBondingOutcome.Unavailable);
            if (store is ICharacterCareerFocusBondingCurrentEnvironmentCapability retained)
                return retained.ListCareerFocusBonding(owners, lease, id, expectedRevision);
            var read = Read(owner, id, lease);
            if (!read.Success || read.Value is not { } saved) return new(MapReadFailure(read).Outcome, Error: read.Error);
            if (saved.ContentRevision != expectedRevision || saved.SavedRevision != saved.ContentRevision
                || !CharacterCareerFocusBondingTransaction.IsValidHistory(saved) || !CharacterCareerPowerGovernance.Allows(saved))
                return new(CharacterCareerFocusBondingOutcome.Conflict);
            try { return new(CharacterCareerFocusBondingOutcome.Available,
                CharacterCareerFocusBondingTransaction.ReadEntries(saved.Document.Content), saved.ContentRevision); }
            catch (Exception e) when (e is InvalidDataException or System.Xml.XmlException or InvalidOperationException
                or FormatException or OverflowException or ArgumentException)
            { return new(CharacterCareerFocusBondingOutcome.Unavailable, Error: "focus_bonding_collection_unavailable"); }
        }
    }
    public CharacterCareerFocusBondingPreviewResult Preview(OwnerContextStamp owner, CharacterCareerFocusBondingRequest request)
    {
        if (!owner.IsValid || !CharacterCareerFocusBondingTransaction.IsValidRequest(request) || !owners.TryAcquire(owner, out var lease))
            return new(CharacterCareerFocusBondingOutcome.Conflict);
        using (lease)
        {
            if (lease.Stamp != owner || store is not ICharacterCareerFocusBondingAtomicCommitCapability atomic || !atomic.CareerFocusBondingGuardsInstalled)
                return new(CharacterCareerFocusBondingOutcome.Unavailable);
            if (store is ICharacterCareerFocusBondingCurrentEnvironmentCapability retained)
                return retained.PreviewCareerFocusBonding(owners, lease, request, sources);
            var read = Read(owner, request.WorkspaceId, lease);
            if (!read.Success || read.Value is not { } saved) return new(MapReadFailure(read).Outcome, Error: read.Error);
            var previous = CharacterCareerFocusBondingTransaction.Lookup(owner.Owner, saved, request.OperationId, new string('0', 64));
            if (previous.Outcome != CharacterCareerFocusBondingOutcome.NotFound) return new(previous.Outcome);
            if (!CharacterCareerFocusBondingTransaction.TryRead(owner, saved, request, sources, atomic.CareerFocusBondingRuntimeDigest,
                out var snapshot, out var error) || !CharacterCareerFocusBondingTransaction.TryPreview(snapshot!, request, out var preview))
                return new(CharacterCareerFocusBondingOutcome.Unavailable, Error: error);
            if (!CharacterCareerFocusBondingTransaction.TryBuild(owner, saved, snapshot!, preview!.Command with { ExplicitlyConfirmed = true }, out _, out _)
                || !CharacterCareerFocusBondingTransaction.IsSourceCurrent(owner, saved, sources, request, snapshot!))
                return new(CharacterCareerFocusBondingOutcome.Conflict, Error: "focus_bonding_source_changed");
            var again = Read(owner, request.WorkspaceId, lease);
            if (!again.Success || again.Value is not { } current || current.ContentRevision != saved.ContentRevision
                || current.SavedRevision != saved.SavedRevision || current.Document.Content != saved.Document.Content
                || current.Document.AuxiliaryStateDigest != saved.Document.AuxiliaryStateDigest || lease.Stamp != owner)
                return new(CharacterCareerFocusBondingOutcome.Conflict);
            return new(CharacterCareerFocusBondingOutcome.Available, preview, saved.ContentRevision);
        }
    }
    public CharacterCareerFocusBondingResult Commit(OwnerContextStamp owner, CharacterCareerFocusBondingCommand command,
        CancellationToken cancellationToken = default) => Dispatch(owner, command, false, cancellationToken);
    public CharacterCareerFocusBondingResult FinalizeDispatch(OwnerContextStamp owner, CharacterCareerFocusBondingCommand originalCommand,
        CancellationToken cancellationToken = default) => Dispatch(owner, originalCommand, true, cancellationToken);
    private CharacterCareerFocusBondingResult Dispatch(OwnerContextStamp owner, CharacterCareerFocusBondingCommand command,
        bool retire, CancellationToken token)
    {
        // Lookup may recover an old applied result after restart; only a new write requires the original live stamp.
        if (!owner.IsValid || !CharacterCareerFocusBondingTransaction.IsConfirmedCommand(command)) return new(CharacterCareerFocusBondingOutcome.Conflict);
        if (!owners.TryAcquire(owner, out var lease)) return new(CharacterCareerFocusBondingOutcome.Conflict);
        using (lease)
        {
            if (lease.Stamp != owner) return new(CharacterCareerFocusBondingOutcome.Conflict);
            if (play is not null && store is Chummer.Application.Session.ICharacterCareerFocusPlayCommitCapability7490 continuation
                && store is ICharacterCareerFocusBondingCurrentEnvironmentCapability originals)
                return play.WithOwnerLease(owners, owner, lease).CommitFocus7490(owner.Owner, command.Request.WorkspaceId.Value,
                    command, retire,
                    () => originals.LookupCareerFocusBonding(owners, lease, command.Request.WorkspaceId,
                        command.Request.OperationId, CharacterCareerFocusBondingTransaction.CommandDigest(command)),
                    admission => continuation.CommitFocusWithPlay7490(owners, lease, command, sources, retire, admission, token));
            if (store is ICharacterCareerFocusBondingCurrentEnvironmentCapability currentStore)
            {
                var priorCurrent = currentStore.LookupCareerFocusBonding(owners, lease, command.Request.WorkspaceId,
                    command.Request.OperationId, CharacterCareerFocusBondingTransaction.CommandDigest(command));
                if (priorCurrent.Outcome != CharacterCareerFocusBondingOutcome.NotFound) return priorCurrent;
                if (!(retire ? CharacterCareerFocusBondingTransaction.SameOwner(owner, command)
                    : CharacterCareerFocusBondingTransaction.OwnerMatches(owner, command)))
                    return new(CharacterCareerFocusBondingOutcome.Conflict);
                CharacterCareerFocusBondingResult result;
                try
                {
                    result = retire ? currentStore.FinalizeCareerFocusBondingDispatch(owners, lease, command, token)
                        : currentStore.CommitCareerFocusBonding(owners, lease, command, sources, token);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
                { result = new(CharacterCareerFocusBondingOutcome.UnknownOutcome); }
                // Lost-response observation is read-only and uses the original operation.
                var recovered = currentStore.LookupCareerFocusBonding(owners, lease, command.Request.WorkspaceId,
                    command.Request.OperationId, CharacterCareerFocusBondingTransaction.CommandDigest(command));
                if (recovered.Outcome == CharacterCareerFocusBondingOutcome.Replayed)
                    return recovered with { Outcome = result.Outcome == CharacterCareerFocusBondingOutcome.Applied
                        ? CharacterCareerFocusBondingOutcome.Applied : CharacterCareerFocusBondingOutcome.Replayed };
                if (recovered.Outcome != CharacterCareerFocusBondingOutcome.NotFound) return recovered;
                return result.Outcome is CharacterCareerFocusBondingOutcome.Conflict or CharacterCareerFocusBondingOutcome.Corrupt
                    or CharacterCareerFocusBondingOutcome.IdempotencyConflict or CharacterCareerFocusBondingOutcome.Canceled
                    or CharacterCareerFocusBondingOutcome.Unavailable or CharacterCareerFocusBondingOutcome.Missing
                    ? result with { Receipt = null } : new(CharacterCareerFocusBondingOutcome.UnknownOutcome);
            }
            var read = Read(owner, command.Request.WorkspaceId, lease);
            if (!read.Success || read.Value is not { } saved) return MapReadFailure(read);
            var prior = CharacterCareerFocusBondingTransaction.Lookup(owner.Owner, saved, command.Request.OperationId,
                CharacterCareerFocusBondingTransaction.CommandDigest(command));
            if (prior.Outcome != CharacterCareerFocusBondingOutcome.NotFound) return prior;
            if (!(retire ? CharacterCareerFocusBondingTransaction.SameOwner(owner, command) : CharacterCareerFocusBondingTransaction.OwnerMatches(owner, command))) return new(CharacterCareerFocusBondingOutcome.Conflict);
            if (store is not ICharacterCareerFocusBondingAtomicCommitCapability atomic || !atomic.CareerFocusBondingGuardsInstalled)
                return new(CharacterCareerFocusBondingOutcome.Unavailable);
            try
            {
                var result = retire ? atomic.FinalizeCareerFocusBondingDispatch(lease, command, token)
                    : atomic.CommitCareerFocusBonding(lease, command, sources, token);
                var after = Read(owner, command.Request.WorkspaceId, lease);
                if (!after.Success || after.Value is not { } observed) return new(CharacterCareerFocusBondingOutcome.UnknownOutcome);
                var recovered = CharacterCareerFocusBondingTransaction.Lookup(owner.Owner, observed, command.Request.OperationId,
                    CharacterCareerFocusBondingTransaction.CommandDigest(command));
                if (recovered.Outcome == CharacterCareerFocusBondingOutcome.Replayed)
                    return recovered with { Outcome = result.Outcome == CharacterCareerFocusBondingOutcome.Applied
                        ? CharacterCareerFocusBondingOutcome.Applied : CharacterCareerFocusBondingOutcome.Replayed };
                if (recovered.Outcome != CharacterCareerFocusBondingOutcome.NotFound) return recovered;
                return result.Outcome is CharacterCareerFocusBondingOutcome.Conflict or CharacterCareerFocusBondingOutcome.Corrupt
                    or CharacterCareerFocusBondingOutcome.IdempotencyConflict or CharacterCareerFocusBondingOutcome.Canceled
                    ? result with { Receipt = null } : new(CharacterCareerFocusBondingOutcome.UnknownOutcome);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            {
                // Adapter failures cannot prove non-application. Never resubmit an issued operation here.
                var after = Read(owner, command.Request.WorkspaceId, lease);
                if (after.Success && after.Value is { } observed)
                {
                    var result = CharacterCareerFocusBondingTransaction.Lookup(owner.Owner, observed, command.Request.OperationId,
                        CharacterCareerFocusBondingTransaction.CommandDigest(command));
                    if (result.Receipt is not null) return result;
                }
                return new(CharacterCareerFocusBondingOutcome.UnknownOutcome);
            }
        }
    }
    public CharacterCareerFocusBondingResult Lookup(OwnerContextStamp owner, CharacterWorkspaceId id, Guid operationId, string digest)
    {
        if (!owner.IsValid || operationId == Guid.Empty || !CharacterCareerFocusBondingTransaction.IsDigest(digest)
            || !owners.TryAcquire(owner, out var lease)) return new(CharacterCareerFocusBondingOutcome.Conflict);
        using (lease)
        {
            if (lease.Stamp != owner) return new(CharacterCareerFocusBondingOutcome.Conflict);
            if (store is ICharacterCareerFocusBondingCurrentEnvironmentCapability retained)
                return play is null ? retained.LookupCareerFocusBonding(owners, lease, id, operationId, digest)
                    : play.WithOwnerLease(owners, owner, lease).RecoverFocus7490(owner.Owner, id.Value, operationId, digest,
                        () => retained.LookupCareerFocusBonding(owners, lease, id, operationId, digest));
            var read = Read(owner, id, lease);
            return read.Success && read.Value is { } saved
                ? CharacterCareerFocusBondingTransaction.Lookup(owner.Owner, saved, operationId, digest) : MapReadFailure(read);
        }
    }
    private WorkspaceStoreReadResult Read(OwnerContextStamp owner, CharacterWorkspaceId id, IOwnerContextLease lease)
    {
        if (!CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id)) return new(WorkspaceOperationOutcome.Corrupt);
        try
        {
            var read = store is ICharacterCareerFocusBondingCurrentEnvironmentCapability retained
                ? retained.ReadCareerFocusBondingWorkspace(owners, lease, id)
                : owner.Owner.IsLocalSingleUser ? store.Get(id) : store.Get(owner.Owner, id);
            return read.Success && (read.Value is null || read.Value.Id != id || read.Value.ContentRevision <= 0
                || read.Value.SavedRevision <= 0 || read.Value.SavedRevision > read.Value.ContentRevision)
                ? new(WorkspaceOperationOutcome.Corrupt) : read;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(WorkspaceOperationOutcome.Unavailable); }
    }
    public static CharacterCareerFocusBondingResult MapReadFailure(WorkspaceStoreReadResult read)
        => new(read.Outcome switch { WorkspaceOperationOutcome.Missing => CharacterCareerFocusBondingOutcome.Missing,
            WorkspaceOperationOutcome.Corrupt => CharacterCareerFocusBondingOutcome.Corrupt,
            WorkspaceOperationOutcome.Conflict => CharacterCareerFocusBondingOutcome.Conflict,
            _ => CharacterCareerFocusBondingOutcome.Unavailable }, Error: read.Error);
}
