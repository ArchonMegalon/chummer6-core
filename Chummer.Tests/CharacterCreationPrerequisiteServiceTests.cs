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
public sealed class CharacterCreationPrerequisiteServiceTests
{
    [TestMethod]
    public void Priority_preview_exposes_global_karma_and_base_attribute_grant_without_writing()
    {
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            CreateAuthority(CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]),
            (store, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                Assert.AreEqual(25m, state.CreationKarmaBudget.Total);
                Assert.AreEqual(0m, state.CreationKarmaBudget.Used);
                Assert.AreEqual(25m, state.CreationKarmaBudget.Remaining);
                Assert.IsTrue(state.CreationKarmaBudget.IsExact);
                Assert.IsNull(state.PendingDraft);

                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> result =
                    service.Preview(PreviewRequest(
                        state.Binding,
                        Assign("A", "B", "C", "D", "E")));

                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
                Assert.IsNotNull(result.Value);
                Assert.AreEqual(16, result.Value.BaseNormalAttributePoints);
                Assert.IsFalse(result.Value.RequiresMetatypeAttributeAdjustment);
                Assert.AreEqual(10, result.Value.SumToTenUsed);
                Assert.IsTrue(result.Value.CanConfirm);
                WorkspaceStoredDocument unchanged = store.Get(id).Value!;
                Assert.AreEqual(1L, unchanged.ContentRevision);
                Assert.AreEqual(0L, unchanged.SavedRevision);
                Assert.IsNull(unchanged.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft);
            });
    }

    [TestMethod]
    public void Heritage_and_talent_children_are_explicit_and_digest_bound()
    {
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            CreateAuthority(CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]),
            (_, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                IReadOnlyDictionary<string, string> ranks = Assign("A", "E", "B", "C", "D");
                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> missing =
                    service.Preview(new CharacterCreationPrerequisitePreviewRequest(
                        state.Binding,
                        ranks));
                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> wrong =
                    service.Preview(new CharacterCreationPrerequisitePreviewRequest(
                        state.Binding,
                        ranks)
                    {
                        HeritageSelectionId = "human",
                        TalentSelectionId = "forged"
                    });

                Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, missing.Outcome);
                CollectionAssert.Contains(missing.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.HeritageSelectionIncomplete);
                CollectionAssert.Contains(missing.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.TalentSelectionIncomplete);
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, wrong.Outcome);
                CollectionAssert.Contains(wrong.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.TalentSelectionInvalid);
            });
    }

    [TestMethod]
    public void Heritage_karma_cost_is_spent_in_preview_receipt_and_draft_integrity()
    {
        CharacterCreationPrerequisiteAuthority authority = WithHumanKarmaCost(
            CreateAuthority(CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]),
            7);
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            authority,
            (store, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                IReadOnlyDictionary<string, string> ranks = Assign("A", "E", "B", "C", "D");
                CharacterCreationPrerequisitePreview preview = service.Preview(
                    PreviewRequest(state.Binding, ranks)).Value!;

                Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
                Assert.AreEqual(7m, preview.CreationKarmaBudget.Used);
                Assert.AreEqual(18m, preview.CreationKarmaBudget.Remaining);
                CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt> confirmed =
                    service.Confirm(ConfirmRequest(
                        preview.Binding,
                        ranks,
                        preview.PreviewDigest,
                        ExplicitlyConfirmed: true));

                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome);
                Assert.AreEqual(18, confirmed.Value!.CreationKarmaRemaining);
                WorkspaceStoredDocument persisted = store.Get(id).Value!;
                CharacterCreationPrerequisiteDraft draft = persisted.Document.AuxiliaryState
                    .CharacterCreationPrerequisiteDraft!;
                Assert.AreEqual(7, draft.CreationKarmaUsed);
                Assert.IsTrue(CharacterCreationPrerequisiteDraftIntegrity.IsValidPending(
                    draft,
                    id,
                    persisted.ContentRevision,
                    CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(
                        persisted.Document.Content),
                    authority));
                CharacterCreationPrerequisiteDraft forged = draft with
                {
                    CreationKarmaUsed = 0,
                    DraftDigest = string.Empty
                };
                forged = forged with
                {
                    DraftDigest = CharacterCreationPrerequisiteDraftIntegrity.ComputeDigest(forged)
                };
                Assert.IsFalse(CharacterCreationPrerequisiteDraftIntegrity.IsValidPending(
                    forged,
                    id,
                    persisted.ContentRevision,
                    CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(
                        persisted.Document.Content),
                    authority));
            });
    }

    [TestMethod]
    public void Priority_requires_the_profile_multiset_and_preserves_duplicate_rank_arrays()
    {
        CharacterCreationPrerequisiteAuthority authority = CreateAuthority(
            CharacterCreationBuildMethods.Priority,
            ["B", "C", "D", "E", "E"]);
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            authority,
            (_, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                CharacterCreationPrerequisitePreview valid = service.Preview(
                    PreviewRequest(
                        state.Binding,
                        Assign("E", "B", "E", "D", "C"))).Value!;
                Assert.IsTrue(valid.CanConfirm);

                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> invalid =
                    service.Preview(PreviewRequest(
                        state.Binding,
                        Assign("B", "B", "C", "D", "E")));
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, invalid.Outcome);
                CollectionAssert.Contains(
                    invalid.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.SelectionInvalid);
            });
    }

    [TestMethod]
    public void Sum_to_ten_accepts_repeated_ranks_only_at_the_exact_weight_total()
    {
        WithWorkspace(
            CharacterCreationBuildMethods.SumToTen,
            CreateAuthority(
                CharacterCreationBuildMethods.SumToTen,
                ["A", "B", "C", "D", "E"]),
            (_, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                CharacterCreationPrerequisitePreview repeated = service.Preview(
                    PreviewRequest(
                        state.Binding,
                        Assign("A", "A", "D", "D", "E"))).Value!;
                Assert.AreEqual(10, repeated.SumToTenUsed);
                Assert.IsTrue(repeated.CanConfirm);

                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> wrong =
                    service.Preview(PreviewRequest(
                        state.Binding,
                        Assign("A", "A", "E", "E", "E")));
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, wrong.Outcome);
                CollectionAssert.Contains(
                    wrong.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.SumToTenMismatch);
            });
    }

    [TestMethod]
    public void Confirm_is_atomic_checkpoints_and_leaves_character_xml_unchanged()
    {
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            CreateAuthority(CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]),
            (store, service, id) =>
            {
                WorkspaceStoredDocument before = store.Get(id).Value!;
                CharacterCreationPrerequisiteState state = Load(service, id);
                CharacterCreationPrerequisitePreview preview = service.Preview(
                    PreviewRequest(
                        state.Binding,
                        Assign("A", "B", "C", "D", "E"))).Value!;

                CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt> result =
                    service.Confirm(ConfirmRequest(
                        preview.Binding,
                        Assign("A", "B", "C", "D", "E"),
                        preview.PreviewDigest,
                        ExplicitlyConfirmed: true));

                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
                Assert.IsNotNull(result.Value);
                Assert.IsFalse(result.Value.CharacterDocumentChanged);
                Assert.AreEqual(2L, result.Value.ContentRevision);
                Assert.AreEqual(2L, result.Value.SavedRevision);
                WorkspaceStoredDocument after = store.Get(id).Value!;
                Assert.AreEqual(before.Document.Content, after.Document.Content);
                Assert.IsNotNull(after.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft);
                CharacterCreationPrerequisiteState resumed = Load(service, id);
                Assert.IsTrue(resumed.CanEnterAttributes);
                Assert.IsFalse(resumed.RequiresMetatypeAttributeAdjustment);
                Assert.AreEqual(16, resumed.BaseNormalAttributePoints);
                Assert.AreEqual(16, resumed.EffectiveNormalAttributePoints);
                Assert.AreEqual(1, resumed.TotalSpecialAttributePoints);

                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> duplicate =
                    service.Preview(PreviewRequest(
                        resumed.Binding,
                        Assign("A", "B", "C", "D", "E")));
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, duplicate.Outcome);
                CollectionAssert.Contains(
                    duplicate.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.DraftDuplicate);
                Assert.AreEqual(2L, store.Get(id).Value!.ContentRevision);
            });
    }

    [TestMethod]
    public void Stale_binding_tampered_authority_and_legacy_priority_state_fail_closed()
    {
        CharacterCreationPrerequisiteAuthority authority = CreateAuthority(
            CharacterCreationBuildMethods.Priority,
            ["A", "B", "C", "D", "E"]);
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            authority,
            (_, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                CharacterCreationPrerequisiteBinding stale = state.Binding with
                {
                    AuthorityDigest = Digest(99)
                };
                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> result =
                    service.Preview(PreviewRequest(
                        stale,
                        Assign("A", "B", "C", "D", "E")));
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, result.Outcome);
                CollectionAssert.Contains(
                    result.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.PrioritiesSourceDrift);
            });

        CharacterCreationPrerequisiteAuthority forged = authority with
        {
            CreationKarmaTotal = 26
        };
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            forged,
            (_, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                CollectionAssert.Contains(
                    state.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.AuthorityUnavailable);
                Assert.IsFalse(state.CreationKarmaBudget.IsExact);
            });

        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            authority,
            (_, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                CollectionAssert.Contains(
                    state.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.LegacyPriorityStateRequiresImport);
            },
            extraXml: "<priorityattributes>A</priorityattributes>");
    }

    [TestMethod]
    public void Confirmation_requires_explicit_consent_and_a_matching_preview_digest()
    {
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            CreateAuthority(CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]),
            (store, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                IReadOnlyDictionary<string, string> selection = Assign("A", "B", "C", "D", "E");
                CharacterCreationPrerequisitePreview preview = service.Preview(
                    PreviewRequest(state.Binding, selection)).Value!;

                CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt> noConsent =
                    service.Confirm(ConfirmRequest(
                        preview.Binding,
                        selection,
                        preview.PreviewDigest,
                        ExplicitlyConfirmed: false));
                CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt> tampered =
                    service.Confirm(ConfirmRequest(
                        preview.Binding,
                        selection,
                        Digest(88),
                        ExplicitlyConfirmed: true));

                Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, noConsent.Outcome);
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, tampered.Outcome);
                Assert.AreEqual(1L, store.Get(id).Value!.ContentRevision);
            });
    }

    [TestMethod]
    public void Talent_skill_choices_build_a_typed_plan_contribution_but_never_partially_write()
    {
        const string spellcastingId = "40c72109-8924-45ca-a4d7-255b75e6a6b0";
        const string arcanaId = "74a68a9e-8c5b-4998-8dbb-08c1e768afc3";
        const string exoticMeleeId = "a1366ec2-772d-4f08-8c65-5f79464d975b";
        CharacterCreationPrerequisiteAuthority authority = WithActiveSkillTalentGrant(
            CreateAuthority(CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]),
            spellcastingId,
            arcanaId,
            exoticMeleeId);
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            authority,
            (store, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                IReadOnlyDictionary<string, string> ranks = Assign("A", "E", "B", "C", "D");
                var missingRequest = new CharacterCreationPrerequisitePreviewRequest(
                    state.Binding,
                    ranks)
                {
                    HeritageSelectionId = "human",
                    TalentSelectionId = "adept"
                };
                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> missing =
                    service.Preview(missingRequest);
                CollectionAssert.Contains(
                    missing.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.TalentActiveSkillSelectionIncomplete);

                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> selected =
                    service.Preview(missingRequest with
                    {
                        TalentActiveSkillSelectionIds = [arcanaId, spellcastingId]
                    });
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, selected.Outcome);
                CollectionAssert.Contains(
                    selected.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.TalentSelectionUnsupported);
                CharacterCreationTalentGrantPlanContribution plan =
                    selected.Value!.TalentSelection!.GrantPlan!;
                Assert.AreEqual(CharacterCreationPrerequisiteSchemas.TalentGrantPlanV1, plan.Schema);
                Assert.HasCount(2, plan.ActiveSkills);
                Assert.IsEmpty(plan.SkillGroups);
                Assert.AreEqual("active-skill", plan.ActiveSkills[0].TargetKind);
                Assert.AreEqual(arcanaId, plan.ActiveSkills[0].SourceId);
                Assert.AreEqual("Arcana", plan.ActiveSkills[0].CanonicalName);
                Assert.AreEqual(spellcastingId, plan.ActiveSkills[1].SourceId);
                Assert.AreEqual(4, plan.ActiveSkills[0].BaseRating);
                Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(
                    plan.PlanDigest));
                Assert.AreEqual(1L, store.Get(id).Value!.ContentRevision);
                Assert.IsNull(store.Get(id).Value!.Document.AuxiliaryState
                    .CharacterCreationPrerequisiteDraft);

                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> forged =
                    service.Preview(missingRequest with
                    {
                        TalentActiveSkillSelectionIds =
                            [spellcastingId, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]
                    });
                CollectionAssert.Contains(
                    forged.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.TalentActiveSkillSelectionInvalid);
                Assert.IsNull(forged.Value!.TalentSelection!.GrantPlan,
                    "A mixed valid+forged selection must never expose a partial plan.");

                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> exotic =
                    service.Preview(missingRequest with
                    {
                        TalentActiveSkillSelectionIds = [spellcastingId, exoticMeleeId]
                    });
                CollectionAssert.Contains(
                    exotic.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers
                        .TalentExoticSkillSpecializationRequired);
                Assert.IsNull(exotic.Value!.TalentSelection!.GrantPlan,
                    "An exotic choice cannot enter a plan without typed specialization authority.");
                Assert.AreEqual(1L, store.Get(id).Value!.ContentRevision);
            });
    }

    [TestMethod]
    public void Forged_talent_skill_grant_above_projected_three_slot_cap_is_rejected()
    {
        const string arcanaId = "74a68a9e-8c5b-4998-8dbb-08c1e768afc3";
        const string spellcastingId = "40c72109-8924-45ca-a4d7-255b75e6a6b0";
        CharacterCreationPrerequisiteAuthority authority = WithActiveSkillTalentGrant(
            CreateAuthority(CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]),
            spellcastingId,
            arcanaId,
            "a1366ec2-772d-4f08-8c65-5f79464d975b");
        string skillsDigest = authority.EffectiveSkillsInputsDigest;
        CharacterCreationPrerequisiteAuthority overSlotLimit = MutateTalentOption(
            authority,
            talent =>
            {
                CharacterCreationTalentActiveSkillGrantProjection grant = talent.ActiveSkillGrant!;
                CharacterCreationTalentActiveSkillChoiceProjection[] options = grant.Options
                    .Concat(
                    [
                        ActiveSkill("33333333-3333-3333-3333-333333333333", "Gymnastics",
                            "Physical Active", "Athletics", Digest(68), skillsDigest),
                        ActiveSkill("44444444-4444-4444-4444-444444444444", "Pistols",
                            "Combat Active", "Firearms", Digest(69), skillsDigest)
                    ])
                    .OrderBy(option => option.CanonicalName, StringComparer.Ordinal)
                    .ThenBy(option => option.SourceId, StringComparer.Ordinal)
                    .ToArray();
                return talent with
                {
                    ActiveSkillGrant = grant with
                    {
                        Quantity = 4,
                        Options = options,
                        GrantDigest = CharacterCreationTalentGrantAuthorityDigest.ComputeActiveGrant(
                            4,
                            grant.BaseRating,
                            grant.SkillType,
                            skillsDigest,
                            options.Select(option => option.SelectionId)),
                        IsSupported = false,
                        Blockers = [CharacterCreationPrerequisiteBlockers
                            .TalentSkillGrantAuthorityUnsupported]
                    }
                };
            });
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            overSlotLimit,
            (store, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                CollectionAssert.Contains(
                    state.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.AuthorityUnavailable);
                Assert.AreEqual(1L, store.Get(id).Value!.ContentRevision);
            });
    }

    [TestMethod]
    public void Current_corpus_active_rule_types_and_canonical_grouped_alias_pass_authority_validation()
    {
        const string arcanaId = "74a68a9e-8c5b-4998-8dbb-08c1e768afc3";
        const string spellcastingId = "40c72109-8924-45ca-a4d7-255b75e6a6b0";
        CharacterCreationPrerequisiteAuthority baseline = WithActiveSkillTalentGrant(
            CreateAuthority(CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]),
            spellcastingId,
            arcanaId,
            "a1366ec2-772d-4f08-8c65-5f79464d975b");
        string skillsDigest = baseline.EffectiveSkillsInputsDigest;
        CharacterCreationTalentActiveSkillGrantProjection sourceGrant = baseline.Options
            .Single(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                              && option.Rank == "E")
            .TalentOptions.Single().ActiveSkillGrant!;
        CharacterCreationTalentActiveSkillChoiceProjection arcana = sourceGrant.Options.Single(
            option => option.SourceId == arcanaId);
        CharacterCreationTalentActiveSkillChoiceProjection spellcasting = sourceGrant.Options.Single(
            option => option.SourceId == spellcastingId);

        CharacterCreationPrerequisiteAuthority WithRule(
            string type,
            CharacterCreationTalentActiveSkillChoiceProjection[] options,
            string query = "",
            string[]? specificNames = null) =>
            MutateTalentOption(
                baseline,
                talent =>
                {
                    CharacterCreationTalentActiveSkillGrantProjection grant =
                        talent.ActiveSkillGrant! with
                        {
                            Quantity = 1,
                            SkillType = type,
                            Options = options,
                            GrantDigest = CharacterCreationTalentGrantAuthorityDigest.ComputeActiveGrant(
                                1,
                                sourceGrant.BaseRating,
                                type,
                                skillsDigest,
                                options.Select(option => option.SelectionId)),
                            IsSupported = true,
                            Blockers = [],
                            SkillTypeQuery = query,
                            SpecificSkillChoiceNames = specificNames ?? []
                        };
                    return talent with { ActiveSkillGrant = grant };
                });

        CharacterCreationTalentActiveSkillChoiceProjection[] matrixOptions =
        [
            ActiveSkill("33333333-3333-3333-3333-333333333333", "Computer",
                "Technical Active", "Electronics", Digest(68), skillsDigest),
            ActiveSkill("44444444-4444-4444-4444-444444444444", "Cybercombat",
                "Technical Active", "Cracking", Digest(69), skillsDigest)
        ];
        CharacterCreationPrerequisiteAuthority[] acceptedAuthorities =
        [
            WithRule(CharacterCreationTalentSkillGrantTypes.Default, sourceGrant.Options.ToArray()),
            WithRule(CharacterCreationTalentSkillGrantTypes.Matrix, matrixOptions),
            WithRule(
                CharacterCreationTalentSkillGrantTypes.Specific,
                [arcana, spellcasting],
                specificNames: ["Arcana", "Spellcasting"]),
            WithRule(
                CharacterCreationTalentSkillGrantTypes.Specific,
                sourceGrant.Options.ToArray()),
            WithRule(
                CharacterCreationTalentSkillGrantTypes.XPath,
                [arcana],
                CharacterCreationTalentSkillGrantTypes.PinnedXPathPredicate)
        ];
        foreach (CharacterCreationPrerequisiteAuthority authority in acceptedAuthorities)
        {
            WithWorkspace(
                CharacterCreationBuildMethods.Priority,
                authority,
                (_, service, id) =>
                {
                    CharacterCreationPrerequisiteState state = Load(service, id);
                    Assert.IsFalse(state.Blockers.Contains(
                        CharacterCreationPrerequisiteBlockers.AuthorityUnavailable));
                });
        }

        CharacterCreationPrerequisiteAuthority grouped = WithSkillGroupTalentGrant(
            CreateAuthority(CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]),
            CharacterCreationTalentSkillGrantTypes.Grouped);
        grouped = MutateTalentOption(
            grouped,
            talent => talent with
            {
                SkillGroupGrant = talent.SkillGroupGrant! with { RequestedGroupNames = [] }
            });
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            grouped,
            (_, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                Assert.IsFalse(state.Blockers.Contains(
                    CharacterCreationPrerequisiteBlockers.AuthorityUnavailable));
            });
    }

    [TestMethod]
    public void Talent_group_choices_are_all_or_nothing_for_mixed_valid_and_forged_ids()
    {
        CharacterCreationPrerequisiteAuthority authority = WithSkillGroupTalentGrant(
            CreateAuthority(CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]));
        CharacterCreationTalentSkillGroupGrantProjection grant = authority.Options.Single(option =>
                option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && option.Rank == "E")
            .TalentOptions.Single().SkillGroupGrant!;
        string firstId = grant.Options[0].SelectionId;
        string secondId = grant.Options[1].SelectionId;
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            authority,
            (store, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                IReadOnlyDictionary<string, string> ranks = Assign("A", "E", "B", "C", "D");
                var request = new CharacterCreationPrerequisitePreviewRequest(state.Binding, ranks)
                {
                    HeritageSelectionId = "human",
                    TalentSelectionId = "aspected",
                    TalentSkillGroupSelectionIds = [secondId, firstId]
                };
                CharacterCreationPrerequisitePreview valid = service.Preview(request).Value!;
                Assert.HasCount(2, valid.TalentSelection!.GrantPlan!.SkillGroups);
                Assert.AreEqual(secondId,
                    valid.TalentSelection.GrantPlan.SkillGroups[0].SelectionId);
                Assert.AreEqual(firstId,
                    valid.TalentSelection.GrantPlan.SkillGroups[1].SelectionId);

                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> forged =
                    service.Preview(request with
                    {
                        TalentSkillGroupSelectionIds =
                            [firstId, $"skill-group:{new string('a', 64)}"]
                    });
                CollectionAssert.Contains(
                    forged.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.TalentSkillGroupSelectionInvalid);
                Assert.IsNull(forged.Value!.TalentSelection!.GrantPlan,
                    "A mixed valid+forged group selection must never expose a partial plan.");
                Assert.AreEqual(1L, store.Get(id).Value!.ContentRevision);
                Assert.IsNull(store.Get(id).Value!.Document.AuxiliaryState
                    .CharacterCreationPrerequisiteDraft);
            });
    }

    [TestMethod]
    public void Recomputed_outer_authority_digest_cannot_hide_forged_grant_or_group_identity()
    {
        CharacterCreationPrerequisiteAuthority active = WithActiveSkillTalentGrant(
            CreateAuthority(CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]),
            "40c72109-8924-45ca-a4d7-255b75e6a6b0",
            "74a68a9e-8c5b-4998-8dbb-08c1e768afc3",
            "a1366ec2-772d-4f08-8c65-5f79464d975b");
        CharacterCreationPrerequisiteAuthority forgedActiveDigest = MutateTalentOption(
            active,
            talent => talent with
            {
                ActiveSkillGrant = talent.ActiveSkillGrant! with { GrantDigest = Digest(90) }
            });
        AssertAuthorityUnavailable(forgedActiveDigest);

        CharacterCreationPrerequisiteAuthority grouped = WithSkillGroupTalentGrant(
            CreateAuthority(CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]));
        CharacterCreationPrerequisiteAuthority missingSkillsDigest = grouped with
        {
            RawSkillsXmlDigest = string.Empty,
            AuthorityDigest = string.Empty
        };
        missingSkillsDigest = missingSkillsDigest with
        {
            AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(
                missingSkillsDigest)
        };
        AssertAuthorityUnavailable(missingSkillsDigest);

        CharacterCreationPrerequisiteAuthority forgedGroupSelection = MutateTalentOption(
            grouped,
            talent =>
            {
                CharacterCreationTalentSkillGroupGrantProjection grant = talent.SkillGroupGrant!;
                CharacterCreationTalentSkillGroupChoiceProjection[] options = grant.Options.ToArray();
                options[0] = options[0] with
                {
                    SelectionId = $"skill-group:{new string('a', 64)}"
                };
                return talent with
                {
                    SkillGroupGrant = grant with
                    {
                        Options = options,
                        GrantDigest = CharacterCreationTalentGrantAuthorityDigest
                            .ComputeSkillGroupGrant(
                                grant.Quantity,
                                grant.BaseRating,
                                grant.SkillGroupType,
                                grouped.EffectiveSkillsInputsDigest,
                                options.Select(option => option.SelectionId))
                    }
                };
            });
        AssertAuthorityUnavailable(forgedGroupSelection);

        CharacterCreationPrerequisiteAuthority forgedGroupDigest = MutateTalentOption(
            grouped,
            talent =>
            {
                CharacterCreationTalentSkillGroupGrantProjection grant = talent.SkillGroupGrant!;
                CharacterCreationTalentSkillGroupChoiceProjection[] options = grant.Options.ToArray();
                string forgedDigest = Digest(91);
                options[0] = options[0] with
                {
                    GroupDigest = forgedDigest,
                    SelectionId = CharacterCreationTalentGrantAuthorityDigest
                        .ComputeSkillGroupSelectionId(forgedDigest)
                };
                return talent with
                {
                    SkillGroupGrant = grant with
                    {
                        Options = options,
                        GrantDigest = CharacterCreationTalentGrantAuthorityDigest
                            .ComputeSkillGroupGrant(
                                grant.Quantity,
                                grant.BaseRating,
                                grant.SkillGroupType,
                                grouped.EffectiveSkillsInputsDigest,
                                options.Select(option => option.SelectionId))
                    }
                };
            });
        AssertAuthorityUnavailable(forgedGroupDigest);
    }

    private static CharacterCreationPrerequisiteState Load(
        ICharacterCreationPrerequisiteService service,
        CharacterWorkspaceId id)
    {
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> result =
            service.Load(new CharacterCreationPrerequisiteLoadRequest(id));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static CharacterCreationPrerequisitePreviewRequest PreviewRequest(
        CharacterCreationPrerequisiteBinding binding,
        IReadOnlyDictionary<string, string> assignments) =>
        new(binding, assignments)
        {
            HeritageSelectionId = "human",
            TalentSelectionId = "mundane"
        };

    private static CharacterCreationPrerequisiteConfirmRequest ConfirmRequest(
        CharacterCreationPrerequisiteBinding binding,
        IReadOnlyDictionary<string, string> assignments,
        string previewDigest,
        bool ExplicitlyConfirmed) =>
        new(binding, assignments, previewDigest, ExplicitlyConfirmed)
        {
            HeritageSelectionId = "human",
            TalentSelectionId = "mundane"
        };

    private static void WithWorkspace(
        string buildMethod,
        CharacterCreationPrerequisiteAuthority authority,
        Action<FileWorkspaceStore, ICharacterCreationPrerequisiteService, CharacterWorkspaceId> action,
        string extraXml = "")
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"chummer-prerequisite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            FileWorkspaceStore store = new(directory);
            CharacterWorkspaceId id = new("prerequisite-runner");
            Assert.IsTrue(store.CreateWorkspaceDocument(
                id,
                new WorkspaceDocument(CharacterXml(buildMethod, extraXml), RulesetDefaults.Sr5)).Success);
            var service = new CharacterCreationPrerequisiteService(
                store,
                new StubCharacterQueries(),
                new StubSourceResolver(authority));
            action(store, service, id);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CharacterXml(string buildMethod, string extraXml) =>
        $"<character><name>Prerequisite Runner</name><alias>Priority</alias>"
        + $"<buildmethod>{buildMethod}</buildmethod><created>false</created>"
        + $"<karma>25</karma><nuyen>0</nuyen>{extraXml}</character>";

    internal static IReadOnlyDictionary<string, string> Assign(
        string heritage,
        string talent,
        string attributes,
        string skills,
        string resources) => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [CharacterCreationPriorityCategoryIds.Heritage] = heritage,
        [CharacterCreationPriorityCategoryIds.Talent] = talent,
        [CharacterCreationPriorityCategoryIds.Attributes] = attributes,
        [CharacterCreationPriorityCategoryIds.Skills] = skills,
        [CharacterCreationPriorityCategoryIds.Resources] = resources
    };

    internal static CharacterCreationPrerequisiteAuthority CreateAuthority(
        string buildMethod,
        IReadOnlyList<string> priorityArray)
    {
        Dictionary<string, int> weights = new(StringComparer.Ordinal)
        {
            ["A"] = 4,
            ["B"] = 3,
            ["C"] = 2,
            ["D"] = 1,
            ["E"] = 0
        };
        CharacterCreationPriorityRankWeight[] rankWeights = weights.Select(pair =>
                new CharacterCreationPriorityRankWeight(
                    pair.Key,
                    pair.Value,
                    [$"priorities.xml#weight:{pair.Key}"]))
            .ToArray();
        Dictionary<string, int> attributePoints = new(StringComparer.Ordinal)
        {
            ["A"] = 24,
            ["B"] = 20,
            ["C"] = 16,
            ["D"] = 14,
            ["E"] = 12
        };
        var options = new List<CharacterCreationPriorityOptionProjection>();
        int sequence = 1;
        foreach (string category in CharacterCreationPriorityCategoryIds.Ordered)
        {
            foreach (string rank in priorityArray.Distinct(StringComparer.Ordinal))
            {
                string id = $"00000000-0000-0000-0000-{sequence:000000000000}";
                CharacterCreationPriorityOptionProjection option = new(
                    category,
                    category,
                    rank,
                    id,
                    $"{category}-{rank}",
                    weights[rank],
                    category == CharacterCreationPriorityCategoryIds.Attributes
                        ? attributePoints[rank]
                        : null,
                    Digest(sequence),
                    [$"priorities.xml#priority:{id}"]);
                if (category == CharacterCreationPriorityCategoryIds.Heritage)
                {
                    option = option with
                    {
                        HeritageOptions = [HumanOption(id)]
                    };
                }
                else if (category == CharacterCreationPriorityCategoryIds.Talent)
                {
                    option = option with
                    {
                        TalentOptions = [MundaneOption(id)]
                    };
                }
                options.Add(option);
                sequence++;
            }
        }
        var authority = new CharacterCreationPrerequisiteAuthority(
            CharacterCreationPrerequisiteSchemas.AuthorityV1,
            "223a11ff-80e0-428b-89a9-6ef1c243b8b6",
            buildMethod,
            25,
            priorityArray.ToArray(),
            "Standard",
            10,
            rankWeights,
            options,
            Digest(41),
            Digest(42),
            Digest(43),
            Digest(44),
            ["settings.xml#setting:test", "priorities.xml"],
            [],
            IsAuthoritative: true,
            AuthorityDigest: string.Empty);
        authority = authority with
        {
            SelectedCustomDataInputsDigest = Digest(47),
            RawMetatypesXmlDigest = Digest(45),
            EffectiveMetatypesInputsDigest = Digest(46),
            MaxNumberMaxAttributesCreate = 1,
            KarmaAttribute = 5,
            AlternateMetatypeAttributeKarma = false,
            ReverseAttributePriorityOrder = false,
            RawSkillsXmlDigest = Digest(60),
            EffectiveSkillsInputsDigest = Digest(61)
        };
        return authority with
        {
            AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(authority)
        };
    }

    private static CharacterCreationPrerequisiteAuthority WithHumanKarmaCost(
        CharacterCreationPrerequisiteAuthority authority,
        int karmaCost)
    {
        CharacterCreationPriorityOptionProjection[] options = authority.Options.Select(option =>
            option.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
                ? option with
                {
                    HeritageOptions = option.HeritageOptions.Select(child => child with
                    {
                        KarmaCost = karmaCost
                    }).ToArray()
                }
                : option).ToArray();
        authority = authority with { Options = options, AuthorityDigest = string.Empty };
        return authority with
        {
            AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(authority)
        };
    }

    private static CharacterCreationTalentActiveSkillChoiceProjection ActiveSkill(
        string sourceId,
        string name,
        string category,
        string? group,
        string sourceNodeDigest,
        string skillsDigest) =>
        new(
            sourceId,
            sourceId,
            name,
            category,
            group,
            sourceNodeDigest,
            skillsDigest,
            [$"skills.xml#skill:{sourceId}"]);

    private static CharacterCreationPrerequisiteAuthority WithActiveSkillTalentGrant(
        CharacterCreationPrerequisiteAuthority authority,
        string skillSourceId,
        string secondSkillSourceId,
        string exoticSkillSourceId)
    {
        string skillsDigest = Digest(61);
        CharacterCreationPriorityOptionProjection[] options = authority.Options.Select(option =>
        {
            if (option.CategoryId != CharacterCreationPriorityCategoryIds.Talent
                || option.Rank != "E")
            {
                return option;
            }
            CharacterCreationTalentActiveSkillChoiceProjection skill = new(
                skillSourceId,
                skillSourceId,
                "Spellcasting",
                "Magical Active",
                "Sorcery",
                Digest(62),
                skillsDigest,
                [$"skills.xml#skill:{skillSourceId}"]);
            CharacterCreationTalentActiveSkillChoiceProjection secondSkill = new(
                secondSkillSourceId,
                secondSkillSourceId,
                "Arcana",
                "Pseudo-Magical Active",
                null,
                Digest(65),
                skillsDigest,
                [$"skills.xml#skill:{secondSkillSourceId}"]);
            CharacterCreationTalentActiveSkillChoiceProjection exoticSkill = new(
                exoticSkillSourceId,
                exoticSkillSourceId,
                "Exotic Melee Weapon",
                "Combat Active",
                null,
                Digest(66),
                skillsDigest,
                [$"skills.xml#skill:{exoticSkillSourceId}"])
            {
                IsExotic = true,
                IsEnabled = false,
                Blockers = [CharacterCreationPrerequisiteBlockers
                    .TalentExoticSkillSpecializationRequired]
            };
            CharacterCreationTalentActiveSkillChoiceProjection[] grantOptions =
                [secondSkill, exoticSkill, skill];
            CharacterCreationTalentActiveSkillGrantProjection grant = new(
                Quantity: 2,
                BaseRating: 4,
                SkillType: CharacterCreationTalentSkillGrantTypes.Active,
                Options: grantOptions,
                GrantDigest: CharacterCreationTalentGrantAuthorityDigest.ComputeActiveGrant(
                    2,
                    4,
                    CharacterCreationTalentSkillGrantTypes.Active,
                    skillsDigest,
                    grantOptions.Select(option => option.SelectionId)),
                IsSupported: true,
                Blockers: [],
                SourceAnchorIds: ["priorities.xml#talent:adept", "skills.xml"]);
            CharacterCreationPriorityTalentOptionProjection adept = new(
                "adept",
                "Adept - 6 Magic",
                "Adept",
                0,
                6,
                null,
                null,
                [],
                Digest(64),
                IsEnabled: false,
                Blockers: [CharacterCreationPrerequisiteBlockers.TalentSelectionUnsupported],
                SourceAnchorIds: ["priorities.xml#talent:adept"])
            {
                ActiveSkillGrant = grant
            };
            return option with { TalentOptions = [adept] };
        }).ToArray();
        authority = authority with
        {
            Options = options,
            RawSkillsXmlDigest = Digest(60),
            EffectiveSkillsInputsDigest = skillsDigest,
            AuthorityDigest = string.Empty
        };
        return authority with
        {
            AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(authority)
        };
    }

    private static CharacterCreationPrerequisiteAuthority WithSkillGroupTalentGrant(
        CharacterCreationPrerequisiteAuthority authority,
        string skillGroupType = CharacterCreationTalentSkillGrantTypes.Choices)
    {
        string skillsDigest = authority.EffectiveSkillsInputsDigest;
        CharacterCreationTalentSkillGroupChoiceProjection Group(
            string name,
            params string[] memberIds)
        {
            string[] orderedMembers = memberIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            string digest = CharacterCreationTalentGrantAuthorityDigest.ComputeSkillGroup(
                skillsDigest,
                name,
                orderedMembers);
            return new CharacterCreationTalentSkillGroupChoiceProjection(
                CharacterCreationTalentGrantAuthorityDigest.ComputeSkillGroupSelectionId(digest),
                name,
                orderedMembers,
                digest,
                skillsDigest,
                [$"skills.xml#skillgroup:{name}"]);
        }

        CharacterCreationTalentSkillGroupChoiceProjection[] groupOptions =
        [
            Group("Conjuring", "11111111-1111-1111-1111-111111111111"),
            Group("Sorcery", "22222222-2222-2222-2222-222222222222")
        ];
        string compatibilityMarker = string.Equals(
            skillGroupType,
            CharacterCreationTalentSkillGrantTypes.Choices,
            StringComparison.Ordinal)
            ? CharacterCreationTalentSkillGrantTypes.GroupChoiceAliasCompatibility
            : string.Empty;
        string[] sourceAnchors = string.IsNullOrEmpty(compatibilityMarker)
            ? ["priorities.xml#talent:aspected", "skills.xml"]
            :
            [
                "priorities.xml#talent:aspected",
                "skills.xml",
                $"compatibility:{compatibilityMarker}"
            ];
        CharacterCreationTalentSkillGroupGrantProjection grant = new(
            2,
            4,
            skillGroupType,
            groupOptions,
            CharacterCreationTalentGrantAuthorityDigest.ComputeSkillGroupGrant(
                2,
                4,
                skillGroupType,
                skillsDigest,
                groupOptions.Select(option => option.SelectionId)),
            IsSupported: true,
            Blockers: [],
            SourceAnchorIds: sourceAnchors)
        {
            CompatibilityMarker = compatibilityMarker,
            RequestedGroupNames = groupOptions.Select(option => option.CanonicalName).ToArray()
        };
        CharacterCreationPriorityOptionProjection[] options = authority.Options.Select(option =>
        {
            if (option.CategoryId != CharacterCreationPriorityCategoryIds.Talent
                || option.Rank != "E")
            {
                return option;
            }
            CharacterCreationPriorityTalentOptionProjection aspected = new(
                "aspected",
                "Aspected Magician - 5 Magic",
                "Aspected Magician",
                0,
                5,
                null,
                null,
                [],
                Digest(67),
                IsEnabled: false,
                Blockers: [CharacterCreationPrerequisiteBlockers.TalentSelectionUnsupported],
                SourceAnchorIds: ["priorities.xml#talent:aspected"])
            {
                SkillGroupGrant = grant
            };
            return option with { TalentOptions = [aspected] };
        }).ToArray();
        authority = authority with { Options = options, AuthorityDigest = string.Empty };
        return authority with
        {
            AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(authority)
        };
    }

    private static CharacterCreationPrerequisiteAuthority MutateTalentOption(
        CharacterCreationPrerequisiteAuthority authority,
        Func<CharacterCreationPriorityTalentOptionProjection,
            CharacterCreationPriorityTalentOptionProjection> mutation)
    {
        CharacterCreationPriorityOptionProjection[] options = authority.Options.Select(option =>
            option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
            && option.Rank == "E"
                ? option with { TalentOptions = [mutation(option.TalentOptions.Single())] }
                : option).ToArray();
        authority = authority with { Options = options, AuthorityDigest = string.Empty };
        return authority with
        {
            AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(authority)
        };
    }

    private static void AssertAuthorityUnavailable(
        CharacterCreationPrerequisiteAuthority authority)
    {
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            authority,
            (_, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                CollectionAssert.Contains(
                    state.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.AuthorityUnavailable);
            });
    }

    internal static CharacterCreationPriorityHeritageOptionProjection HumanOption(string priorityId) =>
        new(
            "human",
            CharacterCreationPriorityChildKinds.Metatype,
            "a53d885d-a4a4-443d-b6a6-b0a55b0a96c7",
            null,
            "Human",
            null,
            1,
            0,
            false,
            HumanAttributes(),
            Digest(50),
            Digest(51),
            IsEnabled: true,
            Blockers: [],
            SourceAnchorIds: [$"priorities.xml#priority:{priorityId}:heritage:0", "metatypes.xml#metatype:human"]);

    internal static CharacterCreationPriorityTalentOptionProjection MundaneOption(string priorityId) =>
        new(
            "mundane",
            "Mundane",
            "Mundane",
            0,
            null,
            null,
            null,
            [],
            Digest(52),
            IsEnabled: true,
            Blockers: [],
            SourceAnchorIds: [$"priorities.xml#priority:{priorityId}:talent:0"]);

    internal static CharacterCreationMetatypeAttributeProjection[] HumanAttributes() =>
    [
        new("BOD", 1, 6, 10), new("AGI", 1, 6, 10), new("REA", 1, 6, 10),
        new("STR", 1, 6, 10), new("CHA", 1, 6, 10), new("INT", 1, 6, 10),
        new("LOG", 1, 6, 10), new("WIL", 1, 6, 10), new("EDG", 2, 7, 7),
        new("MAG", 1, 6, 6), new("RES", 1, 6, 6), new("ESS", 0, 6, 6),
        new("DEP", 0, 0, 0)
    ];

    private static string Digest(int value) =>
        "sha256:" + value.ToString("x64");

    private sealed class StubCharacterQueries : ICharacterFileQueries
    {
        public CharacterFileSummary ParseSummary(CharacterDocument document)
        {
            XElement root = XDocument.Parse(document.Content).Root!;
            return new CharacterFileSummary(
                root.Element("name")?.Value ?? string.Empty,
                root.Element("alias")?.Value ?? string.Empty,
                root.Element("metatype")?.Value ?? string.Empty,
                root.Element("buildmethod")?.Value ?? string.Empty,
                string.Empty,
                string.Empty,
                25,
                0,
                bool.TryParse(root.Element("created")?.Value, out bool created) && created);
        }

        public CharacterValidationResult Validate(CharacterDocument document) =>
            new(true, []);
    }

    private sealed class StubSourceResolver : ICharacterSourceDataResolver
    {
        private readonly CharacterCreationPrerequisiteAuthority _authority;

        public StubSourceResolver(CharacterCreationPrerequisiteAuthority authority)
        {
            _authority = authority;
        }

        public ICharacterSourceDataContext TryCreateContext(string characterXml) =>
            new StubSourceContext(_authority);
    }

    private sealed class StubSourceContext : ICharacterSourceDataContext
    {
        private readonly CharacterCreationPrerequisiteAuthority _authority;

        public StubSourceContext(CharacterCreationPrerequisiteAuthority authority)
        {
            _authority = authority;
        }

        public bool TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority authority)
        {
            authority = _authority;
            return true;
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
}
