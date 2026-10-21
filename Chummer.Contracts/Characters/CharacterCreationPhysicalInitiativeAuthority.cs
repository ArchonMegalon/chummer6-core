namespace Chummer.Contracts.Characters;

/// <summary>Selected SR5 source terms, resolved against the prerequisite draft.
/// The cap triple proves catalog validity and identity; it is not a runtime clamp.</summary>
public sealed record CharacterCreationPhysicalInitiativeAuthority(
    string RulesetId, string Semantics, string SettingsProfileId, string ProfileInputsDigest,
    string PrerequisiteDraftDigest, string MetatypeSourceId, string MetatypeSourceNodeDigest,
    string MetatypeObligationId, string MetatypesInputsDigest,
    CharacterCreationMetatypeInitiativeProjection Metatype, int MaximumDice)
{
    public const string SemanticsV1 = "sr5.chummer5a-local.initiative.v1";
    public const string Unavailable = "physical-initiative-source-unavailable";
    [System.Text.Json.Serialization.JsonIgnore]
    public int BaseDice => Metatype.DirectDice886 ?? Metatype.MinimumDiceFallback;
}

/// <summary>Unrolled physical initiative, independently derived for the graph's lifecycle.</summary>
public sealed record CharacterPhysicalInitiativeProjection(
    int Value, int Dice, int BaseDice, int Reaction, int Intuition, int WoundModifier,
    int InitiativeImprovement, int DiceImprovement, int AdditiveDiceImprovement, bool Created,
    string AuthorityDigest, string ImprovementsDigest);
