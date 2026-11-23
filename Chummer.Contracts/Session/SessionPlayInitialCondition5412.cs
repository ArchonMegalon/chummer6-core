using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Session;

/// <summary>Frozen source condition. Canonical PhysicalFilled includes overflow damage;
/// calculatedvalues/physicalcmoverflow is a display capacity and is never damage authority.</summary>
public sealed record SessionPlayInitialCondition5412(
    string OwnerId, CharacterVersionReference Version, string SourceDigest, string EnvironmentDigest,
    string DerivedDigest, int PhysicalFilled, int StunFilled, int PhysicalMaximum, int StunMaximum,
    int Body, int OverflowModifier, string Digest)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlayConditionState5412? CanonicalCondition { get; init; }
}

/// <summary>Null stabilization means the source did not record that table fact.
/// Null health with positive overflow means incapacitated, stabilization unknown.</summary>
public sealed record SessionPlayConditionState5412(int PhysicalMaximum, int StunMaximum,
    int Physical, int Stun, int Overflow, int Body, int OverflowLimit, bool? Stabilized,
    SessionPlayCrashHealth? Health)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int ExcessStunRemainder { get; init; }
}

public sealed record SessionPlayConditionProjection5412(string InitialDigest,
    SessionPlayConditionState5412 Current, string Revision);

/// <summary>One reviewed, explicitly owned table receipt. Fact and treatment are mutually exclusive.
/// This is not a roll, collision, remote medical authority or canonical settlement.</summary>
public sealed record SessionPlayConditionCommand5412(string InitialDigest, string ExpectedRevision,
    string Cursor, string TableActionId, string Adjudicator, string Evidence, bool ExplicitlyApplyOwned,
    bool? ObservedStabilized, SessionPlayCrashRecoveryInput? Treatment)
{
    public const string EventType = "condition.recovery.5412";
}
