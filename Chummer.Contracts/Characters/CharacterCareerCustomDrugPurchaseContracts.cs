using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerCustomDrugPurchaseOutcome
{
    Available, Applied, Replayed, NotFound, IdempotencyConflict, Conflict, Corrupt,
    Unavailable, Missing, UnknownOutcome, Canceled
}

/// <summary>Normal additional doses for an explicitly selected saved instance; no new stack or price options.</summary>
public sealed record CharacterCareerCustomDrugPurchaseRequest(
    CharacterWorkspaceId WorkspaceId, Guid OperationId, Guid TargetDrugId, decimal AdditionalQuantity,
    DateTime ExpenseDateLocal);

public sealed record CharacterCareerCustomDrugPurchaseBinding(string OwnerId, CharacterWorkspaceId WorkspaceId,
    long WorkspaceRevision, string SourceDigest, string AuxiliaryStateDigest, string RuleStateDigest,
    string CatalogDigest, string RulesDigest, string SettingsProfileId, string RecipeDigest, string RuntimeDigest);

/// <summary>Persist the complete original confirmed command before one dispatch. Recovery uses Lookup only.</summary>
public sealed record CharacterCareerCustomDrugPurchaseCommand(CharacterCareerCustomDrugPurchaseRequest Request,
    CharacterCareerCustomDrugPurchaseBinding Binding, bool ExplicitlyConfirmed = false, string? ExpectedPreviewDigest = null);

public sealed record CharacterCareerCustomDrugPurchaseCatalogQuery(CharacterWorkspaceId WorkspaceId, long ExpectedWorkspaceRevision);

public enum CharacterCareerCustomDrugPurchaseQuantityUnit { Dose }

/// <summary>Maximum is the additional dose bound; MaximumResultingQuantity bounds the resulting saved stack.</summary>
public sealed record CharacterCareerCustomDrugPurchaseCatalogQuantity(CharacterCareerCustomDrugPurchaseQuantityUnit Unit,
    int DecimalPlaces, decimal Minimum, decimal Maximum, decimal Increment, decimal DefaultValue,
    decimal MaximumResultingQuantity);

/// <summary>Pass this unchanged row selection to Preview. There is no implicit target-selection overload.</summary>
public sealed record CharacterCareerCustomDrugPurchaseCatalogSelection(Guid TargetDrugId,
    CharacterCareerCustomDrugPurchaseBinding Binding);

public sealed record CharacterCareerCustomDrugPurchaseCatalogRow(Guid TargetDrugId, string Name, string GradeName,
    decimal CurrentQuantity, bool Supported, CharacterCareerCustomDrugPurchaseCatalogQuantity? Quantity,
    IReadOnlyList<string> Blockers, CharacterCareerCustomDrugPurchaseCatalogSelection? Selection);

public sealed record CharacterCareerCustomDrugPurchaseCatalog(CharacterCareerCustomDrugPurchaseBinding Snapshot,
    IReadOnlyList<CharacterCareerCustomDrugPurchaseCatalogRow> Rows);

public sealed record CharacterCareerCustomDrugPurchaseCatalogResult(CharacterCareerCustomDrugPurchaseOutcome Outcome,
    CharacterCareerCustomDrugPurchaseCatalog? Catalog, long? CurrentWorkspaceRevision, IReadOnlyList<string> Blockers);

/// <summary>Immutable historical canonical evidence, not caller-supplied purchase authority. Price is Core's QuantityPurchase quote.</summary>
public sealed record CharacterCareerCustomDrugPurchaseQuote(CharacterCustomDrugPreparation Authority,
    string SettingsXml, string SettingsAuthority, CharacterCustomDrugSelection Recipe, string SavedDrugXmlBefore,
    CharacterCustomDrugQuote Price, decimal QuantityBefore, decimal QuantityAfter,
    decimal NuyenBefore, decimal NuyenAfter, Guid ExpenseId);

public sealed record CharacterCareerCustomDrugPurchasePreview(CharacterCareerCustomDrugPurchaseCommand Command,
    CharacterCareerCustomDrugPurchaseQuote Quote, string PreviewDigest);

/// <summary>Retains the full original command and quote so later saves or source outages never rebind old intent.</summary>
public sealed record CharacterCareerCustomDrugPurchaseReceipt(CharacterCareerCustomDrugPurchaseCommand Command,
    CharacterCareerCustomDrugPurchaseQuote Quote, string CommandDigest, long CommittedWorkspaceRevision,
    string CharacterPayloadDigestAfter, string PreviousReceiptDigest, string ReceiptDigest);

public sealed record CharacterCareerCustomDrugPurchasePreviewResult(CharacterCareerCustomDrugPurchaseOutcome Outcome,
    CharacterCareerCustomDrugPurchasePreview? Preview = null, long? CurrentWorkspaceRevision = null, string? Error = null);

/// <summary>UnknownOutcome requires lookup of the original operation/digest; never automatically dispatch again.</summary>
public sealed record CharacterCareerCustomDrugPurchaseResult(CharacterCareerCustomDrugPurchaseOutcome Outcome,
    CharacterCareerCustomDrugPurchaseReceipt? Receipt = null, long? CurrentWorkspaceRevision = null, string? Error = null);
