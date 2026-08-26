using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Chummer.Contracts.Characters;

/// <summary>
/// Deterministic SR5 creation-lifestyle projection. Source parsing happens before this
/// boundary; callers can select only stable catalog option ids and cannot submit prices.
/// </summary>
public static class CharacterCreationLifestylesRules
{
    private const int MaximumSelections = 4096;
    private const int MaximumTextLength = 32_767;
    private const int MaximumIncrements = 10_000;
    private const int MaximumRoommates = 100;
    private const int MaximumAspectIncrease = 100;

    public static string ReceiptLedgerRootDigest { get; } =
        CharacterCreationLifestylesDigest.ComputeUtf8(
            "chummer.sr5.creation-lifestyles-receipt-ledger.root.v1");

    public static string ComputeOptionDigest(CharacterCreationLifestyleCatalogOption option) =>
        CharacterCreationLifestylesDigest.Compute(option with { OptionDigest = string.Empty });

    public static string ComputeQualityOptionDigest(
        CharacterCreationLifestyleQualityCatalogOption option) =>
        CharacterCreationLifestylesDigest.Compute(option with { OptionDigest = string.Empty });

    public static string ComputeAuthorityDigest(CharacterCreationLifestylesAuthority authority) =>
        CharacterCreationLifestylesDigest.Compute(authority with { AuthorityDigest = string.Empty });

    public static string ComputeProjectionDigest(CharacterCreationLifestyleProjection projection) =>
        CharacterCreationLifestylesDigest.Compute(projection with { LifestyleDigest = string.Empty });

    public static string ComputeReceiptDigest(CharacterCreationLifestyleReceipt receipt) =>
        CharacterCreationLifestylesDigest.Compute(receipt with { ReceiptDigest = string.Empty });

    public static string ComputePlanDigest(CharacterCreationLifestyleAtomicWritePlan plan) =>
        CharacterCreationLifestylesDigest.Compute(plan with { PlanDigest = string.Empty });

    public static string ComputeStateDigest(CharacterCreationLifestylesState state) =>
        CharacterCreationLifestylesDigest.Compute(state with { SnapshotDigest = string.Empty });

    public static string ComputePreviewDigest(CharacterCreationLifestylePreview preview) =>
        CharacterCreationLifestylesDigest.Compute(preview with { PreviewDigest = string.Empty });

    public static string ComputeIdempotencyKeyDigest(string value) =>
        CharacterCreationLifestylesDigest.ComputeUtf8(value);

    public static string ComputeCommandDigest(CharacterCreationLifestyleConfirmRequest request) =>
        CharacterCreationLifestylesDigest.Compute(new
        {
            Schema = "chummer.sr5.creation-lifestyles.command.v1",
            request.Binding,
            request.Mutation,
            request.PreviewDigest
        });

    public static bool DigestsEqual(string? left, string? right) =>
        CharacterCreationLifestylesDigest.EqualsFixedTime(left, right);

    public static bool IsCanonicalDigest(string? value) =>
        CharacterCreationLifestylesDigest.IsCanonical(value);

    public static bool IsValidAuthority(CharacterCreationLifestylesAuthority? authority)
    {
        if (authority is null
            || !authority.IsAuthoritative
            || !string.Equals(
                authority.Schema,
                CharacterCreationLifestylesSchemas.AuthorityV1,
                StringComparison.Ordinal)
            || !string.Equals(authority.RulesetId, "sr5", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(authority.SettingsProfileId)
            || authority.LifestyleOptions is not { Count: > 0 and <= 65_536 }
            || authority.QualityOptions is not { Count: <= 65_536 }
            || authority.TrustFundLevel is < 0 or > 4
            || authority.SourceAnchorIds is not { Count: > 0 }
            || authority.SourceAnchorIds.Any(string.IsNullOrWhiteSpace)
            || authority.SourceAnchorIds.Distinct(StringComparer.Ordinal).Count()
               != authority.SourceAnchorIds.Count
            || authority.Blockers.Count != 0
            || !CharacterCreationLifestylesDigest.IsCanonical(authority.SourceDigest)
            || !CharacterCreationLifestylesDigest.IsCanonical(authority.ProfileDigest)
            || !CharacterCreationLifestylesDigest.IsCanonical(authority.GmPolicyDigest)
            || !CharacterCreationLifestylesDigest.IsCanonical(authority.RuntimeDigest)
            || authority.LifestyleOptions.Any(option => !IsValidLifestyleOption(option))
            || authority.QualityOptions.Any(option => !IsValidQualityOption(option))
            || authority.LifestyleOptions.Select(option => option.OptionId)
                .Distinct(StringComparer.Ordinal).Count() != authority.LifestyleOptions.Count
            || authority.LifestyleOptions.Select(option => option.SourceId)
                .Distinct().Count() != authority.LifestyleOptions.Count
            || authority.QualityOptions.Select(option => option.OptionId)
                .Distinct(StringComparer.Ordinal).Count() != authority.QualityOptions.Count
            || authority.QualityOptions.Select(option => option.SourceId)
                .Distinct().Count() != authority.QualityOptions.Count)
        {
            return false;
        }

        return CharacterCreationLifestylesDigest.EqualsFixedTime(
            authority.AuthorityDigest,
            ComputeAuthorityDigest(authority));
    }

    public static bool TryProject(
        CharacterCreationLifestyleConfiguration? requested,
        CharacterCreationLifestylesAuthority? authority,
        out CharacterCreationLifestyleProjection projection,
        out IReadOnlyList<string> blockers)
    {
        projection = EmptyProjection();
        var findings = new List<string>();
        if (!IsValidAuthority(authority))
        {
            findings.Add(CharacterCreationLifestylesBlockers.AuthorityUnavailable);
            blockers = Normalize(findings);
            return false;
        }
        if (requested is null || !IsValidConfigurationEnvelope(requested))
        {
            findings.Add(CharacterCreationLifestylesBlockers.InvalidMutation);
            blockers = Normalize(findings);
            return false;
        }

        CharacterCreationLifestyleCatalogOption? baseOption = authority!.LifestyleOptions
            .SingleOrDefault(option => string.Equals(
                option.OptionId,
                requested.BaseLifestyleOptionId,
                StringComparison.Ordinal));
        if (baseOption is null || !IsValidLifestyleOption(baseOption))
        {
            findings.Add(CharacterCreationLifestylesBlockers.InvalidOption);
            blockers = Normalize(findings);
            return false;
        }
        if (!baseOption.IsSelectable)
            findings.AddRange(baseOption.Blockers.Count == 0
                ? [CharacterCreationLifestylesBlockers.SourceDisabled]
                : baseOption.Blockers);
        if (!baseOption.EligibilityIsExact)
            findings.Add(CharacterCreationLifestylesBlockers.UnsupportedSemantics);
        if (!string.Equals(requested.IncrementId, baseOption.DefaultIncrementId, StringComparison.Ordinal))
            findings.Add(CharacterCreationLifestylesBlockers.InvalidMutation);

        CharacterCreationLifestyleConfiguration configuration = AddBuiltInQualities(
            requested,
            baseOption);
        Dictionary<string, CharacterCreationLifestyleQualityCatalogOption> qualityCatalog =
            authority.QualityOptions.ToDictionary(option => option.OptionId, StringComparer.Ordinal);
        var selected = new List<SelectedQuality>();
        var identities = new HashSet<Guid>();
        foreach (CharacterCreationLifestyleQualitySelection selection in configuration.Qualities)
        {
            if (selection is null
                || selection.InstanceId == Guid.Empty
                || !identities.Add(selection.InstanceId)
                || string.IsNullOrWhiteSpace(selection.OptionId)
                || !IsValidText(selection.Extra)
                || !qualityCatalog.TryGetValue(
                    selection.OptionId,
                    out CharacterCreationLifestyleQualityCatalogOption? option)
                || !IsValidQualityOption(option))
            {
                findings.Add(CharacterCreationLifestylesBlockers.InvalidOption);
                continue;
            }

            CharacterCreationLifestyleBuiltInQuality? builtIn = baseOption.BuiltInQualities
                .SingleOrDefault(item => string.Equals(
                    item.QualityOptionId,
                    selection.OptionId,
                    StringComparison.Ordinal)
                    && string.Equals(item.Extra, selection.Extra, StringComparison.Ordinal));
            if (selection.IsBuiltIn != (builtIn is not null))
            {
                findings.Add(CharacterCreationLifestylesBlockers.InvalidMutation);
                continue;
            }
            if (!selection.IsBuiltIn && !option.IsSelectable)
            {
                findings.AddRange(option.Blockers.Count == 0
                    ? [CharacterCreationLifestylesBlockers.SourceDisabled]
                    : option.Blockers);
                continue;
            }
            if (!selection.IsBuiltIn && !option.EligibilityIsExact)
            {
                findings.Add(CharacterCreationLifestylesBlockers.UnsupportedSemantics);
                continue;
            }
            bool canBeFreeByLifestyle = IsAllowedFreeLifestyle(option, baseOption.Name);
            if (selection.UseLifestylePoints
                && !canBeFreeByLifestyle
                && !selection.IsBuiltIn)
            {
                findings.Add(CharacterCreationLifestylesBlockers.InvalidMutation);
                continue;
            }
            selected.Add(new SelectedQuality(selection, option, canBeFreeByLifestyle));
        }

        int[] actualAspectMaximums =
        [
            SafeAspectMaximum(baseOption.MaximumArea, selected.Sum(item => item.Option.AreaMaximumModifier), findings),
            SafeAspectMaximum(baseOption.MaximumComforts, selected.Sum(item => item.Option.ComfortsMaximumModifier), findings),
            SafeAspectMaximum(baseOption.MaximumSecurity, selected.Sum(item => item.Option.SecurityMaximumModifier), findings)
        ];
        if (configuration.Area > actualAspectMaximums[0]
            || configuration.Comforts > actualAspectMaximums[1]
            || configuration.Security > actualAspectMaximums[2]
            || string.Equals(configuration.StyleId, CharacterCreationLifestyleStyleIds.Standard, StringComparison.Ordinal)
               && (configuration.Area != 0
                   || configuration.Comforts != 0
                   || configuration.Security != 0
                   || configuration.BonusLifestylePoints != 0))
        {
            findings.Add(CharacterCreationLifestylesBlockers.InvalidMutation);
        }
        if (configuration.BonusLifestylePoints != 0 && !baseOption.AllowsBonusLifestylePoints)
            findings.Add(CharacterCreationLifestylesBlockers.InvalidMutation);
        bool trustFundEligible = IsTrustFundEligible(authority.TrustFundLevel, baseOption.Name);
        if (configuration.TrustFund && !trustFundEligible)
            findings.Add(CharacterCreationLifestylesBlockers.InvalidMutation);
        if (configuration.TrustFund
            && (configuration.Roommates != 0 || configuration.SplitCostWithRoommates))
            findings.Add(CharacterCreationLifestylesBlockers.InvalidMutation);
        if (configuration.SplitCostWithRoommates && configuration.Roommates == 0)
            findings.Add(CharacterCreationLifestylesBlockers.InvalidMutation);

        int lifestylePoints = ComputeLifestylePoints(baseOption, configuration, selected, findings);
        if (!string.Equals(configuration.StyleId, CharacterCreationLifestyleStyleIds.Standard, StringComparison.Ordinal)
            && lifestylePoints < 0)
        {
            findings.Add(CharacterCreationLifestylesBlockers.LifestylePointsExceeded);
        }
        decimal costPerIncrement = ComputeCostPerIncrement(
            baseOption,
            configuration,
            selected,
            findings);
        decimal totalCost;
        try
        {
            totalCost = checked(costPerIncrement * configuration.Increments);
        }
        catch (OverflowException)
        {
            totalCost = decimal.MaxValue;
            findings.Add(CharacterCreationLifestylesBlockers.AuthorityUnavailable);
        }

        string[] normalized = Normalize(findings);
        var economics = new CharacterCreationLifestyleEconomics(
            costPerIncrement,
            totalCost,
            baseOption.LifestylePoints,
            lifestylePoints,
            configuration.TrustFund && trustFundEligible,
            configuration.SplitCostWithRoommates && configuration.Roommates > 0
                && !configuration.TrustFund,
            normalized);
        string[] anchors = baseOption.SourceAnchorIds
            .Concat(selected.SelectMany(item => item.Option.SourceAnchorIds))
            .Concat(CharacterCreationLifestyleSourceAnchors.All)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var candidate = new CharacterCreationLifestyleProjection(
            configuration,
            baseOption.SourceId,
            baseOption.Name,
            baseOption.SourceBook,
            baseOption.Page,
            economics,
            anchors,
            string.Empty);
        projection = candidate with { LifestyleDigest = ComputeProjectionDigest(candidate) };
        blockers = normalized;
        return normalized.Length == 0;
    }

    public static CharacterCreationLifestyleConfiguration AddBuiltInQualities(
        CharacterCreationLifestyleConfiguration configuration,
        CharacterCreationLifestyleCatalogOption baseOption)
    {
        CharacterCreationLifestyleQualitySelection[] supplied = configuration.Qualities?
            .Where(selection => selection is not null && !selection.IsBuiltIn)
            .ToArray() ?? [];
        CharacterCreationLifestyleQualitySelection[] builtIns = baseOption.BuiltInQualities
            .Select(item => new CharacterCreationLifestyleQualitySelection(
                DeterministicBuiltInIdentity(
                    configuration.LifestyleId,
                    item.QualityOptionId,
                    item.Extra),
                item.QualityOptionId,
                item.Extra,
                UseLifestylePoints: true,
                IsFree: true,
                IsBuiltIn: true))
            .ToArray();
        return configuration with
        {
            Qualities = supplied.Concat(builtIns)
                .OrderBy(item => item.InstanceId)
                .ToArray()
        };
    }

    private static decimal ComputeCostPerIncrement(
        CharacterCreationLifestyleCatalogOption lifestyle,
        CharacterCreationLifestyleConfiguration configuration,
        IReadOnlyList<SelectedQuality> selected,
        List<string> blockers)
    {
        try
        {
            decimal preSplit = lifestyle.BaseCost;
            decimal baseMultiplier = ProductMultiplier(selected
                .Where(item => !item.Selection.IsBuiltIn)
                .Select(EffectiveBaseMultiplier), blockers);
            preSplit = checked(preSplit * baseMultiplier);
            int aspects = checked(configuration.Area + configuration.Comforts + configuration.Security);
            preSplit = checked(preSplit * (1m + 0.1m * aspects));
            preSplit = checked(preSplit
                + configuration.Area * lifestyle.CostPerArea
                + configuration.Comforts * lifestyle.CostPerComfort
                + configuration.Security * lifestyle.CostPerSecurity);

            SelectedQuality[] entertainmentAssets = selected.Where(item =>
                !item.Selection.IsBuiltIn
                && item.Option.QualityType == CharacterCreationLifestyleQualityTypes.Entertainment
                && item.Option.Category.Contains("Asset", StringComparison.Ordinal)).ToArray();
            preSplit = ApplyQualityCostLayer(preSplit, entertainmentAssets, blockers);
            SelectedQuality[] otherPreSplit = selected.Where(item =>
                !item.Selection.IsBuiltIn
                && item.Option.QualityType != CharacterCreationLifestyleQualityTypes.Entertainment
                && item.Option.QualityType != CharacterCreationLifestyleQualityTypes.Contracts).ToArray();
            preSplit = ApplyQualityCostLayer(preSplit, otherPreSplit, blockers);
            if (configuration.Roommates > 0)
                preSplit = checked(preSplit * (1m + 0.1m * configuration.Roommates));
            preSplit = Math.Max(preSplit, 0m);

            decimal result = configuration.TrustFund ? 0m : preSplit;
            if (configuration.SplitCostWithRoommates
                && configuration.Roommates > 0
                && !configuration.TrustFund)
            {
                result = checked(result / (configuration.Roommates + 1m));
            }

            SelectedQuality[] outingsAndServices = selected.Where(item =>
                !item.Selection.IsBuiltIn
                && item.Option.QualityType == CharacterCreationLifestyleQualityTypes.Entertainment
                && !item.Option.Category.Contains("Asset", StringComparison.Ordinal)).ToArray();
            decimal outingMultiplier = ProductMultiplier(
                outingsAndServices.Select(EffectiveMultiplier),
                blockers);
            result = checked(result * outingMultiplier);
            result = checked(result + outingsAndServices.Sum(EffectiveFlatCost));
            decimal baseOnlyMultiplier = ProductMultiplier(
                outingsAndServices.Select(EffectiveBaseMultiplier),
                blockers);
            if (baseOnlyMultiplier != 1m)
                result = checked(result + lifestyle.BaseCost * baseOnlyMultiplier);
            result = checked(result * configuration.Percentage / 100m);

            decimal contracts = selected.Where(item =>
                !item.Selection.IsBuiltIn
                && item.Option.QualityType == CharacterCreationLifestyleQualityTypes.Contracts)
                .Sum(EffectiveFlatCost);
            contracts = configuration.IncrementId switch
            {
                CharacterCreationLifestyleIncrementIds.Day => contracts / (4.34812m * 7m),
                CharacterCreationLifestyleIncrementIds.Week => contracts / 4.34812m,
                _ => contracts
            };
            return checked(result + contracts);
        }
        catch (OverflowException)
        {
            blockers.Add(CharacterCreationLifestylesBlockers.AuthorityUnavailable);
            return decimal.MaxValue;
        }
    }

    private static decimal ApplyQualityCostLayer(
        decimal basis,
        IReadOnlyList<SelectedQuality> selected,
        List<string> blockers)
    {
        decimal multiplier = ProductMultiplier(
            selected.Select(EffectiveMultiplier),
            blockers);
        return checked(basis * multiplier + selected.Sum(EffectiveFlatCost));
    }

    private static decimal ProductMultiplier(
        IEnumerable<decimal> percentages,
        List<string> blockers)
    {
        decimal value = 1m;
        try
        {
            foreach (decimal percentage in percentages)
                if (percentage != 0m)
                    value = checked(value * (1m + percentage / 100m));
            return value;
        }
        catch (OverflowException)
        {
            blockers.Add(CharacterCreationLifestylesBlockers.AuthorityUnavailable);
            return decimal.MaxValue;
        }
    }

    private static decimal EffectiveFlatCost(SelectedQuality quality) =>
        IsCostFree(quality)
            ? 0m
            : quality.Option.FlatCost;

    private static decimal EffectiveMultiplier(SelectedQuality quality) =>
        IsCostFree(quality) ? 0m : quality.Option.CostMultiplierPercent;

    private static decimal EffectiveBaseMultiplier(SelectedQuality quality) =>
        IsCostFree(quality) ? 0m : quality.Option.BaseCostMultiplierPercent;

    private static bool IsCostFree(SelectedQuality quality) =>
        quality.Selection.IsFree
        || quality.Selection.IsBuiltIn
        || quality.Selection.UseLifestylePoints && quality.CanBeFreeByLifestyle;

    private static int ComputeLifestylePoints(
        CharacterCreationLifestyleCatalogOption lifestyle,
        CharacterCreationLifestyleConfiguration configuration,
        IReadOnlyList<SelectedQuality> selected,
        List<string> blockers)
    {
        try
        {
            int negative = selected.Where(item => item.Option.LifestylePointCost < 0)
                .Sum(item => EffectiveLifestylePointCost(item));
            int positive = selected.Where(item => item.Option.LifestylePointCost > 0)
                .Sum(item => EffectiveLifestylePointCost(item));
            int bonus = Math.Min(
                checked(configuration.Roommates + configuration.BonusLifestylePoints - negative),
                checked(2 * lifestyle.LifestylePoints));
            return checked(lifestyle.LifestylePoints
                - configuration.Area
                - configuration.Comforts
                - configuration.Security
                + bonus
                - positive);
        }
        catch (OverflowException)
        {
            blockers.Add(CharacterCreationLifestylesBlockers.AuthorityUnavailable);
            return int.MinValue;
        }
    }

    private static int EffectiveLifestylePointCost(SelectedQuality quality) =>
        quality.Selection.IsFree
        || !quality.Selection.UseLifestylePoints && quality.CanBeFreeByLifestyle
            ? 0
            : quality.Option.LifestylePointCost;

    private static int SafeAspectMaximum(int basis, int modifier, List<string> blockers)
    {
        try
        {
            return Math.Max(0, checked(basis + modifier));
        }
        catch (OverflowException)
        {
            blockers.Add(CharacterCreationLifestylesBlockers.AuthorityUnavailable);
            return 0;
        }
    }

    private static bool IsValidLifestyleOption(CharacterCreationLifestyleCatalogOption option) =>
        option is not null
        && !string.IsNullOrWhiteSpace(option.OptionId)
        && option.SourceId != Guid.Empty
        && !string.IsNullOrWhiteSpace(option.Name)
        && option.BaseCost >= 0m
        && option.StartingNuyenDice >= 0
        && option.StartingNuyenMultiplier >= 0m
        && option.LifestylePoints >= 0
        && option.CostPerArea >= 0m
        && option.CostPerComfort >= 0m
        && option.CostPerSecurity >= 0m
        && option.BaseArea >= 0
        && option.MaximumArea >= option.BaseArea
        && option.BaseComforts >= 0
        && option.MaximumComforts >= option.BaseComforts
        && option.BaseSecurity >= 0
        && option.MaximumSecurity >= option.BaseSecurity
        && CharacterCreationLifestyleIncrementIds.All.Contains(option.DefaultIncrementId)
        && !string.IsNullOrWhiteSpace(option.SourceBook)
        && !string.IsNullOrWhiteSpace(option.Page)
        && option.BuiltInQualities.Count <= MaximumSelections
        && option.BuiltInQualities.All(item => item is not null
            && !string.IsNullOrWhiteSpace(item.QualityOptionId)
            && item.Extra is not null
            && item.SourceAnchorIds.Count > 0
            && item.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor)))
        && option.SourceAnchorIds.Count > 0
        && option.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor))
        && (option.EligibilityIsExact || !option.IsSelectable)
        && CharacterCreationLifestylesDigest.EqualsFixedTime(
            option.OptionDigest,
            ComputeOptionDigest(option));

    private static bool IsValidQualityOption(
        CharacterCreationLifestyleQualityCatalogOption option) =>
        option is not null
        && !string.IsNullOrWhiteSpace(option.OptionId)
        && option.SourceId != Guid.Empty
        && !string.IsNullOrWhiteSpace(option.Name)
        && !string.IsNullOrWhiteSpace(option.Category)
        && !string.IsNullOrWhiteSpace(option.SourceBook)
        && !string.IsNullOrWhiteSpace(option.Page)
        && option.QualityType is CharacterCreationLifestyleQualityTypes.Entertainment
            or CharacterCreationLifestyleQualityTypes.Positive
            or CharacterCreationLifestyleQualityTypes.Negative
            or CharacterCreationLifestyleQualityTypes.Contracts
        && option.AllowedFreeLifestyleNames.Count <= MaximumSelections
        && option.AllowedFreeLifestyleNames.All(name => !string.IsNullOrWhiteSpace(name))
        && option.SourceAnchorIds.Count > 0
        && option.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor))
        && (option.EligibilityIsExact || !option.IsSelectable)
        && CharacterCreationLifestylesDigest.EqualsFixedTime(
            option.OptionDigest,
            ComputeQualityOptionDigest(option));

    private static bool IsValidConfigurationEnvelope(
        CharacterCreationLifestyleConfiguration configuration) =>
        configuration.LifestyleId != Guid.Empty
        && !string.IsNullOrWhiteSpace(configuration.BaseLifestyleOptionId)
        && IsValidText(configuration.Name)
        && configuration.Name.Length > 0
        && CharacterCreationLifestyleStyleIds.All.Contains(configuration.StyleId)
        && CharacterCreationLifestyleIncrementIds.All.Contains(configuration.IncrementId)
        && configuration.Increments is > 0 and <= MaximumIncrements
        && configuration.Percentage is > 0m and <= 1000m
        && configuration.Roommates is >= 0 and <= MaximumRoommates
        && configuration.Area is >= 0 and <= MaximumAspectIncrease
        && configuration.Comforts is >= 0 and <= MaximumAspectIncrease
        && configuration.Security is >= 0 and <= MaximumAspectIncrease
        && configuration.BonusLifestylePoints is >= 0 and <= MaximumAspectIncrease
        && IsValidText(configuration.City)
        && IsValidText(configuration.District)
        && IsValidText(configuration.Borough)
        && configuration.Qualities is { Count: <= MaximumSelections };

    private static bool IsValidText(string? value) =>
        value is not null
        && value.Length <= MaximumTextLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && value.All(character => !char.IsControl(character)
            || character is '\r' or '\n' or '\t');

    private static bool IsAllowedFreeLifestyle(
        CharacterCreationLifestyleQualityCatalogOption option,
        string baseLifestyle) =>
        option.QualityType is CharacterCreationLifestyleQualityTypes.Entertainment
            or CharacterCreationLifestyleQualityTypes.Contracts
        && option.AllowedFreeLifestyleNames.Contains(baseLifestyle, StringComparer.Ordinal);

    private static bool IsTrustFundEligible(int trustFundLevel, string baseLifestyle) =>
        trustFundLevel switch
        {
            1 or 4 => string.Equals(baseLifestyle, "Medium", StringComparison.Ordinal),
            2 => string.Equals(baseLifestyle, "Low", StringComparison.Ordinal),
            3 => string.Equals(baseLifestyle, "High", StringComparison.Ordinal),
            _ => false
        };

    private static Guid DeterministicBuiltInIdentity(
        Guid lifestyleId,
        string optionId,
        string extra)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(
            '\0',
            lifestyleId.ToString("D"),
            optionId,
            extra)));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private static string[] Normalize(IEnumerable<string> blockers) => blockers
        .Where(blocker => !string.IsNullOrWhiteSpace(blocker))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(blocker => blocker, StringComparer.Ordinal)
        .ToArray();

    private static CharacterCreationLifestyleProjection EmptyProjection()
    {
        var configuration = new CharacterCreationLifestyleConfiguration(
            Guid.Empty,
            string.Empty,
            string.Empty,
            CharacterCreationLifestyleStyleIds.Standard,
            CharacterCreationLifestyleIncrementIds.Month,
            0,
            100m,
            0,
            false,
            false,
            0,
            0,
            0,
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            []);
        return new CharacterCreationLifestyleProjection(
            configuration,
            Guid.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            new CharacterCreationLifestyleEconomics(0m, 0m, 0, 0, false, false, []),
            [],
            string.Empty);
    }

    private sealed record SelectedQuality(
        CharacterCreationLifestyleQualitySelection Selection,
        CharacterCreationLifestyleQualityCatalogOption Option,
        bool CanBeFreeByLifestyle);
}

internal static class CharacterCreationLifestylesDigest
{
    private const string Prefix = "sha256:";
    private static readonly SearchValues<char> s_LowerHex = SearchValues.Create("0123456789abcdef");

    public static string Compute<T>(T value)
    {
        JsonElement element = JsonSerializer.SerializeToElement(value);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonical(element, writer);
        }
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    public static string ComputeUtf8(string value) => Prefix
        + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static bool IsCanonical(string? value) =>
        value is { Length: 71 }
        && value.StartsWith(Prefix, StringComparison.Ordinal)
        && value.AsSpan(Prefix.Length).ContainsAnyExcept(s_LowerHex) is false;

    public static bool EqualsFixedTime(string? left, string? right)
    {
        if (!IsCanonical(left) || !IsCanonical(right))
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left!),
            Encoding.ASCII.GetBytes(right!));
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject()
                             .OrderBy(property => property.Name, StringComparer.Ordinal))
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
                throw new InvalidOperationException("Unsupported canonical JSON value kind.");
        }
    }
}
