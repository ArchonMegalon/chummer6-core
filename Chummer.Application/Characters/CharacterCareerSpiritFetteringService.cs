using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterCareerSpiritFetteringService(IWorkspaceStore store, ICharacterSourceDataResolver sources,
    IOwnerContextLeaseAccessor owners) : ICharacterCareerSpiritFetteringService
{
    public CharacterCareerSpiritFetteringListResult List(OwnerContextStamp owner, CharacterWorkspaceId id, long expectedRevision)
    {
        if (!owner.IsValid || !owners.TryAcquire(owner, out var lease)) return new(CharacterCareerSpiritFetteringOutcome.Conflict);
        using (lease)
        {
            if (lease.Stamp != owner || store is not ICharacterCareerSpiritFetteringAtomicCommitCapability { CareerSpiritFetteringGuardsInstalled: true })
                return new(CharacterCareerSpiritFetteringOutcome.Unavailable);
            if (store is ICharacterCareerSpiritFetteringCurrentEnvironmentCapability retained)
                return retained.ListCareerSpiritFettering(owners, lease, id, expectedRevision);
            var read = Read(owner, id, lease);
            if (!read.Success || read.Value is not { } saved) return new(MapReadFailure(read).Outcome, Error: read.Error);
            if (saved.ContentRevision != expectedRevision || saved.SavedRevision != saved.ContentRevision
                || !CharacterCareerSpiritFetteringTransaction.IsValidHistory(saved) || !CharacterCareerPowerGovernance.Allows(saved))
                return new(CharacterCareerSpiritFetteringOutcome.Conflict);
            try { return new(CharacterCareerSpiritFetteringOutcome.Available,
                CharacterCareerSpiritFetteringTransaction.ReadEntries(saved.Document.Content), saved.ContentRevision); }
            catch (Exception e) when (e is InvalidDataException or System.Xml.XmlException or InvalidOperationException or FormatException)
            { return new(CharacterCareerSpiritFetteringOutcome.Unavailable, Error: "spirit_fettering_collection_unavailable"); }
        }
    }
    public CharacterCareerSpiritFetteringPreviewResult Preview(OwnerContextStamp owner, CharacterCareerSpiritFetteringRequest request)
    {
        if (!owner.IsValid || !CharacterCareerSpiritFetteringTransaction.IsValidRequest(request) || !owners.TryAcquire(owner, out var lease))
            return new(CharacterCareerSpiritFetteringOutcome.Conflict);
        using (lease)
        {
            if (lease.Stamp != owner || store is not ICharacterCareerSpiritFetteringAtomicCommitCapability atomic || !atomic.CareerSpiritFetteringGuardsInstalled)
                return new(CharacterCareerSpiritFetteringOutcome.Unavailable);
            if (store is ICharacterCareerSpiritFetteringCurrentEnvironmentCapability retained)
                return retained.PreviewCareerSpiritFettering(owners, lease, request, sources);
            var read = Read(owner, request.WorkspaceId, lease);
            if (!read.Success || read.Value is not { } saved) return new(MapReadFailure(read).Outcome, Error: read.Error);
            var previous = CharacterCareerSpiritFetteringTransaction.Lookup(owner.Owner, saved, request.OperationId, new string('0', 64));
            if (previous.Outcome != CharacterCareerSpiritFetteringOutcome.NotFound) return new(previous.Outcome);
            if (!CharacterCareerSpiritFetteringTransaction.TryRead(owner, saved, request, sources, atomic.CareerSpiritFetteringRuntimeDigest,
                out var snapshot, out var error) || !CharacterCareerSpiritFetteringTransaction.TryPreview(snapshot!, request, out var preview))
                return new(CharacterCareerSpiritFetteringOutcome.Unavailable, Error: error);
            if (!CharacterCareerSpiritFetteringTransaction.TryBuild(owner, saved, snapshot!, preview!.Command with { ExplicitlyConfirmed = true }, out _, out _)
                || !CharacterCareerSpiritFetteringTransaction.IsSourceCurrent(owner, saved, sources, request, snapshot!))
                return new(CharacterCareerSpiritFetteringOutcome.Conflict, Error: "spirit_fettering_source_changed");
            var again = Read(owner, request.WorkspaceId, lease);
            if (!again.Success || again.Value is not { } current || current.ContentRevision != saved.ContentRevision
                || current.SavedRevision != saved.SavedRevision || current.Document.Content != saved.Document.Content
                || current.Document.AuxiliaryStateDigest != saved.Document.AuxiliaryStateDigest || lease.Stamp != owner)
                return new(CharacterCareerSpiritFetteringOutcome.Conflict);
            return new(CharacterCareerSpiritFetteringOutcome.Available, preview, saved.ContentRevision);
        }
    }
    public CharacterCareerSpiritFetteringResult Commit(OwnerContextStamp owner, CharacterCareerSpiritFetteringCommand command,
        CancellationToken cancellationToken = default) => Dispatch(owner, command, false, cancellationToken);
    public CharacterCareerSpiritFetteringResult FinalizeDispatch(OwnerContextStamp owner, CharacterCareerSpiritFetteringCommand originalCommand,
        CancellationToken cancellationToken = default) => Dispatch(owner, originalCommand, true, cancellationToken);
    private CharacterCareerSpiritFetteringResult Dispatch(OwnerContextStamp owner, CharacterCareerSpiritFetteringCommand command,
        bool retire, CancellationToken token)
    {
        // Lookup may recover an old applied result after restart; only a new write requires the original live stamp.
        if (!owner.IsValid || !CharacterCareerSpiritFetteringTransaction.IsConfirmedCommand(command)) return new(CharacterCareerSpiritFetteringOutcome.Conflict);
        if (!owners.TryAcquire(owner, out var lease)) return new(CharacterCareerSpiritFetteringOutcome.Conflict);
        using (lease)
        {
            if (lease.Stamp != owner) return new(CharacterCareerSpiritFetteringOutcome.Conflict);
            var read = Read(owner, command.Request.WorkspaceId, lease);
            if (!read.Success || read.Value is not { } saved) return MapReadFailure(read);
            var prior = CharacterCareerSpiritFetteringTransaction.Lookup(owner.Owner, saved, command.Request.OperationId,
                CharacterCareerSpiritFetteringTransaction.CommandDigest(command));
            if (prior.Outcome != CharacterCareerSpiritFetteringOutcome.NotFound) return prior;
            if (!(retire ? CharacterCareerSpiritFetteringTransaction.SameOwner(owner, command) : CharacterCareerSpiritFetteringTransaction.OwnerMatches(owner, command))) return new(CharacterCareerSpiritFetteringOutcome.Conflict);
            if (store is not ICharacterCareerSpiritFetteringAtomicCommitCapability atomic || !atomic.CareerSpiritFetteringGuardsInstalled)
                return new(CharacterCareerSpiritFetteringOutcome.Unavailable);
            try
            {
                var result = store is ICharacterCareerSpiritFetteringCurrentEnvironmentCapability retained
                    ? retire ? retained.FinalizeCareerSpiritFetteringDispatch(owners, lease, command, token)
                        : retained.CommitCareerSpiritFettering(owners, lease, command, sources, token)
                    : retire ? atomic.FinalizeCareerSpiritFetteringDispatch(lease, command, token)
                        : atomic.CommitCareerSpiritFettering(lease, command, sources, token);
                var after = Read(owner, command.Request.WorkspaceId, lease);
                if (!after.Success || after.Value is not { } observed) return new(CharacterCareerSpiritFetteringOutcome.UnknownOutcome);
                var recovered = CharacterCareerSpiritFetteringTransaction.Lookup(owner.Owner, observed, command.Request.OperationId,
                    CharacterCareerSpiritFetteringTransaction.CommandDigest(command));
                if (recovered.Outcome == CharacterCareerSpiritFetteringOutcome.Replayed)
                    return recovered with { Outcome = result.Outcome == CharacterCareerSpiritFetteringOutcome.Applied
                        ? CharacterCareerSpiritFetteringOutcome.Applied : CharacterCareerSpiritFetteringOutcome.Replayed };
                if (recovered.Outcome != CharacterCareerSpiritFetteringOutcome.NotFound) return recovered;
                return result.Outcome is CharacterCareerSpiritFetteringOutcome.Conflict or CharacterCareerSpiritFetteringOutcome.Corrupt
                    or CharacterCareerSpiritFetteringOutcome.IdempotencyConflict or CharacterCareerSpiritFetteringOutcome.Canceled
                    ? result with { Receipt = null } : new(CharacterCareerSpiritFetteringOutcome.UnknownOutcome);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            {
                // Adapter failures cannot prove non-application. Never resubmit an issued operation here.
                var after = Read(owner, command.Request.WorkspaceId, lease);
                if (after.Success && after.Value is { } observed)
                {
                    var result = CharacterCareerSpiritFetteringTransaction.Lookup(owner.Owner, observed, command.Request.OperationId,
                        CharacterCareerSpiritFetteringTransaction.CommandDigest(command));
                    if (result.Receipt is not null) return result;
                }
                return new(CharacterCareerSpiritFetteringOutcome.UnknownOutcome);
            }
        }
    }
    public CharacterCareerSpiritFetteringResult Lookup(OwnerContextStamp owner, CharacterWorkspaceId id, Guid operationId, string digest)
    {
        if (!owner.IsValid || operationId == Guid.Empty || !CharacterCareerSpiritFetteringTransaction.IsDigest(digest)
            || !owners.TryAcquire(owner, out var lease)) return new(CharacterCareerSpiritFetteringOutcome.Conflict);
        using (lease)
        {
            if (lease.Stamp != owner) return new(CharacterCareerSpiritFetteringOutcome.Conflict);
            var read = Read(owner, id, lease);
            return read.Success && read.Value is { } saved
                ? CharacterCareerSpiritFetteringTransaction.Lookup(owner.Owner, saved, operationId, digest) : MapReadFailure(read);
        }
    }
    private WorkspaceStoreReadResult Read(OwnerContextStamp owner, CharacterWorkspaceId id, IOwnerContextLease lease)
    {
        if (!CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id)) return new(WorkspaceOperationOutcome.Corrupt);
        try
        {
            if (lease.Stamp != owner || owners.Capture() != owner) return new(WorkspaceOperationOutcome.Conflict);
            var read = store is ICharacterCareerSpiritFetteringCurrentEnvironmentCapability retained
                ? retained.ReadCareerSpiritFetteringWorkspace(owners, lease, id)
                : owner.Owner.IsLocalSingleUser ? store.Get(id) : store.Get(owner.Owner, id);
            return read.Success && (read.Value is null || read.Value.Id != id || read.Value.ContentRevision <= 0
                || read.Value.SavedRevision <= 0 || read.Value.SavedRevision > read.Value.ContentRevision)
                ? new(WorkspaceOperationOutcome.Corrupt) : read;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(WorkspaceOperationOutcome.Unavailable); }
    }
    public static CharacterCareerSpiritFetteringResult MapReadFailure(WorkspaceStoreReadResult read)
        => new(read.Outcome switch { WorkspaceOperationOutcome.Missing => CharacterCareerSpiritFetteringOutcome.Missing,
            WorkspaceOperationOutcome.Corrupt => CharacterCareerSpiritFetteringOutcome.Corrupt,
            WorkspaceOperationOutcome.Conflict => CharacterCareerSpiritFetteringOutcome.Conflict,
            _ => CharacterCareerSpiritFetteringOutcome.Unavailable }, Error: read.Error);
}
