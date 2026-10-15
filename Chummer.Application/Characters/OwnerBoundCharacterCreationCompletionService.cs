using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class OwnerBoundCharacterCreationCompletionService(
    IWorkspaceStore store, IOwnerContextAccessor ownerContext,
    ICharacterSourceDataResolver sourceResolver, ICharacterFileQueries characterQueries)
    : IOwnerBoundCharacterCreationCompletionService
{
    public CharacterCreationFoundationResult<CharacterCreationCompletionApplyReview> Review(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CharacterCreationStartingCashChoice startingCash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(startingCash);
        cancellationToken.ThrowIfCancellationRequested();
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return Reject([CharacterCreationCompletionReviewSchemas.SnapshotConflict]);
        using (lease)
        {
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId,
                reuseReadObservation: true);
            var read = view.Get(workspaceId);
            if (!read.Success || read.Value is not { } workspace)
                return Reject([CharacterCreationFinalizationBlockers.WorkspaceUnavailable]);
            try
            {
                proposal = JsonSerializer.Deserialize<CharacterCreationCompletionReviewRequest>(JsonSerializer.Serialize(proposal))
                    ?? throw new InvalidDataException("Completion proposal could not be detached.");
                if (!CharacterCreationCompletionTransaction.TryAdmit(view, expectedOwner.Owner, workspace,
                        sourceResolver, characterQueries, proposal, startingCash, cancellationToken,
                        out _, out var projection, out var blockers)) return Reject(blockers);
                cancellationToken.ThrowIfCancellationRequested();
                return new(CharacterCreationFoundationOutcomes.Success, new(proposal, projection!), []);
            }
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
            { return Reject([CharacterCreationFinalizationBlockers.DraftAuthorityInvalid]); }
        }
    }

    public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> Apply(
        OwnerContextStamp expectedOwner, CharacterCreationCompletionApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CharacterCreationCompletionTransaction.IsConfirmed(request))
            return CharacterCreationCompletionTransaction.Blocked(CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired);
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return CharacterCreationCompletionTransaction.Blocked(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        using (lease)
        {
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, request.Binding.WorkspaceId);
            return ((ICharacterCreationCompletionAtomicCommitCapability)view).CommitCompletion(request,
                sourceResolver, characterQueries, cancellationToken);
        }
    }

    private static CharacterCreationFoundationResult<CharacterCreationCompletionApplyReview> Reject(IReadOnlyList<string> blockers)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, blockers.Count == 0
            ? [CharacterCreationFinalizationBlockers.DraftAuthorityInvalid] : blockers.ToArray());
}
