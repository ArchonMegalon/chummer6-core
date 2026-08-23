using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

/// <summary>
/// Stable saved identity for a Chummer5 knowledge skill. Custom knowledge skills have no
/// source-data GUID, so <see cref="SourceSkillId"/> is deliberately nullable rather than
/// accepting <see cref="Guid.Empty"/> as a second identity shape.
/// </summary>
public sealed record CharacterCareerKnowledgeSkillIdentity(
    Guid SkillId,
    Guid? SourceSkillId);

public sealed record CharacterCareerKnowledgeSkillAdvanceSettings(
    int KarmaNewKnowledgeSkill,
    int KarmaImproveKnowledgeSkill);

public enum CharacterCareerKnowledgeSkillKarmaModifierKind
{
    KnowledgeSkillCostMinimum,
    KnowledgeSkillCost,
    KnowledgeSkillCostMultiplier,
    SkillCategoryCost,
    SkillCategoryCostMultiplier
}

public sealed record CharacterCareerKnowledgeSkillKarmaModifier(
    string ModifierIdentity,
    CharacterCareerKnowledgeSkillKarmaModifierKind Kind,
    string Target,
    int Minimum,
    int Maximum,
    decimal Value);

public sealed record CharacterCareerKnowledgeSkillAdvanceInput(
    CharacterCareerKnowledgeSkillIdentity Identity,
    bool Created,
    bool IsKnowledgeSkill,
    bool AllowUpgrade,
    bool IsNativeLanguage,
    string Name,
    string SkillType,
    string SkillCategory,
    string DictionaryKey,
    int BasePoints,
    int KarmaPoints,
    int TotalBaseRating,
    int RatingMaximum,
    int AvailableKarma,
    CharacterCareerKnowledgeSkillAdvanceSettings Settings,
    IReadOnlyList<CharacterCareerKnowledgeSkillKarmaModifier> Modifiers,
    string RawSourceState,
    string RawRuleState);

public enum CharacterCareerKnowledgeSkillAdvanceBlocker
{
    None,
    UpgradeDisallowed,
    NativeLanguage,
    AtMaximum,
    InsufficientKarma
}

public sealed record CharacterCareerKnowledgeSkillAdvanceQuote(
    CharacterCareerKnowledgeSkillIdentity Identity,
    string Name,
    string SkillType,
    string SkillCategory,
    bool AllowUpgrade,
    bool IsNativeLanguage,
    int BasePoints,
    int KarmaPoints,
    int TotalBaseRating,
    int RatingMaximum,
    int AvailableKarma,
    int KarmaCost,
    bool CanAdvance,
    CharacterCareerKnowledgeSkillAdvanceBlocker Blocker,
    string LogicalRevision,
    string SourceRevision,
    string RuleDigest);

public sealed record CharacterCareerKnowledgeSkillAdvancePlan(
    CharacterCareerKnowledgeSkillIdentity Identity,
    int SavedSkillKarmaPoints,
    int SavedCharacterKarma,
    int ExpenseAmount,
    string ExpenseReason,
    DateTime ExpenseDateLocal,
    Guid ExpenseId,
    string KarmaUndoType,
    string NuyenUndoType,
    string UndoObjectId,
    decimal UndoQuantity,
    string UndoExtra,
    string RuleDigest);

/// <summary>
/// Deterministic Chummer5 authority for raising one saved knowledge skill in Career mode.
/// This preserves the separate new/improve knowledge-skill settings, knowledge-specific
/// minimum overrides, skill and category modifiers, Chummer5 rounding, and exact expense
/// undo identity. Source and rule snapshots are digest-bound so callers must re-quote after
/// any saved-data or rules change.
/// </summary>
public static class CharacterCareerKnowledgeSkillAdvanceRules
{
    public const int RevisionHexLength = 64;
    public const int MaximumRating = 1000;
    public const int MaximumKarma = 9_999_999;
    public const int MaximumNameLength = 512;
    public const int MaximumRuleTextLength = 1_048_576;
    public static readonly DateTime MinimumExpenseDate = new(1753, 1, 1);
    public static readonly DateTime MaximumExpenseDate = new(9998, 12, 31, 23, 59, 59);

    public static bool TryCreateQuote(
        CharacterCareerKnowledgeSkillAdvanceInput? input,
        out CharacterCareerKnowledgeSkillAdvanceQuote quote)
    {
        quote = UnavailableQuote();
        if (!IsValidInput(input))
        {
            return false;
        }

        CharacterCareerKnowledgeSkillAdvanceInput validInput = input!;
        int karmaCost;
        try
        {
            karmaCost = CalculateKarmaCost(validInput);
        }
        catch (OverflowException)
        {
            return false;
        }

        CharacterCareerKnowledgeSkillAdvanceBlocker blocker = ExpectedBlocker(
            validInput.AllowUpgrade,
            validInput.IsNativeLanguage,
            validInput.TotalBaseRating,
            validInput.RatingMaximum,
            validInput.AvailableKarma,
            karmaCost);
        bool canAdvance = blocker == CharacterCareerKnowledgeSkillAdvanceBlocker.None;
        string sourceRevision = Sha256(validInput.RawSourceState);
        string ruleDigest = CalculateRuleDigest(validInput);
        string logicalRevision = CalculateLogicalRevision(
            validInput.Identity,
            validInput.Name,
            validInput.SkillType,
            validInput.SkillCategory,
            validInput.AllowUpgrade,
            validInput.IsNativeLanguage,
            validInput.BasePoints,
            validInput.KarmaPoints,
            validInput.TotalBaseRating,
            validInput.RatingMaximum,
            validInput.AvailableKarma,
            karmaCost,
            canAdvance,
            blocker,
            sourceRevision,
            ruleDigest);

        quote = new CharacterCareerKnowledgeSkillAdvanceQuote(
            validInput.Identity,
            validInput.Name,
            validInput.SkillType,
            validInput.SkillCategory,
            validInput.AllowUpgrade,
            validInput.IsNativeLanguage,
            validInput.BasePoints,
            validInput.KarmaPoints,
            validInput.TotalBaseRating,
            validInput.RatingMaximum,
            validInput.AvailableKarma,
            karmaCost,
            canAdvance,
            blocker,
            logicalRevision,
            sourceRevision,
            ruleDigest);
        return true;
    }

    public static bool TryPlanAdvance(
        CharacterCareerKnowledgeSkillAdvanceQuote? current,
        string? expectedRuleDigest,
        bool confirmed,
        Guid expenseId,
        DateTime expenseDateLocal,
        out CharacterCareerKnowledgeSkillAdvancePlan plan)
    {
        plan = UnavailablePlan();
        DateTime normalizedDate = DateTime.SpecifyKind(expenseDateLocal, DateTimeKind.Unspecified);
        if (!confirmed
            || !IsCoherent(current)
            || !current!.CanAdvance
            || !RevisionMatches(current.RuleDigest, expectedRuleDigest)
            || expenseId == Guid.Empty
            || normalizedDate < MinimumExpenseDate
            || normalizedDate > MaximumExpenseDate)
        {
            return false;
        }

        try
        {
            int savedSkillKarma = checked(current.KarmaPoints + 1);
            int savedCharacterKarma = checked(current.AvailableKarma - current.KarmaCost);
            int targetRating = checked(current.TotalBaseRating + 1);
            plan = new CharacterCareerKnowledgeSkillAdvancePlan(
                current.Identity,
                savedSkillKarma,
                savedCharacterKarma,
                checked(-current.KarmaCost),
                $"Knowledge Skill {current.Name} {current.TotalBaseRating.ToString(CultureInfo.InvariantCulture)} -> {targetRating.ToString(CultureInfo.InvariantCulture)}",
                normalizedDate,
                expenseId,
                current.TotalBaseRating == 0 ? "AddSkill" : "ImproveSkill",
                "AddCyberware",
                current.Identity.SkillId.ToString("D"),
                0m,
                string.Empty,
                current.RuleDigest);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static bool IsCoherent(CharacterCareerKnowledgeSkillAdvanceQuote? quote)
        => quote is not null
            && IsValidIdentity(quote.Identity)
            && IsBoundedRequiredText(quote.Name)
            && IsBoundedOptionalText(quote.SkillType)
            && IsBoundedOptionalText(quote.SkillCategory)
            && quote.BasePoints is >= 0 and <= MaximumRating
            && quote.KarmaPoints is >= 0 and <= MaximumRating
            && quote.TotalBaseRating is >= 0 and <= MaximumRating
            && quote.RatingMaximum is >= 0 and <= MaximumRating
            && quote.AvailableKarma is >= 0 and <= MaximumKarma
            && quote.KarmaCost is >= -1 and <= MaximumKarma
            && quote.CanAdvance == (quote.Blocker == CharacterCareerKnowledgeSkillAdvanceBlocker.None)
            && quote.Blocker == ExpectedBlocker(
                quote.AllowUpgrade,
                quote.IsNativeLanguage,
                quote.TotalBaseRating,
                quote.RatingMaximum,
                quote.AvailableKarma,
                quote.KarmaCost)
            && IsLowerHexRevision(quote.SourceRevision)
            && IsLowerHexRevision(quote.RuleDigest)
            && RevisionMatches(
                CalculateLogicalRevision(
                    quote.Identity,
                    quote.Name,
                    quote.SkillType,
                    quote.SkillCategory,
                    quote.AllowUpgrade,
                    quote.IsNativeLanguage,
                    quote.BasePoints,
                    quote.KarmaPoints,
                    quote.TotalBaseRating,
                    quote.RatingMaximum,
                    quote.AvailableKarma,
                    quote.KarmaCost,
                    quote.CanAdvance,
                    quote.Blocker,
                    quote.SourceRevision,
                    quote.RuleDigest),
                quote.LogicalRevision);

    private static CharacterCareerKnowledgeSkillAdvanceBlocker ExpectedBlocker(
        bool allowUpgrade,
        bool isNativeLanguage,
        int rating,
        int maximum,
        int availableKarma,
        int karmaCost)
        => isNativeLanguage
            ? CharacterCareerKnowledgeSkillAdvanceBlocker.NativeLanguage
            : !allowUpgrade
                ? CharacterCareerKnowledgeSkillAdvanceBlocker.UpgradeDisallowed
                : karmaCost < 0 || rating >= maximum
                    ? CharacterCareerKnowledgeSkillAdvanceBlocker.AtMaximum
                    : availableKarma < karmaCost
                        ? CharacterCareerKnowledgeSkillAdvanceBlocker.InsufficientKarma
                        : CharacterCareerKnowledgeSkillAdvanceBlocker.None;

    private static bool IsValidInput(CharacterCareerKnowledgeSkillAdvanceInput? input)
    {
        if (input is null
            || !input.Created
            || !input.IsKnowledgeSkill
            || !IsValidIdentity(input.Identity)
            || !IsBoundedRequiredText(input.Name)
            || !IsBoundedOptionalText(input.SkillType)
            || !IsBoundedOptionalText(input.SkillCategory)
            || !IsBoundedRequiredText(input.DictionaryKey)
            || input.BasePoints is < 0 or > MaximumRating
            || input.KarmaPoints is < 0 or > MaximumRating
            || input.TotalBaseRating is < 0 or > MaximumRating
            || input.RatingMaximum is < 0 or > MaximumRating
            || input.AvailableKarma is < 0 or > MaximumKarma
            || !IsValidSettings(input.Settings)
            || input.Modifiers is null
            || string.IsNullOrWhiteSpace(input.RawSourceState)
            || input.RawSourceState.Length > MaximumRuleTextLength
            || string.IsNullOrWhiteSpace(input.RawRuleState)
            || input.RawRuleState.Length > MaximumRuleTextLength)
        {
            return false;
        }

        CharacterCareerKnowledgeSkillKarmaModifier[] modifiers = input.Modifiers.ToArray();
        return modifiers.Select(static modifier => modifier.ModifierIdentity)
                .Distinct(StringComparer.Ordinal)
                .Count() == modifiers.Length
            && modifiers.All(modifier => IsValidModifier(input, modifier));
    }

    private static bool IsValidSettings(CharacterCareerKnowledgeSkillAdvanceSettings? settings)
        => settings is not null
            && settings.KarmaNewKnowledgeSkill is >= 0 and <= MaximumKarma
            && settings.KarmaImproveKnowledgeSkill is >= 0 and <= MaximumKarma;

    private static bool IsValidModifier(
        CharacterCareerKnowledgeSkillAdvanceInput input,
        CharacterCareerKnowledgeSkillKarmaModifier modifier)
    {
        if (!IsLowerHexRevision(modifier.ModifierIdentity)
            || modifier.Target is null
            || modifier.Target.Length > MaximumNameLength
            || modifier.Minimum is < 0 or > MaximumRating
            || modifier.Maximum is < 0 or > MaximumRating
            || modifier.Maximum != 0 && modifier.Maximum < modifier.Minimum
            || modifier.Value is < -MaximumKarma or > MaximumKarma
            || modifier.Kind == CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCostMinimum
                && modifier.Value < 0m)
        {
            return false;
        }

        return modifier.Kind switch
        {
            CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCostMinimum =>
                string.IsNullOrEmpty(modifier.Target)
                || string.Equals(modifier.Target, input.DictionaryKey, StringComparison.Ordinal)
                || string.Equals(modifier.Target, input.SkillCategory, StringComparison.Ordinal),
            CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCost
                or CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCostMultiplier =>
                string.IsNullOrEmpty(modifier.Target)
                || string.Equals(modifier.Target, input.DictionaryKey, StringComparison.Ordinal),
            CharacterCareerKnowledgeSkillKarmaModifierKind.SkillCategoryCost
                or CharacterCareerKnowledgeSkillKarmaModifierKind.SkillCategoryCostMultiplier =>
                string.IsNullOrEmpty(modifier.Target)
                || string.Equals(modifier.Target, input.SkillCategory, StringComparison.Ordinal),
            _ => false
        };
    }

    private static int CalculateKarmaCost(CharacterCareerKnowledgeSkillAdvanceInput input)
    {
        int rating = input.TotalBaseRating;
        if (rating >= input.RatingMaximum)
        {
            return -1;
        }

        int optionsCost = rating == 0
            ? input.Settings.KarmaNewKnowledgeSkill
            : input.Settings.KarmaImproveKnowledgeSkill;
        int value = rating == 0
            ? optionsCost
            : checked((rating + 1) * optionsCost);
        int targetRating = checked(rating + 1);
        int minimumOverride = int.MaxValue;
        decimal extra = 0m;
        decimal multiplier = 1m;

        foreach (CharacterCareerKnowledgeSkillKarmaModifier modifier in input.Modifiers)
        {
            if (modifier.Minimum > targetRating
                || modifier.Maximum != 0 && targetRating > modifier.Maximum)
            {
                continue;
            }

            switch (modifier.Kind)
            {
                case CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCostMinimum:
                    minimumOverride = Math.Min(minimumOverride, StandardRound(modifier.Value));
                    break;
                case CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCost:
                case CharacterCareerKnowledgeSkillKarmaModifierKind.SkillCategoryCost:
                    extra = checked(extra + modifier.Value);
                    break;
                case CharacterCareerKnowledgeSkillKarmaModifierKind.KnowledgeSkillCostMultiplier:
                case CharacterCareerKnowledgeSkillKarmaModifierKind.SkillCategoryCostMultiplier:
                    multiplier = checked(multiplier * (modifier.Value / 100m));
                    break;
                default:
                    throw new InvalidOperationException("Unsupported knowledge-skill Karma modifier kind.");
            }
        }

        value = multiplier != 1m
            ? StandardRound(checked(value * multiplier + extra))
            : checked(value + StandardRound(extra));
        int minimumCost = minimumOverride != int.MaxValue
            ? minimumOverride
            : Math.Min(1, optionsCost);
        return Math.Max(value, minimumCost);
    }

    private static int StandardRound(decimal value)
        => decimal.ToInt32(value >= 0m ? decimal.Ceiling(value) : decimal.Floor(value));

    private static bool IsValidIdentity(CharacterCareerKnowledgeSkillIdentity? identity)
        => identity is { SkillId: var skillId, SourceSkillId: var sourceSkillId }
            && skillId != Guid.Empty
            && sourceSkillId != Guid.Empty;

    private static bool IsBoundedRequiredText(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumNameLength;

    private static bool IsBoundedOptionalText(string? value)
        => value is not null && value.Length <= MaximumNameLength;

    private static string CalculateRuleDigest(CharacterCareerKnowledgeSkillAdvanceInput input)
    {
        IEnumerable<string> modifiers = input.Modifiers
            .OrderBy(static modifier => modifier.ModifierIdentity, StringComparer.Ordinal)
            .Select(modifier => string.Join(":",
                modifier.ModifierIdentity,
                modifier.Kind.ToString(),
                modifier.Target,
                modifier.Minimum.ToString(CultureInfo.InvariantCulture),
                modifier.Maximum.ToString(CultureInfo.InvariantCulture),
                modifier.Value.ToString(CultureInfo.InvariantCulture)));
        return Sha256(string.Join('\0',
            input.Identity.SkillId.ToString("D"),
            FormatSourceSkillId(input.Identity.SourceSkillId),
            input.AllowUpgrade.ToString(CultureInfo.InvariantCulture),
            input.IsNativeLanguage.ToString(CultureInfo.InvariantCulture),
            input.DictionaryKey,
            input.SkillType,
            input.SkillCategory,
            input.TotalBaseRating.ToString(CultureInfo.InvariantCulture),
            input.RatingMaximum.ToString(CultureInfo.InvariantCulture),
            input.AvailableKarma.ToString(CultureInfo.InvariantCulture),
            input.Settings.KarmaNewKnowledgeSkill.ToString(CultureInfo.InvariantCulture),
            input.Settings.KarmaImproveKnowledgeSkill.ToString(CultureInfo.InvariantCulture),
            string.Join("|", modifiers),
            input.RawRuleState));
    }

    private static string CalculateLogicalRevision(
        CharacterCareerKnowledgeSkillIdentity identity,
        string name,
        string skillType,
        string category,
        bool allowUpgrade,
        bool isNativeLanguage,
        int basePoints,
        int karmaPoints,
        int rating,
        int maximum,
        int availableKarma,
        int karmaCost,
        bool canAdvance,
        CharacterCareerKnowledgeSkillAdvanceBlocker blocker,
        string sourceRevision,
        string ruleDigest)
        => Sha256(string.Join('\0',
            identity.SkillId.ToString("D"),
            FormatSourceSkillId(identity.SourceSkillId),
            name,
            skillType,
            category,
            allowUpgrade.ToString(CultureInfo.InvariantCulture),
            isNativeLanguage.ToString(CultureInfo.InvariantCulture),
            basePoints.ToString(CultureInfo.InvariantCulture),
            karmaPoints.ToString(CultureInfo.InvariantCulture),
            rating.ToString(CultureInfo.InvariantCulture),
            maximum.ToString(CultureInfo.InvariantCulture),
            availableKarma.ToString(CultureInfo.InvariantCulture),
            karmaCost.ToString(CultureInfo.InvariantCulture),
            canAdvance.ToString(CultureInfo.InvariantCulture),
            blocker.ToString(),
            sourceRevision,
            ruleDigest));

    private static string FormatSourceSkillId(Guid? sourceSkillId)
        => sourceSkillId?.ToString("D") ?? "custom";

    private static bool RevisionMatches(string actual, string? expected)
        => IsLowerHexRevision(actual)
            && string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool IsLowerHexRevision(string? value)
        => value is { Length: RevisionHexLength }
            && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static CharacterCareerKnowledgeSkillAdvanceQuote UnavailableQuote()
        => new(
            new CharacterCareerKnowledgeSkillIdentity(Guid.Empty, null),
            string.Empty,
            string.Empty,
            string.Empty,
            false,
            false,
            0,
            0,
            0,
            0,
            0,
            -1,
            false,
            CharacterCareerKnowledgeSkillAdvanceBlocker.UpgradeDisallowed,
            string.Empty,
            string.Empty,
            string.Empty);

    private static CharacterCareerKnowledgeSkillAdvancePlan UnavailablePlan()
        => new(
            new CharacterCareerKnowledgeSkillIdentity(Guid.Empty, null),
            0,
            0,
            0,
            string.Empty,
            DateTime.MinValue,
            Guid.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            0m,
            string.Empty,
            string.Empty);
}
