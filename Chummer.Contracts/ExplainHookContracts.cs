using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts;

public sealed record ExplainHookReference(
    string HookId,
    string TraceId,
    string SubjectId,
    string? CapabilityId = null,
    string? ProviderId = null,
    string? PackId = null,
    string? RuntimeFingerprint = null);

public sealed record ExplainHookAttachment(
    string TargetKind,
    string TargetId,
    ExplainHookReference Explain);

public sealed record ExplainHookComposition(
    string CompositionId,
    IReadOnlyList<ExplainHookAttachment> Attachments);

public static class ExplainHookCompositionLocalization
{
    public static string ResolveTargetKindKey(ExplainHookAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return $"explain.hook.target.{attachment.TargetKind}";
    }

    public static IReadOnlyList<RulesetExplainParameter> ResolveTargetKindParameters(ExplainHookAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        return
        [
            new RulesetExplainParameter("targetKind", RulesetCapabilityBridge.FromObject(attachment.TargetKind)),
            new RulesetExplainParameter("targetId", RulesetCapabilityBridge.FromObject(attachment.TargetId))
        ];
    }
}
