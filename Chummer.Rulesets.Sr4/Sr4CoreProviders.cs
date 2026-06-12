namespace Chummer.Rulesets.Sr4;

public sealed class Sr4DiceProvider
{
    public Sr4DiceRollResult Evaluate(IReadOnlyList<int> dice)
    {
        ArgumentNullException.ThrowIfNull(dice);

        int hits = 0;
        int ones = 0;
        foreach (int die in dice)
        {
            if (die is < 1 or > 6)
            {
                throw new ArgumentOutOfRangeException(nameof(dice), "SR4 dice values must be between 1 and 6.");
            }

            if (die >= 5)
            {
                hits++;
            }

            if (die == 1)
            {
                ones++;
            }
        }

        bool glitch = ones >= Math.Ceiling(dice.Count / 2.0);
        return new Sr4DiceRollResult(
            DicePool: dice.Count,
            Hits: hits,
            Ones: ones,
            Glitch: glitch,
            CriticalGlitch: glitch && hits == 0);
    }

    public bool ExplodingSixesEnabled(Sr4EdgeMode edgeMode)
        => edgeMode is Sr4EdgeMode.PreRoll or Sr4EdgeMode.PostRollExtraDice;
}

public sealed class Sr4TestProvider
{
    public int BuyHits(int dicePool)
    {
        if (dicePool < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dicePool), "SR4 dice pool cannot be negative.");
        }

        return dicePool / 4;
    }

    public bool SuccessTestSucceeds(int hits, int threshold = 1)
    {
        if (hits < 0 || threshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hits), "SR4 hit counts and thresholds cannot be negative.");
        }

        return hits >= threshold;
    }

    public Sr4OpposedTestResult EvaluateOpposed(int actingHits, int opposingHits, Sr4TiePolicy tiePolicy = Sr4TiePolicy.General)
    {
        if (actingHits < 0 || opposingHits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actingHits), "SR4 opposed hits cannot be negative.");
        }

        int netHits = actingHits - opposingHits;
        return new Sr4OpposedTestResult(
            ActingHits: actingHits,
            OpposingHits: opposingHits,
            NetHits: netHits,
            ActingSideWins: netHits > 0,
            DefenderWinsTie: netHits == 0 && tiePolicy == Sr4TiePolicy.CombatDefenderWins);
    }

    public int RetryPenalty(int unchangedRetryCount, bool combatExempt = false)
    {
        if (unchangedRetryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unchangedRetryCount), "SR4 retry count cannot be negative.");
        }

        return combatExempt ? 0 : unchangedRetryCount * -2;
    }

    public int TeamworkBonusDice(int helperHits, int primarySkillRating)
    {
        if (helperHits < 0 || primarySkillRating < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(helperHits), "SR4 teamwork inputs cannot be negative.");
        }

        return Math.Min(helperHits, primarySkillRating);
    }
}

public sealed class Sr4EdgeProvider
{
    public Sr4LongShotResult BuildLongShotPool(int currentDicePool, int edgeAttribute)
    {
        if (edgeAttribute < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(edgeAttribute), "SR4 Edge cannot be negative.");
        }

        return new Sr4LongShotResult(
            Allowed: currentDicePool <= 0 && edgeAttribute > 0,
            DicePool: currentDicePool <= 0 ? edgeAttribute : currentDicePool,
            RuleOfSix: false);
    }
}

public sealed class Sr4ActionEconomyProvider
{
    public const int CombatTurnSeconds = 3;
    public const int DefaultInitiativePasses = 1;
    public const int BasicRulesMaximumInitiativePasses = 4;

    public Sr4ActionAllotment GetInitiativePassActions(int initiativePasses)
    {
        if (initiativePasses < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(initiativePasses), "SR4 initiative passes must be positive.");
        }

        return new Sr4ActionAllotment(
            FreeActions: 1,
            SimpleActions: 2,
            ComplexActions: 1,
            InitiativePasses: Math.Min(initiativePasses, BasicRulesMaximumInitiativePasses),
            CombatTurnSeconds: CombatTurnSeconds);
    }
}

public sealed class Sr4CharacterCreationProvider
{
    internal static readonly IReadOnlyDictionary<string, Sr4MetatypeProfile> Metatypes =
        new Dictionary<string, Sr4MetatypeProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["Human"] = new("Human", 0, 1, 6, 9),
            ["Ork"] = new("Ork", 20, 4, 9, 13),
            ["Dwarf"] = new("Dwarf", 25, 2, 7, 10),
            ["Elf"] = new("Elf", 30, 1, 6, 9),
            ["Troll"] = new("Troll", 40, 5, 10, 15)
        };

    public const int StandardBuildPoints = 400;
    public const int AttributePointCost = 10;
    public const int FinalNaturalMaximumPointCost = 25;

    public Sr4MetatypeProfile GetMetatypeProfile(string metatype)
    {
        if (!Metatypes.TryGetValue(metatype, out Sr4MetatypeProfile? profile))
        {
            throw new ArgumentOutOfRangeException(nameof(metatype), "Unknown SR4 metatype.");
        }

        return profile;
    }

    public int AttributeCost(int currentValue, int targetValue, int naturalMaximum)
    {
        if (currentValue < 0 || targetValue < currentValue || naturalMaximum < currentValue)
        {
            throw new ArgumentOutOfRangeException(nameof(targetValue), "Invalid SR4 attribute cost inputs.");
        }

        int cost = 0;
        for (int value = currentValue + 1; value <= targetValue; value++)
        {
            cost += value == naturalMaximum ? FinalNaturalMaximumPointCost : AttributePointCost;
        }

        return cost;
    }
}

public sealed class Sr4MetatypeProvider
{
    public Sr4MetatypeProfile GetProfile(string metatype)
    {
        if (!Sr4CharacterCreationProvider.Metatypes.TryGetValue(metatype, out Sr4MetatypeProfile? profile))
        {
            throw new ArgumentOutOfRangeException(nameof(metatype), "Unknown SR4 metatype.");
        }

        return profile;
    }
}

public sealed class Sr4AttributeProvider
{
    public const int AttributePointCost = Sr4CharacterCreationProvider.AttributePointCost;
    public const int FinalNaturalMaximumPointCost = Sr4CharacterCreationProvider.FinalNaturalMaximumPointCost;

    public int AttributeCost(int currentValue, int targetValue, int naturalMaximum)
        => new Sr4CharacterCreationProvider().AttributeCost(currentValue, targetValue, naturalMaximum);

    public bool IsWithinNaturalRange(int value, int minimum, int naturalMaximum)
    {
        if (minimum < 0 || naturalMaximum < minimum)
        {
            throw new ArgumentOutOfRangeException(nameof(naturalMaximum), "Invalid SR4 natural attribute range.");
        }

        return value >= minimum && value <= naturalMaximum;
    }
}

public sealed class Sr4SkillProvider
{
    public const int NaturalMaximum = 6;
    public const int NaturalMaximumWithAptitude = 7;
    public const int ModifiedMaximumNormal = 9;
    public const int ModifiedMaximumWithAptitude = 10;
    public const int SpecializationBonusDice = 2;

    public bool IsValidNaturalRating(int rating, bool aptitude = false)
    {
        if (rating < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rating), "SR4 skill rating cannot be negative.");
        }

        return rating <= (aptitude ? NaturalMaximumWithAptitude : NaturalMaximum);
    }

    public int DefaultingPool(int linkedAttribute)
    {
        if (linkedAttribute < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(linkedAttribute), "SR4 linked attribute cannot be negative.");
        }

        return Math.Max(0, linkedAttribute - 1);
    }

    public int KnowledgeSkillStartingPoints(int logic, int intuition)
    {
        if (logic < 0 || intuition < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(logic), "SR4 knowledge skill attributes cannot be negative.");
        }

        return (logic + intuition) * 3;
    }
}

public sealed class Sr4QualityProvider
{
    public const int PositiveQualityBuildPointLimit = 35;
    public const int NegativeQualityBuildPointLimit = 35;

    public bool IsWithinBuildPointLimit(int positiveQualityBuildPoints, int negativeQualityBuildPoints)
    {
        if (positiveQualityBuildPoints < 0 || negativeQualityBuildPoints < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(positiveQualityBuildPoints), "SR4 quality build point totals cannot be negative.");
        }

        return positiveQualityBuildPoints <= PositiveQualityBuildPointLimit
            && negativeQualityBuildPoints <= NegativeQualityBuildPointLimit;
    }
}

public sealed class Sr4DerivedStatsProvider
{
    public const decimal StartingEssence = 6m;

    public int PhysicalDamageTrack(int body) => 8 + CeilingHalf(body, nameof(body));

    public int StunDamageTrack(int willpower) => 8 + CeilingHalf(willpower, nameof(willpower));

    public int InitiativeAttribute(int reaction, int intuition)
    {
        if (reaction < 0 || intuition < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reaction), "SR4 initiative attributes cannot be negative.");
        }

        return reaction + intuition;
    }

    public int InitiativeScore(int reaction, int intuition, int initiativeTestHits)
    {
        if (initiativeTestHits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initiativeTestHits), "SR4 initiative hits cannot be negative.");
        }

        return InitiativeAttribute(reaction, intuition) + initiativeTestHits;
    }

    public int WoundModifier(int physicalBoxes, int stunBoxes)
    {
        if (physicalBoxes < 0 || stunBoxes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(physicalBoxes), "SR4 damage boxes cannot be negative.");
        }

        return -Math.Max(physicalBoxes / 3, stunBoxes / 3);
    }

    public Sr4EssenceImpact EssenceImpact(decimal essence)
    {
        if (essence < 0 || essence > StartingEssence)
        {
            throw new ArgumentOutOfRangeException(nameof(essence), "SR4 Essence must be between 0 and 6.");
        }

        int lostWholeOrFraction = (int)Math.Ceiling(StartingEssence - essence);
        return new Sr4EssenceImpact(essence, lostWholeOrFraction, lostWholeOrFraction);
    }

    private static int CeilingHalf(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "SR4 attributes cannot be negative.");
        }

        return (value + 1) / 2;
    }
}

public sealed class Sr4CombatProvider
{
    public Sr4CombatAttackResult ResolveRangedAttack(int attackerHits, int defenderHits, int baseDamageValue, int armorRating)
    {
        if (attackerHits < 0 || defenderHits < 0 || baseDamageValue < 0 || armorRating < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attackerHits), "SR4 combat inputs cannot be negative.");
        }

        int netHits = attackerHits - defenderHits;
        bool hits = netHits > 0;
        int modifiedDamage = hits ? baseDamageValue + netHits : 0;
        bool armorExceedsDamage = armorRating >= modifiedDamage;
        return new Sr4CombatAttackResult(hits, netHits, modifiedDamage, armorExceedsDamage ? "Stun" : "Physical");
    }
}

public sealed class Sr4DamageProvider
{
    public int ResistDamage(int modifiedDamageValue, int resistanceHits)
    {
        if (modifiedDamageValue < 0 || resistanceHits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(modifiedDamageValue), "SR4 damage inputs cannot be negative.");
        }

        return Math.Max(0, modifiedDamageValue - resistanceHits);
    }
}

public sealed class Sr4MatrixProvider
{
    public int MatrixInitiative(int response, int intuition) => CheckedSum(response, intuition, nameof(response));

    public int MatrixConditionMonitor(int system) => 8 + ((CheckedNonNegative(system, nameof(system)) + 1) / 2);

    public int MatrixResponsePenalty(int activeProgramCount, int systemRating)
    {
        CheckedNonNegative(activeProgramCount, nameof(activeProgramCount));
        CheckedNonNegative(systemRating, nameof(systemRating));
        return activeProgramCount > systemRating ? -1 : 0;
    }

    private static int CheckedSum(int left, int right, string parameterName)
    {
        CheckedNonNegative(left, parameterName);
        CheckedNonNegative(right, parameterName);
        return left + right;
    }

    private static int CheckedNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "SR4 Matrix inputs cannot be negative.");
        }

        return value;
    }
}

public sealed class Sr4MagicProvider
{
    public int DrainResistancePool(int willpower, int traditionDrainAttribute)
    {
        if (willpower < 0 || traditionDrainAttribute < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(willpower), "SR4 drain attributes cannot be negative.");
        }

        return willpower + traditionDrainAttribute;
    }

    public int DrainDamage(int drainValue, int resistanceHits)
    {
        if (drainValue < 0 || resistanceHits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(drainValue), "SR4 drain inputs cannot be negative.");
        }

        return Math.Max(0, drainValue - resistanceHits);
    }

    public int SummoningDrainValue(int spiritHits)
    {
        if (spiritHits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spiritHits), "SR4 spirit hits cannot be negative.");
        }

        return spiritHits * 2;
    }
}

public sealed class Sr4RiggingProvider
{
    public bool DroneHasMatrixNode() => true;

    public int JumpedInControlPool(int vehicleSkill, int response)
    {
        if (vehicleSkill < 0 || response < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vehicleSkill), "SR4 rigging inputs cannot be negative.");
        }

        return vehicleSkill + response;
    }
}

public sealed class Sr4VehicleProvider
{
    public int VehicleConditionMonitor(int body)
    {
        if (body < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(body), "SR4 vehicle Body cannot be negative.");
        }

        return 8 + ((body + 1) / 2);
    }

    public bool VehicleCanHostNode() => true;
}

public sealed class Sr4GearProvider
{
    public bool TableImportDeferred => true;
}

public sealed class Sr4AdvancementProvider
{
    public int AttributeKarmaCost(int targetRating)
    {
        if (targetRating < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(targetRating), "SR4 target rating must be positive.");
        }

        return targetRating * 3;
    }

    public int SkillKarmaCost(int targetRating)
    {
        if (targetRating < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(targetRating), "SR4 target rating must be positive.");
        }

        return targetRating * 2;
    }
}

public sealed class Sr4ExplainReceiptProvider
{
    public Sr4ExplainReceipt Build(string ruleFactId, string provider, string sourceRef)
    {
        if (string.IsNullOrWhiteSpace(ruleFactId) || string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(sourceRef))
        {
            throw new ArgumentException("SR4 explain receipts require a RuleFact id, provider, and source reference.");
        }

        return new Sr4ExplainReceipt(ruleFactId, provider, sourceRef, PublicSafe: true);
    }
}

public enum Sr4EdgeMode
{
    None,
    PreRoll,
    PostRollExtraDice
}

public enum Sr4TiePolicy
{
    General,
    CombatDefenderWins
}

public sealed record Sr4DiceRollResult(int DicePool, int Hits, int Ones, bool Glitch, bool CriticalGlitch);

public sealed record Sr4OpposedTestResult(int ActingHits, int OpposingHits, int NetHits, bool ActingSideWins, bool DefenderWinsTie);

public sealed record Sr4LongShotResult(bool Allowed, int DicePool, bool RuleOfSix);

public sealed record Sr4ActionAllotment(int FreeActions, int SimpleActions, int ComplexActions, int InitiativePasses, int CombatTurnSeconds);

public sealed record Sr4MetatypeProfile(string Metatype, int BuildPointCost, int BodyMinimum, int BodyNaturalMaximum, int BodyAugmentedMaximum);

public sealed record Sr4EssenceImpact(decimal Essence, int MagicOrResonanceLoss, int MaximumReduction);

public sealed record Sr4CombatAttackResult(bool AttackConnects, int NetHits, int ModifiedDamageValue, string DamageType);

public sealed record Sr4ExplainReceipt(string RuleFactId, string Provider, string SourceRef, bool PublicSafe);
