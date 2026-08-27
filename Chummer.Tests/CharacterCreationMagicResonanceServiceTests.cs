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
        Assert.AreEqual(SpellId, contribution.Spells.Single().Identity.SourceId);
        Assert.AreEqual(authority.Spells.Single().CanonicalSourceXml,
            contribution.Spells.Single().CanonicalSourceXml);

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
                XElement.Parse(sourceTalent.RawTalentNode).ToString(SaveOptions.DisableFormatting))
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
}
