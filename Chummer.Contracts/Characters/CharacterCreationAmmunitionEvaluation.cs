namespace Chummer.Contracts.Characters;

/// <summary>Detached result of Core's actual selected-graph round allocation and
/// source-defined ammunition effects. This describes a preview, not a saved clip,
/// an attack pool, a finalization receipt or permission to mutate the character.</summary>
public sealed record CharacterCreationAmmunitionEvaluation(
    string Schema, string InputsSnapshotDigest, string RequestDigest,
    string RawCharacterXmlDigest, string CompilationDigest, string SelectedGraphDigest,
    string ProjectedGraphDigest, string SourcesDigest,
    IReadOnlyList<CharacterCreationAmmunitionReadyWeapon> Weapons,
    IReadOnlyList<CharacterCreationAmmunitionEffects> Ammunition,
    IReadOnlyList<string> Blockers)
{
    public const string SchemaV1 = "chummer.sr5.creation.ammunition-evaluation.v1";
    public const string Unavailable = "creation-completion.ammunition.evaluation-unavailable";
    public const string EffectsUnavailable = "creation-completion.ammunition.effects-unavailable";
    public const string NotReady = "creation-completion.ammunition.not-ready";
    public bool IsReady => Blockers.Count == 0 && Weapons.Count > 0 && Ammunition.Count > 0;
}

public sealed record CharacterCreationAmmunitionReadyWeapon(
    Guid WeaponId, Guid WeaponSourceId, Guid AmmunitionId, int LoadedRounds,
    string CustodyRevision, string FireStateRevision);

/// <summary>Only the exact source-defined ordinary round currently has this
/// effect evaluator. Zero modifiers are evaluated semantics, not defaults for
/// unrecognized ammunition or a complete weapon damage calculation.</summary>
public sealed record CharacterCreationAmmunitionEffects(
    Guid GearId, Guid SourceId, string SourceDigest, int TotalRounds, int AvailableRounds,
    int DamageModifier, int ArmorPenetrationModifier);
