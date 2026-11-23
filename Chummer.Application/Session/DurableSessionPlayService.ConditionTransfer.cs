using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

public sealed partial class DurableSessionPlayService
{
    public SessionApiResult<SessionConditionTransferReview> ReviewConditionTransfer(OwnerScope owner, string characterId,
        string operationId, ISessionConditionTransferStore canonicalStore) => Guard(() => store.Execute(owner, characterId, tx =>
    {
        Identity(operationId, "operation-id-required");
        var origin = Origin(tx, owner, characterId, out _);
        var operations = tx.ReadOperations();
        Conflict(operations.All(o => o.Result is not null && o.Intent.OperationId != operationId)
            && tx.ReadConditionTransfers().All(t => t.Intent.Review.OperationId != operationId), "original-operation-needs-inspection");
        var current = canonicalStore.Read(owner, characterId);
        ValidateAuthority(current.Authority, owner, characterId);
        Conflict(MatchesOrigin5082(origin.Authority, ValidateContinuation(tx, current.Authority, current)), "canonical-base-or-runtime-changed");
        var snapshot = Replay(tx, origin, History(operations),
            operations.LastOrDefault(o => o.Result?.Snapshot is not null)?.Intent.OperationId ?? "");
        CheckReplay(tx, operations, snapshot);
        Require(snapshot.Play?.Condition5412 is not null, "condition-transfer-authority-required");
        var review = new SessionConditionTransferReview(operationId, owner.NormalizedValue, characterId,
            origin.OverlayId, Digest(origin), snapshot.Play!.ReplicaCursor, snapshot.Play.Condition5412!, current, "") {
            Ammunition = SessionAmmunitionSettlementRules5712.Review(tx, current.Authority.Ammunition, snapshot.Play.Ammunition)
        };
        return review with { Digest = Digest(review) };
    }));

    public SessionApiResult<SessionConditionTransferReceipt> TransferCondition(OwnerScope owner, string characterId,
        SessionConditionTransferReview review, bool explicitlyConfirmed, ISessionConditionTransferStore canonicalStore)
        => Guard(() => store.Execute(owner, characterId, tx =>
    {
        review = System.Text.Json.JsonSerializer.Deserialize<SessionConditionTransferReview>(
            System.Text.Json.JsonSerializer.Serialize(review, Json), Json)!;
        SessionConditionTransferRules.ValidateReview(review, owner, characterId);
        var prior = tx.ReadConditionTransfers().SingleOrDefault(t => t.Intent.Review.OperationId == review.OperationId);
        if (prior is not null)
        {
            Conflict(prior.Intent.Review.Digest == review.Digest, "condition-transfer-payload-mismatch");
            return prior.Receipt ?? ReconcileConditionTransfer(tx, owner, characterId, prior, canonicalStore);
        }
        Require(explicitlyConfirmed, "condition-transfer-explicit-review-required");
        var origin = Origin(tx, owner, characterId, out _);
        var operations = tx.ReadOperations();
        Conflict(operations.All(o => o.Result is not null && o.Intent.OperationId != review.OperationId), "original-operation-needs-inspection");
        var snapshot = Replay(tx, origin, History(operations),
            operations.LastOrDefault(o => o.Result?.Snapshot is not null)?.Intent.OperationId ?? "");
        CheckReplay(tx, operations, snapshot);
        Conflict(review.OriginDigest == Digest(origin) && review.OverlayId == origin.OverlayId
            && review.Cursor == snapshot.Play?.ReplicaCursor && review.Condition == snapshot.Play?.Condition5412,
            "condition-transfer-live-review-stale");
        var current = canonicalStore.Read(owner, characterId);
        Conflict(Digest(current) == Digest(review.Canonical), "condition-transfer-canonical-review-stale");
        Conflict(Digest(review.Ammunition) == Digest(SessionAmmunitionSettlementRules5712.Review(tx,
            current.Authority.Ammunition, snapshot.Play!.Ammunition)), "ammunition-settlement-live-review-stale");
        if (review.Ammunition is not null)
            Conflict(!SessionAmmunitionSettlementRules5712.SameQuantities(review.Ammunition.Before, review.Ammunition.After)
                || current.Authority.InitialCondition5412?.CanonicalCondition != review.Condition.Current
                || SessionAmmunitionSettlementRules5712.RequiresSettlement(tx, current.PlayCheckpoint5712),
                "ammunition-settlement-already-canonical");
        Conflict(MatchesOrigin5082(origin.Authority, ValidateContinuation(tx, current.Authority, current)), "canonical-base-or-runtime-changed");
        // Canonical preflight runs under its lease before publishing intent through this already-held journal transaction.
        // The callback acquires no lock. Its durable intent always precedes canonical target replacement.
        SessionConditionTransferStored? prepared = null;
        var intent = new SessionConditionTransferIntent(review, DateTimeOffset.UtcNow);
        var continuation = review.Ammunition is null ? null : CaptureSettlement5712(tx, intent);
        var admission = new SessionConditionTransferCommit(intent, value => prepared = tx.PrepareConditionTransfer(value), continuation);
        var receipt = canonicalStore.Commit(owner, characterId, admission);
        Conflict(prepared is not null, "condition-transfer-intent-not-published");
        SessionConditionTransferRules.ValidateReceipt(receipt, prepared!.Intent);
        return AcknowledgeConditionTransfer(tx, owner, characterId, prepared, receipt, canonicalStore);
    }));

    public SessionApiResult<SessionConditionTransferReceipt> RecoverConditionTransfer(OwnerScope owner, string characterId,
        string originalOperationId, ISessionConditionTransferStore canonicalStore) => Guard(() => store.Execute(owner, characterId, tx =>
    {
        Identity(originalOperationId, "operation-id-required");
        var original = tx.ReadConditionTransfers().SingleOrDefault(t => t.Intent.Review.OperationId == originalOperationId)
            ?? throw new SessionPlayException(SessionPlayFailures.Conflict("condition-transfer-not-prepared"));
        return original.Receipt ?? ReconcileConditionTransfer(tx, owner, characterId, original, canonicalStore);
    }));

    public SessionApiResult<SessionConditionTransferReceipt> InspectConditionTransfer(OwnerScope owner, string characterId,
        string originalOperationId) => Guard(() => store.Inspect(owner, characterId, reader =>
    {
        Identity(originalOperationId, "operation-id-required");
        var original = reader?.ReadConditionTransfers().SingleOrDefault(t => t.Intent.Review.OperationId == originalOperationId)
            ?? throw new SessionPlayException(SessionPlayFailures.Conflict("condition-transfer-not-prepared"));
        return original.Receipt ?? throw TransferPending(originalOperationId);
    }));

    private static SessionConditionTransferReceipt ReconcileConditionTransfer(ISessionPlayTransaction tx, OwnerScope owner,
        string characterId, SessionConditionTransferStored original, ISessionConditionTransferStore canonicalStore)
    {
        // Never call Commit here. A missing/unknown durable outcome remains blocked under this exact identity.
        var receipt = canonicalStore.Lookup(owner, characterId, original.Intent.Review.OperationId);
        if (receipt is null) throw TransferPending(original.Intent.Review.OperationId);
        SessionConditionTransferRules.ValidateReceipt(receipt, original.Intent);
        return AcknowledgeConditionTransfer(tx, owner, characterId, original, receipt, canonicalStore);
    }

    private static SessionConditionTransferReceipt AcknowledgeConditionTransfer(ISessionPlayTransaction tx, OwnerScope owner,
        string characterId, SessionConditionTransferStored original, SessionConditionTransferCanonicalReceipt canonical,
        ISessionConditionTransferStore canonicalStore)
    {
        var current = canonicalStore.ReadCommittedAuthority(owner, characterId, canonical);
        ValidateAuthority(current, owner, characterId);
        var receipt = new SessionConditionTransferReceipt(canonical, current, "");
        receipt = receipt with { Digest = Digest(receipt) };
        SessionConditionTransferRules.ValidateAcknowledgement(receipt, original.Intent);
        tx.AcknowledgeConditionTransfer(original, receipt);
        return receipt;
    }

    private SessionPlayAuthority ResolveCurrentWithContinuation(ISessionPlayReader reader, OwnerScope owner, string characterId)
    {
        // Check the durable fence before any current-source read or new action.
        if (reader.ReadFocusContinuations7490().LastOrDefault(x => x.Receipt is null) is { } focus) throw FocusPending7490(focus);
        var pending = reader.ReadConditionTransfers().LastOrDefault(t => t.Receipt is null);
        if (pending is not null) throw TransferPending(pending.Intent.Review.OperationId);
        if (reader.ReadNaturalRecoveries().LastOrDefault(t => t.Receipt is null) is { } recovery)
            throw RecoveryPending(recovery.Intent.Command.Request.OperationId);
        if (reader.ReadConditionTransfers().Count == 0 && reader.ReadNaturalRecoveries().Count == 0 && reader.ReadFocusContinuations7490().Count == 0)
            return authority.Resolve(owner, characterId);
        var current = authority.ResolveContinuation(owner, characterId);
        ValidateAuthority(current.Authority, owner, characterId);
        return ValidateContinuation(reader, current.Authority, current);
    }

    private static SessionPlayAuthority ValidateContinuation(ISessionPlayReader reader, SessionPlayAuthority current,
        SessionConditionTransferBinding? custody = null)
    {
        var focuses = reader.ReadFocusContinuations7490();
        if (focuses.LastOrDefault(x => x.Receipt is null) is { } focus) throw FocusPending7490(focus);
        var transfers = reader.ReadConditionTransfers();
        var recoveries = reader.ReadNaturalRecoveries();
        if (transfers.LastOrDefault(t => t.Receipt is null) is { } pending) throw TransferPending(pending.Intent.Review.OperationId);
        if (recoveries.LastOrDefault(t => t.Receipt is null) is { } recovery) throw RecoveryPending(recovery.Intent.Command.Request.OperationId);
        var origin = reader.ReadOrigin();
        if (transfers.Count == 0 && recoveries.Count == 0 && focuses.Count == 0) return current;
        Conflict(origin is not null, "journal-origin-missing");
        // Archived edges remain observable; only this fresh, source-admitted custody permits LIVE work.
        Conflict(custody is not null && !string.IsNullOrWhiteSpace(custody.IncarnationId)
            && !string.IsNullOrWhiteSpace(custody.SlotGeneration) && Digest(custody.Authority) == Digest(current),
            "canonical-continuation-custody-unavailable");
        void CheckCustody(SessionConditionTransferBinding before) => Conflict(
            before.IncarnationId == custody!.IncarnationId && before.SlotGeneration == custody.SlotGeneration,
            "canonical-continuation-custody-changed");
        var edges = new List<(SessionPlayAuthority Before, SessionPlayAuthority After)>();
        foreach (var transfer in transfers)
        {
            SessionConditionTransferRules.ValidateAcknowledgement(transfer.Receipt!, transfer.Intent);
            CheckCustody(transfer.Intent.Review.Canonical);
            Conflict(transfer.Intent.Review.OriginDigest == Digest(origin), "condition-transfer-chain-divergence");
            edges.Add((transfer.Intent.Review.Canonical.Authority, transfer.Receipt!.ContinuedAuthority));
        }
        foreach (var item in recoveries)
        {
            Conflict(item.Intent.Review.OriginDigest == Digest(origin), "recovery-chain-divergence");
            CheckCustody(item.Intent.Review.Canonical);
            var before = item.Intent.Review.Canonical.Authority;
            if (item.Unresolved is { } fence)
            {
                SessionNaturalRecoveryRules.ValidateReceipt(fence, item.Intent);
                edges.Add((before, fence.ContinuedAuthority)); before = fence.ContinuedAuthority;
            }
            SessionNaturalRecoveryRules.ValidateReceipt(item.Receipt!, item.Intent);
            edges.Add((before, item.Receipt!.ContinuedAuthority));
        }
        foreach (var item in focuses)
        {
            var edge = SessionFocusContinuationRules7490.Admitted(item);
            CheckCustody(edge.Before);
            Conflict(edge.OriginDigest == Digest(origin), "focus-continuation-chain-divergence");
            var prefix = History(reader.ReadOperations()).Take(edge.EventCount).ToArray();
            Conflict(prefix.Length == edge.EventCount && Cursor(prefix) == edge.Cursor,
                "focus-continuation-cursor-divergence");
            edges.Add((edge.Before.Authority, item.Receipt!.ContinuedAuthority));
        }
        SessionPlayAuthority expected = origin!.Authority;
        SessionPlayAuthority? historicalAuthority7494 = null;
        while (edges.Count > 0)
        {
            var matches = edges.Where(x => MatchesOrigin5082(expected, x.Before)).ToArray();
            Conflict(matches.Length == 1, "canonical-continuation-chain-divergence");
            historicalAuthority7494 ??= matches[0].Before;
            expected = matches[0].After; edges.Remove(matches[0]);
        }
        // Only this exact source/version/environment is authorized. No arbitrary rebase or wildcard revision.
        Conflict(Digest(expected) == Digest(current), "canonical-base-or-runtime-changed");
        // A focus chain retains the source-admitted capabilities at its first preimage.
        // The immutable origin may predate explicit magic adoption. Returning it here
        // would erase that adoption's validation authority; returning the latest edge
        // would instead apply future focus changes to old receipts. Replay still starts
        // from the unchanged origin. CapabilityHistory7494 selects each adoption's
        // source checkpoint separately from the capabilities available for new offers.
        return focuses.Count == 0 ? origin.Authority : historicalAuthority7494!;
    }

    private static SessionPlayException TransferPending(string operationId) => new(
        SessionPlayFailures.Unavailable("condition-transfer-requires-original-recovery") with { OperationId = operationId });
}
