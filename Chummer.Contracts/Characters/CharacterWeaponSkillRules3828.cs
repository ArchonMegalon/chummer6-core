namespace Chummer.Contracts.Characters;

/// <summary>The existing SR5 Weapon category-to-skill mapping, shared by the
/// detached physical routine. Mirrors the pinned legacy Weapon mapping exactly;
/// routing that legacy callsite through here is outside this worker's scope.
/// Explicit useskill and exotic
/// skill identity resolution remain the caller's responsibility.</summary>
public static class CharacterWeaponSkillRules3828
{
    public static string DictionaryKeyForCategory(string strCategory, string useSkillSpec)
    {
        switch (strCategory)
        {
            case "Bows":
            case "Crossbows":
                return "Archery";

            case "Assault Rifles":
            case "Carbines":
            case "Machine Pistols":
            case "Submachine Guns":
                return "Automatics";

            case "Blades":
                return "Blades";

            case "Clubs":
            case "Improvised Weapons":
                return "Clubs";

            case "Exotic Melee Weapons":
                return "Exotic Melee Weapon (" + useSkillSpec + ")";

            case "Exotic Ranged Weapons":
            case "Special Weapons":
                return "Exotic Ranged Weapon (" + useSkillSpec + ")";

            case "Flamethrowers":
                return "Exotic Ranged Weapon (Flamethrowers)";

            case "Laser Weapons":
                return "Exotic Ranged Weapon (Laser Weapons)";

            case "Assault Cannons":
            case "Grenade Launchers":
            case "Missile Launchers":
            case "Light Machine Guns":
            case "Medium Machine Guns":
            case "Heavy Machine Guns":
                return "Heavy Weapons";

            case "Shotguns":
            case "Sniper Rifles":
            case "Sporting Rifles":
                return "Longarms";

            case "Throwing Weapons":
                return "Throwing Weapons";

            case "Unarmed":
                return "Unarmed Combat";

            default:
                return "Pistols";
        }
    }
}
