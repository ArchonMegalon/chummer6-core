using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterCareerFreeSpriteConversionService(IWorkspaceStore store, ICharacterSourceDataResolver sources,
    IOwnerContextLeaseAccessor owners) : ICharacterCareerFreeSpriteConversionService
{
    public CharacterCareerFreeSpriteConversionPreviewResult Preview(OwnerContextStamp owner, CharacterCareerFreeSpriteConversionRequest request)
    {
        if (!owner.IsValid || !CharacterCareerFreeSpriteConversionTransaction.IsValidRequest(request) || !owners.TryAcquire(owner, out var lease))
            return new(CharacterCareerFreeSpriteConversionOutcome.Conflict);
        using (lease)
        {
            if (lease.Stamp != owner || store is not ICharacterCareerFreeSpriteConversionAtomicCommitCapability atomic || !atomic.CareerFreeSpriteConversionGuardsInstalled)
                return new(CharacterCareerFreeSpriteConversionOutcome.Unavailable);
            if (store is ICharacterCareerFreeSpriteConversionCurrentEnvironmentCapability retained)
                return retained.PreviewCareerFreeSpriteConversion(owners, lease, request, sources);
            var read = Read(owner, request.WorkspaceId, lease);
            if (!read.Success || read.Value is not { } saved) return new(MapReadFailure(read).Outcome, Error: read.Error);
            var previous = CharacterCareerFreeSpriteConversionTransaction.Lookup(owner.Owner, saved, request.OperationId, new string('0', 64));
            if (previous.Outcome != CharacterCareerFreeSpriteConversionOutcome.NotFound) return new(previous.Outcome);
            if (!CharacterCareerFreeSpriteConversionTransaction.TryRead(owner, saved, request, sources, atomic.CareerFreeSpriteConversionRuntimeDigest,
                out var snapshot, out var error) || !CharacterCareerFreeSpriteConversionTransaction.TryPreview(snapshot!, request, out var preview))
                return new(CharacterCareerFreeSpriteConversionOutcome.Unavailable, Error: error);
            if (!CharacterCareerFreeSpriteConversionTransaction.TryBuild(owner, saved, snapshot!, preview!.Command with { ExplicitlyConfirmed = true }, out _, out _)
                || !CharacterCareerFreeSpriteConversionTransaction.IsSourceCurrent(owner, saved, sources, request, snapshot!))
                return new(CharacterCareerFreeSpriteConversionOutcome.Conflict, Error: "free_sprite_conversion_source_changed");
            var again = Read(owner, request.WorkspaceId, lease);
            if (!again.Success || again.Value is not { } current || current.ContentRevision != saved.ContentRevision
                || current.SavedRevision != saved.SavedRevision || current.Document.Content != saved.Document.Content
                || current.Document.AuxiliaryStateDigest != saved.Document.AuxiliaryStateDigest || lease.Stamp != owner)
                return new(CharacterCareerFreeSpriteConversionOutcome.Conflict);
            return new(CharacterCareerFreeSpriteConversionOutcome.Available, preview, saved.ContentRevision);
        }
    }
    public CharacterCareerFreeSpriteConversionResult Commit(OwnerContextStamp owner, CharacterCareerFreeSpriteConversionCommand command,
        CancellationToken token = default)
    {
        // Lookup may recover an old applied result after restart; only a new write requires the original live stamp.
        if (!owner.IsValid || !CharacterCareerFreeSpriteConversionTransaction.IsConfirmedCommand(command)) return new(CharacterCareerFreeSpriteConversionOutcome.Conflict);
        if (!owners.TryAcquire(owner, out var lease)) return new(CharacterCareerFreeSpriteConversionOutcome.Conflict);
        using (lease)
        {
            if (lease.Stamp != owner) return new(CharacterCareerFreeSpriteConversionOutcome.Conflict);
            if (store is ICharacterCareerFreeSpriteConversionReadbackCommit trusted)
                return CommitWithReadback(trusted, owner, lease, command, token);
            var read = Read(owner, command.Request.WorkspaceId, lease);
            if (!read.Success || read.Value is not { } saved) return MapReadFailure(read);
            var prior = CharacterCareerFreeSpriteConversionTransaction.Lookup(owner.Owner, saved, command.Request.OperationId,
                CharacterCareerFreeSpriteConversionTransaction.CommandDigest(command));
            if (prior.Outcome != CharacterCareerFreeSpriteConversionOutcome.NotFound) return prior;
            if (!CharacterCareerFreeSpriteConversionTransaction.OwnerMatches(owner, command)) return new(CharacterCareerFreeSpriteConversionOutcome.Conflict);
            if (store is not ICharacterCareerFreeSpriteConversionAtomicCommitCapability atomic || !atomic.CareerFreeSpriteConversionGuardsInstalled)
                return new(CharacterCareerFreeSpriteConversionOutcome.Unavailable);
            try
            {
                var result = store is ICharacterCareerFreeSpriteConversionCurrentEnvironmentCapability retained
                    ? retained.CommitCareerFreeSpriteConversion(owners, lease, command, sources, token)
                    : atomic.CommitCareerFreeSpriteConversion(lease, command, sources, token);
                var after = Read(owner, command.Request.WorkspaceId, lease);
                if (!after.Success || after.Value is not { } observed) return new(CharacterCareerFreeSpriteConversionOutcome.UnknownOutcome);
                var recovered = CharacterCareerFreeSpriteConversionTransaction.Lookup(owner.Owner, observed, command.Request.OperationId,
                    CharacterCareerFreeSpriteConversionTransaction.CommandDigest(command));
                if (recovered.Outcome == CharacterCareerFreeSpriteConversionOutcome.Replayed)
                    return recovered with { Outcome = result.Outcome == CharacterCareerFreeSpriteConversionOutcome.Applied
                        ? CharacterCareerFreeSpriteConversionOutcome.Applied : CharacterCareerFreeSpriteConversionOutcome.Replayed };
                if (recovered.Outcome != CharacterCareerFreeSpriteConversionOutcome.NotFound) return recovered;
                return result.Outcome is CharacterCareerFreeSpriteConversionOutcome.Conflict or CharacterCareerFreeSpriteConversionOutcome.Corrupt
                    or CharacterCareerFreeSpriteConversionOutcome.IdempotencyConflict or CharacterCareerFreeSpriteConversionOutcome.Canceled
                    ? result with { Receipt = null } : new(CharacterCareerFreeSpriteConversionOutcome.UnknownOutcome);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            {
                // Adapter failures cannot prove non-application. Never resubmit an issued operation here.
                var after = Read(owner, command.Request.WorkspaceId, lease);
                if (after.Success && after.Value is { } observed)
                {
                    var result = CharacterCareerFreeSpriteConversionTransaction.Lookup(owner.Owner, observed, command.Request.OperationId,
                        CharacterCareerFreeSpriteConversionTransaction.CommandDigest(command));
                    if (result.Receipt is not null) return result;
                }
                return new(CharacterCareerFreeSpriteConversionOutcome.UnknownOutcome);
            }
        }
    }
    private CharacterCareerFreeSpriteConversionResult CommitWithReadback(ICharacterCareerFreeSpriteConversionReadbackCommit trusted,
        OwnerContextStamp owner, IOwnerContextLease lease, CharacterCareerFreeSpriteConversionCommand command, CancellationToken token)
    {
        try
        {
            var completed = trusted.CommitCareerFreeSpriteConversion(owners, lease, command, sources, token);
            if (owners.Capture() == owner && lease.Stamp == owner)
            {
                // The internal store alone can prove a refusal or replay before writer entry.
                // Replay deliberately precedes the original command stamp and cancellation checks.
                if (completed.Phase == CharacterCareerFreeSpriteConversionCommitPhase.BeforeConversionWriter)
                    return completed.Result;
                if (MatchesCompletedReadback(command, completed.Result) && !token.IsCancellationRequested)
                    return completed.Result;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            // No reliable returned phase: reconcile read-only, never infer nonapplication.
        }
        // The store has released its workspace operation lease before this fallback.
        // After writer entry only an authorized matching receipt can resolve uncertainty.
        try
        {
            var after = Read(owner, command.Request.WorkspaceId, lease);
            if (after.Success && after.Value is { } observed)
            {
                var recovered = CharacterCareerFreeSpriteConversionTransaction.Lookup(owner.Owner, observed, command.Request.OperationId,
                    CharacterCareerFreeSpriteConversionTransaction.CommandDigest(command));
                if (recovered.Outcome == CharacterCareerFreeSpriteConversionOutcome.Replayed
                    && owners.Capture() == owner && lease.Stamp == owner)
                    return recovered;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException) { }
        return new(CharacterCareerFreeSpriteConversionOutcome.UnknownOutcome);
    }
    private static bool MatchesCompletedReadback(CharacterCareerFreeSpriteConversionCommand command,
        CharacterCareerFreeSpriteConversionResult result)
        => result.Outcome == CharacterCareerFreeSpriteConversionOutcome.Applied && result.Error is null
            && result.Receipt is { } receipt && CharacterCareerFreeSpriteConversionTransaction.IsCoherent(receipt)
            && receipt.Command == command
            && receipt.CommandDigest == CharacterCareerFreeSpriteConversionTransaction.CommandDigest(command)
            && result.CurrentWorkspaceRevision == receipt.CommittedWorkspaceRevision;
    public CharacterCareerFreeSpriteConversionResult Lookup(OwnerContextStamp owner, CharacterWorkspaceId id, Guid operationId, string digest)
    {
        if (!owner.IsValid || operationId == Guid.Empty || !CharacterCareerFreeSpriteConversionTransaction.IsDigest(digest)
            || !owners.TryAcquire(owner, out var lease)) return new(CharacterCareerFreeSpriteConversionOutcome.Conflict);
        using (lease)
        {
            if (lease.Stamp != owner) return new(CharacterCareerFreeSpriteConversionOutcome.Conflict);
            var read = Read(owner, id, lease);
            return read.Success && read.Value is { } saved
                ? CharacterCareerFreeSpriteConversionTransaction.Lookup(owner.Owner, saved, operationId, digest) : MapReadFailure(read);
        }
    }
    private WorkspaceStoreReadResult Read(OwnerContextStamp owner, CharacterWorkspaceId id, IOwnerContextLease lease)
    {
        if (!CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id)) return new(WorkspaceOperationOutcome.Corrupt);
        try
        {
            if (lease.Stamp != owner || owners.Capture() != owner) return new(WorkspaceOperationOutcome.Conflict);
            var read = store is ICharacterCareerFreeSpriteConversionCurrentEnvironmentCapability retained
                ? retained.ReadCareerFreeSpriteConversionWorkspace(owners, lease, id)
                : owner.Owner.IsLocalSingleUser ? store.Get(id) : store.Get(owner.Owner, id);
            return read.Success && (read.Value is null || read.Value.Id != id || read.Value.ContentRevision <= 0
                || read.Value.SavedRevision <= 0 || read.Value.SavedRevision > read.Value.ContentRevision)
                ? new(WorkspaceOperationOutcome.Corrupt) : read;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(WorkspaceOperationOutcome.Unavailable); }
    }
    public static CharacterCareerFreeSpriteConversionResult MapReadFailure(WorkspaceStoreReadResult read)
        => new(read.Outcome switch { WorkspaceOperationOutcome.Missing => CharacterCareerFreeSpriteConversionOutcome.Missing,
            WorkspaceOperationOutcome.Corrupt => CharacterCareerFreeSpriteConversionOutcome.Corrupt,
            WorkspaceOperationOutcome.Conflict => CharacterCareerFreeSpriteConversionOutcome.Conflict,
            _ => CharacterCareerFreeSpriteConversionOutcome.Unavailable }, Error: read.Error);
}
