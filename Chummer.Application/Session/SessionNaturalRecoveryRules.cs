using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

public static class SessionNaturalRecoveryRules
{
    public const int MaximumOperations = 256;
    public static void ValidateReview(SessionNaturalRecoveryReview r, OwnerScope owner, string characterId)
    {
        Conflict(r is not null && r.OwnerId == owner.NormalizedValue && r.CharacterId == characterId
            && r.Recovery.Command.OwnerId == r.OwnerId && r.Recovery.Command.Request.WorkspaceId.Value == characterId
            && !r.Recovery.Command.ExplicitlyConfirmed && r.Recovery.Command.Request.OperationId != Guid.Empty
            && r.Digest == Digest(r with { Digest = "" }) && r.PlayOrdinal >= 0
            && r.EventCount >= 0 && r.EventCount <= SessionPlayLimits.MaxEvents, "recovery-review-invalid");
        Identity(r.OverlayId, "overlay-required");
        SessionPlayInitialConditionSource5412.Validate(r.Canonical.Authority);
        if (r.Recovery.Command.Request.Action != Sr5RecoveryAction.Cancel)
            Conflict(r.Condition.Current == CanonicalCondition(r.Canonical.Authority), "recovery-requires-condition-transfer");
    }
    public static SessionPlayConditionState5412 CanonicalCondition(SessionPlayAuthority authority)
    {
        var values = authority.Trackers.ToDictionary(t => t.TrackerId, t => t.DefaultValue);
        SessionPlayConditionRecovery5412.Seed(authority, values);
        return SessionPlayConditionRecovery5412.Project(authority, values)?.Current
            ?? throw new SessionPlayException(SessionPlayFailures.Unavailable("recovery-condition-authority-required"));
    }
    public static void ValidateReceipt(SessionNaturalRecoveryReceipt receipt, SessionNaturalRecoveryIntent intent)
    {
        var command = intent.Review.Recovery.Command with { ExplicitlyConfirmed = true };
        var op = receipt.Operation; var after = receipt.ContinuedAuthority;
        Conflict(Digest(receipt.Intent) == Digest(intent) && intent.Command == command
            && CharacterSr5NaturalRecoveryTransaction.ValidCommand(command)
            && op.Command == command && op.CommandDigest == CharacterSr5NaturalRecoveryTransaction.Digest(command)
            && op.Digest == CharacterSr5NaturalRecoveryTransaction.OperationDigest(op)
            && receipt.Digest == Digest(receipt with { Digest = "" })
            && after.Binding.OwnerId == intent.Review.OwnerId && after.Binding.Version.CharacterId == intent.Review.CharacterId
            && after.Binding.Version.VersionId == op.RecordedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)
            && after.Binding.Version.RuntimeFingerprint == intent.Review.Canonical.Authority.Binding.Version.RuntimeFingerprint
            && after.Binding.AuthorityDigest == Digest(after with { Binding = after.Binding with { AuthorityDigest = "" } }),
            "recovery-original-divergence");
        var expectedPhase = command.Request.Action switch {
            Sr5RecoveryAction.Reserve => Sr5RecoveryPhase.Reserved, Sr5RecoveryAction.Start => Sr5RecoveryPhase.Started,
            Sr5RecoveryAction.Roll => op.Activity.Phase == Sr5RecoveryPhase.RollUnresolved ? Sr5RecoveryPhase.RollUnresolved : Sr5RecoveryPhase.Rolled,
            Sr5RecoveryAction.Complete => Sr5RecoveryPhase.Completed, Sr5RecoveryAction.Cancel => Sr5RecoveryPhase.Cancelled,
            _ => throw new InvalidOperationException("Recovery action is invalid.") };
        Conflict(op.Activity.Phase == expectedPhase && receipt.Condition == CanonicalCondition(after), "recovery-phase-divergence");
        // Cancel acknowledges unchanged canonical condition, never a live healing checkpoint.
        var before = command.Request.Action == Sr5RecoveryAction.Cancel
            ? CanonicalCondition(intent.Review.Canonical.Authority) : intent.Review.Condition.Current;
        if (expectedPhase != Sr5RecoveryPhase.Completed)
            Conflict(receipt.Condition == before, "recovery-unexpected-condition-change");
        else
        {
            var c = receipt.Condition;
            var completion = op.Activity.Completed;
            Conflict(completion is not null && completion.TransactionId == command.Request.OperationId
                && completion.ActivityId == command.Request.ActivityId && completion.WorkspaceId == command.Request.WorkspaceId
                && completion.AppliedWorkspaceRevision == op.RecordedRevision
                && (command.Request.Track == CharacterSr5HealingTrack.Physical
                    ? c.Physical == completion.DamageBoxesAfter && before.Physical == completion.DamageBoxesBefore && c.Stun == before.Stun
                    : c.Stun == completion.DamageBoxesAfter && before.Stun == completion.DamageBoxesBefore && c.Physical == before.Physical),
                "recovery-completion-result-divergence");
            Conflict(before.Health != SessionPlayCrashHealth.Dead && before.Overflow == 0 && c.Overflow == 0
                && c.Physical <= before.Physical && c.Stun <= before.Stun && c.Stabilized == before.Stabilized
                && c.PhysicalMaximum == before.PhysicalMaximum && c.StunMaximum == before.StunMaximum
                && c.Body == before.Body && c.OverflowLimit == before.OverflowLimit
                && c.ExcessStunRemainder == (c.Stun < before.Stun ? 0 : before.ExcessStunRemainder), "recovery-healing-divergence");
        }
    }
}
