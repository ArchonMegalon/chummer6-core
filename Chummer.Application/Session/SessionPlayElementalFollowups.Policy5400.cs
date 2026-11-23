using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

public static partial class SessionPlayElementalFollowups
{
    internal static bool NeedsVehiclePolicy5400(SessionPlayElementalCommand command)
        => command.Action == SessionPlayElementalAction.FireVehicleTick
            || command.Action == SessionPlayElementalAction.IceVehicle && command.Vehicle is { TouchesSheet: true }
            || command.Action == SessionPlayElementalAction.UncontrolledMovement
                && command.Movement?.Decision == SessionPlayMovementDecision.RegainControl;

    // Preview issues the canonical source-bound command which the existing confirmation journals verbatim.
    // Apply checks equality again; a caller cannot replace this with a freshly self-hashed table choice.
    internal static SessionEventEnvelope PrepareVehiclePolicy5400(SessionEventEnvelope e, SessionPlayAuthority current)
    {
        var command = Decode(e);
        Require(command.RulesVersion is 1 or 2, "elemental-current-rules-required");
        if (!NeedsVehiclePolicy5400(command)) return e;
        var policy = current.VehiclePolicy;
        Require(policy is not null, "vehicle-policy-source-required-repair-and-readopt-profile");
        ValidatePolicy5400(policy!, current);
        if (command.VehiclePolicy is not null)
            Conflict(Digest(command.VehiclePolicy) == Digest(policy), "vehicle-policy-review-source-changed");
        command = command with { RulesVersion = 2, VehiclePolicy = policy };
        return e with { Payload = Encode(command) };
    }

    internal static void ValidateNewVehiclePolicy5400(SessionEventEnvelope e, SessionPlayAuthority current)
    {
        var command = Decode(e);
        Require(command.RulesVersion is 1 or 2, "elemental-current-rules-required");
        if (!NeedsVehiclePolicy5400(command))
        {
            Require(command.RulesVersion == 1 && command.VehiclePolicy is null, "vehicle-policy-unexpected");
            return;
        }
        Require(command.RulesVersion == 2 && command.VehiclePolicy is not null, "vehicle-policy-preview-required");
        ValidatePolicy5400(command.VehiclePolicy!, current);
        Conflict(current.VehiclePolicy is not null && Digest(command.VehiclePolicy) == Digest(current.VehiclePolicy),
            "vehicle-policy-review-source-changed");
    }

    private static void ValidatePolicy5400(SessionPlayVehiclePolicy5400 policy, SessionPlayAuthority authority)
    {
        Require(policy.Version == 1 && policy.PolicyId is SessionPlayVehiclePolicySource5400.Standard or "sr5-profile-vehicle-override/v1"
            && policy.JumpedHotSimBonus is >= 1 and <= 3
            && policy.ArmorEquality is SessionPlayVehicleArmorEquality.ArmorStopsDamage or SessionPlayVehicleArmorEquality.RollResistance
            && !string.IsNullOrWhiteSpace(policy.EnvironmentDigest) && !string.IsNullOrWhiteSpace(policy.SettingsDigest)
            && policy.Pilots is { Count: 5 } && policy.Pilots.All(p => p is not null) && policy.Pilots.Select(p => p.Skill).Distinct().Count() == 5
            && policy.Pilots.All(p => p.Skill != SessionPlayPilotSkill.Maneuvering && Enum.IsDefined(p.Skill)
                && p.Name == PilotSkillName5393(p.Skill) && Guid.TryParse(p.SourceId, out _)
                && !string.IsNullOrWhiteSpace(p.SourceDigest)), "vehicle-policy-invalid");
        Conflict(policy.OwnerId == authority.Binding.OwnerId && policy.CharacterVersion == authority.Binding.Version
            && policy.CharacterSourceDigest == authority.Binding.SourceDigest, "vehicle-policy-owner-or-source-changed");
        Conflict(policy.Digest == Digest(policy with { Digest = "" }), "vehicle-policy-digest-invalid");
    }

    private static SessionPlayVehiclePolicy5400 Policy5400(SessionPlayElementalCommand c, SessionPlayAuthority? authority)
    {
        Require(c.RulesVersion == 2 && c.VehiclePolicy is not null && authority is not null, "vehicle-policy-retained-snapshot-required");
        // Replay deliberately uses the event's admitted snapshot, never today's profile interpretation.
        ValidatePolicy5400(c.VehiclePolicy!, authority!);
        return c.VehiclePolicy!;
    }

    private static SessionPlayVehicleTestInput ApplyVehiclePolicy5400(SessionPlayVehicleTestInput test,
        SessionPlayElementalCommand command, SessionPlayAuthority? authority)
    {
        Require(test.JumpedHotSimBonus == 0, "elemental-per-action-rule-override-forbidden");
        var policy = Policy5400(command, authority);
        ResolvePilotSkill5393(test, policy);
        return test with { JumpedHotSimBonus = test.Control == SessionPlayVehicleControl.JumpedHot ? policy.JumpedHotSimBonus : 0 };
    }
}
