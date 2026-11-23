using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    private readonly bool _retainedRuleSourcesEnabled;
    private readonly ICharacterFileQueries? _retainedCharacterQueries;
    internal const int MaximumRetainedRuleSourceReferences = 4096;
    private const int RetainedWorkspaceRecordSchemaVersion = 5;
    private const int MaximumRetainedWorkspaceRecordBytes = 64 * 1024 * 1024;

    /// <summary>
    /// Explicit opt-in to private retained inputs. The caller must provision a persistent,
    /// private local root (and an owner-only ACL on Windows). Existing constructors stay disabled.
    /// An explicit test root may be temporary; no temporary default is selected by this API.
    /// This legacy overload enables the storage primitive only; Finish Character and
    /// ordinary cold reads also require the overload with explicit character queries.
    /// </summary>
    public static FileWorkspaceStore CreateWithRetainedRuleSources(string persistentStateDirectory)
        => CreateWithRetainedRuleSources(persistentStateDirectory, FileWorkspaceStoreFaultInjector.None);

    /// <summary>Opt in to Finish Character commits and ordinary cold history reads.</summary>
    public static FileWorkspaceStore CreateWithRetainedRuleSources(
        string persistentStateDirectory, ICharacterFileQueries characterQueries)
        => CreateWithRetainedRuleSources(persistentStateDirectory, characterQueries, FileWorkspaceStoreFaultInjector.None);

    /// <summary>Share app custody across independent workspace roots, including cold reopen.</summary>
    public static FileWorkspaceStore CreateWithRetainedRuleSources(string persistentStateDirectory,
        ICharacterFileQueries characterQueries, OriginTrustDomain originTrust)
    {
        ArgumentNullException.ThrowIfNull(originTrust);
        return new(RequireRetainedStateRoot(persistentStateDirectory), FileWorkspaceStoreFaultInjector.None,
            characterQueries, originTrust);
    }

    internal static FileWorkspaceStore CreateWithRetainedRuleSources(string persistentStateDirectory,
        ICharacterFileQueries characterQueries, IFileWorkspaceStoreFaultInjector faultInjector)
        => new(RequireRetainedStateRoot(persistentStateDirectory), faultInjector,
            characterQueries ?? throw new ArgumentNullException(nameof(characterQueries)));

    private FileWorkspaceStore(string persistentStateDirectory, IFileWorkspaceStoreFaultInjector faultInjector,
        ICharacterFileQueries characterQueries, OriginTrustDomain? originTrust = null) : this(persistentStateDirectory, faultInjector, true)
    {
        _retainedCharacterQueries = characterQueries ?? throw new ArgumentNullException(nameof(characterQueries));
        if (originTrust is not null) _originTrust = new(() => originTrust);
        // Registration/cold-start provisioning precedes read-only recovery.
        _ = _originTrust.Value;
    }

    internal static FileWorkspaceStore CreateWithRetainedRuleSources(
        string persistentStateDirectory, IFileWorkspaceStoreFaultInjector faultInjector)
        => new(RequireRetainedStateRoot(persistentStateDirectory), faultInjector, enableRetainedRuleSources: true);

    private FileWorkspaceStore(string persistentStateDirectory, IFileWorkspaceStoreFaultInjector faultInjector,
        bool enableRetainedRuleSources) : this(persistentStateDirectory, faultInjector)
        => _retainedRuleSourcesEnabled = enableRetainedRuleSources;

    private static string RequireRetainedStateRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new ArgumentException("An explicit persistent private state root is required.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    // For store-local callers that do not already hold the lease. References must come from
    // private persisted state, never a command DTO. This does NOT write/commit a public record.
    // Commit/cold-read integration must use the UnderLease overload to avoid lock re-entry.
    internal TResult WithRetainedRuleSources<TResult>(OwnerScope owner, CharacterWorkspaceId id,
        ICharacterFileQueries queries, IReadOnlyList<CharacterCreationRetainedRuleSourceReference> references,
        Func<RetainedRuleSourceWorkspace, TResult> action)
    {
        RequireRetainedRuleSourceStorage();
        if ((!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner))
            || TryGetPath(owner, id) is not { } path
            || !TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
            throw RetainedRuleSourceRejected();
        using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        var read = ReadWorkspaceUnderLease(owner, id, path);
        if (!read.Success || read.Value?.LocalHistory is not { } history)
            throw RetainedRuleSourceRejected();
        return WithRetainedRuleSourcesUnderLease(operation, owner, id, history, queries, references, action);
    }

    // The history and reference list must be taken from the just-validated private record.
    // Keep the callback inside the same lease as final live-source fencing and record replace.
    // Do not retain the recovery port beyond this callback. Returned contexts are replay inputs,
    // never owner leases or save capabilities.
    private TResult WithRetainedRuleSourcesUnderLease<TResult>(WorkspaceOperationLease operation,
        OwnerScope owner, CharacterWorkspaceId id, WorkspaceLocalHistory history, ICharacterFileQueries queries,
        IReadOnlyList<CharacterCreationRetainedRuleSourceReference> references,
        Func<RetainedRuleSourceWorkspace, TResult> action)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(action);
        RequireRetainedRuleSourceStorage();
        using var scope = new RetainedRuleSourceWorkspace(this, owner, id, history.IncarnationId, queries, references);
        if (!scope.ValidateRetainedRuleSources()) throw RetainedRuleSourceRejected();
        return action(scope);
    }

    private void RequireRetainedRuleSourceStorage()
    {
        if (!_retainedRuleSourcesEnabled)
            throw new InvalidOperationException("Retained rule sources require explicitly configured persistent storage.");
        ValidateStaticAncestorChain(_stateDirectory);
        VerifyPrivateStateRoot(_stateDirectory);
    }

    internal enum RetainedRuleSourceDurability
    {
        // Flushed file and atomic publication support process restart. No directory-fsync claim.
        ProcessRestartOnly,
        // Qualified fsync completed for the file and directory chain. This is not power-loss testing
        // or a guarantee about hardware caches, mount behavior, or the separate record commit.
        FileAndDirectoriesFlushed,
        // The target may already be published. Reconcile its exact reference; never replay a commit.
        PublishedNeedsReconciliation
    }

    internal sealed record RetainedRuleSourcePublication(CharacterCreationRetainedRuleSourceReference Reference,
        RetainedRuleSourceDurability Durability)
    {
        internal bool CanCommitReference => Durability != RetainedRuleSourceDurability.PublishedNeedsReconciliation;
        public override string ToString() => nameof(RetainedRuleSourcePublication);
    }

    internal sealed class RetainedRuleSourceWorkspace : ICharacterCreationRuleSourceRecovery, IDisposable
    {
        private readonly FileWorkspaceStore _store;
        private readonly OwnerScope _owner;
        private readonly CharacterWorkspaceId _id;
        private readonly string _incarnation;
        private readonly string _ownerBinding;
        private readonly ICharacterFileQueries _queries;
        private readonly bool _observationOnly;
        private readonly bool _ruleUpdateDurability;
        private readonly Dictionary<string, CharacterCreationRetainedRuleSourceReference> _references = new(StringComparer.Ordinal);
        // No cache crosses this owner/workspace/incarnation-bound recovery lifetime.
        // Raw XML only: imports and all authority/publication fences remain fresh.
        private readonly RetainedCharacterRuleSourceInputs.PreparedXmlScope4505 _preparedXml4505 = new();
        // One successful current capture per explicitly enabled display/private-read recovery.
        // No execution resolution, resolver or admission is retained in this slot.
        private string? _displayCaptureDigest;
        private readonly PrivateCurrentRuleEnvironmentAdmission.CurrentReadEvaluation? _decodedRead8168;
        private CharacterCreationRetainedRuleSourceReference? _displayCaptureReference;
        private byte[]? _displayCaptureBytes;
        private CharacterCreationRuleSourceCapture? _displayCapture;
        private bool _active = true;
        internal CharacterCreationCommittedCampaign? CommittedCampaign { get; private set; }
        private readonly Dictionary<string, CharacterCreationCommittedCampaign> _draftCampaigns = new(StringComparer.Ordinal);

        internal CharacterCreationCommittedCampaign[]? SnapshotDraftCampaigns()
            => _draftCampaigns.Count == 0 ? null
                : _draftCampaigns.Values.OrderBy(value => value.ReceiptDigest, StringComparer.Ordinal).ToArray();

        internal void RetainCommittedDraftCampaign(CharacterCreationCommittedCampaign committed)
        {
            EnsureActive();
            if (!CharacterCreationCampaignRecovery.IsWellFormed(committed)
                || committed.Binding.RetainedIdentity.OwnerId != _owner.Value
                || committed.Binding.RetainedIdentity.WorkspaceId != _id
                || _draftCampaigns.TryGetValue(committed.ReceiptDigest, out var prior) && prior != committed
                || !_draftCampaigns.ContainsKey(committed.ReceiptDigest) && _draftCampaigns.Count >= 128)
                throw RetainedRuleSourceRejected();
            _draftCampaigns[committed.ReceiptDigest] = committed;
        }

        internal void RetainCommittedCampaign(CharacterCreationCommittedCampaign committed)
        {
            EnsureActive();
            if (!CharacterCreationCampaignRecovery.IsWellFormed(committed)
                || committed.Binding.RetainedIdentity.OwnerId != _owner.Value
                || committed.Binding.RetainedIdentity.WorkspaceId != _id
                || CommittedCampaign is not null && CommittedCampaign != committed)
                throw RetainedRuleSourceRejected();
            CommittedCampaign = committed;
        }

        public bool TryRecoverCampaignAuthority(CharacterCreationCampaignBinding binding, string receiptDigest,
            out CharacterCreationCampaignExecutionContext? execution)
        {
            execution = null;
            if (!_active) return false;
            var committed = CommittedCampaign?.ReceiptDigest == receiptDigest ? CommittedCampaign
                : _draftCampaigns.GetValueOrDefault(receiptDigest);
            return committed is not null && CharacterCreationCampaignRecovery.FromCommitted(committed, this)
                .TryRecoverCampaignAuthority(binding, receiptDigest, out execution);
        }

        internal RetainedRuleSourceWorkspace(FileWorkspaceStore store, OwnerScope owner, CharacterWorkspaceId id,
            string incarnation, ICharacterFileQueries queries,
            IReadOnlyList<CharacterCreationRetainedRuleSourceReference> references, bool observationOnly = false,
            string? displayCaptureDigest = null, bool ruleUpdateDurability = false,
            PrivateCurrentRuleEnvironmentAdmission.CurrentReadEvaluation? decodedRead8168 = null)
        {
            _store = store;
            _observationOnly = observationOnly;
            _ruleUpdateDurability = ruleUpdateDurability;
            _displayCaptureDigest = displayCaptureDigest;
            _decodedRead8168 = displayCaptureDigest is null ? null : decodedRead8168;
            _owner = owner;
            _id = id;
            _incarnation = incarnation;
            _queries = queries ?? throw new ArgumentNullException(nameof(queries));
            if ((!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)) || store.TryGetPath(owner, id) is null
                || !Guid.TryParseExact(incarnation, "N", out var parsed) || parsed == Guid.Empty
                || parsed.ToString("N") != incarnation || references is null
                || references.Count > MaximumRetainedRuleSourceReferences)
                throw RetainedRuleSourceRejected();
            _ownerBinding = OwnerScopedStatePath.BuildOwnerComponent(owner);
            // Detach the mutable collection; the individual reference records contain only values.
            var snapshot = references.Take(MaximumRetainedRuleSourceReferences + 1).ToArray();
            if (snapshot.Length > MaximumRetainedRuleSourceReferences) throw RetainedRuleSourceRejected();
            foreach (var reference in snapshot)
            {
                ValidateReference(reference);
                if (_references.TryGetValue(reference.CaptureDigest, out var prior) && prior != reference)
                    throw RetainedRuleSourceRejected();
                _references[reference.CaptureDigest] = reference;
            }
        }

        internal RetainedRuleSourcePublication PrepareAndPublish(CharacterCreationRuleSourceExecutionContext execution)
        {
            EnsureActive();
            ArgumentNullException.ThrowIfNull(execution);
            byte[] exported = execution.ExportPrivateInputs();
            if (exported is null || exported.Length == 0 || exported.Length > RetainedCharacterRuleSourceInputs.MaximumArchiveBytes)
                throw RetainedRuleSourceRejected();
            byte[] bytes = exported.ToArray(); // A custom capture must not retain a mutable publication buffer.
            var reference = new CharacterCreationRetainedRuleSourceReference(1, _ownerBinding, _id.Value,
                _incarnation, RetainedCharacterRuleSourceInputs.Digest(bytes), bytes.LongLength);
            if (!TryImport(bytes, reference, out _)) throw RetainedRuleSourceRejected();
            string directory = ResolveDirectory(create: true);
            using var windowsCaptureLease = OperatingSystem.IsWindows() && _ruleUpdateDurability
                ? _store.PinWindowsRuleUpdateDirectory(directory) : null;
            string target = ResolveArtifact(directory, reference);
            ThrowIfLinkOrReparsePoint(target, "retained rule source");
            if (File.Exists(target)) return ReconcilePublished(reference); // Validate, never overwrite.

            // Distinct from the workspace writer's stale-temp pattern. Orphans (including interrupted
            // publications) are deliberately retained until a complete reference-driven collector exists.
            string temporary = Path.Combine(directory, ".pending-" + Guid.NewGuid().ToString("N"));
            EnsurePathContained(directory, temporary, "retained rule source temporary file");
            using (var stream = OpenNewSecureFile(temporary, FileAccess.Write, FileShare.None, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            ThrowIfLinkOrReparsePoint(temporary, "retained rule source temporary file");
            SetSecureFileMode(temporary);
            _store._faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterTempFileFlushed, target, temporary);
            // Recheck the entire containment/link boundary immediately before publication.
            _ = ResolveDirectory(create: false);
            ThrowIfLinkOrReparsePoint(temporary, "retained rule source temporary file");
            ThrowIfLinkOrReparsePoint(target, "retained rule source");
            try
            {
                File.Move(temporary, target, overwrite: false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Rename/diagnostic failure may occur AFTER publication. Only exact identity can
                // reconcile it. A missing or different target throws; no reference commit is allowed.
                return ReconcilePublished(reference);
            }
            try
            {
                _store._faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterTargetReplaced, target, temporary);
            }
            catch (Exception)
            {
                // Even cancellation or a diagnostic exception after rename is reconciled from
                // immutable bytes. It cannot turn a published artifact into a safe blind retry.
                return ReconcilePublished(reference);
            }
            return ReconcilePublished(reference);
        }

        internal void RetainAdmittedCapture(CharacterCreationRuleSourceExecutionContext execution, string expectedDigest)
        {
            var published = PrepareAndPublish(execution);
            if (!published.CanCommitReference || published.Reference.CaptureDigest != expectedDigest
                || (!_references.ContainsKey(expectedDigest) && _references.Count >= MaximumRetainedRuleSourceReferences))
                throw RetainedRuleSourceRejected();
            _references[expectedDigest] = published.Reference;
        }

        internal CharacterCreationRetainedRuleSourceReference[] SnapshotReferences()
        {
            EnsureActive();
            return _references.Values.OrderBy(value => value.CaptureDigest, StringComparer.Ordinal).ToArray();
        }

        internal RetainedRuleSourcePublication ReconcilePublished(CharacterCreationRetainedRuleSourceReference reference)
        {
            EnsureActive();
            _ = ReadCapture(reference); // Independent bounded read and structural/policy re-validation.
            try
            {
                string directory = ResolveDirectory(create: false);
                if (_ruleUpdateDurability)
                {
                    FlushRuleUpdateFile(ResolveArtifact(directory, reference));
                    _store.FlushRuleUpdateDirectory(directory);
                    // The Windows record journal snapshots every referenced archive
                    // before adoption and seals it before success. No directory flush
                    // or independent durable archive claim is made here.
                    return new(reference, OperatingSystem.IsWindows()
                        ? RetainedRuleSourceDurability.ProcessRestartOnly
                        : RetainedRuleSourceDurability.FileAndDirectoriesFlushed);
                }
                return new(reference, _store.FlushRetainedDirectoryChain(directory));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                or DllNotFoundException or EntryPointNotFoundException)
            {
                return new(reference, RetainedRuleSourceDurability.PublishedNeedsReconciliation);
            }
        }

        public bool TryRecoverRuleSources(string captureDigest, out CharacterCreationRuleSourceExecutionContext? execution)
        {
            execution = null;
            if (!TryRecoverRuleSourceCapture(captureDigest, out var capture) || capture is null) return false;
            execution = new(capture, _queries);
            return true;
        }

        // Return only a capture backed by this call's guarded private-file read.
        // Admission can consume it directly instead of exporting an execution wrapper
        // and restoring the same detached archive again. Read-only reuse requires
        // exact successful bytes; every recovery and final fence still reads live bytes.
        internal bool TryRecoverRuleSourceCapture(string captureDigest, out CharacterCreationRuleSourceCapture? capture)
        {
            capture = null;
            if (!_active || captureDigest is null || !_references.TryGetValue(captureDigest, out var reference)) return false;
            try
            {
                capture = ReadCapture(reference);
                return true;
            }
            catch (Exception error) when (IsRetainedRuleSourceFailure(error)) { return false; }
        }

        public bool ValidateRetainedRuleSources()
        {
            if (!_active) return false;
            try
            {
                _store.RequireRetainedRuleSourceStorage();
                foreach (var reference in _references.Values) _ = ReadCapture(reference);
                return true;
            }
            catch (Exception error) when (IsRetainedRuleSourceFailure(error)) { return false; }
        }

        private CharacterCreationRuleSourceCapture ReadCapture(CharacterCreationRetainedRuleSourceReference reference)
        {
            EnsureActive();
            ValidateReference(reference);
            string path = ResolveArtifact(ResolveDirectory(create: false), reference);
            ThrowIfLinkOrReparsePoint(path, "retained rule source");
            SetSecureFileMode(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != reference.ByteLength) throw RetainedRuleSourceRejected();
            byte[] bytes = new byte[checked((int)reference.ByteLength)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1 || stream.Length != reference.ByteLength) throw RetainedRuleSourceRejected();
            ThrowIfLinkOrReparsePoint(path, "retained rule source");
            _ = ResolveDirectory(create: false);
            bool currentDisplayCapture = _displayCaptureDigest == reference.CaptureDigest;
            if (currentDisplayCapture && _decodedRead8168?.BorrowDecodedCapture8168(
                    _owner.NormalizedValue, _id, _queries, reference, bytes) is { } sharedCapture)
            {
                // File length, containment, links, owner/incarnation reference and
                // exact newly read bytes were checked for THIS read. Retain the
                // existing execution guard; sharing only avoids archive decoding.
                if (!(_observationOnly || sharedCapture.CreateExecutionResolution() is not null))
                    throw RetainedRuleSourceRejected();
                return sharedCapture;
            }
            if (currentDisplayCapture && ReferenceEquals(_displayCaptureReference, reference)
                && _displayCaptureBytes is { } retainedBytes && _displayCapture is { } retainedCapture
                && bytes.AsSpan().SequenceEqual(retainedBytes))
            {
                // Keep TryImport's execution guard fresh even when reconstruction is reused.
                if (retainedCapture.IntegrityDigest != reference.CaptureDigest
                    || !(_observationOnly || retainedCapture.CreateExecutionResolution() is not null))
                    throw RetainedRuleSourceRejected();
                return retainedCapture;
            }
            if (currentDisplayCapture) ClearDisplayCapture();
            if (!TryImport(bytes, reference, out var capture)) throw RetainedRuleSourceRejected();
            // This closed factory returns RuleSourceCapture or SourcebookCapture800.
            // Only the former has null SourcebookSelection: its retained inputs are
            // detached and its resolvers/accounting are fresh on every factory call.
            // Sourcebook envelopes retain their ordinary import path, without reuse.
            if (currentDisplayCapture && capture!.SourcebookSelection is null)
            {
                _decodedRead8168?.RememberDecodedCapture8168(_owner.NormalizedValue, _id,
                    _queries, reference, bytes, capture);
                _displayCaptureReference = reference;
                _displayCaptureBytes = bytes; // Private bounded read buffer; never exposed.
                _displayCapture = capture;
            }
            return capture!;
        }

        private bool TryImport(byte[] bytes, CharacterCreationRetainedRuleSourceReference reference,
            out CharacterCreationRuleSourceCapture? capture)
            => FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(bytes, reference.CaptureDigest, _preparedXml4505, out capture)
                && capture is not null && capture.IntegrityDigest == reference.CaptureDigest
                && (_observationOnly || capture.CreateExecutionResolution() is not null);

        private void ValidateReference(CharacterCreationRetainedRuleSourceReference reference)
        {
            if (reference is null || reference.SchemaVersion != 1 || reference.OwnerBinding != _ownerBinding
                || reference.WorkspaceId != _id.Value || reference.IncarnationId != _incarnation
                || reference.ByteLength <= 0 || reference.ByteLength > RetainedCharacterRuleSourceInputs.MaximumArchiveBytes
                || !IsRetainedCaptureDigest(reference.CaptureDigest)) throw RetainedRuleSourceRejected();
        }

        internal Dictionary<string, byte[]> SnapshotWindowsRuleUpdateSources(string workspaceDirectory)
        {
            var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            long total = 0;
            foreach (var reference in _references.Values)
            {
                _ = ReadCapture(reference);
                string path = ResolveArtifact(ResolveDirectory(create: false), reference);
                total = checked(total + reference.ByteLength);
                if (total > MaximumRuleUpdateBranchBytes) throw RetainedRuleSourceRejected();
                byte[] bytes = _store.ReadWindowsRuleUpdateBytes(path, reference.ByteLength);
                if (bytes.LongLength != reference.ByteLength || RetainedCharacterRuleSourceInputs.Digest(bytes) != reference.CaptureDigest)
                    throw RetainedRuleSourceRejected();
                result.Add(Path.GetRelativePath(workspaceDirectory, path).Replace('\\', '/'), bytes);
            }
            return result;
        }

        private string ResolveDirectory(bool create)
        {
            _store.RequireRetainedRuleSourceStorage();
            string workspaceDirectory = _store.GetWorkspaceDirectory(_owner);
            ValidateStaticAncestorChain(workspaceDirectory);
            if (!Directory.Exists(workspaceDirectory)) throw RetainedRuleSourceRejected();
            string current = workspaceDirectory;
            foreach (string component in new[] { ".retained-rule-sources", _id.Value, _incarnation })
            {
                current = Path.GetFullPath(Path.Combine(current, component));
                EnsurePathContained(workspaceDirectory, current, "retained rule source directory");
                if (create) EnsureSecureDirectory(current, "retained rule source directory");
                else if (!TrySecureExistingDirectory(current, "retained rule source directory")) throw RetainedRuleSourceRejected();
            }
            return current;
        }

        private static string ResolveArtifact(string directory, CharacterCreationRetainedRuleSourceReference reference)
        {
            string result = Path.Combine(directory, reference.CaptureDigest[7..] + ".bin");
            EnsurePathContained(directory, result, "retained rule source");
            return result;
        }

        private void EnsureActive()
        {
            if (!_active) throw RetainedRuleSourceRejected();
        }

        private void ClearDisplayCapture()
        {
            _displayCaptureReference = null;
            _displayCaptureBytes = null;
            _displayCapture = null;
        }

        public void Dispose()
        {
            _active = false;
            _preparedXml4505.Dispose();
            ClearDisplayCapture();
            _displayCaptureDigest = null;
        }
        public override string ToString() => nameof(RetainedRuleSourceWorkspace);
    }

    // This manifest belongs only to the private record envelope. Public workspace
    // documents, receipt archives and continuation payloads never carry it.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record RetainedRuleSourceManifest(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] string OwnerId,
        [property: JsonRequired] string WorkspaceId,
        [property: JsonRequired] string IncarnationId,
        [property: JsonRequired] CharacterCreationRetainedRuleSourceReference[] References,
        [property: JsonRequired] RetainedRuleSourceUse[] Uses)
    {
        // Optional, private and atomically published with its one exact receipt.
        // Never serialized into public auxiliary state or portable continuations.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CharacterCreationCommittedCampaign? CommittedCampaign { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CharacterCreationCommittedCampaign[]? CommittedDraftCampaigns { get; init; }
        // Custody is deliberately separate from current-source execution admission.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public WorkspacePrivateRuleSourceClosureBinding? PrivateClosureBinding { get; init; }
        // Current interpretation and its original activation receipt are separate from V1 custody.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public WorkspaceCurrentRuleEnvironmentBinding? CurrentEnvironment { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public WorkspacePrivateRuleSourceClosureBinding? ActivatedClosureBinding { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public WorkspaceContinuationRestoreReceipt[]? RestoreReceipts { get; init; }
        // Local recovery classification is published in the same record as each
        // exact receipt. Sidecar absence cannot turn a reviewed update into an
        // ordinary restore. This evidence never enters a portable transfer.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public PrivateClosureRestoreEvidence[]? RestoreEvidence { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public LifeModuleOriginalFence? LifeModuleOriginal { get; init; }
        public override string ToString() => nameof(RetainedRuleSourceManifest);
    }

    private const string OrdinaryRestoreEvidence = "chummer.private-closure-restore/v1";
    private const string ReviewedUpdateEvidence = "chummer.reviewed-rule-update/v1";
    private const string RoamingCheckpointEvidence = "chummer.roaming-checkpoint-adoption/v1";

    private static string RestoreEvidenceKind(PrivateClosureRestoreAdmission admission)
        => admission.UpdateOriginal is not null ? ReviewedUpdateEvidence
            : !admission.CaptureOnly && admission.RoamingBaseDigest is not null
                && admission.Receipt.ContentRevision == admission.Target.ContentRevision
                && admission.Receipt.SavedRevision > admission.Target.SavedRevision
                    ? RoamingCheckpointEvidence : OrdinaryRestoreEvidence;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record PrivateClosureRestoreEvidence(
        [property: JsonRequired] string Kind,
        [property: JsonRequired] WorkspaceContinuationRestoreReceipt Receipt);

    private static void ValidateRestoreEvidence(RetainedRuleSourceManifest? manifest)
    {
        var receipts = manifest?.RestoreReceipts;
        var evidence = manifest?.RestoreEvidence;
        if (receipts is null)
        {
            if (evidence is not null) throw RetainedRuleSourceRejected();
            return;
        }
        // Unqualified proposal records have no shipped migration contract. Do
        // not guess their kind from missing files or silently bless a receipt.
        if (receipts is not { Length: > 0 and <= MaximumRetainedRuleSourceReferences }
            || evidence is null || evidence.Length != receipts.Length
            || receipts.Any(receipt => receipt is null)
            || receipts.Select(receipt => receipt.OperationId).Distinct().Count() != receipts.Length
            || evidence.Any(value => value is null || value.Kind is not (OrdinaryRestoreEvidence or ReviewedUpdateEvidence or RoamingCheckpointEvidence))
            || manifest!.CurrentEnvironment is null && evidence.Any(value => value.Kind == RoamingCheckpointEvidence)
            || evidence.Count(value => value.Kind == ReviewedUpdateEvidence) > MaximumRuleUpdateBranches)
            throw RetainedRuleSourceRejected();
        for (int index = 0; index < receipts.Length; index++)
        {
            var receipt = receipts[index];
            if (evidence[index].Receipt != receipt
                || !(new WorkspaceLocalHistory(receipt.IncarnationId, receipt.ContentRevision, receipt.SnapshotDigest)
                    { LastRestore = receipt }).IsValid(receipt.ContentRevision)) throw RetainedRuleSourceRejected();
        }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record RetainedRuleSourceUse(
        [property: JsonRequired] string ReceiptDigest,
        [property: JsonRequired] string CaptureDigest);

    private static RetainedRuleSourceUse[] CollectRetainedRuleSourceUses(WorkspaceDocumentAuxiliaryState state)
    {
        var uses = new List<RetainedRuleSourceUse>();
        void Add(CharacterCreationCompletionReviewRequest proposal, string receiptDigest)
        {
            if (proposal is null || !CharacterCreationCompletionReviewRules.HasValidRuleSourceExecution(proposal))
                throw RetainedRuleSourceRejected();
            if (proposal.RuleSourceExecutionSchema is null) return;
            if (!IsRetainedCaptureDigest(proposal.RuleSourceCaptureDigest!)
                || !CharacterCreationFinalizationDigest.IsCanonical(receiptDigest)
                || uses.Count >= MaximumRetainedRuleSourceReferences)
                throw RetainedRuleSourceRejected();
            uses.Add(new(receiptDigest, proposal.RuleSourceCaptureDigest!));
        }
        void Visit(WorkspaceDocumentAuxiliaryState current, int depth)
        {
            if (current is null || depth > 1) throw RetainedRuleSourceRejected();
            if (current.CharacterCreationCompletionDrafts is { } drafts)
            {
                if (drafts.Count > 128) throw RetainedRuleSourceRejected();
                foreach (var draft in drafts)
                {
                    if (draft?.Baseline?.Review?.Review?.Request is not { } proposal || draft.Receipt is null
                        || draft.Baseline.OriginalState is not { } baseline
                        || baseline.CharacterCreationCompletionDrafts is not null
                        || baseline.CharacterCreationFinalizationArchive is not null)
                        throw RetainedRuleSourceRejected();
                    Add(proposal, draft.Receipt.ReceiptDigest);
                }
            }
            if (current.CharacterCreationFinalizationArchive is { } archive)
            {
                if (current.CharacterCreationFinalizationReceipts is not { Count: 1 } receipts
                    || receipts[0]?.Receipt is not { } finalReceipt)
                    throw RetainedRuleSourceRejected();
                if (archive.CompletionAuthority is { } completion)
                    Add(completion.Proposal, finalReceipt.ReceiptDigest);
                Visit(archive.State, depth + 1);
            }
        }
        Visit(state, 0);
        if (uses.Select(value => value.ReceiptDigest).Distinct(StringComparer.Ordinal).Count() != uses.Count)
            throw RetainedRuleSourceRejected();
        return uses.OrderBy(value => value.ReceiptDigest, StringComparer.Ordinal).ToArray();
    }

    private static Dictionary<string, CharacterCreationCampaignBinding> CollectDraftCampaignBindings(
        WorkspaceDocumentAuxiliaryState state)
    {
        var result = new Dictionary<string, CharacterCreationCampaignBinding>(StringComparer.Ordinal);
        var entries = state.CharacterCreationCompletionDrafts
            ?? state.CharacterCreationFinalizationArchive?.State.CharacterCreationCompletionDrafts;
        if (entries is null) return result;
        if (entries.Count is 0 or > 128) throw RetainedRuleSourceRejected();
        foreach (var entry in entries)
        {
            if (entry?.Baseline?.Review?.Review?.Request is not { } proposal || entry.Receipt is null)
                throw RetainedRuleSourceRejected();
            if (proposal.CampaignAuthority is { } binding
                && !result.TryAdd(entry.Receipt.ReceiptDigest, binding)) throw RetainedRuleSourceRejected();
        }
        return result;
    }

    private static bool MatchesDraftCampaigns(OwnerScope owner, CharacterWorkspaceId id,
        Dictionary<string, CharacterCreationCampaignBinding> bindings, CharacterCreationCommittedCampaign[]? committed)
    {
        if (committed is null) return bindings.Count == 0;
        if (committed.Length is 0 or > 128 || committed.Length != bindings.Count) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return committed.All(value => CharacterCreationCampaignRecovery.IsWellFormed(value)
            && value.Binding.RetainedIdentity.OwnerId == owner.Value
            && value.Binding.RetainedIdentity.WorkspaceId == id && seen.Add(value.ReceiptDigest)
            && bindings.TryGetValue(value.ReceiptDigest, out var binding) && binding == value.Binding);
    }

    private RetainedRuleSourceWorkspace? OpenRecordRuleSources(
        OwnerScope owner, CharacterWorkspaceId id, PersistedWorkspaceRecord record,
        bool reuseCurrentCaptureForRead = false,
        PrivateCurrentRuleEnvironmentAdmission.CurrentReadEvaluation? currentRead = null,
        CancellationToken cancellation = default,
        bool borrowDecodedCaptureForRead = true,
        RewardWriteAdmission? rewardPreparation = null)
    {
        using var sourcesTiming = StartupActivities8910.StartActivity("record-rule-sources");
        currentRead?.ValidateRecord(owner.NormalizedValue, id, CharacterCreationFinalizationDigest.Compute(record));
        rewardPreparation?.ValidateClaimedPreparation(this, TryGetPath(owner, id) ?? throw RetainedRuleSourceRejected(), record);
        ValidatePrivateClosureCustody(record, owner, id);
        var uses = CollectRetainedRuleSourceUses(record.AuxiliaryState ?? WorkspaceDocumentAuxiliaryState.Empty);
        var campaignBinding = record.AuxiliaryState?.CharacterCreationFinalizationArchive?.CompletionAuthority?.Proposal.CampaignAuthority;
        var draftCampaigns = CollectDraftCampaignBindings(record.AuxiliaryState ?? WorkspaceDocumentAuxiliaryState.Empty);
        if (record.RetainedRuleSources is not { } manifest)
        {
            if (record.RecordSchemaVersion == RetainedWorkspaceRecordSchemaVersion || uses.Length != 0
                || campaignBinding is not null || draftCampaigns.Count != 0)
                throw RetainedRuleSourceRejected();
            return null;
        }
        if (record.RecordSchemaVersion is not (RetainedWorkspaceRecordSchemaVersion or Sr6CareerAttributeRecordSchemaVersion) || manifest.SchemaVersion != 1
            || manifest.OwnerId != owner.NormalizedValue || manifest.WorkspaceId != id.Value
            || record.LocalHistory is null || manifest.IncarnationId != record.LocalHistory.IncarnationId
            || manifest.References is not { Length: <= MaximumRetainedRuleSourceReferences }
            || manifest.References.Length == 0 && manifest.CommittedCampaign is null && draftCampaigns.Count == 0
            || !MatchesDraftCampaigns(owner, id, draftCampaigns, manifest.CommittedDraftCampaigns)
            || (campaignBinding is null) != (manifest.CommittedCampaign is null)
            || manifest.CommittedCampaign is { } committed
                && (!CharacterCreationCampaignRecovery.IsWellFormed(committed)
                    || committed.Binding != campaignBinding
                    || committed.Binding.RetainedIdentity.OwnerId != owner.Value
                    || committed.Binding.RetainedIdentity.WorkspaceId != id
                    || record.AuxiliaryState?.CharacterCreationFinalizationReceipts is not { Count: 1 } receipts
                    || receipts[0].Receipt.ReceiptDigest != committed.ReceiptDigest)
            || manifest.Uses is null || manifest.Uses.Length > MaximumRetainedRuleSourceReferences
            || !manifest.Uses.SequenceEqual(uses) || _retainedCharacterQueries is null)
            throw RetainedRuleSourceRejected();
        var digests = uses.Select(value => value.CaptureDigest).ToHashSet(StringComparer.Ordinal);
        if (manifest.CurrentEnvironment is { } current) digests.Add(current.CaptureDigest);
        if (manifest.References.Any(value => value is null)
            || manifest.References.Select(value => value.CaptureDigest).Distinct(StringComparer.Ordinal).Count() != manifest.References.Length
            // Current capture advancement retains historical before captures as private
            // archives. Every reference is still owner/incarnation-bound and reopened below.
            || (manifest.CurrentEnvironment is null
                ? !digests.SetEquals(manifest.References.Select(value => value.CaptureDigest))
                : !digests.IsSubsetOf(manifest.References.Select(value => value.CaptureDigest))))
            throw RetainedRuleSourceRejected();
        // Decoded bytes are not admission. The existing original-record check
        // in AdmitRetainedCurrentEnvironment below remains mandatory before any
        // execution/result is accepted; do not duplicate its canonical hash here.
        var recovery = new RetainedRuleSourceWorkspace(this, owner, id, manifest.IncarnationId,
            _retainedCharacterQueries, manifest.References,
            observationOnly: manifest.PrivateClosureBinding is not null || manifest.CurrentEnvironment is not null,
            displayCaptureDigest: reuseCurrentCaptureForRead ? manifest.CurrentEnvironment?.CaptureDigest : null,
            decodedRead8168: reuseCurrentCaptureForRead && borrowDecodedCaptureForRead ? currentRead : null);
        if (manifest.CommittedCampaign is { } history) recovery.RetainCommittedCampaign(history);
        foreach (var draft in manifest.CommittedDraftCampaigns ?? []) recovery.RetainCommittedDraftCampaign(draft);
        if (recovery.ValidateRetainedRuleSources())
        {
            try
            {
                // The exact claimed reward record already has complete proposed-after
                // admission. Keep all custody/archive checks and historical recovery;
                // its mandatory fresh before/after publication pair runs in the writer.
                if (manifest.CurrentEnvironment is { } environment && rewardPreparation is null)
                {
                    using var environmentTiming = StartupActivities8910.StartActivity("record-current-environment");
                    AdmitRetainedCurrentEnvironment(record, owner, id, recovery, environment, currentRead, cancellation);
                }
                return recovery;
            }
            catch
            {
                recovery.Dispose();
                throw;
            }
        }
        recovery.Dispose();
        throw RetainedRuleSourceRejected();
    }

    private static void ValidateCurrentEnvironmentRecord(PersistedWorkspaceRecord record, OwnerScope owner, CharacterWorkspaceId id)
    {
        ValidateRestoreEvidence(record.RetainedRuleSources);
        if (record.RetainedRuleSources?.LifeModuleOriginal is { } original
            && (!CharacterCreationFinalizationDigest.IsCanonical(original.ReviewDigest)
                || !Guid.TryParseExact(original.SlotGeneration, "N", out var generation) || generation == Guid.Empty
                || record.AuxiliaryState?.CharacterCreationFinalizationReceipts is not { Count: 1 } native
                || native[0].Receipt.BuildMethod != CharacterCreationBuildMethods.LifeModules
                || native[0].Receipt.ReceiptDigest != original.ReceiptDigest))
            throw RetainedRuleSourceRejected();
        var manifest = record.RetainedRuleSources ?? throw RetainedRuleSourceRejected();
        var current = manifest.CurrentEnvironment ?? throw RetainedRuleSourceRejected();
        var activation = manifest.ActivatedClosureBinding ?? throw RetainedRuleSourceRejected();
        var receipts = manifest.RestoreReceipts;
        if (manifest.PrivateClosureBinding is not null || record.RecordSchemaVersion != RetainedWorkspaceRecordSchemaVersion
            || manifest.SchemaVersion != 1 || manifest.OwnerId != owner.NormalizedValue || manifest.WorkspaceId != id.Value
            || record.ContentRevision is not { } revision || record.LocalHistory is not { } history
            || !history.IsValid(revision) || history.LocalAdoption is not null || manifest.IncarnationId != history.IncarnationId
            || current.Schema != WorkspaceCurrentRuleEnvironmentBinding.SchemaV1 || current.OwnerId != owner.NormalizedValue
            || current.WorkspaceId != id || current.ContentRevision != revision || current.SavedRevision != record.SavedRevision
            || string.IsNullOrWhiteSpace(current.ExecutionProfile) || string.IsNullOrWhiteSpace(current.ExecutionProfileFingerprint)
            || !IsRetainedCaptureDigest(current.CaptureDigest) || !IsRetainedCaptureDigest(current.CharacterDigest)
            || manifest.References is null || manifest.References.Any(value => value is null)
            || !manifest.References.Any(value => value.CaptureDigest == current.CaptureDigest)
            || activation.Schema != WorkspacePrivateRuleSourceClosureBinding.SchemaV2
            || activation.OwnerId != owner.NormalizedValue || activation.WorkspaceId != id
            || activation.CurrentEnvironmentDigest is null || !IsRetainedCaptureDigest(activation.CurrentEnvironmentDigest)
            || !IsRetainedCaptureDigest(activation.ClosureDigest) || history.LastRestore is not { } latest
            || latest.SourceDigest != activation.ClosureDigest || latest.SnapshotDigest != activation.ContinuationDigest
            || latest.ContentRevision != activation.ContentRevision || latest.SavedRevision != activation.SavedRevision
            || receipts is not { Length: > 0 and <= MaximumRetainedRuleSourceReferences }
            || receipts.Any(value => value is null) || receipts[^1] != latest
            || receipts.Select(value => value.OperationId).Distinct().Count() != receipts.Length)
            throw RetainedRuleSourceRejected();
        long priorRevision = 0;
        long priorSavedRevision = 0;
        for (int index = 0; index < receipts.Length; index++)
        {
            var receipt = receipts[index];
            bool checkpoint = manifest.RestoreEvidence![index].Kind == RoamingCheckpointEvidence;
            if (receipt.ContentRevision < priorRevision || receipt.ContentRevision == priorRevision
                    && (!checkpoint || receipt.SavedRevision <= priorSavedRevision)
                || receipt.ContentRevision > revision
                || !(new WorkspaceLocalHistory(receipt.IncarnationId, receipt.ContentRevision, receipt.SnapshotDigest)
                    { LastRestore = receipt }).IsValid(receipt.ContentRevision)) throw RetainedRuleSourceRejected();
            priorRevision = receipt.ContentRevision;
            priorSavedRevision = receipt.SavedRevision;
        }
    }

    private void AdmitRetainedCurrentEnvironment(PersistedWorkspaceRecord record, OwnerScope owner, CharacterWorkspaceId id,
        RetainedRuleSourceWorkspace recovery, WorkspaceCurrentRuleEnvironmentBinding environment,
        PrivateCurrentRuleEnvironmentAdmission.CurrentReadEvaluation? currentRead = null,
        CancellationToken cancellation = default)
    {
        currentRead?.ValidateRecord(owner.NormalizedValue, id, CharacterCreationFinalizationDigest.Compute(record));
        if (_retainedCharacterQueries is null || !TryMaterializeRecord(record, out var document, out var revision, out var saved, out _)
            || !recovery.TryRecoverRuleSourceCapture(environment.CaptureDigest, out var capture) || capture is null)
            throw new WorkspaceCurrentRuleEnvironmentUnavailableException(["current-rule-environment-assets-unavailable"]);
        // A display invocation retains the exact file timestamp at every admission
        // boundary. Other callers keep their existing mechanics-only timestamp.
        var snapshot = new WorkspaceContinuationSnapshot(owner.NormalizedValue,
            new(id, document, currentRead?.LastUpdatedUtc ?? DateTimeOffset.UnixEpoch, revision, saved), []);
        if (!PrivateCurrentRuleEnvironmentAdmission.TryAdmit(snapshot, capture, environment, _retainedCharacterQueries,
            cancellation, out _, out var blockers, currentRead, recovery: recovery)) throw new WorkspaceCurrentRuleEnvironmentUnavailableException(blockers);
    }

    // Checkpointing changes saved revision only. It can use the pinned current
    // environment without exposing a character to ordinary ambient computations.
    private WorkspaceStoreReadResult ReadWorkspaceForCheckpointUnderLease(OwnerScope owner, CharacterWorkspaceId id,
        string path, out IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> ledger)
    {
        var read = ReadWorkspaceUnderLease(owner, id, path, out ledger, privateClosureRead: true);
        if (read.Success && ReadPrivateRecord(path).RetainedRuleSources?.PrivateClosureBinding is not null)
            return UnavailableRead();
        return read;
    }

    private void RevalidateCurrentEnvironmentForWrite(PersistedWorkspaceRecord record, RetainedRuleSourceWorkspace? recovery,
        CancellationToken cancellation = default)
    {
        if (record.RetainedRuleSources?.CurrentEnvironment is not { } environment) return;
        if (recovery is null) throw RetainedRuleSourceRejected();
        var owner = environment.OwnerId == OwnerScope.LocalSingleUser.NormalizedValue
            ? OwnerScope.LocalSingleUser : new OwnerScope(environment.OwnerId);
        AdmitRetainedCurrentEnvironment(record, owner, environment.WorkspaceId, recovery, environment, cancellation: cancellation);
    }

    private PersistedWorkspaceRecord ReadPrivateRecord(string path, OrdinaryRosterRecordRead? ordinaryRosterRead = null)
    {
        ThrowIfLinkOrReparsePoint(path, "workspace target");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (_retainedRuleSourcesEnabled && stream.Length > MaximumRetainedWorkspaceRecordBytes)
            throw RetainedRuleSourceRejected();
        try
        {
            return (ordinaryRosterRead is null
                ? JsonSerializer.Deserialize(stream, WorkspaceRecordJsonContext.Default.PersistedWorkspaceRecord)
                : ordinaryRosterRead.Read(stream, out _))
                ?? throw RetainedRuleSourceRejected();
        }
        catch (JsonException) { throw RetainedRuleSourceRejected(); }
    }

    // Reopen under the caller's existing workspace lease; never use a mutable
    // query setter, process cache or the supplied live resolver to recover history.
    private RetainedRuleSourceWorkspace? OpenSavedRuleSources(OwnerScope owner, CharacterWorkspaceId id,
        string path, WorkspaceStoredDocument saved, bool requirePublication = false)
    {
        var record = ReadPrivateRecord(path);
        if (record.LocalHistory?.IncarnationId != saved.LocalHistory?.IncarnationId
            || record.ContentRevision != saved.ContentRevision || record.SavedRevision != saved.SavedRevision
            || WorkspaceDocumentAuxiliaryStateDigest.Compute(record.AuxiliaryState ?? WorkspaceDocumentAuxiliaryState.Empty)
                != saved.Document.AuxiliaryStateDigest)
            throw RetainedRuleSourceRejected();
        var recovery = OpenRecordRuleSources(owner, id, record);
        if (recovery is not null || !requirePublication) return recovery;
        RequireRetainedRuleSourceStorage();
        if (_retainedCharacterQueries is null || saved.LocalHistory is null) throw RetainedRuleSourceRejected();
        return new(this, owner, id, saved.LocalHistory.IncarnationId, _retainedCharacterQueries, []);
    }

    private static RetainedRuleSourceManifest BuildRetainedManifest(OwnerScope owner, CharacterWorkspaceId id,
        WorkspaceLocalHistory history, WorkspaceDocument document, RetainedRuleSourceWorkspace recovery)
        => new(1, owner.NormalizedValue, id.Value, history.IncarnationId,
            recovery.SnapshotReferences(), CollectRetainedRuleSourceUses(document.AuxiliaryState))
            { CommittedCampaign = recovery.CommittedCampaign, CommittedDraftCampaigns = recovery.SnapshotDraftCampaigns() };

    // Every existing writer converges here. Preserve the previous private manifest
    // even when its public document came through a generic CAS/checkpoint API.
    private PersistedWorkspaceRecord PreserveRetainedRuleSources(string path, PersistedWorkspaceRecord record,
        WorkspaceWriteDisposition disposition, PrivateClosureRestoreAdmission? closureAdmission = null,
        FreeSpriteConversionWriteAdmission5732? freeSpriteConversionAdmission = null)
    {
        var previous = disposition == WorkspaceWriteDisposition.ReplaceExisting ? ReadPrivateRecord(path) : null;
        RequirePreservedSr6CareerAttributeHistory(previous, record);
        ValidateRestoreEvidence(previous?.RetainedRuleSources);
        ValidateRestoreEvidence(record.RetainedRuleSources);
        if (closureAdmission is not null)
        {
            closureAdmission.ValidateFinalFence();
            var priorReceipts = previous?.RetainedRuleSources?.RestoreReceipts ?? [];
            var priorEvidence = previous?.RetainedRuleSources?.RestoreEvidence ?? [];
            var expectedEvidence = new PrivateClosureRestoreEvidence(
                RestoreEvidenceKind(closureAdmission),
                closureAdmission.Receipt);
            if (CharacterCreationFinalizationDigest.Compute(previous) != closureAdmission.TargetRecordDigest
                || record.LocalHistory?.LastRestore != closureAdmission.Receipt
                || !(record.RetainedRuleSources?.RestoreReceipts ?? []).SequenceEqual(priorReceipts.Append(closureAdmission.Receipt))
                || !(record.RetainedRuleSources?.RestoreEvidence ?? []).SequenceEqual(priorEvidence.Append(expectedEvidence))
                || (closureAdmission.CurrentEnvironment is null
                    ? record.RetainedRuleSources?.PrivateClosureBinding != closureAdmission.Binding
                    : record.RetainedRuleSources?.ActivatedClosureBinding != closureAdmission.Binding
                        || record.RetainedRuleSources?.CurrentEnvironment is null))
                throw RetainedRuleSourceRejected();
            return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
        }
        // All generic and typed save/Career/finalization writers share this
        // admission. A supplied manifest cannot drop, add or reclassify evidence.
        // Writers without a supplied manifest inherit the previous one below.
        if (record.RetainedRuleSources is { } proposed
            && (!(proposed.RestoreEvidence ?? []).SequenceEqual(previous?.RetainedRuleSources?.RestoreEvidence ?? [])
                || !(proposed.RestoreReceipts ?? []).SequenceEqual(previous?.RetainedRuleSources?.RestoreReceipts ?? [])))
            throw RetainedRuleSourceRejected();
        // No generic writer, even one with a fabricated public document, can activate custody.
        if (previous?.RetainedRuleSources?.CurrentEnvironment is null
            && (record.RetainedRuleSources?.CurrentEnvironment is not null
                || record.RetainedRuleSources?.ActivatedClosureBinding is not null)) throw RetainedRuleSourceRejected();
        if (previous?.RetainedRuleSources?.PrivateClosureBinding is not null
            || record.RetainedRuleSources?.PrivateClosureBinding is not null)
            throw RetainedRuleSourceRejected();
        if (previous?.RetainedRuleSources is { } retained)
        {
            if (previous.LocalHistory?.IncarnationId != record.LocalHistory?.IncarnationId
                || previous.LocalHistory?.LocalAdoption != record.LocalHistory?.LocalAdoption)
                throw RetainedRuleSourceRejected();
            if (record.RetainedRuleSources is { } next)
            {
                if (next.OwnerId != retained.OwnerId || next.WorkspaceId != retained.WorkspaceId
                    || next.IncarnationId != retained.IncarnationId
                    || retained.CommittedCampaign is not null && next.CommittedCampaign != retained.CommittedCampaign
                    || (retained.CommittedDraftCampaigns ?? []).Any(value => !(next.CommittedDraftCampaigns ?? []).Contains(value))
                    || retained.References.Any(value => !next.References.Contains(value))
                    || retained.Uses.Any(value => !next.Uses.Contains(value))) throw RetainedRuleSourceRejected();
                if (retained.LifeModuleOriginal is not null && next.LifeModuleOriginal != retained.LifeModuleOriginal
                    || retained.LifeModuleOriginal is null && next.LifeModuleOriginal is not null && LifeModuleAdmissionFor(path) is null)
                    throw RetainedRuleSourceRejected();
            }
            else record = record with { RetainedRuleSources = retained };
            if (retained.CurrentEnvironment is { } environment)
            {
                if (ConditionTransferAdmissionFor(path) is { } condition)
                {
                    condition.ValidateFinalFence(record);
                    if (!CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources!.ActivatedClosureBinding, retained.ActivatedClosureBinding)
                        || !CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources.RestoreReceipts, retained.RestoreReceipts))
                        throw RetainedRuleSourceRejected();
                    return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
                }
                if (LifeModuleAdmissionFor(path) is { } finalization)
                {
                    finalization.ValidateFinalFence(record);
                    if (!SameLifeModule(record.RetainedRuleSources!.ActivatedClosureBinding, retained.ActivatedClosureBinding)
                        || !SameLifeModule(record.RetainedRuleSources.RestoreReceipts, retained.RestoreReceipts)
                        || !SameLifeModule(record.LocalHistory, previous.LocalHistory)
                        || !SameLifeModule(record.DelegatedGmCharacterEdits, previous.DelegatedGmCharacterEdits)
                        || !SameLifeModule(record.DelegatedGmHistorySegmentStarts, previous.DelegatedGmHistorySegmentStarts))
                        throw RetainedRuleSourceRejected();
                    return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
                }
                if (MartialAcquisitionAdmissionFor(path) is { Current: not null } martial)
                {
                    martial.ValidateFinalFence(record);
                    if (!CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources!.ActivatedClosureBinding,
                            retained.ActivatedClosureBinding)
                        || !CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources.RestoreReceipts,
                            retained.RestoreReceipts)) throw RetainedRuleSourceRejected();
                    return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
                }
                if (LifestyleAdmissionFor(path) is { Current: not null } lifestyle)
                {
                    // Like the adjacent typed Gear admissions, this store/path-local
                    // permit binds the admitted before/after pair under the original
                    // owner/workspace leases. The identity/reference checks above and
                    // private restore custody below remain mandatory.
                    lifestyle.ValidateFinalFence(record);
                    if (!CharacterCareerLifestyleMaintenanceTransaction.Same(record.RetainedRuleSources!.ActivatedClosureBinding,
                            retained.ActivatedClosureBinding)
                        || !CharacterCareerLifestyleMaintenanceTransaction.Same(record.RetainedRuleSources.RestoreReceipts,
                            retained.RestoreReceipts)) throw RetainedRuleSourceRejected();
                    return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
                }
                if (SustainedAdmissionFor(path) is { Current: not null } sustained)
                {
                    // Bind this private admission to the exact closure; full live validation runs after staging.
                    sustained.ValidateRecordBinding(record);
                    if (!CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources!.ActivatedClosureBinding,
                            retained.ActivatedClosureBinding)
                        || !CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources.RestoreReceipts,
                            retained.RestoreReceipts)) throw RetainedRuleSourceRejected();
                    return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
                }
                if (GearAdmissionFor(path) is { Current: not null } gear)
                {
                    // The exact admitted pair and record can be consumed only by this
                    // Gear write, under the original owner/workspace leases.
                    gear.ValidateFinalFence(record);
                    if (!CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources!.ActivatedClosureBinding,
                            retained.ActivatedClosureBinding)
                        || !CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources.RestoreReceipts,
                            retained.RestoreReceipts)) throw RetainedRuleSourceRejected();
                    return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
                }
                if (GearSaleAdmissionFor(path) is { Current: not null } sale)
                {
                    // The exact admitted pair and record can be consumed only by this
                    // Gear write, under the original owner/workspace leases.
                    sale.ValidateFinalFence(record);
                    if (!CharacterCareerGearSaleTransaction.Same(record.RetainedRuleSources!.ActivatedClosureBinding,
                            retained.ActivatedClosureBinding)
                        || !CharacterCareerGearSaleTransaction.Same(record.RetainedRuleSources.RestoreReceipts,
                            retained.RestoreReceipts)) throw RetainedRuleSourceRejected();
                    return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
                }
                if (SpecializationAdmissionFor(path) is { Current: not null } specialization)
                {
                    // Only this store/path's admitted specialization record may publish
                    // its freshly validated current environment under the original leases.
                    specialization.ValidateFinalFence(record);
                    if (!CharacterCareerSpecializationTransaction.Same(record.RetainedRuleSources!.ActivatedClosureBinding,
                            retained.ActivatedClosureBinding)
                        || !CharacterCareerSpecializationTransaction.Same(record.RetainedRuleSources.RestoreReceipts,
                            retained.RestoreReceipts)) throw RetainedRuleSourceRejected();
                    return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
                }
                if (ReputationAdmissionFor(path) is { Current: not null } reputation)
                {
                    reputation.ValidateFinalFence(record);
                    if (!CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources!.ActivatedClosureBinding,
                            retained.ActivatedClosureBinding)
                        || !CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources.RestoreReceipts,
                            retained.RestoreReceipts)) throw RetainedRuleSourceRejected();
                    return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
                }
                if (WizardAdmissionFor(path) is { } wizard)
                {
                    wizard.Validate(record);
                    if (!CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources!.ActivatedClosureBinding,
                            retained.ActivatedClosureBinding)
                        || !CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources.RestoreReceipts,
                            retained.RestoreReceipts)) throw RetainedRuleSourceRejected();
                    return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
                }
                if (SpellLearningAdmissionFor5703(path) is { Current: not null } learningSpell)
                {
                    learningSpell.ValidateFinalFence(record);
                    if (!CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources!.ActivatedClosureBinding,
                            retained.ActivatedClosureBinding)
                        || !CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources.RestoreReceipts,
                            retained.RestoreReceipts)) throw RetainedRuleSourceRejected();
                    return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
                }
                if (freeSpriteConversionAdmission is { } freeSpriteConversion)
                {
                    // Preparation consumes only the central writer's exact private claim.
                    // Full fresh BEFORE/AFTER validation is mandatory just before publication.
                    freeSpriteConversion.ValidatePreparation(this, path, record);
                    if (!CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources!.ActivatedClosureBinding,
                            retained.ActivatedClosureBinding)
                        || !CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources.RestoreReceipts,
                            retained.RestoreReceipts)) throw RetainedRuleSourceRejected();
                    return record;
                }
                if (FocusBondingAdmissionFor6495(path) is { Current: not null } focusBonding)
                {
                    focusBonding.ValidateFinalFence(record);
                    if (!CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources!.ActivatedClosureBinding,
                            retained.ActivatedClosureBinding)
                        || !CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources.RestoreReceipts,
                            retained.RestoreReceipts)) throw RetainedRuleSourceRejected();
                    return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
                }
                if (SpiritFetteringAdmissionFor5724(path) is { Current: not null } spiritFettering)
                {
                    spiritFettering.ValidateFinalFence(record);
                    if (!CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources!.ActivatedClosureBinding,
                            retained.ActivatedClosureBinding)
                        || !CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources.RestoreReceipts,
                            retained.RestoreReceipts)) throw RetainedRuleSourceRejected();
                    return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
                }
                if (ComplexFormLearningAdmissionFor5703(path) is { Current: not null } learningComplexForm)
                {
                    learningComplexForm.ValidateFinalFence(record);
                    if (!CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources!.ActivatedClosureBinding,
                            retained.ActivatedClosureBinding)
                        || !CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources.RestoreReceipts,
                            retained.RestoreReceipts)) throw RetainedRuleSourceRejected();
                    return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
                }
                if (InitiationAdmissionFor5722(path) is { Current: not null } initiation)
                {
                    initiation.ValidateFinalFence(record);
                    if (!CharacterCareerInitiationRules.Same(record.RetainedRuleSources!.ActivatedClosureBinding,
                            retained.ActivatedClosureBinding)
                        || !CharacterCareerInitiationRules.Same(record.RetainedRuleSources.RestoreReceipts,
                            retained.RestoreReceipts)) throw RetainedRuleSourceRejected();
                    return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
                }
                if (ContactsAdmissionFor(path) is { Current: not null } contacts)
                {
                    // Keep retained custody closed to generic writers. Only this
                    // store/path's exact Contacts record has fresh before/after admission.
                    contacts.ValidateFinalFence(record);
                    if (!CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources!.ActivatedClosureBinding,
                            retained.ActivatedClosureBinding)
                        || !CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources.RestoreReceipts,
                            retained.RestoreReceipts)) throw RetainedRuleSourceRejected();
                    return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
                }
                if (PurchaseAdmissionFor(path) is { Current: not null } purchase)
                {
                    purchase.ValidateFinalFence(record);
                    if (!CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources!.ActivatedClosureBinding,
                            retained.ActivatedClosureBinding)
                        || !CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources.RestoreReceipts,
                            retained.RestoreReceipts)) throw RetainedRuleSourceRejected();
                    return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
                }
                if (RewardAdmissionFor(path) is { Current: not null } reward)
                {
                    // Preparation is still under the original workspace lease.
                    // Keep the exact private admission and custody checks here;
                    // the caller's mandatory pre-replacement fence performs fresh
                    // before/after execution, owner, record, slot and archive checks.
                    // Repeating those full executions here adds no publication
                    // protection and delays the native confirmation unnecessarily.
                    reward.ValidatePreparation(record);
                    if (!CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources!.ActivatedClosureBinding,
                            retained.ActivatedClosureBinding)
                        || !CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources.RestoreReceipts,
                            retained.RestoreReceipts)) throw RetainedRuleSourceRejected();
                    return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
                }
                if (NaturalRecoveryAdmissionFor(path) is { Current: not null } recovery)
        {
            recovery.ValidateFinalFence(record);
            if (!CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources!.ActivatedClosureBinding,
                    retained.ActivatedClosureBinding)
                || !CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources.RestoreReceipts,
                    retained.RestoreReceipts)) throw RetainedRuleSourceRejected();
            return record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
        }
        // A generic save cannot install new current captures or acquire Gear's
                // private admission by supplying a replacement manifest.
                if (!CharacterCareerGearEquipmentTransaction.Same(record.RetainedRuleSources, retained))
                    throw RetainedRuleSourceRejected();
                // Preserve exact original-operation receipts through later unrelated writes.
                // A content change needs a newly captured/admitted environment, never ambient rebasing.
                var updatedManifest = record.RetainedRuleSources! with
                {
                    CurrentEnvironment = environment with
                    {
                        ContentRevision = record.ContentRevision ?? throw RetainedRuleSourceRejected(),
                        SavedRevision = record.SavedRevision ?? throw RetainedRuleSourceRejected()
                    },
                    ActivatedClosureBinding = retained.ActivatedClosureBinding,
                    RestoreReceipts = retained.RestoreReceipts,
                    RestoreEvidence = retained.RestoreEvidence
                };
                record = record with { RetainedRuleSources = updatedManifest };
            }
        }
        if (record.RetainedRuleSources is not null && record.RecordSchemaVersion != Sr6CareerAttributeRecordSchemaVersion)
            record = record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
        return record;
    }

    private RetainedRuleSourceWorkspace? OpenWriteRuleSources(string path, PersistedWorkspaceRecord record,
        CancellationToken cancellation = default, RewardWriteAdmission? rewardPreparation = null)
    {
        if (record.RetainedRuleSources is not { } manifest)
        {
            if (CollectRetainedRuleSourceUses(record.AuxiliaryState ?? WorkspaceDocumentAuxiliaryState.Empty).Length != 0)
                throw RetainedRuleSourceRejected();
            return null;
        }
        // The manifest is not authority: bind its private identity back to the
        // actual leased path before allowing it to select any source artifact.
        var owner = manifest.OwnerId == OwnerScope.LocalSingleUser.NormalizedValue
            ? OwnerScope.LocalSingleUser : new OwnerScope(manifest.OwnerId);
        var id = new CharacterWorkspaceId(manifest.WorkspaceId);
        if (TryGetPath(owner, id) is not { } expected || !PathComparer.Equals(Path.GetFullPath(path), expected))
            throw RetainedRuleSourceRejected();
        rewardPreparation?.ValidateClaimedPreparation(this, path, record);
        return OpenRecordRuleSources(owner, id, record, cancellation: cancellation,
            rewardPreparation: rewardPreparation?.Current is not null ? rewardPreparation : null);
    }

    private static bool IsRetainedCaptureDigest(string value)
        => value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.Ordinal)
            && value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static bool IsRetainedRuleSourceFailure(Exception error)
        => error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or OverflowException;

    private static IOException RetainedRuleSourceRejected() => new("Private retained rule sources are unavailable or invalid.");

    // May also be called after the controller's atomic RECORD rename under the same lease.
    // A failed post-rename flush means uncertain durability, never permission to replay a mutation.
    // Linux only: avoid inventing directory durability on platforms without an implemented primitive.
    private RetainedRuleSourceDurability FlushRetainedDirectoryChain(string directory)
    {
        EnsurePathContained(_stateDirectory, directory, "retained rule source directory");
        if (!OperatingSystem.IsLinux()) return RetainedRuleSourceDurability.ProcessRestartOnly;
        // The existing constructor may have created several configured-root ancestors.
        // Include those entries as well, not only the sidecar's immediate directory.
        for (string? current = Path.GetFullPath(directory); current is not null; current = Path.GetDirectoryName(current))
            FlushRetainedLinuxDirectory(current);
        return RetainedRuleSourceDurability.FileAndDirectoriesFlushed;
    }

    private static void FlushRetainedLinuxDirectory(string directory)
    {
        ThrowIfLinkOrReparsePoint(directory, "retained rule source directory");
        const int flags = 0x10000 | 0x80000 | 0x20000; // Linux O_DIRECTORY | O_CLOEXEC | O_NOFOLLOW; O_RDONLY = 0.
        int descriptor = RetainedDirectoryNative.Open(directory, flags);
        if (descriptor < 0) throw RetainedRuleSourceRejected();
        try
        {
            int result;
            do { result = RetainedDirectoryNative.Fsync(descriptor); }
            while (result != 0 && Marshal.GetLastPInvokeError() == 4); // EINTR
            if (result != 0) throw RetainedRuleSourceRejected();
        }
        finally { _ = RetainedDirectoryNative.Close(descriptor); }
    }

    private static class RetainedDirectoryNative
    {
        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        internal static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
        [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
        internal static extern int Fsync(int descriptor);
        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        internal static extern int Close(int descriptor);
    }
}
