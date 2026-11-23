using System.Buffers;
using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using D1203 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private sealed record ProfileSettingCapability1203(string Field, string RelativePath,
        string LeafDigest, string ProfileDigest, string TypedCapabilityDigest);

    private IReadOnlyDictionary<string, ProfileSettingCapability1203>? profileSettingCapabilities1203;

    internal bool SupportsProfileSetting1203(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationSettingObservation observation,
        CharacterCreationSettingConsumer? consumer, string snapshot, string rawXml,
        CharacterCreationCompletionReviewRequest currentRequest,
        ProfileSettingInputValidation1203? inputValidation = null)
    {
        if (consumer != CharacterCreationSettingConsumer.None
            || observation.Key != CharacterCreationObservedSetting.Opaque
            || observation.State != CharacterCreationSettingObservationState.Opaque
            || profileSettingCapabilities1203 is { } known && !known.ContainsKey(observation.SettingId)
            || snapshot != Inputs.SnapshotDigest || rawXml != Inputs.Skills.Binding.RawCharacterXmlDigest
            || ProfileSettingCurrentDigest8574(currentRequest, inputValidation) != requestDigest
            || !MatchesProfileSettingInputs1203(inputValidation)
            || !ProfileSettingCanonicallyEquals1203(
                policy with { SelectedConsumption = null }, Inputs.EffectivePolicy! with { SelectedConsumption = null }, inputValidation)
            || !ProfileSettingCanonicallyEquals1203(
                source, Inputs.Magic.Authority.MysticAdeptPowerPointPolicy, inputValidation)
            || policy.SettingObservations!.Settings.Count(row => row == observation) != 1) return false;
        try
        {
            return ProfileSettingCapabilities1203().TryGetValue(observation.SettingId, out var capability)
                && capability.LeafDigest == observation.SourceValueDigest;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException
            or ArgumentException or OverflowException or XmlException) { return false; }
    }

    private static bool ProfileSettingCanonicallyEquals1203<T>(T left, T right,
        ProfileSettingInputValidation1203? inputValidation)
        => inputValidation is null
            ? CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right)
            : inputValidation.CanonicallyEquals(left, right);

    private static string ProfileSettingCurrentDigest8574<T>(T value, ProfileSettingInputValidation1203? inputValidation)
        => inputValidation is null ? CharacterCreationFinalizationDigest.Compute(value)
            : inputValidation.CanonicalDigest8544(value);

    private bool MatchesProfileSettingInputs1203(ProfileSettingInputValidation1203? inputValidation = null)
        => request.PolicyRequirement == CharacterCreationPolicyRequirement.Complete
            && request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
            && request.RuleSourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
            && CharacterCreationCompleteProfileSettingsExecution.CanAttempt(request)
            && Inputs.RuleSourceExecutionSchema == request.RuleSourceExecutionSchema
            && Inputs.RuleSourceCaptureDigest == request.RuleSourceCaptureDigest
            && Inputs.OriginalCharacter is not null && original.MatchesInputs(Inputs.SnapshotDigest)
            && requestDigest == ProfileSettingCurrentDigest8574(request, inputValidation)
            && request.InputsSnapshotDigest == Inputs.SnapshotDigest
            && (inputValidation is null
                ? CharacterCreationProfileInputRead8576.Matches(Inputs)
                : inputValidation.Matches(Inputs))
            && Inputs.EffectivePolicy is { SettingObservations: not null } policy
            && CharacterCreationCompleteProfileExecution.SupportsDomain(policy,
                Inputs.Magic.Authority.MysticAdeptPowerPointPolicy, CharacterCreationPolicyDomain.Profile)
            && CharacterCreationPolicyConsumptionProjection.MatchesSource(policy,
                Inputs.Magic.Authority.MysticAdeptPowerPointPolicy);

    // Owned by one synchronous Authority or private profile-consumption call.
    // Retains fully validated JSON bytes/digest and bounded immutable string encodings,
    // never an authority, resolver, request, or mutable input object.
    internal sealed class ProfileSettingInputValidation1203 : IDisposable
    {
        private byte[]? validatedJson;
        private string? validatedDigest;
        private sealed record CanonicalSnapshot8544(int ByteLength, byte[] JsonHash, string Digest);
        private readonly Dictionary<Type, CanonicalSnapshot8544> canonicalSnapshots8544 = new();
        private sealed record ComparisonSnapshot8574(int LeftLength, byte[] LeftHash,
            int RightLength, byte[] RightHash, bool Equal);
        private readonly Dictionary<Type, ComparisonSnapshot8574> comparisonSnapshots8574 = new();
        private int digestCalls8544, digestHits8544;
        private const int MaximumCanonicalSnapshotTypes8544 = 16;
        private StringValueEncodingCache? stringValueEncodings;
        private static readonly JsonSerializerOptions serializationOptions = CreateSerializationOptions();
        [ThreadStatic]
        private static StringValueEncodingCache? activeStringValueEncodings;
        private bool disposed;

        internal ProfileSettingInputValidation1203() => stringValueEncodings = new();

        // Opt-in numeric costs for the existing first-three Authority settings
        // samples only. No input, string, digest or authority is retained here.
        internal CostTotals3431? Costs3431 { get; private set; }

        internal void EnableCosts3431()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Costs3431 ??= new();
        }

        internal sealed class CostTotals3431
        {
            internal int InputCalls, ComparisonCalls, SerializationCalls;
            internal long InputTicks, ComparisonTicks, SerializationTicks, ValidatedHashTicks;
        }

        private enum Cost3431 { Inputs, Comparison, Serialization, ValidatedHash }

        private CostScope3431 Measure3431(Cost3431 kind)
            => Costs3431 is { } totals ? new(totals, kind) : default;

        private readonly struct CostScope3431 : IDisposable
        {
            private readonly CostTotals3431? totals;
            private readonly Cost3431 kind;
            private readonly long started;

            internal CostScope3431(CostTotals3431 totals, Cost3431 kind)
            {
                this.totals = totals;
                this.kind = kind;
                started = System.Diagnostics.Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (totals is null) return;
                long ticks = System.Diagnostics.Stopwatch.GetTimestamp() - started;
                switch (kind)
                {
                    case Cost3431.Inputs: totals.InputCalls++; totals.InputTicks += ticks; break;
                    case Cost3431.Comparison: totals.ComparisonCalls++; totals.ComparisonTicks += ticks; break;
                    case Cost3431.Serialization: totals.SerializationCalls++; totals.SerializationTicks += ticks; break;
                    case Cost3431.ValidatedHash: totals.ValidatedHashTicks += ticks; break;
                }
            }
        }

        private static JsonSerializerOptions CreateSerializationOptions()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new StringValueConverter());
            options.MakeReadOnly(populateMissingResolver: true);
            return options;
        }

        private void SerializeFresh<T>(Stream stream, T value)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var cost3431 = Measure3431(Cost3431.Serialization);
            // Only synchronous Stream serialization runs while bound. Never
            // await/yield in this helper or widen it around other validation work.
            // Metadata/converters never own the cache; nested calls restore the
            // actual outer call's cache, and independent threads cannot see it.
            StringValueEncodingCache? previous = activeStringValueEncodings;
            activeStringValueEncodings = stringValueEncodings;
            try
            {
                JsonSerializer.Serialize<T>(stream, value, serializationOptions);
            }
            finally
            {
                activeStringValueEncodings = previous;
            }
        }

        // Every call serializes the entire CURRENT graph and hashes those bytes.
        // A digest can be reused only after an actual fresh SHA-256 match with
        // previously validated JSON, using the same hash trust boundary as the
        // existing canonical comparison. No caller-advertised hash is trusted.
        // Retain at most 16 small hash pairs, never mutable graphs or large JSON.
        // The caller owns this helper's scope and serializes access.
        internal string CanonicalDigest8544<T>(T value)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            canonicalSnapshots8544.TryGetValue(typeof(T), out var previous);
            using var currentJson = new FreshJsonComparisonStream(null);
            SerializeFresh(currentJson, value);
            ReadOnlyMemory<byte> capturedJson = currentJson.GetCapturedJson();
            byte[] actualHash = System.Security.Cryptography.SHA256.HashData(capturedJson.Span);
            bool matches = previous is not null && previous.ByteLength == capturedJson.Length
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(previous.JsonHash, actualHash);
            RecordDigestCosts8544(capturedJson.Length, matches);
            if (matches) return previous!.Digest;

            using var document = JsonDocument.Parse(capturedJson);
            string digest = CharacterCreationFinalizationDigest.Compute(document.RootElement);
            if (previous is not null || canonicalSnapshots8544.Count < MaximumCanonicalSnapshotTypes8544)
                canonicalSnapshots8544[typeof(T)] = new(capturedJson.Length, actualHash, digest);
            return digest;
        }

        private void RecordDigestCosts8544(int bytes, bool hit)
        {
            if (!CharacterCreationCompletionCost3340.Log.IsEnabled()) return;
            digestCalls8544++;
            if (hit) digestHits8544++;
            if (digestCalls8544 <= 16 || digestCalls8544 % 128 == 0)
                CharacterCreationCompletionCost3340.Log.ProfileDigest8544(digestCalls8544, digestHits8544, bytes,
                    canonicalSnapshots8544.Count);
        }

        internal bool CanonicallyEquals<T>(T left, T right)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var cost3431 = Measure3431(Cost3431.Comparison);
            comparisonSnapshots8574.TryGetValue(typeof(T), out var previous);
            // Capture BOTH current graphs exactly once, in the cold path's order.
            // Only already-validated byte hashes can avoid parsing/writing those
            // exact bytes again. Never retain or trust either mutable operand.
            using var leftJson = new FreshJsonComparisonStream(null);
            SerializeFresh(leftJson, left);
            ReadOnlyMemory<byte> leftBytes = leftJson.GetCapturedJson();
            byte[] leftHash = System.Security.Cryptography.SHA256.HashData(leftBytes.Span);
            bool leftMatches = previous is not null && previous.LeftLength == leftBytes.Length
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(previous.LeftHash, leftHash);
            // Preserve syntax-error ordering: an unknown left snapshot is parsed
            // before reading right. The validating writer still runs only after
            // both snapshots, as in CapturedCanonicallyEquals.
            using var leftDocument = leftMatches ? null : JsonDocument.Parse(leftBytes);
            using var rightJson = new FreshJsonComparisonStream(null);
            SerializeFresh(rightJson, right);
            ReadOnlyMemory<byte> rightBytes = rightJson.GetCapturedJson();
            byte[] rightHash = System.Security.Cryptography.SHA256.HashData(rightBytes.Span);
            bool rightMatches = previous is not null && previous.RightLength == rightBytes.Length
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(previous.RightHash, rightHash);
            using var rightDocument = rightMatches ? null : JsonDocument.Parse(rightBytes);
            if (leftMatches && rightMatches)
            {
                CharacterCreationCompletionCost3340.Log.Checkpoint(8574);
                return previous!.Equal;
            }
            // An unchanged side may still be needed for a changed comparison.
            // Parse its captured bytes, never replay getters or converters.
            using var knownLeftDocument = leftDocument is null ? JsonDocument.Parse(leftBytes) : null;
            using var knownRightDocument = rightDocument is null ? JsonDocument.Parse(rightBytes) : null;
            using var hashCost3431 = Measure3431(Cost3431.ValidatedHash);
            bool equal = CharacterCreationFoundationDraftLedgerIntegrity.CapturedCanonicallyEquals(
                (leftDocument ?? knownLeftDocument)!.RootElement, (rightDocument ?? knownRightDocument)!.RootElement);
            // Both equal and unequal results come from the unchanged validator.
            // Malformed input throws before insertion; no failed pair is cached.
            if (previous is not null || comparisonSnapshots8574.Count < MaximumCanonicalSnapshotTypes8544)
                comparisonSnapshots8574[typeof(T)] = new(leftBytes.Length, leftHash, rightBytes.Length, rightHash, equal);
            return equal;
        }

        internal bool Matches(CharacterCreationCompletionReviewInputs inputs)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var cost3431 = Measure3431(Cost3431.Inputs);
            // Match ComputeInputsDigest's projection and declared type with the
            // default serialization contract. Every call still reads the entire
            // current graph; only immutable string VALUE encodings can be reused.
            using var currentJson = new FreshJsonComparisonStream(validatedJson);
            var currentInputs = inputs with { SnapshotDigest = string.Empty };
            // Admit this immutable value before other long strings fill the same
            // bounded cache. Both typed record getters have no custom code.
            if (currentInputs.OriginalCharacter?.RawCharacterXml is { } rawXml)
                stringValueEncodings!.Prime(rawXml);
            SerializeFresh<CharacterCreationCompletionReviewInputs>(currentJson, currentInputs);
            if (currentJson.IsExactMatch)
                return inputs.SnapshotDigest == validatedDigest;

            // Capture only on a mismatch (including a shorter serialization).
            // Reuse only prefix bytes already compared equal, never reread inputs.
            ReadOnlyMemory<byte> capturedJson = currentJson.GetCapturedJson();
            using var document = JsonDocument.Parse(capturedJson);
            string digest = CharacterCreationFinalizationDigest.Compute(document.RootElement);
            if (inputs.SnapshotDigest != digest) return false;
            validatedJson = capturedJson.ToArray();
            validatedDigest = digest;
            return true;
        }

        public void Dispose()
        {
            Costs3431 = null;
            validatedJson = null;
            validatedDigest = null;
            canonicalSnapshots8544.Clear();
            comparisonSnapshots8574.Clear();
            digestCalls8544 = digestHits8544 = 0;
            stringValueEncodings?.Clear();
            stringValueEncodings = null;
            disposed = true;
        }

        // The shared converter has no owner/cache fields. Its metadata can be
        // reused by every validator without capturing any caller's string data.
        private sealed class StringValueConverter : System.Text.Json.Serialization.JsonConverter<string>
        {
            public override string? Read(ref Utf8JsonReader reader, Type typeToConvert,
                JsonSerializerOptions options) => reader.GetString();

            // Nulls retain JsonConverter<string>'s default bypass. Keys always use
            // the default property-name writer, never a cached string VALUE token.
            public override void WriteAsPropertyName(Utf8JsonWriter writer, string value,
                JsonSerializerOptions options) => writer.WritePropertyName(value);

            public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            {
                if (activeStringValueEncodings is { } cache)
                    cache.Write(writer, value);
                else
                    writer.WriteStringValue(value);
            }
        }

        private sealed class StringValueEncodingCache
        {
            private const int MinimumLength = 1024;
            private const int MaximumEntries = 256;
            private const long MaximumRetainedBytes = 4 * 1024 * 1024;
            private readonly Dictionary<string, byte[]> encodings = new(ReferenceEqualityComparer.Instance);
            private long retainedBytes;

            internal void Prime(string value) => _ = GetOrEncode(value);

            internal void Write(Utf8JsonWriter writer, string value)
            {
                byte[]? encoded = GetOrEncode(value);
                if (encoded is null)
                    writer.WriteStringValue(value);
                else
                    // The private bytes are a complete token produced by the default
                    // serializer; they are never exposed or modified after creation.
                    writer.WriteRawValue(encoded, skipInputValidation: true);
            }

            private byte[]? GetOrEncode(string value)
            {
                if (value.Length < MinimumLength) return null;
                if (encodings.TryGetValue(value, out byte[]? encoded)) return encoded;
                // Conservative admission covers UTF-16 keys plus worst-case
                // default JSON escaping (six bytes per code unit), and quotes.
                // A limit miss uses the ordinary writer without allocating a token.
                if (encodings.Count >= MaximumEntries
                    || 8L * value.Length + 2 > MaximumRetainedBytes - retainedBytes)
                    return null;
                // No caller options, mutable graph, or JsonElement enters this
                // cache. Identity is safe only because these keys are strings.
                encoded = JsonSerializer.SerializeToUtf8Bytes<string>(value);
                long entryBytes = 2L * value.Length + encoded.Length;
                if (entryBytes <= MaximumRetainedBytes - retainedBytes)
                {
                    encodings.Add(value, encoded);
                    retainedBytes += entryBytes;
                }
                return encoded;
            }

            internal void Clear()
            {
                encodings.Clear();
                retainedBytes = 0;
            }
        }

        // Per-call sink; the validator alone owns the validated byte array.
        // Internal visibility allows chunk/length regression coverage. Captured
        // memory is borrowed only until disposal and never becomes cached memory.
        internal sealed class FreshJsonComparisonStream : Stream
        {
            private byte[]? expectedJson;
            private byte[]? capture;
            private int length;
            private bool disposed;

            internal FreshJsonComparisonStream(byte[]? expectedJson)
                => this.expectedJson = expectedJson;

            internal bool IsExactMatch
            {
                get
                {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    return capture is null && expectedJson is not null && length == expectedJson.Length;
                }
            }

            internal ReadOnlyMemory<byte> GetCapturedJson()
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                EnsureCapacity(length);
                return capture.AsMemory(0, length);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                ArgumentNullException.ThrowIfNull(buffer);
                Write(buffer.AsSpan(offset, count));
            }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (buffer.IsEmpty) return;
                int nextLength = checked(length + buffer.Length);
                if (capture is null && expectedJson is not null
                    && nextLength <= expectedJson.Length
                    && buffer.SequenceEqual(expectedJson.AsSpan(length, buffer.Length)))
                {
                    length = nextLength;
                    return;
                }

                EnsureCapacity(nextLength);
                buffer.CopyTo(capture.AsSpan(length));
                length = nextLength;
            }

            private void EnsureCapacity(int required)
            {
                if (capture is not null && capture.Length >= required) return;
                if (required > Array.MaxLength) throw new OutOfMemoryException();
                int capacity = Math.Max(required,
                    (int)Math.Min((long)(capture?.Length ?? 2048) * 2, Array.MaxLength));
                byte[] replacement = ArrayPool<byte>.Shared.Rent(capacity);
                try
                {
                    if (capture is not null)
                        capture.AsSpan(0, length).CopyTo(replacement);
                    else if (length != 0)
                    {
                        // Every byte before length has already matched this call's
                        // serialization. Bytes in the mismatching chunk are fresh.
                        expectedJson.AsSpan(0, length).CopyTo(replacement);
                    }
                }
                catch
                {
                    ArrayPool<byte>.Shared.Return(replacement, clearArray: true);
                    throw;
                }
                byte[]? previous = capture;
                capture = replacement;
                if (previous is not null) ArrayPool<byte>.Shared.Return(previous, clearArray: true);
            }

            protected override void Dispose(bool disposing)
            {
                if (!disposed)
                {
                    disposed = true;
                    expectedJson = null;
                    if (capture is not null)
                    {
                        ArrayPool<byte>.Shared.Return(capture, clearArray: true);
                        capture = null;
                    }
                    length = 0;
                }
                base.Dispose(disposing);
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => !disposed;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }
            public override void Flush() => ObjectDisposedException.ThrowIf(disposed, this);
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }

    private IReadOnlyDictionary<string, ProfileSettingCapability1203> ProfileSettingCapabilities1203()
    {
        if (profileSettingCapabilities1203 is not null) return profileSettingCapabilities1203;
        var result = new Dictionary<string, ProfileSettingCapability1203>(StringComparer.Ordinal);
        var policy = Inputs.EffectivePolicy!;
        var profile = XElement.Parse(Inputs.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml,
            LoadOptions.PreserveWhitespace);
        if (profile.HasAttributes) return result;
        using var scope = (resolution.Resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var context = (scope ?? resolution.Resolver).TryCreateContext(Inputs.OriginalCharacter!.RawCharacterXml);
        // The same canonical selection consumer used by CreationSources718 must
        // accept the retained method, ranks, grants and budget. XML names or a
        // caller-supplied authority hash cannot create an admission capability.
        if (context is null || !context.TryResolveCreationPrerequisiteAuthority(out var prerequisite)
            || prerequisite.SettingsProfileId != policy.SettingsProfileId
            || prerequisite.RawProfileInputsDigest != policy.ProfileInputsDigest
            || !CharacterCreationPrerequisiteService.TryExecuteSourceSelection718(prerequisite,
                Inputs.Skills.PrerequisiteDraft, out var executed)) return result;

        Add("buildpoints", null, prerequisite, executed!.CreationKarmaTotal);
        Add("priorityarray", null, prerequisite);
        Add("prioritytable", null, prerequisite, executed.PriorityTable);
        if (executed.SumToTenTarget is int target) Add("sumtoten", null, prerequisite, target);

        if (context.TryResolveCreationPhysicalInitiative886(executed, out var physical) && physical is not null
            && physical == Inputs.PhysicalInitiative886
            && physical.SettingsProfileId == policy.SettingsProfileId
            && physical.ProfileInputsDigest == policy.ProfileInputsDigest)
        {
            if (physical.Metatype.MinimumDiceFallback is int minimum)
                Add("mininitiativedice", null, physical, minimum);
            Add("maxinitiativedice", null, physical, physical.MaximumDice);
        }
        if (context.TryResolveCreationQualitiesAuthority(executed, out var qualities) && qualities.IsAuthoritative
            && qualities.SettingsProfileId == policy.SettingsProfileId && qualities.ProfileDigest == policy.ProfileInputsDigest
            && qualities.CostPolicy == Inputs.Lifestyles.Qualities.Authority.CostPolicy
            && PlainProfileLeaf1203(profile, "qualitykarmalimit", null) is { } limit
            && ScalarMatches1203(limit, qualities.QualityKarmaLimit))
        {
            // Canonical quality authorities omit the default cost policy to preserve
            // their historical digest. Match that nullable representation above before
            // interpreting it, exactly as the completed quality consumer does.
            var cost = qualities.CostPolicy ?? CharacterCreationQualityCostPolicy.Default;
            Add("exceedpositivequalitiescostdoubled", null, qualities, cost.DoublePositiveExcess);
            Add("exceednegativequalitiesnobonus", null, qualities, cost.CapNegativeRebate);
        }
        if (request.Selections.SkillKarmaPurchases is not null
            && context.TryResolveCreationPrioritySkillKarmaPolicy(out var skills)
            && skills is { Schema: CharacterCreationKarmaSkillsPolicy.PriorityPurchaseSchemaV1, AllowSkillRegrouping: not null }
            && skills.SettingsProfileId == policy.SettingsProfileId && skills.RawProfileInputsDigest == policy.ProfileInputsDigest
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(skills, Inputs.Skills.KarmaPurchasePolicy))
            Add("compensateskillgroupkarmadifference", null, skills, skills.CompensateSkillGroupKarmaDifference);
        if (context.TryResolveCreationCarryoverPolicy(out var carryover) && carryover is not null
            && CharacterCreationKarmaFinalizationBudgetRules.IsValidPolicy(carryover)
            && carryover.SettingsProfileId == policy.SettingsProfileId && carryover.RawProfileInputsDigest == policy.ProfileInputsDigest
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(carryover, Inputs.Contacts.CarryoverPolicy))
            Add("karmacarryover", "karmacost", carryover, carryover.MaximumKarma);
        AddNativeMagicCapabilities1430(profile, executed!, Add);
        AddProfileLifecycleCapabilities1217(profile, context, prerequisite, executed!, Add);
        AddMatrixLifecycleCapabilities(profile, executed!, Add);
        AddNativeSettingsCapabilities1420(profile, executed!, Add);
        AddNativeContactSpecialtyCapabilities(profile, context, executed!, Add);
        AddNativeLoadoutCapabilities1432(profile, executed!, Add);
        AddProfileLoadoutCapabilities1215(result, context, profile);
        AddRetainedSr5Capabilities1241(result, context, profile);
        AddRetainedCompatibilityCapabilities1430(result, context, profile);
        profileSettingCapabilities1203 = result;
        return result;

        void Add(string field, string? container, object capability, object? expected = null)
        {
            var leaf = PlainProfileLeaf1203(profile, field, container);
            if (leaf is null || expected is not null && !ScalarMatches1203(leaf, expected)) return;
            string digest = CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting));
            // Match the preserved observation itself. Equal text at another source
            // occurrence cannot select a row by name, nor may any row disappear.
            var observations = policy.SettingObservations!.Settings.Where(row =>
                row.Key == CharacterCreationObservedSetting.Opaque && row.State == CharacterCreationSettingObservationState.Opaque
                && row.SourceValueDigest == digest).ToArray();
            if (observations is not [var observation]) return;
            result.Add(observation.SettingId, new(field, RelativePath1203(profile, leaf), digest,
                CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
                CharacterCreationFinalizationDigest.Compute(capability)));
        }
    }

    private static XElement? PlainProfileLeaf1203(XElement profile, string field, string? container)
    {
        XElement parent = profile;
        if (container is not null)
        {
            var containers = profile.Elements().Where(node => node.Name.LocalName == container).ToArray();
            if (containers is not [var found] || found.Name != container || found.HasAttributes
                || found.Nodes().Any(node => node is not XElement
                    && (node.GetType() != typeof(XText) || !string.IsNullOrWhiteSpace(((XText)node).Value)))) return null;
            parent = found;
        }
        var leaves = parent.Elements().Where(node => node.Name.LocalName == field).ToArray();
        if (leaves is not [var leaf] || leaf.Name != field || leaf.HasAttributes || leaf.HasElements
            || leaf.Nodes().Any(node => node.GetType() != typeof(XText))
            || field != "priorityarray" && leaf.Nodes().ToArray() is not [XText]) return null;
        return leaf;
    }

    private static bool ScalarMatches1203(XElement leaf, object expected) => expected switch
    {
        bool boolean => bool.TryParse(leaf.Value, out bool value) && value == boolean,
        int integer => int.TryParse(leaf.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value == integer,
        string text => leaf.Value == text,
        _ => false
    };

    private static string RelativePath1203(XElement profile, XElement leaf)
        => ReferenceEquals(profile, leaf) ? string.Empty : RelativePath1203(profile, leaf.Parent!)
            + "/" + leaf.Name + "[" + leaf.Parent!.Nodes().TakeWhile(node => !ReferenceEquals(node, leaf)).Count() + "]";

    private IReadOnlyDictionary<string, string> CompleteProfileSettings1203(CharacterCreationCompleteAssessment authority,
        CharacterCreationRuleSourceScopeInvocation invocation, IReadOnlyList<CharacterCreationRuleSourceScopeResult> reports)
    {
        inactiveRegroupingSettings3207.Clear();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var inputValidation = new ProfileSettingInputValidation1203();
        if (!MatchesProfileSettingInputs1203(inputValidation) || authority.Phase != CharacterCreationCompletePhase.Authority
            || authority.Schema != CharacterCreationCompleteAssessment.SchemaV4
            || authority.InputsSnapshotDigest != Inputs.SnapshotDigest || authority.RequestDigest != requestDigest
            || authority.RawCharacterXmlDigest != Inputs.Skills.Binding.RawCharacterXmlDigest
            || authority.SelectionsDigest != CharacterCreationFinalizationDigest.Compute(request.Selections)
            || authority.PolicyDigest != Inputs.EffectivePolicy!.PolicyDigest
            || invocation.RequestDigest != requestDigest || invocation.PolicyDigest != authority.PolicyDigest
            || invocation.CaptureDigest != Inputs.RuleSourceCaptureDigest
            || invocation.WorkspaceId718 != Inputs.OriginalCharacter!.WorkspaceId
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(invocation.CreationDraft718, Inputs.Skills.PrerequisiteDraft)
            || !invocation.TryGetCompletedConsumer("profile", out var consumer)) return result;
        var sources = Inputs.EffectivePolicy.Sources.Where(row => row.Kind == CharacterCreationPolicySourceKind.Rules
            && row.SourceId == "profile").ToArray();
        var profiles = reports.Where(row => row.Scope == "profile").ToArray();
        if (sources is not [var source] || profiles is not [var report]
            || report.ObligationId != $"source:{source.Kind}:{source.Order}" || report.SourceDigest != source.ContentDigest
            || report.ConsumerDigest != consumer || report.Operations.Count == 0
            || report.Operations[0].Disposition != D1203.Structural
            || !report.Operations.Select(row => row.Order).SequenceEqual(Enumerable.Range(0, report.Operations.Count))
            || report.Operations.Select(row => row.Path).Distinct(StringComparer.Ordinal).Count() != report.Operations.Count) return result;
        CompleteAttributeKarmaSetting3201(authority, invocation, report, result);
        var capabilities = ProfileSettingCapabilities1203();
        foreach (var row in authority.Obligations.Where(row => row.Kind == CharacterCreationCompleteObligationKind.Setting
            && row.Evaluability == CharacterCreationCompleteEvaluability.CandidateDependent && row.Setting is not null))
        {
            if (!SupportsProfileSetting1203(Inputs.EffectivePolicy, Inputs.Magic.Authority.MysticAdeptPowerPointPolicy,
                    row.Setting!, row.Consumer, authority.InputsSnapshotDigest, authority.RawCharacterXmlDigest, request, inputValidation)
                || !capabilities.TryGetValue(row.Setting!.SettingId, out var capability)) continue;
            var parents = report.Operations.Where(operation => operation.SourceDigest == capability.ProfileDigest
                && operation.Disposition == D1203.Structural).ToArray();
            if (parents is not [var parent]) continue;
            string path = parent.Path + capability.RelativePath;
            var operations = report.Operations.Where(operation => operation.Path == path
                || operation.Path.StartsWith(path + "/", StringComparison.Ordinal)).ToArray();
            if (operations.Length == 0 || operations[0].Path != path || operations[0].SourceDigest != capability.LeafDigest
                || operations.Any(operation => operation.Classification886 is not null
                    || !(operation.Disposition == D1203.Compiled || capability.Field == "allowskillregrouping"
                        && operation.Disposition == D1203.NotSelected || capability.Field == "sumtoten"
                        && invocation.CreationDraft718!.BuildMethod == CharacterCreationBuildMethods.Priority
                         && operation.Disposition == D1203.NotSelected
                         || IsExcludedCareerSetting1217(capability.Field, operation.Disposition, invocation))
                    || !CharacterCreationFinalizationDigest.IsCanonical(operation.ExecutionDigest)
                    || operation.OperationResult718 is null)
                || !MatchesNativeMagicResult1430(capability.Field, invocation, operations)
                || !MatchesAstralProfileResult1249(capability.Field, invocation, operations)
                || !MatchesMatrixLifecycleResult(capability.Field, invocation, operations)
                || !MatchesRetainedCompatibilityResult1430(capability.Field, invocation, operations)
                || !MatchesNativeSettingsResult1420(capability.Field, invocation, operations)
                || !MatchesNativeContactSpecialtyResult(capability.Field, invocation, operations)
                || !MatchesNativeLoadoutResult1432(capability.Field, invocation, operations)
                || !MatchesRegroupingResult3207(authority, capability, invocation, operations, out bool regroupingInactive)
                || !resolution.HasExecutedProfileSetting1203(invocation, report, operations[0].Order)) continue;
            if (regroupingInactive) inactiveRegroupingSettings3207.Add(row.Id);
            result.Add(row.Id, CharacterCreationFinalizationDigest.Compute(new
            {
                Operation = "private-profile-setting-bridge1203", Authority = inputValidation.CanonicalDigest8544(authority),
                row.Id, row.AuthorityDigest, Observation = row.Setting, row.Consumer, Capability = capability,
                Inputs.SnapshotDigest, Inputs.OriginalCharacter.WorkspaceId, Inputs.OriginalCharacter.ContentRevision,
                Inputs.OriginalCharacter.SavedRevision, authority.RawCharacterXmlDigest,
                invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
                invocation.CreationDraft718, Source = source, report.ObligationId, report.SourceDigest, report.ConsumerDigest,
                Operations = operations.Select(operation => new
                    { Operation = operation, operation.ConsumedValue718, operation.OperationResult718 }).ToArray()
            }));
        }
        return result;
    }
}
