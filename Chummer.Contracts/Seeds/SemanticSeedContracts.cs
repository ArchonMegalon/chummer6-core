namespace Chummer.Contracts.Seeds;

public sealed record CharacterAestheticDigest(
    string CharacterId,
    string? Metatype,
    IReadOnlyList<string> RoleTags,
    IReadOnlyList<string> BuildTags,
    IReadOnlyList<string> VisibleWareTags,
    IReadOnlyList<string> MagicalStyleTags,
    IReadOnlyList<string> OutfitArchetypeTags,
    IReadOnlyList<string> FactionStyleTags,
    IReadOnlyList<string> MoodTags,
    IReadOnlyList<string> TraumaTags,
    IReadOnlyList<string> MotifTags);

public sealed record CharacterDossierSeed(
    string CharacterId,
    string RulesetId,
    string RuntimeFingerprint,
    CharacterAestheticDigest AestheticDigest,
    IReadOnlyDictionary<string, string> Facts);

public sealed record NpcDossierSeed(
    string NpcId,
    string RulesetId,
    IReadOnlyList<string> ArchetypeTags,
    IReadOnlyDictionary<string, string> Facts);

public sealed record RunSummarySeed(
    string RunId,
    string RulesetId,
    IReadOnlyList<string> HeatThresholdKeys,
    IReadOnlyList<string> ConsequenceTags);

public sealed record BuildIdeaSeed(
    string IdeaId,
    string RulesetId,
    IReadOnlyList<string> RoleTags,
    IReadOnlyList<string> PackageTags);

public sealed record ShadowfeedSeed(
    string FeedId,
    string RulesetId,
    IReadOnlyList<string> TopicTags,
    IReadOnlyList<string> FactionTags);
