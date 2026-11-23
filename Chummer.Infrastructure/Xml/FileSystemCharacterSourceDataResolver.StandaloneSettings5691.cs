using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Content;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver : ICharacterRetainedSettingsExportSource
{
    private bool _standaloneSettingsExport5691;

    // Only a complete source export may bind a local settings namespace. A raw
    // .chum5 import has no such binding; it must not select a same-named preset.
    public ICharacterSourceDataResolver ForCompleteSourceExport()
        => new FileSystemCharacterSourceDataResolver(this, _creationSourceExecutionSchema, _karmaQualityPromptSchema)
            { _standaloneSettingsExport5691 = true };

    // An inventory operation in the existing retained I/O archive, not a disk
    // wildcard. It records only this filename's case-insensitive candidates.
    private const string SettingsInventory5691 = "retained-settings-file-v1:";

    private static IEnumerable<string> EnumerateSettingsFile5691(string directory, string pattern)
    {
        string name = pattern[SettingsInventory5691.Length..];
        if (!CharacterRetainedSettingsFile.IsFileSelector(name))
            throw RetainedCharacterRuleSourceInputs.Rejected();
        return Directory.EnumerateFiles(directory, name, new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseInsensitive, MatchType = MatchType.Simple,
            RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0
        });
    }

    private bool TryLoadStandaloneSettings5691(SourceInputSnapshot inputs, ContentOverlayCatalog catalog,
        string selection, out XDocument? document, out string digest)
    {
        document = null;
        digest = string.Empty;
        bool Refuse(string code, string message)
        {
            ReportCompatibilityDiagnostics([RetainedCharacterRuleSourceInputs.CompatibilityDiagnostic(
                code, "settings", null, null, message)]);
            return false;
        }
        if (_retainedInputs is null && !_standaloneSettingsExport5691)
            return Refuse("missing-settings", "A character-only import has no retained settings binding. Import a complete private rule environment with the original selected settings file and ordered custom data.");
        if (!CharacterRetainedSettingsFile.IsFileSelector(selection) || catalog.Overlays.Any(pack => pack.Enabled))
            return Refuse("settings-binding-invalid", "The selected standalone settings filename or source namespace is unsupported.");
        string directory = Path.Combine(Path.GetDirectoryName(catalog.BaseDataPath)!, "settings");
        if (!SourceDirectoryExists(directory))
            return Refuse("missing-settings", "Include the selected original settings file and its ordered custom data in a complete private rule-environment export from the source installation.");
        string[] candidates = inputs.EnumerateFiles(directory, SettingsInventory5691 + selection, SearchOption.TopDirectoryOnly);
        if (candidates.Length != 1 || Path.GetFileName(candidates[0]) != selection)
            return Refuse(candidates.Length == 0 ? "missing-settings" : "ambiguous-settings",
                "Exactly one original settings file must match the selected spelling in the source settings namespace; no preset fallback is allowed.");
        string path = candidates[0];
        if (Path.GetFullPath(path) != Path.GetFullPath(Path.Combine(directory, selection))
            || _retainedInputs is null && ((File.GetAttributes(directory) | File.GetAttributes(path)) & FileAttributes.ReparsePoint) != 0)
            return Refuse("settings-binding-invalid", "The selected settings file must belong directly to the source settings namespace.");
        if (!TryLoadXml(path, out var original) || original?.Root is not { } settings
            || settings.Name != "settings" || settings.HasAttributes
            || settings.Elements("id").ToArray() is not [var id] || id.HasAttributes || id.HasElements
            || settings.Elements("name").ToArray() is not [var name] || name.HasAttributes || name.HasElements)
            return Refuse("settings-binding-invalid", "The selected original file has an unsupported or ambiguous settings identity.");
        byte[] bytes = ReadSourceBytes(path);
        var identity = new CharacterRetainedSettingsFile(CharacterRetainedSettingsFile.LegacyNamespace,
            selection, "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), id.Value, name.Value);
        if (!identity.IsSelectedBy(selection) || inputs.HasSourceDrift)
            return Refuse("settings-binding-invalid", "The original standalone settings identity or bytes changed; re-export the complete source environment.");
        // Only the document wrapper is adapted. In particular the empty GUID,
        // original name and all policy nodes remain exactly the source's values.
        document = WrapStandaloneSettings5691(settings);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFramed(hash, Encoding.UTF8.GetBytes("retained-standalone-settings-v1"));
        AppendFramed(hash, Encoding.UTF8.GetBytes(identity.Namespace));
        AppendFramed(hash, Encoding.UTF8.GetBytes(identity.FileName));
        AppendFramed(hash, bytes);
        digest = "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        inputs.StandaloneSettings5691 = identity;
        inputs.StandaloneSettingsBase5691 = new XDocument(document);
        inputs.SetSelectedProfile("settings.xml", document, digest);
        return true;
    }

    private static XDocument WrapStandaloneSettings5691(XElement original)
        => new(new XElement("chummer", new XElement("settings", new XElement("setting", original.Nodes()))));

    private sealed partial class SourceDataContext
    {
        private bool TryResolveStandaloneSettings5691(string fileName, IReadOnlyList<string> containers,
            string entryName, string selection, out XElement? target)
        {
            target = null;
            var identity = _sourceInputs.StandaloneSettings5691;
            if (identity is null || !identity.IsSelectedBy(selection) || selection != _settingsProfileId
                || fileName != SettingsFile || entryName != "setting" || !containers.SequenceEqual(new[] { "settings" })
                || !TryLoadEffectiveDocument(_catalog, fileName, out var document)
                || document?.Root?.Element("settings")?.Elements("setting").ToArray() is not [var row]
                || ReadValue(row, "id") != identity.AuthorProvidedId || ReadValue(row, "name") != identity.AuthorProvidedName
                || _sourceInputs.HasSourceDrift) return false;
            target = new XElement(row);
            return true;
        }
    }
}
