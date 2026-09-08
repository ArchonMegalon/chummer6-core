using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerReputationOutcome
{
    Available, Applied, Replayed, NotFound, IdempotencyConflict, Conflict, Corrupt, Unavailable, Missing
}

public sealed record CharacterCareerReputationRequest(
    CharacterWorkspaceId WorkspaceId, Guid OperationId, CharacterCareerReputationOperation Operation,
    CharacterCareerReputationAdjustment? Adjustment = null, string Reason = "");

/// <summary>Core-derived bindings. Content digests are not external signatures or GM approval.</summary>
public sealed record CharacterCareerReputationBinding(
    CharacterWorkspaceId WorkspaceId, long WorkspaceRevision, string SourceDigest,
    string AuxiliaryStateDigest, string RuleStateDigest, string SnapshotDigest, string RuntimeDigest);

/// <summary>
/// The host persists this exact command before committing. It may change only
/// ExplicitlyConfirmed after displaying the preview. Unknown results must be
/// looked up or retried with the same operation ID and command, never reissued.
/// </summary>
public sealed record CharacterCareerReputationCommand(
    CharacterCareerReputationRequest Request, CharacterCareerReputationBinding Binding,
    bool ExplicitlyConfirmed = false, string? ExpectedPreviewDigest = null);

public sealed record CharacterCareerReputationPreview(
    CharacterCareerReputationCommand Command, CharacterCareerReputationQuote Quote, string PreviewDigest);

/// <summary>
/// Compact durable evidence, not the complete character or trace graph. The
/// snapshot digest binds the original detailed projection; the quote records
/// its exact arithmetic inputs/outcomes. Later replay does not claim that these
/// outcomes are still the runner's current state.
/// </summary>
public sealed record CharacterCareerReputationReceipt(
    CharacterCareerReputationCommand Command, CharacterCareerReputationQuote Quote,
    string CommandDigest, long CommittedWorkspaceRevision, string CharacterPayloadDigestAfter,
    string PreviousReceiptDigest, string ReceiptDigest);

public sealed record CharacterCareerReputationReadResult(
    CharacterCareerReputationOutcome Outcome, CharacterCareerReputationSnapshot? Snapshot = null,
    string? Error = null);

public sealed record CharacterCareerReputationPreviewResult(
    CharacterCareerReputationOutcome Outcome, CharacterCareerReputationPreview? Preview = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);

public sealed record CharacterCareerReputationResult(
    CharacterCareerReputationOutcome Outcome, CharacterCareerReputationReceipt? Receipt = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);
