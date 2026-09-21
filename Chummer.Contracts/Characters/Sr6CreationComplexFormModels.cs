namespace Chummer.Contracts.Characters;

/// <summary>Source-owned, expanded variant identity; Subject is only used for a named
/// weapon/drone model. This is a draft selection, not an active Matrix effect.</summary>
public sealed record Sr6CreationComplexFormChoice(string CatalogId, string? Subject = null);
public sealed record Sr6CreationComplexFormSelection(IReadOnlyList<Sr6CreationComplexFormChoice> Choices);
public sealed record Sr6CreationComplexFormOption(string Id, string SourceName, string SourceAnchorId,
    string? SubjectKind = null);
public sealed record Sr6CreationComplexFormValue(Sr6CreationComplexFormChoice Choice, string SourceName,
    string SourceAnchorId, bool SubjectNeedsGmReview);
public sealed record Sr6CreationComplexFormPreview(IReadOnlyList<Sr6CreationComplexFormValue> Forms,
    int Limit, int SlotsRemaining, int FreeSlotsUsed, int CharacterPointCost,
    string AuthorityDigest, IReadOnlyList<string> SourceAnchorIds);

public static class Sr6CreationComplexFormBlockers
{
    public const string InvalidSelection = "sr6-creation-complex-form-invalid-selection";
    public const string TalentRequired = "sr6-creation-complex-form-talent-required";
    public const string CatalogUnavailable = "sr6-creation-complex-form-catalog-unavailable";
    public const string LimitExceeded = "sr6-creation-complex-form-limit-exceeded";
}
