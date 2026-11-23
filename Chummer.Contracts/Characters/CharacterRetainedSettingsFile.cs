namespace Chummer.Contracts.Characters;

/// <summary>
/// A selected legacy settings file, not a preset GUID. The namespace is relative
/// to the retained source root; the digest covers the original, unmodified bytes.
/// This describes source identity only and never grants execution authority.
/// </summary>
public sealed record CharacterRetainedSettingsFile(
    string Namespace, string FileName, string SourceSha256,
    string AuthorProvidedId, string AuthorProvidedName)
{
    public const string LegacyNamespace = "chummer5a/settings";

    public static bool IsFileSelector(string value)
        => !string.IsNullOrEmpty(value) && value.Length <= 255
            && value == value.Trim() && value.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("..", StringComparison.Ordinal)
            && !value.Any(c => char.IsControl(c) || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|');

    public bool IsSelectedBy(string selection)
        => Namespace == LegacyNamespace && FileName == selection && IsFileSelector(FileName)
            && SourceSha256 is { Length: 71 } && SourceSha256.StartsWith("sha256:", StringComparison.Ordinal)
            && SourceSha256.Skip(7).All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
            // This branch represents the actual empty-GUID standalone format.
            // Nonempty GUID selection keeps its existing scalar contract.
            && AuthorProvidedId == Guid.Empty.ToString("D")
            && !string.IsNullOrWhiteSpace(AuthorProvidedName);
}
