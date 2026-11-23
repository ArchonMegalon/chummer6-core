using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

public sealed record SessionFocusSourceObservation7490(SessionPlayAuthority Authority, string RuleEnvironmentDigest);
public sealed record SessionFocusCurrent7490(SessionConditionTransferBinding Binding, string SourceEnvironment);

public interface ICharacterCareerFocusPlayCommitCapability7490
{
    CharacterCareerFocusBondingResult CommitFocusWithPlay7490(IOwnerContextLeaseAccessor owners,
        IOwnerContextLease lease, CharacterCareerFocusBondingCommand command,
        ICharacterSourceDataResolver sources, bool retire, SessionFocusCanonicalAdmission7490? admission,
        CancellationToken token);
}

// Constructible only by Application while holding the original journal/owner leases.
public sealed class SessionFocusCanonicalAdmission7490
{
    private Action<SessionConditionTransferBinding, SessionPlayAuthority, string, string, string, CharacterCareerFocusBondingReceipt>? publish;
    private readonly ISessionPlayAuthorityResolver projector;
    private Action? finalCheck;
    public void ValidateFinalObservation() => finalCheck?.Invoke();
    internal SessionFocusCanonicalAdmission7490(ISessionPlayAuthorityResolver projector,
        Action<SessionConditionTransferBinding, SessionPlayAuthority, string, string, string, CharacterCareerFocusBondingReceipt> publish)
        => (this.projector, this.publish) = (projector, publish);

    public void Publish(OwnerContextStamp owner, WorkspaceStoredDocument before, WorkspaceStoredDocument after,
        string slot, CharacterCareerFocusBondingReceipt receipt, ICharacterSourceDataResolver beforeSources,
        string? beforeEnvironment, CharacterCurrentRuleEnvironmentContext? beforeContext,
        ICharacterSourceDataResolver afterSources, string? afterEnvironment,
        CharacterCurrentRuleEnvironmentContext? afterContext)
    {
        var action = Interlocked.Exchange(ref publish, null)
            ?? throw new SessionPlayException(SessionPlayFailures.Conflict("focus-continuation-intent-already-published"));
        var c = receipt.Command;
        Conflict(before.Id == after.Id && before.ContentRevision == c.Binding.WorkspaceRevision
            && before.SavedRevision == before.ContentRevision && after.ContentRevision == receipt.CommittedWorkspaceRevision
            && after.SavedRevision == after.ContentRevision && before.LocalHistory == after.LocalHistory
            && CharacterCareerFocusBondingTransaction.SourceDigest(before.Document) == c.Binding.SourceDigest
            && CharacterCareerFocusBondingTransaction.Hash(after.Document.Content) == receipt.CharacterPayloadDigestAfter
            && CharacterCareerFocusBondingTransaction.SameOwner(owner, c), "focus-continuation-canonical-divergence");
        var a = projector.ObserveFocusSource7490(owner.Owner, before, beforeSources, beforeEnvironment, beforeContext);
        var b = projector.ObserveFocusSource7490(owner.Owner, after, afterSources, afterEnvironment, afterContext);
        Conflict(a.RuleEnvironmentDigest == b.RuleEnvironmentDigest
            && a.Authority.Binding.SourceDigest == Digest(before.Document.State)
            && b.Authority.Binding.SourceDigest == Digest(after.Document.State), "focus-continuation-source-changed");
        var binding = new SessionConditionTransferBinding(a.Authority, before.ContentRevision, before.SavedRevision,
            before.LocalHistory!.IncarnationId, slot, before.Document.AuxiliaryStateDigest, Digest(owner));
        finalCheck = () => {
            var liveBefore = projector.ObserveFocusSource7490(owner.Owner, before, beforeSources, beforeEnvironment, beforeContext);
            var liveAfter = projector.ObserveFocusSource7490(owner.Owner, after, afterSources, afterEnvironment, afterContext);
            Conflict(Digest(liveBefore) == Digest(a) && Digest(liveAfter) == Digest(b), "focus-continuation-source-changed");
        };
        action(binding, b.Authority, after.Document.AuxiliaryStateDigest,
            afterEnvironment ?? b.RuleEnvironmentDigest, a.RuleEnvironmentDigest, receipt);
    }
}
