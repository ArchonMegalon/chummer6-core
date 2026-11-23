using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;

namespace Chummer.Infrastructure.Workspaces;

/// <summary>V1 inputs, including new local-only captures, remain recoverable without migration.</summary>
public sealed class WorkspacePrivateRuleSourceClosureLegacyFormatException : InvalidOperationException
{
    public const string FailureCode = "legacy-rule-inputs-v1";
    internal WorkspacePrivateRuleSourceClosureLegacyFormatException()
        : base("Local v1 rule inputs cannot be privately transferred; retained local history is unchanged.") { }
}

/// <summary>Explicit private current-environment blockers; contains no source bytes or paths.</summary>
public sealed class WorkspaceCurrentRuleEnvironmentUnavailableException : InvalidOperationException
{
    public IReadOnlyList<string> Blockers { get; }
    internal WorkspaceCurrentRuleEnvironmentUnavailableException(IReadOnlyList<string> blockers)
        : base("The retained current rule environment is unavailable.")
        => Blockers = Array.AsReadOnly(blockers.ToArray());
}

/// <summary>
/// Core-owned private wire carrier. Import verifies content consistency only.
/// The host must separately authenticate selection ownership, source-specific
/// copy admission and current campaign authority before storing/using this data.
/// This type cannot adopt/restore a workspace or replay a mutation.
/// </summary>
public sealed class WorkspacePrivateRuleSourceClosure
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions CampaignOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true, MaxDepth = 32
    };
    private readonly byte[] _bytes;
    public WorkspacePrivateRuleSourceClosureBinding Binding { get; }
    private WorkspacePrivateRuleSourceClosure(byte[] bytes, WorkspacePrivateRuleSourceClosureBinding binding)
        => (_bytes, Binding) = (bytes, binding);
    public override string ToString() => nameof(WorkspacePrivateRuleSourceClosure);

    /// <summary>Private authenticated transport/storage only. Never a provider prompt or diagnostic.</summary>
    public byte[] ExportPrivateBytes() => _bytes.ToArray();

    /// <summary>Detached candidate from these exact original bytes; not restore permission.</summary>
    public WorkspaceContinuationExport ExportContinuation()
    {
        var decoded = Decode(_bytes, _bytes.Length);
        return new(decoded.Snapshot, decoded.Binding.ContinuationDigest);
    }

    /// <summary>Detached private observation. V1 has no current-environment assertion.</summary>
    public WorkspaceCurrentRuleEnvironmentBinding? InspectCurrentEnvironment()
        => Decode(_bytes, _bytes.Length).CurrentEnvironment;

    // One decode supplies both detached observations for this bridge export.
    // No admission or lease is reused; no decoded graph is cached on the carrier.
    internal (WorkspaceContinuationExport Continuation, WorkspaceCurrentRuleEnvironmentBinding? Environment)
        InspectForExport(CancellationToken cancellationToken)
    {
        var decoded = Decode(_bytes, _bytes.Length, cancellationToken);
        return (new(decoded.Snapshot, decoded.Binding.ContinuationDigest), decoded.CurrentEnvironment);
    }

    public static WorkspacePrivateRuleSourceClosure Import(ReadOnlyMemory<byte> bytes,
        WorkspacePrivateRuleSourceClosureBinding expected, int maximumBytes, ICharacterFileQueries queries,
        CancellationToken cancellationToken = default)
    {
        try
        {
            CheckBound(maximumBytes);
            cancellationToken.ThrowIfCancellationRequested();
            if (expected is null || bytes.IsEmpty || bytes.Length > maximumBytes) throw Rejected();
            byte[] detached = bytes.ToArray();
            var decoded = Decode(detached, maximumBytes, cancellationToken);
            if (decoded.Binding != expected) throw Rejected();
            using var recovery = new ObservationRecovery(decoded.Captures, decoded.Campaigns,
                queries ?? throw Rejected());
            if (!WorkspacePrivateRuleSourceClosureCompletion.ValidateHistoryObservation(decoded.Snapshot, recovery))
                throw Rejected();
            cancellationToken.ThrowIfCancellationRequested();
            return new(detached, decoded.Binding);
        }
        catch (Exception error) when (IsFailure(error)) { throw Rejected(); }
    }

    /// <summary>
    /// Re-decodes the immutable carrier on each call, validates exact historical
    /// receipt/source/campaign closure, and recomputes completion against current
    /// sources under one real owner lease. No lease crosses an await.
    /// </summary>
    public TResult EvaluateCompletionInputs<TResult>(
        IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        ICharacterSourceDataResolver currentSources, ICharacterFileQueries queries,
        ILifeModulesCatalogService lifeModules, string executionSchema,
        Func<WorkspaceContinuationCompletionResult, CharacterCreationRuleSourceExecutionContext?,
            WorkspacePrivateRuleSourceClosureBinding, TResult> evaluate,
        CancellationToken cancellationToken = default, bool sourcebookSelection = false)
    {
        // This legacy API accepts an ambient resolver. Never silently substitute it
        // for a V2 manifest's exact retained current environment.
        if (Binding.Schema != WorkspacePrivateRuleSourceClosureBinding.SchemaV1) throw Rejected();
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var decoded = Decode(_bytes, _bytes.Length, cancellationToken);
            if (decoded.Binding != Binding) throw Rejected();
            using var recovery = new ObservationRecovery(decoded.Captures, decoded.Campaigns, queries);
            return WorkspacePrivateRuleSourceClosureCompletion.Evaluate(owners, expectedOwner, currentSources,
                queries, lifeModules, decoded.Continuation, Math.Min(_bytes.Length,
                    WorkspacePrivateRuleSourceClosureBinding.MaximumContinuationBytes),
                Binding, recovery, executionSchema, evaluate, cancellationToken, sourcebookSelection);
        }
        catch (Exception error) when (IsFailure(error)) { throw Rejected(); }
    }

    // Only the store's custody transaction consumes this graph. No execution authority is added.
    internal static Decoded DecodeForRestore(byte[] detachedBytes, WorkspacePrivateRuleSourceClosureBinding expected,
        ICharacterFileQueries queries, CancellationToken cancellationToken)
    {
        var decoded = Decode(detachedBytes, WorkspacePrivateRuleSourceClosureBinding.MaximumBytes, cancellationToken);
        using var recovery = new ObservationRecovery(decoded.Captures, decoded.Campaigns, queries);
        if (decoded.Binding != expected
            || !WorkspacePrivateRuleSourceClosureCompletion.ValidateHistoryObservation(decoded.Snapshot, recovery))
            throw Rejected();
        cancellationToken.ThrowIfCancellationRequested();
        return decoded;
    }

    internal static WorkspacePrivateRuleSourceClosure Create(WorkspaceContinuationSnapshot snapshot,
        IReadOnlyList<byte[]> archives, IReadOnlyList<CharacterCreationCommittedCampaign> campaigns, int maximumBytes,
        CancellationToken cancellationToken, WorkspaceCurrentRuleEnvironmentBinding? currentEnvironment = null,
        WorkspaceAuthenticatedHistory? authenticatedHistory = null)
    {
        CheckBound(maximumBytes);
        if (archives.Count > WorkspacePrivateRuleSourceClosureBinding.MaximumArchives
            || campaigns.Count > WorkspacePrivateRuleSourceClosureBinding.MaximumCampaigns) throw Rejected();
        byte[] continuation = WorkspaceContinuationCodec.Encode(new(snapshot, WorkspaceContinuationSnapshotDigest.Compute(snapshot)),
            Math.Min(maximumBytes, WorkspacePrivateRuleSourceClosureBinding.MaximumContinuationBytes));
        var ordered = archives.Select(bytes => (Digest: RetainedCharacterRuleSourceInputs.Digest(bytes), Bytes: bytes))
            .OrderBy(row => row.Digest, StringComparer.Ordinal).ToArray();
        byte[] campaignBytes = JsonSerializer.SerializeToUtf8Bytes(
            campaigns.OrderBy(c => c.ReceiptDigest, StringComparer.Ordinal).ToArray(), CampaignOptions);
        string schema = currentEnvironment is null ? WorkspacePrivateRuleSourceClosureBinding.SchemaV1
            : WorkspacePrivateRuleSourceClosureBinding.SchemaV2;
        byte[]? environmentBytes = currentEnvironment is null ? null
            : JsonSerializer.SerializeToUtf8Bytes(currentEnvironment, CampaignOptions);
        // Optional versioned private history extension. Old V1/V2 bytes remain readable
        // and carry no authenticated outcome. Keys and enrollment never enter this block.
        byte[]? historyBytes = authenticatedHistory is null ? null : JsonSerializer.SerializeToUtf8Bytes(authenticatedHistory, CampaignOptions);
        if (historyBytes is not null && (currentEnvironment is null || historyBytes.Length > WorkspaceAuthenticatedHistory.MaximumBytes)) throw Rejected();
        // Exact lengths are checked before the combined buffer is allocated.
        long length = 4L + Utf8.GetByteCount(schema)
            + 4L + continuation.Length + 4L + 4L + campaignBytes.Length;
        if (environmentBytes is not null) length = checked(length + 4L + environmentBytes.Length);
        if (historyBytes is not null) length = checked(length + 4L + historyBytes.Length);
        foreach (var row in ordered) length = checked(length + 4L + Utf8.GetByteCount(row.Digest) + 4L + row.Bytes.Length);
        if (length > maximumBytes || campaignBytes.Length > MaximumCampaignBytes
            || environmentBytes?.Length > MaximumEnvironmentBytes) throw Rejected();
        using var stream = new MemoryStream(checked((int)length));
        using (var writer = new BinaryWriter(stream, Utf8, leaveOpen: true))
        {
            WriteBlock(writer, Utf8.GetBytes(schema));
            WriteBlock(writer, continuation);
            writer.Write(ordered.Length);
            foreach (var row in ordered) { WriteBlock(writer, Utf8.GetBytes(row.Digest)); WriteBlock(writer, row.Bytes); }
            WriteBlock(writer, campaignBytes);
            if (environmentBytes is not null) WriteBlock(writer, environmentBytes);
            if (historyBytes is not null) WriteBlock(writer, historyBytes);
        }
        byte[] result = stream.ToArray();
        var decoded = Decode(result, maximumBytes, cancellationToken);
        return new(result, decoded.Binding);
    }

    private const int MaximumCampaignBytes = 1024 * 1024;
    private const int MaximumEnvironmentBytes = 8 * 1024 * 1024;

    private static Decoded Decode(byte[] bytes, int maximumBytes, CancellationToken cancellationToken = default)
    {
        CheckBound(maximumBytes);
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length == 0 || bytes.Length > maximumBytes) throw Rejected();
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Utf8);
        string schema = Utf8.GetString(ReadBlock(reader, 128));
        if (schema is not (WorkspacePrivateRuleSourceClosureBinding.SchemaV1
            or WorkspacePrivateRuleSourceClosureBinding.SchemaV2)) throw Rejected();
        byte[] continuation = ReadBlock(reader, WorkspacePrivateRuleSourceClosureBinding.MaximumContinuationBytes);
        if (!WorkspaceContinuationCodec.TryDecodeCandidate(continuation, continuation.Length, out var candidate)) throw Rejected();
        // Canonical encoding excludes duplicate properties and alternate wire identities.
        if (!WorkspaceContinuationCodec.Encode(candidate, continuation.Length).AsSpan().SequenceEqual(continuation)) throw Rejected();
        // Interpret only the strictly decoded, canonically identical DTO graph.
        // Import still requires semantic history observation; export holds the store/owner leases.
        ValidatePrivateText(continuation, model: candidate);
        int count = ReadCount(reader, WorkspacePrivateRuleSourceClosureBinding.MaximumArchives);
        var captures = new Dictionary<string, CharacterCreationRuleSourceCapture>(StringComparer.Ordinal);
        string? previous = null;
        long archiveBytes = 0;
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string digest = Utf8.GetString(ReadBlock(reader, 71));
            if (previous is not null && StringComparer.Ordinal.Compare(previous, digest) >= 0) throw Rejected();
            previous = digest;
            byte[] archive = ReadBlock(reader, RetainedCharacterRuleSourceInputs.MaximumArchiveBytes);
            archiveBytes = checked(archiveBytes + archive.Length);
            var inputs = FileSystemCharacterSourceDataResolver.ImportCaptureInputs(archive, digest);
            // Deliberate old-format refusal. No path stripping or receipt rewriting on import.
            if (!inputs.IsPortable) throw new WorkspacePrivateRuleSourceClosureLegacyFormatException();
            if (!FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(archive, digest, out var capture)
                || capture is null || capture.IntegrityDigest != digest || !captures.TryAdd(digest, capture)) throw Rejected();
        }
        byte[] campaignBytes = ReadBlock(reader, MaximumCampaignBytes);
        ValidatePrivateText(campaignBytes, WorkspacePrivateRuleSourceClosureBinding.MaximumCampaigns);
        WorkspaceCurrentRuleEnvironmentBinding? environment = null;
        byte[]? environmentBytes = null;
        if (schema == WorkspacePrivateRuleSourceClosureBinding.SchemaV2)
        {
            environmentBytes = ReadBlock(reader, MaximumEnvironmentBytes);
            ValidatePrivateText(environmentBytes);
            environment = JsonSerializer.Deserialize<WorkspaceCurrentRuleEnvironmentBinding>(environmentBytes, CampaignOptions)
                ?? throw Rejected();
            if (!captures.TryGetValue(environment.CaptureDigest, out var current)) throw Rejected();
            // Interpretation identifiers remain untrusted observations until independent
            // canonical admission matches them. Never rewrite them to the receiver's version.
            var canonical = DescribeCurrentEnvironment(candidate.Snapshot, current,
                environment.ExecutionProfile, environment.ExecutionProfileFingerprint, environment.AdmissionDiagnostics);
            // Regenerate the entire ordered inventory from original retained inputs.
            // Mutable DTO equality or a caller-supplied digest is insufficient.
            if (!JsonSerializer.SerializeToUtf8Bytes(canonical, CampaignOptions).AsSpan().SequenceEqual(environmentBytes))
                throw Rejected();
        }
        WorkspaceAuthenticatedHistory? authenticatedHistory = null;
        if (environment is not null && stream.Position < stream.Length)
        {
            byte[] historyBytes = ReadBlock(reader, WorkspaceAuthenticatedHistory.MaximumBytes);
            authenticatedHistory = JsonSerializer.Deserialize<WorkspaceAuthenticatedHistory>(historyBytes, CampaignOptions);
            if (authenticatedHistory?.Transitions is not { Length: > 0 } transitions
                || transitions.Length > WorkspaceAuthenticatedHistory.MaximumTransitions
                || transitions.Any(row => row?.Observation is null || row.Observation.Version != 1)
                || !JsonSerializer.SerializeToUtf8Bytes(authenticatedHistory, CampaignOptions).AsSpan().SequenceEqual(historyBytes)) throw Rejected();
        }
        if (stream.Position != stream.Length) throw Rejected();
        var campaigns = JsonSerializer.Deserialize<CharacterCreationCommittedCampaign[]>(campaignBytes, CampaignOptions);
        if (campaigns is null || campaigns.Length > WorkspacePrivateRuleSourceClosureBinding.MaximumCampaigns
            || campaigns.Any(c => c is null || !CharacterCreationCampaignRecovery.IsWellFormed(c))
            || !campaigns.Select(c => c.ReceiptDigest).SequenceEqual(
                campaigns.Select(c => c.ReceiptDigest).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || !JsonSerializer.SerializeToUtf8Bytes(campaigns, CampaignOptions).AsSpan().SequenceEqual(campaignBytes))
            throw Rejected();
        FileWorkspaceStore.ValidatePrivateClosureBindings(candidate.Snapshot, captures.Keys, campaigns, environment?.CaptureDigest);
        var workspace = candidate.Snapshot.Workspace;
        var binding = new WorkspacePrivateRuleSourceClosureBinding(schema,
            candidate.Snapshot.OwnerId, workspace.Id, workspace.ContentRevision, workspace.SavedRevision,
            candidate.SnapshotDigest, RetainedCharacterRuleSourceInputs.Digest(bytes), count, archiveBytes, campaigns.Length)
        {
            CurrentEnvironmentDigest = environmentBytes is null ? null : RetainedCharacterRuleSourceInputs.Digest(environmentBytes)
        };
        return new(candidate.Snapshot, continuation, captures, campaigns, binding, environment, authenticatedHistory);
    }

    internal sealed record Decoded(WorkspaceContinuationSnapshot Snapshot, byte[] Continuation, Dictionary<string, CharacterCreationRuleSourceCapture> Captures,
        CharacterCreationCommittedCampaign[] Campaigns, WorkspacePrivateRuleSourceClosureBinding Binding,
        WorkspaceCurrentRuleEnvironmentBinding? CurrentEnvironment, WorkspaceAuthenticatedHistory? AuthenticatedHistory = null);

    internal static WorkspaceCurrentRuleEnvironmentBinding DescribeCurrentEnvironment(
        WorkspaceContinuationSnapshot snapshot, CharacterCreationRuleSourceCapture capture,
        string? executionProfile = null, string? executionProfileFingerprint = null,
        IReadOnlyList<string>? admissionDiagnostics = null)
    {
        var workspace = snapshot.Workspace;
        byte[] archive = capture.ExportPrivateInputs();
        var inputs = FileSystemCharacterSourceDataResolver.ImportCaptureInputs(archive, capture.IntegrityDigest);
        if (!inputs.IsPortable || !capture.MatchesOriginalCharacter(workspace.Document.Content)
            || inputs.CharacterXml != workspace.Document.Content
            || !Chummer5aLegacyImportCompatibility.TryGetInterpretation(capture.CompatibilityProfile,
                out var resolverSemantics, out var definitionFingerprint)
            || string.IsNullOrWhiteSpace(capture.RequiredPackageSetDigest)
            || string.IsNullOrWhiteSpace(inputs.ProfileId)
            || capture.RequiredAssets.Count == 0
            || (executionProfile is null) != (executionProfileFingerprint is null)
            || executionProfile is not null && (string.IsNullOrWhiteSpace(executionProfile)
                || string.IsNullOrWhiteSpace(executionProfileFingerprint))) throw Rejected();
        Chummer.Contracts.Characters.CharacterRetainedSettingsFile? settingsFile = null;
        if (Chummer.Contracts.Characters.CharacterRetainedSettingsFile.IsFileSelector(inputs.ProfileId))
        {
            var source = capture.CreateReplayResolver().TryCreateContext(workspace.Document.Content);
            if (source is null || !source.TryResolveCreationSourceProfile(out var profile)
                || profile.RetainedSettingsFile is not { } retainedFile
                || !retainedFile.IsSelectedBy(inputs.ProfileId)) throw Rejected();
            settingsFile = retainedFile;
        }
        return new(WorkspaceCurrentRuleEnvironmentBinding.SchemaV1, snapshot.OwnerId, workspace.Id,
            workspace.ContentRevision, workspace.SavedRevision,
            RetainedCharacterRuleSourceInputs.Digest(Utf8.GetBytes(workspace.Document.Content)),
            capture.IntegrityDigest, capture.EffectivePolicyDigest, inputs.ProfileId,
            capture.CompatibilityProfile!, resolverSemantics,
            Chummer5aLegacyImportCompatibility.SourceOrderV1, capture.RequiredPackageSetDigest,
            capture.RequiredRulePacks.ToArray(), capture.RequiredAssets.ToArray(), capture.OrderedCustomSources.ToArray())
        {
            CompatibilityDefinitionFingerprint = definitionFingerprint,
            ExecutionProfile = executionProfile,
            ExecutionProfileFingerprint = executionProfileFingerprint,
            AdmissionDiagnostics = admissionDiagnostics?.ToArray() ?? [],
            RetainedSettingsFile = settingsFile
        };
    }

    private static void ValidatePrivateText(byte[] json, int? maximumArrayEntries = null, object? model = null)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 128 });
        if (maximumArrayEntries is { } limit && (document.RootElement.ValueKind != JsonValueKind.Array
            || document.RootElement.GetArrayLength() > limit)) throw Rejected();
        Visit(document.RootElement, model);
        static void Visit(JsonElement value, object? model)
        {
            if (value.ValueKind == JsonValueKind.String)
                RetainedCharacterRuleSourceInputs.ValidatePortableTransferDocumentText(value.GetString()!);
            else if (value.ValueKind == JsonValueKind.Array)
            {
                var models = (model as System.Collections.IEnumerable)?.GetEnumerator();
                try
                {
                    foreach (var child in value.EnumerateArray())
                        Visit(child, models?.MoveNext() == true ? models.Current : null);
                }
                finally { (models as IDisposable)?.Dispose(); }
            }
            else if (value.ValueKind == JsonValueKind.Object)
                foreach (var property in value.EnumerateObject())
                {
                    RetainedCharacterRuleSourceInputs.ValidatePortableTransferText(property.Name);
                    var member = model?.GetType().GetProperty(property.Name,
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                    object? childModel = member is not null && member.GetIndexParameters().Length == 0
                        ? member.GetValue(model) : null;
                    if (property.Value.ValueKind == JsonValueKind.String
                        && childModel is string sourceXml && property.Value.GetString() == sourceXml
                        && WorkspacePrivateRuleSourceText.TryValidate(model, property.Name, sourceXml))
                        continue;
                    // A field name or a JSON-shaped object cannot create this scope: the parent
                    // must be an actual canonical contract instance, reached from the decoded
                    // continuation. No scope flows into prose, XML, dictionaries or campaigns.
                    if (property.Name == nameof(CharacterCreationKnowledgeCategoryEffect.Condition)
                        && property.Value.ValueKind == JsonValueKind.String
                        && childModel is string condition && property.Value.GetString() == condition
                        && model is CharacterCreationKnowledgeCategoryEffect or CharacterCreationSkillKarmaEffect
                        && CharacterCreationKnowledgeCategoryRules.TryLowerNativeCondition(condition, out _))
                        continue;
                    Visit(property.Value, childModel);
                }
        }
    }

    internal sealed class ObservationRecovery(Dictionary<string, CharacterCreationRuleSourceCapture> captures,
        CharacterCreationCommittedCampaign[] campaigns, ICharacterFileQueries queries)
        : ICharacterCreationRuleSourceRecovery, IDisposable
    {
        private bool _active = true;
        public bool ValidateRetainedRuleSources() => _active;
        public bool TryRecoverRuleSources(string digest, out CharacterCreationRuleSourceExecutionContext? execution)
        {
            execution = null;
            if (!_active || !captures.TryGetValue(digest, out var capture)) return false;
            execution = new(capture, queries);
            return true;
        }
        public bool TryRecoverCampaignAuthority(CharacterCreationCampaignBinding binding, string receiptDigest,
            out CharacterCreationCampaignExecutionContext? execution)
        {
            execution = null;
            var committed = campaigns.SingleOrDefault(c => c.ReceiptDigest == receiptDigest);
            return _active && committed is not null && CharacterCreationCampaignRecovery.FromCommitted(committed, this)
                .TryRecoverCampaignAuthority(binding, receiptDigest, out execution);
        }
        public void Dispose() => _active = false;
        public override string ToString() => nameof(ObservationRecovery);
    }

    private static void CheckBound(int maximumBytes)
    {
        if (maximumBytes is <= 0 or > WorkspacePrivateRuleSourceClosureBinding.MaximumBytes) throw Rejected();
    }
    private static int ReadCount(BinaryReader reader, int maximum)
    {
        int value = reader.ReadInt32();
        return value >= 0 && value <= maximum ? value : throw Rejected();
    }
    private static byte[] ReadBlock(BinaryReader reader, int maximum)
    {
        int count = ReadCount(reader, maximum);
        if (reader.BaseStream.Length - reader.BaseStream.Position < count) throw Rejected();
        byte[] result = reader.ReadBytes(count);
        return result.Length == count ? result : throw Rejected();
    }
    private static void WriteBlock(BinaryWriter writer, byte[] bytes) { writer.Write(bytes.Length); writer.Write(bytes); }
    internal static bool IsFailure(Exception e) => e is not WorkspacePrivateRuleSourceClosureLegacyFormatException
        && e is (IOException or JsonException or ArgumentException
        or InvalidOperationException or OverflowException or System.Xml.XmlException or UnauthorizedAccessException
        or NullReferenceException or KeyNotFoundException or IndexOutOfRangeException or NotSupportedException);
    internal static InvalidOperationException Rejected() => new("Private source closure is unavailable or invalid.");
}
