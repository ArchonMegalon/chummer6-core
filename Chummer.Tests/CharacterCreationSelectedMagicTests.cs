using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationSelectedMagicTests
{
    [TestMethod]
    [DataRow("Adept", "D", "MAG")]
    [DataRow("Magician", "B", "MAG")]
    [DataRow("Technomancer", "B", "RES")]
    [DataRow("Mystic Adept", "B", "MAG")]
    public void Selected_replay_matches_full_actual_source_preview_with_distinct_original_and_proposed_attributes(
        string talentName, string talentRank, string attributeId)
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, talentName, talentRank);
        var service = new CharacterCreationMagicResonanceService(context.Store, context.Resolver);
        var loaded = service.LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        var inputs = loaded.Value!;
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var inputBytesDigest = CharacterCreationMagicResonanceDigest.Compute(inputs);
        CharacterCreationAttributeAllocation[] allocations = [new(attributeId, 1, 0)];
        var choices = MagicResonanceCompletionTests.CompleteSelections(inputs,
            inputs.SelectedTalent.Magic + (attributeId == "MAG" ? 1 : 0),
            talentName == "Mystic Adept" ? 2 : 0);
        var original = CharacterCreationMagicResonanceService.PreviewCompletion(inputs, [], inputs.Selections);
        var proposed = CharacterCreationMagicResonanceService.PreviewCompletion(inputs, allocations, choices);
        Assert.IsNotNull(original.Value, string.Join(",", original.Blockers));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, proposed.Outcome, string.Join(",", proposed.Blockers));
        Assert.IsNotNull(proposed.Value);

        var mutableSpells = choices.Spells.ToList();
        var captured = CharacterCreationMagicResonanceService.CaptureSelectedMagicAuthority(
            inputs, choices with { Spells = mutableSpells });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        Assert.IsTrue(CharacterCreationMagicResonanceService.IsValidSelectedMagicAuthority(witness));
        Assert.AreEqual(inputs.Authority.AuthorityDigest, witness.ProvenanceAuthorityDigest);
        Assert.AreEqual(inputs.SnapshotDigest, witness.OriginalInputsSnapshotDigest);
        Assert.AreEqual(inputs.PrerequisiteDraft.DraftDigest, witness.PrerequisiteDraftDigest);
        Assert.AreEqual(CharacterCreationSelectedMagicRules.ComputePayloadDigest(witness), witness.PayloadDigest);
        Assert.AreNotEqual(witness.ProvenanceAuthorityDigest, witness.PayloadDigest,
            "Selected payload identity must not impersonate the full catalogue authority.");
        Assert.IsFalse(witness.OriginalHadSavedMagic);
        AssertSame(proposed.Value.Selections, witness.ProposedSelections);
        Assert.AreEqual(choices.Tradition, witness.ProposedSelections.Tradition);
        Assert.AreEqual(choices.Stream, witness.ProposedSelections.Stream);
        Assert.AreEqual(choices.MysticAdeptPowerPoints, witness.ProposedSelections.MysticAdeptPowerPoints);
        CollectionAssert.AreEquivalent(choices.AdeptPowers.ToArray(), witness.ProposedSelections.AdeptPowers.ToArray());
        CollectionAssert.AreEquivalent(choices.Spells.ToArray(), witness.ProposedSelections.Spells.ToArray());
        CollectionAssert.AreEquivalent(choices.ComplexForms.ToArray(), witness.ProposedSelections.ComplexForms.ToArray());
        AssertExactSelectedRows(inputs, witness);
        mutableSpells.Clear();
        AssertSame(proposed.Value.Selections, witness.ProposedSelections);

        var evaluated = CharacterCreationMagicResonanceService.EvaluateSelectedMagic(witness,
            inputs.PrerequisiteDraft, inputs.Attributes.Attributes, proposed.Value!.Attributes.Attributes);
        Assert.AreEqual(proposed.Outcome, evaluated.Outcome, string.Join(",", evaluated.Blockers));
        var replay = evaluated.Value!;
        Assert.IsNotNull(replay);
        AssertMatchesFullPreview(original.Value!, replay.Original);
        AssertMatchesFullPreview(proposed.Value, replay.Proposed);
        Assert.AreEqual(witness.PayloadDigest, replay.WitnessPayloadDigest);
        Assert.AreEqual(witness.OriginalAttributesDigest, replay.OriginalAttributesDigest);
        Assert.AreNotEqual(replay.OriginalAttributesDigest, replay.ProposedAttributesDigest);
        Assert.AreEqual(CharacterCreationSelectedMagicRules.ComputeReplayDigest(replay), replay.ReplayDigest);
        Assert.AreEqual(replay.ReplayDigest, CharacterCreationMagicResonanceService.EvaluateSelectedMagic(witness,
            inputs.PrerequisiteDraft, inputs.Attributes.Attributes, proposed.Value.Attributes.Attributes).Value!.ReplayDigest);
        int originalRating = attributeId == "MAG" ? replay.Original.EffectiveAttributes.Magic : replay.Original.EffectiveAttributes.Resonance;
        int proposedRating = attributeId == "MAG" ? replay.Proposed.EffectiveAttributes.Magic : replay.Proposed.EffectiveAttributes.Resonance;
        Assert.AreEqual(originalRating + 1, proposedRating);
        Assert.AreEqual(inputBytesDigest, CharacterCreationMagicResonanceDigest.Compute(inputs));
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Missing_duplicate_or_unselected_source_rows_and_malformed_prerequisite_are_rejected_without_writes()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Magician", "B");
        var service = new CharacterCreationMagicResonanceService(context.Store, context.Resolver);
        var inputs = service.LoadCompletionInputs(new(context.WorkspaceId)).Value!;
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var choices = MagicResonanceCompletionTests.CompleteSelections(inputs, inputs.SelectedTalent.Magic);
        var capture = CharacterCreationMagicResonanceService.CaptureSelectedMagicAuthority(inputs, choices);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, capture.Outcome, string.Join(",", capture.Blockers));
        var witness = capture.Value!;
        Assert.IsTrue(witness.Spells.Count > 0);
        var unselected = inputs.Authority.Spells.First(row => !choices.Spells.Contains(row.Identity));
        CharacterCreationSelectedMagicAuthority[] malformed =
        [
            Reseal(witness with { Spells = witness.Spells.Skip(1).ToArray() }),
            Reseal(witness with { Spells = witness.Spells.Concat([witness.Spells[0]]).ToArray() }),
            Reseal(witness with { Spells = witness.Spells.Concat([unselected]).OrderBy(row => row.Name, StringComparer.Ordinal)
                .ThenBy(row => row.Identity.SourceId, StringComparer.Ordinal).ToArray() }),
            witness with { PayloadDigest = witness.ProvenanceAuthorityDigest }
        ];
        foreach (var bad in malformed)
        {
            Assert.IsFalse(CharacterCreationMagicResonanceService.IsValidSelectedMagicAuthority(bad));
            var rejected = CharacterCreationMagicResonanceService.EvaluateSelectedMagic(bad,
                inputs.PrerequisiteDraft, inputs.Attributes.Attributes, inputs.Attributes.Attributes);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, rejected.Outcome);
            Assert.IsNull(rejected.Value);
            CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationSelectedMagicRules.Invalid);
        }

        var duplicatedTalent = inputs with
        {
            PrerequisiteDraft = inputs.PrerequisiteDraft with
            {
                Assignments = inputs.PrerequisiteDraft.Assignments.Concat(
                    [inputs.PrerequisiteDraft.Assignments.Single(row => row.CategoryId == CharacterCreationPriorityCategoryIds.Talent)]).ToArray()
            }
        };
        var malformedCapture = CharacterCreationMagicResonanceService.CaptureSelectedMagicAuthority(duplicatedTalent, choices);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, malformedCapture.Outcome);
        Assert.IsNull(malformedCapture.Value);
        CollectionAssert.Contains(malformedCapture.Blockers.ToArray(), CharacterCreationSelectedMagicRules.Invalid);
        var staleCapture = CharacterCreationMagicResonanceService.CaptureSelectedMagicAuthority(
            inputs with { SnapshotDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("other-full-input") }, choices);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, staleCapture.Outcome);
        Assert.IsNull(staleCapture.Value);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Saved_adept_history_is_replayed_at_original_magic_and_is_neither_replaced_nor_resealed()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Adept", "D");
        var service = new CharacterCreationMagicResonanceService(context.Store, context.Resolver);
        var initial = service.LoadCompletionInputs(new(context.WorkspaceId)).Value!;
        var choices = MagicResonanceCompletionTests.CompleteSelections(initial, initial.SelectedTalent.Magic);
        MagicResonanceCompletionTests.SaveAttributes(context, []);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        var preview = service.Preview(new(state.Binding, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, preview.Outcome, string.Join(",", preview.Blockers));
        var confirmed = service.Confirm(new(state.Binding, choices, preview.Value!.PreviewDigest,
            "selected-magic-original-adept", true));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome, string.Join(",", confirmed.Blockers));

        using ReadyContext cold = context.Restart();
        var coldService = new CharacterCreationMagicResonanceService(cold.Store, cold.Resolver);
        var loaded = coldService.LoadCompletionInputs(new(cold.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        var inputs = loaded.Value!;
        Assert.IsNotNull(inputs.SavedMagicState);
        var before = cold.Store.Get(cold.WorkspaceId).Value!;
        var files = CaptureFiles(cold);
        var savedDraft = before.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft!;
        var savedReceipt = before.Document.AuxiliaryState.CharacterCreationMagicResonanceReceipts!.Single();
        Assert.AreEqual(confirmed.Value!.ReceiptDigest, savedReceipt.ReceiptDigest);
        var original = CharacterCreationMagicResonanceService.PreviewCompletion(inputs,
            inputs.Attributes.PendingDraft!.Allocations, inputs.Selections);
        var proposed = CharacterCreationMagicResonanceService.PreviewCompletion(inputs, [new("MAG", 1, 0)], inputs.Selections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, original.Outcome);
        Assert.IsNotNull(proposed.Value);
        var captured = CharacterCreationMagicResonanceService.CaptureSelectedMagicAuthority(inputs, inputs.Selections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        Assert.IsTrue(witness.OriginalHadSavedMagic);
        var evaluated = CharacterCreationMagicResonanceService.EvaluateSelectedMagic(witness,
            inputs.PrerequisiteDraft, inputs.Attributes.Attributes, proposed.Value!.Attributes.Attributes);
        Assert.AreEqual(proposed.Outcome, evaluated.Outcome, string.Join(",", evaluated.Blockers));
        Assert.IsNotNull(evaluated.Value);
        AssertMatchesFullPreview(original.Value!, evaluated.Value!.Original);
        AssertMatchesFullPreview(proposed.Value, evaluated.Value.Proposed);
        Assert.AreEqual(initial.SelectedTalent.Magic, evaluated.Value.Original.EffectiveAttributes.Magic);
        Assert.AreEqual(initial.SelectedTalent.Magic + 1, evaluated.Value.Proposed.EffectiveAttributes.Magic);
        Assert.AreEqual(0, evaluated.Value.Original.Blockers.Count);

        var borrowedFutureAttributes = CharacterCreationMagicResonanceService.EvaluateSelectedMagic(witness,
            inputs.PrerequisiteDraft, proposed.Value.Attributes.Attributes, proposed.Value.Attributes.Attributes);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, borrowedFutureAttributes.Outcome);
        Assert.IsNull(borrowedFutureAttributes.Value);
        CollectionAssert.Contains(borrowedFutureAttributes.Blockers.ToArray(), CharacterCreationSelectedMagicRules.HistoryMismatch);
        Assert.IsTrue(inputs.Selections.AdeptPowers.Count > 0);
        var discardedManualPower = CharacterCreationMagicResonanceService.CaptureSelectedMagicAuthority(inputs,
            inputs.Selections with { AdeptPowers = inputs.Selections.AdeptPowers.Skip(1).ToArray() });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, discardedManualPower.Outcome);
        Assert.IsNull(discardedManualPower.Value);
        CollectionAssert.Contains(discardedManualPower.Blockers.ToArray(), CharacterCreationSelectedMagicRules.ManualChoiceConflict);
        AssertWorkspaceUnchanged(cold, before, files);

        // A real saved Attributes edit invalidates the old Magic review. Pure
        // replay must not repair that history or silently issue a fresh receipt.
        MagicResonanceCompletionTests.SaveAttributes(cold, [new("MAG", 1, 0)]);
        var afterAttributes = cold.Store.Get(cold.WorkspaceId).Value!;
        var afterFiles = CaptureFiles(cold);
        Assert.AreEqual(savedDraft.DraftDigest, afterAttributes.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft!.DraftDigest);
        Assert.AreEqual(savedReceipt.ReceiptDigest,
            afterAttributes.Document.AuxiliaryState.CharacterCreationMagicResonanceReceipts!.Single().ReceiptDigest);
        var stale = coldService.LoadCompletionInputs(new(cold.WorkspaceId));
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, stale.Outcome);
        Assert.IsNull(stale.Value);
        CollectionAssert.Contains(stale.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.DraftInvalid);
        AssertWorkspaceUnchanged(cold, afterAttributes, afterFiles);
    }

    private static CharacterCreationSelectedMagicAuthority Reseal(CharacterCreationSelectedMagicAuthority value)
        => value with { PayloadDigest = CharacterCreationSelectedMagicRules.ComputePayloadDigest(value) };

    private static void AssertMatchesFullPreview(CharacterCreationMagicResonanceCompletionProjection full,
        CharacterCreationSelectedMagicEvaluation selected)
    {
        AssertSame(full.Selections, selected.Selections);
        AssertSame(full.EffectiveAttributes, selected.EffectiveAttributes);
        AssertSame(full.TraditionBudget, selected.TraditionBudget);
        AssertSame(full.StreamBudget, selected.StreamBudget);
        AssertSame(full.AdeptPowerPointBudget, selected.AdeptPowerPointBudget);
        AssertSame(full.SpellBudget, selected.SpellBudget);
        AssertSame(full.ComplexFormBudget, selected.ComplexFormBudget);
        AssertSame(full.MysticAdeptPowerPoints, selected.MysticAdeptPowerPoints);
        CollectionAssert.AreEqual(full.SourceAnchorIds.ToArray(), selected.SourceAnchorIds.ToArray());
        CollectionAssert.AreEqual(full.Blockers.ToArray(), selected.Blockers.ToArray());
    }

    private static void AssertExactSelectedRows(CharacterCreationMagicResonanceCompletionInputs inputs,
        CharacterCreationSelectedMagicAuthority selected)
    {
        var full = inputs.Authority.Traditions.Concat(inputs.Authority.Streams).Concat(inputs.Authority.AdeptPowers)
            .Concat(inputs.Authority.Spells).Concat(inputs.Authority.ComplexForms).ToArray();
        var rows = selected.Traditions.Concat(selected.Streams).Concat(selected.AdeptPowers)
            .Concat(selected.Spells).Concat(selected.ComplexForms).ToArray();
        foreach (var row in rows)
        {
            // Compare the complete source payload, not just IDs: automatic
            // quality grants and recursive gear must not be dropped by slicing.
            var expected = full.Single(item => item.Identity == row.Identity);
            AssertSame(expected, row);
            Assert.AreNotSame(expected, row);
        }
        AssertSame(inputs.SelectedTalent, selected.Talent);
        AssertSame(inputs.Authority.MysticAdeptPowerPointPolicy, selected.MysticAdeptPowerPointPolicy);
        CollectionAssert.AreEqual(inputs.Authority.SourceAnchorIds.ToArray(), selected.SourceAnchorIds.ToArray());
    }

    private static void AssertSame<T>(T expected, T actual)
        => Assert.AreEqual(CharacterCreationMagicResonanceDigest.Compute(expected), CharacterCreationMagicResonanceDigest.Compute(actual));

    private static Dictionary<string, byte[]> CaptureFiles(ReadyContext context)
        => Directory.EnumerateFiles(context.Directory, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(context.Directory, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertWorkspaceUnchanged(ReadyContext context, WorkspaceStoredDocument before,
        IReadOnlyDictionary<string, byte[]> files)
    {
        var after = context.Store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
        var actual = CaptureFiles(context);
        CollectionAssert.AreEquivalent(files.Keys.ToArray(), actual.Keys.ToArray());
        foreach (var pair in files)
            CollectionAssert.AreEqual(pair.Value, actual[pair.Key], pair.Key);
    }
}
