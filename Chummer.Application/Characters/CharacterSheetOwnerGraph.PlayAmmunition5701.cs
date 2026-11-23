using System.Xml.Linq;
using Chummer.Application.Session;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    // Called only by the complete sheet calculation, or its private retained
    // evaluation. Neither the context nor this owner graph is returned to Play.
    internal bool MatchesAmmunitionContext5701(XElement calculation, ICharacterSourceDataContext candidate)
        => ReferenceEquals(context, candidate) && MatchesAmmunitionGraph5701(calculation);

    private bool MatchesAmmunitionGraph5701(XElement calculation)
    {
        if (!CharacterCreationFinalizationDigest.IsCanonical(AuthorityDigest) || !checks.All(check => check())) return false;
        if (CharacterCreationOwnedGearGraph1233.GraphDigest(root) == CharacterCreationOwnedGearGraph1233.GraphDigest(calculation)) return true;
        // The current-environment compiler uses the exact reconciled effects.
        // No other field, owner, clip, source byte or selected value may change.
        var compiled = new XElement(root);
        compiled.Elements("improvements").Remove();
        compiled.Add(new XElement("improvements", Effects.Select(row => new XElement(row))));
        return CharacterCreationOwnedGearGraph1233.GraphDigest(compiled) == CharacterCreationOwnedGearGraph1233.GraphDigest(calculation);
    }

    // Domain classification shares the actual consumers: Other's scalar
    // channels, the sheet's defaulting predicate, and the existing typed skill
    // purchase-cost kernel. A shot has no purchase interval; the physical
    // routine additionally requires a learned skill, and never defaults.
    // This does not price a purchase or discard any reconciled effect.
    internal static bool HasPhysicalNonAttackConsumer5701(string type)
        => OtherScalarChannels5701.ContainsKey(type) || IsDefaultingChannel5701(type)
            || CharacterCreationSkillKarmaModifierRules.IsSupportedType(type);

    internal bool ConsumesPhysicalNonAttack5701(XElement calculation, string type)
        => HasPhysicalNonAttackConsumer5701(type) && MatchesAmmunitionGraph5701(calculation);

    internal IReadOnlyList<CharacterWeaponFireState> ProjectAmmunition5701(XElement calculation,
        List<SessionPlayDiagnostic> diagnostics, out SessionPlayAmmunition? ammunition)
    {
        ammunition = null;
        if (!MatchesAmmunitionGraph5701(calculation) || DefenseUnavailableReason is not null
            || CharacterPhysicalRoutineEffectBoundary3828.UnsupportedReason(calculation, this) is not null) return [];
        var result = SessionPlayWeaponAuthority.ResolveForSheet5701(calculation, context, this, diagnostics, out var projected);
        if (!MatchesAmmunitionGraph5701(calculation)) return [];
        ammunition = projected;
        return result;
    }

    internal bool IsCapacityFreeAmmunition5701(XElement node)
        => node.Name == "gear" && node.Parent?.Name == "gears" && node.Parent.Parent?.Name == "character"
            && owners.Any(owner => owner.Kind == "Gear"
                && CharacterCreationOwnedGearGraph1233.GraphDigest(owner.Saved) == CharacterCreationOwnedGearGraph1233.GraphDigest(node))
            && checks.All(check => check())
            && CharacterCreationOriginalInventory3475.IsCapacityFreeOrdinaryAmmunition5701(node, context)
            && checks.All(check => check());
}
