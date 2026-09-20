using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationQualitiesRulesTests
{
    [TestMethod]
    [DataRow(false, false, 30, 40, 104)]
    [DataRow(true, false, 35, 40, 99)]
    [DataRow(false, true, 30, 25, 89)]
    [DataRow(true, true, 35, 25, 84)]
    public void Quality_cost_policy_applies_multiplier_and_excess_before_cap_exempt_purchases(
        bool doublePositive, bool capNegative, int positiveLimit, int negativeLimit, int remaining)
    {
        var exemptPositive = Option("exempt-positive", CharacterCreationQualityType.Positive, 7)
            with { CountsAgainstQualityLimit = false };
        exemptPositive = exemptPositive with { OptionDigest = CharacterCreationQualitiesRules.ComputeOptionDigest(exemptPositive) };
        var exemptNegative = Option("exempt-negative", CharacterCreationQualityType.Negative, -4)
            with { CountsAgainstQualityLimit = false };
        exemptNegative = exemptNegative with { OptionDigest = CharacterCreationQualitiesRules.ComputeOptionDigest(exemptNegative) };
        var authority = Authority(Option("positive", CharacterCreationQualityType.Positive, 15),
            Option("negative", CharacterCreationQualityType.Negative, -20), exemptPositive, exemptNegative)
            with { CostPolicy = new(2, doublePositive, capNegative),
                MayExceedPositiveQualityLimit = true, MayExceedNegativeQualityLimit = true };
        authority = authority with { AuthorityDigest = CharacterCreationQualitiesRules.ComputeAuthorityDigest(authority) };
        var preview = CharacterCreationQualitiesRules.Evaluate(new(Binding(authority, 100), authority,
            authority.Options.Select(option => option.OptionId).ToArray()));
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        Assert.AreEqual(positiveLimit, preview.PositiveQualityBudget.Used);
        Assert.AreEqual(negativeLimit, preview.NegativeQualityBudget.Used);
        Assert.AreEqual(remaining, preview.KarmaRemaining);
        Assert.AreEqual(15, preview.Selections.Single(item => item.OptionId == "positive").KarmaCost,
            "Legacy source BP must not be scaled in the serialized quality instance.");
    }

    [TestMethod]
    public void Quality_cost_policy_keeps_free_limit_contributions_and_metagenic_balance_separate()
    {
        Assert.IsTrue(CharacterCreationQualityCostRules.TryCalculate(new(2, true, true), 25,
            [new(10, true, false, false), new(6, true, true, true),
             new(-5, true, true, true), new(-30, true, false, false),
             new(80, false, false, false)], out var totals));
        Assert.AreEqual(39, totals.PositiveLimitKarma); // 32 + 7 excess, includes free limit-only quality.
        Assert.AreEqual(25, totals.NegativeLimitKarma);
        Assert.AreEqual(12, totals.PositiveKarmaSpent); // Only actual purchases affect the cost excess.
        Assert.AreEqual(10, totals.NegativeKarmaGranted);
        Assert.AreEqual(2, totals.NetKarmaSpent);
        Assert.AreEqual(6, totals.MetagenicPositiveKarma); // Metagenic balance is source BP, not multiplied.
        Assert.AreEqual(5, totals.MetagenicNegativeKarma);
    }

    [TestMethod]
    public void Quality_cost_policy_rejects_overflow_and_negative_multiplier_without_exceptions()
    {
        Assert.IsFalse(CharacterCreationQualityCostRules.TryCalculate(new(-1, false, false), 25,
            [], out _));
        Assert.IsFalse(CharacterCreationQualityCostRules.TryCalculate(new(int.MaxValue, true, false), 25,
            [new(int.MaxValue, true, true, false), new(int.MaxValue, true, true, false),
             new(int.MaxValue, true, true, false)], out _));
        Assert.IsFalse(CharacterCreationQualityCostRules.TryCalculate(new(1, false, false), 25,
            [new(int.MinValue, true, true, false)], out _));
        Assert.IsTrue(CharacterCreationQualityCostRules.TryCalculate(new(0, true, true), 25,
            [new(6, true, true, true), new(-5, true, true, true)], out var free));
        Assert.AreEqual(0, free.NetKarmaSpent);
        Assert.AreEqual(6, free.MetagenicPositiveKarma);

        var authority = Authority(Option("overflow", CharacterCreationQualityType.Positive, int.MaxValue))
            with { CostPolicy = new(2, false, false) };
        authority = authority with { AuthorityDigest = CharacterCreationQualitiesRules.ComputeAuthorityDigest(authority) };
        var preview = CharacterCreationQualitiesRules.Evaluate(new(Binding(authority), authority, ["overflow"]));
        Assert.IsFalse(preview.CanConfirm);
        CollectionAssert.Contains(preview.Blockers.ToArray(), CharacterCreationQualitiesBlockers.AuthorityUnavailable);
    }

    [TestMethod]
    public void Quality_cost_policy_is_digest_bound_and_does_not_rewrite_historical_v1_authority()
    {
        var historical = Authority(Option("quality", CharacterCreationQualityType.Positive, 10));
        string json = System.Text.Json.JsonSerializer.Serialize(historical);
        Assert.IsFalse(json.Contains("CostPolicy", StringComparison.Ordinal));
        var roundtrip = System.Text.Json.JsonSerializer.Deserialize<CharacterCreationQualitiesAuthority>(json)!;
        Assert.AreEqual(historical.AuthorityDigest, CharacterCreationQualitiesRules.ComputeAuthorityDigest(roundtrip));
        Assert.AreEqual(15, CharacterCreationQualitiesRules.Evaluate(new(Binding(roundtrip), roundtrip, ["quality"])).KarmaRemaining);

        var changed = historical with { CostPolicy = new(2, false, false) };
        Assert.AreNotEqual(historical.AuthorityDigest, CharacterCreationQualitiesRules.ComputeAuthorityDigest(changed));
        var forged = CharacterCreationQualitiesRules.Evaluate(new(Binding(historical), changed, ["quality"]));
        Assert.IsFalse(forged.CanConfirm);
        changed = changed with { AuthorityDigest = CharacterCreationQualitiesRules.ComputeAuthorityDigest(changed) };
        var valid = CharacterCreationQualitiesRules.Evaluate(new(Binding(changed), changed, ["quality"]));
        Assert.IsTrue(valid.CanConfirm, string.Join(",", valid.Blockers));
        Assert.AreEqual(5, valid.KarmaRemaining);
    }

    [TestMethod]
    public void Heritage_quality_retains_source_cost_but_consumes_no_budget_and_cannot_be_bought_again()
    {
        var option = Option("heritage-source", CharacterCreationQualityType.Positive, 30, metagenic: true);
        var grant = new CharacterCreationGrantedQuality("talent-quality", option.SourceId, option.SelectionKey,
            option.Name, option.Type, 1, 30, true, false, false, "Heritage", option.SourceAnchorIds, string.Empty);
        grant = grant with { GrantDigest = CharacterCreationQualitiesRules.ComputeGrantDigest(grant) };
        var authority = Authority(option) with { GrantedQualities = [grant], AuthorityDigest = string.Empty };
        authority = authority with { AuthorityDigest = CharacterCreationQualitiesRules.ComputeAuthorityDigest(authority) };
        var preview = CharacterCreationQualitiesRules.Evaluate(new(Binding(authority, creationKarma: 25), authority, []));
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        Assert.AreEqual(25, preview.KarmaRemaining);
        Assert.AreEqual(0, preview.PositiveQualityBudget.Used);
        Assert.AreEqual(0, preview.MetagenicPositiveKarma);
        Assert.AreEqual(30, preview.GrantedQualities.Single().KarmaCost);
        var duplicate = CharacterCreationQualitiesRules.Evaluate(new(Binding(authority), authority, [option.OptionId]));
        Assert.IsFalse(duplicate.CanConfirm);
        CollectionAssert.Contains(duplicate.Blockers.ToArray(), CharacterCreationQualitiesBlockers.DuplicateSelection);
    }

    [TestMethod]
    public void Evaluate_resolves_only_stable_authority_options_and_computes_separate_limits()
    {
        CharacterCreationQualitiesAuthority authority = Authority(
            Option("positive-1", CharacterCreationQualityType.Positive, 12),
            Option("negative-1", CharacterCreationQualityType.Negative, -8));
        CharacterCreationQualitiesPreview preview = CharacterCreationQualitiesRules.Evaluate(new(
            Binding(authority, creationKarma: 25),
            authority,
            ["negative-1", "positive-1"]));

        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        Assert.AreEqual(12, preview.PositiveQualityBudget.Used);
        Assert.AreEqual(8, preview.NegativeQualityBudget.Used);
        Assert.AreEqual(21, preview.KarmaRemaining);
        CollectionAssert.AreEqual(
            new[] { "negative-1", "positive-1" },
            preview.Selections.Select(static item => item.OptionId).ToArray());
        Assert.IsTrue(preview.Selections.All(static item => item.SourceAnchorIds.Count == 1));
    }

    [TestMethod]
    public void Evaluate_fails_closed_for_unknown_duplicate_disabled_and_inexact_choices()
    {
        CharacterCreationQualityCatalogOption disabled = Option(
            "disabled",
            CharacterCreationQualityType.Positive,
            5) with
        {
            IsSelectable = false,
            DisableReasonKey = "quality-requirement-missing",
            OptionDigest = string.Empty
        };
        disabled = disabled with
        {
            OptionDigest = CharacterCreationQualitiesRules.ComputeOptionDigest(disabled)
        };
        CharacterCreationQualitiesAuthority authority = Authority(disabled);

        CharacterCreationQualitiesPreview preview = CharacterCreationQualitiesRules.Evaluate(new(
            Binding(authority),
            authority,
            ["disabled", "disabled", "invented"]));

        Assert.IsFalse(preview.CanConfirm);
        CollectionAssert.Contains(preview.Blockers.ToList(),
            CharacterCreationQualitiesBlockers.DuplicateSelection);
        CollectionAssert.Contains(preview.Blockers.ToList(), "quality-requirement-missing");
        CollectionAssert.Contains(preview.Blockers.ToList(),
            CharacterCreationQualitiesBlockers.InvalidSelection);
        Assert.HasCount(0, preview.Selections);
    }

    [TestMethod]
    public void Evaluate_enforces_profile_caps_karma_and_metagenic_balance()
    {
        CharacterCreationQualitiesAuthority authority = Authority(
            Option("positive", CharacterCreationQualityType.Positive, 26),
            Option("negative", CharacterCreationQualityType.Negative, -26),
            Option("meta-positive", CharacterCreationQualityType.Positive, 6, metagenic: true),
            Option("meta-negative", CharacterCreationQualityType.Negative, -4, metagenic: true));
        CharacterCreationQualitiesPreview preview = CharacterCreationQualitiesRules.Evaluate(new(
            Binding(authority, creationKarma: 0),
            authority,
            ["positive", "negative", "meta-positive", "meta-negative"]));

        Assert.IsFalse(preview.CanConfirm);
        CollectionAssert.Contains(preview.Blockers.ToList(),
            CharacterCreationQualitiesBlockers.PositiveLimitExceeded);
        CollectionAssert.Contains(preview.Blockers.ToList(),
            CharacterCreationQualitiesBlockers.NegativeLimitExceeded);
        CollectionAssert.Contains(preview.Blockers.ToList(),
            CharacterCreationQualitiesBlockers.MetagenicLimitExceeded);
        CollectionAssert.Contains(preview.Blockers.ToList(),
            CharacterCreationQualitiesBlockers.MetagenicImbalanced);
        CollectionAssert.Contains(preview.Blockers.ToList(),
            CharacterCreationQualitiesBlockers.KarmaExceeded);
    }

    [TestMethod]
    public void TryPlan_requires_fresh_explicit_review_and_never_changes_character_document()
    {
        CharacterCreationQualitiesAuthority authority = Authority(
            Option("positive", CharacterCreationQualityType.Positive, 5));
        CharacterCreationQualitiesPreview preview = CharacterCreationQualitiesRules.Evaluate(new(
            Binding(authority), authority, ["positive"]));
        Guid transactionId = Guid.NewGuid();

        Assert.IsFalse(CharacterCreationQualitiesRules.TryPlan(
            preview, preview.PreviewDigest, "quality-draft-1", false, false, transactionId, out _));
        Assert.IsFalse(CharacterCreationQualitiesRules.TryPlan(
            preview, preview.PreviewDigest, "quality-draft-1", true, true, transactionId, out _));
        Assert.IsFalse(CharacterCreationQualitiesRules.TryPlan(
            preview, Digest('f'), "quality-draft-1", true, false, transactionId, out _));
        Assert.IsTrue(CharacterCreationQualitiesRules.TryPlan(
            preview,
            preview.PreviewDigest,
            "quality-draft-1",
            true,
            false,
            transactionId,
            out CharacterCreationQualitiesDraftPlan plan));
        Assert.IsFalse(plan.CharacterDocumentChanged);
        Assert.AreEqual(preview.Binding.ContentRevision + 1, plan.TargetContentRevision);
        Assert.AreEqual(preview.Binding.SavedRevision + 1, plan.TargetSavedRevision);
        Assert.AreEqual(preview.AuthorityDigest, plan.AuthorityDigest);
    }

    [TestMethod]
    public void Binding_and_command_digest_fail_closed_and_are_order_independent()
    {
        CharacterCreationQualitiesAuthority authority = Authority(
            Option("positive", CharacterCreationQualityType.Positive, 5),
            Option("negative", CharacterCreationQualityType.Negative, -5));
        CharacterCreationQualitiesBinding binding = Binding(authority);
        CharacterCreationQualitiesPreview preview = CharacterCreationQualitiesRules.Evaluate(new(
            binding,
            authority,
            ["positive", "negative"]));

        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        Assert.AreEqual(
            CharacterCreationQualitiesRules.ComputeCommandDigest(preview),
            CharacterCreationQualitiesRules.ComputeCommandDigest(
                binding,
                ["negative", "positive"],
                preview.PreviewDigest));

        CharacterCreationQualitiesPreview forged = CharacterCreationQualitiesRules.Evaluate(new(
            binding with
            {
                SavedRevision = binding.SavedRevision - 1,
                AuxiliaryStateDigest = "not-a-digest"
            },
            authority,
            []));
        Assert.IsFalse(forged.CanConfirm);
        CollectionAssert.Contains(
            forged.Blockers.ToList(),
            CharacterCreationQualitiesBlockers.RevisionConflict);
    }

    [TestMethod]
    public void Receipt_validation_binds_cas_plan_draft_and_ledger_chain()
    {
        CharacterCreationQualitiesAuthority authority = Authority(
            Option("positive", CharacterCreationQualityType.Positive, 5));
        CharacterCreationQualitiesPreview preview = CharacterCreationQualitiesRules.Evaluate(new(
            Binding(authority), authority, ["positive"]));
        Assert.IsTrue(CharacterCreationQualitiesRules.TryPlan(
            preview,
            preview.PreviewDigest,
            "quality-draft-2",
            true,
            false,
            Guid.NewGuid(),
            out CharacterCreationQualitiesDraftPlan plan));
        string draftDigest = Digest('d');
        var receipt = new CharacterCreationQualitiesDraftReceipt(
            CharacterCreationQualitiesSchemas.ReceiptV1,
            plan.TransactionId,
            plan.WorkspaceId,
            plan.ExpectedContentRevision,
            plan.TargetContentRevision,
            plan.ExpectedSavedRevision,
            plan.TargetSavedRevision,
            plan.AuthorityDigest,
            plan.RuntimeDigest,
            plan.PreviewDigest,
            plan.IdempotencyKeyDigest,
            plan.CommandDigest,
            plan.PlanDigest,
            draftDigest,
            CharacterCreationQualitiesRules.ReceiptLedgerRootDigest,
            CharacterDocumentChanged: false,
            ReceiptDigest: string.Empty);
        receipt = receipt with
        {
            ReceiptDigest = CharacterCreationQualitiesRules.ComputeReceiptDigest(receipt)
        };

        Assert.IsTrue(CharacterCreationQualitiesRules.IsValidReceipt(receipt, plan, draftDigest));
        Assert.IsFalse(CharacterCreationQualitiesRules.IsValidReceipt(
            receipt with { ContentRevision = receipt.ContentRevision + 1 },
            plan,
            draftDigest));
        Assert.IsFalse(CharacterCreationQualitiesRules.IsValidReceipt(receipt, plan, Digest('e')));
    }

    private static CharacterCreationQualityCatalogOption Option(
        string id,
        CharacterCreationQualityType type,
        int cost,
        bool metagenic = false)
    {
        Guid sourceId = Guid.NewGuid();
        string category = type == CharacterCreationQualityType.Positive ? "Positive" : "Negative";
        string sourceNodeXml = $"<quality><id>{sourceId:D}</id><name>{id}</name><karma>{cost}</karma><category>{category}</category><bonus /><source>SR5</source><page>1</page></quality>";
        var option = new CharacterCreationQualityCatalogOption(
            id,
            sourceId,
            id,
            id,
            type,
            Rating: 1,
            KarmaCost: cost,
            MaximumSelections: 1,
            IsMetagenic: metagenic,
            CountsAgainstQualityLimit: true,
            CountsAgainstKarma: true,
            IsFreeOrGranted: false,
            IsSelectable: true,
            EligibilityIsExact: true,
            DisableReasonKey: null,
            FollowUpChoiceId: null,
            FollowUpChoiceLabel: null,
            SourceAnchorIds: [$"qualities.xml#quality:{id}"],
            SourceNodeXml: sourceNodeXml,
            SourceNodeDigest: CharacterCreationQualitiesRules.ComputeSourceNodeDigest(sourceNodeXml),
            OptionDigest: string.Empty);
        return option with
        {
            OptionDigest = CharacterCreationQualitiesRules.ComputeOptionDigest(option)
        };
    }

    private static CharacterCreationQualitiesAuthority Authority(
        params CharacterCreationQualityCatalogOption[] options)
    {
        var authority = new CharacterCreationQualitiesAuthority(
            CharacterCreationQualitiesSchemas.AuthorityV1,
            "sr5",
            "settings-profile",
            QualityKarmaLimit: 25,
            MayExceedPositiveQualityLimit: false,
            MayExceedNegativeQualityLimit: false,
            MetagenicLimit: 5,
            Options: options,
            GrantedQualities: [],
            SourceAnchorIds: ["qualities.xml", "settings.xml#setting:settings-profile"],
            Blockers: [],
            IsAuthoritative: true,
            SourceDigest: Digest('1'),
            ProfileDigest: Digest('2'),
            GmPolicyDigest: Digest('3'),
            RuntimeDigest: Digest('4'),
            AuthorityDigest: string.Empty);
        return authority with
        {
            AuthorityDigest = CharacterCreationQualitiesRules.ComputeAuthorityDigest(authority)
        };
    }

    private static CharacterCreationQualitiesBinding Binding(
        CharacterCreationQualitiesAuthority authority,
        int creationKarma = 25) => new(
        new CharacterWorkspaceId("quality-workspace"),
        ContentRevision: 7,
        SavedRevision: 7,
        RawCharacterXmlDigest: Digest('5'),
        // Workspace auxiliary digests are raw lowercase SHA-256, unlike
        // Resources/Qualities-owned digests. Keep this fixture contract-exact.
        AuxiliaryStateDigest: new string('6', 64),
        PrerequisiteDraftRevision: 2,
        PrerequisiteDraftDigest: Digest('7'),
        AttributesDraftRevision: 3,
        AttributesDraftDigest: Digest('8'),
        RulesetId: "sr5",
        BuildMethod: CharacterCreationBuildMethods.Priority,
        CharacterCreated: false,
        CreationKarmaTotal: creationKarma,
        CreationKarmaUsedBeforeQualities: 0,
        AuthorityDigest: authority.AuthorityDigest,
        RuntimeDigest: authority.RuntimeDigest);

    private static string Digest(char value) => "sha256:" + new string(value, 64);
}
