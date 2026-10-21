namespace Chummer.Contracts.Characters;

public sealed record CharacterCareerPowerIdentity(Guid InternalId, Guid SourceId, string Extra);
public sealed record CharacterCareerPowerCommand(Guid TransactionId, CharacterCareerPowerIdentity Identity,
    int PaidRating, bool ApplyWayDiscount)
{
    // Null omission preserves all historical command/plan/ledger digests.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCareerMagicBinding? ReadBinding { get; init; }
}
public sealed record CharacterCareerPowerSource(Guid SourceId, string Name, string SourceBook,
    string RawSourceXml, string SourceInputsDigest, string ProfileInputsDigest, string CustomDataInputsDigest);
public sealed record CharacterCareerPowerGrantSource(string Kind, Guid SourceId, string RawSourceXml,
    string AuthorityState);

/// <summary>Exact preview of one add/raise. A plan is data; only fresh store admission permits a write.</summary>
public sealed record CharacterCareerPowerPlan(string OwnerId, string WorkspaceId,
    long ExpectedWorkspaceRevision, long ExpectedSavedRevision, string ExpectedCharacterDigest,
    string ExpectedAuxiliaryDigest, string ExpectedAuthorityDigest, string ExpectedRuntimeFingerprint,
    CharacterCareerPowerCommand Command, CharacterAdeptPowerPointIncreaseQuote Quote, string ResultCharacterDigest)
{
    public long TargetWorkspaceRevision => checked(ExpectedWorkspaceRevision + 1);
    public long TargetSavedRevision => TargetWorkspaceRevision;
}

public sealed record CharacterCareerPowerGrant(string Kind, Guid InstanceId, Guid SourceId,
    string PowerName, string Extra, int FreeLevels, int FreePointQuarterUnits, decimal PoolPoints);
public sealed record CharacterCareerPowerEntry(CharacterCareerPowerPlan Plan,
    IReadOnlyList<CharacterCareerPowerGrant> Grants, string PreviousEntryDigest, string EntryDigest);
public enum CharacterCareerPowerOutcome
{
    NotFound, Applied, Replayed, Missing, Conflict, IdempotencyConflict, Corrupt, Unavailable
}
public sealed record CharacterCareerPowerResult(CharacterCareerPowerOutcome Outcome,
    CharacterCareerPowerEntry? Entry = null, long? CurrentRevision = null, string? Error = null);
public sealed record CharacterCareerPowerReview(CharacterCareerPowerPlan? Plan, string? Blocker);
