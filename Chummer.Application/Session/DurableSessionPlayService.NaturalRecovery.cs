using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

public sealed partial class DurableSessionPlayService
{
    public SessionApiResult<SessionNaturalRecoveryReview> ReviewNaturalRecovery(OwnerScope owner, string characterId,
        Sr5RecoveryRequest request, ISessionNaturalRecoveryStore canonicalStore) => Guard(() => store.Execute(owner, characterId, tx =>
    {
        Require(request.WorkspaceId.Value == characterId && CharacterSr5NaturalRecoveryTransaction.ValidRequest(request), "recovery-request-invalid");
        // Inspect the original before resolving current source or creating any journal origin.
        Conflict(tx.ReadNaturalRecoveries().All(x => x.Intent.Command.Request.OperationId != request.OperationId), "recovery-original-needs-inspection");
        var origin = Origin(tx, owner, characterId, out _);
        var operations = tx.ReadOperations();
        Conflict(operations.All(x => x.Result is not null && x.Intent.OperationId != request.OperationId.ToString("D"))
            && tx.ReadConditionTransfers().All(x => x.Intent.Review.OperationId != request.OperationId.ToString("D")), "original-operation-needs-inspection");
        var current = canonicalStore.Review(owner, characterId, request);
        Conflict(MatchesOrigin5082(origin.Authority, ValidateContinuation(tx, current.Canonical.Authority, current.Canonical)), "canonical-base-or-runtime-changed");
        var snapshot = Replay(tx, origin, History(operations), operations.LastOrDefault(x => x.Result?.Snapshot is not null)?.Intent.OperationId ?? "");
        CheckReplay(tx, operations, snapshot);
        Require(snapshot.Play?.Condition5412 is not null, "recovery-condition-authority-required");
        var review = new SessionNaturalRecoveryReview(current.Recovery, owner.NormalizedValue, characterId,
            origin.OverlayId, Digest(origin), snapshot.Play!.ReplicaCursor, operations.LastOrDefault()?.Ordinal ?? 0,
            History(operations).Length, snapshot.Play.Condition5412!, current.Canonical, "");
        review = review with { Digest = Digest(review) };
        SessionNaturalRecoveryRules.ValidateReview(review, owner, characterId);
        return review;
    }));

    public SessionApiResult<SessionNaturalRecoveryReceipt> ExecuteNaturalRecovery(OwnerScope owner, string characterId,
        SessionNaturalRecoveryReview review, bool explicitlyConfirmed, ISessionNaturalRecoveryStore canonicalStore,
        CancellationToken token = default) => Guard(() => store.Execute(owner, characterId, tx =>
    {
        review = System.Text.Json.JsonSerializer.Deserialize<SessionNaturalRecoveryReview>(System.Text.Json.JsonSerializer.Serialize(review, Json), Json)!;
        SessionNaturalRecoveryRules.ValidateReview(review, owner, characterId);
        var request = review.Recovery.Command.Request;
        var prior = tx.ReadNaturalRecoveries().SingleOrDefault(x => x.Intent.Command.Request.OperationId == request.OperationId);
        if (prior is not null)
        {
            Conflict(prior.Intent.Review.Digest == review.Digest, "recovery-original-payload-mismatch");
            return prior.Receipt is { } priorAcknowledged ? ConfirmNaturalRecoveryAcknowledgement5485(tx, owner, characterId, priorAcknowledged, canonicalStore)
                : ReconcileNaturalRecovery(tx, owner, characterId, prior, canonicalStore);
        }
        var command = review.Recovery.Command with { ExplicitlyConfirmed = true };
        var original = canonicalStore.Lookup(owner, characterId, request.OperationId, CharacterSr5NaturalRecoveryTransaction.Digest(command));
        Conflict(original.Outcome == Sr5RecoveryOutcome.NotFound, "recovery-canonical-original-needs-inspection");
        Require(explicitlyConfirmed, "recovery-explicit-review-required");
        Require(!token.IsCancellationRequested, "recovery-cancelled-before-dispatch");
        var origin = Origin(tx, owner, characterId, out _);
        var operations = tx.ReadOperations();
        var snapshot = Replay(tx, origin, History(operations), operations.LastOrDefault(x => x.Result?.Snapshot is not null)?.Intent.OperationId ?? "");
        CheckReplay(tx, operations, snapshot);
        Conflict(operations.All(x => x.Result is not null) && (operations.LastOrDefault()?.Ordinal ?? 0) == review.PlayOrdinal
            && Digest(origin) == review.OriginDigest && origin.OverlayId == review.OverlayId
            && snapshot.Play?.ReplicaCursor == review.Cursor && snapshot.Play?.Condition5412 == review.Condition,
            "recovery-live-review-stale");
        var current = canonicalStore.Review(owner, characterId, request);
        Conflict(Digest(current.Canonical) == Digest(review.Canonical) && Digest(current.Recovery) == Digest(review.Recovery), "recovery-canonical-review-stale");
        Conflict(MatchesOrigin5082(origin.Authority, ValidateContinuation(tx, current.Canonical.Authority, current.Canonical)),
            "canonical-base-or-runtime-changed");
        SessionNaturalRecoveryStored? prepared = null;
        var admission = new SessionNaturalRecoveryCommit(new(review, command, DateTimeOffset.UtcNow),
            intent => prepared = tx.PrepareNaturalRecovery(intent), receipt => {
                Conflict(prepared is not null, "recovery-intent-not-published");
                tx.AcknowledgeNaturalRecovery(prepared!, receipt);
            });
        try { canonicalStore.Execute(owner, characterId, admission, token); }
        catch (Exception e) when (CharacterSr5NaturalRecoveryService.Expected(e) || e is SessionPlayException)
        {
            // Dispatch is never repeated, even if the publication response was lost.
        }
        var observed = tx.ReadNaturalRecoveries().SingleOrDefault(x => x.Intent.Command.Request.OperationId == request.OperationId);
        if (observed is null) throw RecoveryPending(request.OperationId);
        return observed.Receipt is { } observedAcknowledged ? ConfirmNaturalRecoveryAcknowledgement5485(tx, owner, characterId, observedAcknowledged, canonicalStore)
            : ReconcileNaturalRecovery(tx, owner, characterId, observed, canonicalStore);
    }));

    public SessionApiResult<SessionNaturalRecoveryReceipt> RecoverNaturalRecovery(OwnerScope owner, string characterId,
        Guid originalOperationId, ISessionNaturalRecoveryStore canonicalStore) => Guard(() => store.Execute(owner, characterId, tx =>
    {
        var original = tx.ReadNaturalRecoveries().SingleOrDefault(x => x.Intent.Command.Request.OperationId == originalOperationId)
            ?? throw RecoveryPending(originalOperationId);
        return original.Receipt is { } receipt ? ConfirmNaturalRecoveryAcknowledgement5485(tx, owner, characterId, receipt, canonicalStore)
            : ReconcileNaturalRecovery(tx, owner, characterId, original, canonicalStore);
    }));

    public SessionApiResult<SessionNaturalRecoveryReceipt> InspectNaturalRecovery(OwnerScope owner, string characterId,
        Guid originalOperationId) => Guard(() => store.Inspect(owner, characterId, reader =>
        reader?.ReadNaturalRecoveries().SingleOrDefault(x => x.Intent.Command.Request.OperationId == originalOperationId)?.Receipt
            ?? throw RecoveryPending(originalOperationId)));

    private static SessionNaturalRecoveryReceipt ReconcileNaturalRecovery(ISessionPlayTransaction tx, OwnerScope owner,
        string characterId, SessionNaturalRecoveryStored original, ISessionNaturalRecoveryStore canonicalStore)
    {
        var id = original.Intent.Command.Request.OperationId;
        var result = canonicalStore.Lookup(owner, characterId, id, CharacterSr5NaturalRecoveryTransaction.Digest(original.Intent.Command));
        if (result.Operation is not { } operation || result.Outcome is not (Sr5RecoveryOutcome.Recovered or Sr5RecoveryOutcome.Unknown))
            throw RecoveryPending(id);
        var receipt = canonicalStore.ReadCommitted(owner, characterId, original.Intent, operation);
        SessionNaturalRecoveryRules.ValidateReceipt(receipt, original.Intent);
        tx.AcknowledgeNaturalRecovery(original, receipt);
        if (operation.Activity.Phase == Sr5RecoveryPhase.RollUnresolved) throw RecoveryPending(id);
        return ConfirmNaturalRecoveryAcknowledgement5485(tx, owner, characterId, receipt, canonicalStore);
    }
    private static SessionNaturalRecoveryReceipt ConfirmNaturalRecoveryAcknowledgement5485(ISessionPlayReader reader,
        OwnerScope owner, string characterId, SessionNaturalRecoveryReceipt receipt, ISessionNaturalRecoveryStore canonicalStore)
    {
        if (receipt.Operation.Activity.Phase is (Sr5RecoveryPhase.Completed or Sr5RecoveryPhase.Cancelled)
            && canonicalStore is ISessionNaturalRecoveryAcknowledgementStore acknowledgements)
        {
            var acknowledged = reader.ReadNaturalRecoveries().SingleOrDefault(x =>
                x.Intent.Command.Request.OperationId == receipt.Operation.Command.Request.OperationId)?.Receipt;
            Conflict(acknowledged is not null && Digest(acknowledged) == Digest(receipt), "recovery-acknowledgement-unproven");
            acknowledgements.ConfirmAcknowledgement(owner, characterId, new(receipt));
        }
        return receipt;
    }
    private static SessionPlayException RecoveryPending(Guid id) => new(
        SessionPlayFailures.Unavailable("natural-recovery-requires-original-recovery") with { OperationId = id.ToString("D") });

    private static IReadOnlyList<SessionNaturalRecoveryCheckpoint> RecoveryCheckpoints(ISessionPlayReader reader, long? beforeOrdinal = null)
        => reader.ReadNaturalRecoveries().Where(x => x.Receipt?.Operation.Activity.Phase == Sr5RecoveryPhase.Completed
            && (beforeOrdinal is null || x.Intent.Review.PlayOrdinal < beforeOrdinal.Value))
            .Select(x => new SessionNaturalRecoveryCheckpoint(x.Intent.Review.EventCount, x.Intent.Review.PlayOrdinal,
                x.Receipt!.Digest, x.Intent.Review.Condition.Current, x.Receipt.Condition)).ToArray();
    private static SessionOverlaySnapshot Replay(ISessionPlayReader reader, SessionPlayOrigin origin,
        IReadOnlyList<SessionEventEnvelope> events, string operationId)
        => ReplaySettlement5712(reader, origin, events, operationId, null, out _);
    private static SessionOverlaySnapshot Replay(ISessionPlayReader reader, SessionPlayOrigin origin,
        IReadOnlyList<SessionEventEnvelope> events, string operationId, out SessionPlayElementalProjection elemental)
        => ReplaySettlement5712(reader, origin, events, operationId, null, out elemental);
}
