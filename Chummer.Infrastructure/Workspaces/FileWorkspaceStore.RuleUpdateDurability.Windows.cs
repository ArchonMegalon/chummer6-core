using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    // This is a namespace lease, NOT a directory flush. Windows durability is
    // provided by immutable write-through journal publications and recovery.
    // Keep every ancestor, including those above the configured state root, pinned
    // without DELETE sharing for the duration of each native publication.
    private WindowsRuleUpdateDirectoryLease PinWindowsRuleUpdateDirectory(string directory)
        => PinWindowsRuleUpdateDirectory(_stateDirectory, _faultInjector, directory);

    private static WindowsRuleUpdateDirectoryLease PinWindowsRuleUpdateDirectory(string stateDirectory,
        IFileWorkspaceStoreFaultInjector faults, string directory)
    {
        if (!OperatingSystem.IsWindows()) throw RetainedRuleSourceRejected();
        string full = Path.GetFullPath(directory);
        EnsurePathContained(stateDirectory, full, "rule update directory");
        var paths = new Stack<string>();
        for (string? path = full; path is not null; path = Path.GetDirectoryName(path)) paths.Push(path);
        var result = new WindowsRuleUpdateDirectoryLease();
        try
        {
            uint? volume = null;
            foreach (string path in paths)
            {
                faults.OnStage(FileWorkspaceStoreFaultStage.BeforeRuleUpdateDirectorySync, path, directory);
                var handle = WindowsRuleUpdateNative.OpenDirectory(path);
                result.Handles.Add(handle);
                var info = WindowsRuleUpdateNative.ValidatePath(handle, path, directory: true);
                if (volume is not null && volume != info.VolumeSerialNumber) throw RetainedRuleSourceRejected();
                volume = info.VolumeSerialNumber;
            }
            WindowsRuleUpdateNative.RequireLocalNtfs(full);
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private sealed class WindowsRuleUpdateDirectoryLease : IDisposable
    {
        internal readonly List<SafeFileHandle> Handles = [];
        public void Dispose()
        {
            for (int i = Handles.Count - 1; i >= 0; --i) Handles[i].Dispose();
            Handles.Clear();
        }
    }

    private void PublishWindowsRuleUpdateFile(string temporary, string path)
        => PublishWindowsImmutablePrivateFile(_stateDirectory, _faultInjector, temporary, path);

    // Shared by rule journals and origin custody. New immutable names only;
    // callers retain original-operation reconciliation after any uncertain move.
    private static void PublishWindowsImmutablePrivateFile(string stateDirectory,
        IFileWorkspaceStoreFaultInjector faults, string temporary, string path)
    {
        if (!PathComparer.Equals(Path.GetDirectoryName(temporary), Path.GetDirectoryName(path)))
            throw RetainedRuleSourceRejected();
        using var directories = PinWindowsRuleUpdateDirectory(stateDirectory, faults, Path.GetDirectoryName(path)!);
        // No replace and no cross-volume copy. Existing names, reparse points,
        // and an uncertain previous publication are never silently overwritten.
        ThrowIfLinkOrReparsePoint(path, "rule update publication");
        FlushRuleUpdateFile(temporary);
        WindowsRuleUpdateNative.Publish(temporary, path);
        FlushRuleUpdateFile(path);
    }

    private static class WindowsRuleUpdateNative
    {
        private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
        private const uint OpenExisting = 3, ShareReadWrite = 3;
        private const uint BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000;
        private const uint DirectoryAttribute = 0x10, ReparsePointAttribute = 0x400;

        internal static SafeFileHandle OpenDirectory(string path)
            => Open(path, GenericRead, BackupSemantics | OpenReparsePoint, ShareReadWrite);
        internal static SafeFileHandle OpenFile(string path)
            => Open(path, GenericRead | GenericWrite, OpenReparsePoint, 1);
        private static SafeFileHandle Open(string path, uint access, uint flags, uint share)
        {
            var handle = CreateFileW(path, access, share, IntPtr.Zero, OpenExisting, flags, IntPtr.Zero);
            if (!handle.IsInvalid) return handle;
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw Failure(error);
        }

        internal static FileInformation ValidatePath(SafeFileHandle handle, string expected, bool directory)
        {
            if (!GetFileInformationByHandle(handle, out var info)) throw Failure(Marshal.GetLastPInvokeError());
            if ((info.Attributes & ReparsePointAttribute) != 0
                || ((info.Attributes & DirectoryAttribute) != 0) != directory
                || (!directory && info.Links != 1)) throw RetainedRuleSourceRejected();
            var final = new StringBuilder(32768);
            uint length = GetFinalPathNameByHandleW(handle, final, (uint)final.Capacity, 0);
            if (length == 0 || length >= final.Capacity) throw RetainedRuleSourceRejected();
            string actual = final.ToString();
            if (actual.StartsWith(@"\\?\", StringComparison.Ordinal)) actual = actual[4..];
            if (!PathComparer.Equals(Path.TrimEndingDirectorySeparator(actual), Path.TrimEndingDirectorySeparator(expected)))
                throw RetainedRuleSourceRejected();
            return info;
        }

        internal static void RequireLocalNtfs(string path)
        {
            var mount = new StringBuilder(32768);
            if (!GetVolumePathNameW(path, mount, (uint)mount.Capacity)) throw Failure(Marshal.GetLastPInvokeError());
            // Deliberately limited to fixed local NTFS; SMB/UNC, mapped drives,
            // ReFS, FAT/exFAT and reparse/mount-point chains are not qualified.
            if (GetDriveTypeW(mount.ToString()) != 3) throw RetainedRuleSourceRejected();
            var fs = new StringBuilder(64);
            if (!GetVolumeInformationW(mount.ToString(), null, 0, out _, out _, out uint flags, fs, (uint)fs.Capacity))
                throw Failure(Marshal.GetLastPInvokeError());
            if (!string.Equals(fs.ToString(), "NTFS", StringComparison.OrdinalIgnoreCase) || (flags & 0x80000) != 0)
                throw RetainedRuleSourceRejected(); // FILE_READ_ONLY_VOLUME
        }

        internal static void Publish(string temporary, string path)
        {
            // MOVEFILE_WRITE_THROUGH only. The documented on-disk move is used
            // for a NEW immutable name, never as existing-target adoption.
            if (!MoveFileExW(temporary, path, 0x8)) throw Failure(Marshal.GetLastPInvokeError());
        }
        internal static void Flush(SafeFileHandle handle)
        {
            if (!FlushFileBuffers(handle)) throw Failure(Marshal.GetLastPInvokeError());
        }
        private static IOException Failure(int error)
            => new("Windows rule update durability is unavailable.", new Win32Exception(error));

        [StructLayout(LayoutKind.Sequential)]
        internal struct FileInformation
        {
            internal uint Attributes;
            internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, AccessTime, WriteTime;
            internal uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, Links, FileIndexHigh, FileIndexLow;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumePathNameW(string name, StringBuilder mount, uint length);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint GetDriveTypeW(string root);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumeInformationW(string root, StringBuilder? label, uint labelLength, out uint serial, out uint maximumComponentLength, out uint flags, StringBuilder fs, uint fsLength);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileExW(string existing, string destination, uint flags);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlushFileBuffers(SafeFileHandle handle);
    }
}
