using System.Text.Json.Serialization;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerSpiritFetteringOutcome
{
    Available, Applied, Replayed, NeverApplied, NotFound, IdempotencyConflict,
    Conflict, Corrupt, Unavailable, Missing, UnknownOutcome, Canceled
}

public sealed record CharacterCareerSpiritFetteringRequest(
    CharacterWorkspaceId WorkspaceId, Guid OperationId, Guid SpiritId,
    bool Fettered, DateTime ExpenseDateLocal)
{
    // Null preserves the original fettering command/receipt JSON and digest.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestedName { get; init; }
}

public sealed record CharacterCareerSpiritFetteringBinding(
    string OwnerId, bool LocalSingleUser, string OwnerAuthorityInstanceId, long OwnerTransitionRevision,
    CharacterWorkspaceId WorkspaceId, long WorkspaceRevision, string LocalHistoryIncarnationId,
    string SourceDigest, string AuxiliaryStateDigest, string RuleStateDigest, string RuntimeDigest);

public sealed record CharacterCareerSpiritFetteringCommand(
    CharacterCareerSpiritFetteringRequest Request, CharacterCareerSpiritFetteringBinding Binding,
    bool ExplicitlyConfirmed = false, string? ExpectedPreviewDigest = null);

public sealed record CharacterCareerSpiritFetteringQuote(
    CharacterSpiritFetteringState State, string Name, int ProfileKarmaSpiritFettering,
    int KarmaCost, int KarmaBefore, int KarmaAfter, bool ChangesState)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetName { get; init; }
}

public sealed record CharacterCareerSpiritFetteringPreview(
    CharacterCareerSpiritFetteringCommand Command, CharacterCareerSpiritFetteringQuote Quote, string PreviewDigest);

public sealed record CharacterCareerSpiritFetteringReceipt(
    CharacterCareerSpiritFetteringCommand Command, CharacterCareerSpiritFetteringQuote? Quote,
    string CommandDigest, Guid? ExpenseId, long CommittedWorkspaceRevision,
    string CharacterPayloadDigestAfter, string PreviousReceiptDigest, string ReceiptDigest)
{
    // Missing/false remains an applied receipt. A terminal non-application has no economics.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool NeverApplied { get; init; }
}

public sealed record CharacterCareerSpiritFetteringPreviewResult(
    CharacterCareerSpiritFetteringOutcome Outcome, CharacterCareerSpiritFetteringPreview? Preview = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);

public sealed record CharacterCareerSpiritFetteringResult(
    CharacterCareerSpiritFetteringOutcome Outcome, CharacterCareerSpiritFetteringReceipt? Receipt = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);

public sealed record CharacterCareerSpiritFetteringEntry(Guid SpiritId, string Name, string EntityType, bool Fettered);
public sealed record CharacterCareerSpiritFetteringListResult(CharacterCareerSpiritFetteringOutcome Outcome,
    IReadOnlyList<CharacterCareerSpiritFetteringEntry>? Entries = null, long? CurrentWorkspaceRevision = null, string? Error = null);
