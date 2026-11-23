namespace Chummer.Contracts.Characters;

public static class CharacterCreationRoleObjectiveSchemas
{
    public const string BriefV1 = "chummer.creation-role-brief.v1";
    public const string RoutineV1 = "chummer.creation-role-routine.v1";
    public const string AssessmentV1 = "chummer.creation-role-assessment.v1";
    public const string EvaluatorV1 = "chummer.sr5-role-readiness.v1";
}

public enum CharacterCreationRole
{
    MundaneSurvivor, StreetSamurai, Face, Infiltrator, Mage, MysticAdept,
    Adept, Technomancer, Decker, Rigger
}

// SkillReadiness measures an explicit pool target, not opposed success or a whole role.
// Other modes require their action-specific findings as well as sheet prerequisites.
public enum CharacterCreationRoleRoutineKind
{
    SkillReadiness, Casting, AdeptSkill, TechnomancerForm, DeckerMatrix,
    RiggerManual, RiggerRemote, RiggerJumpedIn, PhysicalAttack
}

public enum CharacterCreationRoleFindingState { AssessedPass, AssessedFailure, Unsupported }
public enum CharacterCreationRoleLimit { Physical, Mental, Social }

/// <summary>Conditions are part of the objective identity, never silent bonuses.
/// V1 evaluates ordinary tests only. Other conditions produce Unsupported.</summary>
public sealed record CharacterCreationRoleConditions(
    bool UseEdge = false, bool ExternalAssistance = false, bool Consumables = false,
    int SustainedEffects = 0, string Preparation = "", string Situation = "ordinary",
    bool SpriteAssistance = false);

/// <summary>Player/policy targets, not game rules. Null means not requested.
/// The risk target is preserved even when its action evaluator is unavailable.</summary>
public sealed record CharacterCreationRoleTargets(
    int MinimumSkillDicePool,
    int? MinimumDefensePool = null,
    CharacterCreationRoleLimit? Limit = null,
    int? MinimumLimit = null,
    decimal? MinimumGroundWalkMeters = null,
    int? MinimumResistancePool = null,
    decimal? MaximumDamageProbability = null);

/// <summary>Retained scenario inputs for action evaluators. V1 does not infer
/// force, target, access or a vehicle from a role name or selected inventory.</summary>
public sealed record CharacterCreationRoleActionScenario(
    int? ForceOrLevel = null, string Target = "", string AccessMode = "",
    string? VehicleSourceId = null)
{
    // Omitted for old briefs so existing durable request/digest identities remain unchanged.
    /// <summary>When omitted on an ordinary physical-single-shot routine with
    /// Consumables declared, the role planner may construct one source-legal kit
    /// within Plan permissions, locks and remaining funds, then bind final owners.
    /// An explicit scenario selects exact existing owners and remains assessment-only.
    /// Multiple simultaneous unbound physical routines remain unsupported.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationPhysicalAttackScenario3819? PhysicalAttack { get; init; }
}

/// <summary>Exact owned instances and current canonical source bytes. This is a
/// detached single-shot readiness request, never permission to fire or buy rounds.</summary>
public sealed record CharacterCreationPhysicalAttackScenario3819(
    Guid WeaponId, Guid WeaponSourceId, string WeaponSourceDigest,
    Guid AmmunitionId, Guid AmmunitionSourceId, string AmmunitionSourceDigest);

public sealed record CharacterCreationRoleRoutineObjective(
    string Schema, string Id, CharacterCreationRole Role, CharacterCreationRoleRoutineKind Kind,
    string SkillSourceId, CharacterCreationRoleConditions Conditions, CharacterCreationRoleTargets Targets,
    IReadOnlyList<string> RequiredEquipmentSourceIds,
    CharacterCreationMagicResonanceOptionIdentity? MagicIdentity = null,
    bool IsFallback = false, CharacterCreationRoleActionScenario? Action = null);

/// <summary>Plan is the existing full permission/lock/consent/budget/source contract.
/// No role or target grants unlocks or spending permission. Both roles use that one wallet.</summary>
public sealed record CharacterCreationRoleBrief(
    string Schema, CharacterCreationRole PrimaryRole, CharacterCreationRole? SecondaryRole,
    string Concept, CharacterCreationCompletionPlanRequest Plan,
    IReadOnlyList<CharacterCreationRoleRoutineObjective> Routines);

public sealed record CharacterCreationRoleCompletionRequest(
    CharacterCreationRoleBrief Brief, int MaximumCandidateEvaluations = 8,
    bool PermitCatalogMagicCompletion = false);

/// <summary>A contribution from an existing Core projection. A residual is explicitly
/// labelled; it is not attributed to an individual power without source evidence.</summary>
public sealed record CharacterCreationRoleContribution(string Label, decimal Value, string Authority);

public sealed record CharacterCreationRoleFinding(
    string Code, CharacterCreationRoleFindingState State, string Detail,
    decimal? Actual, decimal? Target, IReadOnlyList<CharacterCreationRoleContribution> Contributions);

public sealed record CharacterCreationRoleRoutineAssessment(
    string RoutineId, IReadOnlyList<CharacterCreationRoleFinding> Findings);

/// <summary>Detached comparison evidence only; no owner, Apply, save or Career authority.
/// Unsupported dimensions cannot be counted as passed, zero cost, or zero risk.</summary>
public sealed record CharacterCreationRoleAssessment(
    string Schema, string Evaluator, string RequestDigest,
    CharacterCreationFinalizationBinding OriginalBinding,
    string InputsSnapshotDigest, string EffectivePolicyDigest, string RuleSourceCaptureDigest,
    CharacterCreationSourcebookSelectionAuthority? SourcebookSelection,
    string PlanDigest, string ProjectionDigest, string? SheetAuthorityDigest,
    IReadOnlyList<CharacterCreationRoleRoutineAssessment> Routines,
    IReadOnlyList<CharacterCreationRoleFinding> Coverage,
    string AssessmentDigest);

/// <summary>Success means the baseline is legal and complete. Read Findings for usefulness;
/// this type deliberately has no IsEffective, universal score, or CanApply property.</summary>
public sealed record CharacterCreationRoleCompletionCandidate(
    CharacterCreationRoleCompletionRequest Request, CharacterCreationCompletionBaseline Baseline,
    CharacterCreationRoleAssessment Assessment);
