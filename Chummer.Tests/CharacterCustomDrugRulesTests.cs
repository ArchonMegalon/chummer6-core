using Chummer.Contracts.Characters;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCustomDrugRulesTests
{
    private static readonly CharacterCustomDrugComponentId s_Tank = new(Guid.Parse("33ae6b1c-62f6-4824-967d-0e2b37c7d1b9"));
    private static readonly CharacterCustomDrugComponentId s_Crush = new(Guid.Parse("9f4c87ba-4a0d-48e8-8c90-08b689da7203"));
    private static readonly CharacterCustomDrugComponentId s_Smoothtalk = new(Guid.Parse("41018f6e-beb4-4a6c-8d59-501db802ce1a"));
    private static readonly CharacterCustomDrugComponentId s_SpeedEnhancer = new(Guid.Parse("fdca4090-a933-4393-a3bd-7633aad967a2"));
    private static readonly CharacterCustomDrugGradeId s_Pharmaceutical = new(Guid.Parse("b3366009-4884-44d7-9efa-34e213a75e7e"));

    [TestMethod]
    public void Pinned_legacy_profile_quotes_one_foundation_blocks_enhancers_and_aggregate_effects_exactly()
    {
        CharacterCustomDrugPreparation preparation = Preparation(LegacyPolicy());
        CharacterCustomDrugSelection selection = Selection(
            new CharacterCustomDrugComponentSelection(s_Tank, 0),
            new CharacterCustomDrugComponentSelection(s_Crush, 1),
            new CharacterCustomDrugComponentSelection(s_SpeedEnhancer, 0));

        CharacterCustomDrugQuote quote = CharacterCustomDrugRules.Quote(preparation, selection);

        Assert.IsTrue(quote.Exact, quote.BlockReason);
        Assert.AreEqual("Redline", quote.Name);
        Assert.AreEqual("Pharmaceutical", quote.GradeName);
        Assert.AreEqual(145m, quote.ComponentCost);
        Assert.AreEqual(145m, quote.UnitCost, "Pinned Chummer5 stores but does not apply the grade cost multiplier.");
        Assert.AreEqual(290m, quote.ChargedCost);
        Assert.AreEqual(-290m, quote.NuyenDelta);
        Assert.AreEqual(6, quote.Availability);
        Assert.AreEqual(CharacterCustomDrugLegality.Restricted, quote.Legality);
        Assert.AreEqual(7, quote.AddictionRating);
        Assert.AreEqual(3, quote.AddictionThreshold);
        Assert.AreEqual(2m, quote.Effects.Attributes.Single(item => item.Attribute == "BOD").Value);
        Assert.AreEqual(-2m, quote.Effects.Attributes.Single(item => item.Attribute == "CHA").Value);
        Assert.AreEqual(2m, quote.Effects.Attributes.Single(item => item.Attribute == "STR").Value);
        Assert.AreEqual(-1m, quote.Effects.Attributes.Single(item => item.Attribute == "INT").Value);
        Assert.AreEqual(2, quote.Effects.CrashDamage);
        Assert.AreEqual(-3, quote.Effects.Speed);
        Assert.AreEqual(64, quote.QuoteDigest.Length);
    }

    [TestMethod]
    public void Corrected_profile_applies_level_grade_and_grade_threshold_without_changing_recipe_identity()
    {
        CharacterCustomDrugCalculationPolicy corrected = LegacyPolicy() with
        {
            MultiplyComponentCostByLevel = true,
            ApplyGradeCostMultiplier = true,
            ApplyGradeAddictionThresholdModifier = true
        };
        CharacterCustomDrugQuote quote = CharacterCustomDrugRules.Quote(
            Preparation(corrected),
            Selection(
                new CharacterCustomDrugComponentSelection(s_Tank, 0),
                new CharacterCustomDrugComponentSelection(s_Crush, 1),
                new CharacterCustomDrugComponentSelection(s_SpeedEnhancer, 0)));

        Assert.IsTrue(quote.Exact, quote.BlockReason);
        Assert.AreEqual(165m, quote.ComponentCost);
        Assert.AreEqual(330m, quote.UnitCost);
        Assert.AreEqual(660m, quote.ChargedCost);
        Assert.AreEqual(2, quote.AddictionThreshold);
    }

    [TestMethod]
    public void Recipe_requires_exactly_one_foundation_and_enforces_component_limits()
    {
        CharacterCustomDrugPreparation preparation = Preparation(LegacyPolicy());

        AssertBlocked(
            preparation,
            Selection(new CharacterCustomDrugComponentSelection(s_Crush, 0)),
            CharacterCustomDrugBlockers.MissingFoundation);
        AssertBlocked(
            preparation,
            Selection(
                new CharacterCustomDrugComponentSelection(s_Tank, 0),
                new CharacterCustomDrugComponentSelection(s_Tank, 0)),
            CharacterCustomDrugBlockers.DuplicateFoundation);
        AssertBlocked(
            preparation,
            Selection(
                new CharacterCustomDrugComponentSelection(s_Tank, 0),
                new CharacterCustomDrugComponentSelection(s_SpeedEnhancer, 0),
                new CharacterCustomDrugComponentSelection(s_SpeedEnhancer, 0),
                new CharacterCustomDrugComponentSelection(s_SpeedEnhancer, 0),
                new CharacterCustomDrugComponentSelection(s_SpeedEnhancer, 0)),
            CharacterCustomDrugBlockers.ComponentLimit);
    }

    [TestMethod]
    public void Level_three_block_cannot_reverse_the_foundations_negative_attribute()
    {
        AssertBlocked(
            Preparation(LegacyPolicy()),
            Selection(
                new CharacterCustomDrugComponentSelection(s_Tank, 0),
                new CharacterCustomDrugComponentSelection(s_Smoothtalk, 2)),
            CharacterCustomDrugBlockers.FoundationConflict);
    }

    [TestMethod]
    public void Unknown_level_identity_budget_and_arithmetic_inputs_fail_closed()
    {
        CharacterCustomDrugPreparation preparation = Preparation(LegacyPolicy());
        AssertBlocked(
            preparation,
            Selection(new CharacterCustomDrugComponentSelection(s_Tank, 1)),
            CharacterCustomDrugBlockers.ComponentUnavailable);
        AssertBlocked(
            preparation,
            Selection(new CharacterCustomDrugComponentSelection(
                new CharacterCustomDrugComponentId(Guid.NewGuid()), 0)),
            CharacterCustomDrugBlockers.ComponentUnavailable);
        AssertBlocked(
            preparation with { AvailableNuyen = 100m },
            Selection(
                new CharacterCustomDrugComponentSelection(s_Tank, 0),
                new CharacterCustomDrugComponentSelection(s_Crush, 1),
                new CharacterCustomDrugComponentSelection(s_SpeedEnhancer, 0)),
            CharacterCustomDrugBlockers.InsufficientFunds);
        AssertBlocked(
            preparation,
            Selection(new CharacterCustomDrugComponentSelection(s_Tank, 0)) with { Quantity = 0m },
            CharacterCustomDrugBlockers.InvalidQuantity);
        AssertBlocked(
            preparation,
            Selection(new CharacterCustomDrugComponentSelection(s_Tank, 0)) with { MarkupPercent = 0.001m },
            CharacterCustomDrugBlockers.InvalidMarkup);
    }

    [TestMethod]
    public void Quote_digest_is_order_independent_but_binds_rules_character_catalog_and_every_choice()
    {
        CharacterCustomDrugPreparation preparation = Preparation(LegacyPolicy());
        CharacterCustomDrugSelection ordered = Selection(
            new CharacterCustomDrugComponentSelection(s_Tank, 0),
            new CharacterCustomDrugComponentSelection(s_Crush, 1),
            new CharacterCustomDrugComponentSelection(s_SpeedEnhancer, 0));
        CharacterCustomDrugSelection reordered = Selection(
            new CharacterCustomDrugComponentSelection(s_SpeedEnhancer, 0),
            new CharacterCustomDrugComponentSelection(s_Tank, 0),
            new CharacterCustomDrugComponentSelection(s_Crush, 1));
        string digest = CharacterCustomDrugRules.Quote(preparation, ordered).QuoteDigest;

        Assert.AreEqual(digest, CharacterCustomDrugRules.Quote(preparation, reordered).QuoteDigest);
        Assert.AreNotEqual(digest, CharacterCustomDrugRules.Quote(
            preparation with { CharacterDigest = new string('9', 64) }, ordered).QuoteDigest);
        Assert.AreNotEqual(digest, CharacterCustomDrugRules.Quote(
            preparation with { CatalogDigest = new string('8', 64) }, ordered).QuoteDigest);
        Assert.AreNotEqual(digest, CharacterCustomDrugRules.Quote(
            preparation with { RulesDigest = new string('7', 64) }, ordered).QuoteDigest);
        Assert.AreNotEqual(digest, CharacterCustomDrugRules.Quote(
            preparation, ordered with { Quantity = 3m }).QuoteDigest);
        Assert.AreNotEqual(digest, CharacterCustomDrugRules.Quote(
            preparation, ordered with { Stolen = true }).QuoteDigest);
    }

    [TestMethod]
    public void Invalid_authority_never_degrades_to_label_or_first_row_selection()
    {
        CharacterCustomDrugPreparation valid = Preparation(LegacyPolicy());
        CharacterCustomDrugPreparation[] invalid =
        [
            valid with { Exact = false, Blockers = ["source unavailable"] },
            valid with { CatalogDigest = "not-a-digest" },
            valid with { Components = [.. valid.Components, valid.Components[0]] },
            valid with { Grades = [.. valid.Grades, valid.Grades[0]] },
            valid with { Policy = valid.Policy with { MaximumComponents = 0 } }
        ];

        foreach (CharacterCustomDrugPreparation preparation in invalid)
        {
            CharacterCustomDrugQuote quote = CharacterCustomDrugRules.Quote(
                preparation,
                Selection(new CharacterCustomDrugComponentSelection(s_Tank, 0)));
            Assert.IsFalse(quote.Exact);
            Assert.AreEqual(0m, quote.NuyenDelta);
            Assert.AreEqual(string.Empty, quote.QuoteDigest);
        }
    }

    [TestMethod]
    public void Command_and_receipt_digests_bind_all_new_object_identities()
    {
        CharacterCustomDrugSelection selection = Selection(
            new CharacterCustomDrugComponentSelection(s_Tank, 0),
            new CharacterCustomDrugComponentSelection(s_Crush, 1));
        var command = new CharacterCustomDrugCommitCommand(
            ExpectedContentRevision: 44,
            ExpectedCharacterDigest: new string('a', 64),
            ExpectedCatalogDigest: new string('b', 64),
            ExpectedRulesDigest: new string('c', 64),
            ExpectedQuoteDigest: new string('d', 64),
            IdempotencyKey: "recipe:44:nonce",
            selection,
            new CharacterCustomDrugInstanceId(Guid.Parse("11111111-1111-4111-8111-111111111111")),
            [Guid.Parse("22222222-2222-4222-8222-222222222222"), Guid.Parse("33333333-3333-4333-8333-333333333333")],
            Guid.Parse("44444444-4444-4444-8444-444444444444"),
            DateTimeOffset.Parse("2026-08-27T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        string commandDigest = CharacterCustomDrugRules.ComputeCommandDigest(command);
        Assert.AreEqual(64, commandDigest.Length);
        Assert.AreNotEqual(commandDigest, CharacterCustomDrugRules.ComputeCommandDigest(command with
        {
            NewComponentInstanceIds = [command.NewComponentInstanceIds[0], Guid.NewGuid()]
        }));

        var receipt = new CharacterCustomDrugCommitReceipt(
            44,
            45,
            new string('a', 64),
            new string('e', 64),
            new string('b', 64),
            new string('c', 64),
            new string('d', 64),
            commandDigest,
            new string('f', 64),
            command.NewDrugInstanceId,
            command.NewComponentInstanceIds,
            command.NewExpenseId,
            -190m,
            new string('1', 64),
            new string('2', 64),
            ReceiptDigest: string.Empty);
        Assert.AreEqual(64, CharacterCustomDrugRules.ComputeReceiptDigest(receipt).Length);
    }

    private static void AssertBlocked(
        CharacterCustomDrugPreparation preparation,
        CharacterCustomDrugSelection selection,
        string reason)
    {
        CharacterCustomDrugQuote quote = CharacterCustomDrugRules.Quote(preparation, selection);
        Assert.IsFalse(quote.Exact);
        Assert.AreEqual(reason, quote.BlockReason);
        Assert.AreEqual(0m, quote.NuyenDelta);
        Assert.AreEqual(string.Empty, quote.QuoteDigest);
    }

    private static CharacterCustomDrugSelection Selection(
        params CharacterCustomDrugComponentSelection[] components)
        => new(
            " Redline ",
            s_Pharmaceutical,
            Quantity: 2m,
            Stolen: false,
            FreeCost: false,
            MarkupPercent: 0m,
            components);

    private static CharacterCustomDrugCalculationPolicy LegacyPolicy()
        => new(
            MultiplyComponentCostByLevel: false,
            ApplyGradeCostMultiplier: false,
            ApplyGradeAddictionThresholdModifier: false,
            MaximumComponents: 32,
            MaximumQuantity: 1_000m,
            QuantityDecimalPlaces: 2);

    private static CharacterCustomDrugPreparation Preparation(CharacterCustomDrugCalculationPolicy policy)
        => new(
            Exact: true,
            Blockers: [],
            CharacterCustomDrugContext.Career,
            ContentRevision: 44,
            CharacterDigest: new string('a', 64),
            CatalogDigest: new string('b', 64),
            RulesDigest: new string('c', 64),
            SettingsProfileId: "223a11ff-80e0-428b-89a9-6ef1c243b8b6",
            AvailableNuyen: 10_000m,
            policy,
            Grades:
            [
                new CharacterCustomDrugGrade(
                    s_Pharmaceutical,
                    "Pharmaceutical",
                    CostMultiplier: 2m,
                    AddictionThresholdModifier: -1,
                    SourceBook: "CF",
                    SourceNodeDigest: new string('d', 64),
                    SourceAnchorIds: ["drugcomponents.xml#grade:b3366009-4884-44d7-9efa-34e213a75e7e"])
            ],
            Components:
            [
                Component(
                    s_Tank,
                    "Tank",
                    CharacterCustomDrugComponentCategory.Foundation,
                    limit: 1,
                    availability: 4,
                    CharacterCustomDrugLegality.Restricted,
                    cost: 75m,
                    rating: 6,
                    threshold: 2,
                    [
                        Effect(0, attributes:
                        [
                            new("BOD", 2m), new("CHA", -2m), new("WIL", 1m)
                        ], qualities: [new("High Pain Tolerance", 3)])
                    ]),
                Component(
                    s_Crush,
                    "Crush",
                    CharacterCustomDrugComponentCategory.Block,
                    limit: 1,
                    availability: 1,
                    CharacterCustomDrugLegality.Legal,
                    cost: 20m,
                    rating: 0,
                    threshold: 0,
                    [
                        Effect(0, attributes: [new("STR", 1m), new("INT", -1m)]),
                        Effect(1, attributes: [new("STR", 2m), new("INT", -1m)], crashDamage: 2),
                        Effect(2, attributes: [new("STR", 3m), new("INT", -1m)], crashDamage: 2,
                            qualities: [new("Low Pain Tolerance", 0)])
                    ]),
                Component(
                    s_Smoothtalk,
                    "Smoothtalk",
                    CharacterCustomDrugComponentCategory.Block,
                    limit: 1,
                    availability: 1,
                    CharacterCustomDrugLegality.Legal,
                    cost: 20m,
                    rating: 0,
                    threshold: 0,
                    [
                        Effect(0, attributes: [new("CHA", 1m), new("STR", -1m)]),
                        Effect(1, attributes: [new("CHA", 2m), new("STR", -1m)], crashDamage: 2),
                        Effect(2, attributes: [new("CHA", 3m), new("STR", -1m)], crashDamage: 2)
                    ]),
                Component(
                    s_SpeedEnhancer,
                    "Speed Enhancer",
                    CharacterCustomDrugComponentCategory.Enhancer,
                    limit: 3,
                    availability: 1,
                    CharacterCustomDrugLegality.Legal,
                    cost: 50m,
                    rating: 1,
                    threshold: 1,
                    [Effect(0, speed: -3)])
            ]);

    private static CharacterCustomDrugComponentSource Component(
        CharacterCustomDrugComponentId id,
        string name,
        CharacterCustomDrugComponentCategory category,
        int limit,
        int availability,
        CharacterCustomDrugLegality legality,
        decimal cost,
        int rating,
        int threshold,
        IReadOnlyList<CharacterCustomDrugEffectLevel> effects)
        => new(
            id,
            name,
            category,
            limit,
            availability,
            legality,
            cost,
            rating,
            threshold,
            "CF",
            "190",
            new string('e', 64),
            [$"drugcomponents.xml#component:{id.Value:D}"],
            effects);

    private static CharacterCustomDrugEffectLevel Effect(
        int level,
        IReadOnlyList<CharacterCustomDrugAttributeEffect>? attributes = null,
        IReadOnlyList<CharacterCustomDrugLimitEffect>? limits = null,
        IReadOnlyList<CharacterCustomDrugQualityEffect>? qualities = null,
        IReadOnlyList<string>? information = null,
        int initiative = 0,
        int initiativeDice = 0,
        int crashDamage = 0,
        int speed = 0,
        int duration = 0)
        => new(
            level,
            attributes ?? [],
            limits ?? [],
            qualities ?? [],
            information ?? [],
            initiative,
            initiativeDice,
            crashDamage,
            speed,
            duration);
}
