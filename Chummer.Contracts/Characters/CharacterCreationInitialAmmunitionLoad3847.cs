namespace Chummer.Contracts.Characters;

/// <summary>A finalization instruction to have one ordinary round ready, not to fire,
/// purchase ammunition, replace a loaded type, or change equipment. Null instance IDs
/// select exactly one final top-level owner of that source; ambiguity refuses.
/// Source digests are expectations, never authority. Live source and round custody
/// are reconstructed by the existing finalization execution before any allocation.</summary>
public sealed record CharacterCreationInitialAmmunitionLoad3847(
    Guid WeaponSourceId, string WeaponSourceDigest,
    Guid AmmunitionSourceId, string AmmunitionSourceDigest,
    Guid? WeaponId = null, Guid? AmmunitionId = null);
