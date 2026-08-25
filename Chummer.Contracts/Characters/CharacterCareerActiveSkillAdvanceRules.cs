using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public sealed record CharacterCareerActiveSkillIdentity(
    Guid SkillId,
    Guid SourceSkillId);

public sealed record CharacterCareerActiveSkillAdvanceSettings(
    int KarmaNewActiveSkill,
    int KarmaImproveActiveSkill,
    int KarmaNewSkillGroup,
    int KarmaImproveSkillGroup,
    bool CompensateSkillGroupKarmaDifference);

public sealed record CharacterCareerActiveSkillGroupMember(
    Guid SkillId,
    int TotalBaseRating,
    bool Enabled);

public enum CharacterCareerActiveSkillKarmaModifierKind
{
    ActiveSkillCost,
    ActiveSkillCostMultiplier,
    SkillCategoryCost,
    SkillCategoryCostMultiplier
}

public sealed record CharacterCareerActiveSkillKarmaModifier(
    string ModifierIdentity,
    CharacterCareerActiveSkillKarmaModifierKind Kind,
    string Target,
    int Minimum,
    int Maximum,
    decimal Value);

public sealed record CharacterCareerActiveSkillAdvanceInput(
    CharacterCareerActiveSkillIdentity Identity,
    bool Created,
    string Name,
    string SkillCategory,
    string DictionaryKey,
    int BasePoints,
    int KarmaPoints,
    int TotalBaseRating,
    int RatingMaximum,
    int AvailableKarma,
    CharacterCareerActiveSkillAdvanceSettings Settings,
    IReadOnlyList<CharacterCareerActiveSkillGroupMember> OtherGroupMembers,
    IReadOnlyList<CharacterCareerActiveSkillKarmaModifier> Modifiers,
    string RawSourceState,
    string RawRuleState);

public enum CharacterCareerActiveSkillAdvanceBlocker
{
    None,
    AtMaximum,
    InsufficientKarma
}

public sealed record CharacterCareerActiveSkillAdvanceQuote(
    CharacterCareerActiveSkillIdentity Identity,
    string Name,
    string SkillCategory,
    int BasePoints,
    int KarmaPoints,
    int TotalBaseRating,
    int RatingMaximum,
    int AvailableKarma,
    int KarmaCost,
    bool CanAdvance,
    CharacterCareerActiveSkillAdvanceBlocker Blocker,
    string LogicalRevision,
    string SourceRevision,
    string RuleDigest);

public sealed record CharacterCareerActiveSkillAdvancePlan(
    CharacterCareerActiveSkillIdentity Identity,
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
/// Deterministic Chummer5 authority for raising one saved active skill in Career mode.
/// The caller supplies source-resolved settings, enabled skill-group peers and applicable
/// Improvement candidates; this type owns filtering, cost math, rounding and the exact
/// Karma expense/undo plan.
/// </summary>
public static class CharacterCareerActiveSkillAdvanceRules
{
    public const int RevisionHexLength = 64;
    public const int MaximumRating = 1000;
    public const int MaximumKarma = 9_999_999;
    public const int MaximumNameLength = 512;
    public const int MaximumRuleTextLength = 1_048_576;
    public static readonly DateTime MinimumExpenseDate = new(1753, 1, 1);
    public static readonly DateTime MaximumExpenseDate = new(9998, 12, 31, 23, 59, 59);

    public static bool TryCreateQuote(
        CharacterCareerActiveSkillAdvanceInput? input,
        out CharacterCareerActiveSkillAdvanceQuote quote)
    {
        quote = UnavailableQuote();
        if (!IsValidInput(input))
        {
            return false;
        }
        CharacterCareerActiveSkillAdvanceInput validInput = input!;

        int karmaCost;
        try
        {
            karmaCost = CalculateKarmaCost(validInput);
        }
        catch (OverflowException)
        {
            return false;
        }

        CharacterCareerActiveSkillAdvanceBlocker blocker = karmaCost < 0
            || validInput.TotalBaseRating >= validInput.RatingMaximum
                ? CharacterCareerActiveSkillAdvanceBlocker.AtMaximum
                : validInput.AvailableKarma < karmaCost
                    ? CharacterCareerActiveSkillAdvanceBlocker.InsufficientKarma
                    : CharacterCareerActiveSkillAdvanceBlocker.None;
        bool canAdvance = blocker == CharacterCareerActiveSkillAdvanceBlocker.None;
        string sourceRevision = Sha256(validInput.RawSourceState);
        string ruleDigest = CalculateRuleDigest(validInput);
        string logicalRevision = CalculateLogicalRevision(
            validInput.Identity,
            validInput.Name,
            validInput.SkillCategory,
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

        quote = new CharacterCareerActiveSkillAdvanceQuote(
            validInput.Identity,
            validInput.Name,
            validInput.SkillCategory,
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
        CharacterCareerActiveSkillAdvanceQuote? current,
        string? expectedRuleDigest,
        bool confirmed,
        Guid expenseId,
        DateTime expenseDateLocal,
        out CharacterCareerActiveSkillAdvancePlan plan)
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
            plan = new CharacterCareerActiveSkillAdvancePlan(
                current.Identity,
                savedSkillKarma,
                savedCharacterKarma,
                checked(-current.KarmaCost),
                $"Active Skill {current.Name} {current.TotalBaseRating.ToString(CultureInfo.InvariantCulture)} -> {targetRating.ToString(CultureInfo.InvariantCulture)}",
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

    public static bool IsCoherent(CharacterCareerActiveSkillAdvanceQuote? quote)
        => quote is not null
            && IsValidIdentity(quote.Identity)
            && !string.IsNullOrWhiteSpace(quote.Name)
            && quote.Name.Length <= MaximumNameLength
            && !string.IsNullOrWhiteSpace(quote.SkillCategory)
            && quote.SkillCategory.Length <= MaximumNameLength
            && quote.BasePoints is >= 0 and <= MaximumRating
            && quote.KarmaPoints is >= 0 and <= MaximumRating
            && quote.TotalBaseRating is >= 0 and <= MaximumRating
            && quote.RatingMaximum is >= 0 and <= MaximumRating
            && quote.AvailableKarma is >= 0 and <= MaximumKarma
            && quote.KarmaCost is >= -1 and <= MaximumKarma
            && quote.CanAdvance == (quote.Blocker == CharacterCareerActiveSkillAdvanceBlocker.None)
            && quote.Blocker == ExpectedBlocker(quote)
            && IsLowerHexRevision(quote.SourceRevision)
            && IsLowerHexRevision(quote.RuleDigest)
            && RevisionMatches(
                CalculateLogicalRevision(
                    quote.Identity,
                    quote.Name,
                    quote.SkillCategory,
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

    private static CharacterCareerActiveSkillAdvanceBlocker ExpectedBlocker(
        CharacterCareerActiveSkillAdvanceQuote quote)
        => quote.KarmaCost < 0 || quote.TotalBaseRating >= quote.RatingMaximum
            ? CharacterCareerActiveSkillAdvanceBlocker.AtMaximum
            : quote.AvailableKarma < quote.KarmaCost
                ? CharacterCareerActiveSkillAdvanceBlocker.InsufficientKarma
                : CharacterCareerActiveSkillAdvanceBlocker.None;

    private static bool IsValidInput(CharacterCareerActiveSkillAdvanceInput? input)
    {
        if (input is null
            || !input.Created
            || !IsValidIdentity(input.Identity)
            || string.IsNullOrWhiteSpace(input.Name)
            || input.Name.Length > MaximumNameLength
            || string.IsNullOrWhiteSpace(input.SkillCategory)
            || input.SkillCategory.Length > MaximumNameLength
            || string.IsNullOrWhiteSpace(input.DictionaryKey)
            || input.DictionaryKey.Length > MaximumNameLength
            || input.BasePoints is < 0 or > MaximumRating
            || input.KarmaPoints is < 0 or > MaximumRating
            || input.TotalBaseRating is < 0 or > MaximumRating
            || input.RatingMaximum is < 0 or > MaximumRating
            || input.AvailableKarma is < 0 or > MaximumKarma
            || !IsValidSettings(input.Settings)
            || input.OtherGroupMembers is null
            || input.Modifiers is null
            || string.IsNullOrWhiteSpace(input.RawSourceState)
            || input.RawSourceState.Length > MaximumRuleTextLength
            || string.IsNullOrWhiteSpace(input.RawRuleState)
            || input.RawRuleState.Length > MaximumRuleTextLength)
        {
            return false;
        }

        CharacterCareerActiveSkillGroupMember[] members = input.OtherGroupMembers.ToArray();
        if (members.Any(member => member.SkillId == Guid.Empty
                || member.SkillId == input.Identity.SkillId
                || member.TotalBaseRating is < 0 or > MaximumRating)
            || members.Select(static member => member.SkillId).Distinct().Count() != members.Length)
        {
            return false;
        }

        CharacterCareerActiveSkillKarmaModifier[] modifiers = input.Modifiers.ToArray();
        return modifiers.Select(static modifier => modifier.ModifierIdentity).Distinct(StringComparer.Ordinal).Count() == modifiers.Length
            && modifiers.All(modifier => IsValidModifier(input, modifier));
    }

    private static bool IsValidSettings(CharacterCareerActiveSkillAdvanceSettings? settings)
        => settings is not null
            && settings.KarmaNewActiveSkill is >= 0 and <= MaximumKarma
            && settings.KarmaImproveActiveSkill is >= 0 and <= MaximumKarma
            && settings.KarmaNewSkillGroup is >= 0 and <= MaximumKarma
            && settings.KarmaImproveSkillGroup is >= 0 and <= MaximumKarma;

    private static bool IsValidModifier(
        CharacterCareerActiveSkillAdvanceInput input,
        CharacterCareerActiveSkillKarmaModifier modifier)
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

        return modifier.Kind is CharacterCareerActiveSkillKarmaModifierKind.ActiveSkillCost
                or CharacterCareerActiveSkillKarmaModifierKind.ActiveSkillCostMultiplier
            ? string.IsNullOrEmpty(modifier.Target)
                || string.Equals(modifier.Target, input.DictionaryKey, StringComparison.Ordinal)
            : string.IsNullOrEmpty(modifier.Target)
                || string.Equals(modifier.Target, input.SkillCategory, StringComparison.Ordinal);
    }

    private static int CalculateKarmaCost(CharacterCareerActiveSkillAdvanceInput input)
    {
        int rating = input.TotalBaseRating;
        if (rating >= input.RatingMaximum)
        {
            return -1;
        }

        int optionsCost = rating == 0
            ? input.Settings.KarmaNewActiveSkill
            : input.Settings.KarmaImproveActiveSkill;
        int upgrade = rating == 0
            ? optionsCost
            : checked((rating + 1) * optionsCost);

        int skillGroupCostAdjustment = 0;
        CharacterCareerActiveSkillGroupMember[] enabledPeers = input.OtherGroupMembers
            .Where(static member => member.Enabled)
            .ToArray();
        if (input.Settings.CompensateSkillGroupKarmaDifference
            && enabledPeers.Length > 0
            && enabledPeers.Min(static member => member.TotalBaseRating) > rating)
        {
            int groupCost = rating == 0
                ? input.Settings.KarmaNewSkillGroup
                : checked((rating + 1) * input.Settings.KarmaImproveSkillGroup);
            int enabledMemberCount = checked(enabledPeers.Length + 1);
            int nakedSkillCost = rating == 0
                ? checked(enabledMemberCount * input.Settings.KarmaNewActiveSkill)
                : checked(enabledMemberCount * (rating + 1) * input.Settings.KarmaImproveActiveSkill);
            skillGroupCostAdjustment = checked(groupCost - nakedSkillCost);
            upgrade = checked(upgrade + skillGroupCostAdjustment);
        }

        int targetRating = checked(rating + 1);
        decimal extra = 0m;
        decimal multiplier = 1m;
        foreach (CharacterCareerActiveSkillKarmaModifier modifier in input.Modifiers)
        {
            if (modifier.Minimum > targetRating
                || modifier.Maximum != 0 && targetRating > modifier.Maximum)
            {
                continue;
            }

            switch (modifier.Kind)
            {
                case CharacterCareerActiveSkillKarmaModifierKind.ActiveSkillCost:
                case CharacterCareerActiveSkillKarmaModifierKind.SkillCategoryCost:
                    extra = checked(extra + modifier.Value);
                    break;
                case CharacterCareerActiveSkillKarmaModifierKind.ActiveSkillCostMultiplier:
                case CharacterCareerActiveSkillKarmaModifierKind.SkillCategoryCostMultiplier:
                    multiplier = checked(multiplier * (modifier.Value / 100m));
                    break;
                default:
                    throw new InvalidOperationException("Unsupported active-skill Karma modifier kind.");
            }
        }

        upgrade = multiplier != 1m
            ? StandardRound(checked(upgrade * multiplier + extra))
            : checked(upgrade + StandardRound(extra));
        int minimumCost = checked(Math.Min(1, optionsCost) + skillGroupCostAdjustment);
        return Math.Max(upgrade, minimumCost);
    }

    private static int StandardRound(decimal value)
        => decimal.ToInt32(value >= 0m ? decimal.Ceiling(value) : decimal.Floor(value));

    private static bool IsValidIdentity(CharacterCareerActiveSkillIdentity? identity)
        => identity is { SkillId: var skillId, SourceSkillId: var sourceSkillId }
            && skillId != Guid.Empty
            && sourceSkillId != Guid.Empty;

    private static string CalculateRuleDigest(CharacterCareerActiveSkillAdvanceInput input)
    {
        IEnumerable<string> members = input.OtherGroupMembers
            .OrderBy(static member => member.SkillId)
            .Select(member => string.Join(":",
                member.SkillId.ToString("D"),
                member.TotalBaseRating.ToString(CultureInfo.InvariantCulture),
                member.Enabled.ToString(CultureInfo.InvariantCulture)));
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
            input.Identity.SourceSkillId.ToString("D"),
            input.DictionaryKey,
            input.SkillCategory,
            input.RatingMaximum.ToString(CultureInfo.InvariantCulture),
            input.Settings.KarmaNewActiveSkill.ToString(CultureInfo.InvariantCulture),
            input.Settings.KarmaImproveActiveSkill.ToString(CultureInfo.InvariantCulture),
            input.Settings.KarmaNewSkillGroup.ToString(CultureInfo.InvariantCulture),
            input.Settings.KarmaImproveSkillGroup.ToString(CultureInfo.InvariantCulture),
            input.Settings.CompensateSkillGroupKarmaDifference.ToString(CultureInfo.InvariantCulture),
            string.Join("|", members),
            string.Join("|", modifiers),
            input.RawRuleState));
    }

    private static string CalculateLogicalRevision(
        CharacterCareerActiveSkillIdentity identity,
        string name,
        string category,
        int basePoints,
        int karmaPoints,
        int rating,
        int maximum,
        int availableKarma,
        int karmaCost,
        bool canAdvance,
        CharacterCareerActiveSkillAdvanceBlocker blocker,
        string sourceRevision,
        string ruleDigest)
        => Sha256(string.Join('\0',
            identity.SkillId.ToString("D"),
            identity.SourceSkillId.ToString("D"),
            name,
            category,
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

    private static bool RevisionMatches(string actual, string? expected)
        => IsLowerHexRevision(actual)
            && string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool IsLowerHexRevision(string? value)
        => value is { Length: RevisionHexLength }
            && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static CharacterCareerActiveSkillAdvanceQuote UnavailableQuote()
        => new(
            new CharacterCareerActiveSkillIdentity(Guid.Empty, Guid.Empty),
            string.Empty,
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            -1,
            false,
            CharacterCareerActiveSkillAdvanceBlocker.AtMaximum,
            string.Empty,
            string.Empty,
            string.Empty);

    private static CharacterCareerActiveSkillAdvancePlan UnavailablePlan()
        => new(
            new CharacterCareerActiveSkillIdentity(Guid.Empty, Guid.Empty),
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
