using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Source-owned entry to Karma foundation. Never converts Karma into
/// a Priority or Life Modules draft or grants free points. Pending selection
/// confirmation leaves character XML unchanged; explicit whole-build finalization
/// uses a separate source-fenced atomic capability and archives the consumed graph.
/// </summary>
public sealed partial class CharacterCreationKarmaMetatypeService(
    IWorkspaceStore workspaceStore,
    ICharacterSourceDataResolver sourceDataResolver) : ICharacterCreationKarmaMetatypeService
{
    private readonly IWorkspaceStore _workspaceStore = workspaceStore
        ?? throw new ArgumentNullException(nameof(workspaceStore));
    private readonly ICharacterSourceDataResolver _sourceDataResolver = sourceDataResolver
        ?? throw new ArgumentNullException(nameof(sourceDataResolver));

    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> Load(
        CharacterWorkspaceId workspaceId, bool includeSkills = false, bool includeQualities = false, bool includeGear = false, bool includeLifestyles = false, bool includeMagic = false)
        => Load(workspaceId, includeSkills, includeQualities, includeGear, includeLifestyles, includeMagic, out _, out _);

    private CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> Load(
        CharacterWorkspaceId workspaceId, bool includeSkills, bool includeQualities, bool includeGear, bool includeLifestyles, bool includeMagic,
        out CharacterCreationKarmaQualitiesRules.AdmittedCatalog? admittedQualities,
        out ICharacterSourceDataContext? capturedContext)
    {
        admittedQualities = null;
        capturedContext = null;
        WorkspaceStoreReadResult read = _workspaceStore.Get(workspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return Blocked<CharacterCreationKarmaMetatypeState>(
                CharacterCreationKarmaMetatypeBlockers.WorkspaceUnavailable);

        WorkspaceDocument document = workspace.Document;
        CharacterCreationBootstrapBinding? bootstrap = document.AuxiliaryState
            .CharacterCreationBootstrapBinding;
        var decisions = document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions;
        // Never reset other spending by overlooking an existing draft or archive.
        if (bootstrap is null
            || document.RulesetId != RulesetDefaults.Sr5
            || bootstrap.BuildMethod != CharacterCreationBuildMethods.Karma
            || !Equals(document.AuxiliaryState with { CharacterCreationKarmaMetatypeDecisions = null },
                new WorkspaceDocumentAuxiliaryState(CharacterCreationBootstrapBinding: bootstrap)))
        {
            return Blocked<CharacterCreationKarmaMetatypeState>(
                CharacterCreationKarmaMetatypeBlockers.PendingKarmaBootstrapRequired);
        }

        if (!CharacterCreationKarmaMetatypeTransaction.IsValidHistory(workspace))
            return Blocked<CharacterCreationKarmaMetatypeState>(CharacterCreationKarmaMetatypeBlockers.HistoryInvalid);

        try
        {
            ICharacterSourceDataContext? context = _sourceDataResolver.TryCreateContext(document.Content);
            if (context is null
                || !CharacterCreationBootstrapAuthority.TryPrepareBinding(
                    workspaceId, document, context, out var currentBootstrap, out _, out _)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    bootstrap, currentBootstrap))
            {
                return Blocked<CharacterCreationKarmaMetatypeState>(
                    CharacterCreationKarmaMetatypeBlockers.PendingKarmaBootstrapRequired);
            }

            if (!context.TryResolveCreationSourceProfile(out var profile)
                || profile.BuildMethod != CharacterCreationBuildMethods.Karma
                || profile.SettingsProfileId != bootstrap.SettingsProfileId
                || profile.BuildPoints is not >= 0
                || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(profile.RawProfileInputsDigest)
                // The existing profile DTO evaluates a Life Modules budget.
                // Only its exact method-mismatch is inapplicable to this Karma
                // quote. Malformed/duplicate/absent buildpoints still block it.
                || profile.BudgetBlockers.Any(blocker => blocker !=
                    CharacterCreationFoundationBlockers.LifeModuleBudgetProfileBuildMethodMismatch))
            {
                return Blocked<CharacterCreationKarmaMetatypeState>(
                    CharacterCreationKarmaMetatypeBlockers.BudgetAuthorityRequired);
            }

            if (!context.TryResolveCreationMetatypeCatalog(out var catalog)
                || !catalog.IsAuthoritative || catalog.Blockers.Count != 0
                || catalog.Schema != CharacterCreationMetatypeCatalogSchemas.CatalogV1
                || !catalog.SourceContext.IsAuthoritative
                || catalog.SourceContext.Blockers.Count != 0
                || catalog.SourceContext.SettingsProfileId != profile.SettingsProfileId
                || catalog.SourceContext.RawProfileInputsDigest != profile.RawProfileInputsDigest
                || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(catalog.SourceContext.AuthorityDigest)
                || catalog.Options.Count == 0
                || catalog.Options.GroupBy(option => option.OptionId, StringComparer.Ordinal)
                    .Any(group => group.Count() != 1)
                || catalog.Options.Any(option => option.KarmaCost < 0 || option.SourceAnchorIds.Count == 0))
            {
                return Blocked<CharacterCreationKarmaMetatypeState>(
                    CharacterCreationKarmaMetatypeBlockers.MetatypeAuthorityRequired);
            }

            // Attribute costs do not require Priority ranks. A missing policy
            // leaves that later editor unavailable, not a guessed cost of five.
            if (!context.TryResolveCreationAttributePolicy(out var attributePolicy))
                attributePolicy = null;
            if (attributePolicy is not null && (attributePolicy.Schema != CharacterCreationAttributePolicy.SchemaV1
                || attributePolicy.SettingsProfileId != profile.SettingsProfileId
                || attributePolicy.BuildMethod != CharacterCreationBuildMethods.Karma
                || attributePolicy.KarmaAttribute <= 0 || attributePolicy.MaxNumberMaxAttributesCreate < 0
                || attributePolicy.SourceAnchorIds is not { Count: > 0 }
                || attributePolicy.RawProfileInputsDigest != profile.RawProfileInputsDigest
                || attributePolicy.AuthorityDigest != CharacterCreationAttributePolicyAuthority.ComputeDigest(attributePolicy)))
                attributePolicy = null;

            if (!context.TryResolveCreationKarmaTalents(out var talents)) talents = null;
            if (talents is not null && (talents.Schema != CharacterCreationKarmaTalentCatalog.SchemaV1
                || talents.SettingsProfileId != profile.SettingsProfileId
                || talents.RawProfileInputsDigest != profile.RawProfileInputsDigest
                || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(talents.SourceInputsDigest)
                || talents.KarmaQuality <= 0 || talents.SourceAnchorIds is not { Count: > 0 }
                || talents.Options is not { Count: > 0 }
                || talents.Options.GroupBy(item => item.OptionId, StringComparer.Ordinal).Any(group => group.Count() != 1)
                || talents.Options.Any(item => item.KarmaCost < 0
                    || !CharacterCreationKarmaTalentAuthority.IsOptionId(item.OptionId)
                    || item.SourceAnchorIds is not { Count: > 0 })
                || talents.AuthorityDigest != CharacterCreationKarmaTalentAuthority.ComputeDigest(talents)))
                talents = null;

            // Do not parse the complete skill/weapon catalogs on the first
            // metatype page. Once saved, skills always participate in revalidation.
            CharacterCreationKarmaSkillsPolicy? skillsPolicy = null;
            CharacterCreationSkillsCatalog? skillsCatalog = null;
            if (includeSkills || decisions?.LastOrDefault()?.Quote.Skills is not null)
            {
                if (!context.TryResolveCreationKarmaSkillsPolicy(out skillsPolicy)) skillsPolicy = null;
                if (!context.TryResolveCreationSkillsCatalog(out skillsCatalog)) skillsCatalog = null;
                if (skillsPolicy is not null && (skillsPolicy.SettingsProfileId != profile.SettingsProfileId
                    || skillsPolicy.RawProfileInputsDigest != profile.RawProfileInputsDigest
                    || skillsPolicy.AuthorityDigest != CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(skillsPolicy)))
                    skillsPolicy = null;
                if (skillsCatalog is not null && (!CharacterCreationSkillsCatalogAuthority.IsValid(skillsCatalog)
                    || skillsCatalog.SettingsProfileId != profile.SettingsProfileId
                    || skillsCatalog.RawProfileInputsDigest != profile.RawProfileInputsDigest)) skillsCatalog = null;
            }

            if (!context.TryResolveCreationKarmaResourcesPolicy(out var resourcesPolicy)) resourcesPolicy = null;
            if (resourcesPolicy is not null && (!CharacterCreationKarmaResourcesRules.IsValidPolicy(resourcesPolicy)
                || resourcesPolicy.SettingsProfileId != profile.SettingsProfileId
                || resourcesPolicy.RawProfileInputsDigest != profile.RawProfileInputsDigest)) resourcesPolicy = null;

            if (!context.TryResolveCreationKarmaContactsPolicy(out var contactsPolicy)) contactsPolicy = null;
            if (contactsPolicy is not null && (!CharacterCreationKarmaContactsRules.IsValidPolicy(contactsPolicy)
                || contactsPolicy.SettingsProfileId != profile.SettingsProfileId
                || contactsPolicy.RawProfileInputsDigest != profile.RawProfileInputsDigest)) contactsPolicy = null;

            CharacterCreationKarmaQualitiesCatalog? qualitiesCatalog = null;
            if (includeQualities || decisions?.LastOrDefault()?.Quote.Qualities is not null)
            {
                if (!context.TryResolveCreationKarmaQualities(out qualitiesCatalog)) qualitiesCatalog = null;
                admittedQualities = CharacterCreationKarmaQualitiesRules.AdmittedCatalog.TryCreate(qualitiesCatalog);
                qualitiesCatalog = admittedQualities?.Catalog;
                if (qualitiesCatalog is not null && (qualitiesCatalog.Policy.SettingsProfileId != profile.SettingsProfileId
                    || qualitiesCatalog.Policy.RawProfileInputsDigest != profile.RawProfileInputsDigest))
                {
                    qualitiesCatalog = null;
                    admittedQualities = null;
                }
            }

            CharacterCreationGearAuthority? gearAuthority = null;
            if (includeGear || decisions?.LastOrDefault()?.Quote.Gear is not null)
            {
                if (!context.TryResolveCreationGearAuthority(out gearAuthority)
                    || !CharacterCreationGearRules.IsValidAuthority(gearAuthority)
                    || gearAuthority.SettingsProfileId != profile.SettingsProfileId
                    || gearAuthority.ProfileDigest != profile.RawProfileInputsDigest) gearAuthority = null;
            }

            CharacterCreationLifestylesAuthority? lifestylesAuthority = null;
            if (includeLifestyles || decisions?.LastOrDefault()?.Quote.Lifestyles is not null)
            {
                if (!context.TryResolveCreationLifestylesAuthority(out lifestylesAuthority)
                    || !CharacterCreationLifestylesRules.IsValidAuthority(lifestylesAuthority)
                    || lifestylesAuthority.SettingsProfileId != profile.SettingsProfileId
                    || lifestylesAuthority.ProfileDigest != profile.RawProfileInputsDigest) lifestylesAuthority = null;
            }

            CharacterCreationKarmaMagicCatalog? magicCatalog = null;
            if (includeMagic || decisions?.LastOrDefault()?.Quote.Magic is not null)
            {
                if (!context.TryResolveCreationKarmaMagicCatalog(out magicCatalog)
                    || !CharacterCreationKarmaMagicSelectionRules.IsValidCatalog(magicCatalog)
                    || magicCatalog!.SettingsProfileId != profile.SettingsProfileId
                    || magicCatalog.RawProfileInputsDigest != profile.RawProfileInputsDigest
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(magicCatalog.Talents, talents))
                    magicCatalog = null;
            }

            // Admit the source capture again after catalog resolution, then the
            // persisted workspace. A source or workspace changed during load is
            // not a usable UI selection even if its initial read was valid.
            if (!CharacterCreationBootstrapAuthority.TryPrepareBinding(
                    workspaceId, document, context, out var finalBootstrap, out _, out _)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    bootstrap, finalBootstrap))
            {
                return Blocked<CharacterCreationKarmaMetatypeState>(
                    CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            }
            WorkspaceStoreReadResult finalRead = _workspaceStore.Get(workspaceId);
            if (!finalRead.Success || finalRead.Value is not { } finalWorkspace
                || finalWorkspace.ContentRevision != workspace.ContentRevision
                || finalWorkspace.SavedRevision != workspace.SavedRevision
                || finalWorkspace.Document.Content != document.Content
                || finalWorkspace.Document.AuxiliaryStateDigest != document.AuxiliaryStateDigest)
            {
                return Blocked<CharacterCreationKarmaMetatypeState>(
                    CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            }

            var binding = new CharacterCreationKarmaMetatypeBinding(
                workspaceId, workspace.ContentRevision, workspace.SavedRevision,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(document.Content),
                document.AuxiliaryStateDigest,
                bootstrap.BindingDigest,
                profile.RawProfileInputsDigest, catalog.SourceContext.AuthorityDigest, talents?.AuthorityDigest,
                attributePolicy?.AuthorityDigest, skillsPolicy?.AuthorityDigest, skillsCatalog?.CatalogDigest,
                resourcesPolicy?.AuthorityDigest, qualitiesCatalog?.Policy.AuthorityDigest, qualitiesCatalog?.CatalogDigest,
                gearAuthority?.AuthorityDigest, contactsPolicy?.AuthorityDigest, lifestylesAuthority?.AuthorityDigest,
                magicCatalog?.AuthorityDigest);
            CharacterCreationKarmaMetatypeDecision? selection = decisions?.LastOrDefault();
            if (selection is not null && (selection.Quote.KarmaBudget.Total != profile.BuildPoints.Value
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    selection.Quote.Metatype, catalog.Options.SingleOrDefault(option =>
                        option.OptionId == selection.Command.MetatypeOptionId))))
                return Blocked<CharacterCreationKarmaMetatypeState>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            if (selection?.Quote.Talent is { } selectedTalent
                && (talents is null || selection.Quote.Binding.TalentAuthorityDigest != talents.AuthorityDigest
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(selectedTalent,
                        talents.Options.SingleOrDefault(option => option.OptionId == selectedTalent.OptionId))))
                return Blocked<CharacterCreationKarmaMetatypeState>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            if (selection?.Quote.Attributes is { } selectedAttributes
                && (attributePolicy is null || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    selectedAttributes.Policy, attributePolicy)))
                return Blocked<CharacterCreationKarmaMetatypeState>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            if (selection?.Quote.Skills is { } selectedSkills
                && (skillsPolicy is null || skillsCatalog is null || talents is null
                    || selection.Quote.Talent is null || selection.Quote.Attributes is null
                    || selection.Quote.Binding.SkillsPolicyDigest != skillsPolicy.AuthorityDigest
                    || selection.Quote.Binding.SkillsCatalogDigest != skillsCatalog.CatalogDigest
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(selectedSkills,
                        CharacterCreationKarmaSkillsRules.Evaluate(skillsCatalog, skillsPolicy, talents,
                            selection.Quote.Metatype, selection.Quote.Talent.OptionId, selection.Quote.Attributes,
                            selectedSkills.KarmaAvailable, selection.Command.SkillsSelection!))))
                return Blocked<CharacterCreationKarmaMetatypeState>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            if (selection?.Quote.Resources is { } selectedResources
                && (resourcesPolicy is null || selection.Quote.Attributes is null
                    || selection.Quote.Binding.ResourcesPolicyDigest != resourcesPolicy.AuthorityDigest
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(selectedResources,
                        CharacterCreationKarmaResourcesRules.Evaluate(resourcesPolicy, selection.Quote.Attributes,
                            selectedResources.KarmaAvailable, selection.Command.ResourceKarmaInvestment!.Value))))
                return Blocked<CharacterCreationKarmaMetatypeState>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            if (selection?.Quote.Qualities is { } selectedQualities
                && (qualitiesCatalog is null || selection.Quote.Talent is null
                    || selection.Quote.Binding.QualitiesCatalogDigest != qualitiesCatalog.CatalogDigest
                    || selection.Quote.Binding.QualitiesPolicyDigest != qualitiesCatalog.Policy.AuthorityDigest
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(selectedQualities,
                        admittedQualities!.Evaluate(selection.Quote.Metatype,
                            selection.Quote.Talent, selection.Command.QualityOptionIds!))))
                return Blocked<CharacterCreationKarmaMetatypeState>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            if (selection?.Quote.Gear is { } selectedGear
                && (gearAuthority is null || selection.Quote.Resources is null
                    || selection.Quote.Binding.GearAuthorityDigest != gearAuthority.AuthorityDigest
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(selectedGear,
                        CharacterCreationKarmaGearRules.Evaluate(gearAuthority, selection.Quote.Resources,
                            selection.Command.GearSelections!))))
                return Blocked<CharacterCreationKarmaMetatypeState>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            if (selection?.Quote.Contacts is { } selectedContacts
                && (contactsPolicy is null || selectedContacts.Policy.AuthorityDigest != contactsPolicy.AuthorityDigest
                    || !context.TryResolveCreationKarmaGrantSources(selection.Command.MetatypeOptionId,
                        selection.Command.TalentOptionId!, out var racialSources, out var talentSource)
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(selectedContacts.RacialSources, racialSources)
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(selectedContacts.TalentSource, talentSource)))
                return Blocked<CharacterCreationKarmaMetatypeState>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            if (selection?.Quote.Lifestyles is { } selectedLifestyles
                && (lifestylesAuthority is null || selectedLifestyles.SourceAuthorityDigest != lifestylesAuthority.AuthorityDigest
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(selectedLifestyles.ProjectionAuthority,
                        CharacterCreationKarmaLifestylesRules.ProjectionAuthority(lifestylesAuthority, selection.Command.LifestyleSelections!))
                    || selectedLifestyles.Effects is not { } effects
                    || !context.TryResolveCreationKarmaGrantSources(selection.Command.MetatypeOptionId,
                        selection.Command.TalentOptionId!, out var lifestyleRacialSources, out var lifestyleTalentSource)
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(effects.RacialSources, lifestyleRacialSources)
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(effects.TalentSource, lifestyleTalentSource)))
                return Blocked<CharacterCreationKarmaMetatypeState>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            if (selection?.Quote.Magic is { } selectedMagic
                && (magicCatalog is null || selectedMagic.SourceAuthorityDigest != magicCatalog.AuthorityDigest
                    || selection.Quote.Binding.MagicAuthorityDigest != magicCatalog.AuthorityDigest
                    || !context.TryResolveCreationKarmaGrantSources(selection.Command.MetatypeOptionId,
                        selection.Command.TalentOptionId!, out var magicRacial, out var magicTalent)
                    || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(selectedMagic,
                        CharacterCreationKarmaMagicSelectionRules.Evaluate(magicCatalog,
                            CharacterCreationKarmaMagicSelectionRules.WithoutMagic(selection.Quote), magicRacial,
                            magicTalent, selection.Command.MagicSelections!))))
                return Blocked<CharacterCreationKarmaMetatypeState>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            string[] anchors = profile.SourceAnchorIds.Concat(catalog.SourceContext.SourceAnchorIds)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var state = new CharacterCreationKarmaMetatypeState(
                CharacterCreationKarmaMetatypeSchemas.SnapshotV1, binding, profile.SettingsProfileId,
                Budget(profile.BuildPoints.Value, selection?.Quote.KarmaBudget.Used ?? 0, []),
                catalog.Options.ToArray(), anchors, string.Empty, selection, attributePolicy, talents, skillsPolicy, skillsCatalog,
                resourcesPolicy, qualitiesCatalog, gearAuthority, contactsPolicy, lifestylesAuthority, magicCatalog);
            state = state with
            {
                SnapshotDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(state)
            };
            capturedContext = context;
            return new(CharacterCreationFoundationOutcomes.Success, state, []);
        }
        catch (Exception error) when (error is ArgumentException or FormatException
            or IOException or InvalidOperationException or UnauthorizedAccessException
            or System.Xml.XmlException)
        {
            return Blocked<CharacterCreationKarmaMetatypeState>(
                CharacterCreationKarmaMetatypeBlockers.MetatypeAuthorityRequired);
        }
    }

    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeOpen> Open(
        CharacterWorkspaceId workspaceId, bool includeSkills = false, bool includeQualities = false, bool includeGear = false, bool includeLifestyles = false, bool includeMagic = false)
    {
        var loaded = Load(workspaceId, includeSkills, includeQualities, includeGear, includeLifestyles, includeMagic, out var admittedQualities, out var context);
        if (loaded.Value is not { } state)
            return new(loaded.Outcome, null, loaded.Blockers);
        if (state.Selection is not { Command: { } saved })
            return new(loaded.Outcome, new(state, null), loaded.Blockers);

        // Only this service's freshly admitted snapshot reaches PreviewLoaded.
        // The persisted decision has an older binding: never return its quote
        // as a current review or accept a caller-supplied snapshot as authority.
        var preview = PreviewLoaded(state, admittedQualities, saved.MetatypeOptionId, saved.TalentOptionId,
            saved.AttributeAllocations, saved.SkillsSelection, saved.ResourceKarmaInvestment, saved.QualityOptionIds,
            saved.GearSelections, saved.ContactSelections, saved.LifestyleSelections, saved.StartingLifestyleId, saved.MagicSelections, context!);
        return preview.Value is { } quote
            ? new(preview.Outcome, new(state, quote), preview.Blockers)
            : new(preview.Outcome, null, preview.Blockers);
    }

    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> Confirm(
        CharacterCreationKarmaMetatypeConfirmRequest request)
    {
        if (!CharacterCreationKarmaMetatypeTransaction.TryFreezeRequest(request, out request))
            return Blocked<CharacterCreationKarmaMetatypeCommit>(CharacterCreationKarmaMetatypeBlockers.ConfirmationRequired);
        return _workspaceStore is ICharacterCreationKarmaMetatypeAtomicCommitCapability capability
            ? capability.CommitKarmaMetatype(request, _sourceDataResolver)
            : Blocked<CharacterCreationKarmaMetatypeCommit>(CharacterCreationKarmaMetatypeBlockers.PersistenceUnavailable);
    }

    /// <summary>
    /// Read-only completion finances from the persisted build and freshly admitted
    /// sources. A caller-supplied/historical quote is not source authority. This
    /// does not mark Created, acquire write permission, or spend starting cash.
    /// </summary>
    public CharacterCreationFoundationResult<CharacterCreationKarmaFinalizationBudgetQuote> PreviewFinalizationBudget(
        CharacterCreationKarmaMetatypeBinding binding, string foundationQuoteDigest, int diceTotal)
        => LoadFinalizationBudget(binding, foundationQuoteDigest, diceTotal);

    /// <summary>Load source-owned dice and multiplier before asking the player
    /// for a roll. No roll, budget review or confirmation is issued by this read.</summary>
    public CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource> LoadFinalizationStartingCash(
        CharacterCreationKarmaMetatypeBinding binding, string foundationQuoteDigest)
    {
        var result = LoadFinalizationBudget(binding, foundationQuoteDigest, null);
        return new(result.Outcome, result.Value?.StartingCashSource, result.Blockers);
    }

    private CharacterCreationFoundationResult<CharacterCreationKarmaFinalizationBudgetQuote> LoadFinalizationBudget(
        CharacterCreationKarmaMetatypeBinding binding, string foundationQuoteDigest, int? diceTotal)
    {
        ArgumentNullException.ThrowIfNull(binding);
        try
        {
            var read = _workspaceStore.Get(binding.WorkspaceId);
            if (read.Value is not { } workspace || !Matches(workspace))
                return Blocked<CharacterCreationKarmaFinalizationBudgetQuote>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            var context = _sourceDataResolver.TryCreateContext(workspace.Document.Content);
            if (context is null)
                return Blocked<CharacterCreationKarmaFinalizationBudgetQuote>(CharacterCreationKarmaFinalizationBudgetBlockers.PolicyUnavailable);
            // Keep catalog admission and the financial projection in ONE source
            // capture. Creating a second context after Open could miss source
            // drift between the gear quote and its completion calculation.
            var captured = new CharacterCreationKarmaMetatypeService(_workspaceStore,
                new CompletionSourceResolver(workspace.Document.Content, context));
            var opened = captured.Open(binding.WorkspaceId, includeSkills: true, includeQualities: true, includeGear: true);
            if (opened.Value is not { State: { } state, Quote: { } foundation })
                return new(CharacterCreationFoundationOutcomes.Blocked, null, opened.Blockers.Count > 0
                    ? opened.Blockers : [CharacterCreationKarmaMetatypeBlockers.HistoryInvalid]);
            if (state.Binding != binding || foundation.QuoteDigest != foundationQuoteDigest)
                return Blocked<CharacterCreationKarmaFinalizationBudgetQuote>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            if (!context.TryResolveCreationKarmaCarryoverPolicy(out var policy) || policy is null)
                return Blocked<CharacterCreationKarmaFinalizationBudgetQuote>(CharacterCreationKarmaFinalizationBudgetBlockers.PolicyUnavailable);
            if (!TryResolveStartingCash(context, foundation, out var source) || source is null)
                return Blocked<CharacterCreationKarmaFinalizationBudgetQuote>(CharacterCreationKarmaFinalizationBudgetBlockers.StartingCashUnavailable);
            // Terms-only reads validate admission at the source minimum, but
            // expose only the source row, never a guessed player roll/quote.
            var quote = CharacterCreationKarmaFinalizationBudgetRules.Evaluate(policy, source, foundation, diceTotal ?? source.Dice);
            if (quote is null)
                return Blocked<CharacterCreationKarmaFinalizationBudgetQuote>(CharacterCreationKarmaFinalizationBudgetBlockers.BudgetInvalid);
            // Recheck the same captured sources and workspace after projection.
            // Do not silently rebase a review if a settings/file/owner race occurs.
            if (!context.TryResolveCreationKarmaCarryoverPolicy(out var finalPolicy)
                || !TryResolveStartingCash(context, foundation, out var finalSource)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(policy, finalPolicy)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(source, finalSource)
                || !CharacterCreationBootstrapAuthority.TryPrepareBinding(binding.WorkspaceId, workspace.Document,
                    context, out var bootstrap, out _, out _)
                || bootstrap.BindingDigest != binding.BootstrapBindingDigest
                || _workspaceStore.Get(binding.WorkspaceId).Value is not { } finalWorkspace || !Matches(finalWorkspace))
                return Blocked<CharacterCreationKarmaFinalizationBudgetQuote>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            return new(CharacterCreationFoundationOutcomes.Success, quote, []);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or IOException
            or InvalidOperationException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return Blocked<CharacterCreationKarmaFinalizationBudgetQuote>(CharacterCreationKarmaFinalizationBudgetBlockers.PolicyUnavailable);
        }

        bool Matches(WorkspaceStoredDocument workspace) => workspace.ContentRevision == binding.ContentRevision
            && workspace.SavedRevision == binding.SavedRevision && workspace.Document.AuxiliaryStateDigest == binding.AuxiliaryStateDigest
            && CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(workspace.Document.Content)
                == binding.RawCharacterXmlDigest;
    }

    private sealed class CompletionSourceResolver(string xml, ICharacterSourceDataContext context) : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string characterXml) => characterXml == xml ? context : null;
    }

    internal static bool TryResolveStartingCash(ICharacterSourceDataContext context,
        CharacterCreationKarmaMetatypeQuote foundation, out CharacterCreationStartingNuyenSource? source)
    {
        source = null;
        if (foundation.Lifestyles is not { Lines.Count: > 0 } lifestyles)
            return context.TryResolveCreationKarmaDefaultStartingNuyen(out source);
        var selected = lifestyles.Lines.SingleOrDefault(line => line.Configuration.LifestyleId == lifestyles.StartingLifestyleId);
        return selected is not null && context.TryResolveCreationKarmaStartingNuyen(selected.SourceId, out source);
    }

    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote> Preview(
        CharacterCreationKarmaMetatypeBinding binding, string metatypeOptionId, string? talentOptionId = null,
        IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? attributeAllocations = null,
        CharacterCreationKarmaSkillsSelection? skillsSelection = null, decimal? resourceKarmaInvestment = null,
        IReadOnlyList<string>? qualityOptionIds = null,
        IReadOnlyList<CharacterCreationGearSelection>? gearSelections = null,
        IReadOnlyList<CharacterCreationKarmaContactSelection>? contactSelections = null,
        IReadOnlyList<CharacterCreationLifestyleConfiguration>? lifestyleSelections = null, Guid? startingLifestyleId = null,
        CharacterCreationMagicResonanceSelections? magicSelections = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> loaded = Load(binding.WorkspaceId,
            includeSkills: skillsSelection is not null || binding.SkillsCatalogDigest is not null || binding.SkillsPolicyDigest is not null,
            includeQualities: qualityOptionIds is not null || binding.QualitiesCatalogDigest is not null || binding.QualitiesPolicyDigest is not null,
            includeGear: gearSelections is not null || binding.GearAuthorityDigest is not null,
            includeLifestyles: lifestyleSelections is not null || binding.LifestylesAuthorityDigest is not null,
            includeMagic: magicSelections is not null || binding.MagicAuthorityDigest is not null,
            out var admittedQualities, out var context);
        if (loaded.Value is not { } state)
            return new(loaded.Outcome, null, loaded.Blockers);
        if (state.Binding != binding)
            return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);

        return PreviewLoaded(state, admittedQualities, metatypeOptionId, talentOptionId, attributeAllocations, skillsSelection,
            resourceKarmaInvestment, qualityOptionIds, gearSelections, contactSelections, lifestyleSelections, startingLifestyleId, magicSelections, context!);
    }

    private static CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote> PreviewLoaded(
        CharacterCreationKarmaMetatypeState state, CharacterCreationKarmaQualitiesRules.AdmittedCatalog? admittedQualities,
        string metatypeOptionId, string? talentOptionId,
        IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? attributeAllocations,
        CharacterCreationKarmaSkillsSelection? skillsSelection, decimal? resourceKarmaInvestment,
        IReadOnlyList<string>? qualityOptionIds, IReadOnlyList<CharacterCreationGearSelection>? gearSelections,
        IReadOnlyList<CharacterCreationKarmaContactSelection>? contactSelections,
        IReadOnlyList<CharacterCreationLifestyleConfiguration>? lifestyleSelections, Guid? startingLifestyleId,
        CharacterCreationMagicResonanceSelections? magicSelections,
        ICharacterSourceDataContext context)
    {
        CharacterCreationMetatypeOptionProjection? option = state.Options.SingleOrDefault(
            candidate => string.Equals(candidate.OptionId, metatypeOptionId, StringComparison.Ordinal));
        if (option is not { IsEnabled: true } || option.Blockers.Count != 0)
            return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.OptionUnavailable);

        CharacterCreationKarmaTalentOption? talent = null;
        if (talentOptionId is not null)
        {
            if (state.Talents is null)
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.TalentAuthorityRequired);
            talent = state.Talents.Options.SingleOrDefault(item => item.OptionId == talentOptionId);
            if (talent is not { IsEnabled: true, Blockers.Count: 0 })
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.OptionUnavailable);
            if (!CharacterCreationKarmaTalentAuthority.IsCompatible(talent, option))
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.OptionUnavailable);
        }
        else if (state.Selection?.Quote.Talent is not null)
            return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.TalentSelectionRequired);

        CharacterCreationKarmaAttributesQuote? attributes = null;
        if (attributeAllocations is not null)
        {
            if (talent is null)
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.TalentSelectionRequired);
            if (state.AttributePolicy is null)
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationAttributesBlockers.AuthorityUnavailable);
            attributes = CharacterCreationKarmaAttributesRules.Evaluate(option, talent, state.AttributePolicy, attributeAllocations);
            if (attributes is null)
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationAttributesBlockers.AllocationInvalid);
        }
        else if (state.Selection?.Quote.Attributes is not null)
            return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.AttributeSelectionRequired);

        decimal used = (decimal)option.KarmaCost + (talent?.KarmaCost ?? 0) + (attributes?.KarmaUsed ?? 0);
        CharacterCreationKarmaQualitiesQuote? qualities = null;
        if (qualityOptionIds is not null)
        {
            if (talent is null || attributes is null)
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.AttributeSelectionRequired);
            if (admittedQualities is null || !ReferenceEquals(state.QualitiesCatalog, admittedQualities.Catalog))
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.QualitiesAuthorityRequired);
            qualities = admittedQualities.Evaluate(option, talent, qualityOptionIds);
            if (qualities is null)
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationQualitiesBlockers.InvalidSelection);
            // Negative qualities fund the same pool, including skills/resources.
            used += qualities.Costs.NetKarmaSpent;
        }
        else if (state.Selection?.Quote.Qualities is not null)
            return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.QualitiesSelectionRequired);
        CharacterCreationKarmaSkillsQuote? skills = null;
        if (skillsSelection is not null)
        {
            if (talent is null || attributes is not { CanSelect: true })
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.AttributeSelectionRequired);
            if (state.SkillsPolicy is null || state.SkillsCatalog is null || state.Talents is null)
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.SkillsAuthorityRequired);
            if (used > state.KarmaBudget.Total || state.KarmaBudget.Total - used > int.MaxValue)
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.BudgetExceeded);
            skills = CharacterCreationKarmaSkillsRules.Evaluate(state.SkillsCatalog, state.SkillsPolicy, state.Talents,
                option, talent.OptionId, attributes, (int)(state.KarmaBudget.Total - used), skillsSelection);
            if (skills is null)
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationSkillsBlockers.AllocationInvalid);
            used += skills.KarmaUsed;
        }
        else if (state.Selection?.Quote.Skills is not null)
            return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.SkillsSelectionRequired);
        CharacterCreationKarmaResourcesQuote? resources = null;
        if (resourceKarmaInvestment is { } investment)
        {
            if (attributes is null)
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.AttributeSelectionRequired);
            if (state.ResourcesPolicy is null)
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.ResourcesAuthorityRequired);
            resources = CharacterCreationKarmaResourcesRules.Evaluate(state.ResourcesPolicy, attributes,
                state.KarmaBudget.Total - used, investment);
            if (resources is null)
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.ResourceInvestmentInvalid);
            used += investment;
        }
        else if (state.Selection?.Quote.Resources is not null)
            return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.ResourcesSelectionRequired);
        CharacterCreationKarmaGearQuote? gear = null;
        if (gearSelections is not null)
        {
            if (resources is null)
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.ResourcesSelectionRequired);
            if (state.GearAuthority is null)
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.GearAuthorityRequired);
            gear = CharacterCreationKarmaGearRules.Evaluate(state.GearAuthority, resources, gearSelections);
            if (gear is null)
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationGearBlockers.InvalidBasket);
        }
        else if (state.Selection?.Quote.Gear is not null)
            return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.GearSelectionRequired);
        string[] blockers = (attributes?.Blockers ?? []).Concat(qualities?.Blockers ?? []).Concat(skills?.Blockers ?? []).Concat(resources?.Blockers ?? [])
            .Concat(gear?.Blockers ?? []).Concat(used <= state.KarmaBudget.Total
                ? Array.Empty<string>() : [CharacterCreationKarmaMetatypeBlockers.BudgetExceeded])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var quote = new CharacterCreationKarmaMetatypeQuote(
            CharacterCreationKarmaMetatypeSchemas.QuoteV1, state.Binding, state.SnapshotDigest,
            option, Budget((int)state.KarmaBudget.Total, used, blockers), blockers.Length == 0,
            blockers, state.SourceAnchorIds.Concat(option.SourceAnchorIds).Concat(talent?.SourceAnchorIds ?? [])
                .Concat(attributes?.Policy.SourceAnchorIds ?? [])
                .Concat(skills?.Policy.SourceAnchorIds ?? [])
                .Concat(resources?.Policy.SourceAnchorIds ?? [])
                .Concat(qualities?.Policy.SourceAnchorIds ?? [])
                .Concat(qualities?.Selections.SelectMany(quality => quality.SourceAnchorIds) ?? [])
                .Concat(gear?.Lines.SelectMany(line => line.SourceAnchorIds) ?? [])
                .Concat(skills?.Basis.Catalog.ActiveSkills.Concat(skills.Basis.Catalog.KnowledgeSkills)
                    .SelectMany(skill => skill.SourceAnchorIds) ?? [])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), string.Empty, talent, attributes, skills, resources, qualities, gear);
        quote = quote with
        {
            QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quote)
        };
        if (contactSelections is not null)
        {
            if (state.ContactsPolicy is not { } contactPolicy || talent is null
                || !context.TryResolveCreationKarmaGrantSources(option.OptionId, talent.OptionId,
                    out var racialSources, out var talentSource))
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.ContactsAuthorityRequired);
            var contacts = CharacterCreationKarmaContactsRules.Evaluate(contactPolicy, quote, racialSources, talentSource, contactSelections);
            if (!context.TryResolveCreationKarmaContactsPolicy(out var currentPolicy)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(contactPolicy, currentPolicy))
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            // Keep current upstream rows editable when this draft cannot be
            // priced. An absent contact quote is never a zero-cost approval.
            blockers = blockers.Concat(contacts?.Blockers ?? [CharacterCreationContactsBlockers.ContactInvalid])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            quote = quote with
            {
                Contacts = contacts,
                KarmaBudget = Budget((int)state.KarmaBudget.Total, used + (contacts?.KarmaUsed ?? 0), blockers)
                    with { IsExact = contacts is not null },
                CanSelect = blockers.Length == 0,
                Blockers = blockers,
                SourceAnchorIds = quote.SourceAnchorIds.Concat(contactPolicy.SourceAnchorIds)
                    .Concat(racialSources.SelectMany(source => source.SourceAnchorIds))
                    .Concat(talentSource?.SourceAnchorIds ?? [])
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                QuoteDigest = string.Empty
            };
            quote = quote with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quote) };
        }
        else if (state.Selection?.Quote.Contacts is not null)
            return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.ContactsSelectionRequired);
        if (lifestyleSelections is not null)
        {
            if (resources is null || gear is null || qualities is null || skills is null)
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.GearSelectionRequired);
            if (state.LifestylesAuthority is not { } lifestyleAuthority || talent is null
                || !context.TryResolveCreationKarmaGrantSources(option.OptionId, talent.OptionId,
                    out var racial, out var talentSource))
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.LifestylesAuthorityRequired);
            var lifestyles = CharacterCreationKarmaLifestylesRules.EvaluateForFoundation(lifestyleAuthority, quote,
                racial, talentSource, lifestyleSelections, startingLifestyleId);
            if (!context.TryResolveCreationLifestylesAuthority(out var currentAuthority)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(lifestyleAuthority, currentAuthority))
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            blockers = blockers.Concat(lifestyles?.Blockers ?? [CharacterCreationLifestylesBlockers.InvalidMutation])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            quote = quote with
            {
                Lifestyles = lifestyles,
                CanSelect = blockers.Length == 0,
                Blockers = blockers,
                KarmaBudget = quote.KarmaBudget with { Blockers = blockers },
                SourceAnchorIds = quote.SourceAnchorIds.Concat(lifestyles?.Budget.SourceAnchorIds ?? [])
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                QuoteDigest = string.Empty
            };
            quote = quote with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quote) };
        }
        else if (state.Selection?.Quote.Lifestyles is not null || startingLifestyleId is not null)
            return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.LifestylesSelectionRequired);
        if (magicSelections is not null)
        {
            if (state.MagicCatalog is not { } magicCatalog || talent is null
                || !context.TryResolveCreationKarmaGrantSources(option.OptionId, talent.OptionId,
                    out var magicRacial, out var magicTalent))
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.MagicAuthorityRequired);
            var magic = CharacterCreationKarmaMagicSelectionRules.Evaluate(magicCatalog, quote, magicRacial, magicTalent, magicSelections);
            if (!context.TryResolveCreationKarmaMagicCatalog(out var currentCatalog)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(magicCatalog, currentCatalog))
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            blockers = blockers.Concat(magic?.Blockers ?? [CharacterCreationMagicResonanceBlockers.OptionInvalid])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            quote = quote with
            {
                Magic = magic, CanSelect = blockers.Length == 0, Blockers = blockers,
                KarmaBudget = Budget((int)state.KarmaBudget.Total, quote.KarmaBudget.Used + (magic?.Cost.TotalKarma ?? 0), blockers)
                    with { IsExact = quote.KarmaBudget.IsExact && magic is not null },
                SourceAnchorIds = quote.SourceAnchorIds.Concat(magicCatalog.Policy.SourceAnchorIds)
                    .Concat(magic?.Sources.SelectMany(source => source.SourceAnchorIds) ?? [])
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), QuoteDigest = string.Empty
            };
            quote = quote with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quote) };
        }
        else if (state.Selection?.Quote.Magic is not null)
            return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.MagicSelectionRequired);
        return new(CharacterCreationFoundationOutcomes.Success, quote, blockers);
    }

    private static CharacterCreationBudgetState Budget(int total, decimal used, IReadOnlyList<string> blockers)
        => new(CharacterCreationBudgetIds.Karma, "Creation Karma", total, used, total - used,
            true, blockers, "karma");

    private static CharacterCreationFoundationResult<T> Blocked<T>(string blocker) where T : class
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);
}
