using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ready951 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using D951 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    [DataRow(true, true, false)]
    [DataRow(false, true, true)]
    [DataRow(true, true, true)]
    public void ProfileMetadata951_default_profile_name_and_text_are_bound_structural_metadata(
        bool v4, bool unknownSibling, bool renamed)
    {
        string expected = renamed ? "  Table & crew — display 951  " : "Standard (all sources)";
        using var context = Ready951.Create(includeGearReview: true,
            wrapResolver: v4 ? V4Sources905 : null, amendSettings: profile =>
            {
                if (renamed) profile.Element("name")!.Value = expected;
                if (unknownSibling) profile.Add(new XElement("unknown951", "still-unconsumed"));
            });
        var prepared = PrepareProfileSourcebooks935(context, v4, books: true);
        var policy = prepared.Inputs.EffectivePolicy!;
        Assert.IsTrue(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(
            CharacterCreationBuildMethods.Priority, out string defaultProfile));
        Assert.AreEqual(defaultProfile, policy.SettingsProfileId);
        string path = MetadataProfileFile951(context, defaultProfile);
        var selected = XDocument.Load(path).Root!.Element("settings")!.Elements("setting")
            .Single(row => row.Element("id")!.Value == defaultProfile);
        Assert.AreEqual(expected, selected.Element("name")!.Value);
        Assert.AreEqual(unknownSibling ? 101 : 100, selected.Elements().Count(), "Keep the actual default profile.");
        byte[] sourceBytes = File.ReadAllBytes(path);
        byte[] captureBytes = prepared.Execution.ExportPrivateInputs();
        string workspace = CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!);
        var request = SourcebookRequest800(prepared.Inputs);
        var observed = ObserveSources839(context, prepared, request);
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        var report = observed.Capture.Reports.Single(row => row.Scope == "profile");
        var invocation = observed.Capture.Invocations.Single();
        Assert.AreEqual(policy.PolicyDigest, invocation.PolicyDigest);
        Assert.AreEqual(prepared.Inputs.RuleSourceCaptureDigest, invocation.CaptureDigest);
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(request), invocation.RequestDigest);
        Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(invocation.GraphDigest));
        AssertMetadataName951(report, policy.SettingsProfileId, expected, invocation);
        Assert.AreEqual(policy.ProfileInputsDigest, report.SourceDigest);
        AssertUnconsumedMetadataField951(report, policy.SettingsProfileId, "gameplayoptionname");
        AssertUnconsumedMetadataField951(report, policy.SettingsProfileId, "contactpointsexpression");
        if (unknownSibling) AssertUnconsumedMetadataField951(report, policy.SettingsProfileId, "unknown951");
        Assert.IsFalse(report.IsComplete);
        AssertSource696(prepared.Inputs, observed.Review.CompleteAssessment, "profile", CharacterCreationCompleteCoverage.Unresolved);
        Assert.AreEqual(CharacterCreationCompleteCoverage.Unresolved, ErrataDomain788(observed.Review.CompleteAssessment).Coverage);
        Assert.IsFalse(observed.Review.CompleteAssessment.SatisfiesComplete);
        CollectionAssert.AreEqual(sourceBytes, File.ReadAllBytes(path));
        CollectionAssert.AreEqual(captureBytes, prepared.Execution.ExportPrivateInputs());
        Assert.AreEqual(workspace, CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("attribute")]
    [DataRow("nested")]
    [DataRow("comment")]
    [DataRow("cdata")]
    [DataRow("mixed-cdata")]
    [DataRow("empty")]
    [DataRow("whitespace")]
    public void ProfileMetadata951_malformed_names_remain_unconsumed(string malformed)
    {
        using var context = Ready951.Create(includeGearReview: true, amendSettings: profile =>
        {
            var name = profile.Element("name")!;
            switch (malformed)
            {
                case "duplicate": profile.Add(new XElement(name)); break;
                case "attribute": name.SetAttributeValue("extra951", "yes"); break;
                case "nested": name.ReplaceNodes(new XElement("nested951", name.Value)); break;
                case "comment": name.Add(new XComment("nonplain951")); break;
                case "cdata": name.ReplaceNodes(new XCData(name.Value)); break;
                case "mixed-cdata": name.Add(new XCData("suffix951")); break;
                case "empty": name.RemoveNodes(); break;
                case "whitespace": name.Value = "   "; break;
            }
        });
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var observed = ObserveSources839(context, prepared, SourcebookRequest800(prepared.Inputs));
        Assert.AreEqual(true, observed.Review.CompleteAssessment!.MechanicalSucceeded);
        var report = observed.Capture.Reports.Single(row => row.Scope == "profile");
        AssertUnconsumedMetadataField951(report, prepared.Inputs.EffectivePolicy!.SettingsProfileId, "name");
        Assert.IsFalse(report.IsComplete);
        Assert.IsFalse(observed.Review.CompleteAssessment.SatisfiesComplete);
    }

    [TestMethod]
    [DataRow("qualified")]
    [DataRow("qualified-shadow")]
    [DataRow("instruction")]
    public void ProfileMetadata951_noncanonical_names_are_refused_before_completed_execution(string malformed)
    {
        using var context = Ready951.Create(includeGearReview: true, prepareSource: _ => { });
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var source = prepared.Inputs.Magic.Authority.MysticAdeptPowerPointPolicy!;
        Assert.IsTrue(CharacterCreationMysticAdeptPowerPointRules.TryCreatePolicy(source.SettingsProfileId,
            source.SettingsInputsDigest, source.CanonicalSourceXml, out _));
        var profile = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        var name = profile.Element("name")!;
        if (malformed == "qualified") name.Name = XName.Get("name", "urn:metadata951");
        else if (malformed == "qualified-shadow") profile.Add(new XElement(XName.Get("name", "urn:metadata951"), name.Value));
        else name.Add(new XProcessingInstruction("metadata951", "nonplain"));
        Assert.IsFalse(CharacterCreationMysticAdeptPowerPointRules.TryCreatePolicy(source.SettingsProfileId,
            source.SettingsInputsDigest, profile.ToString(SaveOptions.DisableFormatting), out _),
            "The existing canonical profile validator refuses these inputs before metadata execution.");
    }

    [TestMethod]
    public void ProfileMetadata951_name_only_recapture_binds_changed_bytes_and_refuses_stale_invocation()
    {
        using var context = Ready951.Create(includeGearReview: true, prepareSource: _ => { });
        var prepared = PrepareProfileSourcebooks935(context, v4: false, books: true);
        var policy = prepared.Inputs.EffectivePolicy!;
        var request = SourcebookRequest800(prepared.Inputs);
        var observed = ObserveSources839(context, prepared, request);
        var invocation = observed.Capture.Invocations.Single();
        var report = observed.Capture.Reports.Single(row => row.Scope == "profile");
        AssertMetadataName951(report, policy.SettingsProfileId, "Standard (all sources)", invocation);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        byte[] originalCapture = prepared.Execution.ExportPrivateInputs();
        string path = MetadataProfileFile951(context, policy.SettingsProfileId);
        string before = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path));
        const string oldName = "<name>Standard (all sources)</name>";
        const string newName = "<name>Renamed display 951</name>";
        Assert.AreEqual(1, before.Split(oldName, StringSplitOptions.None).Length - 1);
        File.WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes(before.Replace(oldName, newName, StringComparison.Ordinal)));
        byte[] changedBytes = File.ReadAllBytes(path);
        var producer = (ICharacterCreationRuleSourceCapture)context.Resolver;
        Assert.IsTrue(producer.TryCaptureSourcebookRuleSources(original.Document.Content, out var fresh));
        Assert.AreNotEqual(prepared.Inputs.RuleSourceCaptureDigest, fresh!.IntegrityDigest);
        var live = context.Resolver.TryCreateContext(original.Document.Content)!;
        var retained = fresh.CreateReplayResolver().TryCreateContext(original.Document.Content)!;
        Assert.IsTrue(live.TryResolveCreationEffectivePolicy(out var livePolicy));
        Assert.IsTrue(retained.TryResolveCreationEffectivePolicy(out var freshPolicy));
        Assert.AreNotEqual(policy.ProfileInputsDigest, freshPolicy!.ProfileInputsDigest);
        Assert.AreEqual(livePolicy!.ProfileInputsDigest, freshPolicy.ProfileInputsDigest);
        var resolution = fresh.CreateExecutionResolution(prepared.Inputs.RuleSourceExecutionSchema)!;
        Assert.IsTrue(resolution.EnablePurchaseSourceExecution());
        Assert.IsTrue(resolution.EnableCompleteSkillCapSourceExecution());
        Assert.IsTrue(resolution.EnableCandidateSourcebookSelection());
        Assert.AreEqual(0, resolution.ExecuteRemainingSourceScopes(invocation).Count,
            "Old completed invocation must not classify newly captured source bytes.");
        var staleContext = new CharacterCreationRuleSourceExecutionContext(fresh, context.Queries);
        var stale = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request, ruleSources: staleContext);
        Assert.IsNull(stale.Value, "Old review inputs cannot borrow the fresh capture.");
        CollectionAssert.AreEqual(changedBytes, File.ReadAllBytes(path));
        CollectionAssert.AreEqual(originalCapture, prepared.Execution.ExportPrivateInputs());
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(original),
            CharacterCreationFinalizationDigest.Compute(context.Store.Get(context.WorkspaceId).Value!));
    }

    private static void AssertMetadataFromInputs956(CharacterCreationRuleSourceScopeResult report,
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationRuleSourceScopeInvocation invocation)
    {
        var profile = XElement.Parse(inputs.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml,
            LoadOptions.PreserveWhitespace);
        AssertMetadataName951(report, inputs.EffectivePolicy!.SettingsProfileId,
            profile.Elements("name").Single().Value, invocation);
    }

    private static string MetadataProfileFile951(Ready951 context, string profileId)
        => Path.Combine(context.Directory, "source", "data", CharacterCreationBootstrapProfiles.SettingsSourceFile(profileId));

    private static CharacterCreationSourceOperation[] MetadataField951(
        CharacterCreationRuleSourceScopeResult report, string profileId, string field)
    {
        // Source identity remains available even when a malformed envelope prevents all execution.
        string idDigest = CharacterCreationFinalizationDigest.Compute(new XElement("id", profileId).ToString(SaveOptions.DisableFormatting));
        string idPath = report.Operations.Single(row => row.SourceDigest == idDigest && HasField832(row, "id")
            && !row.Path.Contains("/text()", StringComparison.Ordinal)).Path;
        string prefix = idPath[..idPath.LastIndexOf('/')] + "/";
        return report.Operations.Where(row => row.Path.StartsWith(prefix, StringComparison.Ordinal)
            && (HasField832(row, field) || row.Path.Contains("/{urn:metadata951}" + field + "[", StringComparison.Ordinal))).ToArray();
    }

    private static void AssertUnconsumedMetadataField951(CharacterCreationRuleSourceScopeResult report, string profileId, string field)
    {
        var operations = MetadataField951(report, profileId, field);
        Assert.IsTrue(operations.Length > 0, field);
        Assert.IsTrue(operations.All(row => row.Disposition == D951.Unconsumed && row.ExecutionDigest is null
            && row.OperationResult718 is null && row.ConsumedValue718 is null), field);
    }

    private static void AssertMetadataName951(CharacterCreationRuleSourceScopeResult report, string profileId,
        string expected, CharacterCreationRuleSourceScopeInvocation invocation)
    {
        var operations = MetadataField951(report, profileId, "name");
        Assert.AreEqual(2, operations.Length, "Account for exactly the name element and its text.");
        Assert.AreEqual(1, operations.Count(row => row.Path.Contains("/text()", StringComparison.Ordinal)));
        Assert.IsTrue(invocation.TryGetCompletedConsumer("profile", out string consumer));
        Assert.AreEqual(consumer, report.ConsumerDigest);
        string expectedDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
            Consumer = consumer, Disposition = D951.Structural,
            Result = new { Operation = "selected-profile-display-name", ProfileId = profileId, Value = expected }
        });
        foreach (var operation in operations)
        {
            Assert.AreEqual(D951.Structural, operation.Disposition);
            Assert.AreEqual(expectedDigest, operation.ExecutionDigest);
            Assert.AreEqual(expected, operation.ConsumedValue718);
            string source = operation.Path.Contains("/text()", StringComparison.Ordinal) ? expected
                : new XElement("name", expected).ToString(SaveOptions.DisableFormatting);
            Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(source), operation.SourceDigest);
            using var diagnostic = JsonDocument.Parse(operation.OperationResult718!);
            Assert.AreEqual("selected-profile-display-name", diagnostic.RootElement.GetProperty("Operation").GetString());
            Assert.AreEqual(profileId, diagnostic.RootElement.GetProperty("ProfileId").GetString());
            Assert.AreEqual(expected, diagnostic.RootElement.GetProperty("Value").GetString());
        }
    }
}
