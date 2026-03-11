namespace Chummer.Contracts.Owners;

public static class PortalOwnerPropagationContract
{
    public const string OwnerHeaderName = "X-Chummer-Portal-Owner";
    public const string TimestampHeaderName = "X-Chummer-Portal-Owner-Timestamp";
    public const string SignatureHeaderName = "X-Chummer-Portal-Owner-Signature";
    public const string SharedKeyEnvironmentVariable = "CHUMMER_PORTAL_OWNER_SHARED_KEY";
    public const int DefaultMaxAgeSeconds = 300;

    public static string BuildSignaturePayload(string normalizedOwner, string unixTimestamp)
    {
        string owner = new OwnerScope(normalizedOwner).NormalizedValue;
        string timestamp = string.IsNullOrWhiteSpace(unixTimestamp)
            ? string.Empty
            : unixTimestamp.Trim();
        return $"{owner}\n{timestamp}";
    }
}
