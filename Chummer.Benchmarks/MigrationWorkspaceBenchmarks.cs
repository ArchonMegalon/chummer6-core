using BenchmarkDotNet.Attributes;

namespace Chummer.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class MigrationWorkspaceBenchmarks
{
    private WorkspaceBenchmarkRuntime? _sectionRuntime;
    private WorkspaceBenchmarkRuntime? _saveRuntime;

    [Benchmark(Description = "workspace.import.bastion")]
    public object ImportBastionLegacyWorkspace()
    {
        using WorkspaceBenchmarkRuntime runtime = WorkspaceBenchmarkRuntime.Create();
        return runtime.ImportBastion().Summary;
    }

    [GlobalSetup(Target = nameof(GetSkillsSectionFromImportedBastionWorkspace))]
    public void SetupSectionScenario()
    {
        _sectionRuntime = WorkspaceBenchmarkRuntime.CreateImported();
    }

    [GlobalCleanup(Target = nameof(GetSkillsSectionFromImportedBastionWorkspace))]
    public void CleanupSectionScenario()
    {
        _sectionRuntime?.Dispose();
        _sectionRuntime = null;
    }

    [Benchmark(Description = "workspace.section.skills.bastion")]
    public object GetSkillsSectionFromImportedBastionWorkspace()
    {
        return (_sectionRuntime ?? throw new InvalidOperationException("Section benchmark runtime is not initialized."))
            .GetSkillsSection();
    }

    [GlobalSetup(Target = nameof(SaveImportedBastionWorkspace))]
    public void SetupSaveScenario()
    {
        _saveRuntime = WorkspaceBenchmarkRuntime.CreateImported();
    }

    [GlobalCleanup(Target = nameof(SaveImportedBastionWorkspace))]
    public void CleanupSaveScenario()
    {
        _saveRuntime?.Dispose();
        _saveRuntime = null;
    }

    [Benchmark(Description = "workspace.save.bastion")]
    public object SaveImportedBastionWorkspace()
    {
        return (_saveRuntime ?? throw new InvalidOperationException("Save benchmark runtime is not initialized."))
            .Save();
    }

    internal static IReadOnlyList<BenchmarkWorkload> CreateBudgetWorkloads()
    {
        MigrationWorkspaceBenchmarks benchmarks = new();
        return
        [
            new BenchmarkWorkload(
                Name: "workspace.import.bastion",
                Setup: static () => { },
                Execute: benchmarks.ImportBastionLegacyWorkspace,
                Cleanup: static () => { }),
            new BenchmarkWorkload(
                Name: "workspace.section.skills.bastion",
                Setup: benchmarks.SetupSectionScenario,
                Execute: benchmarks.GetSkillsSectionFromImportedBastionWorkspace,
                Cleanup: benchmarks.CleanupSectionScenario),
            new BenchmarkWorkload(
                Name: "workspace.save.bastion",
                Setup: benchmarks.SetupSaveScenario,
                Execute: benchmarks.SaveImportedBastionWorkspace,
                Cleanup: benchmarks.CleanupSaveScenario)
        ];
    }
}
