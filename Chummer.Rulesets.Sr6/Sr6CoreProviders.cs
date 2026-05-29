namespace Chummer.Rulesets.Sr6;

public sealed class Sr6DiceProvider
{
    public Sr6DiceRollResult Evaluate(IReadOnlyList<int> dice)
    {
        ArgumentNullException.ThrowIfNull(dice);

        int hits = 0;
        int ones = 0;
        foreach (int die in dice)
        {
            if (die is < 1 or > 6)
            {
                throw new ArgumentOutOfRangeException(nameof(dice), "SR6 dice values must be between 1 and 6.");
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

        bool glitch = ones > dice.Count / 2.0;
        return new Sr6DiceRollResult(
            DicePool: dice.Count,
            Hits: hits,
            Ones: ones,
            Glitch: glitch,
            CriticalGlitch: glitch && hits == 0);
    }

    public int EvaluateWildDie(int wildDie, int regularHits)
    {
        if (wildDie is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(wildDie), "SR6 wild die value must be between 1 and 6.");
        }

        return wildDie switch
        {
            >= 5 => regularHits + 3,
            1 => Math.Max(0, regularHits - 1),
            _ => regularHits
        };
    }
}

public sealed class Sr6TestProvider
{
    public int BuyHits(int dicePool)
    {
        if (dicePool < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dicePool), "SR6 dice pool cannot be negative.");
        }

        return dicePool / 4;
    }

    public bool SimpleTestSucceeds(int hits, int threshold)
    {
        if (hits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hits), "SR6 hit count cannot be negative.");
        }

        if (threshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "SR6 threshold cannot be negative.");
        }

        return hits >= threshold;
    }

    public Sr6OpposedTestResult EvaluateOpposed(int actingHits, int opposingHits)
    {
        if (actingHits < 0 || opposingHits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actingHits), "SR6 opposed hits cannot be negative.");
        }

        int netHits = actingHits - opposingHits;
        return new Sr6OpposedTestResult(
            ActingHits: actingHits,
            OpposingHits: opposingHits,
            NetHits: netHits,
            ActingSideWins: netHits > 0,
            IsTie: netHits == 0);
    }

    public int RetryPenalty(int unchangedRetryCount, bool combatExempt = false)
    {
        if (unchangedRetryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unchangedRetryCount), "SR6 retry count cannot be negative.");
        }

        return combatExempt ? 0 : unchangedRetryCount * -2;
    }
}

public sealed class Sr6EdgeProvider
{
    public const int MaximumEdgePoints = 7;
    public const int MaximumBonusEdgeGainPerRound = 2;
    public const int AttackDefenseRatingDeltaForEdge = 4;

    public Sr6EdgeAwardResult AwardAttackDefenseRatingEdge(int attackRating, int defenseRating, int bonusEdgeAlreadyGainedThisRound)
    {
        if (bonusEdgeAlreadyGainedThisRound < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bonusEdgeAlreadyGainedThisRound), "SR6 bonus Edge count cannot be negative.");
        }

        int delta = attackRating - defenseRating;
        bool capped = bonusEdgeAlreadyGainedThisRound >= MaximumBonusEdgeGainPerRound;
        string? side = null;
        int awarded = 0;

        if (!capped && Math.Abs(delta) >= AttackDefenseRatingDeltaForEdge)
        {
            side = delta > 0 ? "attacker" : "defender";
            awarded = 1;
        }

        return new Sr6EdgeAwardResult(
            AttackRating: attackRating,
            DefenseRating: defenseRating,
            Delta: delta,
            AwardedSide: side,
            AwardedEdge: awarded,
            RoundCapReached: capped);
    }

    public int ClampEdgePool(int edgePoints)
    {
        if (edgePoints < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(edgePoints), "SR6 Edge points cannot be negative.");
        }

        return Math.Min(edgePoints, MaximumEdgePoints);
    }
}

public sealed class Sr6ActionEconomyProvider
{
    public const int CombatRoundSeconds = 3;
    public const int BaseMajorActions = 1;
    public const int BaseMinorActions = 1;
    public const int MaximumMinorActionsAtTurnStart = 5;

    public Sr6ActionAllotment GetTurnActions(int initiativeDice)
    {
        if (initiativeDice is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(initiativeDice), "SR6 initiative dice must be between 1 and 5.");
        }

        return new Sr6ActionAllotment(
            MajorActions: BaseMajorActions,
            MinorActions: Math.Min(MaximumMinorActionsAtTurnStart, BaseMinorActions + initiativeDice),
            CombatRoundSeconds: CombatRoundSeconds);
    }

    public Sr6ActionConversion ConvertMinorToMajor(int availableMinorActions)
    {
        if (availableMinorActions < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(availableMinorActions), "SR6 minor action count cannot be negative.");
        }

        return new Sr6ActionConversion(
            CanConvert: availableMinorActions >= 4,
            RemainingMinorActions: availableMinorActions >= 4 ? availableMinorActions - 4 : availableMinorActions,
            GainedMajorActions: availableMinorActions >= 4 ? 1 : 0);
    }
}

public sealed class Sr6CharacterCreationProvider
{
    private static readonly IReadOnlyDictionary<char, Sr6PriorityRow> PriorityRows =
        new Dictionary<char, Sr6PriorityRow>
        {
            ['A'] = new('A', 24, 32, 450_000, 13),
            ['B'] = new('B', 16, 24, 275_000, 11),
            ['C'] = new('C', 12, 20, 150_000, 9),
            ['D'] = new('D', 8, 16, 50_000, 4),
            ['E'] = new('E', 2, 10, 8_000, 1)
        };

    public Sr6PriorityRow GetPriorityRow(char priority)
    {
        char normalized = char.ToUpperInvariant(priority);
        if (!PriorityRows.TryGetValue(normalized, out Sr6PriorityRow? row))
        {
            throw new ArgumentOutOfRangeException(nameof(priority), "SR6 priority must be A, B, C, D, or E.");
        }

        return row;
    }

    public Sr6StartingResourceConversion ConvertKarmaToNuyen(int karmaSpent, bool inDebt = false)
    {
        if (karmaSpent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(karmaSpent), "SR6 karma spent cannot be negative.");
        }

        int rate = inDebt ? 5_000 : 2_000;
        return new Sr6StartingResourceConversion(karmaSpent, rate, karmaSpent * rate);
    }
}

public sealed class Sr6MetatypeProvider
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, Sr6AttributeRange>> Ranges =
        new Dictionary<string, IReadOnlyDictionary<string, Sr6AttributeRange>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Human"] = CreateRanges(("Body", 1, 6), ("Agility", 1, 6), ("Reaction", 1, 6), ("Strength", 1, 6), ("Willpower", 1, 6), ("Logic", 1, 6), ("Intuition", 1, 6), ("Charisma", 1, 6), ("Edge", 1, 7)),
            ["Dwarf"] = CreateRanges(("Body", 1, 7), ("Agility", 1, 6), ("Reaction", 1, 5), ("Strength", 1, 8), ("Willpower", 1, 7), ("Logic", 1, 6), ("Intuition", 1, 6), ("Charisma", 1, 6), ("Edge", 1, 6)),
            ["Elf"] = CreateRanges(("Body", 1, 6), ("Agility", 1, 7), ("Reaction", 1, 6), ("Strength", 1, 6), ("Willpower", 1, 6), ("Logic", 1, 6), ("Intuition", 1, 6), ("Charisma", 1, 8), ("Edge", 1, 6)),
            ["Ork"] = CreateRanges(("Body", 1, 8), ("Agility", 1, 6), ("Reaction", 1, 6), ("Strength", 1, 8), ("Willpower", 1, 6), ("Logic", 1, 6), ("Intuition", 1, 6), ("Charisma", 1, 5), ("Edge", 1, 6)),
            ["Troll"] = CreateRanges(("Body", 1, 9), ("Agility", 1, 5), ("Reaction", 1, 6), ("Strength", 1, 9), ("Willpower", 1, 6), ("Logic", 1, 6), ("Intuition", 1, 6), ("Charisma", 1, 5), ("Edge", 1, 6))
        };

    public Sr6AttributeRange GetAttributeRange(string metatype, string attribute)
    {
        if (!Ranges.TryGetValue(metatype, out IReadOnlyDictionary<string, Sr6AttributeRange>? metatypeRanges))
        {
            throw new ArgumentOutOfRangeException(nameof(metatype), "Unknown SR6 metatype.");
        }

        if (!metatypeRanges.TryGetValue(attribute, out Sr6AttributeRange? range))
        {
            throw new ArgumentOutOfRangeException(nameof(attribute), "Unknown SR6 attribute.");
        }

        return range;
    }

    public bool IsWithinRange(string metatype, string attribute, int value)
    {
        Sr6AttributeRange range = GetAttributeRange(metatype, attribute);
        return value >= range.Minimum && value <= range.Maximum;
    }

    private static IReadOnlyDictionary<string, Sr6AttributeRange> CreateRanges(params (string Attribute, int Minimum, int Maximum)[] ranges)
        => ranges.ToDictionary(
            range => range.Attribute,
            range => new Sr6AttributeRange(range.Attribute, range.Minimum, range.Maximum),
            StringComparer.OrdinalIgnoreCase);
}

public sealed class Sr6SkillProvider
{
    public const int StartingMaximum = 6;
    public const int StartingMaximumWithAptitude = 7;
    public const int GameplayMaximum = 9;
    public const int GameplayMaximumWithAptitude = 10;

    public bool IsValidSkillRating(int rating, bool aptitude = false, bool gameplay = false)
    {
        if (rating < 0)
        {
            return false;
        }

        int maximum = gameplay
            ? aptitude ? GameplayMaximumWithAptitude : GameplayMaximum
            : aptitude ? StartingMaximumWithAptitude : StartingMaximum;
        return rating <= maximum;
    }
}

public sealed class Sr6QualityProvider
{
    public const int MaximumQualityCountAtCreation = 6;
    public const int MaximumNetBonusKarmaFromQualities = 20;

    public bool IsCreationQualitySelectionValid(int selectedQualityCount, int netBonusKarma)
    {
        if (selectedQualityCount < 0 || netBonusKarma < 0)
        {
            return false;
        }

        return selectedQualityCount <= MaximumQualityCountAtCreation
               && netBonusKarma <= MaximumNetBonusKarmaFromQualities;
    }
}

public sealed class Sr6DerivedStatsProvider
{
    public int PhysicalConditionMonitor(int body) => CheckedCeilingHalfPlusEight(body, nameof(body));

    public int StunConditionMonitor(int willpower) => CheckedCeilingHalfPlusEight(willpower, nameof(willpower));

    public int InitiativeRank(int reaction, int intuition)
    {
        ValidateNonNegative(reaction, nameof(reaction));
        ValidateNonNegative(intuition, nameof(intuition));
        return reaction + intuition;
    }

    public int InitiativeScore(int reaction, int intuition, IReadOnlyList<int> initiativeDice)
    {
        ArgumentNullException.ThrowIfNull(initiativeDice);
        if (initiativeDice.Count is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(initiativeDice), "SR6 initiative dice count must be between 1 and 5.");
        }

        foreach (int die in initiativeDice)
        {
            if (die is < 1 or > 6)
            {
                throw new ArgumentOutOfRangeException(nameof(initiativeDice), "SR6 initiative dice values must be between 1 and 6.");
            }
        }

        return InitiativeRank(reaction, intuition) + initiativeDice.Sum();
    }

    public int DefenseRating(int body, int armorRating, int effects = 0)
    {
        ValidateNonNegative(body, nameof(body));
        ValidateNonNegative(armorRating, nameof(armorRating));
        return Math.Max(0, body + armorRating + effects);
    }

    public int UnarmedAttackRatingClose(int strength, int reaction)
    {
        ValidateNonNegative(strength, nameof(strength));
        ValidateNonNegative(reaction, nameof(reaction));
        return strength + reaction;
    }

    private static int CheckedCeilingHalfPlusEight(int value, string parameterName)
    {
        ValidateNonNegative(value, parameterName);
        return (int)Math.Ceiling(value / 2.0) + 8;
    }

    private static void ValidateNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "SR6 stat values cannot be negative.");
        }
    }
}

public sealed class Sr6StatusProvider
{
    private static readonly IReadOnlyDictionary<string, Sr6StatusEffect> Statuses =
        new Dictionary<string, Sr6StatusEffect>(StringComparer.OrdinalIgnoreCase)
        {
            ["blinded"] = new("blinded", true, false, true),
            ["burning"] = new("burning", false, true, false),
            ["chilled"] = new("chilled", false, false, false, InitiativeModifier: -4, DicePoolModifier: -1),
            ["confused"] = new("confused", false, false, false, RequiresParameter: true),
            ["cover"] = new("cover", true, false, false),
            ["dazed"] = new("dazed", false, false, false, InitiativeModifier: -4, PreventsEdgeGain: true),
            ["deafened"] = new("deafened", true, false, true),
            ["fatigued"] = new("fatigued", true, false, false),
            ["immobilized"] = new("immobilized", false, false, false, AttackRatingModifier: -3, PreventsReactionDefense: true),
            ["prone"] = new("prone", false, false, false),
            ["wet"] = new("wet", false, true, false, DamageResistanceModifier: -6),
            ["zapped"] = new("zapped", false, false, false, InitiativeModifier: -2, DicePoolModifier: -1)
        };

    public Sr6StatusEffect GetStatus(string id)
    {
        if (!Statuses.TryGetValue(id, out Sr6StatusEffect? status))
        {
            throw new ArgumentOutOfRangeException(nameof(id), "Unknown seeded SR6 status.");
        }

        return status;
    }

    public int CoverDefenseRatingBonus(int coverLevel)
    {
        if (coverLevel is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(coverLevel), "SR6 cover level must be 1 through 4.");
        }

        return coverLevel;
    }

    public int FatiguedDicePoolModifier(int level)
    {
        if (level is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "SR6 fatigued level must be 1 through 3.");
        }

        return level * -2;
    }
}

public sealed class Sr6CombatProvider
{
    public Sr6CombatAttackResult ResolveWeaponAttack(int attackHits, int defenseHits, int baseDamage)
    {
        if (attackHits < 0 || defenseHits < 0 || baseDamage < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attackHits), "SR6 combat hit and damage values cannot be negative.");
        }

        int netHits = Math.Max(0, attackHits - defenseHits);
        bool attackConnects = attackHits >= defenseHits;
        return new Sr6CombatAttackResult(
            AttackHits: attackHits,
            DefenseHits: defenseHits,
            NetHits: netHits,
            AttackConnects: attackConnects,
            ModifiedDamage: attackConnects ? baseDamage + netHits : 0);
    }

    public int SoakDamage(int modifiedDamage, int bodyHits)
    {
        if (modifiedDamage < 0 || bodyHits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(modifiedDamage), "SR6 damage and soak hits cannot be negative.");
        }

        return Math.Max(0, modifiedDamage - bodyHits);
    }

    public Sr6FiringModeAdjustment GetFiringMode(string mode)
    {
        return mode switch
        {
            "SS" => new("SS", 1, 0, 0),
            "SA" => new("SA", 2, -2, 1),
            "BF_narrow" => new("BF_narrow", 4, -4, 2),
            "FA" => new("FA", 10, -6, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), "Unknown seeded SR6 firing mode.")
        };
    }
}

public sealed class Sr6MatrixProvider
{
    public int MatrixAttackRating(int attack, int sleaze) => CheckedSum(attack, sleaze, nameof(attack));

    public int MatrixDefenseRating(int dataProcessing, int firewall) => CheckedSum(dataProcessing, firewall, nameof(dataProcessing));

    public int NoisePenalty(int noise)
    {
        if (noise < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(noise), "SR6 Matrix noise cannot be negative.");
        }

        return -noise;
    }

    public bool DeviceBlockedByNoise(int noise, int deviceRating)
    {
        if (noise < 0 || deviceRating < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(noise), "SR6 noise and device rating cannot be negative.");
        }

        return noise > deviceRating;
    }

    public int AddIllegalActionOverwatchScore(int currentScore, int defenderHits)
    {
        if (currentScore < 0 || defenderHits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentScore), "SR6 Overwatch Score values cannot be negative.");
        }

        return currentScore + defenderHits;
    }

    public bool TriggersConvergence(int overwatchScore) => overwatchScore >= 40;

    private static int CheckedSum(int left, int right, string parameterName)
    {
        if (left < 0 || right < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "SR6 Matrix ratings cannot be negative.");
        }

        return left + right;
    }
}

public sealed class Sr6MagicProvider
{
    public Sr6DrainResult ResistDrain(int drainValue, int hits, int magic)
    {
        if (drainValue < 0 || hits < 0 || magic < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(drainValue), "SR6 drain values cannot be negative.");
        }

        int damage = Math.Max(0, drainValue - hits);
        return new Sr6DrainResult(
            DrainValue: drainValue,
            Hits: hits,
            Damage: damage,
            DamageType: damage > magic ? "Physical" : "Stun",
            NotHealableByMagicOrMedkits: true);
    }

    public int DirectCombatSpellDamage(int netHits, int ampUpDamage = 0)
    {
        if (netHits < 0 || ampUpDamage < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(netHits), "SR6 spell damage values cannot be negative.");
        }

        return netHits + ampUpDamage;
    }

    public int IndirectCombatSpellDamage(int magic, int netHits, int ampUpDamage = 0)
    {
        if (magic < 0 || netHits < 0 || ampUpDamage < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(magic), "SR6 spell damage values cannot be negative.");
        }

        return (int)Math.Ceiling(magic / 2.0) + netHits + ampUpDamage;
    }

    public int SustainedSpellPenalty(int sustainedSpellCount)
    {
        if (sustainedSpellCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sustainedSpellCount), "SR6 sustained spell count cannot be negative.");
        }

        return sustainedSpellCount * -2;
    }
}

public sealed class Sr6RiggingProvider
{
    public int SlavedDroneCapacity(int rccRating)
    {
        if (rccRating < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rccRating), "SR6 RCC rating cannot be negative.");
        }

        return rccRating * 3;
    }

    public int VehicleConditionMonitor(int body) => new Sr6DerivedStatsProvider().PhysicalConditionMonitor(body);

    public int AutonomousDroneInitiativeRank(int pilot)
    {
        if (pilot < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pilot), "SR6 Pilot rating cannot be negative.");
        }

        return pilot * 2;
    }

    public int WeaponMountCapacity(int unaugmentedBody)
    {
        if (unaugmentedBody < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unaugmentedBody), "SR6 drone Body cannot be negative.");
        }

        return unaugmentedBody / 3;
    }

    public int AutopilotAttackPool(int sensor, int? targetingAutosoft = null)
    {
        if (sensor < 0 || targetingAutosoft < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sensor), "SR6 sensor and autosoft ratings cannot be negative.");
        }

        return targetingAutosoft.HasValue ? targetingAutosoft.Value + sensor : Math.Max(0, sensor - 1);
    }
}

public sealed class Sr6GearProvider
{
    public bool IsIllegalAvailabilityAllowedAtCreation(int availabilityRating, bool illegal)
    {
        if (availabilityRating < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(availabilityRating), "SR6 availability rating cannot be negative.");
        }

        return !illegal || availabilityRating < 7;
    }
}

public sealed class Sr6TableImportProvider
{
    public const string RequiredStatus = "private_pdf_line_hash_import_indexed_pending_review";
    public const string RequiredSourceKind = "private_local_sourcebook_pdf_line_hashes";

    public Sr6TableImportReceipt LoadReceipt(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("SR6 table import receipt path is required.", nameof(path));
        }

        using FileStream stream = File.OpenRead(path);
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(stream);
        System.Text.Json.JsonElement root = document.RootElement;
        string status = ReadString(root, "status");
        string ruleset = ReadString(root, "ruleset");
        string sourceKind = ReadString(root, "source_kind");
        int sourcebookCount = ReadInt(root, "sourcebook_count");
        int nonemptyLineCount = ReadInt(root, "nonempty_line_count");
        int candidateTableLineCount = ReadInt(root, "candidate_table_line_count");
        bool copySafe = ReadString(root, "public_copy_policy").Contains("no sourcebook prose", StringComparison.OrdinalIgnoreCase);

        return new Sr6TableImportReceipt(
            Status: status,
            Ruleset: ruleset,
            SourceKind: sourceKind,
            SourcebookCount: sourcebookCount,
            NonemptyLineCount: nonemptyLineCount,
            CandidateTableLineCount: candidateTableLineCount,
            PublicCopySafe: copySafe);
    }

    public bool IsCompliantIndexedImport(Sr6TableImportReceipt receipt)
        => receipt.Status == RequiredStatus
            && receipt.Ruleset.Equals("sr6", StringComparison.OrdinalIgnoreCase)
            && receipt.SourceKind == RequiredSourceKind
            && receipt.SourcebookCount > 0
            && receipt.NonemptyLineCount > 0
            && receipt.CandidateTableLineCount > 0
            && receipt.PublicCopySafe;

    private static string ReadString(System.Text.Json.JsonElement root, string property)
        => root.TryGetProperty(property, out System.Text.Json.JsonElement element)
            ? element.GetString() ?? string.Empty
            : string.Empty;

    private static int ReadInt(System.Text.Json.JsonElement root, string property)
        => root.TryGetProperty(property, out System.Text.Json.JsonElement element) && element.TryGetInt32(out int value)
            ? value
            : 0;
}

public sealed class Sr6AdvancementProvider
{
    public int AttributeCost(int newRank) => RankCost(newRank, multiplier: 5);

    public int ActiveSkillCost(int newRank) => RankCost(newRank, multiplier: 5);

    public int SpecializationCost() => 5;

    public int ExpertiseCost() => 5;

    public int KnowledgeSkillCost() => 3;

    public int PositiveQualityAfterCreationCost(int normalCost)
    {
        if (normalCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(normalCost), "SR6 quality cost cannot be negative.");
        }

        return normalCost * 2;
    }

    public int InitiationCost(int newInitiateGrade) => 10 + CheckedRank(newInitiateGrade);

    public int SubmersionCost(int newSubmersionLevel) => 10 + CheckedRank(newSubmersionLevel);

    private static int RankCost(int newRank, int multiplier) => CheckedRank(newRank) * multiplier;

    private static int CheckedRank(int rank)
    {
        if (rank < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rank), "SR6 rank cannot be negative.");
        }

        return rank;
    }
}

public sealed class Sr6ExplainReceiptProvider
{
    public Sr6ExplainReceipt Create(string provider, string rulefactId, string summary, bool publicSafe = true)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("SR6 explain receipt provider is required.", nameof(provider));
        }

        if (string.IsNullOrWhiteSpace(rulefactId))
        {
            throw new ArgumentException("SR6 explain receipt RuleFact id is required.", nameof(rulefactId));
        }

        return new Sr6ExplainReceipt(provider.Trim(), rulefactId.Trim(), summary.Trim(), publicSafe);
    }
}

public sealed record Sr6DiceRollResult(
    int DicePool,
    int Hits,
    int Ones,
    bool Glitch,
    bool CriticalGlitch);

public sealed record Sr6OpposedTestResult(
    int ActingHits,
    int OpposingHits,
    int NetHits,
    bool ActingSideWins,
    bool IsTie);

public sealed record Sr6EdgeAwardResult(
    int AttackRating,
    int DefenseRating,
    int Delta,
    string? AwardedSide,
    int AwardedEdge,
    bool RoundCapReached);

public sealed record Sr6ActionAllotment(
    int MajorActions,
    int MinorActions,
    int CombatRoundSeconds);

public sealed record Sr6ActionConversion(
    bool CanConvert,
    int RemainingMinorActions,
    int GainedMajorActions);

public sealed record Sr6PriorityRow(
    char Priority,
    int AttributePoints,
    int SkillPoints,
    int ResourcesNuyen,
    int MetatypeAdjustmentPoints);

public sealed record Sr6StartingResourceConversion(
    int KarmaSpent,
    int NuyenPerKarma,
    int Nuyen);

public sealed record Sr6AttributeRange(
    string Attribute,
    int Minimum,
    int Maximum);

public sealed record Sr6StatusEffect(
    string Id,
    bool SupportsLevels,
    bool CancelsWithOtherStatus,
    bool CanAutoFailAtMaximumLevel,
    int InitiativeModifier = 0,
    int DicePoolModifier = 0,
    int AttackRatingModifier = 0,
    int DamageResistanceModifier = 0,
    bool PreventsEdgeGain = false,
    bool PreventsReactionDefense = false,
    bool RequiresParameter = false);

public sealed record Sr6CombatAttackResult(
    int AttackHits,
    int DefenseHits,
    int NetHits,
    bool AttackConnects,
    int ModifiedDamage);

public sealed record Sr6FiringModeAdjustment(
    string Mode,
    int Rounds,
    int AttackRatingDelta,
    int DamageValueDelta);

public sealed record Sr6DrainResult(
    int DrainValue,
    int Hits,
    int Damage,
    string DamageType,
    bool NotHealableByMagicOrMedkits);

public sealed record Sr6ExplainReceipt(
    string Provider,
    string RulefactId,
    string Summary,
    bool PublicSafe);

public sealed record Sr6TableImportReceipt(
    string Status,
    string Ruleset,
    string SourceKind,
    int SourcebookCount,
    int NonemptyLineCount,
    int CandidateTableLineCount,
    bool PublicCopySafe);
