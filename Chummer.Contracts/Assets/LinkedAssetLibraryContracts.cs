namespace Chummer.Contracts.Assets;

public static class LinkedAssetShareSubjectKinds
{
    public const string User = "user";
    public const string Campaign = "campaign";
    public const string PublicCatalog = "public-catalog";
}

public static class LinkedAssetShareAccessLevels
{
    public const string View = "view";
    public const string Link = "link";
    public const string Fork = "fork";
    public const string Manage = "manage";
}

public static class LinkedAssetTransferFormats
{
    public const string Json = "json";
    public const string Bundle = "bundle";
}

public sealed record LinkedAssetShareGrant(
    string SubjectKind,
    string SubjectId,
    string AccessLevel);

public sealed record LinkedAssetLibraryEntry(
    string OwnerId,
    LinkedAssetReference Asset,
    IReadOnlyList<LinkedAssetShareGrant> Shares,
    DateTimeOffset UpdatedAtUtc);

public sealed record LinkedAssetImportReceipt(
    string AssetId,
    string VersionId,
    string AssetType,
    string Format,
    int ImportedCount);

public sealed record LinkedAssetExportReceipt(
    string AssetId,
    string VersionId,
    string AssetType,
    string Format,
    string FileName,
    int DocumentLength);
