using Chummer.Contracts;
using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Diagnostics;

public static class CalculationReportSubjectKinds
{
    public const string DerivedValue = "derived-value";
    public const string QuickAction = "quick-action";
    public const string Tracker = "tracker";
    public const string Validation = "validation";
}

public static class CalculationReportPrivacyModes
{
    public const string SupportCase = "support-case";
    public const string LocalOnly = "local-only";
    public const string PublicRedacted = "public-redacted";
}

public sealed record CalculationReportRecentChange(
    string ChangeKey,
    IReadOnlyList<RulesetExplainParameter> Parameters);

public sealed record CalculationReportInput(
    string ReportId,
    string CalculationKey,
    string AppVersion,
    string Platform,
    string RulesetId,
    string RuntimeFingerprint,
    string? RuleEnvironmentId,
    string SubjectId,
    string SubjectKind,
    RulesetCapabilityValue? ActualValue,
    string? ExpectedValue,
    ExplainTraceDto ExplainTrace,
    IReadOnlyList<CalculationReportRecentChange>? RecentChanges = null,
    string PrivacyMode = CalculationReportPrivacyModes.SupportCase,
    bool IncludesCharacterSnapshot = false);

public sealed record CalculationReportPacket(
    string ReportId,
    string CalculationKey,
    string AppVersion,
    string Platform,
    string RulesetId,
    string RuntimeFingerprint,
    string? RuleEnvironmentId,
    string SubjectId,
    string SubjectKind,
    RulesetCapabilityValue? ActualValue,
    string? ExpectedValue,
    ExplainTraceDto ExplainTrace,
    IReadOnlyList<ExplainEvidencePointerDto> SourceAnchors,
    IReadOnlyList<ExplainEvidencePointerDto> Evidence,
    IReadOnlyList<CalculationReportRecentChange> RecentChanges,
    string PrivacyMode,
    bool IncludesCharacterSnapshot,
    string SummaryKey,
    IReadOnlyList<RulesetExplainParameter> SummaryParameters);
