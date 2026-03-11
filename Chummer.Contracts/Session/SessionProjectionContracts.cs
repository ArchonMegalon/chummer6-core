using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Trackers;

namespace Chummer.Contracts.Session;

public static class SessionDashboardSectionKinds
{
    public const string Summary = "summary";
    public const string Trackers = "trackers";
    public const string QuickActions = "quick-actions";
    public const string Effects = "effects";
    public const string Notes = "notes";
    public const string Sync = "sync";
    public const string Explain = "explain";
}

public static class SessionDashboardCardKinds
{
    public const string Summary = "summary";
    public const string TrackerGroup = "tracker-group";
    public const string ResourceCard = "resource-card";
    public const string QuickActionGroup = "quick-action-group";
    public const string Effects = "effects";
    public const string Notes = "notes";
    public const string SyncBanner = "sync-banner";
    public const string Explain = "explain";
}

public static class SessionSyncBannerStates
{
    public const string UpToDate = "up-to-date";
    public const string Offline = "offline";
    public const string PendingSync = "pending-sync";
    public const string Conflict = "conflict";
    public const string Replayed = "replayed";
    public const string RuntimeMismatch = "runtime-mismatch";
}

public static class SessionExplainEntryKinds
{
    public const string DerivedValue = "derived-value";
    public const string TrackerThreshold = "tracker-threshold";
    public const string QuickActionAvailability = "quick-action-availability";
    public const string ResourceState = "resource-state";
    public const string Replay = "replay";
    public const string Sync = "sync";
}

public sealed record SessionDashboardSection(
    string SectionId,
    string Kind,
    string Title,
    IReadOnlyList<string> CardIds);

public sealed record SessionDashboardCard(
    string CardId,
    string Kind,
    string Title,
    string? PrimaryValue = null,
    string? SecondaryValue = null,
    string? GroupId = null,
    string? ExplainEntryId = null,
    bool IsInteractive = false);

public sealed record SessionTrackerGroup(
    string GroupId,
    string Label,
    IReadOnlyList<TrackerSnapshot> Trackers,
    string? ExplainEntryId = null);

public sealed record SessionQuickActionDescriptor(
    string ActionId,
    string Label,
    string CapabilityId,
    bool IsPinned = false,
    bool IsEnabled = true,
    string? DisabledReasonKey = null,
    IReadOnlyList<RulesetExplainParameter>? DisabledReasonParameters = null,
    string? DisabledReason = null,
    string? ExplainEntryId = null);

public sealed record SessionQuickActionGroup(
    string GroupId,
    string Label,
    IReadOnlyList<SessionQuickActionDescriptor> Actions);

public sealed record SessionSyncBanner(
    string BannerId,
    string Status,
    string Message,
    int PendingEventCount = 0,
    bool RequiresAttention = false,
    string? ExplainEntryId = null);

public sealed record SessionExplainEntry(
    string EntryId,
    string Kind,
    string TitleKey,
    IReadOnlyList<RulesetExplainParameter> TitleParameters,
    string SummaryKey,
    IReadOnlyList<RulesetExplainParameter> SummaryParameters,
    IReadOnlyList<SessionExplainFragment> Fragments,
    string? ProviderId = null,
    string? PackId = null,
    int? GasUsed = null);

public sealed record SessionExplainFragment(
    string FragmentKey,
    IReadOnlyList<RulesetExplainParameter> Parameters);

public sealed record SessionDashboardProjection(
    string OverlayId,
    CharacterVersionReference BaseCharacterVersion,
    string RuntimeFingerprint,
    SessionOverlaySnapshot Overlay,
    SessionRuntimeBundle RuntimeBundle,
    IReadOnlyList<SessionDashboardSection> Sections,
    IReadOnlyList<SessionDashboardCard> Cards,
    IReadOnlyList<SessionTrackerGroup> TrackerGroups,
    IReadOnlyList<SessionQuickActionGroup> QuickActionGroups,
    IReadOnlyList<SessionExplainEntry> ExplainEntries,
    SessionSyncBanner? SyncBanner = null);

public static class SessionProjectionContractLocalization
{
    public static string? ResolveDisabledReasonKey(SessionQuickActionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return string.IsNullOrWhiteSpace(descriptor.DisabledReasonKey)
            ? descriptor.DisabledReason
            : descriptor.DisabledReasonKey;
    }

    public static IReadOnlyList<RulesetExplainParameter> ResolveDisabledReasonParameters(SessionQuickActionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return descriptor.DisabledReasonParameters ?? [];
    }
}
