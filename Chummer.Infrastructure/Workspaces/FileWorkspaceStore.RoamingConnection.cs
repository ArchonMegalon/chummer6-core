using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

// One bounded transport slot beside the existing workspace. It is local custody,
// not portable authority. No token, private key or HTTP response body is persisted.
public sealed record WorkspaceRoamingPublication(Guid OperationId, string Original,
    WorkspaceRoamingRuleEnvironmentSnapshot Snapshot)
{
    public override string ToString() => nameof(WorkspaceRoamingPublication);
}

public sealed record WorkspaceRoamingAdoption(Guid OperationId, string AdmissionDigest,
    WorkspaceRoamingRuleEnvironmentSelection Selection)
{
    public override string ToString() => nameof(WorkspaceRoamingAdoption);
}

public sealed record WorkspaceRoamingLocalCapture(Guid OperationId, string AdmissionDigest,
    WorkspaceRoamingRuleEnvironmentSnapshot Snapshot)
{
    public override string ToString() => nameof(WorkspaceRoamingLocalCapture);
}

public sealed record WorkspaceRoamingConnectionState(long Version, OwnerContextStamp Owner,
    WorkspaceRoamingRuleEnvironmentSelection? CommonBase = null,
    string? LocalBaseDigest = null, WorkspaceRoamingPublication? Publication = null,
    WorkspaceRoamingAdoption? Adoption = null, WorkspaceRoamingLocalCapture? Capture = null)
{
    public override string ToString() => nameof(WorkspaceRoamingConnectionState);
}

public sealed partial class FileWorkspaceStore
{
    private const int MaximumRoamingConnectionBytes = 256 * 1024;
    private static readonly JsonSerializerOptions RoamingConnectionJson = new()
    {
        MaxDepth = 132,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public IReadOnlyList<CharacterWorkspaceId> ListRoamingConnections(IOwnerContextLeaseAccessor owners,
        OwnerContextStamp owner)
    {
        if (!owner.IsValid || !owners.TryAcquire(owner, out var lease)) throw RoamingConnectionUnavailable();
        using (lease)
        {
            if (lease.Stamp != owner) throw RoamingConnectionUnavailable();
            var normalized = new OwnerScope(owner.Owner.NormalizedValue);
            if (!TrySecureExistingWorkspaceDirectory(normalized, allowLegacyMigration: false))
            {
                if (IsConfirmedMissingInventoryDirectory(GetWorkspaceDirectory(normalized))) return [];
                throw RoamingConnectionUnavailable();
            }
            const string suffix = ".json.roaming-connection";
            var result = new List<CharacterWorkspaceId>();
            foreach (string path in Directory.EnumerateFiles(GetWorkspaceDirectory(normalized), "*" + suffix))
            {
                var id = new CharacterWorkspaceId(Path.GetFileName(path)[..^suffix.Length]);
                if (RoamingConnectionPath(normalized, id) != path) throw RoamingConnectionUnavailable();
                ThrowIfLinkOrReparsePoint(path, "roaming connection");
                result.Add(id);
            }
            return result;
        }
    }

    public WorkspaceRoamingConnectionState? ReadRoamingConnection(IOwnerContextLeaseAccessor owners,
        OwnerContextStamp owner, CharacterWorkspaceId id)
    {
        if (!owner.IsValid || !owners.TryAcquire(owner, out var lease)) throw RoamingConnectionUnavailable();
        using (lease)
        {
            if (lease.Stamp != owner) throw RoamingConnectionUnavailable();
            var normalized = new OwnerScope(owner.Owner.NormalizedValue);
            string path = RoamingConnectionPath(normalized, id);
            if (!TrySecureExistingWorkspaceDirectory(normalized, allowLegacyMigration: false))
            {
                if (IsConfirmedMissingInventoryDirectory(GetWorkspaceDirectory(normalized))) return null;
                throw RoamingConnectionUnavailable();
            }
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            return ReadRoamingConnectionUnderLease(path, owner, id);
        }
    }

    // CAS prevents two desktop processes from issuing distinct original publications.
    // Only a successful durable write returns true. A failed acknowledgement is never
    // permission to dispatch; reopening the slot can only lead to read-only recovery.
    public bool SaveRoamingConnection(IOwnerContextLeaseAccessor owners, OwnerContextStamp owner,
        CharacterWorkspaceId id, long expectedVersion, WorkspaceRoamingConnectionState next)
    {
        if (!owner.IsValid || next.Owner != owner || next.Version != checked(expectedVersion + 1)
            || !owners.TryAcquire(owner, out var lease)) throw RoamingConnectionUnavailable();
        using (lease)
        {
            if (lease.Stamp != owner) throw RoamingConnectionUnavailable();
            var normalized = new OwnerScope(owner.Owner.NormalizedValue);
            EnsureRestoreWorkspaceDirectory(normalized);
            string path = RoamingConnectionPath(normalized, id);
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            var previous = ReadRoamingConnectionUnderLease(path, owner, id);
            if ((previous?.Version ?? 0) != expectedVersion) return false;
            // An old authority's pending work cannot be transferred by signing in again.
            if (previous is { } old && old.Owner != owner
                && (old.Publication is not null || old.Adoption is not null || old.Capture is not null)) throw RoamingConnectionUnavailable();
            ValidateRoamingConnection(next, owner, id);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(next, RoamingConnectionJson);
            if (bytes.Length > MaximumRoamingConnectionBytes) throw RoamingConnectionUnavailable();
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = OpenNewSecureFile(temporary, FileAccess.Write, FileShare.None, FileOptions.WriteThrough))
                { stream.Write(bytes); stream.Flush(flushToDisk: true); }
                _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterTempFileFlushed, path, temporary);
                ThrowIfLinkOrReparsePoint(path, "roaming connection");
                if (OperatingSystem.IsWindows())
                {
                    // Same-directory rename on the private local volume. Unlike the
                    // Android fsync implementation, this uses Windows' write-through
                    // move contract and checks its actual success before dispatch.
                    if (!RoamingWindowsNative.MoveFileEx(temporary, path, 0x1 | 0x8))
                        throw new IOException("The private sync record could not be committed.",
                            new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
                }
                else if (OperatingSystem.IsLinux())
                {
                    File.Move(temporary, path, overwrite: true);
                    _ = FlushRetainedDirectoryChain(Path.GetDirectoryName(path)!);
                }
                else throw new IOException("Private sync durability is not supported on this platform.");
                // Deliberately outside a swallowed diagnostic hook: uncertainty stays pending.
                _faultInjector.OnStage(FileWorkspaceStoreFaultStage.AfterTargetReplaced, path, temporary);
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    // The caller already holds the exact live owner lease; do not reacquire it.
    internal WorkspaceRoamingConnectionState? ReadRoamingConnectionForReview(OwnerContextStamp owner,
        CharacterWorkspaceId id)
    {
        var normalized = new OwnerScope(owner.Owner.NormalizedValue);
        if (!TrySecureExistingWorkspaceDirectory(normalized, allowLegacyMigration: false)) return null;
        string path = RoamingConnectionPath(normalized, id);
        using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        return ReadRoamingConnectionUnderLease(path, owner, id);
    }

    private string RoamingConnectionPath(OwnerScope owner, CharacterWorkspaceId id)
        => ValidRestoreOwner(owner) && TryGetPath(owner, id) is { } path
            ? path + ".roaming-connection" : throw RoamingConnectionUnavailable();

    private WorkspaceRoamingConnectionState? ReadRoamingConnectionUnderLease(string path,
        OwnerContextStamp owner, CharacterWorkspaceId id)
    {
        ThrowIfLinkOrReparsePoint(path, "roaming connection");
        if (!File.Exists(path)) return null;
        using var stream = OpenSecureFile(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.None);
        if (stream.Length is <= 0 or > MaximumRoamingConnectionBytes) throw RoamingConnectionUnavailable();
        try
        {
            using var document = JsonDocument.Parse(stream, new() { MaxDepth = 132 });
            ValidatePrivateJsonProperties(document.RootElement);
            var state = document.RootElement.Deserialize<WorkspaceRoamingConnectionState>(RoamingConnectionJson)
                ?? throw RoamingConnectionUnavailable();
            ValidateRoamingConnection(state, owner, id);
            return state;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
            or ArgumentException or KeyNotFoundException or FormatException)
        { throw RoamingConnectionUnavailable(); }
    }

    private static void ValidateRoamingConnection(WorkspaceRoamingConnectionState state,
        OwnerContextStamp owner, CharacterWorkspaceId id)
    {
        // Retain and report old pending state, but never dispatch it under a new stamp.
        if (state.Version <= 0 || !state.Owner.IsValid || state.Owner.Owner != owner.Owner
            || (state.Publication is null ? 0 : 1) + (state.Adoption is null ? 0 : 1) + (state.Capture is null ? 0 : 1) > 1)
            throw RoamingConnectionUnavailable();
        if (state.CommonBase is { } common && (!common.IsValid(owner.Owner.NormalizedValue)
                || common.Snapshot.WorkspaceId != id.Value || state.LocalBaseDigest != common.Snapshot.WorkspaceContinuationDigest)
            || state.CommonBase is null && state.LocalBaseDigest is not null)
            throw RoamingConnectionUnavailable();
        if (state.Publication is { } publication)
        {
            if (publication.OperationId == Guid.Empty || publication.Original is null
                || System.Text.Encoding.UTF8.GetByteCount(publication.Original) > 64 * 1024
                || publication.Snapshot is null || !publication.Snapshot.IsValid(owner.Owner.NormalizedValue)
                || publication.Snapshot.WorkspaceId != id.Value)
                throw RoamingConnectionUnavailable();
            using var original = JsonDocument.Parse(publication.Original, new() { MaxDepth = 132 });
            var root = original.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw RoamingConnectionUnavailable();
            ValidatePrivateJsonProperties(root);
            var pointer = root.Deserialize<WorkspaceRoamingRuleEnvironmentSnapshot>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (pointer != publication.Snapshot
                || root.GetProperty("installationId").GetString() != state.Owner.AuthorityInstanceId
                || root.GetProperty("originInstallationId").GetString() != state.Owner.AuthorityInstanceId
                || root.GetProperty("workspaceSnapshot").ValueKind != JsonValueKind.Null
                || root.GetProperty("workspaceSnapshotDigest").ValueKind != JsonValueKind.Null
                || root.GetProperty("workspaceContinuation").ValueKind != JsonValueKind.Null
                || !root.GetProperty("expectedRemoteRevision").TryGetInt64(out long expectedRevision)
                || expectedRevision < 0
                || expectedRevision == 0 && root.GetProperty("expectedServerToken").ValueKind != JsonValueKind.Null
                || expectedRevision > 0 && !IsRoamingDigest(root.GetProperty("expectedServerToken").GetString())
                || state.CommonBase is { } prior && (prior.RemoteRevision != expectedRevision
                    || prior.ServerToken != root.GetProperty("expectedServerToken").GetString())
                || state.CommonBase is null && expectedRevision != 0)
                throw RoamingConnectionUnavailable();
        }
        if (state.Adoption is { } adoption && (adoption.OperationId == Guid.Empty
            || !IsRoamingDigest(adoption.AdmissionDigest) || adoption.Selection is null
                || !adoption.Selection.IsValid(owner.Owner.NormalizedValue)
                || adoption.Selection.Snapshot.WorkspaceId != id.Value)) throw RoamingConnectionUnavailable();
        if (state.Capture is { } capture && (capture.OperationId == Guid.Empty || !IsRoamingDigest(capture.AdmissionDigest)
            || capture.Snapshot is null
            || !capture.Snapshot.IsValid(owner.Owner.NormalizedValue) || capture.Snapshot.WorkspaceId != id.Value))
            throw RoamingConnectionUnavailable();
    }

    private static bool IsRoamingDigest(string? value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void ValidatePrivateJsonProperties(JsonElement value)
    {
        // Reject ambiguous JSON and credentials, including nested/alternate-case
        // spellings, before an original can be persisted or recovered remotely.
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name) || property.Name.Equals("accessToken", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("grantToken", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("privateKey", StringComparison.OrdinalIgnoreCase)) throw RoamingConnectionUnavailable();
                ValidatePrivateJsonProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var element in value.EnumerateArray()) ValidatePrivateJsonProperties(element);
    }

    private static IOException RoamingConnectionUnavailable() => new("The saved sync state requires recovery.");

    private static class RoamingWindowsNative
    {
        [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool MoveFileEx(string existing, string replacement, uint flags);
    }
}
