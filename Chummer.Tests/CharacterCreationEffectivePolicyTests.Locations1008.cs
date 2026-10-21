using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready1008 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using D1008 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;
using K1008 = Chummer.Contracts.Characters.CharacterCreationLifestyleLocationKind;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow("", "", "", K1008.Placeholder, K1008.Placeholder, K1008.Placeholder, null)]
    [DataRow("Seattle", "Redmond", "Touristville", K1008.Catalogue, K1008.Catalogue, K1008.Catalogue, "C")]
    [DataRow("Seattle", "Redmond", "Black Diamond", K1008.Catalogue, K1008.Catalogue, K1008.Custom, null)]
    [DataRow("Custom 1008", "Redmond", "Touristville", K1008.Custom, K1008.Custom, K1008.Custom, null)]
    [DataRow("Seattle", "", "", K1008.Catalogue, K1008.Absent, K1008.Absent, null)]
    public void Locations1008_full_selected_resolve_hierarchy_and_preserve_custom_text(string city, string district,
        string borough, K1008 cityKind, K1008 districtKind, K1008 boroughKind, string? security)
    {
        using var context = Ready1008.Create(includeGearReview: true, wrapResolver: V4Sources905);
        var prepared = PrepareProfileSourcebooks935(context, v4: true, books: true);
        var home = LifestyleHome1000(prepared.Inputs.Lifestyles.Authority) with { City = city, District = district, Borough = borough };
        var scopes = LifestyleScopes1000(context, prepared, LifestyleRequest1000(prepared.Inputs, home));
        var projection = scopes.Review.Lifestyles!.Lifestyles.Single();
        var location = projection.Location!;
        Assert.AreEqual(city, projection.Configuration.City);
        Assert.AreEqual(district, projection.Configuration.District);
        Assert.AreEqual(borough, projection.Configuration.Borough);
        Assert.AreEqual(cityKind, location.City.Kind);
        Assert.AreEqual(districtKind, location.District.Kind);
        Assert.AreEqual(boroughKind, location.Borough.Kind);
        Assert.AreEqual(security, location.Borough.SecurityRating);
        if (boroughKind is K1008.Custom or K1008.Absent) Assert.IsNull(location.Borough.SourceNodeDigest);
        foreach (var report in new[] { scopes.Full, scopes.Selected })
        {
            AssertLocationExecution1008(report);
            Assert.IsTrue(report.Operations.Where(operation => operation.Path.Contains("/categories[", StringComparison.Ordinal))
                .All(operation => operation.Disposition == D1008.Compiled));
            if (security is not null)
                Assert.IsTrue(report.Operations.Any(operation => HasField832(operation, "secRating")
                    && operation.ConsumedValue718 == security && operation.Disposition == D1008.Compiled));
        }
    }

    [TestMethod]
    public void Locations1008_new_source_names_ids_rating_and_category_drive_real_consumers()
    {
        Guid cityId = Guid.Parse("0cd67158-e303-48be-928f-0b60672f1008");
        using var context = Ready1008.Create(includeGearReview: true, wrapResolver: V4Sources905,
            amendSettings: EnableLifestyleBook1000, prepareSource: root => AmendLifestyle1000(root, source =>
            {
                source.Element("cities")!.Add(new XElement("city", new XElement("id", cityId), new XElement("name", "Harbor 1008"),
                    new XElement("district", new XElement("name", "North 1008"), new XElement("borough",
                        new XElement("name", "Quay 1008"), new XElement("secRating", "Local rating 1008")))));
                source.Element("categories")!.Add(new XElement("category", "Entertainment - Remote 1008"));
                Quality1000(source, "Armory").SetElementValue("category", "Entertainment - Remote 1008");
            }));
        var prepared = PrepareProfileSourcebooks935(context, v4: true, books: true);
        var authority = prepared.Inputs.Lifestyles.Authority;
        var armory = authority.QualityOptions.Single(option => option.Name == "Armory");
        Assert.IsTrue(armory.IsSelectable);
        Assert.AreEqual(CharacterCreationLifestyleQualityTypes.Entertainment, armory.QualityType);
        var empty = LifestyleHome1000(authority);
        var home = empty with { City = "Harbor 1008", District = "North 1008", Borough = "Quay 1008",
            Qualities = [new(Guid.NewGuid(), armory.OptionId, "", false, false, false)] };
        var scopes = LifestyleScopes1000(context, prepared, LifestyleRequest1000(prepared.Inputs, home));
        var actual = scopes.Review.Lifestyles!.Lifestyles.Single();
        Assert.AreEqual(cityId, actual.Location!.City.SourceId);
        Assert.AreEqual("Local rating 1008", actual.Location.Borough.SecurityRating);
        Assert.IsTrue(CharacterCreationLifestylesRules.TryProject(home with { City = "", District = "", Borough = "" }, authority,
            out var withoutLocation, out _));
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(withoutLocation.Economics),
            CharacterCreationFinalizationDigest.Compute(actual.Economics));
        AssertLocationExecution1008(scopes.Full);
        AssertLocationExecution1008(scopes.Selected);
    }

    [TestMethod]
    [DataRow("duplicate-city")]
    [DataRow("duplicate-district")]
    [DataRow("duplicate-id")]
    [DataRow("malformed-id")]
    [DataRow("empty-id")]
    [DataRow("missing-name")]
    [DataRow("duplicate-name")]
    [DataRow("name-child")]
    [DataRow("unknown")]
    [DataRow("namespace")]
    [DataRow("attribute")]
    [DataRow("hierarchy")]
    [DataRow("placeholder-child")]
    [DataRow("duplicate-rating")]
    [DataRow("rating-child")]
    [DataRow("rating-attribute")]
    [DataRow("rating-empty")]
    [DataRow("category-duplicate")]
    [DataRow("category-unknown")]
    [DataRow("category-namespace")]
    [DataRow("category-attribute")]
    [DataRow("undeclared-category")]
    [DataRow("cities-processing-instruction")]
    [DataRow("city-processing-instruction")]
    [DataRow("name-processing-instruction")]
    [DataRow("category-processing-instruction")]
    [DataRow("duplicate-cities-container")]
    [DataRow("duplicate-categories-container")]
    public void Locations1008_malformed_catalogues_fail_fresh_reader_admission(string mutation)
    {
        using var context = Ready1008.Create(includeGearReview: true, prepareSource: _ => { });
        string raw = context.Store.Get(context.WorkspaceId).Value!.Document.Content;
        string sourceRoot = Path.Combine(context.Directory, "source");
        var original = context.Resolver.TryCreateContext(raw)!;
        Assert.IsTrue(original.TryResolveCreationLifestylesAuthority(out var valid));
        Assert.IsTrue(valid.IsAuthoritative);
        AmendLifestyle1000(sourceRoot, source =>
        {
            var cities = source.Element("cities")!;
            var city = cities.Elements("city").Single(row => row.Element("name")!.Value == "Seattle");
            var district = city.Elements("district").First();
            var borough = district.Elements("borough").First();
            var category = source.Element("categories")!.Elements().First();
            if (mutation == "duplicate-city") cities.Add(new XElement(city));
            if (mutation == "duplicate-district") city.Add(new XElement(district));
            if (mutation == "duplicate-id") { city.Add(new XElement("id", "0cd67158-e303-48be-928f-0b60672f1008")); district.Add(new XElement("id", "0cd67158-e303-48be-928f-0b60672f1008")); }
            if (mutation == "malformed-id") city.Add(new XElement("id", "not-an-id"));
            if (mutation == "empty-id") city.Add(new XElement("id", Guid.Empty));
            if (mutation == "missing-name") city.Element("name")!.Remove();
            if (mutation == "duplicate-name") city.Add(new XElement(city.Element("name")!));
            if (mutation == "name-child") city.Element("name")!.Add(new XElement("effect", 1));
            if (mutation == "unknown") city.Add(new XElement("effect1008", 1));
            if (mutation == "namespace") cities.Name = XName.Get("cities", "urn:unknown1008");
            if (mutation == "attribute") city.SetAttributeValue("effect1008", 1);
            if (mutation == "hierarchy") city.Add(new XElement(borough));
            if (mutation == "placeholder-child") cities.Elements("city").First().Add(new XElement("district", new XElement("name", "Real place")));
            if (mutation == "duplicate-rating") borough.Add(new XElement(borough.Element("secRating")!));
            if (mutation == "rating-child") borough.Element("secRating")!.Add(new XElement("effect", 1));
            if (mutation == "rating-attribute") borough.Element("secRating")!.SetAttributeValue("effect", 1);
            if (mutation == "rating-empty") borough.SetElementValue("secRating", " ");
            if (mutation == "category-duplicate") category.AddAfterSelf(new XElement(category));
            if (mutation == "category-unknown") category.Value = "Unknown effect 1008";
            if (mutation == "category-namespace") category.Name = XName.Get("category", "urn:unknown1008");
            if (mutation == "category-attribute") category.SetAttributeValue("effect", 1);
            if (mutation == "undeclared-category") Quality1000(source, "Grid Subscription").SetElementValue("category", "Entertainment - Undeclared");
            if (mutation == "cities-processing-instruction") cities.Add(new XProcessingInstruction("effect1016", "unknown"));
            if (mutation == "city-processing-instruction") city.Add(new XProcessingInstruction("effect1016", "unknown"));
            if (mutation == "name-processing-instruction") city.Element("name")!.Add(new XProcessingInstruction("effect1016", "unknown"));
            if (mutation == "category-processing-instruction") category.Add(new XProcessingInstruction("effect1016", "unknown"));
            if (mutation == "duplicate-cities-container") cities.AddAfterSelf(new XElement(cities));
            if (mutation == "duplicate-categories-container") source.Element("categories")!.AddAfterSelf(new XElement(source.Element("categories")!));
        });
        // A new resolver separates schema refusal from the old reader's drift guard.
        var resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(sourceRoot, sourceRoot, null));
        var reader = resolver.TryCreateContext(raw);
        Assert.IsNotNull(reader);
        bool resolved = reader!.TryResolveCreationLifestylesAuthority(out var authority);
        Assert.IsTrue(!resolved || !authority.IsAuthoritative);
        Assert.IsFalse(CharacterCreationLifestylesRules.TryProject(LifestyleHome1000(valid), authority, out _, out _));
        Assert.AreEqual(raw, context.Store.Get(context.WorkspaceId).Value!.Document.Content);
    }

    [TestMethod]
    public void Locations1008_absent_catalogues_and_historical_authority_make_no_source_claim()
    {
        using var context = Ready1008.Create(includeGearReview: true, prepareSource: root => AmendLifestyle1000(root, source =>
        {
            source.Element("cities")!.Remove();
            source.Element("categories")!.Remove();
        }));
        var reader = context.Resolver.TryCreateContext(context.Store.Get(context.WorkspaceId).Value!.Document.Content)!;
        Assert.IsTrue(reader.TryResolveCreationLifestylesAuthority(out var authority));
        Assert.IsTrue(authority.IsAuthoritative);
        Assert.IsNull(authority.LocationCatalog);
        Assert.IsNull(authority.Categories);
        var home = LifestyleHome1000(authority) with { City = "Historical custom label", District = "District", Borough = "Borough" };
        Assert.IsTrue(CharacterCreationLifestylesRules.TryProject(home, authority, out var projection, out _));
        Assert.IsNull(projection.Location);
        Assert.AreEqual(home.City, projection.Configuration.City);
        Assert.IsFalse(JsonSerializer.Serialize(projection).Contains("\"Location\":", StringComparison.Ordinal));
        Assert.IsFalse(JsonSerializer.Serialize(authority).Contains("\"LocationCatalog\":", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Locations1008_mutable_copies_do_not_change_cached_authority_and_live_drift_refuses()
    {
        using var context = Ready1008.Create(includeGearReview: true, prepareSource: _ => { });
        var reader = context.Resolver.TryCreateContext(context.Store.Get(context.WorkspaceId).Value!.Document.Content)!;
        Assert.IsTrue(reader.TryResolveCreationLifestylesAuthority(out var original));
        var copy = JsonSerializer.Deserialize<CharacterCreationLifestylesAuthority>(JsonSerializer.Serialize(original))!;
        Assert.IsTrue(CharacterCreationLifestylesRules.IsValidAuthority(copy)); // Public data is copyable, execution is not.
        var cities = (IList<CharacterCreationLifestyleLocationNode>)copy.LocationCatalog!.Cities;
        cities[1] = cities[1] with { Name = "Changed copy" };
        Assert.IsFalse(CharacterCreationLifestylesRules.IsValidAuthority(copy));
        Assert.IsTrue(reader.TryResolveCreationLifestylesAuthority(out var fresh));
        Assert.AreEqual(original.AuthorityDigest, fresh.AuthorityDigest);
        Assert.AreEqual("Seattle", fresh.LocationCatalog!.Cities[1].Name);
        File.AppendAllText(Path.Combine(context.Directory, "source", "data", "lifestyles.xml"), "\n<!-- drift1008 -->");
        Assert.IsTrue(!reader.TryResolveCreationLifestylesAuthority(out var drifted) || !drifted.IsAuthoritative);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Locations1008_grid_text_and_allowmultiple_execute_in_quality_projection(bool allowMultiple)
    {
        using var context = Ready1008.Create(includeGearReview: true, amendSettings: EnableLifestyleBook1000,
            prepareSource: root => AmendLifestyle1000(root, source =>
            {
                if (!allowMultiple) Quality1000(source, "Grid Subscription").Element("allowmultiple")!.Remove();
            }));
        var reader = context.Resolver.TryCreateContext(context.Store.Get(context.WorkspaceId).Value!.Document.Content)!;
        Assert.IsTrue(reader.TryResolveCreationLifestylesAuthority(out var authority));
        var grid = authority.QualityOptions.Single(option => option.Name == "Grid Subscription");
        var home = LifestyleHome1000(authority) with { Qualities = [new(Guid.NewGuid(), grid.OptionId, "Private grid", false, false, false)] };
        // The source-provided local grid is also a selection, so this is two subscriptions.
        bool valid = CharacterCreationLifestylesRules.TryProject(home, authority, out _, out var blockers);
        Assert.AreEqual(allowMultiple, valid, string.Join(',', blockers));
        var blank = home with { Qualities = [new(Guid.NewGuid(), grid.OptionId, "", false, false, false)] };
        Assert.IsFalse(CharacterCreationLifestylesRules.TryProject(blank, authority, out _, out _));
    }

    [TestMethod]
    [DataRow("unknown-bonus")]
    [DataRow("prompt-attribute")]
    [DataRow("duplicate-prompt")]
    [DataRow("multiple-text")]
    public void Locations1008_unknown_grid_effects_cannot_supply_builtin_authority(string mutation)
    {
        using var context = Ready1008.Create(includeGearReview: true, prepareSource: _ => { });
        string raw = context.Store.Get(context.WorkspaceId).Value!.Document.Content;
        string sourceRoot = Path.Combine(context.Directory, "source");
        AmendLifestyle1000(sourceRoot, source =>
        {
            var grid = Quality1000(source, "Grid Subscription");
            if (mutation == "unknown-bonus") grid.Element("bonus")!.Add(new XElement("effect1008", 1));
            if (mutation == "prompt-attribute") grid.Element("bonus")!.Element("selecttext")!.SetAttributeValue("effect", 1);
            if (mutation == "duplicate-prompt") grid.Element("bonus")!.Add(new XElement("selecttext"));
            if (mutation == "multiple-text") grid.Element("allowmultiple")!.Value = "False";
        });
        var resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(sourceRoot, sourceRoot, null));
        Assert.IsTrue(resolver.TryCreateContext(raw)!.TryResolveCreationLifestylesAuthority(out var authority));
        Assert.IsFalse(authority.LifestyleOptions.Single(option => option.Name == "Low").EligibilityIsExact);
        Assert.IsFalse(CharacterCreationLifestylesRules.TryProject(LifestyleHome1000(authority), authority, out _, out _));
    }

    [TestMethod]
    [DataRow("", "", "")]
    [DataRow("Seattle", "Redmond", "Touristville")]
    [DataRow("Custom saved city", "Custom saved district", "Custom saved borough")]
    public void Locations1008_ordinary_save_reopen_preserves_location_text_and_selection(string city, string district, string borough)
    {
        using var context = Ready1008.Create(includeGearReview: true);
        var service = new CharacterCreationLifestylesService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        var home = LifestyleHome1000(state.Authority) with { City = city, District = district, Borough = borough };
        var mutation = new CharacterCreationLifestyleMutation(CharacterCreationLifestyleMutationKinds.Create, home.LifestyleId, home);
        var preview = service.Preview(new(state.Binding, mutation)).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(',', preview.Blockers));
        Assert.IsTrue(service.Confirm(new(state.Binding, mutation, preview.PreviewDigest, "locations1008-save", true)).Success);
        using var cold = context.Restart();
        var reopened = new CharacterCreationLifestylesService(cold.Store, cold.Resolver).Load(new(context.WorkspaceId)).Value!;
        Assert.IsTrue(reopened.CanEdit, string.Join(',', reopened.Blockers));
        var actual = reopened.Lifestyles.Single();
        Assert.AreEqual(city, actual.Configuration.City);
        Assert.AreEqual(district, actual.Configuration.District);
        Assert.AreEqual(borough, actual.Configuration.Borough);
        Assert.AreEqual(preview.After!.Location, actual.Location);
    }

    [TestMethod]
    public void Locations1008_identical_boroughs_under_different_selected_parents_do_not_share_selection()
    {
        using var context = Ready1008.Create(includeGearReview: true, wrapResolver: V4Sources905,
            prepareSource: root => AmendLifestyle1000(root, source =>
            {
                source.Element("cities")!.Add(new XElement("city", new XElement("name", "Twin city 1008"),
                    new XElement("district", new XElement("name", "North twin"),
                        new XElement("borough", new XElement("name", "Shared label"), new XElement("secRating", "C"))),
                    new XElement("district", new XElement("name", "South twin"),
                        new XElement("borough", new XElement("name", "Shared label"), new XElement("secRating", "C")))));
            }));
        var prepared = PrepareProfileSourcebooks935(context, v4: true, books: true);
        var home = LifestyleHome1000(prepared.Inputs.Lifestyles.Authority) with
            { City = "Twin city 1008", District = "North twin", Borough = "Shared label" };
        var other = home with { LifestyleId = Guid.NewGuid(), District = "South twin", Borough = "" };
        var request = LifestyleRequest1000(prepared.Inputs, home);
        request = request with { Selections = request.Selections with { Lifestyles = [home, other] } };
        var scopes = LifestyleScopes1000(context, prepared, request);
        foreach (var report in new[] { scopes.Full, scopes.Selected })
        {
            var south = report.Operations.Single(operation => HasField832(operation, "name")
                && !operation.Path.Contains("/text()", StringComparison.Ordinal) && operation.ConsumedValue718 == "South twin");
            string prefix = south.Path[..south.Path.LastIndexOf('/')] + "/borough[";
            var excluded = report.Operations.Where(operation => operation.Path.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            Assert.IsTrue(excluded.Length > 0);
            Assert.IsTrue(excluded.All(operation => operation.Disposition == D1008.NotSelected));
            AssertLocationExecution1008(report);
        }
    }

    [TestMethod]
    public void Locations1008_retained_full_selected_reexecution_preserves_source_selection_after_live_files_disappear()
    {
        using var context = Ready1008.Create(includeGearReview: true, wrapResolver: V4Sources905, prepareSource: _ => { });
        var prepared = PrepareProfileSourcebooks935(context, v4: true, books: true);
        var home = LifestyleHome1000(prepared.Inputs.Lifestyles.Authority) with
            { City = "Seattle", District = "Redmond", Borough = "Touristville" };
        var request = LifestyleRequest1000(prepared.Inputs, home);
        var before = LifestyleScopes1000(context, prepared, request);
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var after = LifestyleScopes1000(context, prepared, request);
        Assert.AreEqual(JsonSerializer.Serialize(before.Full), JsonSerializer.Serialize(after.Full));
        Assert.AreEqual(JsonSerializer.Serialize(before.Selected), JsonSerializer.Serialize(after.Selected));
    }

    [TestMethod]
    public void Locations1008_unknown_selected_effect_refuses_actual_Complete_witness_capture()
    {
        using var context = Ready1008.Create(includeGearReview: true, wrapResolver: V4Sources905,
            prepareSource: root => AmendLifestyle1000(root, source =>
                Quality1000(source, "Grid Subscription").Add(new XElement("unknown1008", 1))));
        var prepared = PrepareProfileSourcebooks935(context, v4: true, books: true);
        var request = SourceRequest832(prepared.Inputs);
        Assert.IsNotNull(request.SourcebookSelection);
        Assert.AreEqual(CharacterCreationPolicyRequirement.Complete, request.PolicyRequirement);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var bootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot.Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var capture = RestoreObserved839(prepared);
        var witness = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, prepared.Inputs, request,
            ruleSources: new CharacterCreationRuleSourceExecutionContext(capture, context.Queries));
        Assert.IsNull(witness.Value);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, witness.Outcome);
        Assert.IsTrue(capture.Reports.Single(report => report.Scope == "lifestyles").Operations.Any(operation =>
            HasField832(operation, "unknown1008") && operation.Disposition == D1008.Unconsumed && operation.ExecutionDigest is null));
    }

    private static void AssertLocationExecution1008(CharacterCreationRuleSourceScopeResult report)
    {
        var operations = report.Operations.Where(operation => operation.Path.Contains("/cities[", StringComparison.Ordinal)).ToArray();
        Assert.IsTrue(operations.Length > 0);
        Assert.IsTrue(operations.All(operation => operation.Disposition != D1008.Unconsumed && operation.ExecutionDigest is not null));
    }
}
