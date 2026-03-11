namespace Chummer.Contracts.Session;

public static class SessionRuntimeBundleIssueOutcomes
{
    public const string Issued = "issued";
    public const string Rotated = "rotated";
    public const string Reissued = "reissued";
    public const string Blocked = "blocked";
}

public static class SessionRuntimeBundleDeliveryModes
{
    public const string Inline = "inline";
    public const string Download = "download";
    public const string Cached = "cached";
}

public static class SessionRuntimeBundleTrustStates
{
    public const string Trusted = "trusted";
    public const string ExpiringSoon = "expiring-soon";
    public const string InvalidSignature = "invalid-signature";
    public const string Revoked = "revoked";
    public const string MissingKey = "missing-key";
}

public static class SessionRuntimeBundleRotationReasons
{
    public const string RuntimeFingerprintChanged = "runtime-fingerprint-changed";
    public const string SignatureExpiring = "signature-expiring";
    public const string KeyRotated = "key-rotated";
    public const string TrustRevoked = "trust-revoked";
}

public sealed record SessionRuntimeBundleSignatureEnvelope(
    string BundleId,
    string KeyId,
    string Signature,
    DateTimeOffset SignedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record SessionRuntimeBundleTrustDiagnostic(
    string State,
    string Message,
    string? KeyId = null,
    string? RuntimeFingerprint = null);

public sealed record SessionRuntimeBundleIssueReceipt(
    string Outcome,
    SessionRuntimeBundle Bundle,
    SessionRuntimeBundleSignatureEnvelope SignatureEnvelope,
    string DeliveryMode,
    IReadOnlyList<SessionRuntimeBundleTrustDiagnostic> Diagnostics);

public sealed record SessionRuntimeBundleRotationNotice(
    string PreviousBundleId,
    string CurrentBundleId,
    string Reason,
    DateTimeOffset RotatedAtUtc,
    bool RequiresClientReload = false);
