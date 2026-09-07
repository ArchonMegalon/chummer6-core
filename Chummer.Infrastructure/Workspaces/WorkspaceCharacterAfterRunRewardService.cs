using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed class WorkspaceCharacterAfterRunRewardService : ICharacterAfterRunRewardService
{
    private readonly IWorkspaceStore _store;

    public WorkspaceCharacterAfterRunRewardService(IWorkspaceStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public CharacterAfterRunRewardReadResult Read(CharacterWorkspaceId workspaceId)
    {
        WorkspaceStoreReadResult read = ReadStore(workspaceId);
        if (!read.Success || read.Value is not { } saved)
            return new(Map(read.Outcome), Error: read.Error);
        if (!IsValidHistory(saved))
            return new(CharacterAfterRunRewardOutcome.Corrupt, Error: "reward_receipt_ledger_corrupt");
        return CharacterAfterRunRewardProjector.TryRead(saved, out var snapshot, out string error)
            ? new(CharacterAfterRunRewardOutcome.Available, snapshot)
            : new(ProjectionOutcome(error), Error: error);
    }

    public CharacterAfterRunRewardPreviewResult Preview(CharacterAfterRunRewardPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CharacterAfterRunRewardProjector.IsValidPreviewRequest(request))
            return new(CharacterAfterRunRewardOutcome.Conflict, Error: "reward_command_invalid");
        WorkspaceStoreReadResult read = ReadStore(request.WorkspaceId);
        if (!read.Success || read.Value is not { } saved)
            return new(Map(read.Outcome), Error: read.Error);
        if (!IsValidHistory(saved))
            return new(CharacterAfterRunRewardOutcome.Corrupt,
                CurrentWorkspaceRevision: saved.ContentRevision, Error: "reward_receipt_ledger_corrupt");
        if (!CharacterAfterRunRewardProjector.TryRead(saved, out var snapshot, out string error))
            return new(ProjectionOutcome(error), CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
        var command = new CharacterAfterRunRewardCommand(
            request.WorkspaceId, request.OperationId, request.RewardId, snapshot.ContentRevision,
            snapshot.SourceDigest, snapshot.AuxiliaryStateDigest, request.KarmaAmount, request.NuyenAmount,
            request.ExpenseDateLocal, request.Reason, request.ExistingKarmaExpenseId, request.ExistingNuyenExpenseId,
            Kind: request.Kind);
        if (!CharacterAfterRunRewardProjector.TryPreview(saved, command, out var preview, out error))
            return new(ProjectionOutcome(error), CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
        if (_store is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            { SupportsWorkspaceAuxiliaryStateAtomicCommit: true })
            return new(CharacterAfterRunRewardOutcome.Unavailable,
                CurrentWorkspaceRevision: saved.ContentRevision, Error: "reward_atomic_commit_unavailable");
        return new(CharacterAfterRunRewardOutcome.Available, preview, saved.ContentRevision);
    }

    public CharacterAfterRunRewardResult Commit(
        CharacterAfterRunRewardCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        // Admit bounded syntax before serialization or I/O. Valid historical
        // commands still reach durable lookup before any current-source checks.
        if (!CharacterAfterRunRewardProjector.IsValidCommand(command))
            return new(CharacterAfterRunRewardOutcome.Conflict, Error: "reward_command_invalid");
        string digest = command.CommandDigest();
        CharacterAfterRunRewardResult prior = Lookup(command.WorkspaceId, command.OperationId, digest);
        if (prior.Outcome != CharacterAfterRunRewardOutcome.NotFound)
            return prior;
        WorkspaceStoreReadResult read = ReadStore(command.WorkspaceId);
        if (!read.Success || read.Value is not { } saved)
            return new(Map(read.Outcome), Error: read.Error);
        if (!IsValidHistory(saved))
            return new(CharacterAfterRunRewardOutcome.Corrupt,
                CurrentWorkspaceRevision: saved.ContentRevision, Error: "reward_receipt_ledger_corrupt");
        if (saved.ContentRevision != command.ExpectedWorkspaceRevision
            || saved.SavedRevision != saved.ContentRevision)
            return new(CharacterAfterRunRewardOutcome.Conflict,
                CurrentWorkspaceRevision: saved.ContentRevision, Error: "reward_workspace_revision_conflict");
        if (!CharacterAfterRunRewardProjector.TryBuild(saved, command,
                out WorkspaceDocument replacement, out var receipt, out string error))
            return new(ProjectionOutcome(error), CurrentWorkspaceRevision: saved.ContentRevision, Error: error);
        if (_store is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            { SupportsWorkspaceAuxiliaryStateAtomicCommit: true } atomicStore)
            return new(CharacterAfterRunRewardOutcome.Unavailable,
                CurrentWorkspaceRevision: saved.ContentRevision, Error: "reward_atomic_commit_unavailable");

        // The final cancellation boundary is immediately before the one atomic
        // write. Once entered, durable evidence decides the result even if the
        // caller cancels or loses the write response.
        cancellationToken.ThrowIfCancellationRequested();
        WorkspaceStoreMutationResult committed;
        try
        {
            committed = atomicStore.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                command.WorkspaceId, command.ExpectedWorkspaceRevision,
                command.ExpectedAuxiliaryStateDigest, replacement);
        }
        catch (Exception exception) when (IsStoreFailure(exception) || exception is OperationCanceledException)
        {
            return Recover(command, digest, CharacterAfterRunRewardOutcome.Unavailable,
                "reward_commit_result_unknown");
        }

        // Re-read the persisted receipt even for an apparent success. A receipt
        // in an adapter response cannot substitute for the durable ledger.
        CharacterAfterRunRewardResult recovered = Lookup(command.WorkspaceId, command.OperationId, digest);
        if (recovered.Outcome == CharacterAfterRunRewardOutcome.Replayed)
        {
            bool knownCommit = committed.Success && committed.Entry is { } entry
                && entry.Id == command.WorkspaceId
                && entry.ContentRevision == receipt.CommittedWorkspaceRevision
                && entry.SavedRevision == receipt.CommittedWorkspaceRevision;
            return knownCommit ? recovered with { Outcome = CharacterAfterRunRewardOutcome.Applied } : recovered;
        }
        if (recovered.Outcome != CharacterAfterRunRewardOutcome.NotFound)
            return recovered;
        return new(committed.Success ? CharacterAfterRunRewardOutcome.Unavailable : Map(committed.Outcome),
            CurrentWorkspaceRevision: recovered.CurrentWorkspaceRevision,
            Error: "reward_commit_not_observed");
    }

    public CharacterAfterRunRewardResult Lookup(
        CharacterWorkspaceId workspaceId,
        Guid operationId,
        string commandDigest)
    {
        if (!CharacterAfterRunRewardProjector.IsValidWorkspaceId(workspaceId)
            || operationId == Guid.Empty || !CharacterAfterRunRewardProjector.IsDigest(commandDigest))
            return new(CharacterAfterRunRewardOutcome.Corrupt, Error: "reward_lookup_invalid");
        WorkspaceStoreReadResult read = ReadStore(workspaceId);
        if (!read.Success || read.Value is not { } saved)
            return new(Map(read.Outcome), Error: read.Error);
        if (!IsValidHistory(saved))
            return new(CharacterAfterRunRewardOutcome.Corrupt,
                CurrentWorkspaceRevision: saved.ContentRevision, Error: "reward_receipt_ledger_corrupt");
        CharacterAfterRunRewardReceipt? existing = saved.Document.AuxiliaryState.CharacterAfterRunRewardReceipts?
            .SingleOrDefault(receipt => receipt.OperationId == operationId);
        if (existing is null)
            return new(CharacterAfterRunRewardOutcome.NotFound, CurrentWorkspaceRevision: saved.ContentRevision);
        if (!string.Equals(existing.CommandDigest, commandDigest, StringComparison.Ordinal))
            return new(CharacterAfterRunRewardOutcome.IdempotencyConflict,
                CurrentWorkspaceRevision: saved.ContentRevision, Error: "reward_operation_command_conflict");
        // JSON reconstruction can materialize IReadOnlyList as a mutable List.
        // Return a detached read-only selection so callers cannot mutate either
        // the observed receipt or a store implementation's retained objects.
        CharacterAfterRunRewardReceipt detached = existing with
        {
            Before = existing.Before with
            {
                SelectedExpenses = Array.AsReadOnly(existing.Before.SelectedExpenses.ToArray())
            }
        };
        return new(CharacterAfterRunRewardOutcome.Replayed, detached, saved.ContentRevision);
    }

    private CharacterAfterRunRewardResult Recover(CharacterAfterRunRewardCommand command, string digest,
        CharacterAfterRunRewardOutcome fallback, string error)
    {
        CharacterAfterRunRewardResult recovered = Lookup(command.WorkspaceId, command.OperationId, digest);
        return recovered.Outcome == CharacterAfterRunRewardOutcome.NotFound
            ? new(fallback, CurrentWorkspaceRevision: recovered.CurrentWorkspaceRevision, Error: error)
            : recovered;
    }

    private WorkspaceStoreReadResult ReadStore(CharacterWorkspaceId workspaceId)
    {
        if (!CharacterAfterRunRewardProjector.IsValidWorkspaceId(workspaceId))
            return new(WorkspaceOperationOutcome.Corrupt, Error: "reward_workspace_identity_invalid");
        try
        {
            WorkspaceStoreReadResult read = _store.Get(workspaceId);
            if (read.Success && (read.Value!.Id != workspaceId || read.Value.ContentRevision <= 0
                || read.Value.SavedRevision < 0 || read.Value.SavedRevision > read.Value.ContentRevision))
                return new(WorkspaceOperationOutcome.Corrupt, Error: "reward_workspace_identity_mismatch");
            return read;
        }
        catch (Exception exception) when (IsStoreFailure(exception))
        {
            return new(WorkspaceOperationOutcome.Unavailable, Error: "reward_workspace_unavailable");
        }
    }

    private static bool IsValidHistory(WorkspaceStoredDocument saved)
    {
        IReadOnlyList<CharacterAfterRunRewardReceipt>? ledger = saved.Document.AuxiliaryState.CharacterAfterRunRewardReceipts;
        if (!CharacterAfterRunRewardReceiptLedgerIntegrity.IsValidLedger(saved.Id, saved.ContentRevision, ledger))
            return false;
        if (ledger is { Count: > 0 } && saved.SavedRevision < ledger[^1].CommittedWorkspaceRevision)
            return false;
        return ledger is not { Count: > 0 }
               || ledger[^1].CommittedWorkspaceRevision != saved.ContentRevision
               || (CharacterAfterRunRewardProjector.IsSupportedDocument(saved.Document)
                   && string.Equals(ledger[^1].CharacterPayloadDigestAfter,
                       CharacterAfterRunRewardProjector.PayloadDigest(saved.Document.Content), StringComparison.Ordinal));
    }

    private static CharacterAfterRunRewardOutcome ProjectionOutcome(string error)
        => error switch
        {
            "reward_workspace_corrupt" or "reward_receipt_ledger_corrupt" or "reward_projection_failed"
                => CharacterAfterRunRewardOutcome.Corrupt,
            "reward_workspace_not_sr5" or "reward_workspace_not_career" or "reward_receipt_capacity_exhausted"
                or "reward_output_size_exceeded"
                or "reward_karma_quote_unavailable" or "reward_nuyen_quote_unavailable"
                => CharacterAfterRunRewardOutcome.Unavailable,
            _ => CharacterAfterRunRewardOutcome.Conflict
        };

    private static CharacterAfterRunRewardOutcome Map(WorkspaceOperationOutcome outcome)
        => outcome switch
        {
            WorkspaceOperationOutcome.Missing => CharacterAfterRunRewardOutcome.Missing,
            WorkspaceOperationOutcome.Conflict => CharacterAfterRunRewardOutcome.Conflict,
            WorkspaceOperationOutcome.Corrupt => CharacterAfterRunRewardOutcome.Corrupt,
            _ => CharacterAfterRunRewardOutcome.Unavailable
        };

    private static bool IsStoreFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or InvalidOperationException;
}
