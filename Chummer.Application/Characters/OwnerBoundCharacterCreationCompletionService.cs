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
    public CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource> LoadStartingCash(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        cancellationToken.ThrowIfCancellationRequested();
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return CashRejected(CharacterCreationCompletionReviewSchemas.SnapshotConflict,
                CharacterCreationFoundationOutcomes.Conflict);
        using (lease)
        {
            try
            {
                proposal = JsonSerializer.Deserialize<CharacterCreationCompletionReviewRequest>(JsonSerializer.Serialize(proposal))
                    ?? throw new InvalidDataException("Completion proposal could not be detached.");
                using ICharacterSourceDataResolverOperationScope? scope =
                    (sourceResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
                ICharacterSourceDataResolver resolver = scope ?? sourceResolver;
                var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId,
                    reuseReadObservation: true);
                var read = view.Get(workspaceId);
                if (!read.Success || read.Value is not { } original)
                    return CashRejected(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
                var loaded = OwnerBoundCharacterCreationCompletionReader.LoadFromObservation(
                    view, resolver, characterQueries, new(workspaceId), cancellationToken);
                if (loaded.Outcome != CharacterCreationFoundationOutcomes.Success || loaded.Value is not { } inputs)
                    return new(loaded.Outcome, null, loaded.Blockers);
                var evaluated = CharacterCreationCompletionReviewService.Review(inputs, proposal, cancellationToken);
                if (evaluated.Outcome != CharacterCreationFoundationOutcomes.Success || evaluated.Value is not { Lifestyles: { } lifestyles } review
                    || review.Blockers.Count != 0)
                    return new(evaluated.Outcome, null, evaluated.Blockers);

                // This is the proposed, evaluated purchase—not the original
                // saved draft. No synthetic draft or dice choice is needed.
                Guid? sourceId = null;
                if (lifestyles.StartingLifestyleId is Guid selected)
                {
                    var rows = lifestyles.Lifestyles.Where(row => row.Configuration.LifestyleId == selected).Take(2).ToArray();
                    if (rows.Length != 1) return CashRejected(CharacterCreationFinalizationBlockers.StartingCashUnavailable);
                    var options = lifestyles.Authority.LifestyleOptions.Where(row =>
                        row.OptionId == rows[0].Configuration.BaseLifestyleOptionId).Take(2).ToArray();
                    if (options.Length != 1) return CashRejected(CharacterCreationFinalizationBlockers.StartingCashUnavailable);
                    sourceId = options[0].SourceId;
                }
                else if (lifestyles.Lifestyles.Count != 0)
                    return CashRejected(CharacterCreationFinalizationBlockers.StartingCashUnavailable);
                var context = resolver.TryCreateContext(original.Document.Content);
                if (context is null) return CashRejected(CharacterCreationFinalizationBlockers.StartingCashUnavailable);
                if (!Resolve(out var source) || source is null
                    || !CharacterCreationFinalizationStartingCashRules.SourcesMatch(source, lifestyles.Authority))
                    return CashRejected(CharacterCreationFinalizationBlockers.StartingCashUnavailable);
                cancellationToken.ThrowIfCancellationRequested();
                // The cached view admitted one coherent observation. Recheck
                // the real store before publishing source terms after the work.
                var current = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId).Get(workspaceId);
                if (!current.Success || current.Value is not { } after
                    || after.Id != original.Id || after.ContentRevision != original.ContentRevision
                    || after.SavedRevision != original.SavedRevision
                    || after.Document.Content != original.Document.Content
                    || after.Document.AuxiliaryStateDigest != original.Document.AuxiliaryStateDigest)
                    return CashRejected(CharacterCreationCompletionReviewSchemas.SnapshotConflict,
                        CharacterCreationFoundationOutcomes.Conflict);
                if (!Resolve(out var observed) || observed?.AuthorityDigest != source.AuthorityDigest)
                    return CashRejected(CharacterCreationFinalizationBlockers.StartingCashUnavailable);
                cancellationToken.ThrowIfCancellationRequested();
                return new(CharacterCreationFoundationOutcomes.Success, source, []);

                bool Resolve(out CharacterCreationStartingNuyenSource? value)
                    => sourceId is Guid id ? context.TryResolveCreationPriorityStartingNuyen(id, out value)
                        : context.TryResolveCreationDefaultStartingNuyen(out value);
            }
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException
                or IOException or UnauthorizedAccessException or System.Xml.XmlException or OverflowException)
            { return CashRejected(CharacterCreationFinalizationBlockers.StartingCashUnavailable); }
        }
    }

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

    private static CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource> CashRejected(
        string blocker, string outcome = CharacterCreationFoundationOutcomes.Blocked)
        => new(outcome, null, [blocker]);
}
