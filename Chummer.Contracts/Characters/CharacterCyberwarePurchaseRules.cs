using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public readonly record struct CharacterCyberwareSourceId(Guid Value);

public readonly record struct CharacterCyberwareGradeId(Guid Value);

public readonly record struct CharacterCyberwareInstanceId(Guid Value);

public sealed record CharacterCyberwarePurchaseGrade(
    CharacterCyberwareGradeId Id,
    string Name,
    decimal CostMultiplier,
    decimal EssenceMultiplier,
    int AvailabilityModifier);

public sealed record CharacterCyberwarePurchaseCatalogEntry(
    CharacterCyberwareSourceId SourceId,
    string Name,
    string Category,
    string EssenceExpression,
    string CapacityExpression,
    string AvailabilityExpression,
    string CostExpression,
    string SourceBook,
    string Page,
    bool BlackMarketEligible,
    string ForcedGrade,
    IReadOnlyList<string> BannedGrades,
    IReadOnlyList<CharacterCyberwarePurchaseGrade> Grades);

public sealed record CharacterCyberwarePurchaseCatalogExclusion(
    CharacterCyberwareSourceId SourceId,
    string Name,
    string Reason);

public sealed record CharacterCyberwarePurchaseSettings(
    bool AllowEssenceDiscounts,
    bool MultiplyRestrictedCost,
    decimal RestrictedCostMultiplier,
    bool MultiplyForbiddenCost,
    decimal ForbiddenCostMultiplier,
    int EssenceDecimals,
    bool DoNotRoundEssenceInternally,
    IReadOnlyList<string> BannedGrades);

public sealed record CharacterCyberwarePurchasePreparation(
    bool Exact,
    IReadOnlyList<string> Blockers,
    long ContentRevision,
    string CharacterDigest,
    string CatalogDigest,
    string SettingsProfileId,
    string CyberwareXmlDigest,
    decimal AvailableNuyen,
    bool ExCon,
    int? EssenceHoleRating,
    int? EssenceAntiHoleRating,
    CharacterCyberwarePurchaseSettings Settings,
    IReadOnlyList<CharacterCyberwarePurchaseCatalogEntry> Entries,
    IReadOnlyList<CharacterCyberwarePurchaseCatalogExclusion> Exclusions);

public sealed record CharacterCyberwarePurchaseSelection(
    CharacterCyberwareSourceId SourceId,
    CharacterCyberwareGradeId GradeId,
    int Rating,
    int EssenceDiscountPercent,
    bool BlackMarketDiscount,
    decimal MarkupPercent,
    bool FreeCost);

public sealed record CharacterCyberwarePurchaseQuote(
    bool Exact,
    string BlockReason,
    CharacterCyberwareSourceId SourceId,
    CharacterCyberwareGradeId GradeId,
    string Name,
    string GradeName,
    int Rating,
    decimal BaseCost,
    decimal ChargedCost,
    decimal NuyenDelta,
    decimal InstalledEssence,
    int? NewEssenceHoleRating,
    int? NewEssenceAntiHoleRating,
    string QuoteDigest);

public sealed record CharacterCyberwarePurchaseCommand(
    long ExpectedContentRevision,
    string ExpectedCharacterDigest,
    string ExpectedCatalogDigest,
    string ExpectedQuoteDigest,
    CharacterCyberwarePurchaseSelection Selection,
    CharacterCyberwareInstanceId NewInstanceId,
    Guid NewExpenseId,
    DateTimeOffset ExpenseDate);

public sealed record CharacterCyberwarePurchaseUndoReceipt(
    long ContentRevision,
    string CharacterDigest,
    long PreviousContentRevision,
    string PreviousCharacterDigest,
    decimal PreviousAvailableNuyen,
    int? PreviousEssenceHoleRating,
    int? PreviousEssenceAntiHoleRating,
    string CatalogDigest,
    string QuoteDigest,
    CharacterCyberwareSourceId SourceId,
    CharacterCyberwareGradeId GradeId,
    CharacterCyberwarePurchaseSelection Selection,
    CharacterCyberwareInstanceId InstanceId,
    Guid ExpenseId,
    DateTimeOffset ExpenseDate,
    decimal NuyenDelta,
    string CyberwareXmlDigest,
    string ExpenseXmlDigest,
    string ReceiptDigest);

public sealed record CharacterCyberwarePurchaseUndoCommand(
    CharacterCyberwarePurchaseUndoReceipt? Receipt);

public sealed record CharacterCyberwarePurchaseCommitResult(
    bool Committed,
    string BlockReason,
    long PreviousContentRevision,
    long NewContentRevision,
    string PreviousCharacterDigest,
    string NewCharacterDigest,
    string CharacterXml,
    CharacterCyberwareInstanceId InstanceId,
    Guid ExpenseId,
    decimal NuyenDelta,
    decimal EssenceHoleDelta,
    string CatalogDigest,
    string QuoteDigest,
    CharacterCyberwarePurchaseUndoReceipt? UndoReceipt);

/// <summary>
/// The exact legacy inputs audited for the bounded Career purchase lane. The
/// executable lane additionally requires the carried cyberware.xml bytes to
/// match the pinned digest before exposing any catalog row.
/// </summary>
public static class CharacterCyberwarePurchaseLegacyAuthority
{
    public const string Commit = "fe4355d06c98cd9b7feade89f5fc1a0e438f7ce3";
    public const string Tree = "20b66829ec2f6046878a0080c5fbe80fd7bb4459";
    public const string SelectCyberwareDesignerSha256 = "e213f8509a229cee4f8b7cefc63429c5a9995fb69c487781a5ac05fc0e9df6cd";
    public const string SelectCyberwareSha256 = "a6d31bdc826cc6b1201d6c70cf3f53f54350ebd829694024d5ec951a40ada175";
    public const string CharacterCreateSha256 = "33099799d70fd8ddfad9e4c129e1d416caabd7d375edbdfea14bc599005acd06";
    public const string CharacterCareerSha256 = "b1f58def07884877638e7c31a5af194a5ce8869c0020447154f827ba56e813ea";
    public const string CyberwareSha256 = "da1f3a596860be62f96b232724acef5b326c320a17dbc1c8b0570403c3b9220d";
    public const string ExpensesSha256 = "5a8376ffb23f57f2206ca1d23493220b1c0efd4bd3ffdaf85506ca15de9738e8";
    public const string CharacterSettingsSha256 = "5fae3d58aa0b0c30920bc4180430ab56250521e5b8db21097b0b9460f74ef943";
    public const string ImprovementManagerSha256 = "0ba804cd4549ac2497e152a1f0aa2f32b17f38cc62da37585c1ecffe70988ffe";
    public const string CharacterSha256 = "ab744d6afedb25683459622a37da12fb12eac421c67661a421cfcc4c42ab9f9e";
    public const string DecimalExtensionsSha256 = "b60e05f94606721cd4ef8087ef9d2ea9b3bda2aabd3371c8f699d0e083cd1ffd";
    public const string GlobalSettingsSha256 = "bf22d91a6ba2d3b24092fa70d00c08d92b62a519ac59dc28fd51c64beb05a577";
    public const string ColorManagerSha256 = "7da611d6219eb8753d5fbb6220c41ca9d5e233f6d38123d1e60bf8bc44df59a1";
    public const string CyberwareXmlSha256 = "8843bcec100e15cd01a01826a1fbe40205853a05adc98c50c82f568662db4fd3";

    public static IReadOnlyList<string> CanonicalInputs { get; } =
    [
        $"commit:{Commit}",
        $"tree:{Tree}",
        $"Chummer/Forms/Selection Forms/SelectCyberware.Designer.cs:{SelectCyberwareDesignerSha256}",
        $"Chummer/Forms/Selection Forms/SelectCyberware.cs:{SelectCyberwareSha256}",
        $"Chummer/Forms/Character Forms/CharacterCreate.cs:{CharacterCreateSha256}",
        $"Chummer/Forms/Character Forms/CharacterCareer.cs:{CharacterCareerSha256}",
        $"Chummer/Backend/Equipment/Cyberware.cs:{CyberwareSha256}",
        $"Chummer/Backend/Uniques/Expenses.cs:{ExpensesSha256}",
        $"Chummer/Backend/Character Settings/CharacterSettings.cs:{CharacterSettingsSha256}",
        $"Chummer/Backend/Static/Managers/ImprovementManager.cs:{ImprovementManagerSha256}",
        $"Chummer/Backend/Characters/Character.cs:{CharacterSha256}",
        $"Chummer/Backend/Static/Extensions/DecimalExtensions.cs:{DecimalExtensionsSha256}",
        $"Chummer/Backend/Static/GlobalSettings.cs:{GlobalSettingsSha256}",
        $"Chummer/Backend/Static/Managers/ColorManager.cs:{ColorManagerSha256}",
        $"Chummer/data/cyberware.xml:{CyberwareXmlSha256}"
    ];
}

public static class CharacterCyberwarePurchaseBlockers
{
    public const string NotCareer = "Cyberware purchase is available only for a saved Career character with created=true.";
    public const string SourceAuthorityUnavailable = "The exact Cyberware source/profile authority is unavailable.";
    public const string PinnedCatalogMismatch = "The carried cyberware.xml bytes do not match the pinned Chummer5 authority.";
    public const string OverlaysUnsupported = "Enabled content overlays are unsupported by the bounded Cyberware purchase lane.";
    public const string ImprovementsUnsupported = "Saved improvements are unsupported because exact Cyberware cost and Essence replay cannot be proven.";
    public const string CatalogEmpty = "The exact source profile exposes no side-effect-free top-level Cyberware rows.";
    public const string StaleRevision = "The character content revision changed after the Cyberware purchase was prepared.";
    public const string StaleCharacter = "The character bytes changed after the Cyberware purchase was prepared.";
    public const string StaleCatalog = "The Cyberware catalog authority changed after the purchase was prepared.";
    public const string StaleQuote = "The Cyberware quote changed after the purchase was confirmed.";
    public const string StaleUndoReceipt = "The commit-issued Cyberware undo receipt is stale or altered.";
    public const string IdentityInvalid = "Source, grade, new instance, and expense identities must be distinct valid GUID authorities.";
}

/// <summary>
/// Pure quote authority. The bounded first slice intentionally admits fixed
/// rating rows only; resolver-side exclusion prevents a dynamic expression from
/// reaching this calculator.
/// </summary>
public static class CharacterCyberwarePurchaseRules
{
    public const decimal MinimumMarkupPercent = -99m;
    public const decimal MaximumMarkupPercent = 1_000m;
    public const int MarkupDecimalPlaces = 2;
    public const int MinimumEssenceDiscountPercent = -100;
    public const int MaximumEssenceDiscountPercent = 100;

    public static CharacterCyberwarePurchaseQuote Quote(
        CharacterCyberwarePurchasePreparation preparation,
        CharacterCyberwarePurchaseSelection selection)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(selection);
        if (!preparation.Exact)
            return Blocked(selection, preparation.Blockers.Count == 0 ? null : preparation.Blockers[0]);
        if (selection.SourceId.Value == Guid.Empty || selection.GradeId.Value == Guid.Empty)
            return Blocked(selection, CharacterCyberwarePurchaseBlockers.IdentityInvalid);
        if (selection.Rating != 0)
            return Blocked(selection, "The bounded catalog admits only fixed-rating Cyberware; rating must be zero.");
        if (selection.MarkupPercent < MinimumMarkupPercent
            || selection.MarkupPercent > MaximumMarkupPercent
            || decimal.Round(selection.MarkupPercent, MarkupDecimalPlaces, MidpointRounding.AwayFromZero)
               != selection.MarkupPercent)
        {
            return Blocked(selection, "Markup must be an exact value from -99.00 through 1000.00 with at most two decimal places.");
        }
        if (selection.EssenceDiscountPercent < MinimumEssenceDiscountPercent
            || selection.EssenceDiscountPercent > MaximumEssenceDiscountPercent
            || selection.EssenceDiscountPercent != 0 && !preparation.Settings.AllowEssenceDiscounts)
        {
            return Blocked(selection, "The integer Essence discount is outside the exact enabled settings profile.");
        }

        CharacterCyberwarePurchaseCatalogEntry[] entries = preparation.Entries
            .Where(candidate => candidate.SourceId == selection.SourceId)
            .Take(2)
            .ToArray();
        if (entries.Length != 1)
            return Blocked(selection, "The selected Cyberware source ID is absent or ambiguous.");
        CharacterCyberwarePurchaseCatalogEntry entry = entries[0];
        CharacterCyberwarePurchaseGrade[] grades = entry.Grades
            .Where(candidate => candidate.Id == selection.GradeId)
            .Take(2)
            .ToArray();
        if (grades.Length != 1)
            return Blocked(selection, "The selected Cyberware grade ID is absent or ambiguous.");
        CharacterCyberwarePurchaseGrade grade = grades[0];
        if (entry.BannedGrades.Contains(grade.Name, StringComparer.Ordinal)
            || entry.ForcedGrade.Length != 0
               && !string.Equals(entry.ForcedGrade, grade.Name, StringComparison.Ordinal))
        {
            return Blocked(selection, "The selected grade violates the source row's exact grade constraints.");
        }
        if (selection.BlackMarketDiscount && !entry.BlackMarketEligible)
            return Blocked(selection, "Black-market discount is unavailable for the selected source category.");
        if (!decimal.TryParse(entry.CostExpression, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal sourceCost)
            || !decimal.TryParse(entry.EssenceExpression, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal sourceEssence)
            || sourceCost < 0m
            || sourceEssence < 0m)
        {
            return Blocked(selection, "The selected source row no longer has fixed exact cost and Essence values.");
        }

        try
        {
            decimal baseCost = checked(sourceCost * grade.CostMultiplier);
            decimal chargedCost = baseCost;
            if (selection.BlackMarketDiscount)
                chargedCost = checked(chargedCost * 0.9m);
            char suffix = AvailabilitySuffix(entry.AvailabilityExpression);
            if (suffix == 'R' && preparation.Settings.MultiplyRestrictedCost)
                chargedCost = checked(chargedCost * preparation.Settings.RestrictedCostMultiplier);
            else if (suffix == 'F' && preparation.Settings.MultiplyForbiddenCost)
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

            var unsigned = new CharacterCyberwarePurchaseQuote(
                Exact: true,
                BlockReason: string.Empty,
                selection.SourceId,
                selection.GradeId,
                entry.Name,
                grade.Name,
                selection.Rating,
                baseCost,
                chargedCost,
                NuyenDelta: -chargedCost,
                installedEssence,
                newHole,
                preparation.EssenceAntiHoleRating,
                QuoteDigest: string.Empty);
            return unsigned with { QuoteDigest = ComputeQuoteDigest(preparation, selection, unsigned) };
        }
        catch (Exception exception) when (exception is OverflowException or ArgumentOutOfRangeException)
        {
            return Blocked(selection, "Cyberware purchase arithmetic exceeded the exact saved-data range.");
        }
    }

    public static string ComputeCharacterDigest(string characterXml)
        => Hex(SHA256.HashData(Encoding.UTF8.GetBytes(characterXml ?? string.Empty)));

    public static bool IsCanonicalDigest(string? digest)
        => digest is { Length: 64 } && digest.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>
    /// Exact pinned Chummer5 DecimalExtensions.StandardRound semantics:
    /// every non-integral value rounds away from zero, not only midpoints.
    /// </summary>
    public static int StandardRound(decimal value)
        => decimal.ToInt32(value >= 0m ? decimal.Ceiling(value) : decimal.Floor(value));

    public static string ComputeUndoReceiptDigest(CharacterCyberwarePurchaseUndoReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        string canonical = string.Join(
            "\n",
            "career-cyberware-purchase-undo-v1",
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture),
            receipt.CharacterDigest,
            receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture),
            receipt.PreviousCharacterDigest,
            receipt.PreviousAvailableNuyen.ToString(CultureInfo.InvariantCulture),
            receipt.PreviousEssenceHoleRating?.ToString(CultureInfo.InvariantCulture) ?? "absent",
            receipt.PreviousEssenceAntiHoleRating?.ToString(CultureInfo.InvariantCulture) ?? "absent",
            receipt.CatalogDigest,
            receipt.QuoteDigest,
            receipt.SourceId.Value.ToString("D"),
            receipt.GradeId.Value.ToString("D"),
            receipt.Selection.SourceId.Value.ToString("D"),
            receipt.Selection.GradeId.Value.ToString("D"),
            receipt.Selection.Rating.ToString(CultureInfo.InvariantCulture),
            receipt.Selection.EssenceDiscountPercent.ToString(CultureInfo.InvariantCulture),
            receipt.Selection.BlackMarketDiscount.ToString(CultureInfo.InvariantCulture),
            receipt.Selection.MarkupPercent.ToString(CultureInfo.InvariantCulture),
            receipt.Selection.FreeCost.ToString(CultureInfo.InvariantCulture),
            receipt.InstanceId.Value.ToString("D"),
            receipt.ExpenseId.ToString("D"),
            receipt.ExpenseDate.ToString("O", CultureInfo.InvariantCulture),
            receipt.NuyenDelta.ToString(CultureInfo.InvariantCulture),
            receipt.CyberwareXmlDigest,
            receipt.ExpenseXmlDigest);
        return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    internal static string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();

    private static char AvailabilitySuffix(string expression)
    {
        string value = expression.Trim();
        return value.EndsWith('R') ? 'R' : value.EndsWith('F') ? 'F' : '\0';
    }

    private static CharacterCyberwarePurchaseQuote Blocked(
        CharacterCyberwarePurchaseSelection selection,
        string? reason)
        => new(
            Exact: false,
            BlockReason: string.IsNullOrWhiteSpace(reason)
                ? "Exact Cyberware purchase authority is unavailable."
                : reason,
            selection.SourceId,
            selection.GradeId,
            Name: string.Empty,
            GradeName: string.Empty,
            selection.Rating,
            BaseCost: 0m,
            ChargedCost: 0m,
            NuyenDelta: 0m,
            InstalledEssence: 0m,
            NewEssenceHoleRating: null,
            NewEssenceAntiHoleRating: null,
            QuoteDigest: string.Empty);

    private static string ComputeQuoteDigest(
        CharacterCyberwarePurchasePreparation preparation,
        CharacterCyberwarePurchaseSelection selection,
        CharacterCyberwarePurchaseQuote quote)
    {
        string canonical = string.Join(
            "\n",
            "career-cyberware-purchase-quote-v1",
            preparation.ContentRevision.ToString(CultureInfo.InvariantCulture),
            preparation.CharacterDigest,
            preparation.CatalogDigest,
            selection.SourceId.Value.ToString("D"),
            selection.GradeId.Value.ToString("D"),
            selection.Rating.ToString(CultureInfo.InvariantCulture),
            selection.EssenceDiscountPercent.ToString(CultureInfo.InvariantCulture),
            selection.BlackMarketDiscount.ToString(CultureInfo.InvariantCulture),
            selection.MarkupPercent.ToString(CultureInfo.InvariantCulture),
            selection.FreeCost.ToString(CultureInfo.InvariantCulture),
            quote.Name,
            quote.GradeName,
            quote.BaseCost.ToString(CultureInfo.InvariantCulture),
            quote.ChargedCost.ToString(CultureInfo.InvariantCulture),
            quote.NuyenDelta.ToString(CultureInfo.InvariantCulture),
            quote.InstalledEssence.ToString(CultureInfo.InvariantCulture),
            quote.NewEssenceHoleRating?.ToString(CultureInfo.InvariantCulture) ?? "absent",
            quote.NewEssenceAntiHoleRating?.ToString(CultureInfo.InvariantCulture) ?? "absent");
        return Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
