using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.LifeModules;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterCreationFoundationService : ICharacterCreationFoundationService
{
    private const string NationalityStageName = "Nationality";

    private readonly IWorkspaceStore _workspaceStore;
    private readonly ICharacterFileQueries _characterFileQueries;
    private readonly ICharacterSourceDataResolver _sourceDataResolver;
    private readonly ILifeModulesCatalogService _lifeModulesCatalog;
    private readonly ICharacterCreationFoundationApplyAuthority _applyAuthority;

    public CharacterCreationFoundationService(
        IWorkspaceStore workspaceStore,
        ICharacterFileQueries characterFileQueries,
        ICharacterSourceDataResolver sourceDataResolver,
        ILifeModulesCatalogService lifeModulesCatalog,
        ICharacterCreationFoundationApplyAuthority applyAuthority)
    {
        _workspaceStore = workspaceStore;
        _characterFileQueries = characterFileQueries;
        _sourceDataResolver = sourceDataResolver;
        _lifeModulesCatalog = lifeModulesCatalog;
        _applyAuthority = applyAuthority;
    }

    public CharacterCreationFoundationResult<CharacterCreationFoundationState> Load(
        CharacterCreationFoundationLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
            return ReadFailure<CharacterCreationFoundationState>(read);

        return BuildState(
            workspace,
            request.EnabledSources,
            sourceFilterApplied: request.EnabledSources is not null);
    }

    public CharacterCreationFoundationResult<CharacterCreationFoundationPreview> Preview(
        CharacterCreationFoundationPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        PreviewEvaluation evaluation = EvaluatePreview(request);
        return evaluation.Result;
    }

    public CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> Confirm(
        CharacterCreationFoundationConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ExplicitlyConfirmed)
        {
            return Blocked<CharacterCreationFoundationApplyReceipt>(
                CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationFoundationBlockers.ExplicitConfirmationRequired);
        }

        PreviewEvaluation evaluation = EvaluatePreview(new CharacterCreationFoundationPreviewRequest(
            Binding: request.Binding,
            RequestedMetatype: request.RequestedMetatype,
            Selection: request.Selection,
            FollowUpValues: request.FollowUpValues));
        if (evaluation.Result.Value is not CharacterCreationFoundationPreview preview
            || evaluation.Context is not CharacterCreationFoundationAuthorityContext context)
        {
            return new CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt>(
                Outcome: evaluation.Result.Outcome,
                Value: null,
                Blockers: evaluation.Result.Blockers);
        }

        if (!DigestEquals(preview.PreviewDigest, request.PreviewDigest))
        {
            return Blocked<CharacterCreationFoundationApplyReceipt>(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationFoundationBlockers.PreviewDigestMismatch);
        }

        if (!preview.CanConfirm || !preview.CanApply || preview.AuthorityBlockers.Count > 0)
        {
            return new CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt>(
                Outcome: CharacterCreationFoundationOutcomes.Blocked,
                Value: null,
                Blockers: preview.AuthorityBlockers);
        }

        context = context with
        {
            OriginDecisionCommand = request.OriginDecisionCommand,
            OriginDecisionStep = request.OriginDecisionStep
        };
        return _applyAuthority.ApplyAndCheckpoint(context, preview.PreviewDigest);
    }

    public CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationPreview>
        PreviewFinalization(CharacterCreationFoundationFinalizationPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return EvaluateFinalization(request);
    }

    public CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt>
        ConfirmFinalization(CharacterCreationFoundationFinalizationConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ExplicitlyConfirmed)
        {
            return Blocked<CharacterCreationFoundationFinalizationReceipt>(
                CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationFoundationBlockers.ExplicitConfirmationRequired);
        }

        CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationPreview>
            evaluation = EvaluateFinalization(
                new CharacterCreationFoundationFinalizationPreviewRequest(
                    request.Binding,
                    request.DraftRevision,
                    request.DraftDigest));
        if (evaluation.Value is not CharacterCreationFoundationFinalizationPreview preview)
        {
            return new CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt>(
                evaluation.Outcome,
                null,
                evaluation.Blockers);
        }
        if (!DigestEquals(preview.PreviewDigest, request.PreviewDigest))
        {
            return Blocked<CharacterCreationFoundationFinalizationReceipt>(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch);
        }
        if (!preview.CanConfirm
            || !preview.CanApply
            || !preview.Compilation.IsCompleteLedgerSupported
            || preview.FinalizationBlocked.Count > 0)
        {
            return new CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt>(
                CharacterCreationFoundationOutcomes.Blocked,
                null,
                preview.FinalizationBlocked);
        }

        // Attributelevel has an isolated, deterministic Quality/Improvement
        // write plan, but Foundation v1 still lacks every required creation
        // stage and the full resource/final-validity transaction. This is the
        // final fail-closed guard: no supported subgraph is ever applied early.
        return Blocked<CharacterCreationFoundationFinalizationReceipt>(
            CharacterCreationFoundationOutcomes.Blocked,
            CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);
    }

    private CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationPreview>
        EvaluateFinalization(CharacterCreationFoundationFinalizationPreviewRequest request)
    {
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.Binding.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
            return ReadFailure<CharacterCreationFoundationFinalizationPreview>(read);
        if (workspace.ContentRevision != request.Binding.ContentRevision
            || workspace.SavedRevision != request.Binding.SavedRevision)
        {
            return Blocked<CharacterCreationFoundationFinalizationPreview>(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationFoundationBlockers.StaleWorkspaceRevision);
        }

        CharacterCreationFoundationResult<CharacterCreationFoundationState> stateResult =
            BuildState(
                workspace,
                request.Binding.SourceFilterApplied
                    ? request.Binding.EnabledSources
                    : null,
                request.Binding.SourceFilterApplied);
        if (stateResult.Value is not CharacterCreationFoundationState state)
        {
            return new CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationPreview>(
                stateResult.Outcome,
                null,
                stateResult.Blockers);
        }
        if (!DigestEquals(
                state.Binding.RawCharacterXmlDigest,
                request.Binding.RawCharacterXmlDigest)
            || !string.Equals(
                request.Binding.CharacterDigestSemantics,
                CharacterCreationFoundationDigestSemantics.RawCharacterXmlSha256,
                StringComparison.Ordinal))
        {
            return Blocked<CharacterCreationFoundationFinalizationPreview>(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationFoundationBlockers.StaleRawCharacterXmlDigest);
        }
        if (!DigestEquals(state.Binding.SourceDigest, request.Binding.SourceDigest)
            || !string.Equals(
                request.Binding.SourceDigestSemantics,
                CharacterCreationFoundationDigestSemantics.RawSourceInputsSha256,
                StringComparison.Ordinal)
            || !state.Binding.EnabledSources.SequenceEqual(
                request.Binding.EnabledSources,
                StringComparer.Ordinal))
        {
            return Blocked<CharacterCreationFoundationFinalizationPreview>(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationFoundationBlockers.SourceDigestConflict);
        }

        CharacterCreationFoundationDraftLedger? draft = state.PendingDraft;
        if (draft is null)
        {
            return Blocked<CharacterCreationFoundationFinalizationPreview>(
                CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationFoundationBlockers.PendingDraftInvalid);
        }
        if (draft.DraftRevision != request.DraftRevision)
        {
            return Blocked<CharacterCreationFoundationFinalizationPreview>(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationFoundationBlockers.FinalizationDraftRevisionConflict);
        }
        if (!DigestEquals(draft.DraftDigest, request.DraftDigest))
        {
            return Blocked<CharacterCreationFoundationFinalizationPreview>(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationFoundationBlockers.FinalizationDraftDigestConflict);
        }

        LifeModuleLegalOptionDto? module = state.NationalityOptions.FirstOrDefault(option =>
            string.Equals(option.ModuleId, draft.Selection.ModuleId, StringComparison.Ordinal));
        LifeModuleVersionProjectionDto? version = module?.Versions.FirstOrDefault(item =>
            string.Equals(item.VersionId, draft.Selection.VersionId, StringComparison.Ordinal));
        bool versionMatches = module is not null
                              && (module.Versions.Count == 0
                                  ? string.IsNullOrWhiteSpace(draft.Selection.VersionId)
                                  : version is not null);
        if (module is null || !versionMatches)
        {
            return Blocked<CharacterCreationFoundationFinalizationPreview>(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
        }

        CharacterCreationFoundationEffectCompilation compilation =
            CharacterCreationFoundationEffectCompiler.Compile(
                workspace.Document.RulesetId,
                draft,
                module,
                version);
        string[] blockers = state.AuthorityBlockers
            .Concat(compilation.Blockers)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        bool canApply = !state.CharacterCreated
                        && compilation.IsCompleteLedgerSupported
                        && blockers.Length == 0;
        var preview = new CharacterCreationFoundationFinalizationPreview(
            Schema: CharacterCreationFoundationSchemas.FinalizationPreviewV1,
            Binding: state.Binding,
            Compilation: compilation,
            FinalizationBlocked: blockers,
            RequiresExplicitConfirmation: true,
            CanConfirm: canApply,
            CanApply: canApply,
            CharacterEffectsApplied: false,
            CharacterCreated: false,
            PreviewDigest: string.Empty);
        preview = preview with
        {
            PreviewDigest = CharacterCreationFoundationDraftLedgerIntegrity
                .ComputeCanonicalDigest(preview with { PreviewDigest = string.Empty })
        };
        return new CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationPreview>(
            blockers.Length == 0
                ? CharacterCreationFoundationOutcomes.Success
                : CharacterCreationFoundationOutcomes.Blocked,
            preview,
            blockers);
    }

    private PreviewEvaluation EvaluatePreview(CharacterCreationFoundationPreviewRequest request)
    {
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.Binding.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
        {
            CharacterCreationFoundationResult<CharacterCreationFoundationPreview> failure =
                ReadFailure<CharacterCreationFoundationPreview>(read);
            return new PreviewEvaluation(failure, null);
        }

        if (workspace.ContentRevision != request.Binding.ContentRevision
            || workspace.SavedRevision != request.Binding.SavedRevision)
        {
            return new PreviewEvaluation(
                Blocked<CharacterCreationFoundationPreview>(
                    CharacterCreationFoundationOutcomes.Conflict,
                    CharacterCreationFoundationBlockers.StaleWorkspaceRevision),
                null);
        }

        CharacterCreationFoundationResult<CharacterCreationFoundationState> stateResult =
            BuildState(
                workspace,
                request.Binding.EnabledSources,
                request.Binding.SourceFilterApplied);
        if (stateResult.Value is not CharacterCreationFoundationState state)
        {
            return new PreviewEvaluation(
                new CharacterCreationFoundationResult<CharacterCreationFoundationPreview>(
                    Outcome: stateResult.Outcome,
                    Value: null,
                    Blockers: stateResult.Blockers),
                null);
        }

        if (!DigestEquals(
                state.Binding.RawCharacterXmlDigest,
                request.Binding.RawCharacterXmlDigest)
            || !string.Equals(
                request.Binding.CharacterDigestSemantics,
                CharacterCreationFoundationDigestSemantics.RawCharacterXmlSha256,
                StringComparison.Ordinal))
        {
            return new PreviewEvaluation(
                Blocked<CharacterCreationFoundationPreview>(
                    CharacterCreationFoundationOutcomes.Conflict,
                    CharacterCreationFoundationBlockers.StaleRawCharacterXmlDigest),
                null);
        }

        if (!DigestEquals(state.Binding.SourceDigest, request.Binding.SourceDigest)
            || !string.Equals(
                request.Binding.SourceDigestSemantics,
                CharacterCreationFoundationDigestSemantics.RawSourceInputsSha256,
                StringComparison.Ordinal))
        {
            return new PreviewEvaluation(
                Blocked<CharacterCreationFoundationPreview>(
                    CharacterCreationFoundationOutcomes.Conflict,
                    CharacterCreationFoundationBlockers.SourceDigestConflict),
                null);
        }

        var selectionBlockers = new List<string>();
        CharacterCreationLegalOption? selectedMetatype = ResolveMetatypeOption(
            state.MetatypeOptions,
            request.RequestedMetatype,
            selectionBlockers);
        if (selectedMetatype is null)
        {
            string[] unresolvedMetatypeBlockers = state.AuthorityBlockers
                .Concat(selectionBlockers)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
            return new PreviewEvaluation(
                new CharacterCreationFoundationResult<CharacterCreationFoundationPreview>(
                    Outcome: CharacterCreationFoundationOutcomes.Blocked,
                    Value: null,
                    Blockers: unresolvedMetatypeBlockers),
                null);
        }

        CharacterFileSummary summary = _characterFileQueries.ParseSummary(
            new CharacterDocument(workspace.Document.Content));
        LifeModuleLegalOptionDto? nationality = state.NationalityOptions.FirstOrDefault(option =>
            string.Equals(option.ModuleId, request.Selection.ModuleId, StringComparison.Ordinal));
        if (nationality is null)
        {
            return new PreviewEvaluation(
                Blocked<CharacterCreationFoundationPreview>(
                    CharacterCreationFoundationOutcomes.Invalid,
                    CharacterCreationFoundationBlockers.NationalityModuleNotFound),
                null);
        }

        string requestedMetatype = selectedMetatype.Label;
        LifeModuleVersionProjectionDto? version = ResolveVersion(
            nationality,
            request.Selection.VersionId,
            selectionBlockers);
        LifeModuleRequirementProjectionDto[] requirementEvaluations = nationality.Requirements
            .Concat(version?.Requirements ?? [])
            .Select(requirement => EvaluateRequirement(requirement, requestedMetatype))
            .ToArray();
        selectionBlockers.AddRange(requirementEvaluations
            .Where(requirement => !requirement.IsMet)
            .Select(requirement => requirement.DisableReasonKey
                                   ?? CharacterCreationFoundationBlockers.CharacterEligibilityAuthorityRequired));
        selectionBlockers.AddRange(nationality.AuthorityBlockers.Where(blocker =>
            !string.Equals(
                blocker,
                XmlCatalogCharacterAuthorityBlocker(),
                StringComparison.Ordinal)));
        if (version is not null)
        {
            selectionBlockers.AddRange(version.AuthorityBlockers.Where(blocker =>
                !string.Equals(
                    blocker,
                    XmlCatalogCharacterAuthorityBlocker(),
                    StringComparison.Ordinal)));
        }

        IReadOnlyDictionary<string, string> followUpValues = NormalizeFollowUpValues(
            request.FollowUpValues);
        ValidateFollowUps(
            nationality.FollowUps.Concat(version?.FollowUps ?? []).ToArray(),
            followUpValues,
            selectionBlockers);

        bool selectionKarmaIsExact = version?.KarmaIsExact ?? nationality.KarmaIsExact;
        decimal selectionKarma = version?.KarmaCost ?? nationality.KarmaCost;
        var selectionCost = new CharacterCreationChoiceCost(
            BudgetId: CharacterCreationBudgetIds.LifeModules,
            Delta: selectionKarma,
            Unit: "karma");
        CharacterCreationBudgetState budgetAfter = ProjectBudgetAfterSelection(
            state.LifeModuleBudget,
            selectedMetatype,
            selectionCost,
            selectionKarmaIsExact);
        selectionBlockers.AddRange(budgetAfter.Blockers);

        LifeModuleRequirementProjectionDto[] evaluatedModuleRequirements =
            requirementEvaluations.Take(nationality.Requirements.Count).ToArray();
        LifeModuleRequirementProjectionDto[] evaluatedVersionRequirements =
            requirementEvaluations.Skip(nationality.Requirements.Count).ToArray();
        LifeModuleVersionProjectionDto? evaluatedVersion = version is null
            ? null
            : version with
            {
                IsEnabled = evaluatedVersionRequirements.All(requirement => requirement.IsMet),
                Requirements = evaluatedVersionRequirements,
                AuthorityBlockers = version.AuthorityBlockers
                    .Where(blocker => !string.Equals(
                        blocker,
                        XmlCatalogCharacterAuthorityBlocker(),
                        StringComparison.Ordinal))
                    .ToArray()
            };
        LifeModuleLegalOptionDto evaluatedNationality = nationality with
        {
            IsEnabled = evaluatedModuleRequirements.All(requirement => requirement.IsMet)
                        && (nationality.Versions.Count == 0 || evaluatedVersion?.IsEnabled == true),
            Requirements = evaluatedModuleRequirements,
            AuthorityBlockers = nationality.AuthorityBlockers
                .Where(blocker => !string.Equals(
                    blocker,
                    XmlCatalogCharacterAuthorityBlocker(),
                    StringComparison.Ordinal))
                .ToArray()
        };

        var context = new CharacterCreationFoundationAuthorityContext(
            Workspace: workspace,
            Summary: summary,
            RequestedMetatype: requestedMetatype,
            SelectedMetatype: selectedMetatype,
            Selection: request.Selection,
            Nationality: evaluatedNationality,
            NationalityVersion: evaluatedVersion,
            RequirementEvaluations: requirementEvaluations,
            FollowUpValues: followUpValues,
            LifeModuleBudgetBefore: state.LifeModuleBudget,
            SelectionCost: selectionCost,
            LifeModuleBudgetAfter: budgetAfter,
            SourceDigest: state.Binding.SourceDigest);
        CharacterCreationFoundationAuthorityPreview authorityPreview = _applyAuthority.Preview(context);

        string[] blockers = state.AuthorityBlockers
            .Where(blocker => blocker is not CharacterCreationFoundationBlockers.WizardStatePersistenceAuthorityRequired)
            .Concat(selectionBlockers)
            .Concat(authorityPreview.Blockers)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        bool canApply = blockers.Length == 0 && authorityPreview.CanApply;
        string previewDigest = Digest(new
        {
            Schema = CharacterCreationFoundationSchemas.PreviewV1,
            Binding = state.Binding,
            RequestedMetatype = context.RequestedMetatype,
            SelectedMetatype = context.SelectedMetatype,
            request.Selection,
            NationalityId = evaluatedNationality.ModuleId,
            VersionId = evaluatedVersion?.VersionId,
            Requirements = requirementEvaluations,
            FollowUpValues = followUpValues,
            BudgetBefore = state.LifeModuleBudget,
            SelectionCost = selectionCost,
            BudgetAfter = budgetAfter,
            authorityPreview.Diff,
            Blockers = blockers,
            authorityPreview.AuthorityPlanDigest
        });
        var preview = new CharacterCreationFoundationPreview(
            Schema: CharacterCreationFoundationSchemas.PreviewV1,
            Binding: state.Binding,
            RequestedMetatype: context.RequestedMetatype,
            Selection: request.Selection,
            Nationality: evaluatedNationality,
            NationalityVersion: evaluatedVersion,
            RequirementEvaluations: requirementEvaluations,
            FollowUpValues: followUpValues,
            LifeModuleBudgetBefore: state.LifeModuleBudget,
            SelectionCost: selectionCost,
            LifeModuleBudgetAfter: budgetAfter,
            Diff: authorityPreview.Diff,
            AuthorityBlockers: blockers,
            RequiresExplicitConfirmation: true,
            CanConfirm: canApply,
            CanApply: canApply,
            CharacterEffectsApplied: false,
            PreviewDigest: previewDigest);
        return new PreviewEvaluation(
            new CharacterCreationFoundationResult<CharacterCreationFoundationPreview>(
                Outcome: blockers.Length == 0
                    ? CharacterCreationFoundationOutcomes.Success
                    : CharacterCreationFoundationOutcomes.Blocked,
                Value: preview,
                Blockers: blockers),
            context);
    }

    private CharacterCreationFoundationResult<CharacterCreationFoundationState> BuildState(
        WorkspaceStoredDocument workspace,
        IReadOnlyCollection<string>? requestedSources,
        bool sourceFilterApplied)
    {
        CharacterDocument characterDocument = new(workspace.Document.Content);
        CharacterValidationResult validation;
        CharacterFileSummary summary;
        try
        {
            validation = _characterFileQueries.Validate(characterDocument);
            summary = _characterFileQueries.ParseSummary(characterDocument);
        }
        catch (Exception ex) when (ex is ArgumentException
            or FormatException
            or InvalidDataException
            or InvalidOperationException)
        {
            return Blocked<CharacterCreationFoundationState>(
                CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationFoundationBlockers.CharacterDocumentInvalid);
        }

        bool hasBootstrapState = CharacterCreationBootstrapAuthority.HasBootstrapState(
            workspace.Document);
        bool bootstrapValid = hasBootstrapState
                              && CharacterCreationBootstrapAuthority.TryValidatePending(
                                  workspace,
                                  _sourceDataResolver,
                                  out _);
        if (hasBootstrapState ? !bootstrapValid : !validation.IsValid)
        {
            return Blocked<CharacterCreationFoundationState>(
                CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationFoundationBlockers.CharacterDocumentInvalid);
        }

        var blockers = new List<string>();
        if (_applyAuthority is not ICharacterCreationFoundationDraftPersistenceCapability
            {
                CanPersistFoundationDrafts: true
            })
        {
            blockers.Add(
                CharacterCreationFoundationBlockers.WizardStatePersistenceAuthorityRequired);
        }
        CharacterCreationSourceProfileAuthority sourceProfile =
            CharacterCreationSourceProfileAuthority.Unavailable;
        ICharacterSourceDataContext? sourceContext =
            _sourceDataResolver.TryCreateContext(workspace.Document.Content);
        bool hasSourceProfileAuthority = sourceContext is not null
            && sourceContext.TryResolveCreationSourceProfile(out sourceProfile)
            && !string.IsNullOrWhiteSpace(sourceProfile.RawProfileInputsDigest);
        if (!hasSourceProfileAuthority)
            blockers.Add(CharacterCreationFoundationBlockers.EnabledSourceAuthorityRequired);

        string[] authoritativeSources = hasSourceProfileAuthority
            ? NormalizeSources(sourceProfile.EnabledSourcebooks)
            : [];
        CharacterCreationMetatypeCatalogAuthority metatypeAuthority =
            CharacterCreationMetatypeCatalogAuthority.Unavailable;
        bool metatypeAuthorityResolved = sourceContext is not null
            && sourceContext.TryResolveCreationMetatypeCatalog(out metatypeAuthority);
        CharacterCreationLegalOption[] metatypeOptions = [];
        bool hasMetatypeAuthority = metatypeAuthorityResolved
            && TryMapMetatypeOptions(
                metatypeAuthority,
                sourceProfile,
                hasSourceProfileAuthority,
                authoritativeSources,
                out metatypeOptions);
        if (!hasMetatypeAuthority)
        {
            blockers.Add(CharacterCreationFoundationBlockers.MetatypeCatalogAuthorityRequired);
            blockers.AddRange(metatypeAuthority.Blockers);
            blockers.AddRange(metatypeAuthority.SourceContext.Blockers);
            metatypeOptions = [];
        }
        string[] normalizedRequestSources = NormalizeSources(requestedSources);
        string[] effectiveSources = sourceFilterApplied
            ? authoritativeSources
                .Intersect(normalizedRequestSources, StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray()
            : authoritativeSources;
        if (hasMetatypeAuthority && sourceFilterApplied)
        {
            metatypeOptions = metatypeOptions
                .Where(option => effectiveSources.Contains(
                    option.SourceId,
                    StringComparer.OrdinalIgnoreCase))
                .ToArray();
        }

        LifeModuleCatalogAuthorityDto? catalogAuthority = null;
        IReadOnlyList<LifeModuleLegalOptionDto> nationalities = [];
        try
        {
            catalogAuthority = _lifeModulesCatalog.GetAuthority();
            if (hasSourceProfileAuthority
                && !string.IsNullOrWhiteSpace(catalogAuthority.RawXmlDigest))
            {
                // An empty list is an authoritative "no books" filter. Null is
                // never passed here because it would expose every source.
                nationalities = _lifeModulesCatalog.GetOptionProjections(
                    NationalityStageName,
                    effectiveSources);
            }
            else if (string.IsNullOrWhiteSpace(catalogAuthority.RawXmlDigest))
            {
                blockers.Add(CharacterCreationFoundationBlockers.LifeModuleCatalogAuthorityRequired);
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException)
        {
            blockers.Add(CharacterCreationFoundationBlockers.LifeModuleCatalogAuthorityRequired);
        }

        string sourceDigest = Digest(new
        {
            Semantics = CharacterCreationFoundationDigestSemantics.RawSourceInputsSha256,
            LifeModulesRawXmlDigest = catalogAuthority?.RawXmlDigest ?? string.Empty,
            SettingsProfileRawInputsDigest = hasSourceProfileAuthority
                ? sourceProfile.RawProfileInputsDigest
                : string.Empty,
            SettingsProfileId = hasSourceProfileAuthority
                ? sourceProfile.SettingsProfileId
                : string.Empty,
            EnabledSources = effectiveSources,
            MetatypeAuthority = new
            {
                metatypeAuthority.Schema,
                metatypeAuthority.IsAuthoritative,
                metatypeAuthority.SourceContext.AuthorityDigest,
                metatypeAuthority.SourceContext.RawMetatypesXmlDigest,
                metatypeAuthority.SourceContext.EffectiveMetatypesInputsDigest,
                metatypeAuthority.SourceContext.RawProfileInputsDigest,
                metatypeAuthority.SourceContext.SelectedCustomDataInputsDigest,
                metatypeAuthority.SourceContext.SettingsProfileId,
                metatypeAuthority.SourceContext.EnabledSourcebooks,
                SourceContextBlockers = metatypeAuthority.SourceContext.Blockers,
                CatalogBlockers = metatypeAuthority.Blockers
            }
        });
        string rawCharacterXmlDigest = DigestRawXml(workspace.Document.Content);
        var binding = new CharacterCreationFoundationBinding(
            WorkspaceId: workspace.Id,
            ContentRevision: workspace.ContentRevision,
            SavedRevision: workspace.SavedRevision,
            RawCharacterXmlDigest: rawCharacterXmlDigest,
            CharacterDigestSemantics: CharacterCreationFoundationDigestSemantics.RawCharacterXmlSha256,
            SourceDigest: sourceDigest,
            SourceDigestSemantics: CharacterCreationFoundationDigestSemantics.RawSourceInputsSha256,
            SourceFilterApplied: sourceFilterApplied,
            EnabledSources: effectiveSources);

        CharacterCreationFoundationDraftLedger? pendingDraft = null;
        CharacterCreationFoundationDraftLedger? persistedDraft = workspace.Document
            .AuxiliaryState.CharacterCreationFoundationDraft;
        if (persistedDraft is not null)
        {
            if (CharacterCreationFoundationDraftLedgerIntegrity.IsValidPending(
                    persistedDraft,
                    workspace.Id,
                    workspace.ContentRevision,
                    rawCharacterXmlDigest,
                    sourceDigest))
            {
                pendingDraft = persistedDraft;
            }
            else
            {
                blockers.Add(CharacterCreationFoundationBlockers.PendingDraftInvalid);
            }
        }
        bool hasExistingLifeModuleQuality = HasExistingLifeModuleQuality(
            workspace.Document.Content);
        CharacterCreationBudgetState lifeModuleBudget = BuildCurrentLifeModuleBudget(
            sourceProfile,
            hasSourceProfileAuthority,
            hasExistingLifeModuleQuality,
            pendingDraft,
            nationalities,
            metatypeOptions);
        blockers.AddRange(lifeModuleBudget.Blockers);
        if (!string.Equals(
                RulesetDefaults.NormalizeOptional(workspace.Document.RulesetId),
                RulesetDefaults.Sr5,
                StringComparison.Ordinal))
        {
            blockers.Add(CharacterCreationFoundationBlockers.RulesetSr5Required);
        }
        if (!string.Equals(
                summary.BuildMethod,
                CharacterCreationBuildMethods.LifeModules,
                StringComparison.Ordinal))
        {
            blockers.Add(CharacterCreationFoundationBlockers.LifeModuleBuildMethodRequired);
        }
        if (summary.Created)
            blockers.Add(CharacterCreationFoundationBlockers.CharacterAlreadyCreated);

        string[] normalizedBlockers = blockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        string snapshotDigest = Digest(new
        {
            Schema = CharacterCreationFoundationSchemas.SnapshotV1,
            Binding = binding,
            RulesetId = workspace.Document.RulesetId,
            summary.Metatype,
            summary.BuildMethod,
            summary.Created,
            Metatypes = metatypeOptions,
            Nationalities = nationalities,
            LifeModuleBudget = lifeModuleBudget,
            PendingDraft = pendingDraft,
            Blockers = normalizedBlockers
        });
        var state = new CharacterCreationFoundationState(
            Schema: CharacterCreationFoundationSchemas.SnapshotV1,
            Binding: binding,
            RulesetId: workspace.Document.RulesetId,
            CurrentMetatype: summary.Metatype,
            BuildMethod: summary.BuildMethod,
            CharacterCreated: summary.Created,
            MetatypeOptions: metatypeOptions,
            NationalityOptions: nationalities,
            LifeModuleBudget: lifeModuleBudget,
            PendingDraft: pendingDraft,
            ResumeStatus: pendingDraft is null
                ? CharacterCreationFoundationResumeStatuses.AuthorityRequired
                : CharacterCreationFoundationResumeStatuses.PendingDraft,
            AuthorityBlockers: normalizedBlockers,
            SnapshotDigest: snapshotDigest);
        return new CharacterCreationFoundationResult<CharacterCreationFoundationState>(
            Outcome: CharacterCreationFoundationOutcomes.Success,
            Value: state,
            Blockers: normalizedBlockers);
    }

    private static LifeModuleVersionProjectionDto? ResolveVersion(
        LifeModuleLegalOptionDto nationality,
        string? versionId,
        ICollection<string> blockers)
    {
        if (nationality.Versions.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(versionId))
                blockers.Add(CharacterCreationFoundationBlockers.NationalityVersionNotApplicable);
            return null;
        }

        if (string.IsNullOrWhiteSpace(versionId))
        {
            blockers.Add(CharacterCreationFoundationBlockers.NationalityVersionRequired);
            return null;
        }

        LifeModuleVersionProjectionDto? version = nationality.Versions.FirstOrDefault(item =>
            string.Equals(item.VersionId, versionId.Trim(), StringComparison.Ordinal));
        if (version is null)
            blockers.Add(CharacterCreationFoundationBlockers.NationalityVersionNotFound);
        return version;
    }

    private static CharacterCreationLegalOption? ResolveMetatypeOption(
        IReadOnlyList<CharacterCreationLegalOption> options,
        string? requestedMetatype,
        ICollection<string> blockers)
    {
        string requested = (requestedMetatype ?? string.Empty).Trim();
        CharacterCreationLegalOption[] matches = options
            .Where(option => string.Equals(option.Label, requested, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length == 0
            && Guid.TryParseExact(requested, "D", out Guid requestedId)
            && requestedId != Guid.Empty)
        {
            string canonicalId = requestedId.ToString("D");
            matches = options
                .Where(option => string.Equals(
                    option.OptionId,
                    canonicalId,
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
        }

        if (matches.Length != 1 || !matches[0].IsEnabled)
        {
            blockers.Add(CharacterCreationFoundationBlockers.MetatypeOptionNotFound);
            return null;
        }

        return matches[0];
    }

    private static bool TryMapMetatypeOptions(
        CharacterCreationMetatypeCatalogAuthority authority,
        CharacterCreationSourceProfileAuthority sourceProfile,
        bool hasSourceProfileAuthority,
        IReadOnlyList<string> authoritativeSources,
        out CharacterCreationLegalOption[] options)
    {
        options = [];
        CharacterCreationMetatypeSourceContextAuthority? sourceContext = authority.SourceContext;
        if (!hasSourceProfileAuthority
            || !string.Equals(
                authority.Schema,
                CharacterCreationMetatypeCatalogSchemas.CatalogV1,
                StringComparison.Ordinal)
            || !authority.IsAuthoritative
            || authority.Blockers is null
            || authority.Blockers.Count != 0
            || sourceContext is null
            || !sourceContext.IsAuthoritative
            || sourceContext.Blockers is null
            || sourceContext.Blockers.Count != 0
            || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(
                sourceContext.AuthorityDigest)
            || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(
                sourceContext.RawMetatypesXmlDigest)
            || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(
                sourceContext.EffectiveMetatypesInputsDigest)
            || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(
                sourceContext.RawProfileInputsDigest)
            || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(
                sourceContext.SelectedCustomDataInputsDigest)
            || !string.Equals(
                sourceContext.SettingsProfileId,
                sourceProfile.SettingsProfileId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                sourceContext.RawProfileInputsDigest,
                sourceProfile.RawProfileInputsDigest,
                StringComparison.Ordinal)
            || !NormalizeSources(sourceContext.EnabledSourcebooks)
                .SequenceEqual(authoritativeSources, StringComparer.OrdinalIgnoreCase)
            || authority.Options is null
            || authority.Options.Count == 0)
        {
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var labels = new HashSet<string>(StringComparer.Ordinal);
        var mapped = new List<CharacterCreationLegalOption>(authority.Options.Count);
        foreach (CharacterCreationMetatypeOptionProjection? option in authority.Options)
        {
            if (option is null
                || !Guid.TryParseExact(option.OptionId, "D", out Guid optionId)
                || optionId == Guid.Empty
                || !string.Equals(option.OptionId, optionId.ToString("D"), StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(option.Label)
                || !string.Equals(option.Label, option.Label.Trim(), StringComparison.Ordinal)
                || !option.IsEnabled
                || option.Blockers is null
                || option.Blockers.Count != 0
                || option.KarmaCost < 0
                || string.IsNullOrWhiteSpace(option.SourceBook)
                || !string.Equals(option.SourceBook, option.SourceBook.Trim(), StringComparison.Ordinal)
                || !authoritativeSources.Contains(
                    option.SourceBook,
                    StringComparer.OrdinalIgnoreCase)
                || option.SourcePage <= 0
                || option.Attributes is null
                || option.Attributes.Count == 0
                || option.Attributes.Any(attribute =>
                    attribute is null
                    || string.IsNullOrWhiteSpace(attribute.AttributeId)
                    || attribute.Minimum < 0
                    || attribute.Maximum < attribute.Minimum
                    || attribute.AugmentedMaximum < attribute.Maximum)
                || option.Initiative is null
                || option.Initiative.Minimum < 0
                || option.Initiative.Maximum < option.Initiative.Minimum
                || option.Initiative.AugmentedMaximum < option.Initiative.Maximum
                || option.Initiative.MinimumDiceFallback < 0
                || option.Movement is null
                || option.GrantedQualities is null
                || option.GrantedQualities.Any(quality =>
                    quality is null
                    || string.IsNullOrWhiteSpace(quality.Name)
                    || quality.SourceAnchorIds is null
                    || quality.SourceAnchorIds.Count == 0
                    || quality.SourceAnchorIds.Any(string.IsNullOrWhiteSpace))
                || option.ExcludedMetavariants is null
                || option.ExcludedMetavariants.Any(excluded =>
                    excluded is null
                    || string.IsNullOrWhiteSpace(excluded.OptionId)
                    || string.IsNullOrWhiteSpace(excluded.Label)
                    || excluded.SourceAnchorIds is null
                    || excluded.SourceAnchorIds.Count == 0
                    || excluded.SourceAnchorIds.Any(string.IsNullOrWhiteSpace))
                || option.SourceAnchorIds is null
                || option.SourceAnchorIds.Count == 0
                || option.SourceAnchorIds.Any(string.IsNullOrWhiteSpace)
                || !ids.Add(option.OptionId)
                || !labels.Add(option.Label))
            {
                return false;
            }

            mapped.Add(MapMetatypeOption(option));
        }

        options = mapped.ToArray();
        return true;
    }

    private static CharacterCreationLegalOption MapMetatypeOption(
        CharacterCreationMetatypeOptionProjection option)
    {
        var consequences = new List<CharacterCreationChoiceConsequence>();
        consequences.AddRange(option.Attributes.Select(attribute =>
            new CharacterCreationChoiceConsequence(
                ConsequenceId: $"metatype:{option.OptionId}:attribute:{attribute.AttributeId}",
                Domain: "attribute-range",
                TargetId: attribute.AttributeId,
                BeforeValue: null,
                AfterValue: string.Create(
                    CultureInfo.InvariantCulture,
                    $"{attribute.Minimum}/{attribute.Maximum}/{attribute.AugmentedMaximum}"),
                SourceAnchorIds: option.SourceAnchorIds)));
        consequences.Add(new CharacterCreationChoiceConsequence(
            ConsequenceId: $"metatype:{option.OptionId}:initiative",
            Domain: "initiative-range",
            TargetId: "initiative",
            BeforeValue: null,
            AfterValue: string.Create(
                CultureInfo.InvariantCulture,
                $"{option.Initiative.Minimum}/{option.Initiative.Maximum}/{option.Initiative.AugmentedMaximum};dice-fallback={option.Initiative.MinimumDiceFallback}"),
            SourceAnchorIds: option.SourceAnchorIds));
        consequences.AddRange(new[]
        {
            (Id: "walk", Rate: option.Movement.Walk),
            (Id: "run", Rate: option.Movement.Run),
            (Id: "sprint", Rate: option.Movement.Sprint)
        }.Select(movement => new CharacterCreationChoiceConsequence(
            ConsequenceId: $"metatype:{option.OptionId}:movement:{movement.Id}",
            Domain: "movement-rate",
            TargetId: movement.Id,
            BeforeValue: null,
            AfterValue: string.Create(
                CultureInfo.InvariantCulture,
                $"{movement.Rate.Ground}/{movement.Rate.Swim}/{movement.Rate.Fly}"),
            SourceAnchorIds: option.SourceAnchorIds)));
        consequences.AddRange(option.GrantedQualities.Select(quality =>
            new CharacterCreationChoiceConsequence(
                ConsequenceId: $"metatype:{option.OptionId}:quality:{quality.Polarity}:{quality.Name}",
                Domain: "quality",
                TargetId: quality.Name,
                BeforeValue: null,
                AfterValue: quality.Polarity,
                SourceAnchorIds: quality.SourceAnchorIds)));
        consequences.AddRange(option.ExcludedMetavariants.Select(excluded =>
            new CharacterCreationChoiceConsequence(
                ConsequenceId: $"metatype:{option.OptionId}:excluded:{excluded.OptionId}",
                Domain: "excluded-metavariant",
                TargetId: excluded.OptionId,
                BeforeValue: null,
                AfterValue: excluded.Label,
                SourceAnchorIds: excluded.SourceAnchorIds)));

        return new CharacterCreationLegalOption(
            OptionId: option.OptionId,
            Label: option.Label,
            IsEnabled: true,
            DisableReasonKey: null,
            DisableReasonArguments: new Dictionary<string, string>(),
            Costs:
            [
                new CharacterCreationChoiceCost(
                    CharacterCreationBudgetIds.LifeModules,
                    option.KarmaCost,
                    "karma")
            ],
            Consequences: consequences,
            SourceAnchorIds: option.SourceAnchorIds,
            SourceId: option.SourceBook,
            SourcePage: option.SourcePage);
    }

    private static LifeModuleRequirementProjectionDto EvaluateRequirement(
        LifeModuleRequirementProjectionDto requirement,
        string requestedMetatype)
    {
        bool canEvaluate = requirement.Operator.Equals("oneof", StringComparison.OrdinalIgnoreCase)
                           && requirement.SubjectKind.Equals("metatype", StringComparison.OrdinalIgnoreCase)
                           && !string.IsNullOrWhiteSpace(requestedMetatype);
        if (!canEvaluate)
        {
            return requirement with
            {
                IsMet = false,
                DisableReasonKey = CharacterCreationFoundationBlockers.CharacterEligibilityAuthorityRequired,
                RequiresCharacterAuthority = true
            };
        }

        bool isMet = requirement.AcceptedValues.Contains(
            requestedMetatype.Trim(),
            StringComparer.OrdinalIgnoreCase);
        return requirement with
        {
            IsMet = isMet,
            DisableReasonKey = isMet
                ? null
                : CharacterCreationFoundationBlockers.LifeModuleRequirementNotMet,
            DisableReasonArguments = isMet
                ? new Dictionary<string, string>()
                : requirement.DisableReasonArguments,
            RequiresCharacterAuthority = false
        };
    }

    private static CharacterCreationFoundationResult<T> ReadFailure<T>(
        WorkspaceStoreReadResult read)
        where T : class
    {
        string outcome = read.Outcome == WorkspaceOperationOutcome.Missing
            ? CharacterCreationFoundationOutcomes.Missing
            : CharacterCreationFoundationOutcomes.Invalid;
        return Blocked<T>(outcome, CharacterCreationFoundationBlockers.WorkspaceUnavailable);
    }

    private static CharacterCreationFoundationResult<T> Blocked<T>(
        string outcome,
        params string[] blockers)
        where T : class
    {
        return new CharacterCreationFoundationResult<T>(
            Outcome: outcome,
            Value: null,
            Blockers: blockers);
    }

    private static string[] NormalizeSources(IReadOnlyCollection<string>? enabledSources)
    {
        return enabledSources?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];
    }

    private static CharacterCreationBudgetState BuildCurrentLifeModuleBudget(
        CharacterCreationSourceProfileAuthority sourceProfile,
        bool hasSourceProfileAuthority,
        bool hasExistingLifeModuleQuality,
        CharacterCreationFoundationDraftLedger? pendingDraft,
        IReadOnlyList<LifeModuleLegalOptionDto> nationalities,
        IReadOnlyList<CharacterCreationLegalOption> metatypes)
    {
        var blockers = new List<string>();
        if (!hasSourceProfileAuthority)
            blockers.Add(CharacterCreationFoundationBlockers.LifeModuleBudgetAuthorityRequired);
        blockers.AddRange(sourceProfile.BudgetBlockers);
        if (hasExistingLifeModuleQuality)
        {
            blockers.Add(
                CharacterCreationFoundationBlockers.LifeModuleBudgetExistingSelectionAuthorityRequired);
        }
        decimal used = 0;
        bool pendingCostIsExact = true;
        if (pendingDraft is not null
            && !TryResolvePendingDraftCost(
                pendingDraft,
                nationalities,
                metatypes,
                out used,
                out pendingCostIsExact))
        {
            blockers.Add(
                CharacterCreationFoundationBlockers.LifeModuleBudgetPendingDraftAuthorityRequired);
        }

        string[] normalizedBlockers = blockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        decimal total = sourceProfile.BuildPoints.GetValueOrDefault();
        bool isExact = hasSourceProfileAuthority
                       && sourceProfile.LifeModuleBudgetIsExact
                       && pendingCostIsExact
                       && normalizedBlockers.Length == 0;
        return new CharacterCreationBudgetState(
            BudgetId: CharacterCreationBudgetIds.LifeModules,
            Label: "Life Modules Karma",
            Total: total,
            Used: isExact ? used : 0,
            Remaining: isExact ? total - used : total,
            IsExact: isExact,
            Blockers: normalizedBlockers,
            Unit: "karma");
    }

    private static CharacterCreationBudgetState ProjectBudgetAfterSelection(
        CharacterCreationBudgetState before,
        CharacterCreationLegalOption selectedMetatype,
        CharacterCreationChoiceCost selectionCost,
        bool selectionCostIsExact)
    {
        var blockers = new List<string>(before.Blockers);
        bool metatypeCostIsExact = TryResolveMetatypeCost(
            selectedMetatype,
            out decimal metatypeCost);
        bool isExact = before.IsExact && selectionCostIsExact && metatypeCostIsExact;
        if (!selectionCostIsExact)
            blockers.Add(CharacterCreationFoundationBlockers.LifeModuleBudgetAuthorityRequired);
        if (!metatypeCostIsExact)
            blockers.Add(CharacterCreationFoundationBlockers.MetatypeLegalityAuthorityRequired);

        decimal used = isExact
            ? metatypeCost + selectionCost.Delta
            : before.Used;
        decimal remaining = isExact ? before.Total - used : before.Remaining;
        if (isExact && remaining < 0)
            blockers.Add(CharacterCreationFoundationBlockers.LifeModuleBudgetExceeded);

        string[] normalizedBlockers = blockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        return before with
        {
            Used = used,
            Remaining = remaining,
            IsExact = isExact,
            Blockers = normalizedBlockers
        };
    }

    private static bool TryResolvePendingDraftCost(
        CharacterCreationFoundationDraftLedger draft,
        IReadOnlyList<LifeModuleLegalOptionDto> nationalities,
        IReadOnlyList<CharacterCreationLegalOption> metatypes,
        out decimal cost,
        out bool costIsExact)
    {
        cost = 0;
        costIsExact = false;
        CharacterCreationLegalOption? metatype = metatypes.FirstOrDefault(option =>
            string.Equals(option.Label, draft.RequestedMetatype, StringComparison.Ordinal));
        if (metatype is null || !TryResolveMetatypeCost(metatype, out decimal metatypeCost))
            return false;

        LifeModuleLegalOptionDto? module = nationalities.FirstOrDefault(option =>
            string.Equals(option.ModuleId, draft.Selection.ModuleId, StringComparison.Ordinal));
        if (module is null)
            return false;

        if (module.Versions.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(draft.Selection.VersionId))
                return false;
            cost = metatypeCost + module.KarmaCost;
            costIsExact = module.KarmaIsExact;
            return true;
        }

        LifeModuleVersionProjectionDto? version = module.Versions.FirstOrDefault(item =>
            string.Equals(item.VersionId, draft.Selection.VersionId, StringComparison.Ordinal));
        if (version is null)
            return false;
        cost = metatypeCost + version.KarmaCost;
        costIsExact = version.KarmaIsExact;
        return true;
    }

    private static bool TryResolveMetatypeCost(
        CharacterCreationLegalOption option,
        out decimal cost)
    {
        cost = 0;
        CharacterCreationChoiceCost[] costs = option.Costs
            .Where(item => string.Equals(
                item.BudgetId,
                CharacterCreationBudgetIds.LifeModules,
                StringComparison.Ordinal)
                && string.Equals(item.Unit, "karma", StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (costs.Length != 1 || costs[0].Delta < 0)
            return false;
        cost = costs[0].Delta;
        return true;
    }

    private static bool HasExistingLifeModuleQuality(string characterXml)
    {
        XDocument document = XDocument.Parse(characterXml, LoadOptions.None);
        return document
            .Descendants("quality")
            .Any(quality =>
                string.Equals(
                    quality.Element("qualitytype")?.Value.Trim(),
                    CharacterCreationBuildMethods.LifeModules,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    quality.Element("qualitysource")?.Value.Trim(),
                    CharacterCreationBuildMethods.LifeModules,
                    StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(quality.Element("stage")?.Value));
    }

    private static IReadOnlyDictionary<string, string> NormalizeFollowUpValues(
        IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var normalized = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> item in values)
        {
            if (string.IsNullOrWhiteSpace(item.Key))
                continue;
            normalized.TryAdd(item.Key.Trim(), item.Value?.Trim() ?? string.Empty);
        }
        return normalized;
    }

    private static void ValidateFollowUps(
        IReadOnlyList<LifeModuleFollowUpPromptDto> prompts,
        IReadOnlyDictionary<string, string> values,
        ICollection<string> blockers)
    {
        HashSet<string> promptIds = prompts
            .Select(prompt => prompt.PromptId)
            .ToHashSet(StringComparer.Ordinal);
        if (values.Keys.Any(key => !promptIds.Contains(key)))
            blockers.Add(CharacterCreationFoundationBlockers.LifeModuleFollowUpUnknown);

        foreach (LifeModuleFollowUpPromptDto prompt in prompts)
        {
            bool hasValue = values.TryGetValue(prompt.PromptId, out string? value)
                            && !string.IsNullOrWhiteSpace(value);
            if (prompt.IsRequired && !hasValue)
            {
                blockers.Add(CharacterCreationFoundationBlockers.LifeModuleFollowUpRequired);
                continue;
            }
            if (!hasValue || prompt.Options.Count == 0)
                continue;

            bool hasEnabledOption = prompt.Options.Any(option =>
                option.IsEnabled
                && string.Equals(option.SourceValue, value, StringComparison.Ordinal));
            if (!hasEnabledOption)
                blockers.Add(CharacterCreationFoundationBlockers.LifeModuleFollowUpOptionInvalid);
        }
    }

    private static string XmlCatalogCharacterAuthorityBlocker() =>
        CharacterCreationFoundationBlockers.CharacterEligibilityAuthorityRequired;

    private static string Digest<T>(T value)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string DigestRawXml(string xml)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(xml ?? string.Empty);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static bool DigestEquals(string left, string right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private sealed record PreviewEvaluation(
        CharacterCreationFoundationResult<CharacterCreationFoundationPreview> Result,
        CharacterCreationFoundationAuthorityContext? Context);
}
