using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed class WorkspaceCharacterCareerGearSaleService(IWorkspaceStore store,
    ICharacterSourceDataResolver sources, IOwnerContextAccessor owners) : ICharacterCareerGearSaleService
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _issued = new(StringComparer.Ordinal);
    private static string IssuedKey(CharacterCareerGearSaleCommand command)
        => CharacterCareerGearSaleTransaction.CommandDigest(command with { ExplicitlyConfirmed = true });
    public CharacterCareerGearSaleCommand Confirm(CharacterCareerGearSaleCommand preview)
    {
        if (preview is null || preview.ExplicitlyConfirmed || !CharacterCareerGearSaleTransaction.IsConfirmedCommand(preview with { ExplicitlyConfirmed = true })
            || !_issued.ContainsKey(IssuedKey(preview)))
            throw new InvalidOperationException("The exact sale preview was not issued by this service.");
        return CharacterCareerGearSaleTransaction.FreezeConfirmedCommand(preview with { ExplicitlyConfirmed = true })!;
    }
    private bool Acquire(OwnerContextStamp expected, out IOwnerContextLease? lease)
    {
        lease = null;
        if (expected.IsValid && owners is IOwnerContextLeaseAccessor authority
            && authority.TryAcquire(expected, out lease) && lease is not null && lease.Stamp == expected) return true;
        lease?.Dispose(); lease = null; return false;
    }
    private ICharacterCareerGearSaleAtomicCommitCapability? Authority
        => store is ICharacterCareerGearSaleAtomicCommitCapability { CareerGearSaleGuardsInstalled: true } atomic ? atomic : null;

    public CharacterCareerGearSaleReadResult Read(OwnerContextStamp owner, CharacterWorkspaceId workspace)
    {
        if (!Acquire(owner, out var lease)) return new(CharacterCareerGearSaleOutcome.Conflict);
        using (lease) return store is FileWorkspaceStore files
            ? files.ReadCareerGearSale(owner.Owner, workspace, sources, owners, lease, owner)
            : Authority?.ReadCareerGearSale(owner.Owner, workspace, sources)
                ?? new(CharacterCareerGearSaleOutcome.Unavailable, Error: "gear_atomic_authority_unavailable");
    }
    public CharacterCareerGearSalePreviewResult Preview(OwnerContextStamp owner,
        CharacterCareerGearSaleBinding displayed, CharacterCareerGearSaleRequest request)
    {
        if (!Acquire(owner, out var lease)) return new(CharacterCareerGearSaleOutcome.Conflict);
        using (lease)
        {
            var result = store is FileWorkspaceStore files
                ? files.PreviewCareerGearSale(owner.Owner, displayed, request, sources, owners, lease, owner)
                : new CharacterCareerGearSalePreviewResult(CharacterCareerGearSaleOutcome.Unavailable);
            if (result.Command is { } preview && result.Outcome == CharacterCareerGearSaleOutcome.Available)
                _issued.TryAdd(IssuedKey(preview), 0);
            return result;
        }
    }

    public CharacterCareerGearSaleResult Commit(OwnerContextStamp owner, CharacterCareerGearSaleCommand command,
        CancellationToken cancellationToken = default)
    {
        if (CharacterCareerGearSaleTransaction.FreezeConfirmedCommand(command) is not { } frozen)
            return new(CharacterCareerGearSaleOutcome.Conflict);
        command = frozen;
        if (!CharacterCareerGearSaleTransaction.IsConfirmedCommand(command)
            || !CharacterCareerGearSaleTransaction.MatchesOwner(owner.Owner, command.Binding)
            || !Acquire(owner, out var lease)) return new(CharacterCareerGearSaleOutcome.Conflict);
        using (lease)
        {
            if (Authority is not { } atomic) return new(CharacterCareerGearSaleOutcome.Unavailable);
            string digest = CharacterCareerGearSaleTransaction.CommandDigest(command);
            CharacterCareerGearSaleResult Observe() => atomic.LookupCareerGearSale(owner.Owner,
                command.Request.WorkspaceId, command.Request.OperationId, digest, command.Binding.IncarnationId, command.Binding.SlotGeneration);
            // Both live and deleted original identities are checked before any reused-workspace dispatch.
            var existing = Observe();
            if (existing.Outcome != CharacterCareerGearSaleOutcome.NotFound) return existing;
            if (command.Binding.OwnerAuthorityInstanceId != owner.AuthorityInstanceId
                || command.Binding.OwnerTransitionRevision != owner.TransitionRevision
                || !_issued.TryRemove(IssuedKey(command), out _))
                return new(CharacterCareerGearSaleOutcome.Unavailable, Error: "gear_sale_original_requires_lookup");
            try
            {
                _ = store is FileWorkspaceStore files
                    ? files.CommitCareerGearSale(owner.Owner, command, sources, owners, lease, owner, cancellationToken)
                    : atomic.CommitCareerGearSale(owner.Owner, command, sources, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            { /* Only an independently observed original outcome can resolve custody. */ }
            var recovered = Observe();
            return recovered.Outcome == CharacterCareerGearSaleOutcome.NotFound
                ? new(CharacterCareerGearSaleOutcome.Unavailable, Error: "gear_original_result_unknown") : recovered;
        }
    }
    public CharacterCareerGearSaleResult CancelUnadmittedOriginal(OwnerContextStamp currentOwner,
        CharacterCareerGearSaleCommand originalCommand, CancellationToken cancellationToken = default)
    {
        if (CharacterCareerGearSaleTransaction.FreezeConfirmedCommand(originalCommand) is not { } command
            || !CharacterCareerGearSaleTransaction.MatchesOwner(currentOwner.Owner, command.Binding)
            || !Acquire(currentOwner, out var lease)) return new(CharacterCareerGearSaleOutcome.Conflict);
        using (lease)
        {
            if (Authority is not { } atomic) return new(CharacterCareerGearSaleOutcome.Unavailable);
            string digest = CharacterCareerGearSaleTransaction.CommandDigest(command);
            CharacterCareerGearSaleResult Observe() => atomic.LookupCareerGearSale(currentOwner.Owner,
                command.Request.WorkspaceId, command.Request.OperationId, digest,
                command.Binding.IncarnationId, command.Binding.SlotGeneration);
            try
            {
                var result = atomic.CancelUnadmittedCareerGearSale(currentOwner.Owner, command, cancellationToken);
                if (result.Outcome != CharacterCareerGearSaleOutcome.NeverApplied) return result;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            { /* Observe the original; neither an exception nor a return proves exclusion. */ }
            var recovered = Observe();
            if (recovered.Outcome == CharacterCareerGearSaleOutcome.NeverApplied
                && (recovered.Receipt is not null || recovered.Original?.Outcome != CharacterCareerGearSaleOutcome.NeverApplied
                    || !CharacterCareerGearSaleTransaction.MatchesOriginal(command, recovered.Original)))
                return new(CharacterCareerGearSaleOutcome.Unavailable, Error: "gear_original_result_unknown");
            return recovered.Outcome == CharacterCareerGearSaleOutcome.NotFound
                ? new(CharacterCareerGearSaleOutcome.Unavailable, Error: "gear_original_result_unknown") : recovered;
        }
    }

    public CharacterCareerGearSaleResult Lookup(OwnerContextStamp owner, string originalOwnerId, bool trustedLocalOwner,
        CharacterWorkspaceId workspace, Guid operationId, string commandDigest, string incarnationId, string slotGeneration)
    {
        if (owner.Owner.NormalizedValue != originalOwnerId || owner.Owner.IsLocalSingleUser != trustedLocalOwner
            || operationId == Guid.Empty || !CharacterCareerGearSaleTransaction.IsDigest(commandDigest)
            || !CharacterCareerGearSaleTransaction.IsGeneration(incarnationId)
            || !CharacterCareerGearSaleTransaction.IsGeneration(slotGeneration)
            || !Acquire(owner, out var lease)) return new(CharacterCareerGearSaleOutcome.Conflict);
        using (lease) return Authority?.LookupCareerGearSale(owner.Owner, workspace, operationId,
            commandDigest, incarnationId, slotGeneration) ?? new(CharacterCareerGearSaleOutcome.Unavailable);
    }
    internal static CharacterCareerGearSaleResult MapReadFailure(WorkspaceStoreReadResult read)
        => new(read.Outcome == WorkspaceOperationOutcome.Corrupt ? CharacterCareerGearSaleOutcome.Corrupt
            : CharacterCareerGearSaleOutcome.Unavailable, Error: read.Error);
}
