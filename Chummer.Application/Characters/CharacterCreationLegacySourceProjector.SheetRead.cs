using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationLegacySourceProjector
{
    // Read-only admission. This does not expand any creation or Career mutation authority.
    // The ordinary compiler owns quality serialization and initiative precedence semantics.
    internal static bool TryBuildSheetQualityEffects(string xml, Guid instanceId,
        out XElement[] effects)
    {
        effects = [];
        var source = ParseBoundedSource(xml, "quality", s_QualitySourceChildren);
        if (source is null || instanceId == Guid.Empty) return false;
        var supplemental = new List<CompiledEffect>();
        // The native AddImprovementCollection scalar handlers use an empty target for
        // Dodge and Physical/Mental/Social for limits, with an empty unique name.
        // Consume every removed instruction explicitly; all other source nodes still
        // go through the existing fail-closed compiler.
        foreach (var container in source.Elements().Where(e => e.Name == "bonus" || e.Name == "firstlevelbonus"))
        {
            foreach (var node in container.Elements().ToArray())
            {
                string? type = node.Name.LocalName switch
                {
                    "dodge" => "Dodge", "physicallimit" => "PhysicalLimit",
                    "mentallimit" => "MentalLimit", "sociallimit" => "SocialLimit", _ => null
                };
                if (type is null) continue;
                if (node.Name.Namespace != XNamespace.None || node.HasAttributes || node.HasElements
                    || !decimal.TryParse(node.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out decimal value)) return false;
                string improvedName = type switch
                {
                    "PhysicalLimit" => "Physical", "MentalLimit" => "Mental", "SocialLimit" => "Social", _ => string.Empty
                };
                supplemental.Add(new(type, improvedName, value));
                node.Remove();
            }
        }
        if (!TryReadQualityDefinition(source, out var definition, sourceExecutionSchema:
                CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)) return false;
        effects = definition.BonusEffects.Concat(definition.FirstLevelEffects).Concat(supplemental)
            .Select(e => BuildImprovement(e, instanceId.ToString("D"), definition.NotesColor)).ToArray();
        return true;
    }
}
