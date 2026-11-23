using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

// Generated default contracts for the closed display reader's three roots.
// The auxiliary graph keeps its existing shared/deferred serializer. Resolve it
// as a leaf instead of putting a converter in Options.Converters, so generated
// writers for the containing snapshots remain eligible. No snapshot, digest,
// owner, source, or evaluation result is retained by these metadata objects.
internal sealed class WorkspaceDisplayReadJsonResolver8827
    : System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver
{
    private readonly WorkspaceDisplayReadJsonContext8827 context = new();

    public System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(
        Type type, JsonSerializerOptions options)
        => type == typeof(WorkspaceDocumentAuxiliaryState)
            ? System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<WorkspaceDocumentAuxiliaryState>(
                options, new WorkspaceDocumentAuxiliaryStateJsonConverter())
            : ((System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver)context).GetTypeInfo(type, options);
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    GenerationMode = System.Text.Json.Serialization.JsonSourceGenerationMode.Default)]
[System.Text.Json.Serialization.JsonSerializable(typeof(WorkspaceDocument))]
[System.Text.Json.Serialization.JsonSerializable(typeof(WorkspaceStoredDocument))]
[System.Text.Json.Serialization.JsonSerializable(typeof(WorkspaceCurrentRuleEnvironmentBinding))]
internal sealed partial class WorkspaceDisplayReadJsonContext8827
    : System.Text.Json.Serialization.JsonSerializerContext;

// Typed adapter to the separately owned canonical Application admission. No callback,
// portable digest, importer result or public success DTO can issue this capability.
internal sealed class PrivateCurrentRuleEnvironmentAdmission
{
    private readonly WorkspaceStoredDocument _current;
    private readonly CharacterCreationRuleSourceCapture _capture;
    private readonly ICharacterFileQueries _queries;
    private readonly CharacterCurrentRuleEnvironmentExecution _execution;
    private readonly RestoreSemanticOperation3287.Observation? _operationObservation;
    internal IReadOnlyList<string> Diagnostics { get; }
    internal IReadOnlyList<string> DescribeUpdateFrom(PrivateCurrentRuleEnvironmentAdmission previous)
        => _execution.DescribeUpdateFrom(previous._execution);

    internal CharacterCurrentRuleEffectComparison? CompareCalculatedUpdateFrom(
        PrivateCurrentRuleEnvironmentAdmission previous, CancellationToken cancellationToken)
        => _operationObservation is { } after && previous._operationObservation is { } before
            && ReferenceEquals(after.Operation, before.Operation)
            ? after.Operation.Compare(after, before, cancellationToken)
            : ReferenceEquals(_queries, previous._queries)
            ? _execution.CompareRuleEffectsFrom(previous._execution,
                previous._current, previous._capture, _current, _capture, _queries, cancellationToken)
            : null;

    private PrivateCurrentRuleEnvironmentAdmission(WorkspaceStoredDocument current,
        CharacterCreationRuleSourceCapture capture, ICharacterFileQueries queries,
        CharacterCurrentRuleEnvironmentExecution execution, IReadOnlyList<string> diagnostics,
        RestoreSemanticOperation3287.Observation? operationObservation = null)
        => (_current, _capture, _queries, _execution, Diagnostics, _operationObservation)
            = (current, capture, queries, execution, diagnostics.ToArray(), operationObservation);

    internal static bool TryAdmit(WorkspaceContinuationSnapshot snapshot, CharacterCreationRuleSourceCapture capture,
        WorkspaceCurrentRuleEnvironmentBinding environment, ICharacterFileQueries queries,
        CancellationToken cancellationToken, out PrivateCurrentRuleEnvironmentAdmission? admission,
        out IReadOnlyList<string> blockers)
        => TryAdmit(snapshot, capture, environment, queries, cancellationToken, out admission, out blockers, null);

    internal static bool TryAdmit(WorkspaceContinuationSnapshot snapshot, CharacterCreationRuleSourceCapture capture,
        WorkspaceCurrentRuleEnvironmentBinding environment, ICharacterFileQueries queries,
        CancellationToken cancellationToken, out PrivateCurrentRuleEnvironmentAdmission? admission,
        out IReadOnlyList<string> blockers, CurrentReadEvaluation? currentRead,
        RestoreSemanticOperation3287? operation = null, RestoreSemanticRole3287 role = RestoreSemanticRole3287.Incoming,
        ICharacterCreationRuleSourceRecovery? recovery = null)
    {
        try
        {
            bool result = TryAdmitCore3287(snapshot, capture, environment, queries, cancellationToken,
                out admission, out blockers, currentRead, operation, role, recovery);
            if (!result) operation?.Invalidate();
            return result;
        }
        catch { operation?.Invalidate(); throw; }
    }

    private static bool TryAdmitCore3287(WorkspaceContinuationSnapshot snapshot, CharacterCreationRuleSourceCapture capture,
        WorkspaceCurrentRuleEnvironmentBinding environment, ICharacterFileQueries queries,
        CancellationToken cancellationToken, out PrivateCurrentRuleEnvironmentAdmission? admission,
        out IReadOnlyList<string> blockers, CurrentReadEvaluation? currentRead,
        RestoreSemanticOperation3287? operation, RestoreSemanticRole3287 role,
        ICharacterCreationRuleSourceRecovery? recovery)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (currentRead is not null && operation is not null) throw WorkspacePrivateRuleSourceClosure.Rejected();
        admission = null;
        var actual = WorkspacePrivateRuleSourceClosure.DescribeCurrentEnvironment(snapshot, capture,
            environment.ExecutionProfile, environment.ExecutionProfileFingerprint, environment.AdmissionDiagnostics);
        if (!JsonSerializer.SerializeToUtf8Bytes(actual).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(environment)))
            throw WorkspacePrivateRuleSourceClosure.Rejected();
        var workspace = snapshot.Workspace;
        var current = new WorkspaceStoredDocument(workspace.Id, workspace.Document,
            workspace.ContentRevision, workspace.SavedRevision, workspace.LastUpdatedUtc);
        current = currentRead?.BindCompleteReadContext(current) ?? current;
        current = current.WithValidatedCreationReceiptHistory(recovery);
        CharacterCurrentRuleEnvironmentExecution? execution;
        IReadOnlyList<string> diagnostics;
        var observation = operation?.Observe(role, snapshot, capture, environment, queries, recovery);
        if (observation is not null)
        {
            execution = observation.Execution;
            diagnostics = observation.Blockers;
        }
        else if (currentRead is null)
        {
            var result = CharacterCurrentRuleEnvironmentAdmission.AdmitCurrentEnvironment(current, capture, queries, cancellationToken);
            execution = result.Execution;
            diagnostics = result.Blockers;
        }
        else execution = currentRead.Evaluate(snapshot.OwnerId, current, capture, environment, queries, cancellationToken, out diagnostics);
        if (execution is null)
        {
            blockers = diagnostics.Count == 0 ? ["current-rule-environment-canonical-admission-unavailable"] : diagnostics.ToArray();
            return false;
        }
        if (environment.ExecutionProfile is null || environment.ExecutionProfileFingerprint is null
            || execution.ExecutionProfile != environment.ExecutionProfile
            || execution.ExecutionProfileFingerprint != environment.ExecutionProfileFingerprint
            || execution.CaptureDigest != environment.CaptureDigest
            || execution.CompatibilityProfile != environment.CompatibilityProfile
            || execution.RequiredPackageSetDigest != environment.RequiredPackageSetDigest
            || !diagnostics.SequenceEqual(environment.AdmissionDiagnostics, StringComparer.Ordinal))
        {
            blockers = ["current-rule-environment-interpretation-mismatch"];
            return false;
        }
        cancellationToken.ThrowIfCancellationRequested();
        admission = new(current, capture, queries, execution, diagnostics, observation);
        blockers = diagnostics.ToArray();
        return true;
    }

    internal void Revalidate(CancellationToken cancellationToken)
        => Revalidate(cancellationToken, null);

    internal void Revalidate(CancellationToken cancellationToken, CurrentReadEvaluation? currentRead)
    {
        if (_operationObservation is { } observation)
        {
            observation.Operation.Validate(observation, cancellationToken);
            return;
        }
        if (currentRead is null
            ? !_execution.Revalidate(_current, _capture, _queries, cancellationToken)
            : !currentRead.Revalidate(_execution, _current, _capture, _queries, cancellationToken))
            throw WorkspacePrivateRuleSourceClosure.Rejected();
    }

    internal void ValidateOperationAfterLiveFence3287(CancellationToken cancellationToken)
    {
        if (_operationObservation is { } observation)
            observation.Operation.Validate(observation, cancellationToken);
    }

    internal void InvalidateOperationObservation3287() => _operationObservation?.Operation.Invalidate();

    // Created only by display observations, synchronous previews or retained-resolver reads.
    // Each use remains synchronous under live owner/workspace leases; a chooser may own
    // the exact observation across reads, with fresh source/record fences at each use.
    // Recovery still reads live files at every boundary; no restore/write path receives this.
    internal sealed class CurrentReadEvaluation : IDisposable
    {
        // Metadata only. Every observation still serializes the complete current
        // document and auxiliary graph afresh, with the existing default wire
        // contract. No byte, property, getter or comparison is omitted.
        private static readonly JsonSerializerOptions ReadSnapshotOptions8170 = CreateReadSnapshotOptions8170();

        private static JsonSerializerOptions CreateReadSnapshotOptions8170()
        {
            // Keep the same default wire policy and opaque auxiliary leaf,
            // but do not discover the remaining display graph by reflection on
            // the first owner-bound roster read. Every value is still serialized
            // afresh at every comparison; this resolver retains metadata only.
            return new JsonSerializerOptions
            {
                TypeInfoResolver = new WorkspaceDisplayReadJsonResolver8827()
            };
        }

        private readonly string _owner;
        private readonly string _recordDigest;
        private readonly CharacterWorkspaceId _id;
        private readonly long _revision, _saved;
        private readonly byte[] _document, _environment;
        private readonly ICharacterFileQueries _queries;
        private readonly WorkspaceStoredDocument? _completeRead;
        private readonly CancellationToken _readCancellation;
        private readonly List<(byte[] Current, CharacterCurrentRuleEnvironmentAdmissionResult Result)> _observations = [];
        private bool _active = true;
        internal DateTimeOffset LastUpdatedUtc { get; }

        // One decoded immutable source archive in this already bound display
        // invocation. Every borrower supplies newly read, contained file bytes
        // and a freshly validated reference. No resolver or authority is cached.
        private const int MaximumDecodedCaptureBytes8168 = 4 * 1024 * 1024;
        private CharacterCreationRetainedRuleSourceReference? _decodedReference8168;
        private byte[]? _decodedBytes8168;
        private CharacterCreationRuleSourceCapture? _decodedCapture8168;

        private void RequireCaptureOwner8168(string owner, CharacterWorkspaceId id, ICharacterFileQueries queries)
        {
            _readCancellation.ThrowIfCancellationRequested();
            if (_active && owner == _owner && id == _id && ReferenceEquals(queries, _queries)) return;
            Dispose();
            throw WorkspacePrivateRuleSourceClosure.Rejected();
        }

        internal CharacterCreationRuleSourceCapture? BorrowDecodedCapture8168(string owner,
            CharacterWorkspaceId id, ICharacterFileQueries queries,
            CharacterCreationRetainedRuleSourceReference reference, ReadOnlySpan<byte> freshlyReadBytes)
        {
            RequireCaptureOwner8168(owner, id, queries);
            if (_decodedCapture8168 is null) return null;
            if (_decodedReference8168 == reference && _decodedBytes8168 is not null
                && freshlyReadBytes.SequenceEqual(_decodedBytes8168)
                && _decodedCapture8168.IntegrityDigest == reference.CaptureDigest)
                return _decodedCapture8168;
            // A changed observation cannot be refreshed or revived by restoring
            // original bytes. The containing display read also retires on failure.
            Dispose();
            throw WorkspacePrivateRuleSourceClosure.Rejected();
        }

        internal void RememberDecodedCapture8168(string owner, CharacterWorkspaceId id,
            ICharacterFileQueries queries, CharacterCreationRetainedRuleSourceReference reference,
            ReadOnlySpan<byte> freshlyReadBytes, CharacterCreationRuleSourceCapture capture)
        {
            RequireCaptureOwner8168(owner, id, queries);
            // Only the store's closed import factory reaches this method. Keep
            // sourcebook envelopes and large archives on their ordinary path.
            if (capture.SourcebookSelection is not null || freshlyReadBytes.Length > MaximumDecodedCaptureBytes8168)
                return;
            if (_decodedCapture8168 is not null || freshlyReadBytes.Length != reference.ByteLength
                || capture.IntegrityDigest != reference.CaptureDigest)
            {
                Dispose();
                throw WorkspacePrivateRuleSourceClosure.Rejected();
            }
            _decodedReference8168 = reference;
            _decodedBytes8168 = freshlyReadBytes.ToArray();
            _decodedCapture8168 = capture;
        }

        private void ClearDecodedCapture8168()
        {
            if (_decodedBytes8168 is not null) CryptographicOperations.ZeroMemory(_decodedBytes8168);
            _decodedBytes8168 = null;
            _decodedReference8168 = null;
            _decodedCapture8168 = null;
        }



        internal CurrentReadEvaluation(string owner, WorkspaceDocumentSnapshot workspace,
            WorkspaceCurrentRuleEnvironmentBinding environment, ICharacterFileQueries queries, string recordDigest,
            WorkspaceStoredDocument? completeRead = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _readCancellation = cancellationToken;
            (_owner, _id, _revision, _saved, _queries) = (owner, workspace.Id, workspace.ContentRevision, workspace.SavedRevision, queries);
            _recordDigest = recordDigest;
            _completeRead = completeRead;
            LastUpdatedUtc = workspace.LastUpdatedUtc;
            _document = JsonSerializer.SerializeToUtf8Bytes(workspace.Document, ReadSnapshotOptions8170);
            _environment = JsonSerializer.SerializeToUtf8Bytes(environment, ReadSnapshotOptions8170);
        }

        // Only the store supplies this private record candidate. It grants no
        // read or write authority: the ordinary read validates the ledger and
        // Evaluate later requires the entire final stored result to match.
        internal WorkspaceStoredDocument BindCompleteReadContext(WorkspaceStoredDocument current)
        {
            _readCancellation.ThrowIfCancellationRequested();
            if (_completeRead is null) return current;
            if (!Matches(current, _queries) || !Matches(_completeRead, _queries)
                || current.LocalHistory is not null || current.HasDelegatedGmHistory
                || current.DelegatedGmHistorySegmentStarts.Count != 0)
            {
                Dispose();
                throw WorkspacePrivateRuleSourceClosure.Rejected();
            }
            return _completeRead;
        }

        internal void ValidateRecord(string owner, CharacterWorkspaceId id, string recordDigest)
        {
            _readCancellation.ThrowIfCancellationRequested();
            if (_active && owner == _owner && id == _id && recordDigest == _recordDigest) return;
            Dispose();
            throw WorkspacePrivateRuleSourceClosure.Rejected();
        }

        private bool Matches(WorkspaceStoredDocument current, ICharacterFileQueries queries)
            => _active && ReferenceEquals(_queries, queries) && current.Id == _id
                && current.ContentRevision == _revision && current.SavedRevision == _saved
                && current.LastUpdatedUtc == LastUpdatedUtc
                && _document.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(current.Document, ReadSnapshotOptions8170));

        internal CharacterCurrentRuleEnvironmentExecution? Evaluate(string owner, WorkspaceStoredDocument current,
            CharacterCreationRuleSourceCapture capture, WorkspaceCurrentRuleEnvironmentBinding environment,
            ICharacterFileQueries queries, CancellationToken cancellationToken, out IReadOnlyList<string> blockers)
        {
            using var readLifetime = CancellationTokenSource.CreateLinkedTokenSource(_readCancellation, cancellationToken);
            cancellationToken = readLifetime.Token;
            cancellationToken.ThrowIfCancellationRequested();
            blockers = ["current-rule-environment-display-observation-changed"];
            if (!Matches(current, queries) || owner != _owner
                || !_environment.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(environment, ReadSnapshotOptions8170)))
            { Dispose(); return null; }
            byte[] exactCurrent = JsonSerializer.SerializeToUtf8Bytes(current, ReadSnapshotOptions8170);
            foreach (var observation in _observations)
            {
                if (!exactCurrent.AsSpan().SequenceEqual(observation.Current)) continue;
                if (!observation.Result.Execution!.RevalidateDisplayObservation(current, capture, queries, cancellationToken))
                { Dispose(); return null; }
                blockers = observation.Result.Blockers;
                return observation.Result.Execution;
            }
            // Every boundary receives the same exact document, revisions and file
            // timestamp. A second distinct observation is not the same current read.
            if (_observations.Count >= 1) { Dispose(); return null; }
            var result = CharacterCurrentRuleEnvironmentAdmission.AdmitCurrentEnvironment(current, capture, queries, cancellationToken);
            blockers = result.Blockers;
            if (result.Execution is null) { Dispose(); return null; }
            if (capture.SourcebookSelection is null)
                _observations.Add((exactCurrent, result));
            return result.Execution;
        }

        internal bool Revalidate(CharacterCurrentRuleEnvironmentExecution execution, WorkspaceStoredDocument current,
            CharacterCreationRuleSourceCapture capture, ICharacterFileQueries queries, CancellationToken cancellationToken)
        {
            using var readLifetime = CancellationTokenSource.CreateLinkedTokenSource(_readCancellation, cancellationToken);
            cancellationToken = readLifetime.Token;
            cancellationToken.ThrowIfCancellationRequested();
            if (!Matches(current, queries)) { Dispose(); return false; }
            if (capture.SourcebookSelection is not null)
                return execution.Revalidate(current, capture, queries, cancellationToken);
            byte[] exactCurrent = JsonSerializer.SerializeToUtf8Bytes(current, ReadSnapshotOptions8170);
            bool valid = _observations.Any(observation => ReferenceEquals(observation.Result.Execution, execution)
                    && exactCurrent.AsSpan().SequenceEqual(observation.Current))
                && execution.RevalidateDisplayObservation(current, capture, queries, cancellationToken);
            if (!valid) Dispose();
            return valid;
        }

        public void Dispose() { _active = false; _observations.Clear(); ClearDecodedCapture8168(); }
    }

    internal CharacterCreationFoundationFinalizationPreview? QuoteLifeModuleFinalization(
        CharacterCreationFoundationFinalizationPreviewRequest request, CancellationToken cancellationToken)
    {
        // QuoteLifeModuleFinalization performs fresh Reevaluate itself, then verifies
        // the retained archive again before returning. The store retains its final
        // owner, source and record checks after the quote.
        return _execution.QuoteLifeModuleFinalization(_current, _capture, _queries, request, cancellationToken);
    }

    internal CharacterCurrentLifeModuleFinalizationPreparation? PrepareLifeModuleFinalization(
        OwnerScope owner, CharacterCreationFoundationFinalizationConfirmRequest request, CancellationToken cancellationToken)
    {
        // The canonical preparation freshly reevaluates this exact context before
        // building the transaction, then revalidates it again before returning.
        // The store still repeats preparation at the final publication fence.
        return _execution.PrepareLifeModuleFinalization(owner, _current, _capture, _queries, request, cancellationToken);
    }

    internal static bool TryAdmit(WorkspacePrivateRuleSourceClosure.Decoded decoded, ICharacterFileQueries queries,
        CancellationToken cancellationToken, out PrivateCurrentRuleEnvironmentAdmission? admission, out IReadOnlyList<string> blockers,
        RestoreSemanticOperation3287? operation = null, RestoreSemanticRole3287 role = RestoreSemanticRole3287.Incoming)
    {
        admission = null;
        blockers = [];
        using var recovery = new WorkspacePrivateRuleSourceClosure.ObservationRecovery(decoded.Captures, decoded.Campaigns, queries);
        return decoded.CurrentEnvironment is not { } environment || TryAdmit(decoded.Snapshot,
            decoded.Captures[environment.CaptureDigest], environment, queries, cancellationToken, out admission, out blockers,
            null, operation, role, recovery);
    }
}

/// <summary>Single-use, process-local custody review. Never serialize this object as a command.</summary>
public sealed class WorkspacePrivateRuleSourceClosureRestoreReview : IDisposable
{
    private byte[]? _bytes;
    internal object Issuer { get; }
    internal OwnerContextStamp Owner { get; }
    internal string? TargetRecordDigest { get; }
    internal string? RoamingBaseDigest { get; init; }
    internal bool CaptureOnly { get; init; }
    public Guid OperationId { get; }
    public string? AdmissionDigest { get; }
    public WorkspacePrivateRuleSourceClosureBinding Binding { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public WorkspaceContinuationRestoreResult Result { get; }
    // Display observations only. Confirm never consumes these caller-visible graphs.
    public WorkspaceContinuationExport? PreviewContinuation { get; internal set; }
    public WorkspaceCurrentRuleEnvironmentBinding? PreviewCurrentEnvironment { get; internal set; }
    public CharacterFileSummary? PreviewCharacter { get; internal set; }
    public WorkspaceRuleUpdatePreview? UpdatePreview { get; internal init; }
    internal PrivateRuleUpdateOriginal? UpdateOriginal { get; init; }
    internal PrivateAuthoredRuleSourceFence? AuthoredSource { get; init; }
    internal PrivateRuleResolution? LocalResolution { get; init; }
    internal Guid? AlternativeOperation { get; init; }
    public WorkspaceContinuationExport? ProposedContinuation => LocalResolution?.Export();
    public WorkspaceRuleEnvironmentTransfer? ProposedTransfer => LocalResolution is { } local
        ? new(WorkspaceRuleEnvironmentTransfer.SchemaV1, local.Binding, local.Length) : null;

    internal WorkspacePrivateRuleSourceClosureRestoreReview(object issuer, OwnerContextStamp owner,
        byte[]? bytes, WorkspacePrivateRuleSourceClosureBinding binding, DateTimeOffset expires,
        WorkspaceContinuationRestoreResult result, string? targetRecordDigest = null,
        Guid operationId = default, string? admissionDigest = null)
    {
        Issuer = issuer; Owner = owner; _bytes = bytes; Binding = binding; ExpiresAtUtc = expires;
        Result = result; TargetRecordDigest = targetRecordDigest;
        OperationId = operationId; AdmissionDigest = admissionDigest;
    }

    internal byte[]? Consume(object issuer) => ReferenceEquals(issuer, Issuer)
        ? Interlocked.Exchange(ref _bytes, null) : null;
    public void Dispose()
    {
        var bytes = Interlocked.Exchange(ref _bytes, null);
        if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
        UpdateOriginal?.Dispose();
        LocalResolution?.Dispose();
    }
    public override string ToString() => nameof(WorkspacePrivateRuleSourceClosureRestoreReview);
}

// Only the synchronous Core restore/resume paths issue a permit. No callback or wire DTO grants authority.
internal sealed class PrivateClosureRestoreAdmission : IDisposable
{
    private readonly IOwnerContextLease _lease;
    private readonly TimeProvider _clock;
    private readonly CancellationToken _cancellation;
    private readonly DateTimeOffset _expires;
    private bool _disposed;
    internal OwnerContextStamp Owner { get; }
    internal WorkspaceContinuationRestoreTarget Target { get; }
    internal string TargetRecordDigest { get; }
    internal string? RoamingBaseDigest { get; }
    internal bool CaptureOnly { get; }
    internal WorkspacePrivateRuleSourceClosureBinding Binding { get; }
    internal PrivateCurrentRuleEnvironmentAdmission? CurrentEnvironment { get; }
    internal FileWorkspaceStore.PrivateClosureResumeStaging? ResumeStaging { get; }
    internal WorkspaceContinuationRestoreReceipt Receipt { get; }
    internal PrivateRuleUpdateOriginal? UpdateOriginal { get; }
    private readonly PrivateCurrentRuleEnvironmentAdmission? _originalAdmission;
    private readonly PrivateAuthoredRuleSourceFence? _authoredSource;
    private readonly PrivateCurrentRuleEnvironmentAdmission? _chosenAdmission;
    internal WorkspacePrivateRuleSourceClosureBinding? ChosenBinding { get; }
    internal byte[]? ChosenBytes { get; }
    internal Guid? AlternativeOperation { get; }

    internal PrivateClosureRestoreAdmission(WorkspacePrivateRuleSourceClosureRestoreReview review,
        IOwnerContextLease lease, TimeProvider clock, CancellationToken cancellation,
        PrivateCurrentRuleEnvironmentAdmission? currentEnvironment = null,
        PrivateCurrentRuleEnvironmentAdmission? originalAdmission = null,
        PrivateRuleUpdateOriginal? updateOriginal = null,
        PrivateRuleResolution? localResolution = null, byte[]? chosenBytes = null,
        PrivateCurrentRuleEnvironmentAdmission? chosenAdmission = null)
    {
        Owner = review.Owner; Target = review.Result.Target!; TargetRecordDigest = review.TargetRecordDigest!;
        RoamingBaseDigest = review.RoamingBaseDigest;
        CaptureOnly = review.CaptureOnly;
        Binding = localResolution?.Binding ?? review.Binding;
        ChosenBinding = localResolution is null ? null : review.Binding;
        AlternativeOperation = review.AlternativeOperation;
        ChosenBytes = localResolution is null ? null : chosenBytes;
        _chosenAdmission = chosenAdmission;
        if (localResolution is not null && (updateOriginal is null || originalAdmission is null || chosenBytes is null || chosenAdmission is null
            || review.LocalResolution?.Binding != localResolution.Binding)) throw WorkspacePrivateRuleSourceClosure.Rejected();
        _lease = lease; _clock = clock; _cancellation = cancellation;
        _expires = review.ExpiresAtUtc;
        CurrentEnvironment = currentEnvironment;
        UpdateOriginal = updateOriginal;
        _originalAdmission = originalAdmission;
        _authoredSource = review.AuthoredSource;
        if ((UpdateOriginal is null) != (originalAdmission is null)) throw WorkspacePrivateRuleSourceClosure.Rejected();
        Receipt = new(review.OperationId, review.AdmissionDigest!, Binding.ContinuationDigest,
            Binding.ClosureDigest, Guid.NewGuid().ToString("N"), Binding.ContentRevision,
            Binding.SavedRevision, clock.GetUtcNow());
    }

    // Resume uses a NEW live owner/canonical admission for the SAME original
    // receipt. No Review is constructed or consumed and no incarnation is minted.
    internal PrivateClosureRestoreAdmission(OwnerContextStamp owner, IOwnerContextLease lease,
        TimeProvider clock, DateTimeOffset expires, CancellationToken cancellation,
        WorkspaceContinuationRestoreTarget target, WorkspacePrivateRuleSourceClosureBinding binding,
        WorkspaceContinuationRestoreReceipt receipt, PrivateCurrentRuleEnvironmentAdmission current,
        FileWorkspaceStore.PrivateClosureResumeStaging staging)
    {
        if (target.Exists || target.SlotGeneration is not null || target.OwnerId != owner.Owner.NormalizedValue
            || target.WorkspaceId != binding.WorkspaceId || binding.OwnerId != owner.Owner.NormalizedValue
            || receipt.SourceDigest != binding.ClosureDigest || receipt.SnapshotDigest != binding.ContinuationDigest
            || receipt.ContentRevision != binding.ContentRevision || receipt.SavedRevision != binding.SavedRevision)
            throw WorkspacePrivateRuleSourceClosure.Rejected();
        Owner = owner; _lease = lease; _clock = clock; _expires = expires; _cancellation = cancellation;
        Target = target; TargetRecordDigest = CharacterCreationFinalizationDigest.Compute<object?>(null);
        Binding = binding; Receipt = receipt; CurrentEnvironment = current; ResumeStaging = staging;
    }

    internal void ValidateFinalFence()
    {
        try
        {
            _cancellation.ThrowIfCancellationRequested();
            CurrentEnvironment?.Revalidate(_cancellation);
            _originalAdmission?.Revalidate(_cancellation);
            _chosenAdmission?.Revalidate(_cancellation);
            _authoredSource?.Validate(_cancellation);
            if (_disposed || _lease.Stamp != Owner || _clock.GetUtcNow() >= _expires)
                throw new InvalidOperationException("private-closure-admission-no-longer-live");
            // The original lease/clock check above may invoke host callbacks.
            // Finish with owned-input checks after those callbacks as well. Cold
            // admissions do no additional work here; their independent path stays.
            CurrentEnvironment?.ValidateOperationAfterLiveFence3287(_cancellation);
            _originalAdmission?.ValidateOperationAfterLiveFence3287(_cancellation);
            _chosenAdmission?.ValidateOperationAfterLiveFence3287(_cancellation);
            ResumeStaging?.ValidateFinalFence();
        }
        catch { InvalidateOperation(); throw; }
    }
    // The resumed writer invokes this AFTER its last retained-source/staging
    // reads, immediately before publication under the existing owner/workspace
    // leases. Keep this fence free of canonical/source/staging I/O.
    internal void ValidateLiveAuthorityFence()
    {
        try
        {
            _cancellation.ThrowIfCancellationRequested();
            // Read the clock before the lease so a clock callback cannot hide a
            // revoked owner. The real owner lease serializes account changes.
            bool expired = _clock.GetUtcNow() >= _expires;
            if (_disposed || expired || _lease.Stamp != Owner)
                throw new InvalidOperationException("private-closure-admission-no-longer-live");
            _cancellation.ThrowIfCancellationRequested();
        }
        catch { InvalidateOperation(); throw; }
    }

    private void InvalidateOperation()
    {
        CurrentEnvironment?.InvalidateOperationObservation3287();
        _originalAdmission?.InvalidateOperationObservation3287();
        _chosenAdmission?.InvalidateOperationObservation3287();
    }
    public void Dispose() { _disposed = true; InvalidateOperation(); }
}

/// <summary>
/// Same-owner private restore. V1 remains custody-only; V2 requires typed canonical admission.
/// Neither path grants campaign authority or source-specific copying permission.
/// The host must explicitly enable retained storage and authenticate source-specific copy admission.
/// </summary>
public sealed partial class WorkspacePrivateRuleSourceClosureRestoreService
{
    private readonly FileWorkspaceStore _store;
    private readonly IOwnerContextAccessor _owners;
    private readonly ICharacterFileQueries _queries;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private readonly object _issuer = new();

    public WorkspacePrivateRuleSourceClosureRestoreService(FileWorkspaceStore store, IOwnerContextAccessor owners,
        ICharacterFileQueries queries, TimeProvider? clock = null, TimeSpan? reviewLifetime = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _owners = owners ?? throw new ArgumentNullException(nameof(owners));
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _clock = clock ?? TimeProvider.System;
        _lifetime = reviewLifetime ?? TimeSpan.FromMinutes(5);
        if (_lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(reviewLifetime));
    }

    public WorkspacePrivateRuleSourceClosureRestoreReview Review(OwnerContextStamp owner,
        ReadOnlyMemory<byte> bytes, WorkspacePrivateRuleSourceClosureBinding expected,
        CancellationToken cancellationToken = default)
        => ReviewCore(owner, bytes, expected, false, cancellationToken);

    /// <summary>Prepares display metadata from the restore review's verified decode.
    /// Preview graphs never grant admission and Confirm always reads its private bytes again.</summary>
    public WorkspacePrivateRuleSourceClosureRestoreReview ReviewWithPreview(OwnerContextStamp owner,
        ReadOnlyMemory<byte> bytes, WorkspacePrivateRuleSourceClosureBinding expected,
        CancellationToken cancellationToken = default)
    {
        var review = ReviewCore(owner, bytes, expected, false, cancellationToken, includePreview: true);
        if (review.PreviewContinuation is not null) return review;
        // Only the early authority refusal skipped preparation. Do not replay a
        // failed decode/summary merely to fill display metadata.
        if (review.Result.Blockers?.Contains("private-closure-authority-unavailable") != true)
        {
            review.Dispose();
            if (review.Result.Outcome == WorkspaceContinuationRestoreOutcome.Canceled)
                throw new OperationCanceledException(cancellationToken);
            throw WorkspacePrivateRuleSourceClosure.Rejected();
        }
        byte[]? captured = null;
        try
        {
            // Early authority/target-support refusals precede decoding. Preserve the
            // old client's display-only preview for these denials without granting
            // or retrying admission. Invalid carriers still fail like Import did.
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes.IsEmpty || bytes.Length > WorkspacePrivateRuleSourceClosureBinding.MaximumBytes)
                throw WorkspacePrivateRuleSourceClosure.Rejected();
            captured = bytes.ToArray();
            var candidate = WorkspacePrivateRuleSourceClosure.DecodeForRestore(captured, expected, _queries, cancellationToken);
            var environment = candidate.CurrentEnvironment
                ?? throw new InvalidDataException("Current rule environment is required.");
            var summary = _queries.ParseSummary(new(candidate.Snapshot.Workspace.Document.Content));
            cancellationToken.ThrowIfCancellationRequested();
            review.PreviewContinuation = new(candidate.Snapshot, candidate.Binding.ContinuationDigest);
            review.PreviewCurrentEnvironment = environment;
            review.PreviewCharacter = summary;
            return review;
        }
        catch (Exception error) when (WorkspacePrivateRuleSourceClosure.IsFailure(error))
        { review.Dispose(); throw WorkspacePrivateRuleSourceClosure.Rejected(); }
        catch { review.Dispose(); throw; }
        finally { if (captured is not null) CryptographicOperations.ZeroMemory(captured); }
    }

    internal WorkspacePrivateRuleSourceClosureRestoreReview ReviewLocalCapture(OwnerContextStamp owner,
        ReadOnlyMemory<byte> bytes, WorkspacePrivateRuleSourceClosureBinding expected,
        CancellationToken cancellationToken)
        => ReviewCore(owner, bytes, expected, false, cancellationToken, captureOnly: true);

    // Only the roaming bridge requests this path. A persisted successful sync base,
    // the exact unchanged local continuation and Core's final record fence are all
    // required. A larger remote revision alone never permits replacement.
    internal WorkspacePrivateRuleSourceClosureRestoreReview ReviewRoaming(OwnerContextStamp owner,
        ReadOnlyMemory<byte> bytes, WorkspacePrivateRuleSourceClosureBinding expected,
        CancellationToken cancellationToken)
        => ReviewCore(owner, bytes, expected, true, cancellationToken);

    private WorkspacePrivateRuleSourceClosureRestoreReview ReviewCore(OwnerContextStamp owner,
        ReadOnlyMemory<byte> bytes, WorkspacePrivateRuleSourceClosureBinding expected, bool roaming,
        CancellationToken cancellationToken, bool captureOnly = false, bool includePreview = false)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var expires = _clock.GetUtcNow() + _lifetime;
        byte[]? captured = null;
        bool transferred = false;
        WorkspaceContinuationExport? previewContinuation = null;
        WorkspaceCurrentRuleEnvironmentBinding? previewEnvironment = null;
        CharacterFileSummary? previewCharacter = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_store.SupportsWorkspacePrivateRuleSourceClosureRestore || bytes.IsEmpty
                || bytes.Length > WorkspacePrivateRuleSourceClosureBinding.MaximumBytes
                || expected.ArchiveCount <= 0 || expected.OwnerId != owner.Owner.NormalizedValue
                || !TryAcquire(owner, out var lease))
                return Denied("private-closure-authority-unavailable");
            using (lease)
            {
                captured = bytes.ToArray();
                var candidate = WorkspacePrivateRuleSourceClosure.DecodeForRestore(captured, expected, _queries, cancellationToken);
                if (includePreview)
                {
                    // One private decode, under the same owner lease and query instance
                    // as canonical admission. Only metadata escapes after all review
                    // fences finish; no accepted execution or decoded source is cached.
                    previewEnvironment = candidate.CurrentEnvironment
                        ?? throw new InvalidDataException("Current rule environment is required.");
                    previewCharacter = _queries.ParseSummary(new(candidate.Snapshot.Workspace.Document.Content));
                    previewContinuation = new(candidate.Snapshot, candidate.Binding.ContinuationDigest);
                }
                if (!PrivateCurrentRuleEnvironmentAdmission.TryAdmit(candidate, _queries, cancellationToken, out var current, out var blockers))
                    return DeniedMany(blockers);
                var observed = _store.InspectPrivateClosureRestoreTarget(Normalize(owner.Owner), expected.WorkspaceId);
                var target = observed.Result with { Blockers = blockers };
                if (target.Outcome != WorkspaceContinuationRestoreOutcome.Available || target.Target is null)
                    return Denied("private-closure-target-unavailable", target);
                if (target.Target.Exists && target.Target.SnapshotDigest == expected.ContinuationDigest
                    && (observed.Binding == expected || captureOnly && observed.HasCurrentEnvironment))
                    return Denied("private-closure-already-current",
                        target with { Outcome = WorkspaceContinuationRestoreOutcome.AlreadyCurrent });
                var sync = roaming ? _store.ReadRoamingConnectionForReview(owner, expected.WorkspaceId) : null;
                string? roamingBase = sync is { CommonBase: not null, Publication: null, Adoption: null, Capture: null }
                    && sync.Owner.Owner == owner.Owner && sync.LocalBaseDigest == target.Target.SnapshotDigest
                    ? sync.LocalBaseDigest : null;
                if (captureOnly)
                {
                    // Persist custody of a NEW local capture without changing a
                    // character or any portable state. Historical/active captures
                    // cannot be relabeled through this entrypoint.
                    if (!target.Target.Exists || observed.HasCurrentEnvironment || observed.Binding is not null
                        || current is null || target.Target.SnapshotDigest != expected.ContinuationDigest)
                        return Denied("private-closure-local-capture-changed");
                    roamingBase = target.Target.SnapshotDigest;
                }
                if (observed.HasUnprovenCurrentEnvironmentBase(current is not null) && roamingBase is null)
                    return Denied("private-closure-common-base-unproven",
                        target with { Outcome = WorkspaceContinuationRestoreOutcome.Conflict });
                // This path grants custody only. Never retire an existing non-custody workspace
                // without an explicit current-source admission/activation contract.
                if (target.Target.Exists && observed.Binding is null && current is null)
                    return Denied("private-closure-current-source-admission-required",
                        target with { Outcome = WorkspaceContinuationRestoreOutcome.Rejected });
                if (target.Target.Exists && expected.ContentRevision <= target.Target.ContentRevision
                    && !captureOnly && (roamingBase is null || !observed.IsRoamingCheckpointAdvance(candidate.Snapshot)))
                    return Denied("private-closure-target-revision-conflict",
                        target with { Outcome = WorkspaceContinuationRestoreOutcome.Conflict });
                if (candidate.Snapshot.Workspace.LastUpdatedUtc.Offset != TimeSpan.Zero)
                    return Denied("continuation-timestamp-representation-unsupported");
                cancellationToken.ThrowIfCancellationRequested();
                if (lease!.Stamp != owner || _clock.GetUtcNow() >= expires)
                    return Denied("private-closure-review-expired");
                Guid operation = Guid.NewGuid();
                string digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                    new { Contract = current is null ? "chummer.private-closure-custody-restore/v1" : "chummer.current-rule-environment-restore/v1", Operation = operation,
                        Owner = owner, Target = target.Target, observed.RecordDigest, Binding = expected, RoamingBaseDigest = roamingBase, CaptureOnly = captureOnly,
                        ByteLength = captured.Length })));
                transferred = true;
                return WithPreview(new(_issuer, owner, captured, expected, expires, target, observed.RecordDigest, operation, digest)
                    { RoamingBaseDigest = roamingBase, CaptureOnly = captureOnly });
            }
        }
        catch (OperationCanceledException) { return Denied("private-closure-canceled", new(WorkspaceContinuationRestoreOutcome.Canceled)); }
        catch (Exception e) when (IsFailure(e)) { return Denied("private-closure-invalid-or-unavailable"); }
        finally { if (!transferred && captured is not null) CryptographicOperations.ZeroMemory(captured); }

        WorkspacePrivateRuleSourceClosureRestoreReview DeniedMany(IReadOnlyList<string> blockers)
            => WithPreview(new(_issuer, owner, null, expected, expires, new(WorkspaceContinuationRestoreOutcome.Rejected, Blockers: blockers.ToArray())));

        WorkspacePrivateRuleSourceClosureRestoreReview Denied(string blocker, WorkspaceContinuationRestoreResult? result = null)
            => WithPreview(new(_issuer, owner, null, expected, expires,
                (result ?? new(WorkspaceContinuationRestoreOutcome.Rejected)) with { Blockers = [blocker] }));

        WorkspacePrivateRuleSourceClosureRestoreReview WithPreview(WorkspacePrivateRuleSourceClosureRestoreReview review)
        {
            review.PreviewContinuation = previewContinuation;
            review.PreviewCurrentEnvironment = previewEnvironment;
            review.PreviewCharacter = previewCharacter;
            return review;
        }
    }

    public WorkspaceContinuationRestoreResult Confirm(WorkspacePrivateRuleSourceClosureRestoreReview review,
        bool explicitlyConfirmed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (!explicitlyConfirmed && review.UpdateOriginal is null)
            return new(WorkspaceContinuationRestoreOutcome.Rejected, Blockers: ["explicit-review-required"]);

        var bytes = review.Consume(_issuer);
        if (bytes is null) return new(WorkspaceContinuationRestoreOutcome.ReviewConsumed);
        if (!explicitlyConfirmed)
        {
            CryptographicOperations.ZeroMemory(bytes);
            review.UpdateOriginal?.Dispose();
            review.LocalResolution?.Dispose();
            return new(WorkspaceContinuationRestoreOutcome.Canceled)
            { RuleUpdate = new(review.OperationId, review.AdmissionDigest!, PrivateRuleUpdateOriginal.Decision, WorkspaceRuleUpdateState.NoMutation) };
        }
        using var originalCopy = review.UpdateOriginal is { } reviewedOriginal
            ? new PrivateRuleUpdateOriginal(reviewedOriginal.Bytes.ToArray(), reviewedOriginal.Binding, reviewedOriginal.LastUpdatedUtc) : null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_clock.GetUtcNow() >= review.ExpiresAtUtc) return NoWrite(new(WorkspaceContinuationRestoreOutcome.ReviewExpired));
            if (!_store.SupportsWorkspacePrivateRuleSourceClosureRestore || !TryAcquire(review.Owner, out var lease))
                return NoWrite(new(WorkspaceContinuationRestoreOutcome.Unavailable));
            using (lease)
            {
                using var semantics = new RestoreSemanticOperation3287(lease!, review.Owner, _clock,
                    review.ExpiresAtUtc, cancellationToken);
                if (review.AuthoredSource is { } authoredSource && !authoredSource.IsCurrent(cancellationToken))
                    return NoWrite(new(WorkspaceContinuationRestoreOutcome.Conflict,
                        Blockers: ["rule-update-staged-source-changed"]));
                var candidate = WorkspacePrivateRuleSourceClosure.DecodeForRestore(bytes, review.Binding, _queries, cancellationToken);
                if (!PrivateCurrentRuleEnvironmentAdmission.TryAdmit(candidate, _queries, cancellationToken, out var current, out var blockers, semantics))
                    return NoWrite(new(WorkspaceContinuationRestoreOutcome.Rejected, Blockers: blockers));
                PrivateCurrentRuleEnvironmentAdmission? originalAdmission = null;
                if (originalCopy is { } original)
                {
                    var prior = WorkspacePrivateRuleSourceClosure.DecodeForRestore(original.Bytes, original.Binding, _queries, cancellationToken);
                    if (!PrivateCurrentRuleEnvironmentAdmission.TryAdmit(prior, _queries, cancellationToken, out originalAdmission, out blockers, semantics, RestoreSemanticRole3287.Original)
                        || originalAdmission is null) return NoWrite(new(WorkspaceContinuationRestoreOutcome.Rejected, Blockers: blockers));

                    // Recalculate the exact chosen branch, before preparing any new local
                    // revision. A display DTO (or a digest of one) is never an admission.
                    var calculated = current?.CompareCalculatedUpdateFrom(originalAdmission, cancellationToken);
                    if (calculated is null || review.UpdatePreview?.Calculated is not { } reviewedCalculated
                        || CharacterCreationFinalizationDigest.Compute(calculated.Display)
                            != CharacterCreationFinalizationDigest.Compute(reviewedCalculated))
                        return NoWrite(new(WorkspaceContinuationRestoreOutcome.Conflict,
                            Blockers: ["rule-update-calculated-observation-changed"]));
                    if (review.Result.Target is not { } reviewedTarget
                        || ComputeRuleUpdateReviewDigest(review.OperationId, review.Owner, reviewedTarget,
                            review.TargetRecordDigest, review.Binding, original.Binding, bytes, original.Bytes,
                            review.UpdatePreview, review.LocalResolution?.Binding, review.AlternativeOperation)
                            != review.AdmissionDigest)
                        return NoWrite(new(WorkspaceContinuationRestoreOutcome.Conflict,
                            Blockers: ["rule-update-reviewed-inputs-changed"]));
                }
                using var local = review.LocalResolution is { } prepared
                    ? PrivateRuleResolution.Prepare(candidate, review.Result.Target!, prepared.CreatedAtUtc, _store, _queries, cancellationToken, semantics, prepared)
                    : null;
                var chosenAdmission = local is null ? null : current;
                if (local is not null)
                {
                    if (local.Binding != review.LocalResolution!.Binding)
                        return NoWrite(new(WorkspaceContinuationRestoreOutcome.Conflict, Blockers: ["rule-resolution-source-changed"]));
                    candidate = local.Decode(_queries, cancellationToken);
                    if (!PrivateCurrentRuleEnvironmentAdmission.TryAdmit(candidate, _queries, cancellationToken, out current, out blockers, semantics, RestoreSemanticRole3287.Local)
                        || current is null) return NoWrite(new(WorkspaceContinuationRestoreOutcome.Rejected, Blockers: blockers));
                }
                using var admission = new PrivateClosureRestoreAdmission(review, lease!, _clock, cancellationToken,
                    current, originalAdmission, originalCopy, local, bytes, chosenAdmission);
                admission.ValidateFinalFence();
                var result = _store.RestorePrivateClosure(admission, candidate);
                return review.UpdateOriginal is null ? result : result with
                {
                    RuleUpdate = new(review.OperationId, review.AdmissionDigest!, PrivateRuleUpdateOriginal.Decision,
                        result.Outcome == WorkspaceContinuationRestoreOutcome.Applied
                            ? WorkspaceRuleUpdateState.CommittedUpdate
                            : _store.RecoverRuleUpdateProgress(Normalize(review.Owner.Owner), review.Binding.WorkspaceId,
                                review.OperationId, review.AdmissionDigest!)?.State ?? WorkspaceRuleUpdateState.NoMutation)
                };
            }
        }
        catch (OperationCanceledException) { return NoWrite(new(WorkspaceContinuationRestoreOutcome.Canceled)); }
        catch (Exception e) when (IsFailure(e))
        {
            // A storage exception may follow rename. Never issue another write from this review.
            return new(WorkspaceContinuationRestoreOutcome.Unavailable, Blockers: ["restore-outcome-requires-recovery"])
            {
                RuleUpdate = review.UpdateOriginal is null ? null
                    : new(review.OperationId, review.AdmissionDigest!, PrivateRuleUpdateOriginal.Decision, WorkspaceRuleUpdateState.Unknown)
            };
        }
        finally { CryptographicOperations.ZeroMemory(bytes); review.UpdateOriginal?.Dispose(); review.LocalResolution?.Dispose(); }

        WorkspaceContinuationRestoreResult NoWrite(WorkspaceContinuationRestoreResult result)
            => review.UpdateOriginal is null ? result : result with
            { RuleUpdate = new(review.OperationId, review.AdmissionDigest!, PrivateRuleUpdateOriginal.Decision, WorkspaceRuleUpdateState.NoMutation) };
    }

    public WorkspaceContinuationRestoreResult Recover(OwnerContextStamp owner, CharacterWorkspaceId workspaceId,
        Guid operationId, string admissionDigest)
    {
        if (!TryAcquire(owner, out var lease)) return new(WorkspaceContinuationRestoreOutcome.Unavailable);
        using (lease)
        {
            // Receipt and progress are reconciled together under one workspace lease.
            return _store.RecoverPrivateClosureRestore(Normalize(owner.Owner), workspaceId, operationId, admissionDigest);
        }
    }

    /// <summary>
    /// Resolve only a pending ordinary import of an originally absent target.
    /// Historical metadata binds the original review; a fresh owner lease and
    /// canonical/source validation are mandatory before the original write resumes.
    /// </summary>
    public WorkspaceContinuationRestoreResult ResumeOriginalMissingTarget(OwnerContextStamp owner,
        CharacterWorkspaceId workspaceId, Guid operationId, string admissionDigest, string closureDigest,
        OwnerContextStamp originalOwner, CancellationToken cancellationToken = default)
    {
        if (!TryAcquire(owner, out var lease)) return new(WorkspaceContinuationRestoreOutcome.Unavailable);
        using (lease)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var recovered = _store.RecoverPrivateClosureRestore(Normalize(owner.Owner), workspaceId, operationId, admissionDigest);
                if (recovered.Outcome != WorkspaceContinuationRestoreOutcome.RecoveryProofMissing || recovered.RuleUpdate is not null)
                    return recovered;
                return _store.ResumePrivateClosureMissingTarget(owner, lease!, originalOwner, workspaceId,
                    operationId, admissionDigest, closureDigest, _clock, _clock.GetUtcNow() + _lifetime, cancellationToken);
            }
            catch (Exception error) when (error is OperationCanceledException || IsFailure(error))
            {
                // Includes post-rename durability failures. Preserve the journal:
                // neither cancellation nor a missing acknowledgement permits Confirm.
                return new(WorkspaceContinuationRestoreOutcome.Unavailable, Blockers: ["restore-outcome-requires-recovery"]);
            }
        }
    }

    private bool TryAcquire(OwnerContextStamp expected, out IOwnerContextLease? lease)
    {
        lease = null;
        if (!expected.IsValid || expected.Owner.UsesLocalSingleUserValue && !expected.Owner.IsLocalSingleUser
            || _owners is not IOwnerContextLeaseAccessor authority) return false;
        if (authority.TryAcquire(expected, out lease) && lease is not null && lease.Stamp == expected) return true;
        lease?.Dispose(); lease = null; return false;
    }
    private static OwnerScope Normalize(OwnerScope owner) => owner.IsLocalSingleUser ? owner : new(owner.NormalizedValue);
    private static bool IsFailure(Exception e) => WorkspacePrivateRuleSourceClosure.IsFailure(e)
        || e is WorkspacePrivateRuleSourceClosureLegacyFormatException;
}
