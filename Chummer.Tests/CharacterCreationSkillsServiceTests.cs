using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationSkillsServiceTests
{
    private const string ReadyXml = "<character><name>Skills Runner</name><alias>Priority</alias>"
                                    + "<buildmethod>Priority</buildmethod><created>false</created>"
                                    + "<karma>25</karma><nuyen>0</nuyen></character>";

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
    {
        string directory = Path.Combine(Path.GetTempPath(), $"chummer-skills-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            CharacterCreationPrerequisiteAuthority prerequisiteAuthority = CreatePrerequisiteAuthority();
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

            action(store, new CharacterCreationSkillsService(store, resolver), id, skillsAuthority);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static CharacterCreationPrerequisiteAuthority CreatePrerequisiteAuthority()
    {
        CharacterCreationPrerequisiteAuthority authority = CharacterCreationPrerequisiteServiceTests.CreateAuthority(
            CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]);
        CharacterCreationPriorityOptionProjection[] options = authority.Options.Select(option =>
        {
            if (option.CategoryId != CharacterCreationPriorityCategoryIds.Skills)
                return option;
            (int active, int groups) = option.Rank switch
            { "A" => (46, 10), "B" => (36, 5), "C" => (28, 2), "D" => (22, 0), "E" => (18, 0), _ => (-1, -1) };
            return option with { BaseActiveSkillPoints = active, BaseSkillGroupPoints = groups };
        }).ToArray();
        authority = authority with { Options = options, AuthorityDigest = string.Empty };
        return authority with { AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(authority) };
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
        active = active.Select(skill => SealCatalog(skill, effectiveSkillsInputsDigest)).ToArray();
        CharacterCreationSkillCatalogEntry[] knowledge =
        [
            .. KnowledgeIds.Select((id, index) => new CharacterCreationSkillCatalogEntry(
                id, CharacterCreationSkillKinds.Knowledge, $"Knowledge {index}", "Academic", "LOG", null,
                false, CharacterCreationSkillsDigest.ComputeUtf8($"knowledge-{index}"), [], [$"skills.xml#skill:{id}"])),
            new(LanguageId, CharacterCreationSkillKinds.Knowledge, "English", "Language", "INT", null,
                false, CharacterCreationSkillsDigest.ComputeUtf8("language"), [], [$"skills.xml#skill:{LanguageId}"])
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
            skill.SourceAnchorIds)
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
