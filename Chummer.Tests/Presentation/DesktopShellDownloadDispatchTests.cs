#nullable enable annotations

using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using Chummer.Blazor;
using Chummer.Blazor.Components.Layout;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Presentation;

[TestClass]
public sealed class DesktopShellDownloadDispatchTests
{
    [TestMethod]
    public void OnAfterRenderAsync_dispatches_pending_download_once_per_version()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        FakeCharacterOverviewPresenter presenter = new();
        presenter.Publish(CreateOverviewState(CreateReceipt("ws-1"), 1));
        TrackingShellPresenter shellPresenter = new(ShellState.Empty);
        RegisterDesktopShellServices(context, presenter, shellPresenter);

        IRenderedComponent<DesktopShell> cut = context.Render<DesktopShell>();

        cut.WaitForAssertion(() => Assert.AreEqual(1, DownloadInvocationCount(context)));
        var firstInvocation = context.JSInterop.Invocations
            .First(invocation => string.Equals(invocation.Identifier, "chummerDownloads.downloadBase64", StringComparison.Ordinal));
        Assert.AreEqual("ws-1.chum5", firstInvocation.Arguments[0]?.ToString());

        presenter.Publish(CreateOverviewState(CreateReceipt("ws-1"), 1));
        cut.WaitForAssertion(() => Assert.AreEqual(1, DownloadInvocationCount(context)));

        presenter.Publish(CreateOverviewState(CreateReceipt("ws-1"), 2));
        cut.WaitForAssertion(() => Assert.AreEqual(2, DownloadInvocationCount(context)));
    }

    [TestMethod]
    public void OnAfterRenderAsync_does_not_dispatch_when_download_is_missing()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        FakeCharacterOverviewPresenter presenter = new();
        presenter.Publish(CreateOverviewState(receipt: null, version: 0));
        TrackingShellPresenter shellPresenter = new(ShellState.Empty);
        RegisterDesktopShellServices(context, presenter, shellPresenter);

        IRenderedComponent<DesktopShell> cut = context.Render<DesktopShell>();
        cut.WaitForAssertion(() => Assert.AreEqual(0, DownloadInvocationCount(context)));
    }

    [TestMethod]
    public void OnAfterRenderAsync_dispatches_json_download_with_json_mime_type()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        FakeCharacterOverviewPresenter presenter = new();
        presenter.Publish(CreateOverviewState(CreateReceipt("ws-1", WorkspaceDocumentFormat.Json, "ws-1-export.json"), 1));
        TrackingShellPresenter shellPresenter = new(ShellState.Empty);
        RegisterDesktopShellServices(context, presenter, shellPresenter);

        IRenderedComponent<DesktopShell> cut = context.Render<DesktopShell>();

        cut.WaitForAssertion(() => Assert.AreEqual(1, DownloadInvocationCount(context)));
        var invocation = context.JSInterop.Invocations
            .First(item => string.Equals(item.Identifier, "chummerDownloads.downloadBase64", StringComparison.Ordinal));
        Assert.AreEqual("ws-1-export.json", invocation.Arguments[0]?.ToString());
        Assert.AreEqual("application/json", invocation.Arguments[2]?.ToString());
    }

    [TestMethod]
    public void OnAfterRenderAsync_dispatches_pending_export_once_per_version()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        FakeCharacterOverviewPresenter presenter = new();
        presenter.Publish(CreateOverviewState(exportReceipt: CreateExportReceipt("ws-1"), exportVersion: 1));
        TrackingShellPresenter shellPresenter = new(ShellState.Empty);
        RegisterDesktopShellServices(context, presenter, shellPresenter);

        IRenderedComponent<DesktopShell> cut = context.Render<DesktopShell>();

        cut.WaitForAssertion(() => Assert.AreEqual(1, ExportInvocationCount(context)));
        var invocation = context.JSInterop.Invocations
            .First(item => string.Equals(item.Identifier, "chummerExports.downloadBase64", StringComparison.Ordinal));
        Assert.AreEqual("ws-1-export.json", invocation.Arguments[0]?.ToString());
        Assert.AreEqual("application/json", invocation.Arguments[2]?.ToString());

        presenter.Publish(CreateOverviewState(exportReceipt: CreateExportReceipt("ws-1"), exportVersion: 1));
        cut.WaitForAssertion(() => Assert.AreEqual(1, ExportInvocationCount(context)));
    }

    [TestMethod]
    public void OnAfterRenderAsync_dispatches_pending_print_once_per_version()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        FakeCharacterOverviewPresenter presenter = new();
        presenter.Publish(CreateOverviewState(printReceipt: CreatePrintReceipt("ws-1"), printVersion: 1));
        TrackingShellPresenter shellPresenter = new(ShellState.Empty);
        RegisterDesktopShellServices(context, presenter, shellPresenter);

        IRenderedComponent<DesktopShell> cut = context.Render<DesktopShell>();

        cut.WaitForAssertion(() => Assert.AreEqual(1, PrintInvocationCount(context)));
        var invocation = context.JSInterop.Invocations
            .First(item => string.Equals(item.Identifier, "chummerPrints.openBase64", StringComparison.Ordinal));
        Assert.AreEqual("ws-1-print.html", invocation.Arguments[0]?.ToString());
        Assert.AreEqual("text/html", invocation.Arguments[2]?.ToString());
        Assert.AreEqual("Runner", invocation.Arguments[3]?.ToString());
    }

    private static int DownloadInvocationCount(BunitContext context)
    {
        return context.JSInterop.Invocations.Count(invocation =>
            string.Equals(invocation.Identifier, "chummerDownloads.downloadBase64", StringComparison.Ordinal));
    }

    private static int ExportInvocationCount(BunitContext context)
    {
        return context.JSInterop.Invocations.Count(invocation =>
            string.Equals(invocation.Identifier, "chummerExports.downloadBase64", StringComparison.Ordinal));
    }

    private static int PrintInvocationCount(BunitContext context)
    {
        return context.JSInterop.Invocations.Count(invocation =>
            string.Equals(invocation.Identifier, "chummerPrints.openBase64", StringComparison.Ordinal));
    }

    private static CharacterOverviewState CreateOverviewState(
        WorkspaceDownloadReceipt? receipt = null,
        long version = 0,
        WorkspaceExportReceipt? exportReceipt = null,
        long exportVersion = 0,
        WorkspacePrintReceipt? printReceipt = null,
        long printVersion = 0)
    {
        return CharacterOverviewState.Empty with
        {
            PendingDownload = receipt,
            PendingDownloadVersion = version,
            PendingExport = exportReceipt,
            PendingExportVersion = exportVersion,
            PendingPrint = printReceipt,
            PendingPrintVersion = printVersion
        };
    }

    private static WorkspaceDownloadReceipt CreateReceipt(
        string workspaceId,
        WorkspaceDocumentFormat format = WorkspaceDocumentFormat.NativeXml,
        string? fileName = null)
    {
        return new WorkspaceDownloadReceipt(
            Id: new CharacterWorkspaceId(workspaceId),
            Format: format,
            ContentBase64: Convert.ToBase64String(Encoding.UTF8.GetBytes("<character/>")),
            FileName: fileName ?? $"{workspaceId}.chum5",
            DocumentLength: 12,
            RulesetId: "sr5");
    }

    private static WorkspaceExportReceipt CreateExportReceipt(string workspaceId)
    {
        return new WorkspaceExportReceipt(
            Id: new CharacterWorkspaceId(workspaceId),
            Format: WorkspaceDocumentFormat.Json,
            ContentBase64: Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"summary\":true}")),
            FileName: $"{workspaceId}-export.json",
            DocumentLength: 16,
            RulesetId: "sr5");
    }

    private static WorkspacePrintReceipt CreatePrintReceipt(string workspaceId)
    {
        return new WorkspacePrintReceipt(
            Id: new CharacterWorkspaceId(workspaceId),
            ContentBase64: Convert.ToBase64String(Encoding.UTF8.GetBytes("<html><body>Runner</body></html>")),
            FileName: $"{workspaceId}-print.html",
            MimeType: "text/html",
            DocumentLength: 32,
            Title: "Runner",
            RulesetId: "sr5");
    }

    private static void RegisterDesktopShellServices(
        BunitContext context,
        ICharacterOverviewPresenter presenter,
        IShellPresenter shellPresenter)
    {
        context.Services.AddSingleton(presenter);
        context.Services.AddSingleton(shellPresenter);
        context.Services.AddSingleton<ICommandAvailabilityEvaluator, DefaultCommandAvailabilityEvaluator>();
        context.Services.AddSingleton<IWorkbenchCoachApiClient>(FakeWorkbenchCoachApiClient.CreateDefault());
        context.Services.AddSingleton<IRulesetPlugin, Sr5RulesetPlugin>();
        context.Services.AddSingleton<IRulesetPluginRegistry, RulesetPluginRegistry>();
        context.Services.AddSingleton<IRulesetShellCatalogResolver, RulesetShellCatalogResolverService>();
        context.Services.AddSingleton<IShellSurfaceResolver, ShellSurfaceResolver>();
    }

    private sealed class TrackingShellPresenter : IShellPresenter
    {
        public TrackingShellPresenter(ShellState state)
        {
            State = state;
        }

        public ShellState State { get; private set; }

        public event EventHandler? StateChanged;

        public Task InitializeAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ExecuteCommandAsync(string commandId, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task SelectTabAsync(string tabId, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ToggleMenuAsync(string menuId, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task SetPreferredRulesetAsync(string rulesetId, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task SyncWorkspaceContextAsync(CharacterWorkspaceId? activeWorkspaceId, CancellationToken ct)
        {
            State = State with { ActiveWorkspaceId = activeWorkspaceId };
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }
}
