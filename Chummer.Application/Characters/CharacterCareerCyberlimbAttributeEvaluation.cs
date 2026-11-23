namespace Chummer.Application.Characters;

/// <summary>
/// A canonical engine evaluation for one AGI/STR attribute in the exact character
/// XML supplied to TryCreateContext. This is read authority, never mutation authority.
/// </summary>
/// <remarks>
/// The evaluator must resolve equipped/excluded/inherited limbs, customization,
/// enhancements, Redliner and limb improvements, profile/metatype limb counts,
/// remaining natural limbs, rounding, augmented caps and the zero-value exceptions
/// through the engine. A saved totalvalue or a partially resolved limb is not evidence.
/// Return unavailable if that evaluation cannot be performed for the selected sources.
///
/// Digests are uppercase SHA-256 of UTF-8: CharacterXmlDigest binds the exact context
/// input; SettingsXmlDigest binds the effective settings XElement serialized with
/// SaveOptions.DisableFormatting. NaturalTotalValue is Value + AttributeModifiers
/// before averaging or the final augmented cap. CalculatedTotalValue is the final
/// engine result, including its minimum/zero handling.
///
/// LimbSourceState binds the exact resolved limb inputs (including character/slot
/// counts and evaluated limb values). RuleSourceState binds the canonical evaluator
/// version and effective source/profile/custom-data authority. Both must be stable
/// across changes only to cached totalvalue, expenses or receipt/correction audits;
/// neither may be the digest of the complete character XML. The input digest is
/// checked separately so staging and receipt recovery cannot reuse stale evaluation.
/// </remarks>
public sealed record CharacterCareerCyberlimbAttributeEvaluation(
    string Abbreviation,
    string CharacterXmlDigest,
    string SettingsXmlDigest,
    int NaturalValue,
    int NaturalTotalValue,
    int TotalAugmentedMaximum,
    int CalculatedTotalValue,
    string LimbSourceState,
    string RuleSourceState);
