using Chummer.Contracts.BuildGhost;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

/// <summary>
/// Pure serialization integrity checks. Constructing or hashing these fixtures
/// does not establish a live owner, source, workspace or executing-engine authority.
/// </summary>
[TestClass]
public sealed class WorkspaceRuleQuestionIntegrityTests
{
    [TestMethod]
    public void Result_digest_is_deterministic_and_excludes_only_its_digest_field()
    {
        WorkspaceRuleQuestionResult packet = Packet();
        string digest = WorkspaceRuleQuestionIntegrity.ComputeResultDigest(packet);

        Assert.AreEqual(71, digest.Length);
        StringAssert.StartsWith(digest, "sha256:");
        Assert.AreEqual(digest, WorkspaceRuleQuestionIntegrity.ComputeResultDigest(packet));
        Assert.AreEqual(digest, WorkspaceRuleQuestionIntegrity.ComputeResultDigest(
            packet with { ResultDigest = "a previous digest is not part of its own input" }));
    }

    [TestMethod]
    public void Result_digest_covers_owner_workspace_source_and_answer_fields()
    {
        WorkspaceRuleQuestionResult packet = Packet();
        WorkspaceRuleQuestionBinding binding = packet.Binding!;
        WorkspaceRuleSourceAnchor anchor = packet.SourceAnchors[0];
        string original = WorkspaceRuleQuestionIntegrity.ComputeResultDigest(packet);
        (string Field, WorkspaceRuleQuestionResult Value)[] mutations =
        [
            ("result-schema", packet with { Schema = "other-schema" }),
            ("binding-schema", packet with { Binding = binding with { Schema = "other-schema" } }),
            ("owner", packet with { Binding = binding with { OwnerId = "owner-b" } }),
            ("trusted-local", packet with { Binding = binding with { TrustedLocalOwner = true } }),
            ("authority", packet with { Binding = binding with { OwnerAuthorityInstanceId = "authority-b" } }),
            ("transition", packet with { Binding = binding with { OwnerTransitionRevision = 9 } }),
            ("workspace", packet with { Binding = binding with { WorkspaceId = new("other") } }),
            ("ruleset", packet with { Binding = binding with { RulesetId = "sr6" } }),
            ("content-revision", packet with { Binding = binding with { ContentRevision = 8 } }),
            ("saved-revision", packet with { Binding = binding with { SavedRevision = 6 } }),
            ("complete-document", packet with { Binding = binding with { WorkspaceDocumentDigest = Digest('e') } }),
            ("settings", packet with { Binding = binding with { SettingsProfileId = "settings-b" } }),
            ("profile", packet with { Binding = binding with { SourceProfileDigest = Digest('e') } }),
            ("node", packet with { Binding = binding with { SourceNodeDigest = Digest('e') } }),
            ("engine-kind", packet with { Binding = binding with { EngineIdentityKind = "other-kind" } }),
            ("engine", packet with { Binding = binding with { EngineFingerprint = Digest('e') } }),
            ("intent", packet with { Binding = binding with { Intent = "other-intent" } }),
            ("subject", packet with { Binding = binding with { SubjectId = "other-subject" } }),
            ("locale", packet with { Binding = binding with { Locale = "de" } }),
            ("anchor-id", packet with { SourceAnchors = [anchor with { AnchorId = "anchor-b" }] }),
            ("anchor-ruleset", packet with { SourceAnchors = [anchor with { RulesetId = "sr6" }] }),
            ("book", packet with { SourceAnchors = [anchor with { SourceBook = "SG" }] }),
            ("page", packet with { SourceAnchors = [anchor with { Page = 124 }] }),
            ("anchor-quality-source", packet with { SourceAnchors = [anchor with { QualitySourceId = "quality-source-b" }] }),
            ("anchor-node", packet with { SourceAnchors = [anchor with { SourceNodeDigest = Digest('e') }] }),
            ("anchor-settings", packet with { SourceAnchors = [anchor with { SettingsProfileId = "settings-b" }] }),
            ("anchor-profile", packet with { SourceAnchors = [anchor with { SourceProfileDigest = Digest('e') }] }),
            ("trace", packet with { SourceAnchors = [anchor with { CalculationTrace = ["different trace"] }] }),
            ("explanation-schema", packet with { Explanation = packet.Explanation with { Schema = "other-schema" } }),
            ("explanation-id", packet with { Explanation = packet.Explanation with { ExplanationId = "explanation-b" } }),
            ("rule", packet with { Explanation = packet.Explanation with { RuleId = "other-rule" } }),
            ("question", packet with { Explanation = packet.Explanation with { Question = "different question" } }),
            ("explanation-status", packet with { Explanation = packet.Explanation with { Status = "unresolved" } }),
            ("answer", packet with { Explanation = packet.Explanation with { Explanation = "different answer" } }),
            ("explanation-anchor", packet with { Explanation = packet.Explanation with { SourceAnchorIds = ["anchor-b"] } }),
            ("uncertainty", packet with { Explanation = packet.Explanation with { UncertaintyReason = "changed" } }),
            ("source-route", packet with { Explanation = packet.Explanation with { SourceLookupRoute = "chummer://changed" } }),
            ("level", packet with { Level = 1 }),
            ("maximum", packet with { MaximumLevel = 4 }),
            ("status", packet with { Status = WorkspaceRuleQuestionStatuses.Unresolved }),
            ("failure", packet with { FailureReason = "changed" })
        ];

        foreach ((string field, WorkspaceRuleQuestionResult value) in mutations)
        {
            Assert.AreNotEqual(original, WorkspaceRuleQuestionIntegrity.ComputeResultDigest(value), field);
        }
    }

    [TestMethod]
    public void Engine_hash_orders_roles_but_binds_every_module_identity_field()
    {
        WorkspaceRuleExecutingModule[] modules = Modules();
        string original = WorkspaceRuleQuestionIntegrity.ComputeEngineFingerprint(modules);
        Assert.AreEqual(original, WorkspaceRuleQuestionIntegrity.ComputeEngineFingerprint(
            modules.Reverse().ToArray()));

        WorkspaceRuleExecutingModule first = modules[0];
        WorkspaceRuleExecutingModule[] alternatives =
        [
            first with { Role = "different-role" },
            first with { ImplementationType = "Different.Type" },
            first with { AssemblyName = "Different.Assembly" },
            first with { ModuleVersionId = new Guid("33333333-3333-3333-3333-333333333333") }
        ];
        foreach (WorkspaceRuleExecutingModule alternative in alternatives)
        {
            Assert.AreNotEqual(original, WorkspaceRuleQuestionIntegrity.ComputeEngineFingerprint(
                [alternative, modules[1]]));
        }

        WorkspaceRuleQuestionResult packet = Packet();
        Assert.AreNotEqual(
            WorkspaceRuleQuestionIntegrity.ComputeResultDigest(packet),
            WorkspaceRuleQuestionIntegrity.ComputeResultDigest(packet with
            {
                Binding = packet.Binding! with { ExecutingModules = [alternatives[0], modules[1]] }
            }));
    }

    [TestMethod]
    public void Hash_helpers_reject_null_inputs()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            WorkspaceRuleQuestionIntegrity.ComputeResultDigest(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            WorkspaceRuleQuestionIntegrity.ComputeEngineFingerprint(null!));
    }

    private static WorkspaceRuleQuestionResult Packet()
    {
        WorkspaceRuleExecutingModule[] modules = Modules();
        var binding = new WorkspaceRuleQuestionBinding(
            WorkspaceRuleQuestionSchemas.BindingV1,
            "owner-a", false, "authority-a", 7,
            new CharacterWorkspaceId("workspace-a"), "sr5", 7, 7,
            Digest('a'), "settings-a", Digest('b'), Digest('c'),
            WorkspaceRuleQuestionSchemas.ExecutingModulesV1,
            WorkspaceRuleQuestionIntegrity.ComputeEngineFingerprint(modules), modules,
            WorkspaceRuleQuestionIntents.QualityLevel, "subject-a", "en");
        var anchor = new WorkspaceRuleSourceAnchor(
            "anchor-a", "sr5", "SR5", 123, "quality-source-a",
            Digest('c'), "settings-a", Digest('b'), ["2 saved instances; maximum 3"]);
        var explanation = new BuildGhostRuleExplanation(
            BuildGhostContractVersions.RuleExplanationV1, "explanation-a",
            WorkspaceRuleQuestionIntents.QualityLevelRuleId,
            "What is the quality level?", WorkspaceRuleQuestionStatuses.Resolved,
            "Level 2, maximum 3.", ["anchor-a"], null, null);
        return new WorkspaceRuleQuestionResult(
            WorkspaceRuleQuestionSchemas.ResultV1, WorkspaceRuleQuestionStatuses.Resolved,
            explanation, [anchor], binding, 2, 3, string.Empty);
    }

    private static WorkspaceRuleExecutingModule[] Modules() =>
    [
        new("contracts", "Example.Contracts", "Example.Contracts",
            new Guid("11111111-1111-1111-1111-111111111111")),
        new("executor", "Example.Executor", "Example.Executor",
            new Guid("22222222-2222-2222-2222-222222222222"))
    ];

    private static string Digest(char digit) => "sha256:" + new string(digit, 64);
}
