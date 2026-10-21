using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready788 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using K788 = Chummer.Contracts.Characters.CharacterCreationCompleteObligationKind;
using C788 = Chummer.Contracts.Characters.CharacterCreationCompleteCoverage;
using E788 = Chummer.Contracts.Characters.CharacterCreationCompleteEvaluability;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    // Source-only proposal: these regressions have not been executed. They protect
    // partial-source refusal; PrivateWire674 remains the full admission gate.
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Complete788_Actual_character_partial_sources_cannot_certify_Errata(bool unknownOperation)
    {
        using var context = Ready788.Create(includeGearReview: true,
            includeNonEmptyPurchases: true, qualityName: "Ambidextrous", prepareSource: root =>
            {
                if (!unknownOperation) return;
                // Preserve all normal sources and selections; add an unsupported
                // operation before the real bootstrap/capture for the negative case.
                string path = Path.Combine(root, "data", "complexforms.xml");
                var source = XDocument.Load(path);
                source.Root!.Add(new XElement("unexecuted788",
                    new XAttribute("amendoperation", "UNKNOWN788"), "0"));
                source.Save(path);
            });
        string before = CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!);
        var prepared = PrepareSourcebooks800(context);
        var request = SourcebookRequest800(prepared.Inputs);
        var authority = CharacterCreationCompleteAssessmentService.AssessAuthority(prepared.Inputs, request, ruleSources: prepared.Execution);
        Assert.IsNotNull(authority.Value);
        var domain = ErrataDomain788(authority.Value!);
        Assert.AreEqual(E788.CandidateDependent, domain.Evaluability);
        Assert.AreEqual(C788.Pending, domain.Coverage);
        Assert.IsNull(domain.EvaluationDigest);
        Assert.AreEqual(E788.CandidateDependent, ErrataConsumer788(authority.Value!).Evaluability);
        Assert.AreEqual(C788.Pending, ErrataConsumer788(authority.Value!).Coverage);
        Assert.IsNull(ErrataConsumer788(authority.Value!).EvaluationDigest);

        // Use the existing transparent observer of the actual retained resolver.
        // It forwards every operation, including the Complete-only cap handshake.
        var observed = Observe718(context, prepared, request);
        var assessment = observed.Review.CompleteAssessment!;
        string diagnostic = CompleteDiagnostic788(prepared.Inputs, assessment);
        Console.WriteLine("Complete788 " + diagnostic);
        Console.WriteLine($"Complete788 reports={observed.Capture.Reports.Count}; "
            + $"omittedReports={Math.Max(0, observed.Capture.Reports.Count - 16)}");
        foreach (var report in observed.Capture.Reports.OrderBy(row => SafeScope788(row.Scope)).Take(16))
            Console.WriteLine($"Complete788 scope={SafeScope788(report.Scope)}; complete={report.IsComplete}; "
                + $"operations={report.Operations.Count}; unconsumed="
                + report.Operations.Count(row => row.Disposition == CharacterCreationSourceOperationDisposition.Unconsumed));
        Assert.AreEqual(true, assessment.MechanicalSucceeded, diagnostic);
        Assert.AreEqual(authority.Value!.Obligations.Count, assessment.Obligations.Count);
        Assert.IsTrue(assessment.Obligations.Any(row => row.Kind == K788.Source
            && row.Coverage != C788.Evaluated), diagnostic);
        Assert.AreEqual(E788.CandidateDependent, ErrataDomain788(assessment).Evaluability);
        Assert.AreEqual(C788.Unresolved, ErrataDomain788(assessment).Coverage, diagnostic);
        Assert.IsNull(ErrataDomain788(assessment).EvaluationDigest);
        Assert.AreEqual(E788.CandidateDependent, ErrataConsumer788(assessment).Evaluability);
        Assert.AreEqual(C788.Unresolved, ErrataConsumer788(assessment).Coverage, diagnostic);
        Assert.IsNull(ErrataConsumer788(assessment).EvaluationDigest);
        var errataGraph = assessment.Obligations.Single(row => row.Kind == K788.ActiveGraph
            && row.Operation == CharacterCreationConsumerPolicyOperation.Errata);
        Assert.AreEqual(C788.Unresolved, errataGraph.Coverage, diagnostic);
        Assert.IsNull(errataGraph.EvaluationDigest);
        if (unknownOperation)
            AssertSource696(prepared.Inputs, assessment, "complexforms", C788.Unresolved);
        Assert.IsFalse(assessment.SatisfiesComplete, diagnostic);
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    public void Complete788_Errata_domain_requires_current_Complete_and_private_source_execution()
    {
        using var context = Ready788.Create(includeGearReview: true, prepareSource: _ => { });
        string before = CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!);
        var legacy = PrepareSources696(context, legacy: true);
        var legacyReview = ReviewSources696(legacy, SourceRequest696(legacy.Inputs));
        Assert.AreEqual(E788.AuthorityUnavailable, ErrataDomain788(legacyReview.CompleteAssessment!).Evaluability);
        Assert.AreEqual(C788.Unresolved, ErrataDomain788(legacyReview.CompleteAssessment!).Coverage);
        Assert.AreEqual(E788.AuthorityUnavailable, ErrataConsumer788(legacyReview.CompleteAssessment!).Evaluability);
        Assert.AreEqual(C788.Unresolved, ErrataConsumer788(legacyReview.CompleteAssessment!).Coverage);

        var prepared = PrepareSourcebooks800(context);
        var request = SourcebookRequest800(prepared.Inputs);
        var historicalPolicy = CharacterCreationCompletionReviewService.Review(prepared.Inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.None }, ruleSources: prepared.Execution);
        Assert.IsNotNull(historicalPolicy.Value?.CompleteAssessment);
        Assert.AreEqual(E788.AuthorityUnavailable, ErrataDomain788(historicalPolicy.Value!.CompleteAssessment!).Evaluability);
        Assert.AreEqual(C788.Unresolved, ErrataDomain788(historicalPolicy.Value.CompleteAssessment!).Coverage);
        Assert.AreEqual(E788.AuthorityUnavailable, ErrataConsumer788(historicalPolicy.Value.CompleteAssessment!).Evaluability);
        Assert.AreEqual(C788.Unresolved, ErrataConsumer788(historicalPolicy.Value.CompleteAssessment!).Coverage);

        var missing = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request);
        var wrong = CharacterCreationCompletionReviewService.Review(prepared.Inputs,
            request with { RuleSourceCaptureDigest = CharacterCreationFinalizationDigest.ComputeUtf8("wrong-capture788") },
            ruleSources: prepared.Execution);
        foreach (var refused in new[] { missing, wrong })
        {
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, refused.Outcome);
            Assert.IsTrue(refused.Value?.CompleteAssessment is null
                || ErrataDomain788(refused.Value.CompleteAssessment).Coverage == C788.Unresolved);
            Assert.IsTrue(refused.Value?.CompleteAssessment is null
                || ErrataDomain788(refused.Value.CompleteAssessment).EvaluationDigest is null);
        }
        Assert.AreEqual(before, CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    private static CharacterCreationCompleteObligation ErrataDomain788(CharacterCreationCompleteAssessment assessment)
        => assessment.Obligations.Single(row => row.Kind == K788.PolicyDomain
            && row.Domain == CharacterCreationPolicyDomain.Errata);

    private static CharacterCreationCompleteObligation ErrataConsumer788(CharacterCreationCompleteAssessment assessment)
        => assessment.Obligations.Single(row => row.Kind == K788.ConsumerOperation
            && row.Operation == CharacterCreationConsumerPolicyOperation.Errata);

    // A fixed test vocabulary prevents source IDs, book names, paths, XML,
    // operation values and private digests from entering assertion/test logs.
    // Group before bounding: enabled books must not hide later campaign rows.
    private static string CompleteDiagnostic788(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompleteAssessment assessment)
    {
        var unresolved = assessment.Obligations.Where(row => row.Coverage is not (C788.Evaluated or C788.ProvenInactive)).ToArray();
        var groups = unresolved.GroupBy(row => new
        {
            row.Kind, row.Domain, row.Operation, row.SourceKind,
            Scope = DiagnosticScope788(inputs, row), row.Evaluability, row.Coverage, row.Reason
        }).Select(group => $"{group.Key.Kind}/{group.Key.Domain}/{group.Key.Operation}/{group.Key.SourceKind}/{group.Key.Scope}:"
            + $"{group.Key.Evaluability}/{group.Key.Coverage}/{group.Key.Reason} x{group.Count()}")
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        const int maximumGroups = 48;
        return $"unresolved={unresolved.Length}; groups={groups.Length}; omittedGroups={Math.Max(0, groups.Length - maximumGroups)}; "
            + string.Join("; ", groups.Take(maximumGroups));
    }

    private static RuleScope788 DiagnosticScope788(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompleteObligation row)
    {
        if (row.Kind != K788.Source || row.SourceKind != CharacterCreationPolicySourceKind.Rules)
            return RuleScope788.None;
        var matches = inputs.EffectivePolicy!.Sources.Where(source => source.Kind == row.SourceKind
            && source.Order == row.SourceOrder).Take(2).ToArray();
        return matches is [var source] ? SafeScope788(source.SourceId) : RuleScope788.Unknown;
    }

    private enum RuleScope788
    {
        None, Unknown, Profile, Metatypes, Priorities, Skills, Qualities, Traditions,
        Streams, Powers, Spells, ComplexForms, Gear, Lifestyles, Weapons, Armor
    }

    private static RuleScope788 SafeScope788(string scope) => scope switch
    {
        "profile" => RuleScope788.Profile, "metatypes" => RuleScope788.Metatypes,
        "priorities" => RuleScope788.Priorities, "skills" => RuleScope788.Skills,
        "qualities" => RuleScope788.Qualities, "traditions" => RuleScope788.Traditions,
        "streams" => RuleScope788.Streams, "powers" => RuleScope788.Powers,
        "spells" => RuleScope788.Spells, "complexforms" => RuleScope788.ComplexForms,
        "gear" => RuleScope788.Gear, "lifestyles" => RuleScope788.Lifestyles,
        "weapons" => RuleScope788.Weapons, "armor" => RuleScope788.Armor,
        _ => RuleScope788.Unknown
    };
}
