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

    internal static FileWorkspaceStore CreateWithRetainedRuleSources(string persistentStateDirectory,
        ICharacterFileQueries characterQueries, IFileWorkspaceStoreFaultInjector faultInjector)
        => new(RequireRetainedStateRoot(persistentStateDirectory), faultInjector,
            characterQueries ?? throw new ArgumentNullException(nameof(characterQueries)));

    private FileWorkspaceStore(string persistentStateDirectory, IFileWorkspaceStoreFaultInjector faultInjector,
        ICharacterFileQueries characterQueries) : this(persistentStateDirectory, faultInjector, true)
        => _retainedCharacterQueries = characterQueries;

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
        // Linux fsync completed for the file and directory chain. This is not power-loss testing
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
        private readonly Dictionary<string, CharacterCreationRetainedRuleSourceReference> _references = new(StringComparer.Ordinal);
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
            IReadOnlyList<CharacterCreationRetainedRuleSourceReference> references)
        {
            _store = store;
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
                return new(reference, _store.FlushRetainedDirectoryChain(ResolveDirectory(create: false)));
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
            if (!_active || captureDigest is null || !_references.TryGetValue(captureDigest, out var reference)) return false;
            try
            {
                execution = new(ReadCapture(reference), _queries);
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
            if (!TryImport(bytes, reference, out var capture)) throw RetainedRuleSourceRejected();
            return capture!;
        }

        private static bool TryImport(byte[] bytes, CharacterCreationRetainedRuleSourceReference reference,
            out CharacterCreationRuleSourceCapture? capture)
            => FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(bytes, reference.CaptureDigest, out capture)
                && capture is not null && capture.IntegrityDigest == reference.CaptureDigest
                && capture.CreateExecutionResolution() is not null;

        private void ValidateReference(CharacterCreationRetainedRuleSourceReference reference)
        {
            if (reference is null || reference.SchemaVersion != 1 || reference.OwnerBinding != _ownerBinding
                || reference.WorkspaceId != _id.Value || reference.IncarnationId != _incarnation
                || reference.ByteLength <= 0 || reference.ByteLength > RetainedCharacterRuleSourceInputs.MaximumArchiveBytes
                || !IsRetainedCaptureDigest(reference.CaptureDigest)) throw RetainedRuleSourceRejected();
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

        public void Dispose() => _active = false;
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
        public override string ToString() => nameof(RetainedRuleSourceManifest);
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
        OwnerScope owner, CharacterWorkspaceId id, PersistedWorkspaceRecord record)
    {
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
        if (record.RecordSchemaVersion != RetainedWorkspaceRecordSchemaVersion || manifest.SchemaVersion != 1
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
        if (manifest.References.Any(value => value is null)
            || manifest.References.Select(value => value.CaptureDigest).Distinct(StringComparer.Ordinal).Count() != manifest.References.Length
            || !digests.SetEquals(manifest.References.Select(value => value.CaptureDigest)))
            throw RetainedRuleSourceRejected();
        var recovery = new RetainedRuleSourceWorkspace(this, owner, id, manifest.IncarnationId,
            _retainedCharacterQueries, manifest.References);
        if (manifest.CommittedCampaign is { } history) recovery.RetainCommittedCampaign(history);
        foreach (var draft in manifest.CommittedDraftCampaigns ?? []) recovery.RetainCommittedDraftCampaign(draft);
        if (recovery.ValidateRetainedRuleSources()) return recovery;
        recovery.Dispose();
        throw RetainedRuleSourceRejected();
    }

    private PersistedWorkspaceRecord ReadPrivateRecord(string path)
    {
        ThrowIfLinkOrReparsePoint(path, "workspace target");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (_retainedRuleSourcesEnabled && stream.Length > MaximumRetainedWorkspaceRecordBytes)
            throw RetainedRuleSourceRejected();
        try
        {
            return JsonSerializer.Deserialize(stream, WorkspaceRecordJsonContext.Default.PersistedWorkspaceRecord)
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
        WorkspaceWriteDisposition disposition)
    {
        var previous = disposition == WorkspaceWriteDisposition.ReplaceExisting ? ReadPrivateRecord(path) : null;
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
            }
            else record = record with { RetainedRuleSources = retained };
        }
        if (record.RetainedRuleSources is not null)
            record = record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion };
        return record;
    }

    private RetainedRuleSourceWorkspace? OpenWriteRuleSources(string path, PersistedWorkspaceRecord record)
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
        return OpenRecordRuleSources(owner, id, record);
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
