using System.Xml.Linq;

namespace Chummer.Application.Characters;

/// <summary>The existing creation defaults, shared by live resolution and private execution.</summary>
public static class CharacterCreationSkillGroupPolicyAuthority
{
    public static bool TryResolve(XElement settings, out bool usePointsOnBrokenGroups,
        out bool strictGroups, out bool specializationsBreakGroups)
    {
        bool brokenValid = TryReadBoolean(settings, "usepointsonbrokengroups", false, out usePointsOnBrokenGroups);
        bool strictValid = TryReadBoolean(settings, "breakskillgroupsincreatemode", false, out strictGroups);
        bool specializationValid = TryReadBoolean(settings, "specializationsbreakskillgroups", true, out specializationsBreakGroups);
        return brokenValid && strictValid && specializationValid;
    }

    internal static bool TryReadBoolean(XElement settings, string name, bool defaultWhenMissing, out bool value)
    {
        XElement[] nodes = settings.Elements(name).Take(2).ToArray();
        value = defaultWhenMissing;
        if (nodes.Length == 0) return true;
        value = false;
        return nodes.Length == 1 && !nodes[0].HasAttributes && !nodes[0].HasElements
            && string.Equals(nodes[0].Value, nodes[0].Value.Trim(), StringComparison.Ordinal)
            && bool.TryParse(nodes[0].Value, out value);
    }
}
