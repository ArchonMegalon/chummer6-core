using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterAfterRunSettlementCurrentStore
{
    private static void SettlementOwner(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease, OwnerContextStamp stamp)
    {
        if (!stamp.IsValid || lease.Stamp != stamp || owners.Capture() != stamp)
            throw new IOException("after_run_owner_changed");
    }

    private string SettlementContext(string path, string owner)
        => CharacterCreationFinalizationDigest.Compute(new
        { Schema = "chummer.core.after-run-proposal-context/v1", Owner = owner,
            Record = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)), Slot = ReadContinuationSlotUnderLease(path) });

    private string SettlementBinding(string path, OwnerContextStamp stamp)
        => CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = "chummer.core.after-run-settlement-current/v1", stamp,
            Record = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)),
            Slot = ReadContinuationSlotUnderLease(path), UpdatedUtc = File.GetLastWriteTimeUtc(path)
        });

    private void ObserveSettlement(string path, string binding, IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, OwnerContextStamp stamp, GearCurrentEnvironment? current)
    {
        current?.ValidateFreeSpriteReadObservation();
        SettlementOwner(owners, lease, stamp);
        if (SettlementBinding(path, stamp) != binding) throw new IOException("after_run_observation_changed");
    }

    private T WithSettlementEnvironment<T>(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease, CharacterWorkspaceId id,
        Func<WorkspaceStoredDocument, GearCurrentEnvironment?, string, IReadOnlyList<DelegatedGmCharacterEditLedgerEntry>, string, OwnerContextStamp, T> action)
    {
        if (!CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id)) throw new ArgumentException("after_run_workspace_invalid");
        var stamp = lease.Stamp; SettlementOwner(owners, lease, stamp);
        string? path = TryGetPath(stamp.Owner, id);
        if (path is null || !File.Exists(path)) throw new FileNotFoundException("after_run_workspace_missing");
        using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        string binding = SettlementBinding(path, stamp);
        var record = ReadPrivateRecord(path);
        // Historical receipt observation must not migrate, repair or activate custody.
        if (!TryMaterializeRecord(record, out _, out _, out _, out bool migration) || migration
            || record.RetainedRuleSources is { CurrentEnvironment: null })
            throw new InvalidOperationException("after_run_current_environment_unavailable");
        var read = ReadGearWorkspaceUnderLease(stamp.Owner, id, path, owners, lease, stamp, out var ledger, out var environment);
        using var current = environment;
        if (!read.Success || read.Value is not { } saved || saved.ContentRevision != saved.SavedRevision)
            throw new InvalidOperationException("after_run_current_workspace_unavailable");
        return action(saved, current, path, ledger, binding, stamp);
    }

    public CharacterAfterRunSettlementSnapshotReadResult ReadAfterRunSettlementSnapshot(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id)
    {
        try
        {
            return WithSettlementEnvironment(owners, lease, id, (saved, current, path, _, binding, stamp) =>
            {
                ObserveSettlement(path, binding, owners, lease, stamp, current);
                return new CharacterAfterRunSettlementSnapshotReadResult(new(WorkspaceOperationOutcome.Success, saved), SettlementContext(path, stamp.Owner.NormalizedValue));
            });
        }
        catch (FileNotFoundException) { return new(new(WorkspaceOperationOutcome.Missing)); }
        catch (Exception error) when (SettlementExpected(error))
        { return new(new(WorkspaceOperationOutcome.Unavailable, Error: "after_run_current_snapshot_unavailable")); }
    }

    public CharacterAfterRunSettlementWorkspaceReadResult ReadAfterRunSettlement(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id, CharacterAfterRunSettlementIdentity identity,
        ICharacterAfterRunSettlementProposalProjectionSource proposals)
    {
        try
        {
            return WithSettlementEnvironment(owners, lease, id, (saved, current, path, _, binding, stamp) =>
            {
                var result = new WorkspaceCharacterAfterRunSettlementWorkspace(this, proposals, saved, binding, SettlementContext(path, stamp.Owner.NormalizedValue)).Read(id, identity);
                ObserveSettlement(path, binding, owners, lease, stamp, current);
                return result;
            });
        }
        catch (FileNotFoundException) { return new(CharacterAfterRunSettlementWorkspaceOutcome.Missing); }
        catch (Exception error) when (SettlementExpected(error))
        { return new(CharacterAfterRunSettlementWorkspaceOutcome.Unavailable, Error: "after_run_current_read_unavailable"); }
    }

    public CharacterAfterRunSettlementWorkspaceLookupResult LookupAfterRunSettlement(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id, Guid transactionId, string commandDigest)
    {
        try
        {
            return WithSettlementEnvironment(owners, lease, id, (saved, current, path, _, binding, stamp) =>
            {
                var adapter = new WorkspaceCharacterAfterRunSettlementWorkspace(this,
                    new UnavailableCharacterAfterRunSettlementProposalProjectionSource(), saved, binding, SettlementContext(path, stamp.Owner.NormalizedValue));
                // Lookup validates legacy ledger format and local/imported lineage. It
                // never needs the old proposal or recreates a command from today's state.
                var result = adapter.Lookup(id, transactionId, commandDigest);
                ObserveSettlement(path, binding, owners, lease, stamp, current);
                return result;
            });
        }
        catch (FileNotFoundException) { return new(CharacterAfterRunSettlementWorkspaceOutcome.Missing); }
        catch (Exception error) when (SettlementExpected(error))
        { return new(CharacterAfterRunSettlementWorkspaceOutcome.Unavailable, Error: "after_run_current_receipt_unavailable"); }
    }

    public CharacterAfterRunSettlementWorkspaceCommitResult CommitAfterRunSettlement(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterAfterRunSettlementWorkspaceCommitRequest request,
        ICharacterAfterRunSettlementProposalProjectionSource proposals, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        CharacterAfterRunSettlementWorkspaceCommitResult result;
        bool dispatched = false;
        try
        {
            // Freeze typed collections once before consulting any host collaborator.
            request = JsonSerializer.Deserialize<CharacterAfterRunSettlementWorkspaceCommitRequest>(JsonSerializer.Serialize(request))!;
            result = WithSettlementEnvironment(owners, lease, request.WorkspaceId, (saved, current, path, ledger, binding, stamp) =>
            {
                WorkspaceCharacterAfterRunSettlementWorkspace? adapter = null;
                bool ProposalCurrent()
                {
                    var read = adapter!.Read(saved.Id, request.Plan.Identity);
                    return read.Input is not null && CharacterAfterRunSettlementRules.TryCreateQuote(read.Input, out var quote)
                        && CharacterCreationFinalizationDigest.Compute(quote) == CharacterCreationFinalizationDigest.Compute(request.ReviewedQuote);
                }
                adapter = new(this, proposals, saved, binding, SettlementContext(path, stamp.Owner.NormalizedValue), (validated, replacement) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!SupportsWorkspaceAuxiliaryStateAtomicCommit || HasPendingGearEquipment(saved))
                        return new(WorkspaceOperationOutcome.Unavailable, Error: "after_run_atomic_checkpoint_unavailable");
                    long revision = checked(saved.ContentRevision + 1);
                    if (!IsValidAuxiliaryStateTransition(saved.Id, saved.ContentRevision, saved.SavedRevision, revision,
                        saved.Document.AuxiliaryState, replacement.AuxiliaryState, saved.Document, replacement, current?.Recovery))
                        return new(WorkspaceOperationOutcome.Corrupt, Error: "after_run_auxiliary_transition_invalid");
                    ObserveSettlement(path, binding, owners, lease, stamp, current);
                    // Reuse the existing store-private admitted-record writer and its
                    // retained-source/final-replace hooks. The only public command is
                    // the typed settlement above; the adapter has rederived the exact
                    // plan, character accounting and immutable receipt before this call.
                    dispatched = true;
                    WriteCurrentLifestyle(saved, replacement, revision, current, path, ledger, ProposalCurrent,
                        () => { SettlementOwner(owners, lease, stamp);
                            if (SettlementBinding(path, stamp) != binding) throw new IOException("after_run_binding_changed"); },
                        cancellationToken);
                    return new(WorkspaceOperationOutcome.Success, new(saved.Id, DateTimeOffset.UtcNow, revision, revision));
                });
                var committed = adapter.Commit(request);
                if (!dispatched) ObserveSettlement(path, binding, owners, lease, stamp, current);
                return committed;
            });
        }
        catch (Exception error) when (SettlementExpected(error))
        { result = new(CharacterAfterRunSettlementWorkspaceOutcome.Unavailable, Error: "after_run_current_commit_unavailable"); }
        if (!dispatched) return result;
        // Dispose the original workspace/admission before observing the durable
        // original transaction. Never redispatch after response loss or cancellation.
        var recovered = LookupAfterRunSettlement(owners, lease, request.WorkspaceId, request.Plan.TransactionId, request.CommandDigest);
        return recovered.Outcome == CharacterAfterRunSettlementWorkspaceOutcome.Replayed
            ? new(result.Outcome == CharacterAfterRunSettlementWorkspaceOutcome.Applied
                    ? CharacterAfterRunSettlementWorkspaceOutcome.Applied : CharacterAfterRunSettlementWorkspaceOutcome.Replayed,
                recovered.CurrentWorkspaceRevision, recovered.ExistingCommandDigest, recovered.ReviewedQuote, recovered.Receipt)
            : new(CharacterAfterRunSettlementWorkspaceOutcome.Indeterminate, recovered.CurrentWorkspaceRevision,
                Error: "after_run_original_commit_not_observed");
    }

    private static bool SettlementExpected(Exception error)
        => error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
            or OperationCanceledException or System.Text.Json.JsonException or OverflowException;
}
