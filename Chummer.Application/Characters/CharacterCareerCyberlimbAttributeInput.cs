namespace Chummer.Application.Characters;

/// <summary>
/// Natural-attribute facts from the Career projector, before cyberlimb averaging.
/// This is an internal read-composition boundary, not an owner mutation command.
/// Digests bind the facts to the exact XML and effective settings being evaluated.
/// The source adapter resolves all limb inputs itself; it never reads totalvalue.
/// </summary>
public sealed record CharacterCareerCyberlimbAttributeInput(
    string Abbreviation,
    string CharacterXmlDigest,
    string SettingsXmlDigest,
    int NaturalValue,
    int NaturalTotalValue,
    int NaturalMaximum,
    int TotalAugmentedMaximum,
    bool NaturalZeroAllowed);
