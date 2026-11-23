using System.Text.Json.Serialization;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerImplantCommerceOutcome
{
    Available, Applied, Replayed, Conflict, Corrupt, Unavailable, Missing,
    Canceled, ReconciliationRequired, NeverApplied
}

public sealed record CharacterCareerImplantCommerceQuery(CharacterWorkspaceId WorkspaceId, long ExpectedRevision);

// IDs are saved instance GUIDs. Action IDs are "sell", "grade:" plus the canonical grade GUID,
// or "rating:" plus a canonical invariant integer above the installed rating, at the same grade.
public sealed record CharacterCareerImplantCommerceRequest(CharacterWorkspaceId WorkspaceId, Guid OperationId,
    Guid InstanceId, string ActionId, decimal RefundPercentage, Guid ExpenseId, DateTimeOffset ExpenseDate);

public sealed record CharacterCareerImplantCommerceBinding(string OwnerId, bool LocalSingleUser,
    string OwnerAuthorityInstanceId, long OwnerTransitionRevision, CharacterWorkspaceId WorkspaceId,
    long ContentRevision, long SavedRevision, string DocumentDigest, string AuxiliaryDigest,
    string CatalogDigest, string SettingsProfileId, string SourceDigest, string RuntimeDigest)
{
    // New reviews bind local history. Omit null so old committed receipts retain their original digests.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LocalHistoryIncarnationId { get; init; }
}

public sealed record CharacterCareerImplantCommerceCommand(CharacterCareerImplantCommerceRequest Request,
    CharacterCareerImplantCommerceBinding Binding, string PreviewDigest, bool ExplicitlyConfirmed = false);

public sealed record CharacterCareerImplantCommerceAction(string Id, string Name, bool Supported, string? Blocker);
public sealed record CharacterCareerImplantCommerceRow(Guid? InstanceId, string Name,
    IReadOnlyList<CharacterCareerImplantCommerceAction> Actions, IReadOnlyList<string> Blockers);
public sealed record CharacterCareerImplantCommerceCatalog(CharacterCareerImplantCommerceBinding Binding,
    IReadOnlyList<CharacterCareerImplantCommerceRow> Rows, decimal RefundMinimum, decimal RefundMaximum,
    int RefundDecimalPlaces, decimal RefundDefault);

// Source evidence, not permission to dispatch caller-supplied XML. Every commit regenerates it.
public sealed record CharacterCareerImplantCommerceItem(Guid InstanceId, Guid SourceId, string Name,
    string InstalledXml, CharacterCyberwareCommerceSnapshot Snapshot);
public sealed record CharacterCareerImplantCommercePreparation(bool Exact, IReadOnlyList<string> Blockers,
    string CatalogDigest, string SettingsProfileId, string SourceDigest,
    IReadOnlyList<CharacterCareerImplantCommerceRow> Rows, IReadOnlyList<CharacterCareerImplantCommerceItem> Items)
{
    // Independently resolved through the actual character source context; never a purchase catalog entry.
    public IReadOnlyList<CharacterCyberwarePurchaseCatalogEntry> BookkeepingSources { get; init; } = [];
}
public sealed record CharacterCareerImplantCommerceQuote(CharacterCareerImplantCommerceItem Item,
    CharacterCyberwareCommerceQuote Canonical, string? ReplacementImplantXml, string ExpenseXml,
    decimal NuyenBefore, decimal NuyenAfter, string Consequences)
{
    // Exact serialized additions, covered by preview/command/receipt digests and regenerated at commit.
    // Omit nulls to retain the original serialization of receipts which created no bookkeeping.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CreatedEssenceHoleXml { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CreatedEssenceAntiHoleXml { get; init; }
}
public sealed record CharacterCareerImplantCommercePreview(CharacterCareerImplantCommerceCommand Command,
    CharacterCareerImplantCommerceQuote Quote);
public sealed record CharacterCareerImplantCommerceReceipt(CharacterCareerImplantCommerceCommand Command,
    CharacterCareerImplantCommerceQuote? Quote, string CommandDigest, long CommittedRevision,
    string PayloadDigestAfter, string PreviousReceiptDigest, string ReceiptDigest)
{
    // A terminal dispatch fence, not an economic receipt. False is omitted to preserve existing digests.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool NeverApplied { get; init; }
}

public sealed record CharacterCareerImplantCommerceCatalogResult(CharacterCareerImplantCommerceOutcome Outcome,
    CharacterCareerImplantCommerceCatalog? Catalog = null, string? Error = null);
public sealed record CharacterCareerImplantCommercePreviewResult(CharacterCareerImplantCommerceOutcome Outcome,
    CharacterCareerImplantCommercePreview? Preview = null, string? Error = null);
public sealed record CharacterCareerImplantCommerceResult(CharacterCareerImplantCommerceOutcome Outcome,
    CharacterCareerImplantCommerceReceipt? Receipt = null, string? Error = null);
