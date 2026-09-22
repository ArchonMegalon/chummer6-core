using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    public void Full_graph_source_capture_is_exact_digest_bound_and_cannot_mutate_the_catalog()
    {
        var catalog = CreateCatalog();
        string digest = catalog.GetAuthority().RawXmlDigest;
        byte[] first = catalog.ReadSourceBytes(digest)!;
        Assert.IsNotNull(first);
        Assert.AreEqual(digest, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(first)));
        first[0] ^= 1;
        Assert.AreEqual(digest, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(catalog.ReadSourceBytes(digest)!)));
        Assert.IsNull(catalog.ReadSourceBytes("sha256:" + new string('0', 64)));
    }

    [TestMethod]
    public void Full_graph_materializes_every_occurrence_and_one_winning_quality_without_touching_runner_or_book()
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = SeedFullGraph(directory);
            var (result, preview) = BuildFullGraph(fixture.Store, fixture.Id);
            Assert.IsTrue(result.IsReady, string.Join(", ", result.Blockers) + "; preview: " + string.Join(", ", preview.FinalizationBlocked));
            var plan = result.Plan!;
            Assert.IsNotNull(preview.EffectWriteSummary);
            Assert.AreEqual(plan.Summary, preview.EffectWriteSummary);
            Assert.HasCount(6, plan.ModuleOwners);
            Assert.HasCount(7, plan.QualityXml);
            Assert.AreEqual(1, plan.GroupQualityCount);
            Assert.AreEqual(0, plan.DependentQualityCount);
            Assert.HasCount(6, plan.ModuleOwners.Select(owner => owner.QualityId).Distinct().ToArray());
            Assert.AreEqual(BountyHunterId, plan.ModuleOwners[4].SourceId);
            Assert.AreEqual(BountyHunterId, plan.ModuleOwners[5].SourceId);
            Assert.AreNotEqual(plan.ModuleOwners[4].QualityId, plan.ModuleOwners[5].QualityId);
            var qualities = plan.QualityXml.Select(XElement.Parse).ToArray();
            var winner = qualities.Single(quality => quality.Element("qualitysource")!.Value == "QualityLevelImprovement");
            Assert.AreEqual("SINner (Corporate Limited)", winner.Element("name")!.Value);
            Assert.AreEqual("Renraku", winner.Element("extra")!.Value);
            Assert.AreEqual("-15", winner.Element("bp")!.Value, "Keep source BP; do not award Karma or zero the source quality.");
            Assert.AreEqual(string.Empty, winner.Element("sourcename")!.Value);
            Assert.IsTrue(qualities.Where(quality => quality != winner).All(quality => quality.Element("qualitysource")!.Value == "LifeModule"));
            var improvements = plan.ImprovementXml.Select(XElement.Parse).ToArray();
            var levels = improvements.Where(item => item.Element("improvementttype")!.Value == "QualityLevel").ToArray();
            CollectionAssert.AreEqual(new[] { "1", "3" }, levels.Select(item => item.Element("val")!.Value).ToArray());
            Assert.IsTrue(levels.All(item => item.Element("improvedname")!.Value == "SINner"));
            CollectionAssert.AreEqual(plan.ModuleOwners.Take(2).Select(owner => owner.QualityId).ToArray(),
                levels.Select(item => item.Element("sourcename")!.Value).ToArray());
            Assert.HasCount(1, plan.PushDispositions);
            Assert.AreEqual("retired-superseded-quality-tier", plan.PushDispositions[0].Disposition);
            Assert.IsFalse(improvements.Any(item => item.Element("improvementttype")!.Value == "PushText"));
            Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(plan with { PlanDigest = string.Empty }), plan.PlanDigest);
            Assert.AreEqual(plan.ModuleOwners.Sum(owner => (decimal)owner.KarmaCost), plan.ModuleKarmaCost);
            Assert.AreEqual(CreateCatalog().GetAuthority().RawXmlDigest, plan.CatalogRawXmlDigest);
            Assert.AreEqual(preview.Binding.SourceDigest, plan.SourceDigest);
            Assert.AreNotEqual(plan.SourceDigest, plan.CatalogRawXmlDigest);
            Assert.IsFalse(preview.CanApply);
            Assert.IsFalse(preview.CharacterCreated);
            var reopened = BuildFullGraph(new FileWorkspaceStore(directory), fixture.Id).Result;
            Assert.IsTrue(reopened.IsReady);
            Assert.AreEqual(JsonSerializer.Serialize(plan), JsonSerializer.Serialize(reopened.Plan));
            CollectionAssert.AreEqual(fixture.Before, File.ReadAllBytes(WorkspacePath(directory, fixture.Id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow("bytes")]
    [DataRow("catalog-digest")]
    [DataRow("occurrence")]
    [DataRow("instruction")]
    [DataRow("metadata")]
    [DataRow("quality-prompt")]
    public void Full_graph_rechecks_source_and_compilation_instead_of_trusting_rehashed_preview(string tamper)
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = SeedFullGraph(directory);
            var result = BuildFullGraph(fixture.Store, fixture.Id, tamper).Result;
            Assert.IsFalse(result.IsReady);
            Assert.IsNull(result.Plan);
            CollectionAssert.AreEqual(fixture.Before, File.ReadAllBytes(WorkspacePath(directory, fixture.Id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Full_graph_blocks_existing_group_quality_without_deleting_or_replacing_it()
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = SeedFullGraph(directory, existingGroupQuality: true);
            var result = BuildFullGraph(fixture.Store, fixture.Id).Result;
            Assert.IsFalse(result.IsReady);
            Assert.IsNull(result.Plan);
            CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationFoundationBlockers.PendingDraftConflict);
            CollectionAssert.AreEqual(fixture.Before, File.ReadAllBytes(WorkspacePath(directory, fixture.Id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static (FileWorkspaceStore Store, CharacterWorkspaceId Id, byte[] Before) SeedFullGraph(string directory,
        bool existingGroupQuality = false, string? existingQualityXml = null, string? pendingXmlTail = null)
    {
        var id = new CharacterWorkspaceId("full-effect-graph");
        FileWorkspaceStore store;
        if (existingGroupQuality || existingQualityXml is not null || pendingXmlTail is not null)
        {
            store = new(directory);
            string? qualityXml = existingQualityXml ?? (existingGroupQuality ? "<quality>"
                + "<sourceid>9ac85feb-ae1e-4996-8514-3570d411e1d5</sourceid><name>SINner (National)</name>"
                + "<guid>00000000-0000-0000-0000-000000000001</guid><extra>Existing nation</extra>"
                + "<qualitysource>Selected</qualitysource></quality>" : null);
            string xml = CharacterXml("Elf").Replace("</character>",
                (qualityXml is null ? string.Empty : "<qualities>" + qualityXml + "</qualities>")
                + pendingXmlTail + "</character>", StringComparison.Ordinal);
            Assert.IsTrue(store.CreateWorkspaceDocument(id, new WorkspaceDocument(xml, RulesetDefaults.Sr5)).Success);
            var initial = CreateService(store);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                Confirm(initial, Preview(initial, Load(initial, id).Binding, TirModuleId, TirHumanElfVersionId, "Elf")).Outcome);
        }
        else store = SeedJourney(directory, id);
        var service = CreateService(store);
        AppendSequence(service, id, [FormativeArcologyId, TeenCorporateId, SkipEducationId, BountyHunterId, BountyHunterId]);
        var state = JourneyState(service, id);
        var request = new CharacterCreationLifeModuleFinishRequest(state.Binding, state.DraftRevision, state.DraftDigest);
        var finish = service.PreviewFinishSelection(request).Value!;
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
            service.ConfirmFinishSelection(new(request, finish.PreviewDigest, true)).Outcome);
        return (store, id, File.ReadAllBytes(WorkspacePath(directory, id)));
    }

    private static (CharacterCreationFoundationSequenceWritePlanResult Result, CharacterCreationFoundationFinalizationPreview Preview)
        BuildFullGraph(FileWorkspaceStore store, CharacterWorkspaceId id, string? tamper = null)
    {
        var service = CreateService(store);
        var first = SequencePreview(service, id);
        var prompt = first.ModuleSequence!.QualityLevels.Single().InstancePrompt!;
        var preview = service.PreviewFinalization(QualityInstanceRequest(service, id,
            new Dictionary<string, string> { [prompt.PromptId] = "Renraku" })).Value!;
        var draft = Load(service, id).PendingDraft!;
        var catalog = CreateCatalog();
        var modules = catalog.GetOptionProjections().ToArray();
        string catalogDigest = catalog.GetAuthority().RawXmlDigest;
        byte[] source = catalog.ReadSourceBytes(catalogDigest)!;
        Assert.AreNotEqual(draft.SourceDigest, catalogDigest, "The complete rule environment is not the individual catalog file.");
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays())
            .TryCreateContext(store.Get(id).Value!.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationFoundationEffectSources(out var sources));
        Assert.IsTrue(sources!.TryCreateAuthorities(out var skills, out var qualities, out var levels, out var contextDigest));
        Assert.AreEqual(preview.ModuleSequence!.SourceContextDigest, contextDigest);
        var sequence = preview.ModuleSequence!;
        if (tamper == "bytes") source[0] ^= 1;
        if (tamper == "catalog-digest") catalogDigest = draft.SourceDigest;
        if (tamper == "occurrence") sequence = sequence with { Occurrences = sequence.Occurrences.Select((row, index) =>
            index == 5 ? row with { OccurrenceId = sequence.Occurrences[4].OccurrenceId } : row).ToArray() };
        if (tamper == "instruction") sequence = sequence with { Occurrences = sequence.Occurrences.Select((row, index) =>
            index == 1 ? row with { Compilation = row.Compilation with { Effects = row.Compilation.Effects.Select((effect, i) =>
                i == 0 ? effect with { TargetId = "EDG" } : effect).ToArray() } } : row).ToArray() };
        if (tamper == "metadata") modules = modules.Select(module => module.ModuleId == BountyHunterId
            ? module with { Name = "Forged source name" } : module).ToArray();
        if (tamper == "quality-prompt") sequence = sequence with { QualityLevels = sequence.QualityLevels.Select(level =>
            level with { InstancePrompt = level.InstancePrompt! with { PromptId = "forged-instance-prompt" } }).ToArray() };
        sequence = sequence with { CompilationDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            sequence with { CompilationDigest = string.Empty }) };
        return (CharacterCreationFoundationLifeModuleQualityWritePlanner.BuildSequence(RulesetDefaults.Sr5,
            store.Get(id).Value!.Document.Content, draft, sequence, modules, source, catalogDigest, skills, qualities, levels), preview);
    }
}
