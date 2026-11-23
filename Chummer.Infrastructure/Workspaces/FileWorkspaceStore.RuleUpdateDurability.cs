using System.Runtime.InteropServices;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    // Android's application-data directory is provisioned by the OS. Never walk
    // above that private boundary. Linux callers provision the existing parent;
    // remember it before this store creates any missing configured-root segments.
    private static string? FindRuleUpdateDurabilityBoundary(string stateDirectory)
    {
        if (OperatingSystem.IsAndroid())
        {
            string applicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrWhiteSpace(applicationData) ? null : Path.GetFullPath(applicationData);
        }
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows()) return null;
        string? boundary = Path.GetDirectoryName(stateDirectory);
        while (boundary is not null && !Directory.Exists(boundary)) boundary = Path.GetDirectoryName(boundary);
        return boundary is null || Path.GetDirectoryName(boundary) is null ? null : boundary;
    }

    internal bool CanUseRuleUpdateDurability()
    {
        try
        {
            // Probe open/fsync/close, including native resolution, before creating
            // an update entry, capture, attempt, or workspace lease file.
            _faultInjector.OnStage(FileWorkspaceStoreFaultStage.BeforeRuleUpdateNativeProbe, _stateDirectory, _stateDirectory);
            FlushRuleUpdateDirectory(_stateDirectory);
            return true;
        }
        catch (Exception error) when (IsRuleUpdateDurabilityFailure(error)) { return false; }
    }

    private static bool IsRuleUpdateDurabilityFailure(Exception error)
        => error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
            or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;

    private void FlushRuleUpdateDirectory(string directory)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Windows has no directory-fsync claim. Immutable publication and the
                // record journal supply durability; this checks/pins the namespace only.
                using var pinned = PinWindowsRuleUpdateDirectory(directory);
                return;
            }
            if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsLinux()) throw RetainedRuleSourceRejected();
            // O_DIRECTORY, O_CLOEXEC, O_NOFOLLOW. ARM uses different directory
            // and no-follow values from x86. O_RDONLY does not require a mode.
            int flags = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm or Architecture.Arm64 => 0x4000 | 0x80000 | 0x8000,
                Architecture.X86 or Architecture.X64 => 0x10000 | 0x80000 | 0x20000,
                _ => throw RetainedRuleSourceRejected()
            };
            if (_ruleUpdateDurabilityBoundary is not { } boundary || !Directory.Exists(boundary))
                throw RetainedRuleSourceRejected();
            string current = Path.GetFullPath(directory);
            EnsurePathContained(_stateDirectory, current, "rule update directory");
            EnsurePathContained(boundary, _stateDirectory, "rule update durability boundary");
            while (true)
            {
                ThrowIfLinkOrReparsePoint(current, "rule update directory");
                _faultInjector.OnStage(FileWorkspaceStoreFaultStage.BeforeRuleUpdateDirectorySync, current, directory);
                int descriptor = RetainedDirectoryNative.Open(current, flags);
                if (descriptor < 0) throw RetainedRuleSourceRejected();
                try
                {
                    int result;
                    do { result = RetainedDirectoryNative.Fsync(descriptor); }
                    while (result != 0 && Marshal.GetLastPInvokeError() == 4);
                    if (result != 0) throw RetainedRuleSourceRejected();
                }
                finally
                {
                    // Do not retry close: on Linux/Android an EINTR may already
                    // have released the descriptor. Failure remains unacknowledged.
                    if (RetainedDirectoryNative.Close(descriptor) != 0) throw RetainedRuleSourceRejected();
                }
                if (PathComparer.Equals(current, boundary)) break;
                current = Path.GetDirectoryName(current) ?? throw RetainedRuleSourceRejected();
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            // Before writes this is a typed refusal; after publication callers
            // retain Unknown. A loader failure must never escape that distinction.
            throw new IOException("Rule update directory durability is unavailable.", error);
        }
    }

    private static void FlushRuleUpdateFile(string path)
    {
        ThrowIfLinkOrReparsePoint(path, "rule update file");
        if (OperatingSystem.IsWindows())
        {
            using var handle = WindowsRuleUpdateNative.OpenFile(path);
            WindowsRuleUpdateNative.ValidatePath(handle, path, directory: false);
            WindowsRuleUpdateNative.Flush(handle);
            return;
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        stream.Flush(flushToDisk: true);
    }

    private void FlushRuleUpdateRecordDirectory(string path)
    {
        // Intentionally outside the diagnostic AfterTargetReplaced catch. This
        // hook represents a failed required post-rename durability operation.
        try
        {
            _faultInjector.OnStage(FileWorkspaceStoreFaultStage.BeforeRuleUpdateRecordDirectorySync, path, path);
            if (OperatingSystem.IsWindows()) SealWindowsRuleUpdatePublication(path);
            else FlushRuleUpdateDirectory(Path.GetDirectoryName(path)!);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new IOException("Rule update directory durability is unavailable.", error);
        }
    }
}
