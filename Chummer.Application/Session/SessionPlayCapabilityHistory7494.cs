using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public sealed partial class DurableSessionPlayService
{
    // Created only after ResolveCurrentWithContinuation has admitted the complete source chain.
    private static SessionPlayCapabilityHistory7494? CapabilityHistory7494(ISessionPlayReader reader)
    {
        var focuses = FocusCheckpoints7490(reader);
        if (focuses.Count == 0) return null;
        var ammunition = reader.ReadConditionTransfers()
            .Where(x => x.Receipt is not null && x.Intent.Review.Ammunition is not null)
            .OrderBy(x => x.Receipt!.Canonical.RecordedRevision)
            .Select(x => new SessionAmmunitionCheckpoint5712(x.Intent.Review.Ammunition!.EventCount,
                x.Receipt!.ContinuedAuthority)).ToArray();
        return new(focuses, ammunition);
    }
}

// Authority-only history: source checkpoints and explicit capability events. Conditions,
// rolls and outcomes remain exclusively in the existing reducer. No origin is rewritten.
internal sealed class SessionPlayCapabilityHistory7494(
    IReadOnlyList<SessionFocusCastingCheckpoint7490> focuses,
    IReadOnlyList<SessionAmmunitionCheckpoint5712> ammunition)
{
    internal SessionPlayAuthority SourceAt(SessionPlayAuthority firstPreimage, int eventCount)
        => focuses.Where(x => x.EventCount <= eventCount).Select(x => x.Authority)
            .Concat(ammunition.Where(x => x.EventCount <= eventCount).Select(x => x.Authority))
            .OrderBy(x => long.Parse(x.Binding.Version.VersionId, System.Globalization.CultureInfo.InvariantCulture))
            .LastOrDefault() ?? firstPreimage;

    internal SessionPlayAuthority At(SessionPlayOrigin origin, IReadOnlyList<SessionEventEnvelope> prefix)
    {
        var prior = origin.Authority;
        int focusIndex = 0, ammunitionIndex = 0;
        long latestAmmunitionRevision = 0;
        for (int boundary = 0; boundary <= prefix.Count; boundary++)
        {
            while (ammunitionIndex < ammunition.Count && ammunition[ammunitionIndex].EventCount == boundary)
            {
                var checkpoint = ammunition[ammunitionIndex++];
                prior = prior with { Derived = checkpoint.Authority.Derived };
                latestAmmunitionRevision = Math.Max(latestAmmunitionRevision, long.Parse(
                    checkpoint.Authority.Binding.Version.VersionId, System.Globalization.CultureInfo.InvariantCulture));
            }
            while (focusIndex < focuses.Count && focuses[focusIndex].EventCount == boundary)
                prior = ApplyFocus(origin, prior, focuses[focusIndex++], prefix.Take(boundary).ToArray(), latestAmmunitionRevision);
            if (boundary == prefix.Count) break;
            var e = prefix[boundary];
            var before = prefix.Take(boundary);
            prior = e.EventType switch
            {
                SessionPlayEventTypes.MagicCapabilitiesAdopt => SessionPlayMagicUpgrade5311.Replay(prior, e, before),
                SessionPlayEventTypes.IndirectCastingAdopt => SessionPlayIndirectUpgrade5338.Replay(prior, e, before),
                SessionPlayEventTypes.InitiativeCapabilitiesAdopt => SessionPlayInitiativeUpgrade5110.Replay(prior, e, before),
                _ => prior
            };
        }
        return prior;
    }

    // Shared by this authority walk and Root's reducer, including its Skills/ammunition rule.
    // Preserve the replay binding; the exact source revision is admitted by the event envelope.
    internal static SessionPlayAuthority ApplyFocus(SessionPlayOrigin origin, SessionPlayAuthority prior,
        SessionFocusCastingCheckpoint7490 checkpoint, IReadOnlyList<SessionEventEnvelope> prefix,
        long latestAmmunitionRevision)
        => prior with {
            CastingAuthority = SessionPlayMagicUpgrade5311.CurrentCapabilities(origin, checkpoint.Authority, prefix).CastingAuthority,
            Derived = checkpoint.Revision > latestAmmunitionRevision
                ? prior.Derived with { Skills = checkpoint.Authority.Derived.Skills } : prior.Derived
        };
}
