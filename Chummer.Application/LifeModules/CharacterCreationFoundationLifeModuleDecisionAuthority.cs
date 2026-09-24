using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.LifeModules;

/// <summary>
/// Production adapter from rules-authoritative SR5 Life Modules draft
/// decisions to the Origin Dossier decision contract. It never
/// creates a second mechanics write path: confirmation is delegated back to
/// <see cref="ICharacterCreationFoundationService"/>, which owns preview,
/// explicit confirmation and the atomic workspace CAS.
/// </summary>
public sealed partial class CharacterCreationFoundationLifeModuleDecisionAuthority :
    ILifeModuleDecisionAuthority, ILifeModuleDecisionInputAuthority, ILifeModuleDecisionHistoryAuthority
{
    private readonly string _ownerId;
    private const string JourneyId = "sr5-life-modules-foundation";
    private const string StageId = "nationality";
    private const string TerminalStageId = "nationality-accepted";
    private const string RuntimeSemantics =
        "chummer.sr5-life-modules.foundation-origin-authority/v4";

    private readonly IWorkspaceStore _workspaceStore;
    private readonly ICharacterCreationFoundationService _foundation;
    private readonly ICharacterFileQueries _characterFiles;
    private readonly Func<string> _localeProvider;
    private CandidateSnapshot? _candidateSnapshot;

    public CharacterCreationFoundationLifeModuleDecisionAuthority(
        IWorkspaceStore workspaceStore,
        ICharacterCreationFoundationService foundation,
        ICharacterFileQueries characterFiles,
        Func<string>? localeProvider = null)
        : this(workspaceStore, foundation, characterFiles, "local-single-user", localeProvider)
    {
    }

    internal CharacterCreationFoundationLifeModuleDecisionAuthority(
        IWorkspaceStore workspaceStore,
        ICharacterCreationFoundationService foundation,
        ICharacterFileQueries characterFiles,
        string ownerId,
        Func<string>? localeProvider = null)
    {
        _ownerId = ownerId;
        _workspaceStore = workspaceStore ?? throw new ArgumentNullException(nameof(workspaceStore));
        _foundation = foundation ?? throw new ArgumentNullException(nameof(foundation));
        _characterFiles = characterFiles ?? throw new ArgumentNullException(nameof(characterFiles));
        _localeProvider = localeProvider ?? (() => CultureInfo.CurrentUICulture.Name);
    }

    public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAuthorityStep> Load(
        string workspaceId)
    {
        if (!TryWorkspaceId(workspaceId, out CharacterWorkspaceId id))
            return Invalid<LifeModuleDecisionAuthorityStep>();

        WorkspaceStoreReadResult read = _workspaceStore.Get(id);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
            return FromRead<LifeModuleDecisionAuthorityStep>(read);

        if (!CharacterCreationFinalizationReceiptLedgerIntegrity.TryReadReceiptHistory(workspace, out var history, out long historyRevision))
            return Invalid<LifeModuleDecisionAuthorityStep>();
        bool archived = workspace.Document.AuxiliaryState.CharacterCreationFinalizationArchive is not null;
        IReadOnlyList<LifeModuleDecisionAcceptance>? acceptances = history.LifeModuleDecisionAcceptances;
        if (acceptances is { Count: > 0 })
        {
            if (!LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(
                    id,
                    historyRevision,
                    acceptances))
                return Invalid<LifeModuleDecisionAuthorityStep>();
            LifeModuleDecisionAuthorityStep stored = acceptances[^1].NextStep;
            // A finished book retains its accepted Creation revision. It is a
            // read-only historical projection, never a fresh Career mutation.
            if (stored.WorkspaceRevision != historyRevision || archived && !stored.IsTerminal)
                return Blocked<LifeModuleDecisionAuthorityStep>(LifeModuleOriginDossierOutcomes.Conflict,
                    LifeModuleOriginDossierBlockers.WorkspaceStale);
            if (!stored.IsTerminal && _foundation is CharacterCreationFoundationService concrete)
            {
                var fresh = BuildContinuationStep(concrete, workspace, stored);
                if (fresh is null || !SameStep(stored, fresh))
                    return Blocked<LifeModuleDecisionAuthorityStep>(LifeModuleOriginDossierOutcomes.Conflict,
                        LifeModuleOriginDossierBlockers.DecisionStale);
            }
            return Success(stored);
        }
        if (archived) return Missing<LifeModuleDecisionAuthorityStep>();

        CharacterCreationFoundationResult<CharacterCreationFoundationState> loaded =
            _foundation.Load(new CharacterCreationFoundationLoadRequest(id));
        if (loaded.Value is not CharacterCreationFoundationState state)
            return FromFoundation<CharacterCreationFoundationState, LifeModuleDecisionAuthorityStep>(loaded);
        if (state.PendingDraft is not null)
        {
            return Blocked<LifeModuleDecisionAuthorityStep>(
                LifeModuleOriginDossierOutcomes.Conflict,
                CharacterCreationFoundationBlockers.PendingDraftConflict);
        }

        return BuildInitial(workspace, state) is { } step
            ? Success(step)
            : Blocked<LifeModuleDecisionAuthorityStep>(
                LifeModuleOriginDossierOutcomes.Blocked,
                LifeModuleOriginDossierBlockers.AuthorityInvalid);
    }

    public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> FindAcceptance(
        string workspaceId,
        string idempotencyKeyDigest)
    {
        if (!TryWorkspaceId(workspaceId, out CharacterWorkspaceId id)
            || !LifeModuleDecisionAcceptanceIntegrity.IsDigest(idempotencyKeyDigest))
            return Invalid<LifeModuleDecisionAcceptance>();
        WorkspaceStoreReadResult read = _workspaceStore.Get(id);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
            return FromRead<LifeModuleDecisionAcceptance>(read);
        if (!CharacterCreationFinalizationReceiptLedgerIntegrity.TryReadReceiptHistory(workspace, out var history, out long historyRevision))
            return Invalid<LifeModuleDecisionAcceptance>();
        IReadOnlyList<LifeModuleDecisionAcceptance>? ledger = history.LifeModuleDecisionAcceptances;
        if (ledger is null || ledger.Count == 0)
            return Missing<LifeModuleDecisionAcceptance>();
        if (!LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(
                id,
                historyRevision,
                ledger))
            return Invalid<LifeModuleDecisionAcceptance>();
        LifeModuleDecisionAcceptance[] matches = ledger.Where(candidate =>
                FixedEquals(candidate.Receipt.IdempotencyKeyDigest, idempotencyKeyDigest))
            .ToArray();
        if (matches.Length == 1 && !workspace.CanReplayReceipt(matches[0].Receipt.WorkspaceRevision))
            return Blocked<LifeModuleDecisionAcceptance>(LifeModuleOriginDossierOutcomes.Conflict,
                LifeModuleOriginDossierBlockers.IdempotencyConflict);
        return matches.Length switch
        {
            0 => Missing<LifeModuleDecisionAcceptance>(),
            1 => Success(matches[0]),
            _ => Invalid<LifeModuleDecisionAcceptance>()
        };
    }

    public LifeModuleDecisionAuthorityResult<IReadOnlyList<LifeModuleDecisionAcceptance>> LoadHistory(string workspaceId)
    {
        if (!TryWorkspaceId(workspaceId, out CharacterWorkspaceId id))
            return Invalid<IReadOnlyList<LifeModuleDecisionAcceptance>>();
        var read = _workspaceStore.Get(id);
        if (!read.Success || read.Value is not { } workspace)
            return FromRead<IReadOnlyList<LifeModuleDecisionAcceptance>>(read);
        if (!CharacterCreationFinalizationReceiptLedgerIntegrity.TryReadReceiptHistory(workspace, out var history, out long historyRevision))
            return Invalid<IReadOnlyList<LifeModuleDecisionAcceptance>>();
        var ledger = history.LifeModuleDecisionAcceptances;
        if (ledger is not { Count: > 0 })
            return Missing<IReadOnlyList<LifeModuleDecisionAcceptance>>();
        return LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(id, historyRevision, ledger)
            ? Success<IReadOnlyList<LifeModuleDecisionAcceptance>>(ledger)
            : Invalid<IReadOnlyList<LifeModuleDecisionAcceptance>>();
    }

    public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> Accept(
        LifeModuleDecisionAcceptanceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> replay =
            FindAcceptance(command.WorkspaceId, command.IdempotencyKeyDigest);
        if (string.Equals(replay.Outcome, LifeModuleOriginDossierOutcomes.Success, StringComparison.Ordinal))
            return replay.Value is { } accepted
                   && accepted.Receipt.ChoiceId == command.ChoiceId
                   && accepted.Receipt.DecisionCommandDigest == command.DecisionCommandDigest
                   && accepted.Receipt.InputResolutionDigest == command.InputResolution?.ResolutionDigest
                ? replay : Blocked<LifeModuleDecisionAcceptance>(LifeModuleOriginDossierOutcomes.Conflict,
                    LifeModuleOriginDossierBlockers.IdempotencyConflict);
        if (!string.Equals(replay.Outcome, LifeModuleOriginDossierOutcomes.Missing, StringComparison.Ordinal))
            return replay;
        if (!TryWorkspaceId(command.WorkspaceId, out CharacterWorkspaceId id))
            return Invalid<LifeModuleDecisionAcceptance>();

        WorkspaceStoreReadResult read = _workspaceStore.Get(id);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
            return FromRead<LifeModuleDecisionAcceptance>(read);
        if (workspace.Document.AuxiliaryState.LifeModuleDecisionAcceptances is { Count: > 0 })
            return AcceptModule(workspace, command);
        CharacterCreationFoundationResult<CharacterCreationFoundationState> loaded =
            _foundation.Load(new CharacterCreationFoundationLoadRequest(id));
        if (loaded.Value is not CharacterCreationFoundationState state)
            return FromFoundation<CharacterCreationFoundationState, LifeModuleDecisionAcceptance>(loaded);
        LifeModuleDecisionAuthorityStep? step = BuildInitial(workspace, state);
        if (step is null || !CommandMatchesStep(command, step))
        {
            return Blocked<LifeModuleDecisionAcceptance>(
                LifeModuleOriginDossierOutcomes.Conflict,
                LifeModuleOriginDossierBlockers.DecisionStale);
        }

        DecisionCandidate? candidate = BuildCandidates(state).SingleOrDefault(item =>
            string.Equals(item.Choice.ChoiceId, command.ChoiceId, StringComparison.Ordinal));
        if (candidate is null
            || !FixedEquals(candidate.Choice.DecisionCommandDigest, command.DecisionCommandDigest))
            return Invalid<LifeModuleDecisionAcceptance>();

        var resolvedPreview = candidate.FoundationPreview;
        if (candidate.Choice.FollowUps is { Count: > 0 })
        {
            var resolved = ResolveFoundationInputs(step, state, candidate, command.InputResolution?.Values);
            if (resolved.Value is not { } resolution || command.InputResolution is null
                || resolution.ResolutionDigest != command.InputResolution.ResolutionDigest)
                return Invalid<LifeModuleDecisionAcceptance>();
            resolvedPreview = _foundation.Preview(new(state.Binding, resolvedPreview.RequestedMetatype,
                candidate.Selection, resolution.Values)).Value!;
        }
        else if (command.InputResolution is not null)
            return Invalid<LifeModuleDecisionAcceptance>();

        CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> confirmed =
            _foundation.Confirm(new CharacterCreationFoundationConfirmRequest(
                state.Binding,
                candidate.FoundationPreview.RequestedMetatype,
                candidate.Selection,
                resolvedPreview.PreviewDigest,
                ExplicitlyConfirmed: true,
                FollowUpValues: resolvedPreview.FollowUpValues)
            {
                OriginDecisionCommand = command,
                OriginDecisionStep = step
            });
        if (confirmed.Value?.OriginDecisionAcceptance is not { } acceptance)
            return FromFoundation<CharacterCreationFoundationApplyReceipt, LifeModuleDecisionAcceptance>(confirmed);
        return Success(acceptance);
    }

    internal static LifeModuleDecisionAcceptance? CreateAcceptance(
        CharacterCreationFoundationAuthorityContext context,
        CharacterCreationFoundationDraftLedger proposed,
        long nextWorkspaceRevision,
        string foundationPreviewDigest)
    {
        LifeModuleDecisionAcceptanceCommand? command = context.OriginDecisionCommand;
        LifeModuleDecisionAuthorityStep? current = context.OriginDecisionStep;
        if (command is null || current is null || current.IsTerminal
            || !CommandMatchesStep(command, current)
            || nextWorkspaceRevision != command.WorkspaceRevision + 1)
            return null;
        LifeModuleDecisionAuthorityChoice[] matches = current.LegalChoices.Where(choice =>
                string.Equals(choice.ChoiceId, command.ChoiceId, StringComparison.Ordinal)
                && FixedEquals(choice.DecisionCommandDigest, command.DecisionCommandDigest))
            .ToArray();
        if (matches.Length != 1)
            return null;
        LifeModuleDecisionAuthorityChoice choice = matches[0];
        if (choice.FollowUps is { Count: > 0 }
            ? !LifeModuleDecisionInputIntegrity.Matches(command.InputResolution, current.WorkspaceId,
                current.WorkspaceRevision, choice.ChoiceId, current.DecisionDigest, choice.DecisionCommandDigest)
              || command.InputResolution!.ResolvedPreviewDigest != foundationPreviewDigest
              || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                  command.InputResolution.Values, context.FollowUpValues)
            : command.InputResolution is not null)
            return null;

        string decisionId = Digest(new
        {
            Kind = "sr5-foundation-nationality",
            command.WorkspaceId,
            command.ChoiceId,
            command.DecisionCommandDigest,
            InputResolutionDigest = command.InputResolution?.ResolutionDigest
        });
        string factId = $"life-module:{context.Nationality.ModuleId}";
        var fact = new OriginCanonicalNarrativeFact(
            factId,
            "accepted-life-module",
            context.Nationality.Name,
            decisionId,
            choice.SourceAnchorIds.ToArray(),
            string.Empty);
        fact = fact with { FactDigest = Digest(fact with { FactDigest = string.Empty }) };
        var metatypeFact = new OriginCanonicalNarrativeFact(
            $"metatype:{context.SelectedMetatype.OptionId}",
            "accepted-metatype",
            context.SelectedMetatype.Label,
            decisionId,
            context.SelectedMetatype.SourceAnchorIds.ToArray(),
            string.Empty);
        metatypeFact = metatypeFact with { FactDigest = Digest(metatypeFact with { FactDigest = string.Empty }) };
        var facts = new List<OriginCanonicalNarrativeFact> { metatypeFact, fact };
        foreach (var prompt in choice.FollowUps ?? [])
        {
            if (!context.FollowUpValues.TryGetValue(prompt.PromptId, out var answer) || string.IsNullOrWhiteSpace(answer)) continue;
            var answerFact = new OriginCanonicalNarrativeFact(factId + ":answer:" + Digest(prompt.PromptId),
                "accepted-life-module-answer", prompt.Label + ": " + answer, decisionId, prompt.SourceAnchorIds, string.Empty);
            facts.Add(answerFact with { FactDigest = Digest(answerFact) });
        }
        string acceptedGraphDigest = Digest(new
        {
            current.DecisionGraphDigest,
            DecisionId = decisionId,
            command.ChoiceId,
            proposed.DraftDigest
        });
        string mechanicsDigest = Digest(new
        {
            proposed.DraftDigest,
            proposed.DraftRevision,
            proposed.ProjectedEffects,
            proposed.FollowUpValues
        });
        string consequence = string.IsNullOrWhiteSpace(context.NationalityVersion?.StoryTemplate)
            ? context.Nationality.StoryTemplate
            : context.NationalityVersion!.StoryTemplate;
        if (string.IsNullOrWhiteSpace(consequence))
            consequence = context.Nationality.Name;

        var terminal = new LifeModuleDecisionAuthorityStep(
            OriginDossierSchemas.DecisionAuthorityStepV1,
            current.RulesetId,
            current.WorkspaceId,
            nextWorkspaceRevision,
            current.OwnerId,
            current.RunnerId,
            current.RunnerDisplayName,
            current.Locale,
            current.JourneyId,
            TerminalStageId,
            current.StageOrder,
            $"{current.TurnId}:accepted",
            current.TurnSequence + 1,
            consequence.Trim(),
            TerminalPrompt(current.Locale),
            [],
            facts,
            [decisionId],
            command.ExpectedTurnSeedDigest,
            acceptedGraphDigest,
            Digest(new { acceptedGraphDigest, proposed.DraftDigest, Terminal = true }),
            command.ExpectedContentDigest,
            command.ExpectedSourceDigest,
            command.ExpectedRulesDigest,
            command.ExpectedRuntimeDigest,
            mechanicsDigest)
        {
            IsTerminal = true
        };
        if (context.OriginContinuation is not null)
        {
            LifeModuleDecisionAuthorityStep? continuation = context.OriginContinuation(
                ProjectCommittedDraft(context.Workspace, proposed), terminal);
            if (continuation is null)
                return null;
            terminal = continuation;
        }
        var receipt = new LifeModuleAcceptedDecisionReceipt(
            OriginDossierSchemas.AcceptedDecisionReceiptV1,
            decisionId,
            command.ChoiceId,
            command.DecisionCommandDigest,
            command.IdempotencyKeyDigest,
            command.WorkspaceRevision,
            nextWorkspaceRevision,
            command.ExpectedContentDigest,
            terminal.ContentDigest,
            terminal.SourceDigest,
            terminal.RulesDigest,
            terminal.RuntimeDigest,
            command.ExpectedDecisionDigest,
            command.ExpectedMechanicsSnapshotDigest,
            terminal.DecisionGraphDigest,
            terminal.MechanicsSnapshotDigest,
            consequence.Trim(),
            facts,
            string.Empty) { InputResolutionDigest = command.InputResolution?.ResolutionDigest };
        return LifeModuleOriginDossierService.SealAcceptanceChapter(current, receipt, terminal);
    }

    private LifeModuleDecisionAuthorityStep? BuildInitial(
        WorkspaceStoredDocument workspace,
        CharacterCreationFoundationState state)
    {
        if (!string.Equals(state.RulesetId, RulesetDefaults.Sr5, StringComparison.Ordinal)
            || !string.Equals(state.BuildMethod, CharacterCreationBuildMethods.LifeModules, StringComparison.Ordinal)
            || state.CharacterCreated
            || state.PendingDraft is not null
            || state.Binding.WorkspaceId != workspace.Id
            || state.Binding.ContentRevision != workspace.ContentRevision
            || state.Binding.SavedRevision != workspace.SavedRevision
            || state.AuthorityBlockers.Count != 0
            || !string.Equals(state.Binding.CharacterDigestSemantics,
                CharacterCreationFoundationDigestSemantics.RawCharacterXmlSha256, StringComparison.Ordinal)
            || !string.Equals(state.Binding.SourceDigestSemantics,
                CharacterCreationFoundationDigestSemantics.RawSourceInputsSha256, StringComparison.Ordinal)
            || !TryFoundationDigest(state.Binding.RawCharacterXmlDigest, out string contentDigest)
            || !TryFoundationDigest(state.Binding.SourceDigest, out string sourceDigest))
            return null;
        DecisionCandidate[] candidates = BuildCandidates(state);
        if (candidates.Length == 0)
            return null;

        CharacterFileSummary summary;
        try
        {
            summary = _characterFiles.ParseSummary(new CharacterDocument(workspace.Document.Content));
        }
        catch
        {
            return null;
        }
        string displayName = FirstText(summary.Alias, summary.Name, "Runner");
        string locale = NormalizeLocale(_localeProvider());
        string runtimeDigest = Digest(RuntimeSemantics);
        string rulesDigest = Digest(new { RulesetId = RulesetDefaults.Sr5, state.Binding.SourceDigest });
        string mechanicsDigest = Digest(new
        {
            state.Binding.ContentRevision,
            state.Binding.RawCharacterXmlDigest,
            state.LifeModuleBudget,
            Choices = candidates.Select(item => item.Choice.DecisionCommandDigest).ToArray()
        });
        string graphDigest = Digest(new
        {
            JourneyId,
            StageId,
            Choices = candidates.Select(item => item.Choice.DecisionCommandDigest).Order().ToArray()
        });
        string decisionDigest = Digest(new
        {
            graphDigest,
            state.Binding.ContentRevision,
            state.Binding.SourceDigest,
            mechanicsDigest
        });
        return new LifeModuleDecisionAuthorityStep(
            OriginDossierSchemas.DecisionAuthorityStepV1,
            RulesetDefaults.Sr5,
            workspace.Id.Value,
            workspace.ContentRevision,
            _ownerId,
            workspace.Id.Value,
            displayName,
            locale,
            JourneyId,
            StageId,
            LifeModuleJourneyStageOrders.Nationality,
            $"{JourneyId}:{workspace.Id.Value}:1",
            1,
            LeadIn(locale),
            Prompt(locale),
            candidates.Select(item => item.Choice).OrderBy(item => item.ChoiceId, StringComparer.Ordinal).ToArray(),
            [],
            [],
            LifeModuleOriginDossierService.TurnLedgerRootDigest,
            graphDigest,
            decisionDigest,
            contentDigest,
            sourceDigest,
            rulesDigest,
            runtimeDigest,
            mechanicsDigest);
    }

    // Foundation owns prefixed SHA-256 identities; Origin owns raw SHA-256
    // identities. Convert only this documented boundary, never arbitrary text.
    private static bool TryFoundationDigest(string value, out string digest)
    {
        digest = string.Empty;
        if (!CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(value))
            return false;
        digest = value[7..];
        return true;
    }

    private DecisionCandidate[] BuildCandidates(CharacterCreationFoundationState state)
    {
        // Load still obtains fresh Foundation authority. Reuse only the exact
        // immutable input projection, never a workspace read or mutation result.
        // Serialized custody prevents callers of Load from mutating our cache.
        string digest = Digest(state);
        var cached = Volatile.Read(ref _candidateSnapshot);
        if (cached?.StateDigest == digest)
            return JsonSerializer.Deserialize<DecisionCandidate[]>(cached.Json)!;
        var candidates = state.MetatypeOptions
            .Where(option => option.IsEnabled && option.DisableReasonKey is null
                && (string.IsNullOrEmpty(state.CurrentMetatype)
                    || string.Equals(option.Label, state.CurrentMetatype, StringComparison.Ordinal)))
            .GroupBy(option => option.OptionId, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .OrderBy(option => option.OptionId, StringComparer.Ordinal)
            .SelectMany(metatype => BuildCandidates(state, metatype))
            .ToArray();
        Volatile.Write(ref _candidateSnapshot, new(digest, JsonSerializer.Serialize(candidates)));
        return candidates;
    }

    private sealed record CandidateSnapshot(string StateDigest, string Json);

    private DecisionCandidate[] BuildCandidates(
        CharacterCreationFoundationState state, CharacterCreationLegalOption metatype)
    {
        var candidates = new List<DecisionCandidate>();
        foreach (LifeModuleLegalOptionDto module in state.NationalityOptions
                     .OrderBy(item => item.ModuleId, StringComparer.Ordinal))
        {
            IEnumerable<LifeModuleVersionProjectionDto?> versions = module.Versions.Count == 0
                ? [null]
                : module.Versions.OrderBy(item => item.VersionId, StringComparer.Ordinal);
            foreach (LifeModuleVersionProjectionDto? version in versions)
            {
                LifeModuleFollowUpPromptDto[] prompts = module.FollowUps
                    .Concat(version?.FollowUps ?? [])
                    .ToArray();
                // Catalog flags cannot evaluate character requirements. Let
                // Foundation resolve them for this exact metatype and reject
                // only its evaluated authority, not the unbound catalog row.
                if (!LifeModuleDecisionInputIntegrity.ValidForms(prompts.Length == 0 ? null : prompts))
                    continue;
                var selection = new CharacterCreationFoundationSelection(
                    module.ModuleId,
                    version?.VersionId);
                CharacterCreationFoundationResult<CharacterCreationFoundationPreview> projected =
                    _foundation.Preview(new CharacterCreationFoundationPreviewRequest(
                        state.Binding,
                        metatype.Label,
                        selection,
                        new Dictionary<string, string>(StringComparer.Ordinal)));
                if (projected.Value is not { } preview
                    || preview.AuthorityBlockers.Any(blocker => blocker != CharacterCreationFoundationBlockers.LifeModuleFollowUpRequired)
                    || preview.Nationality is not { IsEnabled: true }
                    || version is not null && preview.NationalityVersion is not { IsEnabled: true }
                    || !preview.Nationality.KarmaIsExact
                    || !preview.LifeModuleBudgetBefore.IsExact
                    || !preview.LifeModuleBudgetAfter.IsExact
                    || !string.Equals(preview.RequestedMetatype, metatype.Label, StringComparison.Ordinal))
                    continue;
                CharacterCreationFoundationDiffEntry[] metatypeDiff = preview.Diff.Where(item =>
                    item.Domain == "metatype-choice" && item.TargetId == metatype.OptionId
                    && item.AfterValue == metatype.Label && item.IsAuthoritative && item.CanApply
                    && item.Blockers.Count == 0 && item.SourceAnchorIds.Count > 0).ToArray();
                if (metatypeDiff.Length != 1)
                    continue;
                string choiceId = Digest(new
                {
                    MetatypeOptionId = metatype.OptionId,
                    module.ModuleId,
                    VersionId = version?.VersionId ?? string.Empty
                });
                string decisionCommandDigest = Digest(new
                {
                    state.Binding.WorkspaceId,
                    state.Binding.ContentRevision,
                    choiceId,
                    preview.PreviewDigest,
                    preview.Selection
                });
                LifeModuleEffectProjectionDto[] effects =
                [.. preview.NationalityVersion?.Effects ?? [], .. preview.Nationality.Effects];
                string[] anchors = preview.Diff.SelectMany(item => item.SourceAnchorIds)
                    .Concat(preview.NationalityVersion?.SourceAnchorIds ?? [])
                    .Concat(preview.Nationality.SourceAnchorIds)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                if (anchors.Length == 0)
                    continue;
                LifeModuleMechanicsPreviewItem[] items =
                [
                    new(metatypeDiff[0].DiffId, metatypeDiff[0].Domain, metatypeDiff[0].TargetId,
                        metatypeDiff[0].BeforeValue ?? string.Empty, metatypeDiff[0].AfterValue ?? string.Empty,
                        0, metatypeDiff[0].SourceAnchorIds.ToArray(), string.Empty),
                    .. effects.Select(effect =>
                        new LifeModuleMechanicsPreviewItem(
                            effect.EffectId,
                            effect.Domain,
                            effect.TargetId,
                            effect.BeforeValue ?? string.Empty,
                            effect.AfterValue ?? string.Empty,
                            effect.BudgetDelta,
                            effect.SourceAnchorIds.ToArray(),
                            string.Empty))
                ];
                decimal totalCost = preview.LifeModuleBudgetAfter.Used - preview.LifeModuleBudgetBefore.Used;
                var mechanics = new LifeModuleMechanicsPreview(
                    totalCost,
                    totalCost.ToString(CultureInfo.InvariantCulture),
                    true,
                    items,
                    prompts.Where(prompt => prompt.IsRequired).Select(prompt => prompt.PromptId).ToArray(),
                    anchors,
                    string.Empty);
                var choice = new LifeModuleDecisionAuthorityChoice(
                    choiceId,
                    version is null ? $"{metatype.Label} · {module.Name}" : $"{metatype.Label} · {module.Name} · {version.Label}",
                    version?.Source ?? module.Source,
                    version?.PageReference ?? module.PageReference,
                    decisionCommandDigest,
                    mechanics,
                    anchors,
                    [],
                    true) { FollowUps = prompts.Length == 0 ? null : prompts };
                candidates.Add(new DecisionCandidate(selection, preview, choice));
            }
        }
        return candidates.ToArray();
    }

    private static bool CommandMatchesStep(
        LifeModuleDecisionAcceptanceCommand command,
        LifeModuleDecisionAuthorityStep step)
        => string.Equals(command.Schema, OriginDossierSchemas.DecisionAcceptanceCommandV1, StringComparison.Ordinal)
           && string.Equals(command.WorkspaceId, step.WorkspaceId, StringComparison.Ordinal)
           && command.WorkspaceRevision == step.WorkspaceRevision
           && FixedEquals(command.ExpectedContentDigest, step.ContentDigest)
           && FixedEquals(command.ExpectedSourceDigest, step.SourceDigest)
           && FixedEquals(command.ExpectedRulesDigest, step.RulesDigest)
           && FixedEquals(command.ExpectedRuntimeDigest, step.RuntimeDigest)
           && FixedEquals(command.ExpectedDecisionGraphDigest, step.DecisionGraphDigest)
           && FixedEquals(command.ExpectedDecisionDigest, step.DecisionDigest)
           && FixedEquals(command.ExpectedMechanicsSnapshotDigest, step.MechanicsSnapshotDigest)
           && LifeModuleDecisionAcceptanceIntegrity.IsDigest(command.ExpectedTurnSeedDigest)
           && LifeModuleDecisionAcceptanceIntegrity.IsDigest(command.IdempotencyKeyDigest)
           && !string.IsNullOrWhiteSpace(command.IdempotencyKey);

    private static string LeadIn(string locale) => PrimaryLanguage(locale) switch
    {
        "de" => "Deine Herkunft ist noch nicht festgelegt. Die folgenden Optionen stammen aus der aktiven SR5-Regelumgebung.",
        "es" => "Tu origen aún no está decidido. Las siguientes opciones proceden del entorno de reglas SR5 activo.",
        _ => "Your origin is not decided yet. The following options come from the active SR5 rules environment."
    };

    private static string Prompt(string locale) => PrimaryLanguage(locale) switch
    {
        "de" => "Welche Herkunft prägt deinen Runner?",
        "es" => "¿Qué origen define a tu runner?",
        _ => "Which origin shapes your runner?"
    };

    private static string TerminalPrompt(string locale) => PrimaryLanguage(locale) switch
    {
        "de" => "Diese Entscheidung wurde gespeichert. Fahre mit der Charaktererstellung fort.",
        "es" => "Esta decisión se ha guardado. Continúa con la creación del personaje.",
        _ => "This decision has been saved. Continue character creation."
    };

    private static string NormalizeLocale(string? locale)
    {
        string value = string.IsNullOrWhiteSpace(locale) ? "en" : locale.Trim().Replace('_', '-');
        try
        {
            return CultureInfo.GetCultureInfo(value).Name;
        }
        catch (CultureNotFoundException)
        {
            return "en";
        }
    }

    private static string PrimaryLanguage(string locale)
        => locale.Split('-', 2, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();

    private static string FirstText(params string?[] values)
        => values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();

    private static bool TryWorkspaceId(string value, out CharacterWorkspaceId id)
    {
        id = new CharacterWorkspaceId(value?.Trim() ?? string.Empty);
        return !string.IsNullOrWhiteSpace(value)
               && string.Equals(value, value.Trim(), StringComparison.Ordinal)
               && value.Length <= 256;
    }

    internal static string Digest(object value)
        => LifeModuleDecisionAcceptanceIntegrity.ComputeCanonicalDigest(value);

    private static bool FixedEquals(string? left, string? right)
        => LifeModuleDecisionAcceptanceIntegrity.FixedEquals(left, right);

    private static LifeModuleDecisionAuthorityResult<T> Success<T>(T value) where T : class
        => new(LifeModuleOriginDossierOutcomes.Success, value, []);

    private static LifeModuleDecisionAuthorityResult<T> Missing<T>() where T : class
        => new(LifeModuleOriginDossierOutcomes.Missing, null, []);

    private static LifeModuleDecisionAuthorityResult<T> Invalid<T>() where T : class
        => Blocked<T>(LifeModuleOriginDossierOutcomes.Invalid, LifeModuleOriginDossierBlockers.AuthorityInvalid);

    private static LifeModuleDecisionAuthorityResult<T> Blocked<T>(string outcome, string blocker)
        where T : class
        => new(outcome, null, [blocker]);

    private static LifeModuleDecisionAuthorityResult<T> FromRead<T>(WorkspaceStoreReadResult read)
        where T : class
        => read.Outcome switch
        {
            WorkspaceOperationOutcome.Missing => Missing<T>(),
            WorkspaceOperationOutcome.Conflict => Blocked<T>(
                LifeModuleOriginDossierOutcomes.Conflict,
                LifeModuleOriginDossierBlockers.WorkspaceStale),
            _ => Invalid<T>()
        };

    private static LifeModuleDecisionAuthorityResult<TTarget> FromFoundation<TSource, TTarget>(
        CharacterCreationFoundationResult<TSource> result)
        where TSource : class
        where TTarget : class
        => new(
            result.Outcome switch
            {
                CharacterCreationFoundationOutcomes.Success => LifeModuleOriginDossierOutcomes.Success,
                CharacterCreationFoundationOutcomes.Missing => LifeModuleOriginDossierOutcomes.Missing,
                CharacterCreationFoundationOutcomes.Conflict => LifeModuleOriginDossierOutcomes.Conflict,
                CharacterCreationFoundationOutcomes.Invalid => LifeModuleOriginDossierOutcomes.Invalid,
                _ => LifeModuleOriginDossierOutcomes.Blocked
            },
            null,
            result.Blockers.Count == 0 ? [LifeModuleOriginDossierBlockers.AuthorityInvalid] : result.Blockers);

    private sealed record DecisionCandidate(
        CharacterCreationFoundationSelection Selection,
        CharacterCreationFoundationPreview FoundationPreview,
        LifeModuleDecisionAuthorityChoice Choice);
}

/// <summary>Canonical validation for the durable Origin acceptance ledger.</summary>
public static class LifeModuleDecisionAcceptanceIntegrity
{
    private const int MaximumReceipts = 4_096;

    public static bool TryValidateLedger(
        CharacterWorkspaceId workspaceId,
        long currentWorkspaceRevision,
        IReadOnlyList<LifeModuleDecisionAcceptance>? ledger)
    {
        if (ledger is null || ledger.Count == 0 || ledger.Count > MaximumReceipts)
            return false;
        if (ledger[0]?.Receipt is not { } firstReceipt)
            return false;
        long previousRevision = firstReceipt.PreviousWorkspaceRevision;
        var idempotency = new HashSet<string>(StringComparer.Ordinal);
        var acceptedIds = new List<string>();
        var facts = new List<OriginCanonicalNarrativeFact>();
        LifeModuleDecisionAuthorityStep? previousStep = null;
        foreach (LifeModuleDecisionAcceptance acceptance in ledger)
        {
            LifeModuleAcceptedDecisionReceipt? receipt = acceptance?.Receipt;
            LifeModuleDecisionAuthorityStep? next = acceptance?.NextStep;
            if (receipt is null || next is null
                || !string.Equals(receipt.Schema, OriginDossierSchemas.AcceptedDecisionReceiptV1, StringComparison.Ordinal)
                || !string.Equals(next.Schema, OriginDossierSchemas.DecisionAuthorityStepV1, StringComparison.Ordinal)
                || !string.Equals(next.RulesetId, RulesetDefaults.Sr5, StringComparison.Ordinal)
                || !string.Equals(next.WorkspaceId, workspaceId.Value, StringComparison.Ordinal)
                || receipt.PreviousWorkspaceRevision != previousRevision
                || receipt.WorkspaceRevision != previousRevision + 1
                || next.WorkspaceRevision != receipt.WorkspaceRevision
                || !LifeModuleOriginDossierService.TryCreateTurn(next, out _)
                || next.AcceptedDecisionIds.Count == 0
                || string.IsNullOrWhiteSpace(receipt.DecisionId)
                || string.IsNullOrWhiteSpace(receipt.ChoiceId)
                || string.IsNullOrWhiteSpace(receipt.ConsequenceMarkdown)
                || receipt.CanonicalFacts is null
                || receipt.CanonicalFacts.Count == 0
                || !IsDigest(receipt.DecisionCommandDigest)
                || !IsDigest(receipt.IdempotencyKeyDigest)
                || !IsDigest(receipt.PreviousContentDigest)
                || !IsDigest(receipt.ContentDigest)
                || !IsDigest(receipt.SourceDigest)
                || !IsDigest(receipt.RulesDigest)
                || !IsDigest(receipt.RuntimeDigest)
                || !IsDigest(receipt.PreviousDecisionDigest)
                || !IsDigest(receipt.PreviousMechanicsSnapshotDigest)
                || !IsDigest(receipt.AcceptedDecisionGraphDigest)
                || !IsDigest(receipt.MechanicsSnapshotDigest)
                || receipt.InputResolutionDigest is not null && !IsDigest(receipt.InputResolutionDigest)
                || !LifeModuleOriginDossierService.ValidateStoredChapter(acceptance!, acceptedIds.Count + 1)
                || !FixedEquals(next.ContentDigest, receipt.ContentDigest)
                || !FixedEquals(next.SourceDigest, receipt.SourceDigest)
                || !FixedEquals(next.RulesDigest, receipt.RulesDigest)
                || !FixedEquals(next.RuntimeDigest, receipt.RuntimeDigest)
                || !FixedEquals(next.DecisionGraphDigest, receipt.AcceptedDecisionGraphDigest)
                || !FixedEquals(next.MechanicsSnapshotDigest, receipt.MechanicsSnapshotDigest)
                || !next.AcceptedDecisionIds.Contains(receipt.DecisionId, StringComparer.Ordinal)
                || receipt.CanonicalFacts.Any(fact => fact is null
                    || !string.Equals(fact.AcceptedDecisionId, receipt.DecisionId, StringComparison.Ordinal)
                    || !IsDigest(fact.FactDigest)
                    || fact.SourceAnchorIds is null
                    || fact.SourceAnchorIds.Count == 0)
                || !idempotency.Add(receipt.IdempotencyKeyDigest)
                || !FixedEquals(receipt.ReceiptDigest, ComputeReceiptDigest(receipt)))
                return false;
            acceptedIds.Add(receipt.DecisionId);
            facts.AddRange(receipt.CanonicalFacts);
            if (!acceptedIds.SequenceEqual(next.AcceptedDecisionIds, StringComparer.Ordinal)
                || !FixedEquals(
                    ComputeCanonicalDigest(facts.OrderBy(fact => fact.FactId, StringComparer.Ordinal).ToArray()),
                    ComputeCanonicalDigest(next.CanonicalFacts.OrderBy(fact => fact.FactId, StringComparer.Ordinal).ToArray())))
                return false;
            if (previousStep is not null)
            {
                if (previousStep.IsTerminal
                    || previousStep.OwnerId != next.OwnerId || previousStep.RunnerId != next.RunnerId
                    || previousStep.RunnerDisplayName != next.RunnerDisplayName
                    || previousStep.Locale != next.Locale || previousStep.JourneyId != next.JourneyId
                    || previousStep.TurnSequence == int.MaxValue
                    || next.TurnSequence != previousStep.TurnSequence + 1
                    || next.StageOrder < previousStep.StageOrder
                    || !FixedEquals(receipt.PreviousContentDigest, previousStep.ContentDigest)
                    || !FixedEquals(receipt.SourceDigest, previousStep.SourceDigest)
                    || !FixedEquals(receipt.RulesDigest, previousStep.RulesDigest)
                    || !FixedEquals(receipt.RuntimeDigest, previousStep.RuntimeDigest)
                    || !FixedEquals(receipt.PreviousDecisionDigest, previousStep.DecisionDigest)
                    || !FixedEquals(receipt.PreviousMechanicsSnapshotDigest, previousStep.MechanicsSnapshotDigest)
                    || !LifeModuleOriginDossierService.TryCreateTurn(previousStep, out var previousTurn)
                    || previousTurn is null || !FixedEquals(next.PreviousTurnDigest, previousTurn.SeedDigest))
                    return false;
            }
            previousRevision = receipt.WorkspaceRevision;
            previousStep = next;
        }
        return previousRevision <= currentWorkspaceRevision;
    }

    public static string ComputeReceiptDigest(LifeModuleAcceptedDecisionReceipt receipt)
        => ComputeCanonicalDigest(receipt with { ReceiptDigest = string.Empty });

    public static string ComputeCanonicalDigest(object value)
    {
        JsonElement root = JsonSerializer.SerializeToElement(value);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteCanonical(root, writer);
        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    public static bool IsDigest(string? value)
        => value is { Length: 64 }
           && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static bool FixedEquals(string? left, string? right)
        => IsDigest(left) && IsDigest(right)
           && CryptographicOperations.FixedTimeEquals(
               System.Text.Encoding.ASCII.GetBytes(left!),
               System.Text.Encoding.ASCII.GetBytes(right!));

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject()
                             .OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
