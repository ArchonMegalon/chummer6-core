using BenchmarkDotNet.Attributes;
using Chummer.Application.Explain;
using Chummer.Contracts;

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

    [Benchmark(Description = "runtime.explain.trace")]
    public object ComposeExplainTraceReceipt()
    {
        DefaultExplainHookComposer composer = new();
        ExplainHookAttachment[] attachments =
        [
            new(
                TargetKind: "workspace",
                TargetId: "bastion",
                Explain: composer.CreateReference(
                    targetKind: "workspace",
                    targetId: "bastion",
                    traceId: "trace-bastion-import",
                    subjectId: "bastion",
                    capabilityId: "workspace.import",
                    providerId: "chummer6-core",
                    packId: "sr5.core",
                    runtimeFingerprint: "runtime:sr5:core")),
            new(
                TargetKind: "section",
                TargetId: "skills",
                Explain: composer.CreateReference(
                    targetKind: "section",
                    targetId: "skills",
                    traceId: "trace-bastion-skills",
                    subjectId: "skills",
                    capabilityId: "workspace.section.skills",
                    providerId: "chummer6-core",
                    packId: "sr5.core",
                    runtimeFingerprint: "runtime:sr5:core")),
            new(
                TargetKind: "export",
                TargetId: "portable-package",
                Explain: composer.CreateReference(
                    targetKind: "export",
                    targetId: "portable-package",
                    traceId: "trace-bastion-export",
                    subjectId: "portable-package",
                    capabilityId: "workspace.export",
                    providerId: "chummer6-core",
                    packId: "sr5.core",
                    runtimeFingerprint: "runtime:sr5:core"))
        ];

        return composer.Compose("release-proof-pack.bastion", attachments);
    }

    [GlobalSetup(Target = nameof(PrepareExportForImportedBastionWorkspace))]
    public void SetupExportScenario()
    {
        _saveRuntime = WorkspaceBenchmarkRuntime.CreateImported();
    }

    [GlobalCleanup(Target = nameof(PrepareExportForImportedBastionWorkspace))]
    public void CleanupExportScenario()
    {
        _saveRuntime?.Dispose();
        _saveRuntime = null;
    }

    [Benchmark(Description = "workspace.export.bastion")]
    public object PrepareExportForImportedBastionWorkspace()
    {
        return (_saveRuntime ?? throw new InvalidOperationException("Export benchmark runtime is not initialized."))
            .Export();
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
                Cleanup: benchmarks.CleanupSaveScenario),
            new BenchmarkWorkload(
                Name: "runtime.explain.trace",
                Setup: static () => { },
                Execute: benchmarks.ComposeExplainTraceReceipt,
                Cleanup: static () => { }),
            new BenchmarkWorkload(
                Name: "workspace.export.bastion",
                Setup: benchmarks.SetupExportScenario,
                Execute: benchmarks.PrepareExportForImportedBastionWorkspace,
                Cleanup: benchmarks.CleanupExportScenario)
        ];
    }
}
