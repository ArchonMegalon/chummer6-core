using System.Xml.Linq;

namespace Chummer.Application.Characters;

// Shared Magic evaluates the exact source-admitted settings/metatype and saved graph.
// Power grants, purchases, effects, governance and source replay remain in this lane.
internal static partial class CharacterCareerPowerGraph
{
    private static int Magic(XElement root, ICharacterSourceDataContext context, XElement settings,
        XElement metatype, XElement effects, List<string> evidence)
    {
        var evaluated = CharacterCareerMagicEvaluator.Evaluate(root, settings, metatype);
        Need(evaluated.IsSupported, "power_" + string.Join(";", evaluated.Blockers));
        Need(evaluated.EffectivePowerMagic > 0, "power_magic_allocation_invalid");
        // InitiationDisabled denies new initiation only; it does not revoke owned PP.
        return evaluated.EffectivePowerMagic;
    }
}
