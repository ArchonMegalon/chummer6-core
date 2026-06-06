namespace Chummer.Contracts.Content;

public static class ChummerDocumentStatuses
{
    public const string Draft = "draft";
    public const string Approved = "approved";
    public const string Published = "published";
    public const string Archived = "archived";
    public const string Unpublished = "unpublished";
    public const string Deleted = "deleted";
}

public static class ChummerDocumentClassifications
{
    public const string Public = "public";
    public const string PublicSafe = "public_safe";
    public const string CampaignPlayerSafe = "campaign_player_safe";
    public const string GmPrivate = "gm_private";
    public const string Restricted = "restricted";
}

public static class FlipLinkPublicationStatuses
{
    public const string Published = "published";
    public const string Unpublished = "unpublished";
    public const string Deleted = "deleted";
}

public static class DocumentPortalReadinessPostures
{
    public const string OperatorManagedRouteReady = "operator_managed_route_ready";
    public const string OperatorManagedViewerOptional = "operator_managed_viewer_optional";
}

public sealed record ChummerDocument(
    string Id,
    string Slug,
    string Title,
    string Category,
    string SourceRepo,
    string SourcePath,
    string SourceHash,
    string PdfArtifactPath,
    string PdfSha256,
    string PublicClassification,
    string Audience,
    string AccessPolicy,
    string FlipLinkPublicationId,
    string FlipLinkUrl,
    string FlipLinkEmbedCodeHash,
    bool AnalyticsEnabled,
    bool LeadCaptureEnabled,
    bool PasswordProtected,
    string Version,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PublishedAtUtc = null);

public sealed record FlipLinkPublication(
    string Id,
    string ChummerDocumentId,
    string Provider,
    string ProviderPublicationId,
    string FlipLinkUrl,
    string EmbedCodeHash,
    string CnameUrl,
    bool PasswordProtected,
    bool LeadCaptureEnabled,
    bool PaywallEnabled,
    bool AnalyticsEnabled,
    string PublicationStatus,
    string CreatedByUserId,
    DateTimeOffset CreatedAtUtc);

public sealed record FlipLinkPublicationReceipt(
    string Id,
    string PublicationId,
    string DocumentId,
    string PdfSha256,
    string PrivacyScanStatus,
    string CopyrightScanStatus,
    string AccessPolicy,
    string ProviderUrl,
    string EmbedRoute,
    DateTimeOffset CreatedAtUtc);
