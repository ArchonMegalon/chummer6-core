using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed class WorkspaceCharacterCareerSpecializationService(IWorkspaceStore store,
    ICharacterSourceDataResolver sources, IOwnerContextAccessor owners) : ICharacterCareerSpecializationService
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _issued = new(StringComparer.Ordinal);
    private static string IssuedKey(CharacterCareerSpecializationCommand command)
        => CharacterCareerSpecializationTransaction.CommandDigest(command with { ExplicitlyConfirmed = true });
    public CharacterCareerSpecializationCommand Confirm(CharacterCareerSpecializationCommand preview)
    {
        if (preview is null || preview.ExplicitlyConfirmed || !CharacterCareerSpecializationTransaction.IsConfirmedCommand(preview with { ExplicitlyConfirmed = true })
            || !_issued.ContainsKey(IssuedKey(preview)))
            throw new InvalidOperationException("The exact specialization preview was not issued by this service.");
        return CharacterCareerSpecializationTransaction.FreezeConfirmedCommand(preview with { ExplicitlyConfirmed = true })!;
    }
    private bool Acquire(OwnerContextStamp expected, out IOwnerContextLease? lease)
    {
        lease = null;
        if (expected.IsValid && owners is IOwnerContextLeaseAccessor authority
            && authority.TryAcquire(expected, out lease) && lease is not null && lease.Stamp == expected) return true;
        lease?.Dispose(); lease = null; return false;
    }
    private ICharacterCareerSpecializationAtomicCommitCapability? Authority
        => store is ICharacterCareerSpecializationAtomicCommitCapability { CareerSpecializationGuardsInstalled: true } atomic ? atomic : null;

    public CharacterCareerSpecializationReadResult Read(OwnerContextStamp owner, CharacterWorkspaceId workspace)
    {
        if (!Acquire(owner, out var lease)) return new(CharacterCareerSpecializationOutcome.Conflict);
        using (lease) return store is FileWorkspaceStore files
            ? files.ReadCareerSpecialization(owner.Owner, workspace, sources, owners, lease, owner)
            : Authority?.ReadCareerSpecialization(owner.Owner, workspace, sources)
                ?? new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_atomic_authority_unavailable");
    }
    public CharacterCareerSpecializationPreviewResult Preview(OwnerContextStamp owner,
        CharacterCareerSpecializationBinding displayed, CharacterCareerSpecializationRequest request)
    {
        if (displayed is null || !CharacterCareerSpecializationAuthority.IsRequest(request)
            || !Acquire(owner, out var lease)) return new(CharacterCareerSpecializationOutcome.Conflict);
        using (lease)
        {
            var result = store is FileWorkspaceStore files
                ? files.PreviewCareerSpecialization(owner.Owner, displayed, request, sources, owners, lease, owner)
                : new CharacterCareerSpecializationPreviewResult(CharacterCareerSpecializationOutcome.Unavailable);
            if (result.Command is { } preview && result.Outcome == CharacterCareerSpecializationOutcome.Available)
                _issued.TryAdd(IssuedKey(preview), 0);
            return result;
        }
    }

    public CharacterCareerSpecializationResult Commit(OwnerContextStamp owner, CharacterCareerSpecializationCommand command,
        CancellationToken cancellationToken = default)
    {
        if (CharacterCareerSpecializationTransaction.FreezeConfirmedCommand(command) is not { } frozen)
            return new(CharacterCareerSpecializationOutcome.Conflict);
        command = frozen;
        if (!CharacterCareerSpecializationTransaction.IsConfirmedCommand(command)
            || !CharacterCareerSpecializationTransaction.MatchesOwner(owner.Owner, command.Binding)
            || !Acquire(owner, out var lease)) return new(CharacterCareerSpecializationOutcome.Conflict);
        using (lease)
        {
            if (Authority is not { } atomic) return new(CharacterCareerSpecializationOutcome.Unavailable);
            string digest = CharacterCareerSpecializationTransaction.CommandDigest(command);
            CharacterCareerSpecializationResult Observe() => atomic.LookupCareerSpecialization(owner.Owner,
                command.Request.WorkspaceId, command.Request.OperationId, digest, command.Binding.IncarnationId, command.Binding.SlotGeneration);
            // Both live and deleted original identities are checked before any reused-workspace dispatch.
            var existing = Observe();
            if (existing.Outcome != CharacterCareerSpecializationOutcome.NotFound) return existing;
            if (command.Binding.OwnerAuthorityInstanceId != owner.AuthorityInstanceId
                || command.Binding.OwnerTransitionRevision != owner.TransitionRevision
                || !_issued.TryRemove(IssuedKey(command), out _))
                return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_original_requires_lookup");
            try
            {
                _ = store is FileWorkspaceStore files
                    ? files.CommitCareerSpecialization(owner.Owner, command, sources, owners, lease, owner, cancellationToken)
                    : atomic.CommitCareerSpecialization(owner.Owner, command, sources, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            { /* Only an independently observed original outcome can resolve custody. */ }
            var recovered = Observe();
            return recovered.Outcome == CharacterCareerSpecializationOutcome.NotFound
                ? new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_original_result_unknown") : recovered;
        }
    }
    public CharacterCareerSpecializationResult CancelUnadmittedOriginal(OwnerContextStamp currentOwner,
        CharacterCareerSpecializationCommand originalCommand, CancellationToken cancellationToken = default)
    {
        if (CharacterCareerSpecializationTransaction.FreezeConfirmedCommand(originalCommand) is not { } command
            || !CharacterCareerSpecializationTransaction.MatchesOwner(currentOwner.Owner, command.Binding)
            || !Acquire(currentOwner, out var lease)) return new(CharacterCareerSpecializationOutcome.Conflict);
        using (lease)
        {
            if (Authority is not { } atomic) return new(CharacterCareerSpecializationOutcome.Unavailable);
            string digest = CharacterCareerSpecializationTransaction.CommandDigest(command);
            CharacterCareerSpecializationResult Observe() => atomic.LookupCareerSpecialization(currentOwner.Owner,
                command.Request.WorkspaceId, command.Request.OperationId, digest,
                command.Binding.IncarnationId, command.Binding.SlotGeneration);
            try
            {
                var result = store is FileWorkspaceStore files
                    ? files.CancelUnadmittedCareerSpecialization(currentOwner.Owner, command, owners, lease, currentOwner, cancellationToken)
                    : atomic.CancelUnadmittedCareerSpecialization(currentOwner.Owner, command, cancellationToken);
                if (result.Outcome != CharacterCareerSpecializationOutcome.NeverApplied) return result;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            { /* Observe the original; neither an exception nor a return proves exclusion. */ }
            var recovered = Observe();
            if (recovered.Outcome == CharacterCareerSpecializationOutcome.NeverApplied
                && (recovered.Receipt is not null || recovered.Original?.Outcome != CharacterCareerSpecializationOutcome.NeverApplied
                    || !CharacterCareerSpecializationTransaction.MatchesOriginal(command, recovered.Original)))
                return new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_original_result_unknown");
            return recovered.Outcome == CharacterCareerSpecializationOutcome.NotFound
                ? new(CharacterCareerSpecializationOutcome.Unavailable, Error: "specialization_original_result_unknown") : recovered;
        }
    }

    public CharacterCareerSpecializationResult Lookup(OwnerContextStamp owner, string originalOwnerId, bool trustedLocalOwner,
        CharacterWorkspaceId workspace, Guid operationId, string commandDigest, string incarnationId, string slotGeneration)
    {
        if (owner.Owner.NormalizedValue != originalOwnerId || owner.Owner.IsLocalSingleUser != trustedLocalOwner
            || operationId == Guid.Empty || !CharacterCareerSpecializationTransaction.IsDigest(commandDigest)
            || !CharacterCareerSpecializationTransaction.IsGeneration(incarnationId)
            || !CharacterCareerSpecializationTransaction.IsGeneration(slotGeneration)
            || !Acquire(owner, out var lease)) return new(CharacterCareerSpecializationOutcome.Conflict);
        using (lease) return Authority?.LookupCareerSpecialization(owner.Owner, workspace, operationId,
            commandDigest, incarnationId, slotGeneration) ?? new(CharacterCareerSpecializationOutcome.Unavailable);
    }
    internal static CharacterCareerSpecializationResult MapReadFailure(WorkspaceStoreReadResult read)
        => new(read.Outcome == WorkspaceOperationOutcome.Corrupt ? CharacterCareerSpecializationOutcome.Corrupt
            : CharacterCareerSpecializationOutcome.Unavailable, Error: read.Error);
}
