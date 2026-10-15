using System.Reflection;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationManualLoadoutServiceTests
{
    [TestMethod]
    public void Manual_purchase_before_attributes_and_gear_is_reconstructed_by_the_real_completion_reader()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var owner = owners.Capture();
        var resources = new OwnerBoundCharacterCreationResourcesService(context.Store, owners, context.Resolver);
        var funding = resources.Load(owner, new(context.WorkspaceId)).Value!;
        var allocation = funding.Options.Single(row => row.KarmaInvestment == 0 && row.IsEnabled);
        var fundingPreview = resources.Preview(owner, new(funding.Binding, allocation.OptionId)).Value!;
        Assert.IsTrue(fundingPreview.CanConfirm, string.Join(",", fundingPreview.Blockers));
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Applied,
            resources.Confirm(owner, new(funding.Binding, allocation.OptionId, fundingPreview.PreviewDigest, "early-funding", true)).Outcome);
        var manual = new OwnerBoundCharacterCreationLoadoutService(context.Store, owners, context.Resolver);
        var state = manual.Prepare(owner, context.WorkspaceId);
        AssertSuccess(state);
        var weapon = Option(state.Value!.Inputs, CharacterCreationLoadoutKinds.Weapon);
        var command = Preview(manual, owner, state.Value, new([new(weapon.OptionId)], []), "early-weapon");
        AssertSuccess(manual.Confirm(owner, command));
        var store = new FileWorkspaceStore(context.Directory);
        var loaded = new OwnerBoundCharacterCreationCompletionReader(store, owners, context.Resolver, context.Queries)
            .LoadCompletionInputsWithLoadout(owner, new(context.WorkspaceId));
        AssertSuccess(loaded);
        Assert.IsNull(loaded.Value!.Skills.Attributes.PendingDraft);
        Assert.IsNull(loaded.Value.Lifestyles.Gear.State.PendingDraft);
        Assert.AreEqual(weapon.OptionId, loaded.Value.Loadout!.OriginalSelections.Weapons.Single().OptionId);
        Assert.AreEqual(weapon.UnitCost, loaded.Value.Lifestyles.Gear.Resources.KnownPurchaseCost);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Real_manual_purchases_checkpoint_restart_complete_missing_only_and_finalize_once(bool saveArmor)
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        if (saveArmor)
        {
            owners.Set(new OwnerScope("manual-loadout-linked-owner"));
            var adoption = new WorkspaceLocalAdoptionService(context.Store, owners);
            var adoptionReview = adoption.Review(owners.Capture(), context.WorkspaceId);
            Assert.IsNotNull(adoptionReview);
            Assert.AreEqual(WorkspaceLocalAdoptionOutcome.Applied,
                adoption.Confirm(owners.Capture(), adoptionReview, explicitlyConfirmed: true).Outcome);
        }
        var owner = owners.Capture();
        var writes = new CommitProbe();
        var store = new FileWorkspaceStore(context.Directory, writes);
        WorkspaceStoreReadResult Read() => owner.Owner.IsLocalSingleUser
            ? store.Get(context.WorkspaceId) : store.Get(owner.Owner, context.WorkspaceId);
        IOwnerBoundCharacterCreationLoadoutService manual = new OwnerBoundCharacterCreationLoadoutService(store, owners, context.Resolver);
        var before = Read().Value!;
        if (saveArmor) Assert.IsFalse(store.Get(context.WorkspaceId).Success, "No local fallback may conceal a partition error.");
        var files = Snapshot(context);
        var prepared = manual.Prepare(owner, context.WorkspaceId);
        AssertSuccess(prepared);
        var state = prepared.Value!;
        var weapon = Option(state.Inputs, CharacterCreationLoadoutKinds.Weapon);
        var armor = Option(state.Inputs, CharacterCreationLoadoutKinds.Armor);
        var command = Preview(manual, owner, state, new([new(weapon.OptionId, 2)], []), "manual-weapon");
        AssertUnchanged(context, files);
        var purchased = manual.Confirm(owner, command);
        AssertSuccess(purchased);
        var weaponReceipt = purchased.Value!;
        Assert.AreEqual(1, writes.Replacements);
        Assert.AreEqual(before.ContentRevision + 1, weaponReceipt.ContentRevision);
        Assert.AreEqual(weaponReceipt.ContentRevision, Read().Value!.SavedRevision);
        Assert.AreEqual(before.Document.Content, Read().Value!.Document.Content);
        Assert.AreEqual(weapon.UnitCost * 2, weaponReceipt.Draft.PurchaseCost);
        var ids = weaponReceipt.Draft.Instances.Single().InstanceIds.ToArray();
        Assert.AreEqual(2, ids.Distinct().Count());
        if (saveArmor)
        {
            state = manual.Prepare(owner, context.WorkspaceId).Value!;
            var armorCommand = Preview(manual, owner, state,
                new(state.Inputs.OriginalSelections.Weapons, [new(armor.OptionId)]), "manual-armor");
            var second = manual.Confirm(owner, armorCommand);
            AssertSuccess(second);
            Assert.AreEqual(weaponReceipt.ReceiptDigest, second.Value!.PreviousReceiptDigest);
            CollectionAssert.AreEqual(ids, second.Value.Draft.Instances.Single(row => row.OptionId == weapon.OptionId).InstanceIds.ToArray());
        }

        // Fresh store and actual owner-bound reader: no admitted receipt or OriginalSelections is manufactured.
        store = new FileWorkspaceStore(context.Directory, writes);
        var original = Read().Value!;
        var reader = new OwnerBoundCharacterCreationCompletionReader(store, owners, context.Resolver, context.Queries);
        var loaded = reader.LoadCompletionInputsWithLoadout(owner, new(context.WorkspaceId));
        AssertSuccess(loaded);
        var inputs = loaded.Value!;
        var retained = inputs.Loadout!;
        Assert.AreEqual(2, retained.OriginalSelections.Weapons.Single().Quantity);
        Assert.AreEqual(saveArmor ? 1 : 0, retained.OriginalSelections.Armor.Count);
        Assert.AreEqual(saveArmor ? 2 : 1, retained.Receipts!.Count);
        Assert.AreEqual(weapon.SourceId, retained.PendingDraft!.Lines.Single(row => row.Kind == CharacterCreationLoadoutKinds.Weapon).SourceId);
        Assert.AreEqual(weapon.UnitCost, retained.PendingDraft.Lines.Single(row => row.Kind == CharacterCreationLoadoutKinds.Weapon).UnitCost);
        decimal savedTypedCost = 2 * weapon.UnitCost + (saveArmor ? armor.UnitCost : 0m);
        decimal savedGearCost = inputs.Lifestyles.Gear.State.PendingDraft!.Budget.BasketCost;
        Assert.IsTrue(CharacterCreationResourcesService.TryGetRetainedCompletionLifestyleCost(inputs.Lifestyles.Gear.Resources, out decimal lifestyleCost));
        Assert.AreEqual(savedGearCost + lifestyleCost + savedTypedCost, inputs.Lifestyles.Gear.Resources.KnownPurchaseCost);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with { Loadout = retained.OriginalSelections };
        var extra = inputs.Lifestyles.Gear.State.Authority.Options.First(row => row.IsSelectable
            && row.PackageQuantity == 1 && row.PackageCost is > 0m and < 100m && choices.Gear.All(item => item.OptionId != row.OptionId));
        decimal addedCost = extra.PackageCost + (saveArmor ? 0 : armor.UnitCost);
        var request = new CharacterCreationPurchaseCompletionRequest(inputs.Lifestyles.SnapshotDigest, inputs.Magic.SnapshotDigest,
            choices.AttributeAllocations, choices.MagicSelections, choices.QualityOptionIds, choices.TotalKarmaInvestment,
            [new() { Weapon = retained.OriginalSelections.Weapons.Single() }, new() { Armor = new(armor.OptionId) }, new(new(extra.OptionId, 1))],
            choices.StartingLifestyleId)
        {
            LoadoutSnapshotDigest = retained.SnapshotDigest, MaximumAdditionalSpend = addedCost,
            NuyenToReserve = inputs.Lifestyles.Gear.Resources.PendingDraft!.Budget.TotalStartingNuyen
                - inputs.Lifestyles.Gear.Resources.KnownPurchaseCost - addedCost
        };
        var filesBeforeCompletion = Snapshot(context);
        var planned = CharacterCreationPurchaseCompletionPlanner.Plan(inputs.Lifestyles, inputs.Magic, retained, request);
        AssertSuccess(planned);
        var plan = planned.Value!;
        Assert.AreEqual(addedCost, plan.AdditionalSpend);
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.AlreadyPresent, plan.Targets[0].Outcome);
        Assert.AreEqual(saveArmor ? CharacterCreationPurchaseCompletionTargetOutcomes.AlreadyPresent
            : CharacterCreationPurchaseCompletionTargetOutcomes.Added, plan.Targets[1].Outcome);
        Assert.AreEqual(request.NuyenToReserve, plan.Preview.Gear.Resources.Budget.RemainingNuyen);
        Assert.AreEqual(inputs.Lifestyles.Gear.Resources.KnownPurchaseCost + addedCost, plan.Preview.Gear.Resources.Budget.KnownPurchaseCost);
        Assert.AreEqual(2, plan.Loadout!.Weapons.Single().Quantity);
        var capped = CharacterCreationPurchaseCompletionPlanner.Plan(inputs.Lifestyles, inputs.Magic, retained,
            request with { MaximumAdditionalSpend = addedCost - 1 });
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.SpendLimit, capped.Value!.Targets[^1].Outcome);
        Assert.AreEqual(saveArmor ? 0m : armor.UnitCost, capped.Value.AdditionalSpend);
        var otherWeapon = retained.Authority.Options.First(row => row.Kind == CharacterCreationLoadoutKinds.Weapon
            && row.IsSelectable && row.OptionId != weapon.OptionId);
        var locked = CharacterCreationPurchaseCompletionPlanner.Plan(inputs.Lifestyles, inputs.Magic, retained,
            request with { Targets = [new() { Weapon = new(otherWeapon.OptionId) }] });
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.ExistingSelectionLocked, locked.Value!.Targets.Single().Outcome);
        choices = choices with { Gear = plan.Gear, Lifestyles = plan.Lifestyles, Loadout = plan.Loadout };
        var proposal = new CharacterCreationCompletionReviewRequest(inputs.SnapshotDigest, choices);
        var source = context.Resolver.TryCreateContext(original.Document.Content)!;
        var home = choices.Lifestyles.Single(row => row.LifestyleId == choices.StartingLifestyleId);
        var homeSource = inputs.Lifestyles.Authority.LifestyleOptions.Single(row => row.OptionId == home.BaseLifestyleOptionId);
        Assert.IsTrue(source.TryResolveCreationPriorityStartingNuyen(homeSource.SourceId, out var cash));
        Assert.IsNotNull(cash);
        var cashChoice = new CharacterCreationStartingCashChoice(cash.AuthorityDigest, cash.Dice * 3);
        var completion = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        var review = completion.Review(owner, context.WorkspaceId, proposal, cashChoice);
        AssertSuccess(review);
        AssertUnchanged(context, filesBeforeCompletion);
        var apply = new CharacterCreationCompletionApplyRequest(review.Value!.Projection.Binding, review.Value.Proposal,
            review.Value.Projection.ReviewDigest, cashChoice, review.Value.Projection.ProjectionDigest, "manual-loadout-finish", true);
        int manualWrites = writes.Replacements;
        using (var cancellation = new CancellationTokenSource())
        {
            writes.AfterFlush = cancellation.Cancel;
            bool canceled = false;
            try { completion.Apply(owner, apply, cancellation.Token); }
            catch (OperationCanceledException) { canceled = true; }
            Assert.IsTrue(canceled);
            Assert.AreEqual(manualWrites, writes.Replacements);
            AssertUnchanged(context, filesBeforeCompletion);
        }
        var applied = completion.Apply(owner, apply);
        AssertSuccess(applied);
        Assert.AreEqual(manualWrites + 1, writes.Replacements);
        store = new FileWorkspaceStore(context.Directory, writes);
        var reopened = Read();
        Assert.IsTrue(reopened.Success, reopened.Error);
        var saved = reopened.Value!;
        Assert.AreEqual(review.Value.Projection.CharacterXml, saved.Document.Content);
        Assert.AreEqual(original.ContentRevision + 1, saved.ContentRevision);
        Assert.AreEqual(saved.ContentRevision, saved.SavedRevision);
        Assert.AreEqual(1, saved.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!.Count);
        var archived = saved.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.State;
        Assert.AreEqual(saveArmor ? 2 : 1, archived.CharacterCreationLoadoutReceipts!.Count);
        Assert.AreEqual(original.Document.AuxiliaryStateDigest, WorkspaceDocumentAuxiliaryStateDigest.Compute(archived));
        var root = XDocument.Parse(saved.Document.Content).Root!;
        var weapons = root.Element("weapons")!.Elements("weapon").ToArray();
        Assert.AreEqual(2, weapons.Length);
        CollectionAssert.AreEquivalent(ids, weapons.Select(row => Guid.Parse(row.Element("guid")!.Value)).ToArray());
        Assert.IsTrue(weapons.All(row => row.Element("sourceid")!.Value == weapon.SourceId.ToString("D")));
        Assert.IsTrue(weapons.All(row => row.Element("cost")!.Value == XElement.Parse(weapon.SourceNodeXml).Element("cost")!.Value));
        var savedArmor = root.Element("armors")!.Elements("armor").Single();
        Assert.AreEqual(armor.SourceId.ToString("D"), savedArmor.Element("sourceid")!.Value);
        if (saveArmor) Assert.AreEqual(archived.CharacterCreationLoadoutDraft!.Instances.Single(row => row.OptionId == armor.OptionId)
            .InstanceIds.Single().ToString("D"), savedArmor.Element("guid")!.Value);
        var completedFiles = Snapshot(context);
        var replay = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries).Apply(owner, apply);
        AssertSuccess(replay);
        Assert.AreEqual(applied.Value!.ReceiptDigest, replay.Value!.ReceiptDigest);
        manual = new OwnerBoundCharacterCreationLoadoutService(store, owners, context.Resolver);
        Assert.AreEqual(weaponReceipt.ReceiptDigest, manual.LookupReceipt(owner, context.WorkspaceId, command.IdempotencyKey).Value!.ReceiptDigest);
        Assert.AreEqual(weaponReceipt.ReceiptDigest, manual.Confirm(owner, command).Value!.ReceiptDigest);
        Assert.AreEqual(manualWrites + 1, writes.Replacements);
        AssertUnchanged(context, completedFiles);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Gear_and_manual_loadout_cannot_spend_the_same_remaining_funds_in_either_order(bool loadoutFirst)
    {
        // A saved Lifestyle is bound to the Gear draft it was purchased against.
        // Replacing that Gear would make the Lifestyle history stale and test
        // purchase-authority rejection instead of the shared wallet.
        using var context = ReadyContext.Create(includeGearReview: true, includeNonEmptyPurchases: true,
            talentValue: "Mystic Adept", mysticPowerPoints: 1, qualityName: "Guts", includeSkillPurchase: true);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var owner = owners.Capture();
        var manual = new OwnerBoundCharacterCreationLoadoutService(context.Store, owners, context.Resolver);
        var gear = new CharacterCreationGearService(context.Store, context.Resolver);
        var state = manual.Prepare(owner, context.WorkspaceId);
        AssertSuccess(state);
        var initial = state.Value!;
        decimal available = initial.Resources.PendingDraft!.Budget.TotalStartingNuyen - initial.Resources.KnownPurchaseCost;
        var weapon = initial.Inputs.Authority.Options.First(row => row.Kind == CharacterCreationLoadoutKinds.Weapon
            && row.IsSelectable && row.UnitCost * 20 > 100m && row.UnitCost * 20 < available);
        var loadoutChoices = new CharacterCreationLoadoutSelections([new(weapon.OptionId, 20)], []);
        var gearState = gear.Load(new(context.WorkspaceId)).Value!;
        Assert.IsTrue(gearState.CanEdit, string.Join(",", gearState.Blockers));
        var savedGear = gearState.PendingDraft!.Lines.Select(row => new CharacterCreationGearSelection(row.OptionId, row.Quantity)).ToArray();
        var stock = gearState.Authority.Options.Single(row => row.IsSelectable
            && row.SourceId == Guid.Parse("76aca45d-0d4f-45a3-a532-c47ac0b26b89"));
        Assert.AreEqual(1, stock.PackageQuantity);
        Assert.AreEqual(100m, stock.PackageCost);
        Assert.IsTrue(savedGear.All(saved => saved.OptionId != stock.OptionId));
        int quantity = checked((int)decimal.Floor(available / stock.PackageCost));
        Assert.IsTrue(quantity > 0 && quantity <= gearState.Authority.MaximumQuantityPerLine);
        CharacterCreationGearSelection[] gearChoices = [.. savedGear, new(stock.OptionId, quantity)];
        // Each basket fits alone. Together they exceed the one real wallet.
        Assert.IsTrue(quantity * stock.PackageCost <= available);
        Assert.IsTrue(quantity * stock.PackageCost + weapon.UnitCost * 20 > available);
        var gearPreview = gear.Preview(new(gearState.Binding, gearChoices)).Value!;
        Assert.IsTrue(gearPreview.CanConfirm, string.Join(",", gearPreview.Blockers));
        decimal gearCost = quantity * stock.PackageCost;
        Assert.AreEqual(gearState.PendingDraft.Budget.BasketCost + gearCost, gearPreview.BudgetAfter.BasketCost);
        var gearCommand = new CharacterCreationGearConfirmRequest(gearState.Binding, gearChoices,
            gearPreview.PreviewDigest, "shared-wallet-gear", true);
        var loadoutCommand = Preview(manual, owner, initial, loadoutChoices, "shared-wallet-loadout");
        if (loadoutFirst)
            AssertSuccess(manual.Confirm(owner, loadoutCommand));
        else
            Assert.AreEqual(CharacterCreationGearOutcomes.Applied, gear.Confirm(gearCommand).Outcome);
        var beforeRejectedPurchase = Snapshot(context);
        decimal expectedCost = initial.Resources.KnownPurchaseCost + (loadoutFirst ? weapon.UnitCost * 20 : gearCost);
        var currentWallet = manual.Prepare(owner, context.WorkspaceId);
        AssertSuccess(currentWallet);
        Assert.AreEqual(expectedCost, currentWallet.Value!.Resources.KnownPurchaseCost);
        Assert.AreEqual(expectedCost, currentWallet.Value.Budget.KnownPurchaseCost);
        Assert.AreEqual(initial.Budget.TotalStartingNuyen - expectedCost, currentWallet.Value.Budget.RemainingNuyen);
        Assert.IsTrue(currentWallet.Value.Resources.PurchaseAuthorityExact);
        if (loadoutFirst)
        {
            var current = gear.Load(new(context.WorkspaceId)).Value!;
            var blocked = gear.Preview(new(current.Binding, gearChoices));
            Assert.IsFalse(blocked.Value?.CanConfirm ?? false);
            CollectionAssert.Contains(blocked.Blockers.ToList(), CharacterCreationGearBlockers.InsufficientFunds);
            Assert.AreNotEqual(CharacterCreationGearOutcomes.Applied, gear.Confirm(gearCommand).Outcome);
        }
        else
        {
            var blocked = manual.Preview(owner, new(currentWallet.Value.Inputs.Binding,
                currentWallet.Value.Inputs.SnapshotDigest, loadoutChoices));
            Assert.IsNull(blocked.Value);
            CollectionAssert.Contains(blocked.Blockers.ToList(), CharacterCreationResourcesBlockers.InsufficientNuyen);
            Assert.IsNull(manual.Confirm(owner, loadoutCommand).Value);
        }
        AssertUnchanged(context, beforeRejectedPurchase);
        var cold = new OwnerBoundCharacterCreationLoadoutService(new FileWorkspaceStore(context.Directory), owners, context.Resolver)
            .Prepare(owner, context.WorkspaceId);
        AssertSuccess(cold);
        Assert.AreEqual(expectedCost, cold.Value!.Resources.KnownPurchaseCost);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            currentWallet.Value.Budget, cold.Value.Budget));
        Assert.IsTrue(cold.Value.Resources.PurchaseAuthorityExact);
        Assert.IsTrue(cold.Value.Resources.KnownPurchaseCost <= cold.Value.Resources.PendingDraft!.Budget.TotalStartingNuyen);
        AssertUnchanged(context, beforeRejectedPurchase);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Shared_budget_includes_saved_gear_lifestyle_and_manual_purchases_without_rewriting_funding()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var manual = new CharacterCreationLoadoutService(context.Store, context.Resolver);
        var filesBeforeRead = Snapshot(context);
        var initial = manual.Prepare(context.WorkspaceId);
        AssertSuccess(initial);
        var state = initial.Value!;
        var saved = context.Store.Get(context.WorkspaceId).Value!;
        var funding = saved.Document.AuxiliaryState.CharacterCreationResourcesDraft!;
        string fundingJson = System.Text.Json.JsonSerializer.Serialize(funding);
        string receiptsJson = System.Text.Json.JsonSerializer.Serialize(saved.Document.AuxiliaryState.CharacterCreationResourcesReceipts);
        Assert.IsTrue(CharacterCreationResourcesService.TryGetRetainedCompletionLifestyleCost(state.Resources, out decimal lifestyleCost));
        decimal gearCost = saved.Document.AuxiliaryState.CharacterCreationGearDraft!.Budget.BasketCost;
        Assert.IsTrue(lifestyleCost > 0m && gearCost > 0m);
        Assert.AreEqual(3, state.Budget.KarmaInvestment);
        Assert.AreEqual(gearCost + lifestyleCost, state.Budget.KnownPurchaseCost);
        Assert.AreEqual(funding.Budget.TotalStartingNuyen, state.Budget.TotalStartingNuyen);
        Assert.AreEqual(state.Budget.TotalStartingNuyen - gearCost - lifestyleCost, state.Budget.RemainingNuyen);
        Assert.IsTrue(state.Budget.IsExact);
        Assert.AreEqual(0, state.Budget.Blockers.Count);
        AssertUnchanged(context, filesBeforeRead);
        Assert.IsNull(CharacterCreationResourcesService.ReadSavedPurchaseBudget(state.Resources with
        {
            KnownPurchaseCost = state.Resources.KnownPurchaseCost - 1m
        }), "An unbound cost override is not a budget read.");

        var weapon = Option(state.Inputs, CharacterCreationLoadoutKinds.Weapon);
        var armor = Option(state.Inputs, CharacterCreationLoadoutKinds.Armor);
        var preview = manual.Preview(new(state.Inputs.Binding, state.Inputs.SnapshotDigest,
            new([new(weapon.OptionId)], [new(armor.OptionId)])));
        AssertSuccess(preview);
        var committed = manual.Confirm(new(preview.Value!.Request, preview.Value.PreviewDigest, "shared-budget-purchases", true));
        AssertSuccess(committed);
        var afterPurchase = Snapshot(context);
        var coldStore = new FileWorkspaceStore(context.Directory);
        var cold = new CharacterCreationLoadoutService(coldStore, context.Resolver).Prepare(context.WorkspaceId);
        AssertSuccess(cold);
        decimal totalCost = gearCost + lifestyleCost + weapon.UnitCost + armor.UnitCost;
        Assert.AreEqual(totalCost, cold.Value!.Budget.KnownPurchaseCost);
        Assert.AreEqual(totalCost, cold.Value.Resources.KnownPurchaseCost);
        Assert.AreEqual(preview.Value.RemainingNuyen, cold.Value.Budget.RemainingNuyen);
        Assert.AreEqual(Math.Max(0m, cold.Value.Budget.RemainingNuyen - cold.Value.Budget.CarryoverLimit), cold.Value.Budget.CarryoverExcess);
        var reopened = coldStore.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(fundingJson, System.Text.Json.JsonSerializer.Serialize(reopened.Document.AuxiliaryState.CharacterCreationResourcesDraft));
        Assert.AreEqual(receiptsJson, System.Text.Json.JsonSerializer.Serialize(reopened.Document.AuxiliaryState.CharacterCreationResourcesReceipts));
        AssertUnchanged(context, afterPurchase);
    }

    [TestMethod]
    [DataRow("reprice")]
    [DataRow("disabled")]
    [DataRow("cancel")]
    [DataRow("failure")]
    public void Changed_source_or_cancellation_before_rename_never_checkpoints_a_purchase(string failure)
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var owner = owners.Capture();
        var resolver = new ChangingResolver(context.Resolver);
        var probe = new CommitProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var manual = new OwnerBoundCharacterCreationLoadoutService(store, owners, resolver);
        var state = manual.Prepare(owner, context.WorkspaceId).Value!;
        var weapon = Option(state.Inputs, CharacterCreationLoadoutKinds.Weapon);
        resolver.OptionId = weapon.OptionId;
        var command = Preview(manual, owner, state, new([new(weapon.OptionId)], []), "reject-before-rename");
        var before = Snapshot(context);
        using var cancellation = new CancellationTokenSource();
        probe.AfterFlush = () =>
        {
            if (failure == "failure") throw new IOException("Injected pre-rename failure.");
            if (failure == "cancel") cancellation.Cancel();
            else resolver.Change = failure;
        };
        if (failure == "cancel")
        {
            bool canceled = false;
            try { manual.Confirm(owner, command, cancellation.Token); }
            catch (OperationCanceledException) { canceled = true; }
            Assert.IsTrue(canceled);
        }
        else Assert.IsNull(manual.Confirm(owner, command).Value);
        Assert.AreEqual(0, probe.Replacements);
        AssertUnchanged(context, before);
        Assert.IsNull(new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value!.Document.AuxiliaryState.CharacterCreationLoadoutDraft);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Stale_owner_binding_forged_receipts_and_changed_saved_sources_are_rejected_without_writes()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var owner = owners.Capture();
        var resolver = new ChangingResolver(context.Resolver);
        var store = new FileWorkspaceStore(context.Directory);
        var manual = new OwnerBoundCharacterCreationLoadoutService(store, owners, resolver);
        var state = manual.Prepare(owner, context.WorkspaceId).Value!;
        var weapon = Option(state.Inputs, CharacterCreationLoadoutKinds.Weapon);
        resolver.OptionId = weapon.OptionId;
        var command = Preview(manual, owner, state, new([new(weapon.OptionId)], []), "genuine-manual-purchase");
        AssertSuccess(manual.Confirm(owner, command));
        var saved = store.Get(context.WorkspaceId).Value!;
        var before = Snapshot(context);
        Assert.IsNull(manual.Confirm(owner, command with { IdempotencyKey = "stale-revision" }).Value);
        Assert.IsNull(manual.Confirm(owner, command with { ExplicitlyConfirmed = false }).Value);
        Assert.IsNull(manual.Prepare(owner with { AuthorityInstanceId = "foreign" }, context.WorkspaceId).Value);
        owners.Set(new OwnerScope("different-loadout-owner"));
        Assert.IsNull(manual.Confirm(owner, command).Value);
        owners.Set(OwnerScope.LocalSingleUser);
        var currentOwner = owners.Capture();
        Assert.IsNull(manual.Confirm(owner, command).Value, "An ABA owner stamp must remain stale.");

        // A real receipt is not a portable write grant, even after every hash is resealed.
        var receipt = saved.Document.AuxiliaryState.CharacterCreationLoadoutReceipts!.Single();
        var forged = receipt with { IdempotencyKeyDigest = CharacterCreationLoadoutDraftRules.KeyDigest("fabricated") };
        forged = forged with { ReceiptDigest = CharacterCreationLoadoutDraftRules.ReceiptDigest(forged) };
        var replacement = saved.Document with { State = saved.Document.State with
        { AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCreationLoadoutReceipts = [forged] } } };
        Assert.IsTrue(WorkspaceAuxiliaryStateIntegrity.IsValidShape(saved.Id, saved.ContentRevision, replacement.AuxiliaryState),
            "The rejection must be purchase authority, not merely a broken hash.");
        Assert.IsFalse(store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(saved.Id, saved.ContentRevision,
            saved.Document.AuxiliaryStateDigest, replacement).Success);
        var reader = new OwnerBoundCharacterCreationCompletionReader(store, owners, resolver, context.Queries);
        foreach (string change in new[] { "reprice", "disabled" })
        {
            resolver.Change = change;
            Assert.IsNull(manual.Prepare(currentOwner, context.WorkspaceId).Value);
            Assert.IsNull(reader.LoadCompletionInputsWithLoadout(currentOwner, new(context.WorkspaceId)).Value);
        }
        AssertUnchanged(context, before);
    }

    private static CharacterCreationLoadoutCatalogOption Option(CharacterCreationLoadoutCompletionInputs inputs, string kind)
        => inputs.Authority.Options.Where(row => row.Kind == kind && row.IsSelectable && row.UnitCost > 0m).OrderBy(row => row.UnitCost).First();

    private static CharacterCreationLoadoutConfirmRequest Preview(IOwnerBoundCharacterCreationLoadoutService service,
        Chummer.Application.Owners.OwnerContextStamp owner, CharacterCreationLoadoutState state,
        CharacterCreationLoadoutSelections choices, string key)
    {
        var preview = service.Preview(owner, new(state.Inputs.Binding, state.Inputs.SnapshotDigest, choices));
        AssertSuccess(preview);
        return new(preview.Value!.Request, preview.Value.PreviewDigest, key, true);
    }

    private static void AssertSuccess<T>(CharacterCreationFoundationResult<T> result) where T : class
    {
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.IsNotNull(result.Value);
    }

    private sealed class CommitProbe : IFileWorkspaceStoreFaultInjector
    {
        public int Replacements { get; private set; }
        public Action? AfterFlush { get; set; }
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTargetReplaced) Replacements++;
            if (stage != FileWorkspaceStoreFaultStage.AfterTempFileFlushed) return;
            var action = AfterFlush; AfterFlush = null; action?.Invoke();
        }
    }

    private sealed class ChangingResolver(ICharacterSourceDataResolver inner) : ICharacterSourceDataResolver
    {
        public string? Change { get; set; }
        public string? OptionId { get; set; }
        public ICharacterSourceDataContext? TryCreateContext(string xml)
        {
            var source = inner.TryCreateContext(xml);
            if (source is null) return null;
            var proxy = DispatchProxy.Create<ICharacterSourceDataContext, SourceProxy>();
            ((SourceProxy)(object)proxy).Inner = source;
            ((SourceProxy)(object)proxy).Transform = authority =>
            {
                if (Change is null) return authority;
                var options = authority.Options.Select(option =>
                {
                    if (option.OptionId != OptionId) return option;
                    if (Change == "disabled")
                        option = option with { IsSelectable = false, Blockers = [CharacterCreationLoadoutBlockers.SourceDisabled] };
                    else
                    {
                        var sourceNode = XElement.Parse(option.SourceNodeXml);
                        sourceNode.SetElementValue("cost", option.UnitCost + 1);
                        string changedXml = sourceNode.ToString(SaveOptions.DisableFormatting);
                        option = option with { UnitCost = option.UnitCost + 1, SourceNodeXml = changedXml,
                            SourceNodeDigest = CharacterCreationLoadoutRules.ComputeSourceNodeDigest(changedXml) };
                    }
                    return option with { OptionDigest = CharacterCreationLoadoutRules.ComputeOptionDigest(option) };
                }).ToArray();
                var changed = authority with { Options = options, SourceDigest = CharacterCreationLoadoutRules.Compute(options) };
                return changed with { AuthorityDigest = CharacterCreationLoadoutRules.ComputeAuthorityDigest(changed) };
            };
            return proxy;
        }
    }

    public class SourceProxy : DispatchProxy
    {
        public ICharacterSourceDataContext Inner { get; set; } = null!;
        public Func<CharacterCreationLoadoutAuthority, CharacterCreationLoadoutAuthority> Transform { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            object? result = targetMethod!.Invoke(Inner, args);
            if (targetMethod.Name == nameof(ICharacterSourceDataContext.TryResolveCreationLoadoutAuthority)
                && result is true && args is { Length: 1 } && args[0] is CharacterCreationLoadoutAuthority authority)
                args[0] = Transform(authority);
            return result;
        }
    }

    private static Dictionary<string, byte[]> Snapshot(ReadyContext context)
        => Directory.EnumerateFiles(context.Directory, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(context.Directory, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertUnchanged(ReadyContext context, Dictionary<string, byte[]> before)
    {
        var after = Snapshot(context);
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (var entry in before) CollectionAssert.AreEqual(entry.Value, after[entry.Key], entry.Key);
    }
}
