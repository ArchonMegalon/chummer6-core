using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationQualitiesSchemas
{
    public const string AuthorityV1 = "chummer.sr5.priority-creation-qualities-authority.v1";
    public const string StateV1 = "chummer.sr5.priority-creation-qualities-state.v1";
    public const string PreviewV1 = "chummer.sr5.priority-creation-qualities-preview.v1";
    public const string DraftV1 = "chummer.sr5.priority-creation-qualities-draft.v1";
    public const string PlanV1 = "chummer.sr5.priority-creation-qualities-draft-plan.v1";
    public const string ReceiptV1 = "chummer.sr5.priority-creation-qualities-draft-receipt.v1";
}

public static class CharacterCreationQualitiesBlockers
{
    public const string AuthorityUnavailable = "creation-qualities-authority-unavailable";
    public const string CreatedCharacter = "creation-qualities-created-character";
    public const string DraftInvalid = "creation-qualities-draft-invalid";
    public const string DuplicateSelection = "creation-qualities-duplicate-selection";
    public const string EligibilityUnresolved = "creation-qualities-eligibility-unresolved";
    public const string ExplicitConfirmationRequired = "creation-qualities-explicit-confirmation-required";
    public const string IdempotencyConflict = "creation-qualities-idempotency-conflict";
    public const string IdempotencyKeyInvalid = "creation-qualities-idempotency-key-invalid";
    public const string InvalidSelection = "creation-qualities-invalid-selection";
    public const string KarmaExceeded = "creation-qualities-karma-exceeded";
    public const string MetagenicImbalanced = "creation-qualities-metagenic-imbalanced";
    public const string MetagenicLimitExceeded = "creation-qualities-metagenic-limit-exceeded";
    public const string NegativeLimitExceeded = "creation-qualities-negative-limit-exceeded";
    public const string PositiveLimitExceeded = "creation-qualities-positive-limit-exceeded";
    public const string PrerequisiteDraftRequired = "creation-qualities-prerequisite-draft-required";
    public const string AttributesDraftRequired = "creation-qualities-attributes-draft-required";
    public const string PreviewChanged = "creation-qualities-preview-changed";
    public const string PersistenceAuthorityRequired = "creation-qualities-persistence-authority-required";
    public const string ReceiptLedgerInvalid = "creation-qualities-receipt-ledger-invalid";
    public const string RevisionConflict = "creation-qualities-revision-conflict";
    public const string UnsupportedBuildMethod = "creation-qualities-unsupported-build-method";
    public const string UnsupportedRuleset = "creation-qualities-unsupported-ruleset";
}

public enum CharacterCreationQualityType
{
    Positive,
    Negative
}

/// <summary>
/// One completely projected purchase choice. OptionId is the only value a renderer sends
/// back. Rating, cost, follow-up choice, legality and sources remain Core-owned facts.
/// </summary>
public sealed record CharacterCreationQualityCatalogOption(
    string OptionId,
    Guid SourceId,
    string SelectionKey,
    string Name,
    CharacterCreationQualityType Type,
    int Rating,
    int KarmaCost,
    int MaximumSelections,
    bool IsMetagenic,
    bool CountsAgainstQualityLimit,
    bool CountsAgainstKarma,
    bool IsFreeOrGranted,
    bool IsSelectable,
    bool EligibilityIsExact,
    string? DisableReasonKey,
    string? FollowUpChoiceId,
    string? FollowUpChoiceLabel,
    IReadOnlyList<string> SourceAnchorIds,
    string OptionDigest);

/// <summary>
/// A metatype, heritage, Life Module or other already-granted quality projection. The
/// granting authority decides whether it contributes to creation Karma and quality limits.
/// </summary>
public sealed record CharacterCreationGrantedQuality(
    string GrantId,
    Guid SourceId,
    string SelectionKey,
    string Name,
    CharacterCreationQualityType Type,
    int Rating,
    int KarmaCost,
    bool IsMetagenic,
    bool CountsAgainstQualityLimit,
    bool CountsAgainstKarma,
    string Origin,
    IReadOnlyList<string> SourceAnchorIds,
    string GrantDigest);

public sealed record CharacterCreationQualitiesAuthority(
    string Schema,
    string RulesetId,
    string SettingsProfileId,
    int QualityKarmaLimit,
    bool MayExceedPositiveQualityLimit,
    bool MayExceedNegativeQualityLimit,
    int MetagenicLimit,
    IReadOnlyList<CharacterCreationQualityCatalogOption> Options,
    IReadOnlyList<CharacterCreationGrantedQuality> GrantedQualities,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers,
    bool IsAuthoritative,
    string SourceDigest,
    string ProfileDigest,
    string GmPolicyDigest,
    string RuntimeDigest,
    string AuthorityDigest)
{
    public static CharacterCreationQualitiesAuthority Unavailable { get; } = new(
        CharacterCreationQualitiesSchemas.AuthorityV1,
        string.Empty,
        string.Empty,
        0,
        false,
        false,
        0,
        [],
        [],
        [],
        [CharacterCreationQualitiesBlockers.AuthorityUnavailable],
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);
}

public sealed record CharacterCreationQualitiesBinding(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string RawCharacterXmlDigest,
    string AuxiliaryStateDigest,
    long PrerequisiteDraftRevision,
    string PrerequisiteDraftDigest,
    long AttributesDraftRevision,
    string AttributesDraftDigest,
    string RulesetId,
    string BuildMethod,
    bool CharacterCreated,
    int CreationKarmaTotal,
    int CreationKarmaUsedBeforeQualities,
    string AuthorityDigest,
    string RuntimeDigest);

public sealed record CharacterCreationQualitiesInput(
    CharacterCreationQualitiesBinding Binding,
    CharacterCreationQualitiesAuthority Authority,
    IReadOnlyList<string> SelectedOptionIds);

public sealed record CharacterCreationQualitiesLoadRequest(CharacterWorkspaceId WorkspaceId);

public sealed record CharacterCreationQualitiesPreviewRequest(
    CharacterCreationQualitiesBinding Binding,
    IReadOnlyList<string> SelectedOptionIds);

public sealed record CharacterCreationQualitiesConfirmRequest(
    CharacterCreationQualitiesBinding Binding,
    IReadOnlyList<string> SelectedOptionIds,
    string PreviewDigest,
    string IdempotencyKey,
    Guid TransactionId,
    bool ExplicitlyConfirmed);

public sealed record CharacterCreationQualitySelection(
    string OptionId,
    Guid SourceId,
    string SelectionKey,
    string Name,
    CharacterCreationQualityType Type,
    int Rating,
    int KarmaCost,
    bool IsMetagenic,
    bool CountsAgainstQualityLimit,
    bool CountsAgainstKarma,
    bool IsFreeOrGranted,
    string? FollowUpChoiceId,
    string? FollowUpChoiceLabel,
    IReadOnlyList<string> SourceAnchorIds,
    string OptionDigest);

public sealed record CharacterCreationQualitiesBudget(
    int Total,
    int Used,
    int Remaining,
    bool MayExceed,
    IReadOnlyList<string> Blockers);

public sealed record CharacterCreationQualitiesPreview(
    string Schema,
    CharacterCreationQualitiesBinding Binding,
    string AuthorityDigest,
    IReadOnlyList<CharacterCreationQualitySelection> Selections,
    IReadOnlyList<CharacterCreationGrantedQuality> GrantedQualities,
    CharacterCreationQualitiesBudget PositiveQualityBudget,
    CharacterCreationQualitiesBudget NegativeQualityBudget,
    int MetagenicPositiveKarma,
    int MetagenicNegativeKarma,
    int KarmaRemaining,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers,
    bool RequiresExplicitConfirmation,
    bool CanConfirm,
    string PreviewDigest);

public sealed record CharacterCreationQualitiesDraft(
    string Schema,
    CharacterWorkspaceId WorkspaceId,
    long DraftRevision,
    long BaseContentRevision,
    string BaseRawCharacterXmlDigest,
    long PrerequisiteDraftRevision,
    string PrerequisiteDraftDigest,
    long AttributesDraftRevision,
    string AttributesDraftDigest,
    string AuthorityDigest,
    string RuntimeDigest,
    IReadOnlyList<string> SelectedOptionIds,
    IReadOnlyList<CharacterCreationQualitySelection> Selections,
    int PositiveKarmaUsed,
    int NegativeKarmaUsed,
    int KarmaRemaining,
    IReadOnlyList<string> SourceAnchorIds,
    bool CharacterEffectsApplied,
    string LastIdempotencyKeyDigest,
    string LastPreviewDigest,
    string LastCommandDigest,
    string DraftDigest);

public sealed record CharacterCreationQualitiesState(
    string Schema,
    CharacterCreationQualitiesBinding Binding,
    CharacterCreationQualitiesAuthority Authority,
    CharacterCreationPrerequisiteDraft? PrerequisiteDraft,
    CharacterCreationAttributesDraft? AttributesDraft,
    CharacterCreationQualitiesDraft? PendingDraft,
    CharacterCreationQualitiesPreview Preview,
    IReadOnlyList<string> Blockers,
    bool CanEdit,
    string SnapshotDigest);

/// <summary>
/// A confirmed plan persists creation-draft state only. It must not write the canonical
/// character document or mark the character Created; finalization owns those mutations.
/// </summary>
public sealed record CharacterCreationQualitiesDraftPlan(
    string Schema,
    Guid TransactionId,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    long TargetContentRevision,
    long ExpectedSavedRevision,
    long TargetSavedRevision,
    string ExpectedRawCharacterXmlDigest,
    string ExpectedAuxiliaryStateDigest,
    string AuthorityDigest,
    string RuntimeDigest,
    string PreviewDigest,
    string IdempotencyKeyDigest,
    string CommandDigest,
    IReadOnlyList<CharacterCreationQualitySelection> Selections,
    int PositiveKarmaUsed,
    int NegativeKarmaUsed,
    int KarmaRemaining,
    bool CharacterDocumentChanged,
    string PlanDigest);

public sealed record CharacterCreationQualitiesDraftReceipt(
    string Schema,
    Guid TransactionId,
    CharacterWorkspaceId WorkspaceId,
    long PreviousContentRevision,
    long ContentRevision,
    long PreviousSavedRevision,
    long SavedRevision,
    string AuthorityDigest,
    string RuntimeDigest,
    string PreviewDigest,
    string IdempotencyKeyDigest,
    string CommandDigest,
    string PlanDigest,
    string DraftDigest,
    string PreviousReceiptDigest,
    bool CharacterDocumentChanged,
    string ReceiptDigest);

/// <summary>
/// Deterministic SR5 Priority creation-quality validation. Source parsing, requirement
/// evaluation and GM policy projection happen before this boundary; clients cannot submit
/// costs, ratings, free flags, labels or rule answers.
/// </summary>
public static class CharacterCreationQualitiesRules
{
    public static string ReceiptLedgerRootDigest { get; } =
        CharacterCreationQualitiesDigest.ComputeUtf8(
            "chummer.sr5.priority-creation-qualities-receipt-ledger.root.v1");

    public static CharacterCreationQualitiesPreview Evaluate(CharacterCreationQualitiesInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        List<string> blockers = ValidateEnvelope(input.Binding, input.Authority);
        var selected = new List<CharacterCreationQualitySelection>();
        string[] optionIds;
        if (input.SelectedOptionIds is { Count: > 65_536 })
        {
            blockers.Add(CharacterCreationQualitiesBlockers.InvalidSelection);
            optionIds = [];
        }
        else
        {
            optionIds = input.SelectedOptionIds?.ToArray() ?? [];
        }
        if (optionIds.Any(string.IsNullOrWhiteSpace)
            || optionIds.Distinct(StringComparer.Ordinal).Count() != optionIds.Length)
        {
            blockers.Add(CharacterCreationQualitiesBlockers.DuplicateSelection);
        }

        Dictionary<string, CharacterCreationQualityCatalogOption> catalog = input.Authority.Options
            .Where(static option => option is not null && !string.IsNullOrWhiteSpace(option.OptionId))
            .GroupBy(static option => option.OptionId, StringComparer.Ordinal)
            .Where(static group => group.Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.Single(), StringComparer.Ordinal);
        foreach (string optionId in optionIds.Distinct(StringComparer.Ordinal))
        {
            if (!catalog.TryGetValue(optionId, out CharacterCreationQualityCatalogOption? option)
                || !IsValidOption(option))
            {
                blockers.Add(CharacterCreationQualitiesBlockers.InvalidSelection);
                continue;
            }
            if (!option.IsSelectable)
            {
                blockers.Add(option.DisableReasonKey
                    ?? CharacterCreationQualitiesBlockers.InvalidSelection);
                continue;
            }
            if (!option.EligibilityIsExact)
            {
                blockers.Add(CharacterCreationQualitiesBlockers.EligibilityUnresolved);
                continue;
            }
            selected.Add(Project(option));
        }

        foreach (IGrouping<string, CharacterCreationQualitySelection> group in selected.GroupBy(
                     static item => item.SelectionKey,
                     StringComparer.Ordinal))
        {
            int maximum = catalog[group.First().OptionId].MaximumSelections;
            if (group.Count() > maximum)
                blockers.Add(CharacterCreationQualitiesBlockers.DuplicateSelection);
        }

        CostProjection[] all = selected.Select(ToCost)
            .Concat(input.Authority.GrantedQualities.Where(IsValidGrant).Select(ToCost))
            .ToArray();
        int positiveUsed = SafeSum(
            all.Where(static item => item.CountsAgainstQualityLimit && item.KarmaCost > 0)
                .Select(static item => item.KarmaCost),
            blockers);
        int negativeUsed = SafeNegate(SafeSum(
            all.Where(static item => item.CountsAgainstQualityLimit && item.KarmaCost < 0)
                .Select(static item => item.KarmaCost),
            blockers), blockers);
        int metagenicPositive = SafeSum(
            all.Where(static item => item.IsMetagenic && item.KarmaCost > 0)
                .Select(static item => item.KarmaCost),
            blockers);
        int metagenicNegative = SafeNegate(SafeSum(
            all.Where(static item => item.IsMetagenic && item.KarmaCost < 0)
                .Select(static item => item.KarmaCost),
            blockers), blockers);
        int karmaUsed = SafeSum(
            all.Where(static item => item.CountsAgainstKarma)
                .Select(static item => item.KarmaCost),
            blockers);
        int karmaRemaining;
        try
        {
            karmaRemaining = checked(
                input.Binding.CreationKarmaTotal
                - input.Binding.CreationKarmaUsedBeforeQualities
                - karmaUsed);
        }
        catch (OverflowException)
        {
            karmaRemaining = int.MinValue;
            blockers.Add(CharacterCreationQualitiesBlockers.KarmaExceeded);
        }

        if (positiveUsed > input.Authority.QualityKarmaLimit
            && !input.Authority.MayExceedPositiveQualityLimit)
            blockers.Add(CharacterCreationQualitiesBlockers.PositiveLimitExceeded);
        if (negativeUsed > input.Authority.QualityKarmaLimit
            && !input.Authority.MayExceedNegativeQualityLimit)
            blockers.Add(CharacterCreationQualitiesBlockers.NegativeLimitExceeded);
        if (karmaRemaining < 0)
            blockers.Add(CharacterCreationQualitiesBlockers.KarmaExceeded);
        if (metagenicPositive > input.Authority.MetagenicLimit
            || metagenicNegative > input.Authority.MetagenicLimit)
            blockers.Add(CharacterCreationQualitiesBlockers.MetagenicLimitExceeded);
        if ((metagenicPositive != 0 || metagenicNegative != 0)
            && metagenicNegative != metagenicPositive
            && metagenicNegative != metagenicPositive - 1)
            blockers.Add(CharacterCreationQualitiesBlockers.MetagenicImbalanced);

        string[] normalizedBlockers = blockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        CharacterCreationQualitySelection[] normalizedSelections = selected
            .OrderBy(static item => item.OptionId, StringComparer.Ordinal)
            .ToArray();
        CharacterCreationGrantedQuality[] normalizedGrants = input.Authority.GrantedQualities
            .OrderBy(static item => item.GrantId, StringComparer.Ordinal)
            .ToArray();
        string[] anchors = normalizedSelections.SelectMany(static item => item.SourceAnchorIds)
            .Concat(normalizedGrants.SelectMany(static item => item.SourceAnchorIds))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        var preview = new CharacterCreationQualitiesPreview(
            CharacterCreationQualitiesSchemas.PreviewV1,
            input.Binding,
            input.Authority.AuthorityDigest,
            normalizedSelections,
            normalizedGrants,
            Budget(input.Authority.QualityKarmaLimit, positiveUsed,
                input.Authority.MayExceedPositiveQualityLimit,
                CharacterCreationQualitiesBlockers.PositiveLimitExceeded),
            Budget(input.Authority.QualityKarmaLimit, negativeUsed,
                input.Authority.MayExceedNegativeQualityLimit,
                CharacterCreationQualitiesBlockers.NegativeLimitExceeded),
            metagenicPositive,
            metagenicNegative,
            karmaRemaining,
            anchors,
            normalizedBlockers,
            RequiresExplicitConfirmation: true,
            CanConfirm: normalizedBlockers.Length == 0,
            PreviewDigest: string.Empty);
        return preview with
        {
            PreviewDigest = CharacterCreationQualitiesDigest.Compute(
                preview with { PreviewDigest = string.Empty })
        };
    }

    public static bool TryPlan(
        CharacterCreationQualitiesPreview preview,
        string expectedPreviewDigest,
        string idempotencyKey,
        bool explicitlyConfirmed,
        bool transactionIdAlreadyExists,
        Guid transactionId,
        out CharacterCreationQualitiesDraftPlan plan)
    {
        ArgumentNullException.ThrowIfNull(preview);
        plan = null!;
        if (!explicitlyConfirmed
            || transactionId == Guid.Empty
            || transactionIdAlreadyExists
            || !preview.CanConfirm
            || preview.Blockers.Count != 0
            || string.IsNullOrWhiteSpace(idempotencyKey)
            || idempotencyKey.Length > 200
            || !string.Equals(idempotencyKey, idempotencyKey.Trim(), StringComparison.Ordinal)
            || !CharacterCreationQualitiesDigest.EqualsFixedTime(
                preview.PreviewDigest,
                expectedPreviewDigest)
            || !CharacterCreationQualitiesDigest.EqualsFixedTime(
                preview.PreviewDigest,
                CharacterCreationQualitiesDigest.Compute(
                    preview with { PreviewDigest = string.Empty })))
        {
            return false;
        }

        long targetContent;
        long targetSaved;
        try
        {
            targetContent = checked(preview.Binding.ContentRevision + 1);
            targetSaved = checked(preview.Binding.SavedRevision + 1);
        }
        catch (OverflowException)
        {
            return false;
        }
        string idempotencyKeyDigest = CharacterCreationQualitiesDigest.ComputeUtf8(idempotencyKey);
        string commandDigest = ComputeCommandDigest(preview);
        var candidate = new CharacterCreationQualitiesDraftPlan(
            CharacterCreationQualitiesSchemas.PlanV1,
            transactionId,
            preview.Binding.WorkspaceId,
            preview.Binding.ContentRevision,
            targetContent,
            preview.Binding.SavedRevision,
            targetSaved,
            preview.Binding.RawCharacterXmlDigest,
            preview.Binding.AuxiliaryStateDigest,
            preview.AuthorityDigest,
            preview.Binding.RuntimeDigest,
            preview.PreviewDigest,
            idempotencyKeyDigest,
            commandDigest,
            preview.Selections,
            preview.PositiveQualityBudget.Used,
            preview.NegativeQualityBudget.Used,
            preview.KarmaRemaining,
            CharacterDocumentChanged: false,
            PlanDigest: string.Empty);
        plan = candidate with
        {
            PlanDigest = CharacterCreationQualitiesDigest.Compute(
                candidate with { PlanDigest = string.Empty })
        };
        return true;
    }

    public static bool IsValidReceipt(
        CharacterCreationQualitiesDraftReceipt? receipt,
        CharacterCreationQualitiesDraftPlan plan,
        string observedDraftDigest)
        => receipt is not null
           && string.Equals(receipt.Schema, CharacterCreationQualitiesSchemas.ReceiptV1, StringComparison.Ordinal)
           && receipt.TransactionId == plan.TransactionId
           && receipt.WorkspaceId == plan.WorkspaceId
           && receipt.PreviousContentRevision == plan.ExpectedContentRevision
           && receipt.ContentRevision == plan.TargetContentRevision
           && receipt.PreviousSavedRevision == plan.ExpectedSavedRevision
           && receipt.SavedRevision == plan.TargetSavedRevision
           && !receipt.CharacterDocumentChanged
           && CharacterCreationQualitiesDigest.EqualsFixedTime(receipt.AuthorityDigest, plan.AuthorityDigest)
           && CharacterCreationQualitiesDigest.EqualsFixedTime(receipt.RuntimeDigest, plan.RuntimeDigest)
           && CharacterCreationQualitiesDigest.EqualsFixedTime(receipt.PreviewDigest, plan.PreviewDigest)
           && CharacterCreationQualitiesDigest.EqualsFixedTime(receipt.IdempotencyKeyDigest, plan.IdempotencyKeyDigest)
           && CharacterCreationQualitiesDigest.EqualsFixedTime(receipt.CommandDigest, plan.CommandDigest)
           && CharacterCreationQualitiesDigest.EqualsFixedTime(receipt.PlanDigest, plan.PlanDigest)
           && CharacterCreationQualitiesDigest.EqualsFixedTime(receipt.DraftDigest, observedDraftDigest)
           && CharacterCreationQualitiesDigest.IsCanonical(receipt.PreviousReceiptDigest)
           && CharacterCreationQualitiesDigest.EqualsFixedTime(
               receipt.ReceiptDigest,
               CharacterCreationQualitiesDigest.Compute(receipt with { ReceiptDigest = string.Empty }));

    public static string ComputeOptionDigest(CharacterCreationQualityCatalogOption option) =>
        CharacterCreationQualitiesDigest.Compute(option with { OptionDigest = string.Empty });

    public static string ComputeGrantDigest(CharacterCreationGrantedQuality grant) =>
        CharacterCreationQualitiesDigest.Compute(grant with { GrantDigest = string.Empty });

    public static string ComputeAuthorityDigest(CharacterCreationQualitiesAuthority authority) =>
        CharacterCreationQualitiesDigest.Compute(authority with { AuthorityDigest = string.Empty });

    public static string ComputeReceiptDigest(CharacterCreationQualitiesDraftReceipt receipt) =>
        CharacterCreationQualitiesDigest.Compute(receipt with { ReceiptDigest = string.Empty });

    public static string ComputeDraftDigest(CharacterCreationQualitiesDraft draft) =>
        CharacterCreationQualitiesDigest.Compute(draft with { DraftDigest = string.Empty });

    public static string ComputeStateDigest(CharacterCreationQualitiesState state) =>
        CharacterCreationQualitiesDigest.Compute(state with { SnapshotDigest = string.Empty });

    public static string ComputeIdempotencyKeyDigest(string idempotencyKey) =>
        CharacterCreationQualitiesDigest.ComputeUtf8(idempotencyKey);

    public static string ComputeCommandDigest(CharacterCreationQualitiesPreview preview) =>
        ComputeCommandDigest(
            preview.Binding,
            preview.Selections.Select(static item => item.OptionId).ToArray(),
            preview.PreviewDigest);

    public static string ComputeCommandDigest(
        CharacterCreationQualitiesBinding binding,
        IReadOnlyList<string> selectedOptionIds,
        string previewDigest) => CharacterCreationQualitiesDigest.Compute(new
        {
            Schema = "chummer.sr5.priority-creation-qualities-command.v1",
            Binding = binding,
            AuthorityDigest = binding.AuthorityDigest,
            SelectedOptionIds = (selectedOptionIds ?? [])
                .OrderBy(static item => item, StringComparer.Ordinal)
                .ToArray(),
            PreviewDigest = previewDigest
        });

    public static bool DigestsEqual(string? left, string? right) =>
        CharacterCreationQualitiesDigest.EqualsFixedTime(left, right);

    public static bool IsCanonicalDigest(string? value) =>
        CharacterCreationQualitiesDigest.IsCanonical(value);

    public static bool IsStructurallyValidReceipt(
        CharacterCreationQualitiesDraftReceipt? receipt,
        CharacterWorkspaceId workspaceId,
        long persistedContentRevision)
        => receipt is not null
           && string.Equals(receipt.Schema, CharacterCreationQualitiesSchemas.ReceiptV1, StringComparison.Ordinal)
           && receipt.TransactionId != Guid.Empty
           && receipt.WorkspaceId == workspaceId
           && receipt.PreviousContentRevision > 0
           && receipt.ContentRevision == receipt.PreviousContentRevision + 1
           && receipt.ContentRevision <= persistedContentRevision
           && receipt.PreviousSavedRevision > 0
           && receipt.SavedRevision == receipt.PreviousSavedRevision + 1
           && receipt.SavedRevision <= persistedContentRevision
           && !receipt.CharacterDocumentChanged
           && CharacterCreationQualitiesDigest.IsCanonical(receipt.AuthorityDigest)
           && CharacterCreationQualitiesDigest.IsCanonical(receipt.RuntimeDigest)
           && CharacterCreationQualitiesDigest.IsCanonical(receipt.PreviewDigest)
           && CharacterCreationQualitiesDigest.IsCanonical(receipt.IdempotencyKeyDigest)
           && CharacterCreationQualitiesDigest.IsCanonical(receipt.CommandDigest)
           && CharacterCreationQualitiesDigest.IsCanonical(receipt.PlanDigest)
           && CharacterCreationQualitiesDigest.IsCanonical(receipt.DraftDigest)
           && CharacterCreationQualitiesDigest.IsCanonical(receipt.PreviousReceiptDigest)
           && CharacterCreationQualitiesDigest.EqualsFixedTime(
               receipt.ReceiptDigest,
               ComputeReceiptDigest(receipt));

    private static List<string> ValidateEnvelope(
        CharacterCreationQualitiesBinding binding,
        CharacterCreationQualitiesAuthority authority)
    {
        var blockers = new List<string>();
        if (binding.CharacterCreated)
            blockers.Add(CharacterCreationQualitiesBlockers.CreatedCharacter);
        if (!string.Equals(binding.RulesetId, "sr5", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(authority.RulesetId, "sr5", StringComparison.OrdinalIgnoreCase))
            blockers.Add(CharacterCreationQualitiesBlockers.UnsupportedRuleset);
        if (!string.Equals(binding.BuildMethod, CharacterCreationBuildMethods.Priority, StringComparison.Ordinal))
            blockers.Add(CharacterCreationQualitiesBlockers.UnsupportedBuildMethod);
        if (string.IsNullOrWhiteSpace(binding.WorkspaceId.Value)
            || binding.ContentRevision <= 0
            || binding.SavedRevision <= 0
            || binding.ContentRevision != binding.SavedRevision
            || !CharacterCreationQualitiesDigest.IsCanonical(binding.RawCharacterXmlDigest)
            || !CharacterCreationQualitiesDigest.IsCanonical(binding.AuxiliaryStateDigest))
            blockers.Add(CharacterCreationQualitiesBlockers.RevisionConflict);
        if (binding.CreationKarmaTotal < 0
            || binding.CreationKarmaUsedBeforeQualities < 0
            || binding.CreationKarmaUsedBeforeQualities > binding.CreationKarmaTotal)
            blockers.Add(CharacterCreationQualitiesBlockers.KarmaExceeded);
        if (binding.PrerequisiteDraftRevision <= 0
            || !CharacterCreationQualitiesDigest.IsCanonical(binding.PrerequisiteDraftDigest))
            blockers.Add(CharacterCreationQualitiesBlockers.PrerequisiteDraftRequired);
        if (binding.AttributesDraftRevision <= 0
            || !CharacterCreationQualitiesDigest.IsCanonical(binding.AttributesDraftDigest))
            blockers.Add(CharacterCreationQualitiesBlockers.AttributesDraftRequired);
        if (!IsValidAuthority(authority)
            || !CharacterCreationQualitiesDigest.EqualsFixedTime(binding.AuthorityDigest, authority.AuthorityDigest)
            || !CharacterCreationQualitiesDigest.EqualsFixedTime(binding.RuntimeDigest, authority.RuntimeDigest))
            blockers.Add(CharacterCreationQualitiesBlockers.AuthorityUnavailable);
        return blockers;
    }

    private static bool IsValidAuthority(CharacterCreationQualitiesAuthority authority)
        => authority.IsAuthoritative
           && string.Equals(authority.Schema, CharacterCreationQualitiesSchemas.AuthorityV1, StringComparison.Ordinal)
           && string.Equals(authority.RulesetId, "sr5", StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(authority.SettingsProfileId)
           && authority.QualityKarmaLimit >= 0
           && authority.MetagenicLimit >= 0
           && CharacterCreationQualitiesDigest.IsCanonical(authority.SourceDigest)
           && CharacterCreationQualitiesDigest.IsCanonical(authority.ProfileDigest)
           && CharacterCreationQualitiesDigest.IsCanonical(authority.GmPolicyDigest)
           && CharacterCreationQualitiesDigest.IsCanonical(authority.RuntimeDigest)
           && authority.Blockers.Count == 0
           && authority.Options.Count <= 65_536
           && authority.GrantedQualities.Count <= 65_536
           && authority.SourceAnchorIds.Count > 0
           && authority.SourceAnchorIds.All(static item => !string.IsNullOrWhiteSpace(item))
           && authority.SourceAnchorIds.Distinct(StringComparer.Ordinal).Count()
              == authority.SourceAnchorIds.Count
           && authority.Options.All(IsValidOption)
           && authority.Options.Select(static item => item.OptionId)
               .Distinct(StringComparer.Ordinal).Count() == authority.Options.Count
           && authority.GrantedQualities.All(IsValidGrant)
           && authority.GrantedQualities.Select(static item => item.GrantId)
               .Distinct(StringComparer.Ordinal).Count() == authority.GrantedQualities.Count
           && CharacterCreationQualitiesDigest.EqualsFixedTime(
               authority.AuthorityDigest,
               ComputeAuthorityDigest(authority));

    private static bool IsValidOption(CharacterCreationQualityCatalogOption option)
        => !string.IsNullOrWhiteSpace(option.OptionId)
           && option.SourceId != Guid.Empty
           && !string.IsNullOrWhiteSpace(option.SelectionKey)
           && !string.IsNullOrWhiteSpace(option.Name)
           && option.Rating > 0
           && option.MaximumSelections > 0
           && option.SourceAnchorIds.All(static item => !string.IsNullOrWhiteSpace(item))
           && option.SourceAnchorIds.Distinct(StringComparer.Ordinal).Count()
              == option.SourceAnchorIds.Count
           && (option.Type == CharacterCreationQualityType.Positive
               ? option.KarmaCost >= 0
               : option.KarmaCost <= 0)
           && option.SourceAnchorIds.Count > 0
           && (option.EligibilityIsExact || !option.IsSelectable)
           && CharacterCreationQualitiesDigest.EqualsFixedTime(
               option.OptionDigest,
               ComputeOptionDigest(option));

    private static bool IsValidGrant(CharacterCreationGrantedQuality grant)
        => !string.IsNullOrWhiteSpace(grant.GrantId)
           && grant.SourceId != Guid.Empty
           && !string.IsNullOrWhiteSpace(grant.SelectionKey)
           && !string.IsNullOrWhiteSpace(grant.Name)
           && !string.IsNullOrWhiteSpace(grant.Origin)
           && grant.Rating > 0
           && grant.SourceAnchorIds.All(static item => !string.IsNullOrWhiteSpace(item))
           && grant.SourceAnchorIds.Distinct(StringComparer.Ordinal).Count()
              == grant.SourceAnchorIds.Count
           && (grant.Type == CharacterCreationQualityType.Positive
               ? grant.KarmaCost >= 0
               : grant.KarmaCost <= 0)
           && grant.SourceAnchorIds.Count > 0
           && CharacterCreationQualitiesDigest.EqualsFixedTime(
               grant.GrantDigest,
               ComputeGrantDigest(grant));

    private static CharacterCreationQualitySelection Project(CharacterCreationQualityCatalogOption option) => new(
        option.OptionId,
        option.SourceId,
        option.SelectionKey,
        option.Name,
        option.Type,
        option.Rating,
        option.KarmaCost,
        option.IsMetagenic,
        option.CountsAgainstQualityLimit,
        option.CountsAgainstKarma,
        option.IsFreeOrGranted,
        option.FollowUpChoiceId,
        option.FollowUpChoiceLabel,
        option.SourceAnchorIds,
        option.OptionDigest);

    private static CostProjection ToCost(CharacterCreationQualitySelection item) => new(
        item.KarmaCost,
        item.IsMetagenic,
        item.CountsAgainstQualityLimit,
        item.CountsAgainstKarma);

    private static CostProjection ToCost(CharacterCreationGrantedQuality item) => new(
        item.KarmaCost,
        item.IsMetagenic,
        item.CountsAgainstQualityLimit,
        item.CountsAgainstKarma);

    private static CharacterCreationQualitiesBudget Budget(
        int total,
        int used,
        bool mayExceed,
        string blocker) => new(
        total,
        used,
        total - used,
        mayExceed,
        used > total && !mayExceed ? [blocker] : []);

    private static int SafeSum(IEnumerable<int> values, List<string> blockers)
    {
        long sum = 0;
        foreach (int value in values)
        {
            sum += value;
            if (sum is > int.MaxValue or < int.MinValue)
            {
                blockers.Add(CharacterCreationQualitiesBlockers.AuthorityUnavailable);
                return sum > 0 ? int.MaxValue : int.MinValue;
            }
        }
        return (int)sum;
    }

    private static int SafeNegate(int value, List<string> blockers)
    {
        if (value != int.MinValue)
            return -value;
        blockers.Add(CharacterCreationQualitiesBlockers.AuthorityUnavailable);
        return int.MaxValue;
    }

    private readonly record struct CostProjection(
        int KarmaCost,
        bool IsMetagenic,
        bool CountsAgainstQualityLimit,
        bool CountsAgainstKarma);
}

internal static class CharacterCreationQualitiesDigest
{
    private const string Prefix = "sha256:";

    public static string Compute<T>(T value)
    {
        JsonElement root = JsonSerializer.SerializeToElement(value);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteCanonical(root, writer);
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    public static string ComputeUtf8(string value) => Prefix + Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    public static bool IsCanonical(string? value) => value is { Length: 71 }
        && value.StartsWith(Prefix, StringComparison.Ordinal)
        && value.AsSpan(Prefix.Length).ToArray().All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static bool EqualsFixedTime(string? left, string? right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject()
                             .OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException("Unsupported Qualities canonical JSON value kind.");
        }
    }
}
