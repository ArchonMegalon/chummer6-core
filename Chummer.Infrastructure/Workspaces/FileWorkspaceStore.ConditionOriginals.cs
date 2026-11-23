using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Session;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    private const string ConditionOriginalSuffix5472 = ".condition-originals.v1.json";
    private sealed record ConditionOriginal5472(SessionConditionTransferCanonicalReceipt Receipt,
        SessionPlayAuthority? Authority, string? RecordDigest, bool Committed);
    // Full retired private record retains source references, custody, XML and opaque auxiliary data.
    private sealed record ConditionRetired5472(string IncarnationId, string SlotGeneration, PersistedWorkspaceRecord Record);
    private sealed record ConditionOriginalArchive5472(int Schema, string OwnerId, string CharacterId,
        IReadOnlyList<ConditionOriginal5472> Entries, IReadOnlyList<ConditionRetired5472> Retired, string Digest);
    private static ConditionOriginalArchive5472 SealConditionOriginals5472(ConditionOriginalArchive5472 archive)
        => archive with { Digest = SessionPlayEncoding.Digest(archive with { Digest = "" }) };
    private ConditionOriginalArchive5472 ReadConditionOriginals5472(string path, string owner, CharacterWorkspaceId id)
    {
        string archivePath = path + ConditionOriginalSuffix5472;
        EnsurePathContained(_stateDirectory, archivePath, "Condition originals"); ThrowIfLinkOrReparsePoint(archivePath, "Condition originals");
        if (!File.Exists(archivePath)) return SealConditionOriginals5472(new(1, owner, id.Value, [], [], ""));
        using var stream = File.OpenRead(archivePath);
        if (stream.Length > 64 * 1024 * 1024) throw new IOException("condition_original_capacity");
        var archive = JsonSerializer.Deserialize<ConditionOriginalArchive5472>(stream, SessionPlayEncoding.Json);
        if (archive is not { Schema: 1, Entries: not null, Retired: not null } || archive.OwnerId != owner || archive.CharacterId != id.Value
            || archive.Entries.Count > 1024 || archive.Retired.Count > 64 || archive.Digest != SealConditionOriginals5472(archive).Digest)
            throw new IOException("condition_original_corrupt");
        var ids = new HashSet<string>();
        foreach (var entry in archive.Entries)
        {
            SessionConditionTransferRules.ValidateReceipt(entry.Receipt, entry.Receipt.Intent);
            if (entry.Receipt.Intent.Review.OwnerId != owner || entry.Receipt.Intent.Review.CharacterId != id.Value
                || !ids.Add(entry.Receipt.Intent.Review.OperationId)) throw new IOException("condition_original_identity_changed");
            if (entry.Authority is { } authority)
            {
                var acknowledgement = new SessionConditionTransferReceipt(entry.Receipt, authority, "");
                acknowledgement = acknowledgement with { Digest = SessionPlayEncoding.Digest(acknowledgement) };
                SessionConditionTransferRules.ValidateAcknowledgement(acknowledgement, entry.Receipt.Intent);
            }
        }
        return archive;
    }
    private void WriteConditionOriginals5472(string path, ConditionOriginalArchive5472 archive)
    {
        if (archive.Entries.Count > 1024 || archive.Retired.Count > 64) throw new IOException("condition_original_capacity");
        archive = SealConditionOriginals5472(archive);
        WriteOriginalArchive5472(path + ConditionOriginalSuffix5472, archive, 64 * 1024 * 1024);
        if (ReadConditionOriginals5472(path, archive.OwnerId, new(archive.CharacterId)).Digest != archive.Digest)
            throw new IOException("condition_original_readback_changed");
    }
    private void ReserveConditionOriginal5472(string path, SessionConditionTransferCanonicalReceipt receipt,
        WorkspaceStoredDocument proposed, PersistedWorkspaceRecord record, CharacterCurrentRuleEnvironmentReadmissionResult readmission,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string, SessionPlayAuthority> project)
    {
        var capture = readmission.AfterCapture!; var execution = readmission.AfterExecution!;
        var resolver = capture.CreateCurrentEnvironmentResolution(execution.ExecutionProfile, execution.ExecutionProfileFingerprint)?.Resolver
            ?? throw new IOException("condition_original_resolution_unavailable");
        var authority = project(proposed, resolver, receipt.EnvironmentDigest);
        var archive = ReadConditionOriginals5472(path, receipt.Intent.Review.OwnerId, proposed.Id);
        if (archive.Entries.Any(x => x.Receipt.Intent.Review.OperationId == receipt.Intent.Review.OperationId))
            throw new IOException("condition_original_requires_reconciliation");
        var original = new ConditionOriginal5472(receipt, authority, CharacterCreationFinalizationDigest.Compute(record), false);
        var ack = new SessionConditionTransferReceipt(receipt, authority, "");
        SessionConditionTransferRules.ValidateAcknowledgement(ack with { Digest = SessionPlayEncoding.Digest(ack) }, receipt.Intent);
        WriteConditionOriginals5472(path, archive with { Entries = archive.Entries.Append(original).ToArray() });
    }
    private ConditionOriginal5472? ArchivedConditionOriginal5472(string path, CharacterWorkspaceId id, string owner, string operationId)
        => ReadConditionOriginals5472(path, owner, id).Entries.SingleOrDefault(x => x.Receipt.Intent.Review.OperationId == operationId);

    private void PreserveConditionOriginalsBeforeDelete5472(OwnerScope owner, CharacterWorkspaceId id, string path, WorkspaceStoredDocument saved)
    {
        GuardRecoveryDeletion5472(path, saved);
        var archive = ReadConditionOriginals5472(path, owner.NormalizedValue, id);
        var entries = archive.Entries.ToList(); var local = saved.LocalHistory ?? throw new IOException("condition_original_custody_missing");
        foreach (var receipt in saved.Document.AuxiliaryState.SessionConditionTransfers ?? [])
        {
            // Imported originals stay in the full retired record, never become local replay grants.
            if (!saved.CanReplayReceipt(receipt.RecordedRevision) || receipt.Intent.Review.Canonical.IncarnationId != local.IncarnationId) continue;
            if (receipt.Intent.Review.OwnerId != owner.NormalizedValue || receipt.Intent.Review.CharacterId != id.Value)
                throw new IOException("condition_original_identity_changed");
            int index = entries.FindIndex(x => x.Receipt.Intent.Review.OperationId == receipt.Intent.Review.OperationId);
            if (index >= 0)
            {
                if (SessionPlayEncoding.Digest(entries[index].Receipt) != SessionPlayEncoding.Digest(receipt))
                    throw new IOException("condition_original_result_changed");
                entries[index] = entries[index] with { Committed = true };
            }
            else entries.Add(new(receipt, null, null, true)); // Older completed5460 history: exact canonical original is still preserved.
        }
        if (entries.Any(x => !x.Committed)) throw new IOException("condition_original_requires_reconciliation");
        if (entries.Count == 0 && saved.Document.AuxiliaryState.SessionConditionTransfers is not { Count: > 0 }) return;
        var record = ReadPrivateRecord(path);
        var retired = new ConditionRetired5472(local.IncarnationId,
            ReadContinuationSlotUnderLease(path) ?? throw new IOException("condition_original_slot_missing"), record);
        var previous = archive.Retired.LastOrDefault(x => x.IncarnationId == local.IncarnationId);
        if (previous is not null && SessionPlayEncoding.Digest(previous) != SessionPlayEncoding.Digest(retired))
        {
            // A previously interrupted deletion may have archived an earlier state. Preserve it too.
            if (archive.Retired.Any(x => x.IncarnationId == local.IncarnationId && SessionPlayEncoding.Digest(x) == SessionPlayEncoding.Digest(retired))) return;
        }
        var history = archive.Retired.Any(x => SessionPlayEncoding.Digest(x) == SessionPlayEncoding.Digest(retired))
            ? archive.Retired : archive.Retired.Append(retired).ToArray();
        WriteConditionOriginals5472(path, archive with { Entries = entries, Retired = history });
        // DeleteCore rotates the slot and unlinks only after this flushed archive readback.
    }
    private SessionConditionTransferBinding? ReadRetiredConditionBinding5472(string path, CharacterWorkspaceId id,
        string owner, SessionConditionTransferCanonicalReceipt receipt)
    {
        var archive = ReadConditionOriginals5472(path, owner, id);
        var entry = archive.Entries.SingleOrDefault(x => x.Receipt.Intent.Review.OperationId == receipt.Intent.Review.OperationId);
        if (entry is not { Committed: true, Authority: not null } || SessionPlayEncoding.Digest(entry.Receipt) != SessionPlayEncoding.Digest(receipt)) return null;
        var custody = archive.Retired.Any(x => x.IncarnationId == receipt.Intent.Review.Canonical.IncarnationId
            && x.SlotGeneration == receipt.Intent.Review.Canonical.SlotGeneration
            && x.Record.LocalHistory?.IncarnationId == x.IncarnationId
            && (x.Record.AuxiliaryState?.SessionConditionTransfers ?? []).Any(r => r.Digest == receipt.Digest));
        if (!custody) throw new IOException("condition_original_custody_missing");
        return receipt.Intent.Review.Canonical with { Authority = entry.Authority, ContentRevision = receipt.RecordedRevision,
            SavedRevision = receipt.RecordedRevision };
    }
}
