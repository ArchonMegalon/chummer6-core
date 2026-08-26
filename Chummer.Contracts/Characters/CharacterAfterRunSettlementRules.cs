using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public sealed record CharacterAfterRunSettlementIdentity(
    Guid ProposalId,
    Guid RunId,
    Guid CharacterId);

public enum CharacterAfterRunReviewRole
{
    GameMaster,
    CharacterOwner
}

public enum CharacterAfterRunReviewDecision
{
    Approved,
    Rejected
}

public sealed record CharacterAfterRunReview(
    Guid ReviewId,
    CharacterAfterRunReviewRole Role,
    string ActorId,
    CharacterAfterRunReviewDecision Decision,
    string Reason);

public enum CharacterAfterRunContactProposalKind
{
    RunReward,
    KarmaPurchase
}

public sealed record CharacterAfterRunContactProposal(
    Guid ContactId,
    string Name,
    string Role,
    string Location,
    int Connection,
    int Loyalty,
    CharacterAfterRunContactProposalKind Kind);

public sealed record CharacterAfterRunContactSettlement(
    Guid ContactId,
    string Name,
    string Role,
    string Location,
    int Connection,
    int Loyalty,
    CharacterAfterRunContactProposalKind Kind,
    int KarmaCost);

public sealed record CharacterAfterRunSettlementSettings(
    int MaximumHeat,
    int MaximumReputation,
    int MaximumConnection,
    int MaximumLoyalty,
    int KarmaPerContactPoint,
    bool AllowRunRewardContacts,
    bool AllowKarmaPurchasedContacts,
    bool UseCalculatedPublicAwareness);

public sealed record CharacterAfterRunSettlementInput(
    CharacterAfterRunSettlementIdentity Identity,
    bool Created,
    string RulesetId,
    bool TargetOwnedByCharacter,
    bool ProjectionIsExact,
    bool RunCompleted,
    bool ProposalAlreadySettled,
    string ExpectedGmActorId,
    string ExpectedOwnerActorId,
    int CurrentHeat,
    int CurrentStreetCred,
    int CurrentNotoriety,
    int CurrentPublicAwareness,
    int CurrentKarma,
    int HeatDelta,
    int StreetCredDelta,
    int NotorietyDelta,
    int PublicAwarenessDelta,
    CharacterAfterRunSettlementSettings Settings,
    IReadOnlyList<CharacterAfterRunContactProposal> ContactProposals,
    CharacterAfterRunReview? GmReview,
    CharacterAfterRunReview? OwnerReview,
    string RawSourceState,
    string RawCustomDataState,
    string RawGmPolicyState,
    string RawRuntimeState);

public enum CharacterAfterRunSettlementPrerequisite
{
    CareerCharacter,
    Sr5Ruleset,
    ExactTarget,
    ExactProjection,
    RunCompleted,
    ProposalUnsettled,
    GmApproved,
    OwnerApproved,
    HeatWithinPolicy,
    ReputationWithinPolicy,
    ContactsWithinPolicy,
    SufficientKarma
}

public sealed record CharacterAfterRunSettlementPrerequisiteResult(
    CharacterAfterRunSettlementPrerequisite Prerequisite,
    bool Satisfied,
    string Authority);

public enum CharacterAfterRunSettlementBlocker
{
    None,
    NotCareerCharacter,
    UnsupportedRuleset,
    ForeignTarget,
    InexactProjection,
    RunNotCompleted,
    AlreadySettled,
    GmReviewPending,
    GmRejected,
    OwnerReviewPending,
    OwnerRejected,
    HeatOutsidePolicy,
    ReputationOutsidePolicy,
    ContactOutsidePolicy,
    InsufficientKarma
}

public sealed record CharacterAfterRunSettlementQuote(
    CharacterAfterRunSettlementIdentity Identity,
    int HeatBefore,
    int HeatDelta,
    int HeatAfter,
    int StreetCredBefore,
    int StreetCredDelta,
    int StreetCredAfter,
    int NotorietyBefore,
    int NotorietyDelta,
    int NotorietyAfter,
    int PublicAwarenessBefore,
    int RequestedPublicAwarenessDelta,
    int PublicAwarenessAfter,
    int KarmaBefore,
    int ContactKarmaCost,
    int KarmaAfter,
    IReadOnlyList<CharacterAfterRunContactSettlement> Contacts,
    string GmReviewDigest,
    string OwnerReviewDigest,
    IReadOnlyList<CharacterAfterRunSettlementPrerequisiteResult> Prerequisites,
    bool CanSettle,
    CharacterAfterRunSettlementBlocker Blocker,
    string SourceDigest,
    string CustomDataDigest,
    string GmPolicyDigest,
    string RuntimeDigest,
    string LogicalDigest);

public sealed record CharacterAfterRunSettlementPlan(
    CharacterAfterRunSettlementIdentity Identity,
    Guid TransactionId,
    int TargetHeat,
    int TargetStreetCred,
    int TargetNotoriety,
    int TargetPublicAwareness,
    int TargetKarma,
    int ContactKarmaCost,
    IReadOnlyList<CharacterAfterRunContactSettlement> ContactsToAdd,
    Guid ExpenseId,
    int ExpenseAmount,
    string ExpenseReason,
    string GmReviewDigest,
    string OwnerReviewDigest,
    string ExpectedSourceDigest,
    string ExpectedCustomDataDigest,
    string ExpectedGmPolicyDigest,
    string ExpectedRuntimeDigest,
    string ExpectedLogicalDigest,
    string PlanDigest);

public sealed record CharacterAfterRunExpenseObservation(
    int MatchingEntryCount,
    Guid ExpenseId,
    int Amount,
    string Reason,
    string ExpenseType,
    bool Refund);

public sealed record CharacterAfterRunSettlementObservation(
    int MatchingTransactionCount,
    int Heat,
    int StreetCred,
    int Notoriety,
    int PublicAwareness,
    int Karma,
    IReadOnlyList<CharacterAfterRunContactSettlement> AddedContacts,
    CharacterAfterRunExpenseObservation Expense,
    string SourceDigest,
    string CustomDataDigest,
    string GmPolicyDigest,
    string RuntimeDigest);

public sealed record CharacterAfterRunSettlementReceipt(
    Guid TransactionId,
    CharacterAfterRunSettlementIdentity Identity,
    int HeatBefore,
    int HeatAfter,
    int StreetCredBefore,
    int StreetCredAfter,
    int NotorietyBefore,
    int NotorietyAfter,
    int PublicAwarenessBefore,
    int PublicAwarenessAfter,
    int KarmaBefore,
    int KarmaAfter,
    int ContactKarmaCost,
    IReadOnlyList<CharacterAfterRunContactSettlement> AddedContacts,
    Guid ExpenseId,
    int ExpenseAmount,
    string ExpenseReason,
    string GmReviewDigest,
    string OwnerReviewDigest,
    string SourceDigest,
    string CustomDataDigest,
    string GmPolicyDigest,
    string RuntimeDigest,
    string LogicalDigestBefore,
    string LogicalDigestAfter,
    string ReceiptDigest);

public sealed record CharacterAfterRunSettlementCorrectionPlan(
    Guid CorrectionId,
    Guid OriginalTransactionId,
    CharacterAfterRunSettlementIdentity Identity,
    int RestoredHeat,
    int RestoredStreetCred,
    int RestoredNotoriety,
    int RestoredPublicAwareness,
    int RestoredKarma,
    IReadOnlyList<Guid> ContactIdsToRemove,
    Guid ExpenseIdToRemove,
    string Reason,
    string GmReviewDigest,
    string OwnerReviewDigest,
    string ExpectedPostLogicalDigest,
    string OriginalReceiptDigest,
    string CorrectionDigest);

/// <summary>
/// Deterministic, bounded SR5 authority for settling character-local After Run
/// heat, reputation and new-contact proposals. The host owns proposal/review
/// persistence and must apply plans atomically; Core never infers a missing GM or
/// owner decision and never writes a character directly.
/// </summary>
public static class CharacterAfterRunSettlementRules
{
    public const string ContractName = "chummer.core.sr5-after-run-settlement/v1";
    public const string RulesetId = "sr5";
    public const int DigestLength = 64;
    public const int MaximumTextLength = 512;
    public const int MaximumRawStateLength = 1_048_576;
    public const int MaximumValue = 9_999_999;
    public const int MaximumContactCount = 1_000;

    public static bool TryCreateQuote(
        CharacterAfterRunSettlementInput? input,
        out CharacterAfterRunSettlementQuote quote)
    {
        quote = UnavailableQuote();
        if (!IsValidInput(input))
        {
            return false;
        }

        CharacterAfterRunSettlementInput valid = input!;
        int heatAfter;
        int streetCredAfter;
        int notorietyAfter;
        int publicAwarenessAfter;
        int contactKarmaCost;
        int karmaAfter;
        CharacterAfterRunContactSettlement[] contacts;
        try
        {
            heatAfter = checked(valid.CurrentHeat + valid.HeatDelta);
            streetCredAfter = checked(valid.CurrentStreetCred + valid.StreetCredDelta);
            notorietyAfter = checked(valid.CurrentNotoriety + valid.NotorietyDelta);
            publicAwarenessAfter = valid.Settings.UseCalculatedPublicAwareness
                ? checked(streetCredAfter + notorietyAfter / 3)
                : checked(valid.CurrentPublicAwareness
                    + valid.PublicAwarenessDelta);
            contacts = valid.ContactProposals
                .OrderBy(static contact => contact.ContactId)
                .Select(contact => new CharacterAfterRunContactSettlement(
                    contact.ContactId,
                    contact.Name,
                    contact.Role,
                    contact.Location,
                    contact.Connection,
                    contact.Loyalty,
                    contact.Kind,
                    contact.Kind == CharacterAfterRunContactProposalKind.RunReward
                        ? 0
                        : checked(valid.Settings.KarmaPerContactPoint
                            * checked(contact.Connection + contact.Loyalty))))
                .ToArray();
            contactKarmaCost = contacts.Sum(static contact => contact.KarmaCost);
            karmaAfter = checked(valid.CurrentKarma - contactKarmaCost);
        }
        catch (OverflowException)
        {
            return false;
        }

        string sourceDigest = Sha256(valid.RawSourceState);
        string customDataDigest = Sha256(valid.RawCustomDataState);
        string gmPolicyDigest = CalculateGmPolicyDigest(valid);
        string runtimeDigest = Sha256(valid.RawRuntimeState);
        string gmReviewDigest = CalculateReviewDigest(
            CharacterAfterRunReviewRole.GameMaster,
            valid.ExpectedGmActorId,
            valid.GmReview);
        string ownerReviewDigest = CalculateReviewDigest(
            CharacterAfterRunReviewRole.CharacterOwner,
            valid.ExpectedOwnerActorId,
            valid.OwnerReview);

        CharacterAfterRunSettlementBlocker blocker = ExpectedBlocker(
            valid,
            heatAfter,
            streetCredAfter,
            notorietyAfter,
            publicAwarenessAfter,
            contactKarmaCost,
            karmaAfter);
        bool canSettle = blocker == CharacterAfterRunSettlementBlocker.None;
        CharacterAfterRunSettlementPrerequisiteResult[] prerequisites =
            CreatePrerequisites(
                valid,
                heatAfter,
                streetCredAfter,
                notorietyAfter,
                publicAwarenessAfter,
                karmaAfter);
        string logicalDigest = CalculateLogicalDigest(
            valid.Identity,
            valid.CurrentHeat,
            valid.HeatDelta,
            heatAfter,
            valid.CurrentStreetCred,
            valid.StreetCredDelta,
            streetCredAfter,
            valid.CurrentNotoriety,
            valid.NotorietyDelta,
            notorietyAfter,
            valid.CurrentPublicAwareness,
            valid.PublicAwarenessDelta,
            publicAwarenessAfter,
            valid.CurrentKarma,
            contactKarmaCost,
            karmaAfter,
            contacts,
            gmReviewDigest,
            ownerReviewDigest,
            prerequisites,
            canSettle,
            blocker,
            sourceDigest,
            customDataDigest,
            gmPolicyDigest,
            runtimeDigest);

        quote = new CharacterAfterRunSettlementQuote(
            valid.Identity,
            valid.CurrentHeat,
            valid.HeatDelta,
            heatAfter,
            valid.CurrentStreetCred,
            valid.StreetCredDelta,
            streetCredAfter,
            valid.CurrentNotoriety,
            valid.NotorietyDelta,
            notorietyAfter,
            valid.CurrentPublicAwareness,
            valid.PublicAwarenessDelta,
            publicAwarenessAfter,
            valid.CurrentKarma,
            contactKarmaCost,
            karmaAfter,
            contacts,
            gmReviewDigest,
            ownerReviewDigest,
            prerequisites,
            canSettle,
            blocker,
            sourceDigest,
            customDataDigest,
            gmPolicyDigest,
            runtimeDigest,
            logicalDigest);
        return IsCoherent(quote);
    }

    public static bool TryCreatePlan(
        CharacterAfterRunSettlementQuote? quote,
        string? expectedSourceDigest,
        string? expectedCustomDataDigest,
        string? expectedGmPolicyDigest,
        string? expectedRuntimeDigest,
        string? expectedLogicalDigest,
        bool explicitlyConfirmed,
        bool transactionIdAlreadyExists,
        Guid transactionId,
        out CharacterAfterRunSettlementPlan plan)
    {
        plan = UnavailablePlan();
        if (!IsCoherent(quote)
            || !quote!.CanSettle
            || !FixedEquals(quote.SourceDigest, expectedSourceDigest)
            || !FixedEquals(quote.CustomDataDigest, expectedCustomDataDigest)
            || !FixedEquals(quote.GmPolicyDigest, expectedGmPolicyDigest)
            || !FixedEquals(quote.RuntimeDigest, expectedRuntimeDigest)
            || !FixedEquals(quote.LogicalDigest, expectedLogicalDigest)
            || !explicitlyConfirmed
            || transactionIdAlreadyExists
            || transactionId == Guid.Empty)
        {
            return false;
        }

        Guid expenseId = quote.ContactKarmaCost == 0
            ? Guid.Empty
            : transactionId;
        int expenseAmount = -quote.ContactKarmaCost;
        string expenseReason = quote.ContactKarmaCost == 0
            ? string.Empty
            : $"After Run contacts ({quote.Contacts.Count.ToString(CultureInfo.InvariantCulture)})";
        string planDigest = CalculatePlanDigest(
            quote,
            transactionId,
            expenseId,
            expenseAmount,
            expenseReason);
        plan = new CharacterAfterRunSettlementPlan(
            quote.Identity,
            transactionId,
            quote.HeatAfter,
            quote.StreetCredAfter,
            quote.NotorietyAfter,
            quote.PublicAwarenessAfter,
            quote.KarmaAfter,
            quote.ContactKarmaCost,
            quote.Contacts,
            expenseId,
            expenseAmount,
            expenseReason,
            quote.GmReviewDigest,
            quote.OwnerReviewDigest,
            quote.SourceDigest,
            quote.CustomDataDigest,
            quote.GmPolicyDigest,
            quote.RuntimeDigest,
            quote.LogicalDigest,
            planDigest);
        return IsCoherent(plan);
    }

    public static bool TryCreateReceipt(
        Guid transactionId,
        CharacterAfterRunSettlementQuote? reviewed,
        CharacterAfterRunSettlementPlan? plan,
        CharacterAfterRunSettlementObservation? observed,
        out CharacterAfterRunSettlementReceipt receipt)
    {
        receipt = UnavailableReceipt();
        if (transactionId == Guid.Empty
            || !IsCoherent(reviewed)
            || !IsCoherent(plan)
            || !PlanMatchesQuote(reviewed!, plan!)
            || transactionId != plan!.TransactionId
            || !ObservationMatches(plan, observed))
        {
            return false;
        }

        string afterLogicalDigest = CalculatePostLogicalDigest(
            plan.Identity,
            plan.TransactionId,
            plan.TargetHeat,
            plan.TargetStreetCred,
            plan.TargetNotoriety,
            plan.TargetPublicAwareness,
            plan.TargetKarma,
            plan.ContactsToAdd,
            plan.ExpectedSourceDigest,
            plan.ExpectedCustomDataDigest,
            plan.ExpectedGmPolicyDigest,
            plan.ExpectedRuntimeDigest);
        var unsigned = new CharacterAfterRunSettlementReceipt(
            transactionId,
            reviewed!.Identity,
            reviewed.HeatBefore,
            plan.TargetHeat,
            reviewed.StreetCredBefore,
            plan.TargetStreetCred,
            reviewed.NotorietyBefore,
            plan.TargetNotoriety,
            reviewed.PublicAwarenessBefore,
            plan.TargetPublicAwareness,
            reviewed.KarmaBefore,
            plan.TargetKarma,
            plan.ContactKarmaCost,
            plan.ContactsToAdd,
            plan.ExpenseId,
            plan.ExpenseAmount,
            plan.ExpenseReason,
            plan.GmReviewDigest,
            plan.OwnerReviewDigest,
            plan.ExpectedSourceDigest,
            plan.ExpectedCustomDataDigest,
            plan.ExpectedGmPolicyDigest,
            plan.ExpectedRuntimeDigest,
            plan.ExpectedLogicalDigest,
            afterLogicalDigest,
            string.Empty);
        receipt = unsigned with { ReceiptDigest = CalculateReceiptDigest(unsigned) };
        return IsCoherent(receipt);
    }

    public static bool TryRecoverReceipt(
        CharacterAfterRunSettlementReceipt? persistedReceipt,
        Guid expectedTransactionId,
        CharacterAfterRunSettlementObservation? observed,
        string? expectedReceiptDigest,
        out CharacterAfterRunSettlementReceipt receipt)
    {
        receipt = UnavailableReceipt();
        if (!IsCoherent(persistedReceipt)
            || persistedReceipt!.TransactionId != expectedTransactionId
            || !FixedEquals(persistedReceipt.ReceiptDigest, expectedReceiptDigest)
            || !ReceiptMatchesObservation(persistedReceipt, observed))
        {
            return false;
        }

        receipt = persistedReceipt;
        return true;
    }

    public static bool TryPlanCorrection(
        CharacterAfterRunSettlementReceipt? original,
        CharacterAfterRunSettlementObservation? observedPostState,
        Guid correctionId,
        string? reason,
        CharacterAfterRunReview? gmReview,
        string expectedGmActorId,
        CharacterAfterRunReview? ownerReview,
        string expectedOwnerActorId,
        bool correctionIdAlreadyExists,
        bool originalTransactionAlreadyCorrected,
        string? expectedReceiptDigest,
        out CharacterAfterRunSettlementCorrectionPlan correction)
    {
        correction = UnavailableCorrection();
        string normalizedReason = reason?.Trim() ?? string.Empty;
        if (!IsCoherent(original)
            || !ReceiptMatchesObservation(original!, observedPostState)
            || correctionId == Guid.Empty
            || correctionId == original!.TransactionId
            || correctionIdAlreadyExists
            || originalTransactionAlreadyCorrected
            || !FixedEquals(original.ReceiptDigest, expectedReceiptDigest)
            || normalizedReason.Length is 0 or > MaximumTextLength
            || !IsApprovedReview(
                gmReview,
                CharacterAfterRunReviewRole.GameMaster,
                expectedGmActorId)
            || !IsApprovedReview(
                ownerReview,
                CharacterAfterRunReviewRole.CharacterOwner,
                expectedOwnerActorId))
        {
            return false;
        }

        string gmReviewDigest = CalculateReviewDigest(
            CharacterAfterRunReviewRole.GameMaster,
            expectedGmActorId,
            gmReview);
        string ownerReviewDigest = CalculateReviewDigest(
            CharacterAfterRunReviewRole.CharacterOwner,
            expectedOwnerActorId,
            ownerReview);
        Guid[] contactIds = original.AddedContacts
            .Select(static contact => contact.ContactId)
            .OrderBy(static value => value)
            .ToArray();
        var unsigned = new CharacterAfterRunSettlementCorrectionPlan(
            correctionId,
            original.TransactionId,
            original.Identity,
            original.HeatBefore,
            original.StreetCredBefore,
            original.NotorietyBefore,
            original.PublicAwarenessBefore,
            original.KarmaBefore,
            contactIds,
            original.ExpenseId,
            normalizedReason,
            gmReviewDigest,
            ownerReviewDigest,
            original.LogicalDigestAfter,
            original.ReceiptDigest,
            string.Empty);
        correction = unsigned with
        {
            CorrectionDigest = CalculateCorrectionDigest(unsigned)
        };
        return IsCoherent(correction);
    }

    public static bool IsCoherent(CharacterAfterRunSettlementQuote? quote)
        => quote is not null
            && IsValidIdentity(quote.Identity)
            && quote.HeatBefore is >= 0 and <= MaximumValue
            && quote.HeatDelta is >= -MaximumValue and <= MaximumValue
            && IsExactDelta(quote.HeatBefore, quote.HeatDelta, quote.HeatAfter)
            && quote.StreetCredBefore is >= 0 and <= MaximumValue
            && quote.StreetCredDelta is >= -MaximumValue and <= MaximumValue
            && IsExactDelta(
                quote.StreetCredBefore,
                quote.StreetCredDelta,
                quote.StreetCredAfter)
            && quote.NotorietyBefore is >= 0 and <= MaximumValue
            && quote.NotorietyDelta is >= -MaximumValue and <= MaximumValue
            && IsExactDelta(
                quote.NotorietyBefore,
                quote.NotorietyDelta,
                quote.NotorietyAfter)
            && quote.PublicAwarenessBefore is >= 0 and <= MaximumValue
            && quote.RequestedPublicAwarenessDelta
                is >= -MaximumValue and <= MaximumValue
            && quote.PublicAwarenessAfter is >= 0 and <= MaximumValue
            && quote.KarmaBefore is >= 0 and <= MaximumValue
            && quote.ContactKarmaCost is >= 0 and <= MaximumValue
            && quote.KarmaAfter == quote.KarmaBefore - quote.ContactKarmaCost
            && IsCoherentContacts(quote.Contacts)
            && quote.Contacts.Sum(static contact => contact.KarmaCost)
                == quote.ContactKarmaCost
            && IsCanonicalDigest(quote.GmReviewDigest)
            && IsCanonicalDigest(quote.OwnerReviewDigest)
            && IsCoherentPrerequisites(quote.Prerequisites)
            && quote.CanSettle
                == (quote.Blocker == CharacterAfterRunSettlementBlocker.None)
            && Enum.IsDefined(quote.Blocker)
            && IsCanonicalDigest(quote.SourceDigest)
            && IsCanonicalDigest(quote.CustomDataDigest)
            && IsCanonicalDigest(quote.GmPolicyDigest)
            && IsCanonicalDigest(quote.RuntimeDigest)
            && IsCanonicalDigest(quote.LogicalDigest);

    public static bool IsCoherent(CharacterAfterRunSettlementPlan? plan)
        => plan is not null
            && IsValidIdentity(plan.Identity)
            && plan.TransactionId != Guid.Empty
            && plan.TargetHeat is >= 0 and <= MaximumValue
            && plan.TargetStreetCred is >= 0 and <= MaximumValue
            && plan.TargetNotoriety is >= 0 and <= MaximumValue
            && plan.TargetPublicAwareness is >= 0 and <= MaximumValue
            && plan.TargetKarma is >= 0 and <= MaximumValue
            && plan.ContactKarmaCost is >= 0 and <= MaximumValue
            && IsCoherentContacts(plan.ContactsToAdd)
            && plan.ContactsToAdd.Sum(static contact => contact.KarmaCost)
                == plan.ContactKarmaCost
            && plan.ExpenseAmount == -plan.ContactKarmaCost
            && (plan.ContactKarmaCost == 0
                ? plan.ExpenseId == Guid.Empty && plan.ExpenseReason.Length == 0
                : plan.ExpenseId == plan.TransactionId
                    && plan.ExpenseReason is { Length: > 0 and <= MaximumTextLength })
            && IsCanonicalDigest(plan.GmReviewDigest)
            && IsCanonicalDigest(plan.OwnerReviewDigest)
            && IsCanonicalDigest(plan.ExpectedSourceDigest)
            && IsCanonicalDigest(plan.ExpectedCustomDataDigest)
            && IsCanonicalDigest(plan.ExpectedGmPolicyDigest)
            && IsCanonicalDigest(plan.ExpectedRuntimeDigest)
            && IsCanonicalDigest(plan.ExpectedLogicalDigest)
            && IsCanonicalDigest(plan.PlanDigest)
            && FixedEquals(plan.PlanDigest, CalculatePlanDigest(plan));

    public static bool IsCoherent(CharacterAfterRunSettlementReceipt? receipt)
        => receipt is not null
            && receipt.TransactionId != Guid.Empty
            && IsValidIdentity(receipt.Identity)
            && receipt.HeatBefore is >= 0 and <= MaximumValue
            && receipt.HeatAfter is >= 0 and <= MaximumValue
            && receipt.StreetCredBefore is >= 0 and <= MaximumValue
            && receipt.StreetCredAfter is >= 0 and <= MaximumValue
            && receipt.NotorietyBefore is >= 0 and <= MaximumValue
            && receipt.NotorietyAfter is >= 0 and <= MaximumValue
            && receipt.PublicAwarenessBefore is >= 0 and <= MaximumValue
            && receipt.PublicAwarenessAfter is >= 0 and <= MaximumValue
            && receipt.KarmaBefore is >= 0 and <= MaximumValue
            && receipt.KarmaAfter is >= 0 and <= MaximumValue
            && receipt.ContactKarmaCost is >= 0 and <= MaximumValue
            && receipt.KarmaAfter == receipt.KarmaBefore - receipt.ContactKarmaCost
            && IsCoherentContacts(receipt.AddedContacts)
            && receipt.AddedContacts.Sum(static contact => contact.KarmaCost)
                == receipt.ContactKarmaCost
            && receipt.ExpenseAmount == -receipt.ContactKarmaCost
            && (receipt.ContactKarmaCost == 0
                ? receipt.ExpenseId == Guid.Empty && receipt.ExpenseReason.Length == 0
                : receipt.ExpenseId == receipt.TransactionId
                    && receipt.ExpenseReason
                        is { Length: > 0 and <= MaximumTextLength })
            && IsCanonicalDigest(receipt.GmReviewDigest)
            && IsCanonicalDigest(receipt.OwnerReviewDigest)
            && IsCanonicalDigest(receipt.SourceDigest)
            && IsCanonicalDigest(receipt.CustomDataDigest)
            && IsCanonicalDigest(receipt.GmPolicyDigest)
            && IsCanonicalDigest(receipt.RuntimeDigest)
            && IsCanonicalDigest(receipt.LogicalDigestBefore)
            && IsCanonicalDigest(receipt.LogicalDigestAfter)
            && IsCanonicalDigest(receipt.ReceiptDigest)
            && FixedEquals(
                receipt.LogicalDigestAfter,
                CalculatePostLogicalDigest(receipt))
            && FixedEquals(receipt.ReceiptDigest, CalculateReceiptDigest(receipt));

    public static bool IsCoherent(
        CharacterAfterRunSettlementCorrectionPlan? correction)
        => correction is not null
            && correction.CorrectionId != Guid.Empty
            && correction.OriginalTransactionId != Guid.Empty
            && correction.CorrectionId != correction.OriginalTransactionId
            && IsValidIdentity(correction.Identity)
            && correction.RestoredHeat is >= 0 and <= MaximumValue
            && correction.RestoredStreetCred is >= 0 and <= MaximumValue
            && correction.RestoredNotoriety is >= 0 and <= MaximumValue
            && correction.RestoredPublicAwareness is >= 0 and <= MaximumValue
            && correction.RestoredKarma is >= 0 and <= MaximumValue
            && correction.ContactIdsToRemove is not null
            && correction.ContactIdsToRemove.Count <= MaximumContactCount
            && correction.ContactIdsToRemove.All(static id => id != Guid.Empty)
            && correction.ContactIdsToRemove
                .SequenceEqual(correction.ContactIdsToRemove.OrderBy(static id => id))
            && correction.ContactIdsToRemove.Distinct().Count()
                == correction.ContactIdsToRemove.Count
            && correction.Reason is { Length: > 0 and <= MaximumTextLength }
            && IsCanonicalDigest(correction.GmReviewDigest)
            && IsCanonicalDigest(correction.OwnerReviewDigest)
            && IsCanonicalDigest(correction.ExpectedPostLogicalDigest)
            && IsCanonicalDigest(correction.OriginalReceiptDigest)
            && IsCanonicalDigest(correction.CorrectionDigest)
            && FixedEquals(
                correction.CorrectionDigest,
                CalculateCorrectionDigest(correction));

    public static bool IsCanonicalDigest(string? value)
        => value is { Length: DigestLength }
            && value.All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsValidInput(CharacterAfterRunSettlementInput? input)
    {
        if (input is null
            || !IsValidIdentity(input.Identity)
            || input.RulesetId is not { Length: > 0 and <= MaximumTextLength }
            || !IsValidActorId(input.ExpectedGmActorId)
            || !IsValidActorId(input.ExpectedOwnerActorId)
            || input.ExpectedGmActorId == input.ExpectedOwnerActorId
            || input.CurrentHeat is < 0 or > MaximumValue
            || input.CurrentStreetCred is < 0 or > MaximumValue
            || input.CurrentNotoriety is < 0 or > MaximumValue
            || input.CurrentPublicAwareness is < 0 or > MaximumValue
            || input.CurrentKarma is < 0 or > MaximumValue
            || input.HeatDelta is < -MaximumValue or > MaximumValue
            || input.StreetCredDelta is < -MaximumValue or > MaximumValue
            || input.NotorietyDelta is < -MaximumValue or > MaximumValue
            || input.PublicAwarenessDelta is < -MaximumValue or > MaximumValue
            || !IsValidSettings(input.Settings)
            || input.ContactProposals is null
            || input.ContactProposals.Count > MaximumContactCount
            || input.ContactProposals.Any(static contact => !IsValidContact(contact))
            || input.ContactProposals.Select(static contact => contact.ContactId)
                .Distinct().Count() != input.ContactProposals.Count
            || input.GmReview is not null
                && !IsValidReviewShape(input.GmReview)
            || input.OwnerReview is not null
                && !IsValidReviewShape(input.OwnerReview)
            || !IsValidRawState(input.RawSourceState)
            || !IsValidRawState(input.RawCustomDataState)
            || !IsValidRawState(input.RawGmPolicyState)
            || !IsValidRawState(input.RawRuntimeState))
        {
            return false;
        }

        return true;
    }

    private static CharacterAfterRunSettlementBlocker ExpectedBlocker(
        CharacterAfterRunSettlementInput input,
        int heatAfter,
        int streetCredAfter,
        int notorietyAfter,
        int publicAwarenessAfter,
        int contactKarmaCost,
        int karmaAfter)
    {
        if (!input.Created)
            return CharacterAfterRunSettlementBlocker.NotCareerCharacter;
        if (!string.Equals(input.RulesetId, RulesetId, StringComparison.Ordinal))
            return CharacterAfterRunSettlementBlocker.UnsupportedRuleset;
        if (!input.TargetOwnedByCharacter)
            return CharacterAfterRunSettlementBlocker.ForeignTarget;
        if (!input.ProjectionIsExact)
            return CharacterAfterRunSettlementBlocker.InexactProjection;
        if (!input.RunCompleted)
            return CharacterAfterRunSettlementBlocker.RunNotCompleted;
        if (input.ProposalAlreadySettled)
            return CharacterAfterRunSettlementBlocker.AlreadySettled;
        if (input.GmReview is null
            || !ReviewMatches(
                input.GmReview,
                CharacterAfterRunReviewRole.GameMaster,
                input.ExpectedGmActorId))
            return CharacterAfterRunSettlementBlocker.GmReviewPending;
        if (input.GmReview.Decision == CharacterAfterRunReviewDecision.Rejected)
            return CharacterAfterRunSettlementBlocker.GmRejected;
        if (input.OwnerReview is null
            || !ReviewMatches(
                input.OwnerReview,
                CharacterAfterRunReviewRole.CharacterOwner,
                input.ExpectedOwnerActorId))
            return CharacterAfterRunSettlementBlocker.OwnerReviewPending;
        if (input.OwnerReview.Decision == CharacterAfterRunReviewDecision.Rejected)
            return CharacterAfterRunSettlementBlocker.OwnerRejected;
        if (heatAfter < 0 || heatAfter > input.Settings.MaximumHeat)
            return CharacterAfterRunSettlementBlocker.HeatOutsidePolicy;
        if (streetCredAfter < 0
            || streetCredAfter > input.Settings.MaximumReputation
            || notorietyAfter < 0
            || notorietyAfter > input.Settings.MaximumReputation
            || publicAwarenessAfter < 0
            || publicAwarenessAfter > input.Settings.MaximumReputation)
            return CharacterAfterRunSettlementBlocker.ReputationOutsidePolicy;
        if (!ContactsWithinPolicy(input))
            return CharacterAfterRunSettlementBlocker.ContactOutsidePolicy;
        return contactKarmaCost > input.CurrentKarma || karmaAfter < 0
            ? CharacterAfterRunSettlementBlocker.InsufficientKarma
            : CharacterAfterRunSettlementBlocker.None;
    }

    private static CharacterAfterRunSettlementPrerequisiteResult[]
        CreatePrerequisites(
            CharacterAfterRunSettlementInput input,
            int heatAfter,
            int streetCredAfter,
            int notorietyAfter,
            int publicAwarenessAfter,
            int karmaAfter)
        =>
        [
            new(CharacterAfterRunSettlementPrerequisite.CareerCharacter,
                input.Created, "character.created"),
            new(CharacterAfterRunSettlementPrerequisite.Sr5Ruleset,
                string.Equals(input.RulesetId, RulesetId, StringComparison.Ordinal),
                "ruleset.sr5"),
            new(CharacterAfterRunSettlementPrerequisite.ExactTarget,
                input.TargetOwnedByCharacter,
                $"character.internal-id:{input.Identity.CharacterId:D}"),
            new(CharacterAfterRunSettlementPrerequisite.ExactProjection,
                input.ProjectionIsExact, "after-run.projection:exact"),
            new(CharacterAfterRunSettlementPrerequisite.RunCompleted,
                input.RunCompleted, $"run.internal-id:{input.Identity.RunId:D}"),
            new(CharacterAfterRunSettlementPrerequisite.ProposalUnsettled,
                !input.ProposalAlreadySettled,
                $"after-run.proposal-id:{input.Identity.ProposalId:D}"),
            new(CharacterAfterRunSettlementPrerequisite.GmApproved,
                IsApprovedReview(input.GmReview,
                    CharacterAfterRunReviewRole.GameMaster,
                    input.ExpectedGmActorId),
                "review.game-master"),
            new(CharacterAfterRunSettlementPrerequisite.OwnerApproved,
                IsApprovedReview(input.OwnerReview,
                    CharacterAfterRunReviewRole.CharacterOwner,
                    input.ExpectedOwnerActorId),
                "review.character-owner"),
            new(CharacterAfterRunSettlementPrerequisite.HeatWithinPolicy,
                heatAfter >= 0 && heatAfter <= input.Settings.MaximumHeat,
                "gm-policy.maximum-heat"),
            new(CharacterAfterRunSettlementPrerequisite.ReputationWithinPolicy,
                streetCredAfter >= 0
                    && streetCredAfter <= input.Settings.MaximumReputation
                    && notorietyAfter >= 0
                    && notorietyAfter <= input.Settings.MaximumReputation
                    && publicAwarenessAfter >= 0
                    && publicAwarenessAfter <= input.Settings.MaximumReputation,
                "gm-policy.maximum-reputation"),
            new(CharacterAfterRunSettlementPrerequisite.ContactsWithinPolicy,
                ContactsWithinPolicy(input), "gm-policy.contact-acquisition"),
            new(CharacterAfterRunSettlementPrerequisite.SufficientKarma,
                karmaAfter >= 0, "character.karma")
        ];

    private static bool ContactsWithinPolicy(CharacterAfterRunSettlementInput input)
        => input.ContactProposals.All(contact =>
            contact.Connection <= input.Settings.MaximumConnection
            && contact.Loyalty <= input.Settings.MaximumLoyalty
            && (contact.Kind == CharacterAfterRunContactProposalKind.RunReward
                ? input.Settings.AllowRunRewardContacts
                : input.Settings.AllowKarmaPurchasedContacts));

    private static bool ObservationMatches(
        CharacterAfterRunSettlementPlan plan,
        CharacterAfterRunSettlementObservation? observed)
        => observed is not null
            && observed.MatchingTransactionCount == 1
            && observed.Heat == plan.TargetHeat
            && observed.StreetCred == plan.TargetStreetCred
            && observed.Notoriety == plan.TargetNotoriety
            && observed.PublicAwareness == plan.TargetPublicAwareness
            && observed.Karma == plan.TargetKarma
            && ContactsEqual(observed.AddedContacts, plan.ContactsToAdd)
            && ExpenseMatches(
                observed.Expense,
                plan.ExpenseId,
                plan.ExpenseAmount,
                plan.ExpenseReason)
            && FixedEquals(observed.SourceDigest, plan.ExpectedSourceDigest)
            && FixedEquals(
                observed.CustomDataDigest,
                plan.ExpectedCustomDataDigest)
            && FixedEquals(observed.GmPolicyDigest, plan.ExpectedGmPolicyDigest)
            && FixedEquals(observed.RuntimeDigest, plan.ExpectedRuntimeDigest);

    private static bool ReceiptMatchesObservation(
        CharacterAfterRunSettlementReceipt receipt,
        CharacterAfterRunSettlementObservation? observed)
        => observed is not null
            && observed.MatchingTransactionCount == 1
            && observed.Heat == receipt.HeatAfter
            && observed.StreetCred == receipt.StreetCredAfter
            && observed.Notoriety == receipt.NotorietyAfter
            && observed.PublicAwareness == receipt.PublicAwarenessAfter
            && observed.Karma == receipt.KarmaAfter
            && ContactsEqual(observed.AddedContacts, receipt.AddedContacts)
            && ExpenseMatches(
                observed.Expense,
                receipt.ExpenseId,
                receipt.ExpenseAmount,
                receipt.ExpenseReason)
            && FixedEquals(observed.SourceDigest, receipt.SourceDigest)
            && FixedEquals(observed.CustomDataDigest, receipt.CustomDataDigest)
            && FixedEquals(observed.GmPolicyDigest, receipt.GmPolicyDigest)
            && FixedEquals(observed.RuntimeDigest, receipt.RuntimeDigest);

    private static bool ExpenseMatches(
        CharacterAfterRunExpenseObservation? observed,
        Guid expectedId,
        int expectedAmount,
        string expectedReason)
    {
        if (observed is null)
            return false;
        if (expectedAmount == 0)
        {
            return observed.MatchingEntryCount == 0
                && observed.ExpenseId == Guid.Empty
                && observed.Amount == 0
                && observed.Reason.Length == 0
                && observed.ExpenseType.Length == 0
                && !observed.Refund;
        }
        return observed.MatchingEntryCount == 1
            && observed.ExpenseId == expectedId
            && observed.Amount == expectedAmount
            && observed.Reason == expectedReason
            && observed.ExpenseType == "Karma"
            && !observed.Refund;
    }

    private static bool PlanMatchesQuote(
        CharacterAfterRunSettlementQuote quote,
        CharacterAfterRunSettlementPlan plan)
        => plan.Identity == quote.Identity
            && plan.TargetHeat == quote.HeatAfter
            && plan.TargetStreetCred == quote.StreetCredAfter
            && plan.TargetNotoriety == quote.NotorietyAfter
            && plan.TargetPublicAwareness == quote.PublicAwarenessAfter
            && plan.TargetKarma == quote.KarmaAfter
            && plan.ContactKarmaCost == quote.ContactKarmaCost
            && ContactsEqual(plan.ContactsToAdd, quote.Contacts)
            && FixedEquals(plan.GmReviewDigest, quote.GmReviewDigest)
            && FixedEquals(plan.OwnerReviewDigest, quote.OwnerReviewDigest)
            && FixedEquals(plan.ExpectedSourceDigest, quote.SourceDigest)
            && FixedEquals(plan.ExpectedCustomDataDigest, quote.CustomDataDigest)
            && FixedEquals(plan.ExpectedGmPolicyDigest, quote.GmPolicyDigest)
            && FixedEquals(plan.ExpectedRuntimeDigest, quote.RuntimeDigest)
            && FixedEquals(plan.ExpectedLogicalDigest, quote.LogicalDigest);

    private static bool IsValidIdentity(CharacterAfterRunSettlementIdentity? identity)
        => identity is not null
            && identity.ProposalId != Guid.Empty
            && identity.RunId != Guid.Empty
            && identity.CharacterId != Guid.Empty
            && identity.ProposalId != identity.RunId
            && identity.ProposalId != identity.CharacterId;

    private static bool IsValidSettings(CharacterAfterRunSettlementSettings? settings)
        => settings is not null
            && settings.MaximumHeat is >= 0 and <= MaximumValue
            && settings.MaximumReputation is >= 0 and <= MaximumValue
            && settings.MaximumConnection is >= 1 and <= MaximumValue
            && settings.MaximumLoyalty is >= 1 and <= MaximumValue
            && settings.KarmaPerContactPoint is >= 0 and <= MaximumValue;

    private static bool IsValidContact(CharacterAfterRunContactProposal? contact)
        => contact is not null
            && contact.ContactId != Guid.Empty
            && contact.Name is { Length: > 0 and <= MaximumTextLength }
            && contact.Role is { Length: <= MaximumTextLength }
            && contact.Location is { Length: <= MaximumTextLength }
            && contact.Connection is >= 1 and <= MaximumValue
            && contact.Loyalty is >= 1 and <= MaximumValue
            && Enum.IsDefined(contact.Kind);

    private static bool IsCoherentContacts(
        IReadOnlyList<CharacterAfterRunContactSettlement>? contacts)
        => contacts is not null
            && contacts.Count <= MaximumContactCount
            && contacts.All(contact => contact is not null
                && contact.ContactId != Guid.Empty
                && contact.Name is { Length: > 0 and <= MaximumTextLength }
                && contact.Role is { Length: <= MaximumTextLength }
                && contact.Location is { Length: <= MaximumTextLength }
                && contact.Connection is >= 1 and <= MaximumValue
                && contact.Loyalty is >= 1 and <= MaximumValue
                && Enum.IsDefined(contact.Kind)
                && contact.KarmaCost is >= 0 and <= MaximumValue
                && (contact.Kind == CharacterAfterRunContactProposalKind.RunReward
                    ? contact.KarmaCost == 0
                    : true))
            && contacts.Select(static contact => contact.ContactId)
                .SequenceEqual(contacts.Select(static contact => contact.ContactId)
                    .OrderBy(static id => id))
            && contacts.Select(static contact => contact.ContactId).Distinct().Count()
                == contacts.Count;

    private static bool IsCoherentPrerequisites(
        IReadOnlyList<CharacterAfterRunSettlementPrerequisiteResult>? values)
        => values is not null
            && values.Count
                == Enum.GetValues<CharacterAfterRunSettlementPrerequisite>().Length
            && values.Select(static value => value.Prerequisite)
                .SequenceEqual(Enum.GetValues<CharacterAfterRunSettlementPrerequisite>())
            && values.All(static value => value is not null
                && Enum.IsDefined(value.Prerequisite)
                && value.Authority is { Length: > 0 and <= MaximumTextLength });

    private static bool IsValidReviewShape(CharacterAfterRunReview review)
        => review.ReviewId != Guid.Empty
            && Enum.IsDefined(review.Role)
            && IsValidActorId(review.ActorId)
            && Enum.IsDefined(review.Decision)
            && review.Reason is { Length: <= MaximumTextLength };

    private static bool ReviewMatches(
        CharacterAfterRunReview review,
        CharacterAfterRunReviewRole role,
        string actorId)
        => IsValidReviewShape(review)
            && review.Role == role
            && string.Equals(review.ActorId, actorId, StringComparison.Ordinal);

    private static bool IsApprovedReview(
        CharacterAfterRunReview? review,
        CharacterAfterRunReviewRole role,
        string actorId)
        => review is not null
            && ReviewMatches(review, role, actorId)
            && review.Decision == CharacterAfterRunReviewDecision.Approved;

    private static bool IsValidActorId(string? actorId)
        => actorId is { Length: > 0 and <= MaximumTextLength }
            && actorId.All(static character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' or '.');

    private static bool IsValidRawState(string? state)
        => state is { Length: > 0 and <= MaximumRawStateLength };

    private static bool ContactsEqual(
        IReadOnlyList<CharacterAfterRunContactSettlement>? left,
        IReadOnlyList<CharacterAfterRunContactSettlement>? right)
        => left is not null
            && right is not null
            && left.SequenceEqual(right);

    private static bool IsExactDelta(int before, int delta, int after)
    {
        try
        {
            return checked(before + delta) == after
                && after is >= 0 and <= MaximumValue;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static string CalculateReviewDigest(
        CharacterAfterRunReviewRole expectedRole,
        string expectedActorId,
        CharacterAfterRunReview? review)
        => review is null
            ? Sha256(Canonical(
                ContractName,
                "review-missing",
                expectedRole.ToString(),
                expectedActorId))
            : Sha256(Canonical(
                ContractName,
                "review",
                expectedRole.ToString(),
                expectedActorId,
                review.ReviewId.ToString("D"),
                review.Role.ToString(),
                review.ActorId,
                review.Decision.ToString(),
                review.Reason));

    private static string CalculateGmPolicyDigest(
        CharacterAfterRunSettlementInput input)
        => Sha256(Canonical(
            ContractName,
            "gm-policy",
            input.Settings.MaximumHeat.ToString(CultureInfo.InvariantCulture),
            input.Settings.MaximumReputation.ToString(CultureInfo.InvariantCulture),
            input.Settings.MaximumConnection.ToString(CultureInfo.InvariantCulture),
            input.Settings.MaximumLoyalty.ToString(CultureInfo.InvariantCulture),
            input.Settings.KarmaPerContactPoint.ToString(CultureInfo.InvariantCulture),
            input.Settings.AllowRunRewardContacts.ToString(CultureInfo.InvariantCulture),
            input.Settings.AllowKarmaPurchasedContacts.ToString(CultureInfo.InvariantCulture),
            input.Settings.UseCalculatedPublicAwareness.ToString(CultureInfo.InvariantCulture),
            input.RawGmPolicyState));

    private static string CalculateLogicalDigest(
        CharacterAfterRunSettlementIdentity identity,
        int heatBefore,
        int heatDelta,
        int heatAfter,
        int streetCredBefore,
        int streetCredDelta,
        int streetCredAfter,
        int notorietyBefore,
        int notorietyDelta,
        int notorietyAfter,
        int publicAwarenessBefore,
        int publicAwarenessDelta,
        int publicAwarenessAfter,
        int karmaBefore,
        int contactKarmaCost,
        int karmaAfter,
        IReadOnlyList<CharacterAfterRunContactSettlement> contacts,
        string gmReviewDigest,
        string ownerReviewDigest,
        IReadOnlyList<CharacterAfterRunSettlementPrerequisiteResult> prerequisites,
        bool canSettle,
        CharacterAfterRunSettlementBlocker blocker,
        string sourceDigest,
        string customDataDigest,
        string gmPolicyDigest,
        string runtimeDigest)
        => Sha256(Canonical(
            ContractName,
            "quote",
            IdentityText(identity),
            heatBefore.ToString(CultureInfo.InvariantCulture),
            heatDelta.ToString(CultureInfo.InvariantCulture),
            heatAfter.ToString(CultureInfo.InvariantCulture),
            streetCredBefore.ToString(CultureInfo.InvariantCulture),
            streetCredDelta.ToString(CultureInfo.InvariantCulture),
            streetCredAfter.ToString(CultureInfo.InvariantCulture),
            notorietyBefore.ToString(CultureInfo.InvariantCulture),
            notorietyDelta.ToString(CultureInfo.InvariantCulture),
            notorietyAfter.ToString(CultureInfo.InvariantCulture),
            publicAwarenessBefore.ToString(CultureInfo.InvariantCulture),
            publicAwarenessDelta.ToString(CultureInfo.InvariantCulture),
            publicAwarenessAfter.ToString(CultureInfo.InvariantCulture),
            karmaBefore.ToString(CultureInfo.InvariantCulture),
            contactKarmaCost.ToString(CultureInfo.InvariantCulture),
            karmaAfter.ToString(CultureInfo.InvariantCulture),
            ContactsText(contacts),
            gmReviewDigest,
            ownerReviewDigest,
            Canonical(prerequisites.Select(value => Canonical(
                value.Prerequisite.ToString(),
                value.Satisfied.ToString(CultureInfo.InvariantCulture),
                value.Authority)).ToArray()),
            canSettle.ToString(CultureInfo.InvariantCulture),
            blocker.ToString(),
            sourceDigest,
            customDataDigest,
            gmPolicyDigest,
            runtimeDigest));

    private static string CalculatePlanDigest(
        CharacterAfterRunSettlementQuote quote,
        Guid transactionId,
        Guid expenseId,
        int expenseAmount,
        string expenseReason)
        => Sha256(Canonical(
            ContractName,
            "plan",
            IdentityText(quote.Identity),
            transactionId.ToString("D"),
            quote.HeatAfter.ToString(CultureInfo.InvariantCulture),
            quote.StreetCredAfter.ToString(CultureInfo.InvariantCulture),
            quote.NotorietyAfter.ToString(CultureInfo.InvariantCulture),
            quote.PublicAwarenessAfter.ToString(CultureInfo.InvariantCulture),
            quote.KarmaAfter.ToString(CultureInfo.InvariantCulture),
            quote.ContactKarmaCost.ToString(CultureInfo.InvariantCulture),
            ContactsText(quote.Contacts),
            expenseId.ToString("D"),
            expenseAmount.ToString(CultureInfo.InvariantCulture),
            expenseReason,
            quote.GmReviewDigest,
            quote.OwnerReviewDigest,
            quote.SourceDigest,
            quote.CustomDataDigest,
            quote.GmPolicyDigest,
            quote.RuntimeDigest,
            quote.LogicalDigest));

    private static string CalculatePlanDigest(CharacterAfterRunSettlementPlan plan)
        => Sha256(Canonical(
            ContractName,
            "plan",
            IdentityText(plan.Identity),
            plan.TransactionId.ToString("D"),
            plan.TargetHeat.ToString(CultureInfo.InvariantCulture),
            plan.TargetStreetCred.ToString(CultureInfo.InvariantCulture),
            plan.TargetNotoriety.ToString(CultureInfo.InvariantCulture),
            plan.TargetPublicAwareness.ToString(CultureInfo.InvariantCulture),
            plan.TargetKarma.ToString(CultureInfo.InvariantCulture),
            plan.ContactKarmaCost.ToString(CultureInfo.InvariantCulture),
            ContactsText(plan.ContactsToAdd),
            plan.ExpenseId.ToString("D"),
            plan.ExpenseAmount.ToString(CultureInfo.InvariantCulture),
            plan.ExpenseReason,
            plan.GmReviewDigest,
            plan.OwnerReviewDigest,
            plan.ExpectedSourceDigest,
            plan.ExpectedCustomDataDigest,
            plan.ExpectedGmPolicyDigest,
            plan.ExpectedRuntimeDigest,
            plan.ExpectedLogicalDigest));

    private static string CalculatePostLogicalDigest(
        CharacterAfterRunSettlementIdentity identity,
        Guid transactionId,
        int heat,
        int streetCred,
        int notoriety,
        int publicAwareness,
        int karma,
        IReadOnlyList<CharacterAfterRunContactSettlement> contacts,
        string sourceDigest,
        string customDataDigest,
        string gmPolicyDigest,
        string runtimeDigest)
        => Sha256(Canonical(
            ContractName,
            "settled",
            IdentityText(identity),
            transactionId.ToString("D"),
            heat.ToString(CultureInfo.InvariantCulture),
            streetCred.ToString(CultureInfo.InvariantCulture),
            notoriety.ToString(CultureInfo.InvariantCulture),
            publicAwareness.ToString(CultureInfo.InvariantCulture),
            karma.ToString(CultureInfo.InvariantCulture),
            ContactsText(contacts),
            sourceDigest,
            customDataDigest,
            gmPolicyDigest,
            runtimeDigest));

    private static string CalculatePostLogicalDigest(
        CharacterAfterRunSettlementReceipt receipt)
        => CalculatePostLogicalDigest(
            receipt.Identity,
            receipt.TransactionId,
            receipt.HeatAfter,
            receipt.StreetCredAfter,
            receipt.NotorietyAfter,
            receipt.PublicAwarenessAfter,
            receipt.KarmaAfter,
            receipt.AddedContacts,
            receipt.SourceDigest,
            receipt.CustomDataDigest,
            receipt.GmPolicyDigest,
            receipt.RuntimeDigest);

    private static string CalculateReceiptDigest(
        CharacterAfterRunSettlementReceipt receipt)
        => Sha256(Canonical(
            ContractName,
            "receipt",
            receipt.TransactionId.ToString("D"),
            IdentityText(receipt.Identity),
            receipt.HeatBefore.ToString(CultureInfo.InvariantCulture),
            receipt.HeatAfter.ToString(CultureInfo.InvariantCulture),
            receipt.StreetCredBefore.ToString(CultureInfo.InvariantCulture),
            receipt.StreetCredAfter.ToString(CultureInfo.InvariantCulture),
            receipt.NotorietyBefore.ToString(CultureInfo.InvariantCulture),
            receipt.NotorietyAfter.ToString(CultureInfo.InvariantCulture),
            receipt.PublicAwarenessBefore.ToString(CultureInfo.InvariantCulture),
            receipt.PublicAwarenessAfter.ToString(CultureInfo.InvariantCulture),
            receipt.KarmaBefore.ToString(CultureInfo.InvariantCulture),
            receipt.KarmaAfter.ToString(CultureInfo.InvariantCulture),
            receipt.ContactKarmaCost.ToString(CultureInfo.InvariantCulture),
            ContactsText(receipt.AddedContacts),
            receipt.ExpenseId.ToString("D"),
            receipt.ExpenseAmount.ToString(CultureInfo.InvariantCulture),
            receipt.ExpenseReason,
            receipt.GmReviewDigest,
            receipt.OwnerReviewDigest,
            receipt.SourceDigest,
            receipt.CustomDataDigest,
            receipt.GmPolicyDigest,
            receipt.RuntimeDigest,
            receipt.LogicalDigestBefore,
            receipt.LogicalDigestAfter));

    private static string CalculateCorrectionDigest(
        CharacterAfterRunSettlementCorrectionPlan correction)
        => Sha256(Canonical(
            ContractName,
            "correction",
            correction.CorrectionId.ToString("D"),
            correction.OriginalTransactionId.ToString("D"),
            IdentityText(correction.Identity),
            correction.RestoredHeat.ToString(CultureInfo.InvariantCulture),
            correction.RestoredStreetCred.ToString(CultureInfo.InvariantCulture),
            correction.RestoredNotoriety.ToString(CultureInfo.InvariantCulture),
            correction.RestoredPublicAwareness.ToString(CultureInfo.InvariantCulture),
            correction.RestoredKarma.ToString(CultureInfo.InvariantCulture),
            Canonical(correction.ContactIdsToRemove
                .Select(static id => id.ToString("D")).ToArray()),
            correction.ExpenseIdToRemove.ToString("D"),
            correction.Reason,
            correction.GmReviewDigest,
            correction.OwnerReviewDigest,
            correction.ExpectedPostLogicalDigest,
            correction.OriginalReceiptDigest));

    private static string ContactsText(
        IReadOnlyList<CharacterAfterRunContactSettlement> contacts)
        => Canonical(contacts.Select(contact => Canonical(
            contact.ContactId.ToString("D"),
            contact.Name,
            contact.Role,
            contact.Location,
            contact.Connection.ToString(CultureInfo.InvariantCulture),
            contact.Loyalty.ToString(CultureInfo.InvariantCulture),
            contact.Kind.ToString(),
            contact.KarmaCost.ToString(CultureInfo.InvariantCulture))).ToArray());

    private static string IdentityText(CharacterAfterRunSettlementIdentity identity)
        => Canonical(
            identity.ProposalId.ToString("D"),
            identity.RunId.ToString("D"),
            identity.CharacterId.ToString("D"));

    private static string Canonical(params string[] values)
        => string.Join('\0', values.Select(value =>
            string.Concat(
                value.Length.ToString(CultureInfo.InvariantCulture),
                ":",
                value)));

    private static string Sha256(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedEquals(string? left, string? right)
    {
        if (left is null || right is null)
            return false;
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static CharacterAfterRunSettlementQuote UnavailableQuote()
        => new(
            new(Guid.Empty, Guid.Empty, Guid.Empty),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            [], string.Empty, string.Empty, [], false,
            CharacterAfterRunSettlementBlocker.InexactProjection,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    private static CharacterAfterRunSettlementPlan UnavailablePlan()
        => new(
            new(Guid.Empty, Guid.Empty, Guid.Empty),
            Guid.Empty,
            0, 0, 0, 0, 0, 0, [], Guid.Empty, 0, string.Empty,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty);

    private static CharacterAfterRunSettlementReceipt UnavailableReceipt()
        => new(
            Guid.Empty,
            new(Guid.Empty, Guid.Empty, Guid.Empty),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], Guid.Empty, 0,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    private static CharacterAfterRunSettlementCorrectionPlan UnavailableCorrection()
        => new(
            Guid.Empty,
            Guid.Empty,
            new(Guid.Empty, Guid.Empty, Guid.Empty),
            0, 0, 0, 0, 0, [], Guid.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, string.Empty);
}
