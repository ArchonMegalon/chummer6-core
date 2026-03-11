using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

internal static class SessionOverlayEventValidator
{
    public static bool AllowsEvent(SessionEventEnvelope item, ICollection<RulesetCapabilityDiagnostic> diagnostics)
    {
        if (item.Payload.ContainsKey("currentValue"))
        {
            diagnostics.Add(new RulesetCapabilityDiagnostic(
                "session.replay.tracker.absolute-write-blocked",
                "session.replay.tracker.absolute-write-blocked",
                RulesetCapabilityDiagnosticSeverities.Error,
                MessageKey: "session.replay.tracker.absolute-write-blocked"));
            return false;
        }

        if (item.Payload.ContainsKey("absoluteValue"))
        {
            diagnostics.Add(new RulesetCapabilityDiagnostic(
                "session.replay.absolute-write-blocked",
                "session.replay.absolute-write-blocked",
                RulesetCapabilityDiagnosticSeverities.Error,
                MessageKey: "session.replay.absolute-write-blocked"));
            return false;
        }

        return true;
    }
}
