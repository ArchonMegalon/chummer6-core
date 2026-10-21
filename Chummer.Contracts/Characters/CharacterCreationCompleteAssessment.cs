using System.Text.Json.Serialization;

namespace Chummer.Contracts.Characters;

// Append-only, bounded diagnostic vocabulary. None of these rows is live save authority.
public enum CharacterCreationCompletePhase { Authority, Coverage }
public enum CharacterCreationCompleteEvaluability { Supported, CandidateDependent, Unsupported, AuthorityUnavailable }
public enum CharacterCreationCompleteCoverage { Pending, Evaluated, ProvenInactive, Unresolved }
public enum CharacterCreationCompleteObligationKind
{
    PolicyDomain, Source, Sourcebook, SkillLimit, Setting, ConsumerOperation,
    ActiveGraph, SharedBudgets, OriginalSelections, SelectedGrants, SelectedPrerequisites
}
public enum CharacterCreationCompleteReason
{
    CandidateEvaluationRequired, CanonicalEvaluation, SourceSemanticsUnproven,
    ConsumerCoverageUnproven, ConsumerUnavailable, AuthorityUnavailable,
    AbsentSourceDefaultUnproven, OpaqueSource, InvalidSource, DuplicateSource,
    EvaluatedUseUnavailable, MechanicalEvaluationFailed, UntypedInactivity,
    CampaignAuthorityUnavailable
}

public sealed record CharacterCreationCompleteObligation(
    CharacterCreationCompleteObligationKind Kind, string Id,
    CharacterCreationCompleteEvaluability Evaluability,
    CharacterCreationCompleteCoverage Coverage, CharacterCreationCompleteReason Reason,
    string AuthorityDigest)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationPolicyDomain? Domain { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationConsumerPolicyOperation? Operation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationPolicySourceKind? SourceKind { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SourceOrder { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationSettingObservation? Setting { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationSettingConsumer? Consumer { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationSettingConsumptionReason? ConsumptionReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EvaluationDigest { get; init; }
}

/// <summary>
/// Opt-in assessment of an admitted snapshot. Identity hashes bind evidence; they
/// never prove inactivity or establish current owner/source/campaign authority.
/// Coverage is recomputed after canonical mechanics on both full and selected routes.
/// </summary>
public sealed record CharacterCreationCompleteAssessment(
    string Schema, CharacterCreationCompletePhase Phase,
    string InputsSnapshotDigest, string RawCharacterXmlDigest, string SelectedInputsDigest,
    string PolicyDigest, string RequestDigest, string SelectionsDigest, string SettingsObservationDigest,
    bool? MechanicalSucceeded, string? ConsumptionDigest,
    IReadOnlyList<CharacterCreationCompleteObligation> Obligations)
{
    public const string SchemaV1 = "chummer.creation-complete-assessment.v1";
    // Explicit opt-in; V1 never acquires newly executed consumer coverage.
    public const string SchemaV2 = "chummer.creation-complete-assessment.v2";
    // Fresh opt-in: reconstruct configured/default skill caps from retained settings.
    // V1/V2 assessment bytes and their unresolved default semantics stay unchanged.
    public const string SchemaV3 = "chummer.creation-complete-assessment.v3";
    // Fresh opt-in for executed racial/talent grants, including an empty compiled graph.
    // Historical assessments and selected-prerequisite evidence retain their bytes.
    public const string SchemaV4 = "chummer.creation-complete-assessment.v4";
    public const string Invalid = "creation-complete-assessment-invalid";
    public const string FixedAuthorityUnavailable = "creation-complete-fixed-authority-unavailable";
    public const int MaximumObligations = 8192;

    // Computed locally, excluded from serialized claims and never sufficient for Apply.
    // A local diagnostic can execute supported mechanics even when no Complete
    // candidate is possible. Hosted Prepare must use this stricter predicate.
    [JsonIgnore]
    public bool MayRequestCompleteCandidate => Schema is SchemaV1 or SchemaV2 or SchemaV3 or SchemaV4
        && Obligations is { Count: > 0 and <= MaximumObligations }
        && Obligations.All(row => row.Evaluability is
            CharacterCreationCompleteEvaluability.Supported or CharacterCreationCompleteEvaluability.CandidateDependent);

    [JsonIgnore]
    public bool SatisfiesComplete => MayRequestCompleteCandidate && Phase == CharacterCreationCompletePhase.Coverage
        && MechanicalSucceeded == true && ConsumptionDigest is not null
        && Obligations is { Count: > 0 and <= MaximumObligations }
        && Obligations.All(row => row.Coverage is CharacterCreationCompleteCoverage.Evaluated
            or CharacterCreationCompleteCoverage.ProvenInactive);
}
