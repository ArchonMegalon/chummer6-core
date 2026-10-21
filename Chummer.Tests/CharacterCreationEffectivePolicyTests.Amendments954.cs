using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Content;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready954 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Amendments954_real_default_current_and_retained_execution_cover_only_absence(bool retained)
    {
        using var context = Ready954.Create(includeGearReview: true, includeNonEmptyPurchases: true,
            qualityName: "Ambidextrous", prepareSource: _ => { });
        var prepared = PrepareSourcebooks800(context);
        var request = SourcebookRequest800(prepared.Inputs);
        var capture = RestoreAmendments954(prepared);
        Assert.AreNotEqual(capture.EffectivePolicyDigest, prepared.Inputs.EffectivePolicy!.PolicyDigest,
            "Raw capture and enriched policy identities remain distinct.");
        var execution = retained ? new CharacterCreationRuleSourceExecutionContext(capture, context.Queries) : prepared.Execution;
        var original = context.Store.Get(context.WorkspaceId).Value!;
        string before = CharacterCreationFinalizationDigest.Compute(original);
        var authority = CharacterCreationCompleteAssessmentService.AssessAuthority(prepared.Inputs, request, ruleSources: execution);
        Assert.IsNotNull(authority.Value);
        Assert.AreEqual(CharacterCreationCompleteEvaluability.CandidateDependent, AmendmentDomain954(authority.Value!).Evaluability);
        Assert.AreEqual(CharacterCreationCompleteCoverage.Pending, AmendmentDomain954(authority.Value!).Coverage);
        Assert.IsNull(AmendmentDomain954(authority.Value!).EvaluationDigest);

        // Exercise both source-entry paths without inventing a successful Complete
        // witness: selected replay resolves the original stored observation.
        var full = execution.Resolve(prepared.Inputs, request, default);
        var selected = execution.Resolve(original, request, default);
        Assert.IsNotNull(full);
        Assert.IsNotNull(selected);
        string? Trace(CharacterCreationCompleteRuleSourceExecution source) => source.AmendmentAbsence954For(
            prepared.Inputs.SnapshotDigest, prepared.Inputs.Skills.Binding.RawCharacterXmlDigest,
            prepared.Inputs.EffectivePolicy.PolicyDigest, CharacterCreationFinalizationDigest.Compute(request));
        Assert.IsNotNull(Trace(full!));
        Assert.AreEqual(Trace(full!), Trace(selected!));
        Assert.IsNull(full!.AmendmentAbsence954For(prepared.Inputs.SnapshotDigest,
            prepared.Inputs.Skills.Binding.RawCharacterXmlDigest, capture.EffectivePolicyDigest,
            CharacterCreationFinalizationDigest.Compute(request)), "The raw policy digest cannot replace the enriched one.");
        var changedRequest = request with { Selections = request.Selections with { KarmaToReserve = request.Selections.KarmaToReserve + 1 } };
        Assert.IsNull(full.AmendmentAbsence954For(prepared.Inputs.SnapshotDigest,
            prepared.Inputs.Skills.Binding.RawCharacterXmlDigest, prepared.Inputs.EffectivePolicy.PolicyDigest,
            CharacterCreationFinalizationDigest.Compute(changedRequest)));

        var reviewed = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request, ruleSources: execution);
        Assert.IsNotNull(reviewed.Value?.CompleteAssessment);
        var assessment = reviewed.Value!.CompleteAssessment!;
        Assert.AreEqual(true, assessment.MechanicalSucceeded);
        Assert.AreEqual(CharacterCreationCompleteCoverage.Evaluated, AmendmentDomain954(assessment).Coverage);
        Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(AmendmentDomain954(assessment).EvaluationDigest));
        Assert.AreEqual(CharacterCreationCompleteCoverage.Unresolved, ErrataDomain788(assessment).Coverage);
        Assert.IsFalse(assessment.SatisfiesComplete);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome);
        Assert.AreEqual(CharacterCreationCompleteEvaluability.Unsupported, assessment.Obligations.Single(row =>
            row.Kind == CharacterCreationCompleteObligationKind.PolicyDomain && row.Domain == CharacterCreationPolicyDomain.OtherSettings).Evaluability);
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var completeCapture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap,
            prepared.Inputs, request, ruleSources: execution);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, completeCapture.Outcome);
        Assert.IsNull(completeCapture.Value);
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Amendments954_real_global_and_selected_captures_do_not_claim_absence(bool selected)
    {
        using var context = Ready954.Create(includeGearReview: true, prepareSource: _ => { });
        string root = Path.Combine(context.Directory, "source");
        string xml = context.Store.Get(context.WorkspaceId).Value!.Document.Content;
        var catalog = new FileSystemContentOverlayCatalogService(root, root, null).GetCatalog();
        string directory = Path.Combine(root, selected ? "customdata/Amendments954" : "overlay/data");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, selected ? "amend_gear.xml" : "gear.fragment.xml"), "<chummer><gears /></chummer>");
        if (selected)
        {
            File.WriteAllText(Path.Combine(directory, "manifest.xml"),
                "<manifest><guid>15954000-0000-0000-0000-000000000001</guid><version>1.0</version></manifest>");
            EditProfile800(root, profile => profile.Element("customdatadirectorynames")!.ReplaceWith(
                new XElement("customdatadirectorynames", new XElement("customdatadirectoryname",
                    new XElement("directoryname", "Amendments954"), new XElement("enabled", "True"), new XElement("order", 0)))));
            var character = XDocument.Parse(xml);
            character.Root!.Element("customdatadirectorynames")?.Remove();
            character.Root.Add(new XElement("customdatadirectorynames", new XElement("directoryname", "Amendments954")));
            xml = character.ToString(SaveOptions.DisableFormatting);
        }
        else
        {
            File.WriteAllText(Path.Combine(root, "overlay", "manifest.json"), "{\"id\":\"amendments954\"}");
            catalog = catalog with { Overlays = [new("amendments954", "Amendments954", Path.Combine(root, "overlay"),
                directory, string.Empty, 1, true, ContentOverlayModes.MergeCatalog, "test")] };
        }
        var resolver = new FileSystemCharacterSourceDataResolver(new AmendmentCatalog954(catalog));
        Assert.IsTrue(resolver.TryCaptureRuleSources(xml, out var capture));
        var resolution = capture!.CreateExecutionResolution()!;
        Assert.IsTrue(resolution.EnablePurchaseSourceExecution());
        Assert.IsTrue(resolution.EnableCompleteSkillCapSourceExecution());
        Assert.IsTrue(resolution.Resolver.TryCreateContext(xml)!.TryResolveCreationEffectivePolicy(out var policy));
        Assert.IsTrue(policy!.Sources.Any(row => row.Kind == (selected
            ? CharacterCreationPolicySourceKind.SelectedAmend : CharacterCreationPolicySourceKind.GlobalAmend)));
        Assert.IsNull(resolution.ExecuteAmendmentAbsence954());
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            capture.ExportPrivateInputs(), capture.IntegrityDigest, out var cold));
        var coldResolution = cold!.CreateExecutionResolution()!;
        Assert.IsTrue(coldResolution.EnablePurchaseSourceExecution());
        Assert.IsTrue(coldResolution.EnableCompleteSkillCapSourceExecution());
        Assert.IsNull(coldResolution.ExecuteAmendmentAbsence954());
    }

    [TestMethod]
    [DataRow("unknown")]
    [DataRow("disabled")]
    [DataRow("duplicate")]
    [DataRow("namespace")]
    [DataRow("unresolved")]
    public void Amendments954_ignored_or_unresolved_custom_source_inputs_fail_closed(string kind)
    {
        using var context = Ready954.Create(includeGearReview: true, prepareSource: _ => { });
        string root = Path.Combine(context.Directory, "source");
        EditProfile800(root, profile =>
        {
            var container = profile.Element("customdatadirectorynames")!;
            if (kind == "duplicate") profile.Add(new XElement(container));
            else if (kind == "namespace") container.Name = XName.Get("customdatadirectorynames", "urn:unknown954");
            else if (kind == "unknown") container.Add(new XElement("unknown954", "opaque"));
            else container.Add(new XElement("customdatadirectoryname", new XElement("directoryname", "Unknown954"),
                new XElement("enabled", kind == "disabled" ? "False" : "True")));
        });
        var resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(root, root, null));
        bool captured = resolver.TryCaptureRuleSources(context.Store.Get(context.WorkspaceId).Value!.Document.Content, out var capture);
        if (kind == "unresolved") { Assert.IsFalse(captured); return; }
        Assert.IsTrue(captured);
        var resolution = capture!.CreateExecutionResolution()!;
        Assert.IsTrue(resolution.EnablePurchaseSourceExecution());
        Assert.IsTrue(resolution.EnableCompleteSkillCapSourceExecution());
        Assert.IsNull(resolution.ExecuteAmendmentAbsence954(), "An empty resolved inventory cannot hide malformed or ignored selection nodes.");
    }

    [TestMethod]
    public void Amendments954_foreign_stale_and_publicly_copied_capture_cannot_transfer_execution()
    {
        using var context = Ready954.Create(includeGearReview: true, prepareSource: _ => { });
        using var foreign = Ready954.Create(includeGearReview: true, prepareSource: _ => { });
        var prepared = PrepareSourcebooks800(context);
        var other = PrepareSourcebooks800(foreign);
        var request = SourcebookRequest800(prepared.Inputs);
        var otherRequest = SourcebookRequest800(other.Inputs);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var otherOriginal = foreign.Store.Get(foreign.WorkspaceId).Value!;
        string before = CharacterCreationFinalizationDigest.Compute(original);
        string otherBefore = CharacterCreationFinalizationDigest.Compute(otherOriginal);

        // Portable captures identify retained content, not the workspace that read it.
        // Execution must still reconstruct the exact requested original observation.
        Assert.AreNotEqual(context.WorkspaceId, foreign.WorkspaceId);
        Assert.AreNotEqual(prepared.Inputs.SnapshotDigest, other.Inputs.SnapshotDigest);
        Assert.AreEqual(prepared.Inputs.RuleSourceCaptureDigest, other.Inputs.RuleSourceCaptureDigest);
        Assert.IsTrue(prepared.Execution.ExportPrivateInputs().AsSpan().SequenceEqual(other.Execution.ExportPrivateInputs()),
            "These independent fixtures must have byte-identical private rule captures.");
        var equivalent = other.Execution.Resolve(prepared.Inputs, request, default);
        var otherEquivalent = prepared.Execution.Resolve(other.Inputs, otherRequest, default);
        Assert.IsNotNull(equivalent);
        Assert.IsNotNull(otherEquivalent);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(prepared.Inputs, equivalent!.Inputs));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(other.Inputs, otherEquivalent!.Inputs));
        Assert.IsNull(other.Execution.Resolve(prepared.Inputs, otherRequest, default));
        Assert.IsNull(prepared.Execution.Resolve(other.Inputs, request, default));
        Assert.IsNull(other.Execution.Resolve(original, otherRequest, default));
        Assert.IsNull(prepared.Execution.Resolve(otherOriginal, request, default));
        Assert.IsNull(prepared.Execution.Resolve(original with { Id = foreign.WorkspaceId }, request, default),
            "Copying identical source bytes cannot change the original workspace binding.");
        var capture = RestoreAmendments954(prepared);
        var copied = new CharacterCreationRuleSourceExecutionContext(new PublicCapture954(capture), context.Queries);
        Assert.IsNull(copied.Resolve(prepared.Inputs, request, default));
        var absent = CharacterCreationCompleteAssessmentService.FromFullAuthority(prepared.Inputs, request);
        Assert.AreEqual(CharacterCreationCompleteEvaluability.Unsupported, AmendmentDomain954(absent).Evaluability);
        var resolution = capture.CreateExecutionResolution()!;
        Assert.IsNull(resolution.ExecuteAmendmentAbsence954(), "Private source execution must be explicitly enabled.");
        var missingCapability = new MissingAmendmentResolution954(resolution);
        Assert.IsNull(missingCapability.ExecuteAmendmentAbsence954());
        var withoutAbsence = new CharacterCreationRuleSourceExecutionContext(new MissingAmendmentCapture954(capture), context.Queries)
            .Resolve(prepared.Inputs, request, default);
        Assert.IsNotNull(withoutAbsence, "The ordinary private source path must resolve independently of the new capability.");
        var unsupported = CharacterCreationCompleteAssessmentService.FromFullAuthority(prepared.Inputs, request, withoutAbsence);
        Assert.AreEqual(CharacterCreationCompleteEvaluability.Unsupported, AmendmentDomain954(unsupported).Evaluability);

        // The frozen capture remains valid for its historical original. A changed
        // live profile must invalidate fresh owner-bound input admission instead.
        EditProfile800(Path.Combine(context.Directory, "source"), profile =>
            profile.Element("name")!.Value += " changed954");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var fresh = reader.LoadCompletionInputsWithSourcebookSelectionExecution(owners.Capture(), new(context.WorkspaceId), includeLoadout: true);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, fresh.Outcome);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            prepared.Execution.ExportPrivateInputs(), prepared.Inputs.RuleSourceCaptureDigest!, out _));
        var wrong = request with { RuleSourceCaptureDigest = CharacterCreationFinalizationDigest.ComputeUtf8("stale954") };
        Assert.IsNull(prepared.Execution.Resolve(prepared.Inputs, wrong, default));
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
        Assert.AreEqual(otherBefore, CharacterCreationFinalizationDigest.Compute(foreign.Store.Get(foreign.WorkspaceId).Value!));
    }

    private static CharacterCreationCompleteObligation AmendmentDomain954(CharacterCreationCompleteAssessment assessment)
        => assessment.Obligations.Single(row => row.Kind == CharacterCreationCompleteObligationKind.PolicyDomain
            && row.Domain == CharacterCreationPolicyDomain.Amendments);

    private static CharacterCreationRuleSourceCapture RestoreAmendments954(CharacterCreationRuleSourcePreparation prepared)
    {
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(prepared.Execution.ExportPrivateInputs(),
            prepared.Inputs.RuleSourceCaptureDigest!, out var capture));
        return capture!;
    }

    private sealed class AmendmentCatalog954(ContentOverlayCatalog catalog) : IContentOverlayCatalogService
    {
        public ContentOverlayCatalog GetCatalog() => catalog;
        public IReadOnlyList<string> GetDataDirectories() => [catalog.BaseDataPath];
        public IReadOnlyList<string> GetLanguageDirectories() => [];
        public string ResolveDataFile(string fileName) => Path.Combine(catalog.BaseDataPath, fileName);
    }

    private sealed class PublicCapture954(CharacterCreationRuleSourceCapture source) : CharacterCreationRuleSourceCapture
    {
        public override string IntegrityDigest => source.IntegrityDigest;
        public override string EffectivePolicyDigest => source.EffectivePolicyDigest;
        public override CharacterCreationSourcebookSelectionAuthority? SourcebookSelection => source.SourcebookSelection;
        public override byte[] ExportPrivateInputs() => source.ExportPrivateInputs();
        public override ICharacterSourceDataResolver CreateReplayResolver() => source.CreateReplayResolver();
    }

    private sealed class MissingAmendmentResolution954(CharacterCreationRuleSourceResolution source) : CharacterCreationRuleSourceResolution
    {
        public override ICharacterSourceDataResolver Resolver => source.Resolver;
        public override bool EnablePurchaseSourceExecution() => source.EnablePurchaseSourceExecution();
        public override bool EnableCompleteSkillCapSourceExecution() => source.EnableCompleteSkillCapSourceExecution();
        public override bool EnableCandidateSourcebookSelection() => source.EnableCandidateSourcebookSelection();
        public override IReadOnlyDictionary<string, string> ConsumeSkillLimits(IReadOnlyDictionary<string, int> limits)
            => source.ConsumeSkillLimits(limits);
    }

    private sealed class MissingAmendmentCapture954(CharacterCreationRuleSourceCapture source) : CharacterCreationRuleSourceCapture
    {
        public override string IntegrityDigest => source.IntegrityDigest;
        public override string EffectivePolicyDigest => source.EffectivePolicyDigest;
        public override CharacterCreationSourcebookSelectionAuthority? SourcebookSelection => source.SourcebookSelection;
        public override byte[] ExportPrivateInputs() => source.ExportPrivateInputs();
        public override ICharacterSourceDataResolver CreateReplayResolver() => source.CreateReplayResolver();
        public override bool MatchesOriginalCharacter(string xml) => source.MatchesOriginalCharacter(xml);
        public override CharacterCreationRuleSourceResolution? CreateExecutionResolution(string? schema)
            => source.CreateExecutionResolution(schema) is { } resolution ? new MissingAmendmentResolution954(resolution) : null;
    }
}
