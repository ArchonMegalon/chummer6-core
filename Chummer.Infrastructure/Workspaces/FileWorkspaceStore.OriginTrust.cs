using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    /// <summary>
    /// App-owned local custody, independent of workspace imports. All stores in one
    /// app installation should receive the same persistent domain. The private root
    /// must be app-private (owner-only ACL on Windows); never export it in a closure.
    /// Android's app sandbox supplies that boundary. Account-link RSA keys are NOT
    /// used: no workspace-transfer authorization for those keys has been established.
    /// Foreign verifiers require the separately configured issuer and possession protocol.
    /// Compatibility caller-asserted enrollment remains disabled.
    /// </summary>
    public sealed partial class OriginTrustDomain
    {
        private readonly string _root;
        private readonly string _originId;
        private readonly byte[] _publicKey;
        private static readonly object CustodyGate = new();
        private static readonly byte[] Domain = Encoding.UTF8.GetBytes("chummer.workspace-history.signature/v1\0");

        public OriginTrustDomain(string persistentPrivateDirectory)
        {
            _root = RequireRetainedStateRoot(persistentPrivateDirectory);
            ValidateStaticAncestorChain(_root);
            EnsureSecureDirectory(_root, "workspace origin custody");
            lock (CustodyGate)
            {
                using var gate = OpenCustodyLock();
                string path = Path.Combine(_root, "origin.p8");
                if (!File.Exists(path))
                {
                    using var created = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                    byte[] secret = created.ExportPkcs8PrivateKey();
                    try { WriteNew(path, secret); }
                    finally { CryptographicOperations.ZeroMemory(secret); }
                }
                using var key = ReadKey();
                _publicKey = key.ExportSubjectPublicKeyInfo();
                _originId = Fingerprint(_publicKey);
            }
        }

        internal string OriginId => _originId;

        public sealed record OwnerOrigin(string OwnerId, bool LocalSingleUser, string OriginId, string PublicKey)
        {
            public override string ToString() => nameof(OwnerOrigin);
        }

        /// <summary>Public material only; exporting it grants no trust in another domain.</summary>
        public OwnerOrigin ExportOwnerOrigin(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected)
        {
            if (!owners.TryAcquire(expected, out var lease) || lease is null)
                throw new InvalidOperationException("origin-owner-unavailable");
            using (lease)
            {
                RequireOwner(owners, lease, expected);
                return new(expected.Owner.NormalizedValue, expected.Owner.IsLocalSingleUser,
                    _originId, Convert.ToBase64String(_publicKey));
            }
        }

        /// <summary>
        /// Disabled until an independently authenticated enrollment boundary exists.
        /// An owner lease and a caller-supplied matching fingerprint cannot authenticate
        /// an external origin. This compatibility entry point always fails without writes.
        /// </summary>
        public void ProvisionVerifiedOwnerOrigin(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected,
            OwnerOrigin origin, string independentlyVerifiedFingerprint)
            => throw new InvalidOperationException("origin-enrollment-unsupported");

        internal WorkspaceAuthenticatedTransition Sign(WorkspaceHistoricalObservation observation)
        {
            if (observation.Version != 1 || observation.OriginId != _originId)
                throw new IOException("origin-signing-mismatch");
            lock (CustodyGate)
            {
                using var gate = OpenCustodyLock();
                using var key = ReadKey();
                if (Fingerprint(key.ExportSubjectPublicKeyInfo()) != _originId)
                    throw new IOException("origin-custody-changed");
                return new(observation, Convert.ToBase64String(key.SignData(SigningBytes(observation),
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
            }
        }

        internal bool Verify(OwnerScope owner, WorkspaceAuthenticatedTransition transition)
        {
            try
            {
                if (transition?.Observation is not { } o) return false;
                if (o.Version != 1 || o.OwnerId != owner.NormalizedValue || o.LocalSingleUser != owner.IsLocalSingleUser
                    || o.OriginId is not { Length: 64 } || !o.OriginId.All(char.IsAsciiHexDigit) || transition.Signature is not { Length: 88 })
                    return false;
                // Public custody is independently issuer-verified; imports cannot populate it.
                byte[]? material = o.OriginId == _originId ? _publicKey : HistoricalVerifier(owner, o.OriginId);
                if (material is null) return false;
                using var key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(material, out int read);
                return read == material.Length && key.VerifyData(SigningBytes(o), Convert.FromBase64String(transition.Signature),
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or FormatException or InvalidOperationException or JsonException)
            { return false; }
        }

        private static byte[] SigningBytes(WorkspaceHistoricalObservation observation)
            => [.. Domain, .. JsonSerializer.SerializeToUtf8Bytes(observation)];
        private static string Fingerprint(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
        private FileStream OpenCustodyLock()
        {
            ValidateStaticAncestorChain(_root);
            string path = Path.Combine(_root, "custody.lock");
            ThrowIfLinkOrReparsePoint(path, "origin custody lock");
            return OpenSecureFile(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, FileOptions.None);
        }
        private ECDsa ReadKey()
        {
            string path = Path.Combine(_root, "origin.p8");
            byte[] bytes = ReadPublic(path);
            var key = ECDsa.Create();
            try
            {
                key.ImportPkcs8PrivateKey(bytes, out int read);
                if (read != bytes.Length || key.KeySize != 256) throw new IOException("origin-key-invalid");
                return key;
            }
            catch { key.Dispose(); throw; }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        private byte[] ReadPublic(string path)
        {
            ValidateStaticAncestorChain(_root);
            ThrowIfLinkOrReparsePoint(path, "origin custody material");
            using var input = OpenSecureFile(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.None);
            if (input.Length is <= 0 or > 1024) throw new IOException("origin-material-invalid");
            byte[] bytes = new byte[checked((int)input.Length)];
            input.ReadExactly(bytes);
            return bytes;
        }
        private void WriteNew(string path, byte[] bytes)
        {
            // Hold checked same-volume, local-NTFS ancestor handles for the entire
            // write and reuse9981's immutable MOVEFILE_WRITE_THROUGH publication.
            // This is not a directory fsync or an existing-target replacement.
            using var windowsPins = OperatingSystem.IsWindows()
                ? PinWindowsRuleUpdateDirectory(_root, FileWorkspaceStoreFaultInjector.None, Path.GetDirectoryName(path)!) : null;
            string temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
            bool temporaryCreated = false;
            try
            {
                using (var output = OpenNewSecureFile(temporary, FileAccess.Write, FileShare.None, FileOptions.WriteThrough))
                { temporaryCreated = true; output.Write(bytes); output.Flush(flushToDisk: true); }
                ThrowIfLinkOrReparsePoint(path, "origin custody material");
                if (OperatingSystem.IsWindows())
                    PublishWindowsImmutablePrivateFile(_root, FileWorkspaceStoreFaultInjector.None, temporary, path);
                else File.Move(temporary, path, overwrite: false);
                SetSecureFileMode(path);
                if (OperatingSystem.IsLinux()) FlushRetainedLinuxDirectory(Path.GetDirectoryName(path)!);
            }
            finally
            {
                if (temporaryCreated && File.Exists(temporary))
                    DeleteRegularFileIfPresent(temporary, "origin custody temporary file");
            }
        }
        private static void RequireOwner(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease, OwnerContextStamp expected)
        {
            if (!expected.IsValid || owners.Capture() != expected || lease.Stamp != expected)
                throw new InvalidOperationException("origin-owner-changed");
        }
    }
}
