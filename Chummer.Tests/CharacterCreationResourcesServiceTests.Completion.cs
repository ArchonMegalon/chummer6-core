using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class ResourcesCompletionTests
{
    [TestMethod]
    public void Actual_resources_before_first_save_recomputes_mystic_cost_and_never_substitutes_zero_for_unknown_allowance()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var inputs = LoadInputs(context);
        Assert.IsNull(inputs.Resources.PendingDraft);
        Assert.AreEqual(0, inputs.Resources.Receipts.Count);
        Assert.IsTrue(inputs.Resources.PurchaseAuthorityExact);
        Assert.AreEqual(0m, inputs.Resources.KnownPurchaseCost);
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationResourcesDraft);
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationAttributesDraft);
        Assert.IsTrue(CharacterCreationResourcesRules.IsCanonicalDigest(inputs.Resources.SnapshotDigest));
        Assert.AreEqual(inputs.Resources.SnapshotDigest,
            CharacterCreationResourcesCompletionRules.ComputeInputsDigest(inputs.Resources));
        Assert.IsNull(typeof(CharacterCreationResourcesCompletionProjection).GetProperty("CanConfirm"));

        CharacterCreationAttributeAllocation[] attributes = [new("MAG", 1, 0)];
        string[] qualityIds = [GutsOptionId(inputs.Qualities)];
        var onePoint = MagicResonanceCompletionTests.CompleteSelections(inputs.Magic,
            inputs.Magic.SelectedTalent.Magic + 1, mysticPowerPoints: 1);
        var result = Preview(inputs, attributes, onePoint, qualityIds, totalInvestment: 6);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Available, result.Outcome, string.Join(",", result.Blockers));
        var projection = result.Value!;
        int onePointCost = projection.UpstreamBudget.Magic.MysticAdeptPowerPoints!.KarmaCost;
        Assert.IsTrue(onePointCost > 0);
        int available = inputs.Qualities.Binding.CreationKarmaTotal - 10 - onePointCost;
        Assert.AreEqual((int?)available, projection.UpstreamBudget.AvailableKarmaBeforeResources);
        Assert.AreEqual(available - 6, projection.RemainingCreationKarma);
        Assert.AreEqual(6, projection.SelectedOption!.KarmaInvestment);
        Assert.AreEqual(6m * inputs.Resources.Authority.KarmaToNuyenRate, projection.Budget.NuyenFromKarma);
        Assert.AreEqual(inputs.Resources.PriorityOption.BasePriorityNuyen + projection.Budget.NuyenFromKarma,
            projection.Budget.TotalStartingNuyen);
        Assert.AreEqual(projection.Budget.TotalStartingNuyen, projection.Budget.RemainingNuyen);
        Assert.IsTrue(projection.Options.Single(item => item.KarmaInvestment == 6).IsEnabled);
        Assert.IsTrue(CharacterCreationResourcesRules.IsCanonicalDigest(projection.ProposalDigest));
        Assert.AreEqual(projection.ProposalDigest,
            CharacterCreationResourcesCompletionRules.ComputeProjectionDigest(projection));
        Assert.AreEqual(projection.ProposalDigest, Preview(inputs, attributes, onePoint, qualityIds, 6).Value!.ProposalDigest);

        var twoPoints = MagicResonanceCompletionTests.CompleteSelections(inputs.Magic,
            inputs.Magic.SelectedTalent.Magic + 1, mysticPowerPoints: 2);
        var lessAllowance = Preview(inputs, attributes, twoPoints, qualityIds, totalInvestment: 6);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Blocked, lessAllowance.Outcome);
        Assert.IsNotNull(lessAllowance.Value);
        var reduced = lessAllowance.Value!;
        Assert.AreEqual(2 * onePointCost, reduced.UpstreamBudget.Magic.MysticAdeptPowerPoints!.KarmaCost);
        Assert.AreEqual((int?)(available - onePointCost), reduced.UpstreamBudget.AvailableKarmaBeforeResources);
        Assert.IsFalse(reduced.Options.Single(item => item.KarmaInvestment == 6).IsEnabled);
        CollectionAssert.Contains(lessAllowance.Blockers.ToArray(), CharacterCreationResourcesBlockers.InsufficientCreationKarma);

        var unknown = Preview(inputs, attributes, onePoint with { MysticAdeptPowerPoints = int.MaxValue },
            qualityIds, totalInvestment: 0);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Blocked, unknown.Outcome);
        Assert.IsNull(unknown.Value,
            "Unknown upstream allowance must not manufacture any Resources option, even zero investment.");
        CollectionAssert.Contains(unknown.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.PowerBudgetUnsupported);
        CollectionAssert.Contains(unknown.Blockers.ToArray(), CharacterCreationResourcesCompletionSchemas.UpstreamBudgetUnavailable);
        // Exact zero is a valid allowance, unlike the unknown case above.
        var zero = Preview(inputs, [new("MAG", 1, 0), new("BOD", 0, 1)], onePoint,
            qualityIds, totalInvestment: 0);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Available, zero.Outcome, string.Join(",", zero.Blockers));
        Assert.AreEqual((int?)0, zero.Value!.UpstreamBudget.AvailableKarmaBeforeResources);
        Assert.AreEqual(0, zero.Value.RemainingCreationKarma);
        Assert.IsTrue(zero.Value.Options.Single(item => item.KarmaInvestment == 0).IsEnabled);
        Assert.IsFalse(zero.Value.Options.Single(item => item.KarmaInvestment == 1).IsEnabled);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Saved_resources_and_real_gear_history_are_preserved_while_total_investment_is_charged_only_once()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var resources = new CharacterCreationResourcesService(context.Store, context.Resolver);
        var resourceState = resources.Load(new(context.WorkspaceId)).Value!;
        var savedOption = resourceState.Options.Single(item => item.IsEnabled && item.KarmaInvestment == 3);
        var resourcePreview = resources.Preview(new(resourceState.Binding, savedOption.OptionId)).Value!;
        Assert.IsTrue(resourcePreview.CanConfirm, string.Join(",", resourcePreview.Blockers));
        var resourceCommand = new CharacterCreationResourcesConfirmRequest(resourcePreview.Binding,
            savedOption.OptionId, resourcePreview.PreviewDigest, "completion-resources-saved-three", true);
        var resourceReceipt = resources.Confirm(resourceCommand);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Applied, resourceReceipt.Outcome, string.Join(",", resourceReceipt.Blockers));

        var gear = new CharacterCreationGearService(context.Store, context.Resolver);
        var gearState = gear.Load(new(context.WorkspaceId)).Value!;
        var item = gearState.Authority.Options.Where(option => option.IsSelectable && option.PackageQuantity == 1
                && option.PackageCost > 0m && option.PackageCost <= gearState.Budget.TotalStartingNuyen)
            .OrderBy(option => option.PackageCost).ThenBy(option => option.OptionId, StringComparer.Ordinal).First();
        CharacterCreationGearSelection[] basket = [new(item.OptionId, Quantity: 1)];
        var gearPreview = gear.Preview(new(gearState.Binding, basket)).Value!;
        Assert.IsTrue(gearPreview.CanConfirm, string.Join(",", gearPreview.Blockers));
        var gearReceipt = gear.Confirm(new(gearPreview.Binding, basket, gearPreview.PreviewDigest,
            "completion-resources-saved-gear", true));
        Assert.AreEqual(CharacterCreationGearOutcomes.Applied, gearReceipt.Outcome, string.Join(",", gearReceipt.Blockers));

        using ReadyContext cold = context.Restart();
        var before = cold.Store.Get(cold.WorkspaceId).Value!;
        var files = CaptureFiles(cold);
        var inputs = LoadInputs(cold);
        var savedDraft = inputs.Resources.PendingDraft!;
        Assert.AreEqual(3, savedDraft.KarmaInvestment);
        Assert.AreEqual(1, inputs.Resources.Receipts.Count);
        Assert.AreEqual(resourceReceipt.Value!.ReceiptDigest, inputs.Resources.Receipts.Single().Receipt.ReceiptDigest);
        Assert.AreEqual(item.PackageCost, inputs.Resources.KnownPurchaseCost);
        var savedGearReceipts = before.Document.AuxiliaryState.CharacterCreationGearReceipts!;
        Assert.AreEqual(1, savedGearReceipts.Count);
        Assert.AreEqual(gearReceipt.Value!.ReceiptDigest,
            savedGearReceipts.Single().Receipt.ReceiptDigest);
        CharacterCreationAttributeAllocation[] attributes = [new("MAG", 1, 0)];
        string[] qualityIds = [GutsOptionId(inputs.Qualities)];
        var choices = MagicResonanceCompletionTests.CompleteSelections(inputs.Magic,
            inputs.Magic.SelectedTalent.Magic + 1, mysticPowerPoints: 1);

        var unchanged = Preview(inputs, attributes, choices, qualityIds, totalInvestment: 3);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Available, unchanged.Outcome, string.Join(",", unchanged.Blockers));
        Assert.IsFalse(unchanged.Blockers.Contains(CharacterCreationResourcesBlockers.NoChange),
            "Retaining a saved choice is valid in a pure whole-character proposal.");
        var retained = unchanged.Value!;
        int allowance = retained.UpstreamBudget.AvailableKarmaBeforeResources!.Value;
        Assert.AreEqual(allowance - 3, retained.RemainingCreationKarma);
        Assert.AreEqual(10, allowance, "Actual 25 Karma minus Guts 10 and source PP1 cost 5.");

        var allRemaining = Preview(inputs, attributes, choices, qualityIds, totalInvestment: allowance);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Available, allRemaining.Outcome, string.Join(",", allRemaining.Blockers));
        var exhausted = allRemaining.Value!;
        Assert.AreEqual(0, exhausted.RemainingCreationKarma);
        Assert.AreEqual(allowance, exhausted.SelectedOption.KarmaInvestment);
        Assert.AreEqual(allowance * inputs.Resources.Authority.KarmaToNuyenRate, exhausted.Budget.NuyenFromKarma);
        Assert.AreEqual(item.PackageCost, exhausted.Budget.KnownPurchaseCost);
        Assert.AreEqual(exhausted.Budget.TotalStartingNuyen - item.PackageCost,
            exhausted.Budget.RemainingNuyen);
        Assert.AreEqual(savedDraft.DraftDigest,
            LoadInputs(cold).Resources.PendingDraft!.DraftDigest);
        AssertWorkspaceUnchanged(cold, before, files);
    }

    [TestMethod]
    public void Resources_projection_is_not_confirm_authority_and_mixed_actual_snapshots_are_rejected_without_writes()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var inputs = LoadInputs(context);
        CharacterCreationAttributeAllocation[] attributes = [new("MAG", 1, 0)];
        string[] qualityIds = [GutsOptionId(inputs.Qualities)];
        var choices = MagicResonanceCompletionTests.CompleteSelections(inputs.Magic,
            inputs.Magic.SelectedTalent.Magic + 1, mysticPowerPoints: 1);
        var projected = Preview(inputs, attributes, choices, qualityIds, totalInvestment: 3).Value!;
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var resources = new CharacterCreationResourcesService(context.Store, context.Resolver);
        var rejected = resources.Confirm(new(projected.Binding, projected.SelectedOption!.OptionId,
            projected.ProposalDigest, "completion-projection-is-not-resource-confirm", true));
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Conflict, rejected.Outcome);
        Assert.IsNull(rejected.Value);
        CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationResourcesBlockers.PreviewDigestMismatch);
        AssertWorkspaceUnchanged(context, before, files);

        MagicResonanceCompletionTests.SaveAttributes(context, attributes);
        var current = LoadInputs(context);
        Assert.AreNotEqual(inputs.Resources.Binding.ContentRevision, current.Resources.Binding.ContentRevision);
        before = context.Store.Get(context.WorkspaceId).Value!;
        files = CaptureFiles(context);
        var mixed = CharacterCreationResourcesService.PreviewCompletion(inputs.Resources, current.Magic,
            current.Qualities, attributes, choices, qualityIds, 3);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Conflict, mixed.Outcome);
        Assert.IsNull(mixed.Value);
        CollectionAssert.Contains(mixed.Blockers.ToArray(), CharacterCreationResourcesCompletionSchemas.SnapshotConflict);
        // A conflict found deeper in the shared budget evaluator must retain
        // its exact classification through Resources, Gear and Lifestyles.
        var mixedMagic = CharacterCreationResourcesService.PreviewCompletion(inputs.Resources, current.Magic,
            inputs.Qualities, attributes, choices, qualityIds, 3);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Conflict, mixedMagic.Outcome);
        Assert.IsNull(mixedMagic.Value);
        CollectionAssert.Contains(mixedMagic.Blockers.ToArray(), CharacterCreationCompletionBudgetSchemas.SnapshotConflict);
        var stale = resources.Confirm(new(projected.Binding, projected.SelectedOption.OptionId,
            projected.ProposalDigest, "completion-stale-resource-confirm", true));
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Conflict, stale.Outcome);
        Assert.IsNull(stale.Value);
        CollectionAssert.Contains(stale.Blockers.ToArray(), CharacterCreationResourcesBlockers.StaleWorkspaceRevision);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Resources_completion_cancellation_digest_validation_and_detached_choices_leave_exact_files_unchanged()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var inputs = LoadInputs(context);
        CharacterCreationAttributeAllocation[] attributes = [new("MAG", 1, 0)];
        string[] qualityIds = [GutsOptionId(inputs.Qualities)];
        var choices = MagicResonanceCompletionTests.CompleteSelections(inputs.Magic,
            inputs.Magic.SelectedTalent.Magic + 1, mysticPowerPoints: 1);
        var mutableSpells = choices.Spells.ToList();
        choices = choices with { Spells = mutableSpells };
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        bool cancellationObserved = false;
        try
        {
            CharacterCreationResourcesService.PreviewCompletion(inputs.Resources, inputs.Magic, inputs.Qualities,
                attributes, choices, qualityIds, 3, canceled.Token);
        }
        catch (OperationCanceledException)
        {
            cancellationObserved = true;
        }
        Assert.IsTrue(cancellationObserved);

        var corrupt = CharacterCreationResourcesService.PreviewCompletion(
            inputs.Resources with { SnapshotDigest = CharacterCreationResourcesRules.ComputeUtf8("not-admitted") },
            inputs.Magic, inputs.Qualities, attributes, choices, qualityIds, 3);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Blocked, corrupt.Outcome);
        Assert.IsNull(corrupt.Value);
        CollectionAssert.Contains(corrupt.Blockers.ToArray(), CharacterCreationResourcesBlockers.AuthorityUnavailable);

        Assert.AreEqual(64, inputs.Resources.Binding.AuxiliaryStateDigest.Length,
            "Workspace auxiliary digests are bare SHA-256, not Resources-prefixed digests.");
        var malformedAuxiliary = inputs.Resources with
        {
            Binding = inputs.Resources.Binding with
            {
                AuxiliaryStateDigest = "sha256:" + inputs.Resources.Binding.AuxiliaryStateDigest
            }
        };
        malformedAuxiliary = malformedAuxiliary with
        {
            SnapshotDigest = CharacterCreationResourcesCompletionRules.ComputeInputsDigest(malformedAuxiliary)
        };
        var malformed = CharacterCreationResourcesService.PreviewCompletion(malformedAuxiliary,
            inputs.Magic, inputs.Qualities, attributes, choices, qualityIds, 3);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Blocked, malformed.Outcome);
        Assert.IsNull(malformed.Value);
        CollectionAssert.Contains(malformed.Blockers.ToArray(), CharacterCreationResourcesBlockers.AuthorityUnavailable);

        var projection = Preview(inputs, attributes, choices, qualityIds, 3).Value!;
        string captured = CharacterCreationResourcesRules.Compute(projection);
        int spellCount = projection.UpstreamBudget.Magic.Selections.Spells.Count;
        attributes[0] = new("MAG", 100, 100);
        qualityIds[0] = "not-a-source-quality";
        mutableSpells.Clear();
        Assert.AreEqual(captured, CharacterCreationResourcesRules.Compute(projection));
        Assert.AreEqual(spellCount, projection.UpstreamBudget.Magic.Selections.Spells.Count);
        Assert.IsTrue(spellCount > 0);
        Assert.AreEqual(inputs.Resources.SnapshotDigest, LoadInputs(context).Resources.SnapshotDigest);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Retained_real_lifestyle_spending_is_preserved_in_completion_and_gear_replacement_after_reopen()
    {
        using ReadyContext context = ReadyContext.Create(true, buildMethod: CharacterCreationBuildMethods.Priority);
        var resources = new CharacterCreationResourcesService(context.Store, context.Resolver);
        var initial = resources.LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Available, initial.Outcome, string.Join(",", initial.Blockers));
        var beforeLifestyle = context.Store.Get(context.WorkspaceId).Value!;
        var lifestyles = new CharacterCreationLifestylesService(context.Store, context.Resolver);
        var loaded = lifestyles.Load(new(context.WorkspaceId));
        Assert.IsTrue(loaded.Value?.CanEdit == true, string.Join(",", loaded.Blockers));
        var state = loaded.Value!;
        var low = state.Authority.LifestyleOptions.Single(option => option.Name == "Low");
        Guid id = Guid.Parse("24737a91-54b5-41c0-bbd1-4be3e47c7b28");
        var configuration = new CharacterCreationLifestyleConfiguration(id, low.OptionId, "Completion test apartment",
            CharacterCreationLifestyleStyleIds.Standard, low.DefaultIncrementId, 1, 100m, 0,
            false, false, 0, 0, 0, 0, "Seattle", "", "", []);
        var mutation = new CharacterCreationLifestyleMutation(CharacterCreationLifestyleMutationKinds.Create, id, configuration);
        var preview = lifestyles.Preview(new(state.Binding, mutation));
        Assert.IsTrue(preview.Value?.CanConfirm == true, string.Join(",", preview.Value?.Blockers ?? preview.Blockers));
        Assert.AreEqual(2000m, preview.Value!.BudgetAfter.Used);
        var applied = lifestyles.Confirm(new(state.Binding, mutation, preview.Value.PreviewDigest,
            "completion-lifestyle-spending", true));
        Assert.IsTrue(applied.Success, string.Join(",", applied.Blockers));

        using ReadyContext cold = context.Restart();
        var saved = cold.Store.Get(cold.WorkspaceId).Value!;
        Assert.AreEqual(beforeLifestyle.Document.Content, saved.Document.Content);
        Assert.IsNotNull(saved.Document.AuxiliaryState.CharacterCreationLifestylesDraft);
        Assert.AreEqual(1, saved.Document.AuxiliaryState.CharacterCreationLifestyleReceipts!.Count);
        var files = CaptureFiles(cold);
        var coldResources = new CharacterCreationResourcesService(cold.Store, cold.Resolver);
        var ordinary = coldResources.Load(new(cold.WorkspaceId));
        Assert.IsTrue(ordinary.Value?.CanEdit == true, string.Join(",", ordinary.Blockers));
        var admitted = coldResources.LoadCompletionInputs(new(cold.WorkspaceId));
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Available, admitted.Outcome, string.Join(",", admitted.Blockers));
        Assert.AreEqual(2000m + saved.Document.AuxiliaryState.CharacterCreationGearDraft!.Budget.BasketCost,
            admitted.Value!.KnownPurchaseCost);
        Assert.AreEqual(saved.Document.AuxiliaryState.CharacterCreationLifestylesDraft!.DraftDigest,
            admitted.Value.LifestylesDraft!.DraftDigest);
        Assert.AreEqual(saved.Document.AuxiliaryState.CharacterCreationLifestyleReceipts![0].Receipt.ReceiptDigest,
            admitted.Value.LifestyleReceipts[0].Receipt.ReceiptDigest);
        var gear = new CharacterCreationGearService(cold.Store, cold.Resolver).LoadCompletionInputs(new(cold.WorkspaceId));
        Assert.AreEqual(CharacterCreationGearOutcomes.Available, gear.Outcome, string.Join(",", gear.Blockers));
        var inputs = LoadInputs(cold);
        var attributes = inputs.Qualities.Attributes.PendingDraft!.Allocations;
        var replaced = CharacterCreationGearService.PreviewCompletion(gear.Value!, inputs.Magic,
            inputs.Qualities, attributes, inputs.Magic.Selections, inputs.Qualities.SelectedOptionIds,
            admitted.Value.PendingDraft!.KarmaInvestment, []);
        Assert.AreEqual(CharacterCreationGearOutcomes.Available, replaced.Outcome, string.Join(",", replaced.Blockers));
        Assert.AreEqual(0m, replaced.Value!.Budget.BasketCost);
        Assert.AreEqual(2000m, replaced.Value.Resources.Budget.KnownPurchaseCost,
            "Replacing Gear must not erase the saved apartment or charge it twice.");
        Assert.AreEqual(replaced.Value.Resources.Budget.TotalStartingNuyen - 2000m,
            replaced.Value.Resources.Budget.RemainingNuyen);

        var corrupt = admitted.Value with { LifestyleReceipts = [], SnapshotDigest = string.Empty };
        corrupt = corrupt with { SnapshotDigest = CharacterCreationResourcesCompletionRules.ComputeInputsDigest(corrupt) };
        var rejected = CharacterCreationResourcesService.PreviewCompletion(corrupt, inputs.Magic, inputs.Qualities,
            attributes, inputs.Magic.Selections, inputs.Qualities.SelectedOptionIds,
            admitted.Value.PendingDraft.KarmaInvestment);
        Assert.IsNull(rejected.Value);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Blocked, rejected.Outcome);

        // Re-sealing detached hashes must not make a different retained price
        // agree with the actual purchase receipt's full-list spending.
        var originalDraft = admitted.Value.LifestylesDraft!;
        var forgedOptions = originalDraft.ProjectionAuthority.LifestyleOptions.Select(option =>
        {
            if (option.OptionId != low.OptionId) return option;
            var changed = option with { BaseCost = 1m, OptionDigest = string.Empty };
            return changed with { OptionDigest = CharacterCreationLifestylesRules.ComputeOptionDigest(changed) };
        }).ToArray();
        var forgedAuthority = originalDraft.ProjectionAuthority with
        { LifestyleOptions = forgedOptions, AuthorityDigest = string.Empty };
        forgedAuthority = forgedAuthority with
        { AuthorityDigest = CharacterCreationLifestylesRules.ComputeAuthorityDigest(forgedAuthority) };
        var forgedDraft = originalDraft with { ProjectionAuthority = forgedAuthority, DraftDigest = string.Empty };
        forgedDraft = forgedDraft with { DraftDigest = CharacterCreationLifestylesDraftRules.Digest(forgedDraft) };
        var originalEntry = admitted.Value.LifestyleReceipts.Single();
        var forgedPlan = originalEntry.Receipt.WritePlan with { PendingDraft = forgedDraft, PlanDigest = string.Empty };
        forgedPlan = forgedPlan with { PlanDigest = CharacterCreationLifestylesRules.ComputePlanDigest(forgedPlan) };
        var forgedReceipt = originalEntry.Receipt with { WritePlan = forgedPlan, ReceiptDigest = string.Empty };
        forgedReceipt = forgedReceipt with { ReceiptDigest = CharacterCreationLifestylesRules.ComputeReceiptDigest(forgedReceipt) };
        var forgedEntry = originalEntry with { Receipt = forgedReceipt };
        Assert.IsTrue(CharacterCreationLifestyleReceiptLedgerIntegrity.IsValidLedger(
            cold.WorkspaceId, saved.ContentRevision, [forgedEntry]), "The cost check must add protection beyond ledger shape.");
        var repricedHistory = admitted.Value with
        { LifestylesDraft = forgedDraft, LifestyleReceipts = [forgedEntry], SnapshotDigest = string.Empty };
        repricedHistory = repricedHistory with
        { SnapshotDigest = CharacterCreationResourcesCompletionRules.ComputeInputsDigest(repricedHistory) };
        var refused = CharacterCreationResourcesService.PreviewCompletion(repricedHistory, inputs.Magic, inputs.Qualities,
            attributes, inputs.Magic.Selections, inputs.Qualities.SelectedOptionIds, admitted.Value.PendingDraft.KarmaInvestment);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Blocked, refused.Outcome);
        Assert.IsNull(refused.Value);
        AssertWorkspaceUnchanged(cold, saved, files);
    }

    private static CompletionInputs LoadInputs(ReadyContext context)
    {
        var resources = new CharacterCreationResourcesService(context.Store, context.Resolver)
            .LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Available, resources.Outcome, string.Join(",", resources.Blockers));
        var magic = new CharacterCreationMagicResonanceService(context.Store, context.Resolver)
            .LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, magic.Outcome, string.Join(",", magic.Blockers));
        var qualities = MagicResonanceCompletionTests.CompletionQualities(context)
            .LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, qualities.Outcome, string.Join(",", qualities.Blockers));
        return new(resources.Value!, magic.Value!, qualities.Value!);
    }

    private static string GutsOptionId(CharacterCreationQualitiesCompletionInputs inputs)
    {
        var quality = inputs.Authority.Options.Single(item =>
            item.SourceId == Guid.Parse("0d0cb0fc-c79d-418e-9216-8a197369e52d") && item.Rating == 1);
        Assert.AreEqual("Guts", quality.Name);
        Assert.IsTrue(quality.IsSelectable, quality.DisableReasonKey);
        Assert.IsTrue(quality.CountsAgainstKarma);
        Assert.AreEqual(10, quality.KarmaCost);
        return quality.OptionId;
    }

    private static CharacterCreationResourcesResult<CharacterCreationResourcesCompletionProjection> Preview(
        CompletionInputs inputs, IReadOnlyList<CharacterCreationAttributeAllocation> attributes,
        CharacterCreationMagicResonanceSelections choices, IReadOnlyList<string> qualityIds, int totalInvestment)
        => CharacterCreationResourcesService.PreviewCompletion(inputs.Resources, inputs.Magic, inputs.Qualities,
            attributes, choices, qualityIds, totalInvestment);

    private sealed record CompletionInputs(CharacterCreationResourcesCompletionInputs Resources,
        CharacterCreationMagicResonanceCompletionInputs Magic, CharacterCreationQualitiesCompletionInputs Qualities);

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
