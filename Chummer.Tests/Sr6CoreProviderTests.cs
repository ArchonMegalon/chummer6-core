#nullable enable annotations

using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class Sr6DiceProviderTests
{
    [TestMethod]
    public void Dice_provider_counts_hits_glitches_and_critical_glitches()
    {
        Sr6DiceProvider provider = new();

        Sr6DiceRollResult ordinary = provider.Evaluate([1, 2, 5, 6]);
        Sr6DiceRollResult glitch = provider.Evaluate([1, 1, 1, 5]);
        Sr6DiceRollResult critical = provider.Evaluate([1, 1, 1, 2]);

        Assert.AreEqual(2, ordinary.Hits);
        Assert.IsFalse(ordinary.Glitch);
        Assert.IsTrue(glitch.Glitch);
        Assert.IsFalse(glitch.CriticalGlitch);
        Assert.IsTrue(critical.CriticalGlitch);
    }

    [TestMethod]
    public void Wild_die_applies_seeded_hit_and_cancel_rules()
    {
        Sr6DiceProvider provider = new();

        Assert.AreEqual(5, provider.EvaluateWildDie(6, regularHits: 2));
        Assert.AreEqual(1, provider.EvaluateWildDie(1, regularHits: 2));
        Assert.AreEqual(2, provider.EvaluateWildDie(3, regularHits: 2));
    }
}

[TestClass]
public sealed class Sr6TestProviderTests
{
    [TestMethod]
    public void Test_provider_handles_buy_hits_simple_opposed_and_retry_penalties()
    {
        Sr6TestProvider provider = new();

        Assert.AreEqual(3, provider.BuyHits(15));
        Assert.IsTrue(provider.SimpleTestSucceeds(hits: 3, threshold: 3));
        Assert.IsFalse(provider.SimpleTestSucceeds(hits: 2, threshold: 3));

        Sr6OpposedTestResult opposed = provider.EvaluateOpposed(actingHits: 5, opposingHits: 3);
        Assert.AreEqual(2, opposed.NetHits);
        Assert.IsTrue(opposed.ActingSideWins);
        Assert.IsFalse(opposed.IsTie);

        Assert.AreEqual(-4, provider.RetryPenalty(2));
        Assert.AreEqual(0, provider.RetryPenalty(2, combatExempt: true));
    }
}

[TestClass]
public sealed class Sr6EdgeProviderTests
{
    [TestMethod]
    public void Attack_defense_delta_awards_one_edge_until_round_cap()
    {
        Sr6EdgeProvider provider = new();

        Sr6EdgeAwardResult attacker = provider.AwardAttackDefenseRatingEdge(
            attackRating: 10,
            defenseRating: 6,
            bonusEdgeAlreadyGainedThisRound: 0);
        Sr6EdgeAwardResult defender = provider.AwardAttackDefenseRatingEdge(
            attackRating: 4,
            defenseRating: 9,
            bonusEdgeAlreadyGainedThisRound: 0);
        Sr6EdgeAwardResult capped = provider.AwardAttackDefenseRatingEdge(
            attackRating: 10,
            defenseRating: 4,
            bonusEdgeAlreadyGainedThisRound: 2);

        Assert.AreEqual("attacker", attacker.AwardedSide);
        Assert.AreEqual(1, attacker.AwardedEdge);
        Assert.AreEqual("defender", defender.AwardedSide);
        Assert.AreEqual(1, defender.AwardedEdge);
        Assert.AreEqual(0, capped.AwardedEdge);
        Assert.IsTrue(capped.RoundCapReached);
        Assert.AreEqual(7, provider.ClampEdgePool(10));
    }
}

[TestClass]
public sealed class Sr6ActionEconomyProviderTests
{
    [TestMethod]
    public void Action_economy_uses_seeded_major_minor_and_conversion_rules()
    {
        Sr6ActionEconomyProvider provider = new();

        Sr6ActionAllotment allotment = provider.GetTurnActions(initiativeDice: 3);
        Sr6ActionConversion conversion = provider.ConvertMinorToMajor(availableMinorActions: 5);

        Assert.AreEqual(1, allotment.MajorActions);
        Assert.AreEqual(4, allotment.MinorActions);
        Assert.AreEqual(3, allotment.CombatRoundSeconds);
        Assert.IsTrue(conversion.CanConvert);
        Assert.AreEqual(1, conversion.RemainingMinorActions);
        Assert.AreEqual(1, conversion.GainedMajorActions);
    }
}

[TestClass]
public sealed class Sr6CharacterCreationProviderTests
{
    [TestMethod]
    public void Character_creation_provider_projects_priority_rows_and_karma_conversion()
    {
        Sr6CharacterCreationProvider provider = new();

        Sr6PriorityRow priorityA = provider.GetPriorityRow('A');
        Sr6PriorityRow priorityE = provider.GetPriorityRow('e');
        Sr6StartingResourceConversion conversion = provider.ConvertKarmaToNuyen(karmaSpent: 3);

        Assert.AreEqual(24, priorityA.AttributePoints);
        Assert.AreEqual(32, priorityA.SkillPoints);
        Assert.AreEqual(450000, priorityA.ResourcesNuyen);
        Assert.AreEqual(1, priorityE.MetatypeAdjustmentPoints);
        Assert.AreEqual(6000, conversion.Nuyen);
    }
}

[TestClass]
public sealed class Sr6MetatypeProviderTests
{
    [TestMethod]
    public void Metatype_provider_validates_seeded_attribute_ranges()
    {
        Sr6MetatypeProvider provider = new();

        Sr6AttributeRange trollBody = provider.GetAttributeRange("Troll", "Body");
        Sr6AttributeRange elfCharisma = provider.GetAttributeRange("Elf", "Charisma");

        Assert.AreEqual(9, trollBody.Maximum);
        Assert.AreEqual(8, elfCharisma.Maximum);
        Assert.IsTrue(provider.IsWithinRange("Human", "Edge", 7));
        Assert.IsFalse(provider.IsWithinRange("Human", "Edge", 8));
    }
}

[TestClass]
public sealed class Sr6SkillAndQualityProviderTests
{
    [TestMethod]
    public void Skill_and_quality_providers_enforce_creation_limits()
    {
        Sr6SkillProvider skills = new();
        Sr6QualityProvider qualities = new();

        Assert.IsTrue(skills.IsValidSkillRating(6));
        Assert.IsFalse(skills.IsValidSkillRating(7));
        Assert.IsTrue(skills.IsValidSkillRating(7, aptitude: true));
        Assert.IsTrue(skills.IsValidSkillRating(10, aptitude: true, gameplay: true));
        Assert.IsTrue(qualities.IsCreationQualitySelectionValid(selectedQualityCount: 6, netBonusKarma: 20));
        Assert.IsFalse(qualities.IsCreationQualitySelectionValid(selectedQualityCount: 7, netBonusKarma: 20));
    }
}

[TestClass]
public sealed class Sr6DerivedStatsProviderTests
{
    [TestMethod]
    public void Derived_stats_provider_calculates_monitors_initiative_and_ratings()
    {
        Sr6DerivedStatsProvider provider = new();

        Assert.AreEqual(11, provider.PhysicalConditionMonitor(body: 5));
        Assert.AreEqual(10, provider.StunConditionMonitor(willpower: 4));
        Assert.AreEqual(7, provider.InitiativeRank(reaction: 3, intuition: 4));
        Assert.AreEqual(15, provider.InitiativeScore(reaction: 3, intuition: 4, initiativeDice: [5, 3]));
        Assert.AreEqual(14, provider.DefenseRating(body: 4, armorRating: 8, effects: 2));
        Assert.AreEqual(9, provider.UnarmedAttackRatingClose(strength: 5, reaction: 4));
    }
}

[TestClass]
public sealed class Sr6StatusProviderTests
{
    [TestMethod]
    public void Status_provider_projects_seeded_modifiers_and_level_rules()
    {
        Sr6StatusProvider provider = new();

        Sr6StatusEffect dazed = provider.GetStatus("dazed");
        Sr6StatusEffect immobilized = provider.GetStatus("immobilized");

        Assert.AreEqual(-4, dazed.InitiativeModifier);
        Assert.IsTrue(dazed.PreventsEdgeGain);
        Assert.AreEqual(-3, immobilized.AttackRatingModifier);
        Assert.IsTrue(immobilized.PreventsReactionDefense);
        Assert.AreEqual(3, provider.CoverDefenseRatingBonus(3));
        Assert.AreEqual(-6, provider.FatiguedDicePoolModifier(3));
    }
}

[TestClass]
public sealed class Sr6CombatProviderTests
{
    [TestMethod]
    public void Combat_provider_resolves_attack_damage_soak_and_firing_modes()
    {
        Sr6CombatProvider provider = new();

        Sr6CombatAttackResult attack = provider.ResolveWeaponAttack(attackHits: 4, defenseHits: 4, baseDamage: 3);
        Sr6FiringModeAdjustment narrowBurst = provider.GetFiringMode("BF_narrow");

        Assert.IsTrue(attack.AttackConnects);
        Assert.AreEqual(0, attack.NetHits);
        Assert.AreEqual(3, attack.ModifiedDamage);
        Assert.AreEqual(1, provider.SoakDamage(modifiedDamage: 3, bodyHits: 2));
        Assert.AreEqual(4, narrowBurst.Rounds);
        Assert.AreEqual(-4, narrowBurst.AttackRatingDelta);
        Assert.AreEqual(2, narrowBurst.DamageValueDelta);
    }
}

[TestClass]
public sealed class Sr6MatrixProviderTests
{
    [TestMethod]
    public void Matrix_provider_calculates_ratings_noise_and_overwatch()
    {
        Sr6MatrixProvider provider = new();

        Assert.AreEqual(7, provider.MatrixAttackRating(attack: 3, sleaze: 4));
        Assert.AreEqual(8, provider.MatrixDefenseRating(dataProcessing: 3, firewall: 5));
        Assert.AreEqual(-3, provider.NoisePenalty(3));
        Assert.IsTrue(provider.DeviceBlockedByNoise(noise: 4, deviceRating: 3));
        Assert.AreEqual(39, provider.AddIllegalActionOverwatchScore(currentScore: 35, defenderHits: 4));
        Assert.IsTrue(provider.TriggersConvergence(40));
    }
}

[TestClass]
public sealed class Sr6MagicProviderTests
{
    [TestMethod]
    public void Magic_provider_resolves_drain_spell_damage_and_sustaining_penalty()
    {
        Sr6MagicProvider provider = new();

        Sr6DrainResult stunDrain = provider.ResistDrain(drainValue: 5, hits: 2, magic: 4);
        Sr6DrainResult physicalDrain = provider.ResistDrain(drainValue: 8, hits: 1, magic: 4);

        Assert.AreEqual(3, stunDrain.Damage);
        Assert.AreEqual("Stun", stunDrain.DamageType);
        Assert.AreEqual(7, physicalDrain.Damage);
        Assert.AreEqual("Physical", physicalDrain.DamageType);
        Assert.AreEqual(4, provider.DirectCombatSpellDamage(netHits: 3, ampUpDamage: 1));
        Assert.AreEqual(6, provider.IndirectCombatSpellDamage(magic: 5, netHits: 2, ampUpDamage: 1));
        Assert.AreEqual(-4, provider.SustainedSpellPenalty(2));
    }
}

[TestClass]
public sealed class Sr6RiggingProviderTests
{
    [TestMethod]
    public void Rigging_provider_calculates_rcc_vehicle_and_drone_values()
    {
        Sr6RiggingProvider provider = new();

        Assert.AreEqual(12, provider.SlavedDroneCapacity(rccRating: 4));
        Assert.AreEqual(11, provider.VehicleConditionMonitor(body: 5));
        Assert.AreEqual(6, provider.AutonomousDroneInitiativeRank(pilot: 3));
        Assert.AreEqual(2, provider.WeaponMountCapacity(unaugmentedBody: 7));
        Assert.AreEqual(8, provider.AutopilotAttackPool(sensor: 4, targetingAutosoft: 4));
        Assert.AreEqual(3, provider.AutopilotAttackPool(sensor: 4));
    }
}

[TestClass]
public sealed class Sr6GearAdvancementAndExplainProviderTests
{
    [TestMethod]
    public void Gear_advancement_and_explain_providers_keep_seeded_boundaries()
    {
        Sr6GearProvider gear = new();
        Sr6AdvancementProvider advancement = new();
        Sr6ExplainReceiptProvider explain = new();

        Sr6ExplainReceipt receipt = explain.Create(
            provider: "Sr6EdgeProvider",
            rulefactId: "sr6.edge.gain.attack_defense_rating_delta",
            summary: "Award Edge from a rating delta.");

        Assert.IsTrue(gear.IsIllegalAvailabilityAllowedAtCreation(availabilityRating: 6, illegal: true));
        Assert.IsFalse(gear.IsIllegalAvailabilityAllowedAtCreation(availabilityRating: 7, illegal: true));
        Assert.AreEqual(20, advancement.AttributeCost(newRank: 4));
        Assert.AreEqual(5, advancement.SpecializationCost());
        Assert.AreEqual(13, advancement.InitiationCost(newInitiateGrade: 3));
        Assert.AreEqual("Sr6EdgeProvider", receipt.Provider);
        Assert.IsTrue(receipt.PublicSafe);
    }

    [TestMethod]
    public void Table_import_provider_accepts_pdf_line_hash_receipt()
    {
        const string receiptPath = "/docker/chummercomplete/_completion/sr6_rule_authority/SR6_TABLE_IMPORTS.generated.json";
        Sr6TableImportProvider provider = new();

        Sr6TableImportReceipt receipt = provider.LoadReceipt(receiptPath);

        Assert.IsTrue(provider.IsCompliantIndexedImport(receipt));
        Assert.AreEqual("sr6", receipt.Ruleset);
        Assert.IsTrue(receipt.SourcebookCount >= 1);
        Assert.IsTrue(receipt.NonemptyLineCount > 0);
        Assert.IsTrue(receipt.CandidateTableLineCount > 0);
    }
}
