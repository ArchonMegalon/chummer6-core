using System.Collections.Concurrent;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterCareerImplantCommerceService(IWorkspaceStore store,
    ICharacterCareerImplantCommerceAuthority authority, IOwnerContextLeaseAccessor owners) : ICharacterCareerImplantCommerceService
{
    private readonly ConcurrentDictionary<string, string> _issued = new(StringComparer.Ordinal);

    public CharacterCareerImplantCommerceCatalogResult QueryCatalog(OwnerContextStamp owner, CharacterCareerImplantCommerceQuery query)
    {
        if (query is null || !owner.IsValid || !owners.TryAcquire(owner, out var lease))
            return new(CharacterCareerImplantCommerceOutcome.Conflict, Error: "implant_owner_changed");
        using (lease)
        {
            if (lease.Stamp != owner || store is not ICharacterCareerImplantCommerceAtomicCommitCapability atomic
                || !atomic.CareerImplantCommerceGuardsInstalled)
                return new(CharacterCareerImplantCommerceOutcome.Unavailable, Error: "implant_guards_not_installed");
            var read = Read(owner, query.WorkspaceId);
            if (!read.Success || read.Value is not { } saved) return new(CharacterCareerImplantCommerceOutcome.Missing, Error: "implant_workspace_unavailable");
            if (!CharacterCareerImplantCommerceTransaction.IsWorkspace(saved) || saved.ContentRevision != query.ExpectedRevision)
                return new(CharacterCareerImplantCommerceOutcome.Conflict, Error: "implant_saved_revision_changed");
            var p = authority.ReadInstalled(saved.Document.Content, saved.ContentRevision);
            if (!p.Exact) return new(CharacterCareerImplantCommerceOutcome.Unavailable, Error: string.Join("; ", p.Blockers));
            var binding = CharacterCareerImplantCommerceTransaction.Bind(owner, saved, p, atomic.CareerImplantCommerceRuntimeDigest);
            var after = Read(owner, query.WorkspaceId);
            if (!after.Success || after.Value is not { } current || !CharacterCareerImplantCommerceTransaction.WorkspaceCurrent(current, binding))
                return new(CharacterCareerImplantCommerceOutcome.Conflict, Error: "implant_catalog_changed");
            return new(CharacterCareerImplantCommerceOutcome.Available, new(binding, p.Rows,
                CharacterCyberwareCommerceRules.MinimumRefundPercentage, CharacterCyberwareCommerceRules.MaximumRefundPercentage,
                CharacterCyberwareCommerceRules.RefundPercentageDecimalPlaces, CharacterCyberwareCommerceRules.DefaultRefundPercentage));
        }
    }

    public CharacterCareerImplantCommercePreviewResult Preview(OwnerContextStamp owner, CharacterCareerImplantCommerceRequest request,
        CharacterCareerImplantCommerceBinding catalogBinding)
    {
        if (!CharacterCareerImplantCommerceTransaction.IsRequest(request) || catalogBinding is null
            || !CharacterCareerImplantCommerceTransaction.StampMatches(owner, catalogBinding) || !owners.TryAcquire(owner, out var lease))
            return new(CharacterCareerImplantCommerceOutcome.Conflict, Error: "implant_owner_or_request_changed");
        using (lease)
        {
            if (lease.Stamp != owner || store is not ICharacterCareerImplantCommerceAtomicCommitCapability atomic
                || !atomic.CareerImplantCommerceGuardsInstalled)
                return new(CharacterCareerImplantCommerceOutcome.Unavailable, Error: "implant_guards_not_installed");
            var read = Read(owner, request.WorkspaceId);
            if (!read.Success || read.Value is not { } saved) return new(CharacterCareerImplantCommerceOutcome.Missing, Error: "implant_workspace_unavailable");
            if (!CharacterCareerImplantCommerceTransaction.IsWorkspace(saved)
                || !CharacterCareerImplantCommerceTransaction.WorkspaceCurrent(saved, catalogBinding)
                || CharacterCareerImplantCommerceTransaction.HasOperation(saved, request.OperationId))
                return new(CharacterCareerImplantCommerceOutcome.Conflict, Error: "implant_catalog_or_operation_changed");
            var p = authority.ReadInstalled(saved.Document.Content, saved.ContentRevision);
            var b = CharacterCareerImplantCommerceTransaction.Bind(owner, saved, p, atomic.CareerImplantCommerceRuntimeDigest);
            if (b != catalogBinding) return new(CharacterCareerImplantCommerceOutcome.Conflict, Error: "implant_source_changed");
            var quote = authority.Quote(p, request, out var error);
            if (quote is null) return new(CharacterCareerImplantCommerceOutcome.Unavailable, Error: error);
            var command = new CharacterCareerImplantCommerceCommand(request, b,
                CharacterCareerImplantCommerceTransaction.PreviewDigest(request, b, quote));
            // Verify feasibility against the actual candidate before offering confirmation; this does not save.
            if (!CharacterCareerImplantCommerceTransaction.TryBuild(owner, saved, command with { ExplicitlyConfirmed = true },
                authority, atomic.CareerImplantCommerceRuntimeDigest, out _, out _, out error))
                return new(CharacterCareerImplantCommerceOutcome.Unavailable, Error: error);
            string key = OperationKey(command);
            if (!_issued.TryAdd(key, CharacterCareerImplantCommerceTransaction.Digest(command)))
                return new(CharacterCareerImplantCommerceOutcome.ReconciliationRequired, Error: "implant_operation_already_issued");
            return new(CharacterCareerImplantCommerceOutcome.Available, new(command, quote));
        }
    }

    public CharacterCareerImplantCommerceCommand Confirm(CharacterCareerImplantCommercePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var c = preview.Command;
        if (c.ExplicitlyConfirmed || c.PreviewDigest != CharacterCareerImplantCommerceTransaction.PreviewDigest(c.Request, c.Binding, preview.Quote)
            || !_issued.TryGetValue(OperationKey(c), out var issued) || issued != CharacterCareerImplantCommerceTransaction.Digest(c))
            throw new InvalidOperationException("The exact reviewed implant quote was not issued by this service.");
        return c with { ExplicitlyConfirmed = true };
    }

    public CharacterCareerImplantCommerceResult Commit(OwnerContextStamp owner, CharacterCareerImplantCommerceCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!CharacterCareerImplantCommerceTransaction.IsCommand(command)
            || !CharacterCareerImplantCommerceTransaction.StampMatches(owner, command.Binding) || !owners.TryAcquire(owner, out var lease))
            return new(CharacterCareerImplantCommerceOutcome.Conflict, Error: "implant_confirmation_or_owner_changed");
        using (lease)
        {
            if (lease.Stamp != owner) return new(CharacterCareerImplantCommerceOutcome.Conflict, Error: "implant_owner_changed");
            var read = Read(owner, command.Request.WorkspaceId);
            if (!read.Success || read.Value is not { } saved)
                return new(CharacterCareerImplantCommerceOutcome.ReconciliationRequired, Error: "implant_workspace_unavailable");
            string digest = CharacterCareerImplantCommerceTransaction.CommandDigest(command);
            if (CharacterCareerImplantCommerceTransaction.HasOperation(saved, command.Request.OperationId))
                return CharacterCareerImplantCommerceTransaction.Lookup(owner.Owner, saved, command.Request.OperationId, digest);
            // Consumed issuances remain tombstones. A new process has no issuance and cannot resend an old command.
            string key = OperationKey(command);
            string unconfirmed = CharacterCareerImplantCommerceTransaction.Digest(command with { ExplicitlyConfirmed = false });
            if (!_issued.TryUpdate(key, "dispatched", unconfirmed))
                return new(CharacterCareerImplantCommerceOutcome.ReconciliationRequired, Error: "implant_original_dispatch_requires_lookup");
            if (cancellationToken.IsCancellationRequested)
                return new(CharacterCareerImplantCommerceOutcome.Canceled, Error: "implant_canceled_before_dispatch");
            if (store is not ICharacterCareerImplantCommerceAtomicCommitCapability atomic || !atomic.CareerImplantCommerceGuardsInstalled)
                return new(CharacterCareerImplantCommerceOutcome.Unavailable, Error: "implant_guards_not_installed");
            CharacterCareerImplantCommerceResult? result = null;
            try { result = atomic.CommitCareerImplantCommerce(lease, command, authority, cancellationToken); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException) { }
            // Never trust a materialized candidate or adapter success without durable, source-independent readback.
            var observed = Read(owner, command.Request.WorkspaceId);
            if (!observed.Success || observed.Value is not { } current)
                return new(CharacterCareerImplantCommerceOutcome.ReconciliationRequired, Error: "implant_commit_result_unknown");
            var recovered = CharacterCareerImplantCommerceTransaction.Lookup(owner.Owner, current, command.Request.OperationId, digest);
            if (recovered.Outcome == CharacterCareerImplantCommerceOutcome.Replayed)
                return recovered with { Outcome = result?.Outcome == CharacterCareerImplantCommerceOutcome.Applied
                    ? CharacterCareerImplantCommerceOutcome.Applied : CharacterCareerImplantCommerceOutcome.Replayed };
            return result?.Outcome is CharacterCareerImplantCommerceOutcome.Canceled or CharacterCareerImplantCommerceOutcome.Conflict
                or CharacterCareerImplantCommerceOutcome.Corrupt ? result : recovered;
        }
    }

    public CharacterCareerImplantCommerceResult Lookup(OwnerContextStamp currentOwner, CharacterWorkspaceId workspace,
        Guid originalOperationId, string originalCommandDigest)
    {
        if (!currentOwner.IsValid || originalOperationId == Guid.Empty || !CharacterCareerImplantCommerceTransaction.IsDigest(originalCommandDigest)
            || !owners.TryAcquire(currentOwner, out var lease))
            return new(CharacterCareerImplantCommerceOutcome.Conflict, Error: "implant_lookup_owner_or_identity_invalid");
        using (lease)
        {
            if (lease.Stamp != currentOwner) return new(CharacterCareerImplantCommerceOutcome.Conflict, Error: "implant_owner_changed");
            var read = Read(currentOwner, workspace);
            return read.Success && read.Value is { } saved
                ? CharacterCareerImplantCommerceTransaction.Lookup(currentOwner.Owner, saved, originalOperationId, originalCommandDigest)
                : new(CharacterCareerImplantCommerceOutcome.ReconciliationRequired, Error: "implant_original_receipt_unavailable");
        }
    }
    public CharacterCareerImplantCommerceResult FinalizeDispatch(OwnerContextStamp currentOwner,
        CharacterCareerImplantCommerceCommand originalCommand, CancellationToken cancellationToken = default)
    {
        if (!CharacterCareerImplantCommerceTransaction.OwnerMatches(currentOwner, originalCommand)
            || !owners.TryAcquire(currentOwner, out var lease))
            return new(CharacterCareerImplantCommerceOutcome.Conflict, Error: "implant_finalization_owner_changed");
        using (lease)
        {
            if (lease.Stamp != currentOwner || store is not ICharacterCareerImplantCommerceAtomicCommitCapability atomic
                || !atomic.CareerImplantCommerceGuardsInstalled)
                return new(CharacterCareerImplantCommerceOutcome.Unavailable, Error: "implant_guards_not_installed");
            // This can race a dispatch in another service/process. Only the store's workspace lease may seal absence.
            try { _ = atomic.FinalizeCareerImplantCommerceDispatch(lease, originalCommand, cancellationToken); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException) { }
            var read = Read(currentOwner, originalCommand.Request.WorkspaceId);
            return read.Success && read.Value is { } saved
                ? CharacterCareerImplantCommerceTransaction.Lookup(currentOwner.Owner, saved,
                    originalCommand.Request.OperationId, CharacterCareerImplantCommerceTransaction.CommandDigest(originalCommand))
                : new(CharacterCareerImplantCommerceOutcome.ReconciliationRequired, Error: "implant_finalization_result_unknown");
        }
    }

    private WorkspaceStoreReadResult Read(OwnerContextStamp owner, CharacterWorkspaceId workspace)
        => owner.Owner.IsLocalSingleUser ? store.Get(workspace) : store.Get(owner.Owner, workspace);
    private static string OperationKey(CharacterCareerImplantCommerceCommand c)
        => CharacterCareerImplantCommerceTransaction.Digest(new { c.Binding.OwnerId, c.Binding.LocalSingleUser, c.Request.WorkspaceId, c.Request.OperationId });
}
