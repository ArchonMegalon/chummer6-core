using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public sealed record CharacterCareerSkillGroupIdentity(Guid InternalId);

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

public enum CharacterCareerSkillGroupPrerequisite
{
    CareerCharacter,
    Sr5Ruleset,
    ExactTarget,
    ExactMemberProjection,
    GroupIntact,
    GroupEnabled,
    BelowRatingMaximum,
    SufficientKarma
}

public sealed record CharacterCareerSkillGroupPrerequisiteResult(
    CharacterCareerSkillGroupPrerequisite Prerequisite,
    bool Satisfied,
    string Authority);

public sealed record CharacterCareerSkillGroupAdvanceInput(
    CharacterCareerSkillGroupIdentity Identity,
    bool Created,
    string RulesetId,
    bool TargetOwnedByCharacter,
    bool MemberProjectionIsExact,
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
    NotCareerCharacter,
    UnsupportedRuleset,
    ForeignTarget,
    InvalidMemberProjection,
    Broken,
    Disabled,
    AtMaximum,
    InsufficientKarma
}

public enum CharacterCareerSkillGroupTimeAuthority
{
    ImmediateChummerPersistence
}

public sealed record CharacterCareerSkillGroupAdvanceQuote(
    CharacterCareerSkillGroupIdentity Identity,
    string Name,
    int BasePoints,
    int KarmaPoints,
    int GroupRating,
    int CostRating,
    int TargetGroupRating,
    int TargetCostRating,
    int EnabledMemberCount,
    int RatingMaximum,
    int AvailableKarma,
    bool Disabled,
    bool Broken,
    int KarmaCost,
    TimeSpan ApplicationDuration,
    CharacterCareerSkillGroupTimeAuthority TimeAuthority,
    IReadOnlyList<CharacterCareerSkillGroupPrerequisiteResult> Prerequisites,
    bool CanAdvance,
    CharacterCareerSkillGroupAdvanceBlocker Blocker,
    string LogicalRevision,
    string SourceRevision,
    string RuleDigest);

public sealed record CharacterCareerSkillGroupAdvancePlan(
    CharacterCareerSkillGroupIdentity Identity,
    Guid TransactionId,
    int SavedGroupKarmaPoints,
    int SavedCharacterKarma,
    int ExpectedGroupRating,
    int ExpectedCostRating,
    int TargetGroupRating,
    int TargetCostRating,
    int EnabledMemberCount,
    int ExpenseAmount,
    string ExpenseReason,
    DateTime ExpenseDateLocal,
    Guid ExpenseId,
    string KarmaUndoType,
    string NuyenUndoType,
    string UndoObjectId,
    decimal UndoQuantity,
    string UndoExtra,
    string ExpectedLogicalRevision,
    string ExpectedSourceRevision,
    string ExpectedRuleDigest);

public sealed record CharacterCareerSkillGroupExpenseObservation(
    int MatchingEntryCount,
    Guid ExpenseId,
    DateTime ExpenseDateLocal,
    int Amount,
    string Reason,
    string ExpenseType,
    bool Refund,
    bool ForceCareerVisible,
    string KarmaUndoType,
    string NuyenUndoType,
    string UndoObjectId,
    decimal UndoQuantity,
    string UndoExtra);

public sealed record CharacterCareerSkillGroupAdvanceReceipt(
    Guid TransactionId,
    CharacterCareerSkillGroupIdentity Identity,
    int GroupKarmaBefore,
    int GroupKarmaAfter,
    int CharacterKarmaBefore,
    int CharacterKarmaAfter,
    int GroupRatingBefore,
    int GroupRatingAfter,
    int CostRatingBefore,
    int CostRatingAfter,
    int EnabledMemberCount,
    Guid ExpenseId,
    DateTime ExpenseDateLocal,
    int ExpenseAmount,
    string ExpenseReason,
    string ExpenseAuthorityDigest,
    string LogicalRevisionBefore,
    string SourceRevisionBefore,
    string RuleDigestBefore,
    string LogicalRevisionAfter,
    string SourceRevisionAfter,
    string RuleDigestAfter,
    string ReceiptDigest);

public sealed record CharacterCareerSkillGroupCorrectionPlan(
    Guid CorrectionId,
    Guid OriginalTransactionId,
    Guid ExpenseIdToRemove,
    CharacterCareerSkillGroupIdentity Identity,
    int SavedGroupKarmaPoints,
    int SavedCharacterKarma,
    int RestoredGroupRating,
    int RestoredCostRating,
    string Reason,
    string ExpectedPostLogicalRevision,
    string ExpectedPostSourceRevision,
    string ExpectedPostRuleDigest,
    string OriginalReceiptDigest,
    string CorrectionDigest);

/// <summary>
/// Deterministic SR5/Chummer5 authority for one Career-mode skill-group advancement.
/// Chummer5 commit fe4355d is the source authority: the cost rating is the minimum
/// TotalBaseRating among enabled members (or zero), group/category improvements use
/// exact ordinal targets and the mutation increments saved group Karma by one.
/// Persistence remains outside Core and must atomically claim TransactionId, compare
/// all three expected revisions, apply the plan, and persist its receipt.
/// </summary>
public static class CharacterCareerSkillGroupAdvanceRules
{
    public const string ContractName = "chummer.core.sr5-career-skill-group-advance/v2";
    public const string RulesetId = "sr5";
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

        CharacterCareerSkillGroupAdvanceInput valid = input!;
        int groupRating;
        int costRating;
        int enabledMemberCount;
        int karmaCost;
        try
        {
            CharacterCareerSkillGroupMember[] enabled = valid.Members
                .Where(static member => member.Enabled)
                .ToArray();
            enabledMemberCount = enabled.Length;
            costRating = enabled.Length == 0
                ? 0
                : enabled.Min(static member => member.TotalBaseRating);
            groupRating = checked(valid.BasePoints + valid.KarmaPoints);
            if (groupRating >= MaximumRating)
            {
                return false;
            }
            karmaCost = CalculateKarmaCost(valid, costRating);
        }
        catch (OverflowException)
        {
            return false;
        }

        CharacterCareerSkillGroupAdvanceBlocker blocker = ExpectedBlocker(
            valid,
            costRating,
            karmaCost);
        bool canAdvance = blocker == CharacterCareerSkillGroupAdvanceBlocker.None;
        int targetGroupRating = groupRating + 1;
        int targetCostRating = enabledMemberCount == 0
            ? 0
            : costRating == MaximumRating
                ? MaximumRating
                : costRating + 1;
        CharacterCareerSkillGroupPrerequisiteResult[] prerequisites =
        [
            new(CharacterCareerSkillGroupPrerequisite.CareerCharacter,
                valid.Created, "character.created"),
            new(CharacterCareerSkillGroupPrerequisite.Sr5Ruleset,
                string.Equals(valid.RulesetId, RulesetId, StringComparison.Ordinal),
                "ruleset.sr5"),
            new(CharacterCareerSkillGroupPrerequisite.ExactTarget,
                valid.TargetOwnedByCharacter,
                $"skill-group.internal-id:{valid.Identity.InternalId:D}"),
            new(CharacterCareerSkillGroupPrerequisite.ExactMemberProjection,
                valid.MemberProjectionIsExact, "skill-group.members:exact"),
            new(CharacterCareerSkillGroupPrerequisite.GroupIntact,
                !valid.Broken, "skill-group.is-broken"),
            new(CharacterCareerSkillGroupPrerequisite.GroupEnabled,
                !valid.Disabled, "skill-group.is-disabled"),
            new(CharacterCareerSkillGroupPrerequisite.BelowRatingMaximum,
                costRating < valid.RatingMaximum,
                "settings.max-skill-rating"),
            new(CharacterCareerSkillGroupPrerequisite.SufficientKarma,
                karmaCost >= 0 && valid.AvailableKarma >= karmaCost,
                "character.karma")
        ];
        string sourceRevision = Sha256(valid.RawSourceState);
        string ruleDigest = CalculateRuleDigest(valid);
        string logicalRevision = CalculateLogicalRevision(
            valid.Identity, valid.Name, valid.BasePoints, valid.KarmaPoints,
            groupRating, costRating, targetGroupRating, targetCostRating,
            enabledMemberCount, valid.RatingMaximum, valid.AvailableKarma,
            valid.Disabled, valid.Broken, karmaCost, prerequisites, canAdvance,
            blocker, sourceRevision, ruleDigest);

        quote = new CharacterCareerSkillGroupAdvanceQuote(
            valid.Identity, valid.Name, valid.BasePoints, valid.KarmaPoints,
            groupRating, costRating, targetGroupRating, targetCostRating,
            enabledMemberCount, valid.RatingMaximum, valid.AvailableKarma,
            valid.Disabled, valid.Broken, karmaCost, TimeSpan.Zero,
            CharacterCareerSkillGroupTimeAuthority.ImmediateChummerPersistence,
            prerequisites, canAdvance, blocker, logicalRevision, sourceRevision,
            ruleDigest);
        return true;
    }

    public static bool TryPlanAdvance(
        CharacterCareerSkillGroupAdvanceQuote? current,
        string? expectedLogicalRevision,
        string? expectedSourceRevision,
        string? expectedRuleDigest,
        bool confirmed,
        bool transactionIdAlreadyExists,
        Guid transactionId,
        DateTime expenseDateLocal,
        out CharacterCareerSkillGroupAdvancePlan plan)
    {
        plan = UnavailablePlan();
        DateTime normalizedDate = DateTime.SpecifyKind(
            expenseDateLocal,
            DateTimeKind.Unspecified);
        if (!confirmed
            || transactionIdAlreadyExists
            || !IsCoherent(current)
            || !current!.CanAdvance
            || !RevisionMatches(current.LogicalRevision, expectedLogicalRevision)
            || !RevisionMatches(current.SourceRevision, expectedSourceRevision)
            || !RevisionMatches(current.RuleDigest, expectedRuleDigest)
            || transactionId == Guid.Empty
            || normalizedDate < MinimumExpenseDate
            || normalizedDate > MaximumExpenseDate)
        {
            return false;
        }

        try
        {
            plan = new CharacterCareerSkillGroupAdvancePlan(
                current.Identity,
                transactionId,
                checked(current.KarmaPoints + 1),
                checked(current.AvailableKarma - current.KarmaCost),
                current.GroupRating,
                current.CostRating,
                current.TargetGroupRating,
                current.TargetCostRating,
                current.EnabledMemberCount,
                checked(-current.KarmaCost),
                $"Skill Group {current.Name} {current.GroupRating.ToString(CultureInfo.InvariantCulture)} -> {current.TargetGroupRating.ToString(CultureInfo.InvariantCulture)}",
                normalizedDate,
                transactionId,
                "ImproveSkillGroup",
                "AddCyberware",
                current.Identity.InternalId.ToString("D"),
                0m,
                string.Empty,
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
        CharacterCareerSkillGroupAdvanceQuote? reviewed,
        CharacterCareerSkillGroupAdvancePlan? plan,
        CharacterCareerSkillGroupAdvanceQuote? observedPostState,
        CharacterCareerSkillGroupExpenseObservation? observedExpense,
        out CharacterCareerSkillGroupAdvanceReceipt receipt)
    {
        receipt = UnavailableReceipt();
        if (transactionId == Guid.Empty
            || !IsCoherent(reviewed)
            || !IsCoherent(plan)
            || !IsCoherent(observedPostState)
            || reviewed!.Identity != plan!.Identity
            || transactionId != plan.TransactionId
            || transactionId != plan.ExpenseId
            || !PlanMatchesQuote(reviewed, plan)
            || !PostStateMatches(reviewed, plan, observedPostState!)
            || !ExpenseMatchesPlan(observedExpense, plan))
        {
            return false;
        }

        string expenseDigest = CalculateExpenseAuthorityDigest(observedExpense!);
        string receiptDigest = CalculateReceiptDigest(
            transactionId, reviewed.Identity, reviewed.KarmaPoints,
            observedPostState!.KarmaPoints, reviewed.AvailableKarma,
            observedPostState.AvailableKarma, reviewed.GroupRating,
            observedPostState.GroupRating, reviewed.CostRating,
            observedPostState.CostRating, reviewed.EnabledMemberCount,
            plan.ExpenseId, plan.ExpenseDateLocal, plan.ExpenseAmount,
            plan.ExpenseReason, expenseDigest, reviewed.LogicalRevision,
            reviewed.SourceRevision, reviewed.RuleDigest,
            observedPostState.LogicalRevision, observedPostState.SourceRevision,
            observedPostState.RuleDigest);
        receipt = new CharacterCareerSkillGroupAdvanceReceipt(
            transactionId, reviewed.Identity, reviewed.KarmaPoints,
            observedPostState.KarmaPoints, reviewed.AvailableKarma,
            observedPostState.AvailableKarma, reviewed.GroupRating,
            observedPostState.GroupRating, reviewed.CostRating,
            observedPostState.CostRating, reviewed.EnabledMemberCount,
            plan.ExpenseId, plan.ExpenseDateLocal, plan.ExpenseAmount,
            plan.ExpenseReason, expenseDigest, reviewed.LogicalRevision,
            reviewed.SourceRevision, reviewed.RuleDigest,
            observedPostState.LogicalRevision, observedPostState.SourceRevision,
            observedPostState.RuleDigest, receiptDigest);
        return true;
    }

    public static bool TryRecoverReceipt(
        CharacterCareerSkillGroupAdvanceReceipt? persistedReceipt,
        Guid expectedTransactionId,
        CharacterCareerSkillGroupAdvanceQuote? observedPostState,
        CharacterCareerSkillGroupExpenseObservation? observedExpense,
        string? expectedReceiptDigest,
        out CharacterCareerSkillGroupAdvanceReceipt receipt)
    {
        receipt = UnavailableReceipt();
        if (!IsCoherent(persistedReceipt)
            || expectedTransactionId == Guid.Empty
            || persistedReceipt!.TransactionId != expectedTransactionId
            || !RevisionMatches(persistedReceipt.ReceiptDigest, expectedReceiptDigest)
            || !IsCoherent(observedPostState)
            || !PostStateMatchesReceipt(persistedReceipt, observedPostState!)
            || !ExpenseMatchesReceipt(observedExpense, persistedReceipt))
        {
            return false;
        }

        receipt = persistedReceipt;
        return true;
    }

    public static bool TryPlanCorrection(
        CharacterCareerSkillGroupAdvanceReceipt? original,
        CharacterCareerSkillGroupAdvanceQuote? observedPostState,
        CharacterCareerSkillGroupExpenseObservation? observedExpense,
        Guid correctionId,
        string? reason,
        bool correctionIdAlreadyExists,
        bool originalTransactionAlreadyCorrected,
        string? expectedReceiptDigest,
        out CharacterCareerSkillGroupCorrectionPlan correction)
    {
        correction = UnavailableCorrection();
        string normalizedReason = reason?.Trim() ?? string.Empty;
        if (!IsCoherent(original)
            || !IsCoherent(observedPostState)
            || !PostStateMatchesReceipt(original!, observedPostState!)
            || !ExpenseMatchesReceipt(observedExpense, original!)
            || correctionId == Guid.Empty
            || correctionId == original!.TransactionId
            || correctionIdAlreadyExists
            || originalTransactionAlreadyCorrected
            || !RevisionMatches(original.ReceiptDigest, expectedReceiptDigest)
            || normalizedReason.Length is 0 or > MaximumNameLength)
        {
            return false;
        }

        string correctionDigest = CalculateCorrectionDigest(
            correctionId, original.TransactionId, original.ExpenseId,
            original.Identity, original.GroupKarmaBefore,
            original.CharacterKarmaBefore, original.GroupRatingBefore,
            original.CostRatingBefore, normalizedReason,
            original.LogicalRevisionAfter, original.SourceRevisionAfter,
            original.RuleDigestAfter, original.ReceiptDigest);
        correction = new CharacterCareerSkillGroupCorrectionPlan(
            correctionId, original.TransactionId, original.ExpenseId,
            original.Identity, original.GroupKarmaBefore,
            original.CharacterKarmaBefore, original.GroupRatingBefore,
            original.CostRatingBefore, normalizedReason,
            original.LogicalRevisionAfter, original.SourceRevisionAfter,
            original.RuleDigestAfter, original.ReceiptDigest,
            correctionDigest);
        return true;
    }

    public static bool IsCoherent(CharacterCareerSkillGroupAdvanceQuote? quote)
        => quote is not null
            && IsValidIdentity(quote.Identity)
            && !string.IsNullOrWhiteSpace(quote.Name)
            && quote.Name.Length <= MaximumNameLength
            && quote.BasePoints is >= 0 and <= MaximumRating
            && quote.KarmaPoints is >= 0 and <= MaximumRating
            && quote.GroupRating == quote.BasePoints + quote.KarmaPoints
            && quote.GroupRating is >= 0 and <= MaximumRating
            && quote.CostRating is >= 0 and <= MaximumRating
            && quote.TargetGroupRating == Math.Min(MaximumRating, quote.GroupRating + 1)
            && quote.TargetCostRating == (quote.EnabledMemberCount == 0
                ? 0
                : Math.Min(MaximumRating, quote.CostRating + 1))
            && quote.EnabledMemberCount is >= 0 and <= MaximumRating
            && quote.RatingMaximum is >= 0 and <= MaximumRating
            && quote.AvailableKarma is >= 0 and <= MaximumKarma
            && quote.KarmaCost is >= -1 and <= MaximumKarma
            && quote.ApplicationDuration == TimeSpan.Zero
            && quote.TimeAuthority
                == CharacterCareerSkillGroupTimeAuthority.ImmediateChummerPersistence
            && IsCoherentPrerequisites(quote.Prerequisites)
            && PrerequisitesMatchQuote(quote)
            && quote.CanAdvance
                == (quote.Blocker == CharacterCareerSkillGroupAdvanceBlocker.None)
            && quote.Blocker == ExpectedBlocker(quote)
            && IsLowerHexRevision(quote.SourceRevision)
            && IsLowerHexRevision(quote.RuleDigest)
            && RevisionMatches(
                CalculateLogicalRevision(
                    quote.Identity, quote.Name, quote.BasePoints, quote.KarmaPoints,
                    quote.GroupRating, quote.CostRating, quote.TargetGroupRating,
                    quote.TargetCostRating, quote.EnabledMemberCount,
                    quote.RatingMaximum, quote.AvailableKarma, quote.Disabled,
                    quote.Broken, quote.KarmaCost, quote.Prerequisites,
                    quote.CanAdvance, quote.Blocker, quote.SourceRevision,
                    quote.RuleDigest),
                quote.LogicalRevision);

    public static bool IsCoherent(CharacterCareerSkillGroupAdvancePlan? plan)
        => plan is not null
            && IsValidIdentity(plan.Identity)
            && plan.TransactionId != Guid.Empty
            && plan.TransactionId == plan.ExpenseId
            && plan.SavedGroupKarmaPoints is >= 0 and <= MaximumRating
            && plan.SavedCharacterKarma is >= 0 and <= MaximumKarma
            && plan.ExpectedGroupRating is >= 0 and < MaximumRating
            && plan.TargetGroupRating == plan.ExpectedGroupRating + 1
            && plan.ExpectedCostRating is >= 0 and <= MaximumRating
            && plan.TargetCostRating == (plan.EnabledMemberCount == 0
                ? 0
                : Math.Min(MaximumRating, plan.ExpectedCostRating + 1))
            && plan.EnabledMemberCount is >= 0 and <= MaximumRating
            && plan.ExpenseAmount is <= 0 and >= -MaximumKarma
            && !string.IsNullOrWhiteSpace(plan.ExpenseReason)
            && plan.ExpenseReason.Length <= MaximumNameLength
            && plan.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
            && plan.ExpenseDateLocal >= MinimumExpenseDate
            && plan.ExpenseDateLocal <= MaximumExpenseDate
            && plan.ExpenseId != Guid.Empty
            && plan.KarmaUndoType == "ImproveSkillGroup"
            && plan.NuyenUndoType == "AddCyberware"
            && plan.UndoObjectId == plan.Identity.InternalId.ToString("D")
            && plan.UndoQuantity == 0m
            && plan.UndoExtra == string.Empty
            && IsLowerHexRevision(plan.ExpectedLogicalRevision)
            && IsLowerHexRevision(plan.ExpectedSourceRevision)
            && IsLowerHexRevision(plan.ExpectedRuleDigest);

    public static bool IsCoherent(CharacterCareerSkillGroupAdvanceReceipt? receipt)
        => receipt is not null
            && receipt.TransactionId != Guid.Empty
            && receipt.TransactionId == receipt.ExpenseId
            && IsValidIdentity(receipt.Identity)
            && receipt.GroupKarmaBefore is >= 0 and < MaximumRating
            && receipt.GroupKarmaAfter == receipt.GroupKarmaBefore + 1
            && receipt.CharacterKarmaBefore is >= 0 and <= MaximumKarma
            && receipt.CharacterKarmaAfter is >= 0 and <= MaximumKarma
            && receipt.CharacterKarmaAfter
                == receipt.CharacterKarmaBefore + receipt.ExpenseAmount
            && receipt.GroupRatingBefore is >= 0 and < MaximumRating
            && receipt.GroupRatingAfter == receipt.GroupRatingBefore + 1
            && receipt.CostRatingBefore is >= 0 and <= MaximumRating
            && receipt.CostRatingAfter == (receipt.EnabledMemberCount == 0
                ? 0
                : Math.Min(MaximumRating, receipt.CostRatingBefore + 1))
            && receipt.EnabledMemberCount is >= 0 and <= MaximumRating
            && receipt.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
            && receipt.ExpenseDateLocal >= MinimumExpenseDate
            && receipt.ExpenseDateLocal <= MaximumExpenseDate
            && receipt.ExpenseAmount is <= 0 and >= -MaximumKarma
            && !string.IsNullOrWhiteSpace(receipt.ExpenseReason)
            && receipt.ExpenseReason.Length <= MaximumNameLength
            && IsLowerHexRevision(receipt.ExpenseAuthorityDigest)
            && IsLowerHexRevision(receipt.LogicalRevisionBefore)
            && IsLowerHexRevision(receipt.SourceRevisionBefore)
            && IsLowerHexRevision(receipt.RuleDigestBefore)
            && IsLowerHexRevision(receipt.LogicalRevisionAfter)
            && IsLowerHexRevision(receipt.SourceRevisionAfter)
            && IsLowerHexRevision(receipt.RuleDigestAfter)
            && RevisionMatches(
                CalculateReceiptDigest(
                    receipt.TransactionId, receipt.Identity,
                    receipt.GroupKarmaBefore, receipt.GroupKarmaAfter,
                    receipt.CharacterKarmaBefore, receipt.CharacterKarmaAfter,
                    receipt.GroupRatingBefore, receipt.GroupRatingAfter,
                    receipt.CostRatingBefore, receipt.CostRatingAfter,
                    receipt.EnabledMemberCount, receipt.ExpenseId,
                    receipt.ExpenseDateLocal, receipt.ExpenseAmount,
                    receipt.ExpenseReason, receipt.ExpenseAuthorityDigest,
                    receipt.LogicalRevisionBefore, receipt.SourceRevisionBefore,
                    receipt.RuleDigestBefore, receipt.LogicalRevisionAfter,
                    receipt.SourceRevisionAfter, receipt.RuleDigestAfter),
                receipt.ReceiptDigest);

    public static bool IsCoherent(CharacterCareerSkillGroupCorrectionPlan? correction)
        => correction is not null
            && correction.CorrectionId != Guid.Empty
            && correction.OriginalTransactionId != Guid.Empty
            && correction.CorrectionId != correction.OriginalTransactionId
            && correction.ExpenseIdToRemove == correction.OriginalTransactionId
            && IsValidIdentity(correction.Identity)
            && correction.SavedGroupKarmaPoints is >= 0 and < MaximumRating
            && correction.SavedCharacterKarma is >= 0 and <= MaximumKarma
            && correction.RestoredGroupRating is >= 0 and < MaximumRating
            && correction.RestoredCostRating is >= 0 and <= MaximumRating
            && !string.IsNullOrWhiteSpace(correction.Reason)
            && correction.Reason.Length <= MaximumNameLength
            && IsLowerHexRevision(correction.ExpectedPostLogicalRevision)
            && IsLowerHexRevision(correction.ExpectedPostSourceRevision)
            && IsLowerHexRevision(correction.ExpectedPostRuleDigest)
            && IsLowerHexRevision(correction.OriginalReceiptDigest)
            && RevisionMatches(
                CalculateCorrectionDigest(
                    correction.CorrectionId, correction.OriginalTransactionId,
                    correction.ExpenseIdToRemove, correction.Identity,
                    correction.SavedGroupKarmaPoints,
                    correction.SavedCharacterKarma,
                    correction.RestoredGroupRating,
                    correction.RestoredCostRating, correction.Reason,
                    correction.ExpectedPostLogicalRevision,
                    correction.ExpectedPostSourceRevision,
                    correction.ExpectedPostRuleDigest,
                    correction.OriginalReceiptDigest),
                correction.CorrectionDigest);

    private static bool IsValidInput(CharacterCareerSkillGroupAdvanceInput? input)
    {
        if (input is null
            || !IsValidIdentity(input.Identity)
            || input.RulesetId is null or { Length: > MaximumNameLength }
            || string.IsNullOrWhiteSpace(input.Name)
            || input.Name.Length > MaximumNameLength
            || input.BasePoints is < 0 or > MaximumRating
            || input.KarmaPoints is < 0 or > MaximumRating
            || input.RatingMaximum is < 0 or > MaximumRating
            || input.AvailableKarma is < 0 or > MaximumKarma
            || input.Settings is not
                {
                    KarmaNewSkillGroup: >= 0 and <= MaximumKarma,
                    KarmaImproveSkillGroup: >= 0 and <= MaximumKarma
                }
            || input.Members is null
            || input.Modifiers is null
            || string.IsNullOrWhiteSpace(input.RawSourceState)
            || input.RawSourceState.Length > MaximumRuleTextLength
            || string.IsNullOrWhiteSpace(input.RawRuleState)
            || input.RawRuleState.Length > MaximumRuleTextLength)
        {
            return false;
        }

        CharacterCareerSkillGroupMember?[] members = input.Members.ToArray();
        if (members.Any(static member => member is null
                || member.SkillId == Guid.Empty
                || member.TotalBaseRating is < 0 or > MaximumRating
                || string.IsNullOrWhiteSpace(member.SkillCategory)
                || member.SkillCategory.Length > MaximumNameLength)
            || members.Select(static member => member!.SkillId).Distinct().Count()
                != members.Length)
        {
            return false;
        }

        CharacterCareerSkillGroupKarmaModifier?[] modifiers =
            input.Modifiers.ToArray();
        return !modifiers.Any(static modifier => modifier is null)
            && modifiers.Select(static modifier => modifier!.ModifierIdentity)
                .Distinct(StringComparer.Ordinal).Count() == modifiers.Length
            && modifiers.All(modifier => modifier is not null
                && IsValidModifier(input, modifier));
    }

    private static bool IsValidModifier(
        CharacterCareerSkillGroupAdvanceInput input,
        CharacterCareerSkillGroupKarmaModifier modifier)
    {
        if (!Enum.IsDefined(modifier.Kind)
            || !IsLowerHexRevision(modifier.ModifierIdentity)
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
            ? modifier.Target.Length == 0
                || string.Equals(modifier.Target, input.Name, StringComparison.Ordinal)
            : modifier.Target.Length != 0
                && input.Members.Any(member => string.Equals(
                    member.SkillCategory,
                    modifier.Target,
                    StringComparison.Ordinal));
    }

    private static int CalculateKarmaCost(
        CharacterCareerSkillGroupAdvanceInput input,
        int costRating)
    {
        if (input.Disabled)
        {
            return -1;
        }

        int optionsCost;
        int result;
        if (costRating == 0)
        {
            optionsCost = input.Settings.KarmaNewSkillGroup;
            result = optionsCost;
        }
        else if (input.RatingMaximum > costRating)
        {
            optionsCost = input.Settings.KarmaImproveSkillGroup;
            result = checked((costRating + 1) * optionsCost);
        }
        else
        {
            return -1;
        }

        int targetRating = checked(costRating + 1);
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

            bool applies = modifier.Kind is
                    CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCost
                    or CharacterCareerSkillGroupKarmaModifierKind.SkillGroupCostMultiplier
                ? modifier.Target.Length == 0
                    || string.Equals(modifier.Target, input.Name, StringComparison.Ordinal)
                : categories.Contains(modifier.Target);
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
                    throw new InvalidOperationException(
                        "Unsupported skill-group Karma modifier kind.");
            }
        }

        result = multiplier != 1m
            ? StandardRound(checked(result * multiplier + extra))
            : checked(result + StandardRound(extra));
        return Math.Max(result, Math.Min(1, optionsCost));
    }

    private static bool PlanMatchesQuote(
        CharacterCareerSkillGroupAdvanceQuote quote,
        CharacterCareerSkillGroupAdvancePlan plan)
    {
        try
        {
            return plan.Identity == quote.Identity
                && plan.SavedGroupKarmaPoints == checked(quote.KarmaPoints + 1)
                && plan.SavedCharacterKarma == checked(
                    quote.AvailableKarma - quote.KarmaCost)
                && plan.ExpectedGroupRating == quote.GroupRating
                && plan.ExpectedCostRating == quote.CostRating
                && plan.TargetGroupRating == quote.TargetGroupRating
                && plan.TargetCostRating == quote.TargetCostRating
                && plan.EnabledMemberCount == quote.EnabledMemberCount
                && plan.ExpenseAmount == checked(-quote.KarmaCost)
                && plan.ExpenseReason ==
                    $"Skill Group {quote.Name} {quote.GroupRating.ToString(CultureInfo.InvariantCulture)} -> {quote.TargetGroupRating.ToString(CultureInfo.InvariantCulture)}"
                && plan.KarmaUndoType == "ImproveSkillGroup"
                && plan.NuyenUndoType == "AddCyberware"
                && plan.UndoObjectId == quote.Identity.InternalId.ToString("D")
                && plan.UndoQuantity == 0m
                && plan.UndoExtra == string.Empty
                && RevisionMatches(
                    quote.LogicalRevision,
                    plan.ExpectedLogicalRevision)
                && RevisionMatches(quote.SourceRevision, plan.ExpectedSourceRevision)
                && RevisionMatches(quote.RuleDigest, plan.ExpectedRuleDigest);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool PostStateMatches(
        CharacterCareerSkillGroupAdvanceQuote reviewed,
        CharacterCareerSkillGroupAdvancePlan plan,
        CharacterCareerSkillGroupAdvanceQuote post)
        => post.Identity == reviewed.Identity
            && post.Name == reviewed.Name
            && post.BasePoints == reviewed.BasePoints
            && post.KarmaPoints == plan.SavedGroupKarmaPoints
            && post.GroupRating == plan.TargetGroupRating
            && post.CostRating == plan.TargetCostRating
            && post.EnabledMemberCount == reviewed.EnabledMemberCount
            && post.RatingMaximum == reviewed.RatingMaximum
            && post.AvailableKarma == plan.SavedCharacterKarma
            && post.Disabled == reviewed.Disabled
            && post.Broken == reviewed.Broken;

    private static bool PostStateMatchesReceipt(
        CharacterCareerSkillGroupAdvanceReceipt receipt,
        CharacterCareerSkillGroupAdvanceQuote post)
        => post.Identity == receipt.Identity
            && post.KarmaPoints == receipt.GroupKarmaAfter
            && post.AvailableKarma == receipt.CharacterKarmaAfter
            && post.GroupRating == receipt.GroupRatingAfter
            && post.CostRating == receipt.CostRatingAfter
            && post.EnabledMemberCount == receipt.EnabledMemberCount
            && RevisionMatches(post.LogicalRevision, receipt.LogicalRevisionAfter)
            && RevisionMatches(post.SourceRevision, receipt.SourceRevisionAfter)
            && RevisionMatches(post.RuleDigest, receipt.RuleDigestAfter);

    private static bool ExpenseMatchesPlan(
        CharacterCareerSkillGroupExpenseObservation? expense,
        CharacterCareerSkillGroupAdvancePlan plan)
        => IsValidExpenseObservation(expense)
            && expense!.MatchingEntryCount == 1
            && expense.ExpenseId == plan.ExpenseId
            && expense.ExpenseDateLocal == plan.ExpenseDateLocal
            && expense.Amount == plan.ExpenseAmount
            && expense.Reason == plan.ExpenseReason
            && expense.ExpenseType == "Karma"
            && !expense.Refund
            && expense.ForceCareerVisible
            && expense.KarmaUndoType == plan.KarmaUndoType
            && expense.NuyenUndoType == plan.NuyenUndoType
            && expense.UndoObjectId == plan.UndoObjectId
            && expense.UndoQuantity == plan.UndoQuantity
            && expense.UndoExtra == plan.UndoExtra;

    private static bool ExpenseMatchesReceipt(
        CharacterCareerSkillGroupExpenseObservation? expense,
        CharacterCareerSkillGroupAdvanceReceipt receipt)
        => IsValidExpenseObservation(expense)
            && expense!.MatchingEntryCount == 1
            && expense.ExpenseId == receipt.ExpenseId
            && expense.ExpenseDateLocal == receipt.ExpenseDateLocal
            && expense.Amount == receipt.ExpenseAmount
            && expense.Reason == receipt.ExpenseReason
            && expense.ExpenseType == "Karma"
            && !expense.Refund
            && expense.ForceCareerVisible
            && expense.KarmaUndoType == "ImproveSkillGroup"
            && expense.NuyenUndoType == "AddCyberware"
            && expense.UndoObjectId == receipt.Identity.InternalId.ToString("D")
            && expense.UndoQuantity == 0m
            && expense.UndoExtra == string.Empty
            && RevisionMatches(
                CalculateExpenseAuthorityDigest(expense),
                receipt.ExpenseAuthorityDigest);

    private static bool IsValidExpenseObservation(
        CharacterCareerSkillGroupExpenseObservation? expense)
        => expense is not null
            && expense.MatchingEntryCount is >= 0 and <= MaximumRating
            && expense.ExpenseId != Guid.Empty
            && expense.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
            && expense.ExpenseDateLocal >= MinimumExpenseDate
            && expense.ExpenseDateLocal <= MaximumExpenseDate
            && expense.Amount is <= 0 and >= -MaximumKarma
            && !string.IsNullOrWhiteSpace(expense.Reason)
            && expense.Reason.Length <= MaximumNameLength
            && expense.ExpenseType is { Length: > 0 and <= MaximumNameLength }
            && expense.KarmaUndoType is { Length: > 0 and <= MaximumNameLength }
            && expense.NuyenUndoType is { Length: > 0 and <= MaximumNameLength }
            && expense.UndoObjectId is { Length: > 0 and <= MaximumNameLength }
            && expense.UndoExtra is { Length: <= MaximumNameLength };

    private static CharacterCareerSkillGroupAdvanceBlocker ExpectedBlocker(
        CharacterCareerSkillGroupAdvanceInput input,
        int costRating,
        int karmaCost)
    {
        if (!input.Created)
        {
            return CharacterCareerSkillGroupAdvanceBlocker.NotCareerCharacter;
        }

        if (!string.Equals(input.RulesetId, RulesetId, StringComparison.Ordinal))
        {
            return CharacterCareerSkillGroupAdvanceBlocker.UnsupportedRuleset;
        }

        if (!IsValidIdentity(input.Identity))
        {
            return CharacterCareerSkillGroupAdvanceBlocker.ForeignTarget;
        }

        if (!input.TargetOwnedByCharacter)
        {
            return CharacterCareerSkillGroupAdvanceBlocker.ForeignTarget;
        }

        if (!input.MemberProjectionIsExact)
        {
            return CharacterCareerSkillGroupAdvanceBlocker.InvalidMemberProjection;
        }

        if (input.Broken)
        {
            return CharacterCareerSkillGroupAdvanceBlocker.Broken;
        }

        if (input.Disabled)
        {
            return CharacterCareerSkillGroupAdvanceBlocker.Disabled;
        }

        if (karmaCost < 0 || costRating >= input.RatingMaximum)
        {
            return CharacterCareerSkillGroupAdvanceBlocker.AtMaximum;
        }

        return input.AvailableKarma < karmaCost
            ? CharacterCareerSkillGroupAdvanceBlocker.InsufficientKarma
            : CharacterCareerSkillGroupAdvanceBlocker.None;
    }

    private static CharacterCareerSkillGroupAdvanceBlocker ExpectedBlocker(
        CharacterCareerSkillGroupAdvanceQuote quote)
    {
        Dictionary<CharacterCareerSkillGroupPrerequisite, bool> prerequisites =
            quote.Prerequisites.ToDictionary(
                static value => value.Prerequisite,
                static value => value.Satisfied);
        if (!prerequisites[CharacterCareerSkillGroupPrerequisite.CareerCharacter])
        {
            return CharacterCareerSkillGroupAdvanceBlocker.NotCareerCharacter;
        }

        if (!prerequisites[CharacterCareerSkillGroupPrerequisite.Sr5Ruleset])
        {
            return CharacterCareerSkillGroupAdvanceBlocker.UnsupportedRuleset;
        }

        if (!prerequisites[CharacterCareerSkillGroupPrerequisite.ExactTarget])
        {
            return CharacterCareerSkillGroupAdvanceBlocker.ForeignTarget;
        }

        if (!prerequisites[CharacterCareerSkillGroupPrerequisite.ExactMemberProjection])
        {
            return CharacterCareerSkillGroupAdvanceBlocker.InvalidMemberProjection;
        }

        if (!prerequisites[CharacterCareerSkillGroupPrerequisite.GroupIntact])
        {
            return CharacterCareerSkillGroupAdvanceBlocker.Broken;
        }

        if (!prerequisites[CharacterCareerSkillGroupPrerequisite.GroupEnabled])
        {
            return CharacterCareerSkillGroupAdvanceBlocker.Disabled;
        }

        bool belowMaximum = quote.CostRating < quote.RatingMaximum;
        if (prerequisites[CharacterCareerSkillGroupPrerequisite.BelowRatingMaximum]
            != belowMaximum)
        {
            return CharacterCareerSkillGroupAdvanceBlocker.ForeignTarget;
        }

        if (!belowMaximum || quote.KarmaCost < 0)
        {
            return CharacterCareerSkillGroupAdvanceBlocker.AtMaximum;
        }

        bool sufficientKarma = quote.AvailableKarma >= quote.KarmaCost;
        if (prerequisites[CharacterCareerSkillGroupPrerequisite.SufficientKarma]
            != sufficientKarma)
        {
            return CharacterCareerSkillGroupAdvanceBlocker.ForeignTarget;
        }

        return sufficientKarma
            ? CharacterCareerSkillGroupAdvanceBlocker.None
            : CharacterCareerSkillGroupAdvanceBlocker.InsufficientKarma;
    }

    private static bool IsCoherentPrerequisites(
        IReadOnlyList<CharacterCareerSkillGroupPrerequisiteResult>? prerequisites)
    {
        if (prerequisites is null
            || prerequisites.Count
                != Enum.GetValues<CharacterCareerSkillGroupPrerequisite>().Length)
        {
            return false;
        }

        CharacterCareerSkillGroupPrerequisiteResult?[] values =
            prerequisites.ToArray();
        CharacterCareerSkillGroupPrerequisite[] expected =
            Enum.GetValues<CharacterCareerSkillGroupPrerequisite>();
        return !values.Any(static value => value is null)
            && values.Select(static value => value!.Prerequisite)
                .SequenceEqual(expected)
            && values.All(static value => value is not null
                && Enum.IsDefined(value.Prerequisite)
                && !string.IsNullOrWhiteSpace(value.Authority)
                && value.Authority.Length <= MaximumNameLength);
    }

    private static bool PrerequisitesMatchQuote(
        CharacterCareerSkillGroupAdvanceQuote quote)
    {
        CharacterCareerSkillGroupPrerequisiteResult[] values =
            quote.Prerequisites.ToArray();
        return values[0].Authority == "character.created"
            && values[1].Authority == "ruleset.sr5"
            && values[2].Authority == $"skill-group.internal-id:{quote.Identity.InternalId:D}"
            && values[3].Authority == "skill-group.members:exact"
            && values[4].Authority == "skill-group.is-broken"
            && values[4].Satisfied == !quote.Broken
            && values[5].Authority == "skill-group.is-disabled"
            && values[5].Satisfied == !quote.Disabled
            && values[6].Authority == "settings.max-skill-rating"
            && values[6].Satisfied == (quote.CostRating < quote.RatingMaximum)
            && values[7].Authority == "character.karma"
            && values[7].Satisfied == (quote.KarmaCost >= 0
                && quote.AvailableKarma >= quote.KarmaCost);
    }

    private static string CalculateRuleDigest(
        CharacterCareerSkillGroupAdvanceInput input)
    {
        string members = Canonical(input.Members
            .OrderBy(static member => member.SkillId)
            .Select(member => Canonical(
                member.SkillId.ToString("D"),
                member.TotalBaseRating.ToString(CultureInfo.InvariantCulture),
                member.Enabled.ToString(CultureInfo.InvariantCulture),
                member.SkillCategory))
            .ToArray());
        string modifiers = Canonical(input.Modifiers
            .OrderBy(static modifier => modifier.ModifierIdentity,
                StringComparer.Ordinal)
            .Select(modifier => Canonical(
                modifier.ModifierIdentity,
                modifier.Kind.ToString(),
                modifier.Target,
                modifier.Minimum.ToString(CultureInfo.InvariantCulture),
                modifier.Maximum.ToString(CultureInfo.InvariantCulture),
                modifier.Value.ToString(CultureInfo.InvariantCulture)))
            .ToArray());
        return Sha256(Canonical(
            ContractName,
            "rule",
            input.RulesetId,
            input.Created.ToString(CultureInfo.InvariantCulture),
            input.TargetOwnedByCharacter.ToString(CultureInfo.InvariantCulture),
            input.MemberProjectionIsExact.ToString(CultureInfo.InvariantCulture),
            input.Identity.InternalId.ToString("D"),
            input.Name,
            input.BasePoints.ToString(CultureInfo.InvariantCulture),
            input.KarmaPoints.ToString(CultureInfo.InvariantCulture),
            input.RatingMaximum.ToString(CultureInfo.InvariantCulture),
            input.AvailableKarma.ToString(CultureInfo.InvariantCulture),
            input.Disabled.ToString(CultureInfo.InvariantCulture),
            input.Broken.ToString(CultureInfo.InvariantCulture),
            input.Settings.KarmaNewSkillGroup.ToString(CultureInfo.InvariantCulture),
            input.Settings.KarmaImproveSkillGroup.ToString(CultureInfo.InvariantCulture),
            members,
            modifiers,
            input.RawRuleState));
    }

    private static string CalculateLogicalRevision(
        CharacterCareerSkillGroupIdentity identity,
        string name,
        int basePoints,
        int karmaPoints,
        int groupRating,
        int costRating,
        int targetGroupRating,
        int targetCostRating,
        int enabledMemberCount,
        int ratingMaximum,
        int availableKarma,
        bool disabled,
        bool broken,
        int karmaCost,
        IReadOnlyList<CharacterCareerSkillGroupPrerequisiteResult> prerequisites,
        bool canAdvance,
        CharacterCareerSkillGroupAdvanceBlocker blocker,
        string sourceRevision,
        string ruleDigest)
        => Sha256(Canonical(
            ContractName,
            "logical",
            identity.InternalId.ToString("D"),
            name,
            basePoints.ToString(CultureInfo.InvariantCulture),
            karmaPoints.ToString(CultureInfo.InvariantCulture),
            groupRating.ToString(CultureInfo.InvariantCulture),
            costRating.ToString(CultureInfo.InvariantCulture),
            targetGroupRating.ToString(CultureInfo.InvariantCulture),
            targetCostRating.ToString(CultureInfo.InvariantCulture),
            enabledMemberCount.ToString(CultureInfo.InvariantCulture),
            ratingMaximum.ToString(CultureInfo.InvariantCulture),
            availableKarma.ToString(CultureInfo.InvariantCulture),
            disabled.ToString(CultureInfo.InvariantCulture),
            broken.ToString(CultureInfo.InvariantCulture),
            karmaCost.ToString(CultureInfo.InvariantCulture),
            Canonical(prerequisites.Select(prerequisite => Canonical(
                prerequisite.Prerequisite.ToString(),
                prerequisite.Satisfied.ToString(CultureInfo.InvariantCulture),
                prerequisite.Authority)).ToArray()),
            canAdvance.ToString(CultureInfo.InvariantCulture),
            blocker.ToString(),
            sourceRevision,
            ruleDigest));

    private static string CalculateExpenseAuthorityDigest(
        CharacterCareerSkillGroupExpenseObservation expense)
        => Sha256(Canonical(
            ContractName,
            "expense",
            expense.MatchingEntryCount.ToString(CultureInfo.InvariantCulture),
            expense.ExpenseId.ToString("D"),
            expense.ExpenseDateLocal.ToString("O", CultureInfo.InvariantCulture),
            expense.Amount.ToString(CultureInfo.InvariantCulture),
            expense.Reason,
            expense.ExpenseType,
            expense.Refund.ToString(CultureInfo.InvariantCulture),
            expense.ForceCareerVisible.ToString(CultureInfo.InvariantCulture),
            expense.KarmaUndoType,
            expense.NuyenUndoType,
            expense.UndoObjectId,
            expense.UndoQuantity.ToString(CultureInfo.InvariantCulture),
            expense.UndoExtra));

    private static string CalculateReceiptDigest(
        Guid transactionId,
        CharacterCareerSkillGroupIdentity identity,
        int groupKarmaBefore,
        int groupKarmaAfter,
        int characterKarmaBefore,
        int characterKarmaAfter,
        int groupRatingBefore,
        int groupRatingAfter,
        int costRatingBefore,
        int costRatingAfter,
        int enabledMemberCount,
        Guid expenseId,
        DateTime expenseDateLocal,
        int expenseAmount,
        string expenseReason,
        string expenseAuthorityDigest,
        string logicalRevisionBefore,
        string sourceRevisionBefore,
        string ruleDigestBefore,
        string logicalRevisionAfter,
        string sourceRevisionAfter,
        string ruleDigestAfter)
        => Sha256(Canonical(
            ContractName,
            "receipt",
            transactionId.ToString("D"),
            identity.InternalId.ToString("D"),
            groupKarmaBefore.ToString(CultureInfo.InvariantCulture),
            groupKarmaAfter.ToString(CultureInfo.InvariantCulture),
            characterKarmaBefore.ToString(CultureInfo.InvariantCulture),
            characterKarmaAfter.ToString(CultureInfo.InvariantCulture),
            groupRatingBefore.ToString(CultureInfo.InvariantCulture),
            groupRatingAfter.ToString(CultureInfo.InvariantCulture),
            costRatingBefore.ToString(CultureInfo.InvariantCulture),
            costRatingAfter.ToString(CultureInfo.InvariantCulture),
            enabledMemberCount.ToString(CultureInfo.InvariantCulture),
            expenseId.ToString("D"),
            expenseDateLocal.ToString("O", CultureInfo.InvariantCulture),
            expenseAmount.ToString(CultureInfo.InvariantCulture),
            expenseReason,
            expenseAuthorityDigest,
            logicalRevisionBefore,
            sourceRevisionBefore,
            ruleDigestBefore,
            logicalRevisionAfter,
            sourceRevisionAfter,
            ruleDigestAfter));

    private static string CalculateCorrectionDigest(
        Guid correctionId,
        Guid originalTransactionId,
        Guid expenseIdToRemove,
        CharacterCareerSkillGroupIdentity identity,
        int savedGroupKarmaPoints,
        int savedCharacterKarma,
        int restoredGroupRating,
        int restoredCostRating,
        string reason,
        string expectedPostLogicalRevision,
        string expectedPostSourceRevision,
        string expectedPostRuleDigest,
        string originalReceiptDigest)
        => Sha256(Canonical(
            ContractName,
            "correction",
            correctionId.ToString("D"),
            originalTransactionId.ToString("D"),
            expenseIdToRemove.ToString("D"),
            identity.InternalId.ToString("D"),
            savedGroupKarmaPoints.ToString(CultureInfo.InvariantCulture),
            savedCharacterKarma.ToString(CultureInfo.InvariantCulture),
            restoredGroupRating.ToString(CultureInfo.InvariantCulture),
            restoredCostRating.ToString(CultureInfo.InvariantCulture),
            reason,
            expectedPostLogicalRevision,
            expectedPostSourceRevision,
            expectedPostRuleDigest,
            originalReceiptDigest));

    private static string Canonical(params string[] values)
        => string.Concat(values.Select(value => string.Concat(
            value.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            value)));

    private static int StandardRound(decimal value)
        => decimal.ToInt32(
            value >= 0m ? decimal.Ceiling(value) : decimal.Floor(value));

    private static bool IsValidIdentity(CharacterCareerSkillGroupIdentity? identity)
        => identity is { InternalId: var id } && id != Guid.Empty;

    private static bool RevisionMatches(string actual, string? expected)
        => IsLowerHexRevision(actual)
            && string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool IsLowerHexRevision(string? value)
        => value is { Length: RevisionHexLength }
            && value.All(static character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f');

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static CharacterCareerSkillGroupAdvanceQuote UnavailableQuote()
        => new(
            new CharacterCareerSkillGroupIdentity(Guid.Empty), string.Empty,
            0, 0, 0, 0, 0, 0, 0, 0, 0, false, false, -1,
            TimeSpan.Zero,
            CharacterCareerSkillGroupTimeAuthority.ImmediateChummerPersistence,
            [], false, CharacterCareerSkillGroupAdvanceBlocker.ForeignTarget,
            string.Empty, string.Empty, string.Empty);

    private static CharacterCareerSkillGroupAdvancePlan UnavailablePlan()
        => new(
            new CharacterCareerSkillGroupIdentity(Guid.Empty), Guid.Empty,
            0, 0, 0, 0, 0,
            0, 0, 0, string.Empty, DateTime.MinValue, Guid.Empty, string.Empty,
            string.Empty, string.Empty, 0m, string.Empty, string.Empty,
            string.Empty, string.Empty);

    private static CharacterCareerSkillGroupAdvanceReceipt UnavailableReceipt()
        => new(
            Guid.Empty, new CharacterCareerSkillGroupIdentity(Guid.Empty),
            0, 0, 0, 0, 0, 0, 0, 0, 0, Guid.Empty, DateTime.MinValue, 0,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, string.Empty);

    private static CharacterCareerSkillGroupCorrectionPlan UnavailableCorrection()
        => new(
            Guid.Empty, Guid.Empty, Guid.Empty,
            new CharacterCareerSkillGroupIdentity(Guid.Empty), 0, 0, 0, 0,
            string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty);
}
