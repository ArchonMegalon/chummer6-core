using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.Characters;

public interface ICharacterCreationFoundationService
{
    CharacterCreationFoundationResult<CharacterCreationFoundationState> Load(
        CharacterCreationFoundationLoadRequest request);

    CharacterCreationFoundationResult<CharacterCreationFoundationPreview> Preview(
        CharacterCreationFoundationPreviewRequest request);

    CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> Confirm(
        CharacterCreationFoundationConfirmRequest request);
}

public sealed record CharacterCreationFoundationAuthorityContext(
    WorkspaceStoredDocument Workspace,
    CharacterFileSummary Summary,
    string RequestedMetatype,
    CharacterCreationFoundationSelection Selection,
    LifeModuleLegalOptionDto Nationality,
    LifeModuleVersionProjectionDto? NationalityVersion,
    IReadOnlyList<LifeModuleRequirementProjectionDto> RequirementEvaluations,
    IReadOnlyDictionary<string, string> FollowUpValues,
    string SourceDigest);

public sealed record CharacterCreationFoundationAuthorityPreview(
    IReadOnlyList<CharacterCreationFoundationDiffEntry> Diff,
    IReadOnlyList<string> Blockers,
    bool CanApply,
    string AuthorityPlanDigest);

/// <summary>
/// The only authority allowed to persist a confirmed foundation preview.  During
/// creation it must atomically append/replace the typed draft ledger and advance
/// the workspace revision plus saved checkpoint in one durable compare-and-swap
/// transaction, while leaving canonical character effect XML untouched.  A later
/// finalization authority compiles the complete ledger through Chummer5 rules.
/// A document replacement followed by a separate ledger/checkpoint write is not
/// an implementation of this interface.
/// </summary>
public interface ICharacterCreationFoundationApplyAuthority
{
    CharacterCreationFoundationAuthorityPreview Preview(
        CharacterCreationFoundationAuthorityContext context);

    CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> ApplyAndCheckpoint(
        CharacterCreationFoundationAuthorityContext context,
        string previewDigest);
}
