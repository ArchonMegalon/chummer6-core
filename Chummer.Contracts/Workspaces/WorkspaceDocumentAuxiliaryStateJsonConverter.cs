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
    internal static JsonTypeInfo<WorkspaceDocumentAuxiliaryState> TypeInfo =>
        WorkspaceJsonContext.Default.WorkspaceDocumentAuxiliaryState;

    public override WorkspaceDocumentAuxiliaryState? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize(ref reader, TypeInfo);

    public override void Write(
        Utf8JsonWriter writer, WorkspaceDocumentAuxiliaryState value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, TypeInfo);

    // Configuring the auxiliary contract otherwise initializes the complete
    // SR6 selection/preview graph even for SR5-only saves. Keep this leaf opaque
    // until a decision actually occurs. Reuse the generated context with plain
    // options, without this converter, to preserve default wire semantics without
    // generating a second copy of the SR6 graph or recursing through ourselves.
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

    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default,
        Converters = new[] { typeof(DeferredSr6DecisionConverter) })]
    [JsonSerializable(typeof(WorkspaceDocumentAuxiliaryState))]
    private sealed partial class WorkspaceJsonContext : JsonSerializerContext;
}
