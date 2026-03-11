using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Chummer.Contracts.Workspaces;
using Chummer.Avalonia;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Presentation;

#if false
[TestClass]
[Ignore("Temporarily disabled in Linux CI due Avalonia headless runtime deadlock; kept as migration gate scaffold.")]
public sealed class AvaloniaHeadlessSmokeTests
{
    private static readonly object HeadlessInitLock = new();
    private static bool _headlessInitialized;

    [TestMethod]
    public async Task Avalonia_headless_import_edit_switch_save_smoke()
    {
        FakeCharacterOverviewPresenter presenter = new();
        using CharacterOverviewViewModelAdapter adapter = new(presenter);
        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.ImportAsync(Encoding.UTF8.GetBytes("<character />"), CancellationToken.None);

        CharacterWorkspaceId workspaceA = new("headless-workspace-a");
        CharacterWorkspaceId workspaceB = new("headless-workspace-b");
        await adapter.SwitchWorkspaceAsync(workspaceA, CancellationToken.None);
        await adapter.SwitchWorkspaceAsync(workspaceB, CancellationToken.None);
        await presenter.UpdateMetadataAsync(new UpdateWorkspaceMetadata("Headless Runner", "HR1", "headless smoke"), CancellationToken.None);
        await presenter.SaveAsync(CancellationToken.None);

        Assert.AreEqual(1, presenter.InitializeCalls);
        Assert.AreEqual(1, presenter.SaveCalls);
        Assert.IsNotNull(presenter.ImportedContent);
        Assert.AreEqual(workspaceB.Value, presenter.SwitchedWorkspaceId?.Value);
        Assert.AreEqual("Headless Runner", presenter.UpdatedMetadata?.Name);
        Assert.AreEqual("HR1", presenter.UpdatedMetadata?.Alias);
    }

    [TestMethod]
    public void Avalonia_headless_platform_bootstrap_reference()
    {
        EnsureHeadlessPlatform();
        Assert.IsTrue(_headlessInitialized);
    }

    private static void EnsureHeadlessPlatform()
    {
        lock (HeadlessInitLock)
        {
            if (_headlessInitialized)
                return;

            AppBuilder.Configure<global::Avalonia.Application>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
            _headlessInitialized = true;
        }
    }
}
#endif
