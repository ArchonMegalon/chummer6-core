using Chummer.Contracts.Seeds;

namespace Chummer.Application.Seeds;

public sealed class DefaultAestheticDigestService : IAestheticDigestService
{
    public CharacterAestheticDigest BuildDigest(string characterId, string rulesetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);
        string normalizedRulesetId = string.IsNullOrWhiteSpace(rulesetId) ? "sr5" : rulesetId.Trim().ToLowerInvariant();

        return normalizedRulesetId switch
        {
            "sr4" => new CharacterAestheticDigest(
                CharacterId: characterId.Trim(),
                Metatype: "unknown",
                RoleTags: ["street"],
                BuildTags: ["classic"],
                VisibleWareTags: [],
                MagicalStyleTags: [],
                OutfitArchetypeTags: ["retro-street"],
                FactionStyleTags: [],
                MoodTags: ["tense"],
                TraumaTags: [],
                MotifTags: ["chrome"]),
            "sr6" => new CharacterAestheticDigest(
                CharacterId: characterId.Trim(),
                Metatype: "unknown",
                RoleTags: ["hybrid"],
                BuildTags: ["agile"],
                VisibleWareTags: [],
                MagicalStyleTags: [],
                OutfitArchetypeTags: ["sleek"],
                FactionStyleTags: [],
                MoodTags: ["focused"],
                TraumaTags: [],
                MotifTags: ["neon"]),
            _ => new CharacterAestheticDigest(
                CharacterId: characterId.Trim(),
                Metatype: "unknown",
                RoleTags: ["generalist"],
                BuildTags: ["balanced"],
                VisibleWareTags: [],
                MagicalStyleTags: [],
                OutfitArchetypeTags: ["street"],
                FactionStyleTags: [],
                MoodTags: ["focused"],
                TraumaTags: [],
                MotifTags: ["neon"])
        };
    }
}
