using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public sealed record CharacterCareerSkillGroupIdentity(Guid SkillGroupId);

public sealed record CharacterCareerSkillGroupAdvanceSettings(
    int KarmaNewSkillGroup,
    int KarmaImproveSkillGroup);

public sealed record CharacterCareerSkillGroupMember(
    Guid SkillId,
    int TotalBaseRating,
    bool Enabled,
    string SkillCategory);

public enum CharacterCareerSkillGroupKarmaModifierKind
{
    SkillGroupCost,
    SkillGroupCostMultiplier,
    SkillGroupCategoryCost,
    SkillGroupCategoryCostMultiplier
}

public sealed record CharacterCareerSkillGroupKarmaModifier(
    string ModifierIdentity,
    CharacterCareerSkillGroupKarmaModifierKind Kind,
    string Target,
    int Minimum,
    int Maximum,
    decimal Value);

public sealed record CharacterCareerSkillGroupAdvanceInput(
    CharacterCareerSkillGroupIdentity Identity,
    bool Created,
    string Name,
    int BasePoints,
    int KarmaPoints,
    int RatingMaximum,
    int AvailableKarma,
    bool Disabled,
    bool Broken,
    CharacterCareerSkillGroupAdvanceSettings Settings,
    IReadOnlyList<CharacterCareerSkillGroupMember> Members,
    IReadOnlyList<CharacterCareerSkillGroupKarmaModifier> Modifiers,
    string RawSourceState,
    string RawRuleState);

public enum CharacterCareerSkillGroupAdvanceBlocker
{
    None,
    Broken,
    Disabled,
    AtMaximum,
    InsufficientKarma
}

public sealed record CharacterCareerSkillGroupAdvanceQuote(
    CharacterCareerSkillGroupIdentity Identity,
    string Name,
    int BasePoints,
    int KarmaPoints,
    int Rating,
    int RatingMaximum,
    int AvailableKarma,
    bool Disabled,
    bool Broken,
    int KarmaCost,
    bool CanAdvance,
    CharacterCareerSkillGroupAdvanceBlocker Blocker,
    string LogicalRevision,
    string SourceRevision,
    string RuleDigest);

public sealed record CharacterCareerSkillGroupAdvancePlan(
    CharacterCareerSkillGroupIdentity Identity,
    int SavedGroupKarmaPoints,
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
/// Deterministic Chummer5 authority for SkillGroupControl.btnCareerIncrease.
/// Presentation supplies the exact saved group, enabled member ratings/categories,
/// source-profile settings and applicable Improvements. This type owns filtering,
/// cost math, Chummer rounding, confirmation and the exact expense/undo plan.
/// </summary>
public static class CharacterCareerSkillGroupAdvanceRules
{
    public const int RevisionHexLength = 64;
    public const int MaximumRating = 1000;
    public const int MaximumKarma = 9_999_999;
    public const int MaximumNameLength = 512;
    public const int MaximumRuleTextLength = 1_048_576;
    public static readonly DateTime MinimumExpenseDate = new(1753, 1, 1);
    public static readonly DateTime MaximumExpenseDate = new(9998, 12, 31, 23, 59, 59);

    public static bool TryCreateQuote(
        CharacterCareerSkillGroupAdvanceInput? input,
        out CharacterCareerSkillGroupAdvanceQuote quote)
    {
        quote = UnavailableQuote();
        if (!IsValidInput(input))
        {
            return false;
        }

        CharacterCareerSkillGroupAdvanceInput validInput = input!;
        int rating;
        int karmaCost;
        try
        {
            CharacterCareerSkillGroupMember[] enabled = validInput.Members
                .Where(static member => member.Enabled)
                .ToArray();
            rating = enabled.Length == 0
                ? 0
                : enabled.Min(static member => member.TotalBaseRating);
            karmaCost = CalculateKarmaCost(validInput, rating);
        }
        catch (OverflowException)
        {
            return false;
        }

        CharacterCareerSkillGroupAdvanceBlocker blocker = ExpectedBlocker(
            validInput.Broken,
            validInput.Disabled,
            rating,
            validInput.RatingMaximum,
            karmaCost,
            validInput.AvailableKarma);
        bool canAdvance = blocker == CharacterCareerSkillGroupAdvanceBlocker.None;
        string sourceRevision = Sha256(validInput.RawSourceState);
        string ruleDigest = CalculateRuleDigest(validInput, rating);
        string logicalRevision = CalculateLogicalRevision(
            validInput.Identity,
            validInput.Name,
            validInput.BasePoints,
            validInput.KarmaPoints,
            rating,
            validInput.RatingMaximum,
            validInput.AvailableKarma,
            validInput.Disabled,
            validInput.Broken,
            karmaCost,
            canAdvance,
            blocker,
            sourceRevision,
            ruleDigest);

        quote = new CharacterCareerSkillGroupAdvanceQuote(
            validInput.Identity,
            validInput.Name,
            validInput.BasePoints,
            validInput.KarmaPoints,
            rating,
            validInput.RatingMaximum,
            validInput.AvailableKarma,
            validInput.Disabled,
            validInput.Broken,
            karmaCost,
            canAdvance,
            blocker,
            logicalRevision,
            sourceRevision,
            ruleDigest);
        return true;
    }

    public static bool TryPlanAdvance(
        CharacterCareerSkillGroupAdvanceQuote? current,
        string? expectedRuleDigest,
        bool confirmed,
        Guid expenseId,
        DateTime expenseDateLocal,
        out CharacterCareerSkillGroupAdvancePlan plan)
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
            int targetRating = checked(current.Rating + 1);
            plan = new CharacterCareerSkillGroupAdvancePlan(
                current.Identity,
                checked(current.KarmaPoints + 1),
                checked(current.AvailableKarma - current.KarmaCost),
                checked(-current.KarmaCost),
                $"Skill Group {current.Name} {current.Rating.ToString(CultureInfo.InvariantCulture)} -> {targetRating.ToString(CultureInfo.InvariantCulture)}",
                normalizedDate,
                expenseId,
                "ImproveSkillGroup",
                "AddCyberware",
                current.Identity.SkillGroupId.ToString("D"),
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

    public static bool IsCoherent(CharacterCareerSkillGroupAdvanceQuote? quote)
        => quote is not null
            && IsValidIdentity(quote.Identity)
            && !string.IsNullOrWhiteSpace(quote.Name)
            && quote.Name.Length <= MaximumNameLength
            && quote.BasePoints is >= 0 and <= MaximumRating
            && quote.KarmaPoints is >= 0 and <= MaximumRating
            && quote.Rating is >= 0 and <= MaximumRating
            && quote.RatingMaximum is >= 0 and <= MaximumRating
            && quote.AvailableKarma is >= 0 and <= MaximumKarma
            && quote.KarmaCost is >= -1 and <= MaximumKarma
            && quote.CanAdvance == (quote.Blocker == CharacterCareerSkillGroupAdvanceBlocker.None)
            && quote.Blocker == ExpectedBlocker(
                quote.Broken,
                quote.Disabled,
                quote.Rating,
                quote.RatingMaximum,
                quote.KarmaCost,
                quote.AvailableKarma)
            && IsLowerHexRevision(quote.SourceRevision)
            && IsLowerHexRevision(quote.RuleDigest)
            && RevisionMatches(
                CalculateLogicalRevision(
                    quote.Identity,
                    quote.Name,
                    quote.BasePoints,
                    quote.KarmaPoints,
                    quote.Rating,
                    quote.RatingMaximum,
                    quote.AvailableKarma,
                    quote.Disabled,
                    quote.Broken,
                    quote.KarmaCost,
                    quote.CanAdvance,
                    quote.Blocker,
                    quote.SourceRevision,
                    quote.RuleDigest),
                quote.LogicalRevision);

    private static bool IsValidInput(CharacterCareerSkillGroupAdvanceInput? input)
    {
        if (input is null
            || !input.Created
            || !IsValidIdentity(input.Identity)
            || string.IsNullOrWhiteSpace(input.Name)
            || input.Name.Length > MaximumNameLength
            || input.BasePoints is < 0 or > MaximumRating
            || input.KarmaPoints is < 0 or > MaximumRating
            || input.RatingMaximum is < 0 or > MaximumRating
            || input.AvailableKarma is < 0 or > MaximumKarma
            || input.Settings is null
            || input.Settings.KarmaNewSkillGroup is < 0 or > MaximumKarma
            || input.Settings.KarmaImproveSkillGroup is < 0 or > MaximumKarma
            || input.Members is null
            || input.Modifiers is null
            || string.IsNullOrWhiteSpace(input.RawSourceState)
            || input.RawSourceState.Length > MaximumRuleTextLength
            || string.IsNullOrWhiteSpace(input.RawRuleState)
            || input.RawRuleState.Length > MaximumRuleTextLength)
        {
            return false;
        }

        CharacterCareerSkillGroupMember[] members = input.Members.ToArray();
        if (members.Length == 0
            || members.Any(member => member.SkillId == Guid.Empty
                || member.TotalBaseRating is < 0 or > MaximumRating
                || string.IsNullOrWhiteSpace(member.SkillCategory)
                || member.SkillCategory.Length > MaximumNameLength)
            || members.Select(static member => member.SkillId).Distinct().Count() != members.Length)
        {
            return false;
        }

        CharacterCareerSkillGroupKarmaModifier[] modifiers = input.Modifiers.ToArray();
        return modifiers.Select(static modifier => modifier.ModifierIdentity)
                .Distinct(StringComparer.Ordinal).Count() == modifiers.Length
            && modifiers.All(modifier => IsValidModifier(input, modifier));
    }

    private static bool IsValidModifier(
        CharacterCareerSkillGroupAdvanceInput input,
        CharacterCareerSkillGroupKarmaModifier modifier)
    {
        if (!IsLowerHexRevision(modifier.ModifierIdentity)
            || modifier.Target is null
            || modifier.Target.Length > MaximumNameLength
            || modifier.Minimum is < 0 or > MaximumRating
            || modifier.Maximum is < 0 or > MaximumRating
            || modifier.Maximum != 0 && modifier.Maximum < modifier.Minimum
            || modifier.Value is < -MaximumKarma or > MaximumKarma)
        {
            return false;
        }

        return modifier.Kind is CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCost
                or CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCostMultiplier
            ? string.IsNullOrEmpty(modifier.Target)
                || string.Equals(modifier.Target, input.Name, StringComparison.Ordinal)
            : string.IsNullOrEmpty(modifier.Target)
                || input.Members.Any(member =>
                    string.Equals(member.SkillCategory, modifier.Target, StringComparison.Ordinal));
    }

    private static int CalculateKarmaCost(
        CharacterCareerSkillGroupAdvanceInput input,
        int rating)
    {
        if (input.Disabled)
        {
            return -1;
        }

        int optionsCost;
        int result;
        if (rating == 0)
        {
            optionsCost = input.Settings.KarmaNewSkillGroup;
            result = optionsCost;
        }
        else if (input.RatingMaximum > rating)
        {
            optionsCost = input.Settings.KarmaImproveSkillGroup;
            result = checked((rating + 1) * optionsCost);
        }
        else
        {
            return -1;
        }

        int targetRating = checked(rating + 1);
        decimal extra = 0m;
        decimal multiplier = 1m;
        HashSet<string> categories = input.Members
            .Select(static member => member.SkillCategory)
            .ToHashSet(StringComparer.Ordinal);
        foreach (CharacterCareerSkillGroupKarmaModifier modifier in input.Modifiers)
        {
            if (modifier.Minimum > targetRating
                || modifier.Maximum != 0 && targetRating > modifier.Maximum)
            {
                continue;
            }
            bool applies = modifier.Kind is CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCost
                    or CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCostMultiplier
                ? string.IsNullOrEmpty(modifier.Target)
                    || string.Equals(modifier.Target, input.Name, StringComparison.Ordinal)
                : string.IsNullOrEmpty(modifier.Target)
                    || categories.Contains(modifier.Target);
            if (!applies)
            {
                continue;
            }

            switch (modifier.Kind)
            {
                case CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCost:
                case CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCategoryCost:
                    extra = checked(extra + modifier.Value);
                    break;
                case CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCostMultiplier:
                case CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCategoryCostMultiplier:
                    multiplier = checked(multiplier * (modifier.Value / 100m));
                    break;
                default:
                    throw new InvalidOperationException("Unsupported skill-group Karma modifier kind.");
            }
        }

        result = multiplier != 1m
            ? StandardRound(checked(result * multiplier + extra))
            : checked(result + StandardRound(extra));
        return Math.Max(result, Math.Min(1, optionsCost));
    }

    private static int StandardRound(decimal value)
        => decimal.ToInt32(value >= 0m ? decimal.Ceiling(value) : decimal.Floor(value));

    private static CharacterCareerSkillGroupAdvanceBlocker ExpectedBlocker(
        bool broken,
        bool disabled,
        int rating,
        int maximum,
        int karmaCost,
        int availableKarma)
        => broken
            ? CharacterCareerSkillGroupAdvanceBlocker.Broken
            : disabled
                ? CharacterCareerSkillGroupAdvanceBlocker.Disabled
                : karmaCost < 0 || rating >= maximum
                    ? CharacterCareerSkillGroupAdvanceBlocker.AtMaximum
                    : availableKarma < karmaCost
                        ? CharacterCareerSkillGroupAdvanceBlocker.InsufficientKarma
                        : CharacterCareerSkillGroupAdvanceBlocker.None;

    private static bool IsValidIdentity(CharacterCareerSkillGroupIdentity? identity)
        => identity is { SkillGroupId: var id } && id != Guid.Empty;

    private static string CalculateRuleDigest(
        CharacterCareerSkillGroupAdvanceInput input,
        int rating)
    {
        IEnumerable<string> members = input.Members
            .OrderBy(static member => member.SkillId)
            .Select(member => string.Join(":",
                member.SkillId.ToString("D"),
                member.TotalBaseRating.ToString(CultureInfo.InvariantCulture),
                member.Enabled.ToString(CultureInfo.InvariantCulture),
                member.SkillCategory));
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
            input.Identity.SkillGroupId.ToString("D"),
            input.Name,
            input.BasePoints.ToString(CultureInfo.InvariantCulture),
            input.KarmaPoints.ToString(CultureInfo.InvariantCulture),
            rating.ToString(CultureInfo.InvariantCulture),
            input.RatingMaximum.ToString(CultureInfo.InvariantCulture),
            input.AvailableKarma.ToString(CultureInfo.InvariantCulture),
            input.Disabled.ToString(CultureInfo.InvariantCulture),
            input.Broken.ToString(CultureInfo.InvariantCulture),
            input.Settings.KarmaNewSkillGroup.ToString(CultureInfo.InvariantCulture),
            input.Settings.KarmaImproveSkillGroup.ToString(CultureInfo.InvariantCulture),
            string.Join("|", members),
            string.Join("|", modifiers),
            input.RawRuleState));
    }

    private static string CalculateLogicalRevision(
        CharacterCareerSkillGroupIdentity identity,
        string name,
        int basePoints,
        int karmaPoints,
        int rating,
        int maximum,
        int availableKarma,
        bool disabled,
        bool broken,
        int karmaCost,
        bool canAdvance,
        CharacterCareerSkillGroupAdvanceBlocker blocker,
        string sourceRevision,
        string ruleDigest)
        => Sha256(string.Join('\0',
            identity.SkillGroupId.ToString("D"),
            name,
            basePoints.ToString(CultureInfo.InvariantCulture),
            karmaPoints.ToString(CultureInfo.InvariantCulture),
            rating.ToString(CultureInfo.InvariantCulture),
            maximum.ToString(CultureInfo.InvariantCulture),
            availableKarma.ToString(CultureInfo.InvariantCulture),
            disabled.ToString(CultureInfo.InvariantCulture),
            broken.ToString(CultureInfo.InvariantCulture),
            karmaCost.ToString(CultureInfo.InvariantCulture),
            canAdvance.ToString(CultureInfo.InvariantCulture),
            blocker.ToString(),
            sourceRevision,
            ruleDigest));

    private static bool RevisionMatches(string actual, string? expected)
        => IsLowerHexRevision(actual)
            && string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool IsLowerHexRevision(string? value)
        => value is { Length: RevisionHexLength }
            && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static CharacterCareerSkillGroupAdvanceQuote UnavailableQuote()
        => new(
            new CharacterCareerSkillGroupIdentity(Guid.Empty),
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            false,
            false,
            -1,
            false,
            CharacterCareerSkillGroupAdvanceBlocker.AtMaximum,
            string.Empty,
            string.Empty,
            string.Empty);

    private static CharacterCareerSkillGroupAdvancePlan UnavailablePlan()
        => new(
            new CharacterCareerSkillGroupIdentity(Guid.Empty),
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
