using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

public sealed partial class DurableSessionPlayService
{
    public SessionApiResult<SessionPlayElementalReview> PreviewCharacterElementalFollowup(OwnerScope owner,
        string characterId, SessionPatchRequest request) => Guard(() =>
    {
        var intent = Canonical(request.OperationId, "apply", request.OverlayId, request.BaseCharacterVersion, request.Events, null);
        Require(intent.Events.Count == 1 && intent.Events[0].EventType == SessionPlayElementalFollowups.EventType,
            "elemental-followup-requires-single-event");
        return store.Inspect(owner, characterId, reader =>
        {
            var origin = reader?.ReadOrigin() ?? throw new SessionPlayException(SessionPlayFailures.Conflict("journal-origin-missing"));
            var operations = reader!.ReadOperations();
            Conflict(operations.All(o => o.Result is not null && o.Intent.OperationId != intent.OperationId), "original-operation-needs-inspection");
            var current = ResolveCurrentWithContinuation(reader!, owner, characterId);
            ValidateAuthority(current, owner, characterId);
            Conflict(MatchesOrigin5082(origin.Authority, current) && intent.OverlayId == origin.OverlayId
                && intent.BaseCharacterVersion == CurrentPlayVersion5712(reader!, current), "canonical-base-or-runtime-changed");
            var history = History(operations);
            SessionPlayMagicUpgrade5311.ValidateCurrent(origin, current, history);
            var snapshot = Replay(reader!, origin, history,
                operations.LastOrDefault(o => o.Result?.Snapshot is not null)?.Intent.OperationId ?? "", out var projection);
            CheckReplay(reader!, operations, snapshot);
            current = current with { InitialCondition5412 = origin.Authority.InitialCondition5412 is null ? null : current.InitialCondition5412 };
            var values = SessionPlayConditionRecovery5412.ReplayValues(current, snapshot);
            var e = SessionPlayElementalFollowups.PrepareVehiclePolicy5400(intent.Events.Single(), current);
            e = SessionPlayElementalFollowups.PrepareCollisionReview5420(e, current,
                values,
                projection.History, projection.Effects, history);
            intent = Canonical(intent.OperationId, "apply", intent.OverlayId, intent.BaseCharacterVersion, [e], null);
            ValidateCurrentEnvelope5712(reader!, origin, e);
            Conflict(e.Sequence == history.Length + 1L && e.ParentEventId == history.LastOrDefault()?.EventId, "event-order-divergence");
            var command = SessionPlayElementalFollowups.Decode(e);
            SessionPlayElementalFollowups.ValidateNewVehiclePolicy5400(e, current);
            SessionPlayElementalFollowups.ValidateNewCollisionTiming5411(e);
            SessionPlayElementalFollowups.ValidateNewCollisionTransitions5420(e);
            var effects = projection.Effects.ToList();
            var (rolls, outcome) = SessionPlayElementalFollowups.Evaluate(effects, projection.History.ToList(), e, history, true, current, values);
            return new SessionPlayElementalReview(new(intent.OverlayId, intent.BaseCharacterVersion, intent.Events) { OperationId = intent.OperationId },
                intent.PayloadDigest, projection.Effects.Single(x => x.EffectId == command.EffectId), command, rolls, outcome);
        });
    });
}
