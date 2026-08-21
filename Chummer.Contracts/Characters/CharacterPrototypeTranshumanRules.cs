using System.Globalization;
using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

public sealed record CharacterPrototypeTranshumanNodeState(
    Guid CyberwareId,
    bool PrototypeTranshuman);

public sealed record CharacterPrototypeTranshumanSemantics(
    Guid CyberwareId,
    bool PrototypeTranshuman,
    decimal EssenceAllowance,
    IReadOnlyList<CharacterPrototypeTranshumanNodeState> Hierarchy);

/// <summary>
/// Exact saved-data authority for CharacterCreate.chkPrototypeTranshuman.
/// Chummer5 exposes the checkbox only for top-level Bioware while an enabled
/// PrototypeTranshuman improvement supplies a positive Essence allowance.
/// </summary>
public static class CharacterPrototypeTranshumanRules
{
    public static bool TryProject(
        XElement character,
        XElement cyberware,
        out CharacterPrototypeTranshumanSemantics semantics)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(cyberware);
        semantics = new CharacterPrototypeTranshumanSemantics(
            Guid.Empty,
            PrototypeTranshuman: false,
            EssenceAllowance: 0m,
            Hierarchy: Array.Empty<CharacterPrototypeTranshumanNodeState>());

        if (!TryReadCreationMode(character)
            || !IsDirectTopLevelCyberware(character, cyberware)
            || !string.Equals(ReadValue(cyberware, "improvementsource"), "Bioware", StringComparison.Ordinal)
            || !TryReadEssenceAllowance(character, out decimal essenceAllowance))
        {
            return false;
        }

        XElement[] allCyberware = character.Descendants("cyberware").ToArray();
        XElement[] hierarchy = EnumerateHierarchy(cyberware).ToArray();
        var states = new List<CharacterPrototypeTranshumanNodeState>(hierarchy.Length);
        var hierarchyIds = new HashSet<Guid>();
        foreach (XElement item in hierarchy)
        {
            if (!TryReadUniqueStableGuid(allCyberware, item, out Guid id)
                || !hierarchyIds.Add(id)
                || !TryReadOptionalBoolean(item, "prototypetranshuman", out bool selected))
            {
                return false;
            }
            states.Add(new CharacterPrototypeTranshumanNodeState(id, selected));
        }

        if (states.Count == 0)
        {
            return false;
        }

        semantics = new CharacterPrototypeTranshumanSemantics(
            states[0].CyberwareId,
            states[0].PrototypeTranshuman,
            essenceAllowance,
            states.ToArray());
        return true;
    }

    public static bool Matches(
        CharacterPrototypeTranshumanSemantics expected,
        CharacterPrototypeTranshumanSemantics current)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(current);
        return expected.CyberwareId == current.CyberwareId
            && expected.PrototypeTranshuman == current.PrototypeTranshuman
            && expected.EssenceAllowance == current.EssenceAllowance
            && expected.Hierarchy is not null
            && current.Hierarchy is not null
            && expected.Hierarchy.SequenceEqual(current.Hierarchy);
    }

    public static IEnumerable<XElement> EnumerateHierarchy(XElement cyberware)
    {
        ArgumentNullException.ThrowIfNull(cyberware);
        yield return cyberware;
        foreach (XElement child in EnumerateChildren(cyberware))
        {
            foreach (XElement descendant in EnumerateHierarchy(child))
            {
                yield return descendant;
            }
        }
    }

    private static bool TryReadCreationMode(XElement character)
    {
        XElement[] values = character.Elements("created").Take(2).ToArray();
        return values.Length == 1
            && bool.TryParse(values[0].Value, out bool created)
            && !created;
    }

    private static bool IsDirectTopLevelCyberware(XElement character, XElement cyberware)
    {
        XElement[] containers = character.Elements("cyberwares").Take(2).ToArray();
        return containers.Length == 1
            && containers[0].Elements("cyberware").Any(candidate => ReferenceEquals(candidate, cyberware));
    }

    private static bool TryReadEssenceAllowance(XElement character, out decimal allowance)
    {
        allowance = 0m;
        XElement[] improvements = character.Elements("improvements").Take(2).ToArray();
        if (improvements.Length != 1)
        {
            return false;
        }

        foreach (XElement improvement in improvements[0].Elements("improvement"))
        {
            XElement[] types = improvement.Elements("improvementttype").Take(2).ToArray();
            if (types.Length > 1)
            {
                return false;
            }
            if (types.Length != 1
                || !string.Equals(types[0].Value.Trim(), "PrototypeTranshuman", StringComparison.Ordinal))
            {
                continue;
            }
            if (!TryReadImprovementEnabled(improvement, out bool enabled))
            {
                return false;
            }
            if (!enabled)
            {
                continue;
            }

            XElement[] values = improvement.Elements("val").Take(2).ToArray();
            if (values.Length != 1
                || !decimal.TryParse(values[0].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
            {
                return false;
            }
            try
            {
                allowance += value;
            }
            catch (OverflowException)
            {
                return false;
            }
        }
        return allowance > 0m;
    }

    private static bool TryReadImprovementEnabled(XElement improvement, out bool enabled)
    {
        enabled = true;
        XElement[] values = improvement.Elements("enabled").Take(2).ToArray();
        if (values.Length > 1)
        {
            return false;
        }
        if (values.Length == 0)
        {
            return true;
        }

        string saved = values[0].Value.Trim();
        if (int.TryParse(saved, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
        {
            enabled = integer > 0;
            return true;
        }
        return bool.TryParse(saved, out enabled);
    }

    private static bool TryReadUniqueStableGuid(
        IReadOnlyList<XElement> allCyberware,
        XElement item,
        out Guid id)
    {
        id = Guid.Empty;
        XElement[] values = item.Elements("guid").Take(2).ToArray();
        if (values.Length != 1
            || !Guid.TryParseExact(values[0].Value.Trim(), "D", out Guid parsed)
            || parsed == Guid.Empty
            || allCyberware.Count(candidate =>
                Guid.TryParseExact(ReadValue(candidate, "guid"), "D", out Guid candidateId)
                && candidateId == parsed) != 1)
        {
            return false;
        }
        id = parsed;
        return true;
    }

    private static bool TryReadOptionalBoolean(XElement item, string name, out bool value)
    {
        value = false;
        XElement[] values = item.Elements(name).Take(2).ToArray();
        return values.Length switch
        {
            0 => true,
            1 => bool.TryParse(values[0].Value, out value),
            _ => false
        };
    }

    private static IEnumerable<XElement> EnumerateChildren(XElement item)
        => item.Elements("cyberware")
            .Concat(item.Element("children")?.Elements("cyberware") ?? Array.Empty<XElement>())
            .Concat(item.Element("cyberwares")?.Elements("cyberware") ?? Array.Empty<XElement>());

    private static string ReadValue(XElement item, string name)
        => item.Element(name)?.Value.Trim() ?? string.Empty;
}
