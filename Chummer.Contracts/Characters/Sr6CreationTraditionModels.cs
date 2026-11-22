namespace Chummer.Contracts.Characters;

/// <summary>Selected magical tradition for SR6 magical talents (magician/mystic-adept/aspected-magician).</summary>
public sealed record Sr6CreationTraditionSelection(string TraditionId);

public sealed record Sr6CreationTraditionOption(string Id, string Name, string SourceAnchorId);

public static class Sr6CreationTraditionBlockers
{
    public const string InvalidSelection = "sr6-creation-tradition-invalid-selection";
    public const string TalentRequired = "sr6-creation-tradition-talent-required";
}

public sealed record Sr6CreationTraditionPreview(Sr6CreationTraditionOption Tradition,
    string AuthorityDigest, IReadOnlyList<string> SourceAnchorIds);
