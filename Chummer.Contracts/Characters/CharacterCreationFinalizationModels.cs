using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationFinalizationSchemas
{
    public const string StateV1 = "chummer.sr5.creation-finalization.state.v1";
    public const string ReviewV1 = "chummer.sr5.creation-finalization.review.v1";
    public const string PlanV1 = "chummer.sr5.creation-finalization.plan.v1";
    public const string ReceiptV1 = "chummer.sr5.creation-finalization.receipt.v1";
}

public static class CharacterCreationFinalizationOutcomes
{
    public const string Available = "available";
    public const string Applied = "applied";
    public const string Replayed = "replayed";
    public const string NotFound = "not-found";
    public const string Blocked = "blocked";
    public const string Conflict = "conflict";
    public const string Invalid = "invalid";
    public const string Corrupt = "corrupt";
    public const string Unavailable = "unavailable";
}

public static class CharacterCreationFinalizationBlockers
{
    public const string WorkspaceUnavailable = "creation-finalization-workspace-unavailable";
    public const string RulesetSr5Required = "creation-finalization-ruleset-sr5-required";
    public const string CharacterAlreadyCreated = "creation-finalization-character-already-created";
    public const string BuildMethodUnsupported = "creation-finalization-build-method-unsupported";
    public const string BuildMethodNotReady = "creation-finalization-build-method-not-ready";
    public const string BootstrapBindingRequired = "creation-finalization-bootstrap-binding-required";
    public const string PrerequisiteDraftRequired = "creation-finalization-prerequisite-draft-required";
    public const string AttributesDraftRequired = "creation-finalization-attributes-draft-required";
    public const string SkillsDraftRequired = "creation-finalization-skills-draft-required";
    public const string MagicResonanceDraftRequired = "creation-finalization-magic-resonance-draft-required";
    public const string QualitiesDraftRequired = "creation-finalization-qualities-draft-required";
    public const string ResourcesDraftRequired = "creation-finalization-resources-draft-required";
    public const string GearDraftRequired = "creation-finalization-gear-draft-required";
    public const string FoundationDraftNotApplicable = "creation-finalization-foundation-draft-not-applicable";
    public const string LifeModuleDecisionHistoryNotApplicable =
        "creation-finalization-life-module-decision-history-not-applicable";
    public const string DraftAuthorityInvalid = "creation-finalization-draft-authority-invalid";
    public const string QualityEffectsNotProjectable = "creation-finalization-quality-effects-not-projectable";
    public const string GearEffectsNotProjectable = "creation-finalization-gear-effects-not-projectable";
    public const string AwakenedEffectsNotProjectable = "creation-finalization-awakened-effects-not-projectable";
    public const string TalentGrantsNotProjectable = "creation-finalization-talent-grants-not-projectable";
    public const string GlobalKarmaExceeded = "creation-finalization-global-karma-exceeded";
    public const string CarryoverPolicyUnavailable = "creation-finalization-carryover-policy-unavailable";
    public const string StartingCashUnavailable = "creation-finalization-starting-cash-unavailable";
    public const string StartingCashChoiceRequired = "creation-finalization-starting-cash-choice-required";
    public const string StartingCashChoiceInvalid = "creation-finalization-starting-cash-choice-invalid";
    public const string StaleWorkspaceRevision = "creation-finalization-stale-workspace-revision";
    public const string StaleRawCharacterXmlDigest = "creation-finalization-stale-character-digest";
    public const string StaleAuxiliaryStateDigest = "creation-finalization-stale-auxiliary-digest";
    public const string AuthorityDigestMismatch = "creation-finalization-authority-digest-mismatch";
    public const string PreviewDigestMismatch = "creation-finalization-preview-digest-mismatch";
    public const string PlanDigestMismatch = "creation-finalization-plan-digest-mismatch";
    public const string ExplicitConfirmationRequired = "creation-finalization-explicit-confirmation-required";
    public const string IdempotencyKeyInvalid = "creation-finalization-idempotency-key-invalid";
    public const string IdempotencyConflict = "creation-finalization-idempotency-conflict";
    public const string AtomicPersistenceRequired = "creation-finalization-atomic-persistence-required";
    public const string AtomicPersistenceRejected = "creation-finalization-atomic-persistence-rejected";
    public const string PostCommitReopenRequired = "creation-finalization-post-commit-reopen-required";
}

public static class CharacterCreationFinalizationDeltaKinds
{
    public const string Build = "build";
    public const string Metatype = "metatype";
    public const string Attribute = "attribute";
    public const string Skill = "skill";
    public const string SkillGroup = "skill-group";
    public const string Quality = "quality";
    public const string MagicResonance = "magic-resonance";
    public const string Gear = "gear";
    public const string Resources = "resources";
    public const string Lifecycle = "lifecycle";
}

public sealed record CharacterCreationFinalizationBinding(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string RawCharacterXmlDigest,
    string AuxiliaryStateDigest,
    string BuildMethod,
    string AuthorityDigest);

public sealed record CharacterCreationFinalizationLoadRequest(CharacterWorkspaceId WorkspaceId);

public sealed record CharacterCreationFinalizationReviewRequest(
    CharacterCreationFinalizationBinding Binding)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationStartingCashChoice? StartingCash { get; init; }
}

public sealed record CharacterCreationFinalizationConfirmRequest(
    CharacterCreationFinalizationBinding Binding,
    string PreviewDigest,
    string PlanDigest,
    string IdempotencyKey,
    bool ExplicitlyConfirmed)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationStartingCashChoice? StartingCash { get; init; }
}

/// <summary>User-entered dice result bound to the exact displayed source, never an amount override.</summary>
public sealed record CharacterCreationStartingCashChoice(string SourceAuthorityDigest, int DiceTotal);

/// <summary>Core-captured source inputs retained for atomic transition validation, not a client write grant.</summary>
public sealed record CharacterCreationFinalizationStartingCash(
    CharacterCreationStartingNuyenSource Source,
    CharacterCreationLifestylesAuthority Lifestyles,
    CharacterCreationStartingCashChoice Choice,
    string AuthorityDigest);

public sealed record CharacterCreationFinalizationReceiptLookupRequest(
    CharacterWorkspaceId WorkspaceId,
    string IdempotencyKey);

public sealed record CharacterCreationFinalizationStep(
    string StepId,
    bool IsRequired,
    bool IsComplete,
    string? DraftDigest,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationFinalizationState(
    string Schema,
    CharacterCreationFinalizationBinding Binding,
    bool CharacterCreated,
    IReadOnlyList<CharacterCreationFinalizationStep> Steps,
    IReadOnlyList<string> Blockers,
    bool CanReview,
    CharacterCreationFinalizationReceipt? LastReceipt,
    string SnapshotDigest)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationStartingNuyenSource? StartingCashSource { get; init; }

    /// <summary>Only saved lifestyle tiers, preferred expected cash first. Null retains historical Street-only state.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CharacterCreationStartingNuyenSource>? StartingCashSources { get; init; }
}

public sealed record CharacterCreationFinalizationDelta(
    int Order,
    string DeltaId,
    string Kind,
    string TargetId,
    string? BeforeValue,
    string? AfterValue,
    decimal KarmaCost,
    decimal NuyenCost,
    IReadOnlyList<string> SourceAnchorIds)
{
    /// <summary>The canonical name from the admitted draft/source, bound into
    /// the review digest. Not an identity or a client-side catalog lookup.
    /// Omitted on historical deltas to preserve their serialized bytes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetName { get; init; }
}

/// <summary>
/// One sealed, whole-build write plan.  Every ordered delta is reviewable; the
/// replacement XML is deliberately not exposed to clients.  It is regenerated
/// from the persisted typed drafts immediately before the atomic CAS.
/// </summary>
public sealed record CharacterCreationFinalizationPlan(
    string Schema,
    CharacterCreationFinalizationBinding Binding,
    IReadOnlyList<CharacterCreationFinalizationDelta> OrderedDeltas,
    decimal KarmaRemaining,
    decimal StartingNuyen,
    decimal NuyenRemaining,
    IReadOnlyList<string> SourceAnchorIds,
    string ExpectedResultRawCharacterXmlDigest,
    string PlanDigest)
{
    /// <summary>Exact source-profile policy used by this review, never a client override.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationKarmaCarryoverPolicy? CarryoverPolicy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationStartingCashChoice? StartingCash { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StartingCashAuthorityDigest { get; init; }
}

public sealed record CharacterCreationFinalizationReview(
    string Schema,
    CharacterCreationFinalizationBinding Binding,
    CharacterCreationFinalizationPlan? Plan,
    IReadOnlyList<CharacterCreationFinalizationDelta> OrderedDeltas,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds,
    bool RequiresExplicitConfirmation,
    bool CanConfirm,
    string PreviewDigest);

public sealed record CharacterCreationFinalizationReceipt(
    string Schema,
    string ReceiptId,
    CharacterWorkspaceId WorkspaceId,
    string IdempotencyKeyDigest,
    string CommandDigest,
    long PreviousContentRevision,
    long ContentRevision,
    long PreviousSavedRevision,
    long SavedRevision,
    string PreviousRawCharacterXmlDigest,
    string RawCharacterXmlDigest,
    string PreviousAuxiliaryStateDigest,
    string AuthorityDigest,
    string PreviewDigest,
    string PlanDigest,
    string BuildMethod,
    bool CharacterCreated,
    bool RequiresFreshCareerReopen,
    string PreviousReceiptDigest,
    string ReceiptDigest)
{
    // Absent on historical receipts. Omitting null preserves their original
    // digest and allows read-only recovery without retroactive balance changes.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationKarmaCarryoverPolicy? CarryoverPolicy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationStartingCashChoice? StartingCash { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StartingCashAuthorityDigest { get; init; }

    /// <summary>Identifies the completion proposal lane, whose reconstruction
    /// archive is mandatory. Omitted for historical ordinary finalizations.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CompletionAuthorityDigest { get; init; }
}

public sealed record CharacterCreationFinalizationReceiptLedgerEntry(
    string IdempotencyKeyDigest,
    string CommandDigest,
    CharacterCreationFinalizationReceipt Receipt);

public sealed record CharacterCreationFinalizationResult<T>(
    string Outcome,
    T? Value,
    IReadOnlyList<string> Blockers)
    where T : class
{
    public bool Success => Outcome is CharacterCreationFinalizationOutcomes.Available
        or CharacterCreationFinalizationOutcomes.Applied
        or CharacterCreationFinalizationOutcomes.Replayed;
}

// Temporary diagnostic, armed only by the bounded startup-shell observer. This
// synchronous scope never flows to tasks and holds no character or source data.
public static class CharacterCreationValidationTiming8991
{
    public const string ArmProperty = "chummer.validation-predecode8991";
    private static readonly System.Diagnostics.ActivitySource Activities = new("Chummer.Startup.Shell8910");
    [ThreadStatic] private static Scope? current;

    public enum Bucket
    {
        IntegrityPrelude, DraftPrelude, ReceiptDigest, BaselineDigest, CompositeReviewDigest,
        CommandDigest, ReceiptIdentity, DraftReviewDigest, PriorStateDigest, PrefixDigest
    }

    public static string Name(Bucket bucket) => bucket switch
    {
        Bucket.IntegrityPrelude => "validation-integrity-prelude",
        Bucket.DraftPrelude => "validation-draft-prelude",
        Bucket.ReceiptDigest => "validation-receipt-digest",
        Bucket.BaselineDigest => "validation-baseline-digest",
        Bucket.CompositeReviewDigest => "validation-composite-review-digest",
        Bucket.CommandDigest => "validation-command-digest",
        Bucket.ReceiptIdentity => "validation-receipt-identity",
        Bucket.DraftReviewDigest => "validation-draft-review-digest",
        Bucket.PriorStateDigest => "validation-prior-state-digest",
        Bucket.PrefixDigest => "validation-prefix-digest",
        _ => string.Empty
    };

    public static bool IsBucketName(string name) => name is
        "validation-integrity-prelude" or "validation-draft-prelude" or "validation-receipt-digest"
        or "validation-baseline-digest" or "validation-composite-review-digest" or "validation-command-digest"
        or "validation-receipt-identity" or "validation-draft-review-digest" or "validation-prior-state-digest"
        or "validation-prefix-digest";

    public static bool IsUnenteredName(string name) => name.EndsWith("-unentered", StringComparison.Ordinal)
        && IsBucketName(name[..^10]);

    public static Scope? Begin(System.Diagnostics.Activity? auxiliary)
    {
        if (current is not null || auxiliary?.GetCustomProperty(ArmProperty) is not true) return null;
        return current = new Scope();
    }

    public static Measurement? BeginBucket(Bucket bucket) => current?.BeginBucket(bucket);

    public static T Measure<T>(Bucket bucket, Func<T> action)
    {
        using var measurement = BeginBucket(bucket);
        T result = action();
        measurement?.Complete();
        return result;
    }

    public static void BeforeFirstDecode()
    {
        if (current is not { } scope) return;
        scope.ReachedDecode = true;
        scope.Dispose();
    }

    internal static DigestMeasurement? BeginDefaultDigest()
    {
        var bucket = current?.ActiveBucket;
        // Nested Compute calls during materialization belong to the outer
        // serialization interval; do not double-count their inclusive times.
        if (bucket is null || !bucket.IsDigest || bucket.DigestActive) return null;
        return new DigestMeasurement(bucket);
    }

    private static System.Diagnostics.Activity? Start(string name)
    {
        try { return Activities.StartActivity(name); }
        catch { return null; } // Observer failures must not alter validation.
    }

    private static void Stop(System.Diagnostics.Activity? activity, params (string Name, long Value)[] counts)
    {
        try
        {
            if (activity is null) return;
            foreach (var (name, value) in counts) activity.SetTag(name, value);
            activity.Dispose();
        }
        catch { } // Never expose exception text or change reader acceptance.
    }

    private static long Milliseconds(long ticks) => (long)Math.Round(
        ticks * 1000d / System.Diagnostics.Stopwatch.Frequency, MidpointRounding.AwayFromZero);

    public sealed class Scope : IDisposable
    {
        private readonly bool[] entered = new bool[10];
        private readonly System.Diagnostics.Activity? activity = Start("validation-predecode");
        private bool disposed;
        internal Measurement? ActiveBucket;
        internal bool ReachedDecode;

        internal Measurement BeginBucket(Bucket bucket)
        {
            entered[(int)bucket] = true;
            return new Measurement(this, bucket);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            current = null;
            // Missing buckets are explicitly unentered. A completed scope only
            // means the first decode boundary was reached, never valid data/UI.
            for (int i = 0; i < entered.Length; i++)
                if (!entered[i]) Stop(Start(Name((Bucket)i) + "-unentered"));
            Stop(activity, ("entered-count", 1), ("completed-count", ReachedDecode ? 1 : 0));
        }
    }

    public sealed class Measurement : IDisposable
    {
        private readonly Scope scope;
        private readonly Measurement? previous;
        private readonly System.Diagnostics.Activity? activity;
        private bool complete, disposed;
        internal readonly bool IsDigest;
        internal bool DigestActive;
        internal long SerializationTicks, HashTicks, SerializationCount, SerializationCompleted, HashCount, HashCompleted;

        internal Measurement(Scope scope, Bucket bucket)
        {
            this.scope = scope;
            previous = scope.ActiveBucket;
            scope.ActiveBucket = this;
            IsDigest = (int)bucket >= (int)Bucket.ReceiptDigest;
            activity = Start(Name(bucket));
        }

        public void Complete() { complete = true; Dispose(); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            scope.ActiveBucket = previous;
            Stop(activity, ("entered-count", 1), ("completed-count", complete ? 1 : 0),
                ("serialization-count", SerializationCount), ("serialization-completed-count", SerializationCompleted),
                ("serialization-ms", Milliseconds(SerializationTicks)), ("canonical-hash-count", HashCount),
                ("canonical-hash-completed-count", HashCompleted), ("canonical-hash-ms", Milliseconds(HashTicks)));
        }
    }

    internal sealed class DigestMeasurement : IDisposable
    {
        private readonly Measurement bucket;
        private long hashStarted;
        private bool hashing, disposed;
        internal DigestMeasurement(Measurement bucket) { this.bucket = bucket; bucket.DigestActive = true; }

        internal JsonDocument Serialize<T>(T value)
        {
            bucket.SerializationCount++;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                // Includes metadata resolution AND materialization, not a
                // measurement of metadata alone. Same default wire contract.
                JsonDocument document = CharacterCreationFinalizationDigest.CaptureDefaultJson(value);
                bucket.SerializationCompleted++;
                return document;
            }
            finally { bucket.SerializationTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started; }
        }

        internal void BeginHash()
        {
            bucket.HashCount++;
            hashing = true;
            hashStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        internal void CompleteHash()
        {
            bucket.HashCompleted++;
            EndHash();
        }

        internal void PauseHash() => EndHash();

        internal void ResumeHash()
        {
            if (hashing) return;
            hashing = true;
            hashStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        private void EndHash()
        {
            if (!hashing) return;
            bucket.HashTicks += System.Diagnostics.Stopwatch.GetTimestamp() - hashStarted;
            hashing = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            EndHash();
            bucket.DigestActive = false;
        }
    }
}

public static class CharacterCreationFinalizationDigest
{
    private const string Prefix = "sha256:";

    public static string ReceiptLedgerRootDigest { get; } =
        ComputeUtf8("chummer.sr5.creation-finalization.receipt-ledger.root.v1");

    public static string Compute<T>(T value)
    {
        using var timing8991 = CharacterCreationValidationTiming8991.BeginDefaultDigest();
        // Finalized runners replay these hashes during ordinary roster reads.
        // Keep the exact v1 bytes, but avoid retaining a second complete JSON
        // graph and output buffer on every archived authority/receipt check.
        using var output = new CanonicalHashBufferWriter();
        using (var writer = new Utf8JsonWriter(output))
        {
            if (typeof(T) == typeof(string))
            {
                // A string has no properties to order. The default serializer and
                // canonical scalar writer use this same escaping, including null
                // and replacement of unpaired surrogates. Keep the declared-type
                // guard: a string passed as IEnumerable<char> has a different wire
                // contract. Do not serialize/parse/escape large source XML twice.
                timing8991?.BeginHash();
                writer.WriteStringValue((string?)(object?)value);
            }
            else
            {
                using JsonDocument document = timing8991 is null
                    ? CaptureDefaultJson(value) : timing8991.Serialize(value);
                timing8991?.BeginHash();
                WriteCanonical(document.RootElement, writer, new CanonicalPreparation3273(), 0);
                timing8991?.PauseHash(); // Exclude JsonDocument disposal from canonical hashing.
            }
            timing8991?.ResumeHash(); // Include writer flush/disposal and GetDigest below.
        }
        string digest8991 = Prefix + output.GetDigest();
        timing8991?.CompleteHash();
        return digest8991;
    }

    // Baseline, prefix and composite review validation otherwise rebuild the
    // auxiliary graph's reflection contracts after the workspace reader resolved
    // its generated default-wire contracts. Reuse only metadata and writers.
    // Keep the declared-type guard: object/interface roots retain their default
    // polymorphism. Every call still captures the complete current graph.
    // Resolve inside Serialize's measured region, including a cold metadata load.
    internal static JsonDocument CaptureDefaultJson<T>(T value)
        => WorkspaceDocumentAuxiliaryStateJsonConverter.CreationInputTypeInfo<T>() is { } inputTypeInfo
            ? JsonSerializer.SerializeToDocument(value, inputTypeInfo)
            : typeof(T) == typeof(CharacterCreationCompletionDraftBaseline)
            || typeof(T) == typeof(CharacterCreationCompletionDraftPrefix)
            || typeof(T) == typeof(CharacterCreationCompletionDraftCommandDigest)
            || typeof(T) == typeof(CharacterCreationCompletionDraftReview)
            || typeof(T) == typeof(CharacterCreationCompletionDraftReceipt)
            || typeof(T) == typeof(CharacterCreationCompletionReview)
            ? JsonSerializer.SerializeToDocument(value,
                (JsonTypeInfo<T>)WorkspaceDocumentAuxiliaryStateJsonConverter.TypeInfo.Options.GetTypeInfo(typeof(T)))
            : JsonSerializer.SerializeToDocument(value);

    // The caller supplies the same wire contract as the default serializer.
    // Reuse metadata only: every call captures and hashes the complete value.
    public static string Compute<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        using JsonDocument document = JsonSerializer.SerializeToDocument(value, typeInfo);
        using var output = new CanonicalHashBufferWriter();
        using (var writer = new Utf8JsonWriter(output))
        {
            WriteCanonical(document.RootElement, writer, new CanonicalPreparation3273(), 0);
        }
        return Prefix + output.GetDigest();
    }

    // Only the fixed { Schema: string, Xml: string } envelope uses this path.
    // These names are already in ordinal order. Write the same default-escaped
    // scalar bytes as Compute, without serializing, parsing and rewriting XML.
    // No authority, parsed source, or digest is cached.
    internal static string ComputeSourceXmlEnvelope(string schema, string? xml)
    {
        using var output = new CanonicalHashBufferWriter();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteString("Schema", schema);
            writer.WriteString("Xml", xml);
            writer.WriteEndObject();
        }
        return Prefix + output.GetDigest();
    }

    public static string ComputeUtf8(string value) =>
        Prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string ComputeIdempotencyKeyDigest(string idempotencyKey) =>
        ComputeUtf8(idempotencyKey.Trim());

    public static string ComputeReceiptDigest(CharacterCreationFinalizationReceipt receipt) =>
        Compute(receipt with { ReceiptDigest = string.Empty });

    public static bool IsCanonical(string? value) => value is { Length: 71 }
        && value.StartsWith(Prefix, StringComparison.Ordinal)
        && value.AsSpan(Prefix.Length).ToString().All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static bool EqualsFixedTime(string? left, string? right)
    {
        byte[] a = Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] b = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer,
        CanonicalPreparation3273 preparation, int depth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                WriteCanonicalProperties(element, writer, preparation, depth);
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                    WriteCanonical(item, writer, preparation, depth + 1);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
            case JsonValueKind.Number:
                // Preserve escaping, invalid-Unicode rejection and numeric
                // spelling without allocating decoded scalar text copies.
                element.WriteTo(writer);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException("Unsupported finalization JSON value kind.");
        }

        if (writer.BytesPending >= 64 * 1024)
            writer.Flush();
    }

    private static void WriteCanonicalProperties(JsonElement element, Utf8JsonWriter writer,
        CanonicalPreparation3273 preparation, int depth)
    {
        int count = 0;
        foreach (JsonProperty _ in element.EnumerateObject()) count++;
        if (count == 0) return;
        if (count == 1)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(property.Value, writer, preparation, depth + 1);
            }
            return;
        }

        if (preparation.TryWrite(element, count, writer, depth)) return;
        CanonicalProperty[] properties = ArrayPool<CanonicalProperty>.Shared.Rent(count);
        try
        {
            int index = 0;
            foreach (JsonProperty property in element.EnumerateObject())
            {
                properties[index] = new(property, property.Name, index);
                index++;
            }
            Array.Sort(properties, 0, count, CanonicalPropertyComparer.Instance);
            preparation.Remember(properties, count, depth);
            for (int i = 0; i < count; i++)
            {
                writer.WritePropertyName(properties[i].Name);
                WriteCanonical(properties[i].Property.Value, writer, preparation, depth + 1);
            }
        }
        finally
        {
            // Do not retain character data or disposed document references.
            ArrayPool<CanonicalProperty>.Shared.Return(properties, clearArray: true);
        }
    }

    // One serialization owns this preparation. Only detached property-name/order
    // metadata is shared between sibling objects in its immutable JsonDocument;
    // every value is independently written and hashed, including decimal spelling.
    // No authority, report, source adapter, character or result survives the call.
    private sealed class CanonicalPreparation3273
    {
        private const int MaximumDepth = 16;
        private const int MaximumProperties = 128;
        private const int MaximumNameCharacters = 4096;
        private const int MaximumShapesPerDepth = 4;
        private List<ObjectShape>?[]? shapes;

        internal bool TryWrite(JsonElement element, int count, Utf8JsonWriter writer, int depth)
        {
            if (depth >= MaximumDepth || shapes is null || shapes[depth] is not { } candidates)
                return false;
            foreach (ObjectShape shape in candidates)
                if (count == shape.Names.Length && TryWriteShape(element, count, writer, depth, shape))
                    return true;
            return false;
        }

        private bool TryWriteShape(JsonElement element, int count, Utf8JsonWriter writer,
            int depth, ObjectShape shape)
        {
            try
            {
                int index = 0;
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    // ASCII-only prepared names make this an exact comparison to
                    // the historical decoded UTF-16 name, including JSON escapes.
                    // Foreign/non-ASCII/malformed names take the original decoder.
                    if (!property.NameEquals(shape.Names[index])) return false;
                    shape.Values[index++] = property;
                }
                for (int i = 0; i < count; i++)
                {
                    writer.WritePropertyName(shape.EncodedNames[i]);
                    WriteCanonical(shape.Values[shape.Order[i]].Value, writer, this, depth + 1);
                }
                return true;
            }
            finally
            {
                // Scratch only: do not retain any JsonDocument member, including
                // after a late name mismatch or a descendant encoding exception.
                Array.Clear(shape.Values);
            }
        }

        internal void Remember(CanonicalProperty[] sorted, int count, int depth)
        {
            // These are preparation limits, never input/validation limits. The
            // unmodified cold writer handles larger/deeper/non-ASCII objects.
            // The root occurs once, so preparing its shape cannot help this call.
            if (depth == 0 || depth >= MaximumDepth || count > MaximumProperties
                || shapes?[depth]?.Count >= MaximumShapesPerDepth) return;
            int characters = 0;
            for (int i = 0; i < count; i++)
            {
                string name = sorted[i].Name;
                if (name.Length > MaximumNameCharacters - characters) return;
                characters += name.Length;
                foreach (char character in name)
                    if (character is < ' ' or > '~') return;
            }
            var names = new string[count];
            var order = new int[count];
            var encodedNames = new JsonEncodedText[count];
            for (int i = 0; i < count; i++)
            {
                names[sorted[i].Ordinal] = sorted[i].Name;
                order[i] = sorted[i].Ordinal;
                // Same default encoder as the enclosing Utf8JsonWriter. Stable
                // duplicate keys keep their original ordinal, not a name lookup.
                encodedNames[i] = JsonEncodedText.Encode(sorted[i].Name);
            }
            // Sibling branches alternate between a few object layouts. Retain
            // the first bounded set rather than allocating a replacement on
            // every switch. Excess layouts use the unchanged cold writer.
            // Values remain fresh and scratch members are cleared after use.
            var candidates = (shapes ??= new List<ObjectShape>?[MaximumDepth])[depth]
                ??= new List<ObjectShape>(MaximumShapesPerDepth);
            candidates.Add(new(names, order, encodedNames));
        }

        private sealed class ObjectShape(string[] names, int[] order, JsonEncodedText[] encodedNames)
        {
            internal string[] Names { get; } = names;
            internal int[] Order { get; } = order;
            internal JsonEncodedText[] EncodedNames { get; } = encodedNames;
            internal JsonProperty[] Values { get; } = new JsonProperty[names.Length];
        }
    }

    private readonly record struct CanonicalProperty(JsonProperty Property, string Name, int Ordinal);

    private sealed class CanonicalPropertyComparer : IComparer<CanonicalProperty>
    {
        public static readonly CanonicalPropertyComparer Instance = new();

        public int Compare(CanonicalProperty left, CanonicalProperty right)
        {
            int order = StringComparer.Ordinal.Compare(left.Name, right.Name);
            // Preserve the former stable OrderBy behavior for duplicate keys.
            return order != 0 ? order : left.Ordinal.CompareTo(right.Ordinal);
        }
    }

    private sealed class CanonicalHashBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private byte[] _buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);

        public void Advance(int count)
        {
            if ((uint)count > (uint)_buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            _hash.AppendData(_buffer.AsSpan(0, count));
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer;
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer;
        }

        private void EnsureCapacity(int sizeHint)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
            if (sizeHint <= _buffer.Length) return;
            byte[] replacement = ArrayPool<byte>.Shared.Rent(sizeHint);
            ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);
            _buffer = replacement;
        }

        public string GetDigest() => Convert.ToHexStringLower(_hash.GetHashAndReset());

        public void Dispose()
        {
            _hash.Dispose();
            ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);
        }
    }
}
