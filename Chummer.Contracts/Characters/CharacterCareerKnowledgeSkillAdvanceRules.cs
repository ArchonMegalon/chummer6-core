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

public enum CharacterCareerKnowledgeSkillAdvancePrerequisite
{
    CareerCharacter,
    Sr5Ruleset,
    KnowledgeSkill,
    ExactIdentity,
    UpgradeAllowed,
    NotNativeLanguage,
    BelowMaximum,
    SufficientKarma
}

public sealed record CharacterCareerKnowledgeSkillAdvancePrerequisiteResult(
    CharacterCareerKnowledgeSkillAdvancePrerequisite Prerequisite,
    bool Satisfied,
    string Authority);

public sealed record CharacterCareerKnowledgeSkillAdvanceInput(
    CharacterCareerKnowledgeSkillIdentity Identity,
    bool Created,
    string RulesetId,
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
    string RawCharacterState,
    string RawSourceState,
    string RawRuleState);

public enum CharacterCareerKnowledgeSkillAdvanceBlocker
{
    None,
    NotCareerCharacter,
    UnsupportedRuleset,
    NotKnowledgeSkill,
    ForeignIdentity,
    UpgradeDisallowed,
    NativeLanguage,
    AtMaximum,
    InsufficientKarma
}

public enum CharacterCareerKnowledgeSkillTimeAuthority
{
    ImmediateChummerPersistence
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
    TimeSpan ApplicationDuration,
    CharacterCareerKnowledgeSkillTimeAuthority TimeAuthority,
    IReadOnlyList<CharacterCareerKnowledgeSkillAdvancePrerequisiteResult> Prerequisites,
    bool CanAdvance,
    CharacterCareerKnowledgeSkillAdvanceBlocker Blocker,
    string CharacterRevision,
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
    string ExpectedCharacterRevision,
    string ExpectedLogicalRevision,
    string ExpectedSourceRevision,
    string ExpectedRuleDigest);

public sealed record CharacterCareerKnowledgeSkillAdvanceReceipt(
    Guid TransactionId,
    CharacterCareerKnowledgeSkillIdentity Identity,
    string Name,
    string SkillType,
    int SkillKarmaBefore,
    int SkillKarmaAfter,
    int CharacterKarmaBefore,
    int CharacterKarmaAfter,
    Guid ExpenseId,
    int ExpenseAmount,
    string CharacterRevision,
    string LogicalRevision,
    string SourceRevision,
    string RuleDigest,
    string ReceiptDigest);

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
    public const string RulesetId = "sr5";
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

        CharacterCareerKnowledgeSkillAdvanceBlocker blocker = ExpectedBlocker(validInput, karmaCost);
        bool canAdvance = blocker == CharacterCareerKnowledgeSkillAdvanceBlocker.None;
        CharacterCareerKnowledgeSkillAdvancePrerequisiteResult[] prerequisites =
        [
            new(CharacterCareerKnowledgeSkillAdvancePrerequisite.CareerCharacter,
                validInput.Created, "character.created"),
            new(CharacterCareerKnowledgeSkillAdvancePrerequisite.Sr5Ruleset,
                string.Equals(validInput.RulesetId, RulesetId, StringComparison.Ordinal),
                "ruleset.sr5"),
            new(CharacterCareerKnowledgeSkillAdvancePrerequisite.KnowledgeSkill,
                validInput.IsKnowledgeSkill, "newskills.knoskills"),
            new(CharacterCareerKnowledgeSkillAdvancePrerequisite.ExactIdentity,
                IsValidIdentity(validInput.Identity),
                $"knowledge-skill.instance:{validInput.Identity.SkillId:D}"),
            new(CharacterCareerKnowledgeSkillAdvancePrerequisite.UpgradeAllowed,
                validInput.AllowUpgrade, "knowledge-skill.disableupgrades"),
            new(CharacterCareerKnowledgeSkillAdvancePrerequisite.NotNativeLanguage,
                !validInput.IsNativeLanguage, "knowledge-skill.isnativelanguage"),
            new(CharacterCareerKnowledgeSkillAdvancePrerequisite.BelowMaximum,
                validInput.TotalBaseRating < validInput.RatingMaximum,
                "knowledge-skill.rating-maximum"),
            new(CharacterCareerKnowledgeSkillAdvancePrerequisite.SufficientKarma,
                karmaCost >= 0 && validInput.AvailableKarma >= karmaCost,
                "character.karma")
        ];
        string characterRevision = Sha256(validInput.RawCharacterState);
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
            prerequisites,
            canAdvance,
            blocker,
            characterRevision,
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
            TimeSpan.Zero,
            CharacterCareerKnowledgeSkillTimeAuthority.ImmediateChummerPersistence,
            prerequisites,
            canAdvance,
            blocker,
            characterRevision,
            logicalRevision,
            sourceRevision,
            ruleDigest);
        return true;
    }

    public static bool TryPlanAdvance(
        CharacterCareerKnowledgeSkillAdvanceQuote? current,
        string? expectedCharacterRevision,
        string? expectedLogicalRevision,
        string? expectedSourceRevision,
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
            || !RevisionMatches(current.CharacterRevision, expectedCharacterRevision)
            || !RevisionMatches(current.LogicalRevision, expectedLogicalRevision)
            || !RevisionMatches(current.SourceRevision, expectedSourceRevision)
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
                current.CharacterRevision,
                current.LogicalRevision,
                current.SourceRevision,
                current.RuleDigest);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static bool TryCreateReceipt(
        Guid transactionId,
        CharacterCareerKnowledgeSkillAdvanceQuote? reviewed,
        CharacterCareerKnowledgeSkillAdvancePlan? plan,
        int observedSkillKarma,
        int observedCharacterKarma,
        bool expenseExistsExactlyOnce,
        out CharacterCareerKnowledgeSkillAdvanceReceipt receipt)
    {
        receipt = UnavailableReceipt();
        if (transactionId == Guid.Empty
            || !IsCoherent(reviewed)
            || !IsCoherent(plan)
            || reviewed!.Identity != plan!.Identity
            || transactionId != plan.ExpenseId
            || !RevisionMatches(reviewed.CharacterRevision, plan.ExpectedCharacterRevision)
            || !RevisionMatches(reviewed.LogicalRevision, plan.ExpectedLogicalRevision)
            || !RevisionMatches(reviewed.SourceRevision, plan.ExpectedSourceRevision)
            || !RevisionMatches(reviewed.RuleDigest, plan.ExpectedRuleDigest)
            || !PlanMatchesQuote(reviewed, plan)
            || observedSkillKarma != plan.SavedSkillKarmaPoints
            || observedCharacterKarma != plan.SavedCharacterKarma
            || !expenseExistsExactlyOnce)
        {
            return false;
        }

        string digest = CalculateReceiptDigest(
            transactionId, reviewed.Identity, reviewed.Name, reviewed.SkillType,
            reviewed.KarmaPoints, observedSkillKarma, reviewed.AvailableKarma,
            observedCharacterKarma, plan.ExpenseId, plan.ExpenseAmount,
            reviewed.CharacterRevision, reviewed.LogicalRevision,
            reviewed.SourceRevision, reviewed.RuleDigest);
        receipt = new CharacterCareerKnowledgeSkillAdvanceReceipt(
            transactionId,
            reviewed.Identity,
            reviewed.Name,
            reviewed.SkillType,
            reviewed.KarmaPoints,
            observedSkillKarma,
            reviewed.AvailableKarma,
            observedCharacterKarma,
            plan.ExpenseId,
            plan.ExpenseAmount,
            reviewed.CharacterRevision,
            reviewed.LogicalRevision,
            reviewed.SourceRevision,
            reviewed.RuleDigest,
            digest);
        return true;
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
            && quote.ApplicationDuration == TimeSpan.Zero
            && quote.TimeAuthority == CharacterCareerKnowledgeSkillTimeAuthority.ImmediateChummerPersistence
            && IsCoherentPrerequisites(quote.Prerequisites)
            && PrerequisitesMatchQuote(quote)
            && quote.CanAdvance == (quote.Blocker == CharacterCareerKnowledgeSkillAdvanceBlocker.None)
            && quote.Blocker == ExpectedBlocker(quote)
            && IsLowerHexRevision(quote.CharacterRevision)
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
                    quote.Prerequisites,
                    quote.CanAdvance,
                    quote.Blocker,
                    quote.CharacterRevision,
                    quote.SourceRevision,
                    quote.RuleDigest),
                quote.LogicalRevision);

    public static bool IsCoherent(CharacterCareerKnowledgeSkillAdvancePlan? plan)
        => plan is not null
            && IsValidIdentity(plan.Identity)
            && plan.SavedSkillKarmaPoints is >= 0 and <= MaximumRating
            && plan.SavedCharacterKarma is >= 0 and <= MaximumKarma
            && plan.ExpenseAmount is <= 0 and >= -MaximumKarma
            && !string.IsNullOrWhiteSpace(plan.ExpenseReason)
            && plan.ExpenseReason.Length <= MaximumNameLength
            && plan.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
            && plan.ExpenseDateLocal >= MinimumExpenseDate
            && plan.ExpenseDateLocal <= MaximumExpenseDate
            && plan.ExpenseId != Guid.Empty
            && plan.KarmaUndoType is "AddSkill" or "ImproveSkill"
            && plan.NuyenUndoType == "AddCyberware"
            && plan.UndoObjectId == plan.Identity.SkillId.ToString("D")
            && plan.UndoQuantity == 0m
            && plan.UndoExtra == string.Empty
            && IsLowerHexRevision(plan.ExpectedCharacterRevision)
            && IsLowerHexRevision(plan.ExpectedLogicalRevision)
            && IsLowerHexRevision(plan.ExpectedSourceRevision)
            && IsLowerHexRevision(plan.ExpectedRuleDigest);

    public static bool IsCoherent(CharacterCareerKnowledgeSkillAdvanceReceipt? receipt)
        => receipt is not null
            && receipt.TransactionId != Guid.Empty
            && receipt.TransactionId == receipt.ExpenseId
            && IsValidIdentity(receipt.Identity)
            && IsBoundedRequiredText(receipt.Name)
            && IsBoundedOptionalText(receipt.SkillType)
            && receipt.SkillKarmaBefore is >= 0 and <= MaximumRating
            && receipt.SkillKarmaAfter == receipt.SkillKarmaBefore + 1
            && receipt.CharacterKarmaBefore is >= 0 and <= MaximumKarma
            && receipt.CharacterKarmaAfter is >= 0 and <= MaximumKarma
            && receipt.ExpenseAmount is <= 0 and >= -MaximumKarma
            && receipt.CharacterKarmaAfter == receipt.CharacterKarmaBefore + receipt.ExpenseAmount
            && IsLowerHexRevision(receipt.CharacterRevision)
            && IsLowerHexRevision(receipt.LogicalRevision)
            && IsLowerHexRevision(receipt.SourceRevision)
            && IsLowerHexRevision(receipt.RuleDigest)
            && RevisionMatches(
                CalculateReceiptDigest(
                    receipt.TransactionId, receipt.Identity, receipt.Name, receipt.SkillType,
                    receipt.SkillKarmaBefore, receipt.SkillKarmaAfter,
                    receipt.CharacterKarmaBefore, receipt.CharacterKarmaAfter,
                    receipt.ExpenseId, receipt.ExpenseAmount, receipt.CharacterRevision,
                    receipt.LogicalRevision, receipt.SourceRevision, receipt.RuleDigest),
                receipt.ReceiptDigest);

    private static CharacterCareerKnowledgeSkillAdvanceBlocker ExpectedBlocker(
        CharacterCareerKnowledgeSkillAdvanceInput input,
        int karmaCost)
    {
        if (!input.Created)
        {
            return CharacterCareerKnowledgeSkillAdvanceBlocker.NotCareerCharacter;
        }
        if (!string.Equals(input.RulesetId, RulesetId, StringComparison.Ordinal))
        {
            return CharacterCareerKnowledgeSkillAdvanceBlocker.UnsupportedRuleset;
        }
        if (!input.IsKnowledgeSkill)
        {
            return CharacterCareerKnowledgeSkillAdvanceBlocker.NotKnowledgeSkill;
        }
        if (!IsValidIdentity(input.Identity))
        {
            return CharacterCareerKnowledgeSkillAdvanceBlocker.ForeignIdentity;
        }
        if (input.IsNativeLanguage)
        {
            return CharacterCareerKnowledgeSkillAdvanceBlocker.NativeLanguage;
        }
        if (!input.AllowUpgrade)
        {
            return CharacterCareerKnowledgeSkillAdvanceBlocker.UpgradeDisallowed;
        }
        if (karmaCost < 0 || input.TotalBaseRating >= input.RatingMaximum)
        {
            return CharacterCareerKnowledgeSkillAdvanceBlocker.AtMaximum;
        }
        return input.AvailableKarma < karmaCost
            ? CharacterCareerKnowledgeSkillAdvanceBlocker.InsufficientKarma
            : CharacterCareerKnowledgeSkillAdvanceBlocker.None;
    }

    private static CharacterCareerKnowledgeSkillAdvanceBlocker ExpectedBlocker(
        CharacterCareerKnowledgeSkillAdvanceQuote quote)
    {
        Dictionary<CharacterCareerKnowledgeSkillAdvancePrerequisite, bool> prerequisites =
            quote.Prerequisites.ToDictionary(
                static value => value.Prerequisite,
                static value => value.Satisfied);
        if (!prerequisites[CharacterCareerKnowledgeSkillAdvancePrerequisite.CareerCharacter])
        {
            return CharacterCareerKnowledgeSkillAdvanceBlocker.NotCareerCharacter;
        }
        if (!prerequisites[CharacterCareerKnowledgeSkillAdvancePrerequisite.Sr5Ruleset])
        {
            return CharacterCareerKnowledgeSkillAdvanceBlocker.UnsupportedRuleset;
        }
        if (!prerequisites[CharacterCareerKnowledgeSkillAdvancePrerequisite.KnowledgeSkill])
        {
            return CharacterCareerKnowledgeSkillAdvanceBlocker.NotKnowledgeSkill;
        }
        if (!prerequisites[CharacterCareerKnowledgeSkillAdvancePrerequisite.ExactIdentity])
        {
            return CharacterCareerKnowledgeSkillAdvanceBlocker.ForeignIdentity;
        }
        if (!prerequisites[CharacterCareerKnowledgeSkillAdvancePrerequisite.NotNativeLanguage])
        {
            return CharacterCareerKnowledgeSkillAdvanceBlocker.NativeLanguage;
        }
        if (!prerequisites[CharacterCareerKnowledgeSkillAdvancePrerequisite.UpgradeAllowed])
        {
            return CharacterCareerKnowledgeSkillAdvanceBlocker.UpgradeDisallowed;
        }
        if (!prerequisites[CharacterCareerKnowledgeSkillAdvancePrerequisite.BelowMaximum]
            || quote.KarmaCost < 0)
        {
            return CharacterCareerKnowledgeSkillAdvanceBlocker.AtMaximum;
        }
        return prerequisites[CharacterCareerKnowledgeSkillAdvancePrerequisite.SufficientKarma]
            ? CharacterCareerKnowledgeSkillAdvanceBlocker.None
            : CharacterCareerKnowledgeSkillAdvanceBlocker.InsufficientKarma;
    }

    private static bool IsValidInput(CharacterCareerKnowledgeSkillAdvanceInput? input)
    {
        if (input is null
            || !IsValidIdentity(input.Identity)
            || input.RulesetId is null or { Length: > MaximumNameLength }
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
            || string.IsNullOrWhiteSpace(input.RawCharacterState)
            || input.RawCharacterState.Length > MaximumRuleTextLength
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

    private static bool IsCoherentPrerequisites(
        IReadOnlyList<CharacterCareerKnowledgeSkillAdvancePrerequisiteResult>? prerequisites)
    {
        if (prerequisites is null
            || prerequisites.Count != Enum.GetValues<CharacterCareerKnowledgeSkillAdvancePrerequisite>().Length)
        {
            return false;
        }

        CharacterCareerKnowledgeSkillAdvancePrerequisiteResult?[] values = prerequisites.ToArray();
        CharacterCareerKnowledgeSkillAdvancePrerequisite[] expected =
            Enum.GetValues<CharacterCareerKnowledgeSkillAdvancePrerequisite>();
        return !values.Any(static value => value is null)
            && values.Select(static value => value!.Prerequisite).SequenceEqual(expected)
            && values.All(static value => value is not null
                && Enum.IsDefined(value.Prerequisite)
                && !string.IsNullOrWhiteSpace(value.Authority)
                && value.Authority.Length <= MaximumNameLength);
    }

    private static bool PrerequisitesMatchQuote(CharacterCareerKnowledgeSkillAdvanceQuote quote)
    {
        CharacterCareerKnowledgeSkillAdvancePrerequisiteResult[] values =
            quote.Prerequisites.ToArray();
        return values[0].Authority == "character.created"
            && values[1].Authority == "ruleset.sr5"
            && values[2].Authority == "newskills.knoskills"
            && values[3].Authority == $"knowledge-skill.instance:{quote.Identity.SkillId:D}"
            && values[3].Satisfied
            && values[4].Authority == "knowledge-skill.disableupgrades"
            && values[4].Satisfied == quote.AllowUpgrade
            && values[5].Authority == "knowledge-skill.isnativelanguage"
            && values[5].Satisfied == !quote.IsNativeLanguage
            && values[6].Authority == "knowledge-skill.rating-maximum"
            && values[6].Satisfied == (quote.TotalBaseRating < quote.RatingMaximum)
            && values[7].Authority == "character.karma"
            && values[7].Satisfied == (quote.KarmaCost >= 0
                && quote.AvailableKarma >= quote.KarmaCost);
    }

    private static bool PlanMatchesQuote(
        CharacterCareerKnowledgeSkillAdvanceQuote quote,
        CharacterCareerKnowledgeSkillAdvancePlan plan)
    {
        try
        {
            int targetRating = checked(quote.TotalBaseRating + 1);
            return plan.SavedSkillKarmaPoints == checked(quote.KarmaPoints + 1)
                && plan.SavedCharacterKarma == checked(quote.AvailableKarma - quote.KarmaCost)
                && plan.ExpenseAmount == checked(-quote.KarmaCost)
                && string.Equals(
                    plan.ExpenseReason,
                    $"Knowledge Skill {quote.Name} {quote.TotalBaseRating.ToString(CultureInfo.InvariantCulture)} -> {targetRating.ToString(CultureInfo.InvariantCulture)}",
                    StringComparison.Ordinal)
                && plan.KarmaUndoType == (quote.TotalBaseRating == 0 ? "AddSkill" : "ImproveSkill")
                && plan.NuyenUndoType == "AddCyberware"
                && plan.UndoObjectId == quote.Identity.SkillId.ToString("D")
                && plan.UndoQuantity == 0m
                && plan.UndoExtra == string.Empty;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

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
            "chummer5a.knowledge-skill-upgrade/v2",
            input.Identity.SkillId.ToString("D"),
            FormatSourceSkillId(input.Identity.SourceSkillId),
            input.Created.ToString(CultureInfo.InvariantCulture),
            input.RulesetId,
            input.IsKnowledgeSkill.ToString(CultureInfo.InvariantCulture),
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
        IReadOnlyList<CharacterCareerKnowledgeSkillAdvancePrerequisiteResult> prerequisites,
        bool canAdvance,
        CharacterCareerKnowledgeSkillAdvanceBlocker blocker,
        string characterRevision,
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
            string.Join("|", prerequisites.Select(prerequisite => string.Join(":",
                prerequisite.Prerequisite.ToString(),
                prerequisite.Satisfied.ToString(CultureInfo.InvariantCulture),
                prerequisite.Authority))),
            canAdvance.ToString(CultureInfo.InvariantCulture),
            blocker.ToString(),
            characterRevision,
            sourceRevision,
            ruleDigest));

    private static string CalculateReceiptDigest(
        Guid transactionId,
        CharacterCareerKnowledgeSkillIdentity identity,
        string name,
        string skillType,
        int skillKarmaBefore,
        int skillKarmaAfter,
        int characterKarmaBefore,
        int characterKarmaAfter,
        Guid expenseId,
        int expenseAmount,
        string characterRevision,
        string logicalRevision,
        string sourceRevision,
        string ruleDigest)
        => Sha256(string.Join('\0',
            transactionId.ToString("D"), identity.SkillId.ToString("D"),
            FormatSourceSkillId(identity.SourceSkillId), name, skillType,
            skillKarmaBefore.ToString(CultureInfo.InvariantCulture),
            skillKarmaAfter.ToString(CultureInfo.InvariantCulture),
            characterKarmaBefore.ToString(CultureInfo.InvariantCulture),
            characterKarmaAfter.ToString(CultureInfo.InvariantCulture),
            expenseId.ToString("D"), expenseAmount.ToString(CultureInfo.InvariantCulture),
            characterRevision, logicalRevision, sourceRevision, ruleDigest));

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
            TimeSpan.Zero,
            CharacterCareerKnowledgeSkillTimeAuthority.ImmediateChummerPersistence,
            [],
            false,
            CharacterCareerKnowledgeSkillAdvanceBlocker.ForeignIdentity,
            string.Empty,
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
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);

    private static CharacterCareerKnowledgeSkillAdvanceReceipt UnavailableReceipt()
        => new(
            Guid.Empty,
            new CharacterCareerKnowledgeSkillIdentity(Guid.Empty, null),
            string.Empty,
            string.Empty,
            0,
            0,
            0,
            0,
            Guid.Empty,
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);
}
