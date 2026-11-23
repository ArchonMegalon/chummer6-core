using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

public static partial class SessionPlayElementalFollowups
{
    // Core-owned source identities, SR5:146–147; no free-text default eligibility.
    public static string PilotSkillName5393(SessionPlayPilotSkill skill) => skill switch {
        SessionPlayPilotSkill.GroundCraft => "Pilot Ground Craft", SessionPlayPilotSkill.Aircraft => "Pilot Aircraft",
        SessionPlayPilotSkill.Aerospace => "Pilot Aerospace", SessionPlayPilotSkill.Watercraft => "Pilot Watercraft",
        SessionPlayPilotSkill.Walker => "Pilot Walker", SessionPlayPilotSkill.Maneuvering => "Maneuvering",
        _ => throw new SessionPlayException(SessionPlayFailures.Invalid("elemental-pilot-skill-unsupported")) };
    private static SessionPlayPilotSkill ResolvePilotSkill5393(SessionPlayVehicleTestInput x, SessionPlayVehiclePolicy5400? policy = null)
    {
        var candidates = Enum.GetValues<SessionPlayPilotSkill>().Where(s => PilotSkillName5393(s) == x.SkillName).ToArray();
        Require(candidates.Length == 1 && (x.PilotSkill is null || x.PilotSkill == candidates[0]), "elemental-pilot-skill-unsupported");
        var skill = candidates[0];
        Require((x.Control == SessionPlayVehicleControl.Autonomous) == (skill == SessionPlayPilotSkill.Maneuvering), "elemental-pilot-skill-control-mismatch");
        Require(x.Skill > 0 || (policy is null ? skill is SessionPlayPilotSkill.GroundCraft or SessionPlayPilotSkill.Watercraft
            : policy.Pilots.SingleOrDefault(p => p.Skill == skill)?.CanDefault == true),
            "elemental-pilot-skill-cannot-default");
        return skill;
    }
    // External table vehicles only. Source-bound owned-vehicle creation routines are not an
    // authority to change a declared target. Reuse the existing monitor/wound kernels here.
    private static (int Pool, int Limit, int Threshold) VehicleTestBasis5381(SessionPlayVehicleTestInput x, bool currentRules = true, SessionPlayVehiclePolicy5400? policy = null)
    {
        Require(Enum.IsDefined(x.Control), "elemental-vehicle-control-required");
        Label(x.Driver); Label(x.SkillName); Label(x.Terrain); Label(x.ModifierReason); Label(x.FailureDecision);
        Require(x.OrdinaryNoGlitch, "elemental-special-vehicle-roll-open");
        Rating(x.Skill); if (currentRules) ResolvePilotSkill5393(x, policy); Rating(x.ReactionOrPilot, 1); Rating(x.Handling, 1); Rating(x.Body);
        Rating(x.BaseThreshold, 1); Rating(x.TerrainModifier);
        Require(x.DiceModifier is >= -100 and <= 100 && x.SpeedMetersPerTurn is >= 0 and <= 1000000,
            "elemental-vehicle-modifiers-invalid");
        Require(CharacterVehicleConditionMonitorCalculator.TryCalculatePhysicalMaximum(
            x.Drone ? "Drones: Small" : "Cars", x.Body, [], out int maximum)
            && x.VehicleDamage >= 0 && x.VehicleDamage < maximum, "elemental-vehicle-condition-invalid");
        int vehicleWounds = new CharacterSheetConditionValues(maximum, 100, 3, 0, 0, 0, 0, 0, 0)
            .WithDamage(x.VehicleDamage, 0).WoundModifier;
        bool autonomous = x.Control == SessionPlayVehicleControl.Autonomous;
        bool jumped = x.Control is SessionPlayVehicleControl.JumpedCold or SessionPlayVehicleControl.JumpedHot;
        bool remote = x.Control is SessionPlayVehicleControl.RemoteAr or SessionPlayVehicleControl.RemoteCold or SessionPlayVehicleControl.RemoteHot;
        bool ar = x.Control is SessionPlayVehicleControl.OnboardAr or SessionPlayVehicleControl.RemoteAr;
        bool vr = jumped || x.Control is SessionPlayVehicleControl.RemoteCold or SessionPlayVehicleControl.RemoteHot;
        if (jumped) Require(x.ControlRig is >= 1 and <= 3, "elemental-control-rig-required");
        else Require(x.ControlRig == 0, "elemental-followup-contradictory-input");
        if (remote) Rating(x.DataProcessing, 1);
        else Require(x.DataProcessing == 0, "elemental-followup-contradictory-input");
        int pilotWounds;
        if (autonomous)
        {
            Require(x.Condition is null && x.Skill is >= 1 and <= 6, "elemental-autonomous-maneuvering-required");
            pilotWounds = 0;
        }
        else pilotWounds = currentRules && !x.Aware ? UnawarePilotWounds5401(x.Condition) : Wounds(x.Condition);
        // Defaulting is Reaction - 1; a supplied trained skill already includes its applicable specialization.
        int skill = x.Skill == 0 ? -1 : x.Skill;
        // Version two supplies this value from the retained profile policy helper, never the UI.
        // Older events retain their original explicit inputs solely for historical replay.
        if (x.Control == SessionPlayVehicleControl.JumpedHot)
            Require(x.JumpedHotSimBonus is >= 1 and <= 3, "elemental-jumped-hot-sim-table-choice-required");
        else Require(x.JumpedHotSimBonus == 0, "elemental-followup-contradictory-input");
        int hot = x.Control == SessionPlayVehicleControl.JumpedHot ? x.JumpedHotSimBonus
            : x.Control == SessionPlayVehicleControl.RemoteHot ? 2 : 0;
        int pool = Math.Max(0, checked(x.ReactionOrPilot + skill + pilotWounds + x.ControlRig + hot + x.DiceModifier));
        int limit = Math.Max(1, checked(x.Handling + vehicleWounds + x.ControlRig + (ar ? 1 : vr ? 2 : 0)));
        if (remote) limit = Math.Min(limit, x.DataProcessing);
        int threshold = Math.Max(1, checked(x.BaseThreshold + x.TerrainModifier - x.ControlRig));
        return (pool, limit, threshold);
    }
}
