using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCareerFreeSpriteConversionAtomicCommitCapability,
    ICharacterCareerFreeSpriteConversionReadbackCommit
{
    static partial void VerifyCareerFreeSpriteConversionGuards(ref bool installed);
    public bool CareerFreeSpriteConversionGuardsInstalled { get { bool value = false; VerifyCareerFreeSpriteConversionGuards(ref value); return value; } }
    public string CareerFreeSpriteConversionRuntimeDigest => CharacterCareerFreeSpriteConversionTransaction.Hash(
        "chummer.core.sr5-career-free-sprite-conversion-runtime/v1\0" + typeof(CharacterCareerFreeSpriteConversionQuote).Module.ModuleVersionId
        + "\0" + typeof(CharacterCareerFreeSpriteConversionTransaction).Module.ModuleVersionId + "\0" + typeof(FileWorkspaceStore).Module.ModuleVersionId);
    public CharacterCareerFreeSpriteConversionResult CommitCareerFreeSpriteConversion(IOwnerContextLease ownerLease,
        CharacterCareerFreeSpriteConversionCommand command, ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default)
        => WriteFreeSpriteConversion(null, ownerLease, command, sources, cancellationToken).Result;
    public CharacterCareerFreeSpriteConversionResult CommitCareerFreeSpriteConversion(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerFreeSpriteConversionCommand command, ICharacterSourceDataResolver sources,
        CancellationToken cancellationToken = default)
        => WriteFreeSpriteConversion(owners, lease, command, sources, cancellationToken).Result;
    (CharacterCareerFreeSpriteConversionResult Result, CharacterCareerFreeSpriteConversionCommitPhase Phase)
        ICharacterCareerFreeSpriteConversionReadbackCommit.CommitCareerFreeSpriteConversion(
        IOwnerContextLeaseAccessor owners, IOwnerContextLease lease, CharacterCareerFreeSpriteConversionCommand command,
        ICharacterSourceDataResolver sources, CancellationToken cancellationToken)
        => WriteFreeSpriteConversion(owners, lease, command, sources, cancellationToken, consolidateBeforeRead: true);
    private (CharacterCareerFreeSpriteConversionResult Result, CharacterCareerFreeSpriteConversionCommitPhase Phase)
        WriteFreeSpriteConversion(IOwnerContextLeaseAccessor? owners, IOwnerContextLease lease,
        CharacterCareerFreeSpriteConversionCommand command, ICharacterSourceDataResolver sources, CancellationToken token, bool consolidateBeforeRead = false)
    {
        var phase = CharacterCareerFreeSpriteConversionCommitPhase.BeforeConversionWriter;
        var stamp = lease.Stamp; var owner = stamp.Owner;
        if (owners is not null && !LearningOwnerCurrent5703(owners, lease, stamp))
            return (new(CharacterCareerFreeSpriteConversionOutcome.Conflict), phase);
        if (!CareerFreeSpriteConversionGuardsInstalled
            || !consolidateBeforeRead && !CharacterCareerFreeSpriteConversionTransaction.OwnerMatches(stamp, command))
            return (new(CharacterCareerFreeSpriteConversionOutcome.Conflict), phase);
        var id = command.Request.WorkspaceId; string? path = TryGetPath(owner, id);
        if (path is null) return (new(CharacterCareerFreeSpriteConversionOutcome.Missing), phase);
        string digest = CharacterCareerFreeSpriteConversionTransaction.CommandDigest(command); bool completed = false;
        try
        {
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            try
            {
                IReadOnlyList<DelegatedGmCharacterEditLedgerEntry> delegated;
                GearCurrentEnvironment? environment;
                var read = consolidateBeforeRead
                    ? ReadFreeSpriteConversionUnderLease(owners!, lease, stamp, id, path, out delegated, out environment)
                    : ReadGearWorkspaceUnderLease(owner, id, path, owners, lease, stamp, out delegated, out environment);
                using var current = environment;
                if (!read.Success || read.Value is not { } saved) return (CharacterCareerFreeSpriteConversionService.MapReadFailure(read), phase);
                // Preserve the service's saved-workspace admission without requiring a
                // current revision match or a fully checkpointed document for replay.
                if (consolidateBeforeRead && (saved.Id != id || saved.ContentRevision <= 0
                    || saved.SavedRevision <= 0 || saved.SavedRevision > saved.ContentRevision))
                    return (new(CharacterCareerFreeSpriteConversionOutcome.Corrupt), phase);
                var prior = CharacterCareerFreeSpriteConversionTransaction.Lookup(owner, saved, command.Request.OperationId, digest);
                if (prior.Outcome != CharacterCareerFreeSpriteConversionOutcome.NotFound) return (prior, phase);
                if (consolidateBeforeRead && !CharacterCareerFreeSpriteConversionTransaction.OwnerMatches(stamp, command))
                    return (new(CharacterCareerFreeSpriteConversionOutcome.Conflict), phase);
                token.ThrowIfCancellationRequested();
                string? slot = ReadContinuationSlotUnderLease(path);
                string beforeDigest = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path));
                var effective = sources is null ? null : LearningSources5703(current, sources);
                var imported = LearningContext5703(current, saved);
                CharacterCareerFreeSpriteConversionSnapshot? snapshot = null;
                WorkspaceDocument? replacement; CharacterCareerFreeSpriteConversionReceipt? receipt;
                if (effective is null || !CharacterCareerFreeSpriteConversionTransaction.TryReadCurrent5732(stamp, saved, command.Request, effective,
                    LearningRuntime5703(CareerFreeSpriteConversionRuntimeDigest, current, slot, stamp), imported, out snapshot, out _)
                    || !CharacterCareerFreeSpriteConversionTransaction.TryBuild(stamp, saved, snapshot!, command, out replacement, out receipt))
                    return (new(CharacterCareerFreeSpriteConversionOutcome.Conflict), phase);
                if (!IsValidAuxiliaryState(id, receipt!.CommittedWorkspaceRevision, replacement!.AuxiliaryState, current?.Recovery))
                    return (new(CharacterCareerFreeSpriteConversionOutcome.Corrupt), phase);
                var record = BuildPersistedRecord(replacement, receipt.CommittedWorkspaceRevision, receipt.CommittedWorkspaceRevision,
                    saved.LocalHistory!, delegated, saved.DelegatedGmHistorySegmentStarts);
                var proposed = saved with { Document = replacement, ContentRevision = receipt.CommittedWorkspaceRevision,
                    SavedRevision = receipt.CommittedWorkspaceRevision };
                CharacterCurrentRuleEnvironmentReadmissionResult? readmission = null;
                if (current is not null)
                {
                    readmission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                        _retainedCharacterQueries!, proposed, token);
                    if (readmission.AfterCapture is not { } capture || readmission.AfterExecution is not { } execution
                        || readmission.Blockers.Count != 0) throw new IOException("free_sprite_conversion_proposed_environment_unavailable");
                    current.Recovery.RetainAdmittedCapture(new(capture, _retainedCharacterQueries!), capture.IntegrityDigest);
                    var continuation = new WorkspaceContinuationSnapshot(owner.NormalizedValue,
                        new(id, proposed.Document, proposed.LastUpdatedUtc, proposed.ContentRevision, proposed.SavedRevision), []);
                    record = record with
                    {
                        RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion,
                        RetainedRuleSources = current.Record.RetainedRuleSources! with
                        {
                            References = current.Recovery.SnapshotReferences(),
                            CurrentEnvironment = WorkspacePrivateRuleSourceClosure.DescribeCurrentEnvironment(continuation,
                                capture, execution.ExecutionProfile, execution.ExecutionProfileFingerprint, [])
                        }
                    };
                }
                void ValidateCommandFence()
                {
                    token.ThrowIfCancellationRequested();
                    if (lease.Stamp != stamp || !CareerFreeSpriteConversionGuardsInstalled
                        || owners is not null && !LearningOwnerCurrent5703(owners, lease, stamp)
                        || slot != ReadContinuationSlotUnderLease(path)
                        || beforeDigest != CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path))
                        || !CharacterCareerFreeSpriteConversionTransaction.IsSourceCurrent(stamp, saved, effective!, command.Request, snapshot!))
                        throw new IOException("free_sprite_conversion_admission_changed");
                }
                var admission = new FreeSpriteConversionWriteAdmission5732(this, path, record, current, proposed, readmission,
                    receipt!, ValidateCommandFence);
                // Exceptions anywhere inside the atomic writer cannot prove nonapplication.
                phase = CharacterCareerFreeSpriteConversionCommitPhase.ConversionWriterEntered;
                WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: current is null ? () =>
                {
                    ValidateCommandFence();
                    admission.ValidateExactRecord(record);
                } : null, admittedFreeSpriteConversionReceipt: receipt,
                    freeSpriteConversionAdmission: current is null ? null : admission);
                completed = true;
            }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
                && (!consolidateBeforeRead || phase == CharacterCareerFreeSpriteConversionCommitPhase.ConversionWriterEntered)) { }
            if (consolidateBeforeRead)
            {
                // BEFORE is disposed; reopen persisted AFTER under the same operation
                // lease with fresh custody, history, source admission and owner fences.
                var persisted = ReadFreeSpriteConversionUnderLease(owners!, lease, stamp, id, path, out _, out var environment);
                using var after = environment;
                if (!persisted.Success || persisted.Value is not { } saved || saved.Id != id || saved.ContentRevision <= 0
                    || saved.SavedRevision <= 0 || saved.SavedRevision > saved.ContentRevision)
                    return (new(CharacterCareerFreeSpriteConversionOutcome.UnknownOutcome), phase);
                var recovered = CharacterCareerFreeSpriteConversionTransaction.Lookup(owner, saved, command.Request.OperationId, digest);
                return (recovered.Outcome == CharacterCareerFreeSpriteConversionOutcome.Replayed
                    ? recovered with { Outcome = completed ? CharacterCareerFreeSpriteConversionOutcome.Applied : CharacterCareerFreeSpriteConversionOutcome.Replayed }
                    : new(CharacterCareerFreeSpriteConversionOutcome.UnknownOutcome), phase);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            if (consolidateBeforeRead)
                return (new(phase == CharacterCareerFreeSpriteConversionCommitPhase.ConversionWriterEntered
                    ? CharacterCareerFreeSpriteConversionOutcome.UnknownOutcome
                    : e is OperationCanceledException ? CharacterCareerFreeSpriteConversionOutcome.Canceled
                        : CharacterCareerFreeSpriteConversionOutcome.Unavailable), phase);
        }
        // Public adapters retain their existing conservative service reconciliation.
        var observed = owners is not null ? ReadCareerFreeSpriteConversionWorkspace(owners, lease, id)
            : owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (!observed.Success || observed.Value is not { } observedCurrent) return (CharacterCareerFreeSpriteConversionService.MapReadFailure(observed), phase);
        var result = CharacterCareerFreeSpriteConversionTransaction.Lookup(owner, observedCurrent, command.Request.OperationId, digest);
        if (result.Outcome == CharacterCareerFreeSpriteConversionOutcome.Replayed && completed)
            return (result with { Outcome = CharacterCareerFreeSpriteConversionOutcome.Applied }, phase);
        return (result.Outcome == CharacterCareerFreeSpriteConversionOutcome.NotFound
            ? new(CharacterCareerFreeSpriteConversionOutcome.UnknownOutcome, CurrentWorkspaceRevision: observedCurrent.ContentRevision) : result, phase);
    }
    private void ValidateFreeSpriteConversionWrite(string path, PersistedWorkspaceRecord record,
        WorkspaceWriteDisposition disposition, CharacterCareerFreeSpriteConversionReceipt? admitted)
    {
        var previous = disposition == WorkspaceWriteDisposition.ReplaceExisting ? ReadPrivateRecord(path) : null;
        var before = previous?.AuxiliaryState?.CharacterCareerFreeSpriteConversionReceipts;
        var after = record.AuxiliaryState?.CharacterCareerFreeSpriteConversionReceipts;
        if (admitted is null)
        {
            // New-file continuation restoration has separately validated imported history.
            // Ordinary saves cannot append, remove, replace or forge this ledger.
            if (previous is not null && JsonSerializer.Serialize(before) != JsonSerializer.Serialize(after))
                throw new IOException("Free Sprite conversion history requires its typed transaction.");
            return;
        }
        if (previous is null || after is null || after.Count != (before?.Count ?? 0) + 1
            || JsonSerializer.Serialize(after.Take(after.Count - 1)) != JsonSerializer.Serialize(before ?? [])
            || JsonSerializer.Serialize(after[^1]) != JsonSerializer.Serialize(admitted)
            || !CharacterCareerFreeSpriteConversionTransaction.IsCoherent(admitted)
            || record.ContentRevision != previous.ContentRevision + 1 || record.SavedRevision != record.ContentRevision
            || record.ContentRevision != admitted.CommittedWorkspaceRevision)
            throw new IOException("Free Sprite conversion must append exactly one admitted receipt.");
    }
    // Passed only by the typed writer, never inherited by a nested/generic write.
    // A single-use claim also rejects reentry with the same private capability.
    private sealed class FreeSpriteConversionWriteAdmission5732
    {
        private readonly FileWorkspaceStore _store;
        private readonly string _path;
        private readonly PersistedWorkspaceRecord _record;
        private readonly string _recordDigest;
        private readonly WorkspaceStoredDocument _after;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult? _readmission;
        private readonly CharacterCareerFreeSpriteConversionReceipt _receipt;
        private readonly Action _validateCommandFence;
        private bool _claimed;
        internal GearCurrentEnvironment? Current { get; }
        internal FreeSpriteConversionWriteAdmission5732(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record,
            GearCurrentEnvironment? current, WorkspaceStoredDocument after, CharacterCurrentRuleEnvironmentReadmissionResult? readmission,
            CharacterCareerFreeSpriteConversionReceipt receipt, Action validateCommandFence)
        {
            _store = store; _path = System.IO.Path.GetFullPath(path); _record = record;
            Current = current; _after = after; _readmission = readmission; _receipt = receipt;
            _validateCommandFence = validateCommandFence;
            _recordDigest = CharacterCreationFinalizationDigest.Compute(record);
        }
        internal void Claim(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record,
            WorkspaceWriteDisposition disposition, CharacterCareerFreeSpriteConversionReceipt? receipt)
        {
            if (_claimed || Current is null || disposition != WorkspaceWriteDisposition.ReplaceExisting
                || !ReferenceEquals(receipt, _receipt)) throw new IOException("free_sprite_conversion_admission_mismatch");
            ValidateIdentity(store, path, record);
            _claimed = true;
        }
        private void ValidateIdentity(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record)
        {
            if (!ReferenceEquals(store, _store) || !PathComparer.Equals(_path, System.IO.Path.GetFullPath(path)))
                throw new IOException("free_sprite_conversion_admission_mismatch");
            if (!ReferenceEquals(record, _record)) throw new IOException("free_sprite_conversion_exact_record_changed");
            ValidateExactRecord(record);
        }
        internal void ValidateExactRecord(PersistedWorkspaceRecord record)
        {
            if (CharacterCreationFinalizationDigest.Compute(record) != _recordDigest)
                throw new IOException("free_sprite_conversion_exact_record_changed");
        }
        internal void ValidatePreparation(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record)
        {
            if (!_claimed || Current is null) throw new IOException("free_sprite_conversion_admission_mismatch");
            ValidateIdentity(store, path, record);
        }
        internal void ValidateFinalFence(FileWorkspaceStore store, string path, PersistedWorkspaceRecord record,
            RetainedRuleSourceWorkspace? recovery)
        {
            ValidatePreparation(store, path, record);
            _validateCommandFence();
            var current = Current ?? throw new IOException("free_sprite_conversion_admission_mismatch");
            current.ValidateFinalFence(); // Fresh complete BEFORE, including live owner/storage/source fences.
            // Retain the generic late admission's recovered capture, materialization,
            // exact environment description and creation-history authority. The existing
            // AFTER execution is only a comparison target for a full fresh Revalidate.
            if (recovery is null || !recovery.ValidateRetainedRuleSources()
                || record.RetainedRuleSources?.CurrentEnvironment is not { } environment
                || !TryMaterializeRecord(record, out var document, out var revision, out var saved, out _)
                || !recovery.TryRecoverRuleSourceCapture(environment.CaptureDigest, out var capture) || capture is null
                || _readmission?.AfterCapture is not { } admittedCapture || _readmission.AfterExecution is not { } execution
                || _readmission.Blockers.Count != 0 || capture.IntegrityDigest != admittedCapture.IntegrityDigest)
                throw new IOException("free_sprite_conversion_proposed_environment_changed");
            var snapshot = new WorkspaceContinuationSnapshot(environment.OwnerId,
                new(_after.Id, document, _after.LastUpdatedUtc, revision, saved), []);
            var actual = WorkspacePrivateRuleSourceClosure.DescribeCurrentEnvironment(snapshot, capture,
                execution.ExecutionProfile, execution.ExecutionProfileFingerprint, _readmission.Blockers);
            if (!JsonSerializer.SerializeToUtf8Bytes(actual).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(environment))
                || execution.CaptureDigest != environment.CaptureDigest
                || execution.CompatibilityProfile != environment.CompatibilityProfile
                || execution.RequiredPackageSetDigest != environment.RequiredPackageSetDigest
                || !execution.Revalidate((_after with { Document = document, ContentRevision = revision, SavedRevision = saved })
                    .WithValidatedCreationReceiptHistory(recovery), capture, store._retainedCharacterQueries!))
                throw new IOException("free_sprite_conversion_proposed_environment_changed");
            // Fresh evaluation can invoke queries: observe command, exact bytes and
            // both live source closures again before the central writer publishes.
            _validateCommandFence();
            ValidatePreparation(store, path, record);
            if (!current.Recovery.ValidateRetainedRuleSources() || !recovery.ValidateRetainedRuleSources())
                throw new IOException("free_sprite_conversion_proposed_environment_changed");
        }
    }
}
