using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerAttributeKind
{
    Normal,
    Edge,
    Magic,
    Resonance
}

public sealed record CharacterCareerAttributeIdentity(
    string Abbreviation,
    CharacterCareerAttributeKind Kind);

public sealed record CharacterCareerAttributeAdvanceSettings(
    int KarmaAttribute,
    bool AlternateMetatypeAttributeKarma);

public enum CharacterCareerAttributeKarmaModifierKind
{
    AttributeKarmaCost,
    AttributeKarmaCostMultiplier
}

public sealed record CharacterCareerAttributeKarmaModifier(
    string ModifierIdentity,
    CharacterCareerAttributeKarmaModifierKind Kind,
    string TargetAbbreviation,
    int Minimum,
    int Maximum,
    decimal Value);

public enum CharacterCareerAttributePrerequisite
{
    CareerCharacter,
    Sr5Ruleset,
    ExactTarget,
    SpecialAttributeEnabled,
    BelowNaturalMaximum,
    SufficientKarma
}

public sealed record CharacterCareerAttributePrerequisiteResult(
    CharacterCareerAttributePrerequisite Prerequisite,
    bool Satisfied,
    string Authority);

public sealed record CharacterCareerAttributeAdvanceInput(
    CharacterCareerAttributeIdentity Identity,
    bool Created,
    string RulesetId,
    string DisplayName,
    int BasePoints,
    int KarmaPoints,
    int EffectiveValue,
    int NaturalMaximum,
    int MetatypeMinimum,
    int AvailableKarma,
    bool MagicEnabled,
    bool MysticAdept,
    bool MysticAdeptSecondMagicAttributeEnabled,
    bool ResonanceEnabled,
    int BurnedEdgePoints,
    CharacterCareerAttributeAdvanceSettings Settings,
    IReadOnlyList<CharacterCareerAttributeKarmaModifier> Modifiers,
    string RawSourceState,
    string RawRuleState);

public enum CharacterCareerAttributeAdvanceBlocker
{
    None,
    NotCareerCharacter,
    UnsupportedRuleset,
    ForeignTarget,
    SpecialAttributeDisabled,
    AtNaturalMaximum,
    InsufficientKarma
}

public enum CharacterCareerAttributeTimeAuthority
{
    ImmediateChummerPersistence
}

public sealed record CharacterCareerAttributeAdvanceQuote(
    CharacterCareerAttributeIdentity Identity,
    string DisplayName,
    int BasePoints,
    int KarmaPoints,
    int EffectiveValue,
    int TargetValue,
    int NaturalMaximum,
    int MetatypeMinimum,
    int AvailableKarma,
    int KarmaCost,
    bool RepairsBurnedEdge,
    int BurnedEdgePoints,
    TimeSpan ApplicationDuration,
    CharacterCareerAttributeTimeAuthority TimeAuthority,
    IReadOnlyList<CharacterCareerAttributePrerequisiteResult> Prerequisites,
    bool CanAdvance,
    CharacterCareerAttributeAdvanceBlocker Blocker,
    string LogicalRevision,
    string SourceRevision,
    string RuleDigest);

public sealed record CharacterCareerAttributeAdvancePlan(
    CharacterCareerAttributeIdentity Identity,
    int SavedAttributeKarmaPoints,
    int SavedCharacterKarma,
    int BurnedEdgePointsBefore,
    int SavedBurnedEdgePoints,
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

public sealed record CharacterCareerAttributeAdvanceReceipt(
    Guid TransactionId,
    CharacterCareerAttributeIdentity Identity,
    bool RepairsBurnedEdge,
    int AttributeKarmaBefore,
    int AttributeKarmaAfter,
    int CharacterKarmaBefore,
    int CharacterKarmaAfter,
    int BurnedEdgePointsBefore,
    int BurnedEdgePointsAfter,
    Guid ExpenseId,
    int ExpenseAmount,
    string LogicalRevision,
    string SourceRevision,
    string RuleDigest,
    string ReceiptDigest);

public sealed record CharacterCareerAttributeCorrectionPlan(
    Guid CorrectionId,
    Guid OriginalTransactionId,
    Guid ExpenseIdToRemove,
    CharacterCareerAttributeIdentity Identity,
    int SavedAttributeKarmaPoints,
    int SavedCharacterKarma,
    int SavedBurnedEdgePoints,
    string Reason,
    string OriginalReceiptDigest,
    string CorrectionDigest);

/// <summary>
/// Deterministic SR5/Chummer5 authority for a single Career-mode attribute advancement.
/// It deliberately models Chummer's immediate persistence duration (zero), not an
/// invented in-world training duration. Every forward or compensating operation is
/// bound to the exact source, rule and logical revisions that were reviewed.
/// </summary>
public static class CharacterCareerAttributeAdvanceRules
{
    public const int RevisionHexLength = 64;
    public const int MaximumRating = 1000;
    public const int MaximumKarma = 9_999_999;
    public const int MaximumNameLength = 512;
    public const int MaximumRuleTextLength = 1_048_576;
    public const string RulesetId = "sr5";
    public static readonly DateTime MinimumExpenseDate = new(1753, 1, 1);
    public static readonly DateTime MaximumExpenseDate = new(9998, 12, 31, 23, 59, 59);

    private static readonly CharacterCareerAttributeIdentity[] OrderedTargetCatalog =
    [
        new("BOD", CharacterCareerAttributeKind.Normal),
        new("AGI", CharacterCareerAttributeKind.Normal),
        new("REA", CharacterCareerAttributeKind.Normal),
        new("STR", CharacterCareerAttributeKind.Normal),
        new("CHA", CharacterCareerAttributeKind.Normal),
        new("INT", CharacterCareerAttributeKind.Normal),
        new("LOG", CharacterCareerAttributeKind.Normal),
        new("WIL", CharacterCareerAttributeKind.Normal),
        new("EDG", CharacterCareerAttributeKind.Edge),
        new("MAG", CharacterCareerAttributeKind.Magic),
        new("MAGAdept", CharacterCareerAttributeKind.Magic),
        new("RES", CharacterCareerAttributeKind.Resonance)
    ];

    private static readonly Dictionary<string, CharacterCareerAttributeKind> TargetCatalog =
        OrderedTargetCatalog.ToDictionary(
            static identity => identity.Abbreviation,
            static identity => identity.Kind,
            StringComparer.Ordinal);

    private static readonly HashSet<string> AlternateMetatypeExceptions = new(StringComparer.Ordinal)
    {
        "MAG", "RES", "DEP", "MAGAdept"
    };

    public static IReadOnlyList<CharacterCareerAttributeIdentity> GetTargetCatalog()
        => OrderedTargetCatalog.ToArray();

    public static bool TryCreateIdentity(
        string? abbreviation,
        out CharacterCareerAttributeIdentity identity)
    {
        identity = new CharacterCareerAttributeIdentity(string.Empty, CharacterCareerAttributeKind.Normal);
        if (abbreviation is null
            || !TargetCatalog.TryGetValue(abbreviation, out CharacterCareerAttributeKind kind))
        {
            return false;
        }

        identity = new CharacterCareerAttributeIdentity(abbreviation, kind);
        return true;
    }

    public static bool TryCreateQuote(
        CharacterCareerAttributeAdvanceInput? input,
        out CharacterCareerAttributeAdvanceQuote quote)
    {
        quote = UnavailableQuote();
        if (!IsValidInput(input))
        {
            return false;
        }

        CharacterCareerAttributeAdvanceInput valid = input!;
        int karmaCost;
        try
        {
            karmaCost = CalculateKarmaCost(valid);
        }
        catch (OverflowException)
        {
            return false;
        }

        CharacterCareerAttributeAdvanceBlocker blocker = ExpectedBlocker(valid, karmaCost);
        bool canAdvance = blocker == CharacterCareerAttributeAdvanceBlocker.None;
        int targetValue = valid.EffectiveValue == MaximumRating
            ? MaximumRating
            : valid.EffectiveValue + 1;
        bool repairsBurnedEdge = valid.Identity.Kind == CharacterCareerAttributeKind.Edge
            && valid.BurnedEdgePoints > 0;
        CharacterCareerAttributePrerequisiteResult[] prerequisites =
        [
            new(CharacterCareerAttributePrerequisite.CareerCharacter, valid.Created, "character.created"),
            new(CharacterCareerAttributePrerequisite.Sr5Ruleset,
                string.Equals(valid.RulesetId, RulesetId, StringComparison.Ordinal), "ruleset.sr5"),
            new(CharacterCareerAttributePrerequisite.ExactTarget, IsValidIdentity(valid.Identity),
                $"attribute.catalog:{valid.Identity.Abbreviation}"),
            new(CharacterCareerAttributePrerequisite.SpecialAttributeEnabled, IsTargetEnabled(valid),
                SpecialAttributeAuthority(valid.Identity)),
            new(CharacterCareerAttributePrerequisite.BelowNaturalMaximum,
                valid.EffectiveValue < valid.NaturalMaximum, "attribute.total-maximum"),
            new(CharacterCareerAttributePrerequisite.SufficientKarma,
                karmaCost >= 0 && valid.AvailableKarma >= karmaCost, "character.karma")
        ];
        string sourceRevision = Sha256(valid.RawSourceState);
        string ruleDigest = CalculateRuleDigest(valid);
        string logicalRevision = CalculateLogicalRevision(
            valid.Identity, valid.DisplayName, valid.BasePoints, valid.KarmaPoints,
            valid.EffectiveValue, targetValue, valid.NaturalMaximum, valid.MetatypeMinimum,
            valid.AvailableKarma, karmaCost, repairsBurnedEdge, valid.BurnedEdgePoints,
            prerequisites, canAdvance, blocker, sourceRevision, ruleDigest);

        quote = new CharacterCareerAttributeAdvanceQuote(
            valid.Identity,
            valid.DisplayName,
            valid.BasePoints,
            valid.KarmaPoints,
            valid.EffectiveValue,
            targetValue,
            valid.NaturalMaximum,
            valid.MetatypeMinimum,
            valid.AvailableKarma,
            karmaCost,
            repairsBurnedEdge,
            valid.BurnedEdgePoints,
            TimeSpan.Zero,
            CharacterCareerAttributeTimeAuthority.ImmediateChummerPersistence,
            prerequisites,
            canAdvance,
            blocker,
            logicalRevision,
            sourceRevision,
            ruleDigest);
        return true;
    }

    public static bool TryPlanAdvance(
        CharacterCareerAttributeAdvanceQuote? current,
        string? expectedLogicalRevision,
        string? expectedSourceRevision,
        string? expectedRuleDigest,
        bool confirmed,
        Guid expenseId,
        DateTime expenseDateLocal,
        out CharacterCareerAttributeAdvancePlan plan)
    {
        plan = UnavailablePlan();
        DateTime normalizedDate = DateTime.SpecifyKind(expenseDateLocal, DateTimeKind.Unspecified);
        if (!confirmed
            || !IsCoherent(current)
            || !current!.CanAdvance
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
            int savedAttributeKarma = current.RepairsBurnedEdge
                ? current.KarmaPoints
                : checked(current.KarmaPoints + 1);
            int savedBurnedEdge = current.RepairsBurnedEdge
                ? checked(current.BurnedEdgePoints - 1)
                : current.BurnedEdgePoints;
            plan = new CharacterCareerAttributeAdvancePlan(
                current.Identity,
                savedAttributeKarma,
                checked(current.AvailableKarma - current.KarmaCost),
                current.BurnedEdgePoints,
                savedBurnedEdge,
                checked(-current.KarmaCost),
                $"Attribute {current.Identity.Abbreviation} {current.EffectiveValue.ToString(CultureInfo.InvariantCulture)} -> {current.TargetValue.ToString(CultureInfo.InvariantCulture)}",
                normalizedDate,
                expenseId,
                "ImproveAttribute",
                "AddCyberware",
                current.Identity.Abbreviation,
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
        CharacterCareerAttributeAdvanceQuote? reviewed,
        CharacterCareerAttributeAdvancePlan? plan,
        int observedAttributeKarma,
        int observedCharacterKarma,
        int observedBurnedEdgePoints,
        bool expenseExistsExactlyOnce,
        out CharacterCareerAttributeAdvanceReceipt receipt)
    {
        receipt = UnavailableReceipt();
        if (transactionId == Guid.Empty
            || !IsCoherent(reviewed)
            || !IsCoherent(plan)
            || reviewed!.Identity != plan!.Identity
            || transactionId != plan.ExpenseId
            || !RevisionMatches(reviewed.LogicalRevision, plan.ExpectedLogicalRevision)
            || !RevisionMatches(reviewed.SourceRevision, plan.ExpectedSourceRevision)
            || !RevisionMatches(reviewed.RuleDigest, plan.ExpectedRuleDigest)
            || !PlanMatchesQuote(reviewed, plan)
            || observedAttributeKarma != plan.SavedAttributeKarmaPoints
            || observedCharacterKarma != plan.SavedCharacterKarma
            || observedBurnedEdgePoints != plan.SavedBurnedEdgePoints
            || !expenseExistsExactlyOnce)
        {
            return false;
        }

        string digest = CalculateReceiptDigest(
            transactionId, reviewed.Identity, reviewed.RepairsBurnedEdge, reviewed.KarmaPoints,
            observedAttributeKarma, reviewed.AvailableKarma, observedCharacterKarma,
            reviewed.BurnedEdgePoints, observedBurnedEdgePoints, plan.ExpenseId,
            plan.ExpenseAmount, reviewed.LogicalRevision, reviewed.SourceRevision,
            reviewed.RuleDigest);
        receipt = new CharacterCareerAttributeAdvanceReceipt(
            transactionId,
            reviewed.Identity,
            reviewed.RepairsBurnedEdge,
            reviewed.KarmaPoints,
            observedAttributeKarma,
            reviewed.AvailableKarma,
            observedCharacterKarma,
            reviewed.BurnedEdgePoints,
            observedBurnedEdgePoints,
            plan.ExpenseId,
            plan.ExpenseAmount,
            reviewed.LogicalRevision,
            reviewed.SourceRevision,
            reviewed.RuleDigest,
            digest);
        return true;
    }

    public static bool TryPlanCorrection(
        CharacterCareerAttributeAdvanceReceipt? original,
        Guid correctionId,
        string? reason,
        int observedAttributeKarma,
        int observedCharacterKarma,
        int observedBurnedEdgePoints,
        bool expenseExistsExactlyOnce,
        bool correctionIdAlreadyExists,
        string? expectedReceiptDigest,
        out CharacterCareerAttributeCorrectionPlan correction)
    {
        correction = UnavailableCorrection();
        string normalizedReason = reason?.Trim() ?? string.Empty;
        if (!IsCoherent(original)
            || correctionId == Guid.Empty
            || correctionId == original!.TransactionId
            || correctionIdAlreadyExists
            || !RevisionMatches(original.ReceiptDigest, expectedReceiptDigest)
            || normalizedReason.Length is 0 or > MaximumNameLength
            || observedAttributeKarma != original.AttributeKarmaAfter
            || observedCharacterKarma != original.CharacterKarmaAfter
            || observedBurnedEdgePoints != original.BurnedEdgePointsAfter
            || !expenseExistsExactlyOnce)
        {
            return false;
        }

        string correctionDigest = CalculateCorrectionDigest(
            correctionId, original.TransactionId, original.ExpenseId, original.Identity,
            original.AttributeKarmaBefore, original.CharacterKarmaBefore,
            original.BurnedEdgePointsBefore, normalizedReason, original.ReceiptDigest);
        correction = new CharacterCareerAttributeCorrectionPlan(
            correctionId,
            original.TransactionId,
            original.ExpenseId,
            original.Identity,
            original.AttributeKarmaBefore,
            original.CharacterKarmaBefore,
            original.BurnedEdgePointsBefore,
            normalizedReason,
            original.ReceiptDigest,
            correctionDigest);
        return true;
    }

    public static bool IsCoherent(CharacterCareerAttributeAdvanceQuote? quote)
        => quote is not null
            && IsValidIdentity(quote.Identity)
            && !string.IsNullOrWhiteSpace(quote.DisplayName)
            && quote.DisplayName.Length <= MaximumNameLength
            && quote.BasePoints is >= 0 and <= MaximumRating
            && quote.KarmaPoints is >= 0 and <= MaximumRating
            && quote.EffectiveValue is >= 0 and <= MaximumRating
            && quote.TargetValue == Math.Min(MaximumRating, quote.EffectiveValue + 1)
            && quote.NaturalMaximum is >= 0 and <= MaximumRating
            && quote.MetatypeMinimum is >= 0 and <= MaximumRating
            && quote.AvailableKarma is >= 0 and <= MaximumKarma
            && quote.KarmaCost is >= -1 and <= MaximumKarma
            && quote.BurnedEdgePoints is >= 0 and <= MaximumRating
            && quote.RepairsBurnedEdge == (quote.Identity.Kind == CharacterCareerAttributeKind.Edge
                && quote.BurnedEdgePoints > 0)
            && quote.ApplicationDuration == TimeSpan.Zero
            && quote.TimeAuthority == CharacterCareerAttributeTimeAuthority.ImmediateChummerPersistence
            && IsCoherentPrerequisites(quote.Prerequisites)
            && PrerequisitesMatchQuote(quote)
            && quote.CanAdvance == (quote.Blocker == CharacterCareerAttributeAdvanceBlocker.None)
            && quote.Blocker == ExpectedBlocker(quote)
            && IsLowerHexRevision(quote.SourceRevision)
            && IsLowerHexRevision(quote.RuleDigest)
            && RevisionMatches(
                CalculateLogicalRevision(
                    quote.Identity, quote.DisplayName, quote.BasePoints, quote.KarmaPoints,
                    quote.EffectiveValue, quote.TargetValue, quote.NaturalMaximum,
                    quote.MetatypeMinimum, quote.AvailableKarma, quote.KarmaCost,
                    quote.RepairsBurnedEdge, quote.BurnedEdgePoints, quote.Prerequisites,
                    quote.CanAdvance, quote.Blocker, quote.SourceRevision, quote.RuleDigest),
                quote.LogicalRevision);

    public static bool IsCoherent(CharacterCareerAttributeCorrectionPlan? correction)
        => correction is not null
            && correction.CorrectionId != Guid.Empty
            && correction.OriginalTransactionId != Guid.Empty
            && correction.CorrectionId != correction.OriginalTransactionId
            && correction.ExpenseIdToRemove == correction.OriginalTransactionId
            && IsValidIdentity(correction.Identity)
            && correction.SavedAttributeKarmaPoints is >= 0 and <= MaximumRating
            && correction.SavedCharacterKarma is >= 0 and <= MaximumKarma
            && correction.SavedBurnedEdgePoints is >= 0 and <= MaximumRating
            && !string.IsNullOrWhiteSpace(correction.Reason)
            && correction.Reason.Length <= MaximumNameLength
            && IsLowerHexRevision(correction.OriginalReceiptDigest)
            && RevisionMatches(
                CalculateCorrectionDigest(
                    correction.CorrectionId, correction.OriginalTransactionId,
                    correction.ExpenseIdToRemove, correction.Identity,
                    correction.SavedAttributeKarmaPoints, correction.SavedCharacterKarma,
                    correction.SavedBurnedEdgePoints, correction.Reason,
                    correction.OriginalReceiptDigest),
                correction.CorrectionDigest);

    public static bool IsCoherent(CharacterCareerAttributeAdvancePlan? plan)
        => plan is not null
            && IsValidIdentity(plan.Identity)
            && plan.SavedAttributeKarmaPoints is >= 0 and <= MaximumRating
            && plan.SavedCharacterKarma is >= 0 and <= MaximumKarma
            && plan.BurnedEdgePointsBefore is >= 0 and <= MaximumRating
            && plan.SavedBurnedEdgePoints is >= 0 and <= MaximumRating
            && plan.SavedBurnedEdgePoints >= plan.BurnedEdgePointsBefore - 1
            && plan.SavedBurnedEdgePoints <= plan.BurnedEdgePointsBefore
            && plan.ExpenseAmount is <= 0 and >= -MaximumKarma
            && !string.IsNullOrWhiteSpace(plan.ExpenseReason)
            && plan.ExpenseReason.Length <= MaximumNameLength
            && plan.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
            && plan.ExpenseDateLocal >= MinimumExpenseDate
            && plan.ExpenseDateLocal <= MaximumExpenseDate
            && plan.ExpenseId != Guid.Empty
            && plan.KarmaUndoType == "ImproveAttribute"
            && plan.NuyenUndoType == "AddCyberware"
            && plan.UndoObjectId == plan.Identity.Abbreviation
            && plan.UndoQuantity == 0m
            && plan.UndoExtra is not null
            && plan.UndoExtra.Length == 0
            && IsLowerHexRevision(plan.ExpectedLogicalRevision)
            && IsLowerHexRevision(plan.ExpectedSourceRevision)
            && IsLowerHexRevision(plan.ExpectedRuleDigest);

    public static bool IsCoherent(CharacterCareerAttributeAdvanceReceipt? receipt)
        => receipt is not null
            && receipt.TransactionId != Guid.Empty
            && receipt.TransactionId == receipt.ExpenseId
            && IsValidIdentity(receipt.Identity)
            && receipt.AttributeKarmaBefore is >= 0 and <= MaximumRating
            && receipt.AttributeKarmaAfter is >= 0 and <= MaximumRating
            && receipt.RepairsBurnedEdge == (receipt.Identity.Kind == CharacterCareerAttributeKind.Edge
                && receipt.BurnedEdgePointsBefore > 0)
            && receipt.AttributeKarmaAfter == (receipt.RepairsBurnedEdge
                ? receipt.AttributeKarmaBefore
                : receipt.AttributeKarmaBefore + 1)
            && receipt.CharacterKarmaBefore is >= 0 and <= MaximumKarma
            && receipt.CharacterKarmaAfter is >= 0 and <= MaximumKarma
            && receipt.BurnedEdgePointsBefore is >= 0 and <= MaximumRating
            && receipt.BurnedEdgePointsAfter is >= 0 and <= MaximumRating
            && receipt.BurnedEdgePointsAfter == (receipt.RepairsBurnedEdge
                ? receipt.BurnedEdgePointsBefore - 1
                : receipt.BurnedEdgePointsBefore)
            && receipt.ExpenseId != Guid.Empty
            && receipt.ExpenseAmount is <= 0 and >= -MaximumKarma
            && receipt.CharacterKarmaAfter == receipt.CharacterKarmaBefore + receipt.ExpenseAmount
            && IsLowerHexRevision(receipt.LogicalRevision)
            && IsLowerHexRevision(receipt.SourceRevision)
            && IsLowerHexRevision(receipt.RuleDigest)
            && RevisionMatches(
                CalculateReceiptDigest(
                    receipt.TransactionId, receipt.Identity, receipt.RepairsBurnedEdge,
                    receipt.AttributeKarmaBefore,
                    receipt.AttributeKarmaAfter, receipt.CharacterKarmaBefore,
                    receipt.CharacterKarmaAfter, receipt.BurnedEdgePointsBefore,
                    receipt.BurnedEdgePointsAfter, receipt.ExpenseId, receipt.ExpenseAmount,
                    receipt.LogicalRevision, receipt.SourceRevision, receipt.RuleDigest),
                receipt.ReceiptDigest);

    private static bool IsValidInput(CharacterCareerAttributeAdvanceInput? input)
    {
        if (input is null
            || !IsValidIdentity(input.Identity)
            || input.RulesetId is null or { Length: > MaximumNameLength }
            || string.IsNullOrWhiteSpace(input.DisplayName)
            || input.DisplayName.Length > MaximumNameLength
            || input.BasePoints is < 0 or > MaximumRating
            || input.KarmaPoints is < 0 or > MaximumRating
            || input.EffectiveValue is < 0 or > MaximumRating
            || input.NaturalMaximum is < 0 or > MaximumRating
            || input.MetatypeMinimum is < 0 or > MaximumRating
            || input.AvailableKarma is < 0 or > MaximumKarma
            || input.BurnedEdgePoints is < 0 or > MaximumRating
            || input.Identity.Kind != CharacterCareerAttributeKind.Edge && input.BurnedEdgePoints != 0
            || input.Settings is not { KarmaAttribute: >= 0 and <= MaximumKarma }
            || input.Modifiers is null
            || string.IsNullOrWhiteSpace(input.RawSourceState)
            || input.RawSourceState.Length > MaximumRuleTextLength
            || string.IsNullOrWhiteSpace(input.RawRuleState)
            || input.RawRuleState.Length > MaximumRuleTextLength)
        {
            return false;
        }

        CharacterCareerAttributeKarmaModifier?[] modifiers = input.Modifiers.ToArray();
        return !modifiers.Any(static modifier => modifier is null)
               && modifiers.Select(static modifier => modifier!.ModifierIdentity)
                   .Distinct(StringComparer.Ordinal).Count() == modifiers.Length
               && modifiers.All(modifier => modifier is not null
                   && IsValidModifier(input.Identity, modifier));
    }

    private static bool IsValidModifier(
        CharacterCareerAttributeIdentity identity,
        CharacterCareerAttributeKarmaModifier modifier)
        => Enum.IsDefined(modifier.Kind)
            && IsLowerHexRevision(modifier.ModifierIdentity)
            && modifier.TargetAbbreviation is not null
            && modifier.TargetAbbreviation.Length <= MaximumNameLength
            && (modifier.TargetAbbreviation.Length == 0
                || string.Equals(modifier.TargetAbbreviation, identity.Abbreviation, StringComparison.Ordinal))
            && modifier.Minimum is >= 0 and <= MaximumRating
            && modifier.Maximum is >= 0 and <= MaximumRating
            && (modifier.Maximum == 0 || modifier.Maximum >= modifier.Minimum)
            && modifier.Value is >= -MaximumKarma and <= MaximumKarma;

    private static bool IsValidIdentity(CharacterCareerAttributeIdentity? identity)
        => identity?.Abbreviation is not null
            && TargetCatalog.TryGetValue(identity.Abbreviation, out CharacterCareerAttributeKind kind)
            && kind == identity.Kind;

    private static bool IsTargetEnabled(CharacterCareerAttributeAdvanceInput input)
        => input.Identity.Abbreviation switch
        {
            "MAG" => input.MagicEnabled,
            "MAGAdept" => input.MagicEnabled
                && input.MysticAdept
                && input.MysticAdeptSecondMagicAttributeEnabled,
            "RES" => input.ResonanceEnabled,
            _ => true
        };

    private static string SpecialAttributeAuthority(CharacterCareerAttributeIdentity identity)
        => identity.Abbreviation switch
        {
            "MAG" => "character.magenabled",
            "MAGAdept" => "character.magenabled+character.isadept+settings.mysadeptsecondmagattribute",
            "RES" => "character.resenabled",
            _ => "attribute.catalog"
        };

    private static CharacterCareerAttributeAdvanceBlocker ExpectedBlocker(
        CharacterCareerAttributeAdvanceInput input,
        int karmaCost)
    {
        if (!input.Created)
        {
            return CharacterCareerAttributeAdvanceBlocker.NotCareerCharacter;
        }

        if (!string.Equals(input.RulesetId, RulesetId, StringComparison.Ordinal))
        {
            return CharacterCareerAttributeAdvanceBlocker.UnsupportedRuleset;
        }

        if (!IsValidIdentity(input.Identity))
        {
            return CharacterCareerAttributeAdvanceBlocker.ForeignTarget;
        }

        if (!IsTargetEnabled(input))
        {
            return CharacterCareerAttributeAdvanceBlocker.SpecialAttributeDisabled;
        }

        if (karmaCost < 0 || input.EffectiveValue >= input.NaturalMaximum)
        {
            return CharacterCareerAttributeAdvanceBlocker.AtNaturalMaximum;
        }

        return input.AvailableKarma < karmaCost
            ? CharacterCareerAttributeAdvanceBlocker.InsufficientKarma
            : CharacterCareerAttributeAdvanceBlocker.None;
    }

    private static int CalculateKarmaCost(CharacterCareerAttributeAdvanceInput input)
    {
        int value = input.EffectiveValue;
        if (value >= input.NaturalMaximum)
        {
            return -1;
        }

        int optionsCost = input.Settings.KarmaAttribute;
        int upgrade = value == 0 ? optionsCost : checked((value + 1) * optionsCost);
        if (input.Settings.AlternateMetatypeAttributeKarma
            && !AlternateMetatypeExceptions.Contains(input.Identity.Abbreviation))
        {
            upgrade = checked(upgrade - checked((input.MetatypeMinimum - 1) * optionsCost));
        }

        int targetRating = checked(value + 1);
        decimal extra = 0m;
        decimal multiplier = 1m;
        foreach (CharacterCareerAttributeKarmaModifier modifier in input.Modifiers)
        {
            if (modifier.Minimum > targetRating
                || modifier.Maximum != 0 && targetRating > modifier.Maximum)
            {
                continue;
            }

            if (modifier.Kind == CharacterCareerAttributeKarmaModifierKind.AttributeKarmaCost)
            {
                extra = checked(extra + modifier.Value);
            }
            else
            {
                multiplier = checked(multiplier * (modifier.Value / 100m));
            }
        }

        upgrade = multiplier != 1m
            ? StandardRound(checked(upgrade * multiplier + extra))
            : checked(upgrade + StandardRound(extra));
        return Math.Max(upgrade, Math.Min(1, optionsCost));
    }

    private static int StandardRound(decimal value)
        => decimal.ToInt32(value >= 0m ? decimal.Ceiling(value) : decimal.Floor(value));

    private static bool IsCoherentPrerequisites(
        IReadOnlyList<CharacterCareerAttributePrerequisiteResult>? prerequisites)
    {
        if (prerequisites is null || prerequisites.Count != Enum.GetValues<CharacterCareerAttributePrerequisite>().Length)
        {
            return false;
        }

        CharacterCareerAttributePrerequisiteResult?[] values = prerequisites.ToArray();
        CharacterCareerAttributePrerequisite[] expected =
            Enum.GetValues<CharacterCareerAttributePrerequisite>();
        return !values.Any(static value => value is null)
            && values.Select(static value => value!.Prerequisite).SequenceEqual(expected)
            && values.All(static value => value is not null
                && Enum.IsDefined(value.Prerequisite)
                && !string.IsNullOrWhiteSpace(value.Authority)
                && value.Authority.Length <= MaximumNameLength);
    }

    private static bool PrerequisitesMatchQuote(CharacterCareerAttributeAdvanceQuote quote)
    {
        CharacterCareerAttributePrerequisiteResult[] values = quote.Prerequisites.ToArray();
        return values[0].Authority == "character.created"
            && values[1].Authority == "ruleset.sr5"
            && values[2].Authority == $"attribute.catalog:{quote.Identity.Abbreviation}"
            && values[2].Satisfied
            && values[3].Authority == (quote.Identity.Abbreviation switch
                {
                    "MAG" => "character.magenabled",
                    "MAGAdept" => "character.magenabled+character.isadept+settings.mysadeptsecondmagattribute",
                    "RES" => "character.resenabled",
                    _ => "attribute.catalog"
                })
            && values[4].Authority == "attribute.total-maximum"
            && values[4].Satisfied == (quote.EffectiveValue < quote.NaturalMaximum)
            && values[5].Authority == "character.karma"
            && values[5].Satisfied == (quote.KarmaCost >= 0
                && quote.AvailableKarma >= quote.KarmaCost);
    }

    private static CharacterCareerAttributeAdvanceBlocker ExpectedBlocker(
        CharacterCareerAttributeAdvanceQuote quote)
    {
        Dictionary<CharacterCareerAttributePrerequisite, bool> prerequisites =
            quote.Prerequisites.ToDictionary(
                static value => value.Prerequisite,
                static value => value.Satisfied);
        if (!prerequisites[CharacterCareerAttributePrerequisite.CareerCharacter])
        {
            return CharacterCareerAttributeAdvanceBlocker.NotCareerCharacter;
        }

        if (!prerequisites[CharacterCareerAttributePrerequisite.Sr5Ruleset])
        {
            return CharacterCareerAttributeAdvanceBlocker.UnsupportedRuleset;
        }

        if (!prerequisites[CharacterCareerAttributePrerequisite.ExactTarget])
        {
            return CharacterCareerAttributeAdvanceBlocker.ForeignTarget;
        }

        if (!prerequisites[CharacterCareerAttributePrerequisite.SpecialAttributeEnabled])
        {
            return CharacterCareerAttributeAdvanceBlocker.SpecialAttributeDisabled;
        }

        if (prerequisites[CharacterCareerAttributePrerequisite.BelowNaturalMaximum]
            != (quote.EffectiveValue < quote.NaturalMaximum))
        {
            return CharacterCareerAttributeAdvanceBlocker.ForeignTarget;
        }

        if (!prerequisites[CharacterCareerAttributePrerequisite.BelowNaturalMaximum]
            || quote.KarmaCost < 0)
        {
            return CharacterCareerAttributeAdvanceBlocker.AtNaturalMaximum;
        }

        bool sufficientKarma = quote.AvailableKarma >= quote.KarmaCost;
        if (prerequisites[CharacterCareerAttributePrerequisite.SufficientKarma] != sufficientKarma)
        {
            return CharacterCareerAttributeAdvanceBlocker.ForeignTarget;
        }

        return sufficientKarma
            ? CharacterCareerAttributeAdvanceBlocker.None
            : CharacterCareerAttributeAdvanceBlocker.InsufficientKarma;
    }

    private static bool PlanMatchesQuote(
        CharacterCareerAttributeAdvanceQuote quote,
        CharacterCareerAttributeAdvancePlan plan)
    {
        try
        {
            int expectedAttributeKarma = quote.RepairsBurnedEdge
                ? quote.KarmaPoints
                : checked(quote.KarmaPoints + 1);
            int expectedBurnedEdge = quote.RepairsBurnedEdge
                ? checked(quote.BurnedEdgePoints - 1)
                : quote.BurnedEdgePoints;
            return plan.SavedAttributeKarmaPoints == expectedAttributeKarma
                && plan.SavedCharacterKarma == checked(quote.AvailableKarma - quote.KarmaCost)
                && plan.BurnedEdgePointsBefore == quote.BurnedEdgePoints
                && plan.SavedBurnedEdgePoints == expectedBurnedEdge
                && plan.ExpenseAmount == checked(-quote.KarmaCost)
                && string.Equals(
                    plan.ExpenseReason,
                    $"Attribute {quote.Identity.Abbreviation} {quote.EffectiveValue.ToString(CultureInfo.InvariantCulture)} -> {quote.TargetValue.ToString(CultureInfo.InvariantCulture)}",
                    StringComparison.Ordinal)
                && plan.KarmaUndoType == "ImproveAttribute"
                && plan.NuyenUndoType == "AddCyberware"
                && plan.UndoObjectId == quote.Identity.Abbreviation
                && plan.UndoQuantity == 0m
                && plan.UndoExtra == string.Empty;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static string CalculateRuleDigest(CharacterCareerAttributeAdvanceInput input)
    {
        IEnumerable<string> modifiers = input.Modifiers
            .OrderBy(static modifier => modifier.ModifierIdentity, StringComparer.Ordinal)
            .Select(modifier => string.Join(":",
                modifier.ModifierIdentity,
                modifier.Kind.ToString(),
                modifier.TargetAbbreviation,
                modifier.Minimum.ToString(CultureInfo.InvariantCulture),
                modifier.Maximum.ToString(CultureInfo.InvariantCulture),
                modifier.Value.ToString(CultureInfo.InvariantCulture)));
        return Sha256(string.Join('\0',
            "chummer5a.attribute-upgrade/v1",
            input.Identity.Abbreviation,
            input.Identity.Kind.ToString(),
            input.EffectiveValue.ToString(CultureInfo.InvariantCulture),
            input.NaturalMaximum.ToString(CultureInfo.InvariantCulture),
            input.MetatypeMinimum.ToString(CultureInfo.InvariantCulture),
            input.AvailableKarma.ToString(CultureInfo.InvariantCulture),
            input.MagicEnabled.ToString(CultureInfo.InvariantCulture),
            input.MysticAdept.ToString(CultureInfo.InvariantCulture),
            input.MysticAdeptSecondMagicAttributeEnabled.ToString(CultureInfo.InvariantCulture),
            input.ResonanceEnabled.ToString(CultureInfo.InvariantCulture),
            input.BurnedEdgePoints.ToString(CultureInfo.InvariantCulture),
            input.Settings.KarmaAttribute.ToString(CultureInfo.InvariantCulture),
            input.Settings.AlternateMetatypeAttributeKarma.ToString(CultureInfo.InvariantCulture),
            string.Join("|", modifiers),
            input.RawRuleState));
    }

    private static string CalculateLogicalRevision(
        CharacterCareerAttributeIdentity identity,
        string displayName,
        int basePoints,
        int karmaPoints,
        int effectiveValue,
        int targetValue,
        int naturalMaximum,
        int metatypeMinimum,
        int availableKarma,
        int karmaCost,
        bool repairsBurnedEdge,
        int burnedEdgePoints,
        IReadOnlyList<CharacterCareerAttributePrerequisiteResult> prerequisites,
        bool canAdvance,
        CharacterCareerAttributeAdvanceBlocker blocker,
        string sourceRevision,
        string ruleDigest)
        => Sha256(string.Join('\0',
            identity.Abbreviation,
            identity.Kind.ToString(),
            displayName,
            basePoints.ToString(CultureInfo.InvariantCulture),
            karmaPoints.ToString(CultureInfo.InvariantCulture),
            effectiveValue.ToString(CultureInfo.InvariantCulture),
            targetValue.ToString(CultureInfo.InvariantCulture),
            naturalMaximum.ToString(CultureInfo.InvariantCulture),
            metatypeMinimum.ToString(CultureInfo.InvariantCulture),
            availableKarma.ToString(CultureInfo.InvariantCulture),
            karmaCost.ToString(CultureInfo.InvariantCulture),
            repairsBurnedEdge.ToString(CultureInfo.InvariantCulture),
            burnedEdgePoints.ToString(CultureInfo.InvariantCulture),
            string.Join("|", prerequisites.Select(prerequisite => string.Join(":",
                prerequisite.Prerequisite.ToString(),
                prerequisite.Satisfied.ToString(CultureInfo.InvariantCulture),
                prerequisite.Authority))),
            canAdvance.ToString(CultureInfo.InvariantCulture),
            blocker.ToString(),
            sourceRevision,
            ruleDigest));

    private static string CalculateReceiptDigest(
        Guid transactionId,
        CharacterCareerAttributeIdentity identity,
        bool repairsBurnedEdge,
        int attributeKarmaBefore,
        int attributeKarmaAfter,
        int characterKarmaBefore,
        int characterKarmaAfter,
        int burnedEdgeBefore,
        int burnedEdgeAfter,
        Guid expenseId,
        int expenseAmount,
        string logicalRevision,
        string sourceRevision,
        string ruleDigest)
        => Sha256(string.Join('\0',
            transactionId.ToString("D"), identity.Abbreviation, identity.Kind.ToString(),
            repairsBurnedEdge.ToString(CultureInfo.InvariantCulture),
            attributeKarmaBefore.ToString(CultureInfo.InvariantCulture),
            attributeKarmaAfter.ToString(CultureInfo.InvariantCulture),
            characterKarmaBefore.ToString(CultureInfo.InvariantCulture),
            characterKarmaAfter.ToString(CultureInfo.InvariantCulture),
            burnedEdgeBefore.ToString(CultureInfo.InvariantCulture),
            burnedEdgeAfter.ToString(CultureInfo.InvariantCulture), expenseId.ToString("D"),
            expenseAmount.ToString(CultureInfo.InvariantCulture), logicalRevision,
            sourceRevision, ruleDigest));

    private static string CalculateCorrectionDigest(
        Guid correctionId,
        Guid originalTransactionId,
        Guid expenseId,
        CharacterCareerAttributeIdentity identity,
        int attributeKarma,
        int characterKarma,
        int burnedEdgePoints,
        string reason,
        string originalReceiptDigest)
        => Sha256(string.Join('\0',
            correctionId.ToString("D"), originalTransactionId.ToString("D"),
            expenseId.ToString("D"), identity.Abbreviation, identity.Kind.ToString(),
            attributeKarma.ToString(CultureInfo.InvariantCulture),
            characterKarma.ToString(CultureInfo.InvariantCulture),
            burnedEdgePoints.ToString(CultureInfo.InvariantCulture), reason,
            originalReceiptDigest));

    private static bool RevisionMatches(string actual, string? expected)
        => IsLowerHexRevision(actual) && string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool IsLowerHexRevision(string? value)
        => value is { Length: RevisionHexLength }
            && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static CharacterCareerAttributeAdvanceQuote UnavailableQuote()
        => new(
            new CharacterCareerAttributeIdentity(string.Empty, CharacterCareerAttributeKind.Normal),
            string.Empty, 0, 0, 0, 0, 0, 0, 0, -1, false, 0, TimeSpan.Zero,
            CharacterCareerAttributeTimeAuthority.ImmediateChummerPersistence, [], false,
            CharacterCareerAttributeAdvanceBlocker.ForeignTarget, string.Empty, string.Empty, string.Empty);

    private static CharacterCareerAttributeAdvancePlan UnavailablePlan()
        => new(
            new CharacterCareerAttributeIdentity(string.Empty, CharacterCareerAttributeKind.Normal),
            0, 0, 0, 0, 0, string.Empty, DateTime.MinValue, Guid.Empty, string.Empty,
            string.Empty, string.Empty, 0m, string.Empty, string.Empty, string.Empty, string.Empty);

    private static CharacterCareerAttributeAdvanceReceipt UnavailableReceipt()
        => new(
            Guid.Empty, new CharacterCareerAttributeIdentity(string.Empty, CharacterCareerAttributeKind.Normal),
            false, 0, 0, 0, 0, 0, 0, Guid.Empty, 0, string.Empty, string.Empty, string.Empty, string.Empty);

    private static CharacterCareerAttributeCorrectionPlan UnavailableCorrection()
        => new(
            Guid.Empty, Guid.Empty, Guid.Empty,
            new CharacterCareerAttributeIdentity(string.Empty, CharacterCareerAttributeKind.Normal),
            0, 0, 0, string.Empty, string.Empty, string.Empty);
}
