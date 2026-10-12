namespace Chummer.Contracts.Characters;

/// <summary>
/// Explicitly keep previously receipted choices after a later Attributes edit.
/// Current remains a blocked ordinary editor state; this is not general permission
/// to repair an invalid draft or change its selections, prices or source authority.
/// </summary>
public sealed record CharacterCreationQualitiesReReviewState(
    CharacterCreationQualitiesState Current,
    string HistoricalReceiptDigest);

public sealed record CharacterCreationQualitiesReReviewConfirmRequest(
    CharacterCreationQualitiesBinding Binding,
    string HistoricalReceiptDigest,
    IReadOnlyList<string> SelectedOptionIds,
    string PreviewDigest,
    string IdempotencyKey,
    Guid TransactionId,
    bool ExplicitlyReviewedChanges);
