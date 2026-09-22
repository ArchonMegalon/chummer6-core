using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Finalization_sequence_contains_every_source_bound_occurrence_and_reopens_without_writes(bool finish)
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("complete-sequence-preview");
            var store = SeedJourney(directory, id);
            var service = CreateService(store);
            string[] modules = [FormativeArcologyId, TeenCorporateId, SkipEducationId, BountyHunterId, BountyHunterId];
            AppendSequence(service, id, modules);
            if (finish)
            {
                var state = JourneyState(service, id);
                var request = new CharacterCreationLifeModuleFinishRequest(state.Binding, state.DraftRevision, state.DraftDigest);
                var preview = service.PreviewFinishSelection(request).Value!;
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                    service.ConfirmFinishSelection(new(request, preview.PreviewDigest, true)).Outcome);
            }
            byte[] before = File.ReadAllBytes(WorkspacePath(directory, id));
            var draft = Load(service, id).PendingDraft!;
            var result = SequencePreview(service, id);
            var sequence = result.ModuleSequence!;
            Assert.IsNotNull(sequence);
            Assert.AreEqual(draft.DraftDigest, sequence.DraftDigest);
            Assert.AreEqual(draft.DraftRevision, sequence.DraftRevision);
            Assert.AreEqual(draft.SourceDigest, sequence.SourceDigest);
            Assert.IsTrue(IsCanonicalDigest(sequence.SourceContextDigest));
            Assert.HasCount(6, sequence.Occurrences);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, sequence.Occurrences.Select(row => row.Order).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 5 }, sequence.Occurrences.Select(row => row.StageOrder).ToArray());
            CollectionAssert.AreEqual(new[] { TirModuleId }.Concat(modules).ToArray(),
                sequence.Occurrences.Select(row => row.Selection.ModuleId).ToArray());
            Assert.HasCount(6, sequence.Occurrences.Select(row => row.OccurrenceId).Distinct().ToArray());
            Assert.AreEqual(draft.ProjectedEffects.Count + draft.AdditionalModules!.Sum(entry => entry.ProjectedEffects.Count),
                sequence.Occurrences.Sum(row => row.Compilation.Effects.Count));
            Assert.IsTrue(sequence.Occurrences.All(row => row.Compilation.DraftDigest == draft.DraftDigest
                && row.Compilation.DraftRevision == draft.DraftRevision));
            Assert.HasCount(0, sequence.Occurrences[3].Compilation.Effects,
                "Skipping education is a real source-owned zero-effect decision, not a missing stage.");
            CollectionAssert.AreEqual(new[] { "version", "version", "module" },
                sequence.Occurrences[0].Compilation.Effects.Take(3).Select(effect => effect.SourcePhase).ToArray());
            Assert.IsTrue(sequence.Occurrences[2].Compilation.Effects.Any(effect => effect.PromptIds.Count > 0),
                "Unresolved later-module prompts must be visible, not hidden behind nationality.");
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                draft.AdditionalModules[0].FollowUpValues, sequence.Occurrences[1].FollowUpValues));

            var firstRepeat = sequence.Occurrences[4];
            var secondRepeat = sequence.Occurrences[5];
            Assert.AreNotEqual(firstRepeat.OccurrenceId, secondRepeat.OccurrenceId);
            CollectionAssert.AreEqual(firstRepeat.Compilation.Effects.Select(effect => effect.EffectId).ToArray(),
                secondRepeat.Compilation.Effects.Select(effect => effect.EffectId).ToArray(),
                "Source-local IDs stay source-local; occurrence identity distinguishes separate purchases.");
            var winner = sequence.QualityLevels.Single(row => row.Group == "SINner");
            Assert.AreEqual(3, winner.Level, "Nationality tier 1 plus Arcology tier 3 resolves to 3, never 4.");
            Assert.AreEqual("SINner (Corporate Limited)", winner.Target.CanonicalName);
            CollectionAssert.AreEqual(new[] { 1, 3 }, winner.Contributions.Select(value => value.Level).ToArray());
            Assert.AreEqual(sequence.Occurrences[0].OccurrenceId, winner.Contributions[0].OccurrenceId);
            Assert.AreEqual(sequence.Occurrences[1].OccurrenceId, winner.Contributions[1].OccurrenceId);
            Assert.IsTrue(IsCanonicalDigest(winner.QualityLevelsSourceDigest));
            Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                sequence with { CompilationDigest = string.Empty }), sequence.CompilationDigest);
            Assert.AreEqual(finish, sequence.SelectionFinished);
            Assert.AreEqual(!finish, sequence.Blockers.Contains(CharacterCreationFoundationBlockers.FinalizationRequiredStagesIncomplete));
            Assert.IsFalse(result.CanApply);
            Assert.IsFalse(result.CanConfirm);
            CollectionAssert.Contains(sequence.Blockers.ToArray(), CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, ConfirmFinalization(service, result).Outcome);

            var reopened = SequencePreview(CreateService(new FileWorkspaceStore(directory)), id);
            Assert.AreEqual(result.PreviewDigest, reopened.PreviewDigest);
            Assert.AreEqual(JsonSerializer.Serialize(sequence), JsonSerializer.Serialize(reopened.ModuleSequence));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)),
                "Compilation, rejected confirmation and reopen must leave the original character/book bytes untouched.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Finalization_sequence_identity_survives_append_but_parent_preview_changes()
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("sequence-identity");
            var store = SeedJourney(directory, id);
            var service = CreateService(store);
            AppendSequence(service, id, [FormativeArcologyId, TeenCorporateId, SkipEducationId, BountyHunterId]);
            var first = SequencePreview(service, id);
            AppendSequence(service, id, [BountyHunterId]);
            var second = SequencePreview(service, id);
            Assert.HasCount(5, first.ModuleSequence!.Occurrences);
            Assert.HasCount(6, second.ModuleSequence!.Occurrences);
            CollectionAssert.AreEqual(first.ModuleSequence!.Occurrences.Select(row => row.OccurrenceId).ToArray(),
                second.ModuleSequence!.Occurrences.Take(5).Select(row => row.OccurrenceId).ToArray());
            Assert.AreNotEqual(first.ModuleSequence.CompilationDigest, second.ModuleSequence.CompilationDigest);
            Assert.AreNotEqual(first.PreviewDigest, second.PreviewDigest);
            byte[] before = File.ReadAllBytes(WorkspacePath(directory, id));
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, ConfirmFinalization(service, first).Outcome);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow("effect")]
    [DataRow("stage")]
    [DataRow("anchor")]
    [DataRow("answer")]
    public void Finalization_sequence_rejects_rehashed_later_module_tampering(string kind)
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("sequence-tamper");
            var store = SeedJourney(directory, id);
            var service = CreateService(store);
            AppendSequence(service, id, [FormativeArcologyId, TeenCorporateId, SkipEducationId]);
            var workspace = store.Get(id).Value!;
            var draft = workspace.Document.AuxiliaryState.CharacterCreationFoundationDraft!;
            var entries = draft.AdditionalModules!.ToArray();
            var entry = entries[0];
            entries[0] = kind switch
            {
                "effect" => entry with { ProjectedEffects = entry.ProjectedEffects.Select((effect, index) =>
                    index == 0 ? effect with { AfterValue = "99" } : effect).ToArray() },
                "stage" => entry with { StageOrder = 5 },
                "anchor" => entry with { SourceAnchorIds = ["forged-source#not-authority"] },
                "answer" => entry with { FollowUpValues = new Dictionary<string, string> { ["forged-prompt"] = "Renraku" } },
                _ => throw new InvalidOperationException()
            };
            draft = draft with { AdditionalModules = entries, DraftRevision = draft.DraftRevision + 1,
                BaseContentRevision = workspace.ContentRevision };
            draft = draft with { DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(draft) };
            var document = workspace.Document with { State = workspace.Document.State with
            { AuxiliaryState = workspace.Document.AuxiliaryState with { CharacterCreationFoundationDraft = draft } } };
            Assert.IsTrue(store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(id, workspace.ContentRevision,
                workspace.Document.AuxiliaryStateDigest, document).Success);
            byte[] before = File.ReadAllBytes(WorkspacePath(directory, id));
            service = CreateService(new FileWorkspaceStore(directory));
            var loaded = service.Load(new(id));
            Assert.IsNotNull(loaded.Value);
            var result = service.PreviewFinalization(new(loaded.Value.Binding, draft.DraftRevision, draft.DraftDigest));
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
            Assert.IsFalse(result.Value?.CanApply == true);
            Assert.IsTrue(result.Value?.ModuleSequence is null
                || result.Value.ModuleSequence.Blockers.Contains(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict)
                || result.Blockers.Contains(CharacterCreationFoundationBlockers.PendingDraftInvalid));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void AppendSequence(CharacterCreationFoundationService service, CharacterWorkspaceId id,
        IEnumerable<string> modules)
    {
        foreach (string module in modules)
        {
            var request = JourneyRequest(JourneyState(service, id), module);
            var preview = service.PreviewModule(request);
            Assert.IsNotNull(preview.Value, string.Join(", ", preview.Blockers));
            var confirmed = service.ConfirmModule(new(request, preview.Value.PreviewDigest, true));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome, string.Join(", ", confirmed.Blockers));
        }
    }

    private static CharacterCreationFoundationFinalizationPreview SequencePreview(
        CharacterCreationFoundationService service, CharacterWorkspaceId id)
    {
        var state = Load(service, id);
        var result = service.PreviewFinalization(new(state.Binding, state.PendingDraft!.DraftRevision,
            state.PendingDraft.DraftDigest));
        Assert.IsNotNull(result.Value, string.Join(", ", result.Blockers));
        return result.Value;
    }
}
