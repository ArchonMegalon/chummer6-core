using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Workspaces;

/// <summary>
/// Default workspace JSON semantics with generated contracts for persisted
/// auxiliary state and its canonical digest. Do not register this on the state
/// type itself: the generated contract must not recurse through this converter.
/// </summary>
public sealed partial class WorkspaceDocumentAuxiliaryStateJsonConverter
    : JsonConverter<WorkspaceDocumentAuxiliaryState>
{
    private static readonly ActivitySource StartupActivities8910 = new("Chummer.Startup.Shell8910");

    private static readonly Lazy<JsonTypeInfo<WorkspaceDocumentAuxiliaryState>> AuxiliaryTypeInfo = new(
        static () =>
        {
            var options = new JsonSerializerOptions { TypeInfoResolver = new DeferredWorkspaceResolver() };
            options.MakeReadOnly();
            return (JsonTypeInfo<WorkspaceDocumentAuxiliaryState>)options.GetTypeInfo(typeof(WorkspaceDocumentAuxiliaryState));
        });

    internal static JsonTypeInfo<WorkspaceDocumentAuxiliaryState> TypeInfo => AuxiliaryTypeInfo.Value;

    /// <summary>
    /// Capture a fresh finalization preview with the default workspace wire
    /// contract. The caller owns the returned document. Reuse only generated
    /// metadata, never a mutable preview or a previously computed digest.
    /// </summary>
    public static JsonDocument CaptureFinalizationPreview(CharacterCreationFoundationFinalizationPreview? value) =>
        JsonSerializer.SerializeToDocument(value,
            (JsonTypeInfo<CharacterCreationFoundationFinalizationPreview?>)TypeInfo.Options.GetTypeInfo(
                typeof(CharacterCreationFoundationFinalizationPreview)));

    /// <summary>Capture a fresh complete auxiliary graph using the default
    /// workspace wire contract. Only serializer metadata is shared.</summary>
    public static JsonElement CaptureAuxiliaryState(WorkspaceDocumentAuxiliaryState value) =>
        JsonSerializer.SerializeToElement(value, TypeInfo);

    /// <summary>Restore a detached graph for each read; never share mutable
    /// histories or extension data between continuation consumers.</summary>
    public static WorkspaceDocumentAuxiliaryState RestoreAuxiliaryState(JsonElement value) =>
        value.Deserialize(TypeInfo)
            ?? throw new InvalidDataException("Candidate auxiliary state is missing.");

    // Only these sealed, default-wire input roots use the generated workspace
    // contracts. Declared object/interface roots retain normal polymorphism.
    // Reusing metadata never reuses an input observation or its mutable graph.
    internal static JsonTypeInfo<T>? CreationInputTypeInfo<T>()
        => typeof(T) == typeof(CharacterCreationCompletionReviewInputs)
            || typeof(T) == typeof(CharacterCreationSkillsCompletionInputs)
            || typeof(T) == typeof(CharacterCreationMagicResonanceCompletionInputs)
            || typeof(T) == typeof(CharacterCreationLifestylesCompletionInputs)
            || typeof(T) == typeof(CharacterCreationContactsCompletionInputs)
            ? (JsonTypeInfo<T>)TypeInfo.Options.GetTypeInfo(typeof(T))
            : null;

    /// <summary>Copy the entire current Creation input graph. Every call returns
    /// detached collections; this supplies no owner, source or save authority.</summary>
    public static T DetachCreationCompletion<T>(T value)
    {
        JsonTypeInfo<T>? typeInfo = CreationInputTypeInfo<T>();
        return typeInfo is null
            ? JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!
            : JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(value, typeInfo), typeInfo)!;
    }

    public override WorkspaceDocumentAuxiliaryState? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var converterTiming = StartupActivities8910.StartActivity("auxiliary-converter");
        using var typeInfoTiming = StartupActivities8910.StartActivity("auxiliary-typeinfo");
        var typeInfo = TypeInfo;
        typeInfoTiming?.Stop();
        return JsonSerializer.Deserialize(ref reader, typeInfo);
    }

    public override void Write(
        Utf8JsonWriter writer, WorkspaceDocumentAuxiliaryState value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, TypeInfo);

    // Configuring the auxiliary contract otherwise initializes the complete
    // SR6 selection/preview graph even for SR5-only saves. Keep this leaf opaque
    // until a decision actually occurs. The plain generated context preserves
    // the existing wire semantics without generating a second graph or recursion.
    private sealed class DeferredSr6DecisionConverter : JsonConverter<Sr6CreationFoundationDecision>
    {
        private static readonly Lazy<JsonTypeInfo<Sr6CreationFoundationDecision>> DecisionTypeInfo = new(
            static () => new WorkspaceJsonContext(new JsonSerializerOptions()).Sr6CreationFoundationDecision);

        public override Sr6CreationFoundationDecision? Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            JsonSerializer.Deserialize(ref reader, DecisionTypeInfo.Value);

        public override void Write(Utf8JsonWriter writer, Sr6CreationFoundationDecision value,
            JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, DecisionTypeInfo.Value);
    }

    // Absent finalization archives need no historical-authority metadata. A
    // present archive still reads/writes its complete graph with the same plain
    // generated contract, including nested state and optional legacy/SR6 fields.
    private sealed class DeferredArchiveConverter<T> : JsonConverter<T> where T : class
    {
        private static readonly Lazy<JsonTypeInfo<T>> ArchiveTypeInfo = new(
            static () => (JsonTypeInfo<T>)new WorkspaceJsonContext(new JsonSerializerOptions())
                .GetTypeInfo(typeof(T))!);

        public override T? Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            JsonSerializer.Deserialize(ref reader, ArchiveTypeInfo.Value);

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, ArchiveTypeInfo.Value);
    }

    // Share only default-wire metadata across present sections. Absent Career
    // and play sections need no child contracts during a Creation read. Present
    // sections still read/write their entire graph through the plain generated
    // context, including nested auxiliary state and legacy/SR6 members.
    private static readonly Lazy<WorkspaceJsonContext> SectionContext = new(
        static () => new WorkspaceJsonContext(new JsonSerializerOptions()));

    private sealed class DeferredSectionConverter<T> : JsonConverter<T> where T : class
    {
        private static readonly Lazy<JsonTypeInfo<T>> SectionTypeInfo = new(
            static () => (JsonTypeInfo<T>)SectionContext.Value.GetTypeInfo(typeof(T))!);

        public override T? Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            JsonSerializer.Deserialize(ref reader, SectionTypeInfo.Value);

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, SectionTypeInfo.Value);
    }

    private sealed class DeferredWorkspaceResolver : IJsonTypeInfoResolver
    {
        private readonly WorkspaceJsonContext _context = new();

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            // These optional wire sections are unrelated to an editable Creation
            // runner. Keep their metadata opaque until a non-null section occurs;
            // do not discard, project or postpone validation of any present data.
            if (type == typeof(IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry>))
                return Deferred<IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry>>(options);
            if (type == typeof(IReadOnlyList<CharacterAfterRunRewardReceipt>))
                return Deferred<IReadOnlyList<CharacterAfterRunRewardReceipt>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerReputationReceipt>))
                return Deferred<IReadOnlyList<CharacterCareerReputationReceipt>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerQualityTransactionEntry>))
                return Deferred<IReadOnlyList<CharacterCareerQualityTransactionEntry>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerQualityCorrectionEntry>))
                return Deferred<IReadOnlyList<CharacterCareerQualityCorrectionEntry>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerLifestyleMaintenanceReceipt>))
                return Deferred<IReadOnlyList<CharacterCareerLifestyleMaintenanceReceipt>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerInitiationEntry>))
                return Deferred<IReadOnlyList<CharacterCareerInitiationEntry>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerPowerEntry>))
                return Deferred<IReadOnlyList<CharacterCareerPowerEntry>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerLifestylePaymentReceipt>))
                return Deferred<IReadOnlyList<CharacterCareerLifestylePaymentReceipt>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerContactsReceipt>))
                return Deferred<IReadOnlyList<CharacterCareerContactsReceipt>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerGearPurchaseReceipt>))
                return Deferred<IReadOnlyList<CharacterCareerGearPurchaseReceipt>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerSpellLearningReceipt>))
                return Deferred<IReadOnlyList<CharacterCareerSpellLearningReceipt>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerCustomDrugPurchaseReceipt>))
                return Deferred<IReadOnlyList<CharacterCareerCustomDrugPurchaseReceipt>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerComplexFormLearningReceipt>))
                return Deferred<IReadOnlyList<CharacterCareerComplexFormLearningReceipt>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerImplantCommerceReceipt>))
                return Deferred<IReadOnlyList<CharacterCareerImplantCommerceReceipt>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerFreeSpriteConversionReceipt>))
                return Deferred<IReadOnlyList<CharacterCareerFreeSpriteConversionReceipt>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerSpiritFetteringReceipt>))
                return Deferred<IReadOnlyList<CharacterCareerSpiritFetteringReceipt>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerFocusBondingReceipt>))
                return Deferred<IReadOnlyList<CharacterCareerFocusBondingReceipt>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerGearEquipmentReceipt>))
                return Deferred<IReadOnlyList<CharacterCareerGearEquipmentReceipt>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerLoadoutPreset>))
                return Deferred<IReadOnlyList<CharacterCareerLoadoutPreset>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerGearSaleReceipt>))
                return Deferred<IReadOnlyList<CharacterCareerGearSaleReceipt>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerSpecializationReceipt>))
                return Deferred<IReadOnlyList<CharacterCareerSpecializationReceipt>>(options);
            if (type == typeof(IReadOnlyList<CharacterCareerMartialAcquisitionReceipt>))
                return Deferred<IReadOnlyList<CharacterCareerMartialAcquisitionReceipt>>(options);
            if (type == typeof(Sr5RecoveryLedger))
                return Deferred<Sr5RecoveryLedger>(options);
            if (type == typeof(IReadOnlyList<CharacterSustainedSpellReceipt>))
                return Deferred<IReadOnlyList<CharacterSustainedSpellReceipt>>(options);
            if (type == typeof(IReadOnlyList<Chummer.Contracts.Session.SessionConditionTransferCanonicalReceipt>))
                return Deferred<IReadOnlyList<Chummer.Contracts.Session.SessionConditionTransferCanonicalReceipt>>(options);
            if (type == typeof(Chummer.Contracts.Session.SessionCanonicalCondition))
                return Deferred<Chummer.Contracts.Session.SessionCanonicalCondition>(options);
            if (type == typeof(Chummer.Contracts.Session.SessionPlaySettlementContinuation5712))
                return Deferred<Chummer.Contracts.Session.SessionPlaySettlementContinuation5712>(options);
            if (type == typeof(IReadOnlyList<WorkspaceWizardReceipt>))
                return Deferred<IReadOnlyList<WorkspaceWizardReceipt>>(options);

            return type == typeof(Sr6CreationFoundationDecision)
                ? JsonMetadataServices.CreateValueInfo<Sr6CreationFoundationDecision>(options,
                    new DeferredSr6DecisionConverter())
                : type == typeof(CharacterCreationFinalizationArchive)
                    ? JsonMetadataServices.CreateValueInfo<CharacterCreationFinalizationArchive>(options,
                        new DeferredArchiveConverter<CharacterCreationFinalizationArchive>())
                : type == typeof(Sr6CreationFinalizationArchive)
                    ? JsonMetadataServices.CreateValueInfo<Sr6CreationFinalizationArchive>(options,
                        new DeferredArchiveConverter<Sr6CreationFinalizationArchive>())
                : ((IJsonTypeInfoResolver)_context).GetTypeInfo(type, options);
        }

        private static JsonTypeInfo<T> Deferred<T>(JsonSerializerOptions options) where T : class
            => JsonMetadataServices.CreateValueInfo<T>(options, new DeferredSectionConverter<T>());
    }

    // A resolver-local leaf leaves the SR5 child contracts' generated writers
    // usable. A converter in Options.Converters disables every generated writer,
    // making subsequent saves and digests pay for the cold-read optimization.
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
    [JsonSerializable(typeof(WorkspaceDocumentAuxiliaryState))]
    [JsonSerializable(typeof(CharacterCreationCompletionReviewInputs))]
    [JsonSerializable(typeof(CharacterCreationCompletionDraftPrefix))]
    [JsonSerializable(typeof(CharacterCreationCompletionDraftCommandDigest))]
    private sealed partial class WorkspaceJsonContext : JsonSerializerContext;
}
