using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationQualitiesServiceTests
{
    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Quality_completion_before_attributes_is_read_only_and_does_not_authorize_an_ordinary_save(string method)
    {
        using var context = ReadyContext.CreateUnprepared(method);
        PrepareCompletionPrerequisite(context);
        var service = CompletionQualities(context);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var ordinary = service.Load(new(context.WorkspaceId)).Value!;
        Assert.IsFalse(ordinary.CanEdit);
        CollectionAssert.Contains(ordinary.Blockers.ToArray(), CharacterCreationQualitiesBlockers.AttributesDraftRequired);

        var inputs = RequireCompletionInputs(service.LoadCompletionInputs(new(context.WorkspaceId)));
        Assert.IsNull(inputs.Attributes.PendingDraft);
        Assert.IsNull(inputs.SavedQualitiesState);
        Assert.AreEqual(0L, inputs.Binding.AttributesDraftRevision);
        Assert.AreEqual(string.Empty, inputs.Binding.AttributesDraftDigest);
        Assert.AreEqual(before.ContentRevision, inputs.Binding.ContentRevision);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, inputs.Binding.AuxiliaryStateDigest);
        Assert.AreEqual(0, inputs.SelectedOptionIds.Count);
        var projection = CharacterCreationQualitiesService.PreviewWithProposedAttributes(inputs, [], []);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, projection.Outcome, string.Join(",", projection.Blockers));
        Assert.AreEqual(inputs.Binding, projection.Value!.Binding);
        Assert.IsNull(typeof(CharacterCreationAttributeQualitiesPreview).GetProperty("CanConfirm"));
        Assert.IsNull(typeof(CharacterCreationQualitiesCompletionEvaluation).GetProperty("CanConfirm"));
        Assert.IsNull(typeof(CharacterCreationQualitiesCompletionEvaluation).GetProperty("PreviewDigest"));

        var refused = service.Confirm(new(inputs.Binding, [], projection.Value.ProposalDigest,
            "hypothetical-quality-is-not-a-confirmation", Guid.NewGuid(), true));
        Assert.IsNull(refused.Value);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, refused.Outcome);
        CollectionAssert.Contains(refused.Blockers.ToArray(), CharacterCreationQualitiesBlockers.AttributesDraftRequired);
        AssertQualityWorkspaceUnchanged(before, context.Store.Get(context.WorkspaceId).Value!);
    }

    [TestMethod]
    public void Quality_completion_uses_proposed_attribute_karma_and_matches_ordinary_after_the_actual_attribute_save()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        PrepareCompletionPrerequisite(context);
        var service = CompletionQualities(context);
        var inputs = RequireCompletionInputs(service.LoadCompletionInputs(new(context.WorkspaceId)));
        var paid = inputs.Authority.Options.First(option => option.Name == "Ambidextrous" && option.IsSelectable);
        CharacterCreationAttributeAllocation[] allocations = [new("BOD", 0, 1)];
        string[] selections = [paid.OptionId];
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var proposed = CharacterCreationQualitiesService.PreviewWithProposedAttributes(inputs, allocations, selections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, proposed.Outcome, string.Join(",", proposed.Blockers));
        Assert.IsTrue(proposed.Value!.Qualities.ProposedAttributeKarmaUsed > 0);
        Assert.AreEqual(proposed.Value.Attributes.CreationKarmaBudget.Used,
            (decimal)proposed.Value.Qualities.ProposedAttributeKarmaUsed);
        Assert.AreEqual(inputs.Binding.CreationKarmaTotal - proposed.Value.Qualities.ProposedAttributeKarmaUsed - paid.KarmaCost,
            proposed.Value.Qualities.KarmaRemaining);
        Assert.AreEqual(inputs.Binding, proposed.Value.Binding, "A projection must retain its real, unsaved binding.");
        AssertQualityWorkspaceUnchanged(before, context.Store.Get(context.WorkspaceId).Value!);

        SaveCompletionAttributes(context, allocations);
        var ordinaryState = service.Load(new(context.WorkspaceId)).Value!;
        Assert.IsTrue(ordinaryState.CanEdit, string.Join(",", ordinaryState.Blockers));
        var ordinary = service.Preview(new(ordinaryState.Binding, selections));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, ordinary.Outcome, string.Join(",", ordinary.Blockers));
        AssertQualityCompletionMatches(proposed.Value.Qualities, ordinary.Value!);
        Assert.AreEqual(context.Store.Get(context.WorkspaceId).Value!.Document.AuxiliaryState.CharacterCreationAttributesDraft!.CreationKarmaUsed,
            proposed.Value.Qualities.ProposedAttributeKarmaUsed);
        var after = context.Store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(before.ContentRevision + 1, after.ContentRevision);
        Assert.IsNull(after.Document.AuxiliaryState.CharacterCreationQualitiesDraft);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
    }

    [TestMethod]
    public void Quality_completion_preserves_saved_choices_and_racial_grants_after_cold_reopen()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        PrepareCompletionPrerequisite(context, "Elf");
        SaveCompletionAttributes(context, []);
        var service = CompletionQualities(context);
        var initial = service.Load(new(context.WorkspaceId)).Value!;
        var paid = initial.Authority.Options.First(option => option.Name == "Ambidextrous" && option.IsSelectable);
        string[] selected = [paid.OptionId];
        var preview = service.Preview(new(initial.Binding, selected)).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
            service.Confirm(new(preview.Binding, selected, preview.PreviewDigest,
                "completion-quality-baseline", Guid.NewGuid(), true)).Outcome);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        using var cold = context.Restart();
        var inputs = RequireCompletionInputs(CompletionQualities(cold).LoadCompletionInputs(new(cold.WorkspaceId)));
        Assert.IsNotNull(inputs.SavedQualitiesState?.PendingDraft);
        var savedState = inputs.SavedQualitiesState!;
        Assert.AreEqual(before.Document.AuxiliaryState.CharacterCreationQualitiesDraft!.DraftDigest,
            savedState.PendingDraft!.DraftDigest);
        CollectionAssert.AreEqual(selected, inputs.SelectedOptionIds.ToArray());
        Assert.IsTrue(inputs.Authority.GrantedQualities.Any(quality => quality.Name == "Low-Light Vision"));
        var result = CharacterCreationQualitiesService.PreviewWithProposedAttributes(inputs,
            inputs.Attributes.PendingDraft!.Allocations, inputs.SelectedOptionIds);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        AssertQualityCompletionMatches(result.Value!.Qualities, savedState.Preview);
        Assert.AreEqual(JsonSerializer.Serialize(inputs.Authority.GrantedQualities),
            JsonSerializer.Serialize(result.Value.Qualities.GrantedQualities));
        AssertQualityWorkspaceUnchanged(before, cold.Store.Get(context.WorkspaceId).Value!);
    }

    [TestMethod]
    public void Quality_completion_requires_real_prerequisites_source_authority_and_unchanged_binding()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        var service = CompletionQualities(context);
        var unprepared = context.Store.Get(context.WorkspaceId).Value!;
        var missing = service.LoadCompletionInputs(new(context.WorkspaceId));
        Assert.IsNull(missing.Value);
        CollectionAssert.Contains(missing.Blockers.ToArray(), CharacterCreationQualitiesBlockers.PrerequisiteDraftRequired);
        AssertQualityWorkspaceUnchanged(unprepared, context.Store.Get(context.WorkspaceId).Value!);
        PrepareCompletionPrerequisite(context);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var absentSource = CompletionQualities(context, new UnavailableCompletionQualitySource())
            .LoadCompletionInputs(new(context.WorkspaceId));
        Assert.IsNull(absentSource.Value);
        CollectionAssert.Contains(absentSource.Blockers.ToArray(), CharacterCreationQualitiesBlockers.AuthorityUnavailable);
        var inputs = RequireCompletionInputs(service.LoadCompletionInputs(new(context.WorkspaceId)));
        var changedBinding = inputs with { Binding = inputs.Binding with { ContentRevision = inputs.Binding.ContentRevision + 1 } };
        changedBinding = changedBinding with
            { SnapshotDigest = CharacterCreationQualitiesRules.ComputeCompletionInputsDigest(changedBinding) };
        var wrongBinding = CharacterCreationQualitiesService.PreviewWithProposedAttributes(
            changedBinding, [], []);
        Assert.IsNull(wrongBinding.Value);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, wrongBinding.Outcome);
        var changedAuthority = inputs with { Authority = inputs.Authority with { QualityKarmaLimit = inputs.Authority.QualityKarmaLimit + 1 } };
        changedAuthority = changedAuthority with
            { SnapshotDigest = CharacterCreationQualitiesRules.ComputeCompletionInputsDigest(changedAuthority) };
        var wrongAuthority = CharacterCreationQualitiesService.PreviewWithProposedAttributes(
            changedAuthority, [], []);
        Assert.IsNull(wrongAuthority.Value);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, wrongAuthority.Outcome);
        var changedAttributes = inputs with { Attributes = inputs.Attributes with { SnapshotDigest = "sha256:" + new string('f', 64) } };
        changedAttributes = changedAttributes with
            { SnapshotDigest = CharacterCreationQualitiesRules.ComputeCompletionInputsDigest(changedAttributes) };
        var wrongAttributes = CharacterCreationQualitiesService.PreviewWithProposedAttributes(changedAttributes, [], []);
        Assert.IsNull(wrongAttributes.Value);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, wrongAttributes.Outcome);
        AssertQualityWorkspaceUnchanged(before, context.Store.Get(context.WorkspaceId).Value!);
    }

    [TestMethod]
    public void Quality_completion_does_not_rebind_saved_history_after_an_attribute_edit()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        PrepareCompletionPrerequisite(context);
        SaveCompletionAttributes(context, []);
        var service = CompletionQualities(context);
        var initial = service.Load(new(context.WorkspaceId)).Value!;
        string[] selected = [initial.Authority.Options.First(option => option.Name == "Ambidextrous" && option.IsSelectable).OptionId];
        var preview = service.Preview(new(initial.Binding, selected)).Value!;
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, service.Confirm(new(preview.Binding, selected,
            preview.PreviewDigest, "completion-stale-quality-baseline", Guid.NewGuid(), true)).Outcome);
        var original = RequireCompletionInputs(service.LoadCompletionInputs(new(context.WorkspaceId)));
        var historical = CharacterCreationQualitiesService.PreviewWithProposedAttributes(original,
            original.Attributes.PendingDraft!.Allocations, original.SelectedOptionIds).Value!;
        SaveCompletionAttributes(context, [new("BOD", 1, 0)]);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var ordinary = service.Load(new(context.WorkspaceId)).Value!;
        Assert.IsFalse(ordinary.CanEdit);
        CollectionAssert.Contains(ordinary.Blockers.ToArray(), CharacterCreationQualitiesBlockers.DraftInvalid);
        var stale = service.LoadCompletionInputs(new(context.WorkspaceId));
        Assert.IsNull(stale.Value);
        CollectionAssert.Contains(stale.Blockers.ToArray(), CharacterCreationQualitiesBlockers.DraftInvalid);
        Assert.AreEqual(original.SavedQualitiesState!.PendingDraft!.DraftDigest,
            before.Document.AuxiliaryState.CharacterCreationQualitiesDraft!.DraftDigest);
        Assert.AreEqual(historical.ProposalDigest, CharacterCreationQualitiesService.PreviewWithProposedAttributes(original,
            original.Attributes.PendingDraft!.Allocations, original.SelectedOptionIds).Value!.ProposalDigest,
            "A detached historical calculation remains data, not a live save authorization.");
        AssertQualityWorkspaceUnchanged(before, context.Store.Get(context.WorkspaceId).Value!);
    }

    [TestMethod]
    public void Quality_completion_tampering_invalid_choices_and_cancellation_leave_state_unchanged_and_outputs_detached()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        PrepareCompletionPrerequisite(context);
        var service = CompletionQualities(context);
        var inputs = RequireCompletionInputs(service.LoadCompletionInputs(new(context.WorkspaceId)));
        var before = context.Store.Get(context.WorkspaceId).Value!;
        CharacterCreationAttributeAllocation[] attributes = [new("BOD", 1, 0)];
        string[] selected = [inputs.Authority.Options.First(option => option.Name == "Ambidextrous" && option.IsSelectable).OptionId];
        var tampered = CharacterCreationQualitiesService.PreviewWithProposedAttributes(inputs with
            { SnapshotDigest = "sha256:" + new string('f', 64) }, attributes, selected);
        Assert.IsNull(tampered.Value);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, tampered.Outcome);
        var invalidAttribute = CharacterCreationQualitiesService.PreviewWithProposedAttributes(inputs,
            [new("BOD", 100, 0)], selected);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, invalidAttribute.Outcome);
        Assert.IsNull(invalidAttribute.Value);
        var invalidChoice = CharacterCreationQualitiesService.PreviewWithProposedAttributes(inputs, attributes, ["not-a-quality-option"]);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, invalidChoice.Outcome);
        CollectionAssert.Contains(invalidChoice.Blockers.ToArray(), CharacterCreationQualitiesBlockers.InvalidSelection);
        bool canceled = false;
        try { CharacterCreationQualitiesService.PreviewWithProposedAttributes(inputs, attributes, selected, new CancellationToken(true)); }
        catch (OperationCanceledException) { canceled = true; }
        Assert.IsTrue(canceled);
        var result = CharacterCreationQualitiesService.PreviewWithProposedAttributes(inputs, attributes, selected);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        string captured = JsonSerializer.Serialize(result.Value);
        attributes[0] = new("BOD", 100, 0);
        selected[0] = "mutated-after-projection";
        if (inputs.Authority.Options is CharacterCreationQualityCatalogOption[] options)
            options[0] = options[0] with { KarmaCost = 9999 };
        Assert.AreEqual(captured, JsonSerializer.Serialize(result.Value));
        AssertQualityWorkspaceUnchanged(before, context.Store.Get(context.WorkspaceId).Value!);
    }

    private static CharacterCreationQualitiesService CompletionQualities(ReadyContext context,
        ICharacterSourceDataResolver? qualitySource = null)
        => new(context.Store, qualitySource ?? context.Resolver,
            new CharacterCreationPrerequisiteService(context.Store, context.Queries, context.Resolver),
            new CharacterCreationAttributesService(context.Store, context.Resolver));

    private static CharacterCreationQualitiesCompletionInputs RequireCompletionInputs(
        CharacterCreationFoundationResult<CharacterCreationQualitiesCompletionInputs> result)
    {
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static void PrepareCompletionPrerequisite(ReadyContext context, string metatype = "Human")
    {
        var service = new CharacterCreationPrerequisiteService(context.Store, context.Queries, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        var ranks = CharacterCreationPrerequisiteServiceTests.Assign("A", "E", "B", "C", "D");
        var heritage = state.Authority.Options.Single(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
            && option.Rank == "A").HeritageOptions.First(option => option.IsEnabled && option.MetatypeName == metatype
                && option.MetavariantSourceId is null);
        var talent = state.Authority.Options.Single(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
            && option.Rank == "E").TalentOptions.First(option => option.IsEnabled
                && option.Value.Equals("Mundane", StringComparison.OrdinalIgnoreCase));
        var preview = service.Preview(new(state.Binding, ranks)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId }).Value!;
        var confirmed = service.Confirm(new(preview.Binding, ranks, preview.PreviewDigest, true)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome, string.Join(",", confirmed.Blockers));
    }

    private static void SaveCompletionAttributes(ReadyContext context, IReadOnlyList<CharacterCreationAttributeAllocation> allocations)
    {
        var service = new CharacterCreationAttributesService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        var preview = service.Preview(new(state.Binding, allocations)).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        var saved = service.Confirm(new(preview.Binding, allocations, preview.PreviewDigest, true));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, saved.Outcome, string.Join(",", saved.Blockers));
    }

    private static void AssertQualityCompletionMatches(CharacterCreationQualitiesCompletionEvaluation actual,
        CharacterCreationQualitiesPreview expected)
    {
        Assert.AreEqual(JsonSerializer.Serialize(expected.Selections), JsonSerializer.Serialize(actual.Selections));
        Assert.AreEqual(JsonSerializer.Serialize(expected.GrantedQualities), JsonSerializer.Serialize(actual.GrantedQualities));
        Assert.AreEqual(JsonSerializer.Serialize(expected.PositiveQualityBudget), JsonSerializer.Serialize(actual.PositiveQualityBudget));
        Assert.AreEqual(JsonSerializer.Serialize(expected.NegativeQualityBudget), JsonSerializer.Serialize(actual.NegativeQualityBudget));
        Assert.AreEqual(expected.MetagenicPositiveKarma, actual.MetagenicPositiveKarma);
        Assert.AreEqual(expected.MetagenicNegativeKarma, actual.MetagenicNegativeKarma);
        Assert.AreEqual(expected.KarmaRemaining, actual.KarmaRemaining);
        CollectionAssert.AreEqual(expected.SourceAnchorIds.ToArray(), actual.SourceAnchorIds.ToArray());
        CollectionAssert.AreEqual(expected.Blockers.ToArray(), actual.Blockers.ToArray());
    }

    private static void AssertQualityWorkspaceUnchanged(WorkspaceStoredDocument before, WorkspaceStoredDocument after)
    {
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
        Assert.AreEqual(before.LastUpdatedUtc, after.LastUpdatedUtc);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
    }

    private sealed class UnavailableCompletionQualitySource : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string characterXml) => null;
    }
}
