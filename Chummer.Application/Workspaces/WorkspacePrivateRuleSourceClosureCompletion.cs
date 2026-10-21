using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

/// <summary>
/// Trusted Core composition of imported observation custody with fresh owner and
/// source admission. The recovery visitor is synchronous and replay-only; it is
/// never returned to the caller. No target store or mutation service is present.
/// </summary>
public static class WorkspacePrivateRuleSourceClosureCompletion
{
    /// <summary>Starts a fresh synchronous history-validation memo for a Core store observation.
    /// This scope is not an owner lease or a historical recovery capability.</summary>
    public static IDisposable BeginHistoryObservation()
        => CharacterCreationCompletionDraftTransaction.BeginValidationOperation();

    // Full semantic history validation during import, independent of current
    // campaign admission. No validation memo escapes this synchronous scope.
    public static bool ValidateHistoryObservation(WorkspaceContinuationSnapshot snapshot,
        ICharacterCreationRuleSourceRecovery recovery)
    {
        using var history = CharacterCreationCompletionDraftTransaction.BeginValidationOperation();
        var owner = snapshot.OwnerId == OwnerScope.LocalSingleUser.NormalizedValue
            ? OwnerScope.LocalSingleUser : new OwnerScope(snapshot.OwnerId);
        return WorkspaceContinuationHistoryIntegrity.TryValidate(owner, snapshot, recovery)
            && CharacterCreationCompletionTransaction.IsValidCurrentDocument(
                snapshot.Workspace.Document, snapshot.Workspace.ContentRevision, recovery)
            && CharacterCreationCompletionDraftTransaction.IsValidCurrentDocument(
                snapshot.Workspace.Document, snapshot.Workspace.ContentRevision)
            && recovery.ValidateRetainedRuleSources();
    }

    public static TResult Evaluate<TResult>(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        ICharacterSourceDataResolver sourceResolver, ICharacterFileQueries queries,
        ILifeModulesCatalogService lifeModules, ReadOnlyMemory<byte> continuation,
        int maximumBytes, WorkspacePrivateRuleSourceClosureBinding binding,
        ICharacterCreationRuleSourceRecovery recovery, string executionSchema,
        Func<WorkspaceContinuationCompletionResult, CharacterCreationRuleSourceExecutionContext?,
            WorkspacePrivateRuleSourceClosureBinding, TResult> evaluate,
        CancellationToken cancellationToken = default, bool sourcebookSelection = false)
    {
        ArgumentNullException.ThrowIfNull(evaluate);
        if (sourcebookSelection && executionSchema is not (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2
            or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4))
            throw new ArgumentException("Sourcebook execution requires the V2, V3 or V4 source consumer.", nameof(executionSchema));
        ArgumentNullException.ThrowIfNull(recovery);
        if (executionSchema is not (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1
            or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2
            or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4))
            throw Unavailable();
        if (typeof(Task).IsAssignableFrom(typeof(TResult)) || typeof(TResult) == typeof(ValueTask)
            || typeof(TResult).IsGenericType && typeof(TResult).GetGenericTypeDefinition() == typeof(ValueTask<>))
            throw new ArgumentException("Private observation must be synchronous.");
        cancellationToken.ThrowIfCancellationRequested();
        if (continuation.Length > maximumBytes || binding.OwnerId != expectedOwner.Owner.NormalizedValue
            || !OwnerContextAdmission.TryAcquire(owners, expectedOwner, out var lease)) throw Unavailable();
        using (lease)
        {
            using var history = CharacterCreationCompletionDraftTransaction.BeginValidationOperation();
            byte[] bytes = continuation.ToArray();
            try
            {
                if (!WorkspaceContinuationCodec.TryDecodeCandidate(bytes, maximumBytes, out var candidate)
                    || candidate.SnapshotDigest != binding.ContinuationDigest
                    || candidate.Snapshot.Workspace.Id != binding.WorkspaceId
                    || candidate.Snapshot.Workspace.ContentRevision != binding.ContentRevision
                    || candidate.Snapshot.Workspace.SavedRevision != binding.SavedRevision
                    || !WorkspaceContinuationHistoryIntegrity.TryValidate(expectedOwner.Owner, candidate.Snapshot, recovery))
                    throw Unavailable();
                var reader = new WorkspaceContinuationCompletionReader(owners, sourceResolver, queries, lifeModules);
                var fresh = Load(out var sources);
                if (!fresh.Success || sources is null) throw Unavailable();
                cancellationToken.ThrowIfCancellationRequested();
                TResult result = evaluate(fresh, sources, binding);
                // A callback cannot extend the owner/source observation lifetime.
                // Runtime inspection also rejects Task hidden behind object/interface.
                if (result is Task || result is ValueTask
                    || result is not null && result.GetType().IsGenericType
                        && result.GetType().GetGenericTypeDefinition() == typeof(ValueTask<>))
                    throw new ArgumentException("Private observation must be synchronous.");
                cancellationToken.ThrowIfCancellationRequested();
                var current = Load(out _);
                if (!current.Success || current.Identity != fresh.Identity || lease.Stamp != expectedOwner
                    || !recovery.ValidateRetainedRuleSources()) throw Unavailable();
                cancellationToken.ThrowIfCancellationRequested();
                return result;

                WorkspaceContinuationCompletionResult Load(out CharacterCreationRuleSourceExecutionContext? execution)
                    => reader.LoadExecutionUnderLease(expectedOwner, lease, bytes, maximumBytes, out execution,
                        executionSchema, cancellationToken, recovery, sourcebookSelection);
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        }
    }

    private static InvalidOperationException Unavailable()
        => new("Private completion inputs are unavailable or changed.");
}
