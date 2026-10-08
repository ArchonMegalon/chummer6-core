using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Chummer.Contracts.Workspaces;

/// <summary>
/// Default workspace JSON semantics with one generated contract for persisted
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

    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
    [JsonSerializable(typeof(WorkspaceDocumentAuxiliaryState))]
    private sealed partial class WorkspaceJsonContext : JsonSerializerContext;
}
