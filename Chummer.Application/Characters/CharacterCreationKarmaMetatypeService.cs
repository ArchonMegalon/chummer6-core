using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Source-owned entry to Karma foundation. Never converts Karma into
/// a Priority or Life Modules draft, writes character XML, or grants free points.
/// Only its own pending selections may coexist with bootstrap; later spending is not ignored.
/// </summary>
public sealed class CharacterCreationKarmaMetatypeService(
    IWorkspaceStore workspaceStore,
    ICharacterSourceDataResolver sourceDataResolver) : ICharacterCreationKarmaMetatypeService
{
    private readonly IWorkspaceStore _workspaceStore = workspaceStore
        ?? throw new ArgumentNullException(nameof(workspaceStore));
    private readonly ICharacterSourceDataResolver _sourceDataResolver = sourceDataResolver
        ?? throw new ArgumentNullException(nameof(sourceDataResolver));

    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> Load(
        CharacterWorkspaceId workspaceId, bool includeSkills = false, bool includeQualities = false)
    {
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

            CharacterCreationKarmaQualitiesCatalog? qualitiesCatalog = null;
            if (includeQualities || decisions?.LastOrDefault()?.Quote.Qualities is not null)
            {
                if (!context.TryResolveCreationKarmaQualities(out qualitiesCatalog)) qualitiesCatalog = null;
                if (qualitiesCatalog is not null && (!CharacterCreationKarmaQualitiesRules.IsValidCatalog(qualitiesCatalog)
                    || qualitiesCatalog.Policy.SettingsProfileId != profile.SettingsProfileId
                    || qualitiesCatalog.Policy.RawProfileInputsDigest != profile.RawProfileInputsDigest)) qualitiesCatalog = null;
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
                resourcesPolicy?.AuthorityDigest, qualitiesCatalog?.Policy.AuthorityDigest, qualitiesCatalog?.CatalogDigest);
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
                        CharacterCreationKarmaQualitiesRules.Evaluate(qualitiesCatalog, selection.Quote.Metatype,
                            selection.Quote.Talent, selection.Command.QualityOptionIds!))))
                return Blocked<CharacterCreationKarmaMetatypeState>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            string[] anchors = profile.SourceAnchorIds.Concat(catalog.SourceContext.SourceAnchorIds)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var state = new CharacterCreationKarmaMetatypeState(
                CharacterCreationKarmaMetatypeSchemas.SnapshotV1, binding, profile.SettingsProfileId,
                Budget(profile.BuildPoints.Value, selection?.Quote.KarmaBudget.Used ?? 0, []),
                catalog.Options.ToArray(), anchors, string.Empty, selection, attributePolicy, talents, skillsPolicy, skillsCatalog,
                resourcesPolicy, qualitiesCatalog);
            state = state with
            {
                SnapshotDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(state)
            };
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
        CharacterWorkspaceId workspaceId, bool includeSkills = false, bool includeQualities = false)
    {
        var loaded = Load(workspaceId, includeSkills, includeQualities);
        if (loaded.Value is not { } state)
            return new(loaded.Outcome, null, loaded.Blockers);
        if (state.Selection is not { Command: { } saved })
            return new(loaded.Outcome, new(state, null), loaded.Blockers);

        // Only this service's freshly admitted snapshot reaches PreviewLoaded.
        // The persisted decision has an older binding: never return its quote
        // as a current review or accept a caller-supplied snapshot as authority.
        var preview = PreviewLoaded(state, saved.MetatypeOptionId, saved.TalentOptionId,
            saved.AttributeAllocations, saved.SkillsSelection, saved.ResourceKarmaInvestment, saved.QualityOptionIds);
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

    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote> Preview(
        CharacterCreationKarmaMetatypeBinding binding, string metatypeOptionId, string? talentOptionId = null,
        IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? attributeAllocations = null,
        CharacterCreationKarmaSkillsSelection? skillsSelection = null, decimal? resourceKarmaInvestment = null,
        IReadOnlyList<string>? qualityOptionIds = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> loaded = Load(binding.WorkspaceId,
            includeSkills: skillsSelection is not null || binding.SkillsCatalogDigest is not null || binding.SkillsPolicyDigest is not null,
            includeQualities: qualityOptionIds is not null || binding.QualitiesCatalogDigest is not null || binding.QualitiesPolicyDigest is not null);
        if (loaded.Value is not { } state)
            return new(loaded.Outcome, null, loaded.Blockers);
        if (state.Binding != binding)
            return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);

        return PreviewLoaded(state, metatypeOptionId, talentOptionId, attributeAllocations, skillsSelection, resourceKarmaInvestment, qualityOptionIds);
    }

    private static CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote> PreviewLoaded(
        CharacterCreationKarmaMetatypeState state, string metatypeOptionId, string? talentOptionId,
        IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? attributeAllocations,
        CharacterCreationKarmaSkillsSelection? skillsSelection, decimal? resourceKarmaInvestment,
        IReadOnlyList<string>? qualityOptionIds)
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
            if (state.QualitiesCatalog is null)
                return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.QualitiesAuthorityRequired);
            qualities = CharacterCreationKarmaQualitiesRules.Evaluate(state.QualitiesCatalog, option, talent, qualityOptionIds);
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
        string[] blockers = (attributes?.Blockers ?? []).Concat(qualities?.Blockers ?? []).Concat(skills?.Blockers ?? []).Concat(resources?.Blockers ?? []).Concat(used <= state.KarmaBudget.Total
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
                .Concat(skills?.Basis.Catalog.ActiveSkills.Concat(skills.Basis.Catalog.KnowledgeSkills)
                    .SelectMany(skill => skill.SourceAnchorIds) ?? [])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), string.Empty, talent, attributes, skills, resources, qualities);
        quote = quote with
        {
            QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quote)
        };
        return new(CharacterCreationFoundationOutcomes.Success, quote, blockers);
    }

    private static CharacterCreationBudgetState Budget(int total, decimal used, IReadOnlyList<string> blockers)
        => new(CharacterCreationBudgetIds.Karma, "Creation Karma", total, used, total - used,
            true, blockers, "karma");

    private static CharacterCreationFoundationResult<T> Blocked<T>(string blocker) where T : class
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);
}
