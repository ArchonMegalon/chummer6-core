using System.Collections.Immutable;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

public sealed record RunnerLibraryMutationLedgerEntry(
    string IdempotencyKeyDigestSha256,
    string CommandDigestSha256,
    RunnerLibraryMutationReceipt Receipt);

public sealed record RunnerLibraryStoreState(
    string DisplayName,
    RunnerLibraryLifecycle Lifecycle,
    RunnerLibraryLifecycle? LifecycleBeforeDelete,
    long LifecycleRevision,
    DateTimeOffset LastLifecycleUpdatedUtc,
    RunnerLibraryProvenance? Provenance,
    ImmutableArray<RunnerLibraryMutationLedgerEntry> MutationLedger);

public static class RunnerLibraryStoreStateMachine
{
    public const int MaximumMutationLedgerEntries = 4096;

    public static RunnerLibraryStoreState CreateLegacy(
        CharacterWorkspaceId runnerId,
        DateTimeOffset lastUpdatedUtc)
    {
        return new RunnerLibraryStoreState(
            runnerId.Value,
            RunnerLibraryLifecycle.Active,
            null,
            1,
            lastUpdatedUtc.ToUniversalTime(),
            null,
            ImmutableArray<RunnerLibraryMutationLedgerEntry>.Empty);
    }

    public static bool IsValid(
        CharacterWorkspaceId runnerId,
        RunnerLibraryStoreState state)
    {
        if (!RunnerLibraryCanonical.IsSupportedRunnerId(runnerId)
            || !RunnerLibraryCanonical.TryNormalizeDisplayName(
                state.DisplayName,
                out string normalizedName)
            || !string.Equals(normalizedName, state.DisplayName, StringComparison.Ordinal)
            || !Enum.IsDefined(state.Lifecycle)
            || state.LifecycleRevision <= 0
            || state.LastLifecycleUpdatedUtc == default
            || state.LastLifecycleUpdatedUtc.Offset != TimeSpan.Zero
            || state.MutationLedger.IsDefault
            || state.MutationLedger.Length > MaximumMutationLedgerEntries)
        {
            return false;
        }

        if (state.Lifecycle == RunnerLibraryLifecycle.Deleted)
        {
            if (state.LifecycleBeforeDelete is not (RunnerLibraryLifecycle.Active
                or RunnerLibraryLifecycle.Archived))
            {
                return false;
            }
        }
        else if (state.LifecycleBeforeDelete is not null)
        {
            return false;
        }

        if (!IsValidProvenance(runnerId, state.Provenance))
        {
            return false;
        }

        HashSet<string> idempotencyKeys = new(StringComparer.Ordinal);
        RunnerLibraryMutationReceipt? lastLifecycleReceipt = null;
        int targetDuplicateReceiptCount = 0;
        foreach (RunnerLibraryMutationLedgerEntry entry in state.MutationLedger)
        {
            RunnerLibraryMutationReceipt receipt = entry.Receipt;
            bool isDuplicateForSource = receipt.Kind == RunnerLibraryMutationKind.Duplicate
                                        && receipt.SourceRunnerId == runnerId;
            bool isMutationForRunner = receipt.RunnerId == runnerId;
            if (!RunnerLibraryCanonical.IsSha256(entry.IdempotencyKeyDigestSha256)
                || !RunnerLibraryCanonical.IsSha256(entry.CommandDigestSha256)
                || !idempotencyKeys.Add(entry.IdempotencyKeyDigestSha256)
                || !string.Equals(
                    entry.IdempotencyKeyDigestSha256,
                    receipt.IdempotencyKeyDigestSha256,
                    StringComparison.Ordinal)
                || !string.Equals(
                    entry.CommandDigestSha256,
                    receipt.CommandDigestSha256,
                    StringComparison.Ordinal)
                || !string.Equals(
                    receipt.Schema,
                    RunnerLibraryCanonical.ReceiptSchema,
                    StringComparison.Ordinal)
                || !Enum.IsDefined(receipt.Kind)
                || !Enum.IsDefined(receipt.BeforeLifecycle)
                || !Enum.IsDefined(receipt.AfterLifecycle)
                || (receipt.Kind == RunnerLibraryMutationKind.Duplicate
                    && (receipt.SourceRunnerId is not CharacterWorkspaceId sourceId
                        || !RunnerLibraryCanonical.IsSupportedRunnerId(sourceId)
                        || sourceId == receipt.RunnerId))
                || (receipt.Kind != RunnerLibraryMutationKind.Duplicate
                    && receipt.SourceRunnerId is not null)
                || (!isMutationForRunner && !isDuplicateForSource)
                || !RunnerLibraryCanonical.IsSha256(receipt.BeforeStateDigestSha256)
                || !RunnerLibraryCanonical.IsSha256(receipt.AfterStateDigestSha256)
                || !RunnerLibraryCanonical.IsSha256(receipt.ContentDigestSha256)
                || !RunnerLibraryCanonical.IsSha256(receipt.ReceiptDigestSha256)
                || receipt.BeforeLifecycleRevision <= 0
                || receipt.AfterLifecycleRevision <= 0
                || receipt.ContentRevision <= 0
                || receipt.CommittedAtUtc == default
                || receipt.CommittedAtUtc.Offset != TimeSpan.Zero
                || !IsValidSnapshot(
                    receipt.Kind == RunnerLibraryMutationKind.Duplicate
                        ? receipt.SourceRunnerId ?? receipt.RunnerId
                        : receipt.RunnerId,
                    receipt.BeforeDisplayName,
                    receipt.BeforeLifecycle,
                    receipt.BeforeLifecycleBeforeDelete,
                    receipt.BeforeLifecycleRevision,
                    receipt.BeforeProvenance,
                    receipt.ContentDigestSha256,
                    receipt.BeforeStateDigestSha256)
                || !IsValidSnapshot(
                    receipt.RunnerId,
                    receipt.AfterDisplayName,
                    receipt.AfterLifecycle,
                    receipt.AfterLifecycleBeforeDelete,
                    receipt.AfterLifecycleRevision,
                    receipt.AfterProvenance,
                    receipt.ContentDigestSha256,
                    receipt.AfterStateDigestSha256)
                || !string.Equals(
                    receipt.ReceiptDigestSha256,
                    RunnerLibraryCanonical.ComputeReceiptDigest(
                        receipt with { ReceiptDigestSha256 = string.Empty }),
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (receipt.Kind == RunnerLibraryMutationKind.Duplicate)
            {
                if (isMutationForRunner)
                {
                    targetDuplicateReceiptCount++;
                    if (receipt.AfterLifecycle != RunnerLibraryLifecycle.Active
                        || receipt.AfterLifecycleRevision != 1
                        || receipt.ContentRevision != 1)
                    {
                        return false;
                    }
                }

                continue;
            }

            if (!IsValidLifecycleReceiptTransition(receipt)
                || !Equals(receipt.BeforeProvenance, receipt.AfterProvenance))
            {
                return false;
            }

            lastLifecycleReceipt = receipt;
        }

        RunnerLibraryMutationReceipt? initialDuplicateReceipt = state.Provenance is null
            ? null
            : state.MutationLedger.FirstOrDefault()?.Receipt;
        if ((state.Provenance is null && targetDuplicateReceiptCount != 0)
            || (state.Provenance is not null
                && (targetDuplicateReceiptCount != 1
                    || initialDuplicateReceipt is null
                    || initialDuplicateReceipt.Kind != RunnerLibraryMutationKind.Duplicate
                    || initialDuplicateReceipt.RunnerId != runnerId
                    || initialDuplicateReceipt.SourceRunnerId != state.Provenance.SourceRunnerId
                    || !Equals(initialDuplicateReceipt.AfterProvenance, state.Provenance)
                    || !string.Equals(
                        initialDuplicateReceipt.ContentDigestSha256,
                        state.Provenance.SourceContentDigestSha256,
                        StringComparison.Ordinal))))
        {
            return false;
        }

        string projectedDisplayName = initialDuplicateReceipt?.AfterDisplayName ?? runnerId.Value;
        RunnerLibraryLifecycle projectedLifecycle =
            initialDuplicateReceipt?.AfterLifecycle ?? RunnerLibraryLifecycle.Active;
        RunnerLibraryLifecycle? projectedLifecycleBeforeDelete =
            initialDuplicateReceipt?.AfterLifecycleBeforeDelete;
        long projectedLifecycleRevision = initialDuplicateReceipt?.AfterLifecycleRevision ?? 1;
        RunnerLibraryProvenance? projectedProvenance =
            initialDuplicateReceipt?.AfterProvenance;
        DateTimeOffset? priorReceiptCommittedAtUtc = initialDuplicateReceipt?.CommittedAtUtc;
        foreach (RunnerLibraryMutationLedgerEntry entry in state.MutationLedger)
        {
            RunnerLibraryMutationReceipt receipt = entry.Receipt;
            if (ReferenceEquals(receipt, initialDuplicateReceipt)
                || receipt == initialDuplicateReceipt)
            {
                continue;
            }

            if (receipt.BeforeDisplayName != projectedDisplayName
                || receipt.BeforeLifecycle != projectedLifecycle
                || receipt.BeforeLifecycleBeforeDelete != projectedLifecycleBeforeDelete
                || receipt.BeforeLifecycleRevision != projectedLifecycleRevision
                || !Equals(receipt.BeforeProvenance, projectedProvenance))
            {
                return false;
            }

            if (priorReceiptCommittedAtUtc is DateTimeOffset priorCommittedAtUtc
                && receipt.CommittedAtUtc < priorCommittedAtUtc)
            {
                return false;
            }

            priorReceiptCommittedAtUtc = receipt.CommittedAtUtc;

            if (receipt.Kind != RunnerLibraryMutationKind.Duplicate)
            {
                projectedDisplayName = receipt.AfterDisplayName;
                projectedLifecycle = receipt.AfterLifecycle;
                projectedLifecycleBeforeDelete = receipt.AfterLifecycleBeforeDelete;
                projectedLifecycleRevision = receipt.AfterLifecycleRevision;
                projectedProvenance = receipt.AfterProvenance;
            }
        }

        DateTimeOffset expectedLifecycleTimestamp = lastLifecycleReceipt?.CommittedAtUtc
            ?? initialDuplicateReceipt?.CommittedAtUtc
            ?? state.LastLifecycleUpdatedUtc;
        return projectedDisplayName == state.DisplayName
               && projectedLifecycle == state.Lifecycle
               && projectedLifecycleBeforeDelete == state.LifecycleBeforeDelete
               && projectedLifecycleRevision == state.LifecycleRevision
               && Equals(projectedProvenance, state.Provenance)
               && state.LastLifecycleUpdatedUtc == expectedLifecycleTimestamp;
    }

    private static bool IsValidSnapshot(
        CharacterWorkspaceId runnerId,
        string displayName,
        RunnerLibraryLifecycle lifecycle,
        RunnerLibraryLifecycle? lifecycleBeforeDelete,
        long lifecycleRevision,
        RunnerLibraryProvenance? provenance,
        string contentDigestSha256,
        string stateDigestSha256)
    {
        return RunnerLibraryCanonical.IsSupportedRunnerId(runnerId)
               && RunnerLibraryCanonical.TryNormalizeDisplayName(
                   displayName,
                   out string normalizedName)
               && normalizedName == displayName
               && Enum.IsDefined(lifecycle)
               && lifecycleRevision > 0
               && (lifecycle == RunnerLibraryLifecycle.Deleted
                   ? lifecycleBeforeDelete is RunnerLibraryLifecycle.Active
                       or RunnerLibraryLifecycle.Archived
                   : lifecycleBeforeDelete is null)
               && IsValidProvenance(runnerId, provenance)
               && RunnerLibraryCanonical.IsSha256(contentDigestSha256)
               && string.Equals(
                   stateDigestSha256,
                   RunnerLibraryCanonical.ComputeStateDigest(
                       runnerId,
                       displayName,
                       lifecycle,
                       lifecycleBeforeDelete,
                       lifecycleRevision,
                       contentDigestSha256,
                       provenance),
                   StringComparison.Ordinal);
    }

    private static bool IsValidProvenance(
        CharacterWorkspaceId runnerId,
        RunnerLibraryProvenance? provenance)
    {
        return provenance is null
               || (RunnerLibraryCanonical.IsSupportedRunnerId(provenance.SourceRunnerId)
                   && provenance.SourceRunnerId != runnerId
                   && provenance.SourceContentRevision > 0
                   && RunnerLibraryCanonical.IsSha256(
                       provenance.SourceContentDigestSha256));
    }

    private static bool IsValidLifecycleReceiptTransition(RunnerLibraryMutationReceipt receipt)
    {
        if (receipt.AfterLifecycleRevision != receipt.BeforeLifecycleRevision + 1)
        {
            return false;
        }

        return receipt.Kind switch
        {
            RunnerLibraryMutationKind.Rename =>
                receipt.BeforeLifecycle is not RunnerLibraryLifecycle.Deleted
                && receipt.AfterLifecycle == receipt.BeforeLifecycle
                && receipt.BeforeLifecycleBeforeDelete == receipt.AfterLifecycleBeforeDelete,
            RunnerLibraryMutationKind.Archive =>
                receipt.BeforeLifecycle == RunnerLibraryLifecycle.Active
                && receipt.AfterLifecycle == RunnerLibraryLifecycle.Archived
                && receipt.BeforeDisplayName == receipt.AfterDisplayName,
            RunnerLibraryMutationKind.RestoreArchived =>
                receipt.BeforeLifecycle == RunnerLibraryLifecycle.Archived
                && receipt.AfterLifecycle == RunnerLibraryLifecycle.Active
                && receipt.BeforeDisplayName == receipt.AfterDisplayName,
            RunnerLibraryMutationKind.Delete =>
                (receipt.BeforeLifecycle is RunnerLibraryLifecycle.Active
                    or RunnerLibraryLifecycle.Archived)
                && receipt.AfterLifecycle == RunnerLibraryLifecycle.Deleted
                && receipt.BeforeDisplayName == receipt.AfterDisplayName
                && receipt.BeforeLifecycleBeforeDelete is null
                && receipt.AfterLifecycleBeforeDelete == receipt.BeforeLifecycle,
            RunnerLibraryMutationKind.RestoreDeleted =>
                receipt.BeforeLifecycle == RunnerLibraryLifecycle.Deleted
                && (receipt.AfterLifecycle is RunnerLibraryLifecycle.Active
                    or RunnerLibraryLifecycle.Archived)
                && receipt.BeforeDisplayName == receipt.AfterDisplayName
                && receipt.BeforeLifecycleBeforeDelete == receipt.AfterLifecycle
                && receipt.AfterLifecycleBeforeDelete is null,
            _ => false
        };
    }

    public static RunnerLibraryMutationResult? ResolveReplayOrConflict(
        CharacterWorkspaceId stateRunnerId,
        RunnerLibraryStoreState state,
        RunnerLibraryStoreMutation mutation,
        Func<RunnerLibraryItem> currentItem)
    {
        if (!RunnerLibraryCanonical.IsValidStoreMutation(mutation))
        {
            return new RunnerLibraryMutationResult(
                RunnerLibraryOperationOutcome.Invalid,
                Error: "Runner Library mutation is not canonical.");
        }

        if (!IsValid(stateRunnerId, state))
        {
            return new RunnerLibraryMutationResult(
                RunnerLibraryOperationOutcome.Corrupt,
                Error: "Runner Library state is corrupt.");
        }

        RunnerLibraryMutationLedgerEntry? existing = state.MutationLedger.FirstOrDefault(
            entry => string.Equals(
                entry.IdempotencyKeyDigestSha256,
                mutation.IdempotencyKeyDigestSha256,
                StringComparison.Ordinal));
        if (existing is null)
        {
            return null;
        }

        return string.Equals(
            existing.CommandDigestSha256,
            mutation.CommandDigestSha256,
            StringComparison.Ordinal)
            ? new RunnerLibraryMutationResult(
                RunnerLibraryOperationOutcome.Replayed,
                currentItem(),
                existing.Receipt,
                state.LifecycleRevision)
            : new RunnerLibraryMutationResult(
                RunnerLibraryOperationOutcome.Conflict,
                currentItem(),
                CurrentLifecycleRevision: state.LifecycleRevision,
                Error: "Idempotency key was already used for a different Runner Library mutation.");
    }

    public static bool TryApply(
        CharacterWorkspaceId runnerId,
        RunnerLibraryStoreState current,
        RunnerLibraryStoreMutation mutation,
        long contentRevision,
        string contentDigestSha256,
        DateTimeOffset committedAtUtc,
        out RunnerLibraryStoreState replacement,
        out RunnerLibraryMutationReceipt receipt,
        out string? error)
    {
        replacement = current;
        receipt = null!;
        error = null;
        if (runnerId != mutation.RunnerId
            || mutation.Kind == RunnerLibraryMutationKind.Duplicate
            || !RunnerLibraryCanonical.IsValidStoreMutation(mutation))
        {
            error = "Runner Library mutation is not canonical for this runner.";
            return false;
        }

        if (!IsValid(runnerId, current))
        {
            error = "Runner Library current state is corrupt.";
            return false;
        }

        if (contentRevision != mutation.ExpectedContentRevision
            || !string.Equals(
                contentDigestSha256,
                mutation.ExpectedContentDigestSha256,
                StringComparison.Ordinal))
        {
            error = "Runner content revision or digest does not match the expected snapshot.";
            return false;
        }

        if (current.LifecycleRevision != mutation.ExpectedLifecycleRevision)
        {
            error = "Runner lifecycle revision does not match the expected revision.";
            return false;
        }

        if (current.LifecycleRevision == long.MaxValue
            || current.MutationLedger.Length >= MaximumMutationLedgerEntries)
        {
            error = "Runner lifecycle revision or immutable mutation ledger is exhausted.";
            return false;
        }

        string nextDisplayName = current.DisplayName;
        RunnerLibraryLifecycle nextLifecycle;
        RunnerLibraryLifecycle? lifecycleBeforeDelete = null;
        switch (mutation.Kind)
        {
            case RunnerLibraryMutationKind.Rename
                when current.Lifecycle is not RunnerLibraryLifecycle.Deleted
                     && mutation.DisplayName is not null:
                nextDisplayName = mutation.DisplayName;
                nextLifecycle = current.Lifecycle;
                break;
            case RunnerLibraryMutationKind.Archive
                when current.Lifecycle == RunnerLibraryLifecycle.Active:
                nextLifecycle = RunnerLibraryLifecycle.Archived;
                break;
            case RunnerLibraryMutationKind.RestoreArchived
                when current.Lifecycle == RunnerLibraryLifecycle.Archived:
                nextLifecycle = RunnerLibraryLifecycle.Active;
                break;
            case RunnerLibraryMutationKind.Delete
                when current.Lifecycle is RunnerLibraryLifecycle.Active
                    or RunnerLibraryLifecycle.Archived:
                nextLifecycle = RunnerLibraryLifecycle.Deleted;
                lifecycleBeforeDelete = current.Lifecycle;
                break;
            case RunnerLibraryMutationKind.RestoreDeleted
                when current.Lifecycle == RunnerLibraryLifecycle.Deleted
                     && current.LifecycleBeforeDelete is RunnerLibraryLifecycle prior:
                nextLifecycle = prior;
                break;
            default:
                error = "Runner Library lifecycle transition is invalid for the current state.";
                return false;
        }

        long nextRevision = current.LifecycleRevision + 1;
        string beforeStateDigest = RunnerLibraryCanonical.ComputeStateDigest(
            runnerId,
            current.DisplayName,
            current.Lifecycle,
            current.LifecycleBeforeDelete,
            current.LifecycleRevision,
            contentDigestSha256,
            current.Provenance);
        string afterStateDigest = RunnerLibraryCanonical.ComputeStateDigest(
            runnerId,
            nextDisplayName,
            nextLifecycle,
            lifecycleBeforeDelete,
            nextRevision,
            contentDigestSha256,
            current.Provenance);
        RunnerLibraryMutationReceipt unsignedReceipt = new(
            RunnerLibraryCanonical.ReceiptSchema,
            mutation.Kind,
            runnerId,
            null,
            mutation.IdempotencyKeyDigestSha256,
            mutation.CommandDigestSha256,
            beforeStateDigest,
            afterStateDigest,
            current.DisplayName,
            nextDisplayName,
            current.Lifecycle,
            nextLifecycle,
            current.LifecycleBeforeDelete,
            lifecycleBeforeDelete,
            current.LifecycleRevision,
            nextRevision,
            current.Provenance,
            current.Provenance,
            contentRevision,
            contentDigestSha256,
            committedAtUtc.ToUniversalTime(),
            string.Empty);
        receipt = unsignedReceipt with
        {
            ReceiptDigestSha256 = RunnerLibraryCanonical.ComputeReceiptDigest(unsignedReceipt)
        };
        replacement = new RunnerLibraryStoreState(
            nextDisplayName,
            nextLifecycle,
            lifecycleBeforeDelete,
            nextRevision,
            receipt.CommittedAtUtc,
            current.Provenance,
            current.MutationLedger.Add(new RunnerLibraryMutationLedgerEntry(
                mutation.IdempotencyKeyDigestSha256,
                mutation.CommandDigestSha256,
                receipt)));
        return true;
    }

    public static bool TryCreateDuplicate(
        CharacterWorkspaceId sourceRunnerId,
        CharacterWorkspaceId newRunnerId,
        string sourceDisplayName,
        RunnerLibraryLifecycle sourceLifecycle,
        RunnerLibraryLifecycle? sourceLifecycleBeforeDelete,
        string displayName,
        long sourceLifecycleRevision,
        RunnerLibraryProvenance? sourceProvenance,
        long sourceContentRevision,
        string sourceContentDigestSha256,
        RunnerLibraryStoreMutation mutation,
        DateTimeOffset committedAtUtc,
        out RunnerLibraryStoreState duplicateState,
        out RunnerLibraryMutationReceipt receipt)
    {
        duplicateState = null!;
        receipt = null!;
        if (mutation.Kind != RunnerLibraryMutationKind.Duplicate
            || mutation.RunnerId != sourceRunnerId
            || mutation.NewRunnerId != newRunnerId
            || !string.Equals(mutation.DisplayName, displayName, StringComparison.Ordinal)
            || mutation.ExpectedLifecycleRevision != sourceLifecycleRevision
            || mutation.ExpectedContentRevision != sourceContentRevision
            || sourceLifecycle == RunnerLibraryLifecycle.Deleted
            || !string.Equals(
                mutation.ExpectedContentDigestSha256,
                sourceContentDigestSha256,
                StringComparison.Ordinal)
            || !RunnerLibraryCanonical.IsValidStoreMutation(mutation)
            || !IsValidSnapshot(
                sourceRunnerId,
                sourceDisplayName,
                sourceLifecycle,
                sourceLifecycleBeforeDelete,
                sourceLifecycleRevision,
                sourceProvenance,
                sourceContentDigestSha256,
                RunnerLibraryCanonical.ComputeStateDigest(
                    sourceRunnerId,
                    sourceDisplayName,
                    sourceLifecycle,
                    sourceLifecycleBeforeDelete,
                    sourceLifecycleRevision,
                    sourceContentDigestSha256,
                    sourceProvenance)))
        {
            return false;
        }

        RunnerLibraryProvenance provenance = new(
            sourceRunnerId,
            sourceContentRevision,
            sourceContentDigestSha256);
        string sourceStateDigestSha256 = RunnerLibraryCanonical.ComputeStateDigest(
            sourceRunnerId,
            sourceDisplayName,
            sourceLifecycle,
            sourceLifecycleBeforeDelete,
            sourceLifecycleRevision,
            sourceContentDigestSha256,
            sourceProvenance);
        const long duplicateRevision = 1;
        string afterStateDigest = RunnerLibraryCanonical.ComputeStateDigest(
            newRunnerId,
            displayName,
            RunnerLibraryLifecycle.Active,
            null,
            duplicateRevision,
            sourceContentDigestSha256,
            provenance);
        RunnerLibraryMutationReceipt unsignedReceipt = new(
            RunnerLibraryCanonical.ReceiptSchema,
            RunnerLibraryMutationKind.Duplicate,
            newRunnerId,
            sourceRunnerId,
            mutation.IdempotencyKeyDigestSha256,
            mutation.CommandDigestSha256,
            sourceStateDigestSha256,
            afterStateDigest,
            sourceDisplayName,
            displayName,
            sourceLifecycle,
            RunnerLibraryLifecycle.Active,
            sourceLifecycleBeforeDelete,
            null,
            sourceLifecycleRevision,
            duplicateRevision,
            sourceProvenance,
            provenance,
            1,
            sourceContentDigestSha256,
            committedAtUtc.ToUniversalTime(),
            string.Empty);
        receipt = unsignedReceipt with
        {
            ReceiptDigestSha256 = RunnerLibraryCanonical.ComputeReceiptDigest(unsignedReceipt)
        };
        duplicateState = new RunnerLibraryStoreState(
            displayName,
            RunnerLibraryLifecycle.Active,
            null,
            duplicateRevision,
            receipt.CommittedAtUtc,
            provenance,
            ImmutableArray.Create(new RunnerLibraryMutationLedgerEntry(
                mutation.IdempotencyKeyDigestSha256,
                mutation.CommandDigestSha256,
                receipt)));
        return IsValid(newRunnerId, duplicateState);
    }

    public static bool TryAddDuplicateReceipt(
        CharacterWorkspaceId sourceRunnerId,
        RunnerLibraryStoreState state,
        RunnerLibraryMutationReceipt receipt,
        out RunnerLibraryStoreState replacement)
    {
        replacement = state;
        if (!IsValid(sourceRunnerId, state)
            || receipt.Kind != RunnerLibraryMutationKind.Duplicate
            || receipt.SourceRunnerId != sourceRunnerId
            || state.MutationLedger.Length >= MaximumMutationLedgerEntries
            || state.MutationLedger.Any(entry => string.Equals(
                entry.IdempotencyKeyDigestSha256,
                receipt.IdempotencyKeyDigestSha256,
                StringComparison.Ordinal)))
        {
            return false;
        }

        RunnerLibraryMutationLedgerEntry duplicateEntry = new(
            receipt.IdempotencyKeyDigestSha256,
            receipt.CommandDigestSha256,
            receipt);
        int insertIndex = state.MutationLedger.Length;
        int firstMutableIndex = state.Provenance is null ? 0 : 1;
        for (int index = firstMutableIndex; index < state.MutationLedger.Length; index++)
        {
            RunnerLibraryMutationReceipt existingReceipt = state.MutationLedger[index].Receipt;
            if ((existingReceipt.Kind != RunnerLibraryMutationKind.Duplicate
                    && existingReceipt.BeforeLifecycleRevision
                    >= receipt.BeforeLifecycleRevision)
                || (existingReceipt.Kind == RunnerLibraryMutationKind.Duplicate
                    && existingReceipt.BeforeLifecycleRevision
                    == receipt.BeforeLifecycleRevision
                    && (existingReceipt.CommittedAtUtc > receipt.CommittedAtUtc
                        || (existingReceipt.CommittedAtUtc == receipt.CommittedAtUtc
                            && string.CompareOrdinal(
                                existingReceipt.ReceiptDigestSha256,
                                receipt.ReceiptDigestSha256) > 0))))
            {
                insertIndex = index;
                break;
            }
        }

        RunnerLibraryStoreState candidate = state with
        {
            MutationLedger = state.MutationLedger.Insert(insertIndex, duplicateEntry)
        };
        if (!IsValid(sourceRunnerId, candidate))
        {
            return false;
        }

        replacement = candidate;
        return true;
    }

    public static RunnerLibraryItem ToItem(
        CharacterWorkspaceId runnerId,
        RunnerLibraryStoreState state,
        long contentRevision,
        long savedRevision,
        string contentDigestSha256,
        DateTimeOffset lastContentUpdatedUtc)
    {
        return new RunnerLibraryItem(
            runnerId,
            state.DisplayName,
            state.Lifecycle,
            state.LifecycleBeforeDelete,
            state.LifecycleRevision,
            contentRevision,
            savedRevision,
            contentDigestSha256,
            lastContentUpdatedUtc.ToUniversalTime(),
            state.LastLifecycleUpdatedUtc.ToUniversalTime(),
            state.Provenance);
    }
}
