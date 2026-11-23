using System.Xml.Linq;

namespace Chummer.Application.Characters;

/// <summary>Effect coverage only. Success is never owner/source proof: callers
/// must also obtain the complete current sheet authority before using its pool.
/// In particular an invented or stale saved improvement cannot pass that proof.</summary>
internal static class CharacterPhysicalRoutineEffectBoundary3828
{
    internal static string? UnsupportedReason(XElement root, CharacterSheetOwnerGraph? provenOwners5701 = null)
        => Inspect(root, type => provenOwners5701?.ConsumesPhysicalNonAttack5701(root, type) == true);

    // Diagnostic preflight only. Classified source channels still require the private
    // graph in ProjectAmmunition5701 before any attack or round state is returned.
    internal static string? UnsupportedShape5701(XElement root)
        => Inspect(root, CharacterSheetOwnerGraph.HasPhysicalNonAttackConsumer5701);

    private static string? Inspect(XElement root, Func<string, bool> consumedNonAttack)
    {
        if (root.Descendants("limitmodifier").Any()
            || root.Elements().Any(e => (e.Name.LocalName is "sustained" or "sustainedspells" or "drugs") && e.HasElements))
            return "Contextual limits, sustained effects and drugs need an action adapter; all selections are retained.";
        foreach (var row in root.Descendants("improvement"))
        {
            string type = row.Element("improvementttype")?.Value ?? "";
            // Attribute and SkillModifier channels are already in the actual sheet
            // pool. Initiative channels are source-proven by that same sheet and
            // do not change this first ordinary shot. DisableQuality is resolved
            // by the graph's owner activation/reconciliation before that pool.
            // Do not add any effect a second time or discard its owner.
            if (type is not ("Attribute" or "Skill" or "SkillGroup" or "SkillCategory" or "SkillAttribute"
                or "Initiative" or "InitiativeDice" or "InitiativeDiceAdd" or "DisableQuality")
                && !consumedNonAttack(type))
                return "Physical action effect '" + type + "' has no consumed attack/custody semantics in this adapter.";
            if (row.Element("condition")?.Value is { Length: > 0 })
                return "Conditional effect '" + type + "' needs its actual attack context; a generic pool is not substituted.";
        }
        return null;
    }
}
