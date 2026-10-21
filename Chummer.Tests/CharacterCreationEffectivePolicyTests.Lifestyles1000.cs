using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready1000 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using D1000 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    private const string GridQuality1000 = "adaf6b3d-874a-42e5-b08b-37adf1222f23";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Lifestyles1000_real_default_full_selected_executes_locations_categories_and_grid_prompt(bool v4)
    {
        using var context = Ready1000.Create(includeGearReview: true, wrapResolver: v4 ? V4Sources905 : null);
        var prepared = PrepareProfileSourcebooks935(context, v4, books: true);
        var scopes = LifestyleScopes1000(context, prepared, SourceRequest832(prepared.Inputs));
        foreach (var report in new[] { scopes.Full, scopes.Selected })
        {
            string hidden = LifestyleRow1000(report, Guid.Empty.ToString("D"));
            Assert.IsTrue(report.Operations.Where(operation => operation.Path.StartsWith(hidden + "/", StringComparison.Ordinal))
                .All(operation => operation.Disposition == D1000.NotSelected));
            Assert.IsTrue(report.Operations.Any(operation => HasField832(operation, "freegrid")
                && operation.Disposition == D1000.Compiled));
            string quality = LifestyleRow1000(report, GridQuality1000);
            foreach (string field in new[] { "name", "category", "lp", "cost", "allowed", "source", "page" })
                Assert.IsTrue(report.Operations.Any(operation => operation.Path.StartsWith(quality + "/", StringComparison.Ordinal)
                    && HasField832(operation, field) && operation.Disposition == D1000.Compiled), field);
            foreach (string field in new[] { "bonus", "selecttext", "allowmultiple" })
                Assert.IsTrue(report.Operations.Any(operation => operation.Path.StartsWith(quality + "/", StringComparison.Ordinal)
                    && HasField832(operation, field) && operation.Disposition == D1000.Compiled), field);
            AssertLocationExecution1008(report);
            Assert.IsTrue(report.Operations.Where(operation => operation.Path.Contains("/categories[", StringComparison.Ordinal))
                .All(operation => operation.Disposition == D1000.Compiled));
            Assert.IsTrue(report.IsComplete, string.Join(',', report.Operations
                .Where(operation => operation.Disposition == D1000.Unconsumed).Select(operation => operation.Path)));
        }
    }

    [TestMethod]
    [DataRow("Low", "137", false, "2137", 3)]
    [DataRow("Low", "137", true, "2000", 1)]
    [DataRow("High,Luxury", "137", false, "2137", 1)]
    public void Lifestyles1000_live_allowed_and_cost_feed_actual_full_selected_prices(
        string allowed, string cost, bool usePoints, string expectedCost, int expectedPoints)
    {
        using var context = Ready1000.Create(includeGearReview: true, wrapResolver: V4Sources905, amendSettings: EnableLifestyleBook1000,
            prepareSource: root => AmendLifestyle1000(root, source =>
            {
                var quality = Quality1000(source, "Armory");
                quality.SetElementValue("allowed", allowed);
                quality.SetElementValue("cost", cost);
            }));
        var prepared = PrepareProfileSourcebooks935(context, v4: true, books: true);
        var authority = prepared.Inputs.Lifestyles.Authority;
        var armory = authority.QualityOptions.Single(option => option.Name == "Armory");
        Assert.IsTrue(armory.IsSelectable, string.Join(',', armory.Blockers));
        var home = LifestyleHome1000(authority) with
        {
            Qualities = [new(Guid.Parse("310cdadb-975a-41da-9dd3-82a05e9ca000"), armory.OptionId, "", usePoints, false, false)]
        };
        var scopes = LifestyleScopes1000(context, prepared, LifestyleRequest1000(prepared.Inputs, home));
        var projected = scopes.Review.Lifestyles!.Lifestyles.Single();
        Assert.AreEqual(decimal.Parse(expectedCost, CultureInfo.InvariantCulture), projected.Economics.TotalCost);
        Assert.AreEqual(expectedPoints, projected.Economics.LifestylePointsRemaining);
        foreach (var report in new[] { scopes.Full, scopes.Selected })
        {
            string row = LifestyleRow1000(report, armory.SourceId.ToString("D"));
            foreach (string field in new[] { "allowed", "cost" })
                Assert.IsTrue(report.Operations.Any(operation => operation.Path.StartsWith(row + "/", StringComparison.Ordinal)
                    && HasField832(operation, field) && operation.Disposition == D1000.Compiled), field);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Lifestyles1000_missing_sourcebook_selection_refuses_before_source_observation(bool v4)
    {
        using var context = Ready1000.Create(includeGearReview: true, wrapResolver: v4 ? V4Sources905 : null);
        var prepared = PrepareProfileSourcebooks935(context, v4, books: true);
        var request = LifestyleRequest1000(prepared.Inputs, LifestyleHome1000(prepared.Inputs.Lifestyles.Authority));
        Assert.IsNotNull(prepared.Inputs.SourcebookSelection);
        Assert.AreEqual(prepared.Inputs.SourcebookSelection, request.SourcebookSelection);
        var capture = RestoreObserved839(prepared);
        string before = CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!);

        var refused = CharacterCreationCompletionReviewService.Review(prepared.Inputs,
            request with { SourcebookSelection = null },
            ruleSources: new CharacterCreationRuleSourceExecutionContext(capture, context.Queries));

        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, refused.Outcome);
        Assert.IsNull(refused.Value);
        CollectionAssert.AreEqual(new[] { CharacterCreationCompletionReviewSchemas.RequestInvalid }, refused.Blockers.ToArray());
        Assert.AreEqual(0, capture.Invocations.Count);
        Assert.AreEqual(0, capture.Reports.Count);
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("competing")]
    [DataRow("attribute")]
    [DataRow("child")]
    [DataRow("nested-child")]
    [DataRow("numeric")]
    public void Lifestyles1000_malformed_selected_cost_sources_cannot_borrow_zero_or_default(string mutation)
    {
        using var context = Ready1000.Create(includeGearReview: true, wrapResolver: V4Sources905, amendSettings: EnableLifestyleBook1000,
            prepareSource: root => AmendLifestyle1000(root, source =>
            {
                var row = Quality1000(source, "Armory");
                if (mutation == "duplicate") row.Add(new XElement("allowed", "Low"));
                if (mutation == "competing") row.Add(new XElement("allowedfreelifestyles", new XElement("lifestyle", "Low")));
                if (mutation == "attribute") row.Element("allowed")!.SetAttributeValue("unknown1000", true);
                if (mutation == "child") row.Element("allowed")!.Add(new XElement("unknown1000", "Low"));
                if (mutation == "nested-child")
                {
                    row.Element("allowed")!.Remove();
                    row.Add(new XElement("allowedfreelifestyles", new XElement("unknown1000", "Low")));
                }
                if (mutation == "numeric") row.SetElementValue("cost", "not-a-decimal");
            }));
        var prepared = PrepareProfileSourcebooks935(context, v4: true, books: true);
        var authority = prepared.Inputs.Lifestyles.Authority;
        var armory = authority.QualityOptions.Single(option => option.Name == "Armory");
        Assert.IsFalse(armory.IsSelectable);
        var home = LifestyleHome1000(authority) with
        {
            Qualities = [new(Guid.Parse("310cdadb-975a-41da-9dd3-82a05e9ca000"), armory.OptionId, "", false, false, false)]
        };
        Assert.IsFalse(CharacterCreationLifestylesRules.TryProject(home, authority, out _, out var blockers));
        CollectionAssert.Contains(blockers.ToArray(), CharacterCreationLifestylesBlockers.UnsupportedSemantics);
        var observed = ObserveSources839(context, prepared, LifestyleRequest1000(prepared.Inputs, home));
        Assert.AreNotEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        Assert.IsFalse(observed.Review.CompleteAssessment.SatisfiesComplete);
    }

    [TestMethod]
    [DataRow("quality-unknown")]
    [DataRow("quality-namespace")]
    [DataRow("quality-duplicate")]
    [DataRow("hidden-unknown")]
    [DataRow("hidden-malformed")]
    public void Lifestyles1000_unknown_duplicate_namespace_and_hidden_malformed_operations_stay_open(string mutation)
    {
        using var context = Ready1000.Create(includeGearReview: true, wrapResolver: V4Sources905,
            prepareSource: root => AmendLifestyle1000(root, source =>
            {
                var quality = Quality1000(source, "Grid Subscription");
                var hidden = source.Element("lifestyles")!.Elements("lifestyle").Single(row => row.Element("id")!.Value == Guid.Empty.ToString("D"));
                if (mutation == "quality-unknown") quality.Add(new XElement("unknown1000", 1));
                if (mutation == "quality-namespace") quality.Add(new XElement(XName.Get("unknown1000", "urn:unknown1000"), 1));
                if (mutation == "quality-duplicate") quality.Add(new XElement("cost", 50));
                if (mutation == "hidden-unknown") hidden.Add(new XElement("unknown1000", 1));
                if (mutation == "hidden-malformed") hidden.SetElementValue("cost", "not-a-decimal");
            }));
        var prepared = PrepareProfileSourcebooks935(context, v4: true, books: true);
        var scopes = LifestyleScopes1000(context, prepared, SourceRequest832(prepared.Inputs));
        foreach (var report in new[] { scopes.Full, scopes.Selected })
        {
            var affected = report.Operations.Where(operation => mutation.EndsWith("unknown", StringComparison.Ordinal)
                    || mutation.EndsWith("namespace", StringComparison.Ordinal)
                ? operation.Path.Contains("unknown1000", StringComparison.Ordinal)
                : operation.Path.StartsWith(LifestyleRow1000(report, mutation.StartsWith("hidden", StringComparison.Ordinal)
                        ? Guid.Empty.ToString("D") : GridQuality1000) + "/cost[", StringComparison.Ordinal)).ToArray();
            Assert.IsTrue(affected.Length > 0);
            Assert.IsTrue(affected.All(operation => operation.Disposition == D1000.Unconsumed && operation.ExecutionDigest is null));
            Assert.IsFalse(report.IsComplete);
        }
    }

    [TestMethod]
    [DataRow("", "", "")]
    [DataRow("Seattle", "Redmond", "")]
    [DataRow("Unknown city 1000", "Unknown district 1000", "Unknown borough 1000")]
    public void Lifestyles1000_location_strings_preserve_text_and_execute_catalogue_selection(string city, string district, string borough)
    {
        using var context = Ready1000.Create(includeGearReview: true, wrapResolver: V4Sources905);
        var prepared = PrepareProfileSourcebooks935(context, v4: true, books: true);
        var home = LifestyleHome1000(prepared.Inputs.Lifestyles.Authority) with { City = city, District = district, Borough = borough };
        var scopes = LifestyleScopes1000(context, prepared, LifestyleRequest1000(prepared.Inputs, home));
        Assert.AreEqual(city, scopes.Review.Lifestyles!.Lifestyles.Single().Configuration.City);
        AssertLocationExecution1008(scopes.Full);
        AssertLocationExecution1008(scopes.Selected);
    }

    private static string LifestyleRow1000(CharacterCreationRuleSourceScopeResult report, string id)
    {
        var key = report.Operations.Single(operation => HasField832(operation, "id")
            && !operation.Path.Contains("/text()", StringComparison.Ordinal) && operation.ConsumedValue718 == id);
        return key.Path[..key.Path.LastIndexOf('/')];
    }

    private static XElement Quality1000(XElement source, string name) => source.Element("qualities")!.Elements("quality")
        .Single(row => row.Element("name")?.Value == name);

    private static void EnableLifestyleBook1000(XElement profile)
    {
        if (!profile.Element("books")!.Elements("book").Any(book => book.Value == "RF"))
            profile.Element("books")!.Add(new XElement("book", "RF"));
    }

    private static void AmendLifestyle1000(string root, Action<XElement> amend)
    {
        string path = Path.Combine(root, "data", "lifestyles.xml");
        var source = XDocument.Load(path);
        amend(source.Root!);
        source.Save(path);
    }

    private static CharacterCreationLifestyleConfiguration LifestyleHome1000(CharacterCreationLifestylesAuthority authority)
    {
        var low = authority.LifestyleOptions.Single(option => option.Name == "Low");
        return new(Guid.Parse("390cdadb-975a-41da-9dd3-82a05e9ca000"), low.OptionId, "Source 1000 home",
            CharacterCreationLifestyleStyleIds.Advanced, low.DefaultIncrementId, 1, 100m, 0, false, false,
            0, 0, 0, 0, "", "", "", []);
    }

    private static CharacterCreationCompletionReviewRequest LifestyleRequest1000(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationLifestyleConfiguration home)
    {
        // Admission must retain the sourcebook selection from the prepared inputs.
        var request = SourceRequest832(inputs);
        return request with { Selections = request.Selections with { Lifestyles = [home], StartingLifestyleId = home.LifestyleId } };
    }

    private static (CharacterCreationRuleSourceScopeResult Full, CharacterCreationRuleSourceScopeResult Selected,
        CharacterCreationCompletionReview Review) LifestyleScopes1000(Ready1000 context,
        CharacterCreationRuleSourcePreparation prepared, CharacterCreationCompletionReviewRequest request)
    {
        Assert.AreEqual(CharacterCreationPolicyRequirement.Complete, request.PolicyRequirement);
        string before = CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!);
        var full = ObserveSources839(context, prepared, request);
        Assert.AreEqual(true, full.Review.CompleteAssessment!.MechanicalSucceeded);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var bootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot.Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var publicCapture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, prepared.Inputs,
            request, ruleSources: prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, publicCapture.Outcome);
        Assert.IsNull(publicCapture.Value);
        // Exercise the real selected boundary with the same Complete request and
        // partial full review. A historical None witness is not Complete authority.
        var selected = RestoreObserved839(prepared);
        var method = typeof(CharacterCreationSelectedCompletionEvaluator).GetMethod("CaptureReviewed",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var selectedResult = (CharacterCreationFoundationResult<CharacterCreationSelectedCompletionWitness>)method.Invoke(null,
            [original, bootstrap, prepared.Inputs, request, full.Review, CancellationToken.None,
                new CharacterCreationRuleSourceExecutionContext(selected, context.Queries), null])!;
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, selectedResult.Outcome);
        Assert.IsNull(selectedResult.Value);
        Assert.AreEqual(1, selected.Invocations.Count, string.Join(',', selectedResult.Blockers));
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
        Assert.IsFalse(full.Review.CompleteAssessment.SatisfiesComplete);
        return (full.Capture.Reports.Single(report => report.Scope == "lifestyles"),
            selected.Reports.Single(report => report.Scope == "lifestyles"), full.Review);
    }
}
