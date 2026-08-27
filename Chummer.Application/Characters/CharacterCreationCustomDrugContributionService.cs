using System.Security.Cryptography;
using System.Text;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterCreationCustomDrugContributionService(
    IWorkspaceStore store,
    ICharacterCustomDrugAuthority authority)
    : ICharacterCreationCustomDrugContributionService
{
    private const int MaximumIdempotencyKeyLength = 200;

    public CharacterCreationCustomDrugResult Load(CharacterCreationCustomDrugLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceStoreReadResult read = store.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return Blocked(
                CharacterCreationCustomDrugOutcomes.NotFound,
                CharacterCreationCustomDrugBlockers.WorkspaceUnavailable);
        CharacterCreationCustomDrugFinalizationContribution? contribution = workspace.Document
            .AuxiliaryState.CharacterCreationCustomDrugContribution;
        if (contribution is null)
            return Blocked(CharacterCreationCustomDrugOutcomes.NotFound);
        return CharacterCreationCustomDrugContributionRules.IsValid(
            contribution,
            workspace.Id,
            workspace.ContentRevision)
            ? new CharacterCreationCustomDrugResult(
                CharacterCreationCustomDrugOutcomes.Available,
                contribution,
                [])
            : Blocked(
                CharacterCreationCustomDrugOutcomes.Blocked,
                CharacterCreationCustomDrugBlockers.ProjectionRejected);
    }

    public CharacterCreationCustomDrugResult Queue(CharacterCreationCustomDrugQueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ExplicitlyConfirmed)
            return Blocked(
                CharacterCreationCustomDrugOutcomes.Invalid,
                CharacterCreationCustomDrugBlockers.ExplicitConfirmationRequired);
        if (!IsValidIdempotencyKey(request.IdempotencyKey)
            || request.VerificationCommand is null)
            return Blocked(
                CharacterCreationCustomDrugOutcomes.Invalid,
                CharacterCreationCustomDrugBlockers.IdempotencyKeyInvalid);

        string idempotencyDigest = CharacterCreationCustomDrugContributionRules
            .ComputeRequestIdempotencyKeyDigest(request.IdempotencyKey);
        string requestCommandDigest = CharacterCreationCustomDrugContributionRules
            .ComputeRequestCommandDigest(request);
        WorkspaceStoreReadResult read = store.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return Blocked(
                CharacterCreationCustomDrugOutcomes.NotFound,
                CharacterCreationCustomDrugBlockers.WorkspaceUnavailable);
        CharacterCreationCustomDrugResult? replay = ReplayOrConflict(
            workspace.Document.AuxiliaryState.CharacterCreationCustomDrugContribution,
            workspace.Id,
            workspace.ContentRevision,
            idempotencyDigest,
            requestCommandDigest);
        if (replay is not null)
            return replay;

        CharacterCustomDrugCommitCommand requested = request.VerificationCommand;
        if (workspace.ContentRevision != request.ExpectedContentRevision
            || workspace.SavedRevision != request.ExpectedSavedRevision
            || requested.ExpectedContentRevision != request.ExpectedContentRevision)
            return Blocked(
                CharacterCreationCustomDrugOutcomes.Conflict,
                CharacterCreationCustomDrugBlockers.StaleWorkspaceRevision);
        if (!string.Equals(
                workspace.Document.AuxiliaryStateDigest,
                request.ExpectedAuxiliaryStateDigest,
                StringComparison.Ordinal))
            return Blocked(
                CharacterCreationCustomDrugOutcomes.Conflict,
                CharacterCreationCustomDrugBlockers.StaleAuxiliaryStateDigest);
        string rawDigest = CharacterCustomDrugRules.ComputeCharacterDigest(
            workspace.Document.Content);
        if (!FixedEquals(rawDigest, requested.ExpectedCharacterDigest))
            return Blocked(
                CharacterCreationCustomDrugOutcomes.Conflict,
                CharacterCreationCustomDrugBlockers.StaleCharacterDigest);

        CharacterCustomDrugPreparation preparation = authority.Prepare(
            workspace.Document.Content,
            workspace.ContentRevision,
            CharacterCustomDrugContext.Creation);
        if (!preparation.Exact)
            return Blocked(
                CharacterCreationCustomDrugOutcomes.Blocked,
                preparation.Blockers.FirstOrDefault()
                ?? CharacterCustomDrugBlockers.AuthorityUnavailable);
        if (!FixedEquals(preparation.CharacterDigest, requested.ExpectedCharacterDigest))
            return Blocked(
                CharacterCreationCustomDrugOutcomes.Conflict,
                CharacterCreationCustomDrugBlockers.StaleCharacterDigest);
        if (!FixedEquals(preparation.CatalogDigest, requested.ExpectedCatalogDigest))
            return Blocked(
                CharacterCreationCustomDrugOutcomes.Conflict,
                CharacterCreationCustomDrugBlockers.StaleCatalogDigest);
        if (!FixedEquals(preparation.RulesDigest, requested.ExpectedRulesDigest))
            return Blocked(
                CharacterCreationCustomDrugOutcomes.Conflict,
                CharacterCreationCustomDrugBlockers.StaleRulesDigest);
        CharacterCustomDrugQuote reviewed = authority.Quote(preparation, requested.Selection);
        if (!reviewed.Exact)
            return Blocked(CharacterCreationCustomDrugOutcomes.Blocked, reviewed.BlockReason);
        if (!FixedEquals(reviewed.QuoteDigest, requested.ExpectedQuoteDigest))
            return Blocked(
                CharacterCreationCustomDrugOutcomes.Conflict,
                CharacterCreationCustomDrugBlockers.StaleQuoteDigest);
        CharacterCustomDrugCreationProjection currentProjection = authority.ProjectCreation(
            workspace.Document.Content,
            workspace.ContentRevision,
            requested);
        if (!currentProjection.Exact
            || !FixedEquals(currentProjection.QuoteDigest, reviewed.QuoteDigest))
            return Blocked(
                CharacterCreationCustomDrugOutcomes.Blocked,
                currentProjection.BlockReason,
                CharacterCreationCustomDrugBlockers.ProjectionRejected);

        if (store is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            { SupportsWorkspaceAuxiliaryStateAtomicCommit: true } atomic
            || workspace.ContentRevision == long.MaxValue)
            return Blocked(
                CharacterCreationCustomDrugOutcomes.Unavailable,
                CharacterCreationCustomDrugBlockers.PersistenceAuthorityRequired);

        long nextRevision = checked(workspace.ContentRevision + 1);
        CharacterCustomDrugPreparation nextPreparation = preparation with
        {
            ContentRevision = nextRevision
        };
        CharacterCustomDrugQuote nextQuote = authority.Quote(
            nextPreparation,
            requested.Selection);
        if (!nextQuote.Exact)
            return Blocked(CharacterCreationCustomDrugOutcomes.Blocked, nextQuote.BlockReason);
        var nextCommand = new CharacterCustomDrugCommitCommand(
            nextRevision,
            preparation.CharacterDigest,
            preparation.CatalogDigest,
            preparation.RulesDigest,
            nextQuote.QuoteDigest,
            requested.IdempotencyKey,
            requested.Selection,
            requested.NewDrugInstanceId,
            requested.NewComponentInstanceIds.ToArray());
        CharacterCustomDrugCreationProjection nextProjection = authority.ProjectCreation(
            workspace.Document.Content,
            nextRevision,
            nextCommand);
        if (!nextProjection.Exact
            || !FixedEquals(nextProjection.QuoteDigest, nextQuote.QuoteDigest))
            return Blocked(
                CharacterCreationCustomDrugOutcomes.Blocked,
                nextProjection.BlockReason,
                CharacterCreationCustomDrugBlockers.ProjectionRejected);

        var unsigned = new CharacterCreationCustomDrugFinalizationContribution(
            CharacterCreationCustomDrugSchemas.ContributionV1,
            workspace.Id,
            nextRevision,
            preparation.CharacterDigest,
            preparation.CatalogDigest,
            preparation.RulesDigest,
            requested.Selection,
            nextQuote,
            requested.NewDrugInstanceId,
            requested.NewComponentInstanceIds.ToArray(),
            nextProjection.DrugXml,
            nextProjection.DrugXmlDigest,
            idempotencyDigest,
            requestCommandDigest,
            ContributionDigest: string.Empty);
        CharacterCreationCustomDrugFinalizationContribution contribution = unsigned with
        {
            ContributionDigest = CharacterCreationCustomDrugContributionRules
                .ComputeContributionDigest(unsigned)
        };
        if (!CharacterCreationCustomDrugContributionRules.IsValid(
                contribution,
                workspace.Id,
                nextRevision))
            return Blocked(
                CharacterCreationCustomDrugOutcomes.Blocked,
                CharacterCreationCustomDrugBlockers.ProjectionRejected);

        WorkspaceDocument replacement = workspace.Document with
        {
            State = workspace.Document.State with
            {
                AuxiliaryState = workspace.Document.AuxiliaryState with
                {
                    CharacterCreationCustomDrugContribution = contribution
                }
            }
        };
        WorkspaceStoreMutationResult committed = atomic
            .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                workspace.Id,
                workspace.ContentRevision,
                workspace.Document.AuxiliaryStateDigest,
                replacement);
        if (!committed.Success)
        {
            WorkspaceStoreReadResult racedRead = store.Get(workspace.Id);
            CharacterCreationCustomDrugResult? raced = racedRead.Value is { } racedWorkspace
                ? ReplayOrConflict(
                    racedWorkspace.Document.AuxiliaryState
                        .CharacterCreationCustomDrugContribution,
                    racedWorkspace.Id,
                    racedWorkspace.ContentRevision,
                    idempotencyDigest,
                    requestCommandDigest)
                : null;
            if (raced is not null)
                return raced;
            return Blocked(
                committed.Outcome == WorkspaceOperationOutcome.Conflict
                    ? CharacterCreationCustomDrugOutcomes.Conflict
                    : CharacterCreationCustomDrugOutcomes.Unavailable,
                committed.Outcome == WorkspaceOperationOutcome.Conflict
                    ? CharacterCreationCustomDrugBlockers.StaleWorkspaceRevision
                    : CharacterCreationCustomDrugBlockers.PersistenceAuthorityRequired);
        }

        WorkspaceStoreReadResult reopened = store.Get(workspace.Id);
        CharacterCreationCustomDrugFinalizationContribution? persisted = reopened.Value?.Document
            .AuxiliaryState.CharacterCreationCustomDrugContribution;
        return CharacterCreationCustomDrugContributionRules.IsValid(
                   persisted,
                   workspace.Id,
                   nextRevision)
               && persisted is not null
               && CharacterCreationFinalizationDigest.EqualsFixedTime(
                   persisted.ContributionDigest,
                   contribution.ContributionDigest)
            ? new CharacterCreationCustomDrugResult(
                CharacterCreationCustomDrugOutcomes.Applied,
                persisted,
                [])
            : Blocked(
                CharacterCreationCustomDrugOutcomes.Unavailable,
                CharacterCreationCustomDrugBlockers.PersistenceAuthorityRequired);
    }

    private static CharacterCreationCustomDrugResult? ReplayOrConflict(
        CharacterCreationCustomDrugFinalizationContribution? existing,
        CharacterWorkspaceId workspaceId,
        long currentContentRevision,
        string idempotencyDigest,
        string commandDigest)
    {
        if (existing is null
            || !CharacterCreationCustomDrugContributionRules.IsValid(
                existing,
                workspaceId,
                currentContentRevision)
            || !CharacterCreationFinalizationDigest.EqualsFixedTime(
                existing.RequestIdempotencyKeyDigest,
                idempotencyDigest))
            return null;
        return CharacterCreationFinalizationDigest.EqualsFixedTime(
            existing.RequestCommandDigest,
            commandDigest)
            ? new CharacterCreationCustomDrugResult(
                CharacterCreationCustomDrugOutcomes.Replayed,
                existing,
                [])
            : Blocked(
                CharacterCreationCustomDrugOutcomes.Conflict,
                CharacterCreationCustomDrugBlockers.IdempotencyConflict);
    }

    private static bool IsValidIdempotencyKey(string? value) => value is not null
        && value.Length is >= 8 and <= MaximumIdempotencyKeyLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && value.All(static character => !char.IsControl(character));

    private static bool FixedEquals(string? left, string? right)
    {
        byte[] first = Encoding.ASCII.GetBytes(left ?? string.Empty);
        byte[] second = Encoding.ASCII.GetBytes(right ?? string.Empty);
        return first.Length == second.Length
               && CryptographicOperations.FixedTimeEquals(first, second);
    }

    private static CharacterCreationCustomDrugResult Blocked(
        string outcome,
        params string?[] blockers) => new(
        outcome,
        null,
        blockers.Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
            .Select(static blocker => blocker!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static blocker => blocker, StringComparer.Ordinal)
            .ToArray());
}
