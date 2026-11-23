using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerContactsOutcome
{
    Available, Applied, Replayed, NotFound, IdempotencyConflict, Conflict,
    Corrupt, Unavailable, Missing, UnknownOutcome, Canceled
}

public enum CharacterCareerContactChangeKind { Edit, Add, Remove }
public enum CharacterCareerContactEntityType { Contact, Enemy, Pet }

public sealed record CharacterCareerContactIdentity(
    string Name = "", string Role = "", string Location = "", string Notes = "", string CustomName = "",
    string Metatype = "", string Gender = "", string Age = "", string ContactType = "",
    string PreferredPayment = "", string HobbiesVice = "", string PersonalLife = "", string GroupName = "");

/// <summary>Null fields are unchanged. Identity, when supplied, replaces the complete identity projection.</summary>
public sealed record CharacterCareerContactChange(
    Guid ContactId, CharacterCareerContactChangeKind Kind = CharacterCareerContactChangeKind.Edit,
    CharacterCareerContactIdentity? Identity = null, int? Connection = null, int? Loyalty = null,
    bool? IsGroup = null, bool? Free = null, bool? Family = null, bool? Blackmail = null,
    CharacterCareerContactEntityType? EntityType = null);

public sealed record CharacterCareerContactsRequest(
    CharacterWorkspaceId WorkspaceId, Guid OperationId, CharacterCareerContactChange Change);

/// <summary>Core content bindings; these do not authenticate an owner.</summary>
public sealed record CharacterCareerContactsBinding(
    string OwnerId, CharacterWorkspaceId WorkspaceId, long WorkspaceRevision, string SourceDigest,
    string AuxiliaryStateDigest, string RuleStateDigest, string RuntimeDigest, string TargetDigest);

/// <summary>Persist the preview command before confirmation. Only set ExplicitlyConfirmed after review.</summary>
public sealed record CharacterCareerContactsCommand(
    CharacterCareerContactsRequest Request, CharacterCareerContactsBinding Binding,
    bool ExplicitlyConfirmed = false, string? ExpectedPreviewDigest = null);

/// <summary>Saved fields, including latent loyalty/free values. Effective values come from Authority in the quote.</summary>
public sealed record CharacterCareerContactState(
    Guid ContactId, CharacterCareerContactIdentity Identity, CharacterCareerContactEntityType EntityType,
    int Connection, int Loyalty, bool IsGroup, bool Free, bool Family, bool Blackmail,
    bool ReadOnly = false, bool Linked = false, string? RemovalBlocker = null);

/// <summary>Only the target and effects relevant to it; unrelated contact details are never included.</summary>
public sealed record CharacterCareerContactsQuote(
    CharacterCareerContactState? Before, CharacterCareerContactState? After,
    CharacterContactEditSemantics AuthorityBefore, CharacterContactEditSemantics? AuthorityAfter,
    IReadOnlyList<CharacterContactRuleEffect> Effects, int KarmaDelta = 0, decimal NuyenDelta = 0);

public sealed record CharacterCareerContactsPreview(
    CharacterCareerContactsCommand Command, CharacterCareerContactsQuote Quote, string PreviewDigest);

public sealed record CharacterCareerContactsReceipt(
    CharacterCareerContactsCommand Command, CharacterCareerContactsQuote Quote, string CommandDigest,
    long CommittedWorkspaceRevision, string CharacterPayloadDigestAfter,
    string PreviousReceiptDigest, string ReceiptDigest);

public sealed record CharacterCareerContactsPreviewResult(
    CharacterCareerContactsOutcome Outcome, CharacterCareerContactsPreview? Preview = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);

/// <summary>UnknownOutcome requires lookup with the original operation and command digest; do not create a replacement operation.</summary>
public sealed record CharacterCareerContactsResult(
    CharacterCareerContactsOutcome Outcome, CharacterCareerContactsReceipt? Receipt = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);
