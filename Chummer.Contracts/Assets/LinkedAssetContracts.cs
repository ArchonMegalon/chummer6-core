namespace Chummer.Contracts.Assets;

public static class LinkedAssetVisibilityModes
{
    public const string Private = "private";
    public const string Shared = "shared";
    public const string CampaignShared = "campaign-shared";
    public const string Public = "public";
}

public sealed record LinkedAssetReference(
    string AssetId,
    string VersionId,
    string AssetType,
    string Visibility);

public sealed record ContactAsset(
    LinkedAssetReference Reference,
    string Name,
    string Role,
    string Location,
    int Connection,
    int Loyalty,
    string? Notes = null);

public sealed record ContactLinkOverride(
    string? DisplayName = null,
    int? Connection = null,
    int? Loyalty = null,
    string? Notes = null);

public sealed record CharacterContactLink(
    string CharacterId,
    LinkedAssetReference Contact,
    ContactLinkOverride Overrides,
    bool IsFavorite = false);
