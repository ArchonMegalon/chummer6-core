using System.Text.Json.Serialization;

namespace Chummer.Contracts.Characters;

/// <summary>Canonical source identity; names are resolved once by the source compiler.</summary>
public sealed record CharacterCreationSkillKarmaTarget(string Kind, string SourceId, string SourceNodeDigest);

public sealed record CharacterCreationSkillKarmaEffect(
    string ImprovementType, decimal Value, int Minimum, int Maximum, string Condition,
    Guid SourceId, string SourceNodeDigest, string OptionId, int Level, string Container, int Position,
    IReadOnlyList<CharacterCreationSkillKarmaTarget> Targets, IReadOnlyList<string> SourceAnchorIds);

// These facts describe an executed active-skill interval, including compensation.
// They are not a direct group purchase or proof of shared-wallet admission.
public enum CharacterCreationSkillKarmaOperation { ActiveSkillPurchase = 1 }

public sealed record CharacterCreationSkillGroupKarmaTerm(long RatingWeight, int Multiplier, long Charge);

public sealed record CharacterCreationSkillGroupCompensationExecution(
    int Ceiling, CharacterCreationSkillGroupKarmaTerm? NewGroup,
    CharacterCreationSkillGroupKarmaTerm ImproveGroup, long GroupCharge, long PerMemberIndividualCharge);

public sealed record CharacterCreationActiveSkillKarmaExecution(
    string Schema, CharacterCreationSkillKarmaOperation Operation,
    CharacterCreationSkillKarmaTarget Payer, string PolicyDigest, string CanonicalInputDigest,
    int Lower, int Upper, bool CompensationRequested, int? OtherMinimum, int MemberCount,
    int SpecializationCount, CharacterCreationSkillGroupCompensationExecution? GroupCompensation,
    int EvaluatedCharge)
{
    public const string SchemaV1 = "chummer.creation-active-skill-karma-execution.v1";
}

public sealed record CharacterCreationSkillKarmaPrice(
    CharacterCreationSkillKarmaTarget Target, int Lower, int Upper, int RawInterval, int CompensationFloor,
    decimal IntervalMultiplier, decimal IntervalAddition, decimal SpecializationMultiplier,
    decimal SpecializationAddition, int IntervalCost, int SpecializationCost, int KarmaCost,
    IReadOnlyList<CharacterCreationSkillKarmaEffect> Effects)
{
    // Historical prices must retain their original serialized bytes and digests.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationActiveSkillKarmaExecution? Execution { get; init; }
}

/// <summary>Bounded creation consumers of literal, source-compiled improvements.
/// Native StandardRound rounds every nonintegral value away from zero, not to nearest.
/// Active intervals clamp to their compensation adjustment before separately rounded
/// specializations; knowledge rounds the combined amount. Group compensation never
/// uses the (independently modified) group purchase price.</summary>
public static class CharacterCreationSkillKarmaModifierRules
{
    public static bool IsSupportedType(string type) => type is
        "ActiveSkillKarmaCost" or "ActiveSkillKarmaCostMultiplier"
        or "KnowledgeSkillKarmaCost" or "KnowledgeSkillKarmaCostMultiplier"
        or "SkillCategoryKarmaCost" or "SkillCategoryKarmaCostMultiplier"
        or "SkillGroupKarmaCost" or "SkillGroupKarmaCostMultiplier"
        or "SkillGroupCategoryKarmaCost" or "SkillGroupCategoryKarmaCostMultiplier"
        or "SkillCategorySpecializationKarmaCost" or "SkillCategorySpecializationKarmaCostMultiplier"
        or "SkillCategoryPointCostMultiplier";

    public static bool IsValid(IReadOnlyList<CharacterCreationSkillKarmaEffect>? effects) => effects is { Count: <= 2048 }
        && effects.All(e => e is not null && IsSupportedType(e.ImprovementType)
            && CharacterCreationKnowledgeCategoryRules.IsSupportedCondition(e.Condition)
            && e.Minimum >= 0 && e.Maximum >= 0 && (e.Maximum == 0 || e.Maximum >= e.Minimum)
            && (!e.ImprovementType.EndsWith("Multiplier", StringComparison.Ordinal) || e.Value >= 0)
            && e.SourceId != Guid.Empty && CharacterCreationSkillsDigest.IsCanonical(e.SourceNodeDigest)
            && !string.IsNullOrWhiteSpace(e.OptionId) && e.Level > 0 && e.Position >= 0
            && e.Container is "bonus" or "firstlevelbonus" && (e.Container != "firstlevelbonus" || e.Level == 1)
            && e.SourceAnchorIds is not null && e.Targets is { Count: > 0 }
            && e.Targets.All(IsValidTarget) && e.Targets.Distinct().Count() == e.Targets.Count)
        && effects.Select(e => (e.SourceId, e.OptionId, e.Level, e.Container, e.Position)).Distinct().Count() == effects.Count;

    private static bool IsValidTarget(CharacterCreationSkillKarmaTarget? target) => target is not null
        && target.Kind is "active" or "knowledge" or "group" && !string.IsNullOrWhiteSpace(target.SourceId)
        && CharacterCreationSkillsDigest.IsCanonical(target.SourceNodeDigest);

    public static bool TryPrice(CharacterCreationSkillKarmaTarget target, int lower, int upper,
        int rawInterval, int compensationFloor, int specializationCount, int specializationRate,
        IReadOnlyList<CharacterCreationSkillKarmaEffect> effects, out CharacterCreationSkillKarmaPrice? price)
    {
        price = null;
        if (!IsValidTarget(target) || !IsValid(effects) || lower < 0 || upper < lower
            || upper > CharacterCreationStandardPrioritySkillsRules.MaximumSupportedCreationRating
            || specializationCount is < 0 or > 1 || specializationRate < 0
            || target.Kind == "group" && specializationCount != 0
            || effects.Any(e => e.Targets.Any(t => t.Kind == target.Kind && t.SourceId == target.SourceId
                && t.SourceNodeDigest != target.SourceNodeDigest))) return false;
        try
        {
            decimal multiplier = 1, extra = 0, specMultiplier = 1, specExtra = 0;
            // Match native type-loop order; source occurrence order is retained within each type.
            string named = target.Kind switch { "active" => "ActiveSkill", "knowledge" => "KnowledgeSkill", _ => "SkillGroup" };
            string category = target.Kind == "group" ? "SkillGroupCategory" : "SkillCategory";
            foreach (string type in new[] { named + "KarmaCost", named + "KarmaCostMultiplier",
                category + "KarmaCost", category + "KarmaCostMultiplier",
                "SkillCategorySpecializationKarmaCost", "SkillCategorySpecializationKarmaCostMultiplier" })
            foreach (var effect in effects.Where(e => e.ImprovementType == type && e.Targets.Contains(target)))
            {
                if (!CharacterCreationKnowledgeCategoryRules.TryLowerNativeCondition(effect.Condition, out var condition)) return false;
                if (condition == "career") continue;
                bool specialization = type.Contains("Specialization", StringComparison.Ordinal);
                if (specialization)
                {
                    if (effect.Minimum > upper) continue;
                    if (type.EndsWith("Multiplier", StringComparison.Ordinal)) specMultiplier = checked(specMultiplier * effect.Value / 100m);
                    else specExtra = checked(specExtra + effect.Value * specializationCount);
                }
                else
                {
                    if (target.Kind != "knowledge" && lower == upper) continue;
                    if (effect.Minimum > (target.Kind == "knowledge" ? upper : lower)) continue;
                    if (type.EndsWith("Multiplier", StringComparison.Ordinal)) multiplier = checked(multiplier * effect.Value / 100m);
                    else extra = checked(extra + effect.Value * (Math.Min(upper, effect.Maximum == 0 ? int.MaxValue : effect.Maximum)
                        - Math.Max(lower, effect.Minimum - 1)));
                }
            }
            decimal specs = checked(specializationCount * (decimal)specializationRate * specMultiplier + specExtra);
            decimal interval = checked(rawInterval * multiplier + extra);
            // When the native multiplier is one, it rounds only the additive amount.
            // This matters for a negative fractional adjustment to an integral cost.
            int intervalCost = target.Kind == "knowledge" ? Round(interval)
                : Math.Max(compensationFloor, multiplier == 1 ? checked(rawInterval + Round(extra)) : Round(interval));
            int specCost = specMultiplier == 1 ? checked(specializationCount * specializationRate + Round(specExtra)) : Round(specs);
            int cost = target.Kind == "knowledge" ? Round(interval + specs) : checked(intervalCost + specCost);
            price = new(target, lower, upper, rawInterval, compensationFloor, multiplier, extra,
                specMultiplier, specExtra, intervalCost, specCost, Math.Max(0, cost), effects.Where(e => e.Targets.Contains(target)).ToArray());
            return true;
        }
        catch (OverflowException) { return false; }
    }

    public static int Round(decimal value) => checked((int)(value >= 0 ? decimal.Ceiling(value) : decimal.Floor(value)));
}
