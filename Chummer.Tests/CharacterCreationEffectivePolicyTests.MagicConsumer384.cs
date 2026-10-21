using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow(null, false, false)]
    [DataRow("Magician", false, false)]
    [DataRow("Adept", false, false)]
    [DataRow("Mystic Adept", false, false)]
    [DataRow("Mystic Adept", false, true)]
    [DataRow("Mystic Adept", true, false)]
    [DataRow("Technomancer", false, false)]
    public void Complete384_Canonical_magic_matches_independent_cold_execution_without_mutation(
        string? talent, bool separateMagic, bool exchange)
    {
        using var context = MagicContext384(talent, separateMagic, exchange);
        string workspaceBefore = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!);
        var inputs = LoadConsumption(context);
        var request = GrantRequest368(inputs);
        string inputsBefore = JsonSerializer.Serialize(inputs);
        string requestBefore = JsonSerializer.Serialize(request);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var reviewed = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var review = reviewed.Value!;
        Assert.AreEqual(CharacterCreationCompleteCoverage.Evaluated,
            Operation332(review.CompleteAssessment!, CharacterCreationConsumerPolicyOperation.MagicAndResonance).Coverage);
        var full = CharacterCreationCompleteMagicExecution.FromFull(inputs, review, authority);
        Assert.IsNotNull(full);
        Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(full!.DigestFor(authority)));
        Assert.AreEqual("{}", JsonSerializer.Serialize(full), "Private execution is not a serialized proof contract.");
        var magic = review.UpstreamBudget!.Magic;
        if (talent is null)
        {
            Assert.AreEqual(CharacterCreationMagicResonanceKinds.Mundane, magic.Talent.Kind);
            Assert.AreEqual(0, magic.Selections.Spells.Count);
            Assert.AreEqual(0, magic.Selections.AdeptPowers.Count);
            Assert.AreEqual(0, magic.Selections.ComplexForms.Count);
        }
        if (talent == "Magician" || talent == "Mystic Adept")
        {
            Assert.IsNotNull(magic.Selections.Tradition);
            Assert.IsTrue(magic.Selections.Spells.Count > 0);
        }
        if (talent == "Technomancer")
        {
            Assert.IsNotNull(magic.Selections.Stream);
            Assert.IsTrue(magic.Selections.ComplexForms.Count > 0);
        }
        if (separateMagic)
        {
            Assert.IsNotNull(magic.EffectiveAttributes.MagicAdept);
            Assert.IsNull(magic.MysticAdeptPowerPoints);
        }
        if (talent == "Mystic Adept" && !separateMagic) Assert.IsNotNull(magic.MysticAdeptPowerPoints);
        if (exchange) Assert.IsTrue(magic.MysticAdeptPowerPoints!.ExchangedSpellSlots > 0);

        // Encode/decode and replay using the existing real original archive
        // fixture. The helper gets no full Magic catalog or full execution object.
        var cold = Cold342(context, inputs, request, out var witness);
        Assert.AreEqual(JsonSerializer.Serialize(review.CompleteAssessment),
            JsonSerializer.Serialize(cold.CompleteAssessment));
        var original = CharacterCreationCompletionPreservationRules.SnapshotOriginal(inputs);
        var effects = inputs.Contacts.OriginalEffects;
        string coldBefore = JsonSerializer.Serialize(new { witness, cold, original, effects });
        var selectedAuthority = CharacterCreationCompleteAssessmentService.FromSelectedAuthority(witness, request, original, effects);
        Assert.AreEqual(JsonSerializer.Serialize(authority), JsonSerializer.Serialize(selectedAuthority));
        var selected = CharacterCreationCompleteMagicExecution.FromSelected(
            witness, request, cold, original, effects, selectedAuthority);
        Assert.IsNotNull(selected);
        Assert.AreEqual(full.DigestFor(authority), selected!.DigestFor(selectedAuthority));
        Assert.AreEqual(coldBefore, JsonSerializer.Serialize(new { witness, cold, original, effects }));
        Assert.AreEqual(inputsBefore, JsonSerializer.Serialize(inputs));
        Assert.AreEqual(requestBefore, JsonSerializer.Serialize(request));
        Assert.AreEqual(workspaceBefore, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    public void Complete384_Execution_refuses_missing_borrowed_changed_and_historical_authority()
    {
        using var context = MagicContext384(null);
        var inputs = LoadConsumption(context);
        var request = GrantRequest368(inputs);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        Assert.IsNotNull(review);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var execution = CharacterCreationCompleteMagicExecution.FromFull(inputs, review, authority);
        Assert.IsNotNull(execution);
        Assert.IsNull(execution!.DigestFor(null));
        Assert.IsNull(CharacterCreationCompleteMagicExecution.FromFull(inputs, review, null));
        string changed = CharacterCreationFinalizationDigest.Compute("other magic authority");
        foreach (var other in new[]
        {
            authority with { InputsSnapshotDigest = changed },
            authority with { SelectedInputsDigest = changed },
            authority with { PolicyDigest = changed },
            authority with { RequestDigest = changed },
            authority with { SelectionsDigest = changed },
            authority with { RawCharacterXmlDigest = changed },
            authority with { Obligations = authority.Obligations.Skip(1).ToArray() },
            review.CompleteAssessment!
        })
        {
            Assert.IsNull(execution.DigestFor(other));
            Assert.IsNull(CharacterCreationCompleteMagicExecution.FromFull(inputs, review, other));
        }
        foreach (string schema in new[] { CharacterCreationCompleteAssessment.SchemaV1,
            CharacterCreationCompleteAssessment.SchemaV2, CharacterCreationCompleteAssessment.SchemaV3 })
        {
            var historicalRequest = request with { CompleteAssessmentSchema = schema };
            var historical = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, historicalRequest);
            Assert.IsNull(execution.DigestFor(historical));
            Assert.IsNull(CharacterCreationCompleteMagicExecution.FromFull(
                inputs, review with { Request = historicalRequest }, historical));
        }
        var cold = Cold342(context, inputs, request, out var witness);
        var original = CharacterCreationCompletionPreservationRules.SnapshotOriginal(inputs);
        Assert.IsNull(CharacterCreationCompleteMagicExecution.FromSelected(
            witness, request, cold, original, inputs.Contacts.OriginalEffects, null));
        Assert.IsNull(CharacterCreationCompleteMagicExecution.FromSelected(
            witness, request, cold, original, inputs.Contacts.OriginalEffects, authority with { PolicyDigest = changed }));
        Assert.IsNull(CharacterCreationCompleteMagicExecution.FromSelected(
            witness, request, cold, null!, inputs.Contacts.OriginalEffects, authority));
        Assert.IsNull(CharacterCreationCompleteMagicExecution.FromSelected(
            witness, request, cold, original, null!, authority));
        foreach (string schema in new[] { CharacterCreationCompleteAssessment.SchemaV1,
            CharacterCreationCompleteAssessment.SchemaV2, CharacterCreationCompleteAssessment.SchemaV3 })
        {
            var historicalRequest = request with { CompleteAssessmentSchema = schema };
            var historical = CharacterCreationCompleteAssessmentService.FromSelectedAuthority(
                witness, historicalRequest, original, inputs.Contacts.OriginalEffects);
            Assert.IsNull(CharacterCreationCompleteMagicExecution.FromSelected(witness, historicalRequest, cold,
                original, inputs.Contacts.OriginalEffects, historical));
        }
    }

    [TestMethod]
    [DataRow("Magician")]
    [DataRow("Mystic Adept")]
    [DataRow("Technomancer")]
    public void Complete384_Green_results_and_empty_spells_cannot_bypass_canonical_magic_rules(string talent)
    {
        using var context = MagicContext384(talent);
        var inputs = LoadConsumption(context);
        var request = GrantRequest368(inputs);
        var reviewed = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var review = reviewed.Value!;
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var cold = Cold342(context, inputs, request, out var witness);
        var original = CharacterCreationCompletionPreservationRules.SnapshotOriginal(inputs);
        string before = JsonSerializer.Serialize(new { inputs, request, review, witness, cold });
        var choices = request.Selections;
        foreach (var invalid in new[]
        {
            choices with { MagicSelections = choices.MagicSelections with { Tradition = null, Stream = null } },
            choices with { MagicSelections = choices.MagicSelections with { Spells = [], ComplexForms = [] } },
            choices with { MagicSelections = choices.MagicSelections with { MysticAdeptPowerPoints = int.MaxValue } },
            choices with { AttributeAllocations = [new("MAG", int.MaxValue, 0)] }
        })
        {
            var changed = request with { Selections = invalid };
            var changedAuthority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, changed);
            Assert.IsNull(CharacterCreationCompleteMagicExecution.FromFull(
                inputs, review with { Request = changed }, changedAuthority));
            Assert.IsNull(CharacterCreationCompleteMagicExecution.FromSelected(witness, changed, cold, original,
                inputs.Contacts.OriginalEffects, changedAuthority));
        }
        var magic = review.UpstreamBudget!.Magic;
        Assert.IsNull(CharacterCreationCompleteMagicExecution.FromFull(inputs, review with
        {
            UpstreamBudget = review.UpstreamBudget with
            {
                Magic = magic with { EffectiveAttributes = magic.EffectiveAttributes with { Magic = int.MaxValue } }
            }
        }, authority));
        Assert.IsNull(CharacterCreationCompleteMagicExecution.FromSelected(witness, request,
            cold with { Magic = cold.Magic with { Proposed = cold.Magic.Proposed with { Blockers = ["mechanical-failure"] } } },
            original, inputs.Contacts.OriginalEffects, authority));
        Assert.IsNull(CharacterCreationCompleteMagicExecution.FromFull(inputs,
            review with { Blockers = ["mechanical-failure"] }, authority));
        Assert.AreEqual(before, JsonSerializer.Serialize(new { inputs, request, review, witness, cold }));
    }

    [TestMethod]
    public void Complete384_Cold_magic_requires_original_attributes_and_selected_sources_even_after_rehash()
    {
        using var context = MagicContext384("Magician");
        var inputs = LoadConsumption(context);
        var request = GrantRequest368(inputs);
        var cold = Cold342(context, inputs, request, out var witness);
        var original = CharacterCreationCompletionPreservationRules.SnapshotOriginal(inputs);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        foreach (var missing in new[]
        {
            witness.Magic with { Traditions = [] },
            witness.Magic with { Spells = [] },
            witness.Magic with { OriginalAttributesDigest = CharacterCreationFinalizationDigest.Compute("invented original") }
        })
        {
            var source = missing with { PayloadDigest = CharacterCreationSelectedMagicRules.ComputePayloadDigest(missing) };
            var forged = witness with { Magic = source };
            forged = forged with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forged) };
            Assert.IsNull(CharacterCreationCompleteMagicExecution.FromSelected(forged, request,
                cold with { WitnessDigest = forged.WitnessDigest }, original, inputs.Contacts.OriginalEffects, authority));
        }
        var absent = witness with { Skills = witness.Skills with { Attributes = null! } };
        absent = absent with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(absent) };
        Assert.IsNull(CharacterCreationCompleteMagicExecution.FromSelected(absent, request,
            cold with { WitnessDigest = absent.WitnessDigest }, original, inputs.Contacts.OriginalEffects, authority));
        Assert.IsNull(CharacterCreationCompleteMagicExecution.FromSelected(witness, request, cold,
            original with { Magic = new(null, null, [], [], []) }, inputs.Contacts.OriginalEffects, authority));
    }

    [TestMethod]
    public void Complete384_Cancellation_never_issues_execution_or_mutates_inputs()
    {
        using var context = MagicContext384("Mystic Adept", separateMagic: true);
        var inputs = LoadConsumption(context);
        var request = GrantRequest368(inputs);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        Assert.IsNotNull(review);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var cold = Cold342(context, inputs, request, out var witness);
        var original = CharacterCreationCompletionPreservationRules.SnapshotOriginal(inputs);
        string before = JsonSerializer.Serialize(new { inputs, request, review, authority, witness, cold, original });
        string workspaceBefore = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        AssertCancelled384(() => CharacterCreationCompleteMagicExecution.FromFull(inputs, review, authority, cancellation.Token));
        AssertCancelled384(() => CharacterCreationCompleteMagicExecution.FromSelected(witness, request, cold,
            original, inputs.Contacts.OriginalEffects, authority, cancellation.Token));
        Assert.AreEqual(before, JsonSerializer.Serialize(new { inputs, request, review, authority, witness, cold, original }));
        Assert.AreEqual(workspaceBefore, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!));
    }

    private static ReadyContext MagicContext384(string? talent, bool separateMagic = false, bool exchange = false)
        => ReadyContext.Create(false, talentValue: talent, includeSkillPurchase: true,
            mysticPowerPoints: talent == "Mystic Adept" && !separateMagic ? 1 : 0,
            amendSettings: settings =>
            {
                SetLimits340(settings, 6, 6, 12, 12);
                settings.SetElementValue("mysadeptsecondmagattribute", separateMagic.ToString());
                settings.SetElementValue("priorityspellsasadeptpowers", exchange.ToString());
            });

    private static void AssertCancelled384(Action action)
    {
        bool cancelled = false;
        try { action(); }
        catch (OperationCanceledException) { cancelled = true; }
        Assert.IsTrue(cancelled);
    }
}
