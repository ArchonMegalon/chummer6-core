using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

/// <summary>
/// Reads the source tradition's spirit form. Chummer5 defaults an absent field
/// to Materialization; an explicit unsupported or malformed field is not a default.
/// This checks payload shape, not source admission or character eligibility.
/// </summary>
public static class CharacterTraditionSpiritFormRules
{
    public static bool TryRead(XElement source, out string spiritForm)
    {
        spiritForm = string.Empty;
        if (source.Name != "tradition") return false;
        XElement[] fields = source.Elements().Where(item => item.Name.LocalName == "spiritform").ToArray();
        if (fields.Length == 0)
        {
            spiritForm = "Materialization";
            return true;
        }
        if (fields.Length != 1 || fields[0].Name != "spiritform" || fields[0].HasAttributes
            || fields[0].Nodes().Any(node => node is not XText)
            || fields[0].Value is not ("Materialization" or "Possession" or "Inhabitation")) return false;
        spiritForm = fields[0].Value;
        return true;
    }
}
