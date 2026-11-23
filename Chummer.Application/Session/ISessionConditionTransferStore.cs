using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

/// <summary>Called only while holding the journal lease. Implementations must never acquire the journal.</summary>
public interface ISessionConditionTransferStore
{
    SessionConditionTransferBinding Read(OwnerScope owner, string characterId);
    // Original lookup must not read current rules or execute another mutation.
    SessionConditionTransferCanonicalReceipt? Lookup(OwnerScope owner, string characterId, string operationId);
    SessionConditionTransferCanonicalReceipt Commit(OwnerScope owner, string characterId, SessionConditionTransferCommit admission);
    SessionPlayAuthority ReadCommittedAuthority(OwnerScope owner, string characterId, SessionConditionTransferCanonicalReceipt receipt);
}

/// <summary>Issued only by the domain after live replay/CAS under the journal lease. Not a caller-built command.</summary>
public sealed class SessionConditionTransferCommit
{
    private readonly string _intent;
    private Action<SessionConditionTransferIntent>? _publish;
    private readonly string? _continuation;
    internal SessionConditionTransferCommit(SessionConditionTransferIntent intent, Action<SessionConditionTransferIntent> publish,
        SessionPlaySettlementContinuation5712? continuation = null)
    {
        _intent = System.Text.Json.JsonSerializer.Serialize(intent, SessionPlayEncoding.Json); _publish = publish;
        _continuation = continuation is null ? null : System.Text.Json.JsonSerializer.Serialize(continuation, SessionPlayEncoding.Json);
    }
    public SessionPlaySettlementContinuation5712? Continuation5712 => _continuation is null ? null
        : System.Text.Json.JsonSerializer.Deserialize<SessionPlaySettlementContinuation5712>(_continuation, SessionPlayEncoding.Json);
    // Invoked once after canonical preflight, before target replacement; uses the already-held journal transaction.
    public void PublishIntent()
    {
        var publish = Interlocked.Exchange(ref _publish, null)
            ?? throw new SessionPlayException(SessionPlayFailures.Conflict("condition-transfer-intent-already-published"));
        publish(Intent);
    }
    public SessionConditionTransferIntent Intent => System.Text.Json.JsonSerializer.Deserialize<SessionConditionTransferIntent>(
        _intent, SessionPlayEncoding.Json)!;
}
