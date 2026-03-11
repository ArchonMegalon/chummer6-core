using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Content;

public static class RulePackPublicationStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Archived = "archived";
}

public static class RulePackReviewStates
{
    public const string NotRequired = "not-required";
    public const string PendingReview = "pending-review";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

public static class RulePackShareSubjectKinds
{
    public const string User = "user";
    public const string Campaign = "campaign";
    public const string PublicCatalog = "public-catalog";
}

public static class RulePackShareAccessLevels
{
    public const string View = "view";
    public const string Install = "install";
    public const string Fork = "fork";
    public const string Manage = "manage";
}

public static class RegistryEntrySourceKinds
{
    public const string PersistedManifest = "persisted-manifest";
    public const string OverlayCatalogBridge = "overlay-catalog-bridge";
    public const string BuiltInCoreProfile = "built-in-core-profile";
    public const string OverlayDerivedProfile = "overlay-derived-profile";
}

public sealed record RulePackForkLineage(
    string RootPackId,
    string ParentPackId,
    string ParentVersion,
    bool IsFork);

public sealed record RulePackShareGrant(
    string SubjectKind,
    string SubjectId,
    string AccessLevel);

public sealed record RulePackReviewDecision(
    string State,
    string? ReviewerId = null,
    string? Notes = null,
    DateTimeOffset? ReviewedAtUtc = null);

public sealed record RulePackPublicationMetadata(
    string OwnerId,
    string Visibility,
    string PublicationStatus,
    RulePackReviewDecision Review,
    IReadOnlyList<RulePackShareGrant> Shares,
    RulePackForkLineage? ForkLineage = null,
    DateTimeOffset? PublishedAtUtc = null,
    string? PublisherId = null);

public sealed record RulePackRegistryEntry(
    RulePackManifest Manifest,
    RulePackPublicationMetadata Publication,
    ArtifactInstallState Install,
    string SourceKind = RegistryEntrySourceKinds.PersistedManifest);

public sealed record RulePackManifestRecord(
    RulePackManifest Manifest);

public sealed record RulePackPublicationRecord(
    string PackId,
    string Version,
    string RulesetId,
    RulePackPublicationMetadata Publication);

public sealed record RulePackInstallRecord(
    string PackId,
    string Version,
    string RulesetId,
    ArtifactInstallState Install);

public sealed record RulePackInstallHistoryRecord(
    string PackId,
    string Version,
    string RulesetId,
    ArtifactInstallHistoryEntry Entry);

public static class RulePackInstallPreviewChangeKinds
{
    public const string InstallStateChanged = "install-state-changed";
    public const string SessionReplayRequired = "session-replay-required";
    public const string RuntimeReviewRequired = "runtime-review-required";
}

public static class RulePackInstallOutcomes
{
    public const string Applied = "applied";
    public const string AlreadyInstalled = "already-installed";
}

public sealed record RulePackInstallPreviewItem(
    string Kind,
    string Summary,
    string SubjectId,
    bool RequiresConfirmation = false,
    string? SummaryKey = null,
    IReadOnlyList<RulesetExplainParameter>? SummaryParameters = null);

public sealed record RulePackInstallPreviewReceipt(
    string PackId,
    string RulesetId,
    RuleProfileApplyTarget Target,
    IReadOnlyList<RulePackInstallPreviewItem> Changes,
    IReadOnlyList<RuntimeInspectorWarning> Warnings,
    bool RequiresConfirmation = false);

public sealed record RulePackInstallReceipt(
    string PackId,
    string RulesetId,
    RuleProfileApplyTarget Target,
    string Outcome,
    ArtifactInstallState Install,
    RulePackInstallPreviewReceipt Preview);

public sealed record RulePackPublicationReceipt(
    string PackId,
    string Version,
    string PublicationStatus,
    string Visibility,
    string ReviewState,
    IReadOnlyList<RulePackShareGrant> Shares,
    RulePackForkLineage? ForkLineage = null);

public static class RulePackInstallContractLocalization
{
    public static string ResolvePreviewSummaryKey(RulePackInstallPreviewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return string.IsNullOrWhiteSpace(item.SummaryKey)
            ? item.Summary
            : item.SummaryKey;
    }

    public static IReadOnlyList<RulesetExplainParameter> ResolvePreviewSummaryParameters(RulePackInstallPreviewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.SummaryParameters ?? [];
    }
}
