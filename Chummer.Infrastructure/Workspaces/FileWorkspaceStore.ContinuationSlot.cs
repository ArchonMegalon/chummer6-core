using System.Text;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    // Durable target-slot generation, not a cache or stale lock. It remains after
    // deletion so a previously reviewed missing target cannot be resurrected by
    // a create/delete ABA. Rotation precedes mutation: a crash may conservatively
    // invalidate a review, never make an obsolete review valid again.
    private static string? ReadContinuationSlotUnderLease(string workspacePath)
    {
        string path = workspacePath + ".slot";
        ThrowIfLinkOrReparsePoint(path, "workspace slot generation");
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != 32) throw new IOException("Workspace slot generation is invalid.");
            Span<byte> bytes = stackalloc byte[32];
            stream.ReadExactly(bytes);
            string value = Encoding.ASCII.GetString(bytes);
            if (!Guid.TryParseExact(value, "N", out var generation) || generation == Guid.Empty
                || generation.ToString("N") != value)
                throw new IOException("Workspace slot generation is invalid.");
            return value;
        }
        catch (FileNotFoundException) { return null; }
    }

    private static void RotateContinuationSlotUnderLease(string workspacePath)
    {
        string path = workspacePath + ".slot";
        string temporary = path + TempFileMarker + Guid.NewGuid().ToString("N");
        ThrowIfLinkOrReparsePoint(path, "workspace slot generation");
        // A malformed existing marker is not repaired by silently rotating it.
        _ = ReadContinuationSlotUnderLease(workspacePath);
        RemoveStaleTempFiles(path);
        bool moved = false;
        try
        {
            using (var stream = OpenNewSecureFile(temporary, FileAccess.Write, FileShare.None, FileOptions.WriteThrough))
            {
                stream.Write(Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N")));
                stream.Flush(flushToDisk: true);
            }
            ThrowIfLinkOrReparsePoint(path, "workspace slot generation");
            File.Move(temporary, path, overwrite: true);
            moved = true;
        }
        finally
        {
            if (!moved) DeleteRegularFileIfPresent(temporary, "workspace slot generation temporary file");
        }
    }
}
