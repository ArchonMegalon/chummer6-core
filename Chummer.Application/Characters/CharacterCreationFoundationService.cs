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

        return _applyAuthority.ApplyAndCheckpoint(context, preview.PreviewDigest);
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

        var selectionBlockers = new List<string>();
        string requestedMetatype = (request.RequestedMetatype ?? string.Empty).Trim();
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
            selectionCost,
            selectionKarmaIsExact,
            replacesPendingDraft: state.PendingDraft is not null);
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
            .Where(blocker => blocker is not CharacterCreationFoundationBlockers.WizardStatePersistenceAuthorityRequired
                              && !authorityPreview.ResolvedBlockers.Contains(
                                  blocker,
                                  StringComparer.Ordinal))
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

        if (!validation.IsValid)
        {
            return Blocked<CharacterCreationFoundationState>(
                CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationFoundationBlockers.CharacterDocumentInvalid);
        }

        var blockers = new List<string>
        {
            CharacterCreationFoundationBlockers.MetatypeCatalogAuthorityRequired
        };
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
        string[] normalizedRequestSources = NormalizeSources(requestedSources);
        string[] effectiveSources = sourceFilterApplied
            ? authoritativeSources
                .Intersect(normalizedRequestSources, StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray()
            : authoritativeSources;

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
            EnabledSources = effectiveSources
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
            nationalities);
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
            MetatypeOptions: [],
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
        IReadOnlyList<LifeModuleLegalOptionDto> nationalities)
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
        CharacterCreationChoiceCost selectionCost,
        bool selectionCostIsExact,
        bool replacesPendingDraft)
    {
        var blockers = new List<string>(before.Blockers);
        bool isExact = before.IsExact && selectionCostIsExact;
        if (!selectionCostIsExact)
            blockers.Add(CharacterCreationFoundationBlockers.LifeModuleBudgetAuthorityRequired);

        decimal used = isExact
            ? replacesPendingDraft
                ? selectionCost.Delta
                : before.Used + selectionCost.Delta
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
        out decimal cost,
        out bool costIsExact)
    {
        cost = 0;
        costIsExact = false;
        LifeModuleLegalOptionDto? module = nationalities.FirstOrDefault(option =>
            string.Equals(option.ModuleId, draft.Selection.ModuleId, StringComparison.Ordinal));
        if (module is null)
            return false;

        if (module.Versions.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(draft.Selection.VersionId))
                return false;
            cost = module.KarmaCost;
            costIsExact = module.KarmaIsExact;
            return true;
        }

        LifeModuleVersionProjectionDto? version = module.Versions.FirstOrDefault(item =>
            string.Equals(item.VersionId, draft.Selection.VersionId, StringComparison.Ordinal));
        if (version is null)
            return false;
        cost = version.KarmaCost;
        costIsExact = version.KarmaIsExact;
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
