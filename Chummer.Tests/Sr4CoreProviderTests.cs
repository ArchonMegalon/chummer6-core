#nullable enable annotations

using Chummer.Rulesets.Sr4;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class Sr4DiceProviderTests
{
    [TestMethod]
    public void Dice_provider_counts_hits_and_sr4_glitches()
    {
        Sr4DiceProvider provider = new();

        Sr4DiceRollResult ordinary = provider.Evaluate([1, 2, 5, 6]);
        Sr4DiceRollResult glitch = provider.Evaluate([1, 1, 5, 6]);
        Sr4DiceRollResult critical = provider.Evaluate([1, 1, 2, 3]);

        Assert.AreEqual(2, ordinary.Hits);
        Assert.IsFalse(ordinary.Glitch);
        Assert.IsTrue(glitch.Glitch);
        Assert.IsFalse(glitch.CriticalGlitch);
        Assert.IsTrue(critical.CriticalGlitch);
        Assert.IsFalse(provider.ExplodingSixesEnabled(Sr4EdgeMode.None));
        Assert.IsTrue(provider.ExplodingSixesEnabled(Sr4EdgeMode.PreRoll));
    }
}

[TestClass]
public sealed class Sr4TestAndEdgeProviderTests
{
    [TestMethod]
    public void Test_and_edge_providers_project_sr4_seed_rules()
    {
        Sr4TestProvider tests = new();
        Sr4EdgeProvider edge = new();

        Assert.AreEqual(3, tests.BuyHits(15));
        Assert.IsTrue(tests.SuccessTestSucceeds(hits: 1));
        Assert.IsFalse(tests.SuccessTestSucceeds(hits: 1, threshold: 2));

        Sr4OpposedTestResult tie = tests.EvaluateOpposed(actingHits: 3, opposingHits: 3, Sr4TiePolicy.CombatDefenderWins);
        Assert.IsFalse(tie.ActingSideWins);
        Assert.IsTrue(tie.DefenderWinsTie);
        Assert.AreEqual(-4, tests.RetryPenalty(2));
        Assert.AreEqual(0, tests.RetryPenalty(2, combatExempt: true));
        Assert.AreEqual(4, tests.TeamworkBonusDice(helperHits: 6, primarySkillRating: 4));

        Sr4LongShotResult longShot = edge.BuildLongShotPool(currentDicePool: 0, edgeAttribute: 3);
        Assert.IsTrue(longShot.Allowed);
        Assert.AreEqual(3, longShot.DicePool);
        Assert.IsFalse(longShot.RuleOfSix);
    }
}

[TestClass]
public sealed class Sr4CharacterAndDerivedProviderTests
{
    [TestMethod]
    public void Character_creation_and_derived_stats_match_seed_formulas()
    {
        Sr4CharacterCreationProvider creation = new();
        Sr4MetatypeProvider metatypes = new();
        Sr4AttributeProvider attributes = new();
        Sr4SkillProvider skills = new();
        Sr4QualityProvider qualities = new();
        Sr4DerivedStatsProvider derived = new();

        Sr4MetatypeProfile troll = creation.GetMetatypeProfile("Troll");
        Sr4MetatypeProfile ork = metatypes.GetProfile("Ork");
        Assert.AreEqual(400, Sr4CharacterCreationProvider.StandardBuildPoints);
        Assert.AreEqual(40, troll.BuildPointCost);
        Assert.AreEqual(5, troll.BodyMinimum);
        Assert.AreEqual(15, troll.BodyAugmentedMaximum);
        Assert.AreEqual(20, ork.BuildPointCost);
        Assert.AreEqual(35, creation.AttributeCost(currentValue: 4, targetValue: 6, naturalMaximum: 6));
        Assert.AreEqual(35, attributes.AttributeCost(currentValue: 4, targetValue: 6, naturalMaximum: 6));
        Assert.IsTrue(attributes.IsWithinNaturalRange(value: 5, minimum: 1, naturalMaximum: 6));
        Assert.IsFalse(attributes.IsWithinNaturalRange(value: 7, minimum: 1, naturalMaximum: 6));
        Assert.IsTrue(skills.IsValidNaturalRating(6));
        Assert.IsFalse(skills.IsValidNaturalRating(7));
        Assert.IsTrue(skills.IsValidNaturalRating(7, aptitude: true));
        Assert.AreEqual(3, skills.DefaultingPool(linkedAttribute: 4));
        Assert.AreEqual(21, skills.KnowledgeSkillStartingPoints(logic: 3, intuition: 4));
        Assert.IsTrue(qualities.IsWithinBuildPointLimit(positiveQualityBuildPoints: 35, negativeQualityBuildPoints: 35));
        Assert.IsFalse(qualities.IsWithinBuildPointLimit(positiveQualityBuildPoints: 36, negativeQualityBuildPoints: 0));

        Assert.AreEqual(11, derived.PhysicalDamageTrack(body: 5));
        Assert.AreEqual(10, derived.StunDamageTrack(willpower: 4));
        Assert.AreEqual(7, derived.InitiativeAttribute(reaction: 3, intuition: 4));
        Assert.AreEqual(10, derived.InitiativeScore(reaction: 3, intuition: 4, initiativeTestHits: 3));
        Assert.AreEqual(-2, derived.WoundModifier(physicalBoxes: 6, stunBoxes: 3));
        Sr4EssenceImpact essence = derived.EssenceImpact(essence: 4.25m);
        Assert.AreEqual(2, essence.MagicOrResonanceLoss);
        Assert.AreEqual(2, essence.MaximumReduction);
    }
}

[TestClass]
public sealed class Sr4CombatMagicMatrixRiggingProviderTests
{
    [TestMethod]
    public void Combat_magic_matrix_and_rigging_providers_cover_seeded_formulas()
    {
        Sr4ActionEconomyProvider actions = new();
        Sr4CombatProvider combat = new();
        Sr4DamageProvider damage = new();
        Sr4MagicProvider magic = new();
        Sr4MatrixProvider matrix = new();
        Sr4RiggingProvider rigging = new();
        Sr4VehicleProvider vehicles = new();
        Sr4GearProvider gear = new();
        Sr4AdvancementProvider advancement = new();
        Sr4ExplainReceiptProvider receipts = new();

        Sr4ActionAllotment allotment = actions.GetInitiativePassActions(initiativePasses: 5);
        Assert.AreEqual(1, allotment.FreeActions);
        Assert.AreEqual(2, allotment.SimpleActions);
        Assert.AreEqual(1, allotment.ComplexActions);
        Assert.AreEqual(4, allotment.InitiativePasses);
        Assert.AreEqual(3, allotment.CombatTurnSeconds);

        Sr4CombatAttackResult attack = combat.ResolveRangedAttack(attackerHits: 5, defenderHits: 3, baseDamageValue: 4, armorRating: 5);
        Assert.IsTrue(attack.AttackConnects);
        Assert.AreEqual(2, attack.NetHits);
        Assert.AreEqual(6, attack.ModifiedDamageValue);
        Assert.AreEqual("Physical", attack.DamageType);
        Assert.AreEqual(4, damage.ResistDamage(modifiedDamageValue: 6, resistanceHits: 2));

        Assert.AreEqual(8, magic.DrainResistancePool(willpower: 3, traditionDrainAttribute: 5));
        Assert.AreEqual(2, magic.DrainDamage(drainValue: 5, resistanceHits: 3));
        Assert.AreEqual(6, magic.SummoningDrainValue(spiritHits: 3));

        Assert.AreEqual(7, matrix.MatrixInitiative(response: 3, intuition: 4));
        Assert.AreEqual(11, matrix.MatrixConditionMonitor(system: 5));
        Assert.AreEqual(-1, matrix.MatrixResponsePenalty(activeProgramCount: 6, systemRating: 5));

        Assert.IsTrue(rigging.DroneHasMatrixNode());
        Assert.AreEqual(7, rigging.JumpedInControlPool(vehicleSkill: 4, response: 3));
        Assert.AreEqual(12, vehicles.VehicleConditionMonitor(body: 7));
        Assert.IsTrue(vehicles.VehicleCanHostNode());
        Assert.IsTrue(gear.TableImportDeferred);
        Assert.AreEqual(15, advancement.AttributeKarmaCost(targetRating: 5));
        Assert.AreEqual(10, advancement.SkillKarmaCost(targetRating: 5));
        Assert.IsTrue(receipts.Build("sr4.dice.hit_faces", "Sr4DiceProvider", "sr4a_core_2009:p60-62").PublicSafe);
    }
}
