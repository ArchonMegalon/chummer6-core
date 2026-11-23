using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed class WorkspaceCharacterCareerReputationService(
    IWorkspaceStore store, ICharacterSourceDataResolver sourceResolver, IOwnerContextLeaseAccessor? owners = null) : ICharacterCareerReputationService
{
    // Preserve the existing two-argument ABI for independently compiled clients.
    public WorkspaceCharacterCareerReputationService(IWorkspaceStore store, ICharacterSourceDataResolver sourceResolver)
        : this(store, sourceResolver, null) { }

    private readonly IWorkspaceStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ICharacterSourceDataResolver _sources = sourceResolver ?? throw new ArgumentNullException(nameof(sourceResolver));

    public CharacterCareerReputationReadResult Read(CharacterWorkspaceId workspaceId)
    {
        if (owners is not null)
            return Current((capability, lease) => capability.ReadCareerReputation(owners, lease, workspaceId, _sources),
                error => new CharacterCareerReputationReadResult(CharacterCareerReputationOutcome.Unavailable, Error: error));
        var read = ReadStore(workspaceId);
        if (!read.Success || read.Value is not { } saved)
        {
            var failure = MapReadFailure(read);
            return new(failure.Outcome, Error: failure.Error);
        }
        if (!CharacterCareerReputationTransaction.IsValidHistory(saved))
            return new(CharacterCareerReputationOutcome.Corrupt, Error: "reputation_history_invalid");
        return CharacterCareerReputationProjector.TryRead(saved, _sources, out var snapshot, out string error)
            ? new(CharacterCareerReputationOutcome.Available, snapshot)
            : new(CharacterCareerReputationOutcome.Unavailable, Error: error);
    }

    public CharacterCareerReputationPreviewResult Preview(CharacterCareerReputationRequest request)
    {
        if (owners is not null)
            return Current((capability, lease) => capability.PreviewCareerReputation(owners, lease, request, _sources),
                error => new CharacterCareerReputationPreviewResult(CharacterCareerReputationOutcome.Unavailable, Error: error));
        if (!CharacterCareerReputationTransaction.IsValidRequest(request))
            return new(CharacterCareerReputationOutcome.Conflict, Error: "reputation_request_invalid");
        if (_store is not ICharacterCareerReputationAtomicCommitCapability atomic)
            return new(CharacterCareerReputationOutcome.Unavailable, Error: "reputation_atomic_commit_unavailable");
        var read = ReadStore(request.WorkspaceId);
        if (!read.Success || read.Value is not { } saved)
        {
            var failure = MapReadFailure(read);
            return new(failure.Outcome, Error: failure.Error);
        }
        if (!CharacterCareerReputationTransaction.IsValidHistory(saved))
            return new(CharacterCareerReputationOutcome.Corrupt, Error: "reputation_history_invalid");
        if (!CharacterCareerReputationProjector.TryRead(saved, _sources, out var snapshot, out string error))
            return new(CharacterCareerReputationOutcome.Unavailable, Error: error);
        if (!CharacterCareerReputationTransaction.TryPreview(snapshot!, request, atomic.CareerReputationRuntimeDigest, out var preview)
            // Prospective capacity/payload admission only. The simulated receipt
            // and confirmed copy never leave this preview; nothing is reserved.
            || !CharacterCareerReputationTransaction.TryBuild(saved, snapshot!,
                preview!.Command with { ExplicitlyConfirmed = true }, atomic.CareerReputationRuntimeDigest, out _, out _))
            return new(CharacterCareerReputationOutcome.Unavailable, CurrentWorkspaceRevision: saved.ContentRevision,
                Error: "reputation_quote_unavailable");
        return new(CharacterCareerReputationOutcome.Available, preview, saved.ContentRevision);
    }

    public CharacterCareerReputationResult Commit(CharacterCareerReputationCommand command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CharacterCareerReputationTransaction.IsConfirmedCommand(command))
            return new(CharacterCareerReputationOutcome.Conflict, Error: "reputation_command_invalid");
        string digest = CharacterCareerReputationTransaction.CommandDigest(command);
        var existing = Lookup(command.Request.WorkspaceId, command.Request.OperationId, digest);
        if (existing.Outcome != CharacterCareerReputationOutcome.NotFound) return existing;
        if (_store is not ICharacterCareerReputationAtomicCommitCapability atomic)
            return new(CharacterCareerReputationOutcome.Unavailable, Error: "reputation_atomic_commit_unavailable");
        try
        {
            var reported = owners is null
                ? atomic.CommitCareerReputation(command, _sources, cancellationToken)
                : Current((capability, lease) => capability.CommitCareerReputation(owners, lease, command, _sources, cancellationToken),
                    error => new CharacterCareerReputationResult(CharacterCareerReputationOutcome.Unavailable, Error: error));
            // An alternative host adapter must not manufacture a saved outcome.
            // Read the durable ledger independently, without the canceled token.
            var recovered = Lookup(command.Request.WorkspaceId, command.Request.OperationId, digest);
            if (recovered.Outcome == CharacterCareerReputationOutcome.Replayed)
                return recovered with { Outcome = reported.Outcome == CharacterCareerReputationOutcome.Applied
                    && reported.Receipt == recovered.Receipt
                    ? CharacterCareerReputationOutcome.Applied : CharacterCareerReputationOutcome.Replayed };
            if (recovered.Outcome != CharacterCareerReputationOutcome.NotFound) return recovered;
            return reported.Outcome is CharacterCareerReputationOutcome.Applied or CharacterCareerReputationOutcome.Replayed
                ? new(CharacterCareerReputationOutcome.Unavailable, CurrentWorkspaceRevision: recovered.CurrentWorkspaceRevision,
                    Error: "reputation_commit_not_observed")
                : reported with { Receipt = null, CurrentWorkspaceRevision = recovered.CurrentWorkspaceRevision };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                         or InvalidOperationException or OperationCanceledException)
        {
            var recovered = Lookup(command.Request.WorkspaceId, command.Request.OperationId, digest);
            return recovered.Outcome == CharacterCareerReputationOutcome.NotFound
                ? new(CharacterCareerReputationOutcome.Unavailable, CurrentWorkspaceRevision: recovered.CurrentWorkspaceRevision,
                    Error: "reputation_commit_result_unknown")
                : recovered;
        }
    }

    public CharacterCareerReputationResult Lookup(CharacterWorkspaceId workspaceId, Guid operationId, string commandDigest)
    {
        if (owners is not null)
            return Current((capability, lease) => capability.LookupCareerReputation(owners, lease, workspaceId, operationId, commandDigest),
                error => new CharacterCareerReputationResult(CharacterCareerReputationOutcome.Unavailable, Error: error));
        if (operationId == Guid.Empty || !CharacterCareerReputationTransaction.IsDigest(commandDigest))
            return new(CharacterCareerReputationOutcome.Corrupt, Error: "reputation_lookup_invalid");
        var read = ReadStore(workspaceId);
        return read.Success && read.Value is { } saved
            ? CharacterCareerReputationTransaction.Lookup(saved, operationId, commandDigest)
            : MapReadFailure(read);
    }

    private T Current<T>(Func<ICharacterCareerReputationCurrentCapability, IOwnerContextLease, T> action,
        Func<string, T> unavailable)
    {
        if (_store is not ICharacterCareerReputationCurrentCapability capability)
            return unavailable("reputation_current_capability_unavailable");
        try
        {
            var stamp = owners!.Capture();
            if (!stamp.IsValid || !owners.TryAcquire(stamp, out var lease) || lease is null)
                return unavailable("reputation_owner_lease_unavailable");
            using (lease)
            {
                if (lease.Stamp != stamp || owners.Capture() != stamp)
                    return unavailable("reputation_owner_changed");
                return action(capability, lease);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return unavailable("reputation_current_unavailable"); }
    }

    private WorkspaceStoreReadResult ReadStore(CharacterWorkspaceId id)
    {
        if (!CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id))
            return new(WorkspaceOperationOutcome.Corrupt, Error: "reputation_workspace_identity_invalid");
        try
        {
            var result = _store.Get(id);
            return result.Success && (result.Value!.Id != id || result.Value.ContentRevision <= 0
                || result.Value.SavedRevision < 0 || result.Value.SavedRevision > result.Value.ContentRevision)
                ? new(WorkspaceOperationOutcome.Corrupt, Error: "reputation_workspace_identity_mismatch") : result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(WorkspaceOperationOutcome.Unavailable, Error: "reputation_workspace_unavailable"); }
    }

    internal static CharacterCareerReputationResult MapReadFailure(WorkspaceStoreReadResult read)
        => new(read.Outcome switch
        {
            WorkspaceOperationOutcome.Missing => CharacterCareerReputationOutcome.Missing,
            WorkspaceOperationOutcome.Conflict => CharacterCareerReputationOutcome.Conflict,
            WorkspaceOperationOutcome.Corrupt => CharacterCareerReputationOutcome.Corrupt,
            _ => CharacterCareerReputationOutcome.Unavailable
        }, Error: read.Error);
}
