using Chummer.Contracts.Workspaces;
using System.Text.Json.Serialization;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerGearPurchaseOutcome
{
    Available, Applied, Replayed, NotFound, IdempotencyConflict, Conflict, Corrupt,
    Unavailable, Missing, UnknownOutcome, Canceled
}

/// <summary>Only gear.xml sources. Default semantics are a new top-level instance, never an implicit merge.</summary>
public sealed record CharacterCareerGearPurchaseRequest(
    CharacterWorkspaceId WorkspaceId, Guid OperationId, Guid SourceId, int Rating, decimal Quantity,
    DateTime ExpenseDateLocal, decimal MarkupPercent = 0m, bool BlackMarketDiscount = false,
    Guid? ParentGearId = null, Guid? StackTargetId = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCareerGearPurchaseAmmunitionSelection? Ammunition { get; init; }
}

/// <summary>A required explicit choice from this source's effective weapon categories.</summary>
public sealed record CharacterCareerGearPurchaseAmmunitionSelection(string WeaponCategory, string AuthorityDigest);

/// <summary>Historical category witnesses, never permission to use caller-supplied source data.</summary>
public sealed record CharacterCareerGearPurchaseAmmunitionAuthority(string WeaponAuthorityDigest,
    IReadOnlyList<CharacterCreationLoadoutCatalogOption> CategoryWitnesses);

public sealed record CharacterCareerGearPurchaseSource(Guid SourceId, string Name, string SourceBook,
    string SourceXml, string CatalogDigest, string ProfileDigest, string CustomDataDigest)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCareerGearPurchaseAmmunitionAuthority? Ammunition { get; init; }
}

public sealed record CharacterCareerGearPurchaseBinding(string OwnerId, CharacterWorkspaceId WorkspaceId,
    long WorkspaceRevision, string SourceDigest, string AuxiliaryStateDigest, string RuleStateDigest, string RuntimeDigest);

/// <summary>Persist this exact command before committing; confirm only after displaying its quote.</summary>
public sealed record CharacterCareerGearPurchaseCommand(CharacterCareerGearPurchaseRequest Request,
    CharacterCareerGearPurchaseBinding Binding, bool ExplicitlyConfirmed = false, string? ExpectedPreviewDigest = null);

/// <summary>Retains source evidence for cold receipt validation, not permission to buy from caller-supplied XML.</summary>
public sealed record CharacterCareerGearPurchaseQuote(CharacterCareerGearPurchaseSource Source,
    string SettingsXml, string SettingsAuthority, decimal NuyenBefore, decimal UnitCost, decimal TotalCost,
    decimal NuyenAfter, int Availability, string Legality, Guid GearId, Guid ExpenseId, string GearXml);

public sealed record CharacterCareerGearPurchasePreview(CharacterCareerGearPurchaseCommand Command,
    CharacterCareerGearPurchaseQuote Quote, string PreviewDigest);

public sealed record CharacterCareerGearPurchaseReceipt(CharacterCareerGearPurchaseCommand Command,
    CharacterCareerGearPurchaseQuote Quote, string CommandDigest, long CommittedWorkspaceRevision,
    string CharacterPayloadDigestAfter, string PreviousReceiptDigest, string ReceiptDigest);

public sealed record CharacterCareerGearPurchasePreviewResult(CharacterCareerGearPurchaseOutcome Outcome,
    CharacterCareerGearPurchasePreview? Preview = null, long? CurrentWorkspaceRevision = null, string? Error = null);

/// <summary>UnknownOutcome must be reconciled using the original operation and command digest.</summary>
public sealed record CharacterCareerGearPurchaseResult(CharacterCareerGearPurchaseOutcome Outcome,
    CharacterCareerGearPurchaseReceipt? Receipt = null, long? CurrentWorkspaceRevision = null, string? Error = null);

/// <summary>A read at the caller's saved revision. No operation or purchase is dispatched.</summary>
public sealed record CharacterCareerGearPurchaseCatalogQuery(CharacterWorkspaceId WorkspaceId, long ExpectedWorkspaceRevision);

public enum CharacterCareerGearPurchaseQuantityUnit { Item, Currency, Nuyen, Round }

public sealed record CharacterCareerGearPurchaseCatalogQuantity(CharacterCareerGearPurchaseQuantityUnit Unit,
    int DecimalPlaces, decimal Minimum, decimal Maximum, decimal Increment, decimal DefaultValue, int PackageDivisor);

/// <summary>Each integer rating is evaluated by the same bounded authority as Preview.</summary>
public sealed record CharacterCareerGearPurchaseCatalogRating(int Value, IReadOnlyList<string> Blockers);

public sealed record CharacterCareerGearPurchaseCatalogRatings(int Minimum, int Maximum,
    IReadOnlyList<CharacterCareerGearPurchaseCatalogRating> Values);

public sealed record CharacterCareerGearPurchaseCatalogDecimalOption(decimal Minimum, decimal Maximum,
    decimal Increment, decimal DefaultValue);

public sealed record CharacterCareerGearPurchaseCatalogOption(bool Supported, string? Blocker = null);

public sealed record CharacterCareerGearPurchaseCatalogOptions(CharacterCareerGearPurchaseCatalogDecimalOption MarkupPercent,
    CharacterCareerGearPurchaseCatalogOption BlackMarketDiscount, CharacterCareerGearPurchaseCatalogOption ParentGear,
    CharacterCareerGearPurchaseCatalogOption StackPurchase);

/// <summary>Pass the selected row's unchanged selection to the three-argument Preview overload.</summary>
public sealed record CharacterCareerGearPurchaseCatalogSelection(Guid SourceId, CharacterCareerGearPurchaseBinding Binding);

/// <summary>Supported means a source-backed selection exists, not that every quantity/markup is affordable.
/// Preview must authorize the actual inputs. Blocked rows never carry a selection.</summary>
public sealed record CharacterCareerGearPurchaseCatalogRow(Guid SourceId, string Name, string SourceBook,
    bool Supported, CharacterCareerGearPurchaseCatalogRatings? Ratings, CharacterCareerGearPurchaseCatalogQuantity? Quantity,
    CharacterCareerGearPurchaseCatalogOptions Options, IReadOnlyList<string> Blockers,
    CharacterCareerGearPurchaseCatalogSelection? Selection)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CharacterCareerGearPurchaseAmmunitionSelection>? AmmunitionChoices { get; init; }
}

/// <summary>Snapshot.RuleStateDigest binds the whole catalog; only a row Selection is admitted by Preview.
/// No source XML or price expressions are exposed by this query.</summary>
public sealed record CharacterCareerGearPurchaseCatalog(CharacterCareerGearPurchaseBinding Snapshot,
    IReadOnlyList<CharacterCareerGearPurchaseCatalogRow> Rows);

public sealed record CharacterCareerGearPurchaseCatalogResult(CharacterCareerGearPurchaseOutcome Outcome,
    CharacterCareerGearPurchaseCatalog? Catalog, long? CurrentWorkspaceRevision, IReadOnlyList<string> Blockers);
