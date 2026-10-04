namespace Chummer.Contracts.Characters;

/// <summary>Two independent, explicit choices. Labels are never rule identities.</summary>
public sealed record CharacterCreationTalentRestrictionSelection(string SpellCategory, string SpiritSourceId);

public sealed record CharacterCreationTalentSpellCategory(string Value, string Label, string SourceNodeDigest);

public sealed record CharacterCreationTalentSpiritCategory(
    string SourceId, string Name, string Label, string SourceNodeDigest);

/// <summary>Choices projected from effective spells.xml categories and traditions.xml
/// spirits for one exact talent quality. Its digest freezes, but does not authenticate,
/// those inputs: callers must obtain it from the current source context.</summary>
public sealed record CharacterCreationTalentRestrictionCatalog(
    string QualitySourceId,
    string QualitySourceNodeDigest,
    string QualitySourceInputsDigest,
    string SpellsSourceInputsDigest,
    string SpiritsSourceInputsDigest,
    IReadOnlyList<CharacterCreationTalentSpellCategory> SpellCategories,
    IReadOnlyList<CharacterCreationTalentSpiritCategory> SpiritCategories,
    string AuthorityDigest);
