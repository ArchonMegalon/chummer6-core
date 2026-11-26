using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Single source-bound materializer for metatype base bonuses (Priority
/// Dwarf admission: 20% lifestyle surcharge). Shared by the ordinary
/// finalization projector and the completion/selected-graph execution so
/// both paths produce the identical Metatype-owned improvement element and
/// review delta from the same heritage projection. The source anchor always
/// points at the metatype bonus field; no UI or caller-side pricing is
/// involved.
/// </summary>
internal static class CharacterCreationMetatypeBaseBonusMaterializer
{
    internal static bool TryApply(
        CharacterCreationPriorityHeritageSelection heritage,
        XElement root,
        ICollection<CharacterCreationFinalizationDelta> deltas,
        ref int order,
        Func<XElement, string, XElement>? createContainer = null)
    {
        if (heritage.BaseBonuses is not { } bonuses)
            return true;
        if (bonuses.Armor < 0 || bonuses.Reach < 0 || bonuses.LifestyleCostPercent < 0)
            return false;
        var baseBonusImprovements = new List<XElement>();
        foreach (var (type, value, unique, sourceField) in new[]
        {
            ("Armor", bonuses.Armor, "group0", "armor"),
            ("Reach", bonuses.Reach, string.Empty, "reach"),
            ("LifestyleCost", bonuses.LifestyleCostPercent, string.Empty, "lifestylecost")
        })
        {
            if (value == 0) continue;
            baseBonusImprovements.Add(CharacterCreationAwakenedLegacyProjector.Improvement(type, string.Empty,
                heritage.MetatypeSourceId, "Metatype", value, unique));
            CharacterCreationFinalizationProjector.AddDelta(deltas, ref order, "priority-metatype-bonus:" + type,
                CharacterCreationFinalizationDeltaKinds.Metatype, heritage.MetatypeSourceId, "0",
                value.ToString(CultureInfo.InvariantCulture), 0, 0,
                ["metatypes.xml#metatype:" + heritage.MetatypeSourceId + "/bonus/" + sourceField],
                heritage.MetatypeName);
        }
        if (baseBonusImprovements.Count > 0)
        {
            XElement improvementsContainer = createContainer is { } custom
                ? custom(root, "improvements")
                : GetOrCreateDirectContainer(root, "improvements");
            improvementsContainer.Add(baseBonusImprovements);
        }
        return true;
    }

    private static XElement GetOrCreateDirectContainer(XElement root, string name)
    {
        XElement[] nodes = root.Elements(name).Take(2).ToArray();
        if (nodes.Length > 1)
            throw new InvalidDataException($"Duplicate {name} node.");
        if (nodes.Length == 1)
            return nodes[0];
        XElement created = new(name);
        root.Add(created);
        return created;
    }
}
