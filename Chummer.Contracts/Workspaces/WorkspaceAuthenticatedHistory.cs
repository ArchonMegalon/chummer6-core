namespace Chummer.Contracts.Workspaces;

/// <summary>
/// Private historical observations. Signatures are useful only with an independently
/// provisioned owner/origin verifier. No key or trust decision travels in this data.
/// </summary>
public sealed record WorkspaceAuthenticatedHistory(WorkspaceAuthenticatedTransition[] Transitions)
{
    public const int MaximumTransitions = 4096;
    public const int MaximumBytes = 32 * 1024 * 1024;
    public override string ToString() => nameof(WorkspaceAuthenticatedHistory);
}

public sealed record WorkspaceHistoricalState(
    string Format, string EnvelopeJson, string AuxiliaryJson,
    string SourceDigest, string AuxiliaryDigest, string CharacterDigest)
{
    public override string ToString() => nameof(WorkspaceHistoricalState);
}

/// <summary>Exact terminal receipt JSON includes the complete confirmed command.</summary>
public sealed record WorkspaceHistoricalObservation(
    int Version, string OriginId, string OwnerId, bool LocalSingleUser,
    string WorkspaceId, string IncarnationId, string PreviousDigest,
    long BeforeRevision, long AfterRevision, long BeforeSavedRevision, long AfterSavedRevision,
    WorkspaceHistoricalState Before, WorkspaceHistoricalState After,
    string? BeforeCaptureDigest, string? AfterCaptureDigest,
    string? MartialReceiptJson)
{
    public override string ToString() => nameof(WorkspaceHistoricalObservation);
}

public sealed record WorkspaceAuthenticatedTransition(
    WorkspaceHistoricalObservation Observation, string Signature)
{
    public override string ToString() => nameof(WorkspaceAuthenticatedTransition);
}
