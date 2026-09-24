using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.LifeModules;

public sealed class OwnerBoundLifeModuleBookService(IWorkspaceStore store, IOwnerContextAccessor owners)
    : IOwnerBoundLifeModuleBookService
{
    public LifeModuleOriginDossierResult<OriginStoryArcSeed> Load(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId workspaceId, long contentRevision, long savedRevision)
    {
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(owners, expectedOwner, out var lease))
            return Blocked(LifeModuleOriginDossierBlockers.AuthorityInvalid);
        using (lease)
        {
            // One owner-scoped store observation supplies both the current
            // revision and historical ledger. No ambient/local fallback for a
            // linked owner, and no sidecar checkpoint is trusted as book truth.
            var read = expectedOwner.Owner.IsLocalSingleUser
                ? store.Get(workspaceId) : store.Get(expectedOwner.Owner, workspaceId);
            if (!read.Success || read.Value is not { } workspace)
                return Blocked(LifeModuleOriginDossierBlockers.AuthorityInvalid);
            if (workspace.Id != workspaceId || workspace.ContentRevision != contentRevision
                || workspace.SavedRevision != savedRevision)
                return Blocked(LifeModuleOriginDossierBlockers.WorkspaceStale);
            if (!CharacterCreationFinalizationReceiptLedgerIntegrity.TryReadReceiptHistory(
                    workspace, out var history, out long historyRevision))
                return Blocked(LifeModuleOriginDossierBlockers.AuthorityInvalid);
            if (history.LifeModuleDecisionAcceptances is not { Count: > 0 } ledger)
                return new(LifeModuleOriginDossierOutcomes.Missing, null, ["life-module-origin-history-unavailable"]);
            if (!LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(workspaceId, historyRevision, ledger)
                || workspace.Document.AuxiliaryState.CharacterCreationFinalizationArchive is not null
                    && !ledger[^1].NextStep.IsTerminal)
                return Blocked(LifeModuleOriginDossierBlockers.AuthorityInvalid);

            // Project validates the retained chapter bytes and decision chain.
            // Its adapter has no writer, foundation evaluator or provider.
            return new LifeModuleOriginDossierService(new BookHistory(workspaceId.Value, ledger))
                .Project(workspaceId.Value);
        }
    }

    private static LifeModuleOriginDossierResult<OriginStoryArcSeed> Blocked(string blocker)
        => new(LifeModuleOriginDossierOutcomes.Blocked, null, [blocker]);

    private sealed class BookHistory(string workspaceId, IReadOnlyList<LifeModuleDecisionAcceptance> ledger)
        : ILifeModuleDecisionAuthority, ILifeModuleDecisionHistoryAuthority
    {
        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAuthorityStep> Load(string id)
            => id == workspaceId ? new(LifeModuleOriginDossierOutcomes.Success, ledger[^1].NextStep, [])
                : Denied<LifeModuleDecisionAuthorityStep>();
        public LifeModuleDecisionAuthorityResult<IReadOnlyList<LifeModuleDecisionAcceptance>> LoadHistory(string id)
            => id == workspaceId ? new(LifeModuleOriginDossierOutcomes.Success, ledger, [])
                : Denied<IReadOnlyList<LifeModuleDecisionAcceptance>>();
        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> FindAcceptance(string id, string key)
            => Denied<LifeModuleDecisionAcceptance>();
        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> Accept(LifeModuleDecisionAcceptanceCommand command)
            => Denied<LifeModuleDecisionAcceptance>();
        private static LifeModuleDecisionAuthorityResult<T> Denied<T>() where T : class
            => new(LifeModuleOriginDossierOutcomes.Blocked, null, [LifeModuleOriginDossierBlockers.AuthorityInvalid]);
    }
}
