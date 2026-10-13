namespace Chummer.Contracts.Characters;

/// <summary>Exact removed nodes retained for correction, never display-only reconstruction.</summary>
public sealed record CharacterCareerQualityRemovedNode(string Container, int Index, string Xml);

/// <summary>
/// Workspace-owned history. Coherent hashes do not authorize writes, imported
/// replay or a GM decision. Only the dedicated, freshly admitted store may append.
/// </summary>
public sealed record CharacterCareerQualityTransactionEntry(
    CharacterCareerQualityQuote Quote,
    CharacterCareerQualityPlan Plan,
    CharacterCareerQualityReceipt Receipt,
    CharacterCareerQualitySource Source,
    int? ResolvedArmCount,
    IReadOnlyList<CharacterCareerQualityRemovedNode> RemovedNodes,
    string CharacterDigestBefore,
    string CharacterDigestAfter,
    string AuxiliaryDigestBefore,
    string PreviousEntryDigest,
    string EntryDigest);

public enum CharacterCareerQualityCommitOutcome
{
    NotFound, Applied, Replayed, Missing, Conflict, IdempotencyConflict, Corrupt, Unavailable
}

public sealed record CharacterCareerQualityCommitResult(
    CharacterCareerQualityCommitOutcome Outcome,
    CharacterCareerQualityTransactionEntry? Entry = null,
    long? CurrentRevision = null,
    string? Error = null);
