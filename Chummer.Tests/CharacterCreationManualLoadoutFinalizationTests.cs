using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationManualLoadoutFinalizationTests
{
    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority, false, false)]
    [DataRow(CharacterCreationBuildMethods.Priority, true, true)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, true, false)]
    public void Saved_manual_purchases_finalize_normally_once_and_replay_from_cold_archive(
        string method, bool buyArmor, bool buyLifestyle)
    {
        using var context = ReadyContext.Create(true, includeNonEmptyPurchases: true, buildMethod: method);
        var manual = new CharacterCreationLoadoutService(context.Store, context.Resolver);
        var state = manual.Prepare(context.WorkspaceId).Value!;
        Assert.IsNotNull(state);
        var weapon = Cheapest(state.Inputs, CharacterCreationLoadoutKinds.Weapon);
        var first = Purchase(manual, state, new([new(weapon.OptionId, 2)], []), "normal-manual-weapons");
        CharacterCreationLoadoutConfirmRequest? armorCommand = null;
        if (buyArmor)
        {
            state = manual.Prepare(context.WorkspaceId).Value!;
            var armor = Cheapest(state.Inputs, CharacterCreationLoadoutKinds.Armor);
            armorCommand = Purchase(manual, state,
                new(state.Inputs.OriginalSelections.Weapons, [new(armor.OptionId)]), "normal-manual-armor").Command;
        }
        decimal lifestyleCost = buyLifestyle ? BuyLifestyle(context) : 0m;
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var draft = before.Document.AuxiliaryState.CharacterCreationLoadoutDraft!;
        CollectionAssert.AreEqual(first.Receipt.Draft.Instances.Single().InstanceIds.ToArray(),
            draft.Instances.Single(row => row.OptionId == weapon.OptionId).InstanceIds.ToArray());

        var writes = new CommitProbe();
        var store = new FileWorkspaceStore(context.Directory, writes);
        var finalizer = ReadyContext.BuildFinalizer(store, context.Queries, context.Resolver);
        var (review, command) = Review(finalizer, context.WorkspaceId);
        var plan = review.Plan!;
        decimal creationFunding = before.Document.AuxiliaryState.CharacterCreationResourcesDraft!.Budget.TotalStartingNuyen;
        decimal gearCost = before.Document.AuxiliaryState.CharacterCreationGearDraft!.Budget.BasketCost;
        Assert.IsTrue(gearCost > 0);
        decimal remaining = creationFunding - gearCost - lifestyleCost - draft.PurchaseCost;
        Assert.IsTrue(remaining >= 0);
        Assert.AreEqual(remaining.ToString(CultureInfo.InvariantCulture),
            review.OrderedDeltas.Single(row => row.DeltaId == "resources:remaining-nuyen").AfterValue);
        Assert.AreEqual(Math.Min(remaining, plan.CarryoverPolicy!.MaximumNuyen) + plan.StartingNuyen,
            plan.NuyenRemaining);
        Assert.AreEqual(draft.PurchaseCost, review.OrderedDeltas
            .Where(row => row.Kind is CharacterCreationLoadoutKinds.Weapon or CharacterCreationLoadoutKinds.Armor)
            .Sum(row => row.NuyenCost));
        foreach (var line in draft.Lines)
            foreach (string anchor in line.SourceAnchorIds)
                CollectionAssert.Contains(review.SourceAnchorIds.ToArray(), anchor);
        Assert.AreEqual(0, writes.Replacements, "Load and Review must remain read-only.");
        Assert.IsTrue(CharacterCreationFinalizationTransaction.TryBuild(
            store, Chummer.Contracts.Owners.OwnerScope.LocalSingleUser, before, context.Resolver, context.Queries,
            command, out var canonical, out _));
        Assert.IsFalse(store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(before.Id,
            before.ContentRevision, before.Document.AuxiliaryStateDigest, canonical!).Success,
            "Even a valid manual-purchase finalization requires the source-admitted typed commit.");
        Assert.AreEqual(0, writes.Replacements);
        var confirmed = finalizer.Confirm(command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, confirmed.Outcome, string.Join(",", confirmed.Blockers));
        Assert.AreEqual(1, writes.Replacements);

        var cold = new FileWorkspaceStore(context.Directory, writes);
        var career = cold.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(before.ContentRevision + 1, career.ContentRevision);
        Assert.AreEqual(career.ContentRevision, career.SavedRevision);
        Assert.HasCount(1, career.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!);
        Assert.IsNull(career.Document.AuxiliaryState.CharacterCreationLoadoutDraft);
        Assert.IsNull(career.Document.AuxiliaryState.CharacterCreationLoadoutReceipts);
        Assert.AreEqual(JsonSerializer.Serialize(before.Document.AuxiliaryState),
            JsonSerializer.Serialize(career.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.State));
        Assert.IsTrue(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidTransition(context.WorkspaceId,
            before.ContentRevision, before.SavedRevision, career.ContentRevision, before.Document, career.Document));
        var root = XElement.Parse(career.Document.Content);
        Assert.AreEqual("True", root.Element("created")!.Value);
        Assert.AreEqual(creationFunding, decimal.Parse(root.Element("startingnuyen")!.Value, CultureInfo.InvariantCulture));
        Assert.AreEqual(plan.NuyenRemaining, decimal.Parse(root.Element("nuyen")!.Value, CultureInfo.InvariantCulture));
        AssertSavedGraph(root, draft);

        // Neither finalization nor purchase retries may ask current source data
        // to reconstruct or reprice an already accepted historical decision.
        var unavailable = new UnavailableResolver();
        var replayFinalizer = ReadyContext.BuildFinalizer(cold, context.Queries, unavailable);
        var replay = replayFinalizer.Confirm(command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, replay.Outcome);
        Assert.AreEqual(confirmed.Value!.ReceiptDigest, replay.Value!.ReceiptDigest);
        Assert.AreEqual(confirmed.Value.ReceiptDigest,
            replayFinalizer.LookupReceipt(new(context.WorkspaceId, command.IdempotencyKey)).Value!.ReceiptDigest);
        var replayManual = new CharacterCreationLoadoutService(cold, unavailable);
        Assert.AreEqual(first.Receipt.ReceiptDigest, replayManual.Confirm(first.Command).Value!.ReceiptDigest);
        if (armorCommand is not null) Assert.IsNotNull(replayManual.Confirm(armorCommand).Value);
        Assert.AreEqual(1, writes.Replacements);
        Assert.AreEqual(career.ContentRevision, cold.Get(context.WorkspaceId).Value!.ContentRevision);
        if (buyLifestyle) AssertRestoredArchive(context, cold, career, command, first.Command);
    }

    [TestMethod]
    [DataRow("price")]
    [DataRow("damage")]
    [DataRow("disabled")]
    public void Changed_manual_source_after_final_review_cannot_commit_or_reprice_saved_rows(string change)
    {
        using var context = ReadyContext.Create(true);
        var source = new ChangingResolver(context.Resolver);
        var manual = new CharacterCreationLoadoutService(context.Store, source);
        var state = manual.Prepare(context.WorkspaceId).Value!;
        var weapon = Cheapest(state.Inputs, CharacterCreationLoadoutKinds.Weapon);
        _ = Purchase(manual, state, new([new(weapon.OptionId)], []), "drift-manual-purchase");
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var writes = new CommitProbe();
        var store = new FileWorkspaceStore(context.Directory, writes);
        var finalizer = ReadyContext.BuildFinalizer(store, context.Queries, source);
        var (_, command) = Review(finalizer, context.WorkspaceId);
        source.OptionId = weapon.OptionId;
        source.Change = change;
        Assert.IsNull(finalizer.Confirm(command).Value);
        var loaded = finalizer.Load(new(context.WorkspaceId));
        Assert.IsFalse(loaded.Value?.CanReview == true);
        Assert.AreEqual(0, writes.Replacements);
        AssertUnchanged(before, store.Get(context.WorkspaceId).Value!);
        source.Change = null;
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, finalizer.Confirm(command).Outcome,
            "Returning the exact source must make the original saved graph reviewable again.");
        Assert.AreEqual(1, writes.Replacements);
    }

    [TestMethod]
    [DataRow("price")]
    [DataRow("damage")]
    [DataRow("disabled")]
    public void Source_change_after_flush_never_finalizes_manual_purchases_and_exact_retry_commits_once(string change)
    {
        using var context = ReadyContext.Create(true);
        var source = new ChangingResolver(context.Resolver);
        var manual = new CharacterCreationLoadoutService(context.Store, source);
        var state = manual.Prepare(context.WorkspaceId).Value!;
        var weapon = Cheapest(state.Inputs, CharacterCreationLoadoutKinds.Weapon);
        _ = Purchase(manual, state, new([new(weapon.OptionId)], []), "post-flush-manual-purchase");
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var writes = new CommitProbe();
        var store = new FileWorkspaceStore(context.Directory, writes);
        var (_, command) = Review(ReadyContext.BuildFinalizer(store, context.Queries, source), context.WorkspaceId);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var owner = owners.Capture();
        var finalizer = new OwnerBoundCharacterCreationFinalizationService(store, owners, context.Queries, source);
        source.OptionId = weapon.OptionId;
        writes.AfterFlush = () => source.Change = change;

        Assert.IsNull(finalizer.Confirm(owner, command).Value);
        Assert.AreEqual(1, writes.Flushes, "The source must change inside the actual flushed-file commit window.");
        Assert.AreEqual(0, writes.Replacements);
        var cold = new FileWorkspaceStore(context.Directory, writes);
        var stillPending = cold.Get(context.WorkspaceId).Value!;
        AssertUnchanged(before, stillPending);
        Assert.IsNull(stillPending.Document.AuxiliaryState.CharacterCreationFinalizationReceipts);
        Assert.IsNull(stillPending.Document.AuxiliaryState.CharacterCreationFinalizationArchive);
        Assert.AreEqual(0, owners.ActiveLeases);

        source.Change = null;
        var confirmed = finalizer.Confirm(owner, command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, confirmed.Outcome, string.Join(",", confirmed.Blockers));
        Assert.AreEqual(1, writes.Replacements);
        var replay = new OwnerBoundCharacterCreationFinalizationService(cold, owners, context.Queries, new UnavailableResolver())
            .Confirm(owner, command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, replay.Outcome);
        Assert.AreEqual(confirmed.Value!.ReceiptDigest, replay.Value!.ReceiptDigest);
        Assert.AreEqual(1, writes.Replacements);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Individually_receipted_purchases_cannot_finalize_an_overcommitted_combined_budget()
    {
        using var context = ReadyContext.Create(true);
        var gear = new CharacterCreationGearService(context.Store, context.Resolver);
        var gearState = gear.Load(new(context.WorkspaceId)).Value!;
        decimal total = gearState.Budget.TotalStartingNuyen;
        var option = gearState.Authority.Options.Where(row => row.IsSelectable && row.PackageCost > 0
                && row.PackageCost <= total).OrderByDescending(row => row.PackageCost).First();
        ConfirmGear(gear, context.WorkspaceId, [new(option.OptionId, 1)], "earlier-large-gear");
        var earlier = context.Store.Get(context.WorkspaceId).Value!;
        ConfirmGear(gear, context.WorkspaceId, [], "remove-earlier-gear");
        var manual = new CharacterCreationLoadoutService(context.Store, context.Resolver);
        var state = manual.Prepare(context.WorkspaceId).Value!;
        var weapon = state.Inputs.Authority.Options.First(row => row.Kind == CharacterCreationLoadoutKinds.Weapon
            && row.IsSelectable && row.UnitCost > 0 && row.UnitCost * 20 <= total
            && row.UnitCost * 20 + option.PackageCost > total);
        _ = Purchase(manual, state, new([new(weapon.OptionId, 20)], []), "later-manual-purchase");
        var saved = context.Store.Get(context.WorkspaceId).Value!;
        // Both histories are real purchases from this workspace. Reintroduce
        // the older Gear history only in a hostile read observation; it must
        // not be accepted merely because each individual ledger validates.
        var overcommitted = saved with { Document = saved.Document with { State = saved.Document.State with
        {
            AuxiliaryState = saved.Document.AuxiliaryState with
            {
                CharacterCreationGearDraft = earlier.Document.AuxiliaryState.CharacterCreationGearDraft,
                CharacterCreationGearReceipts = earlier.Document.AuxiliaryState.CharacterCreationGearReceipts
            }
        } } };
        var source = context.Resolver.TryCreateContext(saved.Document.Content)!;
        Assert.IsTrue(source.TryResolveCreationCarryoverPolicy(out var policy));
        Assert.IsFalse(CharacterCreationFinalizationProjector.TryProject(overcommitted,
            out _, out _, out _, out _, out _, out _, out var blockers, policy));
        CollectionAssert.Contains(blockers, CharacterCreationResourcesBlockers.InsufficientNuyen);
        var observedStore = DispatchProxy.Create<IWorkspaceStore, ReadProxy>();
        ((ReadProxy)(object)observedStore).Inner = context.Store;
        ((ReadProxy)(object)observedStore).Observation = overcommitted;
        var finalizer = ReadyContext.BuildFinalizer(observedStore, context.Queries, context.Resolver);
        var loaded = finalizer.Load(new(context.WorkspaceId));
        Assert.IsFalse(loaded.Value?.CanReview == true);
        CollectionAssert.Contains(loaded.Blockers.ToArray(), CharacterCreationResourcesBlockers.InsufficientNuyen);
        AssertUnchanged(saved, context.Store.Get(context.WorkspaceId).Value!);
    }

    [TestMethod]
    [DataRow("receipt")]
    [DataRow("funding")]
    [DataRow("identity")]
    [DataRow("xml")]
    public void Ordinary_projection_rejects_unreceipted_unfunded_or_imported_manual_graphs(string change)
    {
        using var context = ReadyContext.Create(true);
        var manual = new CharacterCreationLoadoutService(context.Store, context.Resolver);
        var state = manual.Prepare(context.WorkspaceId).Value!;
        var weapon = Cheapest(state.Inputs, CharacterCreationLoadoutKinds.Weapon);
        _ = Purchase(manual, state, new([new(weapon.OptionId)], []), "normal-rejected-manual");
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var auxiliary = before.Document.AuxiliaryState;
        if (change == "receipt") auxiliary = auxiliary with { CharacterCreationLoadoutReceipts = [] };
        if (change == "funding") auxiliary = auxiliary with { CharacterCreationResourcesReceipts = [] };
        if (change == "identity")
        {
            var draft = auxiliary.CharacterCreationLoadoutDraft! with
            { Instances = [new(weapon.OptionId, [Guid.NewGuid()])] };
            draft = draft with { DraftDigest = CharacterCreationLoadoutDraftRules.DraftDigest(draft) };
            var receipt = auxiliary.CharacterCreationLoadoutReceipts!.Single() with { Draft = draft };
            receipt = receipt with { ReceiptDigest = CharacterCreationLoadoutDraftRules.ReceiptDigest(receipt) };
            auxiliary = auxiliary with { CharacterCreationLoadoutDraft = draft, CharacterCreationLoadoutReceipts = [receipt] };
        }
        string xml = before.Document.Content;
        if (change == "xml")
        {
            var root = XElement.Parse(xml);
            root.Element("weapons")?.Remove();
            root.Add(new XElement("weapons", new XElement("weapon", new XElement("name", "Unpriced import"))));
            xml = root.ToString(SaveOptions.DisableFormatting);
            auxiliary = auxiliary with { CharacterCreationLoadoutDraft = null, CharacterCreationLoadoutReceipts = null };
        }
        var invalid = before with { Document = before.Document with { State = before.Document.State with
        { Payload = xml, AuxiliaryState = auxiliary } } };
        Assert.IsTrue(context.Resolver.TryCreateContext(before.Document.Content)!.TryResolveCreationCarryoverPolicy(out var policy));
        Assert.IsFalse(CharacterCreationFinalizationProjector.TryProject(invalid,
            out _, out _, out _, out _, out _, out _, out _, policy));
        AssertUnchanged(before, context.Store.Get(context.WorkspaceId).Value!);
    }

    private static CharacterCreationLoadoutCatalogOption Cheapest(CharacterCreationLoadoutCompletionInputs inputs, string kind)
        => inputs.Authority.Options.Where(row => row.Kind == kind && row.IsSelectable && row.UnitCost > 0m)
            .OrderBy(row => row.UnitCost).First();

    private static (CharacterCreationLoadoutConfirmRequest Command, CharacterCreationLoadoutReceipt Receipt) Purchase(
        CharacterCreationLoadoutService service, CharacterCreationLoadoutState state,
        CharacterCreationLoadoutSelections selections, string key)
    {
        var preview = service.Preview(new(state.Inputs.Binding, state.Inputs.SnapshotDigest, selections));
        Assert.IsNotNull(preview.Value, string.Join(",", preview.Blockers));
        var command = new CharacterCreationLoadoutConfirmRequest(preview.Value.Request, preview.Value.PreviewDigest, key, true);
        var result = service.Confirm(command);
        Assert.IsNotNull(result.Value, string.Join(",", result.Blockers));
        return (command, result.Value);
    }

    private static (CharacterCreationFinalizationReview Review, CharacterCreationFinalizationConfirmRequest Command) Review(
        ICharacterCreationFinalizationService service, CharacterWorkspaceId id)
    {
        var loaded = service.Load(new(id));
        Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
        Assert.IsTrue(loaded.Value.CanReview, string.Join(",", loaded.Blockers));
        var source = loaded.Value.StartingCashSource!;
        var choice = new CharacterCreationStartingCashChoice(source.AuthorityDigest, source.Dice * 3);
        var result = service.Review(new(loaded.Value.Binding) { StartingCash = choice });
        Assert.IsNotNull(result.Value, string.Join(",", result.Blockers));
        Assert.IsTrue(result.Value.CanConfirm, string.Join(",", result.Blockers));
        return (result.Value, new(loaded.Value.Binding, result.Value.PreviewDigest, result.Value.Plan!.PlanDigest,
            "normal-manual-finalization", true) { StartingCash = choice });
    }

    private static decimal BuyLifestyle(ReadyContext context)
    {
        var service = new CharacterCreationLifestylesService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        var low = state.Authority.LifestyleOptions.Single(row => row.Name == "Low");
        int months = (int)(state.Budget.Total / low.BaseCost);
        Assert.IsTrue(months > 0);
        var id = Guid.NewGuid();
        var configuration = new CharacterCreationLifestyleConfiguration(id, low.OptionId, "Manual runner home",
            CharacterCreationLifestyleStyleIds.Standard, low.DefaultIncrementId, months, 100m, 0,
            false, false, 0, 0, 0, 0, "Seattle", "", "", []);
        var mutation = new CharacterCreationLifestyleMutation(CharacterCreationLifestyleMutationKinds.Create, id, configuration);
        var preview = service.Preview(new(state.Binding, mutation)).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        var result = service.Confirm(new(state.Binding, mutation, preview.PreviewDigest, "normal-manual-home", true));
        Assert.IsTrue(result.Success, string.Join(",", result.Blockers));
        return low.BaseCost * months;
    }

    private static void ConfirmGear(CharacterCreationGearService service, CharacterWorkspaceId id,
        CharacterCreationGearSelection[] selections, string key)
    {
        var state = service.Load(new(id)).Value!;
        var preview = service.Preview(new(state.Binding, selections)).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        var result = service.Confirm(new(state.Binding, selections, preview.PreviewDigest, key, true));
        Assert.AreEqual(CharacterCreationGearOutcomes.Applied, result.Outcome, string.Join(",", result.Blockers));
    }

    private static void AssertSavedGraph(XElement root, CharacterCreationLoadoutDraft draft)
    {
        var nodes = root.Element("weapons")!.Elements("weapon").Concat(root.Element("armors")!.Elements("armor")).ToArray();
        Assert.AreEqual(draft.Instances.Sum(row => row.InstanceIds.Count), nodes.Length);
        foreach (var line in draft.Lines)
        {
            var source = XElement.Parse(line.SourceNodeXml);
            foreach (Guid id in draft.Instances.Single(row => row.OptionId == line.OptionId).InstanceIds)
            {
                var node = nodes.Single(row => row.Element("guid")!.Value == id.ToString("D"));
                Assert.AreEqual(line.Kind, node.Name.LocalName);
                Assert.AreEqual(line.SourceId.ToString("D"), node.Element("sourceid")!.Value);
                foreach (string field in new[] { "name", "category", "source", "page", "cost", "avail" })
                    Assert.AreEqual(source.Element(field)?.Value ?? "", node.Element(field)!.Value, field);
                string stat = line.Kind == CharacterCreationLoadoutKinds.Weapon ? "damage" : "armor";
                Assert.AreEqual(source.Element(stat)!.Value, node.Element(stat)!.Value);
                Assert.AreEqual(line.UnitCost, decimal.Parse(node.Element("cost")!.Value, CultureInfo.InvariantCulture));
            }
        }
    }

    private static void AssertRestoredArchive(ReadyContext context, FileWorkspaceStore source,
        WorkspaceStoredDocument career, CharacterCreationFinalizationConfirmRequest finalization,
        CharacterCreationLoadoutConfirmRequest purchase)
    {
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var exported = new WorkspaceContinuationExportService(source, owners).Export(owners.Capture(), career.Id);
        Assert.IsTrue(exported.Success, exported.Error);
        string directory = Directory.CreateTempSubdirectory("chummer-manual-finalization-restore-").FullName;
        try
        {
            var store = new FileWorkspaceStore(directory);
            var restore = new WorkspaceContinuationRestoreService(store, owners, context.Resolver, context.Queries,
                LifeModulesCatalog(), 16 * 1024 * 1024);
            using var review = restore.Review(owners.Capture(), WorkspaceContinuationCodec.Encode(exported.Value!, 16 * 1024 * 1024));
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Available, review.Result.Outcome);
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, restore.Confirm(review, true).Outcome);
            var cold = new FileWorkspaceStore(directory);
            var restored = cold.Get(career.Id).Value!;
            Assert.AreEqual(career.Document.Content, restored.Document.Content);
            Assert.AreEqual(career.Document.AuxiliaryStateDigest, restored.Document.AuxiliaryStateDigest);
            Assert.IsNotNull(restored.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.State.CharacterCreationLoadoutDraft);
            Assert.IsTrue(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidArchive(career.Id,
                restored.ContentRevision, restored.Document.AuxiliaryState.CharacterCreationFinalizationArchive,
                restored.Document.AuxiliaryState.CharacterCreationFinalizationReceipts));
            // Imported receipts retain provenance but are not local idempotency authority.
            var finalizer = ReadyContext.BuildFinalizer(cold, context.Queries, new UnavailableResolver());
            Assert.AreEqual(CharacterCreationFinalizationOutcomes.Conflict, finalizer.Confirm(finalization).Outcome);
            Assert.IsNull(new CharacterCreationLoadoutService(cold, new UnavailableResolver()).Confirm(purchase).Value);
            AssertUnchanged(restored, cold.Get(career.Id).Value!);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static XmlLifeModulesCatalogService LifeModulesCatalog()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "Chummer", "data", "lifemodules.xml");
            if (File.Exists(path)) return new(path);
        }
        throw new DirectoryNotFoundException("Canonical Life Modules data is missing.");
    }

    private static void AssertUnchanged(WorkspaceStoredDocument before, WorkspaceStoredDocument after)
    {
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
    }

    private sealed class CommitProbe : IFileWorkspaceStoreFaultInjector
    {
        public int Replacements { get; private set; }
        public int Flushes { get; private set; }
        public Action? AfterFlush { get; set; }
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string temporaryPath)
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTargetReplaced) Replacements++;
            if (stage != FileWorkspaceStoreFaultStage.AfterTempFileFlushed) return;
            Flushes++;
            var action = AfterFlush; AfterFlush = null; action?.Invoke();
        }
    }

    private sealed class UnavailableResolver : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
            => throw new AssertFailedException("Receipt replay must not read live source authority.");
    }

    private sealed class ChangingResolver(ICharacterSourceDataResolver inner) : ICharacterSourceDataResolver
    {
        public string? Change { get; set; }
        public string? OptionId { get; set; }
        public ICharacterSourceDataContext? TryCreateContext(string xml)
        {
            var context = inner.TryCreateContext(xml);
            if (context is null) return null;
            var proxy = DispatchProxy.Create<ICharacterSourceDataContext, SourceProxy>();
            ((SourceProxy)(object)proxy).Inner = context;
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
                        var node = XElement.Parse(option.SourceNodeXml);
                        node.SetElementValue(Change == "price" ? "cost" : "damage", Change == "price"
                            ? (option.UnitCost + 1).ToString(CultureInfo.InvariantCulture) : "99P");
                        string xml = node.ToString(SaveOptions.DisableFormatting);
                        option = option with { UnitCost = Change == "price" ? option.UnitCost + 1 : option.UnitCost,
                            SourceNodeXml = xml, SourceNodeDigest = CharacterCreationLoadoutRules.ComputeSourceNodeDigest(xml) };
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

    public class ReadProxy : DispatchProxy
    {
        public IWorkspaceStore Inner { get; set; } = null!;
        public WorkspaceStoredDocument Observation { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            object? result = targetMethod!.Invoke(Inner, args);
            return targetMethod.Name == nameof(IWorkspaceStore.Get) && result is WorkspaceStoreReadResult read
                ? read with { Value = Observation } : result;
        }
    }
}
