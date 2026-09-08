namespace Chummer.Contracts.Characters;

/// <summary>Explicit re-review of a saved pre-TalentAccess Skills draft. Historical
/// choices are not current legality; only the current preview may be confirmed.</summary>
public static class CharacterCreationSkillsReReviewSchemas
{
    public const string BindingV1 = "chummer.creation-skills-rereview-binding.v1";
    public const string SnapshotV1 = "chummer.creation-skills-rereview.v1";
    public const string PreviewV1 = "chummer.creation-skills-rereview-preview.v1";
    public const string CommandV1 = "chummer.creation-skills-rereview-command.v1";
    public const string Unavailable = "creation-skills-rereview-unavailable";
    public const string Stale = "creation-skills-rereview-stale";
    public const string ExplicitReviewRequired = "creation-skills-rereview-explicit-review-required";
}

public sealed record CharacterCreationSkillsReReviewBinding(
    string Schema,
    CharacterCreationSkillsBinding Current,
    string HistoricalDraftDigest,
    string HistoricalReceiptDigest,
    string ContextDigest);

public sealed record CharacterCreationSkillsReReviewChange(
    string Kind,
    string SourceId,
    string Name,
    int? HistoricalRating,
    int? CandidateRating,
    int HistoricalPointCost,
    int CandidatePointCost,
    bool HistoricalNativeLanguage,
    bool CandidateNativeLanguage,
    bool Removed,
    IReadOnlyList<string> Blockers)
{
    public string? HistoricalSpecializationOptionId { get; init; }
    public string? CandidateSpecializationOptionId { get; init; }
    public IReadOnlyList<string> SourceAnchorIds { get; init; } = [];
}

public sealed record CharacterCreationSkillsReReviewPreview(
    string Schema,
    CharacterCreationSkillsReReviewBinding Binding,
    CharacterCreationSkillsPreview CurrentPreview,
    IReadOnlyList<CharacterCreationSkillsReReviewChange> Changes,
    string PreviewDigest);

public sealed record CharacterCreationSkillsReReviewState(
    string Schema,
    CharacterCreationSkillsReReviewBinding Binding,
    CharacterCreationSkillsDraft HistoricalDraft,
    CharacterCreationSkillsState CurrentState,
    CharacterCreationSkillsReReviewPreview InitialPreview,
    string SnapshotDigest);

public sealed record CharacterCreationSkillsReReviewPreviewRequest(
    CharacterCreationSkillsReReviewBinding Binding,
    IReadOnlyList<CharacterCreationSkillAllocation> Allocations,
    IReadOnlyList<CharacterCreationSkillGroupAllocation> GroupAllocations);

public sealed record CharacterCreationSkillsReReviewConfirmRequest(
    CharacterCreationSkillsReReviewBinding Binding,
    IReadOnlyList<CharacterCreationSkillAllocation> Allocations,
    IReadOnlyList<CharacterCreationSkillGroupAllocation> GroupAllocations,
    string PreviewDigest,
    string IdempotencyKey,
    bool ExplicitlyConfirmed,
    bool ExplicitlyReviewedChanges);
