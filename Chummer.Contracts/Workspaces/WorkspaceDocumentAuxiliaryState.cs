using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Workspaces;

/// <summary>
/// Durable workspace-owned state that is not part of the canonical character payload.
/// It must never be projected into a ruleset payload envelope or a character download.
/// </summary>
public sealed record WorkspaceDocumentAuxiliaryState(
    CharacterCreationFoundationDraftLedger? CharacterCreationFoundationDraft = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharacterCreationPrerequisiteDraft? CharacterCreationPrerequisiteDraft = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CharacterCreationAttributesDraft? CharacterCreationAttributesDraft = null)
{
    public static WorkspaceDocumentAuxiliaryState Empty { get; } = new();

    public bool IsEmpty => CharacterCreationFoundationDraft is null
                           && CharacterCreationPrerequisiteDraft is null
                           && CharacterCreationAttributesDraft is null;
}

public static class WorkspaceDocumentAuxiliaryStateDigest
{
    public const string Semantics = "canonical-workspace-document-auxiliary-state-json-sha256-v1";

    public static string Compute(WorkspaceDocumentAuxiliaryState? state)
    {
        JsonElement root = JsonSerializer.SerializeToElement(
            state ?? WorkspaceDocumentAuxiliaryState.Empty);
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            WriteCanonical(root, writer);
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element
                             .EnumerateObject()
                             .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    WriteCanonical(item, writer);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
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
                throw new InvalidOperationException("Unsupported auxiliary-state JSON value kind.");
        }
    }
}
