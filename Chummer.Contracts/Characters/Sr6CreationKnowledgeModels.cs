namespace Chummer.Contracts.Characters;

public static class Sr6CreationLanguageLevels
{
    public const string Basic = "basic";
    public const string Specialist = "specialist";
    public const string Expert = "expert";
    public static IReadOnlyList<string> Ordered { get; } = Array.AsReadOnly(new[] { Basic, Specialist, Expert });
}

public sealed record Sr6CreationKnowledgeEntry(Guid Id, string Name);
public sealed record Sr6CreationLanguageEntry(Guid Id, string Name, string Level);
public sealed record Sr6CreationKnowledgeSelection(string NativeLanguage,
    IReadOnlyList<Sr6CreationKnowledgeEntry> KnowledgeSkills, IReadOnlyList<Sr6CreationLanguageEntry> Languages);
public sealed record Sr6CreationLanguageValue(Guid Id, string Name, string Level, int PointCost, int ComprehensionBonus);
public sealed record Sr6CreationKnowledgePreview(string NativeLanguage, IReadOnlyList<Sr6CreationKnowledgeEntry> KnowledgeSkills,
    IReadOnlyList<Sr6CreationLanguageValue> Languages, int Logic, int PointsSpent, int PointsRemaining,
    bool AllPointsSpent, bool TopicsNeedGmReview, string AttributeAuthorityDigest, string AuthorityDigest,
    IReadOnlyList<string> SourceAnchorIds);

public static class Sr6CreationKnowledgeBlockers
{
    public const string InvalidSelection = "sr6-creation-knowledge-invalid-selection";
    public const string AttributesRequired = "sr6-creation-knowledge-attributes-required";
    public const string BudgetExceeded = "sr6-creation-knowledge-budget-exceeded";
}
