namespace Chummer.Contracts.Characters;

// Additive definitions only. V1/V2 observation traversal and wire identifiers stay frozen.
public static class CharacterCreationNativeContactSpecialtyDefinitions
{
    public static IReadOnlyList<CharacterCreationSettingDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new CharacterCreationSettingDefinition(CharacterCreationObservedSetting.EnableEnemyTracking, "enableenemytracking", null, CharacterCreationSettingValueKind.Boolean),
        new CharacterCreationSettingDefinition(CharacterCreationObservedSetting.EnemyKarmaQualityLimit, "enemykarmaqualitylimit", null, CharacterCreationSettingValueKind.Boolean),
        new CharacterCreationSettingDefinition(CharacterCreationObservedSetting.UseCalculatedPublicAwareness, "usecalculatedpublicawareness", null, CharacterCreationSettingValueKind.Boolean),
        new CharacterCreationSettingDefinition(CharacterCreationObservedSetting.AllowPointBuySpecializationsOnKarmaSkills, "allowpointbuyspecializationsonkarmaskills", null, CharacterCreationSettingValueKind.Boolean),
        new CharacterCreationSettingDefinition(CharacterCreationObservedSetting.FreeMartialArtSpecialization, "freemartialartspecialization", null, CharacterCreationSettingValueKind.Boolean)
    });
}

public sealed record CharacterCreationNativeContactSpecialtyPolicy(
    bool? EnableEnemyTracking, bool? EnemyKarmaQualityLimit, bool? UseCalculatedPublicAwareness,
    bool? AllowPointBuySpecializationsOnKarmaSkills, bool? FreeMartialArtSpecialization,
    int? KarmaEnemy, bool? SpecializationsBreakSkillGroups,
    string SettingsProfileId, string ProfileInputsDigest, string ProfileXmlDigest)
{
    public bool? Value(string field) => field switch
    {
        "enableenemytracking" => EnableEnemyTracking,
        "enemykarmaqualitylimit" => EnemyKarmaQualityLimit,
        "usecalculatedpublicawareness" => UseCalculatedPublicAwareness,
        "allowpointbuyspecializationsonkarmaskills" => AllowPointBuySpecializationsOnKarmaSkills,
        "freemartialartspecialization" => FreeMartialArtSpecialization,
        _ => null
    };
}

// Adjustment is the signed contribution BEFORE the existing negative-quality cap.
// It is not a replacement quality budget, an award, or a permission to add enemies.
public sealed record CharacterCreationEnemyTrackingResult(bool TrackingEnabled, int? KarmaEnemy,
    int? EligibleEnemyCount, int? ConnectionAndLoyalty, int EnemyKarma, int NegativeQualityLimitAdjustment);
public sealed record CharacterCreationSpecializationFundingResult(string Kind, string SourceSkillId,
    bool HasNonFreeSpecialization, int KarmaPoints, int BasePoints, int FreeBase, int GroupRating,
    bool ForcedBuyWithKarma, bool PurchasedWithKarma);
public sealed record CharacterCreationMartialSpecializationBranch(bool Enabled, int MartialArtCount,
    int MartialArtSpecializationOptionCount, bool AddsSpecializationWhenOptionExecutes,
    bool AddedSpecializationIsFree, string AddedImprovementType);
public sealed record CharacterCreationNativeContactSpecialtyProjection(
    CharacterCreationNativeContactSpecialtyPolicy Policy, bool Created, string GraphDigest,
    CharacterCreationEnemyTrackingResult? Enemies, CharacterCareerReputationProjection? Reputation,
    IReadOnlyList<CharacterCreationSpecializationFundingResult>? SpecializationFunding,
    CharacterCreationMartialSpecializationBranch? MartialArts);

public static class CharacterCreationNativeContactSpecialtyRules
{
    // Character.EnemyKarma and NegativeQualityLimitKarma, with native short-circuit order.
    public static CharacterCreationEnemyTrackingResult Enemies(bool tracking, bool qualityLimit,
        int? price, IReadOnlyList<(int Connection, int Loyalty)> nonFreeEnemies)
    {
        if (!tracking) return new(false, null, null, null, 0, 0);
        if (price is null) throw new ArgumentException("Enemy price is required by the active branch.");
        if (price <= 0) return new(true, price, null, null, 0, 0);
        int points = 0;
        foreach (var enemy in nonFreeEnemies) points = checked(points + checked(enemy.Connection + enemy.Loyalty));
        int karma = checked(points * price.Value);
        return new(true, price, nonFreeEnemies.Count, points, karma, qualityLimit ? checked(-karma) : 0);
    }

    // Skill.ForcedBuyWithKarma. The caller supplies actual CanHaveSpecs/IgnoreRules
    // facts, never infers them from the setting's default or a zero price.
    public static bool ForcedBuyWithKarma(bool ignoreRules, bool hasNonFreeSpecialization,
        bool canHaveSpecs, int karmaPoints, long basePoints, int freeBase, int groupRating,
        bool allowPointBuy, bool specializationsBreakGroups)
        => !ignoreRules && hasNonFreeSpecialization && canHaveSpecs
           && ((karmaPoints > 0 && basePoints + freeBase == 0 && !allowPointBuy)
               || specializationsBreakGroups && groupRating > 0);
}
