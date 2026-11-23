using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;
namespace Chummer.Application.Session;
public static partial class SessionPlayElementalFollowups
{
    public static SessionPlayConditionState5412 ConditionState5412(int physicalMaximum, int stunMaximum,
        int physical, int stun, int overflow, int body, int overflowLimit, bool? stabilized, int excessStunRemainder = 0)
    {
        // The bool is only a kernel validation input when the fact is absent. The
        // returned state retains null, and no alive overflowing health is inferred.
        var known = CrashCondition5401(new(physicalMaximum, stunMaximum, physical, stun),
            overflow, body, overflowLimit, stabilized ?? false, excessStunRemainder);
        SessionPlayCrashHealth? health = overflow > 0 && stabilized is null && known.Health != SessionPlayCrashHealth.Dead
            ? null : known.Health;
        return new(physicalMaximum, stunMaximum, physical, stun, overflow, body, overflowLimit, stabilized, health) { ExcessStunRemainder = excessStunRemainder };
    }

    // Shared current transition: collision and general recovery retain the same carry rules.
    public static SessionPlayCrashCondition RecoveryTransition5412(SessionPlayCrashCondition before,
        SessionPlayCrashRecoveryInput input)
    {
        Require(input.Condition is not null && Enum.IsDefined(input.Kind), "elemental-crash-recovery-kind-invalid");
        Require(!string.IsNullOrWhiteSpace(input.Adjudicator) && !string.IsNullOrWhiteSpace(input.Evidence), "elemental-recovery-evidence-required");
        Label(input.Adjudicator); Label(input.Evidence);
        Conflict(before.Health != SessionPlayCrashHealth.Dead, "elemental-dead-passenger-cannot-recover");
        Require(input.Condition.PhysicalMaximum == before.PhysicalMaximum && input.Condition.StunMaximum == before.StunMaximum,
            "elemental-recovery-monitor-source-mismatch");
        var after = CrashCondition5401(input.Condition, input.PhysicalOverflow, before.Body, before.OverflowLimit, input.Stabilized, input.ExcessStunRemainder);
        switch (input.Kind)
        {
            case SessionPlayCrashRecoveryKind.Stabilization:
                Require(before.Overflow > 0 && !before.Stabilized && after.Stabilized && input.ElapsedMinutes == 0
                    && after.Physical == before.Physical && after.Stun == before.Stun && after.Overflow == before.Overflow
                    && after.ExcessStunRemainder == before.ExcessStunRemainder,
                    "elemental-stabilization-receipt-invalid");
                break;
            case SessionPlayCrashRecoveryKind.Healing:
                Require(input.ElapsedMinutes == 0 && after.Physical <= before.Physical && after.Stun <= before.Stun
                    && after.Overflow <= before.Overflow && after.ExcessStunRemainder <= before.ExcessStunRemainder
                    && (after.Physical < before.Physical || after.Stun < before.Stun || after.Overflow < before.Overflow
                        || after.ExcessStunRemainder < before.ExcessStunRemainder)
                    && (before.Overflow == 0 || before.Stabilized) && (after.Overflow == 0 || after.Stabilized),
                    "elemental-healing-receipt-invalid");
                break;
            case SessionPlayCrashRecoveryKind.UnattendedInterval:
                Require(before.Health == SessionPlayCrashHealth.Dying && input.ElapsedMinutes == before.Body
                    && after.Physical == before.Physical && after.Stun == before.Stun && !after.Stabilized
                    && after.ExcessStunRemainder == before.ExcessStunRemainder && after.Overflow == checked(before.Overflow + 1), "elemental-unattended-interval-invalid");
                break;
        }
        return after;
    }
}
