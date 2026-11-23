using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerSkillKind
{
    Active,
    Knowledge
}

/// <summary>
/// Typed saved identity for an active or knowledge skill. Active skills always carry their
/// source-data GUID. Custom knowledge skills deliberately use a null source GUID.
/// </summary>
public sealed record CharacterCareerSkillIdentity(
    Guid SkillId,
    Guid? SourceSkillId,
    CharacterCareerSkillKind Kind);

public sealed record CharacterCareerSkillSpecializationSettings(
    int KarmaActiveSpecialization,
    int KarmaKnowledgeSpecialization,
    bool SpecializationsBreakSkillGroups);

public enum CharacterCareerSkillSpecializationModifierKind
{
    SkillCategorySpecializationKarmaCost,
    SkillCategorySpecializationKarmaCostMultiplier
}

public sealed record CharacterCareerSkillSpecializationModifier(
    string ModifierIdentity,
    CharacterCareerSkillSpecializationModifierKind Kind,
    string TargetSkillCategory,
    int MinimumRating,
    decimal Value);

public enum CharacterCareerSkillSpecializationOptionKind
{
    SourceCatalog,
    CombatWeapon,
    Improvement,
    Custom
}

/// <summary>
/// One deterministic specialization choice. Custom text is represented only by the selected
/// value with kind <see cref="CharacterCareerSkillSpecializationOptionKind.Custom"/>; it must
/// never be inserted into this authority list with a fabricated identity.
/// </summary>
public sealed record CharacterCareerSkillSpecializationOption(
    string OptionIdentity,
    string Name,
    CharacterCareerSkillSpecializationOptionKind Kind,
    string SourceAnchor);

public sealed record CharacterCareerSkillSpecializationSelection(
    string Name,
    CharacterCareerSkillSpecializationOptionKind Kind,
    string? OptionIdentity);

public sealed record CharacterCareerSkillSpecializationInput(
    CharacterCareerSkillIdentity Identity,
    bool Created,
    bool Enabled,
    bool IsExoticSkill,
    bool KarmaUnlocked,
    bool AllowUpgrade,
    bool IsNativeLanguage,
    string SkillName,
    string SkillCategory,
    string DictionaryKey,
    string SkillGroup,
    int TotalBaseRating,
    int ExistingSpecializationCount,
    int AvailableKarma,
    int EnabledSkillGroupMemberCount,
    bool SkillSpecializationsBlocked,
    bool SkillCategorySpecializationsBlocked,
    CharacterCareerSkillSpecializationSettings Settings,
    IReadOnlyList<CharacterCareerSkillSpecializationModifier> Modifiers,
    IReadOnlyList<CharacterCareerSkillSpecializationOption> AvailableOptions,
    CharacterCareerSkillSpecializationSelection Selection,
    string RawCharacterState,
    string RawSourceState,
    string RawRuleState)
{
    /// <summary>Validated specialization names on this saved skill in the current catalog revision.</summary>
    public IReadOnlyList<string> CurrentSpecializations { get; init; } = [];
}

public enum CharacterCareerSkillSpecializationBlocker
{
    None,
    NativeLanguage,
    UpgradeDisallowed,
    SkillDisabled,
    ExoticSkill,
    KarmaLocked,
    RatingRequired,
    SkillSpecializationsBlocked,
    SkillCategorySpecializationsBlocked,
    InsufficientKarma
}

public sealed record CharacterCareerSkillSpecializationQuote(
    CharacterCareerSkillIdentity Identity,
    CharacterCareerSkillSpecializationSelection Selection,
    string SkillName,
    string SkillCategory,
    string SkillGroup,
    bool Enabled,
    bool IsExoticSkill,
    bool KarmaUnlocked,
    bool AllowUpgrade,
    bool IsNativeLanguage,
    int TotalBaseRating,
    int ExistingSpecializationCount,
    int AvailableKarma,
    int EnabledSkillGroupMemberCount,
    bool SkillSpecializationsBlocked,
    bool SkillCategorySpecializationsBlocked,
    bool SpecializationsBreakSkillGroups,
    int KarmaCost,
    bool WillBreakSkillGroup,
    bool CanAdd,
    CharacterCareerSkillSpecializationBlocker Blocker,
    string CharacterRevision,
    string SourceRevision,
    string RuleDigest,
    string LogicalRevision);

public sealed record CharacterCareerSkillSpecializationPlan(
    CharacterCareerSkillIdentity Identity,
    Guid SpecializationId,
    string SpecializationName,
    bool SavedFree,
    bool SavedExpertise,
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
    bool WillBreakSkillGroup,
    string ExpectedCharacterRevision,
    string ExpectedSourceRevision,
    string ExpectedRuleDigest,
    string ExpectedLogicalRevision);

/// <summary>
/// Stack-scoped quote batch for repeated probes of one skill row. Retains at most two
/// validated raw strings (each at most MaximumRuleTextLength UTF-16 code units) and their
/// digests. Only the identical immutable string instance can reuse a raw-state digest.
/// One bounded, detached rule-input preparation also avoids sorting/encoding/hashing the
/// same rule binding for each selection at a rating. Every quote still validates its
/// complete input and evaluates its rules; this is not an authority or freshness cache.
/// Discard the batch at the end of the row, before any later evaluation.
/// </summary>
public ref struct CharacterCareerSkillSpecializationQuoteBatch
{
    private string? characterState;
    private string? characterRevision;
    private string? sourceState;
    private string? sourceRevision;
    private CharacterCareerSkillSpecializationRules.RuleDigestPreparation3261? rulePreparation;

    public bool TryCreateQuote(
        CharacterCareerSkillSpecializationInput? input,
        out CharacterCareerSkillSpecializationQuote quote)
        => CharacterCareerSkillSpecializationRules.TryCreateQuote(input, ref this, out quote);

    internal string GetCharacterRevision(string value)
        => GetRevision(value, ref characterState, ref characterRevision);

    internal string GetSourceRevision(string value)
        => GetRevision(value, ref sourceState, ref sourceRevision);

    internal string GetRuleDigest(CharacterCareerSkillSpecializationInput input)
        => CharacterCareerSkillSpecializationRules.PrepareRuleDigest3261(input, ref rulePreparation);

    private static string GetRevision(string value, ref string? state, ref string? revision)
    {
        if (!ReferenceEquals(value, state))
        {
            string computed = CharacterCareerSkillSpecializationRules.Sha256(value);
            state = value;
            revision = computed;
        }
        return revision!;
    }
}

/// <summary>
/// Deterministic Chummer5 Career authority for adding an active- or knowledge-skill
/// specialization. It preserves CanHaveSpecs eligibility, the separate settings costs,
/// category add/multiplier improvements, Chummer rounding, group-break projection, and the
/// exact AddSpecialization undo identity. Chummer5 imposes no career specialization-count
/// ceiling, so the existing count is revision-bound but never treated as a mechanical cap.
/// </summary>
public static class CharacterCareerSkillSpecializationRules
{
    public const int RevisionHexLength = 64;
    public const int MaximumRating = 1000;
    public const int MaximumKarma = 9_999_999;
    public const int MaximumSettingCost = 100;
    public const int MaximumNameLength = 512;
    public const int MaximumSourceAnchorLength = 2048;
    public const int MaximumRuleTextLength = 1_048_576;
    public static readonly DateTime MinimumExpenseDate = new(1753, 1, 1);
    public static readonly DateTime MaximumExpenseDate = new(9998, 12, 31, 23, 59, 59);

    public static bool TryCreateQuote(
        CharacterCareerSkillSpecializationInput? input,
        out CharacterCareerSkillSpecializationQuote quote)
    {
        var batch = new CharacterCareerSkillSpecializationQuoteBatch();
        // Independent one-shot quotes keep the original canonical preparation.
        return TryCreateQuote(input, ref batch, out quote, prepareRuleDigest: false);
    }

    internal static bool TryCreateQuote(
        CharacterCareerSkillSpecializationInput? input,
        ref CharacterCareerSkillSpecializationQuoteBatch batch,
        out CharacterCareerSkillSpecializationQuote quote,
        bool prepareRuleDigest = true)
    {
        quote = UnavailableQuote();
        if (!IsValidInput(input))
        {
            return false;
        }

        CharacterCareerSkillSpecializationInput validInput = input!;
        int karmaCost;
        try
        {
            karmaCost = CalculateKarmaCost(validInput);
        }
        catch (OverflowException)
        {
            return false;
        }
        if (karmaCost is < -MaximumKarma or > MaximumKarma)
        {
            return false;
        }

        CharacterCareerSkillSpecializationBlocker blocker = ExpectedBlocker(
            validInput.Identity.Kind,
            validInput.Enabled,
            validInput.IsExoticSkill,
            validInput.KarmaUnlocked,
            validInput.AllowUpgrade,
            validInput.IsNativeLanguage,
            validInput.TotalBaseRating,
            validInput.AvailableKarma,
            validInput.SkillSpecializationsBlocked,
            validInput.SkillCategorySpecializationsBlocked,
            karmaCost);
        bool canAdd = blocker == CharacterCareerSkillSpecializationBlocker.None;
        bool willBreakSkillGroup = WillBreakSkillGroup(
            validInput.Identity.Kind,
            validInput.SkillGroup,
            validInput.EnabledSkillGroupMemberCount,
            validInput.Settings.SpecializationsBreakSkillGroups);
        string characterRevision = batch.GetCharacterRevision(validInput.RawCharacterState);
        string sourceRevision = batch.GetSourceRevision(validInput.RawSourceState);
        string ruleDigest = prepareRuleDigest ? batch.GetRuleDigest(validInput) : CalculateRuleDigest(validInput);
        string logicalRevision = CalculateLogicalRevision(
            validInput.Identity,
            validInput.Selection,
            validInput.SkillName,
            validInput.SkillCategory,
            validInput.SkillGroup,
            validInput.Enabled,
            validInput.IsExoticSkill,
            validInput.KarmaUnlocked,
            validInput.AllowUpgrade,
            validInput.IsNativeLanguage,
            validInput.TotalBaseRating,
            validInput.ExistingSpecializationCount,
            validInput.AvailableKarma,
            validInput.EnabledSkillGroupMemberCount,
            validInput.SkillSpecializationsBlocked,
            validInput.SkillCategorySpecializationsBlocked,
            validInput.Settings.SpecializationsBreakSkillGroups,
            karmaCost,
            willBreakSkillGroup,
            canAdd,
            blocker,
            characterRevision,
            sourceRevision,
            ruleDigest);

        quote = new CharacterCareerSkillSpecializationQuote(
            validInput.Identity,
            validInput.Selection,
            validInput.SkillName,
            validInput.SkillCategory,
            validInput.SkillGroup,
            validInput.Enabled,
            validInput.IsExoticSkill,
            validInput.KarmaUnlocked,
            validInput.AllowUpgrade,
            validInput.IsNativeLanguage,
            validInput.TotalBaseRating,
            validInput.ExistingSpecializationCount,
            validInput.AvailableKarma,
            validInput.EnabledSkillGroupMemberCount,
            validInput.SkillSpecializationsBlocked,
            validInput.SkillCategorySpecializationsBlocked,
            validInput.Settings.SpecializationsBreakSkillGroups,
            karmaCost,
            willBreakSkillGroup,
            canAdd,
            blocker,
            characterRevision,
            sourceRevision,
            ruleDigest,
            logicalRevision);
        return true;
    }

    public static bool TryPlanAdd(
        CharacterCareerSkillSpecializationQuote? current,
        string? expectedCharacterRevision,
        string? expectedSourceRevision,
        string? expectedRuleDigest,
        string? expectedLogicalRevision,
        bool confirmed,
        Guid specializationId,
        Guid expenseId,
        DateTime expenseDateLocal,
        out CharacterCareerSkillSpecializationPlan plan)
    {
        plan = UnavailablePlan();
        DateTime normalizedDate = DateTime.SpecifyKind(expenseDateLocal, DateTimeKind.Unspecified);
        if (!confirmed
            || !IsCoherent(current)
            || !current!.CanAdd
            || !RevisionMatches(current.CharacterRevision, expectedCharacterRevision)
            || !RevisionMatches(current.SourceRevision, expectedSourceRevision)
            || !RevisionMatches(current.RuleDigest, expectedRuleDigest)
            || !RevisionMatches(current.LogicalRevision, expectedLogicalRevision)
            || specializationId == Guid.Empty
            || expenseId == Guid.Empty
            || specializationId == expenseId
            || specializationId == current.Identity.SkillId
            || specializationId == current.Identity.SourceSkillId
            || normalizedDate < MinimumExpenseDate
            || normalizedDate > MaximumExpenseDate)
        {
            return false;
        }

        try
        {
            int savedKarma = checked(current.AvailableKarma - current.KarmaCost);
            plan = new CharacterCareerSkillSpecializationPlan(
                current.Identity,
                specializationId,
                current.Selection.Name,
                SavedFree: false,
                SavedExpertise: false,
                savedKarma,
                checked(-current.KarmaCost),
                $"Learned Specialization {current.SkillName} ({current.Selection.Name})",
                normalizedDate,
                expenseId,
                "AddSpecialization",
                "AddCyberware",
                specializationId.ToString("D"),
                0m,
                string.Empty,
                current.WillBreakSkillGroup,
                current.CharacterRevision,
                current.SourceRevision,
                current.RuleDigest,
                current.LogicalRevision);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static bool IsCoherent(CharacterCareerSkillSpecializationQuote? quote)
        => quote is not null
            && IsValidIdentity(quote.Identity)
            && IsValidSelectionShape(quote.Selection)
            && IsBoundedRequiredText(quote.SkillName)
            && IsBoundedOptionalText(quote.SkillCategory)
            && IsBoundedOptionalText(quote.SkillGroup)
            && quote.TotalBaseRating is >= 0 and <= MaximumRating
            && quote.ExistingSpecializationCount >= 0
            && quote.AvailableKarma is >= 0 and <= MaximumKarma
            && quote.EnabledSkillGroupMemberCount >= 0
            && quote.KarmaCost is >= -MaximumKarma and <= MaximumKarma
            && quote.WillBreakSkillGroup == WillBreakSkillGroup(
                quote.Identity.Kind,
                quote.SkillGroup,
                quote.EnabledSkillGroupMemberCount,
                quote.SpecializationsBreakSkillGroups)
            && quote.CanAdd == (quote.Blocker == CharacterCareerSkillSpecializationBlocker.None)
            && quote.Blocker == ExpectedBlocker(
                quote.Identity.Kind,
                quote.Enabled,
                quote.IsExoticSkill,
                quote.KarmaUnlocked,
                quote.AllowUpgrade,
                quote.IsNativeLanguage,
                quote.TotalBaseRating,
                quote.AvailableKarma,
                quote.SkillSpecializationsBlocked,
                quote.SkillCategorySpecializationsBlocked,
                quote.KarmaCost)
            && IsLowerHexRevision(quote.CharacterRevision)
            && IsLowerHexRevision(quote.SourceRevision)
            && IsLowerHexRevision(quote.RuleDigest)
            && RevisionMatches(
                CalculateLogicalRevision(
                    quote.Identity,
                    quote.Selection,
                    quote.SkillName,
                    quote.SkillCategory,
                    quote.SkillGroup,
                    quote.Enabled,
                    quote.IsExoticSkill,
                    quote.KarmaUnlocked,
                    quote.AllowUpgrade,
                    quote.IsNativeLanguage,
                    quote.TotalBaseRating,
                    quote.ExistingSpecializationCount,
                    quote.AvailableKarma,
                    quote.EnabledSkillGroupMemberCount,
                    quote.SkillSpecializationsBlocked,
                    quote.SkillCategorySpecializationsBlocked,
                    quote.SpecializationsBreakSkillGroups,
                    quote.KarmaCost,
                    quote.WillBreakSkillGroup,
                    quote.CanAdd,
                    quote.Blocker,
                    quote.CharacterRevision,
                    quote.SourceRevision,
                    quote.RuleDigest),
                quote.LogicalRevision);

    private static CharacterCareerSkillSpecializationBlocker ExpectedBlocker(
        CharacterCareerSkillKind kind,
        bool enabled,
        bool isExotic,
        bool karmaUnlocked,
        bool allowUpgrade,
        bool isNativeLanguage,
        int rating,
        int availableKarma,
        bool skillBlocked,
        bool categoryBlocked,
        int karmaCost)
        => kind == CharacterCareerSkillKind.Knowledge && isNativeLanguage
            ? CharacterCareerSkillSpecializationBlocker.NativeLanguage
            : kind == CharacterCareerSkillKind.Knowledge && !allowUpgrade
                ? CharacterCareerSkillSpecializationBlocker.UpgradeDisallowed
                : !enabled
                    ? CharacterCareerSkillSpecializationBlocker.SkillDisabled
                    : isExotic
                        ? CharacterCareerSkillSpecializationBlocker.ExoticSkill
                        : !karmaUnlocked
                            ? CharacterCareerSkillSpecializationBlocker.KarmaLocked
                            : rating <= 0
                                ? CharacterCareerSkillSpecializationBlocker.RatingRequired
                                : skillBlocked
                                    ? CharacterCareerSkillSpecializationBlocker.SkillSpecializationsBlocked
                                    : categoryBlocked
                                        ? CharacterCareerSkillSpecializationBlocker.SkillCategorySpecializationsBlocked
                                        : availableKarma < karmaCost
                                            ? CharacterCareerSkillSpecializationBlocker.InsufficientKarma
                                            : CharacterCareerSkillSpecializationBlocker.None;

    private static bool IsValidInput(CharacterCareerSkillSpecializationInput? input)
    {
        if (input is null
            || !input.Created
            || !IsValidIdentity(input.Identity)
            || !IsBoundedRequiredText(input.SkillName)
            || !IsBoundedOptionalText(input.SkillCategory)
            || !IsBoundedRequiredText(input.DictionaryKey)
            || !IsBoundedOptionalText(input.SkillGroup)
            || input.TotalBaseRating is < 0 or > MaximumRating
            || input.ExistingSpecializationCount < 0
            || input.AvailableKarma is < 0 or > MaximumKarma
            || input.EnabledSkillGroupMemberCount < 0
            || !IsValidSettings(input.Settings)
            || input.Modifiers is null
            || input.AvailableOptions is null
            || !IsValidSelectionShape(input.Selection)
            || string.IsNullOrWhiteSpace(input.RawCharacterState)
            || input.RawCharacterState.Length > MaximumRuleTextLength
            || string.IsNullOrWhiteSpace(input.RawSourceState)
            || input.RawSourceState.Length > MaximumRuleTextLength
            || string.IsNullOrWhiteSpace(input.RawRuleState)
            || input.RawRuleState.Length > MaximumRuleTextLength
            || input.Identity.Kind == CharacterCareerSkillKind.Active
                && (input.IsNativeLanguage || !input.AllowUpgrade)
            || input.Identity.Kind == CharacterCareerSkillKind.Knowledge && input.IsExoticSkill
            || string.IsNullOrEmpty(input.SkillGroup) && input.EnabledSkillGroupMemberCount != 0)
        {
            return false;
        }

        CharacterCareerSkillSpecializationModifier[] modifiers = input.Modifiers.ToArray();
        CharacterCareerSkillSpecializationOption[] options = input.AvailableOptions.ToArray();
        if (modifiers.Select(static modifier => modifier.ModifierIdentity)
                .Distinct(StringComparer.Ordinal)
                .Count() != modifiers.Length
            || !modifiers.All(modifier => IsValidModifier(input, modifier))
            || options.Select(static option => option.OptionIdentity)
                .Distinct(StringComparer.Ordinal)
                .Count() != options.Length
            || !options.All(option => IsValidOption(input, option)))
        {
            return false;
        }

        if (input.Selection.Kind == CharacterCareerSkillSpecializationOptionKind.Custom)
        {
            return input.Selection.OptionIdentity is null;
        }

        return input.Selection.OptionIdentity is not null
            && options.Any(option =>
                string.Equals(option.OptionIdentity, input.Selection.OptionIdentity, StringComparison.Ordinal)
                && string.Equals(option.Name, input.Selection.Name, StringComparison.Ordinal)
                && option.Kind == input.Selection.Kind);
    }

    private static bool IsValidIdentity(CharacterCareerSkillIdentity? identity)
        => identity is { SkillId: var skillId, SourceSkillId: var sourceSkillId, Kind: var kind }
            && skillId != Guid.Empty
            && sourceSkillId != Guid.Empty
            && kind is CharacterCareerSkillKind.Active or CharacterCareerSkillKind.Knowledge
            && (kind == CharacterCareerSkillKind.Knowledge || sourceSkillId.HasValue);

    private static bool IsValidSettings(CharacterCareerSkillSpecializationSettings? settings)
        => settings is not null
            && settings.KarmaActiveSpecialization is >= 0 and <= MaximumSettingCost
            && settings.KarmaKnowledgeSpecialization is >= 0 and <= MaximumSettingCost;

    private static bool IsValidModifier(
        CharacterCareerSkillSpecializationInput input,
        CharacterCareerSkillSpecializationModifier modifier)
        => IsLowerHexRevision(modifier.ModifierIdentity)
            && modifier.Kind is CharacterCareerSkillSpecializationModifierKind.SkillCategorySpecializationKarmaCost
                or CharacterCareerSkillSpecializationModifierKind.SkillCategorySpecializationKarmaCostMultiplier
            && modifier.TargetSkillCategory is not null
            && modifier.TargetSkillCategory.Length <= MaximumNameLength
            && (string.IsNullOrEmpty(modifier.TargetSkillCategory)
                || string.Equals(modifier.TargetSkillCategory, input.SkillCategory, StringComparison.Ordinal))
            && modifier.MinimumRating is >= 0 and <= MaximumRating
            && modifier.Value is >= -MaximumKarma and <= MaximumKarma;

    private static bool IsValidOption(
        CharacterCareerSkillSpecializationInput input,
        CharacterCareerSkillSpecializationOption option)
        => IsLowerHexRevision(option.OptionIdentity)
            && option.Kind is CharacterCareerSkillSpecializationOptionKind.SourceCatalog
                or CharacterCareerSkillSpecializationOptionKind.CombatWeapon
                or CharacterCareerSkillSpecializationOptionKind.Improvement
            && IsBoundedRequiredText(option.Name)
            && !string.IsNullOrWhiteSpace(option.SourceAnchor)
            && option.SourceAnchor.Length <= MaximumSourceAnchorLength
            && option.Kind != CharacterCareerSkillSpecializationOptionKind.Custom
            && (option.Kind != CharacterCareerSkillSpecializationOptionKind.CombatWeapon
                || input.Identity.Kind == CharacterCareerSkillKind.Active
                    && string.Equals(input.SkillCategory, "Combat Active", StringComparison.Ordinal));

    private static bool IsValidSelectionShape(CharacterCareerSkillSpecializationSelection? selection)
        => selection is not null
            && IsBoundedRequiredText(selection.Name)
            && selection.Kind is CharacterCareerSkillSpecializationOptionKind.SourceCatalog
                or CharacterCareerSkillSpecializationOptionKind.CombatWeapon
                or CharacterCareerSkillSpecializationOptionKind.Improvement
                or CharacterCareerSkillSpecializationOptionKind.Custom
            && (selection.Kind == CharacterCareerSkillSpecializationOptionKind.Custom
                ? selection.OptionIdentity is null
                : IsLowerHexRevision(selection.OptionIdentity));

    private static int CalculateKarmaCost(CharacterCareerSkillSpecializationInput input)
    {
        int baseCost = input.Identity.Kind == CharacterCareerSkillKind.Knowledge
            ? input.Settings.KarmaKnowledgeSpecialization
            : input.Settings.KarmaActiveSpecialization;
        decimal extra = 0m;
        decimal multiplier = 1m;
        foreach (CharacterCareerSkillSpecializationModifier modifier in input.Modifiers)
        {
            if (modifier.MinimumRating > input.TotalBaseRating)
            {
                continue;
            }

            switch (modifier.Kind)
            {
                case CharacterCareerSkillSpecializationModifierKind.SkillCategorySpecializationKarmaCost:
                    extra = checked(extra + modifier.Value);
                    break;
                case CharacterCareerSkillSpecializationModifierKind.SkillCategorySpecializationKarmaCostMultiplier:
                    multiplier = checked(multiplier * (modifier.Value / 100m));
                    break;
                default:
                    throw new InvalidOperationException("Unsupported specialization Karma modifier kind.");
            }
        }

        return multiplier != 1m
            ? StandardRound(checked(baseCost * multiplier + extra))
            : checked(baseCost + StandardRound(extra));
    }

    private static bool WillBreakSkillGroup(
        CharacterCareerSkillKind kind,
        string skillGroup,
        int enabledMemberCount,
        bool settingsBreakGroups)
        => kind == CharacterCareerSkillKind.Active
            && settingsBreakGroups
            && !string.IsNullOrWhiteSpace(skillGroup)
            && enabledMemberCount > 1;

    private static int StandardRound(decimal value)
        => decimal.ToInt32(value >= 0m ? decimal.Ceiling(value) : decimal.Floor(value));

    // FutureSkillCosts1949 supplies ordinary arrays from the retained source and its
    // per-row modifier projection. Only these non-callback collections qualify.
    // Every call still performs IsValidInput and the rules above, then compares ALL
    // canonical rule fields and every current array element with detached values.
    // No caller-owned collection, quote, validation result or execution receipt is
    // retained. Other collection implementations keep the original observation path.
    // The cap bounds retained references, not admissible inputs; larger inputs replay.
    private const int MaximumPreparedRuleEntries3261 = 4096;

    internal sealed record RuleDigestPreparation3261(
        CharacterCareerSkillIdentity Identity, string DictionaryKey, string SkillCategory,
        string SkillGroup, int TotalBaseRating, int EnabledSkillGroupMemberCount,
        bool SkillSpecializationsBlocked, bool SkillCategorySpecializationsBlocked,
        CharacterCareerSkillSpecializationSettings Settings,
        ImmutableArray<CharacterCareerSkillSpecializationModifier> Modifiers,
        ImmutableArray<CharacterCareerSkillSpecializationOption> Options,
        string RawRuleState, string Digest)
    {
        internal bool Matches(CharacterCareerSkillSpecializationInput input,
            CharacterCareerSkillSpecializationModifier[] modifiers,
            CharacterCareerSkillSpecializationOption[] options)
            => Identity == input.Identity && DictionaryKey == input.DictionaryKey
                && SkillCategory == input.SkillCategory && SkillGroup == input.SkillGroup
                && TotalBaseRating == input.TotalBaseRating
                && EnabledSkillGroupMemberCount == input.EnabledSkillGroupMemberCount
                && SkillSpecializationsBlocked == input.SkillSpecializationsBlocked
                && SkillCategorySpecializationsBlocked == input.SkillCategorySpecializationsBlocked
                && Settings == input.Settings && RawRuleState == input.RawRuleState
                && ModifiersMatch3268(Modifiers.AsSpan(), modifiers.AsSpan())
                && Options.AsSpan().SequenceEqual(options.AsSpan());
    }

    private static bool ModifiersMatch3268(
        ReadOnlySpan<CharacterCareerSkillSpecializationModifier> before,
        ReadOnlySpan<CharacterCareerSkillSpecializationModifier> after)
    {
        if (before.Length != after.Length) return false;
        Span<int> beforeBits = stackalloc int[4];
        Span<int> afterBits = stackalloc int[4];
        for (int index = 0; index < before.Length; index++)
        {
            // Record equality covers every field but treats decimal scales as equal.
            // The historical invariant text encoder preserves scale, so require the
            // exact decimal representation too. A conservative cache miss is safe.
            if (before[index] != after[index]) return false;
            decimal.GetBits(before[index].Value, beforeBits);
            decimal.GetBits(after[index].Value, afterBits);
            if (!beforeBits.SequenceEqual(afterBits)) return false;
        }
        return true;
    }

    internal static string PrepareRuleDigest3261(CharacterCareerSkillSpecializationInput input,
        ref RuleDigestPreparation3261? prepared)
    {
        if (input.Modifiers is not CharacterCareerSkillSpecializationModifier[] modifiers
            || input.AvailableOptions is not CharacterCareerSkillSpecializationOption[] options
            || modifiers.Length > MaximumPreparedRuleEntries3261
            || options.Length > MaximumPreparedRuleEntries3261 - modifiers.Length)
        {
            prepared = null;
            return CalculateRuleDigest(input);
        }
        if (prepared is not null && prepared.Matches(input, modifiers, options)) return prepared.Digest;
        prepared = null;
        var frozenModifiers = modifiers.ToImmutableArray();
        var frozenOptions = options.ToImmutableArray();
        // Bind the retained preparation to the very same detached values passed to
        // the unchanged canonical encoder, never to a later second collection read.
        string digest = CalculateRuleDigest(input with { Modifiers = frozenModifiers, AvailableOptions = frozenOptions });
        prepared = new(input.Identity, input.DictionaryKey, input.SkillCategory, input.SkillGroup,
            input.TotalBaseRating, input.EnabledSkillGroupMemberCount, input.SkillSpecializationsBlocked,
            input.SkillCategorySpecializationsBlocked, input.Settings, frozenModifiers, frozenOptions,
            input.RawRuleState, digest);
        return digest;
    }

    private static string CalculateRuleDigest(CharacterCareerSkillSpecializationInput input)
    {
        IEnumerable<string> modifiers = input.Modifiers
            .OrderBy(static modifier => modifier.ModifierIdentity, StringComparer.Ordinal)
            .Select(modifier => string.Join(":",
                modifier.ModifierIdentity,
                modifier.Kind.ToString(),
                modifier.TargetSkillCategory,
                modifier.MinimumRating.ToString(CultureInfo.InvariantCulture),
                modifier.Value.ToString(CultureInfo.InvariantCulture)));
        IEnumerable<string> options = input.AvailableOptions
            .OrderBy(static option => option.OptionIdentity, StringComparer.Ordinal)
            .Select(option => string.Join(":",
                option.OptionIdentity,
                option.Kind.ToString(),
                option.Name,
                option.SourceAnchor));
        return Sha256(string.Join('\0',
            input.Identity.SkillId.ToString("D"),
            FormatSourceSkillId(input.Identity.SourceSkillId),
            input.Identity.Kind.ToString(),
            input.DictionaryKey,
            input.SkillCategory,
            input.SkillGroup,
            input.TotalBaseRating.ToString(CultureInfo.InvariantCulture),
            input.EnabledSkillGroupMemberCount.ToString(CultureInfo.InvariantCulture),
            input.SkillSpecializationsBlocked.ToString(CultureInfo.InvariantCulture),
            input.SkillCategorySpecializationsBlocked.ToString(CultureInfo.InvariantCulture),
            input.Settings.KarmaActiveSpecialization.ToString(CultureInfo.InvariantCulture),
            input.Settings.KarmaKnowledgeSpecialization.ToString(CultureInfo.InvariantCulture),
            input.Settings.SpecializationsBreakSkillGroups.ToString(CultureInfo.InvariantCulture),
            string.Join("|", modifiers),
            string.Join("|", options),
            input.RawRuleState));
    }

    private static string CalculateLogicalRevision(
        CharacterCareerSkillIdentity identity,
        CharacterCareerSkillSpecializationSelection selection,
        string skillName,
        string category,
        string group,
        bool enabled,
        bool isExotic,
        bool karmaUnlocked,
        bool allowUpgrade,
        bool isNativeLanguage,
        int rating,
        int existingSpecializationCount,
        int availableKarma,
        int enabledSkillGroupMemberCount,
        bool skillBlocked,
        bool categoryBlocked,
        bool specializationsBreakSkillGroups,
        int karmaCost,
        bool willBreakSkillGroup,
        bool canAdd,
        CharacterCareerSkillSpecializationBlocker blocker,
        string characterRevision,
        string sourceRevision,
        string ruleDigest)
        => Sha256(string.Join('\0',
            identity.SkillId.ToString("D"),
            FormatSourceSkillId(identity.SourceSkillId),
            identity.Kind.ToString(),
            selection.Name,
            selection.Kind.ToString(),
            selection.OptionIdentity ?? "custom",
            skillName,
            category,
            group,
            enabled.ToString(CultureInfo.InvariantCulture),
            isExotic.ToString(CultureInfo.InvariantCulture),
            karmaUnlocked.ToString(CultureInfo.InvariantCulture),
            allowUpgrade.ToString(CultureInfo.InvariantCulture),
            isNativeLanguage.ToString(CultureInfo.InvariantCulture),
            rating.ToString(CultureInfo.InvariantCulture),
            existingSpecializationCount.ToString(CultureInfo.InvariantCulture),
            availableKarma.ToString(CultureInfo.InvariantCulture),
            enabledSkillGroupMemberCount.ToString(CultureInfo.InvariantCulture),
            skillBlocked.ToString(CultureInfo.InvariantCulture),
            categoryBlocked.ToString(CultureInfo.InvariantCulture),
            specializationsBreakSkillGroups.ToString(CultureInfo.InvariantCulture),
            karmaCost.ToString(CultureInfo.InvariantCulture),
            willBreakSkillGroup.ToString(CultureInfo.InvariantCulture),
            canAdd.ToString(CultureInfo.InvariantCulture),
            blocker.ToString(),
            characterRevision,
            sourceRevision,
            ruleDigest));

    private static string FormatSourceSkillId(Guid? sourceSkillId)
        => sourceSkillId?.ToString("D") ?? "custom";

    private static bool IsBoundedRequiredText(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumNameLength;

    private static bool IsBoundedOptionalText(string? value)
        => value is not null && value.Length <= MaximumNameLength;

    private static bool RevisionMatches(string actual, string? expected)
        => IsLowerHexRevision(actual)
            && string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool IsLowerHexRevision(string? value)
        => value is { Length: RevisionHexLength }
            && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static CharacterCareerSkillSpecializationQuote UnavailableQuote()
        => new(
            new CharacterCareerSkillIdentity(Guid.Empty, null, CharacterCareerSkillKind.Active),
            new CharacterCareerSkillSpecializationSelection(
                string.Empty,
                CharacterCareerSkillSpecializationOptionKind.Custom,
                null),
            string.Empty,
            string.Empty,
            string.Empty,
            false,
            false,
            false,
            false,
            false,
            0,
            0,
            0,
            0,
            false,
            false,
            false,
            0,
            false,
            false,
            CharacterCareerSkillSpecializationBlocker.SkillDisabled,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);

    private static CharacterCareerSkillSpecializationPlan UnavailablePlan()
        => new(
            new CharacterCareerSkillIdentity(Guid.Empty, null, CharacterCareerSkillKind.Active),
            Guid.Empty,
            string.Empty,
            false,
            false,
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
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);
}
