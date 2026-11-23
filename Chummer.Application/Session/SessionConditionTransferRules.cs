using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using Chummer.Contracts.Workspaces;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

public static class SessionConditionTransferRules
{
    public const int MaximumTransfers = 128;
    public static void ValidateReview(SessionConditionTransferReview review, OwnerScope owner, string characterId)
    {
        Require(review is not null && review.Canonical is not null && review.Canonical.Authority is not null
            && review.Condition is not null && review.Condition.Current is not null && review.Cursor is not null, "condition-transfer-review-required");
        Identity(review.OperationId, "operation-id-required");
        Conflict(review.OwnerId == owner.NormalizedValue && review.CharacterId == characterId
            && review.Canonical.Authority.Binding.OwnerId == owner.NormalizedValue
            && review.Canonical.Authority.Binding.Version.CharacterId == characterId, "condition-transfer-owner-mismatch");
        Require(review.Digest == Digest(review with { Digest = "" }) && review.Cursor.StartsWith("local:", StringComparison.Ordinal)
            && review.Canonical.ContentRevision > 0 && review.Canonical.ContentRevision == review.Canonical.SavedRevision
            && review.Canonical.Authority.Binding.Version.VersionId == review.Canonical.ContentRevision.ToString(CultureInfo.InvariantCulture)
            && review.Canonical.OwnerAuthorityDigest.Length == 64 && !string.IsNullOrWhiteSpace(review.Canonical.IncarnationId) && !string.IsNullOrWhiteSpace(review.Canonical.SlotGeneration),
            "condition-transfer-review-invalid");
        SessionPlayInitialConditionSource5412.Validate(review.Canonical.Authority);
        if (review.Ammunition is not null) SessionAmmunitionSettlementRules5712.Validate(review.Ammunition);
        var c = review.Condition.Current; ValidateState(c);
        var initial = review.Canonical.Authority.InitialCondition5412!;
        Conflict(c.PhysicalMaximum == initial.PhysicalMaximum && c.StunMaximum == initial.StunMaximum
            && c.Body == initial.Body && c.OverflowLimit == initial.Body + initial.OverflowModifier,
            "condition-transfer-rules-changed");
        var saved = SessionPlayElementalFollowups.ConditionState5412(initial.PhysicalMaximum, initial.StunMaximum,
            Math.Min(initial.PhysicalFilled, initial.PhysicalMaximum), initial.StunFilled,
            Math.Max(0, initial.PhysicalFilled - initial.PhysicalMaximum), initial.Body,
            initial.Body + initial.OverflowModifier, initial.CanonicalCondition?.Stabilized, initial.CanonicalCondition?.ExcessStunRemainder ?? 0);
        Conflict(saved.Health != SessionPlayCrashHealth.Dead || c.Health == SessionPlayCrashHealth.Dead,
            "condition-dead-actor-cannot-recover");
    }

    public static void ValidateState(SessionPlayConditionState5412 state)
    {
        Require(state is not null && state.ExcessStunRemainder is 0 or 1
            && state == SessionPlayElementalFollowups.ConditionState5412(state.PhysicalMaximum, state.StunMaximum,
                state.Physical, state.Stun, state.Overflow, state.Body, state.OverflowLimit, state.Stabilized, state.ExcessStunRemainder),
            "condition-transfer-state-invalid");
    }

    public static string PostimageDigest(WorkspaceDocument document) => Digest(document.State with
        { AuxiliaryState = document.AuxiliaryState with { SessionConditionTransfers = null } });

    public static (WorkspaceDocument Document, SessionConditionTransferCanonicalReceipt Receipt) Apply(
        WorkspaceStoredDocument saved, SessionConditionTransferIntent intent, SessionPlaySettlementContinuation5712? continuation = null)
    {
        var review = intent.Review;
        ValidateReview(review, review.OwnerId == OwnerScope.LocalSingleUser.NormalizedValue
            ? OwnerScope.LocalSingleUser : new OwnerScope(review.OwnerId), saved.Id.Value);
        Conflict(saved.ContentRevision == review.Canonical.ContentRevision && saved.SavedRevision == saved.ContentRevision
            && Digest(saved.Document.State) == review.Canonical.Authority.Binding.SourceDigest
            && saved.Document.AuxiliaryStateDigest == review.Canonical.AuxiliaryDigest, "condition-transfer-preimage-changed");
        var history = saved.Document.AuxiliaryState.SessionConditionTransfers ?? [];
        Require(history.Count < MaximumTransfers && history.All(x => x.Intent.Review.OperationId != review.OperationId),
            "condition-transfer-capacity-or-duplicate");
        Conflict((review.Ammunition is null) == (continuation is null), "ammunition-continuation-required");
        if (continuation is not null)
        {
            DurableSessionPlayService.ValidateSettlement5712(continuation, intent);
            continuation = SessionAmmunitionSettlementRules5712.PreserveImportedHistory(continuation, saved.Document.AuxiliaryState.SessionPlaySettlement5712);
            DurableSessionPlayService.ValidateSettlement5712(continuation, intent);
        }
        var c = review.Condition.Current;
        using var reader = XmlReader.Create(new StringReader(saved.Document.Content), new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
        var xml = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        var root = xml.Root ?? throw new SessionPlayException(SessionPlayFailures.Invalid("condition-character-required"));
        SessionPlayInitialConditionSource5412.ValidateScalars(root);
        // Change only canonical damage scalars; never regenerate the character from a projection.
        Set(root, "physicalcmfilled", checked(c.Physical + c.Overflow)); Set(root, "stuncmfilled", c.Stun);
        long revision = checked(saved.ContentRevision + 1);
        var document = saved.Document with { State = saved.Document.State with {
            Payload = review.Ammunition is null
                ? (xml.Declaration is null ? "" : xml.Declaration + "\n") + xml.ToString(SaveOptions.DisableFormatting)
                : SessionAmmunitionSettlementRules5712.Apply(saved.Document.Content, review.Ammunition, c),
            AuxiliaryState = saved.Document.AuxiliaryState with {
                SessionCondition = new(review.OperationId, revision, c), SessionPlaySettlement5712 = continuation ?? saved.Document.AuxiliaryState.SessionPlaySettlement5712 } } };
        var receipt = new SessionConditionTransferCanonicalReceipt(intent, revision, PostimageDigest(document),
            history.LastOrDefault()?.Digest ?? Hash("condition-transfer/v1"), "");
        receipt = receipt with { Digest = Digest(receipt) };
        document = document with { State = document.State with { AuxiliaryState = document.AuxiliaryState with {
            SessionConditionTransfers = history.Append(receipt).ToArray() } } };
        return (document, receipt);
    }

    // Called by the already source-validated natural-recovery writer, before publishing its exact terminal record.
    // This helper does not grant write authority and cannot heal an overflowing or dead character.
    public static WorkspaceDocument WithNaturalRecoveryCondition(WorkspaceDocument before, WorkspaceDocument after,
        Chummer.Contracts.Characters.Sr5RecoveryOperation operation)
    {
        if (before.AuxiliaryState.SessionCondition is not { } stamp) return after;
        var c = stamp.State; ValidateState(c);
        if (operation.Command.Request.Action != Chummer.Contracts.Characters.Sr5RecoveryAction.Complete)
        { Conflict(MatchesCanonicalDamage(after.Content, c), "condition-recovery-unexpected-damage-change"); return after; }
        Conflict(c.Health != SessionPlayCrashHealth.Dead && c.Overflow == 0, "condition-recovery-dead-or-overflow");
        var root = WorkspaceSessionPlayAuthorityResolver.Parse(after.Content);
        SessionPlayInitialConditionSource5412.ValidateScalars(root);
        int p = WorkspaceSessionPlayAuthorityResolver.Integer(root, "physicalcmfilled", 0);
        int s = WorkspaceSessionPlayAuthorityResolver.Integer(root, "stuncmfilled", 0);
        Conflict(p <= c.Physical && s <= c.Stun, "condition-recovery-increased-damage");
        var next = SessionPlayElementalFollowups.ConditionState5412(c.PhysicalMaximum, c.StunMaximum,
            p, s, 0, c.Body, c.OverflowLimit, c.Stabilized, s < c.Stun ? 0 : c.ExcessStunRemainder);
        return after with { State = after.State with { AuxiliaryState = after.AuxiliaryState with {
            SessionCondition = new(operation.Command.Request.OperationId.ToString("D"), operation.RecordedRevision, next) } } };
    }

    public static bool MatchesCanonicalDamage(string xml, SessionPlayConditionState5412 state)
    {
        var root = WorkspaceSessionPlayAuthorityResolver.Parse(xml);
        SessionPlayInitialConditionSource5412.ValidateScalars(root);
        return WorkspaceSessionPlayAuthorityResolver.Integer(root, "physicalcmfilled", 0) == state.Physical + state.Overflow
            && WorkspaceSessionPlayAuthorityResolver.Integer(root, "stuncmfilled", 0) == state.Stun;
    }

    private static void Set(XElement root, string name, int value)
    {
        var element = WorkspaceSessionPlayAuthorityResolver.One(root, name);
        if (element is null) root.Add(new XElement(name, value.ToString(CultureInfo.InvariantCulture)));
        else element.Value = value.ToString(CultureInfo.InvariantCulture);
    }

    public static void ValidateReceipt(SessionConditionTransferCanonicalReceipt receipt, SessionConditionTransferIntent intent)
    {
        Conflict(receipt is not null && Digest(receipt.Intent) == Digest(intent)
            && receipt.Digest == Digest(receipt with { Digest = "" })
            && receipt.RecordedRevision == checked(intent.Review.Canonical.ContentRevision + 1)
            && receipt.PostimageDigest.Length == 64 && receipt.PreviousReceiptDigest.Length == 64 && receipt.EnvironmentDigest.Length == 64,
            "condition-transfer-original-divergence");
    }

    public static void ValidateAcknowledgement(SessionConditionTransferReceipt receipt, SessionConditionTransferIntent intent)
    {
        Require(receipt is not null, "condition-transfer-ack-required");
        ValidateReceipt(receipt.Canonical, intent);
        var after = receipt.ContinuedAuthority;
        Conflict(receipt.Digest == Digest(receipt with { Digest = "" })
            && after.Binding.OwnerId == intent.Review.OwnerId && after.Binding.Version.CharacterId == intent.Review.CharacterId
            && after.Binding.Version.VersionId == receipt.Canonical.RecordedRevision.ToString(CultureInfo.InvariantCulture)
            && after.Binding.Version.RuntimeFingerprint == intent.Review.Canonical.Authority.Binding.Version.RuntimeFingerprint
            && after.Binding.AuthorityDigest == Digest(after with { Binding = after.Binding with { AuthorityDigest = "" } }),
            "condition-transfer-ack-divergence");
        if (intent.Review.Ammunition is { } ammunition)
            Conflict(SessionAmmunitionSettlementRules5712.SameQuantities(after.Ammunition, ammunition.After),
                "ammunition-settlement-ack-changed");
        SessionPlayInitialConditionSource5412.Validate(after);
        Conflict(after.InitialCondition5412!.CanonicalCondition == intent.Review.Condition.Current
            && after.InitialCondition5412.EnvironmentDigest == receipt.Canonical.EnvironmentDigest,
            "condition-transfer-ack-condition-changed");
    }

    public static bool IsValidLedger(CharacterWorkspaceId workspace, long revision, WorkspaceDocumentAuxiliaryState state)
    {
        try
        {
            var list = state.SessionConditionTransfers;
            if (list is { Count: 0 } || list?.Count > MaximumTransfers) return false;
            string previous = Hash("condition-transfer/v1"); long last = 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var receipt in list ?? [])
            {
                ValidateReceipt(receipt, receipt.Intent);
                var review = receipt.Intent.Review;
                ValidateReview(review, review.OwnerId == OwnerScope.LocalSingleUser.NormalizedValue
                    ? OwnerScope.LocalSingleUser : new OwnerScope(review.OwnerId), workspace.Value);
                if (!ids.Add(review.OperationId) || receipt.RecordedRevision <= last || receipt.RecordedRevision > revision
                    || receipt.PreviousReceiptDigest != previous) return false;
                previous = receipt.Digest; last = receipt.RecordedRevision;
            }
            if (state.SessionCondition is { } current)
            {
                ValidateState(current.State);
                if (current.RecordedRevision <= 0 || current.RecordedRevision > revision || string.IsNullOrWhiteSpace(current.OperationId)) return false;
            }
            if (state.SessionPlaySettlement5712 is { } continuation)
            {
                DurableSessionPlayService.ValidateSettlement5712(continuation, continuation.Settlement);
                if (!(list ?? []).Any(x => Digest(x.Intent) == Digest(continuation.Settlement))) return false;
            }
            return list is null || state.SessionCondition is not null;
        }
        catch (Exception e) when (e is SessionPlayException or ArgumentException or InvalidOperationException or OverflowException or NullReferenceException)
        { return false; }
    }
}
