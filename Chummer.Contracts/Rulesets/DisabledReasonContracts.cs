namespace Chummer.Contracts.Rulesets;

public sealed record DisabledReasonPayload(
    string ReasonKey,
    IReadOnlyList<RulesetExplainParameter>? ReasonParameters = null,
    string? Reason = null);

public static class DisabledReasonPayloadLocalization
{
    public static string ResolveReasonKey(DisabledReasonPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return string.IsNullOrWhiteSpace(payload.ReasonKey)
            ? payload.Reason ?? "ruleset.disabled-reason.unknown"
            : payload.ReasonKey;
    }

    public static IReadOnlyList<RulesetExplainParameter> ResolveReasonParameters(DisabledReasonPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return payload.ReasonParameters ?? [];
    }
}
