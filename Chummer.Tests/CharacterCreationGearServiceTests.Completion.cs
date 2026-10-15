using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class GearCompletionTests
{
    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Gear_completion_before_resources_save_uses_real_prices_and_keeps_overspend_blocked(string method)
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(method);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var inputs = LoadInputs(context);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        Assert.IsNull(inputs.Gear.State.ResourcesDraft);
        Assert.IsNull(inputs.Gear.State.PendingDraft);
        Assert.AreEqual(0L, inputs.Gear.State.Binding.ResourcesDraftRevision);
        Assert.AreEqual(string.Empty, inputs.Gear.State.Binding.ResourcesDraftDigest);
        Assert.AreEqual(0, inputs.Gear.Receipts.Count);
        Assert.AreEqual(64, inputs.Gear.State.Binding.AuxiliaryStateDigest.Length,
            "Actual workspace auxiliary digests are bare hex, not domain-prefixed digests.");
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationAttributesDraft);
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationResourcesDraft);
        Assert.IsTrue(CharacterCreationGearRules.IsValidAuthority(inputs.Gear.State.Authority));
        Assert.IsNull(typeof(CharacterCreationGearCompletionProjection).GetProperty("CanConfirm"));

        var empty = Preview(inputs, [], 0);
        Assert.AreEqual(CharacterCreationGearOutcomes.Available, empty.Outcome, string.Join(",", empty.Blockers));
        decimal total = empty.Value!.Budget.TotalStartingNuyen;
        var item = AffordableOption(inputs.Gear.State.Authority, total);
        CharacterCreationGearSelection[] basket = [new(item.OptionId, 1)];
        var result = Preview(inputs, basket, 0);
        Assert.AreEqual(CharacterCreationGearOutcomes.Available, result.Outcome, string.Join(",", result.Blockers));
        var projected = result.Value!;
        Assert.AreEqual(item.PackageCost, projected.Budget.BasketCost);
        Assert.AreEqual(total - item.PackageCost, projected.Budget.RemainingNuyen);
        Assert.AreEqual(item.SourceNodeDigest, projected.Lines.Single().SourceNodeDigest);
        Assert.AreEqual(item.SourceNodeXml, projected.Lines.Single().SourceNodeXml);
        Assert.AreEqual(item.PackageCost, projected.Lines.Single().TotalCost);
        Assert.AreEqual(inputs.Gear.State.Binding, projected.Binding);
        Assert.AreEqual(inputs.Gear.SnapshotDigest, projected.GearSnapshotDigest);
        Assert.IsTrue(CharacterCreationGearRules.IsCanonicalDigest(projected.ProposalDigest));
        Assert.AreEqual(projected.ProposalDigest, Preview(inputs, basket, 0).Value!.ProposalDigest);

        var costly = inputs.Gear.State.Authority.Options.First(option => option.IsSelectable
            && option.PackageQuantity == 1 && option.PackageCost > 0m
            && decimal.Floor(total / option.PackageCost) + 1 <= inputs.Gear.State.Authority.MaximumQuantityPerLine);
        int quantity = (int)decimal.Floor(total / costly.PackageCost) + 1;
        var overspent = Preview(inputs, [new(costly.OptionId, quantity)], 0);
        Assert.AreEqual(CharacterCreationGearOutcomes.Blocked, overspent.Outcome);
        Assert.IsNotNull(overspent.Value);
        CollectionAssert.Contains(overspent.Blockers.ToArray(), CharacterCreationGearBlockers.InsufficientFunds);
        Assert.AreEqual(costly.PackageCost * quantity - total, overspent.Value!.Budget.Overspend);
        Assert.AreEqual(0m, overspent.Value.Budget.RemainingNuyen);
        Assert.IsFalse(overspent.Value.Budget.IsExact);

        var service = new CharacterCreationGearService(context.Store, context.Resolver);
        var ordinary = service.Load(new(context.WorkspaceId));
        CollectionAssert.Contains(ordinary.Blockers.ToArray(), CharacterCreationGearBlockers.ResourcesDraftRequired);
        var confirm = service.Confirm(new(projected.Binding, basket, projected.ProposalDigest,
            "completion-before-resources-is-not-save", true));
        Assert.IsFalse(confirm.Success);
        Assert.IsNull(confirm.Value);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Gear_completion_keeps_ordinary_catalog_quantity_and_unsupported_semantics()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        SaveResources(context, 0, "completion-invalid-resources");
        var inputs = LoadInputs(context);
        var service = new CharacterCreationGearService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        var item = AffordableOption(state.Authority, state.Budget.TotalStartingNuyen);
        var unsupported = state.Authority.Options.First(option => !option.IsSelectable
            && option.Blockers.Contains(CharacterCreationGearBlockers.UnsupportedSemantics));
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        (CharacterCreationGearSelection[] Basket, string Blocker)[] rejected =
        [
            ([new("not-a-source-option", 1)], CharacterCreationGearBlockers.InvalidOption),
            ([new(item.OptionId, 0)], CharacterCreationGearBlockers.InvalidQuantity),
            ([new(item.OptionId, state.Authority.MaximumQuantityPerLine + 1)], CharacterCreationGearBlockers.InvalidQuantity),
            ([new(item.OptionId, 1), new(item.OptionId, 1)], CharacterCreationGearBlockers.DuplicateOption),
            ([new(unsupported.OptionId, 1)], CharacterCreationGearBlockers.UnsupportedSemantics),
            (Enumerable.Repeat(new CharacterCreationGearSelection(item.OptionId, 1),
                state.Authority.MaximumBasketLines + 1).ToArray(), CharacterCreationGearBlockers.InvalidBasket)
        ];
        foreach (var example in rejected)
        {
            var projected = Preview(inputs, example.Basket, 0);
            var ordinary = service.Preview(new(state.Binding, example.Basket));
            Assert.AreEqual(CharacterCreationGearOutcomes.Blocked, projected.Outcome);
            Assert.IsNotNull(projected.Value, example.Blocker);
            Assert.IsNotNull(ordinary.Value, example.Blocker);
            CollectionAssert.Contains(projected.Blockers.ToArray(), example.Blocker);
            CollectionAssert.AreEquivalent(ordinary.Blockers.ToArray(), projected.Blockers.ToArray(), example.Blocker);
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                ordinary.Value!.BudgetAfter, projected.Value!.Budget), example.Blocker);
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                ordinary.Value.After.Lines, projected.Value.Lines), example.Blocker);
        }
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Gear_completion_preserves_cold_saved_basket_history_and_replaces_its_cost_once()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var resourcesReceipt = SaveResources(context, 0, "completion-gear-resources-zero");
        var service = new CharacterCreationGearService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        var item = AffordableOption(state.Authority, state.Budget.TotalStartingNuyen / 2m);
        CharacterCreationGearSelection[] originalBasket = [new(item.OptionId, 1)];
        var ordinary = service.Preview(new(state.Binding, originalBasket)).Value!;
        Assert.IsTrue(ordinary.CanConfirm, string.Join(",", ordinary.Blockers));
        var saved = service.Confirm(new(ordinary.Binding, originalBasket, ordinary.PreviewDigest,
            "completion-gear-saved-basket", true));
        Assert.AreEqual(CharacterCreationGearOutcomes.Applied, saved.Outcome, string.Join(",", saved.Blockers));

        using ReadyContext cold = context.Restart();
        var before = cold.Store.Get(cold.WorkspaceId).Value!;
        var files = CaptureFiles(cold);
        var inputs = LoadInputs(cold);
        Assert.AreEqual(saved.Value!.DraftDigest, inputs.Gear.State.PendingDraft!.DraftDigest);
        Assert.AreEqual(1, inputs.Gear.Receipts.Count);
        Assert.AreEqual(saved.Value.ReceiptDigest, inputs.Gear.Receipts.Single().Receipt.ReceiptDigest);
        Assert.AreEqual(resourcesReceipt.ReceiptDigest, inputs.Gear.Resources.Receipts.Single().Receipt.ReceiptDigest);
        Assert.AreEqual(item.PackageCost, inputs.Gear.Resources.KnownPurchaseCost);
        Assert.AreEqual(1, inputs.Gear.State.PendingDraft.Lines.Single().Quantity);

        var retained = Preview(inputs, originalBasket, 0);
        Assert.AreEqual(CharacterCreationGearOutcomes.Available, retained.Outcome, string.Join(",", retained.Blockers));
        Assert.IsFalse(retained.Blockers.Contains(CharacterCreationGearBlockers.NoChange));
        Assert.AreEqual(item.PackageCost, retained.Value!.Budget.BasketCost);

        CharacterCreationGearSelection[] replacement = [new(item.OptionId, 2)];
        var replaced = Preview(inputs, replacement, 3);
        Assert.AreEqual(CharacterCreationGearOutcomes.Available, replaced.Outcome, string.Join(",", replaced.Blockers));
        decimal projectedTotal = inputs.Gear.Resources.PriorityOption.BasePriorityNuyen
            + 3m * inputs.Gear.Resources.Authority.KarmaToNuyenRate;
        Assert.AreEqual(projectedTotal, replaced.Value!.Budget.TotalStartingNuyen);
        Assert.AreEqual(item.PackageCost * 2m, replaced.Value.Budget.BasketCost);
        Assert.AreEqual(item.PackageCost * 2m, replaced.Value.Resources.Budget.KnownPurchaseCost);
        Assert.AreEqual(projectedTotal - item.PackageCost * 2m, replaced.Value.Budget.RemainingNuyen,
            "The proposed basket replaces the old basket; the saved basket must not be charged twice.");
        Assert.AreEqual(2, replaced.Value.Lines.Single().Quantity);
        Assert.AreEqual(inputs.Gear.State.PendingDraft.DraftDigest, LoadInputs(cold).Gear.State.PendingDraft!.DraftDigest);
        AssertWorkspaceUnchanged(cold, before, files);

        SaveResources(cold, 3, "completion-gear-resources-three");
        var currentService = new CharacterCreationGearService(cold.Store, cold.Resolver);
        var current = currentService.Load(new(cold.WorkspaceId)).Value!;
        var afterSave = currentService.Preview(new(current.Binding, replacement)).Value!;
        Assert.IsTrue(afterSave.CanConfirm, string.Join(",", afterSave.Blockers));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            afterSave.BudgetAfter, replaced.Value.Budget));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            afterSave.After.Lines, replaced.Value.Lines));
        Assert.AreEqual(1, cold.Store.Get(cold.WorkspaceId).Value!.Document.AuxiliaryState.CharacterCreationGearReceipts!.Count);
    }

    [TestMethod]
    public void Gear_completion_lower_funding_prices_cheaper_replacement_instead_of_retained_basket()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        SaveResources(context, 3, "completion-lower-funding-resources");
        var service = new CharacterCreationGearService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        var inputs = LoadInputs(context);
        decimal lowerTotal = inputs.Gear.Resources.PriorityOption.BasePriorityNuyen;
        decimal extra = 3m * inputs.Gear.Resources.Authority.KarmaToNuyenRate;
        var item = state.Authority.Options.Where(option => option.IsSelectable && option.PackageQuantity == 1
                && option.PackageCost > 0m && option.PackageCost <= extra
                && decimal.Floor(lowerTotal / option.PackageCost) + 1 <= state.Authority.MaximumQuantityPerLine)
            .OrderBy(option => option.PackageCost).ThenBy(option => option.OptionId, StringComparer.Ordinal).First();
        int quantity = (int)decimal.Floor(lowerTotal / item.PackageCost) + 1;
        Assert.IsTrue(quantity <= state.Authority.MaximumQuantityPerLine);
        Assert.IsTrue(item.PackageCost * quantity > lowerTotal);
        Assert.IsTrue(item.PackageCost * quantity <= state.Budget.TotalStartingNuyen);
        CharacterCreationGearSelection[] original = [new(item.OptionId, quantity)];
        var originalPreview = service.Preview(new(state.Binding, original)).Value!;
        Assert.IsTrue(originalPreview.CanConfirm, string.Join(",", originalPreview.Blockers));
        var saved = service.Confirm(new(originalPreview.Binding, original, originalPreview.PreviewDigest,
            "completion-lower-funding-basket", true));
        Assert.AreEqual(CharacterCreationGearOutcomes.Applied, saved.Outcome, string.Join(",", saved.Blockers));
        inputs = LoadInputs(context);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var oldBasketFunding = CharacterCreationResourcesService.PreviewCompletion(inputs.Gear.Resources,
            inputs.Magic, inputs.Qualities, Attributes(), Selections(inputs), [], 0);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Blocked, oldBasketFunding.Outcome,
            "The retained costly basket does not fit the proposed lower funding.");
        Assert.IsNotNull(oldBasketFunding.Value);
        Assert.IsTrue(oldBasketFunding.Value!.Budget.Overspend > 0m);

        foreach (CharacterCreationGearSelection[] replacement in new CharacterCreationGearSelection[][]
                 { [], [new(item.OptionId, 1)] })
        {
            var projected = Preview(inputs, replacement, 0);
            Assert.AreEqual(CharacterCreationGearOutcomes.Available, projected.Outcome, string.Join(",", projected.Blockers));
            decimal expectedCost = replacement.Length == 0 ? 0m : item.PackageCost;
            Assert.AreEqual(lowerTotal, projected.Value!.Budget.TotalStartingNuyen);
            Assert.AreEqual(expectedCost, projected.Value.Budget.BasketCost);
            Assert.AreEqual(expectedCost, projected.Value.Resources.Budget.KnownPurchaseCost);
            Assert.AreEqual(lowerTotal - expectedCost, projected.Value.Resources.Budget.RemainingNuyen);
            Assert.AreEqual(0m, projected.Value.Resources.Budget.Overspend);
            Assert.AreEqual(0, projected.Value.Resources.Blockers.Count);
            Assert.AreEqual(inputs.Gear.Resources.Binding, projected.Value.Resources.Binding);
        }
        Assert.AreEqual(quantity, inputs.Gear.State.PendingDraft!.Lines.Single().Quantity);
        Assert.AreEqual(3, inputs.Gear.Resources.PendingDraft!.KarmaInvestment);
        Assert.AreEqual(saved.Value!.ReceiptDigest, LoadInputs(context).Gear.Receipts.Single().Receipt.ReceiptDigest);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Gear_completion_rejects_mixed_snapshots_and_proposal_digest_is_not_confirm_authority()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        SaveResources(context, 0, "completion-gear-confirm-resources");
        var inputs = LoadInputs(context);
        var item = AffordableOption(inputs.Gear.State.Authority, inputs.Gear.Resources.PriorityOption.BasePriorityNuyen);
        CharacterCreationGearSelection[] basket = [new(item.OptionId, 1)];
        var projected = Preview(inputs, basket, 0).Value!;
        var service = new CharacterCreationGearService(context.Store, context.Resolver);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var rejected = service.Confirm(new(projected.Binding, basket, projected.ProposalDigest,
            "completion-proposal-is-not-gear-confirm", true));
        Assert.AreEqual(CharacterCreationGearOutcomes.Conflict, rejected.Outcome);
        Assert.IsNull(rejected.Value);
        CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationGearBlockers.PreviewDigestMismatch);
        AssertWorkspaceUnchanged(context, before, files);

        MagicResonanceCompletionTests.SaveAttributes(context, Attributes());
        var current = LoadInputs(context);
        Assert.AreNotEqual(inputs.Gear.State.Binding.ContentRevision, current.Gear.State.Binding.ContentRevision);
        before = context.Store.Get(context.WorkspaceId).Value!;
        files = CaptureFiles(context);
        var mixed = CharacterCreationGearService.PreviewCompletion(inputs.Gear, current.Magic, current.Qualities,
            Attributes(), Selections(current), [], 0, basket);
        Assert.AreEqual(CharacterCreationGearOutcomes.Conflict, mixed.Outcome);
        Assert.IsNull(mixed.Value);
        var stale = service.Confirm(new(projected.Binding, basket, projected.ProposalDigest,
            "completion-stale-gear-confirm", true));
        Assert.AreEqual(CharacterCreationGearOutcomes.Conflict, stale.Outcome);
        Assert.IsNull(stale.Value);
        CollectionAssert.Contains(stale.Blockers.ToArray(), CharacterCreationGearBlockers.StaleWorkspaceRevision);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Gear_completion_cancellation_invalid_digest_and_detached_results_do_not_write()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var inputs = LoadInputs(context);
        var item = AffordableOption(inputs.Gear.State.Authority, inputs.Gear.Resources.PriorityOption.BasePriorityNuyen);
        CharacterCreationGearSelection[] basket = [new(item.OptionId, 1)];
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        bool canceled = false;
        try
        {
            CharacterCreationGearService.PreviewCompletion(inputs.Gear, inputs.Magic, inputs.Qualities,
                Attributes(), Selections(inputs), [], 0, basket, new CancellationToken(true));
        }
        catch (OperationCanceledException) { canceled = true; }
        Assert.IsTrue(canceled);
        var corrupt = CharacterCreationGearService.PreviewCompletion(
            inputs.Gear with { SnapshotDigest = CharacterCreationGearRules.ComputeUtf8("not-admitted") },
            inputs.Magic, inputs.Qualities, Attributes(), Selections(inputs), [], 0, basket);
        Assert.IsFalse(corrupt.Success);
        Assert.IsNull(corrupt.Value);
        foreach (string invalidAuxiliaryDigest in new[]
                 { "sha256:" + inputs.Gear.State.Binding.AuxiliaryStateDigest, new string('g', 64) })
        {
            var invalidState = inputs.Gear.State with
            {
                Binding = inputs.Gear.State.Binding with { AuxiliaryStateDigest = invalidAuxiliaryDigest }
            };
            invalidState = invalidState with { SnapshotDigest = CharacterCreationGearRules.ComputeStateDigest(invalidState) };
            var invalidResources = inputs.Gear.Resources with
            {
                Binding = inputs.Gear.Resources.Binding with { AuxiliaryStateDigest = invalidAuxiliaryDigest }
            };
            invalidResources = invalidResources with
            {
                SnapshotDigest = CharacterCreationResourcesCompletionRules.ComputeInputsDigest(invalidResources)
            };
            var invalidInputs = inputs.Gear with { State = invalidState, Resources = invalidResources };
            invalidInputs = invalidInputs with
            {
                SnapshotDigest = CharacterCreationGearCompletionRules.ComputeInputsDigest(invalidInputs)
            };
            var malformed = CharacterCreationGearService.PreviewCompletion(invalidInputs, inputs.Magic, inputs.Qualities,
                Attributes(), Selections(inputs), [], 0, basket);
            Assert.AreEqual(CharacterCreationGearOutcomes.Blocked, malformed.Outcome);
            Assert.IsNull(malformed.Value);
            CollectionAssert.Contains(malformed.Blockers.ToArray(), CharacterCreationGearBlockers.AuthorityUnavailable,
                "Resealing mutually consistent snapshots does not admit an invalid auxiliary digest shape.");
        }
        var projected = Preview(inputs, basket, 0).Value!;
        string digest = CharacterCreationGearRules.Compute(projected);
        basket[0] = new("not-a-source-option", int.MaxValue);
        Assert.AreEqual(digest, CharacterCreationGearRules.Compute(projected));
        Assert.AreEqual(item.OptionId, projected.Lines.Single().OptionId);
        Assert.AreEqual(1, projected.Lines.Single().Quantity);
        Assert.AreEqual(inputs.Gear.SnapshotDigest, LoadInputs(context).Gear.SnapshotDigest);
        AssertWorkspaceUnchanged(context, before, files);
    }

    private static CharacterCreationAttributeAllocation[] Attributes() => [new("MAG", 1, 0)];

    private static CharacterCreationMagicResonanceSelections Selections(CompletionInputs inputs)
        => MagicResonanceCompletionTests.CompleteSelections(inputs.Magic,
            inputs.Magic.SelectedTalent.Magic + 1, mysticPowerPoints: 1);

    private static CharacterCreationGearResult<CharacterCreationGearCompletionProjection> Preview(
        CompletionInputs inputs, IReadOnlyList<CharacterCreationGearSelection> basket, int totalInvestment)
        => CharacterCreationGearService.PreviewCompletion(inputs.Gear, inputs.Magic, inputs.Qualities,
            Attributes(), Selections(inputs), [], totalInvestment, basket);

    private static CharacterCreationGearCatalogOption AffordableOption(CharacterCreationGearAuthority authority, decimal total)
        => authority.Options.Where(option => option.IsSelectable && option.PackageQuantity == 1
                && option.PackageCost > 0m && option.PackageCost <= total)
            .OrderBy(option => option.PackageCost).ThenBy(option => option.OptionId, StringComparer.Ordinal).First();

    private static CharacterCreationResourcesReceipt SaveResources(ReadyContext context, int karma, string key)
    {
        var service = new CharacterCreationResourcesService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        var option = state.Options.Single(item => item.IsEnabled && item.KarmaInvestment == karma);
        var preview = service.Preview(new(state.Binding, option.OptionId)).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        var saved = service.Confirm(new(preview.Binding, option.OptionId, preview.PreviewDigest, key, true));
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Applied, saved.Outcome, string.Join(",", saved.Blockers));
        return saved.Value!;
    }

    private static CompletionInputs LoadInputs(ReadyContext context)
    {
        var gear = new CharacterCreationGearService(context.Store, context.Resolver).LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationGearOutcomes.Available, gear.Outcome, string.Join(",", gear.Blockers));
        var magic = new CharacterCreationMagicResonanceService(context.Store, context.Resolver).LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, magic.Outcome, string.Join(",", magic.Blockers));
        var qualities = MagicResonanceCompletionTests.CompletionQualities(context).LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, qualities.Outcome, string.Join(",", qualities.Blockers));
        return new(gear.Value!, magic.Value!, qualities.Value!);
    }

    private sealed record CompletionInputs(CharacterCreationGearCompletionInputs Gear,
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
