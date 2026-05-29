# SR4 Provider Interfaces

## C# interface sketch

```csharp
public interface ISr4RuleProvider
{
    string RulesetId { get; }
    RuleProviderReceipt GetCoverage();
}

public interface ISr4DiceProvider : ISr4RuleProvider
{
    HitCountResult CountHits(IReadOnlyList<int> dice, DiceRollOptions options);
    GlitchResult DetectGlitch(IReadOnlyList<int> dice, DiceRollOptions options);
    DiceRollResult Roll(int dicePool, DiceRollOptions options);
}

public interface ISr4TestProvider : ISr4RuleProvider
{
    SuccessTestResult ResolveSuccess(TestPool pool, int threshold);
    OpposedTestResult ResolveOpposed(TestPool acting, TestPool opposing, Sr4TiePolicy tiePolicy);
    ExtendedTestResult ResolveExtended(TestPool pool, int threshold, TimeSpan interval);
    TeamworkTestResult ResolveTeamwork(TeamworkRequest request);
    DefaultingResult BuildDefaultingPool(AttributeValue linkedAttribute, SkillDefaultingPolicy policy);
}

public interface ISr4EdgeProvider : ISr4RuleProvider
{
    EdgeSpendResult SpendEdge(EdgeSpendRequest request);
    EdgeBurnResult BurnEdge(EdgeBurnRequest request);
    InitiativeEdgeResult ApplyInitiativeEdge(InitiativeEdgeRequest request);
}

public interface ISr4CharacterCreationProvider : ISr4RuleProvider
{
    BuildPointLedger CostDraft(CharacterDraft draft);
    ValidationResult ValidateDraft(CharacterDraft draft);
    CharacterSheet Finalize(CharacterDraft draft);
}

public interface ISr4DerivedStatsProvider : ISr4RuleProvider
{
    int PhysicalDamageTrack(CharacterStats stats);
    int StunDamageTrack(CharacterStats stats);
    int InitiativeAttribute(CharacterStats stats);
    InitiativeProfile Initiative(CharacterStats stats);
    EssenceResult Essence(CharacterAugmentations augmentations);
}

public interface ISr4CombatProvider : ISr4RuleProvider
{
    InitiativeOrder BuildInitiativeOrder(IEnumerable<Combatant> combatants);
    CombatAttackResult ResolveAttack(CombatAttackRequest request);
    DamageResistanceResult ResistDamage(DamageResistanceRequest request);
}

public interface ISr4MagicProvider : ISr4RuleProvider
{
    SpellcastingResult CastSpell(SpellcastingRequest request);
    DrainResult ResistDrain(DrainRequest request);
    SummoningResult SummonSpirit(SummoningRequest request);
    BanishingResult BanishSpirit(BanishingRequest request);
}

public interface ISr4MatrixProvider : ISr4RuleProvider
{
    MatrixActionResult ResolveMatrixAction(MatrixActionRequest request);
    MatrixAttackResult ResolveCybercombat(MatrixAttackRequest request);
    MatrixPerceptionResult Perceive(MatrixPerceptionRequest request);
}

public interface ISr4RiggingProvider : ISr4RuleProvider
{
    DroneCommandResult CommandDrone(DroneCommandRequest request);
    JumpInResult JumpIntoDrone(JumpInRequest request);
    VehicleTestResult ResolveVehicleTest(VehicleTestRequest request);
}

public interface ISr4ExplainReceiptProvider
{
    ExplainReceipt BuildReceipt(RuleComputation computation);
}
```

## Required behavior

All providers must:
- cite RuleFact IDs internally;
- emit public-safe explain receipts;
- reject missing RuleFacts instead of guessing;
- distinguish SR4 from SR5/SR6;
- have deterministic tests and golden fixtures.
