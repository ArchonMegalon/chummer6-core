using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerImplantCommerceAtomicCommitCapability
{
    // Root implements only after installing auxiliary integrity, serialization and generic/atomic guards.
    static partial void VerifyCareerImplantCommerceGuards(ref bool installed);
    public bool CareerImplantCommerceGuardsInstalled
    { get { bool installed = false; VerifyCareerImplantCommerceGuards(ref installed); return installed; } }
    public string CareerImplantCommerceRuntimeDigest => CharacterCareerImplantCommerceTransaction.Hash(
        "chummer.sr5-career-implant-commerce-runtime/v1\0" + typeof(CharacterCyberwareCommerceRules).Module.ModuleVersionId
        + "\0" + typeof(CharacterCareerImplantCommerceTransaction).Module.ModuleVersionId + "\0" + typeof(FileWorkspaceStore).Module.ModuleVersionId);

    public CharacterCareerImplantCommerceResult CommitCareerImplantCommerce(IOwnerContextLease ownerLease,
        CharacterCareerImplantCommerceCommand command, ICharacterCareerImplantCommerceAuthority authority,
        CancellationToken cancellationToken = default)
    {
        if (!CareerImplantCommerceGuardsInstalled)
            return new(CharacterCareerImplantCommerceOutcome.Unavailable, Error: "implant_guards_not_installed");
        var stamp = ownerLease.Stamp;
        if (!CharacterCareerImplantCommerceTransaction.IsCommand(command)
            || !CharacterCareerImplantCommerceTransaction.StampMatches(stamp, command.Binding))
            return new(CharacterCareerImplantCommerceOutcome.Conflict, Error: "implant_owner_or_confirmation_invalid");
        var owner = stamp.Owner;
        var id = command.Request.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null) return new(CharacterCareerImplantCommerceOutcome.Missing);
        bool replaced = false, canceled = false;
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegated);
            if (!read.Success || read.Value is not { } saved)
                return new(CharacterCareerImplantCommerceOutcome.ReconciliationRequired, Error: "implant_workspace_unavailable");
            if (CharacterCareerImplantCommerceTransaction.HasOperation(saved, command.Request.OperationId))
                return CharacterCareerImplantCommerceTransaction.Lookup(owner, saved, command.Request.OperationId,
                    CharacterCareerImplantCommerceTransaction.CommandDigest(command));
            cancellationToken.ThrowIfCancellationRequested();
            if (!CharacterCareerImplantCommerceTransaction.TryBuild(stamp, saved, command, authority, CareerImplantCommerceRuntimeDigest,
                out var replacement, out var receipt, out var error))
                return new(CharacterCareerImplantCommerceOutcome.Conflict, Error: error);
            if (!IsValidAuxiliaryState(id, receipt!.CommittedRevision, replacement!.AuxiliaryState))
                return new(CharacterCareerImplantCommerceOutcome.Corrupt, Error: "implant_auxiliary_invalid");
            var record = BuildPersistedRecord(replacement, receipt.CommittedRevision, receipt.CommittedRevision,
                saved.LocalHistory!, delegated, saved.DelegatedGmHistorySegmentStarts);
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ownerLease.Stamp != stamp || !CareerImplantCommerceGuardsInstalled)
                    throw new InvalidOperationException("implant_owner_or_guards_changed");
                var finalSource = authority.ReadInstalled(saved.Document.Content, saved.ContentRevision);
                if (!finalSource.Exact || CharacterCareerImplantCommerceTransaction.Bind(stamp, saved, finalSource,
                    CareerImplantCommerceRuntimeDigest) != command.Binding)
                    throw new IOException("implant_source_changed_before_replace");
                var finalQuote = authority.Quote(finalSource, command.Request, out _);
                if (finalQuote is null || CharacterCareerImplantCommerceTransaction.PreviewDigest(
                    command.Request, command.Binding, finalQuote) != command.PreviewDigest)
                    throw new IOException("implant_selected_source_changed_before_replace");
                // Source resolution may observe cancellation. This MUST be the last action before replacement.
                cancellationToken.ThrowIfCancellationRequested();
            }, admittedImplantCommerceReceipt: receipt);
            replaced = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        { canceled = ex is OperationCanceledException; }
        var observed = owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (!observed.Success || observed.Value is not { } current)
            return new(CharacterCareerImplantCommerceOutcome.ReconciliationRequired, Error: "implant_commit_readback_unavailable");
        var recovered = CharacterCareerImplantCommerceTransaction.Lookup(owner, current, command.Request.OperationId,
            CharacterCareerImplantCommerceTransaction.CommandDigest(command));
        if (recovered.Outcome == CharacterCareerImplantCommerceOutcome.Replayed)
            return recovered with { Outcome = replaced ? CharacterCareerImplantCommerceOutcome.Applied : CharacterCareerImplantCommerceOutcome.Replayed };
        return canceled && recovered.Outcome == CharacterCareerImplantCommerceOutcome.ReconciliationRequired
            ? new(CharacterCareerImplantCommerceOutcome.Canceled, Error: "implant_canceled_without_replacement") : recovered;
    }

    public CharacterCareerImplantCommerceResult FinalizeCareerImplantCommerceDispatch(IOwnerContextLease ownerLease,
        CharacterCareerImplantCommerceCommand originalCommand, CancellationToken cancellationToken = default)
    {
        var stamp = ownerLease.Stamp;
        if (!CareerImplantCommerceGuardsInstalled || !CharacterCareerImplantCommerceTransaction.OwnerMatches(stamp, originalCommand))
            return new(CharacterCareerImplantCommerceOutcome.Conflict, Error: "implant_finalization_owner_or_guards_changed");
        var owner = stamp.Owner;
        var id = originalCommand.Request.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null) return new(CharacterCareerImplantCommerceOutcome.Missing);
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegated);
            if (!read.Success || read.Value is not { } saved)
                return new(CharacterCareerImplantCommerceOutcome.ReconciliationRequired, Error: "implant_workspace_unavailable");
            if (CharacterCareerImplantCommerceTransaction.HasOperation(saved, originalCommand.Request.OperationId))
                return CharacterCareerImplantCommerceTransaction.Lookup(owner, saved, originalCommand.Request.OperationId,
                    CharacterCareerImplantCommerceTransaction.CommandDigest(originalCommand));
            cancellationToken.ThrowIfCancellationRequested();
            if (!CharacterCareerImplantCommerceTransaction.TryBuildNeverApplied(stamp, saved, originalCommand,
                out var replacement, out var receipt))
                return new(CharacterCareerImplantCommerceOutcome.Conflict, Error: "implant_finalization_history_changed");
            if (!IsValidAuxiliaryState(id, receipt!.CommittedRevision, replacement!.AuxiliaryState))
                return new(CharacterCareerImplantCommerceOutcome.Corrupt, Error: "implant_auxiliary_invalid");
            var record = BuildPersistedRecord(replacement, receipt.CommittedRevision, receipt.CommittedRevision,
                saved.LocalHistory!, delegated, saved.DelegatedGmHistorySegmentStarts);
            // Absence is authoritative ONLY inside this same cross-process lease. The append fences every later Commit.
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
            {
                if (ownerLease.Stamp != stamp || !CareerImplantCommerceGuardsInstalled)
                    throw new InvalidOperationException("implant_finalization_owner_or_guards_changed");
                cancellationToken.ThrowIfCancellationRequested();
            }, admittedImplantCommerceReceipt: receipt);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException) { }
        var observed = owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        return observed.Success && observed.Value is { } current
            ? CharacterCareerImplantCommerceTransaction.Lookup(owner, current, originalCommand.Request.OperationId,
                CharacterCareerImplantCommerceTransaction.CommandDigest(originalCommand))
            : new(CharacterCareerImplantCommerceOutcome.ReconciliationRequired, Error: "implant_finalization_readback_unavailable");
    }

    private void ValidateImplantCommerceWrite(string path, PersistedWorkspaceRecord record,
        WorkspaceWriteDisposition disposition, CharacterCareerImplantCommerceReceipt? admitted)
    {
        var previous = disposition == WorkspaceWriteDisposition.ReplaceExisting ? ReadPrivateRecord(path) : null;
        var before = previous?.AuxiliaryState?.CharacterCareerImplantCommerceReceipts;
        var after = record.AuxiliaryState?.CharacterCareerImplantCommerceReceipts;
        if (admitted is null)
        {
            // Continuation restore has its own imported-history authority. Root must explicitly preserve that path.
            if (previous is not null && JsonSerializer.Serialize(before) != JsonSerializer.Serialize(after))
                throw new IOException("Implant commerce history requires its typed transaction.");
            return;
        }
        if (previous is null || after is null || after.Count != (before?.Count ?? 0) + 1
            || JsonSerializer.Serialize(after.Take(after.Count - 1)) != JsonSerializer.Serialize(before ?? [])
            || JsonSerializer.Serialize(after[^1]) != JsonSerializer.Serialize(admitted)
            || !CharacterCareerImplantCommerceTransaction.IsCoherent(admitted))
            throw new IOException("Implant commerce must append exactly one admitted receipt.");
        if (admitted.NeverApplied && (record.ContentRevision != previous.ContentRevision + 1
            || record.SavedRevision != record.ContentRevision || record.ContentRevision != admitted.CommittedRevision
            || record.Format != previous.Format || JsonSerializer.Serialize(record.Envelope) != JsonSerializer.Serialize(previous.Envelope)
            || JsonSerializer.Serialize(record.AuxiliaryState! with { CharacterCareerImplantCommerceReceipts = before })
                != JsonSerializer.Serialize(previous.AuxiliaryState ?? Chummer.Contracts.Workspaces.WorkspaceDocumentAuxiliaryState.Empty)))
            throw new IOException("Never-applied dispatch finalization must preserve the complete character state.");
    }
}
