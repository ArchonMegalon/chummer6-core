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
/// Production adapter from the first, rules-authoritative SR5 Life Modules
/// foundation decision to the Origin Dossier decision contract.  It never
/// creates a second mechanics write path: confirmation is delegated back to
/// <see cref="ICharacterCreationFoundationService"/>, which owns preview,
/// explicit confirmation and the atomic workspace CAS.
/// </summary>
public sealed class CharacterCreationFoundationLifeModuleDecisionAuthority :
    ILifeModuleDecisionAuthority
{
    private const string OwnerId = "local-single-user";
    private const string JourneyId = "sr5-life-modules-foundation";
    private const string StageId = "nationality";
    private const string TerminalStageId = "nationality-accepted";
    private const string RuntimeSemantics =
        "chummer.sr5-life-modules.foundation-origin-authority/v1";

    private readonly IWorkspaceStore _workspaceStore;
    private readonly ICharacterCreationFoundationService _foundation;
    private readonly ICharacterFileQueries _characterFiles;
    private readonly Func<string> _localeProvider;

    public CharacterCreationFoundationLifeModuleDecisionAuthority(
        IWorkspaceStore workspaceStore,
        ICharacterCreationFoundationService foundation,
        ICharacterFileQueries characterFiles,
        Func<string>? localeProvider = null)
    {
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

        IReadOnlyList<LifeModuleDecisionAcceptance>? acceptances = workspace.Document
            .AuxiliaryState.LifeModuleDecisionAcceptances;
        if (acceptances is { Count: > 0 })
        {
            if (!LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(
                    id,
                    workspace.ContentRevision,
                    acceptances))
                return Invalid<LifeModuleDecisionAuthorityStep>();
            LifeModuleDecisionAuthorityStep terminal = acceptances[^1].NextStep;
            return Success(terminal);
        }

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
        IReadOnlyList<LifeModuleDecisionAcceptance>? ledger = workspace.Document
            .AuxiliaryState.LifeModuleDecisionAcceptances;
        if (ledger is null || ledger.Count == 0)
            return Missing<LifeModuleDecisionAcceptance>();
        if (!LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(
                id,
                workspace.ContentRevision,
                ledger))
            return Invalid<LifeModuleDecisionAcceptance>();
        LifeModuleDecisionAcceptance[] matches = ledger.Where(candidate =>
                FixedEquals(candidate.Receipt.IdempotencyKeyDigest, idempotencyKeyDigest))
            .ToArray();
        return matches.Length switch
        {
            0 => Missing<LifeModuleDecisionAcceptance>(),
            1 => Success(matches[0]),
            _ => Invalid<LifeModuleDecisionAcceptance>()
        };
    }

    public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> Accept(
        LifeModuleDecisionAcceptanceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> replay =
            FindAcceptance(command.WorkspaceId, command.IdempotencyKeyDigest);
        if (string.Equals(replay.Outcome, LifeModuleOriginDossierOutcomes.Success, StringComparison.Ordinal))
            return replay;
        if (!string.Equals(replay.Outcome, LifeModuleOriginDossierOutcomes.Missing, StringComparison.Ordinal))
            return replay;
        if (!TryWorkspaceId(command.WorkspaceId, out CharacterWorkspaceId id))
            return Invalid<LifeModuleDecisionAcceptance>();

        WorkspaceStoreReadResult read = _workspaceStore.Get(id);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
            return FromRead<LifeModuleDecisionAcceptance>(read);
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

        CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> confirmed =
            _foundation.Confirm(new CharacterCreationFoundationConfirmRequest(
                state.Binding,
                state.CurrentMetatype,
                candidate.Selection,
                candidate.FoundationPreview.PreviewDigest,
                ExplicitlyConfirmed: true,
                FollowUpValues: new Dictionary<string, string>(StringComparer.Ordinal))
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
        long nextWorkspaceRevision)
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

        string decisionId = Digest(new
        {
            Kind = "sr5-foundation-nationality",
            command.WorkspaceId,
            command.ChoiceId,
            command.DecisionCommandDigest
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
            [fact],
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
            [fact],
            string.Empty);
        receipt = receipt with
        {
            ReceiptDigest = LifeModuleDecisionAcceptanceIntegrity.ComputeReceiptDigest(receipt)
        };
        return new LifeModuleDecisionAcceptance(receipt, terminal);
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
            || state.AuthorityBlockers.Count != 0)
            return null;
        CharacterCreationLegalOption[] metatypes = state.MetatypeOptions.Where(option =>
                option.IsEnabled
                && option.DisableReasonKey is null
                && string.Equals(option.Label, state.CurrentMetatype, StringComparison.Ordinal))
            .ToArray();
        if (metatypes.Length != 1)
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
            OwnerId,
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
            state.Binding.RawCharacterXmlDigest,
            state.Binding.SourceDigest,
            rulesDigest,
            runtimeDigest,
            mechanicsDigest);
    }

    private DecisionCandidate[] BuildCandidates(CharacterCreationFoundationState state)
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
                if (!module.IsEnabled
                    || module.AuthorityBlockers.Count != 0
                    || version is { IsEnabled: false }
                    || version?.AuthorityBlockers.Count > 0
                    || prompts.Any(prompt => prompt.IsRequired))
                    continue;
                var selection = new CharacterCreationFoundationSelection(
                    module.ModuleId,
                    version?.VersionId);
                CharacterCreationFoundationResult<CharacterCreationFoundationPreview> projected =
                    _foundation.Preview(new CharacterCreationFoundationPreviewRequest(
                        state.Binding,
                        state.CurrentMetatype,
                        selection,
                        new Dictionary<string, string>(StringComparer.Ordinal)));
                if (!string.Equals(projected.Outcome, CharacterCreationFoundationOutcomes.Success, StringComparison.Ordinal)
                    || projected.Value is not { CanConfirm: true, CanApply: true } preview
                    || preview.AuthorityBlockers.Count != 0
                    || preview.Nationality is null
                    || !preview.Nationality.KarmaIsExact)
                    continue;
                string choiceId = Digest(new
                {
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
                LifeModuleMechanicsPreviewItem[] items = effects.Select(effect =>
                        new LifeModuleMechanicsPreviewItem(
                            effect.EffectId,
                            effect.Domain,
                            effect.TargetId,
                            effect.BeforeValue ?? string.Empty,
                            effect.AfterValue ?? string.Empty,
                            effect.BudgetDelta,
                            effect.SourceAnchorIds.ToArray(),
                            string.Empty))
                    .ToArray();
                var mechanics = new LifeModuleMechanicsPreview(
                    preview.SelectionCost.Delta,
                    preview.NationalityVersion?.KarmaRaw ?? preview.Nationality.KarmaRaw,
                    preview.NationalityVersion?.KarmaIsExact ?? preview.Nationality.KarmaIsExact,
                    items,
                    [],
                    anchors,
                    string.Empty);
                var choice = new LifeModuleDecisionAuthorityChoice(
                    choiceId,
                    version is null ? module.Name : $"{module.Name} · {version.Label}",
                    version?.Source ?? module.Source,
                    version?.PageReference ?? module.PageReference,
                    decisionCommandDigest,
                    mechanics,
                    anchors,
                    [],
                    true);
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
        long previousRevision = ledger[0].Receipt.PreviousWorkspaceRevision;
        var idempotency = new HashSet<string>(StringComparer.Ordinal);
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
                || !next.IsTerminal
                || next.LegalChoices.Count != 0
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
            previousRevision = receipt.WorkspaceRevision;
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
