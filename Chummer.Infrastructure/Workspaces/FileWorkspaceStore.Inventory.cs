using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    public CommandResult<IReadOnlyList<WorkspaceStoreEntry>> Inspect()
        => InspectCore(OwnerScope.LocalSingleUser);

    public CommandResult<IReadOnlyList<WorkspaceStoreEntry>> Inspect(OwnerScope owner)
        => IsInvalidScopedOwner(owner)
            ? new(false, null, "Owner scope is invalid.", WorkspaceOperationOutcome.Unavailable)
            : InspectCore(owner);

    private CommandResult<IReadOnlyList<WorkspaceStoreEntry>> InspectCore(OwnerScope owner)
    {
        List<WorkspaceStoreEntry> entries = [];
        try
        {
            string directory = GetWorkspaceDirectory(owner);
            if (!TrySecureExistingWorkspaceDirectory(owner))
            {
                // Directory.Exists also returns false for access failures and
                // files in place of directories. Only confirmed absence may
                // establish an empty inventory. Android-owned ancestors remain
                // outside this existing private-state-root boundary.
                return IsConfirmedMissingInventoryDirectory(directory)
                    ? new(true, Array.Empty<WorkspaceStoreEntry>(), null)
                    : new(false, null, "The local runner inventory is unavailable.", WorkspaceOperationOutcome.Unavailable);
            }

            string[] before = RunnerPaths(directory);
            WorkspaceOperationOutcome? failure = null;
            foreach (string path in before)
            {
                CharacterWorkspaceId id = new(Path.GetFileNameWithoutExtension(path));
                string? canonical = TryGetPath(owner, id);
                if (canonical is null || !PathComparer.Equals(Path.GetFullPath(path), canonical))
                {
                    failure ??= WorkspaceOperationOutcome.Corrupt;
                    continue;
                }
                WorkspaceStoreReadResult read = GetCore(owner, id);
                if (!read.Success || read.Value is not { } value)
                {
                    failure ??= read.Outcome == WorkspaceOperationOutcome.Success
                        ? WorkspaceOperationOutcome.Corrupt : read.Outcome;
                    continue;
                }
                entries.Add(ToEntry(value));
            }

            if (!before.SequenceEqual(RunnerPaths(directory), PathComparer))
                failure = WorkspaceOperationOutcome.Conflict;
            IReadOnlyList<WorkspaceStoreEntry> observed = entries
                .OrderByDescending(entry => entry.LastUpdatedUtc).ToArray();
            return failure is { } outcome
                ? new(false, observed, "The local runner inventory is incomplete. Reload or recover the affected runners.", outcome)
                : new(true, observed, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(false, entries.ToArray(), "The local runner inventory is unavailable.", WorkspaceOperationOutcome.Unavailable);
        }
    }

    private static string[] RunnerPaths(string directory)
        => Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, PathComparer).ToArray();

    private bool IsConfirmedMissingInventoryDirectory(string directory)
    {
        EnsurePathContained(_stateDirectory, directory, "workspace inventory directory");
        string current = _stateDirectory;
        string relative = Path.GetRelativePath(_stateDirectory, directory);
        string[] components = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; ; index++)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { return true; }
            catch (DirectoryNotFoundException) { return true; }
            if ((attributes & FileAttributes.ReparsePoint) != 0
                || (attributes & FileAttributes.Directory) == 0)
                throw new IOException("The workspace inventory path is not a regular directory.");
            if (index == components.Length) return false;
            current = Path.Combine(current, components[index]);
        }
    }
}
