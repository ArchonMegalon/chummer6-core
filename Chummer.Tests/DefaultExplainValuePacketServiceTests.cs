#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Application.Explain;
using Chummer.Contracts;
using Chummer.Contracts.Content;
using Chummer.Contracts.Diagnostics;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class DefaultExplainValuePacketServiceTests
{
    [TestMethod]
    public void CreatePacket_normalizes_inputs_limits_counterfactuals_and_projects_coverage()
    {
        DefaultExplainValuePacketService service = new();

        ExplainValuePacket packet = service.CreatePacket(new ExplainValuePacketInput(
            PacketId: " packet-1 ",
            CalculationKey: " calc.attack ",
            RulesetId: " sr5 ",
            RuntimeFingerprint: " sha256:test ",
            SubjectId: " char-1 ",
            SubjectKind: " character ",
            Value: RulesetCapabilityBridge.FromObject(11),
            ExplainTrace: CreateTrace(
                targetKey: " attack.total ",
                evidence:
                [
                    new ExplainEvidencePointerDto(" runtime-lock ", " sha256:test ", RuleId: "  "),
                    new ExplainEvidencePointerDto(RulesetEvidencePointerKinds.RuleReference, " sr5#combat.attack ", RuleId: " attack.rule "),
                    new ExplainEvidencePointerDto(RulesetEvidencePointerKinds.RuleReference, " sr5#combat.attack ", RuleId: " attack.rule ")
                ],
                stepEvidence:
                [
                    new ExplainEvidencePointerDto(RulesetEvidencePointerKinds.CapabilityDescriptor, " gm-prep.packet/attack "),
                    new ExplainEvidencePointerDto(RulesetEvidencePointerKinds.CapabilityDescriptor, " gm-prep.packet/attack ")
                ]),
            Validation: new ValidationSummary(
                ScopeKind: " character ",
                ScopeId: " char-1 ",
                State: " warnings ",
                SummaryKey: " validation.summary.warnings ",
                SummaryParameters: null!,
                TotalCount: 2,
                ErrorCount: 0,
                WarningCount: 1,
                InfoCount: 1,
                Failures:
                [
                    new ValidationFailureEnvelope(
                        FailureId: " z-failure ",
                        Code: " code-z ",
                        Severity: " warning ",
                        MessageKey: " message.z ",
                        MessageParameters: null!,
                        SubjectId: " char-1 ",
                        RuntimeFingerprint: " sha256:test "),
                    new ValidationFailureEnvelope(
                        FailureId: " a-failure ",
                        Code: " code-a ",
                        Severity: " info ",
                        MessageKey: " message.a ",
                        MessageParameters: null!)
                ]),
            Delta: new RuntimeLockDiffProjection(
                BeforeFingerprint: " before ",
                AfterFingerprint: " after ",
                Changes:
                [
                    new RuntimeLockDiffChange(" z-kind ", " z-subject ", " before-z ", " after-z ", " reason.z ", null!),
                    new RuntimeLockDiffChange(" a-kind ", " a-subject ", " ", " after-a ", " reason.a ", null!)
                ]),
            Counterfactuals:
            [
                CreateCounterfactual(" cf-3 ", ExplainCounterfactualOutcomeKinds.WhatIf, 3, legalityState: " allowed "),
                CreateCounterfactual(" cf-1 ", ExplainCounterfactualOutcomeKinds.Why, 1),
                CreateCounterfactual(" cf-4 ", ExplainCounterfactualOutcomeKinds.WhyNot, 4),
                CreateCounterfactual(" cf-2 ", ExplainCounterfactualOutcomeKinds.WhyNot, 2)
            ],
            PrivacyMode: " support-case ",
            SummaryKey: " explain.packet.summary.attack ",
            SummaryParameters:
            [
                new RulesetExplainParameter("topic", RulesetCapabilityBridge.FromObject("attack"))
            ]));

        Assert.AreEqual("packet-1", packet.PacketId);
        Assert.AreEqual("calc.attack", packet.CalculationKey);
        Assert.AreEqual("sr5", packet.RulesetId);
        Assert.AreEqual("sha256:test", packet.RuntimeFingerprint);
        Assert.AreEqual("char-1", packet.SubjectId);
        Assert.AreEqual("character", packet.SubjectKind);
        Assert.AreEqual("support-case", packet.PrivacyMode);
        Assert.AreEqual("explain.packet.summary.attack", packet.SummaryKey);

        Assert.HasCount(3, packet.Evidence);
        CollectionAssert.AreEqual(
            new[]
            {
                RulesetEvidencePointerKinds.CapabilityDescriptor,
                RulesetEvidencePointerKinds.RuleReference,
                RulesetEvidencePointerKinds.RuntimeLock
            },
            packet.Evidence.Select(pointer => pointer.Kind).ToArray());
        Assert.HasCount(2, packet.SourceAnchors);
        CollectionAssert.AreEquivalent(
            new[]
            {
                RulesetEvidencePointerKinds.RuleReference,
                RulesetEvidencePointerKinds.RuntimeLock
            },
            packet.SourceAnchors.Select(pointer => pointer.Kind).ToArray());

        Assert.IsNotNull(packet.Validation);
        Assert.IsNotNull(packet.Delta);
        CollectionAssert.AreEqual(
            new[] { "a-failure", "z-failure" },
            packet.Validation.Failures.Select(failure => failure.FailureId).ToArray());
        CollectionAssert.AreEqual(
            new[] { "a-kind", "z-kind" },
            packet.Delta.Changes.Select(change => change.Kind).ToArray());
        Assert.IsNull(packet.Delta.Changes[0].BeforeValue);
        Assert.HasCount(0, packet.Validation.SummaryParameters);
        Assert.HasCount(0, packet.Validation.Failures[0].MessageParameters);
        Assert.HasCount(0, packet.Delta.Changes[0].ReasonParameters);

        Assert.AreEqual(DefaultExplainValuePacketService.MaxCounterfactuals, packet.Counterfactuals.Count);
        Assert.AreEqual(1, packet.CounterfactualOverflowCount);
        CollectionAssert.AreEqual(
            new[] { "cf-1", "cf-2", "cf-3" },
            packet.Counterfactuals.Select(counterfactual => counterfactual.CounterfactualId).ToArray());

        Assert.IsTrue(packet.CoverageRegistry.Any(row => row.SurfaceKind == ExplainValuePacketCoverageKinds.MechanicalResult));
        Assert.IsTrue(packet.CoverageRegistry.Any(row => row.SurfaceKind == ExplainValuePacketCoverageKinds.LegalityState));
        Assert.IsTrue(packet.CoverageRegistry.Any(row => row.SurfaceKind == ExplainValuePacketCoverageKinds.Warning));
        Assert.IsTrue(packet.CoverageRegistry.Any(row => row.SurfaceKind == ExplainValuePacketCoverageKinds.BeforeAfterDelta));
        Assert.IsTrue(packet.CoverageRegistry.Any(row => row.SurfaceKind == ExplainValuePacketCoverageKinds.SourceAnchor));
        Assert.AreEqual(
            DefaultExplainValuePacketService.MaxCounterfactuals,
            packet.CoverageRegistry.Count(row => row.SurfaceKind == ExplainValuePacketCoverageKinds.Counterfactual));
    }

    [TestMethod]
    public void CreatePacket_rejects_unknown_counterfactual_outcome_kind()
    {
        DefaultExplainValuePacketService service = new();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => service.CreatePacket(new ExplainValuePacketInput(
            PacketId: "packet-1",
            CalculationKey: "calc.attack",
            RulesetId: "sr5",
            RuntimeFingerprint: "sha256:test",
            SubjectId: "char-1",
            SubjectKind: "character",
            Value: null,
            ExplainTrace: CreateTrace("attack.total"),
            Counterfactuals:
            [
                CreateCounterfactual("cf-1", "unsupported", 0)
            ])));
    }

    private static ExplainCounterfactualInput CreateCounterfactual(
        string id,
        string outcomeKind,
        int displayOrder,
        string? legalityState = null)
        => new(
            CounterfactualId: id,
            OutcomeKind: outcomeKind,
            LabelKey: "explain.counterfactual.label",
            LabelParameters: null,
            Value: RulesetCapabilityBridge.FromObject(displayOrder),
            ExplainTrace: CreateTrace(
                $"counterfactual.{id}",
                evidence:
                [
                    new ExplainEvidencePointerDto(RulesetEvidencePointerKinds.RuleReference, $"sr5#counterfactual.{id}", RuleId: id)
                ]),
            Validation: null,
            Delta: null,
            LegalityState: legalityState,
            SummaryKey: "explain.counterfactual.summary",
            SummaryParameters: null,
            DisplayOrder: displayOrder);

    private static ExplainTraceDto CreateTrace(
        string targetKey,
        IReadOnlyList<ExplainEvidencePointerDto>? evidence = null,
        IReadOnlyList<ExplainEvidencePointerDto>? stepEvidence = null)
        => new(
            TargetKey: targetKey,
            FinalValue: RulesetCapabilityBridge.FromObject(10),
            SummaryKey: "explain.summary",
            SummaryParameters:
            [
                new RulesetExplainParameter("target", RulesetCapabilityBridge.FromObject(targetKey))
            ],
            Steps:
            [
                new TraceStepDto(
                    ProviderId: " rules.provider ",
                    CapabilityId: " capability.attack ",
                    PackId: " official.core ",
                    ExplanationKey: " explain.step ",
                    ExplanationParameters: null!,
                    Category: " combat ",
                    RuleId: " attack.rule ",
                    Evidence: stepEvidence)
            ],
            Evidence: evidence);
}
