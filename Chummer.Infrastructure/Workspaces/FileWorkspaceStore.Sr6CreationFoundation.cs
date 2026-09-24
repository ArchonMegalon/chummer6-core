using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Rulesets.Sr6;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    public CharacterCreationFoundationResult<Sr6CreationFoundationCommit> CommitSr6Foundation(
        OwnerScope owner, Sr6CreationFoundationConfirmRequest request)
    {
        if (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner))
            return Sr6CreationFoundationRules.Blocked<Sr6CreationFoundationCommit>(Sr6CreationFoundationBlockers.WorkspaceUnavailable);
        if (!Sr6CreationFoundationIntegrity.TryFreezeRequest(request, out request))
            return Sr6CreationFoundationRules.Blocked<Sr6CreationFoundationCommit>(Sr6CreationFoundationBlockers.ConfirmationRequired);
        var id = request.Binding.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null) return Sr6CreationFoundationRules.Blocked<Sr6CreationFoundationCommit>(Sr6CreationFoundationBlockers.WorkspaceUnavailable);
        bool committed = false;
        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return Sr6CreationFoundationRules.Blocked<Sr6CreationFoundationCommit>(Sr6CreationFoundationBlockers.WorkspaceUnavailable);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved)
                return Sr6CreationFoundationRules.Blocked<Sr6CreationFoundationCommit>(Sr6CreationFoundationBlockers.WorkspaceUnavailable);
            var prior = Sr6CreationFoundationRules.Lookup(saved, request);
            if (prior is not null) return prior;
            if (!Sr6CreationFoundationRules.TryBuild(saved, request, out var replacement, out var decision)
                || !IsValidAuxiliaryState(id, decision.CommittedContentRevision, replacement.AuxiliaryState))
                return Sr6CreationFoundationRules.Blocked<Sr6CreationFoundationCommit>(Sr6CreationFoundationBlockers.StaleBinding);
            var record = BuildPersistedRecord(replacement, decision.CommittedContentRevision,
                decision.CommittedContentRevision, saved.LocalHistory!, delegatedLedger,
                saved.DelegatedGmHistorySegmentStarts);
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting,
                beforeTargetReplace: () =>
                {
                    if (!Sr6CreationFoundationRules.TryBuild(saved, request, out _, out var current)
                        || current.DecisionDigest != decision.DecisionDigest)
                        throw new IOException("SR6 creation inputs changed before commit.");
                });
            committed = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Inspect the durable operation once; never repeat an ambiguous write.
        }
        var observed = owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (observed.Success && observed.Value is { } workspace
            && Sr6CreationFoundationRules.Lookup(workspace, request) is { } recovered)
            return committed && recovered.Value is { } value
                ? recovered with { Value = value with { Replayed = false } } : recovered;
        return Sr6CreationFoundationRules.Blocked<Sr6CreationFoundationCommit>(Sr6CreationFoundationBlockers.PersistenceUnavailable);
    }
}
