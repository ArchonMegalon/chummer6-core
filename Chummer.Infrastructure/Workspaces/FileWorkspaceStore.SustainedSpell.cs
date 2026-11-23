using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Gear = Chummer.Application.Characters.CharacterCareerGearEquipmentTransaction;
using Tx = Chummer.Application.Characters.CharacterSustainedSpellTransaction;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    private static string SustainedSpellRuntimeDigest => Gear.Hash("chummer.sustained-spell.runtime/v1\0"
        + typeof(CharacterSustainedObjectRules).Module.ModuleVersionId + "\0"
        + typeof(Tx).Module.ModuleVersionId + "\0" + typeof(FileWorkspaceStore).Module.ModuleVersionId);

    private static ICharacterSourceDataResolver SustainedResolver(GearCurrentEnvironment? current, ICharacterSourceDataResolver fallback)
        => current is null ? fallback : current.Capture.CreateCurrentEnvironmentResolution(
            current.Execution.ExecutionProfile, current.Execution.ExecutionProfileFingerprint) is { } resolution
                ? resolution.Resolver
                : throw new IOException("sustained_current_resolver_unavailable");
    private static void CheckSustainedOwner(OwnerContextStamp owner, IOwnerContextAccessor owners, IOwnerContextLease lease)
    {
        if (!owner.IsValid || lease.Stamp != owner || owners is not IOwnerContextLeaseAccessor authority || authority.Capture() != owner)
            throw new IOException("sustained_owner_changed");
    }

    internal CharacterSustainedSpellResult ReadSustainedSpell(OwnerContextStamp owner, IOwnerContextAccessor owners,
        IOwnerContextLease lease, CharacterWorkspaceId id, ICharacterSourceDataResolver sources,
        CharacterSustainedSpellBinding? displayed = null, Guid operationId = default,
        CharacterSustainedSpellPreparationRequest? request = null)
    {
        CheckSustainedOwner(owner, owners, lease);
        string? path = TryGetPath(owner.Owner, id);
        if (path is null) return new(CharacterSustainedSpellOutcome.Unavailable);
        using var operation = AcquireWorkspaceOperation(path);
        // Read and preview own one synchronous workspace lease. Reuse its complete
        // private-record observation through the existing immutable-query path;
        // the final read fence below and Confirm's fresh admission remain intact.
        var read = ReadGearWorkspaceCoreUnderLease(owner.Owner, id, path, owners, lease, owner,
            deferFreeSpriteReadObservation: false, out _, out var environment, completeReadObservation: true);
        using var current = environment;
        if (!read.Success || read.Value is not { } saved || HasPendingGearEquipment(saved)) return new(CharacterSustainedSpellOutcome.Unavailable);
        var resolver = SustainedResolver(current, sources);
        if (!Tx.TryRead(owner, saved, resolver, SustainedSpellRuntimeDigest, ReadContinuationSlotUnderLease(path), out var catalog, current?.Capture))
            return new(CharacterSustainedSpellOutcome.Unavailable);
        CharacterSustainedSpellCommand? command = null;
        if (displayed is not null)
        {
            if (!Gear.Same(displayed, catalog!.Binding) || operationId == Guid.Empty || request is null
                || ReadSustainedCancellations(path).Entries.Any(r => r.Command.OperationId == operationId)
                || saved.Document.AuxiliaryState.CharacterSustainedSpellReceipts?.Any(r => r.Command.OperationId == operationId) == true
                || !CharacterSustainedSpellPreparationPlanner.TryPreview(owner.Owner, saved, resolver, request, out var review, current?.Capture, current?.Execution)
                || review is null || !Gear.Same(review.Binding, displayed.Preparation)) return new(CharacterSustainedSpellOutcome.Conflict);
            command = new(operationId, displayed, review);
            if (!Tx.TryBuild(owner, saved, resolver, catalog!, command with { ExplicitlyConfirmed = true }, out _, out _, current?.Capture, current?.Execution))
                return new(CharacterSustainedSpellOutcome.Unavailable);
        }
        current?.ValidateReadObservation();
        CheckSustainedOwner(owner, owners, lease);
        return new(CharacterSustainedSpellOutcome.Available, catalog, command, CurrentRevision: saved.ContentRevision);
    }

    internal CharacterSustainedSpellResult LookupSustainedSpell(OwnerScope owner, CharacterSustainedSpellLookup original)
    {
        if (!Tx.ValidLookup(original) || !Tx.ValidOwner(owner) || owner.NormalizedValue != original.OriginalOwnerId
            || owner.IsLocalSingleUser != original.TrustedLocalOwner) return new(CharacterSustainedSpellOutcome.Conflict);
        string? path = TryGetPath(owner, original.WorkspaceId);
        if (path is null || !TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
            return new(CharacterSustainedSpellOutcome.Unavailable);
        // This path performs no Get, migration, source recapture, temp cleanup,
        // slot rotation, repair, or dispatch. Receipt validation is historical.
        using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        return LookupSustainedUnderLease(owner, path, original);
    }
    private CharacterSustainedSpellResult LookupSustainedUnderLease(OwnerScope owner, string path, CharacterSustainedSpellLookup original)
    {
        if (!File.Exists(path)) return new(CharacterSustainedSpellOutcome.Conflict);
        var record = ReadPrivateRecord(path);
        if (record.RecordSchemaVersion is null or < 3 || !TryMaterializeRecord(record,
            out var document, out long revision, out long savedRevision, out bool migration) || migration
            || record.LocalHistory is not { } history || !history.IsValid(revision)
            || history.LocalAdoption is { } adoption && (adoption.OwnerId != owner.NormalizedValue || adoption.WorkspaceId != original.WorkspaceId)
            || !WorkspaceDocumentAuxiliaryState.HasSafeExtensionData(document.AuxiliaryState))
            return new(CharacterSustainedSpellOutcome.Unavailable);
        var saved = new WorkspaceStoredDocument(original.WorkspaceId, document, revision, savedRevision,
            new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)) { LocalHistory = history };
        var applied = Tx.Lookup(owner, saved, ReadContinuationSlotUnderLease(path), original);
        // Applied history always resolves the actual original receipt. Never turn it into an exclusion.
        if (applied.Outcome != CharacterSustainedSpellOutcome.NotFound) return applied;
        var archive = ReadSustainedCancellations(path);
        var cancelled = archive.Entries.SingleOrDefault(r => r.Command.OperationId == original.OperationId);
        if (cancelled is null) return applied;
        if (!Gear.Same(Tx.Original(cancelled.Command), original) || cancelled.ObservedRevision > revision)
            return new(CharacterSustainedSpellOutcome.Conflict);
        // The file was flushed before rename. An uncertain directory-flush result must
        // not become release evidence just because the rename is readable. Flush only
        // existing directory handles, then reread; no content writes, creation or repair.
        _ = FlushRetainedDirectoryChain(Path.GetDirectoryName(path)!);
        if (!Gear.Same(archive, ReadSustainedCancellations(path)))
            return new(CharacterSustainedSpellOutcome.Unavailable);
        return new(CharacterSustainedSpellOutcome.Cancelled, CurrentRevision: revision, Cancellation: cancelled);
    }

    // Local execution custody deliberately lives outside exportable/importable auxiliary history.
    // Generic document writers cannot supply, replace or strip this archive. Like the existing
    // Gear original-outcome archive it is scoped to the physical workspace path, but this typed
    // contract claims no terminal evidence for missing/replaced original incarnations or slots.
    private const string SustainedCancellationSuffix = ".sustained-cancellations.v1.json";
    private static readonly JsonSerializerOptions SustainedCancellationJson = new()
    { MaxDepth = 32, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private sealed record SustainedCancellations(int SchemaVersion,
        IReadOnlyList<CharacterSustainedSpellCancellationReceipt> Entries, string Digest);
    private static SustainedCancellations SealSustainedCancellations(
        IReadOnlyList<CharacterSustainedSpellCancellationReceipt> entries)
    {
        var archive = new SustainedCancellations(1, entries, "");
        return archive with { Digest = Gear.Hash(JsonSerializer.Serialize(archive, SustainedCancellationJson)) };
    }
    private SustainedCancellations ReadSustainedCancellations(string workspacePath)
    {
        string path = workspacePath + SustainedCancellationSuffix;
        EnsurePathContained(_stateDirectory, path, "sustained cancellation metadata");
        ThrowIfLinkOrReparsePoint(path, "sustained cancellation metadata");
        FileStream stream;
        try { stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (FileNotFoundException) { return SealSustainedCancellations([]); }
        using (stream)
        {
            if (stream.Length is <= 0 or > Gear.MaximumLedgerBytes)
                throw new IOException("Sustained cancellation metadata size is invalid.");
            try
            {
                using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
                RequireUniqueGearMetadata(json.RootElement);
                var archive = JsonSerializer.Deserialize<SustainedCancellations>(json.RootElement, SustainedCancellationJson);
                if (archive is not { SchemaVersion: 1, Entries: not null }
                    || archive.Entries.Count > Tx.MaximumReceipts
                    || archive.Entries.Any(r => !Tx.ValidCancellation(r)
                        || !PathComparer.Equals(TryGetPath(r.Command.Binding.Preparation.TrustedLocalOwner
                            ? OwnerScope.LocalSingleUser : new OwnerScope(r.Command.Binding.Preparation.OwnerId),
                            r.Command.Binding.Preparation.WorkspaceId), workspacePath))
                    || archive.Entries.Select(r => r.Command.OperationId).Distinct().Count() != archive.Entries.Count
                    || archive.Digest != SealSustainedCancellations(archive.Entries).Digest)
                    throw new IOException("Sustained cancellation metadata is invalid.");
                return archive;
            }
            catch (JsonException error) { throw new IOException("Sustained cancellation metadata is unreadable.", error); }
        }
    }
    internal CharacterSustainedSpellResult CancelSustainedSpell(OwnerContextStamp currentOwner,
        IOwnerContextAccessor owners, IOwnerContextLease lease, CharacterSustainedSpellCommand command,
        string originalDigest, CancellationToken token)
    {
        CheckSustainedOwner(currentOwner, owners, lease);
        if (!Tx.ValidCommand(command) || Tx.CommandDigest(command) != originalDigest
            || !Tx.MatchesOwner(currentOwner.Owner, command.Binding.Preparation))
            return new(CharacterSustainedSpellOutcome.Conflict);
        string? path = TryGetPath(currentOwner.Owner, command.Binding.Preparation.WorkspaceId);
        if (path is null || !TrySecureExistingWorkspaceDirectory(currentOwner.Owner, allowLegacyMigration: false))
            return new(CharacterSustainedSpellOutcome.Unavailable);
        // Exactly the same process/cross-process lease as Confirm, with no Get, migration,
        // rule recapture, cleanup or original-command redispatch. First terminal publication wins.
        using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        var original = Tx.Original(command);
        var prior = LookupSustainedUnderLease(currentOwner.Owner, path, original);
        if (prior.Outcome != CharacterSustainedSpellOutcome.NotFound) return prior;
        var before = ReadPrivateRecord(path);
        if (before.ContentRevision is not { } revision || before.SavedRevision is not { } savedRevision
            || revision < command.Binding.Preparation.ContentRevision
            || savedRevision < command.Binding.Preparation.SavedRevision)
            return new(CharacterSustainedSpellOutcome.Conflict);
        var archive = ReadSustainedCancellations(path);
        var receipt = new CharacterSustainedSpellCancellationReceipt(command, originalDigest, revision, "");
        receipt = receipt with { ReceiptDigest = Tx.CancellationDigest(receipt) };
        if (!Tx.ValidCancellation(receipt) || archive.Entries.Count >= Tx.MaximumReceipts)
            return new(CharacterSustainedSpellOutcome.Unavailable);
        var terminal = SealSustainedCancellations(archive.Entries.Append(receipt).ToArray());
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(terminal, SustainedCancellationJson);
        if (bytes.Length > Gear.MaximumLedgerBytes) return new(CharacterSustainedSpellOutcome.Unavailable);
        string target = path + SustainedCancellationSuffix;
        string temporary = target + TempFileMarker + Guid.NewGuid().ToString("N");
        EnsurePathContained(_stateDirectory, target, "sustained cancellation metadata");
        EnsurePathContained(_stateDirectory, temporary, "sustained cancellation temporary file");
        try
        {
            token.ThrowIfCancellationRequested();
            ThrowIfLinkOrReparsePoint(target, "sustained cancellation metadata");
            using (var stream = OpenNewSecureFile(temporary, FileAccess.Write, FileShare.None, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(flushToDisk: true); }
            _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterTempFileFlushed, target, temporary);
            // Fresh current authorization is the write authority, never the stale stamp embedded
            // in the original intent. Revalidate it and the entire original location at publication.
            token.ThrowIfCancellationRequested();
            CheckSustainedOwner(currentOwner, owners, lease);
            if (!TrySecureExistingWorkspaceDirectory(currentOwner.Owner, allowLegacyMigration: false)
                || Tx.CommandDigest(command) != originalDigest
                || !Gear.Same(before, ReadPrivateRecord(path))
                || ReadContinuationSlotUnderLease(path) != original.SlotGeneration
                || !Gear.Same(archive, ReadSustainedCancellations(path))
                || LookupSustainedUnderLease(currentOwner.Owner, path, original).Outcome != CharacterSustainedSpellOutcome.NotFound)
                throw new IOException("Sustained cancellation original location changed.");
            ThrowIfLinkOrReparsePoint(target, "sustained cancellation metadata");
            ThrowIfLinkOrReparsePoint(temporary, "sustained cancellation temporary file");
            File.Move(temporary, target, overwrite: true);
            // Same platform durability contract as the workspace/Gear archive: flushed atomic
            // replacement and directory-chain flush; no universal power-loss claim on all hosts.
            _ = FlushRetainedDirectoryChain(Path.GetDirectoryName(target)!);
            _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterTargetReplaced, target, temporary);
            return LookupSustainedUnderLease(currentOwner.Owner, path, original);
        }
        finally { DeleteRegularFileIfPresent(temporary, "sustained cancellation temporary file"); }
    }

    internal CharacterSustainedSpellResult ConfirmSustainedSpell(OwnerContextStamp owner, IOwnerContextAccessor owners,
        IOwnerContextLease lease, CharacterSustainedSpellCommand command, string originalDigest,
        ICharacterSourceDataResolver sources, CancellationToken token)
    {
        CheckSustainedOwner(owner, owners, lease);
        if (!Tx.ValidCommand(command) || Tx.CommandDigest(command) != originalDigest
            || !Tx.MatchesOwner(owner.Owner, command.Binding.Preparation)) return new(CharacterSustainedSpellOutcome.Conflict);
        var id = command.Binding.Preparation.WorkspaceId;
        string? path = TryGetPath(owner.Owner, id);
        if (path is null) return new(CharacterSustainedSpellOutcome.Unavailable);
        using var operation = AcquireWorkspaceOperation(path);
        var prior = LookupSustainedUnderLease(owner.Owner, path, Tx.Original(command));
        if (prior.Outcome != CharacterSustainedSpellOutcome.NotFound) return prior;
        token.ThrowIfCancellationRequested();
        var read = ReadGearWorkspaceUnderLease(owner.Owner, id, path, owners, lease, owner, out var delegated, out var environment);
        using var current = environment;
        if (!read.Success || read.Value is not { } saved || HasPendingGearEquipment(saved)) return new(CharacterSustainedSpellOutcome.Unavailable);
        var resolver = SustainedResolver(current, sources);
        if (!Tx.TryRead(owner, saved, resolver, SustainedSpellRuntimeDigest, ReadContinuationSlotUnderLease(path), out var catalog, current?.Capture)
            || !Tx.TryBuild(owner, saved, resolver, catalog!, command, out var replacement, out var receipt, current?.Capture, current?.Execution))
            return new(CharacterSustainedSpellOutcome.Conflict);
        var after = saved with { Document = replacement!, ContentRevision = receipt!.CommittedRevision, SavedRevision = receipt.CommittedRevision };
        if (!IsValidAuxiliaryState(id, after.ContentRevision, after.Document.AuxiliaryState, current?.Recovery))
            return new(CharacterSustainedSpellOutcome.Unavailable);
        var record = BuildPersistedRecord(after.Document, after.ContentRevision, after.SavedRevision,
            saved.LocalHistory!, delegated, saved.DelegatedGmHistorySegmentStarts);
        CharacterCurrentRuleEnvironmentReadmissionResult? readmission = null;
        if (current is not null)
        {
            readmission = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture,
                _retainedCharacterQueries!, after, token);
            if (readmission.AfterCapture is not { } capture || readmission.AfterExecution is not { } execution
                || readmission.Blockers.Count != 0) return new(CharacterSustainedSpellOutcome.Unavailable);
            current.Recovery.RetainAdmittedCapture(new(capture, _retainedCharacterQueries!), capture.IntegrityDigest);
            var snapshot = new WorkspaceContinuationSnapshot(owner.Owner.NormalizedValue,
                new(id, after.Document, after.LastUpdatedUtc, after.ContentRevision, after.SavedRevision), []);
            var description = WorkspacePrivateRuleSourceClosure.DescribeCurrentEnvironment(snapshot, capture,
                execution.ExecutionProfile, execution.ExecutionProfileFingerprint, []);
            record = record with { RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion,
                RetainedRuleSources = current.Record.RetainedRuleSources! with
                { References = current.Recovery.SnapshotReferences(), CurrentEnvironment = description } };
        }
        else record = PreserveRetainedRuleSources(path, record, WorkspaceWriteDisposition.ReplaceExisting);

        using var admission = new SustainedWriteAdmission(this, path, receipt, record, current, after, readmission, () =>
        {
            token.ThrowIfCancellationRequested(); CheckSustainedOwner(owner, owners, lease);
            if (Tx.CommandDigest(command) != originalDigest
                || LookupSustainedUnderLease(owner.Owner, path, Tx.Original(command)).Outcome != CharacterSustainedSpellOutcome.NotFound
                || !Tx.TryRead(owner, saved, resolver, SustainedSpellRuntimeDigest, ReadContinuationSlotUnderLease(path), out var fresh, current?.Capture)
                || !Tx.TryBuild(owner, saved, resolver, fresh!, command, out var finalDocument, out var finalReceipt, current?.Capture, current?.Execution)
                || !Gear.Same(finalDocument, after.Document) || !Gear.Same(finalReceipt, receipt))
                throw new IOException("sustained_original_review_changed");
        });
        // One rename publishes the complete character, auxiliary state and original
        // receipt. There is no postcondition inference and no second commit step.
        WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting);
        return new(CharacterSustainedSpellOutcome.Applied, Receipt: receipt, CurrentRevision: after.ContentRevision);
    }

    [ThreadStatic] private static SustainedWriteAdmission? _sustainedAdmission;
    private sealed class SustainedWriteAdmission : IDisposable
    {
        private readonly SustainedWriteAdmission? previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        internal CharacterSustainedSpellReceipt Receipt { get; }
        internal GearCurrentEnvironment? Current { get; }
        private readonly string beforeDigest, afterDigest;
        private readonly WorkspaceStoredDocument after;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult? readmission;
        private readonly Action revalidate;
        internal SustainedWriteAdmission(FileWorkspaceStore store, string path, CharacterSustainedSpellReceipt receipt,
            PersistedWorkspaceRecord record, GearCurrentEnvironment? current, WorkspaceStoredDocument proposed,
            CharacterCurrentRuleEnvironmentReadmissionResult? admitted, Action fence)
        {
            previous = _sustainedAdmission; Store = store; Path = System.IO.Path.GetFullPath(path); Receipt = receipt;
            Current = current; after = proposed; readmission = admitted; revalidate = fence;
            beforeDigest = CharacterCreationFinalizationDigest.Compute(store.ReadPrivateRecord(path));
            afterDigest = CharacterCreationFinalizationDigest.Compute(record); _sustainedAdmission = this;
        }
        // Preliminary write validators bind the exact original and replacement.
        // The complete live rule/owner fence runs after staging and caller hooks,
        // immediately before the atomic publication in WriteRecordAtomically.
        internal void ValidateRecordBinding(PersistedWorkspaceRecord record)
        {
            if (ReadContinuationSlotUnderLease(Path) != Receipt.Command.Binding.SlotGeneration
                || CharacterCreationFinalizationDigest.Compute(Store.ReadPrivateRecord(Path)) != beforeDigest
                || CharacterCreationFinalizationDigest.Compute(record) != afterDigest)
                throw new IOException("sustained_record_or_slot_changed");
        }
        internal void ValidateFinalFence(PersistedWorkspaceRecord record)
        {
            ValidateRecordBinding(record);
            Current?.ValidateFinalFence();
            if (Current is not null && (readmission?.AfterCapture is not { } capture || readmission.AfterExecution is not { } execution
                || !execution.Revalidate(after, capture, Store._retainedCharacterQueries!)))
                throw new IOException("sustained_after_environment_changed");
            revalidate();
        }
        public void Dispose() => _sustainedAdmission = previous;
    }
    private SustainedWriteAdmission? SustainedAdmissionFor(string path)
        => _sustainedAdmission is { } value && ReferenceEquals(value.Store, this)
            && PathComparer.Equals(value.Path, Path.GetFullPath(path)) ? value : null;

    private void ValidateSustainedWrite(string path, PersistedWorkspaceRecord record, WorkspaceWriteDisposition disposition)
    {
        var previous = disposition == WorkspaceWriteDisposition.ReplaceExisting ? ReadPrivateRecord(path) : null;
        var before = previous?.AuxiliaryState?.CharacterSustainedSpellReceipts;
        var after = record.AuxiliaryState?.CharacterSustainedSpellReceipts;
        var admission = SustainedAdmissionFor(path);
        if (admission is null)
        {
            bool replacingIncarnation = IsGearIncarnationReplacement(previous, record);
            if (previous is not null && !replacingIncarnation && !Gear.Same(before, after))
                throw new IOException("Sustained spell receipts require their typed operation and remain immutable.");
            if ((previous is null || replacingIncarnation) && after is { Count: > 0 }
                && (record.LocalHistory is not { } history || after.Any(r => !history.IsImportedRevision(r.CommittedRevision))))
                throw new IOException("Imported sustained history is never local execution authority.");
            return;
        }
        var receipt = admission.Receipt;
        if (previous is null || !Gear.Same(after, (before ?? []).Append(receipt).ToArray())
            || !Tx.IsValidLedger(receipt.Command.Binding.Preparation.WorkspaceId, receipt.CommittedRevision, after)
            || previous.ContentRevision != receipt.Command.Binding.Preparation.ContentRevision
            || record.ContentRevision != receipt.CommittedRevision || record.SavedRevision != record.ContentRevision
            || !Gear.Same(previous.LocalHistory, record.LocalHistory)
            || !Gear.Same(record.AuxiliaryState! with { CharacterSustainedSpellReceipts = before }, previous.AuxiliaryState))
            throw new IOException("Sustained operation must preserve the full auxiliary state and append exactly one receipt.");
        admission.ValidateRecordBinding(record);
    }
}
