namespace Chummer.Contracts.Session;

/// <summary>Exact saved canonical/source observation under the original owner's lease.</summary>
public sealed record SessionConditionTransferBinding(SessionPlayAuthority Authority, long ContentRevision,
    long SavedRevision, string IncarnationId, string SlotGeneration, string AuxiliaryDigest, string OwnerAuthorityDigest)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlaySettlementIdentity5712? PlayCheckpoint5712 { get; init; }
}

/// <summary>Explicit review of current owned condition, never a healing command.</summary>
public sealed record SessionConditionTransferReview(string OperationId, string OwnerId, string CharacterId,
    string OverlayId, string OriginDigest, string Cursor, SessionPlayConditionProjection5412 Condition,
    SessionConditionTransferBinding Canonical, string Digest)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SessionAmmunitionSettlement5712? Ammunition { get; init; }
}

public sealed record SessionConditionTransferIntent(SessionConditionTransferReview Review, DateTimeOffset PreparedAtUtc);

/// <summary>Current canonical facts absent from character XML. Damage remains cumulative Physical in XML.</summary>
public sealed record SessionCanonicalCondition(string OperationId, long RecordedRevision, SessionPlayConditionState5412 State);

/// <summary>Atomic with canonical damage. PostimageDigest excludes only this append-only receipt list to avoid self-reference.</summary>
public sealed record SessionConditionTransferCanonicalReceipt(SessionConditionTransferIntent Intent,
    long RecordedRevision, string PostimageDigest, string PreviousReceiptDigest, string Digest)
{
    public string EnvironmentDigest { get; init; } = string.Empty;
}

/// <summary>Durable journal acknowledgement of an exact canonical commit, retaining the original replay origin.</summary>
public sealed record SessionConditionTransferReceipt(SessionConditionTransferCanonicalReceipt Canonical,
    SessionPlayAuthority ContinuedAuthority, string Digest);

public sealed record SessionConditionTransferStored(long Ordinal, SessionConditionTransferIntent Intent,
    SessionConditionTransferReceipt? Receipt);
