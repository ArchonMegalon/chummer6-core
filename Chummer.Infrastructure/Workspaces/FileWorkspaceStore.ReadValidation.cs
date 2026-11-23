using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    // Ordinary reads repeatedly revisit the same finalized runner during shell
    // bootstrap and selection. Reuse ONLY deterministic historical shape checks
    // for identical bytes in this store instance. This is not source admission,
    // permission to write/restore, or a cache of caller-visible mutable objects.
    private const int MaximumReadValidations = 128;
    private readonly object _readValidationSync = new();
    private readonly HashSet<ReadValidationKey> _readValidations = [];
    private readonly Queue<ReadValidationKey> _readValidationOrder = new();

    // Numeric diagnostics only; no record/document/authority survives a read.
    // The historical Roster names now count all scoped display reads, not
    // unrelated Get/retained/migration work.
    private long _rosterRecordReads, _rosterRecordDecodes, _rosterRecordMaterializations;
    internal long RosterRecordReads => Interlocked.Read(ref _rosterRecordReads);
    internal long RosterRecordDecodes => Interlocked.Read(ref _rosterRecordDecodes);
    internal long RosterRecordMaterializations => Interlocked.Read(ref _rosterRecordMaterializations);

    private static bool TryMaterializeDisplayRecord(PersistedWorkspaceRecord record,
        OrdinaryRosterRecordRead? read, out WorkspaceDocument document,
        out long revision, out long saved, out bool migration)
        => read is null
            ? TryMaterializeRecord(record, out document, out revision, out saved, out migration)
            : read.Materialize(record, out document, out revision, out saved, out migration);

    // Used immediately after the corresponding physical read, with no external
    // observer between read and digest use. Reuse's Read already recomputed the
    // complete canonical record digest; do not serialize it again at that site.
    private static string DisplayRecordDigestAfterRead(PersistedWorkspaceRecord record, OrdinaryRosterRecordRead? read)
        => read?.DigestAfterRead(record)
            ?? CharacterCreationFinalizationDigest.Compute(record, WorkspaceRecordJsonContext.Default.PersistedWorkspaceRecord);

    // Owned by one ReadCurrentDisplay invocation, never a chooser/store/AsyncLocal.
    // Reuse is enabled only for a positively classified, non-migrating ordinary
    // display record. All five file opens/reads still happen at their old sites.
    // Validators, source/owner/slot/timestamp checks and recovery remain outside
    // this decoder memo and still execute in their original order.
    private sealed class OrdinaryRosterRecordRead(FileWorkspaceStore store)
    {
        private string? byteDigest;
        private PersistedWorkspaceRecord? record;
        private WorkspaceDocument? document;
        private string? recordDigest;
        private WorkspacePayloadEnvelope? envelope;
        private WorkspaceDocumentFormat format;
        private long revision, saved;
        private bool refused;

        internal PersistedWorkspaceRecord? Read(Stream stream, out string? digest)
        {
            if (refused) throw new IOException("Ordinary roster read was already refused.");
            try
            {
                using var readTiming = StartupActivities8910.StartActivity("record-read");
                Interlocked.Increment(ref store._rosterRecordReads);
                if (record is null)
                {
                    Interlocked.Increment(ref store._rosterRecordDecodes);
                    using var decodeTiming = StartupActivities8910.StartActivity("record-decode");
                    var decoded = ReadRecordWithDigest(stream, out digest);
                    byteDigest = digest;
                    return decoded;
                }

                // Hash every current byte, including unknown JSON and whitespace.
                // A changed file cannot borrow validation from an earlier decode,
                // even when its typed projection, size and timestamp are identical.
                using var bytesTiming = StartupActivities8910.StartActivity("record-byte-hash");
                digest = Convert.ToHexStringLower(SHA256.HashData(stream));
                bytesTiming?.Stop();
                if (digest != byteDigest)
                    throw new IOException("Ordinary roster record changed during its read.");
                RevalidateMaterialization();
                return record;
            }
            catch { refused = true; throw; }
        }

        internal void RevalidateMaterialization()
        {
            if (refused) throw new IOException("Ordinary roster read was already refused.");
            if (record is null) return;
            try
            {
                using var canonicalTiming = StartupActivities8910.StartActivity("record-canonical");
                if (CharacterCreationFinalizationDigest.Compute(record,
                        WorkspaceRecordJsonContext.Default.PersistedWorkspaceRecord) != recordDigest
                    || document!.PayloadEnvelope != envelope || document.Format != format
                    || !ReferenceEquals(document.AuxiliaryState, record.AuxiliaryState))
                    throw new IOException("Ordinary roster materialization changed during its read.");
            }
            catch { refused = true; throw; }
        }

        internal void AllowReuse(PersistedWorkspaceRecord captured, WorkspaceDocument capturedDocument,
            long capturedRevision, long capturedSaved, string capturedDigest)
        {
            // Target the actual large auxiliary record. A null auxiliary leaf
            // hydrates through the shared Empty singleton; leave that small-record
            // case on the old decode path rather than sharing its mutable leaf.
            if (byteDigest is null || captured.RetainedRuleSources is not null || captured.AuxiliaryState is null)
                return;
            record = captured;
            document = capturedDocument;
            revision = capturedRevision;
            saved = capturedSaved;
            recordDigest = capturedDigest;
            // Envelope is a detached immutable-value copy of all State scalars.
            // The non-null auxiliary graph is exactly the record's graph and is
            // covered by the full canonical record digest on every revalidation.
            envelope = capturedDocument.PayloadEnvelope;
            format = capturedDocument.Format;
        }

        internal string DigestAfterRead(PersistedWorkspaceRecord current)
        {
            if (refused) throw new IOException("Ordinary roster read was already refused.");
            if (record is null)
                return CharacterCreationFinalizationDigest.Compute(current, WorkspaceRecordJsonContext.Default.PersistedWorkspaceRecord);
            if (!ReferenceEquals(record, current))
                throw new IOException("Ordinary roster record identity changed during its read.");
            return recordDigest!;
        }

        internal bool Materialize(PersistedWorkspaceRecord captured, out WorkspaceDocument value,
            out long contentRevision, out long savedRevision, out bool migration)
        {
            if (record is not null)
            {
                if (!ReferenceEquals(record, captured))
                    throw new IOException("Ordinary roster record identity changed during its read.");
                // The immediately preceding Read checked both mutable graphs.
                // Neither is exposed to a caller between that read and here.
                value = document!;
                contentRevision = revision;
                savedRevision = saved;
                migration = false;
                return true;
            }
            Interlocked.Increment(ref store._rosterRecordMaterializations);
            return TryMaterializeRecord(captured, out value, out contentRevision, out savedRevision, out migration);
        }
    }

    internal int CachedReadValidationCount
    {
        get { lock (_readValidationSync) return _readValidations.Count; }
    }

    private readonly record struct ReadValidationKey(
        string OwnerId, CharacterWorkspaceId WorkspaceId, string RecordDigest);

    private static PersistedWorkspaceRecord? ReadRecordWithDigest(Stream stream, out string? digest)
    {
        using var hash = SHA256.Create();
        using var input = new CryptoStream(stream, hash, CryptoStreamMode.Read, leaveOpen: true);
        // Hash the same bytes the decoder receives, in one pass. A second read
        // or file timestamp/length would not bind validation to this snapshot.
        using var typeInfoTiming = StartupActivities8910.StartActivity("record-typeinfo");
        var typeInfo = WorkspaceRecordJsonContext.Default.PersistedWorkspaceRecord;
        typeInfoTiming?.Stop();
        using var deserializeTiming = StartupActivities8910.StartActivity("record-deserialize");
        var record = JsonSerializer.Deserialize(input, typeInfo);
        deserializeTiming?.Stop();
        digest = input.HasFlushedFinalBlock ? Convert.ToHexStringLower(hash.Hash!) : null;
        return record;
    }

    private bool HasReadValidation(ReadValidationKey? key)
    {
        if (key is null) return false;
        lock (_readValidationSync) return _readValidations.Contains(key.Value);
    }

    private void RememberReadValidation(ReadValidationKey? key)
    {
        if (key is null) return;
        lock (_readValidationSync)
        {
            if (!_readValidations.Add(key.Value)) return;
            _readValidationOrder.Enqueue(key.Value);
            if (_readValidationOrder.Count > MaximumReadValidations)
                _readValidations.Remove(_readValidationOrder.Dequeue());
        }
    }
}
