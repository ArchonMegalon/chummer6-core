using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public readonly record struct CharacterBiowareSourceId(Guid Value);

public readonly record struct CharacterBiowareGradeId(Guid Value);

public readonly record struct CharacterBiowareConfigurationId(Guid Value);

public readonly record struct CharacterBiowareQuoteId(string Value);

public readonly record struct CharacterBiowareInstanceId(Guid Value);

public enum CharacterBiowareLegality
{
    Legal = 0,
    Restricted = 1,
    Forbidden = 2
}

public sealed record CharacterBiowarePurchaseSourceBinding(
    string SettingsProfileId,
    string ProfileDigest,
    string RawBiowareXmlDigest,
    string EffectiveBiowareInputsDigest,
    string SelectedBiowareCustomDataInputsDigest,
    string EffectiveSettingsInputsDigest);

public sealed record CharacterBiowarePurchaseGrade(
    CharacterBiowareGradeId Id,
    string Name,
    decimal CostMultiplier,
    decimal EssenceMultiplier,
    int AvailabilityModifier,
    string SourceBook,
    string Page);

public sealed record CharacterBiowarePurchaseCatalogEntry(
    CharacterBiowareSourceId SourceId,
    string Name,
    string Category,
    string EssenceExpression,
    string CapacityExpression,
    int BaseAvailability,
    CharacterBiowareLegality Legality,
    string AvailabilityExpression,
    string CostExpression,
    string SourceBook,
    string Page,
    bool BlackMarketEligible,
    bool IsGeneware,
    string ForcedGrade,
    IReadOnlyList<string> BannedGrades,
    IReadOnlyList<CharacterBiowarePurchaseGrade> Grades);

public sealed record CharacterBiowarePurchaseCatalogExclusion(
    CharacterBiowareSourceId SourceId,
    string Name,
    string Reason);

public sealed record CharacterBiowarePurchaseSettings(
    bool AllowEssenceDiscounts,
    bool MultiplyRestrictedCost,
    decimal RestrictedCostMultiplier,
    bool MultiplyForbiddenCost,
    decimal ForbiddenCostMultiplier,
    int EssenceDecimals,
    bool DoNotRoundEssenceInternally,
    IReadOnlyList<string> BannedGrades);

public sealed record CharacterBiowarePurchaseCatalogAuthority(
    CharacterBiowarePurchaseSourceBinding Binding,
    CharacterBiowarePurchaseSettings Settings,
    IReadOnlyList<CharacterBiowarePurchaseCatalogEntry> Entries,
    IReadOnlyList<CharacterBiowarePurchaseCatalogExclusion> Exclusions,
    string AuthorityDigest)
{
    public static CharacterBiowarePurchaseCatalogAuthority Unavailable { get; } = new(
        new CharacterBiowarePurchaseSourceBinding(
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty),
        new CharacterBiowarePurchaseSettings(false, false, 0m, false, 0m, 0, false, []),
        [],
        [],
        string.Empty);
}

public sealed record CharacterBiowarePurchasePreparation(
    bool Exact,
    IReadOnlyList<string> Blockers,
    long ContentRevision,
    string CharacterDigest,
    string CatalogDigest,
    CharacterBiowarePurchaseSourceBinding SourceBinding,
    decimal AvailableNuyen,
    bool ExCon,
    int? EssenceHoleRating,
    int? EssenceAntiHoleRating,
    CharacterBiowarePurchaseSettings Settings,
    IReadOnlyList<CharacterBiowarePurchaseCatalogEntry> Entries,
    IReadOnlyList<CharacterBiowarePurchaseCatalogExclusion> Exclusions);

public sealed record CharacterBiowarePurchaseSelection(
    CharacterBiowareConfigurationId ConfigurationId,
    CharacterBiowareSourceId SourceId,
    CharacterBiowareGradeId GradeId,
    int Rating,
    int EssenceDiscountPercent,
    bool BlackMarketDiscount,
    decimal MarkupPercent,
    bool FreeCost);

public sealed record CharacterBiowarePurchaseQuote(
    bool Exact,
    string BlockReason,
    CharacterBiowareQuoteId QuoteId,
    CharacterBiowareConfigurationId ConfigurationId,
    CharacterBiowareSourceId SourceId,
    CharacterBiowareGradeId GradeId,
    string Name,
    string GradeName,
    int Rating,
    decimal BaseCost,
    decimal ChargedCost,
    decimal NuyenDelta,
    decimal InstalledEssence,
    int BaseAvailability,
    int GradeAvailabilityModifier,
    int FinalAvailability,
    CharacterBiowareLegality Legality,
    int? NewEssenceHoleRating,
    int? NewEssenceAntiHoleRating);

public sealed record CharacterBiowarePurchaseCommand(
    long ExpectedContentRevision,
    string ExpectedCharacterDigest,
    string ExpectedCatalogDigest,
    CharacterBiowareQuoteId ExpectedQuoteId,
    CharacterBiowarePurchaseSelection Selection,
    CharacterBiowareInstanceId NewInstanceId,
    Guid NewExpenseId,
    DateTimeOffset ExpenseDate);

public sealed record CharacterBiowarePurchaseUndoReceipt(
    long ContentRevision,
    string CharacterDigest,
    long PreviousContentRevision,
    string PreviousCharacterDigest,
    decimal PreviousAvailableNuyen,
    int? PreviousEssenceHoleRating,
    int? PreviousEssenceAntiHoleRating,
    string CatalogDigest,
    CharacterBiowareQuoteId QuoteId,
    CharacterBiowareSourceId SourceId,
    CharacterBiowareGradeId GradeId,
    CharacterBiowarePurchaseSelection Selection,
    CharacterBiowareInstanceId InstanceId,
    Guid ExpenseId,
    DateTimeOffset ExpenseDate,
    decimal NuyenDelta,
    string BiowareXmlDigest,
    string ExpenseXmlDigest,
    string ReceiptDigest);

public sealed record CharacterBiowarePurchaseUndoCommand(
    CharacterBiowarePurchaseUndoReceipt? Receipt);

public sealed record CharacterBiowarePurchaseCommitResult(
    bool Committed,
    string BlockReason,
    long PreviousContentRevision,
    long NewContentRevision,
    string PreviousCharacterDigest,
    string NewCharacterDigest,
    string CharacterXml,
    CharacterBiowareInstanceId InstanceId,
    Guid ExpenseId,
    decimal NuyenDelta,
    decimal EssenceHoleDelta,
    string CatalogDigest,
    CharacterBiowareQuoteId QuoteId,
    CharacterBiowarePurchaseUndoReceipt? UndoReceipt);

/// <summary>
/// Audited Chummer5 inputs for the Career Bioware purchase lane. The raw
/// carried bioware.xml remains pinned while enabled overlay and selected
/// legacy custom-data inputs are admitted only through digest-bound effective
/// catalog projection.
/// </summary>
public static class CharacterBiowarePurchaseLegacyAuthority
{
    public const string Commit = CharacterCyberwarePurchaseLegacyAuthority.Commit;
    public const string Tree = CharacterCyberwarePurchaseLegacyAuthority.Tree;
    public const string SelectCyberwareDesignerSha256 = CharacterCyberwarePurchaseLegacyAuthority.SelectCyberwareDesignerSha256;
    public const string SelectCyberwareSha256 = CharacterCyberwarePurchaseLegacyAuthority.SelectCyberwareSha256;
    public const string CharacterCareerSha256 = CharacterCyberwarePurchaseLegacyAuthority.CharacterCareerSha256;
    public const string CyberwareSha256 = CharacterCyberwarePurchaseLegacyAuthority.CyberwareSha256;
    public const string ExpensesSha256 = CharacterCyberwarePurchaseLegacyAuthority.ExpensesSha256;
    public const string CharacterSettingsSha256 = CharacterCyberwarePurchaseLegacyAuthority.CharacterSettingsSha256;
    public const string ImprovementManagerSha256 = CharacterCyberwarePurchaseLegacyAuthority.ImprovementManagerSha256;
    public const string CharacterSha256 = CharacterCyberwarePurchaseLegacyAuthority.CharacterSha256;
    public const string DecimalExtensionsSha256 = CharacterCyberwarePurchaseLegacyAuthority.DecimalExtensionsSha256;
    public const string GlobalSettingsSha256 = CharacterCyberwarePurchaseLegacyAuthority.GlobalSettingsSha256;
    public const string ColorManagerSha256 = CharacterCyberwarePurchaseLegacyAuthority.ColorManagerSha256;
    public const string BiowareXmlSha256 = "9139c0c726c960a6d51ad60539acb8ec3e59433945b65aaeaf8e37a60da73cdd";

    public static IReadOnlyList<string> CanonicalInputs { get; } =
    [
        $"commit:{Commit}",
        $"tree:{Tree}",
        $"Chummer/Forms/Selection Forms/SelectCyberware.Designer.cs:{SelectCyberwareDesignerSha256}",
        $"Chummer/Forms/Selection Forms/SelectCyberware.cs:{SelectCyberwareSha256}",
        $"Chummer/Forms/Character Forms/CharacterCareer.cs:{CharacterCareerSha256}",
        $"Chummer/Backend/Equipment/Cyberware.cs:{CyberwareSha256}",
        $"Chummer/Backend/Uniques/Expenses.cs:{ExpensesSha256}",
        $"Chummer/Backend/Character Settings/CharacterSettings.cs:{CharacterSettingsSha256}",
        $"Chummer/Backend/Static/Managers/ImprovementManager.cs:{ImprovementManagerSha256}",
        $"Chummer/Backend/Characters/Character.cs:{CharacterSha256}",
        $"Chummer/Backend/Static/Extensions/DecimalExtensions.cs:{DecimalExtensionsSha256}",
        $"Chummer/Backend/Static/GlobalSettings.cs:{GlobalSettingsSha256}",
        $"Chummer/Backend/Static/Managers/ColorManager.cs:{ColorManagerSha256}",
        $"Chummer/data/bioware.xml:{BiowareXmlSha256}"
    ];
}

public static class CharacterBiowarePurchaseBlockers
{
    public const string NotCareer = "Bioware purchase is available only for a saved Career character with created=true.";
    public const string SourceAuthorityUnavailable = "The exact effective Bioware source/profile authority is unavailable.";
    public const string PinnedCatalogMismatch = "The carried base bioware.xml bytes do not match the pinned Chummer5 authority.";
    public const string ImprovementsUnsupported = "Saved improvements are unsupported because exact Bioware cost and Essence replay cannot be proven.";
    public const string CatalogEmpty = "The effective source profile exposes no side-effect-free top-level Bioware rows.";
    public const string StaleRevision = "The character content revision changed after the Bioware purchase was prepared.";
    public const string StaleCharacter = "The character bytes changed after the Bioware purchase was prepared.";
    public const string StaleCatalog = "The effective Bioware catalog authority changed after the purchase was prepared.";
    public const string StaleQuote = "The typed Bioware quote changed after the purchase was confirmed.";
    public const string StaleUndoReceipt = "The commit-issued Bioware undo receipt is stale or altered.";
    public const string IdentityInvalid = "Source, grade, configuration, quote, new instance, and expense identities must be distinct valid authorities.";
}

public static class CharacterBiowarePurchaseRules
{
    public const decimal MinimumMarkupPercent = -99m;
    public const decimal MaximumMarkupPercent = 1_000m;
    public const int MarkupDecimalPlaces = 2;
    public const int MinimumEssenceDiscountPercent = -100;
    public const int MaximumEssenceDiscountPercent = 100;

    public static CharacterBiowarePurchaseQuote Quote(
        CharacterBiowarePurchasePreparation preparation,
        CharacterBiowarePurchaseSelection selection)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(selection);
        if (!preparation.Exact)
            return Blocked(selection, preparation.Blockers.Count == 0 ? null : preparation.Blockers[0]);
        if (selection.ConfigurationId.Value == Guid.Empty
            || selection.SourceId.Value == Guid.Empty
            || selection.GradeId.Value == Guid.Empty)
            return Blocked(selection, CharacterBiowarePurchaseBlockers.IdentityInvalid);
        if (selection.Rating != 0)
            return Blocked(selection, "The bounded catalog admits only fixed-rating Bioware; rating must be zero.");
        if (selection.MarkupPercent < MinimumMarkupPercent
            || selection.MarkupPercent > MaximumMarkupPercent
            || decimal.Round(selection.MarkupPercent, MarkupDecimalPlaces, MidpointRounding.AwayFromZero)
               != selection.MarkupPercent)
            return Blocked(selection, "Markup must be an exact value from -99.00 through 1000.00 with at most two decimal places.");
        if (selection.EssenceDiscountPercent < MinimumEssenceDiscountPercent
            || selection.EssenceDiscountPercent > MaximumEssenceDiscountPercent
            || selection.EssenceDiscountPercent != 0 && !preparation.Settings.AllowEssenceDiscounts)
            return Blocked(selection, "The integer Essence discount is outside the exact enabled settings profile.");

        CharacterBiowarePurchaseCatalogEntry[] entries = preparation.Entries
            .Where(candidate => candidate.SourceId == selection.SourceId).Take(2).ToArray();
        if (entries.Length != 1)
            return Blocked(selection, "The selected Bioware source ID is absent or ambiguous.");
        CharacterBiowarePurchaseCatalogEntry entry = entries[0];
        CharacterBiowarePurchaseGrade[] grades = entry.Grades
            .Where(candidate => candidate.Id == selection.GradeId).Take(2).ToArray();
        if (grades.Length != 1)
            return Blocked(selection, "The selected Bioware grade ID is absent or ambiguous.");
        CharacterBiowarePurchaseGrade grade = grades[0];
        if (entry.BannedGrades.Contains(grade.Name, StringComparer.Ordinal)
            || entry.ForcedGrade.Length != 0
               && !string.Equals(entry.ForcedGrade, grade.Name, StringComparison.Ordinal))
            return Blocked(selection, "The selected grade violates the source row's exact grade constraints.");
        if (selection.BlackMarketDiscount && !entry.BlackMarketEligible)
            return Blocked(selection, "Black-market discount is unavailable for the selected source category.");
        if (preparation.ExCon && entry.Legality is CharacterBiowareLegality.Restricted or CharacterBiowareLegality.Forbidden)
            return Blocked(selection, "Ex-Con characters cannot buy restricted or forbidden Bioware in this lane.");
        if (!decimal.TryParse(entry.CostExpression, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal sourceCost)
            || !decimal.TryParse(entry.EssenceExpression, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal sourceEssence)
            || sourceCost < 0m || sourceEssence < 0m)
            return Blocked(selection, "The selected source row no longer has fixed exact cost and Essence values.");

        try
        {
            int finalAvailability = checked(entry.BaseAvailability + grade.AvailabilityModifier);
            decimal baseCost = checked(sourceCost * grade.CostMultiplier);
            decimal chargedCost = baseCost;
            if (selection.BlackMarketDiscount)
                chargedCost = checked(chargedCost * 0.9m);
            if (entry.Legality == CharacterBiowareLegality.Restricted
                && preparation.Settings.MultiplyRestrictedCost)
                chargedCost = checked(chargedCost * preparation.Settings.RestrictedCostMultiplier);
            else if (entry.Legality == CharacterBiowareLegality.Forbidden
                     && preparation.Settings.MultiplyForbiddenCost)
                chargedCost = checked(chargedCost * preparation.Settings.ForbiddenCostMultiplier);
            if (selection.MarkupPercent != 0m)
                chargedCost = checked(chargedCost * (1m + selection.MarkupPercent / 100m));
            if (selection.FreeCost)
                chargedCost = 0m;
            if (chargedCost < 0m || chargedCost > preparation.AvailableNuyen)
                return Blocked(selection, "The exact purchase price is invalid or exceeds available Nuyen.");

            decimal essenceMultiplier = grade.EssenceMultiplier;
            if (selection.EssenceDiscountPercent != 0)
                essenceMultiplier = checked(essenceMultiplier * (1m - selection.EssenceDiscountPercent / 100m));
            decimal installedEssence = checked(sourceEssence * Math.Max(0m, essenceMultiplier));
            if (!preparation.Settings.DoNotRoundEssenceInternally)
                installedEssence = decimal.Round(
                    installedEssence,
                    preparation.Settings.EssenceDecimals,
                    MidpointRounding.AwayFromZero);
            int centiEssence = StandardRound(checked(installedEssence * 100m));
            int? newHole = preparation.EssenceHoleRating.HasValue
                ? Math.Max(0, preparation.EssenceHoleRating.Value - centiEssence)
                : null;

            var unsigned = new CharacterBiowarePurchaseQuote(
                true,
                string.Empty,
                new CharacterBiowareQuoteId(string.Empty),
                selection.ConfigurationId,
                selection.SourceId,
                selection.GradeId,
                entry.Name,
                grade.Name,
                selection.Rating,
                baseCost,
                chargedCost,
                -chargedCost,
                installedEssence,
                entry.BaseAvailability,
                grade.AvailabilityModifier,
                finalAvailability,
                entry.Legality,
                newHole,
                preparation.EssenceAntiHoleRating);
            return unsigned with
            {
                QuoteId = new CharacterBiowareQuoteId(ComputeQuoteDigest(preparation, selection, unsigned))
            };
        }
        catch (Exception exception) when (exception is OverflowException or ArgumentOutOfRangeException)
        {
            return Blocked(selection, "Bioware purchase arithmetic exceeded the exact saved-data range.");
        }
    }

    public static string ComputeCatalogAuthorityDigest(CharacterBiowarePurchaseCatalogAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        List<string> lines =
        [
            "career-bioware-purchase-catalog-v1",
            authority.Binding.SettingsProfileId,
            authority.Binding.ProfileDigest,
            authority.Binding.RawBiowareXmlDigest,
            authority.Binding.EffectiveBiowareInputsDigest,
            authority.Binding.SelectedBiowareCustomDataInputsDigest,
            authority.Binding.EffectiveSettingsInputsDigest,
            authority.Settings.AllowEssenceDiscounts.ToString(CultureInfo.InvariantCulture),
            authority.Settings.MultiplyRestrictedCost.ToString(CultureInfo.InvariantCulture),
            authority.Settings.RestrictedCostMultiplier.ToString(CultureInfo.InvariantCulture),
            authority.Settings.MultiplyForbiddenCost.ToString(CultureInfo.InvariantCulture),
            authority.Settings.ForbiddenCostMultiplier.ToString(CultureInfo.InvariantCulture),
            authority.Settings.EssenceDecimals.ToString(CultureInfo.InvariantCulture),
            authority.Settings.DoNotRoundEssenceInternally.ToString(CultureInfo.InvariantCulture)
        ];
        lines.AddRange(authority.Settings.BannedGrades.OrderBy(value => value, StringComparer.Ordinal)
            .Select(value => $"profile-banned-grade:{value}"));
        lines.AddRange(CharacterBiowarePurchaseLegacyAuthority.CanonicalInputs);
        foreach (CharacterBiowarePurchaseCatalogEntry entry in authority.Entries.OrderBy(item => item.SourceId.Value))
        {
            lines.Add(string.Join("|", "entry", entry.SourceId.Value.ToString("D"), entry.Name, entry.Category,
                entry.EssenceExpression, entry.CapacityExpression,
                entry.BaseAvailability.ToString(CultureInfo.InvariantCulture), entry.Legality,
                entry.AvailabilityExpression, entry.CostExpression, entry.SourceBook, entry.Page,
                entry.BlackMarketEligible.ToString(CultureInfo.InvariantCulture),
                entry.IsGeneware.ToString(CultureInfo.InvariantCulture), entry.ForcedGrade,
                string.Join(",", entry.BannedGrades)));
            lines.AddRange(entry.Grades.OrderBy(item => item.Id.Value).Select(grade => string.Join("|",
                "grade", grade.Id.Value.ToString("D"), grade.Name,
                grade.CostMultiplier.ToString(CultureInfo.InvariantCulture),
                grade.EssenceMultiplier.ToString(CultureInfo.InvariantCulture),
                grade.AvailabilityModifier.ToString(CultureInfo.InvariantCulture),
                grade.SourceBook, grade.Page)));
        }
        lines.AddRange(authority.Exclusions.OrderBy(item => item.SourceId.Value).Select(exclusion =>
            string.Join("|", "exclude", exclusion.SourceId.Value.ToString("D"), exclusion.Name, exclusion.Reason)));
        return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines))));
    }

    public static string ComputeCharacterDigest(string characterXml)
        => Hex(SHA256.HashData(Encoding.UTF8.GetBytes(characterXml ?? string.Empty)));

    public static bool IsCanonicalDigest(string? digest)
        => digest is { Length: 64 } && digest.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static int StandardRound(decimal value)
        => decimal.ToInt32(value >= 0m ? decimal.Ceiling(value) : decimal.Floor(value));

    public static string ComputeUndoReceiptDigest(CharacterBiowarePurchaseUndoReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        string canonical = string.Join("\n",
            "career-bioware-purchase-undo-v1",
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture), receipt.CharacterDigest,
            receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture), receipt.PreviousCharacterDigest,
            receipt.PreviousAvailableNuyen.ToString(CultureInfo.InvariantCulture),
            receipt.PreviousEssenceHoleRating?.ToString(CultureInfo.InvariantCulture) ?? "absent",
            receipt.PreviousEssenceAntiHoleRating?.ToString(CultureInfo.InvariantCulture) ?? "absent",
            receipt.CatalogDigest, receipt.QuoteId.Value,
            receipt.SourceId.Value.ToString("D"), receipt.GradeId.Value.ToString("D"),
            receipt.Selection.ConfigurationId.Value.ToString("D"),
            receipt.Selection.SourceId.Value.ToString("D"), receipt.Selection.GradeId.Value.ToString("D"),
            receipt.Selection.Rating.ToString(CultureInfo.InvariantCulture),
            receipt.Selection.EssenceDiscountPercent.ToString(CultureInfo.InvariantCulture),
            receipt.Selection.BlackMarketDiscount.ToString(CultureInfo.InvariantCulture),
            receipt.Selection.MarkupPercent.ToString(CultureInfo.InvariantCulture),
            receipt.Selection.FreeCost.ToString(CultureInfo.InvariantCulture),
            receipt.InstanceId.Value.ToString("D"), receipt.ExpenseId.ToString("D"),
            receipt.ExpenseDate.ToString("O", CultureInfo.InvariantCulture),
            receipt.NuyenDelta.ToString(CultureInfo.InvariantCulture),
            receipt.BiowareXmlDigest, receipt.ExpenseXmlDigest);
        return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    internal static string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();

    private static CharacterBiowarePurchaseQuote Blocked(
        CharacterBiowarePurchaseSelection selection,
        string? reason)
        => new(
            false,
            string.IsNullOrWhiteSpace(reason) ? "Exact Bioware purchase authority is unavailable." : reason,
            new CharacterBiowareQuoteId(string.Empty),
            selection.ConfigurationId,
            selection.SourceId,
            selection.GradeId,
            string.Empty,
            string.Empty,
            selection.Rating,
            0m,
            0m,
            0m,
            0m,
            0,
            0,
            0,
            CharacterBiowareLegality.Legal,
            null,
            null);

    private static string ComputeQuoteDigest(
        CharacterBiowarePurchasePreparation preparation,
        CharacterBiowarePurchaseSelection selection,
        CharacterBiowarePurchaseQuote quote)
    {
        string canonical = string.Join("\n",
            "career-bioware-purchase-quote-v1",
            preparation.ContentRevision.ToString(CultureInfo.InvariantCulture), preparation.CharacterDigest,
            preparation.CatalogDigest, selection.ConfigurationId.Value.ToString("D"),
            selection.SourceId.Value.ToString("D"), selection.GradeId.Value.ToString("D"),
            selection.Rating.ToString(CultureInfo.InvariantCulture),
            selection.EssenceDiscountPercent.ToString(CultureInfo.InvariantCulture),
            selection.BlackMarketDiscount.ToString(CultureInfo.InvariantCulture),
            selection.MarkupPercent.ToString(CultureInfo.InvariantCulture),
            selection.FreeCost.ToString(CultureInfo.InvariantCulture), quote.Name, quote.GradeName,
            quote.BaseCost.ToString(CultureInfo.InvariantCulture),
            quote.ChargedCost.ToString(CultureInfo.InvariantCulture),
            quote.NuyenDelta.ToString(CultureInfo.InvariantCulture),
            quote.InstalledEssence.ToString(CultureInfo.InvariantCulture),
            quote.BaseAvailability.ToString(CultureInfo.InvariantCulture),
            quote.GradeAvailabilityModifier.ToString(CultureInfo.InvariantCulture),
            quote.FinalAvailability.ToString(CultureInfo.InvariantCulture), quote.Legality,
            quote.NewEssenceHoleRating?.ToString(CultureInfo.InvariantCulture) ?? "absent",
            quote.NewEssenceAntiHoleRating?.ToString(CultureInfo.InvariantCulture) ?? "absent");
        return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
