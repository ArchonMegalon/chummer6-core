using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class OwnerBoundCharacterCreationCompletionReader
{
    public CharacterCreationFoundationResult<T> EvaluateCompletePlanning<T>(
        OwnerContextStamp expectedOwner, CharacterCreationSkillsLoadRequest request,
        Func<CharacterCreationAdmittedCompletionContext, CharacterCreationFoundationResult<T>> evaluate,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(evaluate);
        if (typeof(Task).IsAssignableFrom(typeof(T)) || typeof(T) == typeof(ValueTask)
            || typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(ValueTask<>))
            throw new ArgumentException("Completion evaluation must be synchronous.", nameof(evaluate));
        cancellationToken.ThrowIfCancellationRequested();
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return Refuse(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        using (lease)
        using (CharacterCreationCompletionDraftTransaction.BeginValidationOperation())
        {
            var admitted = sourceResolver.ForCreationSourceExecution(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4);
            if (admitted is null) return Refuse(CharacterCreationCompletionReviewSchemas.AuthorityUnavailable);
            CharacterCreationFoundationResult<T> result;
            WorkspaceStoredDocument original;
            string inputDigest, bootstrapDigest;
            (Guid? SourceId, string Digest)[] cashComparisons;
            using (var scope = (admitted as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope())
            {
                var resolver = scope ?? admitted;
                var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, request.WorkspaceId,
                    reuseReadObservation: true);
                var read = view.Get(request.WorkspaceId);
                if (!read.Success || read.Value is not { } document)
                    return Refuse(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
                original = document;
                CharacterCreationRuleSourceCapture? capture = null;
                var loaded = LoadFromObservation(view, resolver, characterQueries,
                    request with { IncludeKarmaPurchasePolicy = true }, cancellationToken,
                    includeLoadout: true, includeEffectivePolicy: true, includeConsumerPolicy: true,
                    knowledgeModifiers: true, policyConsumption: true, includeOriginalCharacter: true,
                    includeRuleSourceExecution: true, captured: value => capture = value,
                    ruleSourceExecutionSchema: CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4,
                    includeSourcebooks: true);
                if (loaded.Outcome != CharacterCreationFoundationOutcomes.Success || loaded.Value is not { } inputs
                    || capture is null || !capture.MatchesOriginalCharacter(original.Document.Content))
                    return new(loaded.Outcome == CharacterCreationFoundationOutcomes.Success
                        ? CharacterCreationFoundationOutcomes.Blocked : loaded.Outcome, default,
                        loaded.Blockers.Count == 0 ? [CharacterCreationCompletionReviewSchemas.AuthorityUnavailable] : loaded.Blockers);
                inputDigest = inputs.SnapshotDigest;
                // Bootstrap and cash are resolved from the same admitted private bytes.
                var source = resolver.TryCreateContext(original.Document.Content);
                if (source is null || !CharacterCreationCompletionBootstrapSnapshot.TryCapture(source,
                        original.Document.Content, out var bootstrap))
                    return Refuse(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
                bootstrapDigest = bootstrap.SnapshotDigest;
                using var context = new CharacterCreationAdmittedCompletionContext(inputs, original, bootstrap,
                    new(capture, characterQueries), source, cancellationToken);
                result = evaluate(context);
                cashComparisons = context.CaptureCashComparisons();
                cancellationToken.ThrowIfCancellationRequested();
            }
            // A new scope must not reuse the earlier source observation cache.
            using var freshScope = (admitted as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var current = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, request.WorkspaceId);
            var reread = current.Get(request.WorkspaceId);
            if (lease.Stamp != expectedOwner || !reread.Success || reread.Value is not { } after
                || after.Id != original.Id || after.ContentRevision != original.ContentRevision
                || after.SavedRevision != original.SavedRevision || after.Document.Content != original.Document.Content
                || after.Document.AuxiliaryStateDigest != original.Document.AuxiliaryStateDigest)
                return Refuse(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
            var freshSource = (freshScope ?? admitted).TryCreateContext(after.Document.Content);
            if (freshSource is null || !CharacterCreationCompletionBootstrapSnapshot.TryCapture(freshSource,
                    after.Document.Content, out var freshBootstrap) || freshBootstrap.SnapshotDigest != bootstrapDigest)
                return Refuse(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
            foreach (var expected in cashComparisons)
            {
                CharacterCreationStartingNuyenSource? observed;
                bool resolved = expected.SourceId is Guid id
                    ? freshSource.TryResolveCreationPriorityStartingNuyen(id, out observed)
                    : freshSource.TryResolveCreationDefaultStartingNuyen(out observed);
                if (!resolved || observed?.AuthorityDigest != expected.Digest)
                    return Refuse(CharacterCreationFinalizationBlockers.StartingCashUnavailable);
            }
            var reloaded = LoadFromObservation(new WorkspaceContinuationReadView(after), freshScope ?? admitted,
                characterQueries, request with { IncludeKarmaPurchasePolicy = true }, cancellationToken,
                includeLoadout: true, includeEffectivePolicy: true, includeConsumerPolicy: true,
                knowledgeModifiers: true, policyConsumption: true, includeOriginalCharacter: true,
                includeRuleSourceExecution: true,
                ruleSourceExecutionSchema: CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4,
                includeSourcebooks: true);
            cancellationToken.ThrowIfCancellationRequested();
            return reloaded.Outcome == CharacterCreationFoundationOutcomes.Success
                && reloaded.Value?.SnapshotDigest == inputDigest && lease.Stamp == expectedOwner
                ? result : Refuse(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        }
        static CharacterCreationFoundationResult<T> Refuse(string blocker)
            => new(CharacterCreationFoundationOutcomes.Blocked, default, [blocker]);
    }
}
