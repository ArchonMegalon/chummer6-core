using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

public sealed partial class DurableSessionPlayService
{
    // The same read-only review convention as PreviewCharacterCasting. Replay
    // uses the actual ammunition/fire reducers; there is no second round formula
    // or durable intent until explicit confirmation enters Execute.
    public SessionApiResult<SessionPlayAmmunitionReview5701> PreviewCharacterAmmunition5701(OwnerScope owner,
        string characterId, SessionPatchRequest request) => Guard(() =>
    {
        var intent = AmmunitionIntent5701(request);
        return store.Inspect(owner, characterId, reader =>
        {
            var origin = reader?.ReadOrigin() ?? throw new SessionPlayException(SessionPlayFailures.Conflict("journal-origin-missing"));
            var operations = reader!.ReadOperations();
            Conflict(operations.All(o => o.Result is not null && o.Intent.OperationId != intent.OperationId), "original-operation-needs-inspection");
            var current = ResolveCurrentWithContinuation(reader, owner, characterId);
            ValidateAuthority(current, owner, characterId);
            Conflict(MatchesOrigin5082(origin.Authority, current) && intent.OverlayId == origin.OverlayId
                && intent.BaseCharacterVersion == CurrentPlayVersion5712(reader!, current), "canonical-base-or-runtime-changed");
            var history = History(operations);
            SessionPlayMagicUpgrade5311.ValidateCurrent(origin, current, history);
            var before = Replay(reader, origin, history, operations.LastOrDefault(o => o.Result?.Snapshot is not null)?.Intent.OperationId ?? "");
            CheckReplay(reader, operations, before);
            ValidateCurrentEnvelope5712(reader!, origin, intent.Events[0]);
            Conflict(intent.Events[0].Sequence == history.Length + 1L && intent.Events[0].ParentEventId == history.LastOrDefault()?.EventId,
                "event-order-divergence");
            var after = Replay(reader, origin, history.Concat(intent.Events).ToArray(), intent.OperationId);
            Require(before.Play?.Ammunition is not null && after.Play?.Ammunition is not null, "ammunition-authority-unavailable");
            SessionAmmunitionSettlementRules5712.ValidateIdentitySets(before.Play!.Ammunition!, after.Play!.Ammunition!);
            var reviewed = new SessionPatchRequest(intent.OverlayId, intent.BaseCharacterVersion, intent.Events) { OperationId = intent.OperationId };
            return new SessionPlayAmmunitionReview5701(reviewed, intent.PayloadDigest, before.Play!.Ammunition!, after.Play!.Ammunition!);
        });
    });

    public SessionApiResult<SessionOverlaySnapshot> ConfirmCharacterAmmunition5701(OwnerScope owner,
        string characterId, SessionPlayAmmunitionReview5701 review, bool explicitlyConfirmed) => Guard(() =>
    {
        Require(explicitlyConfirmed, "explicit-confirmation-required");
        Require(review is not null, "review-required");
        var intent = AmmunitionIntent5701(review!.Request);
        Conflict(intent.PayloadDigest == review.PayloadDigest, "review-payload-changed");
        // Before/After are display values, never mutation authority. Execute
        // rechecks source and journal revisions and owns original-ID reconciliation.
        var result = Execute(owner, characterId, intent);
        if (result.Failure is not null) throw new SessionPlayException(result.Failure);
        return result.Snapshot!;
    });

    private static SessionPlayOperation AmmunitionIntent5701(SessionPatchRequest request)
    {
        Require(request is not null, "request-required");
        var intent = Canonical(request!.OperationId, "apply", request.OverlayId, request.BaseCharacterVersion, request.Events, null);
        Require(intent.Events.Count == 1 && intent.Events[0].EventType is
            SessionPlayEventTypes.WeaponFire or SessionPlayEventTypes.WeaponReload or SessionPlayEventTypes.WeaponUnload,
            "ammunition-requires-single-event");
        return intent;
    }
}
