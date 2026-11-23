using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Session;

// Exact canonical candidate, published under journal -> workspace lock order.
// An intent alone never authorizes continuation.
public sealed record SessionFocusContinuationIntent7490(string OriginDigest, string Cursor,
    int EventCount, long PlayOrdinal, SessionConditionTransferBinding Before,
    SessionPlayAuthority After, string AfterAuxiliaryDigest, string AfterSourceEnvironment, string RuleEnvironmentDigest,
    CharacterCareerFocusBondingReceipt ExpectedReceipt, string Digest);
public sealed record SessionFocusContinuationStored7490(long Ordinal,
    SessionFocusContinuationIntent7490 Intent, SessionFocusContinuationIntent7490? Retirement,
    SessionFocusContinuationReceipt7490? Receipt);
public sealed record SessionFocusContinuationReceipt7490(CharacterCareerFocusBondingReceipt Canonical,
    SessionPlayAuthority ContinuedAuthority, string SourceEnvironment, string Digest);
public sealed record SessionFocusCastingCheckpoint7490(int EventCount, long Revision,
    SessionPlayAuthority Authority);
