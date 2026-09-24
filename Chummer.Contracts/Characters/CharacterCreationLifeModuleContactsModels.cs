namespace Chummer.Contracts.Characters;

/// <summary>Read-only contact contribution to Life Modules creation. Group costs
/// enter the existing quality cap; ordinary overspend uses remaining Karma.
/// Does not save contacts or grant permission to finalize the character.</summary>
public sealed record CharacterCreationLifeModuleContactsQuote(
    CharacterCreationKarmaContactsPolicy Policy,
    string ResourcesQuoteDigest, string AttributesQuoteDigest, string QualityCostsQuoteDigest,
    string SourceDigest, string RulesDigest, string RuntimeDigest,
    IReadOnlyList<CharacterCreationKarmaContactSelection> Selection,
    IReadOnlyList<CharacterCreationKarmaContactLine> Lines,
    CharacterCreationContactBudget ContactBudget, CharacterCreationContactBudget HighPlacesBudget,
    int GroupContactKarma, CharacterCreationQualityCostTotals CombinedQualityCosts,
    int AdditionalQualityKarma, int KarmaUsed, decimal KarmaBeforeContacts, decimal KarmaAfterContacts,
    IReadOnlyList<string> Blockers, string QuoteDigest)
{
    public const string SelectionRequired = "creation-life-module-contacts-selection-required";
    public const string ContactLimitExceeded = "creation-life-module-contact-limit-exceeded";
}
