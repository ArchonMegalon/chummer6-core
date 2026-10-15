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
        => Load(expectedOwner, request, false, cancellationToken);

    public CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadCompletionInputsWithLoadout(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request,
        CancellationToken cancellationToken = default)
        => Load(expectedOwner, request, true, cancellationToken);

    private CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> Load(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request,
        bool includeLoadout, CancellationToken cancellationToken)
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
            return LoadFromObservation(view, resolver, characterQueries, request, cancellationToken, includeLoadout);
        }
    }

    // The caller owns the exact owner lease, source operation scope and real
    // workspace observation. Reused by the read-only proposed-cash route; it
    // never accepts a constructed intermediate draft or portable save authority.
    internal static CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> LoadFromObservation(
        IWorkspaceStore view, ICharacterSourceDataResolver resolver, ICharacterFileQueries characterQueries,
        CharacterCreationSkillsLoadRequest request, CancellationToken cancellationToken, bool includeLoadout = false)
    {
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

        CharacterCreationLoadoutCompletionInputs? loadout = null;
        // A saved typed purchase cannot disappear through the older entry point.
        if (!includeLoadout)
            includeLoadout = lifestyles.Value.Gear.Resources.LoadoutDraft is not null;
        if (includeLoadout)
        {
            var original = view.Get(request.WorkspaceId);
            if (!original.Success || original.Value is null)
                return Reject([CharacterCreationLoadoutCompletionSchemas.SnapshotConflict]);
            var loaded = CharacterCreationLoadoutCompletionService.LoadFromObservation(original.Value, resolver,
                requireAuthority: true);
            if (loaded.Outcome != CharacterCreationFoundationOutcomes.Success) return Reject(loaded.Blockers);
            loadout = loaded.Value;
        }
        cancellationToken.ThrowIfCancellationRequested();

        var inputs = new CharacterCreationCompletionReviewInputs(
            loadout is null ? CharacterCreationCompletionReviewSchemas.InputsV1 : CharacterCreationCompletionReviewSchemas.InputsV2,
            skills.Value, magic.Value, lifestyles.Value, contacts.Value, string.Empty) { Loadout = loadout };
        inputs = inputs with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs) };
        return new(CharacterCreationFoundationOutcomes.Success, inputs, []);
    }

    private static CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> Reject(
        IReadOnlyList<string> blockers, string outcome = CharacterCreationFoundationOutcomes.Blocked)
        => new(outcome, null, blockers.Count == 0
            ? [CharacterCreationCompletionReviewSchemas.AuthorityUnavailable] : blockers.ToArray());
}
