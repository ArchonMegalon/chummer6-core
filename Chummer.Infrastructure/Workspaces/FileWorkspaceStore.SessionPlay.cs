using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Session;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Session;
using Chummer.Contracts.Workspaces;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Infrastructure.Workspaces;

public sealed class WorkspaceSessionPlaySource(IWorkspaceStore store, ICharacterSourceDataResolver sources) : ISessionPlayWorkspaceAuthoritySource
{
    public SessionPlayAuthority ReadCurrent5699(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease lease, string characterId,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string?, CharacterCurrentRuleEnvironmentContext?, SessionPlayAuthority> project)
        => store is FileWorkspaceStore files ? files.ReadSessionPlayCurrent5699(owners, expected, lease, new(characterId), sources, project)
            : throw new SessionPlayException(SessionPlayFailures.Unavailable("session-current-source-store-unavailable"));

    public SessionConditionTransferBinding ReadContinuationCurrent5699(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease lease, string characterId,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string?, CharacterCurrentRuleEnvironmentContext?, SessionPlayAuthority> project)
        => store is FileWorkspaceStore files ? files.ReadSessionContinuationCurrent5699(owners, expected, lease, new(characterId), sources, project)
            : throw new SessionPlayException(SessionPlayFailures.Unavailable("session-continuation-custody-unavailable"));

    public SessionConditionTransferBinding ReadContinuation(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease lease, string characterId,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string?, SessionPlayAuthority> project)
        => store is FileWorkspaceStore files ? files.ReadSessionContinuation5485(owners, expected, lease, new(characterId), sources, project)
            : throw new SessionPlayException(SessionPlayFailures.Unavailable("session-continuation-custody-unavailable"));
    public SessionPlayAuthority Read(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected, IOwnerContextLease lease,
        string characterId, Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string?, SessionPlayAuthority> project)
        => store is FileWorkspaceStore files ? files.ReadSessionPlay5262(owners, expected, lease, new(characterId), sources, project)
            : throw new SessionPlayException(SessionPlayFailures.Unavailable("session-current-source-store-unavailable"));
}

public sealed partial class FileWorkspaceStore
{
    internal SessionPlayAuthority ReadSessionPlay5262(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease lease, CharacterWorkspaceId id, ICharacterSourceDataResolver unboundSources,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string?, SessionPlayAuthority> project)
        => ReadSessionPlayCurrent5699(owners, expected, lease, id, unboundSources,
            (saved, sources, environment, _) => project(saved, sources, environment));

    internal SessionPlayAuthority ReadSessionPlayCurrent5699(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease lease, CharacterWorkspaceId id, ICharacterSourceDataResolver unboundSources,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string?, CharacterCurrentRuleEnvironmentContext?, SessionPlayAuthority> project)
        => ReadSessionObservation5485(owners, expected, lease, id, unboundSources,
            (saved, sources, environment, _, context) => project(saved, sources, environment, context));

    internal SessionConditionTransferBinding ReadSessionContinuation5485(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease lease, CharacterWorkspaceId id, ICharacterSourceDataResolver unboundSources,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string?, SessionPlayAuthority> project)
        => ReadSessionContinuationCurrent5699(owners, expected, lease, id, unboundSources,
            (saved, sources, environment, _) => project(saved, sources, environment));

    internal SessionConditionTransferBinding ReadSessionContinuationCurrent5699(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease lease, CharacterWorkspaceId id, ICharacterSourceDataResolver unboundSources,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string?, CharacterCurrentRuleEnvironmentContext?, SessionPlayAuthority> project)
        => ReadSessionObservation5485(owners, expected, lease, id, unboundSources, (saved, sources, environment, slot, context) =>
        {
            Conflict(saved.LocalHistory is not null && saved.ContentRevision == saved.SavedRevision
                && !string.IsNullOrWhiteSpace(environment) && !string.IsNullOrWhiteSpace(slot), "session-continuation-custody-unavailable");
            return new SessionConditionTransferBinding(project(saved, sources, environment, context), saved.ContentRevision, saved.SavedRevision,
                saved.LocalHistory!.IncarnationId, slot!, saved.Document.AuxiliaryStateDigest, Digest(expected));
        });

    private TResult ReadSessionObservation5485<TResult>(IOwnerContextLeaseAccessor owners, OwnerContextStamp expected,
        IOwnerContextLease lease, CharacterWorkspaceId id, ICharacterSourceDataResolver unboundSources,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string?, string?, CharacterCurrentRuleEnvironmentContext?, TResult> project)
    {
        void CheckOwner() => SessionPlayEncoding.Conflict(expected.IsValid && owners.Capture() == expected && lease.Stamp == expected, "session-owner-lease-changed");
        CheckOwner();
        string? path = TryGetPath(expected.Owner, id);
        if (path is null) throw new SessionPlayException(SessionPlayFailures.Unavailable("owner-workspace-unavailable"));
        using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
        var record = ReadPrivateRecord(path);
        // Do not repair/migrate a record or replace a missing retained current
        // closure with ambient source bytes at this new mechanical read boundary.
        if (!TryMaterializeRecord(record, out _, out _, out _, out bool migration) || migration
            || record.RetainedRuleSources is { CurrentEnvironment: null })
            throw new SessionPlayException(SessionPlayFailures.Unavailable("session-current-source-admission-unavailable"));
        var read = ReadGearWorkspaceUnderLease(expected.Owner, id, path, owners, lease, expected, out _, out var environment);
        using var current = environment;
        if (!read.Success || read.Value is not { } saved || HasPendingGearEquipment(saved))
            throw new SessionPlayException(SessionPlayFailures.Unavailable("session-current-source-admission-unavailable"));
        var resolver = current is null ? unboundSources : current.Capture.CreateCurrentEnvironmentResolution(
            current.Execution.ExecutionProfile, current.Execution.ExecutionProfileFingerprint)?.Resolver
                ?? throw new SessionPlayException(SessionPlayFailures.Unavailable("session-current-source-resolution-unavailable"));
        string? environmentDigest = current is null ? null : SessionPlayEncoding.Digest(new {
            current.Capture.IntegrityDigest, current.Capture.EffectivePolicyDigest,
            current.Execution.ExecutionProfile, current.Execution.ExecutionProfileFingerprint });
        CharacterCurrentRuleEnvironmentContext? imported = null;
        if (current is not null && CurrentCharacterRuleSourceSemantics.IsImportedPriorityCareerProfile5667(
                current.Execution.ExecutionProfile, current.Execution.ExecutionProfileFingerprint))
        {
            imported = CharacterCurrentRuleEnvironmentAdmission.CreateCurrentEnvironmentContext(saved,
                current.Capture, _retainedCharacterQueries!);
            if (imported is null || imported.SemanticsProfile != current.Execution.ExecutionProfile
                || imported.SemanticsFingerprint != current.Execution.ExecutionProfileFingerprint
                || !imported.VerifyRetainedArchive())
                throw new SessionPlayException(SessionPlayFailures.Unavailable("session-imported-current-context-unavailable"));
        }
        var result = project(saved, resolver, environmentDigest, ReadContinuationSlotUnderLease(path), imported);
        if (imported is not null && !imported.VerifyRetainedArchive())
            throw new SessionPlayException(SessionPlayFailures.Unavailable("session-imported-current-context-changed"));
        current?.ValidateReadObservation(); CheckOwner(); return result;
    }
}
