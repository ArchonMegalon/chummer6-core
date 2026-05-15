using Chummer.Contracts.Content;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;

namespace Chummer.Rulesets.Sr6;

public class Sr6RulesetPlugin : IRulesetPlugin
{
    public Sr6RulesetPlugin()
    {
        Capabilities = new Sr6DeterministicRulesetCapabilityHost();
        Rules = new RulesetRuleHostCapabilityAdapter(Capabilities);
        Scripts = new RulesetScriptHostCapabilityAdapter(Capabilities);
    }

    public RulesetId Id { get; } = new(RulesetDefaults.Sr6);

    public string DisplayName => "Shadowrun 6";

    public IRulesetSerializer Serializer { get; } = new Sr6RulesetSerializer();

    public IRulesetShellDefinitionProvider ShellDefinitions { get; } = new Sr6RulesetShellDefinitionProvider();

    public IRulesetCatalogProvider Catalogs { get; } = new Sr6RulesetCatalogProvider();

    public IRulesetCapabilityDescriptorProvider CapabilityDescriptors { get; } = new Sr6RulesetCapabilityDescriptorProvider();

    public IRulesetCapabilityHost Capabilities { get; }

    public IRulesetRuleHost Rules { get; }

    public IRulesetScriptHost Scripts { get; }
}

public class Sr6RulesetSerializer : IRulesetSerializer
{
    public RulesetId RulesetId { get; } = new(RulesetDefaults.Sr6);

    public int SchemaVersion => Sr6WorkspaceCodec.SchemaVersion;

    public WorkspacePayloadEnvelope Wrap(string payloadKind, string payload)
    {
        if (string.IsNullOrWhiteSpace(payloadKind))
        {
            throw new ArgumentException("Payload kind is required.", nameof(payloadKind));
        }

        return new WorkspacePayloadEnvelope(
            RulesetId: RulesetDefaults.Sr6,
            SchemaVersion: SchemaVersion,
            PayloadKind: payloadKind.Trim(),
            Payload: payload ?? string.Empty);
    }
}

public class Sr6RulesetShellDefinitionProvider : IRulesetShellDefinitionProvider
{
    public IReadOnlyList<AppCommandDefinition> GetCommands()
    {
        return Sr6AppCommandCatalog.All;
    }

    public IReadOnlyList<NavigationTabDefinition> GetNavigationTabs()
    {
        return Sr6NavigationTabCatalog.All;
    }
}

public class Sr6RulesetCatalogProvider : IRulesetCatalogProvider
{
    public IReadOnlyList<WorkflowDefinition> GetWorkflowDefinitions()
    {
        return Sr6WorkflowCatalog.Definitions;
    }

    public IReadOnlyList<WorkflowSurfaceDefinition> GetWorkflowSurfaces()
    {
        return Sr6WorkflowCatalog.Surfaces;
    }

    public IReadOnlyList<WorkspaceSurfaceActionDefinition> GetWorkspaceActions()
    {
        return Sr6WorkspaceSurfaceActionCatalog.All;
    }
}

internal static class Sr6WorkflowCatalog
{
    public static readonly IReadOnlyList<WorkflowDefinition> Definitions =
    [
        new(WorkflowDefinitionIds.LibraryShell, "Library Shell", ["sr6.shell.menu", "sr6.shell.toolbar"], false),
        new(WorkflowDefinitionIds.CareerWorkbench, "Career Workbench", ["sr6.career.section"], true),
        new(WorkflowDefinitionIds.SelectionDialog, "Selection Dialog", ["sr6.selection.dialog"], false),
        new(WorkflowDefinitionIds.DiceTool, "Dice Tool", ["sr6.tool.dice"], false),
        new(WorkflowDefinitionIds.SessionDashboard, "Session Dashboard", ["sr6.session.summary"], true, true)
    ];

    public static readonly IReadOnlyList<WorkflowSurfaceDefinition> Surfaces =
    [
        new("sr6.shell.menu", WorkflowDefinitionIds.LibraryShell, WorkflowSurfaceKinds.ShellRegion, ShellRegionIds.MenuBar, WorkflowLayoutTokens.ShellFrame, ["file", "edit", "tools"]),
        new("sr6.shell.toolbar", WorkflowDefinitionIds.LibraryShell, WorkflowSurfaceKinds.ShellRegion, ShellRegionIds.ToolStrip, WorkflowLayoutTokens.ShellFrame, ["new_character", "open_character", "save_character"]),
        new("sr6.career.section", WorkflowDefinitionIds.CareerWorkbench, WorkflowSurfaceKinds.Workbench, ShellRegionIds.SectionPane, WorkflowLayoutTokens.CareerWorkbench, ["tab-create.intake", "tab-info.summary", "tab-info.profile", "tab-skills.skills"]),
        new("sr6.selection.dialog", WorkflowDefinitionIds.SelectionDialog, WorkflowSurfaceKinds.Dialog, ShellRegionIds.DialogHost, WorkflowLayoutTokens.SelectionDialog, ["tab-gear.inventory"]),
        new("sr6.tool.dice", WorkflowDefinitionIds.DiceTool, WorkflowSurfaceKinds.Tool, ShellRegionIds.DialogHost, WorkflowLayoutTokens.ToolPanel, ["dice_roller"]),
        new("sr6.session.summary", WorkflowDefinitionIds.SessionDashboard, WorkflowSurfaceKinds.Dashboard, ShellRegionIds.SummaryHeader, WorkflowLayoutTokens.SessionDashboard, ["tab-info.summary", "tab-info.validate"])
    ];
}

public class Sr6RulesetCapabilityDescriptorProvider : IRulesetCapabilityDescriptorProvider
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
            CapabilityId: RulePackCapabilityIds.DeriveInitiative,
            InvocationKind: RulesetCapabilityInvocationKinds.Rule,
            Title: "Initiative Evaluation",
            Explainable: true,
            SessionSafe: true,
            DefaultGasBudget: DefaultBudget,
            MaximumGasBudget: MaximumBudget,
            TitleKey: "ruleset.capability.derive.initiative.title"),
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

public class Sr6DeterministicRulesetCapabilityHost : IRulesetCapabilityHost
{
    public ValueTask<RulesetCapabilityInvocationResult> InvokeAsync(RulesetCapabilityInvocationRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.Equals(request.InvocationKind, RulesetCapabilityInvocationKinds.Rule, StringComparison.Ordinal)
            && string.Equals(request.CapabilityId, RulePackCapabilityIds.DeriveStat, StringComparison.Ordinal))
        {
            return ValueTask.FromResult(EvaluateDeriveStat(request));
        }

        if (string.Equals(request.InvocationKind, RulesetCapabilityInvocationKinds.Rule, StringComparison.Ordinal)
            && string.Equals(request.CapabilityId, RulePackCapabilityIds.DeriveInitiative, StringComparison.Ordinal))
        {
            return ValueTask.FromResult(EvaluateDeriveInitiative(request));
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
                    "sr6.capability.unsupported",
                    $"SR6 capability '{request.CapabilityId}' is not mapped by the deterministic host.",
                    RulesetCapabilityDiagnosticSeverities.Error,
                    MessageKey: "sr6.capability.unsupported",
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
                    "sr6.rule.executed",
                    "SR6 deterministic derive-stat capability executed.",
                    RulesetCapabilityDiagnosticSeverities.Info,
                    MessageKey: "sr6.rule.executed")
            ],
            Explain: CreateExplainTrace(request.CapabilityId, output, "sr6.host/derive.stat"));
    }

    private static RulesetCapabilityInvocationResult EvaluateDeriveInitiative(RulesetCapabilityInvocationRequest request)
    {
        long reaction = GetIntegerArgument(request.Arguments, "reaction") ?? 0;
        long intuition = GetIntegerArgument(request.Arguments, "intuition") ?? 0;
        long initiativeDice = GetIntegerArgument(request.Arguments, "initiativeDice") ?? 0;
        long finalValue = reaction + intuition + initiativeDice;

        RulesetCapabilityValue output = new(
            RulesetCapabilityValueKinds.Object,
            Properties: new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal)
            {
                ["capability"] = RulesetCapabilityBridge.FromObject(request.CapabilityId),
                ["value"] = RulesetCapabilityBridge.FromObject(finalValue),
                ["reaction"] = RulesetCapabilityBridge.FromObject(reaction),
                ["intuition"] = RulesetCapabilityBridge.FromObject(intuition),
                ["initiativeDice"] = RulesetCapabilityBridge.FromObject(initiativeDice),
                ["formulaKey"] = RulesetCapabilityBridge.FromObject("sr6.initiative.reaction_plus_intuition_plus_dice")
            });

        return new RulesetCapabilityInvocationResult(
            Success: true,
            Output: output,
            Diagnostics:
            [
                new(
                    "sr6.initiative.executed",
                    "SR6 deterministic derive-initiative capability executed.",
                    RulesetCapabilityDiagnosticSeverities.Info,
                    MessageKey: "sr6.initiative.executed")
            ],
            Explain: CreateExplainTrace(
                capabilityId: request.CapabilityId,
                output: output,
                providerId: "sr6.host/derive.initiative",
                targetKey: "initiative.total"));
    }

    private static RulesetCapabilityInvocationResult EvaluateSessionQuickActions(RulesetCapabilityInvocationRequest request)
    {
        string[] quickActions = ["anticipate", "dodge", "full-defense"];
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
                    "sr6.script.executed",
                    "SR6 deterministic session quick-actions capability executed.",
                    RulesetCapabilityDiagnosticSeverities.Info,
                    MessageKey: "sr6.script.executed")
            ],
            Explain: CreateExplainTrace(request.CapabilityId, output, "sr6.host/session.quick-actions"));
    }

    private static RulesetExplainTrace CreateExplainTrace(
        string capabilityId,
        RulesetCapabilityValue output,
        string providerId,
        string? targetKey = null)
    {
        RulesetGasUsage gas = new(
            ProviderInstructionsConsumed: 1,
            RequestInstructionsConsumed: 1,
            PeakMemoryBytes: 256);

        return new RulesetExplainTrace(
            TargetKey: targetKey ?? capabilityId,
            FinalValue: output,
            SummaryKey: "ruleset.explain.summary.sr6.host.execution",
            SummaryParameters:
            [
                new RulesetExplainParameter("capabilityId", RulesetCapabilityBridge.FromObject(capabilityId))
            ],
            Providers:
            [
                new RulesetProviderTrace(
                    ProviderId: providerId,
                    CapabilityId: capabilityId,
                    PackId: "official.sr6.core",
                    Success: true,
                    Steps:
                    [
                        new RulesetTraceStep(
                            ProviderId: providerId,
                            CapabilityId: capabilityId,
                            PackId: "official.sr6.core",
                            ExplanationKey: "ruleset.explain.step.sr6.host.execution",
                            ExplanationParameters:
                            [
                                new RulesetExplainParameter("capabilityId", RulesetCapabilityBridge.FromObject(capabilityId))
                            ],
                            Category: "deterministic-host")
                    ],
                    GasUsage: gas)
            ],
            AggregateGasUsage: gas,
            ProfileId: "official.sr6.core");
    }

    private static long? GetIntegerArgument(IReadOnlyList<RulesetCapabilityArgument> arguments, string name)
    {
        RulesetCapabilityArgument? argument = arguments.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal));
        if (argument is null)
        {
            return null;
        }

        return argument.Value.IntegerValue;
    }
}
