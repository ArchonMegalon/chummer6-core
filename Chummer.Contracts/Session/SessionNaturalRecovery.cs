using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Session;

public sealed record SessionNaturalRecoveryReview(Sr5RecoveryReview Recovery, string OwnerId, string CharacterId,
    string OverlayId, string OriginDigest, string Cursor, long PlayOrdinal, int EventCount,
    SessionPlayConditionProjection5412 Condition, SessionConditionTransferBinding Canonical, string Digest);
public sealed record SessionNaturalRecoveryIntent(SessionNaturalRecoveryReview Review, Sr5RecoveryCommand Command,
    DateTimeOffset PreparedAtUtc);
public sealed record SessionNaturalRecoveryReceipt(SessionNaturalRecoveryIntent Intent, Sr5RecoveryOperation Operation,
    SessionPlayAuthority ContinuedAuthority, SessionPlayConditionState5412 Condition, string Digest);
public sealed record SessionNaturalRecoveryStored(long Ordinal, SessionNaturalRecoveryIntent Intent,
    SessionNaturalRecoveryReceipt? Unresolved, SessionNaturalRecoveryReceipt? Receipt);
// Replay-only checkpoint. It never replaces the origin, events, or previous result snapshots.
public sealed record SessionNaturalRecoveryCheckpoint(int EventCount, long PlayOrdinal, string Digest,
    SessionPlayConditionState5412 Before, SessionPlayConditionState5412 After);
