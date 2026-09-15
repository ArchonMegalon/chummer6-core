using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Infrastructure.Workspaces;

/// <summary>
/// Owns only a directory that this instance successfully created exclusively.
/// The host must provision a stable, private local root owned by the process user.
/// Linux only: other platforms have no implicit temporary-directory fallback.
/// </summary>
/// <remarks>
/// Native identities and no-follow observations reject settled replacements and
/// links. Subsequent managed store I/O and deletion remain path based: a hostile
/// same-UID process can race path swaps. This is not a handle-relative sandbox,
/// secure erasure, or a crash-leftover reclamation service.
/// </remarks>
internal sealed class OwnedWorkspaceScratchDirectory : IDisposable
{
    private const int AtCurrentDirectory = -100;
    private const int AtSymlinkNoFollow = 0x100;
    private const uint StatxBasicStats = 0x7ff;
    private const uint StatxMountId = 0x1000;
    private const uint RequiredStatxFields = 0x10b; // type, mode, uid, inode
    private const int AlreadyExists = 17; // Linux EEXIST
    private const ushort TypeMask = 0xf000;
    private const ushort DirectoryType = 0x4000;
    private const ushort RegularFileType = 0x8000;
    private const ushort PermissionMask = 0x1ff;
    private const ushort PrivateDirectoryMode = 0x1c0; // 0700
    private const ushort PrivateFileMode = 0x180; // 0600
    private const int MaximumCleanupEntries = 1024;
    private const int MaximumCleanupDepth = 32;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly object _cleanupGate = new();
    private readonly Root _root;
    private readonly Identity _identity;
    private bool _removed;

    private OwnedWorkspaceScratchDirectory(Root root, string directoryPath, Identity identity)
    {
        _root = root;
        DirectoryPath = directoryPath;
        _identity = identity;
    }

    internal string DirectoryPath { get; }

    internal sealed record Root(string DirectoryPath, Identity Identity);

    internal readonly record struct Identity(ulong Inode, uint DeviceMajor, uint DeviceMinor, ulong MountId);

    internal static Root ValidateRoot(string path)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Private workspace scratch storage requires Linux.");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
            || path.Any(char.IsControl))
            throw InvalidRoot();
        try
        {
            _ = StrictUtf8.GetByteCount(path);
            string canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (!string.Equals(path, canonical, StringComparison.Ordinal)
                || string.Equals(canonical, Path.GetPathRoot(canonical), StringComparison.Ordinal))
                throw InvalidRoot();
            RequireDirectoryAncestors(canonical);
            NativeStatx state = Observe(canonical);
            RequirePrivateDirectory(state);
            RequireLocalFileSystem(canonical);
            return new(canonical, GetIdentity(state));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                          or EncoderFallbackException or NotSupportedException)
        {
            throw InvalidRoot();
        }
    }

    internal static OwnedWorkspaceScratchDirectory Allocate(Root root)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Private workspace scratch storage requires Linux.");
        RequireCurrentRoot(root);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            string path = Path.Combine(root.DirectoryPath,
                "core-request-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
            RequireDirectChild(root.DirectoryPath, path);
            if (NativeMethods.Mkdir(path, PrivateDirectoryMode) != 0)
            {
                if (Marshal.GetLastPInvokeError() == AlreadyExists)
                    continue;
                throw StorageUnavailable();
            }

            // mkdir returning success, never an existence check, establishes
            // initial ownership. A restrictive umask may remove owner bits.
            // The root remains externally provisioned and is never chmod'ed.
            NativeStatx created = Observe(path);
            if ((created.Mode & TypeMask) != DirectoryType || created.UserId != NativeMethods.GetEffectiveUserId())
                throw StorageUnavailable();
            var owned = new OwnedWorkspaceScratchDirectory(root, path, GetIdentity(created));
            try
            {
                File.SetUnixFileMode(path, (UnixFileMode)PrivateDirectoryMode);
                owned.RequireCurrentDirectory();
                return owned;
            }
            catch
            {
                // Cleanup itself refuses uncertain ownership rather than
                // broadening deletion after an allocation-time path change.
                owned.Dispose();
                throw;
            }
        }
        throw StorageUnavailable();
    }

    public void Dispose()
    {
        lock (_cleanupGate)
        {
            if (_removed)
                return;
            try
            {
                RequireCurrentDirectory();
                var inventory = new List<Entry>();
                CaptureEntries(DirectoryPath, 0, inventory);
                // Validate the complete inventory before removing anything.
                // A second identity check immediately precedes each deletion.
                foreach (Entry entry in inventory)
                    RequireEntry(entry);
                for (int index = inventory.Count - 1; index >= 0; index--)
                {
                    RequireCurrentDirectory();
                    Entry entry = inventory[index];
                    RequireEntry(entry);
                    if (entry.IsDirectory)
                        Directory.Delete(entry.Path, recursive: false);
                    else
                        File.Delete(entry.Path);
                }
                RequireCurrentDirectory();
                Directory.Delete(DirectoryPath, recursive: false);
                _removed = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or InvalidOperationException or ArgumentException)
            {
                // Do not expose paths, character data, or underlying exceptions.
                // Ownership is retained so an explicit retry can finish a
                // partial deletion after the host resolves the failure.
                throw new IOException("Private workspace scratch cleanup could not be completed safely.");
            }
        }
    }

    private void CaptureEntries(string directory, int depth, List<Entry> entries)
    {
        if (depth > MaximumCleanupDepth)
            throw StorageUnavailable();
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            if (entries.Count >= MaximumCleanupEntries)
                throw StorageUnavailable();
            RequireDescendant(DirectoryPath, path);
            NativeStatx state = Observe(path);
            bool isDirectory = (state.Mode & TypeMask) == DirectoryType;
            if (!isDirectory && (state.Mode & TypeMask) != RegularFileType)
                throw StorageUnavailable();
            ushort permissions = isDirectory ? PrivateDirectoryMode : PrivateFileMode;
            if ((state.Mode & PermissionMask) != permissions
                || state.UserId != NativeMethods.GetEffectiveUserId()
                || state.DeviceMajor != _identity.DeviceMajor || state.DeviceMinor != _identity.DeviceMinor
                || GetIdentity(state).MountId != _identity.MountId)
                throw StorageUnavailable();
            var entry = new Entry(path, GetIdentity(state), isDirectory);
            entries.Add(entry);
            if (isDirectory)
                CaptureEntries(path, depth + 1, entries);
        }
    }

    private void RequireCurrentDirectory()
    {
        RequireCurrentRoot(_root);
        RequireDirectChild(_root.DirectoryPath, DirectoryPath);
        NativeStatx state = Observe(DirectoryPath);
        RequirePrivateDirectory(state);
        if (GetIdentity(state) != _identity)
            throw StorageUnavailable();
    }

    private static void RequireCurrentRoot(Root root)
    {
        Root current = ValidateRoot(root.DirectoryPath);
        if (current.Identity != root.Identity)
            throw StorageUnavailable();
    }

    private static void RequireEntry(Entry entry)
    {
        NativeStatx state = Observe(entry.Path);
        if (GetIdentity(state) != entry.Identity
            || (state.Mode & TypeMask) != (entry.IsDirectory ? DirectoryType : RegularFileType))
            throw StorageUnavailable();
    }

    private static void RequirePrivateDirectory(NativeStatx state)
    {
        if ((state.Mode & TypeMask) != DirectoryType
            || (state.Mode & PermissionMask) != PrivateDirectoryMode
            || state.UserId != NativeMethods.GetEffectiveUserId())
            throw StorageUnavailable();
    }

    private static void RequireDirectoryAncestors(string fullPath)
    {
        string current = Path.GetPathRoot(fullPath)!;
        if ((Observe(current).Mode & TypeMask) != DirectoryType)
            throw StorageUnavailable();
        foreach (string component in Path.GetRelativePath(current, fullPath).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, component);
            if ((Observe(current).Mode & TypeMask) != DirectoryType)
                throw StorageUnavailable();
        }
    }

    private static void RequireLocalFileSystem(string path)
    {
        // Best-effort platform observation, not proof against host-controlled
        // bind mounts or unusual filesystem implementations. The host still
        // must provision local storage suitable for FileWorkspaceStore.
        var drive = new DriveInfo(path);
        string format = drive.DriveFormat;
        if (drive.DriveType == DriveType.Network
            || format.StartsWith("nfs", StringComparison.OrdinalIgnoreCase)
            || format.Equals("cifs", StringComparison.OrdinalIgnoreCase)
            || format.Equals("smbfs", StringComparison.OrdinalIgnoreCase)
            || format.Equals("9p", StringComparison.OrdinalIgnoreCase))
            throw StorageUnavailable();
    }

    private static void RequireDirectChild(string root, string child)
    {
        RequireDescendant(root, child);
        if (!string.Equals(Path.GetDirectoryName(child), root, StringComparison.Ordinal))
            throw StorageUnavailable();
    }

    private static void RequireDescendant(string root, string child)
    {
        if (!string.Equals(Path.GetFullPath(child), child, StringComparison.Ordinal)
            || !child.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw StorageUnavailable();
    }

    private static NativeStatx Observe(string path)
    {
        if (NativeMethods.Statx(AtCurrentDirectory, path, AtSymlinkNoFollow,
                StatxBasicStats | StatxMountId, out NativeStatx state) != 0
            || (state.Mask & RequiredStatxFields) != RequiredStatxFields)
            throw StorageUnavailable();
        return state;
    }

    private static Identity GetIdentity(NativeStatx state) => new(state.Inode,
        state.DeviceMajor, state.DeviceMinor, (state.Mask & StatxMountId) != 0 ? state.MountId : 0);

    private static ArgumentException InvalidRoot() => new(
        "An existing canonical private local scratch root owned by the process user is required.", "privateScratchRoot");

    private static IOException StorageUnavailable() => new("Private workspace scratch storage is unavailable.");

    private sealed record Entry(string Path, Identity Identity, bool IsDirectory);

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct NativeStatx
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(20)] public uint UserId;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
        [FieldOffset(144)] public ulong MountId;
    }

    private static class NativeMethods
    {
        [DllImport("libc", EntryPoint = "mkdir", SetLastError = true)]
        internal static extern int Mkdir([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);

        [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
        internal static extern int Statx(int directoryFileDescriptor,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out NativeStatx state);

        [DllImport("libc", EntryPoint = "geteuid")]
        internal static extern uint GetEffectiveUserId();
    }
}
