using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    /// <summary>
    /// Capture an owner-private, path-free source closure under the actual owner
    /// and workspace leases. This does not authorize its upload; the authenticated
    /// host still enforces source-specific copy admission and selection CAS.
    /// V1 source-bearing history fails explicitly; local V1 recovery is unchanged.
    /// </summary>
    public WorkspacePrivateRuleSourceClosure ExportPrivateRuleSourceClosure(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, CharacterWorkspaceId id,
        int maximumBytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (maximumBytes is <= 0 or > WorkspacePrivateRuleSourceClosureBinding.MaximumBytes
                || !expectedOwner.IsValid || owners is not IOwnerContextLeaseAccessor authority)
                throw WorkspacePrivateRuleSourceClosure.Rejected();
            bool admitted = authority.TryAcquire(expectedOwner, out var lease);
            using (lease)
            {
                if (!admitted || lease is null || lease.Stamp != expectedOwner)
                    throw WorkspacePrivateRuleSourceClosure.Rejected();
                OwnerScope owner = expectedOwner.Owner.IsLocalSingleUser
                    ? expectedOwner.Owner : new OwnerScope(expectedOwner.Owner.NormalizedValue);
                RequireRetainedRuleSourceStorage();
                if ((!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner))
                    || _retainedCharacterQueries is null || TryGetPath(owner, id) is not { } path
                    || !TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                    throw WorkspacePrivateRuleSourceClosure.Rejected();
                using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
                using var history = WorkspacePrivateRuleSourceClosureCompletion.BeginHistoryObservation();
                var before = ReadPrivateRecord(path);
                string beforeDigest = CharacterCreationFinalizationDigest.Compute(before);
                var read = ReadWorkspaceUnderLease(owner, id, path, out var ledger);
                if (!read.Success || read.Value is not { } stored) throw WorkspacePrivateRuleSourceClosure.Rejected();
                var record = ReadPrivateRecord(path);
                if (CharacterCreationFinalizationDigest.Compute(record) != beforeDigest) throw WorkspacePrivateRuleSourceClosure.Rejected();
                using var recovery = OpenRecordRuleSources(owner, id, record);
                var archives = new List<byte[]>();
                long total = 0;
                foreach (var reference in record.RetainedRuleSources?.References ?? [])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if ((total = checked(total + reference.ByteLength)) > maximumBytes
                        || recovery is null || !recovery.TryRecoverRuleSources(reference.CaptureDigest, out var execution)
                        || execution is null) throw WorkspacePrivateRuleSourceClosure.Rejected();
                    byte[] bytes = execution.ExportPrivateInputs();
                    // The reference keeps its exact byte identity. No migration or remapping here.
                    if (bytes.LongLength != reference.ByteLength)
                        throw WorkspacePrivateRuleSourceClosure.Rejected();
                    if (!FileSystemCharacterSourceDataResolver.ImportCaptureInputs(bytes, reference.CaptureDigest).IsPortable)
                        throw new WorkspacePrivateRuleSourceClosureLegacyFormatException();
                    archives.Add(bytes);
                }
                var campaigns = new List<CharacterCreationCommittedCampaign>();
                campaigns.AddRange(record.RetainedRuleSources?.CommittedDraftCampaigns ?? []);
                if (record.RetainedRuleSources?.CommittedCampaign is { } final) campaigns.Add(final);
                var snapshot = new WorkspaceContinuationSnapshot(owner.NormalizedValue,
                    new(stored.Id, stored.Document, stored.LastUpdatedUtc, stored.ContentRevision, stored.SavedRevision),
                    ledger.Select(entry => entry.Receipt).ToArray())
                { DelegatedGmHistorySegmentStarts = stored.DelegatedGmHistorySegmentStarts.ToArray() };
                var result = WorkspacePrivateRuleSourceClosure.Create(snapshot, archives, campaigns, maximumBytes, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (lease.Stamp != expectedOwner || CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)) != beforeDigest
                    || recovery is not null && !recovery.ValidateRetainedRuleSources()) throw WorkspacePrivateRuleSourceClosure.Rejected();
                return result;
            }
        }
        catch (Exception error) when (WorkspacePrivateRuleSourceClosure.IsFailure(error))
        { throw WorkspacePrivateRuleSourceClosure.Rejected(); }
    }

    // Reuse the store's exact receipt/source visitor. Neither a client-supplied
    // reference list nor a digest assertion can add/remove historical inputs.
    internal static void ValidatePrivateClosureBindings(WorkspaceContinuationSnapshot snapshot,
        IEnumerable<string> digests, CharacterCreationCommittedCampaign[] campaigns)
    {
        var workspace = snapshot.Workspace;
        var state = workspace.Document.AuxiliaryState;
        var uses = CollectRetainedRuleSourceUses(state);
        if (!digests.ToHashSet(StringComparer.Ordinal).SetEquals(uses.Select(use => use.CaptureDigest)))
            throw WorkspacePrivateRuleSourceClosure.Rejected();
        var expected = CollectDraftCampaignBindings(state);
        if (state.CharacterCreationFinalizationArchive?.CompletionAuthority?.Proposal.CampaignAuthority is { } final)
        {
            if (state.CharacterCreationFinalizationReceipts is not { Count: 1 } receipts
                || !expected.TryAdd(receipts[0].Receipt.ReceiptDigest, final))
                throw WorkspacePrivateRuleSourceClosure.Rejected();
        }
        if (campaigns.Length != expected.Count) throw WorkspacePrivateRuleSourceClosure.Rejected();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var campaign in campaigns)
        {
            if (!CharacterCreationCampaignRecovery.IsWellFormed(campaign)
                || !seen.Add(campaign.ReceiptDigest)
                || !expected.TryGetValue(campaign.ReceiptDigest, out var binding) || binding != campaign.Binding
                || binding.RetainedIdentity.OwnerId != snapshot.OwnerId
                || binding.RetainedIdentity.WorkspaceId != workspace.Id
                || !uses.Any(use => use.ReceiptDigest == campaign.ReceiptDigest
                    && use.CaptureDigest == binding.RetainedIdentity.SourceDigest))
                throw WorkspacePrivateRuleSourceClosure.Rejected();
        }
    }
}
