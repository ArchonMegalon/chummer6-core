using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCreationCurrentLifeModuleFinalizationCapability
{
    public CharacterCreationFoundationResult<CharacterCreationFoundationState>? LoadCurrentLifeModuleFinalization(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        ICharacterFileQueries characterFiles, ICharacterSourceDataResolver unboundResolver,
        ILifeModulesCatalogService unboundCatalog, CancellationToken cancellationToken = default)
        => ReadCurrentLifeModuleNative(owners, expectedOwner, workspaceId, unboundResolver,
            (current, _) => current.Execution.LoadLifeModuleFinalization(current.Stored, current.Capture,
                _retainedCharacterQueries!, cancellationToken),
            (stored, resolver, _) => OwnerBoundCharacterCreationLifeModuleFinalizationService.ReadNativeState(
                stored, characterFiles, resolver, unboundCatalog), cancellationToken);

    public CharacterCreationLifeModuleCatalogProjection? LoadCurrentLifeModuleSuggestionCatalog(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        CharacterCreationFoundationState expectedFoundation, ICharacterFileQueries characterFiles,
        ICharacterSourceDataResolver unboundResolver, ILifeModulesCatalogService unboundCatalog,
        CancellationToken cancellationToken = default)
    {
        if (expectedFoundation?.Binding is null) return null;
        // Freeze caller collections before entering owner/workspace leases.
        var expected = JsonSerializer.SerializeToElement(expectedFoundation)
            .Deserialize<CharacterCreationFoundationState>()!;
        return ReadCurrentLifeModuleNative(owners, expectedOwner, expected.Binding.WorkspaceId, unboundResolver,
            (current, environmentDigest) => current.Execution.LoadLifeModuleSuggestionCatalog(
                current.Stored, current.Capture, _retainedCharacterQueries!, expected, environmentDigest, cancellationToken),
            (stored, resolver, environmentDigest) =>
                OwnerBoundCharacterCreationLifeModuleFinalizationService.ReadNativeSuggestionCatalog(
                    stored, characterFiles, resolver, unboundCatalog, expected, environmentDigest), cancellationToken);
    }

    private T? ReadCurrentLifeModuleNative<T>(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        CharacterWorkspaceId workspaceId, ICharacterSourceDataResolver unboundResolver,
        Func<GearCurrentEnvironment, string, T?> readBound,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string, T?> readUnbound,
        CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidLifeModuleOwner(expectedOwner) || owners is not IOwnerContextLeaseAccessor authority
            || !TryAcquireLifeModuleOwnerLease(owners, expectedOwner, out var lease)) return null;
        using (lease)
        try
        {
            var owner = expectedOwner.Owner;
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false)
                || TryGetPath(owner, workspaceId) is not { } path || !File.Exists(path)) return null;
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            var record = ReadPrivateRecord(path);
            if (record.RetainedRuleSources?.PrivateClosureBinding is not null) return null;
            if (record.RetainedRuleSources?.CurrentEnvironment is not null)
            {
                var read = ReadGearWorkspaceUnderLease(owner, workspaceId, path, owners, lease, expectedOwner, out _, out var admitted);
                using var current = admitted;
                if (!read.Success || current is null || ReadContinuationSlotUnderLease(path) is null) return null;
                var result = readBound(current, HashLifeModule(new { current.Record, Slot = ReadContinuationSlotUnderLease(path) }));
                current.ValidateReadObservation();
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }
            if (record.RetainedRuleSources is { } unbound
                && (unbound.ActivatedClosureBinding is not null || unbound.RestoreReceipts is { Length: > 0 }
                    || unbound.LifeModuleOriginal is not null)) return null;
            // Positive modern-unbound proof only. Observe the exact envelope and
            // history without migration/recovery and keep the same leases through
            // native evaluation. No callback can reenter this store under its lock.
            string before = HashLifeModule(record);
            string? slot = ReadContinuationSlotUnderLease(path);
            using var history = WorkspacePrivateRuleSourceClosureCompletion.BeginHistoryObservation();
            var observed = ReadWorkspaceUnderLease(owner, workspaceId, path, out _, continuationRead: true, privateClosureRead: true);
            if (!observed.Success || observed.Value is not { LocalHistory: not null } stored) return null;
            using var recovery = OpenRecordRuleSources(owner, workspaceId, record);
            bool Unchanged() => authority.Capture() == expectedOwner && lease.Stamp == expectedOwner
                && PathComparer.Equals(TryGetPath(owner, workspaceId), path)
                && ReadContinuationSlotUnderLease(path) == slot && HashLifeModule(ReadPrivateRecord(path)) == before
                && (recovery is null || recovery.ValidateRetainedRuleSources());
            if (!Unchanged()) return null;
            cancellationToken.ThrowIfCancellationRequested();
            using var scope = (unboundResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            if (!Unchanged()) return null;
            var native = readUnbound(stored, scope ?? unboundResolver, HashLifeModule(new { Record = record, Slot = slot }));
            cancellationToken.ThrowIfCancellationRequested();
            return Unchanged() ? native : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (LifeModuleStorageFailure(error)) { return null; }
    }

    public CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt> CommitLifeModuleFinalization(
        CharacterCreationFoundationFinalizationConfirmRequest request, ICharacterSourceDataResolver resolver,
        ILifeModulesCatalogService catalog, ICharacterFileQueries characterFiles)
        => CommitLifeModuleFinalization(OwnerScope.LocalSingleUser, request, resolver, catalog, characterFiles);

    public CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt> CommitLifeModuleFinalization(
        OwnerScope owner, CharacterCreationFoundationFinalizationConfirmRequest request, ICharacterSourceDataResolver resolver,
        ILifeModulesCatalogService catalog, ICharacterFileQueries characterFiles)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(characterFiles);
        if (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)
            || !CharacterCreationLifeModuleFinalizationTransaction.IsConfirmed(request))
            return CharacterCreationLifeModuleFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired);
        // Freeze caller-owned collections before acquiring storage authority.
        request = JsonSerializer.SerializeToElement(request).Deserialize<CharacterCreationFoundationFinalizationConfirmRequest>()!;
        if (!CharacterCreationLifeModuleFinalizationTransaction.IsConfirmed(request))
            return CharacterCreationLifeModuleFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
        var id = request.Binding.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null) return CharacterCreationLifeModuleFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return CharacterCreationLifeModuleFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved)
                return CharacterCreationLifeModuleFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            var prior = CharacterCreationLifeModuleFinalizationTransaction.Lookup(saved, request);
            if (prior is not null) return prior;
            if (!CharacterCreationLifeModuleFinalizationTransaction.TryBuild(owner, saved, resolver, catalog, characterFiles,
                    request, out var replacement, out var receipt, out var blockers))
                return CharacterCreationLifeModuleFinalizationTransaction.Blocked(blockers);
            var record = BuildPersistedRecord(replacement!, receipt!.ContentRevision, receipt.SavedRevision,
                saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting, beforeTargetReplace: () =>
            {
                // Source admission is repeated after flush, immediately before
                // replacing the durable document. No partial effect write exists.
                if (!CharacterCreationLifeModuleFinalizationTransaction.TryBuild(owner, saved, resolver, catalog, characterFiles,
                        request, out var current, out var finalReceipt, out _)
                    || finalReceipt!.ReceiptDigest != receipt.ReceiptDigest
                    || current!.Content != replacement!.Content
                    || current.AuxiliaryStateDigest != replacement.AuxiliaryStateDigest)
                    throw new IOException("Life Modules finalization source changed before commit.");
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // A failure after rename is an observation problem, not permission to
            // retry the mutation. The exact durable receipt decides the result.
        }
        var observed = owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (observed.Success && observed.Value is { } workspace
            && CharacterCreationLifeModuleFinalizationTransaction.Lookup(workspace, request) is { } result) return result;
        return CharacterCreationLifeModuleFinalizationTransaction.Blocked(CharacterCreationFinalizationBlockers.AtomicPersistenceRejected);
    }
    public CurrentLifeModuleFinalizationDisposition ReviewCurrentLifeModuleFinalization(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        CharacterCreationFoundationFinalizationPreviewRequest request,
        out CurrentLifeModuleFinalizationReview? review, CancellationToken cancellationToken = default)
    {
        review = null;
        cancellationToken.ThrowIfCancellationRequested();
        if (request?.Binding is null || !ValidLifeModuleOwner(expectedOwner)
            || owners is not IOwnerContextLeaseAccessor || !TryAcquireLifeModuleOwnerLease(owners, expectedOwner, out var lease))
            return CurrentLifeModuleFinalizationDisposition.Refused;
        using (lease)
        try
        {
            request = FreezeLifeModule(request);
            var id = request.Binding.WorkspaceId;
            if (!TrySecureExistingWorkspaceDirectory(expectedOwner.Owner, allowLegacyMigration: false)
                || TryGetPath(expectedOwner.Owner, id) is not { } path || !File.Exists(path))
                return CurrentLifeModuleFinalizationDisposition.Refused;
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            if (ReadPrivateRecord(path).RetainedRuleSources?.CurrentEnvironment is null)
                return CurrentLifeModuleFinalizationDisposition.Refused;
            var read = ReadGearWorkspaceUnderLease(expectedOwner.Owner, id, path, owners, lease, expectedOwner, out _, out var admitted);
            using var current = admitted;
            if (!read.Success || current is null || ReadContinuationSlotUnderLease(path) is not { } slot
                || current.Stored.LocalHistory is not { } history
                || current.Record.RetainedRuleSources!.LifeModuleOriginal is not null)
                return CurrentLifeModuleFinalizationDisposition.Refused;
            var preview = current.Execution.QuoteLifeModuleFinalization(current.Stored, current.Capture,
                _retainedCharacterQueries!, request, cancellationToken);
            if (preview is null || !SameLifeModule(preview.Binding, request.Binding)
                || preview.PreviewDigest != HashLifeModule(preview with { PreviewDigest = string.Empty }))
                return CurrentLifeModuleFinalizationDisposition.Refused;
            var data = new CurrentLifeModuleFinalizationReview(expectedOwner.Owner.NormalizedValue, expectedOwner.Owner.IsLocalSingleUser,
                expectedOwner.AuthorityInstanceId, expectedOwner.TransitionRevision, history.IncarnationId, slot,
                HashLifeModule(current.Record), HashLifeModule(current.Record.RetainedRuleSources),
                HashLifeModule(current.Record.RetainedRuleSources.CurrentEnvironment), current.Capture.IntegrityDigest,
                current.Execution.ExecutionProfile, current.Execution.ExecutionProfileFingerprint, request, preview, string.Empty);
            // Review is read-only; confirmation independently reacquires fresh authority.
            current.ValidateReadObservation();
            cancellationToken.ThrowIfCancellationRequested();
            review = data with { ReviewDigest = HashLifeModule(data) };
            return CurrentLifeModuleFinalizationDisposition.Reviewed;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (LifeModuleStorageFailure(error))
        { return CurrentLifeModuleFinalizationDisposition.Refused; }
    }

    public CurrentLifeModuleFinalizationResult ConfirmCurrentLifeModuleFinalization(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        CharacterCreationFoundationFinalizationConfirmRequest request,
        CurrentLifeModuleFinalizationReview? review, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CharacterCreationLifeModuleFinalizationTransaction.IsConfirmed(request)) return LifeModuleRefused();
        // The legacy signature has no incarnation/slot fence. Only a validated
        // unbound classification may enter its original ambient native path.
        if (review is null)
        {
            var disposition = PreviewCurrentLifeModuleFinalization(owners, expectedOwner,
                CharacterCreationLifeModuleFinalizationTransaction.ToPreviewRequest(request), out var result, cancellationToken);
            return new(disposition == CurrentLifeModulePreviewDisposition.ProvenUnbound && result is null
                ? CurrentLifeModuleFinalizationDisposition.ProvenUnbound : CurrentLifeModuleFinalizationDisposition.Refused);
        }
        return ExecuteCurrentLifeModuleFinalization(owners, expectedOwner, request, review, lookupOnly: false, cancellationToken);
    }

    public CurrentLifeModuleFinalizationResult LookupCurrentLifeModuleFinalization(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        CharacterCreationFoundationFinalizationConfirmRequest request,
        CurrentLifeModuleFinalizationReview review, CancellationToken cancellationToken = default)
        => ExecuteCurrentLifeModuleFinalization(owners, expectedOwner, request, review, lookupOnly: true, cancellationToken);

    private CurrentLifeModuleFinalizationResult ExecuteCurrentLifeModuleFinalization(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        CharacterCreationFoundationFinalizationConfirmRequest request,
        CurrentLifeModuleFinalizationReview review, bool lookupOnly, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidLifeModuleOwner(expectedOwner) || owners is not IOwnerContextLeaseAccessor
            || !TryAcquireLifeModuleOwnerLease(owners, expectedOwner, out var lease)) return LifeModuleRefused();
        bool publicationAttempted = false;
        using (lease)
        try
        {
            request = FreezeLifeModule(request);
            review = FreezeLifeModule(review);
            if (!ValidLifeModuleReview(review, request) || !MatchesLifeModuleReviewOwner(review, expectedOwner, generation: false))
                return LifeModuleRefused();
            var owner = expectedOwner.Owner;
            var id = request.Binding.WorkspaceId;
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false)
                || TryGetPath(owner, id) is not { } path || !File.Exists(path)) return LifeModuleRefused();
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            // Receipt/archive integrity is historical authority. It requires no
            // new source recovery or current business-rule admission.
            var originalResult = LookupLifeModuleOriginalUnderLease(path, owners, lease, expectedOwner, request, review);
            if (originalResult.Disposition != CurrentLifeModuleFinalizationDisposition.NotFound || lookupOnly) return originalResult;
            if (ReadPrivateRecord(path).RetainedRuleSources?.CurrentEnvironment is null) return LifeModuleRefused();
            var read = ReadGearWorkspaceUnderLease(owner, id, path, owners, lease, expectedOwner, out var delegatedLedger, out var admitted);
            using var current = admitted;
            if (!read.Success || current is null || current.Stored.LocalHistory?.IncarnationId != review.IncarnationId
                || ReadContinuationSlotUnderLease(path) != review.SlotGeneration) return LifeModuleRefused();
            var manifest = current.Record.RetainedRuleSources!;
            // Only a new commit reaches retained preparation. No original fence
            // or imported receipt can be converted into a new operation here.
            if (manifest.LifeModuleOriginal is not null
                || current.Stored.Document.AuxiliaryState.CharacterCreationFinalizationReceipts is { Count: > 0 }) return LifeModuleRefused();
            if (!MatchesLifeModuleReviewOwner(review, expectedOwner, generation: true) || !MatchesLifeModulePredecessor(current, review)) return LifeModuleRefused();
            var prepared = PrepareCurrentLifeModule(current, owner, request, cancellationToken);
            if (prepared is null) return LifeModuleRefused();
            var saved = current.Stored;
            var proposed = saved with { Document = prepared.Document, ContentRevision = prepared.Receipt.ContentRevision,
                SavedRevision = prepared.Receipt.SavedRevision };
            var after = current.Execution.ReadmitProposedCurrentEnvironment(saved, current.Capture, _retainedCharacterQueries!,
                proposed, cancellationToken);
            if (after.AfterCapture is not { } capture || after.AfterExecution is not { } execution || after.Blockers.Count != 0
                || !IsValidAuxiliaryState(id, proposed.ContentRevision, proposed.Document.AuxiliaryState, current.Recovery))
                return LifeModuleRefused();
            current.Recovery.RetainAdmittedCapture(new(capture, _retainedCharacterQueries!), capture.IntegrityDigest);
            var snapshot = new WorkspaceContinuationSnapshot(owner.NormalizedValue,
                new(id, proposed.Document, proposed.LastUpdatedUtc, proposed.ContentRevision, proposed.SavedRevision), []);
            var environment = WorkspacePrivateRuleSourceClosure.DescribeCurrentEnvironment(snapshot, capture,
                execution.ExecutionProfile, execution.ExecutionProfileFingerprint, []);
            var record = BuildPersistedRecord(proposed.Document, proposed.ContentRevision, proposed.SavedRevision,
                saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts) with
            {
                RecordSchemaVersion = RetainedWorkspaceRecordSchemaVersion,
                RetainedRuleSources = manifest with
                {
                    References = current.Recovery.SnapshotReferences(), CurrentEnvironment = environment,
                    LifeModuleOriginal = new(review.ReviewDigest, review.SlotGeneration, prepared.Receipt.ReceiptDigest)
                }
            };
            using var admission = new LifeModuleWriteAdmission(this, path, current, expectedOwner, review, request, prepared, proposed, after, record);
            cancellationToken.ThrowIfCancellationRequested();
            publicationAttempted = true;
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting);
            // Reopen the actual published Career record, with genuine retained
            // admission. Neither expected XML nor a successful rename is a result.
            var reopened = ReadGearWorkspaceUnderLease(owner, id, path, owners, lease, expectedOwner, out _, out var reopenedCurrent);
            using var committed = reopenedCurrent;
            if (!reopened.Success || committed is null || ReadContinuationSlotUnderLease(path) != review.SlotGeneration
                || committed.Record.RetainedRuleSources?.LifeModuleOriginal != record.RetainedRuleSources.LifeModuleOriginal)
                return LifeModuleUnknown(request, review);
            var result = CharacterCreationLifeModuleFinalizationTransaction.Lookup(committed.Stored, request);
            // The published record was freshly admitted by the reader above.
            // This receipt lookup cannot authorize another write.
            committed.ValidateReadObservation();
            return result?.Value is not null ? new(CurrentLifeModuleFinalizationDisposition.Applied, result)
                : LifeModuleUnknown(request, review);
        }
        catch (OperationCanceledException) when (!publicationAttempted) { throw; }
        catch (Exception error) when (LifeModuleStorageFailure(error) || error is OperationCanceledException)
        { return publicationAttempted ? LifeModuleUnknown(request, review) : LifeModuleRefused(); }
    }

    private CurrentLifeModuleFinalizationResult LookupLifeModuleOriginalUnderLease(string path,
        IOwnerContextAccessor owners, IOwnerContextLease lease, OwnerContextStamp expectedOwner,
        CharacterCreationFoundationFinalizationConfirmRequest request, CurrentLifeModuleFinalizationReview review)
    {
        // This narrow reader returns only the existing native receipt DTO. It
        // cannot provide a workspace/resolver or bypass admission for a write.
        RequireRetainedRuleSourceStorage();
        var record = ReadPrivateRecord(path);
        string before = HashLifeModule(record);
        var id = request.Binding.WorkspaceId;
        var owner = expectedOwner.Owner;
        if (record.RetainedRuleSources is not { CurrentEnvironment: not null, PrivateClosureBinding: null } manifest
            || manifest.OwnerId != owner.NormalizedValue || manifest.WorkspaceId != id.Value
            || record.LocalHistory is not { LocalAdoption: null } history
            || history.IncarnationId != review.IncarnationId || manifest.IncarnationId != history.IncarnationId
            || ReadContinuationSlotUnderLease(path) != review.SlotGeneration
            || !TryMaterializeRecord(record, out var document, out var revision, out var saved, out bool migration)
            || migration || !history.IsValid(revision) || record.DelegatedGmHistorySegmentStarts is null
            || !TryMaterializeDelegatedEditLedger(owner, id, revision, record.DelegatedGmCharacterEdits,
                record.DelegatedGmHistorySegmentStarts, out _)) return LifeModuleRefused();
        ValidateCurrentEnvironmentRecord(record, owner, id);
        var workspace = new WorkspaceStoredDocument(id, document, revision, saved, DateTimeOffset.UnixEpoch)
            { LocalHistory = history, DelegatedGmHistorySegmentStarts = record.DelegatedGmHistorySegmentStarts! };
        if (!WorkspaceAuxiliaryStateIntegrity.IsValidShape(id, revision, document.AuxiliaryState)) return LifeModuleRefused();
        var receipts = document.AuxiliaryState.CharacterCreationFinalizationReceipts;
        CurrentLifeModuleFinalizationResult result;
        if (manifest.LifeModuleOriginal is not { } original)
            result = receipts is { Count: > 0 } ? LifeModuleRefused() : new(CurrentLifeModuleFinalizationDisposition.NotFound);
        else
        {
            if (original.ReviewDigest != review.ReviewDigest || original.SlotGeneration != review.SlotGeneration
                || receipts is not { Count: 1 } || receipts[0].Receipt.ReceiptDigest != original.ReceiptDigest
                || receipts[0].Receipt.CommandDigest != CharacterCreationLifeModuleFinalizationTransaction.CommandDigest(request)
                || document.AuxiliaryState.CharacterCreationFinalizationArchive is not { } archive
                || !CharacterCreationLifeModuleFinalizationTransaction.IsValidArchive(id, archive, receipts[0].Receipt)) return LifeModuleRefused();
            var found = CharacterCreationLifeModuleFinalizationTransaction.Lookup(workspace, request);
            result = found?.Value is not null ? new(CurrentLifeModuleFinalizationDisposition.Original, found) : LifeModuleRefused();
        }
        if (owners is not IOwnerContextLeaseAccessor authority || authority.Capture() != expectedOwner || lease.Stamp != expectedOwner
            || !PathComparer.Equals(TryGetPath(owner, id), path) || ReadContinuationSlotUnderLease(path) != review.SlotGeneration
            || HashLifeModule(ReadPrivateRecord(path)) != before) return LifeModuleUnknown(request, review);
        return result;
    }

    private CharacterCurrentLifeModuleFinalizationPreparation? PrepareCurrentLifeModule(GearCurrentEnvironment current,
        OwnerScope owner, CharacterCreationFoundationFinalizationConfirmRequest request, CancellationToken cancellationToken)
    {
        var saved = current.Stored;
        var snapshot = new WorkspaceContinuationSnapshot(owner.NormalizedValue,
            new(saved.Id, saved.Document, saved.LastUpdatedUtc, saved.ContentRevision, saved.SavedRevision), []);
        if (!PrivateCurrentRuleEnvironmentAdmission.TryAdmit(snapshot, current.Capture,
                current.Record.RetainedRuleSources!.CurrentEnvironment!, _retainedCharacterQueries!, cancellationToken,
                out var admission, out var blockers) || admission is null || blockers.Count != 0) return null;
        return admission.PrepareLifeModuleFinalization(owner, request, cancellationToken);
    }

    private static bool MatchesLifeModulePredecessor(GearCurrentEnvironment current, CurrentLifeModuleFinalizationReview review)
        => current.Stored.LocalHistory?.IncarnationId == review.IncarnationId
            && HashLifeModule(current.Record) == review.PredecessorDigest
            && HashLifeModule(current.Record.RetainedRuleSources) == review.RetainedSourcesDigest
            && HashLifeModule(current.Record.RetainedRuleSources!.CurrentEnvironment) == review.CurrentEnvironmentDigest
            && current.Capture.IntegrityDigest == review.CaptureDigest
            && current.Execution.ExecutionProfile == review.ExecutionProfile
            && current.Execution.ExecutionProfileFingerprint == review.ExecutionProfileFingerprint;

    private static bool TryAcquireLifeModuleOwnerLease(IOwnerContextAccessor accessor, OwnerContextStamp expected,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IOwnerContextLease? lease)
    {
        lease = null;
        if (!expected.IsValid || accessor is not IOwnerContextLeaseAccessor authority)
            return false;
        if (authority.TryAcquire(expected, out lease) && lease is not null && lease.Stamp == expected)
            return true;
        lease?.Dispose();
        lease = null;
        return false;
    }

    private static bool ValidLifeModuleOwner(OwnerContextStamp owner)
        => owner.IsValid && !(owner.Owner.UsesLocalSingleUserValue && !owner.Owner.IsLocalSingleUser)
            && (owner.Owner.IsLocalSingleUser || !IsInvalidScopedOwner(owner.Owner));
    private static bool MatchesLifeModuleReviewOwner(CurrentLifeModuleFinalizationReview review, OwnerContextStamp owner, bool generation)
        => review.OwnerId == owner.Owner.NormalizedValue && review.TrustedLocalOwner == owner.Owner.IsLocalSingleUser
            && (!generation || review.OwnerAuthorityInstanceId == owner.AuthorityInstanceId
                && review.OwnerTransitionRevision == owner.TransitionRevision);

    private static bool ValidLifeModuleReview(CurrentLifeModuleFinalizationReview? review,
        CharacterCreationFoundationFinalizationConfirmRequest? request)
        => review is not null && CharacterCreationLifeModuleFinalizationTransaction.IsConfirmed(request)
            && !string.IsNullOrWhiteSpace(review.OwnerId) && review.OwnerId.Length <= 256
            && !string.IsNullOrWhiteSpace(review.OwnerAuthorityInstanceId) && review.OwnerAuthorityInstanceId.Length <= 256
            && review.OwnerTransitionRevision >= 0 && !string.IsNullOrWhiteSpace(review.IncarnationId)
            && Guid.TryParseExact(review.SlotGeneration, "N", out var slot) && slot != Guid.Empty
            && CurrentCharacterRuleSourceSemantics.IsNativeExecutionProfile(review.ExecutionProfile, review.ExecutionProfileFingerprint)
            && new[] { review.PredecessorDigest, review.RetainedSourcesDigest, review.CurrentEnvironmentDigest,
                    review.CaptureDigest, review.ReviewDigest }.All(CharacterCreationFinalizationDigest.IsCanonical)
            && review.Preview is { CanApply: true, FinalizationBlocked.Count: 0 }
            && review.Preview.PreviewDigest == HashLifeModule(review.Preview with { PreviewDigest = string.Empty })
            && review.Preview.PreviewDigest == request!.PreviewDigest
            && SameLifeModule(review.Request, CharacterCreationLifeModuleFinalizationTransaction.ToPreviewRequest(request))
            && SameLifeModule(review.Preview.Binding, request.Binding)
            && review.ReviewDigest == HashLifeModule(review with { ReviewDigest = string.Empty });
    private static T FreezeLifeModule<T>(T value) => JsonSerializer.SerializeToElement(value).Deserialize<T>()!;
    private static string HashLifeModule<T>(T value) => CharacterCreationFinalizationDigest.Compute(value);
    private static bool SameLifeModule<T>(T left, T right) => HashLifeModule(left) == HashLifeModule(right);
    private static CurrentLifeModuleFinalizationResult LifeModuleUnknown(
        CharacterCreationFoundationFinalizationConfirmRequest request, CurrentLifeModuleFinalizationReview review)
        => new(CurrentLifeModuleFinalizationDisposition.Unknown, OriginalIdentity:
            new(request.Binding.WorkspaceId, review.ReviewDigest, CharacterCreationLifeModuleFinalizationTransaction.CommandDigest(request)));
    private static CurrentLifeModuleFinalizationResult LifeModuleRefused() => new(CurrentLifeModuleFinalizationDisposition.Refused);
    private static bool LifeModuleStorageFailure(Exception error) => error is IOException or UnauthorizedAccessException
        or InvalidOperationException or ArgumentException or JsonException or OverflowException or NullReferenceException;

    // One private, bounded original-operation pairing; no new ledger or receipt schema.
    private sealed record LifeModuleOriginalFence(string ReviewDigest, string SlotGeneration, string ReceiptDigest);
    [ThreadStatic] private static LifeModuleWriteAdmission? _lifeModuleWriteAdmission;
    private sealed class LifeModuleWriteAdmission : IDisposable
    {
        private readonly LifeModuleWriteAdmission? _previous;
        internal FileWorkspaceStore Store { get; }
        internal string Path { get; }
        private readonly GearCurrentEnvironment _current;
        private readonly CurrentLifeModuleFinalizationReview _review;
        private readonly OwnerContextStamp _owner;
        private readonly CharacterCreationFoundationFinalizationConfirmRequest _request;
        private readonly CharacterCurrentLifeModuleFinalizationPreparation _prepared;
        private readonly WorkspaceStoredDocument _after;
        private readonly CharacterCurrentRuleEnvironmentReadmissionResult _admission;
        private readonly string _recordDigest;
        internal LifeModuleWriteAdmission(FileWorkspaceStore store, string path, GearCurrentEnvironment current,
            OwnerContextStamp owner, CurrentLifeModuleFinalizationReview review, CharacterCreationFoundationFinalizationConfirmRequest request,
            CharacterCurrentLifeModuleFinalizationPreparation prepared, WorkspaceStoredDocument after,
            CharacterCurrentRuleEnvironmentReadmissionResult admission, PersistedWorkspaceRecord record)
        {
            _previous = _lifeModuleWriteAdmission; Store = store; Path = System.IO.Path.GetFullPath(path); _current = current;
            _owner = owner; _review = review; _request = request; _prepared = prepared; _after = after; _admission = admission;
            _recordDigest = HashLifeModule(record); _lifeModuleWriteAdmission = this;
        }
        internal void ValidateFinalFence(PersistedWorkspaceRecord record)
        {
            _current.ValidateFinalFence();
            if (!ValidLifeModuleReview(_review, _request) || !MatchesLifeModuleReviewOwner(_review, _owner, generation: true)
                || !MatchesLifeModulePredecessor(_current, _review)
                || ReadContinuationSlotUnderLease(Path) != _review.SlotGeneration
                || HashLifeModule(record) != _recordDigest
                || Store.PrepareCurrentLifeModule(_current, _owner.Owner, _request, CancellationToken.None) is not { } repeated
                || !SameLifeModule(repeated.Receipt, _prepared.Receipt) || !SameLifeModule(repeated.Document, _prepared.Document))
                throw new IOException("life_module_finalization_predecessor_changed");
            var actual = _current.Execution.ReadmitProposedCurrentEnvironment(_current.Stored, _current.Capture,
                Store._retainedCharacterQueries!, _after);
            if (actual.AfterCapture is null || actual.AfterExecution is null || actual.Blockers.Count != 0
                || actual.AfterCapture.IntegrityDigest != _admission.AfterCapture!.IntegrityDigest
                || actual.AfterExecution.ExecutionProfile != _admission.AfterExecution!.ExecutionProfile
                || actual.AfterExecution.ExecutionProfileFingerprint != _admission.AfterExecution.ExecutionProfileFingerprint)
                throw new IOException("life_module_finalization_after_changed");
        }
        public void Dispose() => _lifeModuleWriteAdmission = _previous;
    }
    private LifeModuleWriteAdmission? LifeModuleAdmissionFor(string path)
        => _lifeModuleWriteAdmission is { } admission && ReferenceEquals(admission.Store, this)
            && PathComparer.Equals(admission.Path, Path.GetFullPath(path)) ? admission : null;

}
