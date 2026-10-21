using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using K = Chummer.Contracts.Characters.CharacterCreationCompleteObligationKind;
using C = Chummer.Contracts.Characters.CharacterCreationCompleteCoverage;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority, -1, -1, -1, -1, 6, 6)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, -1, -1, -1, -1, 6, 6)]
    [DataRow(CharacterCreationBuildMethods.Priority, 6, 6, 12, 12, 6, 6)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, 6, 6, 12, 12, 6, 6)]
    [DataRow(CharacterCreationBuildMethods.Priority, 8, 7, -1, -1, 8, 7)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, 8, 7, -1, -1, 8, 7)]
    [DataRow(CharacterCreationBuildMethods.Priority, 9, 8, 7, 4, 7, 4)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, 9, 8, 7, 4, 7, 4)]
    [DataRow(CharacterCreationBuildMethods.Priority, -1, -1, 3, 2, 3, 2)]
    public void Complete340_Exact_default_configured_and_clamped_limits_execute_and_cold_replay(
        string method, int configuredActive, int configuredKnowledge, int careerActive, int careerKnowledge,
        int active, int knowledge)
    {
        using var context = ReadyContext.Create(false, buildMethod: method, rankAssignments: ConfiguredSkillRanks(),
            amendSettings: settings => SetLimits340(settings, configuredActive, configuredKnowledge, careerActive, careerKnowledge));
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var inputs = LoadConsumption(context);
        var request = Request340(inputs, active, knowledge);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        foreach (string id in LimitIds340)
            Assert.AreEqual(CharacterCreationCompleteReason.CandidateEvaluationRequired, Limit332(authority, id).Reason);
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        AssertLimits340(full.Value!.CompleteAssessment!, C.Evaluated);
        Assert.AreEqual(active, inputs.Skills.Authority.MaxSkillGroupRatingCreate);
        Assert.AreEqual(active, full.Value.Skills!.SkillGroups.Single().Rating);
        Assert.IsTrue(full.Value.CompleteAssessment!.Obligations.Where(row => row.Kind is K.Source or K.Sourcebook or K.SelectedGrants)
            .All(row => row.Coverage == C.Unresolved));
        Assert.AreEqual(8, full.Value.CompleteAssessment.Obligations.Count(row => row.Kind == K.ActiveGraph));
        AssertGraph352(full.Value.CompleteAssessment);
        Assert.IsFalse(full.Value.CompleteAssessment.SatisfiesComplete);
        if (configuredActive == -1 && careerActive == -1 && configuredKnowledge == -1 && careerKnowledge == -1)
            Assert.AreEqual("sha256:02ee808795c3ba3efe464faf38a34035f6441e3a901bed56647ebfe9900d633e", inputs.Skills.Authority.RuntimeDigest);

        AssertCold340(context, inputs, request, full.Value);
        var complete = CharacterCreationCompletionReviewService.Review(inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, complete.Outcome);
        Assert.AreEqual(CharacterCreationPolicyRequirement.Complete, complete.Value!.Request.PolicyRequirement);
        AssertLimits340(complete.Value.CompleteAssessment!, C.Evaluated);
        CollectionAssert.Contains(complete.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.CoverageUnavailable);

        var group = request.Selections.GroupAllocations.Single();
        var over = CharacterCreationCompletionReviewService.Review(inputs, request with { Selections = request.Selections with
        {
            GroupAllocations = [group with { Rating = active + 1 }]
        } });
        CollectionAssert.Contains(over.Blockers.ToArray(), CharacterCreationSkillsBlockers.GroupInvalid);
        AssertLimits340(over.Value!.CompleteAssessment!, C.Unresolved);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void Complete340_Selected_override_then_amend_controls_group_cap_without_closing_amendments()
    {
        using var context = ReadyContext.Create(false, rankAssignments: ConfiguredSkillRanks(),
            beforeDrafts: character => character.Add(new XElement("customdatadirectorynames", new XElement("directoryname", "Cap340"))),
            amendSettings: settings =>
            {
                SetLimits340(settings, 2, 2, 12, 12);
                settings.SetElementValue("customdatadirectorynames", string.Empty);
            },
            prepareSource: root =>
            {
                string id = CharacterCreationBootstrapProfiles.PrioritySettingsProfileId;
                string file = CharacterCreationBootstrapProfiles.SettingsSourceFile(id);
                string path = Path.Combine(root, "data", file);
                var document = XDocument.Load(path);
                var profile = document.Descendants("setting").Single(row => row.Element("id")?.Value == id);
                profile.Element("customdatadirectorynames")!.Add(new XElement("customdatadirectoryname",
                    new XElement("directoryname", "Cap340"), new XElement("order", 0), new XElement("enabled", true)));
                document.Save(path);
                string directory = Path.Combine(root, "customdata", "Cap340");
                Directory.CreateDirectory(directory);
                var replacement = new XElement(profile);
                SetLimits340(replacement, 9, 8, 12, 12);
                new XDocument(new XElement("chummer", new XElement("settings", replacement)))
                    .Save(Path.Combine(directory, "override_" + file));
                // Existing rows ignore custom_ additions; amend_ applies after override_.
                var ignored = new XElement(profile);
                SetLimits340(ignored, 1, 1, 1, 1);
                new XDocument(new XElement("chummer", new XElement("settings", ignored)))
                    .Save(Path.Combine(directory, "custom_" + file));
                new XDocument(new XElement("chummer", new XElement("settings", new XElement("setting",
                    new XElement("id", id), new XElement("maxskillratingcreate", 8),
                    new XElement("maxskillrating", 6), new XElement("maxknowledgeskillrating", 4)))))
                    .Save(Path.Combine(directory, "amend_" + file));
            });
        var inputs = LoadConsumption(context);
        var source = XElement.Parse(inputs.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml);
        Assert.AreEqual("8", source.Element("maxskillratingcreate")!.Value);
        Assert.AreEqual("6", source.Element("maxskillrating")!.Value);
        Assert.AreEqual(6, inputs.Skills.Authority.MaxSkillGroupRatingCreate);
        Assert.AreEqual(4, inputs.Skills.Authority.MaxKnowledgeSkillRatingCreate);
        var request = Request340(inputs, 6, 4);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        AssertLimits340(review.Value!.CompleteAssessment!, C.Evaluated);
        Assert.AreEqual(C.Unresolved, Operation332(review.Value.CompleteAssessment!,
            CharacterCreationConsumerPolicyOperation.Amendments).Coverage);
        AssertCold340(context, inputs, request, review.Value);

        string amendPath = Path.Combine(context.Directory, "source", "customdata", "Cap340", "amend_"
            + CharacterCreationBootstrapProfiles.SettingsSourceFile(inputs.EffectivePolicy!.SettingsProfileId));
        string originalAmend = File.ReadAllText(amendPath);
        foreach (string invalid in new[] { "duplicate", "six" })
        {
            var bad = XDocument.Parse(originalAmend);
            MutateLimit340(bad.Root!.Element("settings")!.Element("setting")!, "maxskillratingcreate", invalid);
            // Plain amend siblings are ordered replacements, not duplicate
            // effective settings. ADDNODE actually leaves an ambiguous cap.
            if (invalid == "duplicate")
                bad.Root.Element("settings")!.Element("setting")!.Elements("maxskillratingcreate")
                    .Last().SetAttributeValue("amendoperation", "ADDNODE");
            bad.Save(amendPath);
            var current = context.Resolver.TryCreateContext(context.Store.Get(context.WorkspaceId).Value!.Document.Content);
            Assert.IsFalse(current is not null && current.TryResolveCreationSkillsAuthority(out var authority)
                && authority.IsAuthoritative);
        }
    }

    [TestMethod]
    [DataRow("maxskillratingcreate", "duplicate")]
    [DataRow("maxskillrating", "duplicate")]
    [DataRow("maxknowledgeskillratingcreate", "duplicate")]
    [DataRow("maxknowledgeskillrating", "duplicate")]
    [DataRow("maxskillratingcreate", "six")]
    [DataRow("maxskillratingcreate", "nested")]
    [DataRow("maxskillratingcreate", "attribute")]
    [DataRow("maxskillratingcreate", " 6 ")]
    [DataRow("maxskillrating", "-1")]
    [DataRow("maxskillrating", "2147483648")]
    [DataRow("maxknowledgeskillrating", "six")]
    public void Complete340_Malformed_and_duplicate_source_cannot_become_default_authority(string field, string value)
    {
        using var context = ReadyContext.Create(false, amendSettings: settings => SetLimits340(settings, 6, 6, 12, 12));
        var inputs = LoadConsumption(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.SkillLimits) with
            { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV3 };
        string path = ConfiguredSkillSettingsPath(context, CharacterCreationBuildMethods.Priority);
        var document = XDocument.Load(path);
        var profile = ConfiguredSkillProfile(document, CharacterCreationBuildMethods.Priority);
        MutateLimit340(profile, field, value);
        document.Save(path);
        var source = context.Resolver.TryCreateContext(context.Store.Get(context.WorkspaceId).Value!.Document.Content)!;
        Assert.IsTrue(source.TryResolveCreationSkillsAuthority(out var skills));
        Assert.IsFalse(skills.IsAuthoritative);
        CollectionAssert.Contains(skills.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.SkillLimitsUnsupported);

        // A syntactically valid, resealed retained profile still cannot prove bad caps.
        var previous = inputs.Magic.Authority.MysticAdeptPowerPointPolicy!;
        var xml = XElement.Parse(previous.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        MutateLimit340(xml, field, value);
        Assert.IsTrue(CharacterCreationMysticAdeptPowerPointRules.TryCreatePolicy(previous.SettingsProfileId,
            previous.SettingsInputsDigest, xml.ToString(SaveOptions.DisableFormatting), out var forgedSource));
        var forged = WithSettingsSource340(inputs, forgedSource!);
        var assessment = CharacterCreationCompleteAssessmentService.FromFullAuthority(forged, request);
        foreach (string id in field.StartsWith("maxknowledge", StringComparison.Ordinal)
                     ? new[] { CharacterCreationEffectivePolicyContext.KnowledgeLimit }
                     : new[] { CharacterCreationEffectivePolicyContext.ActiveLimit, CharacterCreationEffectivePolicyContext.GroupLimit })
        {
            var row = Limit332(assessment, id);
            Assert.AreEqual(CharacterCreationCompleteEvaluability.Unsupported, row.Evaluability);
            Assert.AreEqual(CharacterCreationCompleteReason.SourceSemanticsUnproven, row.Reason);
        }
    }

    [TestMethod]
    public void Complete340_Equal_numeric_default_and_configured_origins_are_distinct_and_unchecked_groups_stay_unresolved()
    {
        using var context = ReadyContext.Create(false, amendSettings: settings => SetLimits340(settings, -1, -1, -1, -1));
        var inputs = LoadConsumption(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.SkillLimits) with
            { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV3 };
        var absent = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var source = inputs.Magic.Authority.MysticAdeptPowerPointPolicy!;
        var xml = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        xml.Add(new XElement("maxskillratingcreate", 6));
        Assert.IsTrue(CharacterCreationMysticAdeptPowerPointRules.TryCreatePolicy(source.SettingsProfileId,
            source.SettingsInputsDigest, xml.ToString(SaveOptions.DisableFormatting), out var explicitSource));
        // Exercise reconstruction in isolation: the full route still requires its
        // independently admitted source/snapshot and does not trust this reseal.
        var explicitAuthority = CharacterCreationCompleteAssessmentService.FromFullAuthority(
            WithSettingsSource340(inputs, explicitSource!), request);
        Assert.AreNotEqual(Limit332(absent, CharacterCreationEffectivePolicyContext.GroupLimit).AuthorityDigest,
            Limit332(explicitAuthority, CharacterCreationEffectivePolicyContext.GroupLimit).AuthorityDigest);

        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        Assert.AreEqual(C.Unresolved, Limit332(full.Value!.CompleteAssessment!, CharacterCreationEffectivePolicyContext.GroupLimit).Coverage);
        Assert.AreEqual(C.Unresolved, Limit332(full.Value.CompleteAssessment!, CharacterCreationEffectivePolicyContext.KnowledgeLimit).Coverage);
        Assert.AreEqual(C.Unresolved, full.Value.CompleteAssessment!.Obligations.Single(row => row.Kind == K.PolicyDomain
            && row.Domain == CharacterCreationPolicyDomain.SkillLimits).Coverage);
    }

    [TestMethod]
    public void Complete340_Stale_profile_missing_source_and_mismatched_caps_cannot_reuse_execution()
    {
        using var context = ReadyContext.Create(false, rankAssignments: ConfiguredSkillRanks(),
            amendSettings: settings => SetLimits340(settings, 6, 6, 12, 12));
        var inputs = LoadConsumption(context);
        var request = Request340(inputs, 6, 6);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var execution = CharacterCreationCompleteConsumerExecution.FromFull(inputs, review, authority);
        Assert.IsNotNull(execution);
        var source = inputs.Magic.Authority.MysticAdeptPowerPointPolicy!;
        Assert.IsTrue(CharacterCreationMysticAdeptPowerPointRules.TryCreatePolicy(source.SettingsProfileId,
            CharacterCreationFinalizationDigest.Compute("stale-settings"), source.CanonicalSourceXml, out var stale));
        var xml = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        xml.SetElementValue("maxskillratingcreate", 5);
        Assert.IsTrue(CharacterCreationMysticAdeptPowerPointRules.TryCreatePolicy(source.SettingsProfileId,
            source.SettingsInputsDigest, xml.ToString(SaveOptions.DisableFormatting), out var wrongCap));
        foreach (var changedSource in new[] { stale, wrongCap, null })
        {
            var changed = WithSettingsSource340(inputs, changedSource);
            var unproven = CharacterCreationCompleteAssessmentService.FromFullAuthority(changed, request);
            Assert.AreEqual(CharacterCreationCompleteEvaluability.Unsupported,
                Limit332(unproven, CharacterCreationEffectivePolicyContext.GroupLimit).Evaluability);
            Assert.AreEqual(0, execution!.EvaluationsFor(unproven).Count);
            Assert.IsNull(CharacterCreationCompleteConsumerExecution.FromFull(changed, review, unproven));
        }
        var receiptOnly = CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, review.PolicyConsumption, true);
        AssertLimits340(receiptOnly, C.Unresolved);

        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var capture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, capture.Outcome, string.Join(",", capture.Blockers));
        var witness = capture.Value!;
        var forged = witness with { CompleteAssessment = witness.CompleteAssessment! with
        {
            Obligations = witness.CompleteAssessment!.Obligations.Select(row => row.Kind == K.SkillLimit
                ? row with { AuthorityDigest = CharacterCreationFinalizationDigest.Compute("forged-cap-origin") } : row).ToArray()
        } };
        forged = forged with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forged) };
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, forged, request).Value);
        var changedWitness = witness with { Magic = witness.Magic with { MysticAdeptPowerPointPolicy = wrongCap } };
        changedWitness = changedWitness with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(changedWitness) };
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, changedWitness, request).Value);
    }

    [TestMethod]
    [DataRow(CharacterCreationCompleteAssessment.SchemaV1)]
    [DataRow(CharacterCreationCompleteAssessment.SchemaV2)]
    public void Complete340_Historical_receipts_keep_their_inventory_and_cannot_be_resealed_as_V3(string schema)
    {
        using var context = ReadyContext.Create(false, rankAssignments: ConfiguredSkillRanks(),
            amendSettings: settings => SetLimits340(settings, 6, 6, 12, 12));
        var inputs = LoadConsumption(context);
        var request = Request340(inputs, 6, 6) with { CompleteAssessmentSchema = schema };
        string policyBytes = JsonSerializer.Serialize(inputs.EffectivePolicy);
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        var group = Limit332(full.Value!.CompleteAssessment!, CharacterCreationEffectivePolicyContext.GroupLimit);
        Assert.AreEqual(C.Unresolved, group.Coverage);
        Assert.AreEqual(CharacterCreationCompleteReason.AbsentSourceDefaultUnproven, group.Reason);
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(inputs.EffectivePolicy!.Settings[2]), group.AuthorityDigest);
        string bytes = JsonSerializer.Serialize(full.Value);
        AssertCold340(context, inputs, request, full.Value);
        var fresh = CharacterCreationCompletionReviewService.Review(inputs,
            request with { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV3 });
        AssertLimits340(fresh.Value!.CompleteAssessment!, C.Evaluated);
        Assert.AreEqual(bytes, JsonSerializer.Serialize(CharacterCreationCompletionReviewService.Review(inputs, request).Value));
        Assert.AreEqual(policyBytes, JsonSerializer.Serialize(inputs.EffectivePolicy));
        var omitted = CharacterCreationCompletionReviewService.Review(inputs, request with { CompleteAssessmentSchema = null });
        Assert.IsFalse(JsonSerializer.Serialize(omitted.Value).Contains("CompleteAssessment", StringComparison.Ordinal));

        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var old = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request).Value!;
        var upgraded = request with { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV3 };
        var relabeled = old with
        {
            ProposalDigest = CharacterCreationFinalizationDigest.Compute(upgraded),
            CompleteAssessment = old.CompleteAssessment! with
            {
                Schema = CharacterCreationCompleteAssessment.SchemaV3,
                RequestDigest = CharacterCreationFinalizationDigest.Compute(upgraded)
            }
        };
        relabeled = relabeled with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(relabeled) };
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, relabeled, upgraded).Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Complete340_Fresh_owner_and_source_are_required_even_when_historical_replay_succeeds(bool duringCommit)
    {
        using var context = ReadyContext.Create(false, rankAssignments: ConfiguredSkillRanks(),
            amendSettings: settings => SetLimits340(settings, 6, 6, 12, 12));
        var inputs = LoadConsumption(context);
        var request = Request340(inputs, 6, 6);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new PolicyWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        var stamp = owners.Capture();
        var cash = service.LoadStartingCash(stamp, context.WorkspaceId, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, cash.Outcome, string.Join(",", cash.Blockers));
        var review = service.Review(stamp, context.WorkspaceId, request, new(cash.Value!.AuthorityDigest, cash.Value.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        string before = JsonSerializer.Serialize(store.Get(context.WorkspaceId).Value);
        owners.Set(new OwnerScope("cap340-other-owner"));
        Assert.IsNull(service.Apply(stamp, Command(review.Value!)).Value);
        owners.Set(OwnerScope.LocalSingleUser);
        Assert.IsNull(service.Apply(stamp, Command(review.Value!)).Value);
        // A fresh review isolates source rejection from the already rejected owner stamp.
        stamp = owners.Capture();
        cash = service.LoadStartingCash(stamp, context.WorkspaceId, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, cash.Outcome, string.Join(",", cash.Blockers));
        review = service.Review(stamp, context.WorkspaceId, request, new(cash.Value!.AuthorityDigest, cash.Value.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var original = store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var capture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, capture.Outcome, string.Join(",", capture.Blockers));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, capture.Value!, out var archive));
        string path = ConfiguredSkillSettingsPath(context, CharacterCreationBuildMethods.Priority);
        int changes = 0;
        void Change()
        {
            var document = XDocument.Load(path);
            ConfiguredSkillProfile(document, CharacterCreationBuildMethods.Priority).SetElementValue("maxskillratingcreate", 5);
            document.Save(path);
            changes++;
        }
        if (duringCommit) probe.AfterFlush = Change; else Change();
        Assert.IsNull(service.Apply(owners.Capture(), Command(review.Value!)).Value);
        Assert.AreEqual(1, changes);
        Assert.AreEqual(0, probe.Replacements);
        Assert.AreEqual(0, owners.ActiveLeases);
        Assert.AreEqual(before, JsonSerializer.Serialize(new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var cold));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, cold, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
    }

    private static readonly string[] LimitIds340 = [CharacterCreationEffectivePolicyContext.ActiveLimit,
        CharacterCreationEffectivePolicyContext.KnowledgeLimit, CharacterCreationEffectivePolicyContext.GroupLimit];

    private static void SetLimits340(XElement settings, int active, int knowledge, int careerActive, int careerKnowledge)
    {
        foreach (var (name, value) in new[] { ("maxskillratingcreate", active), ("maxknowledgeskillratingcreate", knowledge),
                     ("maxskillrating", careerActive), ("maxknowledgeskillrating", careerKnowledge) })
        {
            settings.Elements(name).Remove();
            if (value >= 0) settings.Add(new XElement(name, value));
        }
    }

    private static void MutateLimit340(XElement settings, string field, string value)
    {
        settings.Elements(field).Remove();
        if (value == "duplicate") settings.Add(new XElement(field, 6), new XElement(field, 6));
        else if (value == "nested") settings.Add(new XElement(field, new XElement("value", 6)));
        else if (value == "attribute") settings.Add(new XElement(field, new XAttribute("value", 6), 6));
        else settings.Add(new XElement(field, value));
    }

    private static CharacterCreationCompletionReviewInputs WithSettingsSource340(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationMysticAdeptPowerPointPolicy? source)
        => inputs with { Magic = inputs.Magic with { Authority = inputs.Magic.Authority with { MysticAdeptPowerPointPolicy = source } } };

    private static CharacterCreationCompletionReviewRequest Request340(CharacterCreationCompletionReviewInputs inputs, int active, int knowledge)
        => ConfiguredSkillRequest(inputs, active, knowledge) with { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV3 };

    private static void AssertLimits340(CharacterCreationCompleteAssessment assessment, C coverage)
    {
        foreach (string id in LimitIds340) Assert.AreEqual(coverage, Limit332(assessment, id).Coverage, id);
        Assert.AreEqual(coverage, assessment.Obligations.Single(row => row.Kind == K.PolicyDomain
            && row.Domain == CharacterCreationPolicyDomain.SkillLimits).Coverage);
    }

    private static void AssertCold340(ReadyContext context, CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReviewRequest request, CharacterCreationCompletionReview full)
    {
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var capture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, capture.Outcome, string.Join(",", capture.Blockers));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, capture.Value!, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var cold));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, cold, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(full.CompleteAssessment), JsonSerializer.Serialize(replay.Value!.CompleteAssessment));
    }
}
