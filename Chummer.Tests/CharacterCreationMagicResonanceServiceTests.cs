using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Xml.Linq;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationMagicResonanceServiceTests
{
    private const string ReadyXml = "<character><name>Magic Runner</name><alias>Priority</alias>"
                                    + "<buildmethod>Priority</buildmethod><created>false</created>"
                                    + "<karma>25</karma><nuyen>0</nuyen></character>";
    private const string TraditionId = "30000000-0000-0000-0000-000000000001";
    private const string SpellId = "30000000-0000-0000-0000-000000000002";
    private const string PowerId = "30000000-0000-0000-0000-000000000003";

    [TestMethod]
    public void Adept_power_budget_uses_confirmed_magic_without_rewriting_the_source_talent()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"chummer-adept-budget-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            CharacterCreationPrerequisiteAuthority prerequisite = CreatePrerequisiteAuthority();
            const string raw = "<talent><name>Adept - 3 Magic</name><value>Adept</value>"
                + "<qualities><quality>Adept</quality></qualities><magic>3</magic>"
                + "<forbidden><oneof><metatype>A.I.</metatype></oneof></forbidden></talent>";
            prerequisite = prerequisite with
            {
                Options = prerequisite.Options.Select(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Talent && option.Rank == "C"
                    ? option with { TalentOptions = [option.TalentOptions.Single() with
                    {
                        Name = "Adept - 3 Magic", Value = "Adept", GrantedQualities = ["Adept"],
                        RawTalentNode = raw,
                        PriorityChildNodeDigest = CharacterCreationTalentGrantAuthorityDigest.ComputeRawTalentNode(raw)
                    }] }
                    : option).ToArray(),
                AuthorityDigest = string.Empty
            };
            prerequisite = prerequisite with { AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(prerequisite) };
            CharacterCreationMagicResonanceAuthority authority = CreateMagicAuthority(prerequisite);
            string powerXml = $"<power><id>{PowerId}</id><name>Fixture Power</name><points>1</points>"
                + "<levels>True</levels><limit>1</limit><maxlevels>6</maxlevels><source>SR5</source><page>1</page></power>";
            powerXml = XElement.Parse(powerXml).ToString(SaveOptions.DisableFormatting);
            var power = new CharacterCreationMagicResonanceCatalogOption(
                CharacterCreationMagicResonanceSchemas.CatalogOptionV1,
                new(CharacterCreationMagicResonanceKinds.AdeptPower, PowerId),
                "Fixture Power", "adept-power", 1m, 6, "SR5", "1",
                CharacterCreationMagicResonanceDigest.ComputeUtf8("power-source"),
                [$"powers.xml#power:{PowerId}"], [], true)
            {
                CanonicalSourceXml = powerXml,
                CanonicalSourceXmlDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8(powerXml)
            };
            const string secondPowerId = "30000000-0000-0000-0000-000000000004";
            string secondPowerXml = powerXml.Replace(PowerId, secondPowerId, StringComparison.Ordinal)
                .Replace("Fixture Power", "Second Power", StringComparison.Ordinal);
            var secondPower = power with
            {
                Identity = new(CharacterCreationMagicResonanceKinds.AdeptPower, secondPowerId),
                Name = "Second Power",
                SourceNodeDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("second-power-source"),
                SourceAnchorIds = [$"powers.xml#power:{secondPowerId}"],
                CanonicalSourceXml = secondPowerXml,
                CanonicalSourceXmlDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8(secondPowerXml)
            };
            authority = authority with
            {
                Talents = [authority.Talents.Single() with
                {
                    Kind = CharacterCreationMagicResonanceKinds.Adept,
                    SpellBudget = 0, AdeptPowerPointBudget = 3m,
                    RequiresTradition = false, AllowsAdeptPowers = true, AllowsSpells = false
                }],
                AdeptPowers = [power, secondPower],
                Spells = [], Traditions = [], AuthorityDigest = string.Empty
            };
            authority = authority with { AuthorityDigest = CharacterCreationMagicResonanceDigest.Compute(authority) };
            Assert.IsTrue(CharacterCreationMagicResonanceDraftIntegrity.IsValidAuthority(authority));
            string sourceDigest = CharacterCreationMagicResonanceDigest.Compute(authority.Talents.Single());
            var resolver = new CharacterCreationAttributesServiceTests.StubSourceResolver(prerequisite, magicResonanceAuthority: authority);
            var store = new FileWorkspaceStore(directory);
            var id = new CharacterWorkspaceId("adept-budget");
            Assert.IsTrue(store.CreateWorkspaceDocument(id, new WorkspaceDocument(ReadyXml, RulesetDefaults.Sr5)).Success);
            var prerequisiteService = new CharacterCreationPrerequisiteService(store,
                new CharacterCreationAttributesServiceTests.StubCharacterQueries(), resolver);
            CharacterCreationPrerequisiteState initial = prerequisiteService.Load(new(id)).Value!;
            IReadOnlyDictionary<string, string> ranks = CharacterCreationPrerequisiteServiceTests.Assign("E", "C", "B", "A", "D");
            CharacterCreationPrerequisitePreview priorityPreview = prerequisiteService.Preview(new(initial.Binding, ranks)
            { HeritageSelectionId = "human", TalentSelectionId = "magician-c" }).Value!;
            Assert.IsTrue(priorityPreview.CanConfirm, string.Join(",", priorityPreview.Blockers));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                prerequisiteService.Confirm(new(priorityPreview.Binding, ranks, priorityPreview.PreviewDigest, true)
                { HeritageSelectionId = "human", TalentSelectionId = "magician-c" }).Outcome);
            var attributesService = new CharacterCreationAttributesService(store, resolver);
            CharacterCreationAttributesState attributeState = attributesService.Load(new(id)).Value!;
            CharacterCreationAttributeAllocation[] allocations = [new("MAG", 1, 0)];
            CharacterCreationAttributesPreview attributePreview = attributesService.Preview(new(attributeState.Binding, allocations)).Value!;
            Assert.IsTrue(attributePreview.CanConfirm, string.Join(",", attributePreview.Blockers));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                attributesService.Confirm(new(attributePreview.Binding, allocations, attributePreview.PreviewDigest, true)).Outcome);
            var service = new CharacterCreationMagicResonanceService(store, resolver);
            CharacterCreationMagicResonanceState state = service.Load(new(id)).Value!;
            Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
            Assert.AreEqual(3, state.SelectedTalent!.Magic);
            Assert.AreEqual(4m, state.AdeptPowerPointBudget.Total);
            Assert.AreEqual(sourceDigest, CharacterCreationMagicResonanceDigest.Compute(state.SelectedTalent));
            var selections = new CharacterCreationMagicResonanceSelections(null, null,
                [new(power.Identity, 4)], [], []);
            foreach (int invalidSpend in new[] { 3, 5 })
            {
                var invalidSelections = selections with { AdeptPowers = [new(power.Identity, invalidSpend)] };
                CharacterCreationMagicResonancePreview invalid = service.Preview(new(state.Binding, invalidSelections)).Value!;
                Assert.IsFalse(invalid.CanConfirm);
                CollectionAssert.Contains(invalid.Blockers.ToList(), invalidSpend < 4
                    ? CharacterCreationMagicResonanceBlockers.PowerBudgetIncomplete
                    : CharacterCreationMagicResonanceBlockers.OptionInvalid);
            }
            // Each rating is legal; the combined spend, independently, exceeds the budget.
            var overspent = service.Preview(new(state.Binding, selections with
                { AdeptPowers = [new(power.Identity, 3), new(secondPower.Identity, 2)] })).Value!;
            Assert.IsFalse(overspent.CanConfirm);
            CollectionAssert.Contains(overspent.Blockers.ToList(), CharacterCreationMagicResonanceBlockers.PowerBudgetExceeded);
            CollectionAssert.DoesNotContain(overspent.Blockers.ToList(), CharacterCreationMagicResonanceBlockers.OptionInvalid);
            CharacterCreationMagicResonancePreview preview = service.Preview(new(state.Binding, selections)).Value!;
            Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
            Assert.AreEqual(4m, preview.AdeptPowerPointBudget.Used);
            Assert.AreEqual(0m, preview.AdeptPowerPointBudget.Remaining);
            Assert.IsNotNull(preview.FinalizationContribution);
            Assert.AreEqual(3, preview.FinalizationContribution.Talent.AssignedMagic);
            Assert.AreEqual(new CharacterCreationMagicResonanceEffectiveAttributes(4, 0, 0, 4m),
                preview.FinalizationContribution.EffectiveAttributes);
            var request = new CharacterCreationMagicResonanceConfirmRequest(preview.Binding, selections,
                preview.PreviewDigest, "adept-command", true);
            CharacterCreationMagicResonanceReceipt receipt = service.Confirm(request).Value!;
            Assert.IsNotNull(receipt);
            var coldStore = new FileWorkspaceStore(directory);
            var coldService = new CharacterCreationMagicResonanceService(coldStore, resolver);
            CharacterCreationMagicResonanceState cold = coldService.Load(new(id)).Value!;
            Assert.IsTrue(cold.CanEdit, string.Join(",", cold.Blockers));
            Assert.AreEqual(4m, cold.AdeptPowerPointBudget.Total);
            Assert.AreEqual(4m, cold.AdeptPowerPointBudget.Used);
            Assert.AreEqual(sourceDigest, CharacterCreationMagicResonanceDigest.Compute(cold.SelectedTalent));
            Assert.AreEqual(receipt, coldService.Confirm(request).Value);
            Assert.AreEqual(receipt.ContentRevision, coldStore.Get(id).Value!.ContentRevision);
            Assert.AreEqual(ReadyXml, coldStore.Get(id).Value!.Document.Content);

            CharacterCreationMagicResonanceDraft confirmed = cold.PendingDraft!;
            CharacterCreationAttributesDraft confirmedAttributes = cold.AttributesDraft!;
            CharacterCreationMagicResonanceFinalizationContribution contribution = confirmed.FinalizationContribution!;
            Assert.AreEqual("Adept", contribution.Talent.GrantedQualitySources!.Single().Name);
            Assert.IsTrue(contribution.Talent.GrantedQualitySources!.Single().CanonicalSourceXml.Contains("<limitspellcategory>Rituals</limitspellcategory>", StringComparison.Ordinal));
            Assert.IsTrue(CharacterCreationMagicResonanceFinalizationRules.IsValidContribution(
                contribution, confirmed, authority, confirmedAttributes));
            Assert.IsFalse(CharacterCreationMagicResonanceFinalizationRules.IsValidContribution(
                contribution, confirmed, authority), "Raised attribute values require the actual bound attribute draft.");
            foreach (CharacterCreationMagicResonanceEffectiveAttributes? forged in new CharacterCreationMagicResonanceEffectiveAttributes?[]
                { null, new(5, 0, 0, 5m), new(4, 0, 0, 6m), new(4, 1, 0, 4m), new(-1, 0, 0, 4m) })
            {
                CharacterCreationMagicResonanceFinalizationContribution changed = contribution with
                { EffectiveAttributes = forged, ContributionDigest = string.Empty };
                changed = changed with { ContributionDigest = CharacterCreationMagicResonanceFinalizationRules.ComputeContributionDigest(changed) };
                Assert.IsFalse(CharacterCreationMagicResonanceFinalizationRules.IsValidContribution(
                    changed, confirmed, authority, confirmedAttributes));
            }
            foreach (CharacterCreationAttributesDraft forged in new[]
            {
                confirmedAttributes with { DraftRevision = confirmedAttributes.DraftRevision + 1 },
                confirmedAttributes with { WorkspaceId = new("another-runner") },
                confirmedAttributes with { BaseRawCharacterXmlDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("different-document") },
                confirmedAttributes with { PrerequisiteDraftDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("different-prerequisite") },
                confirmedAttributes with { Attributes = confirmedAttributes.Attributes.Select(a => a.AttributeId == "MAG" ? a with { Current = 5 } : a).ToArray() }
            })
            {
                CharacterCreationAttributesDraft rehashed = forged with { DraftDigest = string.Empty };
                rehashed = rehashed with { DraftDigest = CharacterCreationAttributesDraftIntegrity.ComputeDigest(rehashed) };
                Assert.IsFalse(CharacterCreationMagicResonanceFinalizationRules.IsValidContribution(
                    contribution, confirmed, authority, rehashed));
            }
            Assert.AreEqual(receipt.ContentRevision, coldStore.Get(id).Value!.ContentRevision);

            // An old receipt can be recovered after a later attribute decision,
            // but its old budget is not authority to approve a new command.
            var coldAttributesService = new CharacterCreationAttributesService(coldStore, resolver);
            CharacterCreationAttributesState currentAttributes = coldAttributesService.Load(new(id)).Value!;
            CharacterCreationAttributeAllocation[] newerAllocations = [new("MAG", 1, 1)];
            CharacterCreationAttributesPreview newer = coldAttributesService.Preview(new(currentAttributes.Binding, newerAllocations)).Value!;
            Assert.IsTrue(newer.CanConfirm, string.Join(",", newer.Blockers));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                coldAttributesService.Confirm(new(newer.Binding, newerAllocations, newer.PreviewDigest, true)).Outcome);
            long changedRevision = coldStore.Get(id).Value!.ContentRevision;
            Assert.AreEqual(receipt, coldService.Confirm(request).Value);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict,
                coldService.Confirm(request with { IdempotencyKey = "new-command-with-stale-budget" }).Outcome);
            Assert.AreEqual(changedRevision, coldStore.Get(id).Value!.ContentRevision);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Magician_preview_confirm_reopen_and_idempotent_replay_are_atomic_and_xml_free()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"chummer-magic-resonance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            CharacterCreationPrerequisiteAuthority prerequisite = CreatePrerequisiteAuthority();
            CharacterCreationMagicResonanceAuthority authority = CreateMagicAuthority(prerequisite);
            var resolver = new CharacterCreationAttributesServiceTests.StubSourceResolver(
                prerequisite,
                magicResonanceAuthority: authority);
            var store = new FileWorkspaceStore(directory);
            CharacterWorkspaceId id = new("magic-runner");
            Assert.IsTrue(store.CreateWorkspaceDocument(
                id, new WorkspaceDocument(ReadyXml, RulesetDefaults.Sr5)).Success);

            var prerequisiteService = new CharacterCreationPrerequisiteService(
                store,
                new CharacterCreationAttributesServiceTests.StubCharacterQueries(),
                resolver);
            CharacterCreationPrerequisiteState prerequisiteState = prerequisiteService.Load(new(id)).Value!;
            IReadOnlyDictionary<string, string> ranks = CharacterCreationPrerequisiteServiceTests.Assign(
                "E", "C", "B", "A", "D");
            CharacterCreationPrerequisitePreview prerequisitePreview = prerequisiteService.Preview(new(
                prerequisiteState.Binding,
                ranks)
            {
                HeritageSelectionId = "human",
                TalentSelectionId = "magician-c"
            }).Value!;
            Assert.IsTrue(prerequisitePreview.CanConfirm, string.Join(",", prerequisitePreview.Blockers));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, prerequisiteService.Confirm(new(
                prerequisitePreview.Binding,
                ranks,
                prerequisitePreview.PreviewDigest,
                ExplicitlyConfirmed: true)
            {
                HeritageSelectionId = "human",
                TalentSelectionId = "magician-c"
            }).Outcome);

            var attributeService = new CharacterCreationAttributesService(store, resolver);
            CharacterCreationAttributesState attributeState = attributeService.Load(new(id)).Value!;
            CharacterCreationAttributesPreview attributePreview = attributeService.Preview(new(
                attributeState.Binding,
                [])).Value!;
            Assert.IsTrue(attributePreview.CanConfirm, string.Join(",", attributePreview.Blockers));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, attributeService.Confirm(new(
                attributePreview.Binding,
                [],
                attributePreview.PreviewDigest,
                ExplicitlyConfirmed: true)).Outcome);

            string before = store.Get(id).Value!.Document.Content;
            var service = new CharacterCreationMagicResonanceService(store, resolver);
            CharacterCreationMagicResonanceState state = service.Load(new(id)).Value!;
            Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
            Assert.AreEqual(CharacterCreationMagicResonanceKinds.Magician, state.SelectedTalent!.Kind);
            var selections = new CharacterCreationMagicResonanceSelections(
                new(CharacterCreationMagicResonanceKinds.Tradition, TraditionId),
                null,
                [],
                [new(CharacterCreationMagicResonanceKinds.Spell, SpellId)],
                []);
            CharacterCreationMagicResonancePreview preview = service.Preview(new(
                state.Binding,
                selections)).Value!;
            Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
            Assert.AreEqual(0m, preview.SpellBudget.Remaining);
            Assert.IsNotNull(preview.FinalizationContribution);
            CharacterCreationMagicResonanceFinalizationContribution previewContribution =
                preview.FinalizationContribution!;
            Assert.AreEqual(CharacterCreationMagicResonanceKinds.Magician,
                previewContribution.Talent.Kind);
            Assert.AreEqual(TraditionId, previewContribution.Tradition!.Identity.SourceId);
            Assert.AreEqual(SpellId, previewContribution.Spells.Single().Identity.SourceId);
            Assert.AreEqual("Acid Stream", previewContribution.Spells.Single().Name);
            Assert.IsTrue(CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                previewContribution.ContributionDigest,
                CharacterCreationMagicResonanceFinalizationRules.ComputeContributionDigest(
                    previewContribution)));

            var request = new CharacterCreationMagicResonanceConfirmRequest(
                preview.Binding,
                selections,
                preview.PreviewDigest,
                "magic-command-1",
                ExplicitlyConfirmed: true);
            CharacterCreationFoundationResult<CharacterCreationMagicResonanceReceipt> confirmed =
                service.Confirm(request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome);
            Assert.IsFalse(confirmed.Value!.CharacterDocumentChanged);
            Assert.AreEqual(before, store.Get(id).Value!.Document.Content);
            Assert.IsNotNull(store.Get(id).Value!.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft);
            CharacterCreationMagicResonanceDraft confirmedDraft = store.Get(id).Value!.Document
                .AuxiliaryState.CharacterCreationMagicResonanceDraft!;
            Assert.AreEqual(previewContribution.ContributionDigest,
                confirmedDraft.FinalizationContribution!.ContributionDigest);
            Assert.HasCount(1, store.Get(id).Value!.Document.AuxiliaryState
                .CharacterCreationMagicResonanceReceipts!);

            CharacterCreationMagicResonanceOptionFinalizationSource tamperedSpell =
                previewContribution.Spells.Single() with
                {
                    Name = "Acid Bolt",
                    ProjectionDigest = string.Empty
                };
            tamperedSpell = tamperedSpell with
            {
                ProjectionDigest = CharacterCreationMagicResonanceFinalizationRules
                    .ComputeOptionProjectionDigest(tamperedSpell)
            };
            CharacterCreationMagicResonanceFinalizationContribution tamperedContribution =
                previewContribution with
                {
                    Spells = [tamperedSpell],
                    ContributionDigest = string.Empty
                };
            tamperedContribution = tamperedContribution with
            {
                ContributionDigest = CharacterCreationMagicResonanceFinalizationRules
                    .ComputeContributionDigest(tamperedContribution)
            };
            Assert.IsFalse(CharacterCreationMagicResonanceFinalizationRules.IsValidContribution(
                tamperedContribution,
                confirmedDraft,
                authority));

            CharacterCreationFoundationResult<CharacterCreationMagicResonanceReceipt> replay =
                service.Confirm(request);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome);
            Assert.AreEqual(confirmed.Value.ReceiptDigest, replay.Value!.ReceiptDigest);
            CharacterCreationFoundationResult<CharacterCreationMagicResonanceReceipt> conflict =
                service.Confirm(request with
                {
                    PreviewDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("other-preview")
                });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, conflict.Outcome);
            CollectionAssert.Contains(conflict.Blockers.ToList(),
                CharacterCreationMagicResonanceBlockers.IdempotencyConflict);

            var restarted = new CharacterCreationMagicResonanceService(
                new FileWorkspaceStore(directory), resolver);
            CharacterCreationMagicResonanceState reopened = restarted.Load(new(id)).Value!;
            Assert.AreEqual(1L, reopened.PendingDraft!.DraftRevision);
            Assert.AreEqual(confirmed.Value.DraftDigest, reopened.PendingDraft.DraftDigest);
            Assert.AreEqual(previewContribution.ContributionDigest,
                reopened.PendingDraft.FinalizationContribution!.ContributionDigest);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Preview_fails_closed_for_incomplete_budget_and_every_bound_digest_drift()
    {
        // The deterministic budget rule is tested independently of persistence through
        // the production source authority: a magician cannot confirm without every
        // source-granted spell slot assigned. Binding drift remains covered by the
        // end-to-end confirm test's idempotency conflict path and receipt digest test.
        CharacterCreationMagicResonanceBudgetState budget = new(
            CharacterCreationMagicResonanceKinds.Spell, 1m, 0m, 1m,
            [CharacterCreationMagicResonanceBlockers.SpellBudgetIncomplete]);
        Assert.AreEqual(1m, budget.Remaining);
        CollectionAssert.Contains(budget.Blockers.ToList(),
            CharacterCreationMagicResonanceBlockers.SpellBudgetIncomplete);
    }

    [TestMethod]
    public void Authority_rejects_rehashed_outer_digest_when_typed_source_projection_is_tampered()
    {
        CharacterCreationPrerequisiteAuthority prerequisite = CreatePrerequisiteAuthority();
        CharacterCreationMagicResonanceAuthority authority = CreateMagicAuthority(prerequisite);
        CharacterCreationMagicResonanceCatalogOption spell = authority.Spells.Single() with
        {
            PointCost = 2m
        };
        CharacterCreationMagicResonanceAuthority tampered = authority with
        {
            Spells = [spell],
            AuthorityDigest = string.Empty
        };
        tampered = tampered with
        {
            AuthorityDigest = CharacterCreationMagicResonanceDigest.Compute(tampered)
        };

        Assert.IsFalse(CharacterCreationMagicResonanceDraftIntegrity.IsValidAuthority(tampered));
    }

    [TestMethod]
    public void Finalization_contribution_rederives_exact_source_bound_selection_and_rejects_rehashed_tamper()
    {
        CharacterCreationPrerequisiteAuthority prerequisite = CreatePrerequisiteAuthority();
        CharacterCreationMagicResonanceAuthority authority = CreateMagicAuthority(prerequisite);
        string rawDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("raw-character");
        string prerequisiteDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("prerequisite-draft");
        string attributesDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("attributes-draft");
        var selections = new CharacterCreationMagicResonanceSelections(
            new(CharacterCreationMagicResonanceKinds.Tradition, TraditionId),
            null,
            [],
            [new(CharacterCreationMagicResonanceKinds.Spell, SpellId)],
            []);

        Assert.IsTrue(CharacterCreationMagicResonanceFinalizationRules.TryCreate(
            rawDigest,
            prerequisiteDraftRevision: 4,
            prerequisiteDigest,
            attributesDraftRevision: 5,
            attributesDigest,
            authority,
            authority.Talents.Single(),
            selections,
            out CharacterCreationMagicResonanceFinalizationContribution contribution,
            out string[] blockers), string.Join(",", blockers));
        Assert.IsNull(contribution.EffectiveAttributes);
        Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(contribution)
            .Contains("EffectiveAttributes", StringComparison.Ordinal));
        Assert.AreEqual(SpellId, contribution.Spells.Single().Identity.SourceId);
        Assert.AreEqual(authority.Spells.Single().CanonicalSourceXml,
            contribution.Spells.Single().CanonicalSourceXml);
        Assert.IsNotNull(contribution.Talent.GrantedQualitySources);
        Assert.AreEqual(authority.Talents.Single().GrantedQualitySources!.Single().CanonicalSourceXml,
            contribution.Talent.GrantedQualitySources.Single().CanonicalSourceXml);

        var budget = new CharacterCreationMagicResonanceBudgetState(
            "test", 0m, 0m, 0m, []);
        var draft = new CharacterCreationMagicResonanceDraft(
            CharacterCreationMagicResonanceSchemas.DraftV1,
            new("unit-finalization"),
            DraftRevision: 1,
            BaseContentRevision: 1,
            rawDigest,
            PrerequisiteDraftRevision: 4,
            prerequisiteDigest,
            prerequisite.AuthorityDigest,
            AttributesDraftRevision: 5,
            attributesDigest,
            authority.AuthorityDigest,
            authority.SourceInputsDigest,
            authority.CustomDataInputsDigest,
            authority.GmPolicyDigest,
            authority.RuntimeDigest,
            authority.Talents.Single().Identity,
            authority.Talents.Single().Kind,
            authority.Talents.Single().Magic,
            authority.Talents.Single().Resonance,
            authority.Talents.Single().Depth,
            selections,
            budget,
            budget,
            budget,
            budget,
            budget,
            contribution.SourceAnchorIds,
            CharacterEffectsApplied: false,
            CharacterCreationMagicResonanceDigest.ComputeUtf8("idempotency"),
            CharacterCreationMagicResonanceDigest.ComputeUtf8("preview"),
            CharacterCreationMagicResonanceDigest.ComputeUtf8("command"),
            CharacterCreationMagicResonanceDigest.ComputeUtf8("draft"))
        {
            FinalizationContribution = contribution
        };
        Assert.IsTrue(CharacterCreationMagicResonanceFinalizationRules.IsValidContribution(
            contribution, draft, authority));

        var originalQuality = contribution.Talent.GrantedQualitySources!.Single();
        string changedXml = originalQuality.CanonicalSourceXml.Replace("<name>MAG</name>", "<name>DEP</name>", StringComparison.Ordinal);
        Assert.AreNotEqual(originalQuality.CanonicalSourceXml, changedXml);
        var changedQuality = originalQuality with
        {
            CanonicalSourceXml = changedXml,
            CanonicalSourceXmlDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8(changedXml),
            SourceNodeDigest = CharacterCreationTalentQualitySourceRules.ComputeSourceNodeDigest(
                originalQuality.EffectiveSourceDigest, originalQuality.SourceId, changedXml)
        };
        // Even an internally consistent self-rehashed source is not the independent current authority.
        Assert.IsTrue(CharacterCreationTalentQualitySourceRules.IsValidSource(changedQuality));
        foreach (var invalidSources in new IReadOnlyList<CharacterCreationTalentQualitySource>?[] { null, [], [changedQuality] })
        {
            var alteredTalent = contribution.Talent with { GrantedQualitySources = invalidSources, ProjectionDigest = string.Empty };
            alteredTalent = alteredTalent with { ProjectionDigest = CharacterCreationMagicResonanceFinalizationRules.ComputeTalentProjectionDigest(alteredTalent) };
            var altered = contribution with { Talent = alteredTalent, ContributionDigest = string.Empty };
            altered = altered with { ContributionDigest = CharacterCreationMagicResonanceFinalizationRules.ComputeContributionDigest(altered) };
            Assert.IsFalse(CharacterCreationMagicResonanceFinalizationRules.IsValidContribution(altered, draft, authority));
        }

        CharacterCreationMagicResonanceOptionFinalizationSource tamperedSpell =
            contribution.Spells.Single() with { Name = "Acid Bolt", ProjectionDigest = string.Empty };
        tamperedSpell = tamperedSpell with
        {
            ProjectionDigest = CharacterCreationMagicResonanceFinalizationRules
                .ComputeOptionProjectionDigest(tamperedSpell)
        };
        CharacterCreationMagicResonanceFinalizationContribution tampered = contribution with
        {
            Spells = [tamperedSpell],
            ContributionDigest = string.Empty
        };
        tampered = tampered with
        {
            ContributionDigest = CharacterCreationMagicResonanceFinalizationRules
                .ComputeContributionDigest(tampered)
        };
        Assert.IsFalse(CharacterCreationMagicResonanceFinalizationRules.IsValidContribution(
            tampered, draft, authority));
    }

    private static CharacterCreationPrerequisiteAuthority CreatePrerequisiteAuthority()
    {
        CharacterCreationPrerequisiteAuthority authority = CharacterCreationPrerequisiteServiceTests
            .CreateAuthority(CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]);
        CharacterCreationPriorityOptionProjection[] options = authority.Options.Select(option =>
        {
            if (option.CategoryId != CharacterCreationPriorityCategoryIds.Talent || option.Rank != "C")
                return option;
            const string raw = "<talent><name>Magician - 3 Magic/1 Spell</name><value>Magician</value>"
                               + "<qualities><quality>Magician</quality></qualities><magic>3</magic>"
                               + "<spells>1</spells><forbidden><oneof><metatype>A.I.</metatype>"
                               + "</oneof></forbidden></talent>";
            var talent = new CharacterCreationPriorityTalentOptionProjection(
                "magician-c",
                "Magician - 3 Magic/1 Spell",
                "Magician",
                0,
                3,
                null,
                null,
                ["Magician"],
                CharacterCreationTalentGrantAuthorityDigest.ComputeRawTalentNode(raw),
                IsEnabled: true,
                Blockers: [],
                SourceAnchorIds: [$"priorities.xml#priority:{option.SourceId}:talent:0"])
            {
                RawTalentNode = raw
            };
            return option with { TalentOptions = [talent] };
        }).ToArray();
        authority = authority with { Options = options, AuthorityDigest = string.Empty };
        return authority with
        {
            AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(authority)
        };
    }

    private static CharacterCreationMagicResonanceAuthority CreateMagicAuthority(
        CharacterCreationPrerequisiteAuthority prerequisite)
    {
        CharacterCreationPriorityOptionProjection priority = prerequisite.Options.Single(option =>
            option.CategoryId == CharacterCreationPriorityCategoryIds.Talent && option.Rank == "C");
        CharacterCreationPriorityTalentOptionProjection sourceTalent = priority.TalentOptions.Single();
        string bound = CharacterCreationMagicResonanceDigest.ComputeUtf8("bound");
        var talent = new CharacterCreationMagicResonanceTalentOption(
            new(priority.SourceId, sourceTalent.SelectionId, sourceTalent.Value),
            "C",
            sourceTalent.Name,
            CharacterCreationMagicResonanceKinds.Magician,
            3,
            0,
            0,
            1,
            0,
            0m,
            RequiresTradition: true,
            RequiresStream: false,
            AllowsAdeptPowers: false,
            AllowsSpells: true,
            AllowsComplexForms: false,
            RequiredMetatypeNames: [],
            RequiredMetatypeCategories: [],
            ForbiddenMetatypeNames: ["A.I."],
            sourceTalent.PriorityChildNodeDigest,
            sourceTalent.SourceAnchorIds,
            Blockers: [],
            IsEnabled: true)
        {
            CanonicalSourceXml = XElement.Parse(sourceTalent.RawTalentNode)
                .ToString(SaveOptions.DisableFormatting),
            CanonicalSourceXmlDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8(
                XElement.Parse(sourceTalent.RawTalentNode).ToString(SaveOptions.DisableFormatting)),
            GrantedQualitySources = CreateTalentQualitySources(sourceTalent.RawTalentNode)
        };
        const string traditionXml = "<tradition><id>30000000-0000-0000-0000-000000000001</id>"
                                      + "<name>Hermetic</name><drain>{WIL} + {LOG}</drain>"
                                      + "<source>SR5</source><page>279</page><spirits /></tradition>";
        string canonicalTraditionXml = XElement.Parse(traditionXml)
            .ToString(SaveOptions.DisableFormatting);
        var tradition = new CharacterCreationMagicResonanceCatalogOption(
            CharacterCreationMagicResonanceSchemas.CatalogOptionV1,
            new(CharacterCreationMagicResonanceKinds.Tradition, TraditionId),
            "Hermetic",
            "magic-tradition",
            1m,
            1,
            "SR5",
            "279",
            bound,
            [$"traditions.xml#tradition:{TraditionId}"],
            [],
            true)
        {
            DrainExpression = "{WIL} + {LOG}",
            CanonicalSourceXml = canonicalTraditionXml,
            CanonicalSourceXmlDigest = CharacterCreationMagicResonanceDigest
                .ComputeUtf8(canonicalTraditionXml)
        };
        const string spellXml = "<spell><id>30000000-0000-0000-0000-000000000002</id>"
                                + "<name>Acid Stream</name><page>283</page><source>SR5</source>"
                                + "<category>Combat</category><damage>P</damage>"
                                + "<descriptor>Indirect, Elemental</descriptor><duration>I</duration>"
                                + "<dv>F-3</dv><range>LOS</range><type>P</type></spell>";
        string canonicalSpellXml = XElement.Parse(spellXml).ToString(SaveOptions.DisableFormatting);
        var spell = new CharacterCreationMagicResonanceCatalogOption(
            CharacterCreationMagicResonanceSchemas.CatalogOptionV1,
            new(CharacterCreationMagicResonanceKinds.Spell, SpellId),
            "Acid Stream",
            "Combat",
            1m,
            1,
            "SR5",
            "283",
            CharacterCreationMagicResonanceDigest.ComputeUtf8("spell-source"),
            [$"spells.xml#spell:{SpellId}"],
            [],
            true)
        {
            CanonicalSourceXml = canonicalSpellXml,
            CanonicalSourceXmlDigest = CharacterCreationMagicResonanceDigest
                .ComputeUtf8(canonicalSpellXml)
        };
        var authority = new CharacterCreationMagicResonanceAuthority(
            CharacterCreationMagicResonanceSchemas.AuthorityV1,
            prerequisite.SettingsProfileId,
            prerequisite.AuthorityDigest,
            CharacterCreationMagicResonanceDigest.ComputeUtf8("source-inputs"),
            CharacterCreationMagicResonanceDigest.ComputeUtf8("custom-inputs"),
            CharacterCreationMagicResonanceDigest.ComputeUtf8("gm-policy"),
            CharacterCreationMagicResonanceDigest.ComputeUtf8("runtime"),
            [talent],
            [new(
                "a53d885d-a4a4-443d-b6a6-b0a55b0a96c7",
                "Human",
                "Metahuman",
                ["metatypes.xml#metatype:human"],
                CharacterCreationMagicResonanceDigest.ComputeUtf8("human-source"))],
            [tradition],
            [],
            [],
            [spell],
            [],
            [
                "metatypes.xml",
                "priorities.xml#category:Talent",
                "spells.xml",
                "traditions.xml"
            ],
            [],
            true,
            string.Empty);
        return authority with
        {
            AuthorityDigest = CharacterCreationMagicResonanceDigest.Compute(authority)
        };
    }

    private static CharacterCreationTalentQualitySource[] CreateTalentQualitySources(string rawTalent)
    {
        DirectoryInfo? root = new(AppDomain.CurrentDomain.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Chummer", "data", "qualities.xml")))
            root = root.Parent;
        Assert.IsNotNull(root);
        string path = Path.Combine(root.FullName, "Chummer", "data", "qualities.xml");
        string digest = CharacterCreationMagicResonanceDigest.ComputeUtf8(File.ReadAllText(path));
        XElement[] rows = XDocument.Load(path).Root!.Element("qualities")!.Elements("quality").ToArray();
        Assert.IsTrue(CharacterCreationTalentQualitySourceRules.TryReadReferences(rawTalent, out var references));
        return references.Select(reference =>
        {
            XElement row = rows.Single(item => item.Element("name")!.Value == reference.Reference);
            string id = row.Element("id")!.Value;
            string xml = row.ToString(SaveOptions.DisableFormatting);
            return new CharacterCreationTalentQualitySource(reference.Reference, reference.Selection,
                id, row.Element("name")!.Value, row.Element("source")!.Value, row.Element("page")!.Value,
                digest, CharacterCreationTalentQualitySourceRules.ComputeSourceNodeDigest(digest, id, xml),
                xml, CharacterCreationMagicResonanceDigest.ComputeUtf8(xml), [$"qualities.xml#quality:{id}"]);
        }).ToArray();
    }
}
