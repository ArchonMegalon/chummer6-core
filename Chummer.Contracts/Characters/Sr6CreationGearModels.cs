namespace Chummer.Contracts.Characters;

public sealed record Sr6CreationGearChoice(Guid Id, string CatalogId, int Quantity);
public sealed record Sr6CreationGearSelection(IReadOnlyList<Sr6CreationGearChoice> Items);

public static class Sr6CreationGearLimits
{
    // Technical transport bounds, not tabletop ownership limits.
    public const int MaximumItems = 128;
    public const int MaximumQuantity = 999;
}

/// <summary>Core-priced purchase option, not an equipped item's runtime statistics.</summary>
public sealed record Sr6CreationGearOption(string Id, string SourceName, string CategoryId,
    int Availability, string Legality, decimal BasePrice, int SizeSurchargePercent,
    decimal UnitPrice, int? Rating, bool Available, string? UnavailableReason, string SourceAnchorId);

public sealed record Sr6CreationGearValue(Sr6CreationGearChoice Choice, Sr6CreationGearOption Option, decimal TotalPrice);
public sealed record Sr6CreationGearPreview(IReadOnlyList<Sr6CreationGearValue> Items,
    decimal ResourcesNuyen, decimal SpentNuyen, decimal RemainingNuyen, decimal MaximumCarryOverNuyen,
    decimal UnspentAboveCarryOver, bool RestrictedItemsNeedGmReview,
    string AuthorityDigest, IReadOnlyList<string> SourceAnchorIds);

public static class Sr6CreationGearBlockers
{
    public const string InvalidSelection = "sr6-creation-gear-invalid";
    public const string CatalogUnavailable = "sr6-creation-gear-catalog-unavailable";
    public const string AvailabilityExceeded = "sr6-creation-gear-availability";
    public const string BudgetExceeded = "sr6-creation-gear-budget";
}
