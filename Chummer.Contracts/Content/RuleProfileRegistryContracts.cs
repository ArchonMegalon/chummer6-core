namespace Chummer.Contracts.Content;

public static class RuleProfileAudienceKinds
{
    public const string General = "general";
    public const string NewPlayers = "new-players";
    public const string Campaign = "campaign";
    public const string Gm = "gm";
    public const string Advanced = "advanced";
}

public static class RuleProfileCatalogKinds
{
    public const string Official = "official";
    public const string Curated = "curated";
    public const string Personal = "personal";
}

public static class RuleProfilePublicationStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Archived = "archived";
}

public static class RuleProfileUpdateChannels
{
    public const string Stable = "stable";
    public const string Preview = "preview";
    public const string CampaignPinned = "campaign-pinned";
}

public sealed record RuleProfilePackSelection(
    ArtifactVersionReference RulePack,
    bool Required = true,
    bool EnabledByDefault = true);

public sealed record RuleProfileDefaultToggle(
    string ToggleId,
    string Value,
    string Label,
    string? Description = null);

public sealed record RuleProfileManifest(
    string ProfileId,
    string Title,
    string Description,
    string RulesetId,
    string Audience,
    string CatalogKind,
    IReadOnlyList<RuleProfilePackSelection> RulePacks,
    IReadOnlyList<RuleProfileDefaultToggle> DefaultToggles,
    ResolvedRuntimeLock RuntimeLock,
    string UpdateChannel,
    string? Notes = null);

public sealed record RuleProfilePublicationMetadata(
    string OwnerId,
    string Visibility,
    string PublicationStatus,
    RulePackReviewDecision Review,
    IReadOnlyList<RulePackShareGrant> Shares,
    DateTimeOffset? PublishedAtUtc = null,
    string? PublisherId = null);

public sealed record RuleProfileRegistryEntry(
    RuleProfileManifest Manifest,
    RuleProfilePublicationMetadata Publication,
    ArtifactInstallState Install,
    string SourceKind = RegistryEntrySourceKinds.PersistedManifest);

public sealed record RuleProfileManifestRecord(
    RuleProfileManifest Manifest);

public sealed record RuleProfilePublicationRecord(
    string ProfileId,
    string RulesetId,
    RuleProfilePublicationMetadata Publication);

public sealed record RuleProfileInstallRecord(
    string ProfileId,
    string RulesetId,
    ArtifactInstallState Install);

public sealed record RuleProfileInstallHistoryRecord(
    string ProfileId,
    string RulesetId,
    ArtifactInstallHistoryEntry Entry);
