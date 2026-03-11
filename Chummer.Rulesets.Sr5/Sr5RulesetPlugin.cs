using Chummer.Contracts.Content;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;

namespace Chummer.Rulesets.Sr5;

public class Sr5RulesetPlugin : IRulesetPlugin
{
    public Sr5RulesetPlugin()
    {
        Capabilities = new Sr5NoOpRulesetCapabilityHost();
        Rules = new RulesetRuleHostCapabilityAdapter(Capabilities);
        Scripts = new RulesetScriptHostCapabilityAdapter(Capabilities);
    }

    public RulesetId Id { get; } = new(RulesetDefaults.Sr5);

    public string DisplayName => "Shadowrun 5";

    public IRulesetSerializer Serializer { get; } = new Sr5RulesetSerializer();

    public IRulesetShellDefinitionProvider ShellDefinitions { get; } = new Sr5RulesetShellDefinitionProvider();

    public IRulesetCatalogProvider Catalogs { get; } = new Sr5RulesetCatalogProvider();

    public IRulesetCapabilityDescriptorProvider CapabilityDescriptors { get; } = new Sr5RulesetCapabilityDescriptorProvider();

    public IRulesetCapabilityHost Capabilities { get; }

    public IRulesetRuleHost Rules { get; }

    public IRulesetScriptHost Scripts { get; }
}

public class Sr5RulesetSerializer : IRulesetSerializer
{
    public RulesetId RulesetId { get; } = new(RulesetDefaults.Sr5);

    public int SchemaVersion => 1;

    public WorkspacePayloadEnvelope Wrap(string payloadKind, string payload)
    {
        if (string.IsNullOrWhiteSpace(payloadKind))
        {
            throw new ArgumentException("Payload kind is required.", nameof(payloadKind));
        }

        return new WorkspacePayloadEnvelope(
            RulesetId: RulesetDefaults.Sr5,
            SchemaVersion: SchemaVersion,
            PayloadKind: payloadKind.Trim(),
            Payload: payload ?? string.Empty);
    }
}

public class Sr5RulesetShellDefinitionProvider : IRulesetShellDefinitionProvider
{
    public IReadOnlyList<AppCommandDefinition> GetCommands()
    {
        return Sr5AppCommandCatalog.All;
    }

    public IReadOnlyList<NavigationTabDefinition> GetNavigationTabs()
    {
        return Sr5NavigationTabCatalog.All;
    }
}

public class Sr5RulesetCatalogProvider : IRulesetCatalogProvider
{
    public IReadOnlyList<WorkflowDefinition> GetWorkflowDefinitions()
    {
        return Sr5WorkflowCatalog.Definitions;
    }

    public IReadOnlyList<WorkflowSurfaceDefinition> GetWorkflowSurfaces()
    {
        return Sr5WorkflowCatalog.Surfaces;
    }

    public IReadOnlyList<WorkspaceSurfaceActionDefinition> GetWorkspaceActions()
    {
        return Sr5WorkspaceSurfaceActionCatalog.All;
    }
}

internal static class Sr5WorkflowCatalog
{
    public static readonly IReadOnlyList<WorkflowDefinition> Definitions =
    [
        new(WorkflowDefinitionIds.LibraryShell, "Library Shell", ["sr5.shell.menu", "sr5.shell.toolbar"], false),
        new(WorkflowDefinitionIds.CareerWorkbench, "Career Workbench", ["sr5.career.section"], true),
        new(WorkflowDefinitionIds.SelectionDialog, "Selection Dialog", ["sr5.selection.dialog"], false),
        new(WorkflowDefinitionIds.DiceTool, "Dice Tool", ["sr5.tool.dice"], false),
        new(WorkflowDefinitionIds.SessionDashboard, "Session Dashboard", ["sr5.session.summary"], true, true)
    ];

    public static readonly IReadOnlyList<WorkflowSurfaceDefinition> Surfaces =
    [
        new("sr5.shell.menu", WorkflowDefinitionIds.LibraryShell, WorkflowSurfaceKinds.ShellRegion, ShellRegionIds.MenuBar, WorkflowLayoutTokens.ShellFrame, ["file", "edit", "tools"]),
        new("sr5.shell.toolbar", WorkflowDefinitionIds.LibraryShell, WorkflowSurfaceKinds.ShellRegion, ShellRegionIds.ToolStrip, WorkflowLayoutTokens.ShellFrame, ["new_character", "open_character", "save_character"]),
        new("sr5.career.section", WorkflowDefinitionIds.CareerWorkbench, WorkflowSurfaceKinds.Workbench, ShellRegionIds.SectionPane, WorkflowLayoutTokens.CareerWorkbench, ["tab-info.summary", "tab-info.profile", "tab-skills.skills"]),
        new("sr5.selection.dialog", WorkflowDefinitionIds.SelectionDialog, WorkflowSurfaceKinds.Dialog, ShellRegionIds.DialogHost, WorkflowLayoutTokens.SelectionDialog, ["tab-gear.inventory"]),
        new("sr5.tool.dice", WorkflowDefinitionIds.DiceTool, WorkflowSurfaceKinds.Tool, ShellRegionIds.DialogHost, WorkflowLayoutTokens.ToolPanel, ["dice_roller"]),
        new("sr5.session.summary", WorkflowDefinitionIds.SessionDashboard, WorkflowSurfaceKinds.Dashboard, ShellRegionIds.SummaryHeader, WorkflowLayoutTokens.SessionDashboard, ["tab-info.summary", "tab-info.validate"])
    ];
}

public class Sr5RulesetCapabilityDescriptorProvider : IRulesetCapabilityDescriptorProvider
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

public class Sr5NoOpRulesetCapabilityHost : IRulesetCapabilityHost
{
    private static readonly IReadOnlyList<RulesetCapabilityDiagnostic> RuleDiagnostics =
    [
        new(
            "sr5.noop.rule",
            "Rule host not configured; no-op evaluation applied.",
            MessageKey: "sr5.noop.rule")
    ];

    public ValueTask<RulesetCapabilityInvocationResult> InvokeAsync(RulesetCapabilityInvocationRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        IReadOnlyDictionary<string, RulesetCapabilityValue> outputProperties = string.Equals(request.InvocationKind, RulesetCapabilityInvocationKinds.Script, StringComparison.Ordinal)
            ? new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal)
            {
                ["scriptId"] = RulesetCapabilityBridge.FromObject(request.CapabilityId),
                ["mode"] = RulesetCapabilityBridge.FromObject("noop"),
                ["inputCount"] = RulesetCapabilityBridge.FromObject(request.Arguments.Count)
            }
            : request.Arguments.ToDictionary(
                static argument => argument.Name,
                static argument => argument.Value,
                StringComparer.Ordinal);

        IReadOnlyList<RulesetCapabilityDiagnostic> diagnostics = string.Equals(request.InvocationKind, RulesetCapabilityInvocationKinds.Script, StringComparison.Ordinal)
            ?
            [
                new(
                    "sr5.noop.script",
                    "Script host not configured; no-op execution applied.",
                    MessageKey: "sr5.noop.script")
            ]
            : RuleDiagnostics;

        return ValueTask.FromResult(new RulesetCapabilityInvocationResult(
            Success: true,
            Output: new RulesetCapabilityValue(RulesetCapabilityValueKinds.Object, Properties: outputProperties),
            Diagnostics: diagnostics));
    }
}
