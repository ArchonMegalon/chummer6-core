#nullable enable annotations

using Chummer.Application.Explain;
using Chummer.Contracts;
using Chummer.Contracts.Diagnostics;
using Chummer.Contracts.Rulesets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class CalculationReportServiceTests
{
    [TestMethod]
    public void CreatePacket_deduplicates_rule_reference_evidence_across_trace()
    {
        DefaultCalculationReportService service = new();
        ExplainEvidencePointerDto ruleReference = new(
            Kind: RulesetEvidencePointerKinds.RuleReference,
            Pointer: "sr6-core#minor-actions",
            RuleId: "sr6.core.actions.minor");
        ExplainTraceDto trace = new(
            TargetKey: "initiative",
            FinalValue: RulesetCapabilityBridge.FromObject("10 + 3d6"),
            SummaryKey: "ruleset.explain.summary.default",
            SummaryParameters: [],
            Steps:
            [
                new TraceStepDto(
                    ProviderId: "official.sr6.core",
                    CapabilityId: "derive.initiative",
                    PackId: null,
                    ExplanationKey: "ruleset.explain.step",
                    ExplanationParameters: [],
                    Category: "derivation",
                    Evidence: [ruleReference])
            ],
            Evidence: [ruleReference]);

        CalculationReportPacket packet = service.CreatePacket(new CalculationReportInput(
            ReportId: "report-1",
            CalculationKey: "initiative",
            AppVersion: "0.1.0-preview",
            Platform: "macos-arm64",
            RulesetId: "sr6",
            RuntimeFingerprint: "sha256:test",
            RuleEnvironmentId: "campaign-rule-env-v3",
            SubjectId: "runner-1",
            SubjectKind: CalculationReportSubjectKinds.DerivedValue,
            ActualValue: RulesetCapabilityBridge.FromObject("10 + 3d6"),
            ExpectedValue: "8 + 1d6",
            ExplainTrace: trace));

        Assert.HasCount(1, packet.SourceAnchors);
        Assert.AreEqual("sr6-core#minor-actions", packet.SourceAnchors[0].Pointer);
        Assert.HasCount(1, packet.Evidence);
        Assert.AreEqual("support.calculation-report.summary", packet.SummaryKey);
    }

    [TestMethod]
    public void CreatePacket_preserves_recent_changes_and_expected_value()
    {
        DefaultCalculationReportService service = new();
        ExplainTraceDto trace = new(
            TargetKey: "essence",
            FinalValue: RulesetCapabilityBridge.FromObject(4.0m),
            SummaryKey: "ruleset.explain.summary.default",
            SummaryParameters: [],
            Steps: []);

        CalculationReportPacket packet = service.CreatePacket(new CalculationReportInput(
            ReportId: "report-2",
            CalculationKey: "essence",
            AppVersion: "0.1.0-preview",
            Platform: "windows-x64",
            RulesetId: "sr6",
            RuntimeFingerprint: "sha256:test-2",
            RuleEnvironmentId: null,
            SubjectId: "runner-2",
            SubjectKind: CalculationReportSubjectKinds.DerivedValue,
            ActualValue: RulesetCapabilityBridge.FromObject(4.0m),
            ExpectedValue: "6.0",
            ExplainTrace: trace,
            RecentChanges:
            [
                new CalculationReportRecentChange(
                    ChangeKey: "cyberware.install",
                    Parameters: [new RulesetExplainParameter("itemId", RulesetCapabilityBridge.FromObject("wired-reflexes-1"))])
            ],
            IncludesCharacterSnapshot: true));

        Assert.AreEqual("6.0", packet.ExpectedValue);
        Assert.HasCount(1, packet.RecentChanges);
        Assert.IsTrue(packet.IncludesCharacterSnapshot);
        Assert.AreEqual("cyberware.install", packet.RecentChanges[0].ChangeKey);
    }
}
