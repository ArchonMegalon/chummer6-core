using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Rulesets.Sr6;

public sealed class Sr6CareerAttributeAdvancementService(IWorkspaceStore store, IOwnerContextAccessor ownerContext)
    : ISr6CareerAttributeAdvancementService
{
    public Sr6CareerAttributeResult Quote(OwnerContextStamp owner, CharacterWorkspaceId id, string attributeId)
        => WithOwner(owner, () =>
        {
            if (!CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id)) return Unavailable("sr6_attribute_workspace_invalid");
            if (store is not ISr6CareerAttributeAtomicCommitCapability atomic) return Unavailable("sr6_attribute_atomic_commit_unavailable");
            var read = owner.Owner.IsLocalSingleUser ? store.Get(id) : store.Get(owner.Owner, id);
            return read.Success && read.Value is { } saved
                ? Sr6CareerAttributeAdvancementRules.Quote(saved, owner.Owner, attributeId, atomic.Sr6CareerAttributeRuntimeDigest)
                : Unavailable("sr6_attribute_workspace_unavailable");
        });

    public Sr6CareerAttributeResult Lookup(OwnerContextStamp owner, Sr6CareerAttributeCommand command)
        => WithOwner(owner, () => LookupCore(owner, command));

    public Sr6CareerAttributeResult Advance(OwnerContextStamp owner, Sr6CareerAttributeCommand command)
        => WithOwner(owner, () =>
        {
            var prior = LookupCore(owner, command);
            if (prior.Outcome != Sr6CareerAttributeOutcome.NotFound) return prior;
            if (store is not ISr6CareerAttributeAtomicCommitCapability atomic) return Unavailable("sr6_attribute_atomic_commit_unavailable");
            try { return atomic.CommitSr6CareerAttribute(owner.Owner, command); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                var recovered = LookupCore(owner, command);
                return recovered.Outcome == Sr6CareerAttributeOutcome.NotFound
                    ? Unavailable("sr6_attribute_commit_unknown_retry_same_operation") : recovered;
            }
        });

    private Sr6CareerAttributeResult LookupCore(OwnerContextStamp owner, Sr6CareerAttributeCommand command)
    {
        if (!Sr6CareerAttributeIntegrity.IsConfirmed(command) || command.Quote.OwnerId != owner.Owner.NormalizedValue)
            return new(Sr6CareerAttributeOutcome.Conflict, Error: "sr6_attribute_command_invalid");
        var id = command.Quote.WorkspaceId;
        var read = owner.Owner.IsLocalSingleUser ? store.Get(id) : store.Get(owner.Owner, id);
        return read.Success && read.Value is { } saved ? Sr6CareerAttributeIntegrity.Lookup(saved, command)
            : Unavailable("sr6_attribute_workspace_unavailable");
    }

    private Sr6CareerAttributeResult WithOwner(OwnerContextStamp expected, Func<Sr6CareerAttributeResult> action)
    {
        if (!expected.IsValid || ownerContext is not IOwnerContextLeaseAccessor accessor
            || !accessor.TryAcquire(expected, out var lease)) return Unavailable("sr6_attribute_owner_unavailable");
        using (lease)
        {
            try { return action(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            { return Unavailable("sr6_attribute_workspace_unavailable"); }
        }
    }

    private static Sr6CareerAttributeResult Unavailable(string error) => new(Sr6CareerAttributeOutcome.Unavailable, Error: error);
}
