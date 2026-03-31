using Chummer.Contracts.Content;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;

namespace Chummer.Rulesets.Sr4;

public class Sr4RulesetPlugin : IRulesetPlugin
{
    public Sr4RulesetPlugin()
    {
        Capabilities = new Sr4DeterministicRulesetCapabilityHost();
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
        new("sr4.career.section", WorkflowDefinitionIds.CareerWorkbench, WorkflowSurfaceKinds.Workbench, ShellRegionIds.SectionPane, WorkflowLayoutTokens.CareerWorkbench, ["tab-create.intake", "tab-info.summary", "tab-info.profile", "tab-skills.skills"]),
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

public class Sr4DeterministicRulesetCapabilityHost : IRulesetCapabilityHost
{
    public ValueTask<RulesetCapabilityInvocationResult> InvokeAsync(RulesetCapabilityInvocationRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.Equals(request.InvocationKind, RulesetCapabilityInvocationKinds.Rule, StringComparison.Ordinal)
            && string.Equals(request.CapabilityId, RulePackCapabilityIds.DeriveStat, StringComparison.Ordinal))
        {
            return ValueTask.FromResult(EvaluateDeriveStat(request));
        }

        if (string.Equals(request.InvocationKind, RulesetCapabilityInvocationKinds.Script, StringComparison.Ordinal)
            && string.Equals(request.CapabilityId, RulePackCapabilityIds.SessionQuickActions, StringComparison.Ordinal))
        {
            return ValueTask.FromResult(EvaluateSessionQuickActions(request));
        }

        return ValueTask.FromResult(new RulesetCapabilityInvocationResult(
            Success: false,
            Output: null,
            Diagnostics:
            [
                new(
                    "sr4.capability.unsupported",
                    $"SR4 capability '{request.CapabilityId}' is not mapped by the deterministic host.",
                    RulesetCapabilityDiagnosticSeverities.Error,
                    MessageKey: "sr4.capability.unsupported",
                    MessageParameters:
                    [
                        new RulesetExplainParameter("capabilityId", RulesetCapabilityBridge.FromObject(request.CapabilityId))
                    ])
            ]));
    }

    private static RulesetCapabilityInvocationResult EvaluateDeriveStat(RulesetCapabilityInvocationRequest request)
    {
        long baseValue = GetIntegerArgument(request.Arguments, "baseValue")
                         ?? GetIntegerArgument(request.Arguments, "base")
                         ?? 0;
        long modifier = GetIntegerArgument(request.Arguments, "modifier") ?? 0;
        long value = baseValue + modifier;

        RulesetCapabilityValue output = new(
            RulesetCapabilityValueKinds.Object,
            Properties: new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal)
            {
                ["capability"] = RulesetCapabilityBridge.FromObject(request.CapabilityId),
                ["value"] = RulesetCapabilityBridge.FromObject(value),
                ["baseValue"] = RulesetCapabilityBridge.FromObject(baseValue),
                ["modifier"] = RulesetCapabilityBridge.FromObject(modifier)
            });

        return new RulesetCapabilityInvocationResult(
            Success: true,
            Output: output,
            Diagnostics:
            [
                new(
                    "sr4.rule.executed",
                    "SR4 deterministic derive-stat capability executed.",
                    RulesetCapabilityDiagnosticSeverities.Info,
                    MessageKey: "sr4.rule.executed")
            ],
            Explain: CreateExplainTrace(request.CapabilityId, output, "sr4.host/derive.stat"));
    }

    private static RulesetCapabilityInvocationResult EvaluateSessionQuickActions(RulesetCapabilityInvocationRequest request)
    {
        string[] quickActions = ["delay-action", "interrupt-action", "full-defense"];
        RulesetCapabilityValue output = new(
            RulesetCapabilityValueKinds.Object,
            Properties: new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal)
            {
                ["capability"] = RulesetCapabilityBridge.FromObject(request.CapabilityId),
                ["actions"] = RulesetCapabilityBridge.FromObject(quickActions)
            });

        return new RulesetCapabilityInvocationResult(
            Success: true,
            Output: output,
            Diagnostics:
            [
                new(
                    "sr4.script.executed",
                    "SR4 deterministic session quick-actions capability executed.",
                    RulesetCapabilityDiagnosticSeverities.Info,
                    MessageKey: "sr4.script.executed")
            ],
            Explain: CreateExplainTrace(request.CapabilityId, output, "sr4.host/session.quick-actions"));
    }

    private static RulesetExplainTrace CreateExplainTrace(string capabilityId, RulesetCapabilityValue output, string providerId)
    {
        RulesetGasUsage gas = new(
            ProviderInstructionsConsumed: 1,
            RequestInstructionsConsumed: 1,
            PeakMemoryBytes: 256);

        return new RulesetExplainTrace(
            TargetKey: capabilityId,
            FinalValue: output,
            SummaryKey: "ruleset.explain.summary.sr4.host.execution",
            SummaryParameters:
            [
                new RulesetExplainParameter("capabilityId", RulesetCapabilityBridge.FromObject(capabilityId))
            ],
            Providers:
            [
                new RulesetProviderTrace(
                    ProviderId: providerId,
                    CapabilityId: capabilityId,
                    PackId: "official.sr4.core",
                    Success: true,
                    Steps:
                    [
                        new RulesetTraceStep(
                            ProviderId: providerId,
                            CapabilityId: capabilityId,
                            PackId: "official.sr4.core",
                            ExplanationKey: "ruleset.explain.step.sr4.host.execution",
                            ExplanationParameters:
                            [
                                new RulesetExplainParameter("capabilityId", RulesetCapabilityBridge.FromObject(capabilityId))
                            ],
                            Category: "deterministic-host")
                    ],
                    GasUsage: gas)
            ],
            AggregateGasUsage: gas,
            ProfileId: "official.sr4.core");
    }

    private static long? GetIntegerArgument(IReadOnlyList<RulesetCapabilityArgument> arguments, string name)
    {
        RulesetCapabilityArgument? argument = arguments.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        if (argument is null)
        {
            return null;
        }

        return argument.Value.Kind switch
        {
            RulesetCapabilityValueKinds.Integer => argument.Value.IntegerValue,
            RulesetCapabilityValueKinds.Number => argument.Value.NumberValue.HasValue ? Convert.ToInt64(argument.Value.NumberValue.Value) : null,
            RulesetCapabilityValueKinds.Decimal => argument.Value.DecimalValue.HasValue ? Convert.ToInt64(argument.Value.DecimalValue.Value) : null,
            RulesetCapabilityValueKinds.String when long.TryParse(argument.Value.StringValue, out long parsed) => parsed,
            _ => null
        };
    }
}
