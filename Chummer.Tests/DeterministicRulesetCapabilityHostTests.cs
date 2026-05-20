#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Contracts.Content;
using Chummer.Contracts.Rulesets;
using Chummer.Rulesets.Sr4;
using Chummer.Rulesets.Sr5;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class DeterministicRulesetCapabilityHostTests
{
    [TestMethod]
    public async Task Sr4_host_derives_stat_and_emits_explain_trace()
    {
        Sr4DeterministicRulesetCapabilityHost host = new();

        RulesetCapabilityInvocationResult result = await host.InvokeAsync(
            CreateRequest(
                RulePackCapabilityIds.DeriveStat,
                RulesetCapabilityInvocationKinds.Rule,
                new Dictionary<string, object?>
                {
                    ["baseValue"] = 7,
                    ["modifier"] = 2
                }),
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("sr4.rule.executed", result.Diagnostics[0].Code);
        Assert.IsNotNull(result.Output?.Properties);
        Assert.AreEqual(9L, result.Output.Properties["value"].IntegerValue);
        Assert.AreEqual("sr4.host/derive.stat", result.Explain?.Providers[0].ProviderId);
        Assert.AreEqual(RulePackCapabilityIds.DeriveStat, result.Explain?.TargetKey);
    }

    [TestMethod]
    public async Task Sr4_host_derives_initiative_and_uses_targeted_formula()
    {
        Sr4DeterministicRulesetCapabilityHost host = new();

        RulesetCapabilityInvocationResult result = await host.InvokeAsync(
            CreateRequest(
                RulePackCapabilityIds.DeriveInitiative,
                RulesetCapabilityInvocationKinds.Rule,
                new Dictionary<string, object?>
                {
                    ["reaction"] = 5,
                    ["intuition"] = 4,
                    ["initiativeDice"] = 2
                }),
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(11L, result.Output?.Properties?["value"].IntegerValue);
        Assert.AreEqual("sr4.initiative.reaction_plus_intuition_plus_dice", result.Output?.Properties?["formulaKey"].StringValue);
        Assert.AreEqual("initiative.total", result.Explain?.TargetKey);
    }

    [TestMethod]
    public async Task Sr4_host_emits_supported_session_quick_actions()
    {
        Sr4DeterministicRulesetCapabilityHost host = new();

        RulesetCapabilityInvocationResult result = await host.InvokeAsync(
            CreateRequest(RulePackCapabilityIds.SessionQuickActions, RulesetCapabilityInvocationKinds.Script),
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(
            new[] { "delay-action", "interrupt-action", "full-defense" },
            result.Output?.Properties?["actions"].Items?.Select(static item => item.StringValue).ToArray());
    }

    [TestMethod]
    public async Task Sr4_host_returns_error_for_unsupported_capability()
    {
        Sr4DeterministicRulesetCapabilityHost host = new();

        RulesetCapabilityInvocationResult result = await host.InvokeAsync(
            CreateRequest("missing.capability", RulesetCapabilityInvocationKinds.Rule),
            CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("sr4.capability.unsupported", result.Diagnostics[0].Code);
        Assert.AreEqual(RulesetCapabilityDiagnosticSeverities.Error, result.Diagnostics[0].Severity);
    }

    [TestMethod]
    public async Task Sr6_host_derives_stat_and_emits_explain_trace()
    {
        Sr6DeterministicRulesetCapabilityHost host = new();

        RulesetCapabilityInvocationResult result = await host.InvokeAsync(
            CreateRequest(
                RulePackCapabilityIds.DeriveStat,
                RulesetCapabilityInvocationKinds.Rule,
                new Dictionary<string, object?>
                {
                    ["base"] = 8,
                    ["modifier"] = 1
                }),
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("sr6.rule.executed", result.Diagnostics[0].Code);
        Assert.AreEqual(9L, result.Output?.Properties?["value"].IntegerValue);
        Assert.AreEqual("sr6.host/derive.stat", result.Explain?.Providers[0].ProviderId);
    }

    [TestMethod]
    public async Task Sr6_host_derives_initiative_and_uses_targeted_formula()
    {
        Sr6DeterministicRulesetCapabilityHost host = new();

        RulesetCapabilityInvocationResult result = await host.InvokeAsync(
            CreateRequest(
                RulePackCapabilityIds.DeriveInitiative,
                RulesetCapabilityInvocationKinds.Rule,
                new Dictionary<string, object?>
                {
                    ["reaction"] = 6,
                    ["intuition"] = 5,
                    ["initiativeDice"] = 1
                }),
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(12L, result.Output?.Properties?["value"].IntegerValue);
        Assert.AreEqual("sr6.initiative.reaction_plus_intuition_plus_dice", result.Output?.Properties?["formulaKey"].StringValue);
        Assert.AreEqual("initiative.total", result.Explain?.TargetKey);
    }

    [TestMethod]
    public async Task Sr6_host_emits_supported_session_quick_actions()
    {
        Sr6DeterministicRulesetCapabilityHost host = new();

        RulesetCapabilityInvocationResult result = await host.InvokeAsync(
            CreateRequest(RulePackCapabilityIds.SessionQuickActions, RulesetCapabilityInvocationKinds.Script),
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(
            new[] { "anticipate", "dodge", "full-defense" },
            result.Output?.Properties?["actions"].Items?.Select(static item => item.StringValue).ToArray());
    }

    [TestMethod]
    public async Task Sr6_host_returns_error_for_unsupported_capability()
    {
        Sr6DeterministicRulesetCapabilityHost host = new();

        RulesetCapabilityInvocationResult result = await host.InvokeAsync(
            CreateRequest("missing.capability", RulesetCapabilityInvocationKinds.Script),
            CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("sr6.capability.unsupported", result.Diagnostics[0].Code);
        Assert.AreEqual(RulesetCapabilityDiagnosticSeverities.Error, result.Diagnostics[0].Severity);
    }

    [TestMethod]
    public async Task Sr5_host_derives_stat_and_emits_explain_trace()
    {
        Sr5DeterministicRulesetCapabilityHost host = new();

        RulesetCapabilityInvocationResult result = await host.InvokeAsync(
            CreateRequest(
                RulePackCapabilityIds.DeriveStat,
                RulesetCapabilityInvocationKinds.Rule,
                new Dictionary<string, object?>
                {
                    ["baseValue"] = 6,
                    ["modifier"] = 3
                }),
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("sr5.rule.executed", result.Diagnostics[0].Code);
        Assert.AreEqual(9L, result.Output?.Properties?["value"].IntegerValue);
        Assert.AreEqual("sr5.host/derive.stat", result.Explain?.Providers[0].ProviderId);
        Assert.AreEqual(RulePackCapabilityIds.DeriveStat, result.Explain?.TargetKey);
    }

    [TestMethod]
    public async Task Sr5_host_derives_initiative_and_uses_targeted_formula()
    {
        Sr5DeterministicRulesetCapabilityHost host = new();

        RulesetCapabilityInvocationResult result = await host.InvokeAsync(
            CreateRequest(
                RulePackCapabilityIds.DeriveInitiative,
                RulesetCapabilityInvocationKinds.Rule,
                new Dictionary<string, object?>
                {
                    ["reaction"] = 5,
                    ["intuition"] = 5,
                    ["initiativeDice"] = 2
                }),
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(12L, result.Output?.Properties?["value"].IntegerValue);
        Assert.AreEqual("sr5.initiative.reaction_plus_intuition_plus_dice", result.Output?.Properties?["formulaKey"].StringValue);
        Assert.AreEqual("initiative.total", result.Explain?.TargetKey);
    }

    [TestMethod]
    public async Task Sr5_host_emits_supported_session_quick_actions()
    {
        Sr5DeterministicRulesetCapabilityHost host = new();

        RulesetCapabilityInvocationResult result = await host.InvokeAsync(
            CreateRequest(RulePackCapabilityIds.SessionQuickActions, RulesetCapabilityInvocationKinds.Script),
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(
            new[] { "delay-action", "interrupt-action", "full-defense" },
            result.Output?.Properties?["actions"].Items?.Select(static item => item.StringValue).ToArray());
    }

    [TestMethod]
    public async Task Sr5_host_returns_error_for_unsupported_capability()
    {
        Sr5DeterministicRulesetCapabilityHost host = new();

        RulesetCapabilityInvocationResult result = await host.InvokeAsync(
            CreateRequest("missing.capability", RulesetCapabilityInvocationKinds.Rule),
            CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("sr5.capability.unsupported", result.Diagnostics[0].Code);
        Assert.AreEqual(RulesetCapabilityDiagnosticSeverities.Error, result.Diagnostics[0].Severity);
    }

    [TestMethod]
    public async Task Deterministic_hosts_respect_cancellation()
    {
        CancellationToken cancellationToken = new(canceled: true);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => new Sr4DeterministicRulesetCapabilityHost().InvokeAsync(
            CreateRequest(RulePackCapabilityIds.DeriveStat, RulesetCapabilityInvocationKinds.Rule),
            cancellationToken).AsTask());
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => new Sr5DeterministicRulesetCapabilityHost().InvokeAsync(
            CreateRequest(RulePackCapabilityIds.DeriveStat, RulesetCapabilityInvocationKinds.Rule),
            cancellationToken).AsTask());
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => new Sr6DeterministicRulesetCapabilityHost().InvokeAsync(
            CreateRequest(RulePackCapabilityIds.DeriveStat, RulesetCapabilityInvocationKinds.Rule),
            cancellationToken).AsTask());
    }

    private static RulesetCapabilityInvocationRequest CreateRequest(
        string capabilityId,
        string invocationKind,
        IReadOnlyDictionary<string, object?>? arguments = null)
        => new(
            CapabilityId: capabilityId,
            InvocationKind: invocationKind,
            Arguments: (arguments ?? new Dictionary<string, object?>())
                .Select(static pair => new RulesetCapabilityArgument(pair.Key, RulesetCapabilityBridge.FromObject(pair.Value)))
                .ToArray());
}
