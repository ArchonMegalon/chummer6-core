using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Chummer.Contracts.Workspaces;

/// <summary>
/// Complete owner-private continuation state, not a character download or an
/// authorization grant. Historical receipts do not authorize replay or restoration.
/// </summary>
public sealed record WorkspaceContinuationSnapshot(
    string OwnerId,
    WorkspaceDocumentSnapshot Workspace,
    IReadOnlyList<DelegatedGmCharacterEditAuditReceipt> DelegatedGmCharacterEdits)
{
    public const string ContractName = "chummer.workspace-continuation-snapshot/v1";
}

/// <summary>Content identity only; restore admission must independently validate authority.</summary>
public sealed record WorkspaceContinuationExport(
    WorkspaceContinuationSnapshot Snapshot,
    string SnapshotDigest);

public static class WorkspaceContinuationSnapshotDigest
{
    public const string Semantics = "canonical-workspace-continuation-json-sha256-v1";

    // Persist the state once, excluding convenience projections such as Content,
    // PayloadEnvelope and AuxiliaryStateDigest. All init-only state is retained.
    private static readonly JsonSerializerOptions Options = new()
    {
        IgnoreReadOnlyProperties = true
    };

    public static string Compute(WorkspaceContinuationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        JsonElement root = JsonSerializer.SerializeToElement(
            new DigestEnvelope(WorkspaceContinuationSnapshot.ContractName, snapshot), Options);
        return ComputeSerializedSnapshot(root.GetProperty(nameof(DigestEnvelope.Snapshot)));
    }

    // The transport calls this only after bounded, strict serialization. Hashing
    // captured bytes avoids re-reading a caller-owned mutable list or payload.
    internal static string ComputeSerializedSnapshot(JsonElement snapshot)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("ContractName", WorkspaceContinuationSnapshot.ContractName);
            writer.WritePropertyName("Snapshot");
            WriteCanonical(snapshot, writer);
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    // An anonymous wrapper has getter-only properties and would serialize as {}
    // with the state-projection options above. Init properties are intentional.
    private sealed record DigestEnvelope(string ContractName, WorkspaceContinuationSnapshot Snapshot);

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject()
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
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
