namespace Chummer.Contracts.Receipts;

public static class ReceiptExposureClasses
{
    public const string Internal = "internal";
    public const string SignedIn = "signed_in";
    public const string PublicSafe = "public_safe";
}

public static class ReceiptLifecycleStates
{
    public const string Draft = "draft";
    public const string Verified = "verified";
    public const string Published = "published";
    public const string Archived = "archived";
}

public static class ReceiptProvenanceClasses
{
    public const string Runtime = "runtime";
    public const string DerivedProjection = "derived_projection";
    public const string ExternalWebhook = "external_webhook";
    public const string HumanReview = "human_review";
}

public sealed record ReceiptEnvelope(
    string ReceiptKind,
    string OwnerScope,
    string ProvenanceClass,
    string ExposureClass,
    string LifecycleState,
    DateTimeOffset CapturedAtUtc,
    string? EvidenceRef = null,
    string? ReviewState = null,
    bool Reproducible = true);

public static class ReceiptEnvelopeFactory
{
    public static ReceiptEnvelope Runtime(
        string receiptKind,
        string ownerScope,
        string exposureClass,
        string lifecycleState = ReceiptLifecycleStates.Verified,
        string? evidenceRef = null,
        string? reviewState = null,
        bool reproducible = true)
        => new(
            receiptKind,
            ownerScope,
            ReceiptProvenanceClasses.Runtime,
            exposureClass,
            lifecycleState,
            DateTimeOffset.UtcNow,
            evidenceRef,
            reviewState,
            reproducible);

    public static ReceiptEnvelope ExternalWebhook(
        string receiptKind,
        string ownerScope,
        string exposureClass = ReceiptExposureClasses.SignedIn,
        string lifecycleState = ReceiptLifecycleStates.Verified,
        string? evidenceRef = null,
        string? reviewState = null,
        bool reproducible = true)
        => new(
            receiptKind,
            ownerScope,
            ReceiptProvenanceClasses.ExternalWebhook,
            exposureClass,
            lifecycleState,
            DateTimeOffset.UtcNow,
            evidenceRef,
            reviewState,
            reproducible);
}
