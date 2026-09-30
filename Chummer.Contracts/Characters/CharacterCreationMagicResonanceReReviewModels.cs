namespace Chummer.Contracts.Characters;

/// <summary>Explicit re-review of an unchanged-source saved Magic draft after a
/// later Attributes edit. Ordinary editing and finalization remain blocked until
/// a separately reviewed current preview is committed.</summary>
public static class CharacterCreationMagicResonanceReReviewSchemas
{
    public const string BindingV1 = "chummer.creation-magic-rereview-binding.v1";
    public const string SnapshotV1 = "chummer.creation-magic-rereview.v1";
    public const string PreviewV1 = "chummer.creation-magic-rereview-preview.v1";
    public const string CommandV1 = "chummer.creation-magic-rereview-command.v1";
    public const string Unavailable = "creation-magic-rereview-unavailable";
    public const string Stale = "creation-magic-rereview-stale";
    public const string ExplicitReviewRequired = "creation-magic-rereview-explicit-review-required";
}

public sealed record CharacterCreationMagicResonanceReReviewBinding(
    string Schema,
    CharacterCreationMagicResonanceBinding Current,
    string HistoricalDraftDigest,
    string HistoricalReceiptDigest,
    string ContextDigest);

public sealed record CharacterCreationMagicResonanceReReviewPreview(
    string Schema,
    CharacterCreationMagicResonanceReReviewBinding Binding,
    CharacterCreationMagicResonancePreview CurrentPreview,
    string PreviewDigest);

public sealed record CharacterCreationMagicResonanceReReviewState(
    string Schema,
    CharacterCreationMagicResonanceReReviewBinding Binding,
    CharacterCreationMagicResonanceDraft HistoricalDraft,
    CharacterCreationMagicResonanceState CurrentState,
    CharacterCreationMagicResonanceReReviewPreview InitialPreview,
    string SnapshotDigest);

public sealed record CharacterCreationMagicResonanceReReviewPreviewRequest(
    CharacterCreationMagicResonanceReReviewBinding Binding,
    CharacterCreationMagicResonanceSelections Selections);

public sealed record CharacterCreationMagicResonanceReReviewConfirmRequest(
    CharacterCreationMagicResonanceReReviewBinding Binding,
    CharacterCreationMagicResonanceSelections Selections,
    string PreviewDigest,
    string IdempotencyKey,
    bool ExplicitlyConfirmed,
    bool ExplicitlyReviewedChanges);
