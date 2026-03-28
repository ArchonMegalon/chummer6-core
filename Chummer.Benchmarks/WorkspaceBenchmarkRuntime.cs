using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;

namespace Chummer.Benchmarks;

internal sealed class WorkspaceBenchmarkRuntime : IDisposable
{
    private readonly string _stateDirectory;
    private readonly WorkspaceService _workspaceService;
    private CharacterWorkspaceId? _workspaceId;

    private WorkspaceBenchmarkRuntime(string stateDirectory, WorkspaceService workspaceService)
    {
        _stateDirectory = stateDirectory;
        _workspaceService = workspaceService;
    }

    public static WorkspaceBenchmarkRuntime Create()
    {
        string stateDirectory = Path.Combine(Path.GetTempPath(), "chummer-benchmarks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stateDirectory);

        CharacterFileService fileService = new();
        CharacterSectionService sectionService = new();
        WorkspaceService workspaceService = new(
            new FileWorkspaceStore(stateDirectory),
            new RulesetWorkspaceCodecResolver(
            [
                new Sr5WorkspaceCodec(
                    new XmlCharacterFileQueries(fileService),
                    new XmlCharacterSectionQueries(sectionService),
                    new XmlCharacterMetadataCommands(fileService))
            ]),
            new WorkspaceImportRulesetDetector());

        return new WorkspaceBenchmarkRuntime(stateDirectory, workspaceService);
    }

    public static WorkspaceBenchmarkRuntime CreateImported()
    {
        WorkspaceBenchmarkRuntime runtime = Create();
        runtime.ImportBastion();
        return runtime;
    }

    public WorkspaceImportResult ImportBastion()
    {
        string payload = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Chummer.Tests", "TestFiles", "Bastion.chum5"));
        WorkspaceImportResult imported = _workspaceService.Import(new WorkspaceImportDocument(
            Content: payload,
            RulesetId: string.Empty,
            Format: WorkspaceDocumentFormat.NativeXml));
        _workspaceId = imported.Id;
        return imported;
    }

    public CharacterSkillsSection GetSkillsSection()
    {
        CharacterWorkspaceId id = _workspaceId ?? throw new InvalidOperationException("No imported workspace is available for the benchmark.");
        return _workspaceService.GetSection(id, "skills") as CharacterSkillsSection
            ?? throw new InvalidOperationException("Imported workspace did not yield a skills section.");
    }

    public WorkspaceSaveReceipt Save()
    {
        CharacterWorkspaceId id = _workspaceId ?? throw new InvalidOperationException("No imported workspace is available for the benchmark.");
        return _workspaceService.Save(id).Value
            ?? throw new InvalidOperationException("Imported workspace did not produce a save receipt.");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_stateDirectory))
            {
                Directory.Delete(_stateDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string ResolveRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "WORKLIST.md"))
                && Directory.Exists(Path.Combine(current, "Chummer.Tests"))
                && Directory.Exists(Path.Combine(current, "Chummer.Benchmarks")))
            {
                return current;
            }

            string? parent = Directory.GetParent(current)?.FullName;
            if (string.Equals(parent, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parent ?? string.Empty;
        }

        throw new InvalidOperationException("Unable to resolve the chummer-core-engine repo root for benchmarks.");
    }
}
