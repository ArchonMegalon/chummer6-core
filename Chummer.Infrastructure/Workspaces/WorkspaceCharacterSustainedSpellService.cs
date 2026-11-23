using System.Xml;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Tx = Chummer.Application.Characters.CharacterSustainedSpellTransaction;

namespace Chummer.Infrastructure.Workspaces;

public sealed class WorkspaceCharacterSustainedSpellService(IWorkspaceStore store,
    ICharacterSourceDataResolver sources, IOwnerContextAccessor owners) : ICharacterSustainedSpellService
{
    private CharacterSustainedSpellResult WithOwner(OwnerContextStamp expected,
        Func<FileWorkspaceStore, IOwnerContextLease, CharacterSustainedSpellResult> action,
        CharacterSustainedSpellOutcome failure = CharacterSustainedSpellOutcome.Unavailable)
    {
        if (store is not FileWorkspaceStore files || owners is not IOwnerContextLeaseAccessor authority)
            return new(CharacterSustainedSpellOutcome.Unavailable);
        if (!expected.IsValid || !Tx.ValidOwner(expected.Owner) || !authority.TryAcquire(expected, out var lease))
            return new(CharacterSustainedSpellOutcome.Conflict);
        using (lease)
        {
            if (lease is null || lease.Stamp != expected || authority.Capture() != expected)
                return new(CharacterSustainedSpellOutcome.Conflict);
            try
            {
                var result = action(files, lease);
                return authority.Capture() == expected && lease.Stamp == expected
                    ? result : new(CharacterSustainedSpellOutcome.Conflict);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                or ArgumentException or FormatException or OverflowException or XmlException or JsonException or OperationCanceledException)
            { return new(failure); }
        }
    }
    public CharacterSustainedSpellResult Read(OwnerContextStamp owner, CharacterWorkspaceId workspace)
        => WithOwner(owner, (files, lease) => files.ReadSustainedSpell(owner, owners, lease, workspace, sources));
    public CharacterSustainedSpellResult Preview(OwnerContextStamp owner, CharacterSustainedSpellBinding displayed,
        Guid operationId, CharacterSustainedSpellPreparationRequest request)
    {
        if (displayed?.Preparation is null || request is null || operationId == Guid.Empty)
            return new(CharacterSustainedSpellOutcome.Conflict);
        return WithOwner(owner, (files, lease) => files.ReadSustainedSpell(owner, owners, lease,
            displayed.Preparation.WorkspaceId, sources, displayed, operationId, request));
    }
    public CharacterSustainedSpellResult Lookup(OwnerContextStamp currentOwner, CharacterSustainedSpellLookup original)
        => WithOwner(currentOwner, (files, _) => files.LookupSustainedSpell(currentOwner.Owner, original));
    public CharacterSustainedSpellResult Cancel(OwnerContextStamp currentOwner,
        CharacterSustainedSpellCommand originalCommand, string originalCommandDigest,
        CancellationToken cancellationToken = default)
    {
        if (!Tx.ValidCommand(originalCommand) || Tx.CommandDigest(originalCommand) != originalCommandDigest
            || !Tx.MatchesOwner(currentOwner.Owner, originalCommand.Binding.Preparation))
            return new(CharacterSustainedSpellOutcome.Conflict);
        return WithOwner(currentOwner, (files, lease) =>
        {
            try { _ = files.CancelSustainedSpell(currentOwner, owners, lease,
                originalCommand, originalCommandDigest, cancellationToken); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                or ArgumentException or FormatException or OverflowException or XmlException or JsonException or OperationCanceledException)
            { /* Never replay a cancellation publication after an uncertain result. */ }
            var found = files.LookupSustainedSpell(currentOwner.Owner, Tx.Original(originalCommand));
            return found.Outcome == CharacterSustainedSpellOutcome.NotFound
                ? new(CharacterSustainedSpellOutcome.Unknown) : found;
        }, CharacterSustainedSpellOutcome.Unknown);
    }
    public CharacterSustainedSpellResult Confirm(OwnerContextStamp owner, CharacterSustainedSpellCommand command,
        string originalCommandDigest, CancellationToken cancellationToken = default)
    {
        if (!Tx.ValidCommand(command) || Tx.CommandDigest(command) != originalCommandDigest
            || !Tx.MatchesOwner(owner.Owner, command.Binding.Preparation)) return new(CharacterSustainedSpellOutcome.Conflict);
        return WithOwner(owner, (files, lease) =>
        {
            // Always read the exact original first. A retry never applies against
            // a later revision, owner session, source closure, runtime or slot.
            var original = Tx.Original(command);
            var prior = files.LookupSustainedSpell(owner.Owner, original);
            if (prior.Outcome != CharacterSustainedSpellOutcome.NotFound) return prior;
            CharacterSustainedSpellResult? result = null;
            try { result = files.ConfirmSustainedSpell(owner, owners, lease, command, originalCommandDigest, sources, cancellationToken); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException
                or ArgumentException or FormatException or OverflowException or XmlException or JsonException or OperationCanceledException)
            { /* The only recovery action is a read of the original receipt. */ }
            var found = files.LookupSustainedSpell(owner.Owner, original);
            if (found.Outcome == CharacterSustainedSpellOutcome.Cancelled) return found;
            if (found.Outcome == CharacterSustainedSpellOutcome.Recorded)
                return found with { Outcome = result?.Outcome == CharacterSustainedSpellOutcome.Applied
                    ? CharacterSustainedSpellOutcome.Applied : CharacterSustainedSpellOutcome.Recorded };
            return found.Outcome == CharacterSustainedSpellOutcome.NotFound
                && result is { Outcome: CharacterSustainedSpellOutcome.Conflict or CharacterSustainedSpellOutcome.Unavailable }
                    ? result : new(CharacterSustainedSpellOutcome.Unknown);
        }, CharacterSustainedSpellOutcome.Unknown);
    }
}
