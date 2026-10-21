using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using C = Chummer.Contracts.Characters.CharacterCreationCompleteCoverage;
using D = Chummer.Contracts.Characters.CharacterCreationPolicyDomain;
using E = Chummer.Contracts.Characters.CharacterCreationCompleteEvaluability;
using K = Chummer.Contracts.Characters.CharacterCreationCompleteObligationKind;
using O = Chummer.Contracts.Characters.CharacterCreationConsumerPolicyOperation;
using R = Chummer.Contracts.Characters.CharacterCreationCompleteReason;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    private static CharacterCreationCompletionReviewRequest Request386(CharacterCreationCompletionReviewInputs inputs)
        => Request(inputs, CharacterCreationPolicyRequirement.None) with
        { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4 };

    private static CharacterCreationCompleteObligation Domain386(CharacterCreationCompleteAssessment assessment, D domain)
        => assessment.Obligations.Single(row => row.Kind == K.PolicyDomain && row.Domain == domain);

    [TestMethod]
    [DataRow("core", CharacterCreationBuildMethods.Priority)]
    [DataRow("selected", CharacterCreationBuildMethods.Priority)]
    [DataRow("all", CharacterCreationBuildMethods.Priority)]
    [DataRow("selected", CharacterCreationBuildMethods.SumToTen)]
    public void Complete386_Real_book_selection_admission_and_independent_cold_replay(string selection, string method)
    {
        string[] expectedBooks = [];
        using var context = ReadyContext.Create(false, buildMethod: method,
            amendSettings: settings => settings.SetElementValue("allowfreegrids", false),
            prepareSource: root =>
            {
                // All means the real fixture's entire book catalog, never an
                // invented wildcard or an empty-list-means-all convention.
                expectedBooks = (selection == "all"
                    ? XDocument.Load(Path.Combine(root, "data", "books.xml")).Root!.Element("books")!
                        .Elements("book").Select(book => book.Element("code")!.Value.Trim())
                    : selection == "selected" ? new[] { "SR5", "HT" } : new[] { "SR5" })
                    .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
                Assert.IsTrue(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(method, out string id));
                string path = Path.Combine(root, "data", CharacterCreationBootstrapProfiles.SettingsSourceFile(id));
                var document = XDocument.Load(path);
                var profile = document.Root!.Element("settings")!.Elements("setting")
                    .Single(row => row.Element("id")!.Value == id);
                profile.Elements("books").Remove();
                profile.Add(new XElement("books", expectedBooks.Reverse().Select(book => new XElement("book", book))));
                document.Save(path);
            });
        var inputs = LoadConsumption(context);
        CollectionAssert.AreEqual(expectedBooks, inputs.EffectivePolicy!.EnabledSourcebooks.ToArray());
        Assert.AreEqual(selection != "core", inputs.Lifestyles.Authority.FreeGridsEnabled);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var sourceContext = context.Resolver.TryCreateContext(original.Document.Content)!;
        Assert.IsTrue(sourceContext.TryIsBookEnabled("SR5", out bool coreEnabled));
        Assert.IsTrue(coreEnabled);
        Assert.IsTrue(sourceContext.TryIsBookEnabled("HT", out bool htEnabled));
        Assert.AreEqual(selection != "core", htEnabled);

        var request = Request386(inputs);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var fullExecution = CharacterCreationCompleteProfileExecution.FromFull(inputs, request, authority);
        Assert.IsNotNull(fullExecution);
        foreach (var domain in new[] { D.Profile, D.Books })
        {
            Assert.AreEqual(E.CandidateDependent, Domain386(authority, domain).Evaluability);
            Assert.AreEqual(C.Pending, Domain386(authority, domain).Coverage);
            Assert.IsTrue(fullExecution.EvaluationsFor(authority).ContainsKey("domain:" + domain));
        }
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        foreach (var domain in new[] { D.Profile, D.Books })
            Assert.AreEqual(C.Evaluated, Domain386(full.Value!.CompleteAssessment!, domain).Coverage);
        Assert.IsTrue(full.Value!.CompleteAssessment!.Obligations.Where(row => row.Kind is K.Source or K.Sourcebook)
            .All(row => row.Coverage == C.Unresolved));
        Assert.IsTrue(full.Value.CompleteAssessment.Obligations.Where(row => row.Kind == K.ActiveGraph
            && row.Operation is O.Amendments or O.Errata or O.Campaign or O.CharacterMechanics)
            .All(row => row.Coverage == C.Unresolved));
        Assert.AreEqual(C.Unresolved, Domain386(full.Value.CompleteAssessment, D.OtherSettings).Coverage);
        Assert.IsFalse(full.Value.CompleteAssessment.SatisfiesComplete);

        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(sourceContext, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, captured.Value!, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var cold));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, cold, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(JsonSerializer.Serialize(full.Value.CompleteAssessment), JsonSerializer.Serialize(replay.Value!.CompleteAssessment));

        var originalSelections = CharacterCreationCompletionPreservationRules.SnapshotOriginal(inputs);
        var selectedAuthority = CharacterCreationCompleteAssessmentService.FromSelectedAuthority(cold!, request,
            originalSelections, inputs.Contacts.OriginalEffects);
        var selectedExecution = CharacterCreationCompleteProfileExecution.FromSelected(cold!, request,
            originalSelections, inputs.Contacts.OriginalEffects, selectedAuthority);
        Assert.IsNotNull(selectedExecution);
        CollectionAssert.AreEquivalent(fullExecution.EvaluationsFor(authority).ToArray(),
            selectedExecution.EvaluationsFor(selectedAuthority).ToArray());

        var blocked = CharacterCreationCompleteAssessmentService.AssessAuthority(inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, blocked.Outcome);
        Assert.IsFalse(blocked.Value!.MayRequestCompleteCandidate);
    }

    [TestMethod]
    public void Complete386_Missing_malformed_duplicate_and_mismatched_canonical_authority_stays_unresolved()
    {
        using var context = Context332();
        var inputs = LoadConsumption(context);
        var policy = inputs.EffectivePolicy!;
        var source = inputs.Magic.Authority.MysticAdeptPowerPointPolicy!;
        Assert.IsFalse(CharacterCreationCompleteProfileExecution.SupportsDomain(policy, null, D.Profile));
        Assert.IsFalse(CharacterCreationCompleteProfileExecution.SupportsDomain(policy,
            source with { CanonicalSourceXml = "<setting>" }, D.Profile));
        Assert.IsFalse(CharacterCreationCompleteProfileExecution.SupportsDomain(policy,
            source with { SettingsProfileId = Guid.NewGuid().ToString("D") }, D.Profile));
        Assert.IsFalse(CharacterCreationCompleteProfileExecution.SupportsDomain(policy,
            source with { SettingsInputsDigest = CharacterCreationFinalizationDigest.Compute("different-profile") }, D.Profile));
        Assert.IsFalse(CharacterCreationCompleteProfileExecution.SupportsDomain(policy with
            { Sources = [.. policy.Sources, policy.Sources.Single(row => row.SourceId == "profile")] }, source, D.Profile));

        foreach (string? booksXml in new[]
        {
            null, "<books><book>SR5</book></books><books><book>SR5</book></books>",
            "<books><book>SR5</book><book>sr5</book></books>", "<books><book /></books>",
            "<books><book><code>SR5</code></book></books>", "<books><book active=\"true\">SR5</book></books>",
            "<books selected=\"all\"><book>SR5</book></books>", "<books>SR5</books>",
            "<books><unknown>SR5</unknown></books>", "<books><book>SR5!</book></books>"
        })
        {
            var xml = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
            xml.Elements("books").Remove();
            if (booksXml is not null) xml.Add(XElement.Parse("<root>" + booksXml + "</root>").Elements());
            Assert.IsTrue(CharacterCreationMysticAdeptPowerPointRules.TryCreatePolicy(source.SettingsProfileId,
                source.SettingsInputsDigest, xml.ToString(SaveOptions.DisableFormatting), out var changedSource));
            var changedPolicy = policy with
            {
                BooksInputsDigest = xml.Element("books") is { } books
                    ? CharacterCreationFinalizationDigest.Compute(books.ToString(SaveOptions.DisableFormatting)) : null
            };
            Assert.IsFalse(CharacterCreationCompleteProfileExecution.SupportsDomain(changedPolicy, changedSource, D.Books), booksXml);
        }
        // Canonical bytes, selected books and the books-node digest must all agree.
        foreach (var changed in new[]
        {
            policy with { EnabledSourcebooks = ["SR5", "HT"] },
            policy with { EnabledSourcebooks = ["SR5", "SR5"] },
            policy with { BooksInputsDigest = null },
            policy with { BooksInputsDigest = CharacterCreationFinalizationDigest.Compute("unrelated-books") }
        }) Assert.IsFalse(CharacterCreationCompleteProfileExecution.SupportsDomain(changed, source, D.Books));
    }

    [TestMethod]
    public void Complete386_Changed_or_borrowed_authority_and_receipts_cannot_create_execution()
    {
        using var context = Context332();
        var inputs = LoadConsumption(context);
        var request = Request386(inputs);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var execution = CharacterCreationCompleteProfileExecution.FromFull(inputs, request, authority);
        Assert.IsNotNull(execution);
        string drift = CharacterCreationFinalizationDigest.Compute("different-authority");
        foreach (var changed in new[]
        {
            authority with { InputsSnapshotDigest = drift }, authority with { RawCharacterXmlDigest = drift },
            authority with { SelectedInputsDigest = drift }, authority with { PolicyDigest = drift },
            authority with { RequestDigest = drift }, authority with { SelectionsDigest = drift },
            authority with { SettingsObservationDigest = drift },
            authority with { Obligations = authority.Obligations.Select(row => row with { AuthorityDigest = drift }).ToArray() }
        }) Assert.AreEqual(0, execution.EvaluationsFor(changed).Count);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        var receiptOnly = CharacterCreationCompleteAssessmentService.AfterEvaluation(authority,
            JsonSerializer.Deserialize<CharacterCreationPolicyConsumption>(JsonSerializer.Serialize(review.PolicyConsumption)), true);
        foreach (var domain in new[] { D.Profile, D.Books })
            Assert.AreEqual(C.Unresolved, Domain386(receiptOnly, domain).Coverage);

        foreach (var changedPolicy in new[]
        {
            inputs.EffectivePolicy! with { EnabledSourcebooks = ["HT", "SR5"] },
            inputs.EffectivePolicy! with { BooksInputsDigest = drift },
            inputs.EffectivePolicy! with { ProfileInputsDigest = drift }
        })
        {
            var changed = WithPolicy(inputs, changedPolicy);
            Assert.IsNull(CharacterCreationCompleteProfileExecution.FromFull(changed, Request386(changed), authority));
        }
        using var other = ReadyContext.Create(false, amendSettings: settings => settings.SetElementValue("name", "Different resolved profile"));
        var otherInputs = LoadConsumption(other);
        Assert.IsNull(CharacterCreationCompleteProfileExecution.FromFull(otherInputs, Request386(otherInputs), authority));
        var borrowed = inputs with { Magic = inputs.Magic with
            { Authority = inputs.Magic.Authority with { MysticAdeptPowerPointPolicy = otherInputs.Magic.Authority.MysticAdeptPowerPointPolicy } } };
        borrowed = borrowed with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(borrowed) };
        Assert.IsNull(CharacterCreationCompleteProfileExecution.FromFull(borrowed, Request386(borrowed), authority));
    }

    [TestMethod]
    public void Complete386_Selected_route_rejects_resealed_profile_and_book_mismatches()
    {
        using var context = Context332();
        var inputs = LoadConsumption(context);
        var request = Request386(inputs);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var capture = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, capture.Outcome, string.Join(",", capture.Blockers));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, capture.Value!, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out _, out var cold));
        var selections = CharacterCreationCompletionPreservationRules.SnapshotOriginal(inputs);
        var effects = inputs.Contacts.OriginalEffects;
        var authority = CharacterCreationCompleteAssessmentService.FromSelectedAuthority(cold!, request, selections, effects);
        Assert.IsNotNull(CharacterCreationCompleteProfileExecution.FromSelected(cold!, request, selections, effects, authority));
        string drift = CharacterCreationFinalizationDigest.Compute("unrelated-profile-or-books");
        foreach (var policy in new[]
        {
            cold!.EffectivePolicy! with { EnabledSourcebooks = ["HT", "SR5"] },
            cold.EffectivePolicy! with { EnabledSourcebooks = ["SR5", "SR5"] },
            cold.EffectivePolicy! with { BooksInputsDigest = drift },
            cold.EffectivePolicy! with { ProfileInputsDigest = drift }
        })
        {
            var sealedPolicy = policy with { PolicyDigest = CharacterCreationEffectivePolicyContext.ComputeDigest(policy) };
            var movedRequest = request with { EffectivePolicyDigest = sealedPolicy.PolicyDigest };
            var forged = cold with { EffectivePolicy = sealedPolicy, ProposalDigest = CharacterCreationFinalizationDigest.Compute(movedRequest) };
            forged = forged with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forged) };
            Assert.IsNull(CharacterCreationCompleteProfileExecution.FromSelected(forged, movedRequest, selections, effects, authority));
            Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, forged, movedRequest).Value);
        }
        var source = cold!.Magic.MysticAdeptPowerPointPolicy!;
        var changedXml = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        changedXml.SetElementValue("name", "Changed retained profile");
        Assert.IsTrue(CharacterCreationMysticAdeptPowerPointRules.TryCreatePolicy(source.SettingsProfileId,
            source.SettingsInputsDigest, changedXml.ToString(SaveOptions.DisableFormatting), out var changedSource));
        foreach (var value in new[] { null, changedSource, source with { CanonicalSourceXml = "<setting>" } })
        {
            var forged = cold with { Magic = cold.Magic with { MysticAdeptPowerPointPolicy = value } };
            forged = forged with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forged) };
            Assert.IsNull(CharacterCreationCompleteProfileExecution.FromSelected(forged, request, selections, effects, authority));
            Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, forged, request).Value);
        }
    }

    [TestMethod]
    [DataRow(CharacterCreationCompleteAssessment.SchemaV1)]
    [DataRow(CharacterCreationCompleteAssessment.SchemaV2)]
    [DataRow(CharacterCreationCompleteAssessment.SchemaV3)]
    public void Complete386_Historical_profile_and_books_obligation_bytes_stay_unchanged(string schema)
    {
        using var context = Context332();
        var inputs = LoadConsumption(context);
        var request = Request386(inputs) with { CompleteAssessmentSchema = schema };
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        Assert.IsNull(CharacterCreationCompleteProfileExecution.FromFull(inputs, request, authority));
        foreach (var domain in new[] { D.Profile, D.Books })
        {
            var source = inputs.EffectivePolicy!.Domains.Single(row => row.Domain == domain);
            var historical = new CharacterCreationCompleteObligation(K.PolicyDomain, "domain:" + domain,
                E.Unsupported, C.Pending, R.SourceSemanticsUnproven, CharacterCreationFinalizationDigest.Compute(source))
                { Domain = domain };
            Assert.AreEqual(JsonSerializer.Serialize(historical), JsonSerializer.Serialize(Domain386(authority, domain)));
        }
        var before = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, before.Outcome, string.Join(",", before.Blockers));
        string bytes = JsonSerializer.Serialize(before.Value);
        CharacterCreationCompletionReviewService.Review(inputs, Request386(inputs));
        Assert.AreEqual(bytes, JsonSerializer.Serialize(CharacterCreationCompletionReviewService.Review(inputs, request).Value));
        foreach (var domain in new[] { D.Profile, D.Books })
            Assert.AreEqual(C.Unresolved, Domain386(before.Value!.CompleteAssessment!, domain).Coverage);
    }
}
