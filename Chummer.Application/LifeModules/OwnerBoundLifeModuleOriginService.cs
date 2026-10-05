using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.LifeModules;

public interface IOwnerBoundLifeModuleOriginService
{
    bool IsCurrent(OwnerContextStamp owner);
    LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Start(OwnerContextStamp owner, string workspaceId);
    LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Restore(OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint);
    LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> AdoptLocalCheckpoint(
        OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint)
        => new(LifeModuleOriginDossierOutcomes.Blocked, null, [LifeModuleOriginDossierBlockers.AuthorityInvalid]);
    LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Prepare(OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint,
        string choiceId, IReadOnlyDictionary<string, string>? followUpValues = null);
    LifeModuleOriginDossierResult<LifeModuleOriginDossierInteractionAdvance> Confirm(OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint,
        string previewDigest, string idempotencyKey, bool explicitlyConfirmed);
}

/// <summary>Read-only capability, separate from Origin decisions and confirmation.</summary>
public interface IOwnerBoundLifeModuleAvailabilityService
{
    LifeModuleOriginDossierResult<LifeModuleDecisionAvailabilitySnapshot> LoadAvailability(
        OwnerContextStamp owner, LifeModuleDecisionAvailabilityRequest request);
}

/// <summary>
/// Admits each synchronous Origin operation for one exact owner transition and
/// workspace. No lease, evaluator, or mutable owner context escapes the call.
/// </summary>
public sealed class OwnerBoundLifeModuleOriginService(
    IWorkspaceStore store, IOwnerContextAccessor owners, ICharacterFileQueries characterFiles,
    ICharacterSourceDataResolver sourceResolver, ILifeModulesCatalogService catalog)
    : IOwnerBoundLifeModuleOriginService, IOwnerBoundLifeModuleAvailabilityService
{
    public LifeModuleOriginDossierResult<LifeModuleDecisionAvailabilitySnapshot> LoadAvailability(
        OwnerContextStamp owner, LifeModuleDecisionAvailabilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return InvokeAuthority(owner, request.WorkspaceId, (authority, _) =>
        {
            var result = authority.LoadAvailability(request);
            return new LifeModuleOriginDossierResult<LifeModuleDecisionAvailabilitySnapshot>(
                result.Outcome, result.Value, result.Blockers);
        });
    }

    public bool IsCurrent(OwnerContextStamp owner)
    {
        if (!OwnerContextAdmission.TryAcquire(owners, owner, out var lease)) return false;
        using (lease) return !owner.Owner.UsesLocalSingleUserValue || owner.Owner.IsLocalSingleUser;
    }

    public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Start(
        OwnerContextStamp owner, string workspaceId)
    {
        var result = Invoke(owner, workspaceId, service => service.Start(workspaceId));
        return result.Value is { } checkpoint && !MatchesOwner(owner, checkpoint)
            ? Denied<LifeModuleOriginDossierDraftCheckpoint>() : result;
    }

    public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Restore(
        OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint)
        => MatchesOwner(owner, checkpoint)
            ? Invoke(owner, checkpoint.WorkspaceId, service => service.Restore(checkpoint))
            : Denied<LifeModuleOriginDossierDraftCheckpoint>();

    public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> AdoptLocalCheckpoint(
        OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (owner.Owner.UsesLocalSingleUserValue || checkpoint.OwnerId != OwnerScope.LocalSingleUser.NormalizedValue)
            return Denied<LifeModuleOriginDossierDraftCheckpoint>();
        return InvokeAuthority(owner, checkpoint.WorkspaceId, (authority, narrativeOwnerId) =>
            narrativeOwnerId == OwnerScope.LocalSingleUser.NormalizedValue
                ? new LifeModuleOriginDossierInteractionService(new(authority), owner.Owner.NormalizedValue,
                    narrativeOwnerId).AdoptLocalCheckpoint(checkpoint)
                : Denied<LifeModuleOriginDossierDraftCheckpoint>());
    }

    public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Prepare(
        OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint,
        string choiceId, IReadOnlyDictionary<string, string>? followUpValues = null)
        => MatchesOwner(owner, checkpoint)
            ? Invoke(owner, checkpoint.WorkspaceId, service => service.Prepare(checkpoint, choiceId, followUpValues))
            : Denied<LifeModuleOriginDossierDraftCheckpoint>();

    public LifeModuleOriginDossierResult<LifeModuleOriginDossierInteractionAdvance> Confirm(
        OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint,
        string previewDigest, string idempotencyKey, bool explicitlyConfirmed)
        => MatchesOwner(owner, checkpoint)
            ? Invoke(owner, checkpoint.WorkspaceId, service => service.Confirm(
                checkpoint, previewDigest, idempotencyKey, explicitlyConfirmed))
            : Denied<LifeModuleOriginDossierInteractionAdvance>();

    private static bool MatchesOwner(OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint)
        => checkpoint.OwnerId == owner.Owner.NormalizedValue;

    private LifeModuleOriginDossierResult<T> Invoke<T>(OwnerContextStamp owner, string workspaceId,
        Func<LifeModuleOriginDossierInteractionService, LifeModuleOriginDossierResult<T>> action) where T : class
        => InvokeAuthority(owner, workspaceId, (authority, narrativeOwnerId) =>
            action(new(new(authority), owner.Owner.NormalizedValue, narrativeOwnerId)));

    private LifeModuleOriginDossierResult<T> InvokeAuthority<T>(OwnerContextStamp owner, string workspaceId,
        Func<CharacterCreationFoundationLifeModuleDecisionAuthority, string, LifeModuleOriginDossierResult<T>> action) where T : class
    {
        if (string.IsNullOrWhiteSpace(workspaceId)
            || (owner.Owner.UsesLocalSingleUserValue && !owner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(owners, owner, out var lease))
            return Denied<T>();
        using (lease)
        {
            using ICharacterSourceDataResolverOperationScope? sourceScope =
                (sourceResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, owner, new CharacterWorkspaceId(workspaceId));
            if (!TryGetNarrativeOwner(view, owner, new CharacterWorkspaceId(workspaceId), out string narrativeOwnerId))
                return Denied<T>();
            var foundation = new CharacterCreationFoundationService(view, characterFiles,
                sourceScope ?? sourceResolver, catalog, new CharacterCreationFoundationDraftApplyAuthority(view));
            var authority = new CharacterCreationFoundationLifeModuleDecisionAuthority(
                view, foundation, characterFiles, narrativeOwnerId);
            return action(authority, narrativeOwnerId);
        }
    }

    private static bool TryGetNarrativeOwner(OwnerBoundCreationWorkspaceStore view,
        OwnerContextStamp owner, CharacterWorkspaceId id, out string narrativeOwnerId)
    {
        narrativeOwnerId = owner.Owner.NormalizedValue;
        // Access is already scoped to the admitted account and exact workspace.
        // Caller-supplied checkpoint fields never establish a handoff.
        if (view.Get(id).Value is not { } workspace) return true;
        if (!CharacterCreationFinalizationReceiptLedgerIntegrity.TryReadReceiptHistory(
                workspace, out var history, out long historyRevision)) return false;
        if (history.LifeModuleDecisionAcceptances is not { Count: > 0 } ledger)
        {
            // Before the first acceptance the identity still participates in
            // the initial turn/checkpoint digest. Preserve it only for a real
            // store-issued local claim, never from a supplied checkpoint.
            if (!owner.Owner.UsesLocalSingleUserValue
                && workspace.LocalHistory is { LocalAdoption: { } claim } initial
                && initial.IsValid(workspace.ContentRevision)
                && claim.OwnerId == owner.Owner.NormalizedValue && claim.WorkspaceId == id
                && claim.IncarnationId == initial.IncarnationId)
                narrativeOwnerId = OwnerScope.LocalSingleUser.NormalizedValue;
            return true;
        }
        if (!LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(id, historyRevision, ledger)) return false;
        string originOwnerId = ledger[^1].NextStep.OwnerId;
        if (originOwnerId != owner.Owner.NormalizedValue)
        {
            if (originOwnerId != OwnerScope.LocalSingleUser.NormalizedValue
                || owner.Owner.UsesLocalSingleUserValue
                || workspace.LocalHistory is not { } local
                || !local.IsValid(workspace.ContentRevision))
                return false;
            bool claimedHere = local.LocalAdoption is { } receipt
                && receipt.OwnerId == owner.Owner.NormalizedValue && receipt.WorkspaceId == id
                && receipt.IncarnationId == local.IncarnationId;
            // An admitted same-account continuation may carry local narrative
            // lineage to another device. Only that store's own restore receipt
            // establishes custody; portable bytes never import LocalAdoption.
            // CanReplayReceipt still excludes every imported decision revision.
            bool restoredHere = local is { ImportedThroughRevision: > 0, LastRestore: not null };
            if (!claimedHere && !restoredHere) return false;
        }
        narrativeOwnerId = originOwnerId;
        return true;
    }

    private static LifeModuleOriginDossierResult<T> Denied<T>() where T : class
        => new(LifeModuleOriginDossierOutcomes.Blocked, null, [LifeModuleOriginDossierBlockers.AuthorityInvalid]);
}
