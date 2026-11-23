using Chummer.Contracts.Owners;
using Chummer.Application.Owners;
using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public interface ISessionPlayAuthorityResolver
{
    SessionFocusCurrent7490 ResolveFocusCurrent7490(OwnerScope owner, string characterId)
        => throw new SessionPlayException(SessionPlayFailures.Unavailable("focus-continuation-current-source-unavailable"));
    SessionFocusCurrent7490 ResolveFocusCurrentWithOwnerLease7490(IOwnerContextLeaseAccessor owners,
        OwnerContextStamp expected, IOwnerContextLease lease, string characterId)
        => ResolveFocusCurrent7490(expected.Owner, characterId);
    SessionFocusSourceObservation7490 ObserveFocusSource7490(OwnerScope owner,
        Chummer.Application.Workspaces.WorkspaceStoredDocument saved,
        Chummer.Application.Characters.ICharacterSourceDataResolver sources, string? environment,
        Chummer.Application.Characters.CharacterCurrentRuleEnvironmentContext? context)
        => throw new SessionPlayException(SessionPlayFailures.Unavailable("focus-continuation-source-unavailable"));
    SessionPlayAuthority Resolve(OwnerScope owner, string characterId);
    SessionConditionTransferBinding ResolveContinuation(OwnerScope owner, string characterId)
        => throw new SessionPlayException(SessionPlayFailures.Unavailable("session-continuation-custody-unavailable"));
    SessionConditionTransferBinding ResolveContinuationWithOwnerLease(IOwnerContextLeaseAccessor owners,
        OwnerContextStamp expected, IOwnerContextLease lease, string characterId)
        => ResolveContinuation(expected.Owner, characterId);
    SessionPlayAuthority ResolveWithOwnerLease(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected, IOwnerContextLease lease, string characterId)
        => Resolve(expected.Owner, characterId);
}

/// <summary>One exclusive, owner/character scoped transaction. The lock spans durable intent and result.</summary>
public interface ISessionPlayStore
{
    T Execute<T>(OwnerScope owner, string characterId, Func<ISessionPlayTransaction, T> action);
    T Inspect<T>(OwnerScope owner, string characterId, Func<ISessionPlayReader?, T> action)
        => throw new SessionPlayException(SessionPlayFailures.Unavailable("read-only-inspection-unavailable"));
}
public interface ISessionPlayReader
{
    IReadOnlyList<SessionFocusContinuationStored7490> ReadFocusContinuations7490() => [];
    SessionPlayOrigin? ReadOrigin();
    IReadOnlyList<SessionPlayStoredOperation> ReadOperations();
    IReadOnlyList<SessionConditionTransferStored> ReadConditionTransfers() => [];
    IReadOnlyList<SessionNaturalRecoveryStored> ReadNaturalRecoveries() => [];
}
public interface ISessionPlayTransaction : ISessionPlayReader
{
    SessionFocusContinuationStored7490 PrepareFocusContinuation7490(SessionFocusContinuationIntent7490 intent)
        => throw new SessionPlayException(SessionPlayFailures.Unavailable("focus-continuation-store-unavailable"));
    void PrepareFocusRetirement7490(SessionFocusContinuationStored7490 original, SessionFocusContinuationIntent7490 intent)
        => throw new SessionPlayException(SessionPlayFailures.Unavailable("focus-continuation-store-unavailable"));
    void AcknowledgeFocusContinuation7490(SessionFocusContinuationStored7490 original,
        SessionFocusContinuationReceipt7490 receipt)
        => throw new SessionPlayException(SessionPlayFailures.Unavailable("focus-continuation-store-unavailable"));
    SessionConditionTransferStored PrepareConditionTransfer(SessionConditionTransferIntent intent)
        => throw new SessionPlayException(SessionPlayFailures.Unavailable("condition-transfer-store-unavailable"));
    void AcknowledgeConditionTransfer(SessionConditionTransferStored original, SessionConditionTransferReceipt receipt)
        => throw new SessionPlayException(SessionPlayFailures.Unavailable("condition-transfer-store-unavailable"));
    SessionNaturalRecoveryStored PrepareNaturalRecovery(SessionNaturalRecoveryIntent intent)
        => throw new SessionPlayException(SessionPlayFailures.Unavailable("natural-recovery-store-unavailable"));
    void AcknowledgeNaturalRecovery(SessionNaturalRecoveryStored original, SessionNaturalRecoveryReceipt receipt)
        => throw new SessionPlayException(SessionPlayFailures.Unavailable("natural-recovery-store-unavailable"));
    void CreateOrigin(SessionPlayOrigin origin);
    SessionPlayStoredOperation Prepare(SessionPlayOperation intent);
    void Complete(SessionPlayStoredOperation operation, SessionPlayOperationResult result);
}
public sealed class SessionPlayException : Exception
{
    public SessionPlayException(SessionPlayFailure failure) : base(failure.Code) { Failure = failure; }
    public SessionPlayFailure Failure { get; }
}
public static class SessionPlayLimits
{
    // Storage/transport budgets, not game rules. Reaching a budget never removes an only copy.
    public const int MaxBatchEvents = 64;
    public const int MaxEvents = 2048;
    public const int MaxOperations = 1024;
    public const int MaxRequestBytes = 65536;
    public const int MaxRecordBytes = 8 * 1024 * 1024;
    public const long MaxCharacterBytes = 64L * 1024 * 1024;
    public const long MaxStoreBytes = 512L * 1024 * 1024;
    public const int MaxCharacters = 1024;
    public const int MaxNoteLength = 2048;
}
