using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationSkillsServiceTests
{
    private const string ReadyXml = "<character><name>Skills Runner</name><alias>Priority</alias>"
                                    + "<buildmethod>Priority</buildmethod><created>false</created>"
                                    + "<karma>25</karma><nuyen>0</nuyen></character>";

    [TestMethod]
    [DataRow("Magician", "Compiling", "Tasking")]
    [DataRow("Mystic Adept", "Compiling", "Tasking")]
    [DataRow("Adept", "Spellcasting", "Sorcery")]
    [DataRow("Aspected Magician", "Spellcasting", "Sorcery")]
    [DataRow("Technomancer", "Spellcasting", "Sorcery")]
    public void Actual_talent_rejects_skill_and_group_purchases_outside_source_unlocks(
        string talentValue, string forbiddenSkill, string forbiddenGroup)
    {
        WithRealTalentSkills(talentValue, (directory, store, resolver, service, id) =>
        {
            var state = Load(service, id);
            Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
            var before = store.Get(id).Value!;
            var native = state.Authority.KnowledgeSkills.First(item => item.CanBeNativeLanguage);
            var skill = state.Authority.ActiveSkills.Single(item => item.Name == forbiddenSkill);
            var group = state.Authority.SkillGroups.Single(item => item.Name == forbiddenGroup);
            CharacterCreationSkillAllocation language = new(native.SourceSkillId,
                CharacterCreationSkillKinds.Knowledge, null, null, true);
            CharacterCreationSkillAllocation[] forbidden =
                [language, new(skill.SourceSkillId, CharacterCreationSkillKinds.Active, 1, null, false)];
            var preview = service.Preview(new(state.Binding, forbidden, [])).Value!;
            Assert.IsFalse(preview.CanConfirm,
                $"{talentValue} must not purchase {forbiddenSkill} without a source-owned unlock.");
            Assert.IsFalse(preview.Skills.Single(item => item.SourceSkillId == skill.SourceSkillId).IsEnabled);
            CollectionAssert.Contains(preview.Blockers.ToArray(), CharacterCreationSkillsBlockers.TalentAccessRequired);
            var rejected = service.Confirm(new(preview.Binding, forbidden, [], preview.PreviewDigest,
                "forbidden-talent-skill", ExplicitlyConfirmed: true));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, rejected.Outcome);

            var groupPreview = service.Preview(new(state.Binding, [language], [new(group.GroupId, 1)])).Value!;
            Assert.IsFalse(groupPreview.CanConfirm,
                $"{talentValue} must not purchase {forbiddenGroup} without a source-owned unlock.");
            Assert.IsFalse(groupPreview.SkillGroups.Single(item => item.GroupId == group.GroupId).IsEnabled);
            CollectionAssert.Contains(groupPreview.Blockers.ToArray(), CharacterCreationSkillsBlockers.TalentAccessRequired);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, service.Confirm(new(groupPreview.Binding,
                [language], [new(group.GroupId, 1)], groupPreview.PreviewDigest, "forbidden-talent-group", true)).Outcome);
            Assert.AreEqual(before.ContentRevision, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(before.Document.Content, store.Get(id).Value!.Document.Content);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);

            var access = state.Authority.TalentAccess!;
            Assert.IsNotNull(access);
            CharacterCreationTalentSkillAccess?[] forgedAccess =
            [
                null,
                access with { Schema = "invented-policy-version" },
                access with { Talent = null! },
                access with { AllowedActiveSkillSourceIds = access.AllowedActiveSkillSourceIds.Append(skill.SourceSkillId)
                    .OrderBy(item => item, StringComparer.Ordinal).ToArray() },
                access with { AllowedSkillGroupIds = access.AllowedSkillGroupIds.Append(group.GroupId)
                    .OrderBy(item => item, StringComparer.Ordinal).ToArray() },
                access with { PrerequisiteDraftDigest = Digest('f') },
                access with { SelectedGroupNames = ["invented-aspect"] },
                access with { SelectedGroupNames = null! },
                access with { Talent = access.Talent with { GrantedQualitySources = [] } },
                access with { Talent = access.Talent with { GrantedQualitySources =
                    access.Talent.GrantedQualitySources!.Select(item => item with { Page = "999" }).ToArray() } }
            ];
            foreach (var forgery in forgedAccess)
            {
                var rehashed = forgery is null ? null : forgery with
                { AccessDigest = CharacterCreationSkillsDigest.Compute(forgery with { AccessDigest = string.Empty }) };
                var authority = Reseal(state.Authority with { TalentAccess = rehashed });
                var altered = state with { Authority = authority,
                    Binding = state.Binding with { SkillsAuthorityDigest = authority.AuthorityDigest }, SnapshotDigest = string.Empty };
                altered = altered with { SnapshotDigest = CharacterCreationSkillsDigest.Compute(altered) };
                Assert.IsFalse(CharacterCreationSkillsDraftIntegrity.IsValidStateProjection(altered),
                    "Outer digest recomputation cannot authorize altered source access.");
            }

            // A correct restriction must still permit source-enabled purchases.
            var allowed = state.Authority.ActiveSkills.Single(item => item.Name ==
                (talentValue == "Technomancer" ? "Compiling" : "Arcana"));
            int minimum = state.Skills.SingleOrDefault(item => item.SourceSkillId == allowed.SourceSkillId)?.GrantedRating ?? 0;
            CharacterCreationSkillAllocation[] legal =
                [language, new(allowed.SourceSkillId, CharacterCreationSkillKinds.Active, minimum + 1, null, false)];
            var legalPreview = service.Preview(new(state.Binding, legal, [])).Value!;
            Assert.IsTrue(legalPreview.CanConfirm, string.Join(",", legalPreview.Blockers));
            var command = new CharacterCreationSkillsConfirmRequest(legalPreview.Binding, legal, [],
                legalPreview.PreviewDigest, "legal-talent-skill", true);
            var committed = service.Confirm(command);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, committed.Outcome, string.Join(",", committed.Blockers));
            var coldStore = new FileWorkspaceStore(directory);
            var coldService = new CharacterCreationSkillsService(coldStore, resolver);
            var cold = Load(coldService, id);
            Assert.IsTrue(cold.CanEdit && CharacterCreationSkillsDraftIntegrity.IsValidStateProjection(cold));
            Assert.AreEqual(access.AccessDigest, cold.Authority.TalentAccess!.AccessDigest);
            Assert.AreEqual(minimum + 1, cold.Skills.Single(item => item.SourceSkillId == allowed.SourceSkillId).Rating);
            Assert.AreEqual(committed.Value!.ReceiptDigest, coldService.Confirm(command).Value!.ReceiptDigest);
            Assert.AreEqual(committed.Value.ContentRevision, coldStore.Get(id).Value!.ContentRevision);

            // With the same runner and Skills source, losing the separate Talent
            // source authority must not fall back to either a permissive catalog
            // or a cached permission packet, nor replay a new mutation.
            var context = resolver.TryCreateContext(coldStore.Get(id).Value!.Document.Content)!;
            Assert.IsTrue(context.TryResolveCreationPrerequisiteAuthority(out var priorityAuthority));
            Assert.IsTrue(context.TryResolveCreationSkillsAuthority(out var skillsAuthority));
            var missingTalentResolver = new CharacterCreationAttributesServiceTests.StubSourceResolver(
                priorityAuthority, skillsAuthority);
            var unavailable = new CharacterCreationSkillsService(coldStore, missingTalentResolver);
            var unavailableState = Load(unavailable, id);
            Assert.IsFalse(unavailableState.CanEdit);
            CollectionAssert.Contains(unavailableState.Blockers.ToArray(), CharacterCreationSkillsBlockers.TalentAccessRequired);
            var lostSourcePreview = unavailable.Preview(new(cold.Binding, legal, []));
            Assert.IsFalse(lostSourcePreview.Value?.CanConfirm == true);
            Assert.AreEqual(committed.Value.ContentRevision, coldStore.Get(id).Value!.ContentRevision);
        });
    }

    [TestMethod]
    [DataRow("Spellcasting", "Sorcery")]
    [DataRow("Compiling", "Tasking")]
    public void Actual_mundane_priority_has_no_implicit_special_skill_access(string skillName, string groupName)
    {
        WithRealTalentSkills("Mundane", (_, store, _, service, id) =>
        {
            var state = Load(service, id);
            Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
            Assert.IsNull(state.Authority.TalentAccess);
            var skill = state.Authority.ActiveSkills.Single(item => item.Name == skillName);
            var group = state.Authority.SkillGroups.Single(item => item.Name == groupName);
            var language = state.Authority.KnowledgeSkills.First(item => item.CanBeNativeLanguage);
            var allocations = new[]
            {
                new CharacterCreationSkillAllocation(language.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true),
                new CharacterCreationSkillAllocation(skill.SourceSkillId, CharacterCreationSkillKinds.Active, 1, null, false)
            };
            var revision = store.Get(id).Value!.ContentRevision;
            var preview = service.Preview(new(state.Binding, allocations, [])).Value!;
            Assert.IsFalse(preview.CanConfirm);
            CollectionAssert.Contains(preview.Blockers.ToArray(), CharacterCreationSkillsBlockers.TalentAccessRequired);
            Assert.IsFalse(service.Preview(new(state.Binding, [allocations[0]], [new(group.GroupId, 1)])).Value!.CanConfirm);
            var ordinary = state.Authority.ActiveSkills.Single(item => item.Name == "Running");
            CharacterCreationSkillAllocation[] legal =
                [allocations[0], new(ordinary.SourceSkillId, CharacterCreationSkillKinds.Active, 1, null, false)];
            var legalPreview = service.Preview(new(state.Binding, legal, [])).Value!;
            Assert.IsTrue(legalPreview.CanConfirm);
            Assert.AreEqual(revision, store.Get(id).Value!.ContentRevision);
            Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(state.Authority).Contains("TalentAccess", StringComparison.Ordinal),
                "Ordinary authority bytes must not acquire an irrelevant optional policy field.");
            var command = new CharacterCreationSkillsConfirmRequest(legalPreview.Binding, legal, [],
                legalPreview.PreviewDigest, "mundane-skill-access", true);
            var confirmed = service.Confirm(command);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome);
            var reopened = Load(service, id);
            Assert.IsTrue(reopened.CanEdit && CharacterCreationSkillsDraftIntegrity.IsValidStateProjection(reopened));
            Assert.IsNull(reopened.Authority.TalentAccess);
            Assert.AreEqual(state.Authority.AuthorityDigest, reopened.Authority.AuthorityDigest);
            Assert.AreEqual(confirmed.Value!.ReceiptDigest, service.Confirm(command).Value!.ReceiptDigest);
        });
    }

    [TestMethod]
    [DataRow("Magician")]
    [DataRow("Aspected Magician")]
    [DataRow("Adept")]
    [DataRow("Mystic Adept")]
    [DataRow("Technomancer")]
    public void Actual_priority_talent_grants_are_free_visible_and_preserved_on_cold_reopen(string talentValue)
    {
        WithRealTalentSkills(talentValue, (directory, store, resolver, service, id) =>
        {
            var state = Load(service, id);
            Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
            var plan = state.PrerequisiteDraft!.TalentSelection!.GrantPlan!;
            Assert.IsNotNull(plan);
            Assert.IsTrue(plan.ActiveSkills.Count + plan.SkillGroups.Count > 0);
            Assert.HasCount(plan.ActiveSkills.Count, state.Skills);
            Assert.HasCount(plan.SkillGroups.Count, state.SkillGroups);
            Assert.AreEqual(0m, state.ActiveSkillPointBudget.Used);
            Assert.AreEqual(0m, state.SkillGroupPointBudget.Used);
            Assert.IsTrue(CharacterCreationSkillsDraftIntegrity.IsValidStateProjection(state));
            var native = state.Authority.KnowledgeSkills.First(item => item.CanBeNativeLanguage);
            CharacterCreationSkillAllocation[] allocations =
                [new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
            CharacterCreationSkillGroupAllocation[] groups = [];
            if (plan.ActiveSkills.Count > 0)
            {
                var grant = plan.ActiveSkills[0];
                allocations = [.. allocations, new(grant.SourceId, CharacterCreationSkillKinds.Active,
                    grant.BaseRating + 1, null, false)];
            }
            else
            {
                var grant = plan.SkillGroups[0];
                var group = state.Authority.SkillGroups.Single(item => item.Name == grant.CanonicalName);
                groups = [new(group.GroupId, grant.BaseRating + 1)];
            }
            var preview = service.Preview(new(state.Binding, allocations, groups)).Value!;
            Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
            Assert.AreEqual(plan.ActiveSkills.Count > 0 ? 1m : 0m, preview.ActiveSkillPointBudget.Used);
            Assert.AreEqual(plan.SkillGroups.Count > 0 ? 1m : 0m, preview.SkillGroupPointBudget.Used);
            foreach (var grant in plan.ActiveSkills)
            {
                var skill = preview.Skills.Single(item => item.SourceSkillId == grant.SourceId);
                Assert.IsTrue(skill.EffectiveRating >= grant.BaseRating);
                Assert.IsTrue(grant.SourceAnchorIds.All(skill.SourceAnchorIds.Contains));
            }
            foreach (var grant in plan.SkillGroups)
            {
                var group = preview.SkillGroups.Single(item => item.Name == grant.CanonicalName);
                Assert.AreEqual(grant.BaseRating + 1, group.Rating);
                Assert.IsTrue(grant.SourceAnchorIds.All(group.SourceAnchorIds.Contains));
            }
            string originalXml = store.Get(id).Value!.Document.Content;
            var command = new CharacterCreationSkillsConfirmRequest(preview.Binding, allocations, groups,
                preview.PreviewDigest, "actual-talent-grants", ExplicitlyConfirmed: true);
            var confirmed = service.Confirm(command);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome,
                string.Join(",", confirmed.Blockers));
            var coldStore = new FileWorkspaceStore(directory);
            var coldService = new CharacterCreationSkillsService(coldStore, resolver);
            var cold = Load(coldService, id);
            Assert.IsTrue(cold.CanEdit, string.Join(",", cold.Blockers));
            Assert.IsTrue(CharacterCreationSkillsDraftIntegrity.IsValidStateProjection(cold));
            Assert.AreEqual(CharacterCreationSkillsDigest.Compute(preview.Skills),
                CharacterCreationSkillsDigest.Compute(cold.Skills));
            Assert.AreEqual(CharacterCreationSkillsDigest.Compute(preview.SkillGroups),
                CharacterCreationSkillsDigest.Compute(cold.SkillGroups));
            Assert.AreEqual(originalXml, coldStore.Get(id).Value!.Document.Content,
                "Confirming a wizard draft must not apply its effects to XML yet.");
            long revision = coldStore.Get(id).Value!.ContentRevision;
            Assert.AreEqual(confirmed.Value!.ReceiptDigest, coldService.Confirm(command).Value!.ReceiptDigest);
            Assert.AreEqual(revision, coldStore.Get(id).Value!.ContentRevision);

            // Leaving out a free row resets only purchased increases, not the grant.
            var reset = coldService.Preview(new(cold.Binding,
                [new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true)], [])).Value!;
            Assert.IsTrue(reset.CanConfirm, string.Join(",", reset.Blockers));
            Assert.AreEqual(0m, reset.ActiveSkillPointBudget.Used);
            Assert.AreEqual(0m, reset.SkillGroupPointBudget.Used);
            foreach (var grant in plan.ActiveSkills)
                Assert.AreEqual(grant.BaseRating, reset.Skills.Single(item => item.SourceSkillId == grant.SourceId).Rating);
            foreach (var grant in plan.SkillGroups)
                Assert.AreEqual(grant.BaseRating, reset.SkillGroups.Single(item => item.Name == grant.CanonicalName).Rating);

            CharacterCreationSkillAllocation[] belowMinimum = allocations;
            CharacterCreationSkillGroupAllocation[] belowMinimumGroups = groups;
            if (plan.ActiveSkills.Count > 0)
                belowMinimum = [new(plan.ActiveSkills[0].SourceId, CharacterCreationSkillKinds.Active,
                    plan.ActiveSkills[0].BaseRating - 1, null, false),
                    new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
            else
                belowMinimumGroups = [groups[0] with { Rating = plan.SkillGroups[0].BaseRating - 1 }];
            var invalid = coldService.Preview(new(cold.Binding, belowMinimum, belowMinimumGroups));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, invalid.Outcome);
            Assert.IsTrue(invalid.Blockers.Contains(plan.ActiveSkills.Count > 0
                ? CharacterCreationSkillsBlockers.RatingInvalid : CharacterCreationSkillsBlockers.GroupInvalid));
            Assert.AreEqual(revision, coldStore.Get(id).Value!.ContentRevision);

            // Rehashing a forged free rating does not give it source authority.
            var draft = cold.PendingDraft!;
            var forged = draft with
            {
                Skills = draft.Skills.Select(item => item.GrantedRating > 0
                    ? item with { GrantedRating = item.GrantedRating + 1 } : item).ToArray(),
                SkillGroups = draft.SkillGroups.Select(item => item.GrantedRating > 0
                    ? item with { GrantedRating = item.GrantedRating + 1 } : item).ToArray()
            };
            forged = forged with { DraftDigest = CharacterCreationSkillsDraftIntegrity.ComputeDigest(forged) };
            Assert.IsFalse(CharacterCreationSkillsDraftIntegrity.IsStructurallyValidPending(forged, id,
                cold.Binding.ContentRevision, cold.Binding.RawCharacterXmlDigest, cold.PrerequisiteDraft!,
                cold.AttributesDraft!, cold.Authority, cold.Binding.ContributionInputsDigest));
            // Hostile persisted arrays must fail closed, not throw while trying
            // to enumerate a forged free-grant projection.
            CharacterCreationSkillsDraft[] malformed =
            [
                draft with { Skills = [null!] },
                draft with { SkillGroups = [null!] },
                draft with { Skills = draft.Skills.Select(item => item with { SourceAnchorIds = null! }).ToArray() },
                draft with
                {
                    Skills = draft.Skills.Select(item => item.GrantedRating > 0
                        ? item with { PointCost = item.PointCost + 1 } : item).ToArray(),
                    SkillGroups = draft.SkillGroups.Select(item => item.GrantedRating > 0
                        ? item with { PointCost = item.PointCost + 1 } : item).ToArray()
                }
            ];
            foreach (var candidate in malformed)
            {
                var rehashed = candidate with { DraftDigest = CharacterCreationSkillsDraftIntegrity.ComputeDigest(candidate) };
                Assert.IsFalse(CharacterCreationSkillsDraftIntegrity.IsStructurallyValidPending(rehashed, id,
                    cold.Binding.ContentRevision, cold.Binding.RawCharacterXmlDigest, cold.PrerequisiteDraft!,
                    cold.AttributesDraft!, cold.Authority, cold.Binding.ContributionInputsDigest));
            }
            var ordinary = cold.Skills.Single(item => item.IsNativeLanguage);
            Assert.AreEqual(0, ordinary.GrantedRating);
            Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(ordinary)
                .Contains(nameof(CharacterCreationSkillProjection.GrantedRating), StringComparison.Ordinal),
                "Zero grants must preserve the existing non-grant JSON shape.");
        });
    }

    private static void WithRealTalentSkills(string talentValue,
        Action<string, FileWorkspaceStore, ICharacterSourceDataResolver,
            ICharacterCreationSkillsService, CharacterWorkspaceId> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"chummer-real-talent-skills-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            DirectoryInfo? core = new(AppContext.BaseDirectory);
            while (core is not null && !File.Exists(Path.Combine(core.FullName, "Chummer", "data", "priorities.xml")))
                core = core.Parent;
            Assert.IsNotNull(core);
            ICharacterSourceDataResolver resolver = new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(core.FullName, core.FullName, null));
            ICharacterFileQueries queries = new XmlCharacterFileQueries(new CharacterFileService());
            var store = new FileWorkspaceStore(directory);
            var codec = new Sr5WorkspaceCodec(queries,
                new XmlCharacterSectionQueries(new CharacterSectionService(resolver)),
                new XmlCharacterMetadataCommands(new CharacterFileService()));
            var bootstrap = new CharacterCreationBootstrapService(store,
                new RulesetWorkspaceCodecResolver([codec]), queries, resolver);
            var created = bootstrap.Create(new(CharacterCreationBootstrapSchemas.RequestV1,
                CharacterCreationBootstrapStages.AwaitingFoundationSelection, RulesetDefaults.Sr5,
                "Talent skills runner", "Talent", CharacterCreationBuildMethods.Priority,
                CharacterCreationBootstrapProfiles.PrioritySettingsProfileId));
            Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, created.Outcome,
                string.Join(",", created.Blockers));
            var id = created.Value!.WorkspaceId;
            var prerequisites = new CharacterCreationPrerequisiteService(store, queries, resolver);
            var state = prerequisites.Load(new(id)).Value!;
            string heritageRank = talentValue == "Mundane" ? "D" : "E";
            string talentRank = talentValue == "Mundane" ? "E" : "B";
            var ranks = CharacterCreationPrerequisiteServiceTests.Assign(heritageRank, talentRank, "A", "C", talentValue == "Mundane" ? "B" : "D");
            var heritage = state.Authority.Options.Single(item => item.CategoryId == "heritage" && item.Rank == heritageRank)
                .HeritageOptions.First(item => item.MetatypeName == "Human" && item.MetavariantSourceId is null && item.IsEnabled);
            var talent = state.Authority.Options.Single(item => item.CategoryId == "talent" && item.Rank == talentRank)
                .TalentOptions.First(item => item.Value == talentValue && item.IsEnabled);
            string[] skills = talent.ActiveSkillGrant?.Options.Where(item => item.IsEnabled)
                .Take(talent.ActiveSkillGrant.Quantity).Select(item => item.SelectionId).ToArray() ?? [];
            string[] groups = talent.SkillGroupGrant?.Options.Take(talent.SkillGroupGrant.Quantity)
                .Select(item => item.SelectionId).ToArray() ?? [];
            var preview = prerequisites.Preview(new(state.Binding, ranks)
            {
                HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId,
                TalentActiveSkillSelectionIds = skills, TalentSkillGroupSelectionIds = groups
            }).Value!;
            Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
            var confirmed = prerequisites.Confirm(new(preview.Binding, ranks, preview.PreviewDigest, true)
            {
                HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId,
                TalentActiveSkillSelectionIds = skills, TalentSkillGroupSelectionIds = groups
            });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome,
                string.Join(",", confirmed.Blockers));
            var attributes = new CharacterCreationAttributesService(store, resolver);
            var attributeState = attributes.Load(new(id)).Value!;
            var attributePreview = attributes.Preview(new(attributeState.Binding, [])).Value!;
            Assert.IsTrue(attributePreview.CanConfirm, string.Join(",", attributePreview.Blockers));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                attributes.Confirm(new(attributePreview.Binding, [], attributePreview.PreviewDigest, true)).Outcome);
            action(directory, store, resolver, new CharacterCreationSkillsService(store, resolver), id);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Preview_uses_selected_priority_and_unaugmented_attributes_plus_authoritative_contributions()
    {
        WithReadySkills((_, service, id, _) =>
        {
            CharacterCreationSkillsState state = Load(service, id);
            Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
            Assert.AreEqual(28m, state.ActiveSkillPointBudget.Total);
            Assert.AreEqual(2m, state.SkillGroupPointBudget.Total);
            Assert.AreEqual(12m, state.KnowledgeSkillPointBudget.Total);
            Assert.AreEqual(3, state.IntuitionUnaugmented);
            Assert.AreEqual(2, state.LogicUnaugmented);
            Assert.AreEqual(2, state.KnowledgePointContributions.Single().Points);

            CharacterCreationSkillAllocation[] allocations =
            [
                new(ActiveIds[0], CharacterCreationSkillKinds.Active, 6, "spec-0", false),
                new(KnowledgeIds[0], CharacterCreationSkillKinds.Knowledge, 6, null, false),
                new(KnowledgeIds[1], CharacterCreationSkillKinds.Knowledge, 6, null, false),
                new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)
            ];
            CharacterCreationSkillsPreview preview = service.Preview(
                new CharacterCreationSkillsPreviewRequest(state.Binding, allocations, [])).Value!;
            Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
            Assert.AreEqual(7m, preview.ActiveSkillPointBudget.Used);
            Assert.AreEqual(12m, preview.KnowledgeSkillPointBudget.Used);
            Assert.AreEqual(0, preview.KnowledgePointOverflowToActive);
            CharacterCreationSkillProjection native = preview.Skills.Single(skill => skill.IsNativeLanguage);
            Assert.IsNull(native.Rating);
            Assert.IsNull(native.EffectiveRating);
        });
    }

    [TestMethod]
    public void Preview_digest_is_order_independent_and_identity_bound()
    {
        WithReadySkills((_, service, id, _) =>
        {
            CharacterCreationSkillsState state = Load(service, id);
            CharacterCreationSkillAllocation[] forward =
            [
                new(ActiveIds[0], CharacterCreationSkillKinds.Active, 2, null, false),
                new(KnowledgeIds[0], CharacterCreationSkillKinds.Knowledge, 2, null, false)
            ];
            CharacterCreationSkillsPreview first = service.Preview(
                new CharacterCreationSkillsPreviewRequest(state.Binding, forward, [])).Value!;
            CharacterCreationSkillsPreview second = service.Preview(
                new CharacterCreationSkillsPreviewRequest(state.Binding, forward.Reverse().ToArray(), [])).Value!;
            Assert.AreEqual(first.PreviewDigest, second.PreviewDigest);

            CharacterCreationFoundationResult<CharacterCreationSkillsPreview> drift = service.Preview(
                new CharacterCreationSkillsPreviewRequest(
                    state.Binding with { RuntimeDigest = Digest('9') },
                    forward,
                    []));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, drift.Outcome);
            CollectionAssert.Contains(drift.Blockers.ToList(), CharacterCreationSkillsBlockers.RuntimeDrift);
        });
    }

    [TestMethod]
    public void Illegal_rating_specialization_group_native_and_overspend_fail_closed()
    {
        WithReadySkills((_, service, id, authority) =>
        {
            CharacterCreationSkillsState state = Load(service, id);
            CharacterCreationSkillGroupCatalogEntry group = authority.SkillGroups.Single();
            CharacterCreationSkillAllocation[] allocations =
            [
                new(ActiveIds[0], CharacterCreationSkillKinds.Active, 7, "forged-specialization", false),
                .. ActiveIds.Skip(1).Select(skill => new CharacterCreationSkillAllocation(
                    skill, CharacterCreationSkillKinds.Active, 6, null, false)),
                new(KnowledgeIds[0], CharacterCreationSkillKinds.Knowledge, 1, null, true),
                new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true),
                new(KnowledgeIds[1], CharacterCreationSkillKinds.Knowledge, null, null, true)
            ];
            CharacterCreationFoundationResult<CharacterCreationSkillsPreview> invalid = service.Preview(
                new CharacterCreationSkillsPreviewRequest(
                    state.Binding,
                    allocations,
                    [new CharacterCreationSkillGroupAllocation(group.GroupId, 7)]));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, invalid.Outcome);
            string[] expected =
            [
                CharacterCreationSkillsBlockers.RatingInvalid,
                CharacterCreationSkillsBlockers.SpecializationInvalid,
                CharacterCreationSkillsBlockers.GroupInvalid,
                CharacterCreationSkillsBlockers.GroupBroken,
                CharacterCreationSkillsBlockers.NativeLanguageInvalid,
                CharacterCreationSkillsBlockers.NativeLanguageLimitExceeded,
                CharacterCreationSkillsBlockers.ActiveBudgetExceeded
            ];
            foreach (string blocker in expected)
                CollectionAssert.Contains(invalid.Blockers.ToList(), blocker);
        });
    }

    [TestMethod]
    public void Movement_gated_skill_is_visible_but_rejected_for_incompatible_metatype()
    {
        WithReadySkillsContext((store, service, id, skillsAuthority, prerequisiteAuthority, resolver) =>
        {
            CharacterCreationSkillsState state = Load(service, id);
            Assert.IsTrue(state.MovementCapability.Ground);
            Assert.IsTrue(state.MovementCapability.Swim);
            Assert.IsFalse(state.MovementCapability.Fly);
            CharacterCreationSkillCatalogEntry flight = state.Authority.ActiveSkills.Single(skill =>
                skill.RequiresFlyMovement);

            CharacterCreationFoundationResult<CharacterCreationSkillsPreview> result = service.Preview(
                new CharacterCreationSkillsPreviewRequest(
                    state.Binding,
                    [
                        new CharacterCreationSkillAllocation(
                            flight.SourceSkillId,
                            CharacterCreationSkillKinds.Active,
                            1,
                            null,
                            false),
                        new CharacterCreationSkillAllocation(
                            LanguageId,
                            CharacterCreationSkillKinds.Knowledge,
                            null,
                            null,
                            true)
                    ],
                    []));

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
            CollectionAssert.Contains(
                result.Blockers.ToList(),
                CharacterCreationSkillsBlockers.MovementRequirementUnmet);

            var restarted = new CharacterCreationSkillsService(store, resolver);
            CharacterCreationSkillsState reloaded = Load(restarted, id);
            Assert.AreEqual(state.MovementCapability, reloaded.MovementCapability);
            Assert.AreEqual(
                state.Binding.PrerequisiteAuthorityDigest,
                reloaded.Binding.PrerequisiteAuthorityDigest);

            CharacterCreationPrerequisiteAuthority driftedPrerequisite = prerequisiteAuthority with
            {
                Options = prerequisiteAuthority.Options.Select(option => option with
                {
                    HeritageOptions = option.HeritageOptions.Select(heritage => heritage with
                    {
                        Movement = heritage.Movement with
                        {
                            Walk = heritage.Movement.Walk with
                            {
                                Fly = heritage.Movement.Walk.Fly + 1m
                            }
                        }
                    }).ToArray()
                }).ToArray(),
                AuthorityDigest = string.Empty
            };
            driftedPrerequisite = driftedPrerequisite with
            {
                AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(driftedPrerequisite)
            };
            Assert.AreNotEqual(
                prerequisiteAuthority.AuthorityDigest,
                driftedPrerequisite.AuthorityDigest);

            var drifted = new CharacterCreationSkillsService(
                store,
                CreateResolver(driftedPrerequisite, skillsAuthority));
            CharacterCreationFoundationResult<CharacterCreationSkillsState> driftedState =
                drifted.Load(new(id));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, driftedState.Outcome);
            Assert.IsNotNull(driftedState.Value);
            Assert.IsFalse(driftedState.Value.CanEdit);
            CollectionAssert.Contains(
                driftedState.Blockers.ToList(),
                CharacterCreationSkillsBlockers.PrerequisiteSourceDrift);
            CharacterCreationFoundationResult<CharacterCreationSkillsPreview> stalePreview = drifted.Preview(
                new CharacterCreationSkillsPreviewRequest(state.Binding, [], []));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, stalePreview.Outcome);
            CollectionAssert.Contains(
                stalePreview.Blockers.ToList(),
                CharacterCreationSkillsBlockers.PrerequisiteSourceDrift);
        });
    }

    [TestMethod]
    public void Group_retains_canonical_membership_when_only_one_member_is_movement_enabled()
    {
        WithReadySkills((_, service, id, authority) =>
        {
            CharacterCreationSkillsState state = Load(service, id);
            CharacterCreationSkillGroupCatalogEntry group = authority.SkillGroups.Single();
            Assert.HasCount(2, group.MemberSkillSourceIds);
            Assert.AreEqual(1, group.MemberSkillSourceIds.Count(memberId =>
                authority.ActiveSkills.Single(skill => skill.SourceSkillId == memberId) is { } skill
                && (!skill.RequiresFlyMovement || state.MovementCapability.Fly)));

            CharacterCreationSkillsPreview preview = service.Preview(new(
                state.Binding,
                [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)],
                [new CharacterCreationSkillGroupAllocation(group.GroupId, 1)])).Value!;

            Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
            CharacterCreationSkillGroupProjection projected = preview.SkillGroups.Single();
            CollectionAssert.AreEqual(
                group.MemberSkillSourceIds.ToArray(),
                projected.MemberSkillSourceIds.ToArray());
        });
    }

    [TestMethod]
    public void Movement_capability_uses_any_numeric_rate_and_special_disables_every_domain()
    {
        var mixedRates = new CharacterCreationMetatypeMovementProjection(
            new CharacterCreationMetatypeMovementRate(0m, 0m, 0m),
            new CharacterCreationMetatypeMovementRate(0m, 0m, 1m),
            new CharacterCreationMetatypeMovementRate(1m, 0m, 0m));
        WithReadySkillsContext((_, service, id, _) =>
        {
            CharacterCreationSkillsState state = Load(service, id);
            Assert.IsTrue(state.MovementCapability.Ground);
            Assert.IsFalse(state.MovementCapability.Swim);
            Assert.IsTrue(state.MovementCapability.Fly);
        }, prerequisiteTransform: authority => WithHeritageMovement(authority, mixedRates));

        WithReadySkillsContext((_, service, id, _) =>
        {
            CharacterCreationSkillsState state = Load(service, id);
            Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
            Assert.IsTrue(state.PrerequisiteDraft!.HeritageSelection!.Movement.IsSpecial);
            Assert.AreEqual(
                new CharacterCreationMovementCapability(false, false, false),
                state.MovementCapability);
        }, prerequisiteTransform: authority => WithHeritageMovement(
            authority,
            CharacterCreationMetatypeMovementProjection.Special));
    }

    [TestMethod]
    public void Preview_without_prerequisite_or_attribute_draft_is_blocked()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"chummer-skills-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            CharacterCreationPrerequisiteAuthority prerequisite = CreatePrerequisiteAuthority();
            CharacterCreationSkillsAuthority skills = CreateSkillsAuthority(prerequisite);
            var store = new FileWorkspaceStore(directory);
            CharacterWorkspaceId id = new("skills-empty-runner");
            Assert.IsTrue(store.CreateWorkspaceDocument(
                id,
                new WorkspaceDocument(ReadyXml, RulesetDefaults.Sr5)).Success);
            var service = new CharacterCreationSkillsService(
                store,
                CreateResolver(prerequisite, skills));
            CharacterCreationSkillsState state = Load(service, id);
            Assert.IsFalse(state.CanEdit);

            CharacterCreationFoundationResult<CharacterCreationSkillsPreview> preview = service.Preview(new(
                state.Binding,
                [],
                []));

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, preview.Outcome);
            Assert.IsNull(preview.Value);
            CollectionAssert.Contains(
                preview.Blockers.ToList(),
                CharacterCreationSkillsBlockers.PrerequisiteSourceDrift);
            CollectionAssert.Contains(
                preview.Blockers.ToList(),
                CharacterCreationSkillsBlockers.AttributesDraftRequired);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Confirm_is_atomic_replayable_after_restart_and_old_receipt_cannot_overwrite_newer_revision()
    {
        WithReadySkills((store, service, id, authority) =>
        {
            CharacterCreationSkillsState state = Load(service, id);
            CharacterCreationSkillAllocation[] firstAllocations =
            [
                new(ActiveIds[0], CharacterCreationSkillKinds.Active, 1, null, false),
                new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)
            ];
            CharacterCreationSkillsPreview firstPreview = service.Preview(
                new CharacterCreationSkillsPreviewRequest(state.Binding, firstAllocations, [])).Value!;
            var firstCommand = new CharacterCreationSkillsConfirmRequest(
                firstPreview.Binding, firstAllocations, [], firstPreview.PreviewDigest,
                "skills-command-one", ExplicitlyConfirmed: true);
            CharacterCreationSkillsReceipt first = service.Confirm(firstCommand).Value!;

            var restarted = new CharacterCreationSkillsService(
                store,
                CreateResolver(CreatePrerequisiteAuthority(), authority));
            CharacterCreationSkillsReceipt replay = restarted.Confirm(firstCommand).Value!;
            Assert.AreEqual(first.ReceiptDigest, replay.ReceiptDigest);

            CharacterCreationSkillsState newer = Load(restarted, id);
            CharacterCreationSkillAllocation[] secondAllocations =
            [
                new(ActiveIds[0], CharacterCreationSkillKinds.Active, 2, null, false),
                new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)
            ];
            CharacterCreationSkillsPreview secondPreview = restarted.Preview(
                new CharacterCreationSkillsPreviewRequest(newer.Binding, secondAllocations, [])).Value!;
            CharacterCreationSkillsReceipt second = restarted.Confirm(new(
                secondPreview.Binding, secondAllocations, [], secondPreview.PreviewDigest,
                "skills-command-two", true)).Value!;
            Assert.IsTrue(second.ContentRevision > first.ContentRevision);
            Assert.AreEqual(first.ReceiptDigest, restarted.Confirm(firstCommand).Value!.ReceiptDigest);
            Assert.AreEqual(second.ContentRevision, store.Get(id).Value!.ContentRevision);

            IReadOnlyList<CharacterCreationSkillsReceipt> ledger = store.Get(id).Value!.Document
                .AuxiliaryState.CharacterCreationSkillsReceipts!;
            Assert.IsTrue(CharacterCreationSkillsDraftIntegrity.IsValidReceiptLedger(
                ledger, id, second.ContentRevision));
            Assert.IsFalse(CharacterCreationSkillsDraftIntegrity.IsValidReceiptLedger(
                ledger.Skip(1).ToArray(), id, second.ContentRevision));
            Assert.IsFalse(CharacterCreationSkillsDraftIntegrity.IsValidReceiptLedger(
                ledger.Reverse().ToArray(), id, second.ContentRevision));
            CharacterCreationSkillsReceipt forgedGap = ledger[1] with
            {
                PreviousContentRevision = ledger[0].ContentRevision - 1,
                ReceiptDigest = string.Empty
            };
            forgedGap = forgedGap with
            {
                ReceiptDigest = CharacterCreationSkillsDigest.ComputeReceipt(forgedGap)
            };
            Assert.IsFalse(CharacterCreationSkillsDraftIntegrity.IsValidReceiptLedger(
                [ledger[0], forgedGap], id, second.ContentRevision));

            CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> conflict = restarted.Confirm(
                firstCommand with { Allocations = secondAllocations });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, conflict.Outcome);
            CollectionAssert.Contains(conflict.Blockers.ToList(), CharacterCreationSkillsBlockers.IdempotencyConflict);
        });
    }

    [TestMethod]
    public void Concurrent_same_idempotency_key_returns_one_committed_receipt_to_every_retry()
    {
        WithReadySkills((_, service, id, _) =>
        {
            CharacterCreationSkillsState state = Load(service, id);
            CharacterCreationSkillAllocation[] allocations =
            [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
            CharacterCreationSkillsPreview preview = service.Preview(new(
                state.Binding, allocations, [])).Value!;
            var command = new CharacterCreationSkillsConfirmRequest(
                preview.Binding,
                allocations,
                [],
                preview.PreviewDigest,
                "concurrent-idempotent-confirm",
                true);
            using var start = new ManualResetEventSlim(false);
            Task<CharacterCreationFoundationResult<CharacterCreationSkillsReceipt>>[] retries =
                Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
                {
                    start.Wait();
                    return service.Confirm(command);
                })).ToArray();
            start.Set();
            Task.WaitAll(retries);

            CharacterCreationFoundationResult<CharacterCreationSkillsReceipt>[] results =
                retries.Select(task => task.Result).ToArray();
            Assert.IsTrue(results.All(result =>
                result.Outcome == CharacterCreationFoundationOutcomes.Success
                && result.Value is not null),
                string.Join(" | ", results.Select(result =>
                    $"{result.Outcome}:{string.Join(',', result.Blockers)}")));
            Assert.AreEqual(
                1,
                results.Select(result => result.Value!.ReceiptDigest)
                    .Distinct(StringComparer.Ordinal).Count());
        });
    }

    [TestMethod]
    public void Explicit_confirmation_is_required_without_any_persisted_effect()
    {
        WithReadySkills((store, service, id, _) =>
        {
            CharacterCreationSkillsState state = Load(service, id);
            CharacterCreationSkillAllocation[] allocations =
            [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
            CharacterCreationSkillsPreview preview = service.Preview(new(
                state.Binding, allocations, [])).Value!;
            CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> result = service.Confirm(new(
                preview.Binding, allocations, [], preview.PreviewDigest, "not-confirmed", false));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, result.Outcome);
            CollectionAssert.Contains(
                result.Blockers.ToList(),
                CharacterCreationSkillsBlockers.ExplicitConfirmationRequired);
            Assert.IsNull(store.Get(id).Value!.Document.AuxiliaryState.CharacterCreationSkillsDraft);
        });
    }

    [TestMethod]
    public void Identical_logical_allocation_is_rejected_after_confirm()
    {
        WithReadySkills((_, service, id, _) =>
        {
            CharacterCreationSkillsState state = Load(service, id);
            CharacterCreationSkillAllocation[] allocations =
            [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
            CharacterCreationSkillsPreview preview = service.Preview(new(
                state.Binding, allocations, [])).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, service.Confirm(new(
                preview.Binding, allocations, [], preview.PreviewDigest, "first-logical-draft", true)).Outcome);

            CharacterCreationSkillsState current = Load(service, id);
            CharacterCreationFoundationResult<CharacterCreationSkillsPreview> duplicate = service.Preview(new(
                current.Binding, allocations, []));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, duplicate.Outcome);
            CollectionAssert.Contains(
                duplicate.Blockers.ToList(),
                CharacterCreationSkillsBlockers.DraftDuplicate);
        });
    }

    [TestMethod]
    public void Cross_workspace_and_stale_draft_source_identity_are_rejected_without_write()
    {
        WithReadySkills((store, service, id, _) =>
        {
            CharacterCreationSkillsState state = Load(service, id);
            CharacterCreationFoundationResult<CharacterCreationSkillsPreview> cross = service.Preview(new(
                state.Binding with { WorkspaceId = new CharacterWorkspaceId("other") }, [], []));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Missing, cross.Outcome);
            CharacterCreationFoundationResult<CharacterCreationSkillsPreview> stale = service.Preview(new(
                state.Binding with { AttributesDraftDigest = Digest('8') }, [], []));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, stale.Outcome);
            Assert.IsNull(store.Get(id).Value!.Document.AuxiliaryState.CharacterCreationSkillsDraft);
        });
    }

    [TestMethod]
    public void Injected_atomic_commit_failure_exposes_no_partial_draft_or_receipt()
    {
        var injector = new ArmedAtomicWriteFaultInjector();
        WithReadySkills((store, service, id, _) =>
        {
            CharacterCreationSkillsState state = Load(service, id);
            CharacterCreationSkillsPreview preview = service.Preview(new(
                state.Binding,
                [
                    new(ActiveIds[0], CharacterCreationSkillKinds.Active, 1, null, false),
                    new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)
                ],
                [])).Value!;
            long beforeRevision = store.Get(id).Value!.ContentRevision;
            injector.Armed = true;
            CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> result = service.Confirm(new(
                preview.Binding,
                [
                    new(ActiveIds[0], CharacterCreationSkillKinds.Active, 1, null, false),
                    new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)
                ],
                [],
                preview.PreviewDigest,
                "injected-commit-failure",
                true));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, result.Outcome);
            WorkspaceStoredDocument unchanged = store.Get(id).Value!;
            Assert.AreEqual(beforeRevision, unchanged.ContentRevision);
            Assert.IsNull(unchanged.Document.AuxiliaryState.CharacterCreationSkillsDraft);
            Assert.IsNull(unchanged.Document.AuxiliaryState.CharacterCreationSkillsReceipts);
        }, injector);
    }

    [TestMethod]
    public void Post_replace_diagnostic_failure_reports_the_known_atomic_commit()
    {
        var injector = new ArmedAtomicWriteFaultInjector
        {
            Stage = FileWorkspaceStoreFaultStage.AfterTargetReplaced
        };
        WithReadySkills((store, service, id, _) =>
        {
            CharacterCreationSkillsState state = Load(service, id);
            CharacterCreationSkillAllocation[] allocations =
            [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
            CharacterCreationSkillsPreview preview = service.Preview(new(
                state.Binding, allocations, [])).Value!;
            injector.Armed = true;
            CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> result = service.Confirm(new(
                preview.Binding, allocations, [], preview.PreviewDigest, "post-replace", true));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
            Assert.IsNotNull(result.Value);
            WorkspaceStoredDocument committed = store.Get(id).Value!;
            Assert.AreEqual(result.Value.ContentRevision, committed.ContentRevision);
            Assert.AreEqual(
                result.Value.ReceiptDigest,
                committed.Document.AuxiliaryState.CharacterCreationSkillsReceipts![^1].ReceiptDigest);
        }, injector);
    }

    [TestMethod]
    public void Standard_priority_budget_contract_rejects_any_tampered_rank()
    {
        CharacterCreationPrerequisiteAuthority authority = CreatePrerequisiteAuthority();
        Assert.IsTrue(CharacterCreationStandardPrioritySkillsRules.HasExactBudgetTable(authority.Options));
        foreach (string rank in new[] { "A", "B", "C", "D", "E" })
        {
            CharacterCreationPriorityOptionProjection[] tampered = authority.Options.Select(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Skills && option.Rank == rank
                    ? option with { BaseActiveSkillPoints = option.BaseActiveSkillPoints + 1 }
                    : option).ToArray();
            Assert.IsFalse(CharacterCreationStandardPrioritySkillsRules.HasExactBudgetTable(tampered), rank);
        }
    }

    [TestMethod]
    public void Authority_policy_specialization_and_contribution_tampering_fails_closed()
    {
        CharacterCreationSkillsAuthority valid = CreateSkillsAuthority();
        Assert.IsTrue(CharacterCreationSkillsDraftIntegrity.IsValidAuthority(valid));

        CharacterCreationSkillsAuthority badPolicy = Reseal(valid with { UsePointsOnBrokenGroups = true });
        Assert.IsFalse(CharacterCreationSkillsDraftIntegrity.IsValidAuthority(badPolicy));

        CharacterCreationSkillCatalogEntry first = valid.ActiveSkills[0];
        CharacterCreationSkillsAuthority duplicateSpecialization = Reseal(valid with
        {
            ActiveSkills = valid.ActiveSkills.Select(skill => skill.SourceSkillId == first.SourceSkillId
                ? skill with { Specializations = [first.Specializations[0], first.Specializations[0]] }
                : skill).ToArray()
        });
        Assert.IsFalse(CharacterCreationSkillsDraftIntegrity.IsValidAuthority(duplicateSpecialization));

        CharacterCreationSkillsAuthority duplicateContribution = Reseal(valid with
        {
            KnowledgePointContributions =
            [valid.KnowledgePointContributions[0], valid.KnowledgePointContributions[0]]
        });
        Assert.IsFalse(CharacterCreationSkillsDraftIntegrity.IsValidAuthority(duplicateContribution));

        CharacterCreationSkillsAuthority duplicateName = Reseal(valid with
        {
            ActiveSkills = valid.ActiveSkills.Select((skill, index) => index == 1
                ? skill with { Name = valid.ActiveSkills[0].Name }
                : skill).OrderBy(skill => skill.Name, StringComparer.Ordinal)
                .ThenBy(skill => skill.SourceSkillId, StringComparer.Ordinal).ToArray()
        });
        Assert.IsFalse(CharacterCreationSkillsDraftIntegrity.IsValidAuthority(duplicateName));

        CharacterCreationKnowledgePointContribution original = valid.KnowledgePointContributions[0];
        CharacterCreationSkillsAuthority unboundContribution = Reseal(valid with
        {
            KnowledgePointContributions = [original with { Points = original.Points + 1 }]
        });
        Assert.IsFalse(CharacterCreationSkillsDraftIntegrity.IsValidAuthority(unboundContribution));
    }

    [TestMethod]
    public void Skills_authority_must_match_prerequisite_profile_identity_and_digest()
    {
        WithReadySkills((store, _, id, authority) =>
        {
            CharacterCreationPrerequisiteAuthority prerequisite = CreatePrerequisiteAuthority();
            CharacterCreationSkillsAuthority[] drifted =
            [
                Reseal(authority with { SettingsProfileId = "other-profile" }),
                Reseal(authority with { RawProfileInputsDigest = Digest('9') })
            ];
            foreach (CharacterCreationSkillsAuthority candidate in drifted)
            {
                var service = new CharacterCreationSkillsService(
                    store,
                    CreateResolver(prerequisite, candidate));
                CharacterCreationSkillsState state = Load(service, id);
                Assert.IsFalse(state.CanEdit);
                CollectionAssert.Contains(
                    state.Blockers.ToList(),
                    CharacterCreationSkillsBlockers.SkillsSourceDrift);
                Assert.IsNull(store.Get(id).Value!.Document.AuxiliaryState.CharacterCreationSkillsDraft);
            }
        });
    }

    private static readonly string[] ActiveIds = Enumerable.Range(1, 6)
        .Select(index => $"00000000-0000-0000-0000-{index:000000000000}").ToArray();
    private static readonly string[] KnowledgeIds =
    ["10000000-0000-0000-0000-000000000001", "10000000-0000-0000-0000-000000000002"];
    private const string LanguageId = "20000000-0000-0000-0000-000000000001";

    private static CharacterCreationSkillsState Load(ICharacterCreationSkillsService service, CharacterWorkspaceId id)
    {
        CharacterCreationFoundationResult<CharacterCreationSkillsState> result = service.Load(new(id));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static void WithReadySkills(
        Action<FileWorkspaceStore, ICharacterCreationSkillsService, CharacterWorkspaceId, CharacterCreationSkillsAuthority> action,
        IFileWorkspaceStoreFaultInjector? faultInjector = null)
        => WithReadySkillsContext(
            (store, service, id, skillsAuthority, _, _) =>
                action(store, service, id, skillsAuthority),
            faultInjector);

    private static void WithReadySkillsContext(
        Action<FileWorkspaceStore, ICharacterCreationSkillsService, CharacterWorkspaceId,
            CharacterCreationSkillsAuthority> action,
        IFileWorkspaceStoreFaultInjector? faultInjector = null,
        Func<CharacterCreationPrerequisiteAuthority, CharacterCreationPrerequisiteAuthority>?
            prerequisiteTransform = null)
        => WithReadySkillsContext(
            (store, service, id, skillsAuthority, _, _) =>
                action(store, service, id, skillsAuthority),
            faultInjector,
            prerequisiteTransform);

    private static void WithReadySkillsContext(
        Action<FileWorkspaceStore, ICharacterCreationSkillsService, CharacterWorkspaceId,
            CharacterCreationSkillsAuthority, CharacterCreationPrerequisiteAuthority,
            CharacterCreationAttributesServiceTests.StubSourceResolver> action,
        IFileWorkspaceStoreFaultInjector? faultInjector = null,
        Func<CharacterCreationPrerequisiteAuthority, CharacterCreationPrerequisiteAuthority>?
            prerequisiteTransform = null)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"chummer-skills-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            CharacterCreationPrerequisiteAuthority prerequisiteAuthority = CreatePrerequisiteAuthority();
            prerequisiteAuthority = prerequisiteTransform?.Invoke(prerequisiteAuthority)
                                    ?? prerequisiteAuthority;
            CharacterCreationSkillsAuthority skillsAuthority = CreateSkillsAuthority(prerequisiteAuthority);
            CharacterCreationAttributesServiceTests.StubSourceResolver resolver =
                CreateResolver(prerequisiteAuthority, skillsAuthority);
            FileWorkspaceStore store = faultInjector is null
                ? new FileWorkspaceStore(directory)
                : new FileWorkspaceStore(directory, faultInjector);
            CharacterWorkspaceId id = new("skills-runner");
            Assert.IsTrue(store.CreateWorkspaceDocument(id, new WorkspaceDocument(ReadyXml, RulesetDefaults.Sr5)).Success);
            var prerequisites = new CharacterCreationPrerequisiteService(
                store,
                new CharacterCreationAttributesServiceTests.StubCharacterQueries(),
                resolver);
            CharacterCreationPrerequisiteState prerequisiteState = prerequisites.Load(new(id)).Value!;
            IReadOnlyDictionary<string, string> ranks = CharacterCreationPrerequisiteServiceTests.Assign(
                "A", "E", "B", "C", "D");
            CharacterCreationPrerequisitePreview prerequisitePreview = prerequisites.Preview(new(
                prerequisiteState.Binding, ranks) { HeritageSelectionId = "human", TalentSelectionId = "mundane" }).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, prerequisites.Confirm(new(
                prerequisitePreview.Binding, ranks, prerequisitePreview.PreviewDigest, true)
                { HeritageSelectionId = "human", TalentSelectionId = "mundane" }).Outcome);

            var attributes = new CharacterCreationAttributesService(store, resolver);
            CharacterCreationAttributesState attributeState = attributes.Load(new(id)).Value!;
            CharacterCreationAttributeAllocation[] allocations =
            [new("INT", 2, 0), new("LOG", 1, 0)];
            CharacterCreationAttributesPreview attributePreview = attributes.Preview(new(
                attributeState.Binding, allocations)).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, attributes.Confirm(new(
                attributePreview.Binding, allocations, attributePreview.PreviewDigest, true)).Outcome);

            action(
                store,
                new CharacterCreationSkillsService(store, resolver),
                id,
                skillsAuthority,
                prerequisiteAuthority,
                resolver);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static CharacterCreationPrerequisiteAuthority CreatePrerequisiteAuthority()
    {
        CharacterCreationPrerequisiteAuthority authority = CharacterCreationPrerequisiteServiceTests.CreateAuthority(
            CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]);
        CharacterCreationPriorityOptionProjection[] options = authority.Options.Select(option =>
        {
            if (option.CategoryId == CharacterCreationPriorityCategoryIds.Skills)
            {
                (int active, int groups) = option.Rank switch
                { "A" => (46, 10), "B" => (36, 5), "C" => (28, 2), "D" => (22, 0), "E" => (18, 0), _ => (-1, -1) };
                return option with { BaseActiveSkillPoints = active, BaseSkillGroupPoints = groups };
            }
            if (option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage)
            {
                return option with
                {
                    HeritageOptions = option.HeritageOptions.Select(heritage => heritage with
                    {
                        Movement = new CharacterCreationMetatypeMovementProjection(
                            new CharacterCreationMetatypeMovementRate(2m, 1m, 0m),
                            new CharacterCreationMetatypeMovementRate(4m, 0m, 0m),
                            new CharacterCreationMetatypeMovementRate(2m, 1m, 0m))
                    }).ToArray()
                };
            }
            return option;
        }).ToArray();
        authority = authority with { Options = options, AuthorityDigest = string.Empty };
        return authority with { AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(authority) };
    }

    private static CharacterCreationPrerequisiteAuthority WithHeritageMovement(
        CharacterCreationPrerequisiteAuthority authority,
        CharacterCreationMetatypeMovementProjection movement)
    {
        CharacterCreationPrerequisiteAuthority changed = authority with
        {
            Options = authority.Options.Select(option => option with
            {
                HeritageOptions = option.HeritageOptions.Select(heritage => heritage with
                {
                    Movement = movement
                }).ToArray()
            }).ToArray(),
            AuthorityDigest = string.Empty
        };
        return changed with
        {
            AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(changed)
        };
    }

    [TestMethod]
    public void Authority_rejects_forged_native_language_capability()
    {
        CharacterCreationSkillsAuthority authority = CreateSkillsAuthority();
        CharacterCreationSkillCatalogEntry language = authority.KnowledgeSkills.Single(skill =>
            skill.SourceSkillId == LanguageId);
        CharacterCreationSkillCatalogEntry hiddenLanguageIdentity = SealCatalog(
            language with { CanBeNativeLanguage = false },
            authority.EffectiveSkillsInputsDigest);
        CharacterCreationSkillsAuthority missingCapability = Reseal(authority with
        {
            KnowledgeSkills = authority.KnowledgeSkills.Select(skill =>
                skill.SourceSkillId == LanguageId ? hiddenLanguageIdentity : skill).ToArray()
        });
        Assert.IsFalse(CharacterCreationSkillsDraftIntegrity.IsValidAuthority(missingCapability));

        CharacterCreationSkillCatalogEntry active = authority.ActiveSkills[0];
        CharacterCreationSkillCatalogEntry forgedActive = SealCatalog(
            active with { CanBeNativeLanguage = true },
            authority.EffectiveSkillsInputsDigest);
        CharacterCreationSkillsAuthority widenedCapability = Reseal(authority with
        {
            ActiveSkills = authority.ActiveSkills.Select(skill =>
                skill.SourceSkillId == active.SourceSkillId ? forgedActive : skill).ToArray()
        });
        Assert.IsFalse(CharacterCreationSkillsDraftIntegrity.IsValidAuthority(widenedCapability));
    }

    private static CharacterCreationSkillsAuthority CreateSkillsAuthority(
        CharacterCreationPrerequisiteAuthority? prerequisite = null)
    {
        prerequisite ??= CreatePrerequisiteAuthority();
        string effectiveSkillsInputsDigest = prerequisite.EffectiveSkillsInputsDigest;
        CharacterCreationSkillCatalogEntry[] active = ActiveIds.Select((id, index) => new CharacterCreationSkillCatalogEntry(
            id, CharacterCreationSkillKinds.Active, $"Active {index}", "Combat Active", "AGI", index < 2 ? "Athletics" : null,
            false, CharacterCreationSkillsDigest.ComputeUtf8($"active-{index}"),
            [new CharacterCreationSkillSpecializationOption($"spec-{index}", $"Spec {index}", $"skills.xml#spec:{index}")],
            [$"skills.xml#skill:{id}"])).ToArray();
        active = active.Select((skill, index) => skill with
            {
                RequiresFlyMovement = index == 1
            })
            .Select(skill => SealCatalog(skill, effectiveSkillsInputsDigest))
            .ToArray();
        CharacterCreationSkillCatalogEntry[] knowledge =
        [
            .. KnowledgeIds.Select((id, index) => new CharacterCreationSkillCatalogEntry(
                id, CharacterCreationSkillKinds.Knowledge, $"Knowledge {index}", "Academic", "LOG", null,
                false, CharacterCreationSkillsDigest.ComputeUtf8($"knowledge-{index}"), [], [$"skills.xml#skill:{id}"])),
            new CharacterCreationSkillCatalogEntry(
                LanguageId,
                CharacterCreationSkillKinds.Knowledge,
                "English",
                "Language",
                "INT",
                null,
                false,
                CharacterCreationSkillsDigest.ComputeUtf8("language"),
                [],
                [$"skills.xml#skill:{LanguageId}"])
            {
                CanBeNativeLanguage = true
            }
        ];
        knowledge = knowledge.OrderBy(skill => skill.Name, StringComparer.Ordinal)
            .ThenBy(skill => skill.SourceSkillId, StringComparer.Ordinal).ToArray();
        knowledge = knowledge.Select(skill => SealCatalog(skill, effectiveSkillsInputsDigest)).ToArray();
        string groupDigest = CharacterCreationSkillsDigest.Compute(new
        {
            Schema = "chummer.sr5.creation-skill-group-source.v1",
            Name = "Athletics",
            MemberSkillSourceIds = ActiveIds.Take(2).ToArray(),
            EffectiveSkillsInputsDigest = effectiveSkillsInputsDigest
        });
        string characterDigest = CharacterCreationFoundationDraftLedgerIntegrity
            .ComputeRawCharacterXmlDigest(ReadyXml);
        string[] contributionAnchors = ["qualities.xml#linguist"];
        const string contributionId = "quality:linguist";
        const int contributionPoints = 2;
        string contributionDigest = CharacterCreationSkillsDigest.Compute(new
        {
            Schema = "chummer.sr5.creation-knowledge-point-contribution.v1",
            ContributionId = contributionId,
            Points = contributionPoints,
            SourceCharacterXmlDigest = characterDigest,
            SourceAnchorIds = contributionAnchors
        });
        var contribution = new CharacterCreationKnowledgePointContribution(
            contributionId, contributionPoints, characterDigest, contributionDigest, contributionAnchors);
        string runtime = CharacterCreationStandardPrioritySkillsRules.ComputeRuntimeDigest(false, false, true);
        var authority = new CharacterCreationSkillsAuthority(
            CharacterCreationSkillsSchemas.AuthorityV1,
            prerequisite.SettingsProfileId,
            effectiveSkillsInputsDigest,
            prerequisite.RawProfileInputsDigest,
            "({INTUnaug} + {LOGUnaug}) * 2", 6, 6, 6, 1, false, false, true,
            active, knowledge,
            [new(groupDigest, "Athletics", ActiveIds.Take(2).ToArray(), groupDigest, ["skills.xml#group:Athletics"])],
            [contribution], ["settings.xml", "skills.xml"], [], true, runtime, string.Empty);
        return authority with { AuthorityDigest = CharacterCreationSkillsDigest.Compute(authority) };
    }

    private static CharacterCreationSkillsAuthority Reseal(CharacterCreationSkillsAuthority authority)
    {
        authority = authority with { AuthorityDigest = string.Empty };
        return authority with { AuthorityDigest = CharacterCreationSkillsDigest.Compute(authority) };
    }

    private static CharacterCreationSkillCatalogEntry SealCatalog(
        CharacterCreationSkillCatalogEntry skill,
        string effectiveSkillsInputsDigest) => skill with
    {
        SourceNodeDigest = CharacterCreationStandardPrioritySkillsRules.ComputeCatalogProjectionDigest(
            effectiveSkillsInputsDigest,
            skill.SourceSkillId,
            skill.Kind,
            skill.Name,
            skill.Category,
            skill.DefaultAttribute,
            skill.SkillGroup,
            skill.IsExotic,
            skill.Specializations,
            skill.SourceAnchorIds,
            skill.CanDefault,
            skill.IgnoresSourceDisabled,
            skill.RequiresGroundMovement,
            skill.RequiresSwimMovement,
            skill.RequiresFlyMovement,
            skill.CanBeNativeLanguage)
    };

    private static CharacterCreationAttributesServiceTests.StubSourceResolver CreateResolver(
        CharacterCreationPrerequisiteAuthority prerequisite,
        CharacterCreationSkillsAuthority skills) => new(prerequisite, skills);

    private static string Digest(char value) => "sha256:" + new string(value, 64);

    private sealed class ArmedAtomicWriteFaultInjector : IFileWorkspaceStoreFaultInjector
    {
        public bool Armed { get; set; }
        public FileWorkspaceStoreFaultStage Stage { get; init; } =
            FileWorkspaceStoreFaultStage.AfterTempFileFlushed;

        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            _ = targetPath;
            _ = tempPath;
            if (Armed && stage == Stage)
                throw new IOException("injected before atomic target replacement");
        }
    }
}
