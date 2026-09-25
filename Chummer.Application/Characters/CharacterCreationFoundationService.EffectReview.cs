using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationFoundationService
{
    internal CharacterCreationFoundationResult<CharacterCreationFoundationEffectCompilation> ReviewFoundationEffects(
        CharacterCreationFoundationPreviewRequest request, string expectedPreviewDigest)
    {
        var evaluated = EvaluatePreview(request);
        if (evaluated.Result.Value is not { CanConfirm: true, CanApply: true } preview
            || evaluated.Result.Blockers.Count != 0 || evaluated.Context is not { } context)
            return new(evaluated.Result.Outcome, null, evaluated.Result.Blockers);
        if (!DigestEquals(preview.PreviewDigest, expectedPreviewDigest))
            return Blocked<CharacterCreationFoundationEffectCompilation>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationFoundationBlockers.PreviewDigestMismatch);
        return CompileEffectReview(context.Workspace,
            CharacterCreationFoundationDraftApplyAuthority.BuildProposedLedger(context),
            context.Nationality, context.NationalityVersion);
    }

    internal CharacterCreationFoundationResult<CharacterCreationFoundationEffectCompilation> ReviewModuleEffects(
        CharacterCreationLifeModulePreviewRequest request, string expectedPreviewDigest)
    {
        var read = _workspaceStore.Get(request.Binding.WorkspaceId);
        if (read.Value is not { } workspace)
            return ReadFailure<CharacterCreationFoundationEffectCompilation>(read);
        var loaded = BuildJourney(workspace, request.Binding.EnabledSources, request.Binding.SourceFilterApplied);
        if (loaded.Value is not { } state || loaded.Blockers.Count != 0)
            return new(loaded.Outcome, null, loaded.Blockers);
        var evaluated = EvaluateModule(request, workspace, state).Result;
        if (evaluated.Value is not { CanConfirm: true } preview || evaluated.Blockers.Count != 0)
            return new(evaluated.Outcome, null, evaluated.Blockers);
        if (!DigestEquals(preview.PreviewDigest, expectedPreviewDigest))
            return Blocked<CharacterCreationFoundationEffectCompilation>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationFoundationBlockers.PreviewDigestMismatch);
        var module = state.Options.Single(item => item.ModuleId == preview.Entry.Selection.ModuleId);
        var version = module.Versions.SingleOrDefault(item => item.VersionId == preview.Entry.Selection.VersionId);
        return CompileEffectReview(workspace, workspace.Document.AuxiliaryState.CharacterCreationFoundationDraft!,
            module, version, preview.Entry);
    }

    private CharacterCreationFoundationResult<CharacterCreationFoundationEffectCompilation> CompileEffectReview(
        WorkspaceStoredDocument workspace, CharacterCreationFoundationDraftLedger draft,
        LifeModuleLegalOptionDto module, LifeModuleVersionProjectionDto? version,
        CharacterCreationLifeModuleDraftEntry? occurrence = null)
    {
        var context = _sourceDataResolver.TryCreateContext(workspace.Document.Content);
        if (context is null || !context.TryResolveCreationFoundationEffectSources(out var sources)
            || sources is null || !sources.TryCreateAuthorities(out var skills, out var qualities,
                out var levels, out string sourceDigest))
            return Blocked<CharacterCreationFoundationEffectCompilation>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);
        var compilation = CharacterCreationFoundationEffectCompiler.Compile(workspace.Document.RulesetId,
            draft, module, version, skills, qualities, sourceDigest, levels, occurrence);
        // A review is allowed before later stages/finalization exist. It retains
        // unsupported instruction status and never grants partial-apply authority.
        if (compilation.Blockers.Contains(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict))
            return new(CharacterCreationFoundationOutcomes.Conflict, null, compilation.Blockers);
        return new(CharacterCreationFoundationOutcomes.Success, compilation, []);
    }
}
