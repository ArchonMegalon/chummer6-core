namespace Chummer.Contracts.Characters;

/// <summary>Output of Core comparing original identity metadata with its actual
/// evaluated candidate graph. This DTO is neither execution nor write authority.
/// It does not generate identity, validate fictional facts, or evaluate SIN gear.</summary>
public sealed record CharacterCreationIdentityEvaluation(
    string Schema, string InputsSnapshotDigest, string RequestDigest, string RawCharacterXmlDigest,
    string CompilationDigest, string OriginalIdentityDigest, string EvaluatedIdentityDigest,
    IReadOnlyList<string> Blockers)
{
    public const string SchemaV1 = "chummer.sr5.creation.identity-preservation.v1";
    public const string Changed = "creation-completion.identity.changed";
    public const string InvalidGraph = "creation-completion.identity.invalid-graph";

    public bool IsPreserved => Blockers.Count == 0 && OriginalIdentityDigest == EvaluatedIdentityDigest;
}
