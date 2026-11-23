using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Session;

// Service-issued choices for already resolved spells. No casting, drain or spell benefit authority.
public sealed record SessionPlaySustainingChoice(string CapabilityId, Guid SpellId, Guid SourceId,
    string DisplayName, string SourceBook, string SourcePage, string SourceDigest,
    int MinimumForce, int MaximumForce, int MinimumNetHits, int MaximumNetHits)
{
    public string? EnvironmentDigest { get; init; }
}
public sealed record SessionPlayMechanicalEffect(string InstanceId, Guid SpellId, string DisplayName,
    int Force, int NetHits, bool SelfSustained, bool Active, CharacterSustainedObjectIdentity? BaselineIdentity,
    string StateDigest);
public sealed record SessionPlaySustainingProjection(string AuthorityDigest,
    IReadOnlyList<SessionPlaySustainingChoice> Choices, IReadOnlyList<SessionPlayMechanicalEffect> Instances,
    int PenalizedObjectCount, int DicePoolPenalty)
{
    public const string Coverage = "Sustaining penalty only for an already resolved spell. Force and Net Hits are registration inputs, not proof of a legal cast. This operation does not resolve casting, drain, spell benefits or other mechanical effects. Canonical character and PDF are unchanged.";
}

// Captured only by Core after complete source/owner graph admission. Never accepted in a command.
public sealed record SessionPlaySustainingAllowance(decimal Value, int Rating);
public sealed record SessionPlaySustainingSkillBasis(string Id, int RawPool, bool CanRoll);
public sealed record SessionPlaySustainingBasis(int? RawDefense,
    IReadOnlyList<SessionPlaySustainingSkillBasis> Skills, int Composure, int Memory);
public sealed record SessionPlaySustainingAuthority(string Digest,
    IReadOnlyList<SessionPlaySustainingChoice> Choices, IReadOnlyList<SessionPlayMechanicalEffect> Baseline,
    int PerObject, bool Psyche, IReadOnlyList<SessionPlaySustainingAllowance> Allowances,
    SessionPlaySustainingBasis Basis);

public sealed record SessionPlaySustainingApply(string AuthorityDigest, string Cursor,
    string CapabilityId, string InstanceId, int Force, int NetHits);
public sealed record SessionPlaySustainingEnd(string AuthorityDigest, string Cursor,
    string InstanceId, string ExpectedStateDigest);
public sealed record SessionPlayPoolChange(string Label, int? Before, int? After);
public sealed record SessionPlaySustainingReview(SessionPatchRequest Request, string PayloadDigest,
    SessionPlaySustainingProjection Before, SessionPlaySustainingProjection After,
    IReadOnlyList<SessionPlayPoolChange> PoolChanges);
public enum SessionPlayLookupStatus { NotFound, PreparedOnly, Completed }
public sealed record SessionPlayOperationLookup(SessionPlayLookupStatus Status, string OperationId,
    string PayloadDigest, SessionPlayOperationResult? Result);
