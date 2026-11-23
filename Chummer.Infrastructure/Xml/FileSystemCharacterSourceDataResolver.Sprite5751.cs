using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        // Native saves the Critter tab. It selects exactly one catalog; the owner
        // graph must then prove that tab from the selected source. No lookup
        // failure can fall through to the other catalog, including Free Sprite.
        private bool UsesSpriteCatalog5751 => UsesImportedCareerOwnerSemantics5698 && SelectsSpriteCatalog5751;

        private bool SelectsSpriteCatalog5751 =>
            _character.Elements("critter").ToArray() is [var critter] && !critter.HasAttributes && !critter.HasElements
            && bool.TryParse(critter.Value, out bool enabled) && enabled
            && _character.Elements("metatypecategory").ToArray() is [var category] && !category.HasAttributes && !category.HasElements
            && (category.Value.EndsWith("Sprites", StringComparison.Ordinal)
                || category.Value == CharacterFreeSpriteConversionRules.FreeSpriteCategory);

        private bool CaptureSpriteDependencies5751()
        {
            // Live capture has no imported-owner accounting yet. Retain the exact
            // selected catalog inputs before that later admission, without granting
            // source/owner authority or switching the live resolver's semantics.
            if (!SelectsSpriteCatalog5751
                || _buildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
                || _character.Elements("created").ToArray() is not [var created]
                || created.HasAttributes || created.HasElements
                || !bool.TryParse(created.Value, out bool finished) || !finished) return true;
            foreach (string file in new[] { "critters.xml", "critterpowers.xml" })
            {
                if (!TryComputeEffectiveInputDigest(_catalog, file, out _)
                    || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, file, out _)) return false;
                // Preserve the precise membership query replay uses, including an
                // empty result. The existing exporter retains every observed byte.
                foreach (CustomDirectory directory in _customDirectories)
                    _ = EnumerateSourceFiles(directory.Path, $"*_{file}", SearchOption.AllDirectories);
            }
            if (!UsesSpriteCatalog5751) return true;
            string identity = _character.Element("metatypeguid")?.Value ?? _character.Element("metatypeid")?.Value
                ?? _character.Element("metatype")?.Value ?? "";
            if (!TryResolveSheetNamedSource("Metatype", identity, out var source) || source is null) return false;
            var row = XElement.Parse(source.SourceXml);
            if (!CharacterSpriteSource5751.SupportsSource(row)) return false;
            foreach (var power in row.Elements("powers").Elements())
                if (!TryResolveSheetGrantSource("CritterPower", power.Value, out _)) return false;
            foreach (var skill in row.Elements("skills").Elements())
                if (!TryResolveSheetNamedSource("Skill", skill.Value, out _)) return false;
            foreach (var power in _character.Elements("critterpowers").Elements("critterpower"))
                if (!Guid.TryParse(power.Element("sourceid")?.Value, out var id)
                    || !TryResolveSheetOwnerSource("CritterPower", id, out _)) return false;
            // Absence is a retained fact too. Stock contains no Denial: capture
            // must remain possible so review can explain its missing source.
            _ = TryResolveSheetOwnerSource("CritterPower", CharacterFreeSpriteConversionRules.DenialSourceId, out _);
            return !_sourceInputs.HasSourceDrift;
        }
    }
}
