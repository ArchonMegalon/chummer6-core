using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using C = Chummer.Contracts.Characters.CharacterCreationCompleteCoverage;
using K = Chummer.Contracts.Characters.CharacterCreationCompleteObligationKind;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RuleSource410_Initial_reader_binds_consumer_policy_after_rule_source_capture(bool amend)
    {
        using var context = RuleContext410(amend: amend);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        string before = JsonSerializer.Serialize(original);
        var inputs = Prepare410(context).Inputs;

        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1, inputs.RuleSourceExecutionSchema);
        Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(inputs.RuleSourceCaptureDigest));
        Assert.IsTrue(CharacterCreationCompletionConsumerPolicyProjection.IsValid(inputs.EffectivePolicy!, inputs),
            "Consumer policy must bind the admitted rule-source identity before original-input validation.");
        Assert.IsTrue(CharacterCreationOriginalCharacterSource.TryFromFull(inputs, out _));
        Assert.AreEqual(original.Document.Content, inputs.OriginalCharacter!.RawCharacterXml);
        AssertSame396(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));

        var stripped = inputs with { RuleSourceExecutionSchema = null, RuleSourceCaptureDigest = null };
        stripped = stripped with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(stripped) };
        Assert.IsFalse(CharacterCreationCompletionConsumerPolicyProjection.IsValid(stripped.EffectivePolicy!, stripped),
            "Rehashing inputs cannot remove the rule-source identity bound into the consumer policy.");
        Assert.IsFalse(CharacterCreationOriginalCharacterSource.TryFromFull(stripped, out _));
    }

    [TestMethod]
    public void RuleSource410_Amended_cap_executes_full_and_restored_cold_on_the_real_original()
    {
        using var context = RuleContext410();
        var original = context.Store.Get(context.WorkspaceId).Value!;
        string before = JsonSerializer.Serialize(original);
        var prepared = Prepare410(context);
        var inputs = prepared.Inputs;
        var request = RuleRequest410(inputs);
        Assert.AreEqual(6, inputs.Skills.Authority.MaxActiveSkillRatingCreate);
        var full = CharacterCreationCompletionReviewService.Review(inputs, request, ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        Assert.AreEqual(5, full.Value!.Skills!.Skills.Single(row => row.Name == "Pistols").Rating);
        Assert.AreEqual(C.Evaluated, SelectedSource410(full.Value.CompleteAssessment!).Coverage);
        AssertAmendments410(full.Value.CompleteAssessment!, C.Evaluated);
        AssertOtherObligations410(full.Value.CompleteAssessment!);

        var bootstrap = inputs.OriginalCharacter!.BootstrapSourceSnapshot.Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request,
            ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, captured.Value!, out var archive));
        byte[] privateSources = prepared.Execution.ExportPrivateInputs();
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(privateSources,
            inputs.RuleSourceCaptureDigest!, out var restored));
        Assert.AreNotEqual(inputs.EffectivePolicy!.PolicyDigest, restored!.EffectivePolicyDigest,
            "The raw inventory and enriched consumer policy are distinct identities.");
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        Assert.IsNull(context.Resolver.TryCreateContext(original.Document.Content));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var witness));
        var coldCapability = new CharacterCreationRuleSourceExecutionContext(restored, context.Queries);
        var cold = CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, witness, request,
            ruleSources: coldCapability);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, cold.Outcome, string.Join(",", cold.Blockers));
        Assert.AreEqual(5, cold.Value!.Skills.Skills.Single(row => row.Name == "Pistols").Rating);
        AssertSame396(JsonSerializer.Serialize(full.Value.CompleteAssessment), JsonSerializer.Serialize(cold.Value.CompleteAssessment));
        var fullAgain = CharacterCreationCompletionReviewService.Review(inputs, request, ruleSources: coldCapability);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, fullAgain.Outcome, string.Join(",", fullAgain.Blockers));
        Assert.AreEqual(full.Value.ReviewDigest, fullAgain.Value!.ReviewDigest);

        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var fullGraph = CharacterCreationSelectedEffectsConsumer.FromFull(inputs, full.Value, authority);
        Assert.IsNotNull(fullGraph);
        Assert.IsTrue(CharacterCreationOriginalCharacterSource.TryFromSelected(original, coldBootstrap, witness, request, out var coldOriginal));
        var selectedAuthority = CharacterCreationCompleteAssessmentService.FromSelectedAuthority(witness, request,
            CharacterCreationCompletionPreservationRules.SnapshotOriginal(inputs), inputs.Contacts.OriginalEffects);
        var coldGraph = CharacterCreationSelectedEffectsConsumer.FromSelected(witness, request, cold.Value,
            selectedAuthority, inputs.Loadout?.PendingDraft?.Instances, originalCharacter: coldOriginal);
        Assert.IsNotNull(coldGraph);
        AssertSame396(fullGraph.CopyCompiledGraph().ToString(SaveOptions.DisableFormatting),
            coldGraph.CopyCompiledGraph().ToString(SaveOptions.DisableFormatting));
        AssertSame396(JsonSerializer.Serialize(fullGraph.CopyDeltas()), JsonSerializer.Serialize(coldGraph.CopyDeltas()));
        Assert.IsTrue(fullGraph.CopyDeltas().Count > 0);
        Assert.IsTrue(XNode.DeepEquals(XDocument.Parse(original.Document.Content).Root!.Element("customnote"),
            fullGraph.CopyCompiledGraph().Element("customnote")));
        AssertSame396(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        Assert.AreEqual(inputs.Skills.Binding.RawCharacterXmlDigest, witness.CompleteAssessment!.RawCharacterXmlDigest);
        Assert.IsFalse(ContainsOriginalContent410(JsonSerializer.SerializeToElement(witness),
            original.Document.Content, context.Directory),
            "A witness may retain source digests, but not raw character XML or private source paths.");
        Assert.AreEqual("{}", JsonSerializer.Serialize(coldCapability));

        var complete = CharacterCreationCompletionReviewService.Review(inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete }, ruleSources: coldCapability);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, complete.Outcome);
        CollectionAssert.Contains(complete.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.CoverageUnavailable);
        Assert.IsFalse(complete.Value!.CompleteAssessment!.SatisfiesComplete);
    }

    [TestMethod]
    public void RuleSource410_Privacy_check_distinguishes_digest_fields_from_original_content()
    {
        const string original = "<character><customnote>private origin</customnote></character>";
        const string directory = "/private/rule-source-fixture";
        bool Contains(object value) => ContainsOriginalContent410(JsonSerializer.SerializeToElement(value), original, directory);
        Assert.IsFalse(Contains(new { nested = new { RawCharacterXmlDigest = new string('a', 64) } }));
        Assert.IsTrue(Contains(new { nested = new { RawCharacterXml = "" } }));
        Assert.IsTrue(Contains(new { nested = new { rawCharacterXml = "" } }));
        Assert.IsTrue(Contains(new { nested = new[] { original } }));
        Assert.IsTrue(Contains(new { nested = new[] { directory + "/settings.xml" } }));
    }

    private static bool ContainsOriginalContent410(JsonElement value, string original, string directory)
    {
        if (value.ValueKind == JsonValueKind.Object)
            return value.EnumerateObject().Any(property =>
                string.Equals(property.Name, "RawCharacterXml", StringComparison.OrdinalIgnoreCase)
                || ContainsOriginalContent410(property.Value, original, directory));
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Any(item => ContainsOriginalContent410(item, original, directory));
        return value.ValueKind == JsonValueKind.String && value.GetString() is { } text
            && (text.Contains(original, StringComparison.Ordinal) || text.Contains(directory, StringComparison.Ordinal));
    }

    [TestMethod]
    public void RuleSource410_Independently_prepared_unamended_cap_four_rejects_rating_five()
    {
        using var control = RuleContext410(amend: false);
        var prepared = Prepare410(control);
        Assert.AreEqual(4, prepared.Inputs.Skills.Authority.MaxActiveSkillRatingCreate);
        var result = CharacterCreationCompletionReviewService.Review(prepared.Inputs, RuleRequest410(prepared.Inputs),
            ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        Assert.IsTrue(result.Blockers.Count > 0);
        Assert.IsFalse(result.Value?.CompleteAssessment?.Obligations.Any(row => row.Kind == K.Source && row.Coverage == C.Evaluated) ?? false);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RuleSource410_Unknown_sibling_or_file_cannot_be_certified_by_a_consumed_cap(bool separateFile)
    {
        using var context = RuleContext410(unknownSibling: !separateFile, unknownFile: separateFile);
        var prepared = Prepare410(context);
        Assert.AreEqual(6, prepared.Inputs.Skills.Authority.MaxActiveSkillRatingCreate);
        var request = RuleRequest410(prepared.Inputs);
        var full = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request, ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        Assert.AreEqual(5, full.Value!.Skills!.Skills.Single(row => row.Name == "Pistols").Rating);
        Assert.AreEqual(C.Evaluated, Limit332(full.Value.CompleteAssessment!, CharacterCreationEffectivePolicyContext.ActiveLimit).Coverage);
        Assert.AreEqual(C.Unresolved, SelectedSource410(full.Value.CompleteAssessment!).Coverage);
        AssertAmendments410(full.Value.CompleteAssessment!, C.Unresolved);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var bootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot.Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, prepared.Inputs, request,
            ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(prepared.Execution.ExportPrivateInputs(),
            prepared.Inputs.RuleSourceCaptureDigest!, out var restored));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, captured.Value!, request,
            ruleSources: new(restored!, context.Queries));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(C.Unresolved, SelectedSource410(replay.Value!.CompleteAssessment!).Coverage);
        AssertAmendments410(replay.Value.CompleteAssessment!, C.Unresolved);
    }

    [TestMethod]
    [DataRow("unknown-rule-410", C.Unresolved)]
    [DataRow(" \t\r\n ", C.Evaluated)]
    public void RuleSource410_Selected_setting_leaf_text_requires_consumption(string text, C expectedCoverage)
    {
        using var context = RuleContext410(selectedSettingText: text);
        var prepared = Prepare410(context);
        Assert.AreEqual(6, prepared.Inputs.Skills.Authority.MaxActiveSkillRatingCreate);
        var request = RuleRequest410(prepared.Inputs);
        var full = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request, ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        Assert.AreEqual(5, full.Value!.Skills!.Skills.Single(row => row.Name == "Pistols").Rating);
        Assert.AreEqual(C.Evaluated, Limit332(full.Value.CompleteAssessment!, CharacterCreationEffectivePolicyContext.ActiveLimit).Coverage);
        Assert.AreEqual(expectedCoverage, SelectedSource410(full.Value.CompleteAssessment!).Coverage);
        AssertAmendments410(full.Value.CompleteAssessment!, expectedCoverage);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var bootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot.Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, prepared.Inputs, request,
            ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(prepared.Execution.ExportPrivateInputs(),
            prepared.Inputs.RuleSourceCaptureDigest!, out var restored));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, captured.Value!, request,
            ruleSources: new(restored!, context.Queries));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(expectedCoverage, SelectedSource410(replay.Value!.CompleteAssessment!).Coverage);
        AssertAmendments410(replay.Value.CompleteAssessment!, expectedCoverage);
    }

    [TestMethod]
    public void RuleSource410_Exactly_excluded_profile_mixed_text_does_not_taint_selected_source()
    {
        using var context = RuleContext410(inactiveSibling: true, inactiveSettingText: "unknown-rule-410");
        var prepared = Prepare410(context);
        Assert.AreEqual(6, prepared.Inputs.Skills.Authority.MaxActiveSkillRatingCreate);
        var request = RuleRequest410(prepared.Inputs);
        var full = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request, ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        Assert.AreEqual(5, full.Value!.Skills!.Skills.Single(row => row.Name == "Pistols").Rating);
        Assert.AreEqual(C.Evaluated, Limit332(full.Value.CompleteAssessment!, CharacterCreationEffectivePolicyContext.ActiveLimit).Coverage);
        Assert.AreEqual(C.Evaluated, SelectedSource410(full.Value.CompleteAssessment!).Coverage);
        AssertAmendments410(full.Value.CompleteAssessment!, C.Evaluated);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var bootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot.Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, prepared.Inputs, request,
            ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(prepared.Execution.ExportPrivateInputs(),
            prepared.Inputs.RuleSourceCaptureDigest!, out var restored));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, captured.Value!, request,
            ruleSources: new(restored!, context.Queries));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(C.Evaluated, SelectedSource410(replay.Value!.CompleteAssessment!).Coverage);
        AssertAmendments410(replay.Value.CompleteAssessment!, C.Evaluated);
    }

    [TestMethod]
    public void RuleSource410_Actual_nonmatching_profile_selector_accounts_for_its_inapplicable_sibling()
    {
        using var context = RuleContext410(inactiveSibling: true);
        var prepared = Prepare410(context);
        var result = CharacterCreationCompletionReviewService.Review(prepared.Inputs, RuleRequest410(prepared.Inputs),
            ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.AreEqual(C.Evaluated, SelectedSource410(result.Value!.CompleteAssessment!).Coverage);
        AssertOtherObligations410(result.Value.CompleteAssessment!);
    }

    [TestMethod]
    public void RuleSource410_Missing_rebound_and_stripped_capabilities_fail_closed()
    {
        using var context = RuleContext410();
        using var foreign = RuleContext410(amend: false);
        var prepared = Prepare410(context);
        var other = Prepare410(foreign);
        var inputs = prepared.Inputs;
        var request = RuleRequest410(inputs);
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs, request).Value);
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs, request, ruleSources: other.Execution).Value);
        foreach (string? variant in new string?[] { null, "unknown" })
            Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs,
                request with { RuleSourceExecutionSchema = variant }, ruleSources: prepared.Execution).Value);
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs,
            request with { RuleSourceCaptureDigest = null }, ruleSources: prepared.Execution).Value);
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs,
            request with { InputsSnapshotDigest = other.Inputs.SnapshotDigest }, ruleSources: prepared.Execution).Value);
        var missing = inputs with { RuleSourceCaptureDigest = null };
        missing = missing with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(missing) };
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(missing,
            request with { InputsSnapshotDigest = missing.SnapshotDigest }, ruleSources: prepared.Execution).Value);
        var withoutOriginal = inputs with { OriginalCharacter = null };
        withoutOriginal = withoutOriginal with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(withoutOriginal) };
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(withoutOriginal,
            request with { InputsSnapshotDigest = withoutOriginal.SnapshotDigest }, ruleSources: prepared.Execution).Value);
        byte[] changed = prepared.Execution.ExportPrivateInputs();
        changed[^1] ^= 1;
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(changed, inputs.RuleSourceCaptureDigest!, out _));
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture([], inputs.RuleSourceCaptureDigest!, out _));
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(prepared.Execution.ExportPrivateInputs(),
            inputs.RuleSourceCaptureDigest!, out var restored));
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs, request,
            ruleSources: new(new CaptureWithoutExecution410(restored!), context.Queries)).Value);

        var executionForFive = prepared.Execution.Resolve(inputs, request, default);
        Assert.IsNotNull(executionForFive);
        var differentRequest = request with { Selections = request.Selections with
            { SkillAllocations = request.Selections.SkillAllocations.Select(row => row.Kind == CharacterCreationSkillKinds.Active
                ? row with { Rating = 4 } : row).ToArray() } };
        var differentReview = CharacterCreationCompletionReviewService.Review(inputs, differentRequest, ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, differentReview.Outcome, string.Join(",", differentReview.Blockers));
        var differentAuthority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, differentRequest);
        var rebound = CharacterCreationCompleteConsumerExecution.FromFull(inputs, differentReview.Value!, differentAuthority,
            ruleSourceExecution: executionForFive);
        Assert.IsNotNull(rebound);
        Assert.IsFalse(rebound.EvaluationsFor(differentAuthority).ContainsKey(SelectedSource410(differentAuthority).Id));

        var original = context.Store.Get(context.WorkspaceId).Value!;
        string before = JsonSerializer.Serialize(original);
        var bootstrap = inputs.OriginalCharacter!.BootstrapSourceSnapshot.Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request,
            ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, captured.Value!, request).Value);
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(foreign.Store.Get(foreign.WorkspaceId).Value!, bootstrap,
            captured.Value!, request, ruleSources: prepared.Execution).Value);
        foreach (var changedWitness in new[] { captured.Value! with { RuleSourceExecutionSchema = null },
            captured.Value! with { RuleSourceExecutionSchema = "unknown" }, captured.Value! with { RuleSourceCaptureDigest = other.Inputs.RuleSourceCaptureDigest } })
        {
            var resealed = changedWitness with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(changedWitness) };
            Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, resealed, request,
                ruleSources: prepared.Execution).Value);
        }
        var removed = request with { Selections = request.Selections with
            { SkillAllocations = request.Selections.SkillAllocations.Where(row => row.Kind != CharacterCreationSkillKinds.Active).ToArray(),
                ChangeableSkillTargets = [] } };
        var refusal = CharacterCreationCompletionReviewService.Review(inputs, removed, ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, refusal.Outcome);
        AssertSame396(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void RuleSource410_Omission_keeps_legacy_bytes_and_source_verdicts()
    {
        using var context = RuleContext410();
        var inputs = LoadOriginal396(context);
        var request = OriginalRequest396(inputs);
        string inputJson = JsonSerializer.Serialize(inputs), requestJson = JsonSerializer.Serialize(request);
        Assert.IsFalse(inputJson.Contains("RuleSource", StringComparison.Ordinal));
        Assert.IsFalse(requestJson.Contains("RuleSource", StringComparison.Ordinal));
        AssertSame396(inputJson, JsonSerializer.Serialize(inputs with { RuleSourceExecutionSchema = null, RuleSourceCaptureDigest = null }));
        AssertSame396(requestJson, JsonSerializer.Serialize(request with { RuleSourceExecutionSchema = null, RuleSourceCaptureDigest = null }));
        var reviewed = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        Assert.AreEqual(C.Unresolved, SelectedSource410(reviewed.Value!.CompleteAssessment!).Coverage);
        var amendments = Operation332(reviewed.Value.CompleteAssessment!, CharacterCreationConsumerPolicyOperation.Amendments);
        Assert.AreEqual(CharacterCreationCompleteEvaluability.AuthorityUnavailable, amendments.Evaluability);
        Assert.AreEqual(C.Unresolved, amendments.Coverage);
        var cold = Cold342(context, inputs, request, out var witness);
        Assert.IsFalse(JsonSerializer.Serialize(witness).Contains("RuleSource", StringComparison.Ordinal));
        AssertSame396(JsonSerializer.Serialize(reviewed.Value.CompleteAssessment), JsonSerializer.Serialize(cold.CompleteAssessment));
    }

    private static ReadyContext RuleContext410(bool amend = true, bool unknownSibling = false, bool unknownFile = false,
        bool inactiveSibling = false, string? selectedSettingText = null, string? inactiveSettingText = null)
        => ReadyContext.Create(false, includeNonEmptyPurchases: true, includeSkillPurchase: true,
            beforeDrafts: character =>
            {
                character.Add(new XElement("customdatadirectorynames", new XElement("directoryname", "Cap410")));
                character.Add(new XElement("customnote", new XAttribute("source", "manual"), "Retain original graph"));
            },
            amendSettings: settings =>
            {
                SetLimits340(settings, 4, 6, 12, 12);
                settings.SetElementValue("customdatadirectorynames", string.Empty);
                settings.Element("customdatadirectorynames")!.Add(new XElement("customdatadirectoryname",
                    new XElement("directoryname", "Cap410"), new XElement("order", 0), new XElement("enabled", true)));
            },
            prepareSource: root =>
            {
                string directory = Path.Combine(root, "customdata", "Cap410");
                Directory.CreateDirectory(directory);
                if (amend)
                {
                    var entry = new XElement("setting", new XElement("id", CharacterCreationBootstrapProfiles.PrioritySettingsProfileId),
                        new XElement("maxskillratingcreate", new XAttribute("amendoperation", "REPLACE"), 6));
                    if (unknownSibling) entry.Add(new XElement("unknown-rule-410", new XElement("value", 17)));
                    var settings = new XElement("settings", entry);
                    if (selectedSettingText is not null) settings.Add(new XElement("setting", selectedSettingText));
                    if (inactiveSibling) settings.Add(new XElement("setting",
                        new XElement("id", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), new XElement("unknown-rule-410", 17), inactiveSettingText));
                    new XDocument(new XElement("chummer", settings)).Save(Path.Combine(directory,
                        "amend_" + CharacterCreationBootstrapProfiles.SettingsSourceFile(CharacterCreationBootstrapProfiles.PrioritySettingsProfileId)));
                }
                if (unknownFile) new XDocument(new XElement("chummer", new XElement("unhandled-rule-410", 17)))
                    .Save(Path.Combine(directory, "unhandled-410.xml"));
            });

    private static CharacterCreationRuleSourcePreparation Prepare410(ReadyContext context)
    {
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        IOwnerBoundCharacterCreationCompletionReader reader = new OwnerBoundCharacterCreationCompletionReader(
            context.Store, owners, context.Resolver, context.Queries);
        var result = reader.LoadCompletionInputsWithRuleSourceExecution(owners.Capture(), new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.AreEqual(0, owners.ActiveLeases);
        return result.Value!;
    }

    private static CharacterCreationCompletionReviewRequest RuleRequest410(CharacterCreationCompletionReviewInputs inputs)
    {
        var request = OriginalRequest396(inputs);
        string pistols = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols").SourceSkillId;
        return request with
        {
            RuleSourceExecutionSchema = CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1,
            RuleSourceCaptureDigest = inputs.RuleSourceCaptureDigest,
            Selections = request.Selections with
            {
                SkillAllocations = request.Selections.SkillAllocations.Select(row => row.SourceSkillId == pistols
                    && row.Kind == CharacterCreationSkillKinds.Active ? row with { Rating = 5 } : row).ToArray(),
                ChangeableSkillTargets = [new(CharacterCreationSkillKinds.Active, pistols)]
            }
        };
    }

    private static CharacterCreationCompleteObligation SelectedSource410(CharacterCreationCompleteAssessment assessment)
        => assessment.Obligations.Single(row => row.Kind == K.Source && row.SourceKind == CharacterCreationPolicySourceKind.SelectedAmend);

    private static void AssertAmendments410(CharacterCreationCompleteAssessment assessment, C expectedCoverage)
    {
        var rows = assessment.Obligations.Where(row => row.Kind is K.ConsumerOperation or K.ActiveGraph
            && row.Operation == CharacterCreationConsumerPolicyOperation.Amendments).ToArray();
        Assert.AreEqual(2, rows.Length);
        foreach (var row in rows)
        {
            string diagnostic = $"{row.Id}: {row.Evaluability}, {row.Coverage}, {row.Reason}";
            Assert.AreEqual(CharacterCreationCompleteEvaluability.CandidateDependent, row.Evaluability, diagnostic);
            Assert.AreEqual(expectedCoverage, row.Coverage, diagnostic);
            if (expectedCoverage == C.Evaluated)
                Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(row.EvaluationDigest), diagnostic);
            else
                Assert.IsNull(row.EvaluationDigest, diagnostic);
        }
        Assert.AreEqual(rows[0].EvaluationDigest, rows[1].EvaluationDigest,
            "Both amendment obligations must consume the same exact source execution.");
    }

    private static void AssertOtherObligations410(CharacterCreationCompleteAssessment assessment)
    {
        Assert.IsFalse(assessment.SatisfiesComplete);
        Assert.IsTrue(assessment.Obligations.Where(row => row.Kind == K.Source && row.SourceKind == CharacterCreationPolicySourceKind.Rules
            || row.Kind == K.ActiveGraph && row.Operation is CharacterCreationConsumerPolicyOperation.CharacterMechanics
                or CharacterCreationConsumerPolicyOperation.Errata or CharacterCreationConsumerPolicyOperation.Campaign)
            .All(row => row.Coverage == C.Unresolved));
    }

    private sealed class CaptureWithoutExecution410(CharacterCreationRuleSourceCapture capture) : CharacterCreationRuleSourceCapture
    {
        public override string IntegrityDigest => capture.IntegrityDigest;
        public override string EffectivePolicyDigest => capture.EffectivePolicyDigest;
        public override byte[] ExportPrivateInputs() => capture.ExportPrivateInputs();
        public override ICharacterSourceDataResolver CreateReplayResolver() => capture.CreateReplayResolver();
        public override bool MatchesOriginalCharacter(string xml) => capture.MatchesOriginalCharacter(xml);
    }
}
