using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

/// <summary>
/// Typed request and persisted binding schemas for an uncreated SR5 character whose
/// metatype has deliberately not been selected yet.  The request marker is state,
/// never source authority; authority is resolved and rebound by Core before use.
/// </summary>
public static class CharacterCreationBootstrapSchemas
{
    public const string RequestV1 = "chummer.character_creation_bootstrap_request.v1";
    public const string MarkerV1 = "chummer.character_creation_bootstrap_marker.v1";
    public const string BindingV1 = "chummer.character_creation_bootstrap_binding.v1";
    public const string ReceiptV1 = "chummer.character_creation_bootstrap_receipt.v1";
}

public static class CharacterCreationBootstrapStages
{
    public const string AwaitingFoundationSelection = "awaiting-foundation-selection";
}

public static class CharacterCreationBootstrapXml
{
    public const string MarkerElement = "creationbootstrap";
    public const string SchemaElement = "schema";
    public const string StageElement = "stage";
}

public static class CharacterCreationBootstrapOutcomes
{
    public const string Success = "success";
    public const string Invalid = "invalid";
    public const string Conflict = "conflict";
    public const string Unavailable = "unavailable";
}

public static class CharacterCreationBootstrapBlockers
{
    public const string RequestSchemaInvalid = "creation-bootstrap-request-schema-invalid";
    public const string RequestStageInvalid = "creation-bootstrap-request-stage-invalid";
    public const string RulesetSr5Required = "creation-bootstrap-ruleset-sr5-required";
    public const string DisplayIdentityRequired = "creation-bootstrap-display-identity-required";
    public const string BuildMethodInvalid = "creation-bootstrap-build-method-invalid";
    public const string SettingsProfileInvalid = "creation-bootstrap-settings-profile-invalid";
    public const string CharacterDocumentInvalid = "creation-bootstrap-character-document-invalid";
    public const string MarkerInvalid = "creation-bootstrap-marker-invalid";
    public const string MarkerDuplicate = "creation-bootstrap-marker-duplicate";
    public const string MetatypeAlreadySelected = "creation-bootstrap-metatype-already-selected";
    public const string CharacterAlreadyCreated = "creation-bootstrap-character-already-created";
    public const string PreselectedCreationState = "creation-bootstrap-preselected-creation-state";
    public const string BindingMissing = "creation-bootstrap-binding-missing";
    public const string BindingInvalid = "creation-bootstrap-binding-invalid";
    public const string BindingStale = "creation-bootstrap-binding-stale";
    public const string SourceContextUnavailable = "creation-bootstrap-source-context-unavailable";
    public const string SourceProfileUnavailable = "creation-bootstrap-source-profile-unavailable";
    public const string SourceProfileInvalid = "creation-bootstrap-source-profile-invalid";
    public const string MetatypeAuthorityUnavailable = "creation-bootstrap-metatype-authority-unavailable";
    public const string PrerequisiteAuthorityUnavailable = "creation-bootstrap-prerequisite-authority-unavailable";
    public const string AtomicCreateUnavailable = "creation-bootstrap-atomic-create-unavailable";
    public const string WorkspaceCreateFailed = "creation-bootstrap-workspace-create-failed";
}

public sealed record CharacterCreationBootstrapRequest(
    string Schema,
    string Stage,
    string RulesetId,
    string Name,
    string Alias,
    string BuildMethod,
    string SettingsProfileId);

/// <summary>
/// Resolver-produced, digest-bound evidence persisted atomically with the new
/// workspace.  It is revalidated against current source inputs on every creation
/// service load and therefore cannot authorize a stale or ordinary import.
/// </summary>
public sealed record CharacterCreationBootstrapBinding(
    string Schema,
    string Stage,
    CharacterWorkspaceId WorkspaceId,
    string RulesetId,
    string BuildMethod,
    string SettingsProfileId,
    string RawCharacterXmlDigest,
    string RawProfileInputsDigest,
    string MetatypeAuthorityDigest,
    string PrerequisiteAuthorityDigest,
    string SettingsSourceAnchor,
    string BindingDigest);

public sealed record CharacterCreationBootstrapPreparation(
    string CharacterXml,
    CharacterFileSummary Summary,
    CharacterCreationBootstrapBinding Binding,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationBootstrapReceipt(
    string Schema,
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    CharacterFileSummary Summary,
    CharacterCreationBootstrapBinding Binding,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationBootstrapResult<T>(
    string Outcome,
    T? Value,
    IReadOnlyList<string> Blockers)
    where T : class;

public static class CharacterCreationBootstrapBindingDigest
{
    private const string Prefix = "sha256:";

    public static string Compute(CharacterCreationBootstrapBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        JsonElement root = JsonSerializer.SerializeToElement(
            binding with { BindingDigest = string.Empty });
        ArrayBufferWriter<byte> buffer = new();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonical(root, writer);
        }

        return Prefix + Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    public static bool IsCanonical(string? digest)
    {
        if (digest is not { Length: 71 }
            || !digest.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return digest.AsSpan(Prefix.Length).ToArray().All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    public static bool IsValid(CharacterCreationBootstrapBinding? binding)
        => binding is not null
           && IsCanonical(binding.BindingDigest)
           && FixedTimeEquals(binding.BindingDigest, Compute(binding));

    public static bool FixedTimeEquals(string? left, string? right)
    {
        byte[] leftBytes = System.Text.Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] rightBytes = System.Text.Encoding.UTF8.GetBytes(right ?? string.Empty);
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

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
                throw new InvalidOperationException("Unsupported bootstrap binding JSON value kind.");
        }
    }
}
