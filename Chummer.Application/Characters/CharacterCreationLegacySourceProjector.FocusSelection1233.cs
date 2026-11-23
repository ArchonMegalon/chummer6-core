using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationLegacySourceProjector
{
    internal static bool TryBuildFocusGear1233(CharacterCreationGearLine line, out XElement gear)
    {
        gear = new XElement("gear");
        if (line.Focus is not { Selection: { } focus }
            || line.LineDigest != CharacterCreationGearRules.ComputeLineDigest(line)
            || line.SourceNodeDigest != CharacterCreationGearRules.ComputeSourceNodeDigest(line.SourceNodeXml)
            || line.SourceNodeDigest != focus.SourceNodeDigest
            || focus.InstanceId == Guid.Empty || focus.FocusId == Guid.Empty || focus.InstanceId == focus.FocusId
            || (focus.Stack is null ? focus.Bonded != focus.FocusId.HasValue : !focus.Bonded || focus.FocusId.HasValue)
            || string.IsNullOrWhiteSpace(focus.OwnerWorkspaceId.Value) || focus.SelectionId == Guid.Empty
            || focus.Grade != string.Empty || line.Quantity != 1 || line.PackageQuantity != 1
            || !CharacterCreationFocusSelectionRules.TryReadSource(line.SourceNodeXml, out var source)
            || source!.SourceId != line.SourceId || source.Name != line.Name || source.Category != line.Category
            || source.SourceBook != line.SourceBook || source.Page != line.Page
            || focus.Rating < source.MinimumRating || focus.Rating > source.MaximumRating
            || !CharacterCreationFocusSelectionRules.TryEvaluate(source.CostExpression, focus.Rating, out var cost)
            || cost != line.PackageCost || cost != line.TotalCost
            || !CharacterCreationFocusSelectionRules.TryAvailability(source.AvailabilityExpression, focus.Rating, out var availability, out var legality)
            || availability != line.Availability || legality != line.Legality
            || focus.Extra is null || focus.Extra.Length > 256 || focus.Extra.Trim() != focus.Extra
            || (source.SelectTradition ? string.IsNullOrWhiteSpace(focus.Extra) || focus.ExtraSourceId is null
                || focus.ExtraSourceId == Guid.Empty : focus.Extra.Length != 0 || focus.ExtraSourceId is not null)) return false;

        // Reuse the native serializer only after all original source semantics have
        // been parsed. The normalized node is an internal serialization input, never
        // a source authority, source digest, or source-scope receipt.
        var original = XElement.Parse(line.SourceNodeXml);
        var scalar = new XElement(original);
        scalar.Element("bonus")?.Remove();
        // Executed against the final source-compiled quality graph by ValidateExtras.
        scalar.Element("required")?.Remove();
        scalar.Element("minrating")?.Remove();
        scalar.SetElementValue("rating", "0");
        scalar.SetElementValue("cost", cost.ToString(CultureInfo.InvariantCulture));
        scalar.SetElementValue("avail", availability.ToString(CultureInfo.InvariantCulture)
            + (legality == CharacterCreationGearLegality.Restricted ? "R" : legality == CharacterCreationGearLegality.Forbidden ? "F" : ""));
        if (!TryReadGearDefinition(scalar, out var definition)) return false;
        gear = BuildSavedGear(definition, focus.InstanceId.ToString("D"), 1);
        gear.SetElementValue("rating", focus.Rating.ToString(CultureInfo.InvariantCulture));
        gear.SetElementValue("minrating", original.Element("minrating")?.Value ?? string.Empty);
        gear.SetElementValue("maxrating", original.Element("rating")!.Value);
        gear.SetElementValue("cost", source.CostExpression);
        gear.SetElementValue("avail", source.AvailabilityExpression);
        gear.SetElementValue("extra", focus.Extra);
        gear.SetElementValue("bonded", LegacyBoolean(focus.Bonded));
        // The admitted source grammar permits only a text-free selecttradition
        // prompt. Gear.Save uses IsNullOrInnerTextIsEmpty and persists <bonus />
        // for this source. Tradition identity is still checked above and again
        // against the compiled character by ValidateExtras; source XML stays intact.
        gear.Element("bonus")!.ReplaceWith(new XElement("bonus"));
        // Gear has no grade source in this admitted grammar; absence is explicit.
        return true;
    }

    // CharacterCreate's native aggregate is constructed, not loaded from gear.xml.
    // It has Guid.Empty as sourceid; only the ordered components have sources/costs.
    internal static XElement BuildStackOwner1253(Guid id, string displayName, decimal cost)
    {
        var definition = new GearDefinition(Guid.Empty, displayName, "Stacked Focus", "", "", "0", 0,
            CharacterCreationGearLegality.Legal, 1, cost, cost.ToString(CultureInfo.InvariantCulture), "", "SR5", "1",
            false, "", "", "", 0, false, false, 1, 0, "", DefaultNotesColor, "", "", "", "", "", "", "", "", "", "", "", false);
        var owner = BuildSavedGear(definition, id.ToString("D"), 1);
        owner.SetElementValue("wirelesson", "True");
        return owner;
    }
}
