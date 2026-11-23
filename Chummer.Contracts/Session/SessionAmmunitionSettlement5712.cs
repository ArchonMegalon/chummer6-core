namespace Chummer.Contracts.Session;

/// <summary>Core-issued absolute custody review. Stock totals include every loaded round.</summary>
public sealed record SessionAmmunitionSettlement5712(SessionPlayAmmunition Before,
    SessionPlayAmmunition After, string HistoryDigest, IReadOnlyList<SessionAmmunitionOperation5712> Operations)
{
    public int EventCount { get; init; }
    public long PlayOrdinal { get; init; }
}

public sealed record SessionAmmunitionOperation5712(string OperationId, string PayloadDigest,
    IReadOnlyList<string> EventIds);

/// <summary>
/// Complete historical Play custody at the atomic canonical checkpoint. The last settlement
/// is intentionally unacknowledged here: its canonical receipt is in the same workspace record.
/// Import preserves evidence, never grants permission to replay an imported operation locally.
/// </summary>
public sealed record SessionPlaySettlementContinuation5712(SessionPlayOrigin Origin,
    IReadOnlyList<SessionPlayStoredOperation> Operations,
    IReadOnlyList<SessionConditionTransferStored> Transfers,
    IReadOnlyList<SessionNaturalRecoveryStored> Recoveries,
    SessionConditionTransferIntent Settlement, string Digest)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SessionFocusContinuationStored7490>? Focuses7490 { get; init; }
    // Flat prior journal segments survive a return/import followed by a new local Play origin.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SessionPlaySettlementContinuation5712>? ImportedHistory { get; init; }
}

public sealed record SessionPlaySettlementIdentity5712(string OriginDigest, string OperationsDigest,
    string TransfersDigest, string RecoveriesDigest, string SettlementDigest)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? FocusesDigest7490 { get; init; }
}
