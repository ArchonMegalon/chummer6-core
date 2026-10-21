using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready739 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using Owners739 = Chummer.Tests.CharacterCreationAttributesServiceTests.AllocationOwners;

namespace Chummer.Tests;

// Authored only. Execute after 735 + 739; these are not strict Finish acceptance.
public sealed partial class CharacterCreationEffectivePolicyTests
{
    [DataTestMethod]
    [DataRow("limits")]
    [DataRow("block-or")]
    [DataRow("category-and")]
    public void Mechanics739_Granted_descriptor_operations_execute_real_complete_candidates(string mode)
    {
        var fixture = DescriptorFixture739();
        var candidates = new[] { fixture.First, fixture.Second, fixture.Excluded };
        foreach (string id in candidates)
        {
            // Confirm this candidate through the real Magic service before capture.
            // Complete preserves a saved spell; it does not authorize replacing it.
            using var context = CreateDescriptorContext739(fixture, mode, selectedSpellId: id);
            var owners = new Owners739();
            var prepared = Prepare695(context, context.Store, owners);
            string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
            var request = Request695(prepared.Inputs);
            var source = prepared.Inputs.Magic.SelectedTalent.GrantedQualitySources!.Single(row =>
                XElement.Parse(row.CanonicalSourceXml).Element("bonus")!.Elements("limitspelldescriptor").Any());
            Assert.IsTrue(CharacterCreationTalentQualitySourceRules.IsValidSource(source));
            var bonus = XElement.Parse(source.CanonicalSourceXml).Element("bonus")!;
            var option = prepared.Inputs.Magic.Authority.Spells.Single(row => row.Identity.SourceId == id);
            Assert.AreEqual(option.Identity, request.Selections.MagicSelections.Spells.Single());
            Assert.AreEqual(0, CharacterCreationCompletionPreservationRules.Check(prepared.Inputs, request.Selections).Count);
            // Independent native selector semantics over retained *source*
            // operations, not compiled effects or a mechanics helper's result.
            string descriptor = XElement.Parse(option.CanonicalSourceXml).Element("descriptor")?.Value ?? "";
            bool allowed = bonus.Elements("limitspelldescriptor").Any(row =>
                    descriptor.Contains(row.Value, StringComparison.Ordinal))
                && !bonus.Elements("blockspelldescriptor").Any(row =>
                    descriptor.Contains(row.Value, StringComparison.Ordinal))
                && bonus.Elements("limitspellcategory").All(row => row.Value == option.Category);
            var result = CharacterCreationCompletionReviewService.Review(
                prepared.Inputs, request, ruleSources: prepared.Execution);
            Assert.IsNotNull(result.Value, string.Join(",", result.Blockers));
            Assert.IsFalse(result.Blockers.Contains(CharacterCreationCompletionReviewSchemas.SelectionPreservationConflict),
                Diagnostic772(result.Value!));
            // MechanicalSucceeded describes the preceding allocation checks,
            // not the source-consumer verdict. A descriptor exclusion must
            // withhold mechanics coverage and Complete admission even when
            // those earlier allocation checks succeed.
            var assessment = result.Value!.CompleteAssessment!;
            Assert.AreEqual(allowed, MechanicsRows695(assessment).All(row =>
                row.Coverage == CharacterCreationCompleteCoverage.Evaluated
                && CharacterCreationFinalizationDigest.IsCanonical(row.EvaluationDigest)),
                Diagnostic772(result.Value));
            if (allowed)
            {
                AssertCompleteMechanics772(result.Value);
                AssertDescriptorOwner739(prepared.Inputs, result.Value, source);
            }
            else
            {
                AssertNoMechanics695(assessment);
                Assert.IsFalse(assessment.SatisfiesComplete, Diagnostic772(result.Value));
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
                Assert.IsTrue(result.Blockers.Contains(CharacterCreationEffectivePolicyContext.CoverageUnavailable),
                    Diagnostic772(result.Value));
            }
            Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
            Assert.AreEqual(0, owners.ActiveLeases);
        }
        // The fixture itself must distinguish OR from AND for descriptor limits.
        Assert.AreNotEqual(fixture.FirstDescriptor, fixture.SecondDescriptor);
        Assert.IsFalse(fixture.FirstDescriptor.Contains(fixture.SecondDescriptor, StringComparison.Ordinal));
        Assert.IsFalse(fixture.SecondDescriptor.Contains(fixture.FirstDescriptor, StringComparison.Ordinal));
    }

    [TestMethod]
    public void Mechanics739_Granted_descriptor_source_identity_drift_refuses_and_retained_replay_survives()
    {
        var fixture = DescriptorFixture739();
        using var context = CreateDescriptorContext739(fixture, "limits", selectedSpellId: fixture.Second);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var owners = new Owners739();
        var prepared = Prepare695(context, store, owners);
        var original = store.Get(context.WorkspaceId).Value!;
        string before = JsonSerializer.Serialize(original);
        var option = prepared.Inputs.Magic.Authority.Spells.Single(row => row.Identity.SourceId == fixture.Second);
        var request = SelectSpell739(Request695(prepared.Inputs), option.Identity);
        Assert.AreEqual(option.Identity, prepared.Inputs.Magic.Selections.Spells.Single());
        var full = ReviewDescriptor772(prepared, request);
        AssertMechanics695(full.CompleteAssessment!);
        byte[] retained = prepared.Execution.ExportPrivateInputs();
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            retained, prepared.Inputs.RuleSourceCaptureDigest!, out var restored));

        string sourceRoot = Path.Combine(context.Directory, "source");
        string path = Path.Combine(sourceRoot, "data", "qualities.xml");
        byte[] originalSource = File.ReadAllBytes(path);
        foreach (bool identityOnly in new[] { false, true })
        {
            File.WriteAllBytes(path, originalSource);
            var document = XDocument.Load(path);
            var quality = document.Root!.Element("qualities")!.Elements("quality")
                .Single(row => row.Element("id")!.Value == fixture.QualityId);
            if (identityOnly) quality.SetElementValue("id", Guid.NewGuid().ToString("D"));
            else quality.Element("bonus")!.Elements("limitspelldescriptor").First().Value = fixture.SecondDescriptor;
            document.Save(path);
            var changed = new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(sourceRoot, sourceRoot, null));
            Assert.IsTrue(changed.TryCaptureRuleSources(original.Document.Content, out var capture));
            AssertNoMechanics695(CharacterCreationCompletionReviewService.Review(
                prepared.Inputs, request, ruleSources: new(capture!, context.Queries)).Value?.CompleteAssessment);
        }
        System.IO.Directory.Delete(sourceRoot, recursive: true);
        Assert.IsNull(context.Resolver.TryCreateContext(original.Document.Content));
        var cold = ReviewDescriptor772(new(prepared.Inputs, new(restored!, context.Queries)), request);
        AssertMechanics695(cold.CompleteAssessment!);
        CollectionAssert.AreEqual(MechanicsRows695(full.CompleteAssessment!).Select(row => row.EvaluationDigest).ToArray(),
            MechanicsRows695(cold.CompleteAssessment!).Select(row => row.EvaluationDigest).ToArray());
        Assert.AreEqual(JsonSerializer.Serialize(full.UpstreamBudget!.Magic), JsonSerializer.Serialize(cold.UpstreamBudget!.Magic));
        Assert.AreEqual(before, JsonSerializer.Serialize(store.Get(context.WorkspaceId).Value));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("nested")]
    [DataRow("attribute")]
    [DataRow("unknown-neighbor")]
    [DataRow("lifestyle-cost")]
    [DataRow("contact-cost")]
    public void Mechanics764_Descriptor_grant_rejects_unresolved_or_cost_changing_source_without_writes(string variant)
    {
        var fixture = DescriptorFixture739();
        using var context = CreateDescriptorContext739(fixture, "limits", bonus =>
        {
            var descriptor = bonus.Elements("limitspelldescriptor").First();
            switch (variant)
            {
                case "empty": descriptor.Value = " "; break;
                case "nested": descriptor.Add(new XElement("choice", fixture.FirstDescriptor)); break;
                case "attribute": descriptor.SetAttributeValue("prompt", "true"); break;
                case "unknown-neighbor": bonus.Add(new XElement("unknown764", "0")); break;
                case "lifestyle-cost": bonus.Add(new XElement("lifestylecost", "10")); break;
                case "contact-cost": bonus.Add(new XElement("contactkarma", "1")); break;
                default: throw new ArgumentOutOfRangeException(nameof(variant));
            }
        });
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var owners = new Owners739();
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var loaded = reader.LoadCompletionInputsWithPurchaseSourceExecution(owners.Capture(), new(context.WorkspaceId), includeLoadout: true);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome);
        Assert.IsNull(loaded.Value);
        Assert.IsTrue(loaded.Blockers.Contains(CharacterCreationLifestylesBlockers.UnsupportedSemantics),
            string.Join(",", loaded.Blockers));
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Mechanics772_Descriptor_candidate_cannot_replace_confirmed_spell()
    {
        var fixture = DescriptorFixture739();
        using var context = CreateDescriptorContext739(fixture, "limits", selectedSpellId: fixture.First);
        var owners = new Owners739();
        var prepared = Prepare695(context, context.Store, owners);
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var baseline = Request695(prepared.Inputs);
        AssertMechanics695(ReviewDescriptor772(prepared, baseline).CompleteAssessment!);
        var other = prepared.Inputs.Magic.Authority.Spells.Single(row => row.Identity.SourceId == fixture.Second);
        var result = CharacterCreationCompletionReviewService.Review(prepared.Inputs,
            SelectSpell739(baseline, other.Identity), ruleSources: prepared.Execution);
        Assert.IsTrue(result.Blockers.Contains(CharacterCreationCompletionReviewSchemas.SelectionPreservationConflict),
            string.Join(",", result.Blockers));
        Assert.IsNotNull(result.Value);
        Assert.AreEqual(false, result.Value!.CompleteAssessment!.MechanicalSucceeded, Diagnostic772(result.Value));
        AssertNoMechanics695(result.Value.CompleteAssessment);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Mechanics772_Namespaced_descriptor_grant_blocks_skill_talent_access_without_writes()
    {
        var fixture = DescriptorFixture739();
        // Establish valid drafts first. This malformed source is rejected by
        // skills talent access, before the later Complete purchase-source reader.
        using var context = CreateDescriptorContext739(fixture, "limits", selectedSpellId: fixture.First);
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        string path = Path.Combine(context.Directory, "source", "data", "qualities.xml");
        var qualities = XDocument.Load(path);
        qualities.Root!.Element("qualities")!.Elements("quality")
            .Single(row => row.Element("id")!.Value == fixture.QualityId)
            .Element("bonus")!.Elements("limitspelldescriptor").First().Name =
                XName.Get("limitspelldescriptor", "urn:unresolved764");
        qualities.Save(path);

        var loaded = new CharacterCreationSkillsService(context.Store, context.Resolver).Load(new(context.WorkspaceId));
        Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
        Assert.IsTrue(loaded.Value!.Blockers.Contains(CharacterCreationSkillsBlockers.TalentAccessRequired),
            string.Join(",", loaded.Value.Blockers));
        Assert.IsFalse(loaded.Value.CanEdit);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    private static CharacterCreationCompletionReview ReviewDescriptor772(CharacterCreationRuleSourcePreparation prepared,
        CharacterCreationCompletionReviewRequest request)
    {
        var result = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request, ruleSources: prepared.Execution);
        Assert.IsNotNull(result.Value, string.Join(",", result.Blockers));
        AssertCompleteMechanics772(result.Value!);
        return result.Value;
    }

    private static void AssertCompleteMechanics772(CharacterCreationCompletionReview review)
    {
        string diagnostic = Diagnostic772(review);
        Assert.AreEqual(true, review.CompleteAssessment!.MechanicalSucceeded, diagnostic);
        Assert.IsTrue(MechanicsRows695(review.CompleteAssessment).All(row =>
            row.Coverage == CharacterCreationCompleteCoverage.Evaluated
            && CharacterCreationFinalizationDigest.IsCanonical(row.EvaluationDigest)), diagnostic);
        AssertMechanics695(review.CompleteAssessment);
    }

    // Test-only diagnostic: blocker codes and obligation enums, never source
    // payloads, names, identifiers, paths, hashes, or private capture contents.
    private static string Diagnostic772(CharacterCreationCompletionReview review)
        => string.Join(",", review.Blockers.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
            + ";coverage=" + string.Join(",", review.CompleteAssessment!.Obligations
                .Select(row => $"{row.Kind}/{row.Domain}/{row.Coverage}")
                .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal));

    private sealed record Descriptor739(string QualityId, string TalentValue, string First, string Second,
        string Excluded, string FirstDescriptor, string SecondDescriptor, string Category, string OtherCategory);

    private static Descriptor739 DescriptorFixture739()
    {
        using var seed = Ready739.Create(true, talentValue: "Magician");
        var prepared = Prepare695(seed, seed.Store, new Owners739());
        var spells = prepared.Inputs.Magic.Authority.Spells.Where(row => row.IsEnabled
            && row.Blockers.Count == 0 && CharacterCreationMagicResonanceFinalizationRules.HasValidOptionPayload(row)).ToArray();
        string Descriptor(CharacterCreationMagicResonanceCatalogOption row)
            => XElement.Parse(row.CanonicalSourceXml).Element("descriptor")?.Value ?? "";
        var pair = (from first in spells
                    from second in spells
                    where first.Category == second.Category && Descriptor(first).Length > 0 && Descriptor(second).Length > 0
                        && !Descriptor(first).Contains(Descriptor(second), StringComparison.Ordinal)
                        && !Descriptor(second).Contains(Descriptor(first), StringComparison.Ordinal)
                    select (first, second)).First();
        var excluded = spells.First(row => row.Category == pair.first.Category
            && !Descriptor(row).Contains(Descriptor(pair.first), StringComparison.Ordinal)
            && !Descriptor(row).Contains(Descriptor(pair.second), StringComparison.Ordinal));
        var quality = prepared.Inputs.Magic.SelectedTalent.GrantedQualitySources!.Single(row =>
            XElement.Parse(row.CanonicalSourceXml).Element("bonus")?.Element("enableattribute")?.Element("name")?.Value == "MAG");
        return new(quality.SourceId, prepared.Inputs.Magic.PrerequisiteDraft.TalentSelection!.Value,
            pair.first.Identity.SourceId, pair.second.Identity.SourceId, excluded.Identity.SourceId,
            Descriptor(pair.first), Descriptor(pair.second), pair.first.Category,
            spells.First(row => row.Category != pair.first.Category).Category);
    }

    private static Ready739 CreateDescriptorContext739(Descriptor739 fixture, string mode, Action<XElement>? amendBonus = null,
        string? selectedSpellId = null)
        => Ready739.Create(true, talentValue: fixture.TalentValue, prepareSource: root =>
        {
            // Amend real sources before Bootstrap; never rewrite a captured
            // input, witness, grant plan or original character to admit a case.
            string qualitiesPath = Path.Combine(root, "data", "qualities.xml");
            var qualities = XDocument.Load(qualitiesPath);
            var bonus = qualities.Root!.Element("qualities")!.Elements("quality")
                .Single(row => row.Element("id")!.Value == fixture.QualityId).Element("bonus")!;
            bonus.Add(new XElement("limitspelldescriptor", fixture.FirstDescriptor),
                new XElement("limitspelldescriptor", fixture.SecondDescriptor),
                new XElement("limitspellcategory", fixture.Category));
            if (mode == "block-or")
                bonus.Add(new XElement("blockspelldescriptor", fixture.SecondDescriptor),
                    new XElement("blockspelldescriptor", fixture.FirstDescriptor));
            if (mode == "category-and")
                bonus.Add(new XElement("limitspellcategory", fixture.OtherCategory));
            amendBonus?.Invoke(bonus);
            qualities.Save(qualitiesPath);
            string prioritiesPath = Path.Combine(root, "data", "priorities.xml");
            var priorities = XDocument.Load(prioritiesPath);
            foreach (var talent in priorities.Descendants("talent").Where(row => row.Element("value")?.Value == fixture.TalentValue))
                talent.SetElementValue("spells", 1);
            priorities.Save(prioritiesPath);
        }, spellSourceIds: selectedSpellId is null ? null : [selectedSpellId]);

    private static CharacterCreationCompletionReviewRequest SelectSpell739(
        CharacterCreationCompletionReviewRequest request, CharacterCreationMagicResonanceOptionIdentity identity)
        => request with { Selections = request.Selections with
        { MagicSelections = request.Selections.MagicSelections with { Spells = [identity] } } };

    private static void AssertDescriptorOwner739(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReview review, CharacterCreationTalentQualitySource source)
    {
        // Inspect the real Finish compiler, in addition to the real Complete
        // mechanics assertion above. No fabricated typed marker enters either.
        var root = XElement.Parse(inputs.OriginalCharacter!.RawCharacterXml);
        if (root.Element("qualities") is null) root.Add(new XElement("qualities"));
        var deltas = new List<CharacterCreationFinalizationDelta>();
        int order = 0;
        string seed = CharacterCreationFinalizationDigest.Compute(new { inputs.SnapshotDigest, review.Request.Selections });
        Assert.IsTrue(CharacterCreationAwakenedLegacyProjector.TryApply(root, inputs.Skills.PrerequisiteDraft,
            review.Skills!.Attributes.Attributes, review.Skills.Skills, review.Skills.SkillGroups,
            inputs.Magic, review.UpstreamBudget!.Magic, seed, deltas, ref order));
        var owner = root.Element("qualities")!.Elements("quality").Single(row => row.Element("sourceid")!.Value == source.SourceId);
        var actual = root.Element("improvements")!.Elements("improvement")
            .Where(row => row.Element("improvementttype")!.Value == "LimitSpellDescriptor").ToArray();
        var expected = XElement.Parse(source.CanonicalSourceXml).Element("bonus")!.Elements("limitspelldescriptor").ToArray();
        CollectionAssert.AreEqual(expected.Select(row => row.Value).ToArray(),
            actual.Select(row => row.Element("improvedname")!.Value).ToArray());
        Assert.IsTrue(actual.All(row => row.Element("sourcename")!.Value == owner.Element("guid")!.Value
            && row.Element("improvementsource")!.Value == "Quality"));
        Assert.IsTrue(deltas.Any(row => row.SourceAnchorIds.Contains("qualities.xml#quality:" + source.SourceId)));
    }
}
