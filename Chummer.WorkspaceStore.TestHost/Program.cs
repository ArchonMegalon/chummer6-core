using System.Diagnostics;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;

return Run(args);

static int Run(string[] args)
{
    try
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: <save|save-signal|save-block|delete|delete-signal> <state-directory> <workspace-id> [payload] [signal-files]");
            return 2;
        }

        string operation = args[0];
        string stateDirectory = args[1];
        CharacterWorkspaceId workspaceId = new(args[2]);

        switch (operation)
        {
            case "save" when args.Length == 4:
                return ReplaceCurrent(
                    new FileWorkspaceStore(stateDirectory),
                    workspaceId,
                    CreateDocument(args[3]));
            case "save-signal" when args.Length == 5:
            {
                FileWorkspaceStore store = new(stateDirectory);
                File.WriteAllText(args[4], "started");
                return ReplaceCurrent(store, workspaceId, CreateDocument(args[3]));
            }
            case "save-block" when args.Length == 6:
                return ReplaceCurrent(
                    new FileWorkspaceStore(
                        stateDirectory,
                        new BlockingFaultInjector(args[4], args[5])),
                    workspaceId,
                    CreateDocument(args[3]));
            case "delete" when args.Length == 3:
                return DeleteCurrent(new FileWorkspaceStore(stateDirectory), workspaceId);
            case "delete-signal" when args.Length == 4:
            {
                FileWorkspaceStore store = new(stateDirectory);
                File.WriteAllText(args[3], "started");
                return DeleteCurrent(store, workspaceId);
            }
            default:
                Console.Error.WriteLine("Invalid operation or argument count.");
                return 2;
        }
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception);
        return 1;
    }
}

static int ReplaceCurrent(
    FileWorkspaceStore store,
    CharacterWorkspaceId workspaceId,
    WorkspaceDocument document)
{
    WorkspaceStoreReadResult read = store.Get(workspaceId);
    if (!read.Success || read.Value is not WorkspaceStoredDocument current)
    {
        return 3;
    }

    WorkspaceStoreMutationResult result = store.ReplaceWorkspaceDocument(
        workspaceId,
        current.ContentRevision,
        document);
    return result.Success ? 0 : result.Outcome == WorkspaceOperationOutcome.Conflict ? 4 : 3;
}

static int DeleteCurrent(FileWorkspaceStore store, CharacterWorkspaceId workspaceId)
{
    WorkspaceStoreReadResult read = store.Get(workspaceId);
    if (!read.Success || read.Value is not WorkspaceStoredDocument current)
    {
        return 3;
    }

    WorkspaceStoreMutationResult result = store.Delete(workspaceId, current.ContentRevision);
    return result.Success ? 0 : result.Outcome == WorkspaceOperationOutcome.Conflict ? 4 : 3;
}

static WorkspaceDocument CreateDocument(string payload)
{
    return new WorkspaceDocument(
        $"<character><name>{System.Security.SecurityElement.Escape(payload)}</name></character>",
        RulesetId: RulesetDefaults.Sr5);
}

internal sealed class BlockingFaultInjector : IFileWorkspaceStoreFaultInjector
{
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(20);
    private readonly string _readyFile;
    private readonly string _releaseFile;

    public BlockingFaultInjector(string readyFile, string releaseFile)
    {
        _readyFile = readyFile;
        _releaseFile = releaseFile;
    }

    public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
    {
        if (stage != FileWorkspaceStoreFaultStage.AfterTempFileFlushed)
        {
            return;
        }

        File.WriteAllText(_readyFile, "ready");
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!File.Exists(_releaseFile))
        {
            if (stopwatch.Elapsed >= ReleaseTimeout)
            {
                throw new TimeoutException("Timed out waiting for the test release signal.");
            }

            Thread.Sleep(10);
        }
    }
}
