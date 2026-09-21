namespace Chummer.Contracts.Characters;

public sealed record Sr6CreationSpellSelection(IReadOnlyList<string> CatalogIds);

public sealed record Sr6CreationSpellOption(string Id, string SourceName, string Kind,
    string CategoryId, string SourceAnchorId);

/// <summary>Known-formula draft choices, not cast spells or prepared alchemical objects.</summary>
public sealed record Sr6CreationSpellPreview(IReadOnlyList<Sr6CreationSpellOption> Spells,
    int Limit, int SlotsRemaining, int FreeSlotsUsed, int CharacterPointCost, string UseId,
    string AuthorityDigest, IReadOnlyList<string> SourceAnchorIds);

public static class Sr6CreationSpellBlockers
{
    public const string InvalidSelection = "sr6-creation-spell-invalid-selection";
    public const string TalentRequired = "sr6-creation-spell-talent-required";
    public const string CatalogUnavailable = "sr6-creation-spell-catalog-unavailable";
    public const string LimitExceeded = "sr6-creation-spell-limit-exceeded";
}
