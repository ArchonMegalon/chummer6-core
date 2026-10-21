using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    // Called only after the private effects compiler matches this authority.
    // Join its actual instances to independently reconstructed Talent sources;
    // do not implement another grant rule or trust a caller-provided graph.
    private bool TryExecutedGrantedGear(IReadOnlyList<CharacterCreationGearLine> purchases,
        XElement graph, IReadOnlyList<CharacterCreationFinalizationDelta> deltas,
        out IReadOnlyList<CharacterCreationTalentGearSource> granted)
    {
        granted = Array.Empty<CharacterCreationTalentGearSource>();
        var qualities = Inputs.Magic.SelectedTalent.GrantedQualitySources
            ?? Array.Empty<CharacterCreationTalentQualitySource>();
        if (qualities.Any(quality => !CharacterCreationTalentQualitySourceRules.IsValidSource(quality)))
            return false;
        var sources = qualities.SelectMany(quality => (quality.GrantedGearSources
            ?? Array.Empty<CharacterCreationTalentGearSource>()).Select(gear => (Quality: quality, Gear: gear))).ToArray();
        var gearDeltas = deltas.Where(delta => delta.Kind == CharacterCreationFinalizationDeltaKinds.Gear).ToArray();
        var gearContainers = graph.Elements("gears").ToArray();
        var gearNodes = gearContainers.SelectMany(container => container.Elements("gear")).ToArray();
        if (gearContainers.Length > 1
            || gearNodes.Length != gearDeltas.Length
            || gearDeltas.Length != purchases.Count + sources.Length
            || gearDeltas.Select(delta => delta.DeltaId).Distinct(StringComparer.Ordinal).Count() != gearDeltas.Length)
            return false;

        // The existing private purchase execution owns the price calculation.
        // An unrecognized origin cannot be relabelled as a paid catalogue line.
        foreach (var purchase in purchases)
            if (gearDeltas.Count(delta => delta.DeltaId == "gear:" + purchase.OptionId
                    && delta.TargetId == purchase.SourceId.ToString("D")
                    && delta.KarmaCost == 0 && delta.NuyenCost == purchase.TotalCost) != 1)
                return false;

        var actualGrants = gearDeltas.Where(delta => delta.DeltaId.StartsWith("talent-gear:", StringComparison.Ordinal)).ToArray();
        if (actualGrants.Length != sources.Length) return false;
        for (int index = 0; index < sources.Length; index++)
        {
            var (quality, source) = sources[index];
            var delta = actualGrants[index];
            string instance = delta.DeltaId["talent-gear:".Length..];
            if (!Guid.TryParse(instance, out _)
                || !CharacterCreationTalentQualitySourceRules.IsValidGearSource(source)
                || delta.TargetId != source.SourceId || delta.TargetName != source.Name
                || delta.KarmaCost != 0 || delta.NuyenCost != 0
                || !delta.SourceAnchorIds.SequenceEqual(source.SourceAnchorIds, StringComparer.Ordinal))
                return false;
            var saved = gearNodes.Where(node => node.Element("guid")?.Value == instance).ToArray();
            if (saved is not [var gear]
                || gear.Element("sourceid")?.Value != source.SourceId
                || gear.Element("name")?.Value != source.Name
                || gear.Element("category")?.Value != source.Category
                || gear.Element("parentid")?.Value is not { } parent
                || !Guid.TryParse(parent, out _)
                || deltas.Count(row => row.DeltaId == "talent-quality:" + parent
                    && row.Kind == CharacterCreationFinalizationDeltaKinds.Quality
                    && row.TargetId == quality.SourceId
                    && row.SourceAnchorIds.SequenceEqual(quality.SourceAnchorIds, StringComparer.Ordinal)) != 1)
                return false;
        }
        // Other origins are not implemented by this Priority compiler. The
        // supported Talent grants are evaluated rather than withheld forever.
        granted = sources.Select(row => row.Gear).ToArray();
        return true;
    }
}
