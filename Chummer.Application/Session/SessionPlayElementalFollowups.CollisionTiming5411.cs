using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

public static partial class SessionPlayElementalFollowups
{
    // Admission only: historical replay must not synthesize a new clock or change old results.
    internal static void ValidateNewCollisionTiming5411(SessionEventEnvelope e)
    {
        var c = Decode(e);
        if (c.Action == SessionPlayElementalAction.UncontrolledMovement)
            Require(c.Movement?.Timing is { Version: 1 }, "elemental-reviewed-movement-time-required");
    }

    public static (SessionPlayCrashTimeBasis Basis, string AnchorId) CollisionTimingAnchor5411(
        SessionPlayCrashIncident x, IReadOnlyList<SessionPlayElementalOutcome> history)
    {
        var driver = x.Occupants.SingleOrDefault(p => p.Input.IsOnboardDriver);
        if (driver?.ComposureHits is not null)
        {
            var test = history.LastOrDefault(h => h.Incident?.IncidentId == x.IncidentId
                && h.Input.Action == SessionPlayElementalAction.CrashComposure
                && h.Input.Composure is { Hits: not null, Recovery: null } c && c.Index == driver.Input.Index);
            Require(test is not null, "elemental-driver-composure-anchor-unavailable");
            return (SessionPlayCrashTimeBasis.DriverComposure, test!.EventId);
        }
        return (x.Collided ? SessionPlayCrashTimeBasis.Collision : SessionPlayCrashTimeBasis.UncontrolledIncident, x.IncidentId);
    }

    private static void ValidateCollisionTiming5411(SessionPlayCrashIncident x, SessionPlayCrashMovementTiming timing,
        IReadOnlyList<SessionPlayElementalOutcome> history)
    {
        Require(timing.Version == 1 && Enum.IsDefined(timing.Basis) && timing.ElapsedCombatTurns is >= 0 and <= 1000000,
            "elemental-movement-time-invalid");
        Require(!string.IsNullOrWhiteSpace(timing.Adjudicator) && !string.IsNullOrWhiteSpace(timing.Evidence),
            "elemental-movement-time-evidence-required");
        Label(timing.Adjudicator); Label(timing.Evidence);
        var anchor = CollisionTimingAnchor5411(x, history);
        Conflict(timing.Basis == anchor.Basis && timing.AnchorId == anchor.AnchorId, "elemental-movement-time-anchor-changed");
        if (x.MovementTiming is { } previous && previous.Basis == timing.Basis && previous.AnchorId == timing.AnchorId)
            Conflict(timing.ElapsedCombatTurns >= previous.ElapsedCombatTurns, "elemental-movement-time-cannot-rewind");
    }

    // Same canonical monitor kernel and strict below-full rule as VehicleTestBasis5381.
    // An enabled Pilot does not repair a wreck. The exceptional jumped-in Edge maneuver is not this route.
    public static bool CollisionChassisOperable5411(SessionPlayCrashIncident x)
    {
        int damage = x.VehicleDamageAfter ?? x.PriorVehicleDamage ?? x.OriginalTest.VehicleDamage;
        return CharacterVehicleConditionMonitorCalculator.TryCalculatePhysicalMaximum(
            x.OriginalTest.Drone ? "Drones: Small" : "Cars", x.OriginalTest.Body, [], out int maximum)
            && damage >= 0 && damage < maximum;
    }
}
