using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

public sealed partial class DurableSessionPlayService
{
    private static void RefuseMechanicalReplay5262(SessionPlayOperation intent)
    {
        if (intent.Events.Any(e => e.EventType == SessionPlayConditionCommand5412.EventType))
            throw new SessionPlayException(SessionPlayFailures.Unavailable("condition-prepared-outcome-uncertain")
                with { OperationId = intent.OperationId });
        if (intent.Events.Any(e => e.EventType == SessionPlayEventTypes.ElementalFollowup))
            throw new SessionPlayException(SessionPlayFailures.Unavailable("elemental-followup-prepared-outcome-uncertain")
                with { OperationId = intent.OperationId });
        if (intent.Events.Any(e => (e.EventType is SessionPlayEventTypes.MagicCapabilitiesAdopt or SessionPlayEventTypes.IndirectCastingAdopt)))
            throw new SessionPlayException(SessionPlayFailures.Unavailable("magic-adoption-prepared-outcome-uncertain")
                with { OperationId = intent.OperationId });
        if (intent.Events.Any(e => (e.EventType is SessionPlayEventTypes.SpellCast or SessionPlayEventTypes.IndirectSpellCast)))
            throw new SessionPlayException(SessionPlayFailures.Unavailable("casting-prepared-outcome-uncertain")
                with { OperationId = intent.OperationId });
        if (SessionPlaySustainingEffects.IsMechanical(intent))
            throw new SessionPlayException(SessionPlayFailures.Unavailable("sustaining-prepared-outcome-uncertain")
                with { OperationId = intent.OperationId });
    }

    public SessionApiResult<SessionPlayOperationLookup> InspectCharacterOperation(OwnerScope owner,
        string characterId, SessionPatchRequest request) => Guard(() =>
    {
        var intent = Canonical(request.OperationId, "apply", request.OverlayId, request.BaseCharacterVersion, request.Events, null);
        Conflict(intent.BaseCharacterVersion.CharacterId == characterId
            && intent.Events.All(e => e.ActorId == owner.NormalizedValue), "operation-owner-character-mismatch");
        return store.Inspect(owner, characterId, reader =>
        {
            var original = reader?.ReadOperations().SingleOrDefault(o => o.Intent.OperationId == intent.OperationId);
            if (original is null) return new SessionPlayOperationLookup(SessionPlayLookupStatus.NotFound, intent.OperationId, intent.PayloadDigest, null);
            Conflict(original.Intent.PayloadDigest == intent.PayloadDigest, "operation-payload-mismatch");
            return new SessionPlayOperationLookup(original.Result is null ? SessionPlayLookupStatus.PreparedOnly : SessionPlayLookupStatus.Completed,
                intent.OperationId, intent.PayloadDigest, original.Result);
        });
    });

    public SessionApiResult<SessionPlaySustainingReview> PreviewCharacterSustaining(OwnerScope owner,
        string characterId, SessionPatchRequest request) => Guard(() =>
    {
        var intent = Canonical(request.OperationId, "apply", request.OverlayId, request.BaseCharacterVersion, request.Events, null);
        Require(intent.Events.Count == 1 && SessionPlaySustainingEffects.IsMechanical(intent), "sustaining-requires-single-event");
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
            var before = Replay(reader!, origin, history, operations.LastOrDefault(o => o.Result?.Snapshot is not null)?.Intent.OperationId ?? "");
            CheckReplay(reader!, operations, before);
            var after = Replay(reader!, origin, history.Concat(intent.Events).ToArray(), intent.OperationId);
            Require(before.Play!.MechanicalEffects is not null && after.Play!.MechanicalEffects is not null, "sustaining-authority-unavailable");
            var changes = new List<SessionPlayPoolChange>();
            var b = before.Play.Derived; var a = after.Play!.Derived;
            changes.Add(new("Defense", b.DefensePool, a.DefensePool));
            foreach (var skill in b.Skills)
            {
                var next = a.Skills.Single(s => s.Guid == skill.Guid);
                changes.Add(new(skill.Name, skill.DicePool, next.DicePool));
                foreach (var spec in skill.Specializations)
                    changes.Add(new(skill.Name + " · " + spec.Name, spec.DicePool, next.Specializations.Single(s => s.Name == spec.Name).DicePool));
            }
            foreach (string name in new[] { "Composure", "Memory" })
                changes.Add(new(name, b.OtherPoolsAndModifiers[name], a.OtherPoolsAndModifiers[name]));
            return new SessionPlaySustainingReview(new(intent.OverlayId, intent.BaseCharacterVersion, intent.Events) { OperationId = intent.OperationId },
                intent.PayloadDigest, before.Play.MechanicalEffects!, after.Play.MechanicalEffects!, changes);
        });
    });
}
