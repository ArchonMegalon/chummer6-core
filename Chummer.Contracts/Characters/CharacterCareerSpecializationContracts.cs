using System.Text.Json.Serialization;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerSpecializationOutcome
{
    Available, Pending, Applied, Recorded, Rejected, NotFound, Conflict, Unavailable, Corrupt,
    // Original archive metadata only; never a typed workspace receipt.
    NeverApplied = 9
}

public sealed record CharacterCareerSpecializationRequest(CharacterWorkspaceId WorkspaceId, Guid OperationId,
    CharacterCareerSkillIdentity SkillIdentity, CharacterCareerSkillSpecializationSelection Selection,
    Guid SpecializationId, Guid ExpenseId, DateTime ExpenseDateLocal);

public sealed record CharacterCareerSpecializationBinding(string OwnerId, bool TrustedLocalOwner,
    CharacterWorkspaceId WorkspaceId, long ContentRevision, long SavedRevision,
    string SourceDigest, string AuxiliaryDigest, string EnvironmentDigest, string RuntimeDigest,
    string IncarnationId, string SlotGeneration)
{
    public string OwnerAuthorityInstanceId { get; init; } = "";
    public long OwnerTransitionRevision { get; init; }
}

public sealed record CharacterCareerSpecializationCatalog(CharacterCareerSpecializationBinding Binding,
    IReadOnlyList<CharacterCareerSkillSpecializationInput> Skills, int OmittedSkillCount);

public sealed record CharacterCareerSpecializationCommand(CharacterCareerSpecializationRequest Request,
    CharacterCareerSpecializationBinding Binding, CharacterCareerSkillSpecializationQuote Quote, bool ExplicitlyConfirmed = false);

/// <summary>A stored original-operation outcome. Pending is never completion evidence.</summary>
public sealed record CharacterCareerSpecializationReceipt(CharacterCareerSpecializationCommand Command,
    string CommandDigest, CharacterCareerSpecializationOutcome Outcome, long RecordedRevision,
    long? CommittedRevision, string? PayloadDigestAfter, string? Rejection, string ReceiptDigest);

public sealed record CharacterCareerSpecializationReadResult(CharacterCareerSpecializationOutcome Outcome,
    CharacterCareerSpecializationCatalog? Catalog = null, string? Error = null);
public sealed record CharacterCareerSpecializationPreviewResult(CharacterCareerSpecializationOutcome Outcome,
    CharacterCareerSpecializationCommand? Command = null, string? Error = null);
public enum CharacterCareerSpecializationOriginalLocation { LiveOriginal, Missing, DifferentIncarnation }

/// <summary>Private execution metadata only: no character, specialization, source body or display label.</summary>
public sealed record CharacterCareerSpecializationOriginalOutcome(string OwnerId, bool TrustedLocalOwner,
    CharacterWorkspaceId WorkspaceId, string IncarnationId, string SlotGeneration, Guid OperationId,
    string CommandDigest, long ExpectedRevision, CharacterCareerSpecializationOutcome Outcome,
    long RecordedRevision, long? CommittedRevision, string? ReceiptDigest);
public sealed record CharacterCareerSpecializationResult(CharacterCareerSpecializationOutcome Outcome,
    CharacterCareerSpecializationReceipt? Receipt = null, long? CurrentRevision = null, string? Error = null,
    CharacterCareerSpecializationOriginalOutcome? Original = null,
    CharacterCareerSpecializationOriginalLocation OriginalLocation = CharacterCareerSpecializationOriginalLocation.LiveOriginal);

