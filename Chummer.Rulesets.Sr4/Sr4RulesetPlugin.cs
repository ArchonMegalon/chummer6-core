using Chummer.Contracts.Content;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;

namespace Chummer.Rulesets.Sr4;

public class Sr4RulesetPlugin : IRulesetPlugin
{
    public Sr4RulesetPlugin()
    {
        Capabilities = new Sr4NoOpRulesetCapabilityHost();
        Rules = new RulesetRuleHostCapabilityAdapter(Capabilities);
        Scripts = new RulesetScriptHostCapabilityAdapter(Capabilities);
    }

    public RulesetId Id { get; } = new(RulesetDefaults.Sr4);

    public string DisplayName => "Shadowrun 4";

    public IRulesetSerializer Serializer { get; } = new Sr4RulesetSerializer();

    public IRulesetShellDefinitionProvider ShellDefinitions { get; } = new Sr4RulesetShellDefinitionProvider();

    public IRulesetCatalogProvider Catalogs { get; } = new Sr4RulesetCatalogProvider();

    public IRulesetCapabilityDescriptorProvider CapabilityDescriptors { get; } = new Sr4RulesetCapabilityDescriptorProvider();

    public IRulesetCapabilityHost Capabilities { get; }

    public IRulesetRuleHost Rules { get; }

    public IRulesetScriptHost Scripts { get; }
}

public class Sr4RulesetSerializer : IRulesetSerializer
{
    public RulesetId RulesetId { get; } = new(RulesetDefaults.Sr4);

    public int SchemaVersion => Sr4WorkspaceCodec.SchemaVersion;

    public WorkspacePayloadEnvelope Wrap(string payloadKind, string payload)
    {
        if (string.IsNullOrWhiteSpace(payloadKind))
        {
            throw new ArgumentException("Payload kind is required.", nameof(payloadKind));
        }

        return new WorkspacePayloadEnvelope(
            RulesetId: RulesetDefaults.Sr4,
            SchemaVersion: SchemaVersion,
            PayloadKind: payloadKind.Trim(),
            Payload: payload ?? string.Empty);
    }
}

public class Sr4RulesetShellDefinitionProvider : IRulesetShellDefinitionProvider
{
    public IReadOnlyList<AppCommandDefinition> GetCommands()
    {
        return Sr4AppCommandCatalog.All;
    }

    public IReadOnlyList<NavigationTabDefinition> GetNavigationTabs()
    {
        return Sr4NavigationTabCatalog.All;
    }
}

public class Sr4RulesetCatalogProvider : IRulesetCatalogProvider
{
    public IReadOnlyList<WorkflowDefinition> GetWorkflowDefinitions()
    {
        return Sr4WorkflowCatalog.Definitions;
    }

    public IReadOnlyList<WorkflowSurfaceDefinition> GetWorkflowSurfaces()
    {
        return Sr4WorkflowCatalog.Surfaces;
    }

    public IReadOnlyList<WorkspaceSurfaceActionDefinition> GetWorkspaceActions()
    {
        return Sr4WorkspaceSurfaceActionCatalog.All;
    }
}

internal static class Sr4WorkflowCatalog
{
    public static readonly IReadOnlyList<WorkflowDefinition> Definitions =
    [
        new(WorkflowDefinitionIds.LibraryShell, "Library Shell", ["sr4.shell.menu", "sr4.shell.toolbar"], false),
        new(WorkflowDefinitionIds.CareerWorkbench, "Career Workbench", ["sr4.career.section"], true),
        new(WorkflowDefinitionIds.SelectionDialog, "Selection Dialog", ["sr4.selection.dialog"], false),
        new(WorkflowDefinitionIds.DiceTool, "Dice Tool", ["sr4.tool.dice"], false),
        new(WorkflowDefinitionIds.SessionDashboard, "Session Dashboard", ["sr4.session.summary"], true, true)
    ];

    public static readonly IReadOnlyList<WorkflowSurfaceDefinition> Surfaces =
    [
        new("sr4.shell.menu", WorkflowDefinitionIds.LibraryShell, WorkflowSurfaceKinds.ShellRegion, ShellRegionIds.MenuBar, WorkflowLayoutTokens.ShellFrame, ["file", "edit", "tools"]),
        new("sr4.shell.toolbar", WorkflowDefinitionIds.LibraryShell, WorkflowSurfaceKinds.ShellRegion, ShellRegionIds.ToolStrip, WorkflowLayoutTokens.ShellFrame, ["new_character", "open_character", "save_character"]),
        new("sr4.career.section", WorkflowDefinitionIds.CareerWorkbench, WorkflowSurfaceKinds.Workbench, ShellRegionIds.SectionPane, WorkflowLayoutTokens.CareerWorkbench, ["tab-info.summary", "tab-info.profile", "tab-skills.skills"]),
        new("sr4.selection.dialog", WorkflowDefinitionIds.SelectionDialog, WorkflowSurfaceKinds.Dialog, ShellRegionIds.DialogHost, WorkflowLayoutTokens.SelectionDialog, ["tab-gear.inventory"]),
        new("sr4.tool.dice", WorkflowDefinitionIds.DiceTool, WorkflowSurfaceKinds.Tool, ShellRegionIds.DialogHost, WorkflowLayoutTokens.ToolPanel, ["dice_roller"]),
        new("sr4.session.summary", WorkflowDefinitionIds.SessionDashboard, WorkflowSurfaceKinds.Dashboard, ShellRegionIds.SummaryHeader, WorkflowLayoutTokens.SessionDashboard, ["tab-info.summary", "tab-info.validate"])
    ];
}

public class Sr4RulesetCapabilityDescriptorProvider : IRulesetCapabilityDescriptorProvider
{
    private static readonly RulesetGasBudget DefaultBudget = new(
        ProviderInstructionLimit: 1_000,
        RequestInstructionLimit: 5_000,
        MemoryBytesLimit: 1_048_576,
        WallClockLimit: TimeSpan.FromSeconds(1));

    private static readonly RulesetGasBudget MaximumBudget = new(
        ProviderInstructionLimit: 5_000,
        RequestInstructionLimit: 20_000,
        MemoryBytesLimit: 4_194_304,
        WallClockLimit: TimeSpan.FromSeconds(2));

    private static readonly IReadOnlyList<RulesetCapabilityDescriptor> Descriptors =
    [
        new(
            CapabilityId: RulePackCapabilityIds.DeriveStat,
            InvocationKind: RulesetCapabilityInvocationKinds.Rule,
            Title: "Derived Stat Evaluation",
            Explainable: true,
            SessionSafe: false,
            DefaultGasBudget: DefaultBudget,
            MaximumGasBudget: MaximumBudget,
            TitleKey: "ruleset.capability.derive.stat.title"),
        new(
            CapabilityId: RulePackCapabilityIds.SessionQuickActions,
            InvocationKind: RulesetCapabilityInvocationKinds.Script,
            Title: "Session Quick Actions",
            Explainable: true,
            SessionSafe: true,
            DefaultGasBudget: DefaultBudget,
            MaximumGasBudget: MaximumBudget,
            TitleKey: "ruleset.capability.session.quick-actions.title")
    ];

    public IReadOnlyList<RulesetCapabilityDescriptor> GetCapabilityDescriptors() => Descriptors;
}

public class Sr4NoOpRulesetCapabilityHost : IRulesetCapabilityHost
{
    private const string RuleErrorMessage = "SR4 rules engine is not implemented; this ruleset remains experimental.";

    public ValueTask<RulesetCapabilityInvocationResult> InvokeAsync(RulesetCapabilityInvocationRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<RulesetCapabilityDiagnostic> diagnostics = string.Equals(request.InvocationKind, RulesetCapabilityInvocationKinds.Script, StringComparison.Ordinal)
            ?
            [
                new(
                    "sr4.script.experimental",
                    $"SR4 script host is not implemented; script '{request.CapabilityId}' cannot be executed because the ruleset remains experimental.",
                    RulesetCapabilityDiagnosticSeverities.Error,
                    MessageKey: "sr4.script.experimental",
                    MessageParameters:
                    [
                        new RulesetExplainParameter("capabilityId", RulesetCapabilityBridge.FromObject(request.CapabilityId))
                    ])
            ]
            :
            [
                new(
                    "sr4.rule.experimental",
                    RuleErrorMessage,
                    RulesetCapabilityDiagnosticSeverities.Error,
                    MessageKey: "sr4.rule.experimental"),
                new(
                    "sr4.rule.unavailable",
                    $"Rule '{request.CapabilityId}' cannot be evaluated until SR4 rule providers are implemented.",
                    RulesetCapabilityDiagnosticSeverities.Error,
                    MessageKey: "sr4.rule.unavailable",
                    MessageParameters:
                    [
                        new RulesetExplainParameter("capabilityId", RulesetCapabilityBridge.FromObject(request.CapabilityId))
                    ])
            ];

        return ValueTask.FromResult(new RulesetCapabilityInvocationResult(
            Success: false,
            Output: null,
            Diagnostics: diagnostics));
    }
}
