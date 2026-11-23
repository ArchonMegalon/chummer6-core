using System.Globalization;
using System.Xml.Linq;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationLegacySourceProjector
{
    private static bool IsMatrixDiceEffect(string name)
        => name is "matrixinitiativedice" or "matrixinitiativepass" or "matrixinitiativediceadd" or "matrixinitiativepassadd";

    private static bool TryReadMatrixDiceEffect(XElement node, out CompiledEffect effect)
    {
        effect = null!;
        if (node.Name.Namespace != XNamespace.None || node.HasAttributes
            || node.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
            || !decimal.TryParse(node.Value.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal value)) return false;
        bool unique = node.Name.LocalName is "matrixinitiativedice" or "matrixinitiativepass";
        // Native AddImprovementCollection writes MatrixInitiativeDice even for
        // the ...add XML instruction. Do not invent a DiceAdd output channel.
        effect = new("MatrixInitiativeDice", string.Empty, value, Unique886: unique ? "matrixinitiativepass" : null);
        return true;
    }
}
