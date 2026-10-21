namespace Chummer.Contracts.Characters;

// These names identify two fixed implementations, not caller-selected digests.
public static class CharacterCreationLifestyleCompilerVersions
{
    public const string PreLocations1008 = "sr5-lifestyles-pre-locations1008";
    public const string Locations1008 = "sr5-lifestyles-locations1008";
}

public sealed record CharacterCreationLifestyleAuthorityReviewState(
    string HistoricalReceiptDigest,
    CharacterCreationLifestylesDraft HistoricalDraft,
    CharacterCreationLifestylesAuthority? CurrentAuthority);

public sealed record CharacterCreationLifestyleAuthorityReviewRequest(
    string HistoricalReceiptDigest,
    IReadOnlyList<CharacterCreationLifestyleConfiguration> Lifestyles);

// Immutable evidence of a newly confirmed transition. The old receipt is never rewritten.
public sealed record CharacterCreationLifestyleAuthorityTransition(
    string PreviousCompiler,
    string CurrentCompiler,
    string HistoricalReceiptDigest,
    CharacterCreationLifestylesDraft HistoricalDraft,
    CharacterCreationLifestylesAuthority CurrentAuthority);
