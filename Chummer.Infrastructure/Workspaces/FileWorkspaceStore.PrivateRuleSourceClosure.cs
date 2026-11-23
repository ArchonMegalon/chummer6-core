using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCreationCurrentLifeModuleFinalizationPreviewCapability,
    IWorkspaceCurrentDisplayStoreCapability, IWorkspaceCurrentDisplayRosterStoreCapability, IWorkspaceCurrentConditionMonitorStoreCapability
{
    private static readonly System.Diagnostics.ActivitySource StartupActivities8910 =
        new("Chummer.Startup.Shell8910");

    public CommandResult<WorkspaceDocumentSnapshot> GetCurrentDisplayWorkspace(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterWorkspaceId id, CancellationToken cancellationToken = default)
        => ReadCurrentDisplay(owners, expectedOwner, ownerLease, id, null, false, out _, out _, out _, cancellationToken);

    public CommandResult<WorkspaceOverviewProjection> GetCurrentDisplayOverview(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterWorkspaceId id, IRulesetWorkspaceCodecResolver unboundCodecs,
        CancellationToken cancellationToken = default)
    {
        var read = ReadCurrentDisplay(owners, expectedOwner, ownerLease, id, unboundCodecs, true,
            out var overview, out _, out _, cancellationToken);
        return read.Success && overview is not null
            ? new(true, overview, null, WorkspaceOperationOutcome.Success)
            : new(false, null, read.Error, read.Outcome);
    }

    public CommandResult<IReadOnlyList<WorkspaceListItem>> ListCurrentDisplayWorkspaces(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        IRulesetWorkspaceCodecResolver unboundCodecs, int? maxCount = null,
        CancellationToken cancellationToken = default)
        => ListCurrentDisplayWorkspaces8159(owners, expectedOwner, ownerLease,
            unboundCodecs, maxCount, cancellationToken, null);

    private CommandResult<IReadOnlyList<WorkspaceListItem>> ListCurrentDisplayWorkspaces8159(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        IRulesetWorkspaceCodecResolver unboundCodecs, int? maxCount,
        CancellationToken cancellationToken, StartupDisplayObservation8159? startup)
    {
        using var rosterTiming = StartupActivities8910.StartActivity("roster-inventory");
        const string refused = "The local runner roster is unavailable for the current owner and rule environment.";
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!IsCurrentDisplayOwner(owners, expectedOwner, ownerLease))
                return new(false, null, refused, WorkspaceOperationOutcome.Unavailable);
            OwnerScope owner = expectedOwner.Owner.IsLocalSingleUser
                ? expectedOwner.Owner : new(expectedOwner.Owner.NormalizedValue);
            string directory = GetWorkspaceDirectory(owner);
            List<WorkspaceListItem> rows = [];
            if (TrySecureExistingWorkspaceDirectory(owner, recoverStaleTempFiles: false))
            {
                foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var id = new CharacterWorkspaceId(Path.GetFileNameWithoutExtension(path));
                    if (TryGetPath(owner, id) is null) continue;
                    // Enumeration supplies only an id. The closed reader classifies,
                    // admits and projects its bytes under the existing owner lease
                    // and one workspace lease. Never retry a refused row via Get.
                    WorkspaceListItem? row;
                    var read = startup is null
                        ? ReadCurrentDisplay(owners, expectedOwner, ownerLease, id, unboundCodecs,
                            false, out _, out row, out _, cancellationToken, includeRoster: true)
                        : startup.ReadRosterRow(id, ownerLease, cancellationToken, out row);
                    if (read.Success && row is not null) rows.Add(row);
                }
            }
            else if (!IsConfirmedMissingInventoryDirectory(directory))
                return new(false, null, refused, WorkspaceOperationOutcome.Unavailable);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentDisplayOwner(owners, expectedOwner, ownerLease))
                return new(false, null, refused, WorkspaceOperationOutcome.Unavailable);
            // Match List's newest-first order and positive limit, retaining only
            // display rows. This is neither a complete inventory nor edit authority.
            return new(true, rows.OrderByDescending(row => row.LastUpdatedUtc)
                .Take(maxCount is > 0 ? maxCount.Value : rows.Count).ToArray(), null,
                WorkspaceOperationOutcome.Success);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(false, null, refused, WorkspaceOperationOutcome.Unavailable);
        }
    }

    // This is deliberately a closed query, not a delegate/execution adapter. InProcess
    // forwards its existing live lease; acquiring another owner lease here can deadlock.
    private CommandResult<WorkspaceDocumentSnapshot> ReadCurrentDisplay(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterWorkspaceId id, IRulesetWorkspaceCodecResolver? unboundCodecs, bool includeOverview,
        out WorkspaceOverviewProjection? overview, out WorkspaceListItem? rosterItem,
        out CharacterConditionMonitorSection? conditionMonitor,
        CancellationToken cancellationToken, bool includeRoster = false, CurrentDisplayObservation6178? observation = null,
        bool includeConditionMonitor = false)
    {
        // Fixed purposes only: neither the workspace identity nor any record data
        // enters the diagnostic. Roster selection is scoped by the startup caller.
        using var displayTiming = StartupActivities8910.StartActivity(includeRoster ? "display-read-roster"
            : includeOverview ? "display-read-overview"
            : includeConditionMonitor ? "display-read-condition" : "display-read-verification");
        overview = null;
        rosterItem = null;
        conditionMonitor = null;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!IsCurrentDisplayOwner(owners, expectedOwner, ownerLease))
                return CurrentDisplayRefused();
            OwnerScope owner = expectedOwner.Owner.IsLocalSingleUser
                ? expectedOwner.Owner : new(expectedOwner.Owner.NormalizedValue);
            if (!ValidRestoreOwner(owner) || TryGetPath(owner, id) is not { } path)
                return CurrentDisplayRefused();
            // Preserve Get's existing guarded owner-directory migration ordering.
            // The actual owner lease remains held; record handling below is classified
            // under its workspace lease before any ordinary record migration/cleanup.
            if (!TrySecureExistingWorkspaceDirectory(owner, recoverStaleTempFiles: false))
                return CurrentDisplayRefused(IsConfirmedMissingInventoryDirectory(GetWorkspaceDirectory(owner))
                    ? WorkspaceOperationOutcome.Missing : WorkspaceOperationOutcome.Unavailable);
            using var leaseTiming = StartupActivities8910.StartActivity("workspace-lease");
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            leaseTiming?.Stop();
            if (!File.Exists(path))
                return CurrentDisplayRefused(WorkspaceOperationOutcome.Missing);
            // All display reads revisit the same record under this owner/workspace
            // lease. Reuse its decode only after ordinary, non-migrating admission;
            // every live read and complete-record mutation check still runs.
            OrdinaryRosterRecordRead? ordinaryRosterRead = new(this);
            using var classifyTiming = StartupActivities8910.StartActivity("record-classify");
            var record = ReadPrivateRecord(path, ordinaryRosterRead);
            string before = DisplayRecordDigestAfterRead(record, ordinaryRosterRead);
            string? slot = ReadContinuationSlotUnderLease(path);
            if (!TryMaterializeDisplayRecord(record, ordinaryRosterRead, out var capturedDocument, out var capturedRevision,
                out var capturedSavedRevision, out bool migration))
                return CurrentDisplayRefused();
            classifyTiming?.Stop();
            using var history = WorkspacePrivateRuleSourceClosureCompletion.BeginHistoryObservation();
            bool ordinaryUnboundRead = record.RetainedRuleSources is null;
            if (ordinaryUnboundRead && !migration)
                ordinaryRosterRead?.AllowReuse(record, capturedDocument, capturedRevision, capturedSavedRevision, before);
            else
                ordinaryRosterRead = null; // Retained and migrating records keep their original readers.
            if (ordinaryUnboundRead)
            {
                // Keep ordinary Get's guarded read/migration and interruption recovery, but
                // only after positive unbound classification under this workspace operation.
                // Get itself would reacquire the nonreentrant operation. Retained records
                // must never reach this cleanup or the ordinary reader's migration path.
                cancellationToken.ThrowIfCancellationRequested();
                RemoveStaleTempFiles(path);
                using var ordinaryTiming = StartupActivities8910.StartActivity("ordinary-open");
                var ordinary = ReadWorkspaceUnderLease(owner, id, path, out _, reuseReadValidation: true,
                    ordinaryRosterRead: ordinaryRosterRead);
                ordinaryTiming?.Stop();
                if (!ordinary.Success || ordinary.Value is not { } loaded)
                    return CurrentDisplayRefused(ordinary.Outcome);

                // A reused document is the identical graph just checked against the
                // complete canonical record by this operation's reader. Serializing
                // that same object twice adds no comparison. Distinct documents and
                // all uncached/migration/retained paths keep the full comparison;
                // every live byte, owner, slot and final mutation fence stays below.
                // Migration uses the existing atomic writer. Observe what it actually
                // published (including an acknowledged post-replace interruption), not
                // the old digest or an assumed handcrafted migration result.
                record = ReadPrivateRecord(path, ordinaryRosterRead);
                if (record.RetainedRuleSources is not null
                    || !TryMaterializeDisplayRecord(record, ordinaryRosterRead, out capturedDocument, out capturedRevision,
                        out capturedSavedRevision, out migration) || migration
                    || loaded.ContentRevision != capturedRevision || loaded.SavedRevision != capturedSavedRevision
                    || ((ordinaryRosterRead is null || !ReferenceEquals(loaded.Document, capturedDocument))
                        && CharacterCreationFinalizationDigest.Compute(loaded.Document, WorkspaceRecordJsonContext.Default.WorkspaceDocument)
                            != CharacterCreationFinalizationDigest.Compute(capturedDocument, WorkspaceRecordJsonContext.Default.WorkspaceDocument)))
                    return CurrentDisplayRefused();
                before = DisplayRecordDigestAfterRead(record, ordinaryRosterRead);
            }
            else if (migration)
                return CurrentDisplayRefused();

            DateTimeOffset displayTimestamp = new(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            var capturedSnapshot = new WorkspaceDocumentSnapshot(id, capturedDocument, displayTimestamp,
                capturedRevision, capturedSavedRevision);
            using var singleRead = observation is null
                && record.RetainedRuleSources?.CurrentEnvironment is { } currentEnvironment
                && _retainedCharacterQueries is not null
                ? new PrivateCurrentRuleEnvironmentAdmission.CurrentReadEvaluation(owner.NormalizedValue,
                    capturedSnapshot, currentEnvironment, _retainedCharacterQueries, before)
                : null;
            // The optional chooser observation owns only deterministic display evaluation.
            // Every read still opens/revalidates sources and performs the final live fences.
            var currentRead = observation is null ? singleRead : observation.BindRecord(
                before, slot, capturedSnapshot, record.RetainedRuleSources?.CurrentEnvironment,
                _retainedCharacterQueries);
            // Revalidate the published unbound bytes using the same ordinary validator.
            // Bound reads retain exact continuation parsing and retained admission below.
            using var observedTiming = StartupActivities8910.StartActivity("observed-open");
            var observed = ReadWorkspaceUnderLease(owner, id, path, out var ledger,
                continuationRead: !ordinaryUnboundRead, reuseReadValidation: ordinaryUnboundRead,
                privateClosureRead: !ordinaryUnboundRead, reuseCurrentCaptureForRead: !ordinaryUnboundRead, currentRead: currentRead,
                ordinaryRosterRead: ordinaryRosterRead);
            observedTiming?.Stop();
            if (!observed.Success || observed.Value is not { LocalHistory: not null } stored)
                return CurrentDisplayRefused(observed.Outcome);
            if (stored.LastUpdatedUtc != displayTimestamp
                || stored.ContentRevision != capturedRevision || stored.SavedRevision != capturedSavedRevision
                || ((ordinaryRosterRead is null || !ReferenceEquals(stored.Document, capturedDocument))
                    && CharacterCreationFinalizationDigest.Compute(stored.Document, WorkspaceRecordJsonContext.Default.WorkspaceDocument)
                        != CharacterCreationFinalizationDigest.Compute(capturedDocument, WorkspaceRecordJsonContext.Default.WorkspaceDocument)))
                return CurrentDisplayRefused();
            using var recovery = OpenRecordRuleSources(owner, id, record, reuseCurrentCaptureForRead: true, currentRead: currentRead);
            var environment = record.RetainedRuleSources?.CurrentEnvironment;
            if (record.RetainedRuleSources?.PrivateClosureBinding is not null
                || environment is null && record.RetainedRuleSources?.ActivatedClosureBinding is not null)
                return CurrentDisplayRefused();

            var snapshot = new WorkspaceDocumentSnapshot(id, stored.Document, stored.LastUpdatedUtc,
                stored.ContentRevision, stored.SavedRevision);
            using var projectionTiming = StartupActivities8910.StartActivity("display-projection");
            WorkspaceOverviewProjection? projected = null;
            CharacterConditionMonitorSection? projectedMonitor = null;
            CharacterFileSummary? summary = null;
            PrivateCurrentRuleEnvironmentAdmission? admission = null;
            if (environment is not null)
            {
                RequireRetainedRuleSourceStorage();
                if (slot is null || record.RetainedRuleSources?.LifeModuleOriginal is { } original
                    && original.SlotGeneration != slot) return CurrentDisplayRefused();
                ValidateCurrentEnvironmentRecord(record, owner, id);
                // OpenRecordRuleSources already admits the exact current environment,
                // including on document-only reads. Only projections need a separate
                // execution observation spanning projection work. The final live-source
                // admission and owner/slot/timestamp/record fences below apply to both.
                if (includeOverview || includeRoster || includeConditionMonitor)
                {
                    if (_retainedCharacterQueries is null || recovery is null
                        || !recovery.TryRecoverRuleSourceCapture(environment.CaptureDigest, out var capture)
                        || capture is null)
                        return CurrentDisplayRefused();
                    var continuation = new WorkspaceContinuationSnapshot(owner.NormalizedValue, snapshot,
                        ledger.Select(entry => entry.Receipt).ToArray())
                    {
                        DelegatedGmHistorySegmentStarts = stored.DelegatedGmHistorySegmentStarts.ToArray()
                    };
                    if (!PrivateCurrentRuleEnvironmentAdmission.TryAdmit(continuation, capture, environment,
                        _retainedCharacterQueries, cancellationToken, out admission, out _, currentRead, recovery: recovery) || admission is null)
                        return CurrentDisplayRefused();
                    // The admitted SR5 capture and exact document are the only rules input.
                    // Match the canonical SR5 codec envelope, parser and validation semantics.
                    var envelope = stored.Document.PayloadEnvelope;
                    if (stored.Document.Format != WorkspaceDocumentFormat.NativeXml
                        || envelope.RulesetId != "sr5" || envelope.SchemaVersion != 1
                        || envelope.PayloadKind != "sr5/chum5-xml")
                        return CurrentDisplayRefused(WorkspaceOperationOutcome.Corrupt);
                    string xml = envelope.Payload.Length > 0 && envelope.Payload[0] == '\uFEFF'
                        ? envelope.Payload[1..] : envelope.Payload;
                    var document = new CharacterDocument(xml);
                    // After successful recovery/admission, before the final retained-byte reread.
                    _faultInjector.OnStage(FileWorkspaceStoreFaultStage.BeforeBoundDisplayProjection, path, string.Empty);
                    summary = _retainedCharacterQueries.ParseSummary(document);
                    var validation = _retainedCharacterQueries.Validate(document);
                    if (!validation.IsValid) return CurrentDisplayRefused(WorkspaceOperationOutcome.Corrupt);
                    // Match SR5's NativeXml/BOM materialization while preserving the original
                    // captured bytes in the snapshot. No ambient codec supplies rules here.
                    _ = System.Text.Encoding.UTF8.GetBytes(xml);
                    if (includeOverview)
                    {
                        var sections = new CharacterSectionService(capture.CreateReplayResolver());
                        projected = new(snapshot, sections.ParseOverview(xml), validation);
                    }
                    if (includeConditionMonitor)
                    {
                        var resolution = capture.CreateCurrentEnvironmentResolution(
                            environment.ExecutionProfile!, environment.ExecutionProfileFingerprint!);
                        if (resolution is null) return CurrentDisplayRefused();
                        projectedMonitor = CharacterCurrentConditionMonitorQuery.Project(
                            stored, resolution.Resolver, recovery);
                    }
                }
            }
            else if (includeOverview)
            {
                // Classification, codec evaluation and final digest check all remain inside
                // this same owner/workspace observation. No classify-release-ambient gap.
                var envelope = stored.Document.PayloadEnvelope;
                var codec = unboundCodecs?.Resolve(envelope.RulesetId);
                if (codec is null || stored.Document.State is null || !Enum.IsDefined(stored.Document.Format)
                    || envelope.RulesetId != codec.RulesetId || envelope.SchemaVersion != codec.SchemaVersion
                    || envelope.PayloadKind != codec.PayloadKind)
                    return CurrentDisplayRefused(WorkspaceOperationOutcome.Corrupt);
                _ = codec.ParseSummary(envelope);
                var validation = codec.Validate(envelope);
                if (!validation.IsValid) return CurrentDisplayRefused(WorkspaceOperationOutcome.Corrupt);
                _ = codec.BuildDownload(id, envelope, stored.Document.Format);
                projected = new(snapshot, codec.ParseOverview(envelope), validation);
            }
            else if (includeConditionMonitor)
            {
                // Preserve ordinary codec semantics only after this operation
                // has proved that no retained/current environment is present.
                var codec = unboundCodecs?.Resolve(stored.Document.RulesetId);
                projectedMonitor = codec?.ParseSection("conditionmonitor", stored)
                    as CharacterConditionMonitorSection;
                if (projectedMonitor is null) return CurrentDisplayRefused();
            }
            else if (includeRoster)
            {
                // Preserve the ordinary roster's summary/fallback behavior, but
                // only after excluding current/private environments under this lease.
                try
                {
                    summary = unboundCodecs!.Resolve(stored.Document.RulesetId)
                        .ParseSummary(stored.Document.PayloadEnvelope);
                }
                catch
                {
                    summary = new CharacterFileSummary($"Workspace {id.Value}", string.Empty,
                        string.Empty, string.Empty, string.Empty, string.Empty, 0m, 0m, false);
                }
            }

            projectionTiming?.Stop();
            using var fencesTiming = StartupActivities8910.StartActivity("display-fences");
            cancellationToken.ThrowIfCancellationRequested();
            admission?.Revalidate(cancellationToken, currentRead);
            if (recovery is not null && !recovery.ValidateRetainedRuleSources())
                return CurrentDisplayRefused();
            if (environment is not null)
                AdmitRetainedCurrentEnvironment(record, owner, id, recovery!, environment, currentRead);
            if (!IsCurrentDisplayOwner(owners, expectedOwner, ownerLease)
                || File.GetLastWriteTimeUtc(path) != displayTimestamp.UtcDateTime
                || ReadContinuationSlotUnderLease(path) != slot
                || DisplayRecordDigestAfterRead(ReadPrivateRecord(path, ordinaryRosterRead), ordinaryRosterRead) != before)
                return CurrentDisplayRefused();
            cancellationToken.ThrowIfCancellationRequested();
            overview = projected;
            conditionMonitor = projectedMonitor;
            if (includeRoster && summary is not null)
                rosterItem = new(id, summary, stored.LastUpdatedUtc, stored.Document.RulesetId,
                    stored.SavedRevision > 0, stored.ContentRevision, stored.SavedRevision);
            return new(true, snapshot, null, WorkspaceOperationOutcome.Success);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // Never turn a failed binding/admission/projection into ambient execution.
            overview = null;
            return CurrentDisplayRefused();
        }
    }

    private static bool IsCurrentDisplayOwner(IOwnerContextAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease ownerLease)
        => owners is IOwnerContextLeaseAccessor authority && expected.IsValid
            && !(expected.Owner.UsesLocalSingleUserValue && !expected.Owner.IsLocalSingleUser)
            && ownerLease is not null && ownerLease.Stamp == expected
            && authority.Capture() == expected;

    private static CommandResult<WorkspaceDocumentSnapshot> CurrentDisplayRefused(
        WorkspaceOperationOutcome outcome = WorkspaceOperationOutcome.Unavailable)
        => new(false, null, "Workspace display is unavailable for the current owner and rule environment.", outcome);

    /// <summary>
    /// Capture an owner-private, path-free source closure under the actual owner
    /// and workspace leases. This does not authorize its upload; the authenticated
    /// host still enforces source-specific copy admission and selection CAS.
    /// V1 source-bearing history fails explicitly; local V1 recovery is unchanged.
    /// </summary>
    public WorkspacePrivateRuleSourceClosure ExportPrivateRuleSourceClosure(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, CharacterWorkspaceId id,
        int maximumBytes, CancellationToken cancellationToken = default)
        => ExportPrivateRuleSourceClosureCore(owners, expectedOwner, id, maximumBytes, null, cancellationToken);

    /// <summary>
    /// Captures the current character's selected profile and original dependencies in
    /// addition to historical receipt archives. This observation does not assert
    /// supported mechanics or authorize receiver activation.
    /// </summary>
    public WorkspacePrivateRuleSourceClosure ExportCurrentRuleEnvironmentClosure(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, CharacterWorkspaceId id, int maximumBytes,
        ICharacterSourceDataResolver currentSources, CancellationToken cancellationToken = default)
        => ExportPrivateRuleSourceClosureCore(owners, expectedOwner, id, maximumBytes,
            currentSources ?? throw new ArgumentNullException(nameof(currentSources)), cancellationToken);

    /// <summary>
    /// Selects interpretation only for a NEW current capture. An already retained
    /// environment and every history archive keep their original bytes and profile.
    /// Used by the native private-rules transfer entrypoint; does not adopt anything.
    /// </summary>
    public WorkspacePrivateRuleSourceClosure ExportCurrentRuleEnvironmentClosure(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, CharacterWorkspaceId id,
        int maximumBytes, ICharacterSourceDataResolver currentSources, string newCaptureCompatibilityProfile,
        CancellationToken cancellationToken = default)
        => ExportPrivateRuleSourceClosureCore(owners, expectedOwner, id, maximumBytes,
            currentSources ?? throw new ArgumentNullException(nameof(currentSources)), cancellationToken,
            newCaptureCompatibilityProfile, selectNewCaptureProfile: true);

    /// <summary>
    /// Explicit detached authoring against an exact, saved, retained baseline. The
    /// selected character/settings/profile/order remain those of that baseline.
    /// Staged sources are admitted in full; no workspace is changed by this call.
    /// Use RestoreService.ReviewAuthoredUpdate for review/confirmation with a live
    /// staged-source fence. Ordinary exports never select this authoring mode.
    /// </summary>
    public WorkspacePrivateRuleSourceClosure AuthorCurrentRuleEnvironmentUpdate(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        WorkspacePrivateRuleSourceClosureBinding baseline, ICharacterSourceDataResolver stagedSources,
        int maximumBytes = WorkspacePrivateRuleSourceClosureBinding.MaximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(stagedSources);
        return ExportPrivateRuleSourceClosureCore(owners, expectedOwner, baseline.WorkspaceId,
            maximumBytes, stagedSources, cancellationToken, authoredBaseline: baseline);
    }

    private WorkspacePrivateRuleSourceClosure ExportPrivateRuleSourceClosureCore(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, CharacterWorkspaceId id, int maximumBytes,
        ICharacterSourceDataResolver? currentSources, CancellationToken cancellationToken,
        string newCaptureCompatibilityProfile = Chummer5aLegacyImportCompatibility.ProfileV1,
        bool selectNewCaptureProfile = false, WorkspacePrivateRuleSourceClosureBinding? authoredBaseline = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (maximumBytes is <= 0 or > WorkspacePrivateRuleSourceClosureBinding.MaximumBytes
                || !Chummer5aLegacyImportCompatibility.IsSupportedProfile(newCaptureCompatibilityProfile)
                || !expectedOwner.IsValid || owners is not IOwnerContextLeaseAccessor authority)
                throw WorkspacePrivateRuleSourceClosure.Rejected();
            bool admitted = authority.TryAcquire(expectedOwner, out var lease);
            using (lease)
            {
                if (!admitted || lease is null || lease.Stamp != expectedOwner)
                    throw WorkspacePrivateRuleSourceClosure.Rejected();
                OwnerScope owner = expectedOwner.Owner.IsLocalSingleUser
                    ? expectedOwner.Owner : new OwnerScope(expectedOwner.Owner.NormalizedValue);
                // Owner authority remains leased before taking the Play journal and then the workspace.
                return WithSessionPlayExport5712(owner, id, playReader5712 =>
                {
                RequireRetainedRuleSourceStorage();
                if ((!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner))
                    || _retainedCharacterQueries is null || TryGetPath(owner, id) is not { } path
                    || !TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                    throw WorkspacePrivateRuleSourceClosure.Rejected();
                using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
                using var history = WorkspacePrivateRuleSourceClosureCompletion.BeginHistoryObservation();
                var before = ReadPrivateRecord(path);
                RequireSettledSessionPlay5712(playReader5712, before.AuxiliaryState);
                string beforeDigest = CharacterCreationFinalizationDigest.Compute(before);
                // Reuse deterministic admission only within this export's owner/workspace
                // leases. Each boundary still reopens and validates retained source bytes.
                // Preserve the mechanics-only timestamp used by retained admission; the
                // exported snapshot below still carries the actual stored timestamp.
                using var currentRead = before.RetainedRuleSources?.CurrentEnvironment is { } retainedEnvironment
                    && _retainedCharacterQueries is XmlCharacterFileQueries { HasImmutableEvaluationQueries2764: true }
                    && TryMaterializeRecord(before, out var retainedDocument, out var retainedRevision,
                        out var retainedSavedRevision, out bool requiresMigration) && !requiresMigration
                    ? new PrivateCurrentRuleEnvironmentAdmission.CurrentReadEvaluation(owner.NormalizedValue,
                        new(id, retainedDocument, DateTimeOffset.UnixEpoch, retainedRevision, retainedSavedRevision),
                        retainedEnvironment, _retainedCharacterQueries, beforeDigest)
                    : null;
                var read = ReadWorkspaceUnderLease(owner, id, path, out var ledger,
                    continuationRead: before.RetainedRuleSources?.PrivateClosureBinding is not null, privateClosureRead: true,
                    reuseCurrentCaptureForRead: true, currentRead: currentRead);
                if (!read.Success || read.Value is not { } stored) throw WorkspacePrivateRuleSourceClosure.Rejected();
                var record = ReadPrivateRecord(path);
                if (CharacterCreationFinalizationDigest.Compute(record) != beforeDigest) throw WorkspacePrivateRuleSourceClosure.Rejected();
                // This recovery owns its own capture; validation, export and the final fence
                // reread bytes even when they reuse the detached archive reconstruction.
                using var recovery = OpenRecordRuleSources(owner, id, record,
                    reuseCurrentCaptureForRead: true, currentRead: currentRead);
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
                WorkspaceCurrentRuleEnvironmentBinding? environment = record.RetainedRuleSources?.CurrentEnvironment;
                CharacterCurrentRuleEnvironmentExecution? capturedExecution = null;
                bool authoring = authoredBaseline is not null;
                var originalEnvironment = environment;
                if (authoring)
                {
                    if (environment is null || currentSources is null || stored.SavedRevision <= 0
                        || stored.ContentRevision != stored.SavedRevision
                        || authoredBaseline!.Schema != WorkspacePrivateRuleSourceClosureBinding.SchemaV2
                        || record.RetainedRuleSources?.PrivateClosureBinding is not null)
                        throw WorkspacePrivateRuleSourceClosure.Rejected();
                    // Trusted factory, including every receipt, old capture and authenticated
                    // history byte. A revision number alone is not a baseline identity.
                    var original = WorkspacePrivateRuleSourceClosure.Create(snapshot, archives, campaigns,
                        maximumBytes, cancellationToken, environment, record.AuthenticatedHistory);
                    if (original.Binding != authoredBaseline) throw WorkspacePrivateRuleSourceClosure.Rejected();
                    newCaptureCompatibilityProfile = environment.CompatibilityProfile;
                    selectNewCaptureProfile = true;
                }
                bool capturedLive = currentSources is not null && (environment is null || authoring);
                if (capturedLive)
                {
                    // Historical custody cannot become current by relabelling receipts.
                    if (record.RetainedRuleSources?.PrivateClosureBinding is not null)
                        throw WorkspacePrivateRuleSourceClosure.Rejected();
                    var capture = CaptureCurrent();
                    if (authoring)
                    {
                        var previousBytes = archives.Single(bytes => RetainedCharacterRuleSourceInputs.Digest(bytes)
                            == originalEnvironment!.CaptureDigest);
                        var previousInputs = FileSystemCharacterSourceDataResolver.ImportCaptureInputs(previousBytes,
                            originalEnvironment!.CaptureDigest);
                        var stagedInputs = FileSystemCharacterSourceDataResolver.ImportCaptureInputs(capture.ExportPrivateInputs(),
                            capture.IntegrityDigest);
                        // Locations and manifest versions may change in a staged
                        // installation. Selected pack identities and their order
                        // may not; those belong to the leased saved runner.
                        if (!previousInputs.SelectionInputs.Select(row => (row.Name, row.ManifestId))
                            .SequenceEqual(stagedInputs.SelectionInputs.Select(row => (row.Name, row.ManifestId))))
                            throw WorkspacePrivateRuleSourceClosure.Rejected();
                    }
                    var canonical = CharacterCurrentRuleEnvironmentAdmission.AdmitCurrentEnvironment(stored, capture,
                        _retainedCharacterQueries!, cancellationToken);
                    if (authoring && canonical.Execution is null)
                        throw new WorkspaceCurrentRuleEnvironmentUnavailableException(canonical.Blockers);
                    capturedExecution = canonical.Execution;
                    environment = WorkspacePrivateRuleSourceClosure.DescribeCurrentEnvironment(snapshot, capture,
                        canonical.Execution?.ExecutionProfile, canonical.Execution?.ExecutionProfileFingerprint, canonical.Blockers);
                    if (authoring && (environment.ChosenRuleProfileId != originalEnvironment!.ChosenRuleProfileId
                        || environment.CompatibilityProfile != originalEnvironment.CompatibilityProfile
                        || environment.RetainedSettingsFile?.Namespace != originalEnvironment.RetainedSettingsFile?.Namespace
                        || environment.RetainedSettingsFile?.FileName != originalEnvironment.RetainedSettingsFile?.FileName))
                        throw WorkspacePrivateRuleSourceClosure.Rejected();
                    if (canonical.Execution is { } execution && (execution.CaptureDigest != capture.IntegrityDigest
                        || execution.RequiredPackageSetDigest != capture.RequiredPackageSetDigest
                        || execution.CompatibilityProfile != capture.CompatibilityProfile)) throw WorkspacePrivateRuleSourceClosure.Rejected();
                    if (!archives.Any(bytes => RetainedCharacterRuleSourceInputs.Digest(bytes) == capture.IntegrityDigest))
                    {
                        byte[] bytes = capture.ExportPrivateInputs();
                        if (checked(total + bytes.LongLength) > maximumBytes) throw WorkspacePrivateRuleSourceClosure.Rejected();
                        archives.Add(bytes);
                    }
                }
                if (record.AuthenticatedHistory is not null && !VerifyAuthenticatedHistory(owner, id, stored.Document,
                    stored.ContentRevision, stored.SavedRevision, record.AuthenticatedHistory, digest => recovery is not null
                        && recovery.TryRecoverRuleSourceCapture(digest, out var historicalCapture) ? historicalCapture : null))
                    throw WorkspacePrivateRuleSourceClosure.Rejected();
                var result = WorkspacePrivateRuleSourceClosure.Create(snapshot, archives, campaigns, maximumBytes,
                    cancellationToken, environment, record.AuthenticatedHistory);
                if (record.RetainedRuleSources?.PrivateClosureBinding is { } retainedBinding
                    && result.Binding != retainedBinding) throw WorkspacePrivateRuleSourceClosure.Rejected();
                cancellationToken.ThrowIfCancellationRequested();
                if (lease.Stamp != expectedOwner || CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)) != beforeDigest
                    || recovery is not null && !recovery.ValidateRetainedRuleSources()) throw WorkspacePrivateRuleSourceClosure.Rejected();
                // Reobserve actual bytes in a new operation scope. Never rebase after drift.
                if (capturedLive)
                {
                    var finalCapture = CaptureCurrent();
                    if (finalCapture.IntegrityDigest != environment!.CaptureDigest
                        || capturedExecution is not null && !capturedExecution.Revalidate(stored, finalCapture,
                            _retainedCharacterQueries!, cancellationToken)) throw WorkspacePrivateRuleSourceClosure.Rejected();
                }
                if (!capturedLive && environment is not null)
                    AdmitRetainedCurrentEnvironment(record, owner, id, recovery!, environment, currentRead);
                cancellationToken.ThrowIfCancellationRequested();
                if (lease.Stamp != expectedOwner || CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)) != beforeDigest)
                    throw WorkspacePrivateRuleSourceClosure.Rejected();
                return result;

                CharacterCreationRuleSourceCapture CaptureCurrent()
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // This explicit complete export binds the producer's selected
                    // settings namespace; ordinary character-only imports cannot.
                    var exportSources = currentSources is ICharacterRetainedSettingsExportSource producer
                        ? producer.ForCompleteSourceExport() : currentSources;
                    using var scope = (exportSources as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
                    var sources = scope ?? exportSources;
                    if (sources is not ICharacterCreationRuleSourceCapture collector)
                        throw new WorkspaceCurrentRuleEnvironmentUnavailableException(["current-rule-environment-capture-unavailable"]);
                    CharacterCreationRuleSourceCapture? capture;
                    IReadOnlyList<Chummer.Contracts.Content.RulePackResolutionDiagnostic> diagnostics;
                    bool captured = selectNewCaptureProfile
                        ? collector.TryCaptureChummer5aRuleSources(stored.Document.Content, newCaptureCompatibilityProfile,
                            out capture, out diagnostics)
                        : collector.TryCaptureChummer5aRuleSources(stored.Document.Content, out capture, out diagnostics);
                    if (!captured || capture is null)
                    {
                        var blockers = diagnostics.Select(value => value.MessageKey).Where(value => !string.IsNullOrWhiteSpace(value))
                            .Select(value => value!).Distinct(StringComparer.Ordinal).ToArray();
                        throw new WorkspaceCurrentRuleEnvironmentUnavailableException(blockers.Length == 0
                            ? ["current-rule-environment-method-or-profile-unavailable"] : blockers);
                    }
                    // A scope must honor the requested interpretation, never silently
                    // return V1 or an unbound capture for a new-version request.
                    if (selectNewCaptureProfile && capture.CompatibilityProfile != newCaptureCompatibilityProfile
                        || !FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(capture.ExportPrivateInputs(),
                            capture.IntegrityDigest, out var restored) || restored is null
                        || restored.CompatibilityProfile != capture.CompatibilityProfile)
                        throw WorkspacePrivateRuleSourceClosure.Rejected();
                    return restored;
                }
                });
            }
        }
        catch (WorkspaceCurrentRuleEnvironmentUnavailableException) { throw; }
        catch (Exception error) when (WorkspacePrivateRuleSourceClosure.IsFailure(error))
        { throw WorkspacePrivateRuleSourceClosure.Rejected(); }
    }

    /// <summary>
    /// Synchronous current-environment read. Ordinary readers cannot accidentally
    /// compute with an ambient resolver. The callback receives only the exact retained
    /// resolver admitted for this character; it must not reenter store mutations or await.
    /// Host wiring is explicit and is not enabled by storing a carrier.
    /// </summary>
    public TResult WithCurrentRuleEnvironment<TResult>(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        CharacterWorkspaceId id, Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, TResult> read,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (typeof(Task).IsAssignableFrom(typeof(TResult)) || typeof(TResult) == typeof(ValueTask)
            || typeof(TResult).IsGenericType && typeof(TResult).GetGenericTypeDefinition() == typeof(ValueTask<>))
            throw new ArgumentException("Current-environment reads must be synchronous.");
        cancellationToken.ThrowIfCancellationRequested();
        if (!expectedOwner.IsValid || expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser
            || owners is not IOwnerContextLeaseAccessor authority
            || !authority.TryAcquire(expectedOwner, out var ownerLease)) throw WorkspacePrivateRuleSourceClosure.Rejected();
        using (ownerLease)
        {
            if (ownerLease is null || ownerLease.Stamp != expectedOwner) throw WorkspacePrivateRuleSourceClosure.Rejected();
            OwnerScope owner = expectedOwner.Owner.IsLocalSingleUser ? expectedOwner.Owner : new(expectedOwner.Owner.NormalizedValue);
            RequireRetainedRuleSourceStorage();
            if (!ValidRestoreOwner(owner) || TryGetPath(owner, id) is not { } path
                || !TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false)) throw WorkspacePrivateRuleSourceClosure.Rejected();
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            var record = ReadPrivateRecord(path);
            string before = CharacterCreationFinalizationDigest.Compute(record);
            var environment = record.RetainedRuleSources?.CurrentEnvironment
                ?? throw new WorkspaceCurrentRuleEnvironmentUnavailableException(["current-rule-environment-not-active"]);
            if (_retainedCharacterQueries is null
                || !TryMaterializeRecord(record, out var document, out var revision, out var saved, out bool migration)
                || migration) throw WorkspacePrivateRuleSourceClosure.Rejected();
            DateTimeOffset timestamp = new(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            // Reuse only this synchronous read's exact canonical observation. The
            // callback gets a fresh resolver, never the cached execution. Every
            // recovery still reopens the owner/incarnation-bound source bytes.
            // An injected query adapter can change answers during the callback.
            // Only the existing sealed, immutable parser may reuse domain results.
            using var currentRead = _retainedCharacterQueries is XmlCharacterFileQueries
                { HasImmutableEvaluationQueries2764: true }
                ? new PrivateCurrentRuleEnvironmentAdmission.CurrentReadEvaluation(
                    owner.NormalizedValue, new(id, document, timestamp, revision, saved),
                    environment, _retainedCharacterQueries, before)
                : null;
            using var recovery = OpenRecordRuleSources(owner, id, record,
                reuseCurrentCaptureForRead: true, currentRead: currentRead);
            var observed = ReadWorkspaceUnderLease(owner, id, path, out _, privateClosureRead: true,
                reuseCurrentCaptureForRead: true, currentRead: currentRead);
            if (!observed.Success || observed.Value is not { } stored || stored.LastUpdatedUtc != timestamp)
                throw WorkspacePrivateRuleSourceClosure.Rejected();
            if (recovery is null || !recovery.TryRecoverRuleSourceCapture(environment.CaptureDigest, out var capture)
                || capture is null)
                throw new WorkspaceCurrentRuleEnvironmentUnavailableException(["current-rule-environment-assets-unavailable"]);
            cancellationToken.ThrowIfCancellationRequested();
            TResult result = read(stored, capture.CreateReplayResolver());
            if (result is Task || result is ValueTask || typeof(Task).IsAssignableFrom(typeof(TResult))
                || typeof(TResult) == typeof(ValueTask) || typeof(TResult).IsGenericType
                    && typeof(TResult).GetGenericTypeDefinition() == typeof(ValueTask<>)
                || result is not null && result.GetType().IsGenericType
                    && result.GetType().GetGenericTypeDefinition() == typeof(ValueTask<>))
                throw new ArgumentException("Current-environment reads must be synchronous.");
            cancellationToken.ThrowIfCancellationRequested();
            if (ownerLease.Stamp != expectedOwner || CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)) != before
                || !recovery.ValidateRetainedRuleSources()) throw WorkspacePrivateRuleSourceClosure.Rejected();
            AdmitRetainedCurrentEnvironment(record, owner, id, recovery, environment, currentRead);
            // The memo is observation-only and expires below. Recheck the live
            // record and timestamp after admission before publishing the result.
            if (ownerLease.Stamp != expectedOwner || File.GetLastWriteTimeUtc(path) != timestamp.UtcDateTime
                || CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)) != before)
                throw WorkspacePrivateRuleSourceClosure.Rejected();
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
    }

    public CurrentLifeModulePreviewDisposition PreviewCurrentLifeModuleFinalization(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        CharacterCreationFoundationFinalizationPreviewRequest request,
        out CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationPreview>? result,
        CancellationToken cancellationToken = default)
    {
        result = null;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (request?.Binding is null || !expectedOwner.IsValid
                || expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser
                || owners is not IOwnerContextLeaseAccessor authority
                || !authority.TryAcquire(expectedOwner, out var ownerLease))
                return CurrentLifeModulePreviewDisposition.Refused;
            using (ownerLease)
            {
                if (ownerLease is null || ownerLease.Stamp != expectedOwner)
                    return CurrentLifeModulePreviewDisposition.Refused;
                OwnerScope owner = expectedOwner.Owner.IsLocalSingleUser ? expectedOwner.Owner : new(expectedOwner.Owner.NormalizedValue);
                var id = request.Binding.WorkspaceId;
                if (!ValidRestoreOwner(owner) || TryGetPath(owner, id) is not { } path
                    || !TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                    return CurrentLifeModulePreviewDisposition.Refused;
                using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
                var record = ReadPrivateRecord(path);
                string before = CharacterCreationFinalizationDigest.Compute(record);
                // The restore observation mode validates the actual envelope/history
                // but refuses migration. It is not itself current-execution authority.
                using var history = WorkspacePrivateRuleSourceClosureCompletion.BeginHistoryObservation();
                if (!TryMaterializeRecord(record, out var capturedDocument, out var capturedRevision,
                    out var capturedSavedRevision, out bool migration) || migration)
                    return CurrentLifeModulePreviewDisposition.Refused;
                DateTimeOffset readTimestamp = new(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
                string? slot = ReadContinuationSlotUnderLease(path);
                using var currentRead = record.RetainedRuleSources?.CurrentEnvironment is { } currentEnvironment
                    && _retainedCharacterQueries is not null
                    ? new PrivateCurrentRuleEnvironmentAdmission.CurrentReadEvaluation(owner.NormalizedValue,
                        new(id, capturedDocument, readTimestamp, capturedRevision, capturedSavedRevision),
                        currentEnvironment, _retainedCharacterQueries, before)
                    : null;
                var observed = ReadWorkspaceUnderLease(owner, id, path, out var ledger,
                    continuationRead: true, privateClosureRead: true,
                    reuseCurrentCaptureForRead: currentRead is not null, currentRead: currentRead);
                if (!observed.Success || observed.Value is not { LocalHistory: not null } stored)
                    return CurrentLifeModulePreviewDisposition.Refused;
                if (stored.LastUpdatedUtc != readTimestamp || stored.ContentRevision != capturedRevision
                    || stored.SavedRevision != capturedSavedRevision
                    || CharacterCreationFinalizationDigest.Compute(stored.Document)
                        != CharacterCreationFinalizationDigest.Compute(capturedDocument))
                    return CurrentLifeModulePreviewDisposition.Refused;
                using var recovery = OpenRecordRuleSources(owner, id, record,
                    reuseCurrentCaptureForRead: currentRead is not null, currentRead: currentRead);
                var environment = record.RetainedRuleSources?.CurrentEnvironment;
                var custody = record.RetainedRuleSources?.PrivateClosureBinding;
                if (environment is null && custody is null)
                {
                    // Ordinary modern records do not require retained storage.
                    // Missing, corrupt, legacy and custody-only records never reach here.
                    if (recovery is not null && !recovery.ValidateRetainedRuleSources())
                        return CurrentLifeModulePreviewDisposition.Refused;
                    cancellationToken.ThrowIfCancellationRequested();
                    bool unchanged = CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)) == before
                        && ownerLease.Stamp == expectedOwner;
                    cancellationToken.ThrowIfCancellationRequested();
                    return unchanged ? CurrentLifeModulePreviewDisposition.ProvenUnbound : CurrentLifeModulePreviewDisposition.Refused;
                }
                if (environment is null || custody is not null)
                    return CurrentLifeModulePreviewDisposition.Refused;
                RequireRetainedRuleSourceStorage();
                if (_retainedCharacterQueries is null || recovery is null
                    || !recovery.TryRecoverRuleSourceCapture(environment.CaptureDigest, out var capture) || capture is null)
                    return CurrentLifeModulePreviewDisposition.Refused;
                var snapshot = new WorkspaceContinuationSnapshot(owner.NormalizedValue,
                    new(id, stored.Document, stored.LastUpdatedUtc, stored.ContentRevision, stored.SavedRevision),
                    ledger.Select(entry => entry.Receipt).ToArray())
                    { DelegatedGmHistorySegmentStarts = stored.DelegatedGmHistorySegmentStarts.ToArray() };
                if (!PrivateCurrentRuleEnvironmentAdmission.TryAdmit(snapshot, capture, environment,
                    _retainedCharacterQueries, cancellationToken, out var admitted, out _, currentRead, recovery: recovery) || admitted is null)
                    return CurrentLifeModulePreviewDisposition.Refused;
                var preview = admitted.QuoteLifeModuleFinalization(request, cancellationToken);
                if (preview is null) return CurrentLifeModulePreviewDisposition.Refused;
                // The quote still performs fresh canonical re-evaluation. Reuse only
                // this invocation's unchanged observation for the surrounding read fences.
                admitted.Revalidate(cancellationToken, currentRead);
                if (!recovery.ValidateRetainedRuleSources()) return CurrentLifeModulePreviewDisposition.Refused;
                cancellationToken.ThrowIfCancellationRequested();
                // The original full record digest includes revision, incarnation,
                // both bindings and retained references. Never rebase after drift.
                if (ownerLease.Stamp != expectedOwner
                    || File.GetLastWriteTimeUtc(path) != readTimestamp.UtcDateTime
                    || ReadContinuationSlotUnderLease(path) != slot
                    || CharacterCreationFinalizationDigest.Compute(ReadPrivateRecord(path)) != before)
                    return CurrentLifeModulePreviewDisposition.Refused;
                cancellationToken.ThrowIfCancellationRequested();
                result = new(preview.FinalizationBlocked.Count == 0
                    ? CharacterCreationFoundationOutcomes.Success : CharacterCreationFoundationOutcomes.Blocked,
                    preview, preview.FinalizationBlocked);
                return CurrentLifeModulePreviewDisposition.Preview;
            }
        }
        catch (OperationCanceledException) { result = null; throw; }
        catch (Exception) { result = null; return CurrentLifeModulePreviewDisposition.Refused; }
    }

    // Reuse the store's exact receipt/source visitor. Neither a client-supplied
    // reference list nor a digest assertion can add/remove historical inputs.
    internal static void ValidatePrivateClosureBindings(WorkspaceContinuationSnapshot snapshot,
        IEnumerable<string> digests, CharacterCreationCommittedCampaign[] campaigns, string? currentCaptureDigest = null)
    {
        var workspace = snapshot.Workspace;
        var state = workspace.Document.AuxiliaryState;
        var uses = CollectRetainedRuleSourceUses(state);
        var required = uses.Select(use => use.CaptureDigest).ToHashSet(StringComparer.Ordinal);
        if (currentCaptureDigest is not null) required.Add(currentCaptureDigest);
        // Current closures keep earlier current captures as custody-only archives.
        // They never select execution; only the exact currentCaptureDigest does.
        var available = digests.ToHashSet(StringComparer.Ordinal);
        if (currentCaptureDigest is null ? !available.SetEquals(required) : !required.IsSubsetOf(available))
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
