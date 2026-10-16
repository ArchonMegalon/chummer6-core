using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Pure whole-proposal review. Original observations never become current owner or save authority.</summary>
public static class CharacterCreationCompletionReviewService
{
    public static CharacterCreationFoundationResult<CharacterCreationCompletionReview> Review(
        CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        inputs = Detach(inputs);
        request = Detach(request);
        if (!HasSelectionsShape(request.Selections)
            || (inputs.Loadout is null) != (request.Selections.Loadout is null)
            || (inputs.Skills?.KarmaPurchasePolicy is null) != (request.Selections.SkillKarmaPurchases is null))
            return Refused(CharacterCreationFoundationOutcomes.Invalid, CharacterCreationCompletionReviewSchemas.RequestInvalid);
        if (inputs.Skills is null || !HasInputsShape(inputs)
            || inputs.Schema != (inputs.Loadout is null ? CharacterCreationCompletionReviewSchemas.InputsV1
                : CharacterCreationCompletionReviewSchemas.InputsV2)
            || !SameDigest(inputs.SnapshotDigest, CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs)))
            return Refused(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationCompletionReviewSchemas.AuthorityUnavailable);
        if (!SameDigest(inputs.SnapshotDigest, request.InputsSnapshotDigest) || !HasCoherentInputs(inputs))
            return Refused(CharacterCreationFoundationOutcomes.Conflict, CharacterCreationCompletionReviewSchemas.SnapshotConflict);

        var choices = request.Selections;
        CharacterCreationLoadoutCompletionProjection? loadout = null;
        if (inputs.Loadout is not null)
        {
            var loadoutResult = CharacterCreationLoadoutCompletionService.Preview(inputs.Loadout, choices.Loadout!);
            if (loadoutResult.Value is null) return new(loadoutResult.Outcome, null, loadoutResult.Blockers);
            loadout = loadoutResult.Value;
        }
        var skillsResult = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs.Skills,
            choices.AttributeAllocations, choices.SkillAllocations, choices.GroupAllocations, choices.SkillKarmaPurchases, cancellationToken);
        var lifestylesResult = CharacterCreationLifestylesService.PreviewCompletion(inputs.Lifestyles, inputs.Magic,
            choices.AttributeAllocations, choices.MagicSelections, choices.QualityOptionIds, choices.TotalKarmaInvestment,
            choices.Gear, choices.Lifestyles, choices.StartingLifestyleId, loadout, cancellationToken);
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
        if (choices.SkillKarmaPurchases is { Count: > 0 } && upstreamResult.Value is { } priced
            && CharacterCreationSkillsService.HasUnsupportedCompletionSkillModifiers(
                priced.Qualities.Qualities.Selections.Select(row => row.SourceNodeXml)
                    .Concat((inputs.Magic.SelectedTalent.GrantedQualitySources ?? []).Select(row => row.CanonicalSourceXml))))
            skillsResult = new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationSkillKarmaPurchaseBlockers.ModifiersUnsupported]);
        var contactsResult = CharacterCreationContactsService.PreviewCompletion(inputs.Contacts,
            choices.AttributeAllocations, choices.QualityOptionIds, choices.Contacts, cancellationToken);
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

        blockers.AddRange(CharacterCreationCompletionPreservationRules.Check(inputs, choices));
        var balances = BuildBalances(inputs, skillsResult.Value, upstreamResult.Value,
            lifestylesResult.Value, contactsResult.Value, blockers, loadout);
        if (choices.KarmaToReserve > 0 && (balances.RemainingCreationKarma is not int remaining || remaining < choices.KarmaToReserve))
            blockers.Add(CharacterCreationCompletionPlanBlockers.KarmaReserveRequired);
        string[] normalized = Normalize(blockers);
        var review = new CharacterCreationCompletionReview(loadout is null ? CharacterCreationCompletionReviewSchemas.ReviewV1
                : CharacterCreationCompletionReviewSchemas.ReviewV2,
            inputs.SnapshotDigest, request, skillsResult.Value, upstreamResult.Value, lifestylesResult.Value,
            contactsResult.Value, balances, normalized, string.Empty) { Loadout = loadout };
        review = review with { ReviewDigest = CharacterCreationCompletionReviewRules.ComputeReviewDigest(review) };
        cancellationToken.ThrowIfCancellationRequested();
        return new(normalized.Length == 0 ? CharacterCreationFoundationOutcomes.Success : CharacterCreationFoundationOutcomes.Blocked,
            Detach(review), normalized);
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
                if (skillCost < 0 || contactCost < 0) throw new OverflowException();
                remaining = checked(resources.RemainingCreationKarma - skillCost - contactCost);
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

    private static bool HasCoherentInputs(CharacterCreationCompletionReviewInputs inputs)
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

    private static bool HasInputsShape(CharacterCreationCompletionReviewInputs inputs)
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
