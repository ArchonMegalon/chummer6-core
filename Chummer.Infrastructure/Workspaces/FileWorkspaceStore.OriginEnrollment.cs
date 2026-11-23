using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    // Exact decoded history, never key material in a closure, is the online admission target.
    public string? InspectOriginHistoryDigest(ReadOnlyMemory<byte> archive,
        WorkspacePrivateRuleSourceClosureBinding binding, CancellationToken ct = default)
    {
        byte[] bytes = archive.ToArray();
        try
        {
            var decoded = WorkspacePrivateRuleSourceClosure.DecodeForRestore(bytes, binding, _retainedCharacterQueries!, ct);
            return decoded.AuthenticatedHistory is { } history ? WorkspaceOriginProtocol.HistoryDigest(history) : null;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private void RequireIncomingOriginHistory(PrivateClosureRestoreAdmission admission,
        WorkspacePrivateRuleSourceClosure.Decoded decoded, WorkspaceAuthenticatedHistory? previous)
    {
        var source = decoded.Snapshot.Workspace;
        if (decoded.AuthenticatedHistory is not { } history)
        {
            if (previous is not null) throw new InvalidOperationException("origin-history-removal-refused");
            return;
        }
        if (!VerifyAuthenticatedHistory(admission.Owner.Owner, source.Id, source.Document,
            source.ContentRevision, source.SavedRevision, history, digest => decoded.Captures.GetValueOrDefault(digest)))
            throw new InvalidOperationException("origin-history-signature-refused");
        AuthenticatedOriginTrust.RequireIncoming(admission.Owner, history, previous, admission.Receipt.OperationId);
    }

    // Bridges resolve the store's actual custody instance; a separate domain cannot
    // accidentally enroll a different key than the one signing saved observations.
    public OriginTrustDomain WorkspaceOriginTrust => _originTrust.Value;

    public sealed partial class OriginTrustDomain
    {
        private readonly string? _issuerId;
        private readonly string? _issuerKey;
        private readonly TimeProvider _enrollmentClock = TimeProvider.System;
        private readonly Action<string>? _syncEnrollmentDirectory;
        private readonly Dictionary<Guid, (OwnerContextStamp Stamp, WorkspaceOriginRequest Request)> _pending = [];
        private readonly Dictionary<string, VerifiedOriginEnrollment> _incoming = [];
        private const int MaximumCustodySnapshots = 4096;

        // Composition-root trust anchor ONLY. Never populate from a closure, HTTP response,
        // account RSA key, caller fingerprint, or an untrusted settings import.
        // Windows uses the shared9981 immutable publication primitive directly.
        // Other platforms supply their approved real directory durability barrier.
        public OriginTrustDomain(string privateDirectory, string issuerId, string issuerPublicKey,
            Action<string>? syncDirectory = null, TimeProvider? clock = null) : this(privateDirectory)
        {
            if (!WorkspaceOriginProtocol.Token(issuerId, 64)
                || !WorkspaceOriginProtocol.PublicKey(issuerPublicKey,
                    WorkspaceOriginProtocol.Digest(Convert.FromBase64String(issuerPublicKey))))
                throw new ArgumentException("origin-issuer-invalid");
            _issuerId = issuerId; _issuerKey = issuerPublicKey;
            if (!OperatingSystem.IsWindows() && syncDirectory is null) throw new ArgumentNullException(nameof(syncDirectory));
            _syncEnrollmentDirectory = syncDirectory;
            _enrollmentClock = clock ?? TimeProvider.System;
        }

        public WorkspaceOriginPublicationProof CreatePublicationProof(IOwnerContextLeaseAccessor owners,
            OwnerContextStamp stamp, VerifiedOriginEnrollment verified, Guid operation,
            WorkspaceRuleEnvironmentTransfer transfer, string historyDigest)
        {
            using var lease = Acquire(owners, stamp);
            lock (CustodyGate)
            {
                using var gate = OpenCustodyLock();
                var snapshot = verified.Snapshot;
                var latest = ReadSnapshots(stamp.Owner).LastOrDefault()?.Payload;
                if (!ReferenceEquals(verified.Issuer, this) || verified.Owner != stamp || operation == Guid.Empty
                    || latest is null || latest.OwnerEpoch != snapshot.OwnerEpoch || latest.Revision != snapshot.Revision
                    || snapshot.Erased || !WorkspaceOriginProtocol.Fresh(snapshot.IssuedAt, snapshot.ExpiresAt, Now)
                    || snapshot.Request.HistoryDigest != historyDigest
                    || historyDigest != "" && !WorkspaceOriginProtocol.Hash(historyDigest)
                    || !transfer.IsValid(stamp.Owner.NormalizedValue, transfer.Binding.WorkspaceId.Value, transfer.Binding.ContinuationDigest))
                    throw Refused();
                var binding = snapshot.Origins.SingleOrDefault(b => b.OriginId == _originId && b.Active && !b.Revoked
                    && b.InstallationId == snapshot.Request.InstallationId) ?? throw Refused();
                var claims = new WorkspaceOriginPublicationClaims(binding.InstallationId, operation, _originId,
                    snapshot.OwnerEpoch, binding.InstallationEpoch, transfer.Identity, historyDigest);
                using var key = ReadKey();
                if (Fingerprint(key.ExportSubjectPublicKeyInfo()) != _originId) throw Refused();
                return new(claims, Convert.ToBase64String(key.SignData(WorkspaceOriginProtocol.Bytes("publication", claims),
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
            }
        }

        public WorkspaceOriginRequest PrepareOriginRequest(IOwnerContextLeaseAccessor owners, OwnerContextStamp stamp,
            string installationId, Guid originalOperation, string historyDigest = "")
        {
            using var lease = Acquire(owners, stamp);
            lock (CustodyGate)
            {
                using var custody = OpenCustodyLock();
                RequireIssuer();
                if (ReadSnapshots(stamp.Owner).Any(s => s.Payload.Request.OperationId == originalOperation)) throw Refused();
                if (originalOperation == Guid.Empty || !WorkspaceOriginProtocol.Token(installationId, 64)
                    || historyDigest != "" && !WorkspaceOriginProtocol.Hash(historyDigest)) throw Refused();
                if (_pending.TryGetValue(originalOperation, out var pending) && pending.Stamp != stamp) throw Refused();
                string dir = OwnerDirectory(stamp.Owner);
                string path = Path.Combine(dir, "operation-" + originalOperation.ToString("N") + ".json");
                WorkspaceOriginRequest request;
                if (File.Exists(path)) request = ReadEnrollment<WorkspaceOriginRequest>(path);
                else
                {
                    if (Directory.EnumerateFiles(dir, "operation-*.json").Take(MaximumCustodySnapshots).Count() >= MaximumCustodySnapshots)
                        throw Refused();
                    request = new(installationId, originalOperation, WorkspaceOriginProtocol.NewNonce(),
                        _originId, Convert.ToBase64String(_publicKey), historyDigest);
                    WriteEnrollment(path, request);
                }
                if (!WorkspaceOriginProtocol.Valid(request) || request.InstallationId != installationId
                    || request.OperationId != originalOperation || request.HistoryDigest != historyDigest
                    || request.OriginId != _originId || request.PublicKey != Convert.ToBase64String(_publicKey)) throw Refused();
                RequireOwner(owners, lease, stamp);
                _pending[originalOperation] = (stamp, request);
                return request;
            }
        }

        public WorkspaceOriginProof ProveOrigin(IOwnerContextLeaseAccessor owners, OwnerContextStamp stamp,
            WorkspaceOriginSigned<WorkspaceOriginChallenge> signed)
        {
            using var lease = Acquire(owners, stamp);
            lock (CustodyGate)
            {
                using var custody = OpenCustodyLock();
                var c = signed?.Payload;
                if (c is null || c.OwnerId != stamp.Owner.NormalizedValue
                    || c.OwnerEpoch == Guid.Empty || !WorkspaceOriginProtocol.Hash(c.InstallationEpoch)
                    || !WorkspaceOriginProtocol.Nonce(c.Challenge) || !WorkspaceOriginProtocol.Fresh(c.IssuedAt, c.ExpiresAt, Now)
                    || !Pending(stamp, c.Request) || c.Request.HistoryDigest != "" || !VerifyIssuer("challenge", signed)) throw Refused();
                using var key = ReadKey();
                if (Fingerprint(key.ExportSubjectPublicKeyInfo()) != _originId) throw Refused();
                RequireOwner(owners, lease, stamp);
                return new(c, Convert.ToBase64String(key.SignData(WorkspaceOriginProtocol.Bytes("possession", c),
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
            }
        }

        public bool MarkOriginDispatch(IOwnerContextLeaseAccessor owners, OwnerContextStamp stamp,
            WorkspaceOriginRequest request, string phase)
        {
            using var lease = Acquire(owners, stamp);
            lock (CustodyGate)
            {
                using var custody = OpenCustodyLock();
                if (!Pending(stamp, request) || phase is not ("begin" or "prove")) throw Refused();
                string path = Path.Combine(OwnerDirectory(stamp.Owner), "dispatch-" + request.OperationId.ToString("N") + "-" + phase + ".json");
                if (File.Exists(path))
                {
                    if (ReadEnrollment<WorkspaceOriginRequest>(path) != request) throw Refused();
                    return false;
                }
                WriteEnrollment(path, request); // durable BEFORE the network boundary
                RequireOwner(owners, lease, stamp);
                return true;
            }
        }

        // No public constructor and no caller-supplied success flag can mint this capability.
        public sealed class VerifiedOriginEnrollment
        {
            internal OriginTrustDomain Issuer { get; }
            internal OwnerContextStamp Owner { get; }
            internal WorkspaceOriginSnapshot Snapshot { get; }
            internal Guid? RestoreOperation { get; set; }
            internal VerifiedOriginEnrollment(OriginTrustDomain issuer, OwnerContextStamp owner, WorkspaceOriginSnapshot snapshot)
                => (Issuer, Owner, Snapshot) = (issuer, owner, snapshot);
            public override string ToString() => nameof(VerifiedOriginEnrollment);
        }

        public VerifiedOriginEnrollment AcceptOriginSnapshot(IOwnerContextLeaseAccessor owners, OwnerContextStamp stamp,
            WorkspaceOriginSigned<WorkspaceOriginSnapshot> signed)
        {
            using var lease = Acquire(owners, stamp);
            lock (CustodyGate)
            {
                using var custody = OpenCustodyLock();
                if (signed is null) throw Refused();
                var s = signed.Payload;
                if (!ValidSnapshot(stamp.Owner, signed) || !Pending(stamp, s.Request)
                    || !WorkspaceOriginProtocol.Fresh(s.IssuedAt, s.ExpiresAt, Now)
                    || !s.Erased && !s.Origins.Any(b => b.Active && b.OriginId == _originId
                        && b.PublicKey == Convert.ToBase64String(_publicKey) && b.InstallationId == s.Request.InstallationId)) throw Refused();
                var prior = ReadSnapshots(stamp.Owner);
                var last = prior.LastOrDefault()?.Payload;
                if (last is not null && (last.Erased || last.OwnerEpoch != s.OwnerEpoch || last.Revision > s.Revision
                    || last.Revision == s.Revision && JsonSerializer.Serialize(last.Origins) != JsonSerializer.Serialize(s.Origins))) throw Refused();
                foreach (var old in prior.SelectMany(p => p.Payload.Origins))
                    if (s.Origins.FirstOrDefault(x => x.OriginId == old.OriginId) is not { } current
                        || current.PublicKey != old.PublicKey || current.InstallationId != old.InstallationId
                        || old.Revoked && (!current.Revoked || current.Active)
                        || !old.Active && current.Active && current.InstallationEpoch == old.InstallationEpoch) throw Refused();
                if (prior.Length >= MaximumCustodySnapshots) throw Refused();
                string path = Path.Combine(OwnerDirectory(stamp.Owner), "verified-" + prior.Length.ToString("D8") + ".json");
                // Unique append: a failed write never replaces the previous trusted state.
                WriteEnrollment(path, signed);
                RequireOwner(owners, lease, stamp);
                _pending.Remove(s.Request.OperationId);
                _incoming.Clear();
                var capability = new VerifiedOriginEnrollment(this, stamp, s);
                if (!s.Erased && s.Request.HistoryDigest != "") _incoming[s.Request.HistoryDigest] = capability;
                return capability;
            }
        }

        internal void RequireIncoming(OwnerContextStamp stamp, WorkspaceAuthenticatedHistory history,
            WorkspaceAuthenticatedHistory? previous, Guid restoreOperation)
        {
            lock (CustodyGate)
            {
                var transitions = history.Transitions;
                int known = 0;
                while (known < transitions.Length && known < (previous?.Transitions.Length ?? 0)
                    && JsonSerializer.Serialize(transitions[known]) == JsonSerializer.Serialize(previous!.Transitions[known])) known++;
                if (transitions.Skip(known).All(t => t.Observation.OriginId == _originId)) return;
                // A reviewed resolution may append local same-state checkpoints after the exact imported history.
                int prefix = transitions.Length;
                VerifiedOriginEnrollment? capability = null;
                while (prefix > known)
                {
                    if (_incoming.TryGetValue(WorkspaceOriginProtocol.HistoryDigest(new(transitions[..prefix])), out capability)) break;
                    if (transitions[prefix - 1].Observation.OriginId != _originId) throw Refused();
                    prefix--;
                }
                if (capability is null || capability.Issuer != this || capability.Owner != stamp
                    || restoreOperation == Guid.Empty || capability.RestoreOperation is { } original && original != restoreOperation
                    || capability.Snapshot.Erased || !WorkspaceOriginProtocol.Fresh(capability.Snapshot.IssuedAt,
                        capability.Snapshot.ExpiresAt, Now)) throw Refused();
                var latest = ReadSnapshots(stamp.Owner).LastOrDefault()?.Payload;
                if (latest is null || latest.OwnerEpoch != capability.Snapshot.OwnerEpoch
                    || latest.Revision != capability.Snapshot.Revision || latest.Erased) throw Refused();
                foreach (var t in transitions.Skip(known))
                    if (t.Observation.OriginId != _originId
                        && !latest.Origins.Any(b => b.OriginId == t.Observation.OriginId && b.Active)) throw Refused();
                capability.RestoreOperation = restoreOperation;
            }
        }

        private byte[]? HistoricalVerifier(OwnerScope owner, string origin)
        {
            lock (CustodyGate)
            {
                using var custody = OpenCustodyLock();
                // Freshness/revocation is for incoming changes. It never invalidates admitted historical signatures.
                var binding = ReadSnapshots(owner).SelectMany(x => x.Payload.Origins).FirstOrDefault(x => x.OriginId == origin);
                return binding is null ? null : Convert.FromBase64String(binding.PublicKey);
            }
        }
        private long Now => _enrollmentClock.GetUtcNow().ToUnixTimeSeconds();
        private bool Pending(OwnerContextStamp stamp, WorkspaceOriginRequest request)
            => WorkspaceOriginProtocol.Valid(request) && _pending.TryGetValue(request.OperationId, out var p)
                && p.Stamp == stamp && p.Request == request;
        private void RequireIssuer() { if (_issuerKey is null || _issuerId is null || !OperatingSystem.IsWindows() && _syncEnrollmentDirectory is null) throw Refused(); }
        private bool VerifyIssuer<T>(string purpose, WorkspaceOriginSigned<T>? value)
            => _issuerKey is not null && value is not null && value.IssuerId == _issuerId
                && WorkspaceOriginProtocol.Verify(_issuerKey, purpose, value.Payload, value.Signature);
        private bool ValidSnapshot(OwnerScope owner, WorkspaceOriginSigned<WorkspaceOriginSnapshot> signed)
        {
            var s = signed.Payload;
            return s is not null && !owner.IsLocalSingleUser
                && s.OwnerId == owner.NormalizedValue && s.OwnerEpoch != Guid.Empty && s.Revision > 0
                && WorkspaceOriginProtocol.Valid(s.Request) && s.Origins is { Length: <= WorkspaceOriginProtocol.MaximumOrigins }
                && s.Origins.All(b => b is not null && WorkspaceOriginProtocol.PublicKey(b.PublicKey, b.OriginId)
                    && WorkspaceOriginProtocol.Token(b.InstallationId, 64) && WorkspaceOriginProtocol.Hash(b.InstallationEpoch)
                    && !(b.Revoked && b.Active))
                && s.Origins.Select(b => b.OriginId).Distinct().Count() == s.Origins.Length
                && VerifyIssuer("snapshot", signed);
        }
        private WorkspaceOriginSigned<WorkspaceOriginSnapshot>[] ReadSnapshots(OwnerScope owner)
        {
            if (_issuerKey is null || owner.IsLocalSingleUser) return [];
            string dir = OwnerDirectory(owner);
            var paths = Directory.EnumerateFiles(dir, "verified-*.json").Order(StringComparer.Ordinal).Take(MaximumCustodySnapshots + 1).ToArray();
            if (paths.Length > MaximumCustodySnapshots) throw Refused();
            var snapshots = new List<WorkspaceOriginSigned<WorkspaceOriginSnapshot>>();
            for (int index = 0; index < paths.Length; index++)
            {
                if (Path.GetFileName(paths[index]) != "verified-" + index.ToString("D8") + ".json") throw Refused();
                var s = ReadEnrollment<WorkspaceOriginSigned<WorkspaceOriginSnapshot>>(paths[index]);
                if (!ValidSnapshot(owner, s)) throw Refused();
                if (snapshots.LastOrDefault()?.Payload is { } prior
                    && (prior.OwnerEpoch != s.Payload.OwnerEpoch || prior.Revision > s.Payload.Revision)) throw Refused();
                snapshots.Add(s);
            }
            return snapshots.ToArray();
        }
        private string OwnerDirectory(OwnerScope owner)
        {
            if (owner.IsLocalSingleUser || !owner.NormalizedValue.StartsWith("install-account-v1:", StringComparison.Ordinal)
                || !WorkspaceOriginProtocol.Hash(owner.NormalizedValue[19..])) throw Refused();
            ValidateStaticAncestorChain(_root);
            string dir = Path.Combine(_root, "enrollment-" + WorkspaceOriginProtocol.Digest(System.Text.Encoding.UTF8.GetBytes(owner.NormalizedValue)));
            EnsureSecureDirectory(dir, "origin owner custody");
            return dir;
        }
        private T ReadEnrollment<T>(string path)
        {
            ValidateStaticAncestorChain(Path.GetDirectoryName(path)!);
            ThrowIfLinkOrReparsePoint(path, "origin enrollment");
            using var input = OpenSecureFile(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.None);
            if (input.Length is <= 0 or > WorkspaceOriginProtocol.MaximumWireBytes) throw Refused();
            return JsonSerializer.Deserialize<T>(input, WorkspaceOriginProtocol.Json) ?? throw Refused();
        }
        private void WriteEnrollment<T>(string path, T value)
        {
            RequireIssuer();
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, WorkspaceOriginProtocol.Json);
            if (bytes.Length > WorkspaceOriginProtocol.MaximumWireBytes) throw Refused();
            WriteNew(path, bytes);
            if (!OperatingSystem.IsWindows())
            {
                _syncEnrollmentDirectory!(Path.GetDirectoryName(path)!);
                _syncEnrollmentDirectory(_root);
            }
            if (!JsonSerializer.SerializeToUtf8Bytes(ReadEnrollment<T>(path), WorkspaceOriginProtocol.Json).AsSpan().SequenceEqual(bytes)) throw Refused();
        }
        private static IOwnerContextLease Acquire(IOwnerContextLeaseAccessor owners, OwnerContextStamp stamp)
        {
            if (stamp.Owner.IsLocalSingleUser || !owners.TryAcquire(stamp, out var lease) || lease is null) throw Refused();
            try { RequireOwner(owners, lease, stamp); return lease; }
            catch { lease.Dispose(); throw; }
        }
        private static InvalidOperationException Refused() => new("origin-enrollment-refused");
    }
}
