using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public sealed record SessionNaturalRecoveryCanonicalReview(Sr5RecoveryReview Recovery, SessionConditionTransferBinding Canonical);
// Every call is under the caller's owner and journal leases. Implementations never acquire the journal.
public interface ISessionNaturalRecoveryStore
{
    SessionNaturalRecoveryCanonicalReview Review(OwnerScope owner, string characterId, Sr5RecoveryRequest request);
    Sr5RecoveryResult Lookup(OwnerScope owner, string characterId, Guid operationId, string commandDigest);
    SessionNaturalRecoveryReceipt ReadCommitted(OwnerScope owner, string characterId,
        SessionNaturalRecoveryIntent intent, Sr5RecoveryOperation operation);
    void Execute(OwnerScope owner, string characterId, SessionNaturalRecoveryCommit commit, CancellationToken token);
}

// Optional private-store capability. The witness cannot be constructed by native callers.
public interface ISessionNaturalRecoveryAcknowledgementStore
{
    void ConfirmAcknowledgement(OwnerScope owner, string characterId, SessionNaturalRecoveryAcknowledgement acknowledgement);
}
public sealed class SessionNaturalRecoveryAcknowledgement
{
    private readonly string _receipt;
    internal SessionNaturalRecoveryAcknowledgement(SessionNaturalRecoveryReceipt receipt)
        => _receipt = System.Text.Json.JsonSerializer.Serialize(receipt, SessionPlayEncoding.Json);
    public SessionNaturalRecoveryReceipt Receipt
        => System.Text.Json.JsonSerializer.Deserialize<SessionNaturalRecoveryReceipt>(_receipt, SessionPlayEncoding.Json)!;
}

public sealed class SessionNaturalRecoveryCommit
{
    private readonly string _intent;
    private Action<SessionNaturalRecoveryIntent>? _prepare;
    private readonly Action<SessionNaturalRecoveryReceipt> _acknowledge;
    private bool _prepared;
    internal SessionNaturalRecoveryCommit(SessionNaturalRecoveryIntent intent,
        Action<SessionNaturalRecoveryIntent> prepare, Action<SessionNaturalRecoveryReceipt> acknowledge)
    { _intent = System.Text.Json.JsonSerializer.Serialize(intent, SessionPlayEncoding.Json); _prepare = prepare; _acknowledge = acknowledge; }
    public SessionNaturalRecoveryIntent Intent => System.Text.Json.JsonSerializer.Deserialize<SessionNaturalRecoveryIntent>(_intent, SessionPlayEncoding.Json)!;
    public void PublishIntent()
    {
        if (_prepared) return; // The second write is the same Roll command, never a new intent.
        var prepare = Interlocked.Exchange(ref _prepare, null)
            ?? throw new InvalidOperationException("Recovery intent publication is unresolved.");
        prepare(Intent); _prepared = true;
    }
    public void Acknowledge(SessionNaturalRecoveryReceipt receipt)
    {
        if (!_prepared) throw new InvalidOperationException("Recovery intent was not published.");
        SessionNaturalRecoveryRules.ValidateReceipt(receipt, Intent);
        _acknowledge(receipt); // Already-held journal transaction; no lock acquisition.
    }
}
