using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Pure whole-proposal review. Original observations never become current owner or save authority.</summary>
public static partial class CharacterCreationCompletionReviewService
{
    public static CharacterCreationFoundationResult<CharacterCreationCompletionReview> Review(
        CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request,
        CancellationToken cancellationToken = default, CharacterCreationRuleSourceExecutionContext? ruleSources = null,
        CharacterCreationCampaignExecutionContext? campaign = null)
        => ReviewCore(inputs, request, cancellationToken, CharacterCreationCompositeEditStage.None, ruleSources, campaign);

    internal static CharacterCreationFoundationResult<CharacterCreationCompletionReview> ReviewCompositeEdit(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request,
        CharacterCreationCompositeEditStage stage,
        CharacterCreationContactsService.CompletionPreviewReuse? contactsPreviewReuse = null)
        => ReviewCore(inputs, request, CancellationToken.None, stage, contactsPreviewReuse: contactsPreviewReuse);

    // Reconstruct the full arithmetic used by a retained finalization (including
    // native reputation provenance). This is deliberately not the public Review
    // entry point: only store-recovered, receipt-bound history can enter it.
    internal static CharacterCreationFoundationResult<CharacterCreationCompletionReview> ReplayCommittedCampaign(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request,
        WorkspaceStoredDocument original,
        CharacterCreationRuleSourceExecutionContext ruleSources, CharacterCreationCampaignExecutionContext campaign,
        CancellationToken cancellationToken)
        => campaign is { IsCommittedReplay: true, Phase: CharacterCreationCampaignPhase.Replay,
                Binding.RetainedIdentity: { } identity }
            && campaign.AuthorizesOriginal(identity.OwnerId, original, request.CampaignAuthority,
                CharacterCreationCampaignPhase.Replay, cancellationToken)
            ? ReviewCore(inputs, request, cancellationToken, CharacterCreationCompositeEditStage.None,
                ruleSources, campaign, committedReplay: true)
            : Refused(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationCampaignExecutionContext.Blocker);

    // Read-only observation of exactly the admitted composite choices. Reuse
    // the complete canonical projection (including versioned source consumers),
    // without interpreting a retained campaign/rebase binding as live permission.
    // The internal result carries no Complete assessment and is never persisted.
    internal static CharacterCreationFoundationResult<CharacterCreationCompletionReview> ObserveComposite(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request,
        CancellationToken cancellationToken = default, CharacterCreationRuleSourceExecutionContext? ruleSources = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (inputs.CompositeBaseline is not { } composite || !Same(composite.Selections, request.Selections))
            return Refused(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationCompletionReviewSchemas.SelectionPreservationConflict);
        return ReviewCore(inputs, request, cancellationToken, CharacterCreationCompositeEditStage.None,
            ruleSources, compositeObservation: true);
    }

    private static CharacterCreationFoundationResult<CharacterCreationCompletionReview> ReviewCore(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request,
        CancellationToken cancellationToken, CharacterCreationCompositeEditStage stage,
        CharacterCreationRuleSourceExecutionContext? ruleSources = null, CharacterCreationCampaignExecutionContext? campaign = null,
        bool committedReplay = false, bool compositeObservation = false,
        CharacterCreationContactsService.CompletionPreviewReuse? contactsPreviewReuse = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(request);
        CharacterCreationCompletionCost3340.Log.Checkpoint(1); // cost3340 review.begin
        using var validation = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
        cancellationToken.ThrowIfCancellationRequested();
        if (inputs.OriginalCharacter is { } originalInput
            && !CharacterCreationOriginalCharacterSource.HasBoundedXml(originalInput.RawCharacterXml))
            return Refused(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        if (request.Selections is null)
            return Refused(CharacterCreationFoundationOutcomes.Invalid, CharacterCreationCompletionReviewSchemas.RequestInvalid);
        if (!CharacterCreationSkillCompletionPermissionRules.TrySnapshotAuthorityCatalogs(inputs.Skills?.Authority,
                out var authority, cancellationToken))
            return Refused(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationCompletionReviewSchemas.AuthorityUnavailable);
        inputs = inputs with { Skills = inputs.Skills! with { Authority = authority } };
        if (!CharacterCreationSkillCompletionPermissionRules.TrySnapshot(authority,
                request.Selections.ChangeableSkillTargets, out var permissions, cancellationToken))
            return Refused(CharacterCreationFoundationOutcomes.Invalid, CharacterCreationCompletionReviewSchemas.InvalidChangePermissions);
        request = request with { Selections = request.Selections with { ChangeableSkillTargets = permissions } };
        inputs = Detach(inputs);
        request = Detach(request);
        if (!CharacterCreationCompletionReviewRules.HasValidAdditionalConsumerExecution(request)
            || !CharacterCreationCompletionReviewRules.HasValidRuleSourceExecution(request)
            || inputs.RuleSourceExecutionSchema != request.RuleSourceExecutionSchema
            || inputs.RuleSourceCaptureDigest != request.RuleSourceCaptureDigest
            || inputs.SourcebookSelection != request.SourcebookSelection
            || (request.RuleSourceExecutionSchema is null) != (ruleSources is null)
            || !CharacterCreationCompletionReviewRules.HasValidOriginalCharacterExecution(request)
            || (inputs.OriginalCharacter is null) != (request.OriginalCharacterExecutionSchema is null)
            || inputs.OriginalCharacter is { } original && original.Schema != request.OriginalCharacterExecutionSchema)
            return Refused(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationCompletionReviewSchemas.RequestInvalid);
        if (!HasSelectionsShape(request.Selections)
            || (inputs.Loadout is null) != (request.Selections.Loadout is null)
            || (inputs.Skills?.KarmaPurchasePolicy is null) != (request.Selections.SkillKarmaPurchases is null))
            return Refused(CharacterCreationFoundationOutcomes.Invalid, CharacterCreationCompletionReviewSchemas.RequestInvalid);
        if (inputs.Skills is null || !HasInputsShape(inputs)
            || (inputs.AttributePolicy is not null || inputs.LoadoutLegalityPolicy is not null)
                && inputs.EffectivePolicy is null
            || inputs.Schema != (inputs.EffectivePolicy is not null ? CharacterCreationCompletionReviewSchemas.InputsV3
                : inputs.Loadout is null ? CharacterCreationCompletionReviewSchemas.InputsV1
                : CharacterCreationCompletionReviewSchemas.InputsV2)
            || !SameDigest(inputs.SnapshotDigest, CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs)))
            return Refused(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationCompletionReviewSchemas.AuthorityUnavailable);
        if (!SameDigest(inputs.SnapshotDigest, request.InputsSnapshotDigest) || !HasCoherentInputs(inputs))
            return Refused(CharacterCreationFoundationOutcomes.Conflict, CharacterCreationCompletionReviewSchemas.SnapshotConflict);

        if ((inputs.EffectivePolicy?.SettingObservations is null
                ? request.PolicyConsumptionSchema is not null
                : request.PolicyConsumptionSchema != CharacterCreationPolicyConsumption.SchemaV1)
            || request.EffectivePolicyDigest != inputs.EffectivePolicy?.PolicyDigest
            || inputs.EffectivePolicy is { } policy && !CharacterCreationEffectivePolicyValidation.IsValid(policy, inputs))
            return Refused(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationEffectivePolicyContext.Invalid);
        if (!CharacterCreationEffectivePolicyValidation.MayEvaluate(inputs.EffectivePolicy, request))
            return Refused(CharacterCreationFoundationOutcomes.Blocked, request.CompleteAssessmentSchema is null
                ? CharacterCreationEffectivePolicyContext.CoverageUnavailable : CharacterCreationCompleteAssessment.Invalid);
        if (!compositeObservation && !CharacterCreationAttributesReplacementExecution.AuthorizesReview(inputs, request, committedReplay))
            return Refused(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationAttributesReplacementIntent.Invalid);
        // Assessment uses whole-proposal preservation, never a relaxed stage edit.
        if (inputs.OriginalCharacter is not null
            && !CharacterCreationOriginalCharacterSource.TryFromFull(inputs, out _, cancellationToken))
            return Refused(CharacterCreationFoundationOutcomes.Conflict, CharacterCreationCompletionReviewSchemas.SnapshotConflict);

        if (request.CompleteAssessmentSchema is not null && stage != CharacterCreationCompositeEditStage.None)
            return Refused(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationCompleteAssessment.Invalid);
        CharacterCreationCompletionCost3340.Log.Checkpoint(2); // cost3340 review.admission.returned
        var ruleSourceExecution = ruleSources?.Resolve(inputs, request, cancellationToken);
        CharacterCreationCompletionCost3340.Log.Checkpoint(3); // cost3340 review.resolve.returned
        if (request.RuleSourceExecutionSchema is not null && ruleSourceExecution is null)
            return Refused(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationCompleteAssessment.Invalid);
        if (ruleSourceExecution is not null) inputs = ruleSourceExecution.Inputs;
        if (!compositeObservation && ((request.CampaignAuthority is null) != (campaign is null)
            || campaign?.Phase == CharacterCreationCampaignPhase.Replay && !committedReplay
            || committedReplay && campaign is not { IsCommittedReplay: true, Phase: CharacterCreationCampaignPhase.Replay }))
            return Refused(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationCampaignExecutionContext.Blocker);
        // Freeze this prefix before Execute can attempt its first currentness callback.
        var campaignExecution = campaign?.Execute(request, inputs.SnapshotDigest,
            inputs.Skills.Binding.RawCharacterXmlDigest, inputs.EffectivePolicy!.PolicyDigest, cancellationToken, committedReplay);
        if (!compositeObservation && request.CampaignAuthority is not null && campaignExecution is null)
            return Refused(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationCampaignExecutionContext.Blocker);
        var completeAuthority = request.CompleteAssessmentSchema is null ? null
            : CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request, ruleSourceExecution);

        CharacterCreationCompletionCost3340.Log.Checkpoint(4); // cost3340 review.authority.returned
        var choices = request.Selections;
        CharacterCreationLoadoutCompletionProjection? loadout = null;
        if (inputs.Loadout is not null)
        {
            var loadoutResult = stage == CharacterCreationCompositeEditStage.Loadout
                ? CharacterCreationLoadoutCompletionService.PreviewCompositeEdit(inputs.Loadout, choices.Loadout!)
                : CharacterCreationLoadoutCompletionService.Preview(inputs.Loadout, choices.Loadout!);
            if (loadoutResult.Value is null) return new(loadoutResult.Outcome, null, loadoutResult.Blockers);
            loadout = loadoutResult.Value;
        }
        CharacterCreationLifestylesService.CompleteGraphExecution? lifestyleExecution = null;
        var lifestylesResult = CharacterCreationLifestylesService.PreviewCompletion(inputs.Lifestyles, inputs.Magic,
            choices.AttributeAllocations, choices.MagicSelections, choices.QualityOptionIds, choices.TotalKarmaInvestment,
            choices.Gear, choices.Lifestyles, choices.StartingLifestyleId, loadout, cancellationToken,
            completeAuthority?.Schema is CharacterCreationCompleteAssessment.SchemaV3 or CharacterCreationCompleteAssessment.SchemaV4
                ? execution => lifestyleExecution = execution : null, completeAuthority, contactsPreviewReuse);
        if (!CharacterCreationAmmunitionCompletionConsumer.TryValidateAssignments(choices.AmmunitionAssignments,
                lifestylesResult.Value?.Gear.Lines ?? [], loadout?.Lines ?? [], out var ammunitionBlockers))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, ammunitionBlockers);
        // Lifestyles evaluated this budget through Gear/Resources with these same
        // detached inputs and choices. Keep its own findings, not purchase blockers.
        // A missing projection still needs an independent upstream failure report.
        CharacterCreationFoundationResult<CharacterCreationCompletionBudgetPreview> upstreamResult =
            lifestylesResult.Value?.Gear.Resources.UpstreamBudget is { } upstream
                ? new(upstream.Blockers.Count == 0
                        ? CharacterCreationFoundationOutcomes.Success : CharacterCreationFoundationOutcomes.Blocked,
                    upstream, upstream.Blockers)
                : CharacterCreationCompletionBudgetService.Preview(inputs.Magic, inputs.Lifestyles.Qualities,
                    choices.AttributeAllocations, choices.MagicSelections, choices.QualityOptionIds, cancellationToken);
        // V5 prices skills only after the shared upstream projection has admitted
        // the selected quality sources. A preliminary unmodified quote cannot
        // stand in for that authority or contribute to the final shared wallet.
        // ObservationV2 is unshipped and now retains executed intervals. The 303/305
        // evidence qualifies only its frozen bytes, not these changed 307 inputs.
        // Only the new observation contract opts into executed interval retention.
        // V1/default replay must retain the old result shape and digest behavior.
        bool captureSkillExecution = inputs.EffectivePolicy?.SettingObservations?.Schema
            == CharacterCreationSettingsObservation.SchemaV2;
        CharacterCreationFoundationResult<CharacterCreationAttributeSkillsPreview> skillsResult;
        if (inputs.EffectivePolicy?.Consumers?.Schema == CharacterCreationCompletionConsumerPolicy.SchemaV5)
            skillsResult = upstreamResult.Value is { } knowledgeUpstream
                ? CharacterCreationSkillsService.PreviewCompletionKnowledgePurchases(inputs.Skills, choices,
                    knowledgeUpstream.Qualities.Qualities.Selections,
                    (inputs.Magic.SelectedTalent.GrantedQualitySources ?? []).Select(row => row.CanonicalSourceXml), cancellationToken, captureSkillExecution,
                    allowDiceOnlyGrants: completeAuthority?.Schema == CharacterCreationCompleteAssessment.SchemaV4)
                : new(CharacterCreationFoundationOutcomes.Blocked, null,
                    [CharacterCreationSkillKarmaPurchaseBlockers.ModifiersUnsupported]);
        else
        {
            skillsResult = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills,
                choices.AttributeAllocations, choices.SkillAllocations, choices.GroupAllocations, choices.SkillKarmaPurchases, cancellationToken, captureSkillExecution);
            if (choices.SkillKarmaPurchases is not null && upstreamResult.Value is { } priced
                && CharacterCreationSkillsService.HasUnsupportedCompletionSkillModifiers(
                    priced.Qualities.Qualities.Selections.Select(row => row.SourceNodeXml)
                        .Concat((inputs.Magic.SelectedTalent.GrantedQualitySources ?? []).Select(row => row.CanonicalSourceXml))))
                skillsResult = new(CharacterCreationFoundationOutcomes.Blocked, null,
                    [CharacterCreationSkillKarmaPurchaseBlockers.ModifiersUnsupported]);
        }
        var contactsResult = CharacterCreationContactsService.PreviewCompletion(inputs.Contacts,
            choices.AttributeAllocations, choices.QualityOptionIds, choices.Contacts, cancellationToken, contactsPreviewReuse);
        cancellationToken.ThrowIfCancellationRequested();
        var blockers = new List<string>(skillsResult.Blockers.Concat(upstreamResult.Blockers)
            .Concat(lifestylesResult.Blockers).Concat(contactsResult.Blockers));
        if (skillsResult.Outcome == CharacterCreationFoundationOutcomes.Conflict
            || upstreamResult.Outcome == CharacterCreationFoundationOutcomes.Conflict
            || lifestylesResult.Outcome == CharacterCreationLifestyleOutcomes.Conflict
            || contactsResult.Outcome == CharacterCreationContactOutcomes.Conflict
            || !HasCoherentProjections(inputs, skillsResult.Value, upstreamResult.Value,
                lifestylesResult.Value, contactsResult.Value))
            return new(CharacterCreationFoundationOutcomes.Conflict, null,
                Normalize(blockers.Append(CharacterCreationCompletionReviewSchemas.SnapshotConflict)));

        blockers.AddRange(CharacterCreationCompletionPreservationRules.CheckForCompositeEdit(inputs, choices, stage, cancellationToken,
            attributesReplacement: !compositeObservation && request.AttributesReplacement is not null));
        var balances = BuildBalances(inputs, skillsResult.Value, upstreamResult.Value,
            lifestylesResult.Value, contactsResult.Value, blockers, loadout);
        balances = EvaluateSharedCharges1239(inputs, request, completeAuthority, ruleSourceExecution,
            skillsResult.Value, upstreamResult.Value, lifestylesResult.Value, contactsResult.Value,
            loadout, balances, blockers, cancellationToken);
        if (choices.KarmaToReserve > 0 && (balances.RemainingCreationKarma is not int remaining || remaining < choices.KarmaToReserve))
            blockers.Add(CharacterCreationCompletionPlanBlockers.KarmaReserveRequired);
        CharacterCreationSelectedPrerequisiteConsumer.Execution? selectedPrerequisites = null;
        if (stage == CharacterCreationCompositeEditStage.None && blockers.Count == 0
            && skillsResult.Value is { } prerequisiteSkills && upstreamResult.Value is { } prerequisiteUpstream)
        {
            selectedPrerequisites = CharacterCreationSelectedPrerequisiteConsumer.EvaluateFull(inputs.Magic,
                prerequisiteUpstream.Magic, prerequisiteSkills, prerequisiteUpstream.Qualities.Qualities,
                inputs.SnapshotDigest, CharacterCreationFinalizationDigest.Compute(choices), completeAuthority);
            if (selectedPrerequisites is null) blockers.Add(CharacterCreationSelectedPrerequisiteConsumer.Blocker);
        }
        string[] normalized = Normalize(blockers);
        var review = new CharacterCreationCompletionReview(inputs.EffectivePolicy is not null ? CharacterCreationCompletionReviewSchemas.ReviewV3
                : loadout is null ? CharacterCreationCompletionReviewSchemas.ReviewV1
                : CharacterCreationCompletionReviewSchemas.ReviewV2,
            inputs.SnapshotDigest, request, skillsResult.Value, upstreamResult.Value, lifestylesResult.Value,
            contactsResult.Value, balances, normalized, string.Empty)
        {
            Loadout = loadout,
            SelectedPrerequisites = completeAuthority is null ? null : selectedPrerequisites?.Evaluation
        };
        // This projection sees mechanical blockers ONLY. Adding the coverage
        // verdict first would recursively suppress the actual consumption proof.
        CharacterCreationCompletionCost3340.Log.Checkpoint(5); // cost3340 review.mechanics.returned
        review = review with { PolicyConsumption = CharacterCreationPolicyConsumptionProjection.FromFull(inputs, review) };
        CharacterCreationCompletionCost3340.Log.Checkpoint(6); // cost3340 review.policy-consumption.returned
        if (completeAuthority is not null)
        {
            var consumers1222 = CharacterCreationCompleteConsumerExecution.FromFull(inputs, review, completeAuthority,
                cancellationToken, lifestyleExecution, ruleSourceExecution);
            CharacterCreationCompletionCost3340.Log.Checkpoint(7); // cost3340 review.consumers.returned
            if (ruleSourceExecution?.EssenceValidationFailed1222 == true)
            {
                normalized = Normalize(normalized.Append(ruleSourceExecution!.EssenceFailureBlocker1222));
                review = review with { Blockers = normalized };
            }
            review = review with { CompleteAssessment = CharacterCreationCompleteAssessmentService.AfterEvaluation(
                completeAuthority, review.PolicyConsumption, normalized.Length == 0, selectedPrerequisites, consumers1222) };
            if (!compositeObservation)
                review = review with { Identity = consumers1222?.IdentityFor(completeAuthority),
                    Ammunition = consumers1222?.AmmunitionFor(completeAuthority) };
            if (campaignExecution is not null)
                review = review with { CompleteAssessment = campaignExecution.Project(review.CompleteAssessment!, cancellationToken) };
            CharacterCreationCompletionCost3340.Log.Checkpoint(8); // cost3340 review.assessment.returned
#if CHUMMER_PRIVATE_OPAQUE_POLICY3186
            ObserveOpaquePolicy3186(inputs, review, ruleSourceExecution);
#endif
            // Observation executes the same source/effect consumers but cannot
            // assert current campaign policy coverage or authorize completion.
            if (!compositeObservation && !CharacterCreationEffectivePolicyValidation.SatisfiesAfterEvaluation(
                    inputs.EffectivePolicy, request, review.CompleteAssessment))
            {
                normalized = Normalize(normalized.Append(CharacterCreationEffectivePolicyContext.CoverageUnavailable));
                review = review with { Blockers = normalized };
            }
        }
        if (compositeObservation)
            review = review with { CompleteAssessment = null };
        CharacterCreationCompletionCost3340.Log.Checkpoint(9); // cost3340 review.coverage.returned
        review = review with { ReviewDigest = CharacterCreationCompletionReviewRules.ComputeReviewDigest(review) };
        cancellationToken.ThrowIfCancellationRequested();
        var result3340 = new CharacterCreationFoundationResult<CharacterCreationCompletionReview>(
            normalized.Length == 0 ? CharacterCreationFoundationOutcomes.Success : CharacterCreationFoundationOutcomes.Blocked,
            Detach(review), normalized);
        CharacterCreationCompletionCost3340.Log.Checkpoint(10); // cost3340 review.digest-detach.returned
        return result3340;
    }

    private static CharacterCreationCompletionPoolBalances BuildBalances(
        CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationAttributeSkillsPreview? skills,
        CharacterCreationCompletionBudgetPreview? upstream,
        CharacterCreationLifestylesCompletionProjection? lifestyles,
        CharacterCreationContactsCompletionProjection? contacts,
        ICollection<string> blockers,
        CharacterCreationLoadoutCompletionProjection? loadout)
    {
        var resources = lifestyles?.Gear.Resources;
        // Point-funded and granted levels are excluded by the exact skill quote.
        int? skillsKarma = skills is null ? null : skills.Skills.Sum(row => row.KarmaCost);
        int? contactKarma = contacts?.AdditionalKarmaCost;
        int? remaining = null;
        int? overspend = null;
        if (resources is not null && skillsKarma is int skillCost && contactKarma is int contactCost)
        {
            try
            {
                // Resources already includes Attributes, Qualities, Mystic PP and
                // the full investment. Subtract only these independent costs once.
                remaining = CharacterCreationKarmaFinalizationBudgetRules.RemainingCreationKarma995(
                    resources.RemainingCreationKarma, skillCost, contactCost);
                overspend = remaining.Value < 0 ? checked(-remaining.Value) : 0;
                if (overspend > 0) blockers.Add(CharacterCreationFinalizationBlockers.GlobalKarmaExceeded);
            }
            catch (OverflowException)
            {
                remaining = null;
                overspend = null;
            }
        }
        if (remaining is null) blockers.Add(CharacterCreationCompletionReviewSchemas.KarmaBudgetUnavailable);

        // Insufficient funds do not make source-priced amounts unknown. Keep
        // the blocking outcome, but show the known shortage in the review.
        bool nuyenExact = lifestyles is not null && lifestyles.Budget.IsExact
            && lifestyles.Gear.Budget.IsExact
            && (resources!.Budget.IsExact || resources.Budget.Blockers.Count > 0
                && resources.Budget.Blockers.All(value => value == CharacterCreationResourcesBlockers.InsufficientNuyen));
        return new(inputs.Skills.PrerequisiteDraft.CreationKarmaTotal,
            upstream?.Qualities.Blockers.Count == 0 ? upstream.Qualities.Qualities.KarmaRemaining : null,
            skillsKarma,
            upstream?.AvailableKarmaBeforeResources is not null ? upstream.Magic.MysticAdeptPowerPoints?.KarmaCost ?? 0 : null,
            resources?.SelectedOption.KarmaInvestment,
            contactKarma, remaining, overspend,
            nuyenExact ? resources!.Budget.TotalStartingNuyen : null,
            nuyenExact ? lifestyles!.Gear.Budget.BasketCost : null,
            nuyenExact ? lifestyles!.Budget.Used : null,
            nuyenExact ? resources!.Budget.RemainingNuyen : null,
            nuyenExact ? resources!.Budget.Overspend : null) { LoadoutCost = loadout?.PurchaseCost };
    }

    internal static bool HasCoherentInputs(CharacterCreationCompletionReviewInputs inputs)
    {
        var skills = inputs.Skills;
        var magic = inputs.Magic;
        var lifestyles = inputs.Lifestyles;
        var qualities = lifestyles.Qualities;
        var gear = lifestyles.Gear;
        var resources = gear.Resources;
        var contacts = inputs.Contacts;
        var basis = skills.Binding;
        var prerequisite = skills.PrerequisiteDraft;
        if (prerequisite.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
            || basis.ContentRevision <= 0 || basis.ContentRevision != basis.SavedRevision
            || !SameAttributes(skills.Attributes, magic.Attributes)
            || !SameAttributes(skills.Attributes, qualities.Attributes)
            || !Same(prerequisite, magic.PrerequisiteDraft) || !Same(prerequisite, qualities.PrerequisiteDraft)
            || !Same(prerequisite, resources.PrerequisiteDraft)
            || !Same(qualities, contacts.Qualities)
            || !SameObservation(basis, magic.Binding.WorkspaceId, magic.Binding.ContentRevision, magic.Binding.SavedRevision,
                magic.Binding.RawCharacterXmlDigest, magic.Binding.AuxiliaryStateDigest)
            || !SameObservation(basis, qualities.Binding.WorkspaceId, qualities.Binding.ContentRevision, qualities.Binding.SavedRevision,
                qualities.Binding.RawCharacterXmlDigest, qualities.Binding.AuxiliaryStateDigest)
            || !SameObservation(basis, gear.State.Binding.WorkspaceId, gear.State.Binding.ContentRevision, gear.State.Binding.SavedRevision,
                gear.State.Binding.RawCharacterXmlDigest, gear.State.Binding.AuxiliaryStateDigest)
            || !SameObservation(basis, resources.Binding.WorkspaceId, resources.Binding.ContentRevision, resources.Binding.SavedRevision,
                resources.Binding.RawCharacterXmlDigest, resources.Binding.AuxiliaryStateDigest)
            || !SameObservation(basis, lifestyles.Binding.WorkspaceId, lifestyles.Binding.ContentRevision, lifestyles.Binding.SavedRevision,
                lifestyles.Binding.ContentDigest, lifestyles.Binding.AuxiliaryStateDigest)
            || !SameObservation(basis, contacts.Binding.WorkspaceId, contacts.Binding.ContentRevision, contacts.Binding.SavedRevision,
                contacts.Binding.ContentDigest, contacts.Binding.AuxiliaryStateDigest)) return false;

        string profile = contacts.BootstrapBinding.SettingsProfileId;
        if (!Same(inputs.Loadout?.PendingDraft, resources.LoadoutDraft)
            || !Same(inputs.Loadout?.Receipts, resources.LoadoutReceipts)) return false;
        if (inputs.Loadout is { } loadout && (loadout.Authority.SettingsProfileId != profile
                || loadout.Authority.ProfileDigest != contacts.BootstrapBinding.RawProfileInputsDigest
                || !SameObservation(basis, loadout.Binding.WorkspaceId, loadout.Binding.ContentRevision,
                    loadout.Binding.SavedRevision, loadout.Binding.RawCharacterXmlDigest, loadout.Binding.AuxiliaryStateDigest)))
            return false;
        if (skills.Authority.SettingsProfileId != profile || magic.Authority.SettingsProfileId != profile
            || qualities.Authority.SettingsProfileId != profile || resources.Authority.SettingsProfileId != profile
            || gear.State.Authority.SettingsProfileId != profile || lifestyles.Authority.SettingsProfileId != profile
            || skills.Authority.RawProfileInputsDigest != contacts.BootstrapBinding.RawProfileInputsDigest
            || !Same(contacts.TalentQualitySources, magic.SelectedTalent.GrantedQualitySources ?? [])) return false;
        // These particular source identities are shared, unlike unrelated domain
        // SourceDigest/RuntimeDigest values which intentionally have different scopes.
        return skills.Authority.TalentAccess is not { } access
            || Same(access.Talent, magic.SelectedTalent) && access.TalentRuntimeDigest == magic.Authority.RuntimeDigest;
    }

    private static bool HasCoherentProjections(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationAttributeSkillsPreview? skills, CharacterCreationCompletionBudgetPreview? upstream,
        CharacterCreationLifestylesCompletionProjection? lifestyles, CharacterCreationContactsCompletionProjection? contacts)
    {
        var attributeDigests = new List<string>();
        var qualityDigests = new List<string>();
        if (skills is not null)
        {
            if (!SameDigest(skills.SkillsSnapshotDigest, inputs.Skills.SnapshotDigest)
                || !SameDigest(skills.AttributesSnapshotDigest, inputs.Skills.Attributes.SnapshotDigest)) return false;
            attributeDigests.Add(skills.Attributes.PreviewDigest);
        }
        if (upstream is not null)
        {
            attributeDigests.Add(upstream.Magic.Attributes.PreviewDigest);
            attributeDigests.Add(upstream.Qualities.Attributes.PreviewDigest);
            qualityDigests.Add(upstream.Qualities.ProposalDigest);
        }
        if (lifestyles is not null)
        {
            attributeDigests.Add(lifestyles.Qualities.Attributes.PreviewDigest);
            qualityDigests.Add(lifestyles.Qualities.ProposalDigest);
            qualityDigests.Add(lifestyles.Gear.Resources.UpstreamBudget.Qualities.ProposalDigest);
            if (upstream is not null && !SameDigest(lifestyles.Gear.Resources.UpstreamBudget.ProposalDigest, upstream.ProposalDigest))
                return false;
        }
        if (contacts is not null)
        {
            attributeDigests.Add(contacts.Upstream.Attributes.PreviewDigest);
            qualityDigests.Add(contacts.Upstream.ProposalDigest);
        }
        return attributeDigests.Distinct(StringComparer.Ordinal).Count() <= 1
            && qualityDigests.Distinct(StringComparer.Ordinal).Count() <= 1;
    }

    internal static bool HasInputsShape(CharacterCreationCompletionReviewInputs inputs)
        => inputs.Skills is { Binding: not null, Attributes: { Binding: not null, Attributes: not null },
                Authority: { ActiveSkills: not null, KnowledgeSkills: not null, SkillGroups: not null },
                PrerequisiteDraft: not null, Allocations: not null, GroupAllocations: not null }
            && inputs.Skills.Attributes.Attributes.All(value => value is not null)
            && inputs.Skills.Authority.ActiveSkills.All(value => value is not null)
            && inputs.Skills.Authority.KnowledgeSkills.All(value => value is not null)
            && inputs.Skills.Authority.SkillGroups.All(value => value is not null && value.MemberSkillSourceIds is not null)
            && inputs.Skills.Allocations.All(value => value is not null)
            && inputs.Skills.GroupAllocations.All(value => value is not null)
            && inputs.Magic is { Binding: not null, Attributes.Binding: not null, Authority: not null,
                PrerequisiteDraft: not null, SelectedTalent: not null }
            && HasMagicShape(inputs.Magic.Selections)
            && inputs.Lifestyles is { Binding: not null, Authority: not null, Lifestyles: not null,
                Qualities: { Binding: not null, Attributes.Binding: not null, Authority: not null,
                    PrerequisiteDraft: not null, SelectedOptionIds: not null },
                Gear: { State: { Binding: not null, Authority: not null },
                    Resources: { Binding: not null, Authority: not null, PrerequisiteDraft: not null } } }
            && inputs.Lifestyles.Lifestyles.All(value => value is not null)
            && (inputs.Lifestyles.Gear.State.PendingDraft is null
                || inputs.Lifestyles.Gear.State.PendingDraft.Lines is not null
                    && inputs.Lifestyles.Gear.State.PendingDraft.Lines.All(value => value is not null))
            && inputs.Contacts is { Binding: not null, BootstrapBinding: not null, Qualities: not null,
                Contacts: not null, TalentQualitySources: not null }
            && inputs.Contacts.Contacts.All(value => value is not null)
            && (inputs.Loadout is null || CharacterCreationLoadoutCompletionService.IsValidInputs(inputs.Loadout));

    private static bool HasSelectionsShape(CharacterCreationCompletionSelections? choices)
        => choices is { AttributeAllocations: not null, SkillAllocations: not null, GroupAllocations: not null,
                QualityOptionIds: not null, Gear: not null, Contacts: not null, Lifestyles: not null,
                ChangeableAttributeIds: not null, ChangeableSkillTargets: not null }
            && choices.KarmaToReserve >= 0
            && choices.AttributeAllocations.Count <= 32 && choices.SkillAllocations.Count <= 65_536
            && choices.GroupAllocations.Count <= 65_536 && choices.QualityOptionIds.Count <= 65_536
            && choices.Gear.Count <= 65_536
            && choices.Contacts.Count <= CharacterCreationKarmaContactsRules.MaximumSelections
            && choices.AttributeAllocations.All(value => value is not null)
            && choices.SkillAllocations.All(value => value is not null)
            && choices.GroupAllocations.All(value => value is not null)
            && choices.QualityOptionIds.All(value => value is not null)
            && choices.Gear.All(value => value is not null)
            && choices.Contacts.All(value => value is not null)
            && choices.Lifestyles.All(value => value is not null)
            && CharacterCreationSkillsService.IsCompletionPurchaseShapeValid(choices.SkillKarmaPurchases)
            && HasMagicShape(choices.MagicSelections)
            && (choices.Loadout is null || CharacterCreationLoadoutCompletionService.IsValidSelections(choices.Loadout));

    private static bool HasMagicShape(CharacterCreationMagicResonanceSelections? selections)
        => selections is { AdeptPowers: not null, Spells: not null, ComplexForms: not null }
            && selections.AdeptPowers.All(value => value is not null)
            && selections.Spells.All(value => value is not null)
            && selections.ComplexForms.All(value => value is not null);

    private static bool SameObservation(CharacterCreationSkillsBinding basis, CharacterWorkspaceId workspaceId,
        long contentRevision, long savedRevision, string rawDigest, string auxiliaryDigest)
        => basis.WorkspaceId == workspaceId && basis.ContentRevision == contentRevision && basis.SavedRevision == savedRevision
            && SameDigest(basis.RawCharacterXmlDigest, rawDigest) && basis.AuxiliaryStateDigest == auxiliaryDigest;

    private static bool SameAttributes(CharacterCreationAttributesState left, CharacterCreationAttributesState right)
        => left.Binding == right.Binding && SameDigest(left.SnapshotDigest, right.SnapshotDigest) && Same(left, right);

    private static bool Same<T>(T left, T right)
        => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);

    private static bool SameDigest(string? left, string? right)
        => CharacterCreationSkillsDigest.EqualsFixedTime(left, right);

    private static T Detach<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;

    private static string[] Normalize(IEnumerable<string> blockers)
        => blockers.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();

    private static CharacterCreationFoundationResult<CharacterCreationCompletionReview> Refused(string outcome, string blocker)
        => new(outcome, null, [blocker]);
}

// Optional fixed numeric checkpoints for the bounded private cost experiment.
// No character, source, owner, policy, exception or provider data is emitted.
// Disabled unless an EventListener explicitly subscribes; no timing/authority cache.
[System.Diagnostics.Tracing.EventSource(Name = "Chummer-Completion-Cost3340")]
internal sealed class CharacterCreationCompletionCost3340 : System.Diagnostics.Tracing.EventSource
{
    internal static readonly CharacterCreationCompletionCost3340 Log = new();
    private CharacterCreationCompletionCost3340() { }

    private int settingsInvocations3365;

    [System.Diagnostics.Tracing.NonEvent]
    internal bool TryBeginSettings3365(out int invocation)
    {
        invocation = 0;
        if (!IsEnabled()) return false;
        int observed = System.Threading.Volatile.Read(ref settingsInvocations3365);
        while (observed < 3)
        {
            int previous = System.Threading.Interlocked.CompareExchange(
                ref settingsInvocations3365, observed + 1, observed);
            if (previous == observed)
            {
                invocation = observed + 1;
                return true;
            }
            observed = previous;
        }
        return false;
    }

    [System.Diagnostics.Tracing.Event(2, Level = System.Diagnostics.Tracing.EventLevel.Informational)]
    public void Settings3365(int invocation, int settingCount, int fallbackCount,
        long directTicks, long fallbackTicks, long rowTicks)
    {
        if (IsEnabled()) WriteEvent(2, invocation, settingCount, fallbackCount,
            directTicks, fallbackTicks, rowTicks);
    }

    // Nested totals: serialization is inside Inputs/Comparison, and validated
    // hashing is inside Comparison. Never add these overlapping totals together.
    [System.Diagnostics.Tracing.Event(3, Level = System.Diagnostics.Tracing.EventLevel.Informational)]
    public void ProfileCosts3431(int invocation, int inputCalls, long inputTicks,
        int comparisonCalls, long comparisonTicks, int serializationCalls,
        long serializationTicks, long validatedHashTicks)
    {
        if (IsEnabled()) WriteEvent(3, invocation, inputCalls, inputTicks,
            comparisonCalls, comparisonTicks, serializationCalls, serializationTicks, validatedHashTicks);
    }

    // Bounded numeric diagnostics only; no source values, hashes or identities.
    [System.Diagnostics.Tracing.Event(4, Level = System.Diagnostics.Tracing.EventLevel.Informational)]
    public void ProfileDigest8544(int calls, int hits, int serializedBytes, int retainedTypes)
    {
        if (IsEnabled()) WriteEvent(4, calls, hits, serializedBytes, retainedTypes);
    }

    [System.Diagnostics.Tracing.Event(1, Level = System.Diagnostics.Tracing.EventLevel.Informational)]
    public void Checkpoint(int phase)
    {
        if (IsEnabled()) WriteEvent(1, phase);
    }
}
