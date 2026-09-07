using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationAttributesServiceTests
{
    [TestMethod]
    public void Preview_projects_exact_normal_special_and_global_karma_budgets()
    {
        WithConfirmedPrerequisite((store, service, id, beforeXml) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
            Assert.AreEqual(20m, state.NormalPointBudget.Total);
            Assert.AreEqual(1m, state.SpecialPointBudget.Total);
            Assert.AreEqual(25m, state.CreationKarmaBudget.Total);
            Assert.AreEqual(1, state.MaxNumberMaxAttributesCreate);
            Assert.AreEqual(5, state.KarmaAttribute);
            Assert.AreEqual(1, state.Attributes.Single(item => item.AttributeId == "BOD").Current);
            Assert.AreEqual(2, state.Attributes.Single(item => item.AttributeId == "EDG").Current);
            Assert.IsFalse(state.Attributes.Single(item => item.AttributeId == "MAG").IsEnabled);
            CharacterCreationAttributeProjection essence = state.Attributes.Single(item =>
                item.AttributeId == "ESS");
            Assert.IsFalse(essence.IsEnabled);
            Assert.AreEqual(0, essence.Minimum);
            Assert.AreEqual(6, essence.Maximum);
            Assert.AreEqual(6, essence.AugmentedMaximum);
            Assert.AreEqual(6, essence.Current);
            CollectionAssert.Contains(
                essence.DisableReasons.ToList(),
                CharacterCreationAttributesBlockers.EssenceNotSpendable);

            CharacterCreationFoundationResult<CharacterCreationAttributesPreview> result =
                service.Preview(new CharacterCreationAttributesPreviewRequest(
                    state.Binding,
                    [
                        new CharacterCreationAttributeAllocation("BOD", 5, 0),
                        new CharacterCreationAttributeAllocation("AGI", 0, 1),
                        new CharacterCreationAttributeAllocation("EDG", 1, 0)
                    ]));

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
            CharacterCreationAttributesPreview preview = result.Value!;
            Assert.IsTrue(preview.CanConfirm);
            Assert.AreEqual(5m, preview.NormalPointBudget.Used);
            Assert.AreEqual(15m, preview.NormalPointBudget.Remaining);
            Assert.AreEqual(1m, preview.SpecialPointBudget.Used);
            Assert.AreEqual(10m, preview.CreationKarmaBudget.Used);
            Assert.AreEqual(6, preview.Attributes.Single(item => item.AttributeId == "BOD").Current);
            Assert.AreEqual(2, preview.Attributes.Single(item => item.AttributeId == "AGI").Current);
            Assert.AreEqual(10, preview.Attributes.Single(item => item.AttributeId == "AGI").KarmaCost);
            Assert.AreEqual(beforeXml, store.Get(id).Value!.Document.Content);
            Assert.IsNull(store.Get(id).Value!.Document.AuxiliaryState.CharacterCreationAttributesDraft);
        });
    }

    [TestMethod]
    public void Confirm_is_atomic_reopens_and_preserves_the_prerequisite_sibling()
    {
        WithConfirmedPrerequisite((store, service, id, beforeXml) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            CharacterCreationPrerequisiteDraft prerequisite = state.PrerequisiteDraft!;
            CharacterCreationAttributeAllocation[] allocations =
            [
                new("BOD", 4, 0),
                new("EDG", 1, 0)
            ];
            CharacterCreationAttributesPreview preview = service.Preview(
                new CharacterCreationAttributesPreviewRequest(state.Binding, allocations)).Value!;
            CharacterCreationFoundationResult<CharacterCreationAttributesReceipt> confirmed =
                service.Confirm(new CharacterCreationAttributesConfirmRequest(
                    preview.Binding,
                    allocations,
                    preview.PreviewDigest,
                    ExplicitlyConfirmed: true));

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome);
            Assert.IsFalse(confirmed.Value!.CharacterDocumentChanged);
            WorkspaceStoredDocument after = store.Get(id).Value!;
            Assert.AreEqual(beforeXml, after.Document.Content);
            Assert.AreEqual(prerequisite.DraftDigest,
                after.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft!.DraftDigest);
            Assert.IsNotNull(after.Document.AuxiliaryState.CharacterCreationAttributesDraft);
            CharacterCreationAttributesState reopened = Load(service, id);
            Assert.AreEqual(5,
                reopened.Attributes.Single(item => item.AttributeId == "BOD").Current);
            Assert.AreEqual(1L, reopened.PendingDraft!.DraftRevision);
        });
    }

    [TestMethod]
    public void Halve_attribute_points_uses_exact_legacy_integer_division()
    {
        CharacterCreationPrerequisiteAuthority authority = WithHalvedHuman(
            CharacterCreationPrerequisiteServiceTests.CreateAuthority(
                CharacterCreationBuildMethods.Priority,
                ["A", "B", "C", "D", "E"]));
        WithConfirmedPrerequisite((_, service, id, _) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            Assert.AreEqual(10m, state.NormalPointBudget.Total);
            Assert.IsTrue(state.PrerequisiteDraft!.HeritageSelection!.HalvesNormalAttributePoints);
        }, authority);
    }

    [TestMethod]
    public void Disabled_duplicate_over_budget_and_maximum_count_allocations_fail_closed()
    {
        WithConfirmedPrerequisite((_, service, id, _) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            CharacterCreationFoundationResult<CharacterCreationAttributesPreview> invalid =
                service.Preview(new CharacterCreationAttributesPreviewRequest(
                    state.Binding,
                    [
                        new CharacterCreationAttributeAllocation("BOD", 5, 0),
                        new CharacterCreationAttributeAllocation("BOD", 1, 0),
                        new CharacterCreationAttributeAllocation("AGI", 5, 0),
                        new CharacterCreationAttributeAllocation("MAG", 1, 0),
                        new CharacterCreationAttributeAllocation("EDG", 6, 0)
                    ]));

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, invalid.Outcome);
            CollectionAssert.Contains(invalid.Blockers.ToList(),
                CharacterCreationAttributesBlockers.AllocationDuplicate);
            CollectionAssert.Contains(invalid.Blockers.ToList(),
                CharacterCreationAttributesBlockers.AttributeDisabled);
            CollectionAssert.Contains(invalid.Blockers.ToList(),
                CharacterCreationAttributesBlockers.AllocationInvalid);
            CollectionAssert.Contains(invalid.Blockers.ToList(),
                CharacterCreationAttributesBlockers.MaximumAttributeCountExceeded);
        });
    }

    [TestMethod]
    public void Stale_binding_preview_tamper_and_missing_explicit_confirmation_do_not_write()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            CharacterCreationAttributeAllocation[] allocations = [new("BOD", 1, 0)];
            CharacterCreationAttributesPreview preview = service.Preview(
                new CharacterCreationAttributesPreviewRequest(state.Binding, allocations)).Value!;
            CharacterCreationAttributesBinding stale = state.Binding with
            {
                PrerequisiteDraftDigest = Digest('9')
            };
            CharacterCreationFoundationResult<CharacterCreationAttributesPreview> staleResult =
                service.Preview(new CharacterCreationAttributesPreviewRequest(stale, allocations));
            CharacterCreationFoundationResult<CharacterCreationAttributesReceipt> noConsent =
                service.Confirm(new CharacterCreationAttributesConfirmRequest(
                    preview.Binding,
                    allocations,
                    preview.PreviewDigest,
                    ExplicitlyConfirmed: false));
            CharacterCreationFoundationResult<CharacterCreationAttributesReceipt> tampered =
                service.Confirm(new CharacterCreationAttributesConfirmRequest(
                    preview.Binding,
                    allocations,
                    Digest('8'),
                    ExplicitlyConfirmed: true));

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, staleResult.Outcome);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, noConsent.Outcome);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, tampered.Outcome);
            Assert.IsNull(store.Get(id).Value!.Document.AuxiliaryState.CharacterCreationAttributesDraft);
        });
    }

    [TestMethod]
    public void Recomputed_draft_digest_cannot_hide_a_tampered_attribute_projection()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            CharacterCreationAttributeAllocation[] allocations = [new("BOD", 1, 0)];
            CharacterCreationAttributesPreview preview = service.Preview(
                new CharacterCreationAttributesPreviewRequest(state.Binding, allocations)).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(new CharacterCreationAttributesConfirmRequest(
                    preview.Binding,
                    allocations,
                    preview.PreviewDigest,
                    ExplicitlyConfirmed: true)).Outcome);

            WorkspaceStoredDocument persisted = store.Get(id).Value!;
            CharacterCreationAttributesDraft current = persisted.Document.AuxiliaryState
                .CharacterCreationAttributesDraft!;
            CharacterCreationAttributeProjection[] forgedAttributes = current.Attributes
                .Select(item => item.AttributeId == "BOD"
                    ? item with { Current = item.Current + 1 }
                    : item)
                .ToArray();
            CharacterCreationAttributesDraft forged = current with
            {
                DraftRevision = current.DraftRevision + 1,
                BaseContentRevision = persisted.ContentRevision,
                Attributes = forgedAttributes,
                DraftDigest = string.Empty
            };
            forged = forged with
            {
                DraftDigest = CharacterCreationAttributesDraftIntegrity.ComputeDigest(forged)
            };
            WorkspaceDocument replacement = persisted.Document with
            {
                State = persisted.Document.State with
                {
                    AuxiliaryState = persisted.Document.AuxiliaryState with
                    {
                        CharacterCreationAttributesDraft = forged
                    }
                }
            };
            WorkspaceStoreMutationResult mutation = store
                .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    persisted.ContentRevision,
                    persisted.Document.AuxiliaryStateDigest,
                    replacement);
            Assert.IsTrue(mutation.Success, mutation.Error);

            CharacterCreationAttributesState rejected = Load(service, id);
            Assert.IsNull(rejected.PendingDraft);
            CollectionAssert.Contains(rejected.Blockers.ToList(),
                CharacterCreationAttributesBlockers.DraftInvalid);
        });
    }

    [TestMethod]
    [DataRow("Magician", "MAG", 3)]
    [DataRow("Adept", "MAG", 2)]
    [DataRow("Mystic Adept", "MAG", 3)]
    [DataRow("Aspected Magician", "MAG", 2)]
    [DataRow("Technomancer", "RES", 3)]
    public void Awakened_priority_grants_are_starting_values_with_separate_point_and_karma_costs(
        string kind, string attributeId, int startingValue)
    {
        CharacterCreationPrerequisiteAuthority authority = WithTalent(kind, attributeId, startingValue);
        WithConfirmedPrerequisite((store, service, id, xml) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
            CharacterCreationAttributeProjection initial = state.Attributes.Single(a => a.AttributeId == attributeId);
            Assert.IsTrue(initial.IsEnabled);
            Assert.AreEqual(startingValue, initial.Minimum);
            Assert.AreEqual(startingValue, initial.Current);
            Assert.AreEqual(6, initial.Maximum);
            Assert.AreEqual(0m, state.SpecialPointBudget.Used);
            Assert.IsFalse(state.Attributes.Single(a => a.AttributeId == (attributeId == "MAG" ? "RES" : "MAG")).IsEnabled);
            Assert.IsFalse(state.Attributes.Single(a => a.AttributeId == "DEP").IsEnabled);
            CollectionAssert.Contains(initial.SourceAnchorIds.ToArray(), "priorities.xml#test:awakened");

            CharacterCreationAttributeAllocation[] allocations = [new(attributeId, 1, 1)];
            CharacterCreationAttributesPreview preview = service.Preview(new(state.Binding, allocations)).Value!;
            Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
            CharacterCreationAttributeProjection raised = preview.Attributes.Single(a => a.AttributeId == attributeId);
            Assert.AreEqual(startingValue + 2, raised.Current);
            Assert.AreEqual((startingValue + 2) * 5, raised.KarmaCost);
            Assert.AreEqual(1m, preview.SpecialPointBudget.Used);
            Assert.AreEqual(0m, preview.NormalPointBudget.Used);
            Assert.AreEqual((decimal)raised.KarmaCost, preview.CreationKarmaBudget.Used);
            Assert.AreEqual(xml, store.Get(id).Value!.Document.Content);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(new(preview.Binding, allocations, preview.PreviewDigest, true)).Outcome);
            Assert.AreEqual(xml, store.Get(id).Value!.Document.Content);
            CharacterCreationAttributesState reopened = Load(service, id);
            Assert.IsTrue(reopened.CanEdit, string.Join(",", reopened.Blockers));
            Assert.AreEqual(raised, reopened.Attributes.Single(a => a.AttributeId == attributeId)
                with { SourceAnchorIds = raised.SourceAnchorIds });
            Assert.AreEqual(1m, reopened.SpecialPointBudget.Used);
        }, authority, "awakened");
    }

    [TestMethod]
    [DataRow("Magician", "MAG")]
    [DataRow("Technomancer", "RES")]
    public void Awakened_allocations_share_edge_budget_and_reject_caps_overflow_and_disabled_attributes(
        string kind, string attributeId)
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            WorkspaceStoredDocument before = store.Get(id).Value!;
            (CharacterCreationAttributeAllocation[] Allocations, string Blocker)[] cases =
            [
                ([new(attributeId, 1, 0), new("EDG", 1, 0)], CharacterCreationAttributesBlockers.SpecialPointsExceeded),
                ([new(attributeId, 0, 2)], CharacterCreationAttributesBlockers.GlobalKarmaExceeded),
                ([new(attributeId, 0, 4)], CharacterCreationAttributesBlockers.AllocationInvalid),
                ([new(attributeId, int.MaxValue, int.MaxValue)], CharacterCreationAttributesBlockers.AllocationInvalid),
                ([new(attributeId == "MAG" ? "RES" : "MAG", 1, 0)], CharacterCreationAttributesBlockers.AttributeDisabled),
                ([new("ESS", 1, 0)], CharacterCreationAttributesBlockers.AttributeDisabled),
                ([new("DEP", 1, 0)], CharacterCreationAttributesBlockers.AttributeDisabled)
            ];
            foreach (var test in cases)
            {
                CharacterCreationAttributesPreview preview = service.Preview(new(state.Binding, test.Allocations)).Value!;
                Assert.IsFalse(preview.CanConfirm);
                CollectionAssert.Contains(preview.Blockers.ToList(), test.Blocker);
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
                    service.Confirm(new(preview.Binding, test.Allocations, preview.PreviewDigest, true)).Outcome);
            }
            Assert.AreEqual(before.ContentRevision, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
        }, WithTalent(kind, attributeId, 3), "awakened");
    }

    [TestMethod]
    public void Source_grant_sets_magic_minimum_and_can_raise_its_metatype_maximum_without_free_spending()
    {
        WithConfirmedPrerequisite((_, service, id, _) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
            CharacterCreationAttributeProjection magic = state.Attributes.Single(a => a.AttributeId == "MAG");
            Assert.AreEqual(7, magic.Minimum);
            Assert.AreEqual(7, magic.Maximum);
            Assert.AreEqual(7, magic.AugmentedMaximum);
            Assert.AreEqual(7, magic.Current);
            Assert.AreEqual(0m, state.SpecialPointBudget.Used);
            CharacterCreationAttributesPreview overCap = service.Preview(new(state.Binding, [new("MAG", 1, 0)])).Value!;
            Assert.IsFalse(overCap.CanConfirm);
            CollectionAssert.Contains(overCap.Blockers.ToList(), CharacterCreationAttributesBlockers.AllocationInvalid);
        }, WithTalent("Magician", "MAG", 7), "awakened");
    }

    private static CharacterCreationPrerequisiteAuthority WithTalent(
        string kind, string attributeId, int grant, string? additionalQuality = null)
    {
        CharacterCreationPrerequisiteAuthority authority = CharacterCreationPrerequisiteServiceTests.CreateAuthority(
            CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]);
        string[] qualities = additionalQuality is null ? [kind] : [kind, additionalQuality];
        string raw = new XElement("talent",
            new XElement("name", kind), new XElement("value", kind),
            new XElement("qualities", qualities.Select(quality => new XElement("quality", quality))),
            new XElement(attributeId == "MAG" ? "magic" : "resonance", grant)).ToString(SaveOptions.DisableFormatting);
        authority = authority with
        {
            Options = authority.Options.Select(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Talent && option.Rank == "E"
                ? option with
                {
                    TalentOptions = [new CharacterCreationPriorityTalentOptionProjection(
                        "awakened", kind, kind, 0, attributeId == "MAG" ? grant : null,
                        attributeId == "RES" ? grant : null, null, qualities,
                        CharacterCreationTalentGrantAuthorityDigest.ComputeRawTalentNode(raw), true, [],
                        ["priorities.xml#test:awakened"]) { RawTalentNode = raw }]
                }
                : option).ToArray(),
            AuthorityDigest = string.Empty
        };
        return authority with { AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(authority) };
    }

    [TestMethod]
    public void An_extra_talent_quality_cannot_silently_change_special_attribute_authority()
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            Assert.IsFalse(state.CanEdit);
            CollectionAssert.Contains(state.Blockers.ToList(), CharacterCreationAttributesBlockers.SpecialAttributeAuthorityIncomplete);
            long revision = store.Get(id).Value!.ContentRevision;
            CharacterCreationAttributesPreview preview = service.Preview(new(state.Binding, [new("MAG", 1, 0)])).Value!;
            Assert.IsFalse(preview.CanConfirm);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
                service.Confirm(new(preview.Binding, [new("MAG", 1, 0)], preview.PreviewDigest, true)).Outcome);
            Assert.AreEqual(revision, store.Get(id).Value!.ContentRevision);
        }, WithTalent("Magician", "MAG", 3, "Unknown attribute effect"), "awakened");
    }

    [TestMethod]
    [DataRow("Magician", "MAG", 0)]
    [DataRow("Magician", "MAG", -1)]
    [DataRow("Magician", "RES", 3)]
    [DataRow("Technomancer", "MAG", 3)]
    [DataRow("Mundane", "MAG", 3)]
    [DataRow("Unresolved talent", "MAG", 3)]
    public void Inconsistent_or_unresolved_talent_grants_do_not_enable_attribute_mutation(
        string kind, string attributeId, int grant)
    {
        WithConfirmedPrerequisite((store, service, id, _) =>
        {
            CharacterCreationAttributesState state = Load(service, id);
            Assert.IsFalse(state.CanEdit);
            CollectionAssert.Contains(state.Blockers.ToList(), CharacterCreationAttributesBlockers.SpecialAttributeAuthorityIncomplete);
            Assert.IsFalse(state.Attributes.Single(a => a.AttributeId == "MAG").IsEnabled);
            Assert.IsFalse(state.Attributes.Single(a => a.AttributeId == "RES").IsEnabled);
            CharacterCreationAttributesPreview preview = service.Preview(new(state.Binding, [])).Value!;
            Assert.IsFalse(preview.CanConfirm);
            long revision = store.Get(id).Value!.ContentRevision;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked,
                service.Confirm(new(preview.Binding, [], preview.PreviewDigest, true)).Outcome);
            Assert.AreEqual(revision, store.Get(id).Value!.ContentRevision);
        }, WithTalent(kind, attributeId, grant), "awakened");
    }

    private static CharacterCreationAttributesState Load(
        ICharacterCreationAttributesService service,
        CharacterWorkspaceId id)
    {
        CharacterCreationFoundationResult<CharacterCreationAttributesState> result =
            service.Load(new CharacterCreationAttributesLoadRequest(id));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static void WithConfirmedPrerequisite(
        Action<FileWorkspaceStore, ICharacterCreationAttributesService, CharacterWorkspaceId, string> action,
        CharacterCreationPrerequisiteAuthority? suppliedAuthority = null,
        string talentSelectionId = "mundane")
    {
        string directory = Path.Combine(Path.GetTempPath(), $"chummer-attributes-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            CharacterCreationPrerequisiteAuthority authority = suppliedAuthority
                ?? CharacterCreationPrerequisiteServiceTests.CreateAuthority(
                    CharacterCreationBuildMethods.Priority,
                    ["A", "B", "C", "D", "E"]);
            FileWorkspaceStore store = new(directory);
            CharacterWorkspaceId id = new("attributes-runner");
            string xml = "<character><name>Attributes Runner</name><alias>Priority</alias>"
                         + "<buildmethod>Priority</buildmethod><created>false</created>"
                         + "<karma>25</karma><nuyen>0</nuyen></character>";
            Assert.IsTrue(store.CreateWorkspaceDocument(
                id,
                new WorkspaceDocument(xml, RulesetDefaults.Sr5)).Success);
            var sourceResolver = new StubSourceResolver(authority);
            var prerequisiteService = new CharacterCreationPrerequisiteService(
                store,
                new StubCharacterQueries(),
                sourceResolver);
            CharacterCreationPrerequisiteState prerequisiteState = prerequisiteService.Load(
                new CharacterCreationPrerequisiteLoadRequest(id)).Value!;
            IReadOnlyDictionary<string, string> ranks = CharacterCreationPrerequisiteServiceTests.Assign(
                "A", "E", "B", "C", "D");
            CharacterCreationPrerequisitePreview prerequisitePreview = prerequisiteService.Preview(
                new CharacterCreationPrerequisitePreviewRequest(prerequisiteState.Binding, ranks)
                {
                    HeritageSelectionId = "human",
                    TalentSelectionId = talentSelectionId
                }).Value!;
            CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt> prerequisiteReceipt =
                prerequisiteService.Confirm(new CharacterCreationPrerequisiteConfirmRequest(
                    prerequisitePreview.Binding,
                    ranks,
                    prerequisitePreview.PreviewDigest,
                    ExplicitlyConfirmed: true)
                {
                    HeritageSelectionId = "human",
                    TalentSelectionId = talentSelectionId
                });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, prerequisiteReceipt.Outcome);

            var service = new CharacterCreationAttributesService(store, sourceResolver);
            action(store, service, id, xml);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CharacterCreationPrerequisiteAuthority WithHalvedHuman(
        CharacterCreationPrerequisiteAuthority authority)
    {
        CharacterCreationPriorityOptionProjection[] options = authority.Options.Select(option =>
        {
            if (option.CategoryId != CharacterCreationPriorityCategoryIds.Heritage)
                return option;
            return option with
            {
                HeritageOptions = option.HeritageOptions.Select(child => child with
                {
                    HalvesNormalAttributePoints = true
                }).ToArray()
            };
        }).ToArray();
        authority = authority with { Options = options, AuthorityDigest = string.Empty };
        return authority with
        {
            AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(authority)
        };
    }

    private static string Digest(char value) => "sha256:" + new string(value, 64);

    internal sealed class StubSourceResolver : ICharacterSourceDataResolver
    {
        private readonly CharacterCreationPrerequisiteAuthority _authority;
        private readonly CharacterCreationSkillsAuthority? _skillsAuthority;
        private readonly CharacterCreationMagicResonanceAuthority? _magicResonanceAuthority;

        public StubSourceResolver(
            CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationSkillsAuthority? skillsAuthority = null,
            CharacterCreationMagicResonanceAuthority? magicResonanceAuthority = null)
        {
            _authority = authority;
            _skillsAuthority = skillsAuthority;
            _magicResonanceAuthority = magicResonanceAuthority;
        }

        public ICharacterSourceDataContext TryCreateContext(string characterXml) =>
            new StubSourceContext(_authority, _skillsAuthority, _magicResonanceAuthority);
    }

    internal sealed class StubSourceContext : ICharacterSourceDataContext
    {
        private readonly CharacterCreationPrerequisiteAuthority _authority;
        private readonly CharacterCreationSkillsAuthority? _skillsAuthority;
        private readonly CharacterCreationMagicResonanceAuthority? _magicResonanceAuthority;

        public StubSourceContext(
            CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationSkillsAuthority? skillsAuthority = null,
            CharacterCreationMagicResonanceAuthority? magicResonanceAuthority = null)
        {
            _authority = authority;
            _skillsAuthority = skillsAuthority;
            _magicResonanceAuthority = magicResonanceAuthority;
        }

        public bool TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority authority)
        {
            authority = _authority;
            return true;
        }

        public bool TryResolveCreationSkillsAuthority(out CharacterCreationSkillsAuthority authority)
        {
            authority = _skillsAuthority ?? CharacterCreationSkillsAuthority.Unavailable;
            return _skillsAuthority is not null;
        }

        public bool TryResolveCreationMagicResonanceAuthority(
            out CharacterCreationMagicResonanceAuthority authority)
        {
            authority = _magicResonanceAuthority ?? CharacterCreationMagicResonanceAuthority.Unavailable;
            return _magicResonanceAuthority is not null;
        }

        public bool TryResolveCyberwareGradeDeviceRating(
            string gradeName,
            string improvementSource,
            out int deviceRating)
        {
            deviceRating = 0;
            return false;
        }

        public bool TryResolveVehicleModBonuses(
            string sourceId,
            string name,
            out CharacterVehicleModSourceBonuses bonuses)
        {
            bonuses = CharacterVehicleModSourceBonuses.Empty;
            return false;
        }
    }

    internal sealed class StubCharacterQueries : ICharacterFileQueries
    {
        public CharacterFileSummary ParseSummary(CharacterDocument document)
        {
            XElement root = XDocument.Parse(document.Content).Root!;
            return new CharacterFileSummary(
                root.Element("name")?.Value ?? string.Empty,
                root.Element("alias")?.Value ?? string.Empty,
                string.Empty,
                root.Element("buildmethod")?.Value ?? string.Empty,
                string.Empty,
                string.Empty,
                25,
                0,
                false);
        }

        public CharacterValidationResult Validate(CharacterDocument document) => new(true, []);
    }
}
