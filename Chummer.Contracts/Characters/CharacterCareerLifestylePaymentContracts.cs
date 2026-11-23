using Chummer.Contracts.Workspaces;
using System.Text.Json.Serialization;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerLifestylePaymentOutcome
{
    Available, Applied, Replayed, NotFound, IdempotencyConflict, Conflict,
    Corrupt, Unavailable, Missing, UnknownOutcome, Canceled
}

/// <summary>One paid interval for an existing SR5 Career lifestyle. Dates are local, unspecified, whole seconds.</summary>
public sealed record CharacterCareerLifestylePaymentRequest(
    CharacterWorkspaceId WorkspaceId, Guid OperationId, Guid LifestyleId, DateTime ExpenseDateLocal);

/// <summary>Core-produced content bindings, not external signatures or owner authentication.</summary>
public sealed record CharacterCareerLifestylePaymentBinding(
    string OwnerId, CharacterWorkspaceId WorkspaceId, long WorkspaceRevision,
    string SourceDigest, string AuxiliaryStateDigest, string RuleStateDigest, string RuntimeDigest)
{
    // Missing only on historical receipts. New payments always bind the saved incarnation.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IncarnationId { get; init; }
}

/// <summary>Persist the exact command before committing; only set ExplicitlyConfirmed after displaying the quote.</summary>
public sealed record CharacterCareerLifestylePaymentCommand(
    CharacterCareerLifestylePaymentRequest Request, CharacterCareerLifestylePaymentBinding Binding,
    bool ExplicitlyConfirmed = false, string? ExpectedPreviewDigest = null);

public sealed record CharacterCareerLifestylePaymentQuote(
    CharacterLifestyleIncrementState Inputs, CharacterLifestyleIncrementQuote Increment,
    decimal NuyenAfter, Guid ExpenseId);

public sealed record CharacterCareerLifestylePaymentPreview(
    CharacterCareerLifestylePaymentCommand Command, CharacterCareerLifestylePaymentQuote Quote, string PreviewDigest);

/// <summary>Historical completion evidence. Replays do not claim that these balances remain current.</summary>
public sealed record CharacterCareerLifestylePaymentReceipt(
    CharacterCareerLifestylePaymentCommand Command, CharacterCareerLifestylePaymentQuote Quote,
    string CommandDigest, long CommittedWorkspaceRevision, string CharacterPayloadDigestAfter,
    string PreviousReceiptDigest, string ReceiptDigest);

public sealed record CharacterCareerLifestylePaymentPreviewResult(
    CharacterCareerLifestylePaymentOutcome Outcome, CharacterCareerLifestylePaymentPreview? Preview = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);

/// <summary>UnknownOutcome requires lookup with the original identity/digest; never create a replacement operation.</summary>
public sealed record CharacterCareerLifestylePaymentResult(
    CharacterCareerLifestylePaymentOutcome Outcome, CharacterCareerLifestylePaymentReceipt? Receipt = null,
    long? CurrentWorkspaceRevision = null, string? Error = null);
