using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready935 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using D935 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;
using P935 = Chummer.Application.Characters.CharacterCreationSourcebookSelectionProjection;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ProfileSourcebooks935_default_profile_books_execute_without_certifying_siblings(bool v4, bool unknownSibling)
    {
        using var context = Ready935.Create(includeGearReview: true, includeNonEmptyPurchases: true,
            qualityName: "Ambidextrous", wrapResolver: v4 ? V4Sources905 : null,
            amendSettings: profile =>
            {
                if (unknownSibling) profile.Add(new XElement("unknown935", 1));
            });
        var prepared = PrepareProfileSourcebooks935(context, v4, books: true);
        var inputs = prepared.Inputs;
        var policy = inputs.EffectivePolicy!;
        Assert.IsTrue(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(
            CharacterCreationBuildMethods.Priority, out var defaultProfile));
        Assert.AreEqual(defaultProfile, policy.SettingsProfileId);
        Assert.IsTrue(policy.EnabledSourcebooks.Count > 1, "Use the actual all-source default, without replacing its book list.");
        var before = CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!);
        var request = SourcebookRequest800(inputs);
        var observed = ObserveSources839(context, prepared, request);
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        AssertBooks800(inputs, observed.Review.CompleteAssessment);
        var invocation = observed.Capture.Invocations.Single();
        Assert.AreEqual(policy.PolicyDigest, invocation.PolicyDigest);
        Assert.AreNotEqual(observed.Capture.EffectivePolicyDigest, invocation.PolicyDigest,
            "A retained inventory digest is distinct from the enriched consumer-policy digest.");
        Assert.AreEqual("{}", JsonSerializer.Serialize(invocation));
        Assert.IsTrue(invocation.TryGetExecutedProfileSourcebooks935(defaultProfile,
            policy.ProfileInputsDigest, policy.BooksInputsDigest!, out var projection, out var catalogue));
        Assert.AreEqual(inputs.SourcebookSelection, catalogue);
        Assert.AreEqual(inputs.SnapshotDigest, projection!.Binding.InputsSnapshotDigest);
        Assert.AreEqual(inputs.RuleSourceCaptureDigest, projection.Binding.RuleSourceCaptureDigest);
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(request.Selections), projection.Binding.SelectionsDigest);
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(request), invocation.RequestDigest);
        Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(invocation.GraphDigest));

        var profile = observed.Capture.Reports.Single(report => report.Scope == "profile");
        AssertExecutedProfileBooks935(profile, policy.SettingsProfileId, projection);
        var qualityIdentity947 = QualityIdentity947(inputs);
        Assert.IsTrue(ReadQuality947(invocation, qualityIdentity947, out var quality947, out bool qualityNoTarget947, out var qualityDigest947));
        AssertQualityLeaf947(profile, policy.SettingsProfileId, quality947!, qualityNoTarget947, qualityDigest947!);
        AssertMetadataFromInputs956(profile, inputs, invocation);
        foreach (string field in unknownSibling ? new[] { "usepointsonbrokengroups", "unknown935" }
            : new[] { "usepointsonbrokengroups" })
        {
            string selected = SelectedProfilePath935(profile, policy.SettingsProfileId) + "/";
            var operations = profile.Operations.Where(row => row.Path.StartsWith(selected, StringComparison.Ordinal)
                && HasField832(row, field)).ToArray();
            Assert.IsTrue(operations.Length > 0, field);
            Assert.IsTrue(operations.All(row => row.Disposition == D935.Unconsumed && row.ExecutionDigest is null
                && row.OperationResult718 is null && row.ConsumedValue718 is null), field);
        }
        Assert.IsFalse(profile.IsComplete);
        AssertSource696(inputs, observed.Review.CompleteAssessment, "profile", CharacterCreationCompleteCoverage.Unresolved);
        Assert.AreEqual(CharacterCreationCompleteCoverage.Unresolved, ErrataDomain788(observed.Review.CompleteAssessment).Coverage);
        Assert.IsFalse(observed.Review.CompleteAssessment.SatisfiesComplete);
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    public void ProfileSourcebooks935_inventory_without_actual_selection_execution_cannot_cover_books()
    {
        using var context = Ready935.Create(includeGearReview: true, prepareSource: _ => { });
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: false);
        var observed = ObserveSources839(context, prepared, SourceRequest832(prepared.Inputs));
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        var policy = prepared.Inputs.EffectivePolicy!;
        Assert.AreNotEqual(observed.Capture.EffectivePolicyDigest, policy.PolicyDigest,
            "Inventory-only books still use an enriched Complete consumer policy.");
        Assert.IsTrue(policy.EnabledSourcebooks.Count > 0);
        Assert.IsNull(prepared.Inputs.SourcebookSelection);
        Assert.IsFalse(observed.Capture.Invocations.Single().TryGetExecutedProfileSourcebooks935(policy.SettingsProfileId,
            policy.ProfileInputsDigest, policy.BooksInputsDigest!, out var result, out var catalogue));
        Assert.IsNull(result);
        Assert.IsNull(catalogue);
        AssertUnexecutedProfileBooks935(observed.Capture.Reports.Single(row => row.Scope == "profile"), policy.SettingsProfileId);
    }

    [TestMethod]
    public void ProfileSourcebooks935_copied_resealed_and_mismatched_invocations_do_not_transfer_execution()
    {
        using var context = Ready935.Create(includeGearReview: true, prepareSource: _ => { });
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var inputs = prepared.Inputs;
        var policy = inputs.EffectivePolicy!;
        var request = SourcebookRequest800(inputs);
        var observed = ObserveSources839(context, prepared, request);
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        var invocation = observed.Capture.Invocations.Single();
        Assert.IsTrue(invocation.TryGetExecutedProfileSourcebooks935(policy.SettingsProfileId,
            policy.ProfileInputsDigest, policy.BooksInputsDigest!, out var executed, out _));
        string different = CharacterCreationFinalizationDigest.Compute("foreign-profile-sourcebooks935");
        Assert.IsFalse(invocation.TryGetExecutedProfileSourcebooks935(Guid.NewGuid().ToString("D"),
            policy.ProfileInputsDigest, policy.BooksInputsDigest!, out _, out _));
        Assert.IsFalse(invocation.TryGetExecutedProfileSourcebooks935(policy.SettingsProfileId,
            different, policy.BooksInputsDigest!, out _, out _));
        Assert.IsFalse(invocation.TryGetExecutedProfileSourcebooks935(policy.SettingsProfileId,
            policy.ProfileInputsDigest, different, out _, out _));

        // A valid public result can be copied and rehashed; it still cannot
        // register a capability on even an otherwise identical invocation.
        var publicCopy = JsonSerializer.Deserialize<P935.Result>(JsonSerializer.Serialize(executed))!;
        Assert.AreEqual(executed!.ProjectionDigest, publicCopy.ProjectionDigest);
        var forgedCatalogue = inputs.SourcebookSelection! with { FilterInputsDigest = different };
        var forgedInputs = inputs with { SourcebookSelection = forgedCatalogue };
        forgedInputs = forgedInputs with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(forgedInputs) };
        var forgedRequest = SourcebookRequest800(forgedInputs);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, CharacterCreationCompletionReviewService.Review(
            forgedInputs, forgedRequest, ruleSources: prepared.Execution).Outcome);

        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            prepared.Execution.ExportPrivateInputs(), inputs.RuleSourceCaptureDigest!, out var retained));
        var resolution = retained!.CreateExecutionResolution(inputs.RuleSourceExecutionSchema)!;
        Assert.IsTrue(resolution.EnablePurchaseSourceExecution());
        Assert.IsTrue(resolution.EnableCompleteSkillCapSourceExecution());
        Assert.IsTrue(resolution.EnableCandidateSourcebookSelection());
        foreach (string changed in new[] { "none", "policy", "inventory-policy", "capture", "request", "graph", "consumer", "resealed" })
        {
            var consumers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var source in policy.Sources)
                if (invocation.TryGetCompletedConsumer(source.SourceId, out var digest)) consumers.TryAdd(source.SourceId, digest);
            if (changed == "consumer") consumers["profile"] = different;
            var copy = new CharacterCreationRuleSourceScopeInvocation(
                changed == "policy" ? different : changed == "inventory-policy"
                    ? retained!.EffectivePolicyDigest : invocation.PolicyDigest,
                changed == "capture" ? different : invocation.CaptureDigest,
                changed == "request" ? different : changed == "resealed"
                    ? CharacterCreationFinalizationDigest.Compute(forgedRequest) : invocation.RequestDigest,
                changed == "graph" ? different : invocation.GraphDigest, consumers, invocation.CompiledEffects)
            {
                SourceExecution832 = invocation.SourceExecution832,
                CompiledMovement832 = invocation.CompiledMovement832,
                CreationDraft718 = invocation.CreationDraft718,
                Attributes718 = invocation.Attributes718,
                AttributePolicy718 = invocation.AttributePolicy718,
                Skills718 = invocation.Skills718,
                ResourcesExecution718 = invocation.ResourcesExecution718,
                LifestyleAuthority718 = invocation.LifestyleAuthority718,
                Lifestyles718 = invocation.Lifestyles718,
                StartingLifestyleId718 = invocation.StartingLifestyleId718,
                WorkspaceId718 = invocation.WorkspaceId718
            };
            Assert.IsFalse(copy.TryGetExecutedProfileSourcebooks935(policy.SettingsProfileId,
                policy.ProfileInputsDigest, policy.BooksInputsDigest!, out _, out _), changed);
            var reports = resolution.ExecuteRemainingSourceScopes(copy);
            // Capture mismatch refuses dispatch. A copied consumer-policy digest
            // cannot register books execution, even if it equals the inventory.
            if (changed == "capture") Assert.AreEqual(0, reports.Count, changed);
            else AssertUnexecutedProfileBooks935(reports.Single(row => row.Scope == "profile"), policy.SettingsProfileId);
        }
        // Negative attempts do not consume or poison the genuine capability.
        AssertExecutedProfileBooks935(resolution.ExecuteRemainingSourceScopes(invocation).Single(row => row.Scope == "profile"),
            policy.SettingsProfileId, executed);
    }

    [TestMethod]
    public void ProfileSourcebooks935_stale_source_and_malformed_selection_cannot_borrow_retained_execution()
    {
        using var context = Ready935.Create(includeGearReview: true, prepareSource: _ => { });
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var observed = ObserveSources839(context, prepared, SourcebookRequest800(prepared.Inputs));
        var invocation = observed.Capture.Invocations.Single();
        var policy = prepared.Inputs.EffectivePolicy!;
        var original = context.Store.Get(context.WorkspaceId).Value!;
        string sourceRoot = Path.Combine(context.Directory, "source");
        string path = Path.Combine(sourceRoot, "data", CharacterCreationBootstrapProfiles.SettingsSourceFile(policy.SettingsProfileId));
        byte[] before = File.ReadAllBytes(path);
        // A different retained capture cannot accept the old invocation, even
        // though its selected book codes and normalized catalogue are identical.
        File.AppendAllText(path, "\n<!-- changed-profile-input935 -->");
        var producer = (ICharacterCreationRuleSourceCapture)context.Resolver;
        Assert.IsTrue(producer.TryCaptureSourcebookRuleSources(original.Document.Content, out var changed));
        Assert.AreNotEqual(prepared.Inputs.RuleSourceCaptureDigest, changed!.IntegrityDigest);
        var resolution = changed.CreateExecutionResolution()!;
        Assert.IsTrue(resolution.EnablePurchaseSourceExecution());
        Assert.IsTrue(resolution.EnableCompleteSkillCapSourceExecution());
        Assert.IsTrue(resolution.EnableCandidateSourcebookSelection());
        Assert.AreEqual(0, resolution.ExecuteRemainingSourceScopes(invocation).Count);
        foreach (string kind in new[] { "duplicate-container", "duplicate-book", "unknown-sibling", "malformed-book" })
        {
            File.WriteAllBytes(path, before);
            EditProfile800(sourceRoot, profile =>
            {
                var books = profile.Element("books")!;
                if (kind == "duplicate-container") profile.Add(new XElement(books));
                else if (kind == "duplicate-book") books.Add(new XElement(books.Elements("book").First()));
                else if (kind == "unknown-sibling") books.Add(new XElement("unknown935", "SR5"));
                else books.Elements("book").First().SetAttributeValue("unknown935", 1);
            });
            Assert.IsFalse(producer.TryCaptureSourcebookRuleSources(original.Document.Content, out _), kind);
        }
        File.WriteAllBytes(path, before);
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(original),
            CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    public void ProfileSourcebooks935_V4_cold_retained_reexecution_recreates_the_same_operation_evidence()
    {
        using var context = Ready935.Create(includeGearReview: true, wrapResolver: V4Sources905, prepareSource: _ => { });
        var prepared = PrepareProfileSourcebooks935(context, v4: true, books: true);
        var request = SourcebookRequest800(prepared.Inputs);
        var before = ObserveSources839(context, prepared, request);
        var first = before.Capture.Invocations.Single();
        var policy = prepared.Inputs.EffectivePolicy!;
        Assert.IsTrue(first.TryGetExecutedProfileSourcebooks935(policy.SettingsProfileId,
            policy.ProfileInputsDigest, policy.BooksInputsDigest!, out var executed, out _));
        AssertExecutedProfileBooks935(before.Capture.Reports.Single(row => row.Scope == "profile"), policy.SettingsProfileId, executed!);
        byte[] capture = prepared.Execution.ExportPrivateInputs();
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var cold = ObserveSources839(context, prepared, request);
        Assert.IsFalse(ReferenceEquals(first, cold.Capture.Invocations.Single()));
        Assert.AreEqual(JsonSerializer.Serialize(before.Review.CompleteAssessment), JsonSerializer.Serialize(cold.Review.CompleteAssessment));
        Assert.AreEqual(JsonSerializer.Serialize(before.Capture.Reports), JsonSerializer.Serialize(cold.Capture.Reports));
        Assert.AreEqual(string.Join("\n", before.Capture.Reports.SelectMany(row => row.Operations).Select(row => row.OperationResult718)),
            string.Join("\n", cold.Capture.Reports.SelectMany(row => row.Operations).Select(row => row.OperationResult718)));
        AssertExecutedProfileBooks935(cold.Capture.Reports.Single(row => row.Scope == "profile"), policy.SettingsProfileId, executed!);
        CollectionAssert.AreEqual(capture, prepared.Execution.ExportPrivateInputs());
    }

    private static CharacterCreationRuleSourcePreparation PrepareProfileSourcebooks935(Ready935 context, bool v4, bool books)
    {
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        IOwnerBoundCharacterCreationCompletionReader reader = new OwnerBoundCharacterCreationCompletionReader(
            context.Store, owners, context.Resolver, context.Queries);
        var loaded = v4
            ? reader.LoadCompletionInputsWithInitiativeSourceExecution(owners.Capture(), new(context.WorkspaceId), includeLoadout: true, includeSourcebooks: books)
            : reader.LoadCompletionInputsWithMovementSourceExecution(owners.Capture(), new(context.WorkspaceId), includeLoadout: true, includeSourcebooks: books);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        Assert.AreEqual(0, owners.ActiveLeases);
        return loaded.Value!;
    }

    private static string SelectedProfilePath935(CharacterCreationRuleSourceScopeResult report, string profileId)
    {
        var id = report.Operations.Single(row => HasField832(row, "id") && !row.Path.Contains("/text()", StringComparison.Ordinal)
            && row.Disposition == D935.Compiled && row.ConsumedValue718 == profileId);
        return id.Path[..id.Path.LastIndexOf('/')];
    }

    private static CharacterCreationSourceOperation[] ProfileBooksOperations935(CharacterCreationRuleSourceScopeResult report, string profileId)
    {
        string prefix = SelectedProfilePath935(report, profileId) + "/books[";
        var books = report.Operations.Where(row => row.Path.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        Assert.IsTrue(books.Length > 0);
        return books;
    }

    private static void AssertUnexecutedProfileBooks935(CharacterCreationRuleSourceScopeResult report, string profileId)
    {
        Assert.IsTrue(ProfileBooksOperations935(report, profileId).All(row => row.Disposition == D935.Unconsumed
            && row.ExecutionDigest is null && row.ConsumedValue718 is null && row.OperationResult718 is null));
        Assert.IsFalse(report.IsComplete);
    }

    private static void AssertExecutedProfileBooks935(CharacterCreationRuleSourceScopeResult report, string profileId, P935.Result projection)
    {
        var operations = ProfileBooksOperations935(report, profileId);
        Assert.AreEqual(1 + 2 * projection.EnabledBooks.Count, operations.Length,
            "Account for the selected books container, every book element and every text occurrence.");
        foreach (var operation in operations)
        {
            Assert.AreEqual(D935.Compiled, operation.Disposition, operation.Path);
            Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(operation.ExecutionDigest));
            using var output = JsonDocument.Parse(operation.OperationResult718!);
            Assert.AreEqual("ProjectSourcebooks800", output.RootElement.GetProperty("Operation").GetString());
            Assert.AreEqual(projection.SelectionDigest, output.RootElement.GetProperty("SelectionDigest").GetString());
            Assert.AreEqual(projection.ProjectionDigest, output.RootElement.GetProperty("ProjectionDigest").GetString());
            if (operation.Path.Contains("/book[", StringComparison.Ordinal))
            {
                var book = output.RootElement.GetProperty("Result").Deserialize<P935.Book>()!;
                Assert.AreEqual(operation.ConsumedValue718, book.Code);
                Assert.AreEqual(projection.EnabledBooks.Single(row => row.Code == book.Code), book);
            }
            else
            {
                Assert.AreEqual(projection.Binding.BooksInputsDigest, operation.SourceDigest);
                Assert.AreEqual(projection.EnabledBooks.Count, output.RootElement.GetProperty("Result").GetArrayLength());
            }
        }
    }
}
