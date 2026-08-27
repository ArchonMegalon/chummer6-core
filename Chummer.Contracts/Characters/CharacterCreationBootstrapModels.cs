using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Contracts.Rulesets;
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

public static class CharacterCreationBootstrapRevisions
{
    public const long InitialContentRevision = 1;
    public const long InitialSavedRevision = 0;
}

/// <summary>
/// The only SR5 creation-profile identities that may seed an authority-bound
/// pending-selection workspace. A resolvable settings row is not sufficient:
/// method and profile id are one exact, case-sensitive tuple.
/// </summary>
public static class CharacterCreationBootstrapProfiles
{
    public const string PrioritySettingsProfileId =
        "223a11ff-80e0-428b-89a9-6ef1c243b8b6";
    public const string SumToTenSettingsProfileId =
        "3509a807-68ee-4c18-b7d5-b130313b4b77";
    public const string KarmaSettingsProfileId =
        "fe7bb0d9-3cd9-4a75-825e-135b95a4f3ef";
    public const string LifeModulesSettingsProfileId =
        "8a31af6d-7137-4284-872b-7d8087e156c6";

    /// <summary>
    /// Resolves the one canonical settings profile that Core permits for a
    /// supported SR5 creation method. Presentation callers must use this
    /// mapping instead of copying profile identifiers into a UI repository.
    /// </summary>
    public static bool TryResolveCanonicalSettingsProfileId(
        string? buildMethod,
        out string settingsProfileId)
    {
        settingsProfileId = buildMethod switch
        {
            CharacterCreationBuildMethods.Priority => PrioritySettingsProfileId,
            CharacterCreationBuildMethods.SumToTen => SumToTenSettingsProfileId,
            CharacterCreationBuildMethods.Karma => KarmaSettingsProfileId,
            CharacterCreationBuildMethods.LifeModules => LifeModulesSettingsProfileId,
            _ => string.Empty
        };
        return settingsProfileId.Length != 0;
    }

    public static bool IsExactCanonicalTuple(string? buildMethod, string? settingsProfileId)
        => TryResolveCanonicalSettingsProfileId(buildMethod, out string expected)
           && string.Equals(settingsProfileId, expected, StringComparison.Ordinal);

    public static string[] ExpectedSourceAnchorIds(
        string buildMethod,
        string settingsProfileId)
    {
        if (!IsExactCanonicalTuple(buildMethod, settingsProfileId))
            return [];

        string settingsAnchor = $"settings.xml#setting:{settingsProfileId}";
        return buildMethod is CharacterCreationBuildMethods.Priority
                or CharacterCreationBuildMethods.SumToTen
            ? ["metatypes.xml", "priorities.xml", settingsAnchor, "skills.xml"]
            : ["metatypes.xml", settingsAnchor];
    }

    public static bool HasExactCanonicalSourceAnchors(
        string buildMethod,
        string settingsProfileId,
        IReadOnlyList<string>? sourceAnchorIds)
    {
        string[] expected = ExpectedSourceAnchorIds(buildMethod, settingsProfileId);
        return expected.Length > 0
               && sourceAnchorIds is not null
               && sourceAnchorIds.Count == expected.Length
               && sourceAnchorIds.SequenceEqual(expected, StringComparer.Ordinal);
    }
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
    public const string SourceAnchorsInvalid = "creation-bootstrap-source-anchors-invalid";
    public const string AtomicCreateUnavailable = "creation-bootstrap-atomic-create-unavailable";
    public const string WorkspaceCreateFailed = "creation-bootstrap-workspace-create-failed";
    public const string ActivationProjectionUnavailable =
        "creation-bootstrap-activation-projection-unavailable";
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
    long InitialContentRevision,
    long InitialSavedRevision,
    string RawCharacterXmlDigest,
    string RawProfileInputsDigest,
    string MetatypeAuthorityDigest,
    string PrerequisiteAuthorityDigest,
    string SettingsSourceAnchor,
    IReadOnlyList<string> SourceAnchorIds,
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
    IReadOnlyList<string> SourceAnchorIds,
    string ReceiptDigest);

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
           && string.Equals(
               binding.Schema,
               CharacterCreationBootstrapSchemas.BindingV1,
               StringComparison.Ordinal)
           && string.Equals(
               binding.Stage,
               CharacterCreationBootstrapStages.AwaitingFoundationSelection,
               StringComparison.Ordinal)
           && !string.IsNullOrWhiteSpace(binding.WorkspaceId.Value)
           && string.Equals(binding.RulesetId, RulesetDefaults.Sr5, StringComparison.Ordinal)
           && CharacterCreationBootstrapProfiles.IsExactCanonicalTuple(
               binding.BuildMethod,
               binding.SettingsProfileId)
           && binding.InitialContentRevision
               == CharacterCreationBootstrapRevisions.InitialContentRevision
           && binding.InitialSavedRevision
               == CharacterCreationBootstrapRevisions.InitialSavedRevision
           && IsCanonical(binding.RawCharacterXmlDigest)
           && IsCanonical(binding.RawProfileInputsDigest)
           && IsCanonical(binding.MetatypeAuthorityDigest)
           && (binding.BuildMethod is CharacterCreationBuildMethods.Priority
                   or CharacterCreationBuildMethods.SumToTen
               ? IsCanonical(binding.PrerequisiteAuthorityDigest)
               : string.IsNullOrEmpty(binding.PrerequisiteAuthorityDigest))
           && string.Equals(
               binding.SettingsSourceAnchor,
               $"settings.xml#setting:{binding.SettingsProfileId}",
               StringComparison.Ordinal)
           && CharacterCreationBootstrapProfiles.HasExactCanonicalSourceAnchors(
               binding.BuildMethod,
               binding.SettingsProfileId,
               binding.SourceAnchorIds)
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

public static class CharacterCreationBootstrapReceiptDigest
{
    private const string Prefix = "sha256:";

    public static string Compute(CharacterCreationBootstrapReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        JsonElement root = JsonSerializer.SerializeToElement(
            receipt with { ReceiptDigest = string.Empty });
        ArrayBufferWriter<byte> buffer = new();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonical(root, writer);
        }

        return Prefix + Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    public static bool IsValid(CharacterCreationBootstrapReceipt? receipt)
        => receipt is not null
           && string.Equals(
               receipt.Schema,
               CharacterCreationBootstrapSchemas.ReceiptV1,
               StringComparison.Ordinal)
           && CharacterCreationBootstrapBindingDigest.IsValid(receipt.Binding)
           && receipt.WorkspaceId == receipt.Binding.WorkspaceId
           && receipt.ContentRevision == receipt.Binding.InitialContentRevision
           && receipt.SavedRevision == receipt.Binding.InitialSavedRevision
           && receipt.ContentRevision == CharacterCreationBootstrapRevisions.InitialContentRevision
           && receipt.SavedRevision == CharacterCreationBootstrapRevisions.InitialSavedRevision
           && CharacterCreationBootstrapProfiles.HasExactCanonicalSourceAnchors(
               receipt.Binding.BuildMethod,
               receipt.Binding.SettingsProfileId,
               receipt.SourceAnchorIds)
           && receipt.SourceAnchorIds.SequenceEqual(
               receipt.Binding.SourceAnchorIds,
               StringComparer.Ordinal)
           && CharacterCreationBootstrapBindingDigest.IsCanonical(receipt.ReceiptDigest)
           && CharacterCreationBootstrapBindingDigest.FixedTimeEquals(
               receipt.ReceiptDigest,
               Compute(receipt));

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
                throw new InvalidOperationException(
                    "Unsupported bootstrap receipt JSON value kind.");
        }
    }
}
