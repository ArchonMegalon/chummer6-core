namespace Chummer.Contracts.Owners;

public static class OwnerRepositoryMutationKinds
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Share = "share";
    public const string Fork = "fork";
    public const string Archive = "archive";
    public const string Delete = "delete";
    public const string Restore = "restore";
}

public static class OwnerRepositoryMutationStatuses
{
    public const string Applied = "applied";
    public const string Deferred = "deferred";
    public const string Conflict = "conflict";
    public const string Rejected = "rejected";
    public const string NotFound = "not-found";
}

public static class OwnerRepositoryArchiveModes
{
    public const string Hide = "hide";
    public const string SoftDelete = "soft-delete";
    public const string RetainHistory = "retain-history";
}

public static class OwnerRepositoryShareAccessLevels
{
    public const string View = "view";
    public const string Link = "link";
    public const string Install = "install";
    public const string Fork = "fork";
    public const string Manage = "manage";
}

public sealed record OwnerRepositoryShareGrant(
    string SubjectKind,
    string SubjectId,
    string AccessLevel);

public sealed record OwnerRepositoryMutationReceipt(
    string MutationKind,
    string Status,
    string AssetKind,
    string AssetId,
    OwnerScope Actor,
    DateTimeOffset AppliedAtUtc,
    string? VersionId = null,
    bool RequiresReindex = false,
    string? Message = null);

public sealed record OwnerRepositoryShareReceipt(
    string AssetKind,
    string AssetId,
    OwnerScope Actor,
    IReadOnlyList<OwnerRepositoryShareGrant> Grants,
    string Status,
    DateTimeOffset SharedAtUtc);

public sealed record OwnerRepositoryForkReceipt(
    string AssetKind,
    string SourceAssetId,
    string SourceVersionId,
    string ForkedAssetId,
    OwnerScope Actor,
    string Status,
    DateTimeOffset ForkedAtUtc);

public sealed record OwnerRepositoryArchiveReceipt(
    string AssetKind,
    string AssetId,
    string Mode,
    OwnerScope Actor,
    string Status,
    DateTimeOffset ArchivedAtUtc,
    bool RetainsHistory = true);
