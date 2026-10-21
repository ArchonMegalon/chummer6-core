using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

// Real-source coverage of ordinary composite editing. Runtime is serialized
// by the controller; these cases are not a claim of executed acceptance.
public sealed partial class CharacterCreationCompletionApplyTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Composite89_nonempty_stage_edit_preserves_paid_choices_and_cold_finish(bool magic)
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var seed = Composite89Seed(context, owners, retained: false);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var filesBeforeRead = Snapshot(context);
        var edit = Composite89Prepare(context, owners, seed.Choices, magic, context.Resolver);
        AssertUnchanged(context, filesBeforeRead, "Composite stage read and preview");
        var committed = edit.Confirm(new FileWorkspaceStore(context.Directory), owners.Capture());
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, committed.Outcome,
            string.Join(",", committed.Blockers));
        Assert.IsNotNull(committed.Receipt);
        var after = new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value!;
        Assert.AreEqual(before.ContentRevision + 1, after.ContentRevision);
        Assert.AreEqual(after.ContentRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.IsNull(after.Document.AuxiliaryState.CharacterCreationFinalizationArchive);
        Assert.IsNull(after.Document.AuxiliaryState.CharacterCreationFinalizationReceipts);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            before.Document.AuxiliaryState.CharacterCreationCompletionDrafts,
            after.Document.AuxiliaryState.CharacterCreationCompletionDrafts));
        var files = Snapshot(context);
        Assert.AreEqual(committed.Receipt,
            edit.Confirm(new FileWorkspaceStore(context.Directory), owners.Capture()).Receipt);
        Assert.IsNull(edit.ConflictingCommand(new FileWorkspaceStore(context.Directory), owners.Capture()).Receipt);
        AssertUnchanged(context, files, "Exact ordinary command replay");

        var reopened = Composite89Inputs(context, new FileWorkspaceStore(context.Directory), owners);
        Assert.IsNotNull(reopened.CompositeBaseline);
        var current = reopened.CompositeBaseline!.Selections;
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(edit.Choices, current));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            seed.Choices.SkillKarmaPurchases, current.SkillKarmaPurchases));
        // Domain input snapshots retain original history; Finish consumes the
        // current composite plus actual typed successors.
        var magicState = new OwnerBoundCharacterCreationMagicResonanceService(
            new FileWorkspaceStore(context.Directory), owners, context.Resolver, context.Queries)
            .Load(owners.Capture(), new(context.WorkspaceId)).Value!;
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            edit.Choices.MagicSelections, magicState.CompositeSelections));
        var qualityState = new OwnerBoundCharacterCreationQualitiesService(
            new FileWorkspaceStore(context.Directory), owners, context.Queries, context.Resolver)
            .Load(owners.Capture(), new(context.WorkspaceId)).Value!;
        CollectionAssert.AreEquivalent(edit.Choices.QualityOptionIds.ToArray(),
            qualityState.Preview.Selections.Select(row => row.OptionId).ToArray());
        var service = Service(context, new FileWorkspaceStore(context.Directory), owners);
        var review = service.ReviewDraft(owners.Capture(), context.WorkspaceId,
            new(reopened.SnapshotDigest, current));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome,
            string.Join(",", review.Blockers));
        Assert.AreEqual(seed.Review.Review.Balances.RemainingCreationKarma + edit.KarmaReleased,
            review.Value!.Review.Balances.RemainingCreationKarma);
        Assert.AreEqual(seed.Review.Review.Balances.RemainingNuyen, review.Value.Review.Balances.RemainingNuyen);
        var finish = service.Review(owners.Capture(), context.WorkspaceId, review.Value.Review.Request,
            CashChoice(context, reopened, current));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, finish.Outcome,
            string.Join(",", finish.Blockers));
        Assert.AreEqual(seed.Receipt, service.LookupDraft(owners.Capture(),
            CharacterCreationCompletionDraftTransaction.LookupRequest(seed.Command)).Value);
        AssertUnchanged(context, files, "Cold source-admitted Finish review");
    }

    [TestMethod]
    public void Composite89_magic_then_quality_edits_feed_a_fresh_finish()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var seed = Composite89Seed(context, owners, retained: false);
        var magic = Composite89Prepare(context, owners, seed.Choices, true, context.Resolver);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
            magic.Confirm(new FileWorkspaceStore(context.Directory), owners.Capture()).Outcome);
        var qualities = Composite89Prepare(context, owners, magic.Choices, false, context.Resolver);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
            qualities.Confirm(new FileWorkspaceStore(context.Directory), owners.Capture()).Outcome);
        var cold = new FileWorkspaceStore(context.Directory);
        var inputs = Composite89Inputs(context, cold, owners);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            qualities.Choices, inputs.CompositeBaseline!.Selections));
        var finish = Service(context, cold, owners).Review(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, inputs.CompositeBaseline.Selections),
            CashChoice(context, inputs, inputs.CompositeBaseline.Selections));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, finish.Outcome, string.Join(",", finish.Blockers));
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    [DataRow(false, true)]
    public void Composite89_stage_atomic_fault_preserves_history_and_exact_replay(bool magic, bool afterRename)
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var seed = Composite89Seed(context, owners, retained: false);
        var edit = Composite89Prepare(context, owners, seed.Choices, magic, context.Resolver);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = Snapshot(context);
        var probe = new AtomicWriteProbe
        {
            ThrowAt = afterRename ? FileWorkspaceStoreFaultStage.AfterTargetReplaced
                : FileWorkspaceStoreFaultStage.AfterTempFileFlushed,
            Armed = true
        };
        var attempted = edit.Confirm(new FileWorkspaceStore(context.Directory, probe), owners.Capture());
        Assert.AreEqual(1, probe.Throws, "The real ordinary transaction must reach the requested fault boundary.");
        Assert.AreEqual(afterRename ? 1 : 0, probe.Replacements);
        var cold = new FileWorkspaceStore(context.Directory);
        var reopened = cold.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(before.Document.Content, reopened.Document.Content);
        Assert.AreEqual(before.ContentRevision + (afterRename ? 1 : 0), reopened.ContentRevision);
        Assert.AreEqual(reopened.ContentRevision, reopened.SavedRevision);
        if (afterRename)
        {
            var knownFiles = Snapshot(context);
            // Ordinary Magic/Qualities expose exact Confirm replay, not LookupDraft.
            var reconciled = edit.Confirm(cold, owners.Capture());
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reconciled.Outcome,
                string.Join(",", reconciled.Blockers));
            object persisted = magic
                ? reopened.Document.AuxiliaryState.CharacterCreationMagicResonanceReceipts!.Last()
                : reopened.Document.AuxiliaryState.CharacterCreationQualitiesReceipts!.Last();
            Assert.AreEqual(persisted, reconciled.Receipt);
            if (attempted.Receipt is not null) Assert.AreEqual(attempted.Receipt, reconciled.Receipt);
            Assert.IsNull(edit.ConflictingCommand(cold, owners.Capture()).Receipt);
            AssertUnchanged(context, knownFiles, "Unknown outcome reconciles without another write");
        }
        else
        {
            Assert.IsNull(attempted.Receipt);
            AssertUnchanged(context, files, "Failure before rename");
        }
        Assert.AreEqual(seed.Receipt, Service(context, cold, owners).LookupDraft(owners.Capture(),
            CharacterCreationCompletionDraftTransaction.LookupRequest(seed.Command)).Value);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    [DataRow(false, true)]
    public void Composite89_stage_owner_ABA_or_source_loss_refuses_without_write(bool magic, bool sourceLoss)
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var seed = Composite89Seed(context, owners, retained: false);
        var source = new Composite89SourceGate(context.Resolver);
        var edit = Composite89Prepare(context, owners, seed.Choices, magic, source);
        var stamp = owners.Capture();
        var before = Snapshot(context);
        if (sourceLoss) source.Available = false;
        else
        {
            owners.Set(new OwnerScope("composite89-other"));
            owners.Set(OwnerScope.LocalSingleUser);
        }
        Assert.IsNull(edit.Confirm(new FileWorkspaceStore(context.Directory), stamp).Receipt);
        AssertUnchanged(context, before, "A previous preview cannot grant current owner/source authority");
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Composite89_stage_edit_readmits_retained_lifestyle_inputs_without_rewriting_history(bool magic)
    {
        using var context = ReadyContext.Create(true, includeNonEmptyPurchases: true,
            talentValue: "Mystic Adept", mysticPowerPoints: 1, qualityName: "Guts");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        Composite89SaveContact(context);
        Composite89SaveLifestyle(context);
        var seed = Composite89Seed(context, owners, retained: true);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsNotNull(before.Document.AuxiliaryState.CharacterCreationLifestylesDraft);
        Assert.IsNotNull(before.Document.AuxiliaryState.CharacterCreationContactsDraft);
        var edit = Composite89Prepare(context, owners, seed.Choices, magic, context.Resolver);
        var result = edit.Confirm(new FileWorkspaceStore(context.Directory), owners.Capture());
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome,
            string.Join(",", result.Blockers));
        var cold = new FileWorkspaceStore(context.Directory);
        var after = cold.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            before.Document.AuxiliaryState.CharacterCreationContactsDraft,
            after.Document.AuxiliaryState.CharacterCreationContactsDraft));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            before.Document.AuxiliaryState.CharacterCreationContactReceipts,
            after.Document.AuxiliaryState.CharacterCreationContactReceipts));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            before.Document.AuxiliaryState.CharacterCreationLifestyleReceipts,
            after.Document.AuxiliaryState.CharacterCreationLifestyleReceipts));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            before.Document.AuxiliaryState.CharacterCreationLifestylesDraft,
            after.Document.AuxiliaryState.CharacterCreationLifestylesDraft));
        var inputs = Composite89Inputs(context, cold, owners);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            edit.Choices, inputs.CompositeBaseline!.Selections));
        var review = Service(context, cold, owners).ReviewDraft(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, inputs.CompositeBaseline.Selections));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome,
            string.Join(",", review.Blockers));
    }

    [TestMethod]
    public void Composite89_quality_preview_refuses_local_budget_success_that_overspends_shared_karma()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var seed = Composite89Seed(context, owners, retained: false);
        var service = new OwnerBoundCharacterCreationQualitiesService(context.Store, owners, context.Queries, context.Resolver);
        var stamp = owners.Capture();
        var state = service.Load(stamp, new(context.WorkspaceId)).Value!;
        Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
        int remaining = seed.Review.Review.Balances.RemainingCreationKarma
            ?? throw new InvalidOperationException("The admitted shared balance must be exact.");
        CharacterCreationQualitiesPreview? locallyValid = null;
        string[]? overBudget = null;
        foreach (var option in state.Authority.Options.Where(row => row.IsSelectable
            && row.Type == CharacterCreationQualityType.Positive && row.CountsAgainstKarma
            && !seed.Choices.QualityOptionIds.Contains(row.OptionId)))
        {
            string[] candidate = [.. seed.Choices.QualityOptionIds, option.OptionId];
            var local = CharacterCreationQualitiesRules.Evaluate(new(state.Binding, state.Authority, candidate));
            if (!local.CanConfirm || local.PositiveQualityBudget.Used - state.Preview.PositiveQualityBudget.Used
                <= remaining) continue;
            locallyValid = local;
            overBudget = candidate;
            break;
        }
        Assert.IsNotNull(locallyValid, "Fixture needs a legal local quality choice that exceeds the exact shared budget.");
        var files = Snapshot(context);
        var preview = service.Preview(stamp, new(state.Binding, overBudget!));
        Assert.IsFalse(preview.Value?.CanConfirm == true, "Magic, resources and explicit paid skills also consume Karma.");
        Assert.IsNull(service.Confirm(stamp, new(state.Binding, overBudget!, locallyValid!.PreviewDigest,
            "composite89-shared-overspend", Guid.NewGuid(), true)).Value);
        AssertUnchanged(context, files);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Composite89_selected_current_source_byte_drift_rejects_old_preview(bool magic)
    {
        using var context = ReadyContext.Create(true, includeNonEmptyPurchases: true,
            talentValue: "Mystic Adept", mysticPowerPoints: 1, qualityName: "Guts", prepareSource: _ => { });
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var seed = Composite89Seed(context, owners, retained: true);
        var edit = Composite89Prepare(context, owners, seed.Choices, magic, context.Resolver);
        string selectedId;
        if (magic) selectedId = edit.Choices.MagicSelections.Spells.Last().SourceId;
        else
        {
            var state = new OwnerBoundCharacterCreationQualitiesService(context.Store, owners, context.Queries, context.Resolver)
                .Load(owners.Capture(), new(context.WorkspaceId)).Value!;
            selectedId = state.Authority.Options.Single(row => row.OptionId == edit.Choices.QualityOptionIds.Single())
                .SourceId.ToString();
        }
        string path = Path.Combine(context.Directory, "source", "data", magic ? "spells.xml" : "qualities.xml");
        var document = XDocument.Load(path);
        var selected = document.Descendants(magic ? "spell" : "quality").Single(row =>
            string.Equals(row.Element("id")?.Value, selectedId, StringComparison.OrdinalIgnoreCase));
        selected.SetElementValue("name", selected.Element("name")!.Value + " changed");
        document.Save(path);
        // The source fixture mutation is intentional; no workspace/checkpoint mutation is allowed.
        var files = Snapshot(context);
        Assert.IsNull(edit.Confirm(new FileWorkspaceStore(context.Directory), owners.Capture()).Receipt);
        AssertUnchanged(context, files);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    private static CharacterCreationCompletionReviewInputs Composite89Inputs(ReadyContext context,
        IWorkspaceStore store, CharacterCreationAttributesServiceTests.AllocationOwners owners)
    {
        var result = new OwnerBoundCharacterCreationCompletionReader(store, owners, context.Resolver, context.Queries)
            .LoadCompletionInputs(owners.Capture(), new(context.WorkspaceId) { IncludeKarmaPurchasePolicy = true });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        return result.Value!;
    }

    private static Composite89Baseline Composite89Seed(ReadyContext context,
        CharacterCreationAttributesServiceTests.AllocationOwners owners, bool retained)
    {
        var inputs = Composite89Inputs(context, context.Store, owners);
        var choices = retained ? CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs)
            : UnsavedChoices(inputs);
        var paid = inputs.Skills.Authority.KnowledgeSkills.First(row => !row.CanBeNativeLanguage && !row.IsExotic
            && choices.SkillAllocations.All(selected => selected.SourceSkillId != row.SourceSkillId));
        choices = choices with
        {
            MagicSelections = Composite89NormalizeMagic(choices.MagicSelections),
            SkillKarmaPurchases = [new(paid.SourceSkillId, paid.Kind, paid.SourceNodeDigest, 1)]
        };
        Assert.IsTrue(choices.MagicSelections.Spells.Count > 0);
        Assert.IsTrue(choices.MagicSelections.AdeptPowers.Count > 0);
        Assert.IsTrue(choices.QualityOptionIds.Count > 0);
        var service = Service(context, context.Store, owners);
        var review = service.ReviewDraft(owners.Capture(), context.WorkspaceId, new(inputs.SnapshotDigest, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var command = Draft56Command(review.Value!, "composite89-seed");
        var applied = service.ApplyDraft(owners.Capture(), command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        return new(choices, review.Value!, command, applied.Value!);
    }

    private static Composite89Edit Composite89Prepare(ReadyContext context,
        CharacterCreationAttributesServiceTests.AllocationOwners owners, CharacterCreationCompletionSelections choices,
        bool magic, ICharacterSourceDataResolver resolver)
    {
        var cold = new FileWorkspaceStore(context.Directory);
        var stamp = owners.Capture();
        if (magic)
        {
            var service = new OwnerBoundCharacterCreationMagicResonanceService(cold, owners, resolver, context.Queries);
            var inputs = service.LoadCompletionInputs(stamp, new(context.WorkspaceId));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, inputs.Outcome, string.Join(",", inputs.Blockers));
            var state = service.Load(stamp, new(context.WorkspaceId)).Value!;
            Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                choices.MagicSelections, state.CompositeSelections), "Load exposes effective choices separately from history.");
            Assert.AreEqual((decimal)choices.MagicSelections.Spells.Count, state.SpellBudget.Used);
            var alternative = state.Authority.Spells.First(row => row.IsEnabled && row.Blockers.Count == 0
                && !choices.MagicSelections.Spells.Contains(row.Identity));
            var next = Composite89NormalizeMagic(choices.MagicSelections with
            {
                Spells = choices.MagicSelections.Spells.Skip(1).Append(alternative.Identity).ToArray()
            });
            CharacterCreationMagicResonancePreview? accepted = null;
            foreach (var power in state.Authority.AdeptPowers.Where(row => row.IsEnabled && row.Blockers.Count == 0
                && choices.MagicSelections.AdeptPowers.All(existing => existing.Identity != row.Identity))
                .OrderBy(row => row.PointCost))
            {
                var candidate = Composite89NormalizeMagic(next with { AdeptPowers = [new(power.Identity, 1)] });
                var preview = service.Preview(stamp, new(state.Binding, candidate));
                if (preview.Value?.CanConfirm != true) continue;
                next = candidate;
                accepted = preview.Value;
                break;
            }
            Assert.IsNotNull(accepted, "A nonempty source-valid spell and power replacement must be admitted.");
            var command = new CharacterCreationMagicResonanceConfirmRequest(state.Binding, next,
                accepted!.PreviewDigest, "composite89-magic", true);
            return new(choices with { MagicSelections = next }, 0,
                (store, owner) => Composite89Result.From(new OwnerBoundCharacterCreationMagicResonanceService(
                    store, owners, resolver, context.Queries).Confirm(owner, command)),
                (store, owner) => Composite89Result.From(new OwnerBoundCharacterCreationMagicResonanceService(
                    store, owners, resolver, context.Queries).Confirm(owner, command with { PreviewDigest = new string('0', 64) })));
        }
        else
        {
            var service = new OwnerBoundCharacterCreationQualitiesService(cold, owners, context.Queries, resolver);
            var inputs = service.LoadCompletionInputs(stamp, new(context.WorkspaceId));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, inputs.Outcome, string.Join(",", inputs.Blockers));
            var state = service.Load(stamp, new(context.WorkspaceId)).Value!;
            Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
            CollectionAssert.AreEquivalent(choices.QualityOptionIds.ToArray(),
                state.Preview.Selections.Select(row => row.OptionId).ToArray());
            int oldCost = state.Preview.PositiveQualityBudget.Used;
            // A cheaper real positive choice changes shared Karma while retaining a nonempty page.
            CharacterCreationQualitiesPreview? accepted = null;
            string[]? next = null;
            foreach (var option in state.Authority.Options.Where(row => row.IsSelectable
                && row.Type == CharacterCreationQualityType.Positive && row.CountsAgainstKarma
                && row.CountsAgainstQualityLimit && !row.IsMetagenic && !row.IsFreeOrGranted
                && row.KarmaCost > 0 && row.KarmaCost < oldCost
                && !choices.QualityOptionIds.Contains(row.OptionId)).OrderBy(row => row.OptionId, StringComparer.Ordinal))
            {
                var candidate = service.Preview(stamp, new(state.Binding, [option.OptionId]));
                if (candidate.Value?.CanConfirm != true) continue;
                accepted = candidate.Value;
                next = [option.OptionId];
                break;
            }
            Assert.IsNotNull(accepted, "At least one source-valid replacement must be editable; blanket refusal is not success.");
            var command = new CharacterCreationQualitiesConfirmRequest(state.Binding, next!, accepted!.PreviewDigest,
                "composite89-qualities", Guid.NewGuid(), true);
            return new(choices with { QualityOptionIds = next! }, oldCost - accepted.PositiveQualityBudget.Used,
                (store, owner) => Composite89Result.From(new OwnerBoundCharacterCreationQualitiesService(
                    store, owners, context.Queries, resolver).Confirm(owner, command)),
                (store, owner) => Composite89Result.From(new OwnerBoundCharacterCreationQualitiesService(
                    store, owners, context.Queries, resolver).Confirm(owner, command with { PreviewDigest = new string('0', 64) })));
        }
    }

    private static CharacterCreationMagicResonanceSelections Composite89NormalizeMagic(
        CharacterCreationMagicResonanceSelections choices)
        => choices with
        {
            AdeptPowers = choices.AdeptPowers.OrderBy(row => row.Identity.Kind, StringComparer.Ordinal)
                .ThenBy(row => row.Identity.SourceId, StringComparer.Ordinal).ThenBy(row => row.Levels).ToArray(),
            Spells = choices.Spells.OrderBy(row => row.Kind, StringComparer.Ordinal)
                .ThenBy(row => row.SourceId, StringComparer.Ordinal).ToArray(),
            ComplexForms = choices.ComplexForms.OrderBy(row => row.Kind, StringComparer.Ordinal)
                .ThenBy(row => row.SourceId, StringComparer.Ordinal).ToArray()
        };

    private static void Composite89SaveContact(ReadyContext context)
    {
        var service = new CharacterCreationContactsService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
        var edit = new CharacterCreationContactEdit(Guid.NewGuid(),
            state.NewContactTemplate!.Identity with { Name = "Retained medic" }, Connection: 1, Loyalty: 1)
            { ChangeKind = CharacterCreationContactChangeKind.Add };
        var preview = service.Preview(new(state.Binding, edit));
        Assert.IsTrue(preview.Success, string.Join(",", preview.Blockers));
        var saved = service.Confirm(new(state.Binding, edit, preview.Value!.PreviewDigest,
            "composite89-retained-contact", true));
        Assert.AreEqual(CharacterCreationContactOutcomes.Applied, saved.Outcome, string.Join(",", saved.Blockers));
    }

    private static void Composite89SaveLifestyle(ReadyContext context)
    {
        var service = new CharacterCreationLifestylesService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
        var low = state.Authority.LifestyleOptions.Single(row => row.Name == "Low");
        var home = new CharacterCreationLifestyleConfiguration(Guid.NewGuid(), low.OptionId, "Retained home",
            CharacterCreationLifestyleStyleIds.Standard, low.DefaultIncrementId, 1, 100m, 0,
            false, false, 0, 0, 0, 0, "Seattle", "", "", []);
        var mutation = new CharacterCreationLifestyleMutation(CharacterCreationLifestyleMutationKinds.Create,
            home.LifestyleId, home);
        var preview = service.Preview(new(state.Binding, mutation));
        Assert.IsTrue(preview.Value?.CanConfirm == true, string.Join(",", preview.Blockers));
        var saved = service.Confirm(new(state.Binding, mutation, preview.Value!.PreviewDigest, "composite89-home", true));
        Assert.IsTrue(saved.Success, string.Join(",", saved.Blockers));
    }

    private sealed record Composite89Baseline(CharacterCreationCompletionSelections Choices,
        CharacterCreationCompletionDraftReview Review, CharacterCreationCompletionDraftApplyRequest Command,
        CharacterCreationCompletionDraftReceipt Receipt);

    private sealed record Composite89Edit(CharacterCreationCompletionSelections Choices, int KarmaReleased,
        Func<IWorkspaceStore, OwnerContextStamp, Composite89Result> Confirm,
        Func<IWorkspaceStore, OwnerContextStamp, Composite89Result> ConflictingCommand);

    private sealed record Composite89Result(string Outcome, object? Receipt, IReadOnlyList<string> Blockers)
    {
        internal static Composite89Result From<T>(CharacterCreationFoundationResult<T> result) where T : class
            => new(result.Outcome, result.Value, result.Blockers);
    }

    private sealed class Composite89SourceGate(ICharacterSourceDataResolver inner) : ICharacterSourceDataResolver
    {
        internal bool Available { get; set; } = true;
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
            => Available ? inner.TryCreateContext(characterXml) : null;
    }
}
