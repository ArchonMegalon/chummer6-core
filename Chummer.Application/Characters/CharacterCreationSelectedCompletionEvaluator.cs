using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Selected historical replay only. Capture validates the complete admitted
/// graph before reducing it; replay reads original choices from the real archive
/// and independently prices both the original and proposed selections. Neither
/// operation establishes current source, owner or persistence authority.
/// </summary>
internal static class CharacterCreationSelectedCompletionEvaluator
{
    internal const string Invalid = "creation-selected-completion-witness-invalid";
    internal const string HistoryMismatch = "creation-selected-completion-history-mismatch";

    internal static string ComputeWitnessDigest(CharacterCreationSelectedCompletionWitness witness)
        => CharacterCreationFinalizationDigest.Compute(witness with { WitnessDigest = string.Empty });

    internal static string ComputeReplayDigest(CharacterCreationSelectedCompletionEvaluation evaluation)
        => CharacterCreationFinalizationDigest.Compute(evaluation with { ReplayDigest = string.Empty });

    internal static CharacterCreationFoundationResult<CharacterCreationSelectedCompletionWitness> Capture(
        WorkspaceStoredDocument original, CharacterCreationCompletionBootstrapSnapshot bootstrap,
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            inputs = Detach(inputs);
            request = Detach(request);
            var reviewed = CharacterCreationCompletionReviewService.Review(inputs, request, cancellationToken);
            if (reviewed.Outcome != CharacterCreationFoundationOutcomes.Success || reviewed.Value is not { } review
                || review.UpstreamBudget is null || review.Lifestyles is null)
                return new(CharacterCreationFoundationOutcomes.Blocked, null, reviewed.Blockers);
            var choices = request.Selections;
            var originalGear = inputs.Lifestyles.Gear.State.PendingDraft?.Lines.Select(row => row.OptionId).ToArray() ?? [];
            if (!CharacterCreationQualitiesRules.TryCaptureSelectedAuthority(inputs.Lifestyles.Qualities.Authority,
                    inputs.Lifestyles.Qualities.SelectedOptionIds, choices.QualityOptionIds, out var qualities)
                || !CharacterCreationGearRules.TryCaptureSelectedAuthority(inputs.Lifestyles.Gear.State.Authority,
                    originalGear, choices.Gear.Select(row => row.OptionId).ToArray(), out var gear))
                return Refuse<CharacterCreationSelectedCompletionWitness>(Invalid);
            var magic = CharacterCreationMagicResonanceService.CaptureSelectedMagicAuthority(
                inputs.Magic, choices.MagicSelections, cancellationToken);
            if (magic.Value is null) return new(magic.Outcome, null, magic.Blockers);
            CharacterCreationSelectedLoadoutAuthority? loadout = null;
            if (inputs.Loadout is { } loadoutInputs
                && (choices.Loadout is null || !CharacterCreationLoadoutRules.TryCaptureSelectedAuthority(
                    loadoutInputs.Authority, loadoutInputs.OriginalSelections, choices.Loadout, out loadout)))
                return Refuse<CharacterCreationSelectedCompletionWitness>(Invalid);
            var candidate = new CharacterCreationSelectedCompletionWitness(
                loadout is null ? CharacterCreationSelectedCompletionWitness.SchemaV1
                    : CharacterCreationSelectedCompletionWitness.SchemaV2, inputs.SnapshotDigest, review.ReviewDigest,
                inputs.Skills, qualities!, magic.Value, gear!, inputs.Lifestyles.Gear.Resources,
                inputs.Lifestyles.Authority, inputs.Contacts.BootstrapBinding, inputs.Contacts.Policy,
                inputs.Contacts.CarryoverPolicy, inputs.Contacts.Binding.SourceDigest,
                review.UpstreamBudget.Qualities.ProposalDigest,
                CharacterCreationFinalizationDigest.Compute(request), string.Empty) { Loadout = loadout };
            candidate = candidate with { WitnessDigest = ComputeWitnessDigest(candidate) };
            var replay = Replay(original, bootstrap, candidate, request, cancellationToken);
            if (replay.Value is null || replay.Outcome != CharacterCreationFoundationOutcomes.Success)
                return new(replay.Outcome, null, replay.Blockers);
            // Capture also proves that the selected arithmetic reproduces the
            // fully admitted proposal, not merely an internally consistent slice.
            if (!MatchesFullReview(replay.Value, review))
                return Refuse<CharacterCreationSelectedCompletionWitness>(Invalid);
            return new(CharacterCreationFoundationOutcomes.Success, candidate, []);
        }
        catch (Exception error) when (IsMalformed(error))
        { return Refuse<CharacterCreationSelectedCompletionWitness>(Invalid); }
    }

    internal static CharacterCreationFoundationResult<CharacterCreationSelectedCompletionEvaluation> Replay(
        WorkspaceStoredDocument original, CharacterCreationCompletionBootstrapSnapshot bootstrap,
        CharacterCreationSelectedCompletionWitness witness, CharacterCreationCompletionReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(witness);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            witness = Detach(witness);
            request = Detach(request);
            if (!IsValidEnvelope(witness, request)
                || !TryReadOriginal(original, bootstrap, witness, out var baseline, out var effects)
                || baseline is null || effects is null || !MatchesGrantedQualities(witness))
                return Refuse<CharacterCreationSelectedCompletionEvaluation>(HistoryMismatch);
            var choices = request.Selections;
            var preservation = CharacterCreationCompletionPreservationRules.Check(witness.Skills, baseline, choices);
            if (preservation.Count != 0) return new(CharacterCreationFoundationOutcomes.Blocked, null, preservation);
            var skillsResult = CharacterCreationSkillsService.PreviewWithProposedAttributes(witness.Skills,
                choices.AttributeAllocations, choices.SkillAllocations, choices.GroupAllocations, cancellationToken);
            if (skillsResult.Value is not { } skills || skillsResult.Blockers.Count != 0)
                return new(CharacterCreationFoundationOutcomes.Blocked, null, skillsResult.Blockers);
            var session = CharacterCreationAttributesService.CreateDraftSession(witness.Skills.Attributes);
            var originalAllocations = witness.Skills.Attributes.Attributes.Select(row =>
                new CharacterCreationAttributeAllocation(row.AttributeId, row.PriorityPointsSpent, row.KarmaLevels)).ToArray();
            var originalAttributes = session?.Preview(originalAllocations).Value;
            if (originalAttributes is null
                || originalAttributes.Blockers.Any(value => value != CharacterCreationAttributesBlockers.DraftDuplicate)
                || !Same(originalAttributes.Attributes, witness.Skills.Attributes.Attributes)
                || !TryKarma(originalAttributes.CreationKarmaBudget.Used, out int originalAttributeCost)
                || !TryKarma(skills.Attributes.CreationKarmaBudget.Used, out int attributeCost)
                || !CharacterCreationQualitiesRules.TryEvaluateSelectedAuthority(witness.Qualities,
                    baseline.QualityOptionIds, witness.Skills.PrerequisiteDraft.CreationKarmaTotal,
                    originalAttributeCost, out var originalQualities)
                || !CharacterCreationQualitiesRules.TryEvaluateSelectedAuthority(witness.Qualities,
                    choices.QualityOptionIds, witness.Skills.PrerequisiteDraft.CreationKarmaTotal,
                    attributeCost, out var qualities)
                || originalQualities is null || qualities is null
                || originalQualities.Blockers.Count != 0 || qualities.Blockers.Count != 0)
                return Refuse<CharacterCreationSelectedCompletionEvaluation>(Invalid);
            var magicResult = CharacterCreationMagicResonanceService.EvaluateSelectedMagic(witness.Magic,
                witness.Skills.PrerequisiteDraft, originalAttributes.Attributes, skills.Attributes.Attributes, cancellationToken);
            if (magicResult.Value is not { } magic || magicResult.Blockers.Count != 0)
                return new(CharacterCreationFoundationOutcomes.Blocked, null, magicResult.Blockers);
            if (!MatchesOriginalPrices(original, witness, originalQualities, magic.Original)
                || !MatchesOriginalPurchases(original, witness, baseline, originalQualities,
                    originalAttributes.Attributes, effects, cancellationToken))
                return Refuse<CharacterCreationSelectedCompletionEvaluation>(HistoryMismatch);
            if (!CharacterCreationPriorityKarmaBudget.TryEvaluate(qualities.KarmaRemaining, 0,
                    magic.Proposed.MysticAdeptPowerPoints, out int availableKarma))
                return Refuse<CharacterCreationSelectedCompletionEvaluation>(CharacterCreationFinalizationBlockers.GlobalKarmaExceeded);

            var resourcesInput = new SelectedCompletionResourcesPricingInput(witness.Resources.Authority,
                witness.Resources.PriorityOption, availableKarma, choices.TotalKarmaInvestment, true, 0m, []);
            var funding = CharacterCreationResourcesService.PriceSelectedCompletion(resourcesInput, cancellationToken);
            if (funding.Value is not { } initialFunding || funding.Blockers.Count != 0)
                return new(CharacterCreationFoundationOutcomes.Blocked, null, funding.Blockers);
            if (!CharacterCreationGearRules.TryProjectSelectedBasket(choices.Gear, witness.Gear,
                    initialFunding.Budget.TotalStartingNuyen, out var gear, out var gearBudget, out var gearBlockers))
                return new(CharacterCreationFoundationOutcomes.Blocked, null, gearBlockers);
            string[] gearAnchors = gear.SelectMany(row => row.SourceAnchorIds).Concat(CharacterCreationGearSourceAnchors.All)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            CharacterCreationLoadoutCompletionProjection? loadout = null;
            if (witness.Loadout is { } loadoutAuthority)
            {
                if (!CharacterCreationLoadoutRules.TryProjectSelectedBasket(choices.Loadout!, loadoutAuthority,
                        out var loadoutLines, out decimal loadoutCost, out var loadoutBlockers))
                    return new(CharacterCreationFoundationOutcomes.Blocked, null, loadoutBlockers);
                loadout = CharacterCreationLoadoutCompletionService.BuildProjection(
                    choices.Loadout!, loadoutLines, loadoutCost, original.Document.AuxiliaryState.CharacterCreationLoadoutDraft?.Instances);
            }
            decimal afterLoadout = checked(gearBudget.RemainingNuyen - (loadout?.PurchaseCost ?? 0m));
            if (afterLoadout < 0m)
                return Refuse<CharacterCreationSelectedCompletionEvaluation>(CharacterCreationResourcesBlockers.InsufficientNuyen);
            string[] purchaseAnchors = loadout is null ? gearAnchors : gearAnchors
                .Concat(loadout.Lines.SelectMany(line => line.SourceAnchorIds))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var lifestyles = CharacterCreationLifestylesService.PriceSelectedCompletion(new(witness.Lifestyles,
                witness.Skills.PrerequisiteDraft, witness.Magic.Talent.GrantedQualitySources ?? [], qualities.Selections,
                witness.ProposedQualityProjectionProvenanceSeed, choices.Lifestyles, choices.StartingLifestyleId,
                afterLoadout, purchaseAnchors, []), cancellationToken);
            if (lifestyles.Value is not { } lifestylePrice || lifestyles.Blockers.Count != 0)
                return new(CharacterCreationFoundationOutcomes.Blocked, null, lifestyles.Blockers);
            var finalFunding = CharacterCreationResourcesService.PriceSelectedCompletion(resourcesInput with
                { FullPurchaseCost = checked(gearBudget.BasketCost + lifestylePrice.FullLifestyleCost
                    + (loadout?.PurchaseCost ?? 0m)) }, cancellationToken);
            if (finalFunding.Value is not { } resources || finalFunding.Blockers.Count != 0)
                return new(CharacterCreationFoundationOutcomes.Blocked, null, finalFunding.Blockers);
            var contactResult = CharacterCreationContactsService.PriceSelectedCompletion(new(witness.ContactsPolicy,
                witness.Skills.PrerequisiteDraft, witness.Magic.Talent.GrantedQualitySources ?? [], effects, baseline.Contacts,
                skills.Attributes.Attributes, qualities.Selections, choices.Contacts,
                witness.ContactsSourceProvenanceSeed, []), cancellationToken);
            if (contactResult.Value is not { AdditionalKarmaCost: int contactCost } contacts || contactResult.Blockers.Count != 0)
                return new(CharacterCreationFoundationOutcomes.Blocked, null, contactResult.Blockers);
            int remaining = checked(resources.RemainingCreationKarma - contactCost);
            if (contactCost < 0 || remaining < 0)
                return Refuse<CharacterCreationSelectedCompletionEvaluation>(CharacterCreationFinalizationBlockers.GlobalKarmaExceeded);
            var balances = new CharacterCreationCompletionPoolBalances(witness.Skills.PrerequisiteDraft.CreationKarmaTotal,
                qualities.KarmaRemaining, 0, magic.Proposed.MysticAdeptPowerPoints?.KarmaCost ?? 0,
                resources.SelectedOption.KarmaInvestment, contactCost, remaining, 0,
                resources.Budget.TotalStartingNuyen, gearBudget.BasketCost, lifestylePrice.FullLifestyleCost,
                resources.Budget.RemainingNuyen, resources.Budget.Overspend)
                { LoadoutCost = loadout?.PurchaseCost };
            var result = new CharacterCreationSelectedCompletionEvaluation(witness.WitnessDigest, skills,
                originalQualities, qualities, magic, resources, gear, gearBudget, gearAnchors,
                lifestylePrice, contacts, balances, string.Empty) { Loadout = loadout };
            result = result with { ReplayDigest = ComputeReplayDigest(result) };
            cancellationToken.ThrowIfCancellationRequested();
            return new(CharacterCreationFoundationOutcomes.Success, result, []);
        }
        catch (Exception error) when (IsMalformed(error))
        { return Refuse<CharacterCreationSelectedCompletionEvaluation>(Invalid); }
    }

    private static bool IsValidEnvelope(CharacterCreationSelectedCompletionWitness witness,
        CharacterCreationCompletionReviewRequest request)
    {
        if (!HasMatchingLoadoutSchema(witness, request)
            || witness.Skills?.Attributes?.Attributes is null || witness.Skills.Attributes.Binding is null
            || witness.Skills.Binding is null || witness.Skills.Authority is null || witness.Skills.PrerequisiteDraft is null
            || witness.Skills.Attributes.Attributes.Any(row => row is null)
            || witness.Skills.Authority.ActiveSkills is null || witness.Skills.Authority.KnowledgeSkills is null
            || witness.Skills.Authority.SkillGroups is null || witness.Skills.Allocations is null
            || witness.Skills.GroupAllocations is null
            || witness.Skills.Authority.ActiveSkills.Any(row => row is null)
            || witness.Skills.Authority.KnowledgeSkills.Any(row => row is null)
            || witness.Skills.Authority.SkillGroups.Any(row => row?.MemberSkillSourceIds is null)
            || witness.Skills.PrerequisiteDraft.Assignments is null
            || witness.Skills.PrerequisiteDraft.Assignments.Any(row => row is null)
            || witness.Resources?.Authority is null || witness.Resources.Binding is null
            || witness.Resources.Receipts is null || witness.Resources.LifestyleReceipts is null
            || witness.Lifestyles is null || witness.BootstrapBinding is null
            || witness.ContactsPolicy is null || witness.CarryoverPolicy is null || request.Selections is null
            || request.Selections.AttributeAllocations is null || request.Selections.SkillAllocations is null
            || request.Selections.GroupAllocations is null || request.Selections.QualityOptionIds is null
            || request.Selections.MagicSelections is null || request.Selections.Gear is null
            || request.Selections.Contacts is null || request.Selections.Lifestyles is null
            || request.Selections.AttributeAllocations.Any(row => row is null)
            || request.Selections.SkillAllocations.Any(row => row is null)
            || request.Selections.GroupAllocations.Any(row => row is null)
            || request.Selections.QualityOptionIds.Any(row => row is null)
            || request.Selections.Contacts.Any(row => row is null)
            || request.Selections.Lifestyles.Any(row => row is null)
            || request.Selections.Gear.Any(row => row is null)
            || request.Selections.MagicSelections.AdeptPowers is null || request.Selections.MagicSelections.Spells is null
            || request.Selections.MagicSelections.ComplexForms is null
            || request.Selections.MagicSelections.AdeptPowers.Any(row => row?.Identity is null)
            || request.Selections.MagicSelections.Spells.Any(row => row is null)
            || request.Selections.MagicSelections.ComplexForms.Any(row => row is null)
            || !CharacterCreationFinalizationDigest.IsCanonical(witness.ProvenanceInputsSnapshotDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(witness.ProvenanceReviewDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(witness.ContactsSourceProvenanceSeed)
            || !CharacterCreationFinalizationDigest.IsCanonical(witness.ProposedQualityProjectionProvenanceSeed)
            || witness.ProvenanceInputsSnapshotDigest != request.InputsSnapshotDigest
            || witness.ProposalDigest != CharacterCreationFinalizationDigest.Compute(request)
            || witness.WitnessDigest != ComputeWitnessDigest(witness)
            || !CharacterCreationQualitiesRules.IsValidSelectedAuthority(witness.Qualities)
            || !CharacterCreationMagicResonanceService.IsValidSelectedMagicAuthority(witness.Magic)
            || !CharacterCreationGearRules.IsValidSelectedAuthority(witness.Gear)
            || !CharacterCreationResourcesRules.IsValidAuthority(witness.Resources.Authority)
            || !CharacterCreationResourcesRules.IsValidPriorityOption(witness.Resources.PriorityOption)
            || !CharacterCreationLifestylesRules.IsValidAuthority(witness.Lifestyles)
            || !CharacterCreationKarmaContactsRules.IsValidPolicy(witness.ContactsPolicy)
            || !CharacterCreationKarmaFinalizationBudgetRules.IsValidPolicy(witness.CarryoverPolicy)
            || witness.Resources.SnapshotDigest != CharacterCreationResourcesCompletionRules.ComputeInputsDigest(witness.Resources)
            || !witness.Resources.PurchaseAuthorityExact
            || !Same(witness.Skills.PrerequisiteDraft, witness.Resources.PrerequisiteDraft)) return false;
        string profile = witness.BootstrapBinding.SettingsProfileId;
        var talentAccess = witness.Skills.Authority.TalentAccess;
        return witness.Skills.Authority.SettingsProfileId == profile && witness.Qualities.SettingsProfileId == profile
            && witness.Magic.SettingsProfileId == profile && witness.Gear.SettingsProfileId == profile
            && witness.Resources.Authority.SettingsProfileId == profile && witness.Lifestyles.SettingsProfileId == profile
            && (witness.Loadout is null || witness.Loadout.SettingsProfileId == profile
                && witness.Loadout.ProfileDigest == witness.BootstrapBinding.RawProfileInputsDigest)
            && witness.Skills.Authority.RawProfileInputsDigest == witness.BootstrapBinding.RawProfileInputsDigest
            && witness.Qualities.ProfileDigest == witness.BootstrapBinding.RawProfileInputsDigest
            && witness.Gear.ProfileDigest == witness.BootstrapBinding.RawProfileInputsDigest
            && witness.Resources.Authority.ProfileDigest == witness.BootstrapBinding.RawProfileInputsDigest
            && witness.Lifestyles.ProfileDigest == witness.BootstrapBinding.RawProfileInputsDigest
            && witness.ContactsPolicy.SettingsProfileId == profile && witness.CarryoverPolicy.SettingsProfileId == profile
            && witness.ContactsPolicy.RawProfileInputsDigest == witness.BootstrapBinding.RawProfileInputsDigest
            && witness.CarryoverPolicy.RawProfileInputsDigest == witness.BootstrapBinding.RawProfileInputsDigest
            && (talentAccess is null || Same(talentAccess.Talent, witness.Magic.Talent)
                && talentAccess.TalentRuntimeDigest == witness.Magic.RuntimeDigest)
            && witness.Qualities.ProposedOptionIds.SequenceEqual(request.Selections.QualityOptionIds.Order(StringComparer.Ordinal))
            && witness.Gear.ProposedOptionIds.SequenceEqual(request.Selections.Gear.Select(row => row.OptionId).Order(StringComparer.Ordinal))
            && Same(witness.Magic.ProposedSelections, NormalizeMagic(request.Selections.MagicSelections));
    }

    private static bool HasMatchingLoadoutSchema(CharacterCreationSelectedCompletionWitness witness,
        CharacterCreationCompletionReviewRequest request)
        => witness.Schema switch
        {
            CharacterCreationSelectedCompletionWitness.SchemaV1
                => witness.Loadout is null && request.Selections?.Loadout is null,
            CharacterCreationSelectedCompletionWitness.SchemaV2
                => witness.Loadout is not null && request.Selections?.Loadout is not null
                    && CharacterCreationLoadoutRules.IsValidSelectedAuthority(witness.Loadout)
                    && Same(witness.Loadout.ProposedSelections, request.Selections.Loadout),
            _ => false
        };

    private static bool TryReadOriginal(WorkspaceStoredDocument original,
        CharacterCreationCompletionBootstrapSnapshot bootstrap, CharacterCreationSelectedCompletionWitness witness,
        out CharacterCreationCompletionOriginalSelections? baseline, out IReadOnlyList<CharacterContactRuleEffect>? effects)
    {
        baseline = null; effects = null;
        var auxiliary = original.Document.AuxiliaryState;
        var binding = witness.Skills.Binding;
        string rawDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(original.Document.Content);
        if (original.Document.RulesetId != RulesetDefaults.Sr5 || original.Document.Format != WorkspaceDocumentFormat.NativeXml
            || original.ContentRevision <= 0 || original.ContentRevision != original.SavedRevision
            || original.Id != binding.WorkspaceId || original.ContentRevision != binding.ContentRevision
            || original.SavedRevision != binding.SavedRevision || rawDigest != binding.RawCharacterXmlDigest
            || original.Document.AuxiliaryStateDigest != binding.AuxiliaryStateDigest
            || auxiliary.CharacterCreationFoundationDraft is not null || auxiliary.LifeModuleDecisionAcceptances is not null
            || auxiliary.CharacterCreationFinalizationArchive is not null
            || !WorkspaceAuxiliaryStateIntegrity.IsValidShape(original.Id, original.ContentRevision, auxiliary)
            || !bootstrap.IsValid() || bootstrap.RawCharacterXmlDigest != rawDigest
            || !CharacterCreationBootstrapAuthority.TryPrepareBinding(original.Id, original.Document,
                bootstrap.CreateFrozenContext(), out var prepared, out _, out _)
            || !Same(prepared, auxiliary.CharacterCreationBootstrapBinding) || !Same(prepared, witness.BootstrapBinding)
            || !Same(auxiliary.CharacterCreationPrerequisiteDraft, witness.Skills.PrerequisiteDraft)
            || !CharacterCreationPrerequisiteDraftIntegrity.IsValidPending(auxiliary.CharacterCreationPrerequisiteDraft,
                original.Id, original.ContentRevision, rawDigest, bootstrap.Prerequisite)
            || !Same(auxiliary.CharacterCreationAttributesDraft, witness.Skills.Attributes.PendingDraft)
            || !Same(auxiliary.CharacterCreationSkillsDraft, witness.Skills.SavedSkillsState?.PendingDraft)
            || !Same(auxiliary.CharacterCreationResourcesDraft, witness.Resources.PendingDraft)
            || !Same(auxiliary.CharacterCreationResourcesReceipts ?? [], witness.Resources.Receipts)
            || !Same(auxiliary.CharacterCreationLoadoutDraft, witness.Resources.LoadoutDraft)
            || !Same(auxiliary.CharacterCreationLoadoutReceipts, witness.Resources.LoadoutReceipts)
            || !Same(auxiliary.CharacterCreationLifestylesDraft, witness.Resources.LifestylesDraft)
            || !Same(auxiliary.CharacterCreationLifestyleReceipts ?? [], witness.Resources.LifestyleReceipts)) return false;
        var resourceBinding = witness.Resources.Binding;
        if (resourceBinding.WorkspaceId != original.Id || resourceBinding.ContentRevision != original.ContentRevision
            || resourceBinding.SavedRevision != original.SavedRevision || resourceBinding.RawCharacterXmlDigest != rawDigest
            || resourceBinding.AuxiliaryStateDigest != original.Document.AuxiliaryStateDigest
            || resourceBinding.AuthorityDigest != witness.Resources.Authority.AuthorityDigest) return false;
        var root = XDocument.Parse(original.Document.Content).Root;
        if (root?.Name != "character" || root.Elements("contacts").Count() > 1
            || root.Element("contacts")?.Elements().Any() == true) return false;
        var savedLoadout = auxiliary.CharacterCreationLoadoutDraft;
        if (savedLoadout is not null && witness.Loadout is null) return false;
        if (witness.Loadout is not null && (!IsEmptyOwnedContainer(root, "weapons")
            || !IsEmptyOwnedContainer(root, "armors")
            || !Same(witness.Loadout.OriginalSelections, savedLoadout?.Selections ?? CharacterCreationLoadoutSelections.Empty)
            || savedLoadout is not null && (savedLoadout.RawCharacterXmlDigest != rawDigest
                || savedLoadout.Authority.AuthorityDigest != witness.Loadout.AuthorityDigest
                || !CharacterCreationLoadoutRules.TryProjectSelectedBasket(savedLoadout.Selections, witness.Loadout,
                    out var originalLines, out decimal originalCost, out _)
                || originalCost != savedLoadout.PurchaseCost || !Same(originalLines, savedLoadout.Lines)))) return false;
        effects = CharacterContactEditSemanticsResolver.ReadEffects(root);
        var magic = auxiliary.CharacterCreationMagicResonanceDraft?.Selections ?? new(null, null, [], [], []);
        baseline = new(magic, auxiliary.CharacterCreationQualitiesDraft?.SelectedOptionIds ?? [],
            auxiliary.CharacterCreationGearDraft?.Lines.Select(row => new CharacterCreationGearSelection(row.OptionId, row.Quantity)).ToArray() ?? [],
            auxiliary.CharacterCreationResourcesDraft?.KarmaInvestment ?? 0,
            auxiliary.CharacterCreationContactsDraft?.Contacts ?? [], auxiliary.CharacterCreationLifestylesDraft?.Lifestyles ?? [])
            { Loadout = witness.Loadout is null ? null : savedLoadout?.Selections ?? CharacterCreationLoadoutSelections.Empty };
        return witness.Magic.OriginalHadSavedMagic == (auxiliary.CharacterCreationMagicResonanceDraft is not null)
            && Same(witness.Magic.OriginalSelections, NormalizeMagic(magic))
            && witness.Qualities.OriginalOptionIds.SequenceEqual(baseline.QualityOptionIds.Order(StringComparer.Ordinal))
            && witness.Gear.OriginalOptionIds.SequenceEqual(baseline.Gear.Select(row => row.OptionId).Order(StringComparer.Ordinal));
    }

    private static bool IsEmptyOwnedContainer(XElement root, string name)
    {
        var containers = root.Elements(name).ToArray();
        return containers.Length <= 1 && containers.All(value => !value.HasElements
            && !value.HasAttributes && string.IsNullOrWhiteSpace(value.Value));
    }

    private static bool MatchesOriginalPrices(WorkspaceStoredDocument original,
        CharacterCreationSelectedCompletionWitness witness, CharacterCreationQualitiesCompletionEvaluation qualities,
        CharacterCreationSelectedMagicEvaluation magic)
    {
        var state = original.Document.AuxiliaryState;
        if (state.CharacterCreationQualitiesDraft is { } qualityDraft
            && (qualityDraft.AuthorityDigest != witness.Qualities.ProvenanceAuthorityDigest
                || qualityDraft.RuntimeDigest != witness.Qualities.RuntimeDigest
                || qualityDraft.BaseRawCharacterXmlDigest != witness.Skills.Binding.RawCharacterXmlDigest
                || qualityDraft.PrerequisiteDraftRevision != witness.Skills.PrerequisiteDraft.DraftRevision
                || qualityDraft.PrerequisiteDraftDigest != witness.Skills.PrerequisiteDraft.DraftDigest
                || qualityDraft.AttributesDraftRevision != witness.Skills.Attributes.PendingDraft?.DraftRevision
                || qualityDraft.AttributesDraftDigest != witness.Skills.Attributes.PendingDraft?.DraftDigest
                || !qualityDraft.SelectedOptionIds.SequenceEqual(qualities.Selections.Select(row => row.OptionId), StringComparer.Ordinal)
                || !Same(qualityDraft.Selections, qualities.Selections)
                || !Same(qualityDraft.SourceAnchorIds, qualities.SourceAnchorIds)
                || qualityDraft.PositiveKarmaUsed != qualities.PositiveQualityBudget.Used
                || qualityDraft.NegativeKarmaUsed != qualities.NegativeQualityBudget.Used
                || qualityDraft.KarmaRemaining != qualities.KarmaRemaining)) return false;
        if (state.CharacterCreationMagicResonanceDraft is { } magicDraft
            && (magicDraft.AuthorityDigest != witness.Magic.ProvenanceAuthorityDigest
                || magicDraft.RuntimeDigest != witness.Magic.RuntimeDigest
                || magicDraft.BaseRawCharacterXmlDigest != witness.Skills.Binding.RawCharacterXmlDigest
                || magicDraft.PrerequisiteDraftDigest != witness.Skills.PrerequisiteDraft.DraftDigest
                || magicDraft.PrerequisiteAuthorityDigest != witness.Skills.PrerequisiteDraft.AuthorityDigest
                || magicDraft.AttributesDraftRevision != witness.Skills.Attributes.PendingDraft?.DraftRevision
                || magicDraft.AttributesDraftDigest != witness.Skills.Attributes.PendingDraft?.DraftDigest
                || magicDraft.SourceInputsDigest != witness.Magic.SourceInputsDigest
                || magicDraft.CustomDataInputsDigest != witness.Magic.CustomDataInputsDigest
                || magicDraft.GmPolicyDigest != witness.Magic.GmPolicyDigest
                || !Same(magicDraft.TraditionBudget, magic.TraditionBudget)
                || !Same(magicDraft.StreamBudget, magic.StreamBudget)
                || !Same(magicDraft.AdeptPowerPointBudget, magic.AdeptPowerPointBudget)
                || !Same(magicDraft.SpellBudget, magic.SpellBudget)
                || !Same(magicDraft.ComplexFormBudget, magic.ComplexFormBudget))) return false;
        var assignment = witness.Skills.PrerequisiteDraft.Assignments.Where(row =>
            row.CategoryId == CharacterCreationPriorityCategoryIds.Resources).ToArray();
        if (assignment.Length != 1 || assignment[0].SourceId != witness.Resources.PriorityOption.SourceId
            || assignment[0].Rank != witness.Resources.PriorityOption.Rank
            || !Same(witness.Resources.PriorityOption, witness.Resources.Authority.PriorityOptions.SingleOrDefault(row =>
                row.SourceId == witness.Resources.PriorityOption.SourceId))) return false;
        return true;
    }

    private static bool MatchesGrantedQualities(CharacterCreationSelectedCompletionWitness witness)
    {
        // The supported pending Priority/Sum-to-Ten source resolver has no base
        // grants. Its only additions are the actual prerequisite's racial grants
        // and the selected Talent's source grants. A rehashed witness cannot add
        // another free quality or omit one of those independently derived rows.
        var prerequisite = witness.Skills.PrerequisiteDraft;
        var grants = new List<CharacterCreationGrantedQuality>();
        foreach (var source in prerequisite.HeritageSelection?.RacialQualitySources ?? [])
        {
            if (!CharacterCreationTalentQualityGrants.TryProject(source, prerequisite.DraftDigest, out var projected))
                return false;
            var racial = projected with { GrantId = "priority-racial:" + projected.GrantId, GrantDigest = string.Empty };
            grants.Add(racial with { GrantDigest = CharacterCreationQualitiesRules.ComputeGrantDigest(racial) });
        }
        foreach (var source in witness.Magic.Talent.GrantedQualitySources ?? [])
        {
            if (!CharacterCreationTalentQualityGrants.TryProject(source, prerequisite.DraftDigest, out var projected))
                return false;
            grants.Add(projected);
        }
        return Same(witness.Qualities.GrantedQualities, grants.OrderBy(row => row.GrantId, StringComparer.Ordinal).ToArray());
    }

    private static bool MatchesOriginalPurchases(WorkspaceStoredDocument original,
        CharacterCreationSelectedCompletionWitness witness, CharacterCreationCompletionOriginalSelections baseline,
        CharacterCreationQualitiesCompletionEvaluation qualities,
        IReadOnlyList<CharacterCreationAttributeProjection> attributes,
        IReadOnlyList<CharacterContactRuleEffect> effects, CancellationToken cancellationToken)
    {
        var state = original.Document.AuxiliaryState;
        var resources = witness.Resources;
        var resourceAuthority = resources.Authority;
        string raw = witness.Skills.Binding.RawCharacterXmlDigest;
        if (resources.Schema != CharacterCreationResourcesCompletionSchemas.InputsV1
            || resources.Binding.WorkspaceRevision != resources.Binding.ContentRevision
            || resources.Binding.PrerequisiteDraftRevision != witness.Skills.PrerequisiteDraft.DraftRevision
            || resources.Binding.PrerequisiteDraftDigest != witness.Skills.PrerequisiteDraft.DraftDigest
            || resources.Binding.SourceDigest != resourceAuthority.SourceDigest
            || resources.Binding.RulesDigest != resourceAuthority.RulesDigest
            || resources.Binding.RuntimeDigest != resourceAuthority.RuntimeDigest
            || resourceAuthority.BuildMethod != witness.Skills.PrerequisiteDraft.BuildMethod
            || !CharacterCreationResourcesService.TryGetRetainedCompletionLifestyleCost(resources, out decimal lifestyleCost)
            || !CharacterCreationResourcesService.TryGetRetainedCompletionLoadoutCost(resources, out decimal loadoutCost)
            || resources.KnownPurchaseCost != checked(lifestyleCost + loadoutCost + (state.CharacterCreationGearDraft?.Budget.BasketCost ?? 0m)))
            return false;
        if (state.CharacterCreationResourcesDraft is { } savedResources)
        {
            var price = CharacterCreationResourcesService.PriceSelectedCompletion(new(resourceAuthority,
                resources.PriorityOption, int.MaxValue, savedResources.KarmaInvestment, true,
                savedResources.Budget.KnownPurchaseCost, []), cancellationToken);
            if (price.Value is not { } priced || price.Blockers.Count != 0
                || savedResources.PrerequisiteDraftRevision != resources.Binding.PrerequisiteDraftRevision
                || savedResources.PrerequisiteDraftDigest != resources.Binding.PrerequisiteDraftDigest
                || savedResources.BaseRawCharacterXmlDigest != raw
                || savedResources.AuthorityDigest != resourceAuthority.AuthorityDigest
                || savedResources.SourceDigest != resourceAuthority.SourceDigest
                || savedResources.RulesDigest != resourceAuthority.RulesDigest
                || savedResources.RuntimeDigest != resourceAuthority.RuntimeDigest
                || savedResources.SelectedOptionId != priced.SelectedOption.OptionId
                || !Same(savedResources.SourceAnchorIds, priced.SelectedOption.SourceAnchorIds)
                || !Same(savedResources.Budget, priced.Budget)) return false;
            var contribution = savedResources.FinalizationContribution;
            if (contribution.PriorityRank != resources.PriorityOption.Rank
                || contribution.PrioritySourceId != resources.PriorityOption.SourceId
                // The retained contribution separates the Priority principal
                // from Karma conversion, exactly as the ordinary writer does.
                || contribution.StartingNuyen != resources.PriorityOption.BasePriorityNuyen
                || contribution.NuyenKarma != priced.SelectedOption.KarmaInvestment
                || contribution.ExpectedRawCharacterXmlDigest != raw
                || !Same(contribution.SourceAnchorIds, priced.SelectedOption.SourceAnchorIds)) return false;
        }
        if (state.CharacterCreationGearDraft is { } savedGear)
        {
            if (state.CharacterCreationResourcesDraft is not { } savedFunding
                || savedGear.BaseRawCharacterXmlDigest != raw
                || savedGear.AuthorityDigest != witness.Gear.ProvenanceAuthorityDigest
                || savedGear.SourceDigest != witness.Gear.SourceDigest
                || savedGear.RulesDigest != witness.Gear.RulesDigest
                || savedGear.RuntimeDigest != witness.Gear.RuntimeDigest
                || !TryOriginalGearFunding(savedGear, savedFunding, resources.Receipts, out decimal historicalTotal)
                || !CharacterCreationGearRules.TryProjectSelectedBasket(baseline.Gear, witness.Gear,
                    historicalTotal, out var lines, out var budget, out _)
                || !Same(savedGear.Lines, lines) || !Same(savedGear.Budget, budget)) return false;
            string[] anchors = lines.SelectMany(row => row.SourceAnchorIds).Concat(CharacterCreationGearSourceAnchors.All)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var contribution = new CharacterCreationGearFinalizationContribution(CharacterCreationGearSchemas.ContributionV1,
                raw, savedGear.ResourcesDraftRevision, savedGear.ResourcesDraftDigest, lines, budget.BasketCost, anchors, string.Empty);
            contribution = contribution with { ContributionDigest = CharacterCreationGearRules.ComputeContributionDigest(contribution) };
            if (!Same(savedGear.FinalizationContribution, contribution)) return false;
        }
        if (state.CharacterCreationContactsDraft is { } savedContacts)
        {
            if (!MatchesContactInputs(original, witness, savedContacts)
                || state.CharacterCreationContactReceipts is not { Count: > 0 } receipts
                || !Same(savedContacts, receipts[^1].Receipt.WritePlan.PendingDraft)) return false;
        }
        var contactPrice = CharacterCreationContactsService.PriceSelectedCompletion(new(witness.ContactsPolicy,
            witness.Skills.PrerequisiteDraft, witness.Magic.Talent.GrantedQualitySources ?? [], effects, baseline.Contacts,
            attributes, qualities.Selections, baseline.Contacts, witness.ContactsSourceProvenanceSeed, []), cancellationToken);
        if (contactPrice.Value is null || contactPrice.Blockers.Count != 0) return false;
        if (state.CharacterCreationLifestylesDraft is { } savedLifestyles
            && (!MatchesContactInputs(original, witness, savedLifestyles.InputAuthority)
                || savedLifestyles.InputAuthority.Contacts.Count != 0
                || savedLifestyles.SkillsDraftDigest != state.CharacterCreationSkillsDraft?.DraftDigest
                || savedLifestyles.ResourcesDraftDigest != state.CharacterCreationResourcesDraft?.DraftDigest
                || savedLifestyles.GearDraftDigest != state.CharacterCreationGearDraft?.DraftDigest
                || savedLifestyles.MagicDraftDigest != state.CharacterCreationMagicResonanceDraft?.DraftDigest)) return false;
        return true;
    }

    private static bool MatchesContactInputs(WorkspaceStoredDocument original,
        CharacterCreationSelectedCompletionWitness witness, CharacterCreationContactsDraft draft)
    {
        var state = original.Document.AuxiliaryState;
        return draft.WorkspaceId == original.Id
            && draft.RawCharacterXmlDigest == witness.Skills.Binding.RawCharacterXmlDigest
            && draft.PrerequisiteDraftDigest == state.CharacterCreationPrerequisiteDraft?.DraftDigest
            && draft.AttributesDraftDigest == state.CharacterCreationAttributesDraft?.DraftDigest
            && draft.QualitiesDraftDigest == state.CharacterCreationQualitiesDraft?.DraftDigest
            && Same(draft.Policy, witness.ContactsPolicy) && Same(draft.CarryoverPolicy, witness.CarryoverPolicy);
    }

    private static bool TryOriginalGearFunding(CharacterCreationGearDraft gear,
        CharacterCreationResourcesDraft current, IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry> receipts,
        out decimal total)
    {
        total = current.Budget.TotalStartingNuyen;
        if (gear.ResourcesDraftRevision == current.DraftRevision && gear.ResourcesDraftDigest == current.DraftDigest) return true;
        var historical = receipts.Select(row => row.Receipt).SingleOrDefault(row =>
            row.DraftRevision == gear.ResourcesDraftRevision && row.DraftDigest == gear.ResourcesDraftDigest);
        if (historical is null || historical.DraftRevision >= current.DraftRevision
            || historical.WorkspaceRevision > gear.BaseContentRevision
            || historical.RawCharacterXmlDigest != gear.BaseRawCharacterXmlDigest
            || historical.PrerequisiteDraftDigest != current.PrerequisiteDraftDigest
            || historical.AuthorityDigest != current.AuthorityDigest || historical.SourceDigest != current.SourceDigest
            || historical.RulesDigest != current.RulesDigest || historical.RuntimeDigest != current.RuntimeDigest) return false;
        total = historical.TotalStartingNuyen;
        return true;
    }

    private static bool MatchesFullReview(CharacterCreationSelectedCompletionEvaluation selected,
        CharacterCreationCompletionReview full)
        => Same(selected.Skills, full.Skills) && Same(selected.Balances, full.Balances)
            && Same(selected.Loadout, full.Loadout)
            && Same(selected.Qualities.Selections, full.UpstreamBudget!.Qualities.Qualities.Selections)
            && Same(selected.Qualities.GrantedQualities, full.UpstreamBudget.Qualities.Qualities.GrantedQualities)
            && Same(selected.Qualities.PositiveQualityBudget, full.UpstreamBudget.Qualities.Qualities.PositiveQualityBudget)
            && Same(selected.Qualities.NegativeQualityBudget, full.UpstreamBudget.Qualities.Qualities.NegativeQualityBudget)
            && Same(selected.Qualities.SourceAnchorIds, full.UpstreamBudget.Qualities.Qualities.SourceAnchorIds)
            && Same(selected.Magic.Proposed.Selections, full.UpstreamBudget.Magic.Selections)
            && Same(selected.Magic.Proposed.EffectiveAttributes, full.UpstreamBudget.Magic.EffectiveAttributes)
            && Same(selected.Magic.Proposed.TraditionBudget, full.UpstreamBudget.Magic.TraditionBudget)
            && Same(selected.Magic.Proposed.StreamBudget, full.UpstreamBudget.Magic.StreamBudget)
            && Same(selected.Magic.Proposed.AdeptPowerPointBudget, full.UpstreamBudget.Magic.AdeptPowerPointBudget)
            && Same(selected.Magic.Proposed.SpellBudget, full.UpstreamBudget.Magic.SpellBudget)
            && Same(selected.Magic.Proposed.ComplexFormBudget, full.UpstreamBudget.Magic.ComplexFormBudget)
            && Same(selected.Magic.Proposed.MysticAdeptPowerPoints, full.UpstreamBudget.Magic.MysticAdeptPowerPoints)
            && Same(selected.Magic.Proposed.SourceAnchorIds, full.UpstreamBudget.Magic.SourceAnchorIds)
            && Same(selected.Gear, full.Lifestyles!.Gear.Lines) && Same(selected.GearBudget, full.Lifestyles.Gear.Budget)
            && Same(selected.Resources.SelectedOption, full.Lifestyles.Gear.Resources.SelectedOption)
            && Same(selected.Resources.Budget, full.Lifestyles.Gear.Resources.Budget)
            && Same(selected.Lifestyles.Authority, full.Lifestyles.Authority)
            && Same(selected.Lifestyles.Lifestyles, full.Lifestyles.Lifestyles)
            && Same(selected.Lifestyles.Budget, full.Lifestyles.Budget)
            && Same(selected.Contacts.Contacts, full.Contacts!.Contacts)
            && Same(selected.Contacts.ContactBudget, full.Contacts.ContactBudget)
            && Same(selected.Contacts.HighPlacesBudget, full.Contacts.HighPlacesBudget);

    private static CharacterCreationMagicResonanceSelections NormalizeMagic(CharacterCreationMagicResonanceSelections value)
        => value with
        {
            AdeptPowers = value.AdeptPowers.OrderBy(row => row.Identity.Kind, StringComparer.Ordinal)
                .ThenBy(row => row.Identity.SourceId, StringComparer.Ordinal).ToArray(),
            Spells = value.Spells.OrderBy(row => row.Kind, StringComparer.Ordinal).ThenBy(row => row.SourceId, StringComparer.Ordinal).ToArray(),
            ComplexForms = value.ComplexForms.OrderBy(row => row.Kind, StringComparer.Ordinal).ThenBy(row => row.SourceId, StringComparer.Ordinal).ToArray()
        };

    private static bool TryKarma(decimal value, out int karma)
    {
        karma = 0;
        if (value < 0m || value > int.MaxValue || value != decimal.Truncate(value)) return false;
        karma = (int)value;
        return true;
    }

    private static bool Same<T>(T left, T right)
        => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);

    private static T Detach<T>(T value)
        => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;

    private static bool IsMalformed(Exception error)
        => error is ArgumentException or InvalidOperationException or InvalidDataException
            or JsonException or XmlException or OverflowException;

    private static CharacterCreationFoundationResult<T> Refuse<T>(string blocker) where T : class
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);
}
