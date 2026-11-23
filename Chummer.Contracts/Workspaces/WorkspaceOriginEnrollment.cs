using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Chummer.Contracts.Workspaces;

// This is the single Core wire protocol, also consumed by Hub and both clients.
// No private material, bearer token, caller owner assertion or imported key grants trust.
public sealed record WorkspaceOriginRequest(string InstallationId, Guid OperationId, string Nonce,
    string OriginId, string PublicKey, string HistoryDigest = "");
public sealed record WorkspaceOriginChallenge(WorkspaceOriginRequest Request, string OwnerId,
    Guid OwnerEpoch, string InstallationEpoch, long IssuedAt, long ExpiresAt, string Challenge);
public sealed record WorkspaceOriginProof(WorkspaceOriginChallenge Challenge, string Signature);
public sealed record WorkspaceOriginBinding(string OriginId, string PublicKey, string InstallationId,
    string InstallationEpoch, bool Active, bool Revoked = false);
public sealed record WorkspaceOriginSnapshot(WorkspaceOriginRequest Request, string OwnerId,
    Guid OwnerEpoch, long Revision, long IssuedAt, long ExpiresAt, bool Erased,
    WorkspaceOriginBinding[] Origins);
public sealed record WorkspaceOriginSigned<T>(string IssuerId, T Payload, string Signature);
public sealed record WorkspaceOriginOperation(WorkspaceOriginSigned<WorkspaceOriginChallenge> Challenge,
    bool Completed);

public sealed record WorkspaceOriginPublicationClaims(string InstallationId, Guid OperationId,
    string OriginId, Guid OwnerEpoch, string InstallationEpoch, string TransferIdentity, string HistoryDigest);
public sealed record WorkspaceOriginPublicationProof(WorkspaceOriginPublicationClaims Claims, string Signature);

public static class WorkspaceOriginProtocol
{
    public const int MaximumWireBytes = 32 * 1024;
    public const int MaximumOrigins = 32;
    public const int LifetimeSeconds = 120;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { MaxDepth = 12, AllowDuplicateProperties = false, RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true };
    public static byte[] Bytes<T>(string purpose, T value)
        => [.. Encoding.UTF8.GetBytes("chummer.workspace-origin/1/" + purpose + "\0"),
            .. JsonSerializer.SerializeToUtf8Bytes(value, Json)];
    public static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string HistoryDigest(WorkspaceAuthenticatedHistory history)
        => Digest(JsonSerializer.SerializeToUtf8Bytes(history));
    public static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static bool Token(string? value, int maximum) => value is { Length: > 0 } && value.Length <= maximum
        && value.All(c => c is >= '!' and <= '~');
    public static bool Nonce(string? value)
    {
        try { return value is { Length: 44 } && Convert.FromBase64String(value).Length == 32
            && Convert.ToBase64String(Convert.FromBase64String(value)) == value; }
        catch (FormatException) { return false; }
    }
    public static string NewNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public static bool Valid(WorkspaceOriginRequest r) => r is not null && Token(r.InstallationId, 64)
        && r.OperationId != Guid.Empty && Nonce(r.Nonce) && Hash(r.OriginId) && PublicKey(r.PublicKey, r.OriginId)
        && (r.HistoryDigest == "" || Hash(r.HistoryDigest));
    public static bool PublicKey(string? encoded, string id)
    {
        try
        {
            if (encoded is not { Length: 124 }) return false; // canonical P256 SPKI: 91 bytes
            byte[] bytes = Convert.FromBase64String(encoded);
            using var key = ECDsa.Create(); key.ImportSubjectPublicKeyInfo(bytes, out int read);
            return read == bytes.Length && key.ExportParameters(false).Curve.Oid.Value == "1.2.840.10045.3.1.7"
                && Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) == encoded && Digest(bytes) == id;
        }
        catch (Exception e) when (e is CryptographicException or FormatException or ArgumentException) { return false; }
    }
    public static bool Verify<T>(string key, string purpose, T payload, string signature)
    {
        try
        {
            if (signature is not { Length: 88 } || key is not { Length: 124 }) return false;
            byte[] bytes = Convert.FromBase64String(key);
            using var verifier = ECDsa.Create(); verifier.ImportSubjectPublicKeyInfo(bytes, out int read);
            return read == bytes.Length && verifier.ExportParameters(false).Curve.Oid.Value == "1.2.840.10045.3.1.7"
                && verifier.VerifyData(Bytes(purpose, payload), Convert.FromBase64String(signature),
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or ArgumentException) { return false; }
    }
    public static bool Fresh(long issued, long expires, long now) => issued <= now && expires > now
        && issued >= now - LifetimeSeconds && expires > issued && expires - issued <= LifetimeSeconds;
}
