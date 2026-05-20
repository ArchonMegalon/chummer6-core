#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Application.Content;
using Chummer.Application.Validation;
using Chummer.Contracts;
using Chummer.Contracts.Content;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class RuntimeLockDiffAndValidationSummaryServiceTests
{
    [TestMethod]
    public void Default_runtime_lock_diff_service_orders_and_projects_all_change_kinds()
    {
        DefaultRuntimeLockDiffService service = new();

        RuntimeLockDiffProjection diff = service.Diff(
            new ResolvedRuntimeLock(
                RulesetId: "sr5",
                ContentBundles:
                [
                    new ContentBundleDescriptor("bundle-old", "sr5", "1.0.0", "Old", "Old bundle", ["old.xml"])
                ],
                RulePacks:
                [
                    new ArtifactVersionReference("pack-old", "1.0.0")
                ],
                ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["derive.stat"] = "provider-old"
                },
                EngineApiVersion: "1.0.0",
                RuntimeFingerprint: "sha256:before"),
            new ResolvedRuntimeLock(
                RulesetId: "sr6",
                ContentBundles:
                [
                    new ContentBundleDescriptor("bundle-new", "sr6", "2.0.0", "New", "New bundle", ["new.xml"])
                ],
                RulePacks:
                [
                    new ArtifactVersionReference("pack-new", "2.0.0")
                ],
                ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["derive.stat"] = "provider-new"
                },
                EngineApiVersion: "2.0.0",
                RuntimeFingerprint: "sha256:after"));

        CollectionAssert.AreEqual(
            new[]
            {
                RuntimeLockDiffChangeKinds.RulesetChanged,
                RuntimeLockDiffChangeKinds.EngineApiChanged,
                RuntimeLockDiffChangeKinds.ContentBundleAdded,
                RuntimeLockDiffChangeKinds.ContentBundleRemoved,
                RuntimeLockDiffChangeKinds.RulePackAdded,
                RuntimeLockDiffChangeKinds.RulePackRemoved,
                RuntimeLockDiffChangeKinds.ProviderBindingChanged
            },
            diff.Changes.Select(change => change.Kind).ToArray());
        Assert.AreEqual("sha256:before", diff.BeforeFingerprint);
        Assert.AreEqual("sha256:after", diff.AfterFingerprint);
        Assert.AreEqual("bundle-new@2.0.0", diff.Changes[2].SubjectId);
        Assert.AreEqual("pack-old@1.0.0", diff.Changes[5].SubjectId);
        Assert.AreEqual("derive.stat", diff.Changes[6].SubjectId);
    }

    [TestMethod]
    public void Default_validation_summary_service_orders_failures_counts_severities_and_attaches_hooks()
    {
        DefaultValidationSummaryService service = new();

        ValidationSummary summary = service.BuildSummary(
            scopeKind: " Character ",
            scopeId: " char-7 ",
            diagnostics:
            [
                new RulesetCapabilityDiagnostic(
                    Code: " warning.code ",
                    Message: "warning message",
                    Severity: RulesetCapabilityDiagnosticSeverities.Warning,
                    MessageParameters:
                    [
                        new RulesetExplainParameter("subjectId", RulesetCapabilityBridge.FromObject("char-7")),
                        new RulesetExplainParameter("capabilityId", RulesetCapabilityBridge.FromObject("derive.stat"))
                    ]),
                new RulesetCapabilityDiagnostic(
                    Code: " error.code ",
                    Message: "error message",
                    Severity: RulesetCapabilityDiagnosticSeverities.Error,
                    MessageKey: " validation.error ",
                    MessageParameters:
                    [
                        new RulesetExplainParameter("subjectId", RulesetCapabilityBridge.FromObject("char-6")),
                        new RulesetExplainParameter("providerId", RulesetCapabilityBridge.FromObject("provider-x")),
                        new RulesetExplainParameter("packId", RulesetCapabilityBridge.FromObject("pack-a"))
                    ]),
                new RulesetCapabilityDiagnostic(
                    Code: " info.code ",
                    Message: "info message",
                    Severity: " ",
                    MessageParameters: [])
            ],
            runtimeFingerprint: " sha256:test ",
            explainHooksByCode: new Dictionary<string, ExplainHookReference>(StringComparer.Ordinal)
            {
                ["error.code"] = new ExplainHookReference("explain-1", "entry", "hook.summary")
            });

        Assert.AreEqual("character", summary.ScopeKind);
        Assert.AreEqual("char-7", summary.ScopeId);
        Assert.AreEqual(ValidationSummaryStates.Invalid, summary.State);
        Assert.AreEqual("validation.summary.invalid", summary.SummaryKey);
        Assert.AreEqual(3, summary.TotalCount);
        Assert.AreEqual(1, summary.ErrorCount);
        Assert.AreEqual(1, summary.WarningCount);
        Assert.AreEqual(1, summary.InfoCount);
        CollectionAssert.AreEqual(
            new[] { "error.code", "warning.code", "info.code" },
            summary.Failures.Select(failure => failure.Code).ToArray());
        Assert.AreEqual("0000:error.code:char-6", summary.Failures[0].FailureId);
        Assert.AreEqual(" validation.error ", summary.Failures[0].MessageKey);
        Assert.AreEqual("provider-x", summary.Failures[0].ProviderId);
        Assert.AreEqual("pack-a", summary.Failures[0].PackId);
        Assert.AreEqual("sha256:test", summary.Failures[0].RuntimeFingerprint);
        Assert.IsNotNull(summary.Failures[0].Explain);
        Assert.AreEqual("derive.stat", summary.Failures[1].CapabilityId);
        Assert.AreEqual(RulesetCapabilityDiagnosticSeverities.Info, summary.Failures[2].Severity);
    }
}
