using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority, 3, 2, 12, 12)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, 3, 2, 12, 12)]
    [DataRow(CharacterCreationBuildMethods.Priority, 8, 7, 12, 12)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, 8, 7, 12, 12)]
    [DataRow(CharacterCreationBuildMethods.Priority, 9, 8, 7, 4)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, 9, 8, 7, 4)]
    [DataRow(CharacterCreationBuildMethods.Priority, 0, 0, 12, 12)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, 0, 0, 12, 12)]
    public void Configured_creation_skill_limits_bound_real_points_groups_and_V4_replay(
        string method, int configuredActive, int configuredKnowledge, int careerActive, int careerKnowledge)
    {
        using var context = ReadyContext.Create(false, buildMethod: method,
            rankAssignments: ConfiguredSkillRanks(), amendSettings: settings =>
            {
                SetConfiguredSkillCaps(settings, configuredActive, configuredKnowledge);
                settings.SetElementValue("maxskillrating", careerActive);
                settings.SetElementValue("maxknowledgeskillrating", careerKnowledge);
            });
        int active = Math.Min(configuredActive, careerActive);
        int knowledge = Math.Min(configuredKnowledge, careerKnowledge);
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        var inputs = LoadConsumers(context, includeSkillPurchases: true);
        var authority = inputs.Skills.Authority;
        Assert.AreEqual(active, authority.MaxActiveSkillRatingCreate);
        Assert.AreEqual(knowledge, authority.MaxKnowledgeSkillRatingCreate);
        Assert.AreEqual(active, authority.MaxSkillGroupRatingCreate);
        Assert.AreEqual(active, inputs.Skills.KarmaPurchasePolicy!.MaxSkillGroupRatingCreate);
        foreach (var (id, cap) in new[]
                 {
                     (CharacterCreationEffectivePolicyContext.ActiveLimit, active),
                     (CharacterCreationEffectivePolicyContext.KnowledgeLimit, knowledge),
                     (CharacterCreationEffectivePolicyContext.GroupLimit, active)
                 })
        {
            var row = inputs.EffectivePolicy!.Settings.Single(item => item.SettingId == id);
            Assert.AreEqual((int?)cap, row.IntegerValue);
            Assert.AreEqual(CharacterCreationPolicyDisposition.Effective, row.Disposition);
        }

        var pistols = authority.ActiveSkills.Single(row => row.Name == "Pistols");
        var academic = authority.KnowledgeSkills.First(row => row.Category == "Academic");
        var athletics = authority.SkillGroups.Single(row => row.Name == "Athletics");
        var request = ConfiguredSkillRequest(inputs, active, knowledge);
        foreach (var original in inputs.Skills.Allocations)
            Assert.AreEqual(original, request.Selections.SkillAllocations.Single(row =>
                row.Kind == original.Kind && row.SourceSkillId == original.SourceSkillId));
        var reviewed = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        if (active > 0)
        {
            Assert.AreEqual((int?)active, reviewed.Value!.Skills!.Skills.Single(row => row.SourceSkillId == pistols.SourceSkillId).Rating);
            Assert.AreEqual(active, reviewed.Value.Skills.SkillGroups.Single(row => row.GroupId == athletics.GroupId).Rating);
            foreach (var permission in request.Selections.ChangeableSkillTargets)
            {
                var locked = CharacterCreationCompletionReviewService.Review(inputs, request with
                {
                    Selections = request.Selections with
                    {
                        ChangeableSkillTargets = request.Selections.ChangeableSkillTargets
                            .Where(target => target != permission).ToArray()
                    }
                });
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, locked.Outcome);
                CollectionAssert.Contains(locked.Blockers.ToArray(),
                    CharacterCreationCompletionReviewSchemas.SelectionPreservationConflict);
            }
        }
        if (knowledge > 0)
            Assert.AreEqual((int?)knowledge, reviewed.Value!.Skills!.Skills.Single(row => row.SourceSkillId == academic.SourceSkillId).Rating);

        foreach (var (skill, cap) in new[] { (pistols, active), (academic, knowledge) })
        {
            var over = request with { Selections = request.Selections with
            {
                SkillAllocations = [.. request.Selections.SkillAllocations.Where(row => row.SourceSkillId != skill.SourceSkillId),
                    new(skill.SourceSkillId, skill.Kind, cap + 1, null, false)]
            } };
            var denied = CharacterCreationCompletionReviewService.Review(inputs, over);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, denied.Outcome);
            CollectionAssert.Contains(denied.Blockers.ToArray(), CharacterCreationSkillsBlockers.RatingInvalid);
        }
        var overGroup = CharacterCreationCompletionReviewService.Review(inputs, request with
        {
            Selections = request.Selections with { GroupAllocations = [new(athletics.GroupId, active + 1)] }
        });
        CollectionAssert.Contains(overGroup.Blockers.ToArray(), CharacterCreationSkillsBlockers.GroupInvalid);
        CollectionAssert.Contains(CharacterCreationCompletionReviewService.Review(inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete }).Blockers.ToArray(),
            CharacterCreationEffectivePolicyContext.CoverageUnavailable);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        AssertV4Commit(context, inputs, request, reviewed.Value!);
    }

    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority, 3, 2)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, 3, 2)]
    [DataRow(CharacterCreationBuildMethods.Priority, 8, 7)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, 8, 7)]
    public void Configured_creation_skill_limits_bind_optional_Karma_to_the_same_caps(
        string method, int active, int knowledge)
    {
        using var context = ReadyContext.Create(false, buildMethod: method, rankAssignments: ConfiguredSkillRanks(),
            amendSettings: settings => SetConfiguredSkillCaps(settings, active, knowledge));
        var inputs = LoadConsumers(context, includeSkillPurchases: true);
        var pistols = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
        var academic = inputs.Skills.Authority.KnowledgeSkills.First(row => row.Category == "Academic");
        var request = Request(inputs, CharacterCreationPolicyRequirement.SkillLimits);
        request = request with { Selections = request.Selections with
        {
            SkillAllocations = [.. request.Selections.SkillAllocations,
                new(pistols.SourceSkillId, pistols.Kind, active - 1, null, false),
                new(academic.SourceSkillId, academic.Kind, knowledge - 1, null, false)],
            SkillKarmaPurchases = [new(pistols.SourceSkillId, pistols.Kind, pistols.SourceNodeDigest, active),
                new(academic.SourceSkillId, academic.Kind, academic.SourceNodeDigest, knowledge)]
        } };
        var reviewed = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        Assert.AreEqual((int?)(active * 2 + knowledge), reviewed.Value!.Balances.SkillsKarmaCost);
        foreach (var skill in new[] { pistols, academic })
        {
            var denied = CharacterCreationCompletionReviewService.Review(inputs, request with
            {
                Selections = request.Selections with
                {
                    SkillKarmaPurchases = request.Selections.SkillKarmaPurchases!.Select(row =>
                        row.SourceSkillId == skill.SourceSkillId ? row with { Rating = row.Rating + 1 } : row).ToArray()
                }
            });
            CollectionAssert.Contains(denied.Blockers.ToArray(), CharacterCreationSkillsBlockers.RatingInvalid);
        }
        AssertV4Commit(context, inputs, request, reviewed.Value!);
    }

    [TestMethod]
    [DataRow("maxskillratingcreate", "-1")]
    [DataRow("maxskillratingcreate", "six")]
    [DataRow("maxskillratingcreate", "2147483648")]
    [DataRow("maxskillratingcreate", "101")]
    [DataRow("maxskillratingcreate", "duplicate")]
    [DataRow("maxskillratingcreate", "nested")]
    [DataRow("maxknowledgeskillratingcreate", "-1")]
    [DataRow("maxknowledgeskillratingcreate", "101")]
    [DataRow("maxknowledgeskillratingcreate", "duplicate")]
    [DataRow("maxskillrating", "-1")]
    [DataRow("maxskillrating", "six")]
    [DataRow("maxknowledgeskillrating", "duplicate")]
    public void Configured_creation_skill_limits_refuse_invalid_authored_profile_fields(string field, string value)
    {
        using var context = ReadyContext.Create(false, amendSettings: settings => SetConfiguredSkillCaps(settings, 6, 6));
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        string path = ConfiguredSkillSettingsPath(context, CharacterCreationBuildMethods.Priority);
        XDocument document = XDocument.Load(path);
        var profile = ConfiguredSkillProfile(document, CharacterCreationBuildMethods.Priority);
        if (field.EndsWith("create", StringComparison.Ordinal))
            profile.Elements(field[..^6]).Remove(); // No career clamp masks this invalid/unsafe effective cap.
        profile.Elements(field).Remove();
        if (value == "duplicate") profile.Add(new XElement(field, 6), new XElement(field, 6));
        else if (value == "nested") profile.Add(new XElement(field, new XElement("value", 6)));
        else profile.Add(new XElement(field, value));
        document.Save(path);
        var source = context.Resolver.TryCreateContext(context.Store.Get(context.WorkspaceId).Value!.Document.Content)!;
        Assert.IsTrue(source.TryResolveCreationSkillsAuthority(out var authority));
        Assert.IsFalse(authority.IsAuthoritative);
        CollectionAssert.Contains(authority.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.SkillLimitsUnsupported);
        Assert.IsTrue(source.TryResolveCreationEffectivePolicy(out var policy));
        string id = field.StartsWith("maxknowledge", StringComparison.Ordinal)
            ? CharacterCreationEffectivePolicyContext.KnowledgeLimit : CharacterCreationEffectivePolicyContext.ActiveLimit;
        var row = policy!.Settings.Single(item => item.SettingId == id);
        Assert.AreEqual(CharacterCreationPolicyDisposition.Unsupported, row.Disposition);
        Assert.AreEqual(value == "101" ? CharacterCreationPolicyReason.ConfiguredLimitUnsupported : CharacterCreationPolicyReason.InvalidSetting,
            row.Reason);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void Configured_creation_skill_limits_keep_default_runtime_bytes_and_admit_the_bounded_ceiling()
    {
        using var context = ReadyContext.Create(false, amendSettings: settings =>
        {
            foreach (string field in new[] { "maxskillratingcreate", "maxskillrating", "maxknowledgeskillratingcreate", "maxknowledgeskillrating" })
                settings.Elements(field).Remove();
        });
        var defaults = LoadConsumers(context);
        Assert.AreEqual("sha256:02ee808795c3ba3efe464faf38a34035f6441e3a901bed56647ebfe9900d633e", defaults.Skills.Authority.RuntimeDigest);
        Assert.AreEqual(6, defaults.Skills.Authority.MaxActiveSkillRatingCreate);
        Assert.AreEqual(6, defaults.Skills.Authority.MaxKnowledgeSkillRatingCreate);
        string path = ConfiguredSkillSettingsPath(context, CharacterCreationBuildMethods.Priority);
        var document = XDocument.Load(path);
        SetConfiguredSkillCaps(ConfiguredSkillProfile(document, CharacterCreationBuildMethods.Priority), 100, 100);
        document.Save(path);
        var source = context.Resolver.TryCreateContext(context.Store.Get(context.WorkspaceId).Value!.Document.Content)!;
        Assert.IsTrue(source.TryResolveCreationSkillsAuthority(out var authority));
        Assert.IsTrue(authority.IsAuthoritative, string.Join(",", authority.Blockers));
        Assert.AreEqual(100, authority.MaxActiveSkillRatingCreate);
        Assert.AreEqual(100, authority.MaxKnowledgeSkillRatingCreate);
        Assert.AreEqual(100, authority.MaxSkillGroupRatingCreate);
        Assert.AreNotEqual(defaults.Skills.Authority.RuntimeDigest, authority.RuntimeDigest);
        Assert.IsTrue(source.TryResolveCreationEffectivePolicy(out var policy));
        Assert.IsTrue(policy!.Settings.Take(3).All(row => row.Disposition == CharacterCreationPolicyDisposition.Effective));
    }

    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority, false)]
    [DataRow(CharacterCreationBuildMethods.Priority, true)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, false)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, true)]
    public void Configured_creation_skill_limits_reject_source_drift_before_atomic_Apply(string method, bool duringCommit)
    {
        using var context = ReadyContext.Create(false, buildMethod: method, rankAssignments: ConfiguredSkillRanks(),
            amendSettings: settings => SetConfiguredSkillCaps(settings, 8, 7));
        var inputs = LoadConsumers(context, includeSkillPurchases: true);
        var request = ConfiguredSkillRequest(inputs, 8, 7);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new PolicyWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        var cash = service.LoadStartingCash(owners.Capture(), context.WorkspaceId, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, cash.Outcome, string.Join(",", cash.Blockers));
        var review = service.Review(owners.Capture(), context.WorkspaceId, request, new(cash.Value!.AuthorityDigest, cash.Value.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var original = store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        string before = JsonSerializer.Serialize(store.Get(context.WorkspaceId).Value);
        string path = ConfiguredSkillSettingsPath(context, method);
        int changes = 0;
        void Change()
        {
            var document = XDocument.Load(path);
            ConfiguredSkillProfile(document, method).SetElementValue("maxskillratingcreate", 7);
            document.Save(path);
            changes++;
        }
        if (duringCommit) probe.AfterFlush = Change; else Change();
        Assert.IsNull(service.Apply(owners.Capture(), Command(review.Value!)).Value);
        Assert.AreEqual(1, changes);
        Assert.AreEqual(0, probe.Replacements);
        Assert.AreEqual(before, JsonSerializer.Serialize(new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value));
        Assert.AreEqual(0, owners.ActiveLeases);
        // A current source change revokes Apply, not the exact historical rules witness.
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, captured.Value!, out var archive));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var coldBootstrap, out var coldWitness));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, coldBootstrap, coldWitness, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
    }

    [TestMethod]
    public void Configured_creation_skill_limits_keep_owner_and_source_ABA_closed()
    {
        using var context = ReadyContext.Create(false, rankAssignments: ConfiguredSkillRanks(),
            amendSettings: settings => SetConfiguredSkillCaps(settings, 8, 7));
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var stamp = owners.Capture();
        var inputs = LoadConsumers(context, includeSkillPurchases: true);
        var request = ConfiguredSkillRequest(inputs, 8, 7);
        var cash = service.LoadStartingCash(stamp, context.WorkspaceId, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, cash.Outcome, string.Join(",", cash.Blockers));
        var review = service.Review(stamp, context.WorkspaceId, request, new(cash.Value!.AuthorityDigest, cash.Value.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var command = Command(review.Value!);
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        owners.Set(new OwnerScope("configured-skills-other-owner"));
        Assert.IsNull(service.Apply(stamp, command).Value);
        owners.Set(OwnerScope.LocalSingleUser);
        Assert.IsNull(service.Apply(stamp, command).Value);
        string root = Path.Combine(context.Directory, "source");
        string path = ConfiguredSkillSettingsPath(context, CharacterCreationBuildMethods.Priority);
        byte[] bytes = File.ReadAllBytes(path);
        DateTime time = File.GetLastWriteTimeUtc(path);
        bool changed = false;
        var resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(root, root, null), file =>
        {
            if (changed || file != path) return;
            changed = true;
            File.AppendAllText(path, "\n<!-- configured limit intermediate B -->");
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, time);
        });
        Assert.IsNull(new OwnerBoundCharacterCreationCompletionService(context.Store, owners, resolver, context.Queries)
            .Apply(owners.Capture(), command).Value);
        Assert.IsTrue(changed);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    private static IReadOnlyDictionary<string, string> ConfiguredSkillRanks() => new Dictionary<string, string>
    {
        [CharacterCreationPriorityCategoryIds.Heritage] = "C",
        [CharacterCreationPriorityCategoryIds.Talent] = "E",
        [CharacterCreationPriorityCategoryIds.Attributes] = "B",
        [CharacterCreationPriorityCategoryIds.Skills] = "A",
        [CharacterCreationPriorityCategoryIds.Resources] = "D"
    };

    private static void SetConfiguredSkillCaps(XElement profile, int active, int knowledge)
    {
        profile.SetElementValue("maxskillratingcreate", active);
        profile.SetElementValue("maxknowledgeskillratingcreate", knowledge);
        profile.Elements("maxskillrating").Remove();
        profile.Elements("maxknowledgeskillrating").Remove();
    }

    private static string ConfiguredSkillSettingsPath(ReadyContext context, string method)
    {
        Assert.IsTrue(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(method, out string id));
        return Path.Combine(context.Directory, "source", "data", CharacterCreationBootstrapProfiles.SettingsSourceFile(id));
    }

    private static XElement ConfiguredSkillProfile(XDocument document, string method)
    {
        Assert.IsTrue(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(method, out string id));
        return document.Root!.Element("settings")!.Elements("setting").Single(row => row.Element("id")!.Value == id);
    }

    private static CharacterCreationCompletionReviewRequest ConfiguredSkillRequest(
        CharacterCreationCompletionReviewInputs inputs, int active, int knowledge)
    {
        var request = Request(inputs, CharacterCreationPolicyRequirement.SkillLimits);
        var pistols = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
        var academic = inputs.Skills.Authority.KnowledgeSkills.First(row => row.Category == "Academic");
        var athletics = inputs.Skills.Authority.SkillGroups.Single(row => row.Name == "Athletics");
        var allocations = request.Selections.SkillAllocations.ToList();
        if (active > 0) allocations.Add(new(pistols.SourceSkillId, pistols.Kind, active, pistols.Specializations.First().OptionId, false));
        if (knowledge > 0) allocations.Add(new(academic.SourceSkillId, academic.Kind, knowledge, null, false));
        return request with { Selections = request.Selections with
        {
            SkillAllocations = allocations.ToArray(),
            GroupAllocations = active > 0
                ? [.. request.Selections.GroupAllocations, new(athletics.GroupId, active)]
                : request.Selections.GroupAllocations,
            // A new group raises every member, including protected zero/default choices.
            // Grant exactly that closure; the individual additions need no existing-row unlock.
            ChangeableSkillTargets = active > 0
                ? [.. request.Selections.ChangeableSkillTargets,
                    new(CharacterCreationSkillCompletionKinds.Group, athletics.GroupId),
                    .. athletics.MemberSkillSourceIds.Select(id =>
                        new CharacterCreationSkillCompletionTarget(CharacterCreationSkillKinds.Active, id))]
                : request.Selections.ChangeableSkillTargets
        } };
    }
}
