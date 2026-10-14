using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Reads the original completion inputs under one exact owner lease, one source
/// operation scope and one actual workspace observation. It never creates drafts
/// or writes a runner; the detached bundle must still pass the combined review.
/// </summary>
public sealed class OwnerBoundCharacterCreationCompletionReader(
    IWorkspaceStore store,
    IOwnerContextAccessor ownerContext,
    ICharacterSourceDataResolver sourceResolver,
    ICharacterFileQueries characterQueries) : IOwnerBoundCharacterCreationCompletionReader
{
    public CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadCompletionInputs(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return Reject([CharacterCreationCompletionReviewSchemas.SnapshotConflict],
                CharacterCreationFoundationOutcomes.Conflict);
        using (lease)
        {
            using ICharacterSourceDataResolverOperationScope? scope =
                (sourceResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            ICharacterSourceDataResolver resolver = scope ?? sourceResolver;
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner,
                request.WorkspaceId, reuseReadObservation: true);
            var skills = new CharacterCreationSkillsService(view, resolver).LoadCompletionInputs(request);
            if (skills.Value is null) return Reject(skills.Blockers);
            cancellationToken.ThrowIfCancellationRequested();
            var magic = new CharacterCreationMagicResonanceService(view, resolver)
                .LoadCompletionInputs(new(request.WorkspaceId));
            if (magic.Value is null) return Reject(magic.Blockers);
            cancellationToken.ThrowIfCancellationRequested();
            var lifestyles = new CharacterCreationLifestylesService(view, resolver, characterQueries)
                .LoadCompletionInputs(new(request.WorkspaceId));
            if (lifestyles.Value is null) return Reject(lifestyles.Blockers);
            cancellationToken.ThrowIfCancellationRequested();
            var contacts = new CharacterCreationContactsService(view, resolver, characterQueries)
                .LoadCompletionInputs(new(request.WorkspaceId));
            if (contacts.Value is null) return Reject(contacts.Blockers);
            cancellationToken.ThrowIfCancellationRequested();

            var inputs = new CharacterCreationCompletionReviewInputs(
                CharacterCreationCompletionReviewSchemas.InputsV1,
                skills.Value, magic.Value, lifestyles.Value, contacts.Value, string.Empty);
            inputs = inputs with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs) };
            return new(CharacterCreationFoundationOutcomes.Success, inputs, []);
        }
    }

    private static CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> Reject(
        IReadOnlyList<string> blockers, string outcome = CharacterCreationFoundationOutcomes.Blocked)
        => new(outcome, null, blockers.Count == 0
            ? [CharacterCreationCompletionReviewSchemas.AuthorityUnavailable] : blockers.ToArray());
}
