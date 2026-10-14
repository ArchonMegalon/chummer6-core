using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class MagicResonanceCompletionTests
{
    [TestMethod]
    [DataRow("Adept", "D", "MAG")]
    [DataRow("Magician", "B", "MAG")]
    [DataRow("Technomancer", "B", "RES")]
    public void Actual_source_completion_before_attributes_uses_proposed_magic_or_resonance_without_saving(
        string talentName, string talentRank, string attributeId)
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        SavePrerequisite(context, talentName, talentRank);
        var service = new CharacterCreationMagicResonanceService(context.Store, context.Resolver);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var ordinaryBefore = service.Load(new(context.WorkspaceId)).Value!;
        Assert.IsFalse(ordinaryBefore.CanEdit);
        CollectionAssert.Contains(ordinaryBefore.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.AttributesDraftRequired);

        var loaded = service.LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        var inputs = loaded.Value!;
        Assert.IsNull(inputs.Attributes.PendingDraft);
        Assert.IsNull(inputs.SavedMagicState);
        Assert.AreEqual(0L, inputs.Binding.AttributesDraftRevision);
        Assert.AreEqual(string.Empty, inputs.Binding.AttributesDraftDigest);
        Assert.AreEqual(before.ContentRevision, inputs.Binding.ContentRevision);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, inputs.Binding.AuxiliaryStateDigest);
        Assert.IsTrue(CharacterCreationMagicResonanceDigest.IsCanonical(inputs.SnapshotDigest));
        Assert.IsNull(typeof(CharacterCreationMagicResonanceCompletionInputs).GetProperty("CanConfirm"));
        Assert.IsNull(typeof(CharacterCreationMagicResonanceCompletionProjection).GetProperty("CanConfirm"));

        var talentBefore = CharacterCreationMagicResonanceDigest.Compute(inputs.SelectedTalent);
        int granted = attributeId == "MAG" ? inputs.SelectedTalent.Magic : inputs.SelectedTalent.Resonance;
        CharacterCreationAttributeAllocation[] attributes = [new(attributeId, 1, 0)];
        int proposedMagic = attributeId == "MAG" ? granted + 1 : inputs.SelectedTalent.Magic;
        var choices = CompleteSelections(inputs, proposedMagic);
        var result = CharacterCreationMagicResonanceService.PreviewCompletion(inputs, attributes, choices);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var projection = result.Value!;
        Assert.AreEqual(inputs.Binding, projection.Binding);
        Assert.AreEqual(granted + 1, projection.Attributes.Attributes.Single(item => item.AttributeId == attributeId).Current);
        Assert.AreEqual(granted + 1, attributeId == "MAG" ? projection.EffectiveAttributes.Magic : projection.EffectiveAttributes.Resonance);
        Assert.AreEqual(talentBefore, CharacterCreationMagicResonanceDigest.Compute(projection.Talent),
            "Proposed attributes must not rewrite the source talent grant.");
        Assert.AreEqual(inputs.SelectedTalent.SpellBudget, (int)projection.SpellBudget.Total);
        Assert.AreEqual(inputs.SelectedTalent.ComplexFormBudget, (int)projection.ComplexFormBudget.Total);
        if (talentName == "Adept")
            Assert.AreEqual((decimal)(granted + 1), projection.AdeptPowerPointBudget.Total);
        Assert.IsTrue(CharacterCreationMagicResonanceDigest.IsCanonical(projection.ProposalDigest));
        Assert.AreEqual(projection.ProposalDigest,
            CharacterCreationMagicResonanceService.PreviewCompletion(inputs, attributes, choices).Value!.ProposalDigest);
        AssertWorkspaceUnchanged(context, before, files);
        Assert.IsFalse(service.Load(new(context.WorkspaceId)).Value!.CanEdit,
            "A detached projection must not unlock ordinary Magic confirmation.");

        // The real ordinary service remains the comparison oracle. Only this
        // explicit Attributes confirmation creates an Attributes draft.
        SaveAttributes(context, attributes);
        var savedAttributes = context.Store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(before.ContentRevision + 1, savedAttributes.ContentRevision);
        Assert.AreEqual(before.Document.Content, savedAttributes.Document.Content);
        Assert.IsNull(savedAttributes.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft);
        var current = service.Load(new(context.WorkspaceId)).Value!;
        Assert.IsTrue(current.CanEdit, string.Join(",", current.Blockers));
        var ordinary = service.Preview(new(current.Binding, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, ordinary.Outcome, string.Join(",", ordinary.Blockers));
        AssertProjectionMatchesOrdinary(projection, ordinary.Value!);
        Assert.AreEqual(savedAttributes.ContentRevision, context.Store.Get(context.WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    public void Actual_source_mystic_completion_binds_power_purchase_to_proposed_magic_profile_cost_and_spell_budget()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        SavePrerequisite(context, "Mystic Adept", "B");
        var service = new CharacterCreationMagicResonanceService(context.Store, context.Resolver);
        var loaded = service.LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        var inputs = loaded.Value!;
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        CharacterCreationAttributeAllocation[] attributes = [new("MAG", 1, 0)];
        int magic = inputs.SelectedTalent.Magic + 1;
        const int purchased = 2;
        var choices = CompleteSelections(inputs, magic, purchased);
        var proposed = CharacterCreationMagicResonanceService.PreviewCompletion(inputs, attributes, choices);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, proposed.Outcome, string.Join(",", proposed.Blockers));
        var projection = proposed.Value!;
        var purchase = projection.MysticAdeptPowerPoints!;
        var policy = inputs.Authority.MysticAdeptPowerPointPolicy!;
        Assert.IsNotNull(purchase);
        Assert.AreEqual(magic, purchase.MaximumPowerPoints);
        Assert.AreEqual(purchased, purchase.PowerPoints);
        Assert.AreEqual(policy.PrioritySpellsAsPowerPoints ? Math.Min(purchased, inputs.SelectedTalent.SpellBudget) : 0,
            purchase.ExchangedSpellSlots);
        Assert.AreEqual((purchased - purchase.ExchangedSpellSlots) * policy.KarmaPerPowerPoint, purchase.KarmaCost);
        Assert.AreEqual(inputs.SelectedTalent.SpellBudget - purchase.ExchangedSpellSlots, purchase.SpellBudget);
        Assert.AreEqual((decimal)purchase.SpellBudget, projection.SpellBudget.Total);
        Assert.AreEqual((decimal)purchased, projection.AdeptPowerPointBudget.Total);
        Assert.AreEqual(0m, projection.AdeptPowerPointBudget.Remaining);
        Assert.AreEqual(0m, projection.SpellBudget.Remaining);
        Assert.AreEqual(policy.PolicyDigest, purchase.Policy.PolicyDigest);
        CollectionAssert.IsSubsetOf(policy.SourceAnchorIds.ToArray(), projection.SourceAnchorIds.ToArray());

        var excessive = CharacterCreationMagicResonanceService.PreviewCompletion(inputs, attributes,
            choices with { MysticAdeptPowerPoints = magic + 1 });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, excessive.Outcome);
        Assert.IsNotNull(excessive.Value);
        CollectionAssert.Contains(excessive.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.PowerBudgetUnsupported);
        AssertWorkspaceUnchanged(context, before, files);

        SaveAttributes(context, attributes);
        var ordinary = service.Load(new(context.WorkspaceId)).Value!;
        var preview = service.Preview(new(ordinary.Binding, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, preview.Outcome, string.Join(",", preview.Blockers));
        AssertProjectionMatchesOrdinary(projection, preview.Value!);
    }

    [TestMethod]
    public void Actual_saved_magic_choices_and_receipts_survive_completion_and_stale_attributes_require_review()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        SavePrerequisite(context, "Magician", "B");
        var service = new CharacterCreationMagicResonanceService(context.Store, context.Resolver);
        var initial = service.LoadCompletionInputs(new(context.WorkspaceId)).Value!;
        CharacterCreationAttributeAllocation[] attributes = [new("MAG", 1, 0)];
        var choices = CompleteSelections(initial, initial.SelectedTalent.Magic + 1);
        SaveAttributes(context, attributes);
        var ready = service.Load(new(context.WorkspaceId)).Value!;
        var preview = service.Preview(new(ready.Binding, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, preview.Outcome, string.Join(",", preview.Blockers));
        var command = new CharacterCreationMagicResonanceConfirmRequest(ready.Binding, choices,
            preview.Value!.PreviewDigest, "completion-real-source-magic", true);
        var confirmed = service.Confirm(command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome, string.Join(",", confirmed.Blockers));

        using ReadyContext cold = context.Restart();
        var coldService = new CharacterCreationMagicResonanceService(cold.Store, cold.Resolver);
        var before = cold.Store.Get(cold.WorkspaceId).Value!;
        var files = CaptureFiles(cold);
        var saved = before.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft!;
        var receipts = before.Document.AuxiliaryState.CharacterCreationMagicResonanceReceipts!;
        Assert.AreEqual(1, receipts.Count);
        Assert.AreEqual(confirmed.Value!.ReceiptDigest, receipts.Single().ReceiptDigest);
        var loaded = coldService.LoadCompletionInputs(new(cold.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        var inputs = loaded.Value!;
        Assert.IsNotNull(inputs.SavedMagicState);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(saved.Selections, inputs.Selections));
        Assert.AreEqual(saved.DraftDigest, inputs.SavedMagicState!.PendingDraft!.DraftDigest);
        var result = CharacterCreationMagicResonanceService.PreviewCompletion(inputs, attributes, inputs.Selections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(saved.Selections, result.Value!.Selections));
        AssertWorkspaceUnchanged(cold, before, files);

        // Existing persisted history becomes stale through a normal real
        // Attributes edit, not an invented store or manually forged draft.
        SaveAttributes(cold, [new("MAG", 1, 0), new("BOD", 1, 0)]);
        var afterAttributes = cold.Store.Get(cold.WorkspaceId).Value!;
        var afterFiles = CaptureFiles(cold);
        Assert.AreEqual(saved.DraftDigest, afterAttributes.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft!.DraftDigest);
        Assert.AreEqual(confirmed.Value.ReceiptDigest,
            afterAttributes.Document.AuxiliaryState.CharacterCreationMagicResonanceReceipts!.Single().ReceiptDigest);
        var deniedConfirmation = coldService.Confirm(new(inputs.Binding, inputs.Selections,
            result.Value!.ProposalDigest, "completion-projection-is-not-confirmation", true));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, deniedConfirmation.Outcome);
        Assert.IsNull(deniedConfirmation.Value);
        CollectionAssert.Contains(deniedConfirmation.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision);
        AssertWorkspaceUnchanged(cold, afterAttributes, afterFiles);
        var stale = coldService.LoadCompletionInputs(new(cold.WorkspaceId));
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, stale.Outcome);
        Assert.IsNull(stale.Value, "Completion must not silently discard or replace stale saved Magic choices.");
        CollectionAssert.Contains(stale.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.DraftInvalid);
        AssertWorkspaceUnchanged(cold, afterAttributes, afterFiles);
    }

    [TestMethod]
    public void Actual_source_completion_rejects_corrupt_digest_source_binding_and_invalid_attributes_without_writes()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        var service = new CharacterCreationMagicResonanceService(context.Store, context.Resolver);
        var unprepared = context.Store.Get(context.WorkspaceId).Value!;
        var unpreparedFiles = CaptureFiles(context);
        var noPrerequisite = service.LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, noPrerequisite.Outcome);
        Assert.IsNull(noPrerequisite.Value);
        AssertWorkspaceUnchanged(context, unprepared, unpreparedFiles);

        SavePrerequisite(context, "Magician", "B");
        var inputs = service.LoadCompletionInputs(new(context.WorkspaceId)).Value!;
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        CharacterCreationAttributeAllocation[] attributes = [new("MAG", 1, 0)];
        var choices = CompleteSelections(inputs, inputs.SelectedTalent.Magic + 1);
        var corrupt = CharacterCreationMagicResonanceService.PreviewCompletion(
            inputs with { SnapshotDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("wrong-snapshot") }, attributes, choices);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, corrupt.Outcome);
        Assert.IsNull(corrupt.Value);
        CollectionAssert.Contains(corrupt.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.AuthorityUnavailable);

        var sourceDrift = inputs with
        {
            Binding = inputs.Binding with { SourceInputsDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("different-source") }
        };
        sourceDrift = Reseal(sourceDrift);
        var mismatchedSource = CharacterCreationMagicResonanceService.PreviewCompletion(sourceDrift, attributes, choices);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, mismatchedSource.Outcome);
        Assert.IsNull(mismatchedSource.Value);
        CollectionAssert.Contains(mismatchedSource.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.AuthorityUnavailable);

        var wrongAttributes = inputs with
        {
            Attributes = inputs.Attributes with
            {
                Binding = inputs.Attributes.Binding with { ContentRevision = inputs.Binding.ContentRevision + 1 }
            }
        };
        wrongAttributes = Reseal(wrongAttributes);
        var mismatchedAttributes = CharacterCreationMagicResonanceService.PreviewCompletion(wrongAttributes, attributes, choices);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, mismatchedAttributes.Outcome);
        Assert.IsNull(mismatchedAttributes.Value);
        CollectionAssert.Contains(mismatchedAttributes.Blockers.ToArray(), CharacterCreationMagicResonanceCompletionSchemas.SnapshotConflict);

        var overBudget = CharacterCreationMagicResonanceService.PreviewCompletion(inputs, [new("MAG", 100, 0)], choices);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, overBudget.Outcome);
        Assert.IsNull(overBudget.Value);
        var incomplete = CharacterCreationMagicResonanceService.PreviewCompletion(inputs, attributes,
            new CharacterCreationMagicResonanceSelections(null, null, [], [], []));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, incomplete.Outcome);
        Assert.IsNotNull(incomplete.Value, "Incomplete selections remain an explanatory projection, not confirmation authority.");
        CollectionAssert.Contains(incomplete.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.TraditionRequired);
        CollectionAssert.Contains(incomplete.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.SpellBudgetIncomplete);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Actual_source_completion_cancellation_and_detached_mutable_requests_never_change_workspace_or_projection()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        SavePrerequisite(context, "Magician", "B");
        var service = new CharacterCreationMagicResonanceService(context.Store, context.Resolver);
        var inputs = service.LoadCompletionInputs(new(context.WorkspaceId)).Value!;
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        CharacterCreationAttributeAllocation[] attributes = [new("MAG", 1, 0)];
        var choices = CompleteSelections(inputs, inputs.SelectedTalent.Magic + 1);
        var mutableSpells = choices.Spells.ToList();
        choices = choices with { Spells = mutableSpells };
        bool canceled = false;
        try
        {
            CharacterCreationMagicResonanceService.PreviewCompletion(inputs, attributes, choices, new CancellationToken(true));
        }
        catch (OperationCanceledException) { canceled = true; }
        Assert.IsTrue(canceled);
        AssertWorkspaceUnchanged(context, before, files);

        var result = CharacterCreationMagicResonanceService.PreviewCompletion(inputs, attributes, choices);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var captured = CharacterCreationMagicResonanceDigest.Compute(result.Value);
        Assert.IsTrue(mutableSpells.Count > 0);
        mutableSpells.Clear();
        attributes[0] = new("MAG", 100, 0);
        Assert.AreEqual(captured, CharacterCreationMagicResonanceDigest.Compute(result.Value));
        Assert.IsTrue(result.Value!.Selections.Spells.Count > 0);
        Assert.AreEqual(inputs.SnapshotDigest, service.LoadCompletionInputs(new(context.WorkspaceId)).Value!.SnapshotDigest);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Combined_completion_charges_actual_attribute_quality_and_mystic_cost_once_and_never_invents_allowance()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        SavePrerequisite(context, "Mystic Adept", "B");
        var magicService = new CharacterCreationMagicResonanceService(context.Store, context.Resolver);
        var magic = magicService.LoadCompletionInputs(new(context.WorkspaceId)).Value!;
        var qualitiesResult = CompletionQualities(context).LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, qualitiesResult.Outcome, string.Join(",", qualitiesResult.Blockers));
        var qualities = qualitiesResult.Value!;
        // Ambidextrous's {arm} limit is admitted only by the existing mundane
        // anatomy lane. Use an actual fixed-cost SR5 quality for this Mystic.
        var paid = qualities.Authority.Options.Single(item =>
            item.SourceId == Guid.Parse("0d0cb0fc-c79d-418e-9216-8a197369e52d") && item.Rating == 1);
        Assert.AreEqual("Guts", paid.Name);
        Assert.IsTrue(paid.IsSelectable, paid.DisableReasonKey);
        Assert.IsTrue(paid.CountsAgainstKarma);
        Assert.AreEqual(10, paid.KarmaCost);
        CharacterCreationAttributeAllocation[] attributes = [new("MAG", 1, 0), new("BOD", 0, 1)];
        string[] qualityIds = [paid.OptionId];
        var choices = CompleteSelections(magic, magic.SelectedTalent.Magic + 1, mysticPowerPoints: 1);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var combined = CharacterCreationCompletionBudgetService.Preview(magic, qualities, attributes, choices, qualityIds);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, combined.Outcome, string.Join(",", combined.Blockers));
        var preview = combined.Value!;
        int attributeCost = preview.Qualities.Qualities.ProposedAttributeKarmaUsed;
        Assert.IsTrue(attributeCost > 0);
        Assert.IsTrue(paid.KarmaCost > 0);
        Assert.IsTrue(preview.Magic.MysticAdeptPowerPoints!.KarmaCost > 0);
        int expected = qualities.Binding.CreationKarmaTotal - attributeCost - paid.KarmaCost
            - preview.Magic.MysticAdeptPowerPoints.KarmaCost;
        Assert.IsTrue(expected >= 0);
        Assert.AreEqual((int?)expected, preview.AvailableKarmaBeforeResources);
        Assert.AreEqual(preview.Magic.Attributes.PreviewDigest, preview.Qualities.Attributes.PreviewDigest);
        Assert.IsTrue(CharacterCreationMagicResonanceDigest.IsCanonical(preview.ProposalDigest));
        Assert.AreEqual(preview.ProposalDigest,
            CharacterCreationCompletionBudgetService.Preview(magic, qualities, attributes, choices, qualityIds).Value!.ProposalDigest);

        // Missing spells remain blockers, but must not erase a valid PP cost.
        var partial = CharacterCreationCompletionBudgetService.Preview(magic, qualities, attributes,
            choices with { Spells = [] }, qualityIds);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, partial.Outcome);
        Assert.IsNotNull(partial.Value);
        CollectionAssert.Contains(partial.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.SpellBudgetIncomplete);
        Assert.AreEqual((int?)expected, partial.Value!.AvailableKarmaBeforeResources);
        Assert.AreEqual(preview.Magic.MysticAdeptPowerPoints.KarmaCost, partial.Value.Magic.MysticAdeptPowerPoints!.KarmaCost);

        var malformedPurchase = CharacterCreationCompletionBudgetService.Preview(magic, qualities, attributes,
            choices with { MysticAdeptPowerPoints = int.MaxValue }, qualityIds);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, malformedPurchase.Outcome);
        Assert.IsNotNull(malformedPurchase.Value);
        CollectionAssert.Contains(malformedPurchase.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.PowerBudgetUnsupported);
        Assert.IsNull(malformedPurchase.Value!.AvailableKarmaBeforeResources,
            "A rejected purchase must not become a zero-cost purchase in the combined budget.");

        var unaffordableChoices = CompleteSelections(magic, magic.SelectedTalent.Magic + 1,
            mysticPowerPoints: magic.SelectedTalent.Magic + 1);
        var overspent = CharacterCreationCompletionBudgetService.Preview(magic, qualities, attributes, unaffordableChoices, qualityIds);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, overspent.Outcome);
        Assert.IsNotNull(overspent.Value);
        Assert.IsNull(overspent.Value!.AvailableKarmaBeforeResources);
        CollectionAssert.Contains(overspent.Blockers.ToArray(), CharacterCreationFinalizationBlockers.GlobalKarmaExceeded);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Combined_completion_rejects_different_real_workspace_snapshots_without_writing()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        SavePrerequisite(context, "Mystic Adept", "B");
        var magicService = new CharacterCreationMagicResonanceService(context.Store, context.Resolver);
        var original = magicService.LoadCompletionInputs(new(context.WorkspaceId)).Value!;
        var choices = CompleteSelections(original, original.SelectedTalent.Magic + 1, mysticPowerPoints: 1);
        CharacterCreationAttributeAllocation[] attributes = [new("MAG", 1, 0)];
        SaveAttributes(context, attributes);
        var currentQualities = CompletionQualities(context).LoadCompletionInputs(new(context.WorkspaceId)).Value!;
        Assert.AreNotEqual(original.Attributes.SnapshotDigest, currentQualities.Attributes.SnapshotDigest);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var mixed = CharacterCreationCompletionBudgetService.Preview(original, currentQualities, attributes, choices, []);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, mixed.Outcome);
        Assert.IsNull(mixed.Value);
        CollectionAssert.Contains(mixed.Blockers.ToArray(), CharacterCreationCompletionBudgetSchemas.SnapshotConflict);
        bool canceled = false;
        try
        {
            CharacterCreationCompletionBudgetService.Preview(original, currentQualities, attributes, choices, [], new CancellationToken(true));
        }
        catch (OperationCanceledException) { canceled = true; }
        Assert.IsTrue(canceled);
        AssertWorkspaceUnchanged(context, before, files);
    }

    internal static CharacterCreationQualitiesService CompletionQualities(ReadyContext context)
        => new(context.Store, context.Resolver,
            new CharacterCreationPrerequisiteService(context.Store, context.Queries, context.Resolver),
            new CharacterCreationAttributesService(context.Store, context.Resolver));

    internal static void SavePrerequisite(ReadyContext context, string talentName, string talentRank)
    {
        var service = new CharacterCreationPrerequisiteService(context.Store, context.Queries, context.Resolver);
        var loaded = service.Load(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        var state = loaded.Value!;
        var ranks = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CharacterCreationPriorityCategoryIds.Heritage] = "E",
            [CharacterCreationPriorityCategoryIds.Talent] = talentRank,
            [CharacterCreationPriorityCategoryIds.Attributes] = "A",
            [CharacterCreationPriorityCategoryIds.Skills] = "C",
            [CharacterCreationPriorityCategoryIds.Resources] = talentRank == "D" ? "B" : "D"
        };
        var heritage = state.Authority.Options.Single(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage && option.Rank == "E")
            .HeritageOptions.First(option => option.IsEnabled && option.MetavariantSourceId is null && option.MetatypeName == "Human");
        var talent = state.Authority.Options.Single(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Talent && option.Rank == talentRank)
            .TalentOptions.First(option => option.IsEnabled && option.Value == talentName);
        string[] skills = talent.ActiveSkillGrant?.Options.Where(item => item.IsEnabled)
            .Take(talent.ActiveSkillGrant.Quantity).Select(item => item.SelectionId).ToArray() ?? [];
        string[] groups = talent.SkillGroupGrant?.Options.Take(talent.SkillGroupGrant.Quantity)
            .Select(item => item.SelectionId).ToArray() ?? [];
        var request = new CharacterCreationPrerequisitePreviewRequest(state.Binding, ranks)
        {
            HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId,
            TalentActiveSkillSelectionIds = skills, TalentSkillGroupSelectionIds = groups
        };
        var preview = service.Preview(request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, preview.Outcome, string.Join(",", preview.Blockers));
        var saved = service.Confirm(new(state.Binding, ranks, preview.Value!.PreviewDigest, true)
        {
            HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId,
            TalentActiveSkillSelectionIds = skills, TalentSkillGroupSelectionIds = groups
        });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, saved.Outcome, string.Join(",", saved.Blockers));
        Assert.IsNull(context.Store.Get(context.WorkspaceId).Value!.Document.AuxiliaryState.CharacterCreationAttributesDraft);
    }

    internal static void SaveAttributes(ReadyContext context, IReadOnlyList<CharacterCreationAttributeAllocation> allocations)
    {
        var service = new CharacterCreationAttributesService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        var preview = service.Preview(new(state.Binding, allocations));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, preview.Outcome, string.Join(",", preview.Blockers));
        var saved = service.Confirm(new(state.Binding, allocations, preview.Value!.PreviewDigest, true));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, saved.Outcome, string.Join(",", saved.Blockers));
    }

    internal static CharacterCreationMagicResonanceSelections CompleteSelections(
        CharacterCreationMagicResonanceCompletionInputs inputs, int magic, int mysticPowerPoints = 0)
    {
        var talent = inputs.SelectedTalent;
        Assert.IsTrue(CharacterCreationMysticAdeptPowerPointRules.TryEvaluate(inputs.Authority.MysticAdeptPowerPointPolicy,
            talent.Kind, magic, talent.SpellBudget, mysticPowerPoints, out var purchase));
        decimal remaining = talent.Kind == CharacterCreationMagicResonanceKinds.MysticAdept ? mysticPowerPoints
            : talent.Kind == CharacterCreationMagicResonanceKinds.Adept ? magic : 0m;
        var powers = new List<CharacterCreationAdeptPowerAllocation>();
        foreach (var power in inputs.Authority.AdeptPowers.Where(item => item.IsEnabled && item.PointCost > 0)
                     .OrderByDescending(item => item.PointCost).ThenBy(item => item.Identity.SourceId, StringComparer.Ordinal))
        {
            int levels = (int)Math.Min(CharacterCreationAdeptPowerSourceRules.EffectiveMaximumLevels(power, magic),
                decimal.Floor(remaining / power.PointCost));
            if (levels == 0) continue;
            powers.Add(new(power.Identity, levels));
            remaining -= levels * power.PointCost;
        }
        Assert.AreEqual(0m, remaining, "The real source catalogue must fund this fixture without fabricated power costs.");
        return new(
            talent.RequiresTradition ? inputs.Authority.Traditions.Single(item => item.IsEnabled && item.Name == "Hermetic").Identity : null,
            talent.RequiresStream ? inputs.Authority.Streams.Single(item => item.IsEnabled && item.Name == "Default").Identity : null,
            powers,
            inputs.Authority.Spells.Where(item => item.IsEnabled).Take(purchase?.SpellBudget ?? talent.SpellBudget)
                .Select(item => item.Identity).ToArray(),
            inputs.Authority.ComplexForms.Where(item => item.IsEnabled).Take(talent.ComplexFormBudget)
                .Select(item => item.Identity).ToArray()) { MysticAdeptPowerPoints = mysticPowerPoints };
    }

    private static CharacterCreationMagicResonanceCompletionInputs Reseal(CharacterCreationMagicResonanceCompletionInputs inputs)
        => inputs with { SnapshotDigest = CharacterCreationMagicResonanceDigest.Compute(inputs with { SnapshotDigest = string.Empty }) };

    private static void AssertProjectionMatchesOrdinary(CharacterCreationMagicResonanceCompletionProjection proposed,
        CharacterCreationMagicResonancePreview ordinary)
    {
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(proposed.Talent, ordinary.Talent));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(proposed.Selections, ordinary.Selections));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(proposed.TraditionBudget, ordinary.TraditionBudget));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(proposed.StreamBudget, ordinary.StreamBudget));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(proposed.AdeptPowerPointBudget, ordinary.AdeptPowerPointBudget));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(proposed.SpellBudget, ordinary.SpellBudget));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(proposed.ComplexFormBudget, ordinary.ComplexFormBudget));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(proposed.MysticAdeptPowerPoints, ordinary.MysticAdeptPowerPoints));
        CollectionAssert.AreEquivalent(proposed.SourceAnchorIds.ToArray(), ordinary.SourceAnchorIds.ToArray());
    }

    private static IReadOnlyDictionary<string, byte[]> CaptureFiles(ReadyContext context)
        => Directory.GetFiles(context.Directory, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(context.Directory, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertWorkspaceUnchanged(ReadyContext context, WorkspaceStoredDocument before,
        IReadOnlyDictionary<string, byte[]> files)
    {
        var after = context.Store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
        var currentFiles = CaptureFiles(context);
        CollectionAssert.AreEquivalent(files.Keys.ToArray(), currentFiles.Keys.ToArray());
        foreach (var pair in files)
            CollectionAssert.AreEqual(pair.Value, currentFiles[pair.Key], pair.Key);
    }
}
