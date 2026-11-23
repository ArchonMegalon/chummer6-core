using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed class WorkspaceCharacterCareerGearEquipmentService(IWorkspaceStore store,
    ICharacterSourceDataResolver sources, IOwnerContextAccessor owners) : ICharacterCareerGearEquipmentService
{
    private bool Acquire(OwnerContextStamp expected, out IOwnerContextLease? lease)
    {
        lease = null;
        if (expected.IsValid && owners is IOwnerContextLeaseAccessor authority
            && authority.TryAcquire(expected, out lease) && lease is not null && lease.Stamp == expected) return true;
        lease?.Dispose(); lease = null; return false;
    }
    private ICharacterCareerGearEquipmentAtomicCommitCapability? Authority
        => store is ICharacterCareerGearEquipmentAtomicCommitCapability { CareerGearEquipmentGuardsInstalled: true } atomic ? atomic : null;

    public CharacterCareerGearEquipmentReadResult Read(OwnerContextStamp owner, CharacterWorkspaceId workspace)
        => Read(owner, workspace, CancellationToken.None);

    public CharacterCareerGearEquipmentReadResult Read(OwnerContextStamp owner, CharacterWorkspaceId workspace,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Acquire(owner, out var lease)) return new(CharacterCareerGearEquipmentOutcome.Conflict);
        using (lease)
        {
            var result = store is FileWorkspaceStore files
                ? files.ReadCareerGearEquipment(owner.Owner, workspace, sources, owners, lease, owner, cancellationToken)
                : Authority?.ReadCareerGearEquipment(owner.Owner, workspace, sources)
                    ?? new(CharacterCareerGearEquipmentOutcome.Unavailable, Error: "gear_atomic_authority_unavailable");
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
    }
    public CharacterCareerGearEquipmentPreviewResult Preview(OwnerContextStamp owner,
        CharacterCareerGearEquipmentBinding displayed, CharacterCareerGearEquipmentRequest request)
    {
        if (!Acquire(owner, out var lease)) return new(CharacterCareerGearEquipmentOutcome.Conflict);
        using (lease) return store is FileWorkspaceStore files
            ? files.PreviewCareerGearEquipment(owner.Owner, displayed, request, sources, owners, lease, owner)
            : Authority?.PreviewCareerGearEquipment(owner.Owner, displayed, request, sources)
                ?? new(CharacterCareerGearEquipmentOutcome.Unavailable);
    }
    public CharacterCareerGearEquipmentResult Commit(OwnerContextStamp owner, CharacterCareerGearEquipmentCommand command,
        CancellationToken cancellationToken = default)
    {
        if (CharacterCareerGearEquipmentTransaction.FreezeConfirmedCommand(command) is not { } frozen)
            return new(CharacterCareerGearEquipmentOutcome.Conflict);
        command = frozen;
        if (!CharacterCareerGearEquipmentTransaction.IsConfirmedCommand(command)
            || !CharacterCareerGearEquipmentTransaction.MatchesOwner(owner.Owner, command.Binding)
            || !Acquire(owner, out var lease)) return new(CharacterCareerGearEquipmentOutcome.Conflict);
        using (lease)
        {
            if (Authority is not { } atomic) return new(CharacterCareerGearEquipmentOutcome.Unavailable);
            string digest = CharacterCareerGearEquipmentTransaction.CommandDigest(command);
            CharacterCareerGearEquipmentResult Observe() => atomic.LookupCareerGearEquipment(owner.Owner,
                command.Request.WorkspaceId, command.Request.OperationId, digest, command.Binding.IncarnationId, command.Binding.SlotGeneration);
            // Both live and deleted original identities are checked before any reused-workspace dispatch.
            var existing = Observe();
            if (existing.Outcome != CharacterCareerGearEquipmentOutcome.NotFound) return existing;
            try
            {
                _ = store is FileWorkspaceStore files
                    ? files.CommitCareerGearEquipment(owner.Owner, command, sources, owners, lease, owner, cancellationToken)
                    : atomic.CommitCareerGearEquipment(owner.Owner, command, sources, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            { /* Only an independently observed original outcome can resolve custody. */ }
            var recovered = Observe();
            return recovered.Outcome == CharacterCareerGearEquipmentOutcome.NotFound
                ? new(CharacterCareerGearEquipmentOutcome.Unavailable, Error: "gear_original_result_unknown") : recovered;
        }
    }
    public CharacterCareerGearEquipmentResult CancelUnadmittedOriginal(OwnerContextStamp currentOwner,
        CharacterCareerGearEquipmentCommand originalCommand, CancellationToken cancellationToken = default)
    {
        if (CharacterCareerGearEquipmentTransaction.FreezeConfirmedCommand(originalCommand) is not { } command
            || !CharacterCareerGearEquipmentTransaction.MatchesOwner(currentOwner.Owner, command.Binding)
            || !Acquire(currentOwner, out var lease)) return new(CharacterCareerGearEquipmentOutcome.Conflict);
        using (lease)
        {
            if (Authority is not { } atomic) return new(CharacterCareerGearEquipmentOutcome.Unavailable);
            string digest = CharacterCareerGearEquipmentTransaction.CommandDigest(command);
            CharacterCareerGearEquipmentResult Observe() => atomic.LookupCareerGearEquipment(currentOwner.Owner,
                command.Request.WorkspaceId, command.Request.OperationId, digest,
                command.Binding.IncarnationId, command.Binding.SlotGeneration);
            try
            {
                var result = atomic.CancelUnadmittedCareerGearEquipment(currentOwner.Owner, command, cancellationToken);
                if (result.Outcome != CharacterCareerGearEquipmentOutcome.NeverApplied) return result;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            { /* Observe the original; neither an exception nor a return proves exclusion. */ }
            var recovered = Observe();
            if (recovered.Outcome == CharacterCareerGearEquipmentOutcome.NeverApplied
                && (recovered.Receipt is not null || recovered.Original?.Outcome != CharacterCareerGearEquipmentOutcome.NeverApplied
                    || !CharacterCareerGearEquipmentTransaction.MatchesOriginal(command, recovered.Original)))
                return new(CharacterCareerGearEquipmentOutcome.Unavailable, Error: "gear_original_result_unknown");
            return recovered.Outcome == CharacterCareerGearEquipmentOutcome.NotFound
                ? new(CharacterCareerGearEquipmentOutcome.Unavailable, Error: "gear_original_result_unknown") : recovered;
        }
    }

    public CharacterCareerGearEquipmentResult Lookup(OwnerContextStamp owner, string originalOwnerId, bool trustedLocalOwner,
        CharacterWorkspaceId workspace, Guid operationId, string commandDigest, string incarnationId, string slotGeneration)
    {
        if (owner.Owner.NormalizedValue != originalOwnerId || owner.Owner.IsLocalSingleUser != trustedLocalOwner
            || operationId == Guid.Empty || !CharacterCareerGearEquipmentTransaction.IsDigest(commandDigest)
            || !CharacterCareerGearEquipmentTransaction.IsGeneration(incarnationId)
            || !CharacterCareerGearEquipmentTransaction.IsGeneration(slotGeneration)
            || !Acquire(owner, out var lease)) return new(CharacterCareerGearEquipmentOutcome.Conflict);
        using (lease) return Authority?.LookupCareerGearEquipment(owner.Owner, workspace, operationId,
            commandDigest, incarnationId, slotGeneration) ?? new(CharacterCareerGearEquipmentOutcome.Unavailable);
    }
    internal static CharacterCareerGearEquipmentResult MapReadFailure(WorkspaceStoreReadResult read)
        => new(read.Outcome == WorkspaceOperationOutcome.Corrupt ? CharacterCareerGearEquipmentOutcome.Corrupt
            : CharacterCareerGearEquipmentOutcome.Unavailable, Error: read.Error);
}
