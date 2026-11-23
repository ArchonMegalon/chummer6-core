using System.Text.Json.Serialization;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerMartialAcquisitionKind { Art, Technique }
public enum CharacterCareerMartialAcquisitionOutcome
{
    Available, Applied, Replayed, NeverApplied, NotFound, IdempotencyConflict,
    Conflict, Corrupt, Unavailable, Missing, UnknownOutcome, Canceled
}
public sealed record CharacterCareerMartialAcquisitionCatalogBinding(
    string OwnerId, bool LocalSingleUser, string OwnerAuthorityInstanceId, long OwnerTransitionRevision,
    CharacterWorkspaceId WorkspaceId, long WorkspaceRevision, string LocalHistoryIncarnationId,
    string SourceDigest, string AuxiliaryStateDigest, string CatalogDigest, string RuntimeDigest);
public sealed record CharacterCareerMartialAcquisitionRequest(
    CharacterWorkspaceId WorkspaceId, Guid OperationId, Guid SourceId,
    CharacterCareerMartialAcquisitionKind Kind, Guid? ParentArtId, DateTime ExpenseDateLocal,
    CharacterCareerMartialAcquisitionCatalogBinding? CatalogBinding = null);
public sealed record CharacterCareerMartialAcquisitionBinding(
    string OwnerId, bool LocalSingleUser, string OwnerAuthorityInstanceId, long OwnerTransitionRevision,
    CharacterWorkspaceId WorkspaceId, long WorkspaceRevision, string LocalHistoryIncarnationId,
    string SourceDigest, string AuxiliaryStateDigest, string RuleStateDigest, string RuntimeDigest);
public sealed record CharacterCareerMartialAcquisitionCommand(
    CharacterCareerMartialAcquisitionRequest Request, CharacterCareerMartialAcquisitionBinding Binding,
    bool ExplicitlyConfirmed = false, string? ExpectedPreviewDigest = null);
public sealed record CharacterCareerMartialAcquisitionEffect(
    string Type, string SkillKey, Guid? SkillId, string Specialization, int Value, Guid? SpecializationId = null);
public sealed record CharacterCareerMartialAcquisitionQuote(
    Guid SourceId, CharacterCareerMartialAcquisitionKind Kind, Guid? ParentArtId,
    string Name, string ParentName, string Book, string Page, int KarmaCost, int KarmaBefore, int KarmaAfter,
    int TechniqueRate, bool FirstTechnique, bool FreeMartialArtSpecialization,
    string SourceXml, string ParentSourceXml, IReadOnlyList<CharacterCareerMartialAcquisitionEffect> Effects);
public sealed record CharacterCareerMartialAcquisitionPreview(
    CharacterCareerMartialAcquisitionCommand Command, CharacterCareerMartialAcquisitionQuote Quote, string PreviewDigest);
public sealed record CharacterCareerMartialAcquisitionReceipt(
    CharacterCareerMartialAcquisitionCommand Command, CharacterCareerMartialAcquisitionQuote? Quote,
    string CommandDigest, Guid? ExpenseId, long CommittedWorkspaceRevision, string CharacterPayloadDigestAfter,
    string PreviousReceiptDigest, string ReceiptDigest,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool NeverApplied = false);
public sealed record CharacterCareerMartialAcquisitionPreviewResult(
    CharacterCareerMartialAcquisitionOutcome Outcome, CharacterCareerMartialAcquisitionPreview? Preview = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);
public sealed record CharacterCareerMartialAcquisitionResult(
    CharacterCareerMartialAcquisitionOutcome Outcome, CharacterCareerMartialAcquisitionReceipt? Receipt = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);
public sealed record CharacterCareerMartialAcquisitionEntry(
    Guid SourceId, CharacterCareerMartialAcquisitionKind Kind, Guid? ParentArtId,
    string Name, string ParentName, string Book, string Page, int? KarmaCost,
    bool IsAvailable, IReadOnlyList<string> Blockers, CharacterCareerMartialAcquisitionCatalogBinding CatalogBinding);
public sealed record CharacterCareerMartialAcquisitionListResult(
    CharacterCareerMartialAcquisitionOutcome Outcome, IReadOnlyList<CharacterCareerMartialAcquisitionEntry>? Entries = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);
