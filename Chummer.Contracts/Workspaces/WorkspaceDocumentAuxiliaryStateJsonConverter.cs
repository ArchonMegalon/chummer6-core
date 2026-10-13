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
    private static readonly Lazy<JsonTypeInfo<WorkspaceDocumentAuxiliaryState>> AuxiliaryTypeInfo = new(
        static () =>
        {
            var options = new JsonSerializerOptions { TypeInfoResolver = new DeferredWorkspaceResolver() };
            options.MakeReadOnly();
            return (JsonTypeInfo<WorkspaceDocumentAuxiliaryState>)options.GetTypeInfo(typeof(WorkspaceDocumentAuxiliaryState));
        });

    internal static JsonTypeInfo<WorkspaceDocumentAuxiliaryState> TypeInfo => AuxiliaryTypeInfo.Value;

    public override WorkspaceDocumentAuxiliaryState? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize(ref reader, TypeInfo);

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

    private sealed class DeferredWorkspaceResolver : IJsonTypeInfoResolver
    {
        private readonly WorkspaceJsonContext _context = new();

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
            type == typeof(Sr6CreationFoundationDecision)
                ? JsonMetadataServices.CreateValueInfo<Sr6CreationFoundationDecision>(options,
                    new DeferredSr6DecisionConverter())
                : ((IJsonTypeInfoResolver)_context).GetTypeInfo(type, options);
    }

    // A resolver-local leaf leaves the SR5 child contracts' generated writers
    // usable. A converter in Options.Converters disables every generated writer,
    // making subsequent saves and digests pay for the cold-read optimization.
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
    [JsonSerializable(typeof(WorkspaceDocumentAuxiliaryState))]
    private sealed partial class WorkspaceJsonContext : JsonSerializerContext;
}
