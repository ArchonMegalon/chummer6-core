using System.Globalization;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver : ICharacterCreationBootstrapSourceResolver
{
    public bool TryPrepareNativeSr5Creation(string initialCharacterXml, out string characterXml,
        out ICharacterSourceDataContext? sourceContext)
    {
        characterXml = string.Empty;
        sourceContext = null;
        // A retained archive admits its exact original XML, never new creation.
        if (_retainedInputs is not null) return false;
        try
        {
            var bootstrap = new NativeCreationBootstrapSelection();
            SourceDataContext? context = TryCreateContextCore(initialCharacterXml, null, bootstrap: bootstrap);
            if (context is null || bootstrap.CharacterXml is null
                || !context.TryAdmitReuse(FreezeContentOverlayCatalog(_overlays.GetCatalog())))
                return false;
            characterXml = bootstrap.CharacterXml;
            sourceContext = context;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException
            or IOException or InvalidDataException or InvalidOperationException
            or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }
    }

    private sealed class NativeCreationBootstrapSelection
    {
        internal string? CharacterXml { get; private set; }

        internal bool TrySeed(XDocument document, string settingsKey, IReadOnlyList<CustomDirectory> directories,
            out string characterXml)
        {
            characterXml = string.Empty;
            XElement? character = document.Root;
            if (character is null || character.Name != "character"
                || !NativeSr5CreationProfiles.IsNativeProfile(settingsKey)
                || !CharacterCreationBootstrapProfiles.IsExactCanonicalTuple(
                    RulesetDefaults.Sr5, ReadValue(character, "buildmethod"), settingsKey)
                || !ExactLeaf(character, "settings", settingsKey)
                || !ExactLeaf(character, "gameedition", "SR5")
                || !ExactLeaf(character, "created", "False")
                || character.Elements().Any(node => node.Name.LocalName is "customdatadirectorynames" or "metatype")
                || character.Elements().Where(node => node.Name.LocalName == CharacterCreationBootstrapXml.MarkerElement)
                    .ToArray() is not [var marker]
                || marker.Name != CharacterCreationBootstrapXml.MarkerElement || marker.HasAttributes
                || marker.Elements().Count() != 2
                || !ExactLeaf(marker, CharacterCreationBootstrapXml.SchemaElement, CharacterCreationBootstrapSchemas.MarkerV1)
                || !ExactLeaf(marker, CharacterCreationBootstrapXml.StageElement,
                    CharacterCreationBootstrapStages.AwaitingFoundationSelection))
                return false;

            // The shared resolver already discovered, ordered and validated these
            // selected directories and the pinned native package in this snapshot.
            // Keep its saved/source equality and all subsequent source reads intact.
            character.Add(new XElement("customdatadirectorynames", directories.Select(directory =>
                new XElement("directoryname", directory.Name))));
            using StringWriter writer = new(CultureInfo.InvariantCulture);
            document.Save(writer, SaveOptions.DisableFormatting);
            characterXml = CharacterXml = writer.ToString();
            return true;
        }

        private static bool ExactLeaf(XElement parent, string name, string value)
            => parent.Elements().Where(node => node.Name.LocalName == name).ToArray() is [var leaf]
                && leaf.Name == name && !leaf.HasAttributes && !leaf.HasElements && leaf.Value == value;
    }
}
