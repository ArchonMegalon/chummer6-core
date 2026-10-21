using System.Globalization;
using System.Xml.Linq;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationLegacySourceProjector
{
    private static bool IsPhysicalInitiativeEffect886(string name)
        => name is "initiative" or "initiativedice" or "initiativepass" or "initiativediceadd" or "initiativepassadd";

    private static bool TryReadPhysicalInitiativeEffect886(XElement node, out CompiledEffect effect)
    {
        effect = null!;
        // Literal unconditional instructions only. Rating/formulas, custom attributes,
        // suppression, dodge and attribute bonuses keep their own existing blockers.
        if (node.Name.Namespace != XNamespace.None || node.HasElements
            || node.Nodes().OfType<XProcessingInstruction>().Any()
            || node.Attributes().Any(a => a.Name != "precedence")
            || node.Attribute("precedence") is { Value: not ("0" or "1" or "-1") }
            || !decimal.TryParse(node.Value.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal value)) return false;
        bool dice = node.Name.LocalName is "initiativedice" or "initiativepass";
        string type = node.Name.LocalName == "initiative" ? "Initiative" : dice ? "InitiativeDice" : "InitiativeDiceAdd";
        // Only the dice handler reads precedence. Legacy initiative/additive handlers
        // receive the (empty, in this admitted container shape) container unique name.
        string? unique = dice ? node.Attribute("precedence") is { } precedence
            ? "precedence" + precedence.Value : node.Name.LocalName : null;
        effect = new(type, string.Empty, value, Unique886: unique);
        return true;
    }
}
