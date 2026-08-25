using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerQualityType
{
    Positive,
    Negative
}

public enum CharacterCareerQualityOrigin
{
    Selected,
    Metatype,
    MetatypeRemovable,
    BuiltIn,
    LifeModule,
    Improvement,
    MetatypeRemovedAtChargen,
    Heritage,
    QualityLevelImprovement
}

public enum CharacterCareerQualityOperation
{
    AcquireLevel,
    RemoveLevel,
    RemoveAllLevels
}

public sealed record CharacterCareerQualityIdentity(
    Guid InternalId,
    Guid SourceId);

public sealed record CharacterCareerQualityInstance(
    CharacterCareerQualityIdentity Identity,
    string Extra,
    string SourceName,
    CharacterCareerQualityType Type,
    CharacterCareerQualityOrigin Origin);

public sealed record CharacterCareerQualitySettings(
    int KarmaQuality,
    bool DontDoubleQualityPurchases,
    bool DontDoubleQualityRefunds);

public sealed record CharacterCareerQualityDefinition(
    Guid SourceId,
    string Name,
    CharacterCareerQualityType Type,
    int BaseKarma,
    bool Implemented,
    bool SourceEnabled,
    bool CareerOnly,
    bool ChargenOnly,
    bool OnlyPriorityGiven,
    bool DoubleCostCareer,
    bool StagedPurchase,
    bool RefundKarmaOnRemove,
    bool NoLevels,
    bool LimitIsUnlimited,
    int LevelLimit,
    bool Metagenic,
    bool ContributeToBp,
    bool CostDiscountDefined,
    bool CostDiscountProjectionIsExact,
    bool CostDiscountRequirementsMet,
    int CostDiscountValue);

public sealed record CharacterCareerQualityEligibilityProjection(
    bool IsExact,
    bool GeneralRequirementsMet,
    bool RequiredOneOfQualityMet,
    bool RequiredOneOfMetatypeMet,
    bool RequiredAllQualitiesMet,
    bool ForbiddenQualitiesClear,
    IReadOnlyList<Guid> ConflictingQualityInternalIds,
    IReadOnlyList<string> MissingRequirementIds,
    string ProjectionDigest);

public enum CharacterCareerQualityEffectFamily
{
    Bonus,
    FirstLevelBonus,
    AddedQuality,
    AddedWeapon,
    NaturalWeapon,
    CritterPower,
    ChoiceSelection
}

public sealed record CharacterCareerQualityEffectProjection(
    bool IsExact,
    IReadOnlyList<CharacterCareerQualityEffectFamily> AppliedFamilies,
    IReadOnlyList<CharacterCareerQualityEffectFamily> UnsupportedFamilies,
    int MutationCount,
    string DeltaDigest);

public sealed record CharacterCareerQualityExecutionBinding(
    string OwnerId,
    string WorkspaceId,
    long WorkspaceRevision,
    long SavedRevision,
    string RuntimeFingerprint,
    string ContentDigest);

public sealed record CharacterCareerQualityAuthorityProjection(
    bool Created,
    string RulesetId,
    bool DefinitionProjectionIsExact,
    bool IdentityProjectionIsExact,
    bool GmAllows,
    bool GmFreeCostApproved,
    bool HasMentorSpiritWay,
    int MetagenicLimit,
    CharacterCareerQualitySettings Settings,
    CharacterCareerQualityEligibilityProjection Eligibility,
    CharacterCareerQualityEffectProjection Effects);

public sealed record CharacterCareerQualityInput(
    CharacterCareerQualityOperation Operation,
    CharacterCareerQualityIdentity Identity,
    bool Created,
    string RulesetId,
    bool DefinitionProjectionIsExact,
    bool IdentityProjectionIsExact,
    bool ProposedInternalIdUnused,
    bool TargetOwnedByCharacter,
    bool GmAllows,
    bool GmFreeCostApproved,
    bool HasMentorSpiritWay,
    int MetagenicLimit,
    int AvailableKarma,
    string Extra,
    string SourceName,
    CharacterCareerQualityDefinition Definition,
    CharacterCareerQualitySettings Settings,
    CharacterCareerQualityEligibilityProjection Eligibility,
    CharacterCareerQualityEffectProjection Effects,
    IReadOnlyList<CharacterCareerQualityInstance> MatchingInstances,
    CharacterCareerQualityExecutionBinding Binding,
    string RawSourceState,
    string RawRuleState);

public enum CharacterCareerQualityPrerequisite
{
    CareerCharacter,
    Sr5Ruleset,
    ExactDefinition,
    ExactIdentityProjection,
    ExactTargetOwnership,
    EnabledSource,
    ImplementedDefinition,
    CareerAvailability,
    GmPermission,
    ExactEligibilityProjection,
    RequirementsSatisfied,
    ForbiddenQualitiesClear,
    LegalLevel,
    RemovableOrigin,
    ExactCostDiscount,
    ExactEffectProjection,
    SupportedEffectFamilies,
    SufficientKarma
}

public sealed record CharacterCareerQualityPrerequisiteResult(
    CharacterCareerQualityPrerequisite Prerequisite,
    bool Satisfied,
    string Authority);

public enum CharacterCareerQualityBlocker
{
    None,
    NotCareerCharacter,
    UnsupportedRuleset,
    InvalidDefinitionProjection,
    InvalidIdentityProjection,
    ForeignOrCollidingTarget,
    SourceDisabled,
    UnimplementedDefinition,
    CareerUnavailable,
    GmRestricted,
    InvalidEligibilityProjection,
    MissingRequirement,
    ForbiddenConflict,
    DuplicateOrLevelLimit,
    UnremovableOrigin,
    InvalidCostDiscountProjection,
    InvalidEffectProjection,
    UnsupportedEffectFamily,
    InsufficientKarma
}

public enum CharacterCareerQualityTimeAuthority
{
    ImmediateChummerPersistence
}

public sealed record CharacterCareerQualityQuote(
    CharacterCareerQualityOperation Operation,
    CharacterCareerQualityIdentity Identity,
    CharacterCareerQualityDefinition Definition,
    string Extra,
    string SourceName,
    int LevelBefore,
    int LevelAfter,
    IReadOnlyList<CharacterCareerQualityInstance> InstancesBefore,
    IReadOnlyList<Guid> AffectedInternalIds,
    bool TargetIdentityResolved,
    int AvailableKarma,
    int RuleKarmaCost,
    int CharacterKarmaDelta,
    bool CreatesExpense,
    bool ExpenseRefund,
    string ExpenseReason,
    string KarmaUndoType,
    string UndoObjectId,
    string UndoExtra,
    TimeSpan ApplicationDuration,
    CharacterCareerQualityTimeAuthority TimeAuthority,
    IReadOnlyList<CharacterCareerQualityPrerequisiteResult> Prerequisites,
    bool CanApply,
    CharacterCareerQualityBlocker Blocker,
    CharacterCareerQualityAuthorityProjection Authority,
    CharacterCareerQualityExecutionBinding Binding,
    string SourceRevision,
    string RuleDigest,
    string LogicalRevision);

public sealed record CharacterCareerQualityStateObservation(
    CharacterCareerQualityIdentity Identity,
    CharacterCareerQualityDefinition Definition,
    string Extra,
    string SourceName,
    IReadOnlyList<CharacterCareerQualityInstance> Instances,
    int AvailableKarma,
    CharacterCareerQualityExecutionBinding Binding,
    string SourceRevision,
    string RuleDigest,
    string StateDigest);

public sealed record CharacterCareerQualityPlan(
    Guid TransactionId,
    CharacterCareerQualityOperation Operation,
    CharacterCareerQualityIdentity Identity,
    CharacterCareerQualityDefinition Definition,
    string Extra,
    string SourceName,
    IReadOnlyList<CharacterCareerQualityInstance> InstancesBefore,
    IReadOnlyList<CharacterCareerQualityInstance> InstancesAfter,
    IReadOnlyList<Guid> AffectedInternalIds,
    int SavedCharacterKarma,
    bool CreatesExpense,
    Guid ExpenseId,
    DateTime ExpenseDateLocal,
    int ExpenseAmount,
    string ExpenseReason,
    bool ExpenseRefund,
    string ExpenseType,
    bool ForceCareerVisible,
    string KarmaUndoType,
    string NuyenUndoType,
    string UndoObjectId,
    decimal UndoQuantity,
    string UndoExtra,
    string OwnerId,
    string WorkspaceId,
    long ExpectedWorkspaceRevision,
    long TargetWorkspaceRevision,
    long ExpectedSavedRevision,
    long TargetSavedRevision,
    string ExpectedRuntimeFingerprint,
    string ExpectedContentDigest,
    string ExpectedSourceRevision,
    string ExpectedRuleDigest,
    string ExpectedLogicalRevision);

public sealed record CharacterCareerQualityExpenseObservation(
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

public sealed record CharacterCareerQualityReceipt(
    Guid TransactionId,
    CharacterCareerQualityOperation Operation,
    CharacterCareerQualityIdentity Identity,
    CharacterCareerQualityDefinition Definition,
    string Extra,
    string SourceName,
    IReadOnlyList<CharacterCareerQualityInstance> InstancesBefore,
    IReadOnlyList<CharacterCareerQualityInstance> InstancesAfter,
    IReadOnlyList<Guid> AffectedInternalIds,
    int CharacterKarmaBefore,
    int CharacterKarmaAfter,
    bool CreatesExpense,
    Guid ExpenseId,
    DateTime ExpenseDateLocal,
    int ExpenseAmount,
    string ExpenseReason,
    bool ExpenseRefund,
    string ExpenseAuthorityDigest,
    string OwnerId,
    string WorkspaceId,
    long WorkspaceRevisionBefore,
    long WorkspaceRevisionAfter,
    long SavedRevisionBefore,
    long SavedRevisionAfter,
    string RuntimeFingerprint,
    string ContentDigest,
    string SourceRevisionBefore,
    string RuleDigestBefore,
    string LogicalRevisionBefore,
    string SourceRevisionAfter,
    string RuleDigestAfter,
    string StateDigestAfter,
    string ReceiptDigest);

public sealed record CharacterCareerQualityCorrectionPlan(
    Guid CorrectionId,
    Guid OriginalTransactionId,
    CharacterCareerQualityOperation OriginalOperation,
    CharacterCareerQualityIdentity Identity,
    IReadOnlyList<CharacterCareerQualityInstance> RestoreInstances,
    IReadOnlyList<Guid> RemoveInternalIds,
    int SavedCharacterKarma,
    bool RemoveExpense,
    Guid ExpenseIdToRemove,
    string Reason,
    string OwnerId,
    string WorkspaceId,
    long ExpectedWorkspaceRevision,
    long TargetWorkspaceRevision,
    long ExpectedSavedRevision,
    long TargetSavedRevision,
    string ExpectedRuntimeFingerprint,
    string ExpectedContentDigest,
    string ExpectedSourceRevision,
    string ExpectedRuleDigest,
    string ExpectedStateDigest,
    string OriginalReceiptDigest,
    string CorrectionDigest);

/// <summary>
/// Deterministic SR5/Chummer5 authority for Career-mode quality acquisition and
/// removal. Chummer5 commit fe4355d is the behavioral authority. Each call
/// represents one acquired level, one removed level, or a full same-source/
/// extra/source-name/type removal. Core never interprets quality labels or raw
/// XML effects: the caller must supply exact eligibility and effect projections,
/// and every unsupported effect family fails closed.
/// Inputs are trusted engine-side projections, never client authorization claims.
/// The persistence executor must reload the bound owner/workspace, recompute the
/// complete matching-instance projection, atomically compare both revisions,
/// claim the transaction id, apply the full plan, and persist the receipt in one
/// transaction. The boolean replay observations are evidence for this pure rule
/// layer; they are not a substitute for the executor's durable unique constraints.
/// </summary>
public static class CharacterCareerQualityRules
{
    public const string ContractName = "chummer.core.sr5-career-quality/v1";
    public const string RulesetId = "sr5";
    public const int RevisionHexLength = 64;
    public const int MaximumRating = 1000;
    public const int MaximumKarma = 9_999_999;
    public const int MaximumTextLength = 512;
    public const int MaximumReasonLength = 1024;
    public const int MaximumRuleTextLength = 1_048_576;
    public const long MaximumRevision = 9_007_199_254_740_991;
    public static readonly DateTime MinimumExpenseDate = new(1753, 1, 1);
    public static readonly DateTime MaximumExpenseDate = new(9998, 12, 31, 23, 59, 59);

    public static bool TryCreateQuote(
        CharacterCareerQualityInput? input,
        out CharacterCareerQualityQuote quote)
    {
        quote = UnavailableQuote();
        if (!IsValidInput(input))
        {
            return false;
        }

        CharacterCareerQualityInput valid = input!;
        int levelBefore = valid.MatchingInstances.Count;
        int levelAfter = valid.Operation switch
        {
            CharacterCareerQualityOperation.AcquireLevel => checked(levelBefore + 1),
            CharacterCareerQualityOperation.RemoveLevel => checked(levelBefore - 1),
            CharacterCareerQualityOperation.RemoveAllLevels => 0,
            _ => throw new InvalidOperationException("Unsupported quality operation.")
        };

        int ruleKarmaCost;
        int karmaDelta;
        bool createsExpense;
        bool expenseRefund;
        try
        {
            CalculateTransaction(valid, levelBefore, out ruleKarmaCost,
                out karmaDelta, out createsExpense, out expenseRefund);
        }
        catch (OverflowException)
        {
            return false;
        }

        CharacterCareerQualityBlocker blocker = ExpectedBlocker(
            valid, levelBefore, ruleKarmaCost, karmaDelta);
        bool canApply = blocker == CharacterCareerQualityBlocker.None;
        IReadOnlyList<Guid> affectedIds = AffectedInternalIds(valid);
        bool targetIdentityResolved = TargetIdentityIsExact(valid);
        string reason = ExpenseReason(valid.Operation, valid.Definition.Type,
            valid.Definition.Name);
        string karmaUndoType = valid.Operation == CharacterCareerQualityOperation.AcquireLevel
            ? "AddQuality"
            : "RemoveQuality";
        string undoObjectId = valid.Operation == CharacterCareerQualityOperation.AcquireLevel
            ? valid.Identity.InternalId.ToString("D")
            : valid.Identity.SourceId.ToString("D");
        string undoExtra = valid.Operation == CharacterCareerQualityOperation.AcquireLevel
            ? string.Empty
            : valid.Extra;
        CharacterCareerQualityPrerequisiteResult[] prerequisites = CreatePrerequisites(
            valid, levelBefore, ruleKarmaCost, karmaDelta);
        CharacterCareerQualityAuthorityProjection authority =
            CreateAuthorityProjection(valid);
        string sourceRevision = Sha256(valid.RawSourceState);
        string ruleDigest = CalculateRuleDigest(valid);
        string logicalRevision = CalculateLogicalRevision(
            valid.Operation, valid.Identity, valid.Definition, valid.Extra,
            valid.SourceName, levelBefore, levelAfter, valid.MatchingInstances,
            affectedIds, valid.AvailableKarma, ruleKarmaCost, karmaDelta,
            createsExpense, expenseRefund, reason, karmaUndoType, undoObjectId,
            undoExtra, prerequisites, canApply, blocker, authority,
            valid.Binding, sourceRevision, ruleDigest);

        quote = new CharacterCareerQualityQuote(
            valid.Operation, valid.Identity, valid.Definition, valid.Extra,
            valid.SourceName, levelBefore, levelAfter,
            CopyInstances(valid.MatchingInstances), affectedIds,
            targetIdentityResolved, valid.AvailableKarma, ruleKarmaCost,
            karmaDelta, createsExpense,
            expenseRefund, reason, karmaUndoType, undoObjectId, undoExtra,
            TimeSpan.Zero,
            CharacterCareerQualityTimeAuthority.ImmediateChummerPersistence,
            prerequisites, canApply, blocker, authority, valid.Binding,
            sourceRevision, ruleDigest, logicalRevision);
        return true;
    }

    public static bool TryPlan(
        CharacterCareerQualityQuote? current,
        string? expectedLogicalRevision,
        string? expectedSourceRevision,
        string? expectedRuleDigest,
        string? expectedRuntimeFingerprint,
        string? expectedContentDigest,
        long expectedWorkspaceRevision,
        long expectedSavedRevision,
        bool confirmed,
        bool transactionIdAlreadyExists,
        Guid transactionId,
        DateTime expenseDateLocal,
        out CharacterCareerQualityPlan plan)
    {
        plan = UnavailablePlan();
        DateTime normalizedDate = DateTime.SpecifyKind(expenseDateLocal,
            DateTimeKind.Unspecified);
        if (!confirmed
            || transactionIdAlreadyExists
            || !IsCoherent(current)
            || !current!.CanApply
            || transactionId == Guid.Empty
            || !RevisionMatches(current.LogicalRevision, expectedLogicalRevision)
            || !RevisionMatches(current.SourceRevision, expectedSourceRevision)
            || !RevisionMatches(current.RuleDigest, expectedRuleDigest)
            || !RevisionMatches(current.Binding.RuntimeFingerprint, expectedRuntimeFingerprint)
            || !RevisionMatches(current.Binding.ContentDigest, expectedContentDigest)
            || current.Binding.WorkspaceRevision != expectedWorkspaceRevision
            || current.Binding.SavedRevision != expectedSavedRevision
            || normalizedDate < MinimumExpenseDate
            || normalizedDate > MaximumExpenseDate
            || current.Binding.WorkspaceRevision >= MaximumRevision
            || current.Binding.SavedRevision >= MaximumRevision)
        {
            return false;
        }

        IReadOnlyList<CharacterCareerQualityInstance> after = CreatePostInstances(current);
        int savedKarma;
        try
        {
            savedKarma = checked(current.AvailableKarma + current.CharacterKarmaDelta);
        }
        catch (OverflowException)
        {
            return false;
        }

        Guid expenseId = current.CreatesExpense ? transactionId : Guid.Empty;
        plan = new CharacterCareerQualityPlan(
            transactionId, current.Operation, current.Identity,
            current.Definition, current.Extra, current.SourceName,
            CopyInstances(current.InstancesBefore), after,
            current.AffectedInternalIds.ToArray(), savedKarma,
            current.CreatesExpense, expenseId, normalizedDate,
            current.CharacterKarmaDelta, current.ExpenseReason,
            current.ExpenseRefund, "Karma", false, current.KarmaUndoType,
            "AddCyberware", current.UndoObjectId, 0m, current.UndoExtra,
            current.Binding.OwnerId, current.Binding.WorkspaceId,
            current.Binding.WorkspaceRevision,
            current.Binding.WorkspaceRevision + 1,
            current.Binding.SavedRevision,
            current.Binding.SavedRevision + 1,
            current.Binding.RuntimeFingerprint, current.Binding.ContentDigest,
            current.SourceRevision, current.RuleDigest,
            current.LogicalRevision);
        return IsCoherent(plan);
    }

    public static bool TryCreateStateObservation(
        CharacterCareerQualityIdentity? identity,
        CharacterCareerQualityDefinition? definition,
        string? extra,
        string? sourceName,
        IReadOnlyList<CharacterCareerQualityInstance>? instances,
        int availableKarma,
        CharacterCareerQualityExecutionBinding? binding,
        string? rawSourceState,
        string? ruleDigest,
        out CharacterCareerQualityStateObservation observation)
    {
        observation = UnavailableStateObservation();
        if (!IsValidIdentity(identity)
            || !IsValidDefinition(definition)
            || identity!.SourceId != definition!.SourceId
            || !IsValidText(extra, true)
            || !IsValidText(sourceName, true)
            || !IsValidInstances(instances, definition, extra!, sourceName!)
            || availableKarma is < -MaximumKarma or > MaximumKarma
            || !IsCoherentBinding(binding)
            || string.IsNullOrWhiteSpace(rawSourceState)
            || rawSourceState.Length > MaximumRuleTextLength
            || !IsLowerHexRevision(ruleDigest))
        {
            return false;
        }

        string sourceRevision = Sha256(rawSourceState);
        string stateDigest = CalculateStateDigest(
            identity, definition, extra!, sourceName!, instances!,
            availableKarma, binding!, sourceRevision, ruleDigest!);
        observation = new CharacterCareerQualityStateObservation(
            identity, definition, extra!, sourceName!, CopyInstances(instances!),
            availableKarma, binding!, sourceRevision, ruleDigest!, stateDigest);
        return true;
    }

    public static bool TryCreateReceipt(
        Guid transactionId,
        CharacterCareerQualityQuote? reviewed,
        CharacterCareerQualityPlan? plan,
        CharacterCareerQualityStateObservation? observedPostState,
        CharacterCareerQualityExpenseObservation? observedExpense,
        out CharacterCareerQualityReceipt receipt)
    {
        receipt = UnavailableReceipt();
        if (transactionId == Guid.Empty
            || !IsCoherent(reviewed)
            || !IsCoherent(plan)
            || !IsCoherent(observedPostState)
            || transactionId != plan!.TransactionId
            || !reviewed!.CanApply
            || !PlanMatchesQuote(plan, reviewed!)
            || !PostStateMatches(plan, observedPostState!)
            || !ExpenseMatchesPlan(observedExpense, plan))
        {
            return false;
        }

        string expenseDigest = CalculateExpenseAuthorityDigest(observedExpense!);
        string receiptDigest = CalculateReceiptDigest(
            transactionId, plan, reviewed!, observedPostState!, expenseDigest);
        receipt = new CharacterCareerQualityReceipt(
            transactionId, plan.Operation, plan.Identity, plan.Definition,
            plan.Extra, plan.SourceName, CopyInstances(plan.InstancesBefore),
            CopyInstances(plan.InstancesAfter), plan.AffectedInternalIds.ToArray(),
            reviewed!.AvailableKarma, observedPostState!.AvailableKarma,
            plan.CreatesExpense, plan.ExpenseId, plan.ExpenseDateLocal,
            plan.ExpenseAmount, plan.ExpenseReason, plan.ExpenseRefund,
            expenseDigest, plan.OwnerId, plan.WorkspaceId,
            plan.ExpectedWorkspaceRevision, plan.TargetWorkspaceRevision,
            plan.ExpectedSavedRevision, plan.TargetSavedRevision,
            plan.ExpectedRuntimeFingerprint, plan.ExpectedContentDigest,
            reviewed.SourceRevision, reviewed.RuleDigest,
            reviewed.LogicalRevision, observedPostState.SourceRevision,
            observedPostState.RuleDigest, observedPostState.StateDigest,
            receiptDigest);
        return IsCoherent(receipt);
    }

    public static bool TryRecoverReceipt(
        CharacterCareerQualityReceipt? persistedReceipt,
        Guid expectedTransactionId,
        CharacterCareerQualityStateObservation? observedPostState,
        CharacterCareerQualityExpenseObservation? observedExpense,
        string? expectedReceiptDigest,
        out CharacterCareerQualityReceipt receipt)
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
        CharacterCareerQualityReceipt? original,
        CharacterCareerQualityStateObservation? observedPostState,
        CharacterCareerQualityExpenseObservation? observedExpense,
        Guid correctionId,
        string? reason,
        bool correctionIdAlreadyExists,
        bool originalTransactionAlreadyCorrected,
        string? expectedReceiptDigest,
        out CharacterCareerQualityCorrectionPlan correction)
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
            || normalizedReason.Length is 0 or > MaximumTextLength
            || original.WorkspaceRevisionAfter >= MaximumRevision
            || original.SavedRevisionAfter >= MaximumRevision)
        {
            return false;
        }

        IReadOnlyList<CharacterCareerQualityInstance> restore =
            original.Operation == CharacterCareerQualityOperation.AcquireLevel
                ? []
                : CopyInstances(original.InstancesBefore
                    .Where(instance => original.AffectedInternalIds.Contains(
                        instance.Identity.InternalId)));
        IReadOnlyList<Guid> remove =
            original.Operation == CharacterCareerQualityOperation.AcquireLevel
                ? original.AffectedInternalIds.ToArray()
                : [];
        string digest = CalculateCorrectionDigest(
            correctionId, original.TransactionId, original.Operation,
            original.Identity, restore, remove, original.CharacterKarmaBefore,
            original.CreatesExpense, original.ExpenseId, normalizedReason,
            original.OwnerId, original.WorkspaceId,
            original.WorkspaceRevisionAfter, original.WorkspaceRevisionAfter + 1,
            original.SavedRevisionAfter, original.SavedRevisionAfter + 1,
            original.RuntimeFingerprint, original.ContentDigest,
            original.SourceRevisionAfter, original.RuleDigestAfter,
            original.StateDigestAfter, original.ReceiptDigest);
        correction = new CharacterCareerQualityCorrectionPlan(
            correctionId, original.TransactionId, original.Operation,
            original.Identity, restore, remove, original.CharacterKarmaBefore,
            original.CreatesExpense, original.ExpenseId, normalizedReason,
            original.OwnerId, original.WorkspaceId,
            original.WorkspaceRevisionAfter, original.WorkspaceRevisionAfter + 1,
            original.SavedRevisionAfter, original.SavedRevisionAfter + 1,
            original.RuntimeFingerprint, original.ContentDigest,
            original.SourceRevisionAfter, original.RuleDigestAfter,
            original.StateDigestAfter, original.ReceiptDigest, digest);
        return IsCoherent(correction);
    }

    public static bool IsCoherent(CharacterCareerQualityQuote? quote)
    {
        if (quote is null
            || !IsValidIdentity(quote.Identity)
            || !IsValidDefinition(quote.Definition)
            || quote.Identity.SourceId != quote.Definition.SourceId
            || !IsValidText(quote.Extra, true)
            || !IsValidText(quote.SourceName, true)
            || quote.LevelBefore != quote.InstancesBefore.Count
            || quote.LevelBefore is < 0 or > MaximumRating
            || quote.LevelAfter != ExpectedLevelAfter(quote.Operation, quote.LevelBefore)
            || !IsValidInstances(quote.InstancesBefore, quote.Definition,
                quote.Extra, quote.SourceName)
            || !AffectedIdsMatch(quote.Operation, quote.Identity,
                quote.InstancesBefore, quote.AffectedInternalIds)
            || quote.AvailableKarma is < -MaximumKarma or > MaximumKarma
            || quote.RuleKarmaCost is < -MaximumKarma or > MaximumKarma
            || quote.CharacterKarmaDelta is < -MaximumKarma or > MaximumKarma
            || !IsValidReason(quote.ExpenseReason)
            || quote.KarmaUndoType is not ("AddQuality" or "RemoveQuality")
            || !IsValidText(quote.UndoObjectId, false)
            || !IsValidText(quote.UndoExtra, true)
            || !QuoteOperationSemanticsMatch(quote)
            || quote.ApplicationDuration != TimeSpan.Zero
            || quote.TimeAuthority != CharacterCareerQualityTimeAuthority.ImmediateChummerPersistence
            || !IsValidAuthorityProjection(quote.Authority)
            || !QuoteArithmeticMatches(quote)
            || !IsCoherentBinding(quote.Binding)
            || !IsCoherentPrerequisites(quote.Prerequisites)
            || quote.CanApply != (quote.Blocker == CharacterCareerQualityBlocker.None)
            || quote.Blocker != ExpectedBlocker(quote)
            || !IsLowerHexRevision(quote.SourceRevision)
            || !IsLowerHexRevision(quote.RuleDigest)
            || !RevisionMatches(CalculateLogicalRevision(
                    quote.Operation, quote.Identity, quote.Definition, quote.Extra,
                    quote.SourceName, quote.LevelBefore, quote.LevelAfter,
                    quote.InstancesBefore, quote.AffectedInternalIds,
                    quote.AvailableKarma, quote.RuleKarmaCost,
                    quote.CharacterKarmaDelta, quote.CreatesExpense,
                    quote.ExpenseRefund, quote.ExpenseReason,
                    quote.KarmaUndoType, quote.UndoObjectId, quote.UndoExtra,
                    quote.Prerequisites, quote.CanApply, quote.Blocker,
                    quote.Authority, quote.Binding, quote.SourceRevision,
                    quote.RuleDigest),
                quote.LogicalRevision))
        {
            return false;
        }

        return PrerequisitesMatchQuote(quote);
    }

    public static bool IsCoherent(CharacterCareerQualityPlan? plan)
        => plan is not null
            && plan.TransactionId != Guid.Empty
            && IsValidIdentity(plan.Identity)
            && IsValidDefinition(plan.Definition)
            && plan.Identity.SourceId == plan.Definition.SourceId
            && IsValidText(plan.Extra, true)
            && IsValidText(plan.SourceName, true)
            && IsValidInstances(plan.InstancesBefore, plan.Definition,
                plan.Extra, plan.SourceName)
            && IsValidInstances(plan.InstancesAfter, plan.Definition,
                plan.Extra, plan.SourceName)
            && ExpectedLevelAfter(plan.Operation, plan.InstancesBefore.Count)
                == plan.InstancesAfter.Count
            && AffectedIdsMatch(plan.Operation, plan.Identity,
                plan.InstancesBefore, plan.AffectedInternalIds)
            && PostInstancesMatchPlan(plan)
            && plan.SavedCharacterKarma is >= -MaximumKarma and <= MaximumKarma
            && (plan.CreatesExpense
                ? plan.ExpenseId == plan.TransactionId
                : plan.ExpenseId == Guid.Empty)
            && plan.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
            && plan.ExpenseDateLocal >= MinimumExpenseDate
            && plan.ExpenseDateLocal <= MaximumExpenseDate
            && plan.ExpenseAmount is >= -MaximumKarma and <= MaximumKarma
            && IsValidReason(plan.ExpenseReason)
            && plan.ExpenseType == "Karma"
            && !plan.ForceCareerVisible
            && plan.KarmaUndoType is "AddQuality" or "RemoveQuality"
            && plan.NuyenUndoType == "AddCyberware"
            && IsValidText(plan.UndoObjectId, false)
            && IsValidText(plan.UndoExtra, true)
            && plan.UndoQuantity == 0m
            && PlanOperationSemanticsMatch(plan)
            && IsValidText(plan.OwnerId, false)
            && IsValidText(plan.WorkspaceId, false)
            && IsNextRevision(plan.ExpectedWorkspaceRevision,
                plan.TargetWorkspaceRevision)
            && IsNextRevision(plan.ExpectedSavedRevision,
                plan.TargetSavedRevision)
            && IsLowerHexRevision(plan.ExpectedRuntimeFingerprint)
            && IsLowerHexRevision(plan.ExpectedContentDigest)
            && IsLowerHexRevision(plan.ExpectedSourceRevision)
            && IsLowerHexRevision(plan.ExpectedRuleDigest)
            && IsLowerHexRevision(plan.ExpectedLogicalRevision);

    public static bool IsCoherent(CharacterCareerQualityStateObservation? value)
        => value is not null
            && IsValidIdentity(value.Identity)
            && IsValidDefinition(value.Definition)
            && value.Identity.SourceId == value.Definition.SourceId
            && IsValidText(value.Extra, true)
            && IsValidText(value.SourceName, true)
            && IsValidInstances(value.Instances, value.Definition,
                value.Extra, value.SourceName)
            && value.AvailableKarma is >= -MaximumKarma and <= MaximumKarma
            && IsCoherentBinding(value.Binding)
            && IsLowerHexRevision(value.SourceRevision)
            && IsLowerHexRevision(value.RuleDigest)
            && RevisionMatches(CalculateStateDigest(
                    value.Identity, value.Definition, value.Extra,
                    value.SourceName, value.Instances, value.AvailableKarma,
                    value.Binding, value.SourceRevision, value.RuleDigest),
                value.StateDigest);

    public static bool IsCoherent(CharacterCareerQualityReceipt? receipt)
        => receipt is not null
            && receipt.TransactionId != Guid.Empty
            && IsValidIdentity(receipt.Identity)
            && IsValidDefinition(receipt.Definition)
            && receipt.Identity.SourceId == receipt.Definition.SourceId
            && IsValidInstances(receipt.InstancesBefore, receipt.Definition,
                receipt.Extra, receipt.SourceName)
            && IsValidInstances(receipt.InstancesAfter, receipt.Definition,
                receipt.Extra, receipt.SourceName)
            && ExpectedLevelAfter(receipt.Operation, receipt.InstancesBefore.Count)
                == receipt.InstancesAfter.Count
            && AffectedIdsMatch(receipt.Operation, receipt.Identity,
                receipt.InstancesBefore, receipt.AffectedInternalIds)
            && receipt.CharacterKarmaBefore is >= -MaximumKarma and <= MaximumKarma
            && receipt.CharacterKarmaAfter is >= -MaximumKarma and <= MaximumKarma
            && receipt.CharacterKarmaAfter
                == receipt.CharacterKarmaBefore + receipt.ExpenseAmount
            && (receipt.CreatesExpense
                ? receipt.ExpenseId == receipt.TransactionId
                : receipt.ExpenseId == Guid.Empty)
            && receipt.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
            && receipt.ExpenseDateLocal >= MinimumExpenseDate
            && receipt.ExpenseDateLocal <= MaximumExpenseDate
            && receipt.ExpenseAmount is >= -MaximumKarma and <= MaximumKarma
            && IsValidReason(receipt.ExpenseReason)
            && ReceiptOperationSemanticsMatch(receipt)
            && ReceiptInstancesMatchOperation(receipt)
            && IsLowerHexRevision(receipt.ExpenseAuthorityDigest)
            && IsValidText(receipt.OwnerId, false)
            && IsValidText(receipt.WorkspaceId, false)
            && IsNextRevision(receipt.WorkspaceRevisionBefore,
                receipt.WorkspaceRevisionAfter)
            && IsNextRevision(receipt.SavedRevisionBefore,
                receipt.SavedRevisionAfter)
            && IsLowerHexRevision(receipt.RuntimeFingerprint)
            && IsLowerHexRevision(receipt.ContentDigest)
            && IsLowerHexRevision(receipt.SourceRevisionBefore)
            && IsLowerHexRevision(receipt.RuleDigestBefore)
            && IsLowerHexRevision(receipt.LogicalRevisionBefore)
            && IsLowerHexRevision(receipt.SourceRevisionAfter)
            && IsLowerHexRevision(receipt.RuleDigestAfter)
            && IsLowerHexRevision(receipt.StateDigestAfter)
            && RevisionMatches(CalculateReceiptDigest(receipt),
                receipt.ReceiptDigest);

    public static bool IsCoherent(CharacterCareerQualityCorrectionPlan? correction)
        => correction is not null
            && correction.CorrectionId != Guid.Empty
            && correction.OriginalTransactionId != Guid.Empty
            && correction.CorrectionId != correction.OriginalTransactionId
            && IsValidIdentity(correction.Identity)
            && correction.RestoreInstances is not null
            && correction.RemoveInternalIds is not null
            && correction.RestoreInstances.All(static value => value is not null)
            && correction.RemoveInternalIds.All(static value => value != Guid.Empty)
            && correction.RemoveInternalIds.Distinct().Count()
                == correction.RemoveInternalIds.Count
            && CorrectionInverseShapeMatches(correction)
            && correction.SavedCharacterKarma is >= -MaximumKarma and <= MaximumKarma
            && (correction.RemoveExpense
                ? correction.ExpenseIdToRemove != Guid.Empty
                : correction.ExpenseIdToRemove == Guid.Empty)
            && IsValidText(correction.Reason, false)
            && IsValidText(correction.OwnerId, false)
            && IsValidText(correction.WorkspaceId, false)
            && IsNextRevision(correction.ExpectedWorkspaceRevision,
                correction.TargetWorkspaceRevision)
            && IsNextRevision(correction.ExpectedSavedRevision,
                correction.TargetSavedRevision)
            && IsLowerHexRevision(correction.ExpectedRuntimeFingerprint)
            && IsLowerHexRevision(correction.ExpectedContentDigest)
            && IsLowerHexRevision(correction.ExpectedSourceRevision)
            && IsLowerHexRevision(correction.ExpectedRuleDigest)
            && IsLowerHexRevision(correction.ExpectedStateDigest)
            && IsLowerHexRevision(correction.OriginalReceiptDigest)
            && RevisionMatches(CalculateCorrectionDigest(correction),
                correction.CorrectionDigest);

    private static bool IsValidInput(CharacterCareerQualityInput? input)
    {
        if (input is null
            || !Enum.IsDefined(input.Operation)
            || !IsValidIdentity(input.Identity)
            || input.RulesetId is null or { Length: > MaximumTextLength }
            || !IsValidText(input.Extra, true)
            || !IsValidText(input.SourceName, true)
            || input.MetagenicLimit is < 0 or > MaximumKarma
            || input.AvailableKarma is < -MaximumKarma or > MaximumKarma
            || !IsValidDefinition(input.Definition)
            || input.Identity.SourceId != input.Definition.SourceId
            || input.Settings is not
                {
                    KarmaQuality: >= 0 and <= MaximumKarma
                }
            || !IsValidEligibility(input.Eligibility)
            || !IsValidEffects(input.Effects)
            || input.MatchingInstances is null
            || input.MatchingInstances.Count > MaximumRating
            || !IsValidInstances(input.MatchingInstances, input.Definition,
                input.Extra, input.SourceName)
            || !IsCoherentBinding(input.Binding)
            || string.IsNullOrWhiteSpace(input.RawSourceState)
            || input.RawSourceState.Length > MaximumRuleTextLength
            || string.IsNullOrWhiteSpace(input.RawRuleState)
            || input.RawRuleState.Length > MaximumRuleTextLength)
        {
            return false;
        }

        return true;
    }

    private static bool IsValidDefinition(CharacterCareerQualityDefinition? value)
        => value is not null
            && value.SourceId != Guid.Empty
            && IsValidText(value.Name, false)
            && Enum.IsDefined(value.Type)
            && value.BaseKarma is >= -MaximumKarma and <= MaximumKarma
            && (value.Type == CharacterCareerQualityType.Positive
                ? value.BaseKarma >= 0
                : value.BaseKarma <= 0)
            && value.LevelLimit is >= 0 and <= MaximumRating
            && !(value.NoLevels && (value.LimitIsUnlimited || value.LevelLimit > 1))
            && !(value.LimitIsUnlimited && value.LevelLimit != 0)
            && value.CostDiscountValue is >= -MaximumKarma and <= MaximumKarma;

    private static bool IsValidEligibility(
        CharacterCareerQualityEligibilityProjection? value)
        => value is not null
            && value.ConflictingQualityInternalIds is not null
            && value.MissingRequirementIds is not null
            && value.ConflictingQualityInternalIds.All(static id => id != Guid.Empty)
            && value.ConflictingQualityInternalIds.Distinct().Count()
                == value.ConflictingQualityInternalIds.Count
            && value.MissingRequirementIds.All(id => IsValidText(id, false))
            && value.MissingRequirementIds.Distinct(StringComparer.Ordinal).Count()
                == value.MissingRequirementIds.Count
            && IsLowerHexRevision(value.ProjectionDigest);

    private static bool IsValidEffects(CharacterCareerQualityEffectProjection? value)
        => value is not null
            && value.AppliedFamilies is not null
            && value.UnsupportedFamilies is not null
            && value.AppliedFamilies.All(Enum.IsDefined)
            && value.UnsupportedFamilies.All(Enum.IsDefined)
            && value.AppliedFamilies.Distinct().Count() == value.AppliedFamilies.Count
            && value.UnsupportedFamilies.Distinct().Count()
                == value.UnsupportedFamilies.Count
            && !value.AppliedFamilies.Intersect(value.UnsupportedFamilies).Any()
            && value.MutationCount is >= 0 and <= MaximumKarma
            && IsLowerHexRevision(value.DeltaDigest);

    private static bool IsCoherentBinding(CharacterCareerQualityExecutionBinding? value)
        => value is not null
            && IsValidText(value.OwnerId, false)
            && IsValidText(value.WorkspaceId, false)
            && value.WorkspaceRevision is >= 0 and <= MaximumRevision
            && value.SavedRevision is >= 0 and <= MaximumRevision
            && IsLowerHexRevision(value.RuntimeFingerprint)
            && IsLowerHexRevision(value.ContentDigest);

    private static bool IsValidAuthorityProjection(
        CharacterCareerQualityAuthorityProjection? value)
        => value is not null
            && value.RulesetId is { Length: <= MaximumTextLength }
            && value.MetagenicLimit is >= 0 and <= MaximumKarma
            && value.Settings is
                {
                    KarmaQuality: >= 0 and <= MaximumKarma
                }
            && IsValidEligibility(value.Eligibility)
            && IsValidEffects(value.Effects);

    private static CharacterCareerQualityAuthorityProjection
        CreateAuthorityProjection(CharacterCareerQualityInput input)
        => new(
            input.Created, input.RulesetId,
            input.DefinitionProjectionIsExact,
            input.IdentityProjectionIsExact, input.GmAllows,
            input.GmFreeCostApproved, input.HasMentorSpiritWay,
            input.MetagenicLimit, input.Settings,
            input.Eligibility with
            {
                ConflictingQualityInternalIds =
                    input.Eligibility.ConflictingQualityInternalIds.ToArray(),
                MissingRequirementIds =
                    input.Eligibility.MissingRequirementIds.ToArray()
            },
            input.Effects with
            {
                AppliedFamilies = input.Effects.AppliedFamilies.ToArray(),
                UnsupportedFamilies = input.Effects.UnsupportedFamilies.ToArray()
            });

    private static bool IsValidInstances(
        IReadOnlyList<CharacterCareerQualityInstance>? values,
        CharacterCareerQualityDefinition definition,
        string extra,
        string sourceName)
    {
        if (values is null || values.Count > MaximumRating)
        {
            return false;
        }

        CharacterCareerQualityInstance?[] instances = values.ToArray();
        return !instances.Any(static instance => instance is null)
            && instances.All(instance => instance is not null
                && IsValidIdentity(instance.Identity)
                && instance.Identity.SourceId == definition.SourceId
                && instance.Type == definition.Type
                && instance.Extra == extra
                && instance.SourceName == sourceName
                && IsValidText(instance.Extra, true)
                && IsValidText(instance.SourceName, true)
                && Enum.IsDefined(instance.Origin))
            && instances.Select(instance => instance!.Identity.InternalId)
                .Distinct().Count() == instances.Length;
    }

    private static CharacterCareerQualityPrerequisiteResult[] CreatePrerequisites(
        CharacterCareerQualityInput input,
        int levelBefore,
        int ruleKarmaCost,
        int karmaDelta)
    {
        bool requirementsMet = input.Eligibility.GeneralRequirementsMet
            && input.Eligibility.RequiredOneOfQualityMet
            && input.Eligibility.RequiredOneOfMetatypeMet
            && input.Eligibility.RequiredAllQualitiesMet
            && input.Eligibility.MissingRequirementIds.Count == 0;
        bool targetExact = TargetIdentityIsExact(input);
        return
        [
            new(CharacterCareerQualityPrerequisite.CareerCharacter,
                input.Created, "character.created"),
            new(CharacterCareerQualityPrerequisite.Sr5Ruleset,
                input.RulesetId == RulesetId, "ruleset.sr5"),
            new(CharacterCareerQualityPrerequisite.ExactDefinition,
                input.DefinitionProjectionIsExact,
                $"quality.source-id:{input.Definition.SourceId:D}"),
            new(CharacterCareerQualityPrerequisite.ExactIdentityProjection,
                input.IdentityProjectionIsExact, "quality.instances:exact"),
            new(CharacterCareerQualityPrerequisite.ExactTargetOwnership,
                targetExact,
                $"quality.internal-id:{input.Identity.InternalId:D}"),
            new(CharacterCareerQualityPrerequisite.EnabledSource,
                input.Definition.SourceEnabled, "quality.source-enabled"),
            new(CharacterCareerQualityPrerequisite.ImplementedDefinition,
                input.Definition.Implemented, "quality.implemented"),
            new(CharacterCareerQualityPrerequisite.CareerAvailability,
                !input.Definition.ChargenOnly && !input.Definition.OnlyPriorityGiven,
                "quality.career-availability"),
            new(CharacterCareerQualityPrerequisite.GmPermission,
                input.GmAllows, "campaign.quality-policy"),
            new(CharacterCareerQualityPrerequisite.ExactEligibilityProjection,
                input.Eligibility.IsExact, input.Eligibility.ProjectionDigest),
            new(CharacterCareerQualityPrerequisite.RequirementsSatisfied,
                requirementsMet, "quality.required"),
            new(CharacterCareerQualityPrerequisite.ForbiddenQualitiesClear,
                input.Eligibility.ForbiddenQualitiesClear
                    && input.Eligibility.ConflictingQualityInternalIds.Count == 0,
                "quality.forbidden"),
            new(CharacterCareerQualityPrerequisite.LegalLevel,
                IsLegalLevel(input, levelBefore), "quality.level-limit"),
            new(CharacterCareerQualityPrerequisite.RemovableOrigin,
                IsRemovableOrigin(input), "quality.origin"),
            new(CharacterCareerQualityPrerequisite.ExactCostDiscount,
                !input.Definition.CostDiscountDefined
                    || input.Definition.CostDiscountProjectionIsExact,
                "quality.costdiscount"),
            new(CharacterCareerQualityPrerequisite.ExactEffectProjection,
                input.Effects.IsExact, input.Effects.DeltaDigest),
            new(CharacterCareerQualityPrerequisite.SupportedEffectFamilies,
                input.Effects.UnsupportedFamilies.Count == 0,
                "quality.effects:supported"),
            new(CharacterCareerQualityPrerequisite.SufficientKarma,
                HasSufficientKarma(input, ruleKarmaCost, karmaDelta),
                "character.karma")
        ];
    }

    private static CharacterCareerQualityBlocker ExpectedBlocker(
        CharacterCareerQualityInput input,
        int levelBefore,
        int ruleKarmaCost,
        int karmaDelta)
    {
        if (!input.Created) return CharacterCareerQualityBlocker.NotCareerCharacter;
        if (input.RulesetId != RulesetId) return CharacterCareerQualityBlocker.UnsupportedRuleset;
        if (!input.DefinitionProjectionIsExact) return CharacterCareerQualityBlocker.InvalidDefinitionProjection;
        if (!input.IdentityProjectionIsExact) return CharacterCareerQualityBlocker.InvalidIdentityProjection;
        bool selectedExists = input.MatchingInstances.Any(instance =>
            instance.Identity == input.Identity);
        if (input.Operation == CharacterCareerQualityOperation.AcquireLevel
                ? !input.ProposedInternalIdUnused || selectedExists
                : !input.TargetOwnedByCharacter || !selectedExists)
            return CharacterCareerQualityBlocker.ForeignOrCollidingTarget;
        if (!input.Definition.SourceEnabled) return CharacterCareerQualityBlocker.SourceDisabled;
        if (!input.Definition.Implemented) return CharacterCareerQualityBlocker.UnimplementedDefinition;
        if (input.Definition.ChargenOnly || input.Definition.OnlyPriorityGiven)
            return CharacterCareerQualityBlocker.CareerUnavailable;
        if (!input.GmAllows) return CharacterCareerQualityBlocker.GmRestricted;
        if (!input.Eligibility.IsExact) return CharacterCareerQualityBlocker.InvalidEligibilityProjection;
        if (!input.Eligibility.GeneralRequirementsMet
            || !input.Eligibility.RequiredOneOfQualityMet
            || !input.Eligibility.RequiredOneOfMetatypeMet
            || !input.Eligibility.RequiredAllQualitiesMet
            || input.Eligibility.MissingRequirementIds.Count != 0)
            return CharacterCareerQualityBlocker.MissingRequirement;
        if (!input.Eligibility.ForbiddenQualitiesClear
            || input.Eligibility.ConflictingQualityInternalIds.Count != 0)
            return CharacterCareerQualityBlocker.ForbiddenConflict;
        if (!IsLegalLevel(input, levelBefore))
            return CharacterCareerQualityBlocker.DuplicateOrLevelLimit;
        if (!IsRemovableOrigin(input))
            return CharacterCareerQualityBlocker.UnremovableOrigin;
        if (input.Definition.CostDiscountDefined
            && !input.Definition.CostDiscountProjectionIsExact)
            return CharacterCareerQualityBlocker.InvalidCostDiscountProjection;
        if (!input.Effects.IsExact)
            return CharacterCareerQualityBlocker.InvalidEffectProjection;
        if (input.Effects.UnsupportedFamilies.Count != 0)
            return CharacterCareerQualityBlocker.UnsupportedEffectFamily;
        return HasSufficientKarma(input, ruleKarmaCost, karmaDelta)
            ? CharacterCareerQualityBlocker.None
            : CharacterCareerQualityBlocker.InsufficientKarma;
    }

    private static CharacterCareerQualityBlocker ExpectedBlocker(
        CharacterCareerQualityQuote quote)
    {
        Dictionary<CharacterCareerQualityPrerequisite, bool> p =
            quote.Prerequisites.ToDictionary(
                static value => value.Prerequisite,
                static value => value.Satisfied);
        if (!p[CharacterCareerQualityPrerequisite.CareerCharacter])
            return CharacterCareerQualityBlocker.NotCareerCharacter;
        if (!p[CharacterCareerQualityPrerequisite.Sr5Ruleset])
            return CharacterCareerQualityBlocker.UnsupportedRuleset;
        if (!p[CharacterCareerQualityPrerequisite.ExactDefinition])
            return CharacterCareerQualityBlocker.InvalidDefinitionProjection;
        if (!p[CharacterCareerQualityPrerequisite.ExactIdentityProjection])
            return CharacterCareerQualityBlocker.InvalidIdentityProjection;
        if (!p[CharacterCareerQualityPrerequisite.ExactTargetOwnership])
            return CharacterCareerQualityBlocker.ForeignOrCollidingTarget;
        if (!p[CharacterCareerQualityPrerequisite.EnabledSource])
            return CharacterCareerQualityBlocker.SourceDisabled;
        if (!p[CharacterCareerQualityPrerequisite.ImplementedDefinition])
            return CharacterCareerQualityBlocker.UnimplementedDefinition;
        if (!p[CharacterCareerQualityPrerequisite.CareerAvailability])
            return CharacterCareerQualityBlocker.CareerUnavailable;
        if (!p[CharacterCareerQualityPrerequisite.GmPermission])
            return CharacterCareerQualityBlocker.GmRestricted;
        if (!p[CharacterCareerQualityPrerequisite.ExactEligibilityProjection])
            return CharacterCareerQualityBlocker.InvalidEligibilityProjection;
        if (!p[CharacterCareerQualityPrerequisite.RequirementsSatisfied])
            return CharacterCareerQualityBlocker.MissingRequirement;
        if (!p[CharacterCareerQualityPrerequisite.ForbiddenQualitiesClear])
            return CharacterCareerQualityBlocker.ForbiddenConflict;
        if (!p[CharacterCareerQualityPrerequisite.LegalLevel])
            return CharacterCareerQualityBlocker.DuplicateOrLevelLimit;
        if (!p[CharacterCareerQualityPrerequisite.RemovableOrigin])
            return CharacterCareerQualityBlocker.UnremovableOrigin;
        if (!p[CharacterCareerQualityPrerequisite.ExactCostDiscount])
            return CharacterCareerQualityBlocker.InvalidCostDiscountProjection;
        if (!p[CharacterCareerQualityPrerequisite.ExactEffectProjection])
            return CharacterCareerQualityBlocker.InvalidEffectProjection;
        if (!p[CharacterCareerQualityPrerequisite.SupportedEffectFamilies])
            return CharacterCareerQualityBlocker.UnsupportedEffectFamily;
        return p[CharacterCareerQualityPrerequisite.SufficientKarma]
            ? CharacterCareerQualityBlocker.None
            : CharacterCareerQualityBlocker.InsufficientKarma;
    }

    private static bool HasSufficientKarma(
        CharacterCareerQualityInput input,
        int ruleKarmaCost,
        int karmaDelta)
    {
        long resultingKarma = (long)input.AvailableKarma + karmaDelta;
        if (resultingKarma is < -MaximumKarma or > MaximumKarma)
        {
            return false;
        }

        if (input.Operation == CharacterCareerQualityOperation.AcquireLevel
            && input.Definition.Type == CharacterCareerQualityType.Positive
            && input.Definition.StagedPurchase)
        {
            return true;
        }

        return ruleKarmaCost >= 0
            ? resultingKarma >= 0
            : true;
    }

    private static bool IsLegalLevel(CharacterCareerQualityInput input, int levelBefore)
    {
        if (input.Operation != CharacterCareerQualityOperation.AcquireLevel)
        {
            return levelBefore > 0;
        }

        if (input.Definition.NoLevels)
        {
            return levelBefore == 0;
        }

        if (input.Definition.LimitIsUnlimited)
        {
            return levelBefore < MaximumRating;
        }

        int limit = input.Definition.LevelLimit > 0
            ? input.Definition.LevelLimit
            : 1;
        return levelBefore < limit;
    }

    private static bool IsRemovableOrigin(CharacterCareerQualityInput input)
    {
        if (input.Operation == CharacterCareerQualityOperation.AcquireLevel)
        {
            return true;
        }

        CharacterCareerQualityInstance? selected = input.MatchingInstances
            .FirstOrDefault(instance => instance.Identity == input.Identity);
        if (selected is null)
        {
            return false;
        }
        if (selected.Origin is CharacterCareerQualityOrigin.Metatype
            or CharacterCareerQualityOrigin.Improvement
            or CharacterCareerQualityOrigin.QualityLevelImprovement)
        {
            return false;
        }

        return true;
    }

    private static void CalculateTransaction(
        CharacterCareerQualityInput input,
        int levelBefore,
        out int ruleKarmaCost,
        out int karmaDelta,
        out bool createsExpense,
        out bool expenseRefund)
        => CalculateTransaction(
            input.Operation, input.Definition, input.Settings,
            input.GmFreeCostApproved, input.HasMentorSpiritWay,
            input.MetagenicLimit, levelBefore, out ruleKarmaCost,
            out karmaDelta, out createsExpense, out expenseRefund);

    private static void CalculateTransaction(
        CharacterCareerQualityOperation operation,
        CharacterCareerQualityDefinition definition,
        CharacterCareerQualitySettings settings,
        bool gmFreeCostApproved,
        bool hasMentorSpiritWay,
        int metagenicLimit,
        int levelBefore,
        out int ruleKarmaCost,
        out int karmaDelta,
        out bool createsExpense,
        out bool expenseRefund)
    {
        bool free = gmFreeCostApproved
            || definition.Metagenic && metagenicLimit > 0
            || definition.Name == "Mentor Spirit" && hasMentorSpiritWay;
        int qualityKarma = free ? 0 : definition.BaseKarma;
        if (!free
            && definition.CostDiscountDefined
            && definition.CostDiscountRequirementsMet)
        {
            qualityKarma = definition.Type == CharacterCareerQualityType.Positive
                ? checked(qualityKarma + definition.CostDiscountValue)
                : checked(qualityKarma - definition.CostDiscountValue);
        }

        if (operation == CharacterCareerQualityOperation.AcquireLevel)
        {
            ruleKarmaCost = checked(qualityKarma * settings.KarmaQuality);
            if (!settings.DontDoubleQualityPurchases
                && definition.DoubleCostCareer)
            {
                ruleKarmaCost = checked(ruleKarmaCost * 2);
            }

            if (definition.Type == CharacterCareerQualityType.Positive)
            {
                createsExpense = definition.ContributeToBp;
                karmaDelta = createsExpense ? checked(-ruleKarmaCost) : 0;
                expenseRefund = false;
            }
            else
            {
                createsExpense = true;
                karmaDelta = 0;
                expenseRefund = false;
            }

            return;
        }

        if (definition.Type == CharacterCareerQualityType.Positive)
        {
            if (definition.RefundKarmaOnRemove)
            {
                int refund = checked(definition.BaseKarma
                    * settings.KarmaQuality);
                if (!settings.DontDoubleQualityPurchases
                    && definition.DoubleCostCareer)
                {
                    refund = checked(refund * 2);
                }

                ruleKarmaCost = checked(-refund);
                karmaDelta = refund;
                createsExpense = true;
                expenseRefund = true;
            }
            else
            {
                ruleKarmaCost = 0;
                karmaDelta = 0;
                createsExpense = false;
                expenseRefund = false;
            }

            return;
        }

        int buyoff = checked(-definition.BaseKarma
            * settings.KarmaQuality);
        if (!settings.DontDoubleQualityRefunds)
        {
            buyoff = checked(buyoff * 2);
        }

        if (operation == CharacterCareerQualityOperation.RemoveAllLevels)
        {
            buyoff = checked(buyoff * levelBefore);
        }

        ruleKarmaCost = buyoff;
        karmaDelta = checked(-buyoff);
        createsExpense = true;
        expenseRefund = false;
    }

    private static Guid[] AffectedInternalIds(
        CharacterCareerQualityInput input)
        => input.Operation switch
        {
            CharacterCareerQualityOperation.AcquireLevel =>
                [input.Identity.InternalId],
            CharacterCareerQualityOperation.RemoveLevel =>
                [input.Identity.InternalId],
            CharacterCareerQualityOperation.RemoveAllLevels => input.MatchingInstances
                .Select(static instance => instance.Identity.InternalId)
                .OrderBy(static id => id)
                .ToArray(),
            _ => []
        };

    private static bool TargetIdentityIsExact(CharacterCareerQualityInput input)
    {
        bool selectedExists = input.MatchingInstances.Any(instance =>
            instance.Identity == input.Identity);
        return input.Operation == CharacterCareerQualityOperation.AcquireLevel
            ? input.ProposedInternalIdUnused && !selectedExists
            : input.TargetOwnedByCharacter && selectedExists;
    }

    private static CharacterCareerQualityInstance[] CreatePostInstances(
        CharacterCareerQualityQuote quote)
    {
        List<CharacterCareerQualityInstance> result =
            CopyInstances(quote.InstancesBefore).ToList();
        switch (quote.Operation)
        {
            case CharacterCareerQualityOperation.AcquireLevel:
                result.Add(new CharacterCareerQualityInstance(
                    quote.Identity, quote.Extra, quote.SourceName,
                    quote.Definition.Type, CharacterCareerQualityOrigin.Selected));
                break;
            case CharacterCareerQualityOperation.RemoveLevel:
                result.RemoveAll(instance => instance.Identity == quote.Identity);
                break;
            case CharacterCareerQualityOperation.RemoveAllLevels:
                result.Clear();
                break;
        }

        return result.OrderBy(static instance => instance.Identity.InternalId).ToArray();
    }

    private static bool PostInstancesMatchPlan(CharacterCareerQualityPlan plan)
        => InstanceSequencesEqual(plan.InstancesAfter,
            CreatePostInstances(new CharacterCareerQualityQuote(
                plan.Operation, plan.Identity, plan.Definition, plan.Extra,
                plan.SourceName, plan.InstancesBefore.Count,
                plan.InstancesAfter.Count, plan.InstancesBefore,
                plan.AffectedInternalIds, true, 0, 0, 0, plan.CreatesExpense,
                plan.ExpenseRefund, plan.ExpenseReason, plan.KarmaUndoType,
                plan.UndoObjectId, plan.UndoExtra, TimeSpan.Zero,
                CharacterCareerQualityTimeAuthority.ImmediateChummerPersistence,
                [], true, CharacterCareerQualityBlocker.None,
                UnavailableAuthorityProjection(),
                new CharacterCareerQualityExecutionBinding(
                    plan.OwnerId, plan.WorkspaceId,
                    plan.ExpectedWorkspaceRevision, plan.ExpectedSavedRevision,
                    plan.ExpectedRuntimeFingerprint, plan.ExpectedContentDigest),
                plan.ExpectedSourceRevision, plan.ExpectedRuleDigest,
                plan.ExpectedLogicalRevision)));

    private static bool PlanOperationSemanticsMatch(
        CharacterCareerQualityPlan plan)
    {
        if (plan.Operation == CharacterCareerQualityOperation.AcquireLevel)
        {
            if (plan.KarmaUndoType != "AddQuality"
                || plan.UndoObjectId != plan.Identity.InternalId.ToString("D")
                || plan.UndoExtra != string.Empty
                || plan.ExpenseRefund)
            {
                return false;
            }

            return plan.Definition.Type == CharacterCareerQualityType.Negative
                ? plan.CreatesExpense && plan.ExpenseAmount == 0
                : plan.CreatesExpense == plan.Definition.ContributeToBp
                    && (plan.CreatesExpense || plan.ExpenseAmount == 0);
        }

        if (plan.KarmaUndoType != "RemoveQuality"
            || plan.UndoObjectId != plan.Identity.SourceId.ToString("D")
            || plan.UndoExtra != plan.Extra)
        {
            return false;
        }

        if (plan.Definition.Type == CharacterCareerQualityType.Negative)
        {
            return plan.CreatesExpense
                && !plan.ExpenseRefund
                && plan.ExpenseAmount <= 0;
        }

        return plan.CreatesExpense == plan.Definition.RefundKarmaOnRemove
            && plan.ExpenseRefund == plan.Definition.RefundKarmaOnRemove
            && (plan.CreatesExpense
                ? plan.ExpenseAmount >= 0
                : plan.ExpenseAmount == 0);
    }

    private static bool QuoteOperationSemanticsMatch(
        CharacterCareerQualityQuote quote)
    {
        if (quote.Operation == CharacterCareerQualityOperation.AcquireLevel)
        {
            if (quote.KarmaUndoType != "AddQuality"
                || quote.UndoObjectId != quote.Identity.InternalId.ToString("D")
                || quote.UndoExtra != string.Empty
                || quote.ExpenseRefund)
            {
                return false;
            }

            return quote.Definition.Type == CharacterCareerQualityType.Negative
                ? quote.CreatesExpense && quote.CharacterKarmaDelta == 0
                : quote.CreatesExpense == quote.Definition.ContributeToBp
                    && (quote.CreatesExpense
                        || quote.CharacterKarmaDelta == 0);
        }

        if (quote.KarmaUndoType != "RemoveQuality"
            || quote.UndoObjectId != quote.Identity.SourceId.ToString("D")
            || quote.UndoExtra != quote.Extra)
        {
            return false;
        }

        return quote.Definition.Type == CharacterCareerQualityType.Negative
            ? quote.CreatesExpense
                && !quote.ExpenseRefund
                && quote.CharacterKarmaDelta <= 0
            : quote.CreatesExpense == quote.Definition.RefundKarmaOnRemove
                && quote.ExpenseRefund
                    == quote.Definition.RefundKarmaOnRemove
                && quote.CharacterKarmaDelta >= 0;
    }

    private static bool ReceiptOperationSemanticsMatch(
        CharacterCareerQualityReceipt receipt)
    {
        if (!receipt.CreatesExpense
            && (receipt.ExpenseId != Guid.Empty
                || receipt.ExpenseAmount != 0
                || receipt.ExpenseRefund))
        {
            return false;
        }

        if (receipt.Operation == CharacterCareerQualityOperation.AcquireLevel)
        {
            return receipt.Definition.Type == CharacterCareerQualityType.Negative
                ? receipt.CreatesExpense
                    && receipt.ExpenseAmount == 0
                    && !receipt.ExpenseRefund
                : receipt.CreatesExpense == receipt.Definition.ContributeToBp
                    && !receipt.ExpenseRefund;
        }

        return receipt.Definition.Type == CharacterCareerQualityType.Negative
            ? receipt.CreatesExpense
                && receipt.ExpenseAmount <= 0
                && !receipt.ExpenseRefund
            : receipt.CreatesExpense == receipt.Definition.RefundKarmaOnRemove
                && receipt.ExpenseRefund
                    == receipt.Definition.RefundKarmaOnRemove
                && receipt.ExpenseAmount >= 0;
    }

    private static bool ReceiptInstancesMatchOperation(
        CharacterCareerQualityReceipt receipt)
    {
        List<CharacterCareerQualityInstance> expected =
            CopyInstances(receipt.InstancesBefore).ToList();
        switch (receipt.Operation)
        {
            case CharacterCareerQualityOperation.AcquireLevel:
                if (expected.Any(instance => instance.Identity == receipt.Identity))
                    return false;
                expected.Add(new CharacterCareerQualityInstance(
                    receipt.Identity, receipt.Extra, receipt.SourceName,
                    receipt.Definition.Type, CharacterCareerQualityOrigin.Selected));
                break;
            case CharacterCareerQualityOperation.RemoveLevel:
                if (expected.RemoveAll(instance => instance.Identity
                        == receipt.Identity) != 1)
                    return false;
                break;
            case CharacterCareerQualityOperation.RemoveAllLevels:
                expected.Clear();
                break;
            default:
                return false;
        }

        return InstanceSequencesEqual(expected, receipt.InstancesAfter);
    }

    private static bool CorrectionInverseShapeMatches(
        CharacterCareerQualityCorrectionPlan correction)
    {
        if (correction.RemoveExpense
            ? correction.ExpenseIdToRemove != correction.OriginalTransactionId
            : correction.ExpenseIdToRemove != Guid.Empty)
        {
            return false;
        }

        if (correction.RestoreInstances
            .Select(static value => value.Identity.InternalId)
            .Distinct().Count() != correction.RestoreInstances.Count
            || correction.RestoreInstances.Any(instance =>
                !IsValidIdentity(instance.Identity)
                || instance.Identity.SourceId != correction.Identity.SourceId
                || !Enum.IsDefined(instance.Type)
                || !Enum.IsDefined(instance.Origin)
                || !IsValidText(instance.Extra, true)
                || !IsValidText(instance.SourceName, true)))
        {
            return false;
        }

        return correction.OriginalOperation switch
        {
            CharacterCareerQualityOperation.AcquireLevel =>
                correction.RestoreInstances.Count == 0
                && correction.RemoveInternalIds.SequenceEqual(
                    [correction.Identity.InternalId]),
            CharacterCareerQualityOperation.RemoveLevel =>
                correction.RemoveInternalIds.Count == 0
                && correction.RestoreInstances.Count == 1
                && correction.RestoreInstances[0].Identity
                    == correction.Identity,
            CharacterCareerQualityOperation.RemoveAllLevels =>
                correction.RemoveInternalIds.Count == 0
                && correction.RestoreInstances.Count > 0
                && correction.RestoreInstances.Any(instance =>
                    instance.Identity == correction.Identity),
            _ => false
        };
    }

    private static bool AffectedIdsMatch(
        CharacterCareerQualityOperation operation,
        CharacterCareerQualityIdentity identity,
        IReadOnlyList<CharacterCareerQualityInstance> before,
        IReadOnlyList<Guid>? affected)
    {
        if (affected is null
            || affected.Any(static id => id == Guid.Empty)
            || affected.Distinct().Count() != affected.Count)
        {
            return false;
        }

        Guid[] expected = operation == CharacterCareerQualityOperation.RemoveAllLevels
            ? before.Select(static value => value.Identity.InternalId)
                .OrderBy(static id => id).ToArray()
            : [identity.InternalId];
        return affected.OrderBy(static id => id).SequenceEqual(expected);
    }

    private static bool PlanMatchesQuote(
        CharacterCareerQualityPlan plan,
        CharacterCareerQualityQuote quote)
        => plan.Operation == quote.Operation
            && plan.Identity == quote.Identity
            && plan.Definition == quote.Definition
            && plan.Extra == quote.Extra
            && plan.SourceName == quote.SourceName
            && InstanceSequencesEqual(plan.InstancesBefore, quote.InstancesBefore)
            && plan.AffectedInternalIds.SequenceEqual(quote.AffectedInternalIds)
            && plan.SavedCharacterKarma
                == quote.AvailableKarma + quote.CharacterKarmaDelta
            && plan.CreatesExpense == quote.CreatesExpense
            && plan.ExpenseAmount == quote.CharacterKarmaDelta
            && plan.ExpenseReason == quote.ExpenseReason
            && plan.ExpenseRefund == quote.ExpenseRefund
            && plan.KarmaUndoType == quote.KarmaUndoType
            && plan.UndoObjectId == quote.UndoObjectId
            && plan.UndoExtra == quote.UndoExtra
            && plan.OwnerId == quote.Binding.OwnerId
            && plan.WorkspaceId == quote.Binding.WorkspaceId
            && plan.ExpectedWorkspaceRevision == quote.Binding.WorkspaceRevision
            && plan.ExpectedSavedRevision == quote.Binding.SavedRevision
            && RevisionMatches(plan.ExpectedRuntimeFingerprint,
                quote.Binding.RuntimeFingerprint)
            && RevisionMatches(plan.ExpectedContentDigest,
                quote.Binding.ContentDigest)
            && RevisionMatches(plan.ExpectedSourceRevision, quote.SourceRevision)
            && RevisionMatches(plan.ExpectedRuleDigest, quote.RuleDigest)
            && RevisionMatches(plan.ExpectedLogicalRevision,
                quote.LogicalRevision);

    private static bool PostStateMatches(
        CharacterCareerQualityPlan plan,
        CharacterCareerQualityStateObservation post)
        => post.Identity == plan.Identity
            && post.Definition == plan.Definition
            && post.Extra == plan.Extra
            && post.SourceName == plan.SourceName
            && InstanceSequencesEqual(post.Instances, plan.InstancesAfter)
            && post.AvailableKarma == plan.SavedCharacterKarma
            && post.Binding.OwnerId == plan.OwnerId
            && post.Binding.WorkspaceId == plan.WorkspaceId
            && post.Binding.WorkspaceRevision == plan.TargetWorkspaceRevision
            && post.Binding.SavedRevision == plan.TargetSavedRevision
            && RevisionMatches(post.Binding.RuntimeFingerprint,
                plan.ExpectedRuntimeFingerprint)
            && RevisionMatches(post.Binding.ContentDigest,
                plan.ExpectedContentDigest)
            && RevisionMatches(post.SourceRevision, plan.ExpectedSourceRevision)
            && RevisionMatches(post.RuleDigest, plan.ExpectedRuleDigest);

    private static bool PostStateMatchesReceipt(
        CharacterCareerQualityReceipt receipt,
        CharacterCareerQualityStateObservation post)
        => post.Identity == receipt.Identity
            && post.Definition == receipt.Definition
            && post.Extra == receipt.Extra
            && post.SourceName == receipt.SourceName
            && InstanceSequencesEqual(post.Instances, receipt.InstancesAfter)
            && post.AvailableKarma == receipt.CharacterKarmaAfter
            && post.Binding.OwnerId == receipt.OwnerId
            && post.Binding.WorkspaceId == receipt.WorkspaceId
            && post.Binding.WorkspaceRevision == receipt.WorkspaceRevisionAfter
            && post.Binding.SavedRevision == receipt.SavedRevisionAfter
            && RevisionMatches(post.Binding.RuntimeFingerprint,
                receipt.RuntimeFingerprint)
            && RevisionMatches(post.Binding.ContentDigest, receipt.ContentDigest)
            && RevisionMatches(post.SourceRevision, receipt.SourceRevisionAfter)
            && RevisionMatches(post.RuleDigest, receipt.RuleDigestAfter)
            && RevisionMatches(post.StateDigest, receipt.StateDigestAfter);

    private static bool ExpenseMatchesPlan(
        CharacterCareerQualityExpenseObservation? expense,
        CharacterCareerQualityPlan plan)
    {
        if (!IsValidExpenseObservation(expense)) return false;
        if (!plan.CreatesExpense)
        {
            return expense!.MatchingEntryCount == 0;
        }

        return expense!.MatchingEntryCount == 1
            && expense.ExpenseId == plan.ExpenseId
            && expense.ExpenseDateLocal == plan.ExpenseDateLocal
            && expense.Amount == plan.ExpenseAmount
            && expense.Reason == plan.ExpenseReason
            && expense.ExpenseType == plan.ExpenseType
            && expense.Refund == plan.ExpenseRefund
            && expense.ForceCareerVisible == plan.ForceCareerVisible
            && expense.KarmaUndoType == plan.KarmaUndoType
            && expense.NuyenUndoType == plan.NuyenUndoType
            && expense.UndoObjectId == plan.UndoObjectId
            && expense.UndoQuantity == plan.UndoQuantity
            && expense.UndoExtra == plan.UndoExtra;
    }

    private static bool ExpenseMatchesReceipt(
        CharacterCareerQualityExpenseObservation? expense,
        CharacterCareerQualityReceipt receipt)
    {
        if (!IsValidExpenseObservation(expense)) return false;
        if (!receipt.CreatesExpense)
        {
            return expense!.MatchingEntryCount == 0
                && RevisionMatches(CalculateExpenseAuthorityDigest(expense),
                    receipt.ExpenseAuthorityDigest);
        }

        return expense!.MatchingEntryCount == 1
            && expense.ExpenseId == receipt.ExpenseId
            && expense.ExpenseDateLocal == receipt.ExpenseDateLocal
            && expense.Amount == receipt.ExpenseAmount
            && expense.Reason == receipt.ExpenseReason
            && expense.ExpenseType == "Karma"
            && expense.Refund == receipt.ExpenseRefund
            && !expense.ForceCareerVisible
            && expense.KarmaUndoType == (receipt.Operation
                == CharacterCareerQualityOperation.AcquireLevel
                    ? "AddQuality"
                    : "RemoveQuality")
            && expense.NuyenUndoType == "AddCyberware"
            && expense.UndoObjectId == (receipt.Operation
                == CharacterCareerQualityOperation.AcquireLevel
                    ? receipt.Identity.InternalId.ToString("D")
                    : receipt.Identity.SourceId.ToString("D"))
            && expense.UndoQuantity == 0m
            && expense.UndoExtra == (receipt.Operation
                == CharacterCareerQualityOperation.AcquireLevel
                    ? string.Empty
                    : receipt.Extra)
            && RevisionMatches(CalculateExpenseAuthorityDigest(expense),
                receipt.ExpenseAuthorityDigest);
    }

    private static bool IsValidExpenseObservation(
        CharacterCareerQualityExpenseObservation? expense)
    {
        if (expense is null || expense.MatchingEntryCount is < 0 or > 1)
            return false;
        if (expense.MatchingEntryCount == 0)
        {
            return expense.ExpenseId == Guid.Empty
                && expense.Amount == 0
                && expense.Reason == string.Empty
                && expense.ExpenseType == string.Empty
                && !expense.Refund
                && !expense.ForceCareerVisible
                && expense.KarmaUndoType == string.Empty
                && expense.NuyenUndoType == string.Empty
                && expense.UndoObjectId == string.Empty
                && expense.UndoQuantity == 0m
                && expense.UndoExtra == string.Empty;
        }

        return expense.ExpenseId != Guid.Empty
            && expense.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
            && expense.ExpenseDateLocal >= MinimumExpenseDate
            && expense.ExpenseDateLocal <= MaximumExpenseDate
            && expense.Amount is >= -MaximumKarma and <= MaximumKarma
            && IsValidReason(expense.Reason)
            && expense.ExpenseType == "Karma"
            && expense.KarmaUndoType is "AddQuality" or "RemoveQuality"
            && expense.NuyenUndoType == "AddCyberware"
            && IsValidText(expense.UndoObjectId, false)
            && IsValidText(expense.UndoExtra, true)
            && expense.UndoQuantity == 0m;
    }

    private static bool IsCoherentPrerequisites(
        IReadOnlyList<CharacterCareerQualityPrerequisiteResult>? values)
    {
        if (values is null
            || values.Count != Enum.GetValues<CharacterCareerQualityPrerequisite>().Length)
            return false;
        CharacterCareerQualityPrerequisiteResult?[] actual = values.ToArray();
        return !actual.Any(static value => value is null)
            && actual.Select(value => value!.Prerequisite)
                .SequenceEqual(Enum.GetValues<CharacterCareerQualityPrerequisite>())
            && actual.All(value => value is not null
                && Enum.IsDefined(value.Prerequisite)
                && IsValidText(value.Authority, false));
    }

    private static bool PrerequisitesMatchQuote(CharacterCareerQualityQuote quote)
    {
        CharacterCareerQualityPrerequisiteResult[] p = quote.Prerequisites.ToArray();
        CharacterCareerQualityInstance? selected = quote.InstancesBefore
            .FirstOrDefault(instance => instance.Identity == quote.Identity);
        bool targetExact = quote.TargetIdentityResolved;
        int levelLimit = quote.Definition.LevelLimit > 0
            ? quote.Definition.LevelLimit
            : 1;
        bool legalLevel = quote.Operation == CharacterCareerQualityOperation.AcquireLevel
            ? quote.Definition.NoLevels
                ? quote.LevelBefore == 0
                : quote.Definition.LimitIsUnlimited
                    ? quote.LevelBefore < MaximumRating
                    : quote.LevelBefore < levelLimit
            : quote.LevelBefore > 0;
        bool removableOrigin = quote.Operation == CharacterCareerQualityOperation.AcquireLevel
            || selected is not null
                && selected.Origin is not (
                    CharacterCareerQualityOrigin.Metatype
                    or CharacterCareerQualityOrigin.Improvement
                    or CharacterCareerQualityOrigin.QualityLevelImprovement);
        long resultingKarma = (long)quote.AvailableKarma
            + quote.CharacterKarmaDelta;
        bool sufficientKarma = resultingKarma
                is >= -MaximumKarma and <= MaximumKarma
            && (quote.Operation
                == CharacterCareerQualityOperation.AcquireLevel
            && quote.Definition.Type == CharacterCareerQualityType.Positive
            && quote.Definition.StagedPurchase
                ? true
                : quote.RuleKarmaCost < 0
                    || resultingKarma >= 0);
        bool requirementsSatisfied =
            quote.Authority.Eligibility.GeneralRequirementsMet
            && quote.Authority.Eligibility.RequiredOneOfQualityMet
            && quote.Authority.Eligibility.RequiredOneOfMetatypeMet
            && quote.Authority.Eligibility.RequiredAllQualitiesMet
            && quote.Authority.Eligibility.MissingRequirementIds.Count == 0;
        bool forbiddenClear =
            quote.Authority.Eligibility.ForbiddenQualitiesClear
            && quote.Authority.Eligibility.ConflictingQualityInternalIds.Count == 0;
        return p[0].Authority == "character.created"
            && p[0].Satisfied == quote.Authority.Created
            && p[1].Authority == "ruleset.sr5"
            && p[1].Satisfied == (quote.Authority.RulesetId == RulesetId)
            && p[2].Authority == $"quality.source-id:{quote.Definition.SourceId:D}"
            && p[2].Satisfied == quote.Authority.DefinitionProjectionIsExact
            && p[3].Authority == "quality.instances:exact"
            && p[3].Satisfied == quote.Authority.IdentityProjectionIsExact
            && p[4].Authority == $"quality.internal-id:{quote.Identity.InternalId:D}"
            && p[4].Satisfied == targetExact
            && p[5].Authority == "quality.source-enabled"
            && p[5].Satisfied == quote.Definition.SourceEnabled
            && p[6].Authority == "quality.implemented"
            && p[6].Satisfied == quote.Definition.Implemented
            && p[7].Authority == "quality.career-availability"
            && p[7].Satisfied == (!quote.Definition.ChargenOnly
                && !quote.Definition.OnlyPriorityGiven)
            && p[8].Authority == "campaign.quality-policy"
            && p[8].Satisfied == quote.Authority.GmAllows
            && p[9].Authority == quote.Authority.Eligibility.ProjectionDigest
            && p[9].Satisfied == quote.Authority.Eligibility.IsExact
            && p[10].Authority == "quality.required"
            && p[10].Satisfied == requirementsSatisfied
            && p[11].Authority == "quality.forbidden"
            && p[11].Satisfied == forbiddenClear
            && p[12].Authority == "quality.level-limit"
            && p[12].Satisfied == legalLevel
            && p[13].Authority == "quality.origin"
            && p[13].Satisfied == removableOrigin
            && p[14].Authority == "quality.costdiscount"
            && p[14].Satisfied == (!quote.Definition.CostDiscountDefined
                || quote.Definition.CostDiscountProjectionIsExact)
            && p[15].Authority == quote.Authority.Effects.DeltaDigest
            && p[15].Satisfied == quote.Authority.Effects.IsExact
            && p[16].Authority == "quality.effects:supported"
            && p[16].Satisfied
                == (quote.Authority.Effects.UnsupportedFamilies.Count == 0)
            && p[17].Authority == "character.karma"
            && p[17].Satisfied == sufficientKarma;
    }

    private static bool QuoteArithmeticMatches(CharacterCareerQualityQuote quote)
    {
        try
        {
            CalculateTransaction(
                quote.Operation, quote.Definition, quote.Authority.Settings,
                quote.Authority.GmFreeCostApproved,
                quote.Authority.HasMentorSpiritWay,
                quote.Authority.MetagenicLimit, quote.LevelBefore,
                out int ruleKarmaCost, out int karmaDelta,
                out bool createsExpense, out bool expenseRefund);
            return quote.RuleKarmaCost == ruleKarmaCost
                && quote.CharacterKarmaDelta == karmaDelta
                && quote.CreatesExpense == createsExpense
                && quote.ExpenseRefund == expenseRefund;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static int ExpectedLevelAfter(
        CharacterCareerQualityOperation operation, int before)
        => operation switch
        {
            CharacterCareerQualityOperation.AcquireLevel when before < MaximumRating => before + 1,
            CharacterCareerQualityOperation.RemoveLevel when before > 0 => before - 1,
            CharacterCareerQualityOperation.RemoveAllLevels when before > 0 => 0,
            _ => -1
        };

    private static string ExpenseReason(
        CharacterCareerQualityOperation operation,
        CharacterCareerQualityType type,
        string name)
        => operation switch
        {
            CharacterCareerQualityOperation.AcquireLevel
                when type == CharacterCareerQualityType.Positive => $"Quality {name}",
            CharacterCareerQualityOperation.AcquireLevel => $"Negative Quality {name}",
            _ when type == CharacterCareerQualityType.Positive => $"Remove Positive Quality {name}",
            _ => $"Remove Negative Quality {name}"
        };

    private static string CalculateRuleDigest(CharacterCareerQualityInput input)
        => Sha256(Canonical(
            ContractName, "rule", input.RulesetId,
            input.Created.ToString(CultureInfo.InvariantCulture),
            input.DefinitionProjectionIsExact.ToString(CultureInfo.InvariantCulture),
            input.IdentityProjectionIsExact.ToString(CultureInfo.InvariantCulture),
            input.ProposedInternalIdUnused.ToString(CultureInfo.InvariantCulture),
            input.TargetOwnedByCharacter.ToString(CultureInfo.InvariantCulture),
            input.GmAllows.ToString(CultureInfo.InvariantCulture),
            input.GmFreeCostApproved.ToString(CultureInfo.InvariantCulture),
            input.HasMentorSpiritWay.ToString(CultureInfo.InvariantCulture),
            input.MetagenicLimit.ToString(CultureInfo.InvariantCulture),
            DefinitionCanonical(input.Definition),
            input.Settings.KarmaQuality.ToString(CultureInfo.InvariantCulture),
            input.Settings.DontDoubleQualityPurchases.ToString(CultureInfo.InvariantCulture),
            input.Settings.DontDoubleQualityRefunds.ToString(CultureInfo.InvariantCulture),
            EligibilityCanonical(input.Eligibility), EffectsCanonical(input.Effects),
            input.RawRuleState));

    private static string CalculateLogicalRevision(
        CharacterCareerQualityOperation operation,
        CharacterCareerQualityIdentity identity,
        CharacterCareerQualityDefinition definition,
        string extra,
        string sourceName,
        int levelBefore,
        int levelAfter,
        IReadOnlyList<CharacterCareerQualityInstance> instances,
        IReadOnlyList<Guid> affectedIds,
        int availableKarma,
        int ruleKarmaCost,
        int karmaDelta,
        bool createsExpense,
        bool expenseRefund,
        string expenseReason,
        string karmaUndoType,
        string undoObjectId,
        string undoExtra,
        IReadOnlyList<CharacterCareerQualityPrerequisiteResult> prerequisites,
        bool canApply,
        CharacterCareerQualityBlocker blocker,
        CharacterCareerQualityAuthorityProjection authority,
        CharacterCareerQualityExecutionBinding binding,
        string sourceRevision,
        string ruleDigest)
        => Sha256(Canonical(
            ContractName, "logical", operation.ToString(),
            IdentityCanonical(identity), DefinitionCanonical(definition), extra,
            sourceName, levelBefore.ToString(CultureInfo.InvariantCulture),
            levelAfter.ToString(CultureInfo.InvariantCulture),
            InstancesCanonical(instances),
            Canonical(affectedIds.OrderBy(static id => id)
                .Select(static id => id.ToString("D")).ToArray()),
            availableKarma.ToString(CultureInfo.InvariantCulture),
            ruleKarmaCost.ToString(CultureInfo.InvariantCulture),
            karmaDelta.ToString(CultureInfo.InvariantCulture),
            createsExpense.ToString(CultureInfo.InvariantCulture),
            expenseRefund.ToString(CultureInfo.InvariantCulture), expenseReason,
            karmaUndoType, undoObjectId, undoExtra,
            Canonical(prerequisites.Select(value => Canonical(
                value.Prerequisite.ToString(),
                value.Satisfied.ToString(CultureInfo.InvariantCulture),
                value.Authority)).ToArray()),
            canApply.ToString(CultureInfo.InvariantCulture), blocker.ToString(),
            AuthorityCanonical(authority), BindingCanonical(binding),
            sourceRevision, ruleDigest));

    private static string CalculateExpenseAuthorityDigest(
        CharacterCareerQualityExpenseObservation expense)
        => Sha256(Canonical(
            ContractName, "expense",
            expense.MatchingEntryCount.ToString(CultureInfo.InvariantCulture),
            expense.ExpenseId.ToString("D"),
            expense.ExpenseDateLocal.ToString("O", CultureInfo.InvariantCulture),
            expense.Amount.ToString(CultureInfo.InvariantCulture), expense.Reason,
            expense.ExpenseType,
            expense.Refund.ToString(CultureInfo.InvariantCulture),
            expense.ForceCareerVisible.ToString(CultureInfo.InvariantCulture),
            expense.KarmaUndoType, expense.NuyenUndoType,
            expense.UndoObjectId,
            expense.UndoQuantity.ToString(CultureInfo.InvariantCulture),
            expense.UndoExtra));

    private static string CalculateStateDigest(
        CharacterCareerQualityIdentity identity,
        CharacterCareerQualityDefinition definition,
        string extra,
        string sourceName,
        IReadOnlyList<CharacterCareerQualityInstance> instances,
        int availableKarma,
        CharacterCareerQualityExecutionBinding binding,
        string sourceRevision,
        string ruleDigest)
        => Sha256(Canonical(
            ContractName, "state", IdentityCanonical(identity),
            DefinitionCanonical(definition), extra, sourceName,
            InstancesCanonical(instances),
            availableKarma.ToString(CultureInfo.InvariantCulture),
            BindingCanonical(binding), sourceRevision, ruleDigest));

    private static string CalculateReceiptDigest(
        Guid transactionId,
        CharacterCareerQualityPlan plan,
        CharacterCareerQualityQuote before,
        CharacterCareerQualityStateObservation after,
        string expenseDigest)
        => Sha256(Canonical(
            ContractName, "receipt", transactionId.ToString("D"),
            plan.Operation.ToString(), IdentityCanonical(plan.Identity),
            DefinitionCanonical(plan.Definition), plan.Extra, plan.SourceName,
            InstancesCanonical(plan.InstancesBefore),
            InstancesCanonical(plan.InstancesAfter),
            Canonical(plan.AffectedInternalIds.Select(static id => id.ToString("D")).ToArray()),
            before.AvailableKarma.ToString(CultureInfo.InvariantCulture),
            after.AvailableKarma.ToString(CultureInfo.InvariantCulture),
            plan.CreatesExpense.ToString(CultureInfo.InvariantCulture),
            plan.ExpenseId.ToString("D"),
            plan.ExpenseDateLocal.ToString("O", CultureInfo.InvariantCulture),
            plan.ExpenseAmount.ToString(CultureInfo.InvariantCulture),
            plan.ExpenseReason,
            plan.ExpenseRefund.ToString(CultureInfo.InvariantCulture),
            expenseDigest, plan.OwnerId, plan.WorkspaceId,
            plan.ExpectedWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            plan.TargetWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            plan.ExpectedSavedRevision.ToString(CultureInfo.InvariantCulture),
            plan.TargetSavedRevision.ToString(CultureInfo.InvariantCulture),
            plan.ExpectedRuntimeFingerprint, plan.ExpectedContentDigest,
            before.SourceRevision, before.RuleDigest, before.LogicalRevision,
            after.SourceRevision, after.RuleDigest, after.StateDigest));

    private static string CalculateReceiptDigest(CharacterCareerQualityReceipt value)
        => Sha256(Canonical(
            ContractName, "receipt", value.TransactionId.ToString("D"),
            value.Operation.ToString(), IdentityCanonical(value.Identity),
            DefinitionCanonical(value.Definition), value.Extra, value.SourceName,
            InstancesCanonical(value.InstancesBefore),
            InstancesCanonical(value.InstancesAfter),
            Canonical(value.AffectedInternalIds.Select(static id => id.ToString("D")).ToArray()),
            value.CharacterKarmaBefore.ToString(CultureInfo.InvariantCulture),
            value.CharacterKarmaAfter.ToString(CultureInfo.InvariantCulture),
            value.CreatesExpense.ToString(CultureInfo.InvariantCulture),
            value.ExpenseId.ToString("D"),
            value.ExpenseDateLocal.ToString("O", CultureInfo.InvariantCulture),
            value.ExpenseAmount.ToString(CultureInfo.InvariantCulture),
            value.ExpenseReason,
            value.ExpenseRefund.ToString(CultureInfo.InvariantCulture),
            value.ExpenseAuthorityDigest, value.OwnerId, value.WorkspaceId,
            value.WorkspaceRevisionBefore.ToString(CultureInfo.InvariantCulture),
            value.WorkspaceRevisionAfter.ToString(CultureInfo.InvariantCulture),
            value.SavedRevisionBefore.ToString(CultureInfo.InvariantCulture),
            value.SavedRevisionAfter.ToString(CultureInfo.InvariantCulture),
            value.RuntimeFingerprint, value.ContentDigest,
            value.SourceRevisionBefore, value.RuleDigestBefore,
            value.LogicalRevisionBefore, value.SourceRevisionAfter,
            value.RuleDigestAfter, value.StateDigestAfter));

    private static string CalculateCorrectionDigest(
        CharacterCareerQualityCorrectionPlan value)
        => CalculateCorrectionDigest(
            value.CorrectionId, value.OriginalTransactionId,
            value.OriginalOperation, value.Identity, value.RestoreInstances,
            value.RemoveInternalIds, value.SavedCharacterKarma,
            value.RemoveExpense, value.ExpenseIdToRemove, value.Reason,
            value.OwnerId, value.WorkspaceId, value.ExpectedWorkspaceRevision,
            value.TargetWorkspaceRevision, value.ExpectedSavedRevision,
            value.TargetSavedRevision, value.ExpectedRuntimeFingerprint,
            value.ExpectedContentDigest, value.ExpectedSourceRevision,
            value.ExpectedRuleDigest, value.ExpectedStateDigest,
            value.OriginalReceiptDigest);

    private static string CalculateCorrectionDigest(
        Guid correctionId,
        Guid originalTransactionId,
        CharacterCareerQualityOperation originalOperation,
        CharacterCareerQualityIdentity identity,
        IReadOnlyList<CharacterCareerQualityInstance> restoreInstances,
        IReadOnlyList<Guid> removeInternalIds,
        int savedCharacterKarma,
        bool removeExpense,
        Guid expenseIdToRemove,
        string reason,
        string ownerId,
        string workspaceId,
        long expectedWorkspaceRevision,
        long targetWorkspaceRevision,
        long expectedSavedRevision,
        long targetSavedRevision,
        string expectedRuntimeFingerprint,
        string expectedContentDigest,
        string expectedSourceRevision,
        string expectedRuleDigest,
        string expectedLogicalRevision,
        string originalReceiptDigest)
        => Sha256(Canonical(
            ContractName, "correction", correctionId.ToString("D"),
            originalTransactionId.ToString("D"), originalOperation.ToString(),
            IdentityCanonical(identity), InstancesCanonical(restoreInstances),
            Canonical(removeInternalIds.Select(static id => id.ToString("D")).ToArray()),
            savedCharacterKarma.ToString(CultureInfo.InvariantCulture),
            removeExpense.ToString(CultureInfo.InvariantCulture),
            expenseIdToRemove.ToString("D"), reason, ownerId, workspaceId,
            expectedWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            targetWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            expectedSavedRevision.ToString(CultureInfo.InvariantCulture),
            targetSavedRevision.ToString(CultureInfo.InvariantCulture),
            expectedRuntimeFingerprint, expectedContentDigest,
            expectedSourceRevision, expectedRuleDigest,
            expectedLogicalRevision, originalReceiptDigest));

    private static string DefinitionCanonical(CharacterCareerQualityDefinition value)
        => Canonical(
            value.SourceId.ToString("D"), value.Name, value.Type.ToString(),
            value.BaseKarma.ToString(CultureInfo.InvariantCulture),
            value.Implemented.ToString(CultureInfo.InvariantCulture),
            value.SourceEnabled.ToString(CultureInfo.InvariantCulture),
            value.CareerOnly.ToString(CultureInfo.InvariantCulture),
            value.ChargenOnly.ToString(CultureInfo.InvariantCulture),
            value.OnlyPriorityGiven.ToString(CultureInfo.InvariantCulture),
            value.DoubleCostCareer.ToString(CultureInfo.InvariantCulture),
            value.StagedPurchase.ToString(CultureInfo.InvariantCulture),
            value.RefundKarmaOnRemove.ToString(CultureInfo.InvariantCulture),
            value.NoLevels.ToString(CultureInfo.InvariantCulture),
            value.LimitIsUnlimited.ToString(CultureInfo.InvariantCulture),
            value.LevelLimit.ToString(CultureInfo.InvariantCulture),
            value.Metagenic.ToString(CultureInfo.InvariantCulture),
            value.ContributeToBp.ToString(CultureInfo.InvariantCulture),
            value.CostDiscountDefined.ToString(CultureInfo.InvariantCulture),
            value.CostDiscountProjectionIsExact.ToString(CultureInfo.InvariantCulture),
            value.CostDiscountRequirementsMet.ToString(CultureInfo.InvariantCulture),
            value.CostDiscountValue.ToString(CultureInfo.InvariantCulture));

    private static string EligibilityCanonical(
        CharacterCareerQualityEligibilityProjection value)
        => Canonical(
            value.IsExact.ToString(CultureInfo.InvariantCulture),
            value.GeneralRequirementsMet.ToString(CultureInfo.InvariantCulture),
            value.RequiredOneOfQualityMet.ToString(CultureInfo.InvariantCulture),
            value.RequiredOneOfMetatypeMet.ToString(CultureInfo.InvariantCulture),
            value.RequiredAllQualitiesMet.ToString(CultureInfo.InvariantCulture),
            value.ForbiddenQualitiesClear.ToString(CultureInfo.InvariantCulture),
            Canonical(value.ConflictingQualityInternalIds.OrderBy(static id => id)
                .Select(static id => id.ToString("D")).ToArray()),
            Canonical(value.MissingRequirementIds.OrderBy(static id => id,
                StringComparer.Ordinal).ToArray()), value.ProjectionDigest);

    private static string EffectsCanonical(CharacterCareerQualityEffectProjection value)
        => Canonical(
            value.IsExact.ToString(CultureInfo.InvariantCulture),
            Canonical(value.AppliedFamilies.OrderBy(static family => family)
                .Select(static family => family.ToString()).ToArray()),
            Canonical(value.UnsupportedFamilies.OrderBy(static family => family)
                .Select(static family => family.ToString()).ToArray()),
            value.MutationCount.ToString(CultureInfo.InvariantCulture),
            value.DeltaDigest);

    private static string BindingCanonical(CharacterCareerQualityExecutionBinding value)
        => Canonical(value.OwnerId, value.WorkspaceId,
            value.WorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            value.SavedRevision.ToString(CultureInfo.InvariantCulture),
            value.RuntimeFingerprint, value.ContentDigest);

    private static string AuthorityCanonical(
        CharacterCareerQualityAuthorityProjection value)
        => Canonical(
            value.Created.ToString(CultureInfo.InvariantCulture),
            value.RulesetId,
            value.DefinitionProjectionIsExact.ToString(CultureInfo.InvariantCulture),
            value.IdentityProjectionIsExact.ToString(CultureInfo.InvariantCulture),
            value.GmAllows.ToString(CultureInfo.InvariantCulture),
            value.GmFreeCostApproved.ToString(CultureInfo.InvariantCulture),
            value.HasMentorSpiritWay.ToString(CultureInfo.InvariantCulture),
            value.MetagenicLimit.ToString(CultureInfo.InvariantCulture),
            value.Settings.KarmaQuality.ToString(CultureInfo.InvariantCulture),
            value.Settings.DontDoubleQualityPurchases.ToString(CultureInfo.InvariantCulture),
            value.Settings.DontDoubleQualityRefunds.ToString(CultureInfo.InvariantCulture),
            EligibilityCanonical(value.Eligibility),
            EffectsCanonical(value.Effects));

    private static string IdentityCanonical(CharacterCareerQualityIdentity value)
        => Canonical(value.InternalId.ToString("D"), value.SourceId.ToString("D"));

    private static string InstancesCanonical(
        IEnumerable<CharacterCareerQualityInstance> values)
        => Canonical(values.OrderBy(static value => value.Identity.InternalId)
            .Select(value => Canonical(IdentityCanonical(value.Identity),
                value.Extra, value.SourceName, value.Type.ToString(),
                value.Origin.ToString())).ToArray());

    private static CharacterCareerQualityInstance[] CopyInstances(
        IEnumerable<CharacterCareerQualityInstance> values)
        => values.OrderBy(static value => value.Identity.InternalId).ToArray();

    private static bool InstanceSequencesEqual(
        IEnumerable<CharacterCareerQualityInstance> left,
        IEnumerable<CharacterCareerQualityInstance> right)
        => left.OrderBy(static value => value.Identity.InternalId)
            .SequenceEqual(right.OrderBy(static value => value.Identity.InternalId));

    private static bool IsValidIdentity(CharacterCareerQualityIdentity? value)
        => value is { InternalId: var internalId, SourceId: var sourceId }
            && internalId != Guid.Empty
            && sourceId != Guid.Empty;

    private static bool IsValidText(string? value, bool allowEmpty)
        => value is not null
            && value.Length <= MaximumTextLength
            && (allowEmpty || !string.IsNullOrWhiteSpace(value));

    private static bool IsValidReason(string? value)
        => value is not null
            && value.Length <= MaximumReasonLength
            && !string.IsNullOrWhiteSpace(value);

    private static bool IsNextRevision(long current, long next)
        => current is >= 0 and < MaximumRevision
            && next is > 0 and <= MaximumRevision
            && next == current + 1;

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

    private static string Canonical(params string[] values)
        => string.Concat(values.Select(value => string.Concat(
            value.Length.ToString(CultureInfo.InvariantCulture), ":", value)));

    private static CharacterCareerQualityQuote UnavailableQuote()
        => new(
            CharacterCareerQualityOperation.AcquireLevel,
            new CharacterCareerQualityIdentity(Guid.Empty, Guid.Empty),
            UnavailableDefinition(), string.Empty, string.Empty, 0, 0, [], [],
            false, 0, 0, 0, false, false, string.Empty, string.Empty, string.Empty,
            string.Empty, TimeSpan.Zero,
            CharacterCareerQualityTimeAuthority.ImmediateChummerPersistence,
            [], false, CharacterCareerQualityBlocker.InvalidDefinitionProjection,
            UnavailableAuthorityProjection(), UnavailableBinding(), string.Empty,
            string.Empty, string.Empty);

    private static CharacterCareerQualityPlan UnavailablePlan()
        => new(
            Guid.Empty, CharacterCareerQualityOperation.AcquireLevel,
            new CharacterCareerQualityIdentity(Guid.Empty, Guid.Empty),
            UnavailableDefinition(), string.Empty, string.Empty, [], [], [], 0,
            false, Guid.Empty, DateTime.MinValue, 0, string.Empty, false,
            string.Empty, false, string.Empty, string.Empty, string.Empty, 0m,
            string.Empty, string.Empty, string.Empty, 0, 0, 0, 0,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    private static CharacterCareerQualityStateObservation UnavailableStateObservation()
        => new(
            new CharacterCareerQualityIdentity(Guid.Empty, Guid.Empty),
            UnavailableDefinition(), string.Empty, string.Empty, [], 0,
            UnavailableBinding(), string.Empty, string.Empty, string.Empty);

    private static CharacterCareerQualityReceipt UnavailableReceipt()
        => new(
            Guid.Empty, CharacterCareerQualityOperation.AcquireLevel,
            new CharacterCareerQualityIdentity(Guid.Empty, Guid.Empty),
            UnavailableDefinition(), string.Empty, string.Empty, [], [], [], 0,
            0, false, Guid.Empty, DateTime.MinValue, 0, string.Empty, false,
            string.Empty, string.Empty, string.Empty, 0, 0, 0, 0, string.Empty,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty);

    private static CharacterCareerQualityCorrectionPlan UnavailableCorrection()
        => new(
            Guid.Empty, Guid.Empty, CharacterCareerQualityOperation.AcquireLevel,
            new CharacterCareerQualityIdentity(Guid.Empty, Guid.Empty), [], [],
            0, false, Guid.Empty, string.Empty, string.Empty, string.Empty, 0, 0,
            0, 0, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty);

    private static CharacterCareerQualityDefinition UnavailableDefinition()
        => new(
            Guid.Empty, string.Empty, CharacterCareerQualityType.Positive, 0,
            false, false, false, false, false, false, false, false, true,
            false, 0, false, false, false, false, false, 0);

    private static CharacterCareerQualityAuthorityProjection
        UnavailableAuthorityProjection()
        => new(
            false, string.Empty, false, false, false, false, false, 0,
            new CharacterCareerQualitySettings(0, false, false),
            new CharacterCareerQualityEligibilityProjection(
                false, false, false, false, false, false, [], [], string.Empty),
            new CharacterCareerQualityEffectProjection(
                false, [], [], 0, string.Empty));

    private static CharacterCareerQualityExecutionBinding UnavailableBinding()
        => new(string.Empty, string.Empty, 0, 0, string.Empty, string.Empty);
}
