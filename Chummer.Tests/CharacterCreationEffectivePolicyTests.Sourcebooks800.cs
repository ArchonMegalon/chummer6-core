using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using Chummer.Infrastructure.Workspaces;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready800 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using Coverage800 = Chummer.Contracts.Characters.CharacterCreationCompleteCoverage;
using Kind800 = Chummer.Contracts.Characters.CharacterCreationCompleteObligationKind;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    // Scoped selection tests do not replace whole-character Complete admission.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Sourcebooks825_real_Draft_keeps_exact_sourcebook_capture_through_private_transfer(bool linked)
    {
        using var context = Ready800.Create(includeGearReview: true, includeNonEmptyPurchases: true,
            qualityName: "Ambidextrous", prepareSource: root => SelectBooks800(root, "core"),
            sourcebooksOwner800: linked ? new OwnerScope("sourcebooks825-linked") : null);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        if (context.SourcebooksOwner800 is { } owner) owners.Set(owner);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var prepared = PrepareDraft550(context, store, owners, sourcebookSelection: true);
        var receipt = Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(
            owners.Capture(), prepared.Command, CharacterCreationCampaignExecutionContext.Capture(
                prepared.Authority, CharacterCreationCampaignPhase.Apply)));
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var stored = owners.Capture().Owner.IsLocalSingleUser
            ? cold.Get(context.WorkspaceId) : cold.Get(owners.Capture().Owner, context.WorkspaceId);
        Assert.IsTrue(stored.Success, stored.Error);
        Assert.AreEqual(receipt, stored.Value!.Document.AuxiliaryState.CharacterCreationCompletionDrafts!.Single().Receipt);
        string before = CharacterCreationFinalizationDigest.Compute(stored.Value);
        var exported = cold.ExportPrivateRuleSourceClosure(owners, owners.Capture(), context.WorkspaceId,
            WorkspacePrivateRuleSourceClosureBinding.MaximumBytes);
        byte[] wire = exported.ExportPrivateBytes();
        Assert.IsFalse(System.Text.Encoding.UTF8.GetString(wire).Contains(context.Directory, StringComparison.Ordinal));
        Assert.AreEqual(1, exported.Binding.ArchiveCount);
        Assert.AreEqual(1, exported.Binding.CampaignCount);
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var imported = WorkspacePrivateRuleSourceClosure.Import(wire, exported.Binding,
            WorkspacePrivateRuleSourceClosureBinding.MaximumBytes, context.Queries);
        Assert.AreEqual(exported.Binding, imported.Binding);
        CollectionAssert.AreEqual(wire, imported.ExportPrivateBytes(), "Transfer must not strip or reseal the sourcebook envelope.");
        var reopened = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var after = owners.Capture().Owner.IsLocalSingleUser
            ? reopened.Get(context.WorkspaceId) : reopened.Get(owners.Capture().Owner, context.WorkspaceId);
        Assert.IsTrue(after.Success, after.Error);
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(after.Value!));
        Assert.AreEqual(0, owners.ActiveLeases);
        if (linked) Assert.IsFalse(reopened.Get(context.WorkspaceId).Success);
    }

    [TestMethod]
    public void Sourcebooks825_private_input_decoder_preserves_legacy_bytes_and_rejects_envelope_damage()
    {
        using var context = Ready800.Create(includeGearReview: true, prepareSource: root => SelectBooks800(root, "core"));
        var original = PrepareSources696(context);
        byte[] legacy = original.Execution.ExportPrivateInputs();
        var oldInputs = FileSystemCharacterSourceDataResolver.ImportCaptureInputs(legacy, original.Inputs.RuleSourceCaptureDigest!);
        CollectionAssert.AreEqual(legacy, oldInputs.Export());
        var current = PrepareSourcebooks800(context);
        byte[] archive = current.Execution.ExportPrivateInputs();
        var inputs = FileSystemCharacterSourceDataResolver.ImportCaptureInputs(archive, current.Inputs.RuleSourceCaptureDigest!);
        Assert.IsTrue(inputs.IsPortable);
        Assert.AreEqual(oldInputs.CharacterXml, inputs.CharacterXml);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            archive, current.Inputs.RuleSourceCaptureDigest!, out var capture));
        CollectionAssert.AreEqual(archive, capture!.ExportPrivateInputs());
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            FileSystemCharacterSourceDataResolver.ImportCaptureInputs(archive, original.Inputs.RuleSourceCaptureDigest!));
        byte[] unknown = archive.ToArray();
        unknown[0] ^= 1;
        byte[] appended = [.. archive, (byte)0];
        byte[] truncated = archive[..^1];
        foreach (byte[] malformed in new[] { unknown, appended, truncated })
        {
            // Even a coherently rehashed envelope cannot admit malformed private inputs.
            string digest = RetainedCharacterRuleSourceInputs.Digest(malformed);
            try
            {
                FileSystemCharacterSourceDataResolver.ImportCaptureInputs(malformed, digest);
                Assert.Fail("Malformed inputs must not be admitted by the private decoder.");
            }
            catch (Exception error) when (error is InvalidOperationException or IOException) { }
            Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(malformed, digest, out _));
        }
        Assert.IsNull(original.Inputs.SourcebookSelection);
        CollectionAssert.AreEqual(legacy, original.Execution.ExportPrivateInputs());
    }

    [TestMethod]
    [DataRow("core", false)]
    [DataRow("selected", false)]
    [DataRow("all", false)]
    [DataRow("core", true)]
    [DataRow("selected", true)]
    [DataRow("all", true)]
    public void Sourcebooks800_real_bootstrap_current_and_selected_cold_replay_filter_identity(string mode, bool linked)
    {
        using var context = Ready800.Create(includeGearReview: true, includeNonEmptyPurchases: true,
            qualityName: "Ambidextrous", prepareSource: root => SelectBooks800(root, mode),
            sourcebooksOwner800: linked ? new OwnerScope("sourcebooks800-linked") : null);
        var owner = context.SourcebooksOwner800 ?? OwnerScope.LocalSingleUser;
        var original = (owner.IsLocalSingleUser ? context.Store.Get(context.WorkspaceId)
            : context.Store.Get(owner, context.WorkspaceId)).Value!;
        Assert.IsNotNull(original);
        string before = CharacterCreationFinalizationDigest.Compute(original);
        var prepared = PrepareSourcebooks800(context);
        Assert.IsNotNull(prepared.Inputs.SourcebookSelection);
        var complete = SourcebookRequest800(prepared.Inputs);
        var authority = CharacterCreationCompleteAssessmentService.AssessAuthority(prepared.Inputs, complete,
            ruleSources: prepared.Execution);
        Assert.IsNotNull(authority.Value, string.Join(",", authority.Blockers));
        Assert.IsTrue(authority.Value!.Obligations.Where(row => row.Kind == Kind800.Sourcebook)
            .All(row => row.Evaluability == CharacterCreationCompleteEvaluability.CandidateDependent));
        var reviewed = ReviewSources696(prepared, complete);
        Assert.AreEqual(true, reviewed.CompleteAssessment!.MechanicalSucceeded);
        AssertBooks800(prepared.Inputs, reviewed.CompleteAssessment);
        Assert.IsFalse(reviewed.CompleteAssessment.SatisfiesComplete,
            "Book selection must not erase unresolved campaign, setting, source or mechanical obligations.");
        Assert.IsTrue(reviewed.CompleteAssessment.Obligations.Any(row => row.Kind != Kind800.Sourcebook
            && row.Coverage != Coverage800.Evaluated));

        // A separate scoped request enables selected-history proof despite unrelated
        // Complete blockers. The strict Complete request and its assertions above remain.
        var scoped = complete with { PolicyRequirement = CharacterCreationPolicyRequirement.SkillLimits };
        var ordinary = CharacterCreationCompletionReviewService.Review(prepared.Inputs, scoped, ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, ordinary.Outcome, string.Join(",", ordinary.Blockers));
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            prepared.Execution.ExportPrivateInputs(), prepared.Inputs.RuleSourceCaptureDigest!, out var retained));
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            retained!.CreateReplayResolver().TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var witness = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap!, prepared.Inputs, scoped,
            ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, witness.Outcome, string.Join(",", witness.Blockers));
        var detached = JsonSerializer.Deserialize<CharacterCreationSelectedCompletionWitness>(JsonSerializer.Serialize(witness.Value))!;
        var liveProjection = retained.CreateExecutionResolution()!.ConsumeSourcebookSelection(prepared.Inputs, scoped)!;
        Assert.IsNotNull(liveProjection);
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            prepared.Execution.ExportPrivateInputs(), prepared.Inputs.RuleSourceCaptureDigest!, out var coldCapture));
        var coldExecution = new CharacterCreationRuleSourceExecutionContext(coldCapture!, context.Queries);
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap!, detached, scoped, ruleSources: coldExecution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        var coldProjection = coldCapture!.CreateExecutionResolution()!.ConsumeSourcebookSelection(prepared.Inputs, scoped)!;
        Assert.AreEqual(liveProjection.SelectionDigest, coldProjection.SelectionDigest);
        Assert.AreEqual(liveProjection.ProjectionDigest, coldProjection.ProjectionDigest);
        Assert.AreEqual(BookEvaluations800(ordinary.Value!.CompleteAssessment!), BookEvaluations800(replay.Value!.CompleteAssessment!));
        var coldFull = CharacterCreationCompletionReviewService.Review(prepared.Inputs, scoped, ruleSources: coldExecution);
        Assert.AreEqual(BookEvaluations800(ordinary.Value.CompleteAssessment!), BookEvaluations800(coldFull.Value!.CompleteAssessment!));
        var fakeAuthority = detached.SourcebookSelection! with { CatalogueXmlDigest = CharacterCreationFinalizationDigest.Compute("forged800") };
        var fakeRequest = scoped with { SourcebookSelection = fakeAuthority };
        var forged = detached with { SourcebookSelection = fakeAuthority, ProposalDigest = CharacterCreationFinalizationDigest.Compute(fakeRequest) };
        forged = forged with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forged) };
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
            CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap!, forged, fakeRequest, ruleSources: coldExecution).Outcome,
            "Rehashing a coherent witness/request cannot replace the retained catalogue.");
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(
            (owner.IsLocalSingleUser ? context.Store.Get(context.WorkspaceId)
                : context.Store.Get(owner, context.WorkspaceId)).Value!));
        if (linked) Assert.IsFalse(context.Store.Get(context.WorkspaceId).Success, "No local partition may mask linked owner reads.");
    }

    [TestMethod]
    public void Sourcebooks800_actual_source_less_ignore_disabled_and_disabled_candidate_paths()
    {
        string disabledId = string.Empty;
        using var context = Ready800.Create(includeGearReview: true, qualityName: "Ambidextrous", prepareSource: root =>
        {
            SelectBooks800(root, "core");
            string path = Path.Combine(root, "data", "skills.xml");
            var document = XDocument.Load(path);
            var rows = document.Root!.Element("skills")!.Elements("skill").ToArray();
            var disabled = rows.Single(row => row.Element("name")!.Value == "Artisan");
            disabledId = disabled.Element("id")!.Value;
            disabled.SetElementValue("source", "RG");
            var exceptional = rows.Single(row => row.Element("name")!.Value == "Armorer");
            exceptional.SetElementValue("source", "RG");
            exceptional.SetElementValue("ignoresourcedisabled", "True");
            document.Save(path);
        });
        var prepared = PrepareSourcebooks800(context);
        var request = SourcebookRequest800(prepared.Inputs);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            prepared.Execution.ExportPrivateInputs(), prepared.Inputs.RuleSourceCaptureDigest!, out var capture));
        var projection = capture!.CreateExecutionResolution()!.ConsumeSourcebookSelection(prepared.Inputs, request)!;
        var skills = projection.Scopes.Single(scope => scope.Identity.Scope == "skills").Decisions;
        Assert.IsTrue(skills.Any(row => row.Rule == CharacterCreationSourcebookSelectionProjection.SourceRule.BuiltIn && row.Eligible));
        Assert.IsTrue(skills.Any(row => row.Rule == CharacterCreationSourcebookSelectionProjection.SourceRule.IgnoreDisabled && row.SourceBook == "RG" && row.Eligible));
        Assert.IsTrue(skills.Any(row => row.Rule == CharacterCreationSourcebookSelectionProjection.SourceRule.Required && row.SourceBook == "RG" && !row.Eligible));
        Assert.IsFalse(prepared.Inputs.Skills.Authority.ActiveSkills.Any(row => row.SourceSkillId == disabledId));
        var illegal = request with { Selections = request.Selections with
        { SkillAllocations = request.Selections.SkillAllocations.Append(new(disabledId, CharacterCreationSkillKinds.Active, 1, null, false)).ToArray() } };
        var rejected = CharacterCreationCompletionReviewService.Review(prepared.Inputs, illegal, ruleSources: prepared.Execution);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, rejected.Outcome);
        Assert.IsFalse(rejected.Value?.CompleteAssessment?.SatisfiesComplete == true);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Sourcebooks800_catalogue_and_choice_filters_use_real_global_or_selected_amendments(bool selected)
    {
        string rootPath = string.Empty, overlayPath = string.Empty;
        using var context = Ready800.Create(includeGearReview: true, qualityName: "Ambidextrous",
            prepareSource: root =>
            {
                rootPath = root;
                overlayPath = selected ? Path.Combine(root, "customdata", "sourcebooks800") : Path.Combine(root, "global800");
                string directory = selected ? overlayPath : Path.Combine(overlayPath, "data");
                Directory.CreateDirectory(directory);
                if (!selected) File.WriteAllText(Path.Combine(overlayPath, "manifest.json"),
                    "{\"id\":\"sourcebooks800\",\"mode\":\"merge-catalog\",\"enabled\":true}");
                var book = new XElement("book", new XElement("id", "158253c7-fdc0-4355-b265-e57507c57fd5"),
                    new XElement("name", "Sourcebooks800 novel catalogue entry"), new XElement("code", "NOVEL800"));
                new XDocument(new XElement("chummer", new XElement("books", book))).Save(Path.Combine(directory, "custom_books.xml"));
                var gear = XDocument.Load(Path.Combine(root, "data", "gear.xml")).Root!.Element("gears")!.Elements("gear").First();
                XElement changed;
                if (selected)
                    changed = new XElement("gear", new XElement(gear.Element("id")!),
                        new XElement("source", new XAttribute("amendoperation", "replace"), "NOVEL800"));
                else { changed = new XElement(gear); changed.SetElementValue("source", "NOVEL800"); }
                new XDocument(new XElement("chummer", new XElement("gears", changed)))
                    .Save(Path.Combine(directory, selected ? "amend_gear.xml" : "custom_gear.xml"));
                EditProfile800(root, profile =>
                {
                    profile.Element("books")!.Add(new XElement("book", "NOVEL800"));
                    if (selected) profile.SetElementValue("customdatadirectorynames", null);
                    if (selected) profile.Add(new XElement("customdatadirectorynames", new XElement("customdatadirectoryname",
                        new XElement("directoryname", "sourcebooks800"), new XElement("order", 0), new XElement("enabled", "True"))));
                });
            },
            wrapResolver: original => selected ? original : new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(rootPath, rootPath, overlayPath)));
        var prepared = PrepareSourcebooks800(context);
        var request = SourcebookRequest800(prepared.Inputs);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            prepared.Execution.ExportPrivateInputs(), prepared.Inputs.RuleSourceCaptureDigest!, out var capture));
        var result = capture!.CreateExecutionResolution()!.ConsumeSourcebookSelection(prepared.Inputs, request)!;
        Assert.IsTrue(result.EnabledBooks.Any(book => book.Code == "NOVEL800"));
        Assert.IsTrue(result.Scopes.Single(scope => scope.Identity.Scope == "gear").Decisions
            .Any(row => row.SourceBook == "NOVEL800" && row.Eligible));
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            prepared.Execution.ExportPrivateInputs(), prepared.Inputs.RuleSourceCaptureDigest!, out var cold));
        var replay = cold!.CreateExecutionResolution()!.ConsumeSourcebookSelection(prepared.Inputs, request)!;
        Assert.AreEqual(result.SelectionDigest, replay.SelectionDigest);
        Assert.AreEqual(result.ProjectionDigest, replay.ProjectionDigest);
    }

    [TestMethod]
    public void Sourcebooks800_live_catalogue_drift_fails_before_publication_without_workspace_writes()
    {
        string sourceRoot = string.Empty;
        bool armed = false;
        using var context = Ready800.Create(includeGearReview: true, prepareSource: root => sourceRoot = root,
            wrapResolver: _ => new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(sourceRoot, sourceRoot, null), path =>
                {
                    if (!armed || Path.GetFileName(path) != "books.xml") return;
                    armed = false;
                    File.AppendAllText(path, "<!-- changed after captured bytes -->");
                }));
        var original = context.Store.Get(context.WorkspaceId).Value!;
        string before = CharacterCreationFinalizationDigest.Compute(original);
        armed = true;
        Assert.IsFalse(((ICharacterCreationRuleSourceCapture)context.Resolver)
            .TryCaptureSourcebookRuleSources(original.Document.Content, out _));
        Assert.IsFalse(armed, "The test must actually reach the retained catalogue read.");
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    public void Sourcebooks800_valid_books_do_not_discharge_an_unrelated_selected_rule_failure()
    {
        using var context = Ready800.Create(includeGearReview: true, prepareSource: root => SelectBooks800(root, "core"));
        var prepared = PrepareSourcebooks800(context);
        var request = SourcebookRequest800(prepared.Inputs);
        var skill = prepared.Inputs.Skills.Authority.ActiveSkills.First(row => row.Name == "Pistols");
        var invalid = request with { Selections = request.Selections with
        { SkillAllocations = request.Selections.SkillAllocations.Append(new(skill.SourceSkillId, CharacterCreationSkillKinds.Active, 99, null, false)).ToArray() } };
        var result = CharacterCreationCompletionReviewService.Review(prepared.Inputs, invalid, ruleSources: prepared.Execution);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
        Assert.IsFalse(result.Value?.CompleteAssessment?.SatisfiesComplete == true);
    }

    [TestMethod]
    [DataRow("unknown-selection")]
    [DataRow("duplicate-selection")]
    [DataRow("malformed-selection")]
    [DataRow("duplicate-code")]
    [DataRow("duplicate-id")]
    [DataRow("malformed-catalogue")]
    [DataRow("unavailable-catalogue")]
    [DataRow("unknown-disabled-source")]
    public void Sourcebooks800_real_producer_rejects_bad_catalogue_selection_and_unknown_disabled_rows(string change)
    {
        using var context = Ready800.Create(includeGearReview: true, prepareSource: _ => { });
        var original = context.Store.Get(context.WorkspaceId).Value!;
        string root = Path.Combine(context.Directory, "source");
        string bookPath = Path.Combine(root, "data", "books.xml");
        var document = XDocument.Load(bookPath);
        var books = document.Root!.Element("books")!;
        if (change == "unavailable-catalogue") File.Delete(bookPath);
        else if (change == "unknown-disabled-source")
        {
            string path = Path.Combine(root, "data", "skills.xml");
            var source = XDocument.Load(path);
            source.Root!.Element("skills")!.Elements("skill").First().SetElementValue("source", "UNKNOWN800");
            source.Save(path);
        }
        else if (change.EndsWith("selection", StringComparison.Ordinal))
            EditProfile800(root, profile => profile.Element("books")!.Add(new XElement("book",
                change == "unknown-selection" ? "UNKNOWN800" : change == "duplicate-selection" ? "SR5" : " SR5 ")));
        else
        {
            var rows = books.Elements("book").ToArray();
            if (change == "duplicate-code") rows[1].Element("code")!.Value = rows[0].Element("code")!.Value;
            else if (change == "duplicate-id") rows[1].Element("id")!.Value = rows[0].Element("id")!.Value;
            else rows[0].Element("id")!.Value = "not-a-guid";
            document.Save(bookPath);
        }
        Assert.IsFalse(((ICharacterCreationRuleSourceCapture)context.Resolver)
            .TryCaptureSourcebookRuleSources(original.Document.Content, out _));
    }

    [TestMethod]
    public void Sourcebooks800_current_prepare_requires_private_capability_and_refuses_rehashed_authority()
    {
        using var context = Ready800.Create(includeGearReview: true, prepareSource: _ => { });
        var prepared = PrepareSourcebooks800(context);
        var request = SourcebookRequest800(prepared.Inputs);
        Assert.IsNull(CharacterCreationCompleteAssessmentService.AssessAuthority(prepared.Inputs, request).Value,
            "A serialized catalogue commitment cannot create a private execution capability.");
        var changed = prepared.Inputs.SourcebookSelection! with { FilterInputsDigest = CharacterCreationFinalizationDigest.Compute("invented800") };
        var forged = prepared.Inputs with { SourcebookSelection = changed };
        forged = forged with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(forged) };
        var forgedRequest = SourcebookRequest800(forged);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
            CharacterCreationCompletionReviewService.Review(forged, forgedRequest, ruleSources: prepared.Execution).Outcome);
        Assert.IsNull(CharacterCreationCompleteAssessmentService.AssessAuthority(forged, forgedRequest,
            ruleSources: prepared.Execution).Value);
        Assert.IsFalse(CharacterCreationCompletionReviewRules.HasValidRuleSourceExecution(request with
            { RuleSourceExecutionSchema = CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1 }));
        Assert.IsFalse(CharacterCreationCompletionReviewRules.HasValidRuleSourceExecution(request with
            { SourcebookSelection = changed with { Schema = "unrecognized800" } }));
    }

    [TestMethod]
    public void Sourcebooks800_legacy_capture_bytes_and_null_wire_remain_unchanged_and_never_gain_selection()
    {
        using var context = Ready800.Create(includeGearReview: true, prepareSource: _ => { });
        var old = PrepareSources696(context);
        byte[] bytes = old.Execution.ExportPrivateInputs();
        string json = JsonSerializer.Serialize(old.Inputs);
        Assert.IsFalse(json.Contains("SourcebookSelection", StringComparison.Ordinal));
        var current = PrepareSourcebooks800(context);
        Assert.AreNotEqual(old.Inputs.RuleSourceCaptureDigest, current.Inputs.RuleSourceCaptureDigest);
        var unchanged = PrepareSources696(context);
        CollectionAssert.AreEqual(bytes, unchanged.Execution.ExportPrivateInputs());
        Assert.AreEqual(json, JsonSerializer.Serialize(unchanged.Inputs));
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(bytes, old.Inputs.RuleSourceCaptureDigest!, out var legacy));
        Assert.IsNull(legacy!.SourcebookSelection);
        var replay = CharacterCreationCompletionReviewService.Review(old.Inputs, SourceRequest696(old.Inputs),
            ruleSources: new CharacterCreationRuleSourceExecutionContext(legacy, context.Queries));
        Assert.IsNotNull(replay.Value);
        Assert.IsTrue(replay.Value!.CompleteAssessment!.Obligations.Where(row => row.Kind == Kind800.Sourcebook)
            .All(row => row.Coverage != Coverage800.Evaluated));
        byte[] tampered = current.Execution.ExportPrivateInputs();
        tampered[^1] ^= 1;
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(tampered, current.Inputs.RuleSourceCaptureDigest!, out _));
    }

    [TestMethod]
    public void Sourcebooks800_original_budget_authority_survives_before_candidate_filtering()
    {
        using var context = Ready800.Create(includeGearReview: true,
            prepareSource: root => SelectBooks800(root, "core"));
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var live = context.Resolver.TryCreateContext(original.Document.Content)!;
        Assert.IsTrue(live.TryResolveCreationResourcesAuthority(out var originalResources));
        var prepared = PrepareSourcebooks800(context);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            prepared.Execution.ExportPrivateInputs(), prepared.Inputs.RuleSourceCaptureDigest!, out var capture));
        var resolution = capture!.CreateExecutionResolution()!;
        var retainedOriginal = resolution.Resolver.TryCreateContext(original.Document.Content)!;
        Assert.IsTrue(retainedOriginal.TryResolveCreationResourcesAuthority(out var admittedResources));
        Assert.AreEqual(originalResources.AuthorityDigest, admittedResources.AuthorityDigest,
            "Original receipt admission must not replace its catalogue with the candidate's filtered projection.");
        Assert.IsTrue(resolution.EnableCandidateSourcebookSelection());
        Assert.IsNotNull(resolution.ConsumeSourcebookSelection(prepared.Inputs, SourcebookRequest800(prepared.Inputs)));
        var legacy = PrepareSources696(context);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            legacy.Execution.ExportPrivateInputs(), legacy.Inputs.RuleSourceCaptureDigest!, out var oldCapture));
        Assert.IsFalse(oldCapture!.CreateExecutionResolution()!.EnableCandidateSourcebookSelection());
    }

    [TestMethod]
    public void Sourcebooks1039_omitted_plan_field_preserves_historical_bytes_and_digest()
    {
        // Frozen pre1035 wire shape, not an executable source capability.
        const string historical = """
            {"InputsSnapshotDigest":"legacy-inputs","Attributes":null,"Skills":null,"Magic":null,"QualityOptionIds":[],"Contacts":[],"TotalKarmaInvestment":0,"Purchases":[],"StartingLifestyleId":null,"KarmaToReserve":0,"NuyenToReserve":0,"MaximumAdditionalPurchaseSpend":0,"MaximumPurchaseEvaluations":32,"PermitNegativeQualityAdditions":false}
            """;
        var request = JsonSerializer.Deserialize<CharacterCreationCompletionPlanRequest>(historical)!;
        Assert.IsNull(request.SourcebookSelection);
        Assert.IsNull(request.ProfileSettingsExecutionSchema);
        Assert.AreEqual(historical, JsonSerializer.Serialize(request));
        using var original = JsonDocument.Parse(historical);
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(original.RootElement),
            CharacterCreationFinalizationDigest.Compute(request));
        Assert.AreEqual(historical, JsonSerializer.Serialize(request with { SourcebookSelection = null, ProfileSettingsExecutionSchema = null }));
    }

    [TestMethod]
    public void Sourcebooks1039_scoped_plan_and_session_bind_the_exact_private_capture()
    {
        using var context = Ready800.Create(includeGearReview: true, prepareSource: _ => { });
        var prepared = PrepareSourcebooks800(context);
        var request = SourcebookPlan1039(prepared.Inputs, CharacterCreationPolicyRequirement.SkillLimits);
        var before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var session = CharacterCreationCompletionPlanner.CreateSessionWithExecution(prepared.Inputs, request, prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, session.Outcome, string.Join(",", session.Blockers));
        Assert.IsNotNull(session.Value);
        var planned = session.Value!.Plan(request);
        var direct = CharacterCreationCompletionPlanner.PlanWithExecution(prepared.Inputs, request, prepared.Execution);
        Assert.IsNotNull(planned.Value, string.Join(",", planned.Blockers));
        Assert.IsNotNull(direct.Value, string.Join(",", direct.Blockers));
        Assert.AreEqual(prepared.Inputs.SourcebookSelection, planned.Value!.Request.SourcebookSelection);
        Assert.AreEqual(prepared.Inputs.SourcebookSelection, planned.Value.Review.Request.SourcebookSelection);
        Assert.AreEqual(direct.Value!.PlanDigest, planned.Value.PlanDigest);
        Assert.AreEqual(direct.Value.Review.ReviewDigest, planned.Value.Review.ReviewDigest);

        var swapped = request.SourcebookSelection! with
        { FilterInputsDigest = CharacterCreationFinalizationDigest.Compute("different-selection1039") };
        foreach (var changed in new[] { request with { SourcebookSelection = null }, request with { SourcebookSelection = swapped } })
        {
            var denied = session.Value.Plan(changed);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, denied.Outcome);
            Assert.IsNull(denied.Value);
            CollectionAssert.Contains(denied.Blockers.ToArray(), CharacterCreationCompletionPlanBlockers.PolicyConflict);
            Assert.IsNull(CharacterCreationCompletionPlanner.PlanWithExecution(prepared.Inputs, changed, prepared.Execution).Value);
            Assert.IsNull(CharacterCreationCompletionPlanner.CreateSessionWithExecution(prepared.Inputs, changed, prepared.Execution).Value);
        }
        Assert.IsNull(CharacterCreationCompletionPlanner.CreateSessionWithExecution(prepared.Inputs, request, null).Value,
            "The portable plan pins must not replace the private capture.");
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void Sourcebooks1039_Complete_admission_forwards_sources_and_keeps_unrelated_fixed_gates()
    {
        using var context = Ready800.Create(includeGearReview: true, prepareSource: _ => { });
        var prepared = PrepareSourcebooks800(context);
        var request = SourcebookPlan1039(prepared.Inputs, CharacterCreationPolicyRequirement.Complete);
        var authority = CharacterCreationCompleteAssessmentService.AssessAuthority(prepared.Inputs,
            SourcebookRequest800(prepared.Inputs), ruleSources: prepared.Execution);
        Assert.IsNotNull(authority.Value, string.Join(",", authority.Blockers));
        Assert.IsFalse(authority.Value!.MayRequestCompleteCandidate,
            "This sourcebook fixture still has unrelated fixed Complete obligations.");
        var plan = CharacterCreationCompletionPlanner.PlanWithExecution(prepared.Inputs, request, prepared.Execution);
        var session = CharacterCreationCompletionPlanner.CreateSessionWithExecution(prepared.Inputs, request, prepared.Execution);
        Assert.IsNull(plan.Value);
        Assert.IsNull(session.Value);
        CollectionAssert.AreEqual(authority.Blockers.ToArray(), plan.Blockers.ToArray());
        CollectionAssert.AreEqual(authority.Blockers.ToArray(), session.Blockers.ToArray());
        var absent = CharacterCreationCompletionPlanner.CreateSessionWithExecution(prepared.Inputs, request, null);
        Assert.IsNull(absent.Value);
        CollectionAssert.Contains(absent.Blockers.ToArray(), CharacterCreationSourcebookSelectionAuthority.Unavailable);
    }

    [TestMethod]
    public void ProfileFinish1043_real_source_opt_in_changes_evaluability_without_Complete_coverage()
    {
        using var context = Ready800.Create(includeGearReview: true, prepareSource: _ => { });
        var prepared = PrepareSourcebooks800(context);
        var before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var legacy = SourcebookRequest800(prepared.Inputs);
        Assert.IsNull(legacy.ProfileSettingsExecutionSchema);
        var absent = CharacterCreationCompleteAssessmentService.AssessAuthority(prepared.Inputs, legacy,
            ruleSources: prepared.Execution);
        Assert.IsNotNull(absent.Value);
        Assert.AreEqual(CharacterCreationCompleteEvaluability.Unsupported, absent.Value!.Obligations.Single(row =>
            row.Kind == CharacterCreationCompleteObligationKind.PolicyDomain && row.Domain == CharacterCreationPolicyDomain.OtherSettings).Evaluability);
        CollectionAssert.Contains(absent.Blockers.ToArray(), CharacterCreationCompleteAssessment.FixedAuthorityUnavailable);
        var opted = legacy with { ProfileSettingsExecutionSchema = CharacterCreationCompletionReviewSchemas.ProfileSettingsExecutionV1 };
        var authority = CharacterCreationCompleteAssessmentService.AssessAuthority(prepared.Inputs, opted,
            ruleSources: prepared.Execution);
        Assert.IsNotNull(authority.Value);
        Assert.AreEqual(CharacterCreationCompleteEvaluability.CandidateDependent, authority.Value!.Obligations.Single(row =>
            row.Kind == CharacterCreationCompleteObligationKind.PolicyDomain && row.Domain == CharacterCreationPolicyDomain.OtherSettings).Evaluability);
        CollectionAssert.AreEqual(absent.Value.Obligations.Select(row => row.Id).ToArray(),
            authority.Value.Obligations.Select(row => row.Id).ToArray());
        var reviewed = CharacterCreationCompletionReviewService.Review(prepared.Inputs, opted, ruleSources: prepared.Execution);
        Assert.IsNotNull(reviewed.Value);
        Assert.IsFalse(reviewed.Value!.CompleteAssessment!.SatisfiesComplete);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome);
        var missing = CharacterCreationCompletionReviewService.Review(prepared.Inputs, opted);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, missing.Outcome);
        var unknown = CharacterCreationCompletionReviewService.Review(prepared.Inputs,
            opted with { ProfileSettingsExecutionSchema = "unknown-profile1043" }, ruleSources: prepared.Execution);
        Assert.IsNull(unknown.Value);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void ProfileFinish1043_scoped_plan_and_session_bind_every_profile_projection()
    {
        using var context = Ready800.Create(includeGearReview: true, prepareSource: _ => { });
        var prepared = PrepareSourcebooks800(context);
        // Scoped Core projection control only. The native V5 factory requires Complete.
        var request = SourcebookPlan1039(prepared.Inputs, CharacterCreationPolicyRequirement.SkillLimits) with
        { ProfileSettingsExecutionSchema = CharacterCreationCompletionReviewSchemas.ProfileSettingsExecutionV1 };
        var before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var admitted = CharacterCreationCompletionPlanner.CreateSessionWithExecution(prepared.Inputs, request, prepared.Execution);
        Assert.IsNotNull(admitted.Value, string.Join(",", admitted.Blockers));
        var session = admitted.Value!;
        var planned = session.Plan(request);
        var direct = CharacterCreationCompletionPlanner.PlanWithExecution(prepared.Inputs, request, prepared.Execution);
        Assert.IsNotNull(planned.Value, string.Join(",", planned.Blockers));
        Assert.IsNotNull(direct.Value, string.Join(",", direct.Blockers));
        Assert.AreEqual(request.ProfileSettingsExecutionSchema, planned.Value!.Review.Request.ProfileSettingsExecutionSchema);
        Assert.AreEqual(request.ProfileSettingsExecutionSchema, direct.Value!.Review.Request.ProfileSettingsExecutionSchema);
        Assert.AreEqual(direct.Value.PlanDigest, planned.Value.PlanDigest);
        foreach (var pin in new string?[] { null, "unknown-profile1043", CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2 })
        {
            var changed = request with { ProfileSettingsExecutionSchema = pin };
            var refused = session.Plan(changed);
            Assert.IsNull(refused.Value);
            CollectionAssert.Contains(refused.Blockers.ToArray(), CharacterCreationCompletionPlanBlockers.PolicyConflict);
        }
        var legacy = request with { ProfileSettingsExecutionSchema = null };
        var cold = CharacterCreationCompletionPlanner.CreateSessionWithExecution(prepared.Inputs, legacy, prepared.Execution);
        Assert.IsNotNull(cold.Value);
        Assert.IsNull(cold.Value!.Plan(request).Value, "An absent retained pin cannot acquire profile execution.");
        Assert.IsNull(CharacterCreationCompletionPlanner.CreateSessionWithExecution(prepared.Inputs, request, null).Value);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    private static CharacterCreationCompletionPlanRequest SourcebookPlan1039(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationPolicyRequirement requirement)
    {
        var pins = SourcebookRequest800(inputs);
        return CharacterCreationCompletionPlannerTests.RetainingRequest(inputs) with
        {
            PolicyRequirement = requirement,
            EffectivePolicyDigest = pins.EffectivePolicyDigest,
            PolicyConsumptionSchema = pins.PolicyConsumptionSchema,
            CompleteAssessmentSchema = pins.CompleteAssessmentSchema,
            AdditionalConsumerExecutionSchema = pins.AdditionalConsumerExecutionSchema,
            MaximumAttributesDefaultExecutionSchema = pins.MaximumAttributesDefaultExecutionSchema,
            OriginalCharacterExecutionSchema = pins.OriginalCharacterExecutionSchema,
            RuleSourceExecutionSchema = pins.RuleSourceExecutionSchema,
            RuleSourceCaptureDigest = pins.RuleSourceCaptureDigest,
            SourcebookSelection = pins.SourcebookSelection,
            SkillKarmaPurchases = pins.Selections.SkillKarmaPurchases
        };
    }

    private static CharacterCreationRuleSourcePreparation PrepareSourcebooks800(Ready800 context)
    {
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        if (context.SourcebooksOwner800 is { } owner) owners.Set(owner);
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var loaded = reader.LoadCompletionInputsWithSourcebookSelectionExecution(owners.Capture(), new(context.WorkspaceId), includeLoadout: true);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        Assert.AreEqual(0, owners.ActiveLeases);
        return loaded.Value!;
    }

    private static CharacterCreationCompletionReviewRequest SourcebookRequest800(CharacterCreationCompletionReviewInputs inputs)
        => SourceRequest696(inputs) with { SourcebookSelection = inputs.SourcebookSelection };

    private static void AssertBooks800(CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompleteAssessment assessment)
    {
        var rows = assessment.Obligations.Where(row => row.Kind == Kind800.Sourcebook).ToArray();
        Assert.AreEqual(inputs.EffectivePolicy!.EnabledSourcebooks.Count, rows.Length);
        Assert.IsTrue(rows.Length > 0);
        Assert.IsTrue(rows.All(row => row.Coverage == Coverage800.Evaluated));
    }

    private static string BookEvaluations800(CharacterCreationCompleteAssessment assessment)
        => JsonSerializer.Serialize(assessment.Obligations.Where(row => row.Kind == Kind800.Sourcebook).ToArray());

    private static void SelectBooks800(string root, string mode)
    {
        var catalog = XDocument.Load(Path.Combine(root, "data", "books.xml"));
        string[] codes = mode == "all" ? catalog.Root!.Element("books")!.Elements("book").Select(row => row.Element("code")!.Value).ToArray()
            : mode == "selected" ? ["SR5", "RG"] : ["SR5"];
        EditProfile800(root, profile => profile.Element("books")!.ReplaceWith(new XElement("books", codes.Select(code => new XElement("book", code)))));
    }

    private static void EditProfile800(string root, Action<XElement> edit)
    {
        Assert.IsTrue(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(CharacterCreationBuildMethods.Priority, out var id));
        string path = Path.Combine(root, "data", CharacterCreationBootstrapProfiles.SettingsSourceFile(id));
        var document = XDocument.Load(path);
        edit(document.Root!.Element("settings")!.Elements("setting").Single(row => row.Element("id")!.Value == id));
        document.Save(path);
    }
}
