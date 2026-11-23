using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Session;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;
using Chummer.Contracts.Workspaces;
using Tx = Chummer.Application.Characters.CharacterSr5NaturalRecoveryTransaction;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    private SessionConditionTransferBinding RecoverySessionBinding(string path, WorkspaceStoredDocument saved,
        GearCurrentEnvironment? current, OwnerContextStamp stamp,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string, SessionPlayAuthority> project)
    {
        if (current is null || saved.ContentRevision != saved.SavedRevision || HasPendingGearEquipment(saved))
            throw new IOException("recovery_session_retained_source_required");
        var authority = project(saved, RecoverySources(current, null!), ConditionEnvironmentDigest(current));
        SessionPlayInitialConditionSource5412.Validate(authority);
        return new(authority, saved.ContentRevision, saved.SavedRevision, saved.LocalHistory!.IncarnationId,
            ReadContinuationSlotUnderLease(path) ?? throw new IOException("recovery_session_slot_required"),
            saved.Document.AuxiliaryStateDigest, SessionPlayEncoding.Digest(stamp));
    }
    internal SessionNaturalRecoveryCanonicalReview ReviewSessionNaturalRecovery5472(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, Sr5RecoveryRequest request, ICharacterSourceDataResolver sources,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string, SessionPlayAuthority> project)
    {
        var stamp = lease.Stamp; RecoveryOwner(owners, lease, stamp);
        string path = TryGetPath(stamp.Owner, request.WorkspaceId) ?? throw new IOException("recovery_workspace_unavailable");
        using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        var saved = ReadRecoveryUnderLease(path, request.WorkspaceId, owners, lease, stamp, out _, out var environment);
        using var current = environment;
        var binding = RecoverySessionBinding(path, saved, current, stamp, project);
        var review = Tx.Review(stamp.Owner, saved, request, RecoverySources(current, sources), RecoveryRuntime(current), RecoveryBinding(path, stamp, saved, current));
        current!.ValidateReadObservation(); RecoveryOwner(owners, lease, stamp);
        return new(review, binding);
    }
    // Original lookup does not require today's rules, source resolver, or quote. No repair/migration.
    private Sr5RecoveryResult LookupRecoveryOriginal5472(string path, CharacterWorkspaceId id, string owner, Guid operationId, string digest)
    {
        var record = ReadPrivateRecord(path);
        if (!TryMaterializeRecord(record, out var document, out long revision, out long savedRevision, out bool migration)
            || migration || record.LocalHistory is not { } local || !local.IsValid(revision)) return new(Sr5RecoveryOutcome.Unknown);
        var saved = new WorkspaceStoredDocument(id, document, revision, savedRevision,
            new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)) { LocalHistory = local };
        var scope = owner == Chummer.Contracts.Owners.OwnerScope.LocalSingleUser.NormalizedValue
            ? Chummer.Contracts.Owners.OwnerScope.LocalSingleUser : new Chummer.Contracts.Owners.OwnerScope(owner);
        return Tx.Lookup(scope, saved, operationId, digest);
    }
    internal Sr5RecoveryResult LookupSessionNaturalRecovery5472(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        CharacterWorkspaceId id, Guid operationId, string digest)
    {
        try
        {
            var stamp = lease.Stamp; RecoveryOwner(owners, lease, stamp);
            string path = TryGetPath(stamp.Owner, id) ?? throw new IOException("recovery_workspace_unavailable");
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            var result = LookupRecoveryOriginal5472(path, id, stamp.Owner.NormalizedValue, operationId, digest);
            RecoveryOwner(owners, lease, stamp); return result;
        }
        catch (Exception ex) when (RecoveryExpected(ex)) { return new(Sr5RecoveryOutcome.Unknown); }
    }
    internal SessionNaturalRecoveryReceipt ReadSessionNaturalRecovery5472(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id, SessionNaturalRecoveryIntent intent, Sr5RecoveryOperation original,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string, SessionPlayAuthority> project)
    {
        var stamp = lease.Stamp; RecoveryOwner(owners, lease, stamp);
        string path = TryGetPath(stamp.Owner, id) ?? throw new IOException("recovery_workspace_unavailable");
        using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        return ReadRecoveryContinuationUnderLease5472(path, owners, lease, intent, original, project);
    }
    private SessionNaturalRecoveryReceipt ReadRecoveryContinuationUnderLease5472(string path, IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, SessionNaturalRecoveryIntent intent, Sr5RecoveryOperation original,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string, SessionPlayAuthority> project)
    {
        var stamp = lease.Stamp; var id = intent.Command.Request.WorkspaceId;
        var lookup = LookupRecoveryOriginal5472(path, id, stamp.Owner.NormalizedValue, intent.Command.Request.OperationId, Tx.Digest(intent.Command));
        if (lookup.Operation?.Digest != original.Digest) throw new IOException("recovery_original_changed");
        var plan = RequireRecoveryContinuationOriginal5472(path, intent, original);
        var saved = ReadRecoveryUnderLease(path, id, owners, lease, stamp, out _, out var environment);
        using var current = environment;
        if (saved.Document.AuxiliaryState.Sr5NaturalRecovery?.Operations.LastOrDefault()?.Digest != original.Digest
            || saved.ContentRevision != original.RecordedRevision || Tx.Hash(saved.Document.Content) != original.PayloadDigest
            || saved.LocalHistory?.IncarnationId != intent.Review.Canonical.IncarnationId
            || ReadContinuationSlotUnderLease(path) != intent.Review.Canonical.SlotGeneration)
            throw new IOException("recovery_committed_state_changed");
        var binding = RecoverySessionBinding(path, saved, current, stamp, project);
        var receipt = new SessionNaturalRecoveryReceipt(intent, original, binding.Authority,
            SessionNaturalRecoveryRules.CanonicalCondition(binding.Authority), "");
        receipt = receipt with { Digest = SessionPlayEncoding.Digest(receipt) };
        SessionNaturalRecoveryRules.ValidateReceipt(receipt, intent);
        if (receipt.Digest != plan.Receipt.Digest) throw new IOException("recovery_continued_authority_changed");
        current!.ValidateReadObservation(); RecoveryOwner(owners, lease, stamp);
        return receipt;
    }
}
