using Chummer.Application.Workspaces;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Tx = Chummer.Application.Characters.CharacterSr5NaturalRecoveryTransaction;

namespace Chummer.Application.Characters;

public sealed class CharacterSr5NaturalRecoveryService(IWorkspaceStore store, ICharacterSourceDataResolver sources,
    IOwnerContextAccessor? ownerContext)
    : ICharacterSr5NaturalRecoveryService
{
    private readonly IOwnerContextLeaseAccessor? owners = ownerContext as IOwnerContextLeaseAccessor;
    public CharacterSr5NaturalRecoveryService(IWorkspaceStore store, ICharacterSourceDataResolver sources)
        : this(store, sources, null) { }

    private IOwnerContextLease Acquire(OwnerScope owner)
    {
        var stamp = owners!.Capture();
        if (!stamp.IsValid || stamp.Owner != owner || !owners.TryAcquire(stamp, out var lease))
            throw new InvalidOperationException("Recovery owner changed.");
        if (lease.Stamp == stamp && owners.Capture() == stamp) return lease;
        lease.Dispose(); throw new InvalidOperationException("Recovery owner changed.");
    }
    private ICharacterSr5NaturalRecoveryCurrentEnvironmentStore CurrentStore() =>
        store is ICharacterSr5NaturalRecoveryCurrentEnvironmentStore { SupportsRetainedNaturalRecovery: true } current
            ? current : throw new InvalidOperationException("Retained recovery persistence is unavailable.");

    private TResult Observe<TResult>(OwnerScope owner, CharacterWorkspaceId id,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string, string?, TResult> project)
    {
        if (owners is not null)
        {
            using var lease = Acquire(owner);
            return CurrentStore().ReadNaturalRecovery(owners, lease, id, sources, project);
        }
        var saved = Saved(owner, id) ?? throw new InvalidOperationException("Recovery workspace is unavailable.");
        using var scope = (sources as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        return project(saved, scope ?? sources,
            (store as ICharacterSr5NaturalRecoveryStore)?.NaturalRecoveryRuntimeDigest ?? "", null);
    }
    private WorkspaceStoredDocument? Saved(OwnerScope owner, CharacterWorkspaceId id)
    {
        if (!Tx.ValidOwner(owner) || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(id)) return null;
        var result = owner.IsLocalSingleUser ? store.Get(id) : store.Get(owner, id);
        return result.Success && result.Value?.Id == id && Tx.IsValidHistory(result.Value) ? result.Value : null;
    }
    public Sr5RecoveryScreen? Read(OwnerScope owner, CharacterWorkspaceId workspace)
    {
        try
        {
            return Observe(owner, workspace, (saved, resolver, runtime, binding) =>
            {
                var ledger = saved.Document.AuxiliaryState.Sr5NaturalRecovery;
                Sr5RecoveryScreen Screen(string? reason, IReadOnlyList<Sr5RecoveryTrack>? tracks = null,
                    IReadOnlyList<Sr5RecoveryCalendarWindow>? windows = null) => new(workspace, saved.ContentRevision,
                        saved.Document.AuxiliaryStateDigest, Tx.LedgerDigest(ledger), tracks ?? [], windows ?? [], Tx.Activities(ledger), reason);
                if (store is not ICharacterSr5NaturalRecoveryStore { SupportsNaturalRecovery: true } atomic)
                    return Screen("Natural recovery persistence is unavailable in this build.");
                try
                {
                    var authority = Tx.ReadAuthority(saved, resolver);
                    var windows = Tx.Windows(authority.Calendar);
                    var tracks = Enum.GetValues<CharacterSr5HealingTrack>().Select(track =>
                    {
                        var quote = Tx.Quote(saved, resolver, runtime, Guid.NewGuid(), track, windows[0].Start);
                        return new Sr5RecoveryTrack(track, quote.DamageBoxesBefore,
                            track == CharacterSr5HealingTrack.Stun ? authority.StunCapacity : authority.PhysicalCapacity, quote);
                    }).ToArray();
                    return Screen(null, tracks, windows);
                }
                catch (Exception ex) when (Expected(ex))
                { return Screen("Saved Physical/Stun tracks, an exact supported attribute/modifier source, and a saved calendar week are required. Matrix/Core tracks and overflow recovery are unavailable here."); }
            });
        }
        catch (Exception ex) when (Expected(ex)) { return null; }
    }
    public Sr5RecoveryResult Preview(OwnerScope owner, Sr5RecoveryRequest request)
    {
        try
        {
            if (!Tx.ValidRequest(request) || store is not ICharacterSr5NaturalRecoveryStore { SupportsNaturalRecovery: true } atomic)
                return new(Sr5RecoveryOutcome.Unavailable);
            return Observe(owner, request.WorkspaceId, (saved, resolver, runtime, binding) =>
                new Sr5RecoveryResult(Sr5RecoveryOutcome.Available, Review: Tx.Review(owner, saved, request, resolver, runtime, binding)));
        }
        catch (Exception ex) when (Expected(ex))
        { return new(Sr5RecoveryOutcome.Conflict, Reason: "The runner, rules, calendar or recovery prerequisites changed. Reload before reviewing again."); }
    }
    public Sr5RecoveryResult Recover(OwnerScope owner, CharacterWorkspaceId workspace, Guid operationId, string digest)
    {
        try
        {
            if (owners is not null)
            {
                using var lease = Acquire(owner);
                return CurrentStore().LookupNaturalRecovery(owners, lease, workspace, operationId, digest);
            }
            var saved = Saved(owner, workspace); return saved is null ? new(Sr5RecoveryOutcome.Unknown) : Tx.Lookup(owner, saved, operationId, digest);
        }
        catch (Exception ex) when (Expected(ex)) { return new(Sr5RecoveryOutcome.Unknown); }
    }
    public Sr5RecoveryResult Execute(OwnerScope owner, Sr5RecoveryCommand command, CancellationToken token = default)
    {
        if (!Tx.ValidCommand(command) || command.OwnerId != owner.NormalizedValue) return new(Sr5RecoveryOutcome.Conflict);
        if (owners is not null)
        {
            try
            {
                // One owner lease spans original lookup, the only dispatch and readback.
                using var lease = Acquire(owner);
                var current = CurrentStore();
                string digest = Tx.Digest(command);
                var prior = current.LookupNaturalRecovery(owners, lease, command.Request.WorkspaceId, command.Request.OperationId, digest);
                if (prior.Outcome != Sr5RecoveryOutcome.NotFound) return prior;
                Sr5RecoveryResult? leasedResult = null;
                try { leasedResult = current.ExecuteNaturalRecovery(owners, lease, command, sources, token); }
                catch (Exception ex) when (Expected(ex)) { }
                var leasedObserved = current.LookupNaturalRecovery(owners, lease, command.Request.WorkspaceId, command.Request.OperationId, digest);
                if (leasedObserved.Outcome == Sr5RecoveryOutcome.Recovered)
                    return leasedObserved with { Outcome = leasedResult?.Outcome == Sr5RecoveryOutcome.Applied ? Sr5RecoveryOutcome.Applied : Sr5RecoveryOutcome.Recovered };
                return leasedObserved.Outcome == Sr5RecoveryOutcome.NotFound && leasedResult is { Outcome: Sr5RecoveryOutcome.Conflict or Sr5RecoveryOutcome.Unavailable }
                    ? leasedResult : leasedObserved;
            }
            catch (Exception ex) when (Expected(ex)) { return new(Sr5RecoveryOutcome.Unknown); }
        }
        var original = Recover(owner, command.Request.WorkspaceId, command.Request.OperationId, Tx.Digest(command));
        if (original.Outcome != Sr5RecoveryOutcome.NotFound) return original;
        if (store is not ICharacterSr5NaturalRecoveryStore { SupportsNaturalRecovery: true } atomic)
            return new(Sr5RecoveryOutcome.Unavailable);
        Sr5RecoveryResult? result = null;
        try { result = atomic.ExecuteNaturalRecovery(owner, command, sources, token); }
        catch (Exception ex) when (Expected(ex)) { }
        var observed = Recover(owner, command.Request.WorkspaceId, command.Request.OperationId, Tx.Digest(command));
        if (observed.Outcome == Sr5RecoveryOutcome.Recovered)
            return observed with { Outcome = result?.Outcome == Sr5RecoveryOutcome.Applied ? Sr5RecoveryOutcome.Applied : Sr5RecoveryOutcome.Recovered };
        return observed.Outcome == Sr5RecoveryOutcome.NotFound && result is { Outcome: Sr5RecoveryOutcome.Conflict or Sr5RecoveryOutcome.Unavailable }
            ? result : observed;
    }
    internal static bool Expected(Exception e) => e is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException
        or ArgumentException or FormatException or OverflowException or System.Xml.XmlException or OperationCanceledException;
}
