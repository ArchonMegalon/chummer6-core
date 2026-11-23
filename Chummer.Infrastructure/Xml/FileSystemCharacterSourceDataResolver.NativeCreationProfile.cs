using System.Security.Cryptography;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Application.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    // Validate the declared native preset inside the ordinary source snapshot.
    // The same reads replay from retained inputs; no host/package access is added
    // to a cold restore. Prior profiles never consult this package.
    private static bool TryValidateNativeCreationPackage(XElement character, string settingsKey,
        XElement settings, IReadOnlyList<CustomDirectory> directories)
    {
        if (!NativeSr5CreationProfiles.IsNativeProfile(settingsKey)) return true;
        if (!CharacterCreationBootstrapProfiles.IsExactCanonicalTuple(ReadValue(character, "buildmethod"), settingsKey)
            || character.Elements(CharacterCreationLifestyleMechanicsRules.CapabilityElement).ToArray() is not [var marker]
            || marker.HasAttributes || marker.HasElements || marker.Value != CharacterCreationLifestyleMechanicsRules.Capability
            || settings.Elements("customdatadirectorynames").ToArray() is not [var selection]
            || selection.Elements().Any(entry => entry.Name != "customdatadirectoryname")
            || selection.Elements("customdatadirectoryname").Count(entry =>
                ReadValue(entry, "directoryname") == NativeSr5CreationProfiles.PackageSelectionKey
                && ParseBool(ReadValue(entry, "enabled"))) != 1)
            return false;
        // The native preset pins its own package, not the entire selected ruleset.
        // Other explicitly selected packs still use ordinary ordered resolution,
        // source capture and the saved-directory equality check in the caller.
        var required = directories.Where(directory =>
            directory.ManifestId?.ToString("D") == NativeSr5CreationProfiles.PackageId).ToArray();
        if (required.Length != 1) return false;
        CustomDirectory directory = required[0];
        if (directory.Name != NativeSr5CreationProfiles.PackageDirectory || !directory.ManifestValid
            || directory.ManifestId?.ToString("D") != NativeSr5CreationProfiles.PackageId
            || string.Join('.', directory.Version.Parts) != NativeSr5CreationProfiles.PackageVersion)
            return false;

        (string Name, string Sha256)[] assets =
        [
            ("amend_lifestyles.xml", "11465b9166712163fa92825021ec8f15f5060fdee8a3c4f0629782ea951d85e2"),
            ("manifest.xml", "7c4253da43bff2a32ce73a53680bc1db7e52967298c6ff340091dd92242f4cc7"),
        ];
        // Product documentation lives outside customdata. Keep the runtime
        // package's exact two-file inventory and both executable asset hashes.
        string[] files = EnumerateSourceFiles(directory.Path, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory.Path, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal).ToArray();
        if (!files.SequenceEqual(assets.Select(asset => asset.Name), StringComparer.Ordinal)) return false;
        foreach (var asset in assets)
        {
            string path = Path.Combine(directory.Path, asset.Name);
            if (!SourceFileExists(path)
                || Convert.ToHexString(SHA256.HashData(ReadSourceBytes(path))).ToLowerInvariant() != asset.Sha256)
                return false;
        }
        return true;
    }

    // Common live/capture/retained context boundary. Read both declarations from
    // the SAME source snapshot, after selected-settings amendments were resolved.
    // An explicitly cloned resolver is configuration, never capability authority.
    private bool TrySelectKarmaQualityPromptResolver2742(XElement character, string settingsKey,
        XElement settings, out FileSystemCharacterSourceDataResolver? selected)
    {
        selected = null;
        const string element = NativeSr5CreationProfiles.KarmaQualityPromptElement;
        var characterMarkers = character.Elements().Where(node => node.Name.LocalName == element).ToArray();
        var profileMarkers = settings.Elements().Where(node => node.Name.LocalName == element).ToArray();
        bool promptedProfile = settingsKey == NativeSr5CreationProfiles.KarmaQualityPrompts;
        if (!promptedProfile && characterMarkers.Length == 0 && profileMarkers.Length == 0)
        {
            selected = ForKarmaQualityPrompts(null);
            return selected is not null;
        }
        if (!promptedProfile || !ExactMarker(characterMarkers) || !ExactMarker(profileMarkers)
            || !ExactLeaf(character, "settings", settingsKey)
            || !ExactLeaf(character, "buildmethod", CharacterCreationBuildMethods.Karma)
            || !ExactLeaf(character, "gameedition", "SR5")
            || !ExactLeaf(settings, "id", settingsKey)
            || !ExactLeaf(settings, "buildmethod", CharacterCreationBuildMethods.Karma)
            // V1-V6 keep their original grammar; this capability belongs to V7.
            || _ruleSourceAccounting?.HasHistoricalCurrentSemantics2742 == true)
            return false;
        selected = ForKarmaQualityPrompts(CharacterCreationKarmaQualityPrompts.SourceSchema);
        return selected is not null;

        static bool ExactMarker(XElement[] nodes)
            => nodes is [var marker] && marker.Name == NativeSr5CreationProfiles.KarmaQualityPromptElement && !marker.HasAttributes
                && marker.Nodes().ToArray() is [XText text] && text.GetType() == typeof(XText)
                && text.Value == CharacterCreationKarmaQualityPrompts.SourceSchema;
        static bool ExactLeaf(XElement parent, string name, string value)
            => parent.Elements().Where(node => node.Name.LocalName == name).ToArray() is [var leaf]
                && leaf.Name == name && !leaf.HasAttributes && !leaf.HasElements && leaf.Value == value;
    }

    private sealed partial class SourceDataContext : ICurrentKarmaQualityPromptSource2742
    {
        public bool HasKarmaQualityPrompts2742
            => _sourceInputs.KarmaQualityPromptSchema == CharacterCreationKarmaQualityPrompts.SourceSchema
                && _buildMethod == CharacterCreationBuildMethods.Karma
                && _settingsProfileId == NativeSr5CreationProfiles.KarmaQualityPrompts
                && _sourceInputs.Accounting?.HasHistoricalCurrentSemantics2742 != true;

        public bool TryReadKarmaQualityPromptCatalog2742(out CharacterCreationKarmaQualitiesCatalog? catalog)
        {
            catalog = null;
            // Source availability is distinct from a new purchase/budget authority.
            // Career owners are checked against their archive AND this current source
            // projection. Never expose this saved-instance-free projection to quoting.
            return HasKarmaQualityPrompts2742
                && TryResolveCreationQualitySources(out var source, sourceAvailabilityOnly2742: true)
                && source.IsAuthoritative && source.Blockers.Count == 0
                && TryProjectKarmaQualitiesCatalog2742(source, out catalog);
        }
    }

    private sealed partial class RuleSourceAccounting
    {
        // Includes V1, which is observation-only and is not a native/current
        // execution profile. It must not acquire V7 grammar through this reader.
        internal bool HasHistoricalCurrentSemantics2742
            => currentEnvironmentProfile is not null && !HasLifestyleMechanics2684;
    }

}
