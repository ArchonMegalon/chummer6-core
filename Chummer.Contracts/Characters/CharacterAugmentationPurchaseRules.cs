namespace Chummer.Contracts.Characters;

/// <summary>Source identity, not the legacy XML container name.</summary>
public enum CharacterAugmentationKind
{
    Cyberware = 0,
    Bioware = 1
}

public sealed record CharacterAugmentationPurchaseBinding(string OwnerId, string WorkspaceId, Guid OperationId);

public sealed record CharacterAugmentationPurchaseSelection(
    CharacterAugmentationKind Kind,
    Guid SourceId,
    Guid GradeId,
    int Rating,
    int Quantity,
    int EssenceDiscountPercent,
    bool BlackMarketDiscount,
    decimal MarkupPercent,
    bool FreeCost);

public sealed record CharacterAugmentationPurchaseGrade(
    Guid Id, string Name, decimal CostMultiplier, decimal EssenceMultiplier, int AvailabilityModifier);

public sealed record CharacterAugmentationPurchaseCatalogEntry(
    CharacterAugmentationKind Kind,
    Guid SourceId,
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
    IReadOnlyList<CharacterAugmentationPurchaseGrade> Grades,
    int MinimumRating,
    int MaximumRating,
    string SourceXml);

public sealed record CharacterAugmentationPurchaseExclusion(Guid SourceId, string Name, string Reason);

public sealed record CharacterAugmentationPurchasePreparation(
    bool Exact,
    IReadOnlyList<string> Blockers,
    CharacterAugmentationKind Kind,
    long ContentRevision,
    string CharacterDigest,
    string CatalogDigest,
    string SettingsProfileId,
    string SourceXmlDigest,
    decimal AvailableNuyen,
    bool ExCon,
    int? EssenceHoleRating,
    int? EssenceAntiHoleRating,
    CharacterCyberwarePurchaseSettings Settings,
    IReadOnlyList<CharacterAugmentationPurchaseCatalogEntry> Entries,
    IReadOnlyList<CharacterAugmentationPurchaseExclusion> Exclusions,
    string CharacterXml,
    string SettingsXml,
    decimal? CurrentEssence = null,
    decimal? MaximumEssence = null);

/// <summary>Native Improvement.Save fields, bound to one purchased instance at materialization.</summary>
public sealed record CharacterAugmentationPurchaseEffect(
    string Type,
    string ImprovedName,
    decimal Value,
    int Minimum = 0,
    int Maximum = 0,
    decimal Augmented = 0m,
    int AugmentedMaximum = 0,
    int Rating = 1,
    string UniqueName = "",
    string Condition = "",
    string Exclude = "",
    string Target = "",
    int AddToRating = 0,
    string ImprovementSource = "Bioware");

public sealed record CharacterAugmentationPurchaseQuote(
    bool Allowed,
    string? BlockReason,
    CharacterAugmentationPurchaseSelection Selection,
    string GradeName,
    decimal ChargedCost,
    decimal NuyenDelta,
    decimal InstalledEssence,
    int? NewEssenceHoleRating,
    int? NewEssenceAntiHoleRating,
    decimal? PreviousEssence,
    decimal? NewEssence,
    IReadOnlyList<CharacterAugmentationPurchaseEffect> Effects,
    string QuoteDigest);

public sealed record CharacterAugmentationPurchaseCommand(
    CharacterAugmentationPurchaseBinding Binding,
    long ExpectedContentRevision,
    string ExpectedCharacterDigest,
    string ExpectedCatalogDigest,
    string ExpectedQuoteDigest,
    string ExpectedSettingsProfileId,
    CharacterAugmentationPurchaseSelection Selection,
    IReadOnlyList<Guid> NewInstanceIds,
    Guid NewExpenseId,
    DateTimeOffset ExpenseDate);

public sealed record CharacterAugmentationPurchaseReceipt(
    string Schema,
    CharacterAugmentationPurchaseBinding Binding,
    CharacterAugmentationKind Kind,
    Guid SourceId,
    Guid GradeId,
    string SettingsProfileId,
    long PreviousContentRevision,
    long NewContentRevision,
    string PreviousCharacterDigest,
    string CatalogDigest,
    string QuoteDigest,
    string CommandDigest,
    IReadOnlyList<Guid> InstanceIds,
    Guid ExpenseId,
    decimal NuyenDelta,
    string MaterializedDigest,
    string ReceiptDigest)
{
    /// <summary>Complete durable provenance permits cold lookup without a separate UI checkpoint.</summary>
    public CharacterAugmentationPurchaseCommand? Command { get; init; }
}

/// <summary>
/// Applied means a candidate XML is ready for the existing atomic workspace transaction.
/// It does not mean persisted. AlreadyApplied is source-independent durable receipt recovery.
/// A missing receipt returns neither flag; the caller must resolve unknown storage outcomes.
/// </summary>
public sealed record CharacterAugmentationPurchaseResult(
    bool Applied,
    bool AlreadyApplied,
    string? BlockReason,
    string CharacterXml,
    CharacterAugmentationPurchaseReceipt? Receipt);

public static class CharacterAugmentationPurchaseSchemas
{
    public const string ReceiptV1 = "chummer.sr5-career-augmentation-purchase.receipt.v1";
}
