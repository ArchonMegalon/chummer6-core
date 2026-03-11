using Chummer.Contracts.Owners;

namespace Chummer.Contracts.Content;

public static class BuildKitPublicationStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Archived = "archived";
}

public sealed record BuildKitRegistryEntry(
    BuildKitManifest Manifest,
    OwnerScope Owner,
    string Visibility,
    string PublicationStatus,
    DateTimeOffset UpdatedAtUtc);
