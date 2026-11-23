using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerFreeSpriteConversionOutcome
{
    Available, Applied, Replayed, NotFound, IdempotencyConflict,
    Conflict, Corrupt, Unavailable, Missing, UnknownOutcome, Canceled
}
public sealed record CharacterCareerFreeSpriteConversionRequest(
    CharacterWorkspaceId WorkspaceId, Guid OperationId, Guid CritterPowerId, long ExpectedWorkspaceRevision);
public sealed record CharacterCareerFreeSpriteConversionBinding(
    string OwnerId, bool LocalSingleUser, string OwnerAuthorityInstanceId, long OwnerTransitionRevision,
    CharacterWorkspaceId WorkspaceId, long WorkspaceRevision, string LocalHistoryIncarnationId,
    string SourceDigest, string AuxiliaryStateDigest, string RuleStateDigest, string RuntimeDigest);
public sealed record CharacterCareerFreeSpriteConversionCommand(
    CharacterCareerFreeSpriteConversionRequest Request, CharacterCareerFreeSpriteConversionBinding Binding,
    bool ExplicitlyConfirmed = false, string? ExpectedPreviewDigest = null);
public sealed record CharacterCareerFreeSpriteConversionQuote(
    CharacterFreeSpriteConversionState State, CharacterFreeSpriteConversionIdentity Identity,
    string CategoryAfter, string DenialName, Guid DenialSourceId, string DenialSourceBook,
    int KarmaBefore, int KarmaAfter, decimal NuyenBefore, decimal NuyenAfter,
    string DenialSourceDigest, string DenialPowerXml, IReadOnlyList<string> DenialEffectsXml);
public sealed record CharacterCareerFreeSpriteConversionPreview(
    CharacterCareerFreeSpriteConversionCommand Command, CharacterCareerFreeSpriteConversionQuote Quote, string PreviewDigest);
public sealed record CharacterCareerFreeSpriteConversionReceipt(
    CharacterCareerFreeSpriteConversionCommand Command, CharacterCareerFreeSpriteConversionQuote Quote,
    string CommandDigest, long CommittedWorkspaceRevision, string CharacterPayloadDigestAfter,
    string PreviousReceiptDigest, string ReceiptDigest);
public sealed record CharacterCareerFreeSpriteConversionPreviewResult(
    CharacterCareerFreeSpriteConversionOutcome Outcome, CharacterCareerFreeSpriteConversionPreview? Preview = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);
public sealed record CharacterCareerFreeSpriteConversionResult(
    CharacterCareerFreeSpriteConversionOutcome Outcome, CharacterCareerFreeSpriteConversionReceipt? Receipt = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);
