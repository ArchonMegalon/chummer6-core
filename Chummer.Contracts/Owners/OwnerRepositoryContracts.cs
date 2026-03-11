namespace Chummer.Contracts.Owners;

public static class OwnerRepositoryAssetKinds
{
    public const string Character = "character";
    public const string SessionLedger = "session-ledger";
    public const string LinkedAsset = "linked-asset";
    public const string RulePack = "rulepack";
    public const string BuildKit = "buildkit";
}

public static class OwnerRepositoryScopeModes
{
    public const string Owned = "owned";
    public const string SharedWithMe = "shared-with-me";
    public const string Campaign = "campaign";
    public const string PublicCatalog = "public-catalog";
}

public static class OwnerRepositorySortModes
{
    public const string UpdatedDesc = "updated-desc";
    public const string CreatedDesc = "created-desc";
    public const string TitleAsc = "title-asc";
}

public sealed record OwnerRepositoryQuery(
    string ScopeMode,
    string AssetKind,
    string? Search = null,
    string? CampaignId = null,
    string? Visibility = null,
    string SortMode = OwnerRepositorySortModes.UpdatedDesc,
    int Offset = 0,
    int Limit = 50);

public sealed record OwnerRepositoryEntry(
    string AssetKind,
    string AssetId,
    string Title,
    OwnerScope Owner,
    string Visibility,
    DateTimeOffset UpdatedAtUtc,
    string? VersionId = null,
    string? Summary = null,
    bool CanEdit = false,
    bool CanShare = false);

public sealed record OwnerRepositoryPage(
    string ScopeMode,
    string AssetKind,
    IReadOnlyList<OwnerRepositoryEntry> Entries,
    int TotalCount,
    string? ContinuationToken = null);

public sealed record OwnerRepositoryQueryReceipt(
    OwnerRepositoryQuery Query,
    int ReturnedCount,
    int TotalCount,
    string? ContinuationToken = null);
