# SR6 Provider Interfaces

## C# interface sketch

```csharp
public interface ISr6RuleProvider
{
    string RulesetId { get; }
    RuleProviderReceipt GetCoverage();
}

public interface ISr6DiceProvider : ISr6RuleProvider
{
    DiceRollResult Roll(int dicePool, DiceRollOptions options);
    HitCountResult CountHits(IReadOnlyList<int> dice, DiceRollOptions options);
    GlitchResult DetectGlitch(IReadOnlyList<int> dice, DiceRollOptions options);
}

public interface ISr6TestProvider : ISr6RuleProvider
{
    SimpleTestResult ResolveSimple(TestPool pool, int threshold);
    OpposedTestResult ResolveOpposed(TestPool acting, TestPool opposing, TiePolicy tiePolicy);
    ExtendedTestResult ResolveExtended(TestPool pool, int threshold, TimeSpan interval);
    TeamworkTestResult ResolveTeamwork(TeamworkRequest request);
}

public interface ISr6DerivedStatsProvider : ISr6RuleProvider
{
    int PhysicalMonitor(CharacterStats stats);
    int StunMonitor(CharacterStats stats);
    InitiativeProfile Initiative(CharacterStats stats);
    int DefenseRating(CharacterStats stats, GearLoadout gear);
    AttackRatingProfile AttackRating(CharacterStats stats, AttackContext context);
}

public interface ISr6EdgeProvider : ISr6RuleProvider
{
    EdgeAwardResult AwardCombatEdge(AttackDefenseRatingComparison comparison, EdgeState state);
    EdgeSpendResult SpendEdge(EdgeSpendRequest request);
}

public interface ISr6CharacterCreationProvider : ISr6RuleProvider
{
    PriorityBuildResult ApplyPriorities(PrioritySelection selection);
    ValidationResult ValidateCreation(CharacterDraft draft);
    CharacterSheet FinalizeCharacter(CharacterDraft draft);
}

public interface ISr6CombatProvider : ISr6RuleProvider
{
    CombatAttackResult ResolveAttack(CombatAttackRequest request);
    DamageSoakResult SoakDamage(DamageSoakRequest request);
    InitiativeOrder BuildInitiativeOrder(IEnumerable<Combatant> combatants);
}

public interface ISr6MatrixProvider : ISr6RuleProvider
{
    MatrixActionResult ResolveMatrixAction(MatrixActionRequest request);
    OverwatchResult ApplyOverwatch(OverwatchRequest request);
    NoiseResult CalculateNoise(MatrixNoiseContext context);
}

public interface ISr6MagicProvider : ISr6RuleProvider
{
    SpellcastingResult CastSpell(SpellcastingRequest request);
    DrainResult ResistDrain(DrainRequest request);
    SummoningResult SummonSpirit(SummoningRequest request);
    BanishingResult BanishSpirit(BanishingRequest request);
}

public interface ISr6ExplainReceiptProvider
{
    ExplainReceipt BuildReceipt(RuleComputation computation);
}
```

## Required provider behavior

All providers must:
- cite RuleFact IDs internally;
- emit explain receipts;
- avoid sourcebook prose;
- have deterministic tests;
- reject missing RuleFacts instead of guessing.
