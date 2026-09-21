using Chummer.Contracts.LifeModules;

namespace Chummer.Contracts.Characters;

/// <summary>A source-projected choice, not applied character effects.</summary>
public sealed record CharacterCreationLifeModuleDraftEntry(
    int StageOrder,
    string StageId,
    CharacterCreationFoundationSelection Selection,
    decimal KarmaCost,
    IReadOnlyList<LifeModuleRequirementProjectionDto> RequirementEvaluations,
    IReadOnlyList<LifeModuleEffectProjectionDto> ProjectedEffects,
    IReadOnlyDictionary<string, string> FollowUpValues,
    IReadOnlyList<string> SourceAnchorIds,
    string StoryTemplate);

public sealed record CharacterCreationLifeModuleJourneyState(
    CharacterCreationFoundationBinding Binding,
    long DraftRevision,
    string DraftDigest,
    int CurrentStageOrder,
    IReadOnlyList<LifeModuleLegalOptionDto> Options,
    CharacterCreationBudgetState Budget,
    IReadOnlyList<CharacterCreationLifeModuleDraftEntry> AdditionalModules);

public sealed record CharacterCreationLifeModulePreviewRequest(
    CharacterCreationFoundationBinding Binding,
    long DraftRevision,
    string DraftDigest,
    CharacterCreationFoundationSelection Selection,
    IReadOnlyDictionary<string, string>? FollowUpValues = null);

public sealed record CharacterCreationLifeModulePreview(
    CharacterCreationLifeModulePreviewRequest Request,
    CharacterCreationLifeModuleDraftEntry Entry,
    CharacterCreationBudgetState BudgetBefore,
    CharacterCreationBudgetState BudgetAfter,
    IReadOnlyList<string> Blockers,
    bool CanConfirm,
    string PreviewDigest);

public sealed record CharacterCreationLifeModuleConfirmRequest(
    CharacterCreationLifeModulePreviewRequest Request,
    string PreviewDigest,
    bool ExplicitlyConfirmed);

public sealed record CharacterCreationLifeModuleApplyReceipt(
    CharacterCreationFoundationBinding Binding,
    long PreviousContentRevision,
    long ContentRevision,
    long SavedRevision,
    long DraftRevision,
    string DraftDigest,
    CharacterCreationLifeModuleDraftEntry Entry,
    bool CharacterEffectsApplied);
