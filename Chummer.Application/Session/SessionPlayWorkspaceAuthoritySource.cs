using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

// One synchronous projection within the original owner's existing lease. The
// store retains ownership of source admission and its lifetime; no archive escapes.
public interface ISessionPlayWorkspaceAuthoritySource
{
    // Explicit current context travels only inside the existing synchronous lease.
    // Older sources keep native behavior and cannot invent an imported context.
    SessionPlayAuthority ReadCurrent5699(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease lease, string characterId,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string?, CharacterCurrentRuleEnvironmentContext?, SessionPlayAuthority> project)
        => Read(owners, expected, lease, characterId, (saved, sources, environment) => project(saved, sources, environment, null));

    SessionConditionTransferBinding ReadContinuationCurrent5699(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease lease, string characterId,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string?, CharacterCurrentRuleEnvironmentContext?, SessionPlayAuthority> project)
        => ReadContinuation(owners, expected, lease, characterId,
            (saved, sources, environment) => project(saved, sources, environment, null));

    SessionConditionTransferBinding ReadContinuation(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease lease, string characterId,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string?, SessionPlayAuthority> project)
        => throw new SessionPlayException(SessionPlayFailures.Unavailable("session-continuation-custody-unavailable"));
    SessionPlayAuthority Read(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected, IOwnerContextLease lease,
        string characterId, Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string?, SessionPlayAuthority> project);
}

public sealed partial class DurableSessionPlayService
{
    public DurableSessionPlayService WithOwnerLease(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected, IOwnerContextLease lease)
        => new(new LeaseBoundJournal5472(store, owners, expected, lease), new LeaseBoundAuthority5262(authority, owners, expected, lease));

    // Original-only reads deliberately skip source resolution, but never skip actual owner/lease validation.
    private sealed class LeaseBoundJournal5472(ISessionPlayStore inner, IOwnerContextLeaseAccessor owners,
        OwnerContextStamp expected, IOwnerContextLease lease) : ISessionPlayStore
    {
        private void Check(OwnerScope owner) => Conflict(expected.IsValid && owner == expected.Owner
            && owners.Capture() == expected && lease.Stamp == expected, "session-owner-lease-changed");
        public T Execute<T>(OwnerScope owner, string characterId, Func<ISessionPlayTransaction, T> action)
        {
            Check(owner);
            return inner.Execute(owner, characterId, tx => { Check(owner); var result = action(tx); Check(owner); return result; });
        }
        public T Inspect<T>(OwnerScope owner, string characterId, Func<ISessionPlayReader?, T> action)
        {
            Check(owner);
            return inner.Inspect(owner, characterId, reader => { Check(owner); var result = action(reader); Check(owner); return result; });
        }
    }

    private sealed class LeaseBoundAuthority5262(ISessionPlayAuthorityResolver inner, IOwnerContextLeaseAccessor owners,
        OwnerContextStamp expected, IOwnerContextLease lease) : ISessionPlayAuthorityResolver
    {
        public SessionFocusCurrent7490 ResolveFocusCurrent7490(OwnerScope owner, string characterId)
        {
            Conflict(expected.IsValid && owner == expected.Owner && owners.Capture() == expected && lease.Stamp == expected,
                "session-owner-lease-changed");
            var value = inner.ResolveFocusCurrentWithOwnerLease7490(owners, expected, lease, characterId);
            Conflict(owners.Capture() == expected && lease.Stamp == expected, "session-owner-lease-changed");
            return value;
        }
        public SessionFocusSourceObservation7490 ObserveFocusSource7490(OwnerScope owner, WorkspaceStoredDocument saved,
            ICharacterSourceDataResolver sources, string? environment, CharacterCurrentRuleEnvironmentContext? context)
        {
            Conflict(expected.IsValid && owner == expected.Owner && owners.Capture() == expected && lease.Stamp == expected,
                "session-owner-lease-changed");
            var value = inner.ObserveFocusSource7490(owner, saved, sources, environment, context);
            Conflict(owners.Capture() == expected && lease.Stamp == expected, "session-owner-lease-changed");
            return value;
        }
        public SessionConditionTransferBinding ResolveContinuation(OwnerScope owner, string characterId)
        {
            Conflict(expected.IsValid && owner == expected.Owner && owners.Capture() == expected && lease.Stamp == expected,
                "session-owner-lease-changed");
            var result = inner.ResolveContinuationWithOwnerLease(owners, expected, lease, characterId);
            Conflict(owners.Capture() == expected && lease.Stamp == expected, "session-owner-lease-changed");
            return result;
        }
        public SessionPlayAuthority Resolve(OwnerScope owner, string characterId)
        {
            void Check() => Conflict(expected.IsValid && owner == expected.Owner && owners.Capture() == expected && lease.Stamp == expected,
                "session-owner-lease-changed");
            Check(); var result = inner.ResolveWithOwnerLease(owners, expected, lease, characterId); Check(); return result;
        }
    }
}
