using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterAfterRunSettlementService :
    ICharacterAfterRunSettlementService
{
    private readonly ICharacterAfterRunSettlementWorkspace _workspace;

    public CharacterAfterRunSettlementService(
        ICharacterAfterRunSettlementWorkspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    }

    public CharacterAfterRunSettlementQuoteResult Quote(
        CharacterAfterRunSettlementQuoteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidQuoteRequest(request))
        {
            return QuoteFailure(
                CharacterAfterRunSettlementServiceOutcome.Invalid,
                "invalid_quote_request");
        }

        CharacterAfterRunSettlementWorkspaceReadResult read = _workspace.Read(
            request.WorkspaceId,
            request.Identity);
        if (read.Outcome != CharacterAfterRunSettlementWorkspaceOutcome.Available
            || read.CurrentWorkspaceRevision <= 0
            || read.Input is null)
        {
            return QuoteFailure(MapReadOutcome(read.Outcome), ReadBlocker(read));
        }

        if (read.Input.Identity != request.Identity
            || !CharacterAfterRunSettlementRules.TryCreateQuote(
                read.Input,
                out CharacterAfterRunSettlementQuote quote)
            || !CharacterAfterRunSettlementServiceIntegrity.TryComputeBindingDigest(
                request.WorkspaceId,
                read.CurrentWorkspaceRevision,
                quote,
                out string bindingDigest))
        {
            return QuoteFailure(
                CharacterAfterRunSettlementServiceOutcome.Corrupt,
                "authoritative_projection_invalid");
        }

        return new CharacterAfterRunSettlementQuoteResult(
            CharacterAfterRunSettlementServiceOutcome.Available,
            new CharacterAfterRunSettlementQuoteBinding(
                CharacterAfterRunSettlementServiceSchemas.QuoteV1,
                request.WorkspaceId,
                read.CurrentWorkspaceRevision,
                request.Identity,
                quote,
                bindingDigest),
            quote.CanSettle ? [] : [quote.Blocker.ToString()]);
    }

    public CharacterAfterRunSettlementResult Settle(
        CharacterAfterRunSettlementCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!CharacterAfterRunSettlementServiceIntegrity.TryComputeCommandDigest(
                command,
                out string commandDigest))
        {
            return Failure(
                command,
                CharacterAfterRunSettlementServiceOutcome.Invalid,
                "invalid_command");
        }

        CharacterAfterRunSettlementWorkspaceLookupResult initialLookup =
            _workspace.Lookup(
                command.WorkspaceId,
                command.TransactionId,
                commandDigest);
        CharacterAfterRunSettlementResult? replay = MapLookup(
            command,
            commandDigest,
            initialLookup);
        if (replay is not null)
        {
            return replay;
        }

        CharacterAfterRunSettlementWorkspaceReadResult read = _workspace.Read(
            command.WorkspaceId,
            command.Identity);
        if (read.Outcome != CharacterAfterRunSettlementWorkspaceOutcome.Available
            || read.Input is null)
        {
            return Failure(
                command,
                MapReadOutcome(read.Outcome),
                ReadBlocker(read),
                commandDigest,
                read.CurrentWorkspaceRevision);
        }
        if (read.CurrentWorkspaceRevision != command.ExpectedWorkspaceRevision)
        {
            return Failure(
                command,
                CharacterAfterRunSettlementServiceOutcome.Conflict,
                "stale_workspace_revision",
                commandDigest,
                read.CurrentWorkspaceRevision);
        }
        if (read.Input.Identity != command.Identity
            || !CharacterAfterRunSettlementRules.TryCreateQuote(
                read.Input,
                out CharacterAfterRunSettlementQuote quote))
        {
            return Failure(
                command,
                CharacterAfterRunSettlementServiceOutcome.Corrupt,
                "authoritative_projection_invalid",
                commandDigest,
                read.CurrentWorkspaceRevision);
        }
        if (!CharacterAfterRunSettlementServiceIntegrity.TryComputeBindingDigest(
                command.WorkspaceId,
                read.CurrentWorkspaceRevision,
                quote,
                out string bindingDigest)
            || !FixedEquals(bindingDigest, command.ExpectedBindingDigest)
            || !FixedEquals(quote.SourceDigest, command.ExpectedSourceDigest)
            || !FixedEquals(
                quote.CustomDataDigest,
                command.ExpectedCustomDataDigest)
            || !FixedEquals(
                quote.GmPolicyDigest,
                command.ExpectedGmPolicyDigest)
            || !FixedEquals(quote.RuntimeDigest, command.ExpectedRuntimeDigest)
            || !FixedEquals(quote.LogicalDigest, command.ExpectedLogicalDigest))
        {
            return Failure(
                command,
                CharacterAfterRunSettlementServiceOutcome.Conflict,
                "stale_quote_binding",
                commandDigest,
                read.CurrentWorkspaceRevision,
                quote);
        }
        if (!CharacterAfterRunSettlementRules.TryCreatePlan(
                quote,
                command.ExpectedSourceDigest,
                command.ExpectedCustomDataDigest,
                command.ExpectedGmPolicyDigest,
                command.ExpectedRuntimeDigest,
                command.ExpectedLogicalDigest,
                command.ExplicitlyConfirmed,
                transactionIdAlreadyExists: false,
                command.TransactionId,
                out CharacterAfterRunSettlementPlan plan))
        {
            return Failure(
                command,
                CharacterAfterRunSettlementServiceOutcome.Blocked,
                quote.CanSettle
                    ? "explicit_confirmation_required"
                    : quote.Blocker.ToString(),
                commandDigest,
                read.CurrentWorkspaceRevision,
                quote);
        }

        CharacterAfterRunSettlementWorkspaceCommitResult committed =
            _workspace.Commit(new CharacterAfterRunSettlementWorkspaceCommitRequest(
                command.WorkspaceId,
                command.ExpectedWorkspaceRevision,
                commandDigest,
                quote,
                plan));
        return MapCommit(command, commandDigest, quote, committed);
    }

    private CharacterAfterRunSettlementResult MapCommit(
        CharacterAfterRunSettlementCommand command,
        string commandDigest,
        CharacterAfterRunSettlementQuote quote,
        CharacterAfterRunSettlementWorkspaceCommitResult commit)
    {
        if (commit.Outcome is CharacterAfterRunSettlementWorkspaceOutcome.Applied
            or CharacterAfterRunSettlementWorkspaceOutcome.Replayed)
        {
            return MapSuccessfulCommit(command, commandDigest, quote, commit);
        }

        if (commit.Outcome is CharacterAfterRunSettlementWorkspaceOutcome.Conflict
            or CharacterAfterRunSettlementWorkspaceOutcome.Indeterminate)
        {
            CharacterAfterRunSettlementWorkspaceLookupResult recovery =
                _workspace.Lookup(
                    command.WorkspaceId,
                    command.TransactionId,
                    commandDigest);
            CharacterAfterRunSettlementResult? recovered = MapLookup(
                command,
                commandDigest,
                recovery);
            if (recovered is not null)
            {
                return recovered;
            }
            if (commit.Outcome == CharacterAfterRunSettlementWorkspaceOutcome.Indeterminate)
            {
                return Failure(
                    command,
                    CharacterAfterRunSettlementServiceOutcome.Unavailable,
                    "commit_outcome_unresolved",
                    commandDigest,
                    Math.Max(
                        commit.CurrentWorkspaceRevision,
                        recovery.CurrentWorkspaceRevision),
                    quote);
            }
        }

        return Failure(
            command,
            commit.Outcome switch
            {
                CharacterAfterRunSettlementWorkspaceOutcome.Conflict
                    => CharacterAfterRunSettlementServiceOutcome.Conflict,
                CharacterAfterRunSettlementWorkspaceOutcome.IdempotencyConflict
                    => CharacterAfterRunSettlementServiceOutcome.IdempotencyConflict,
                CharacterAfterRunSettlementWorkspaceOutcome.Missing
                    => CharacterAfterRunSettlementServiceOutcome.Missing,
                CharacterAfterRunSettlementWorkspaceOutcome.Corrupt
                    => CharacterAfterRunSettlementServiceOutcome.Corrupt,
                _ => CharacterAfterRunSettlementServiceOutcome.Unavailable
            },
            commit.Error ?? "atomic_commit_failed",
            commandDigest,
            commit.CurrentWorkspaceRevision,
            quote);
    }

    private static CharacterAfterRunSettlementResult MapSuccessfulCommit(
        CharacterAfterRunSettlementCommand command,
        string commandDigest,
        CharacterAfterRunSettlementQuote quote,
        CharacterAfterRunSettlementWorkspaceCommitResult commit)
    {
        CharacterAfterRunSettlementServiceOutcome outcome =
            commit.Outcome == CharacterAfterRunSettlementWorkspaceOutcome.Applied
                ? CharacterAfterRunSettlementServiceOutcome.Applied
                : CharacterAfterRunSettlementServiceOutcome.Replayed;
        bool validRevision = outcome == CharacterAfterRunSettlementServiceOutcome.Applied
            ? commit.CurrentWorkspaceRevision
                == command.ExpectedWorkspaceRevision + 1
            : commit.CurrentWorkspaceRevision > command.ExpectedWorkspaceRevision;
        CharacterAfterRunSettlementQuote? reviewed =
            commit.ReviewedQuote ?? quote;
        if (!validRevision
            || !FixedEquals(commit.ExistingCommandDigest, commandDigest)
            || !IsValidReceipt(command, reviewed, commit.Receipt))
        {
            return Failure(
                command,
                CharacterAfterRunSettlementServiceOutcome.Corrupt,
                "commit_receipt_invalid",
                commandDigest,
                commit.CurrentWorkspaceRevision,
                quote);
        }

        return Success(
            command,
            commandDigest,
            outcome,
            commit.CurrentWorkspaceRevision,
            reviewed!,
            commit.Receipt!);
    }

    private static CharacterAfterRunSettlementResult? MapLookup(
        CharacterAfterRunSettlementCommand command,
        string commandDigest,
        CharacterAfterRunSettlementWorkspaceLookupResult lookup)
    {
        switch (lookup.Outcome)
        {
            case CharacterAfterRunSettlementWorkspaceOutcome.NotFound:
                return null;
            case CharacterAfterRunSettlementWorkspaceOutcome.Replayed:
                if (!FixedEquals(lookup.ExistingCommandDigest, commandDigest)
                    || lookup.CurrentWorkspaceRevision
                        <= command.ExpectedWorkspaceRevision
                    || !IsValidReceipt(
                        command,
                        lookup.ReviewedQuote,
                        lookup.Receipt))
                {
                    return Failure(
                        command,
                        CharacterAfterRunSettlementServiceOutcome.Corrupt,
                        "replay_receipt_invalid",
                        commandDigest,
                        lookup.CurrentWorkspaceRevision);
                }
                return Success(
                    command,
                    commandDigest,
                    CharacterAfterRunSettlementServiceOutcome.Replayed,
                    lookup.CurrentWorkspaceRevision,
                    lookup.ReviewedQuote!,
                    lookup.Receipt!);
            case CharacterAfterRunSettlementWorkspaceOutcome.IdempotencyConflict:
                return Failure(
                    command,
                    CharacterAfterRunSettlementServiceOutcome.IdempotencyConflict,
                    "transaction_id_conflict",
                    commandDigest,
                    lookup.CurrentWorkspaceRevision);
            case CharacterAfterRunSettlementWorkspaceOutcome.Missing:
            case CharacterAfterRunSettlementWorkspaceOutcome.Corrupt:
            case CharacterAfterRunSettlementWorkspaceOutcome.Unavailable:
            case CharacterAfterRunSettlementWorkspaceOutcome.Indeterminate:
                return Failure(
                    command,
                    MapReadOutcome(lookup.Outcome),
                    lookup.Error ?? "workspace_lookup_failed",
                    commandDigest,
                    lookup.CurrentWorkspaceRevision);
            default:
                return Failure(
                    command,
                    CharacterAfterRunSettlementServiceOutcome.Corrupt,
                    "workspace_lookup_outcome_invalid",
                    commandDigest,
                    lookup.CurrentWorkspaceRevision);
        }
    }

    private static bool IsValidReceipt(
        CharacterAfterRunSettlementCommand command,
        CharacterAfterRunSettlementQuote? reviewed,
        CharacterAfterRunSettlementReceipt? receipt)
        => CharacterAfterRunSettlementRules.IsCoherent(reviewed)
            && CharacterAfterRunSettlementRules.IsCoherent(receipt)
            && reviewed!.Identity == command.Identity
            && receipt!.Identity == command.Identity
            && receipt.TransactionId == command.TransactionId
            && FixedEquals(reviewed.SourceDigest, command.ExpectedSourceDigest)
            && FixedEquals(
                reviewed.CustomDataDigest,
                command.ExpectedCustomDataDigest)
            && FixedEquals(
                reviewed.GmPolicyDigest,
                command.ExpectedGmPolicyDigest)
            && FixedEquals(reviewed.RuntimeDigest, command.ExpectedRuntimeDigest)
            && FixedEquals(reviewed.LogicalDigest, command.ExpectedLogicalDigest)
            && FixedEquals(
                receipt.LogicalDigestBefore,
                reviewed.LogicalDigest)
            && FixedEquals(receipt.SourceDigest, reviewed.SourceDigest)
            && FixedEquals(receipt.CustomDataDigest, reviewed.CustomDataDigest)
            && FixedEquals(receipt.GmPolicyDigest, reviewed.GmPolicyDigest)
            && FixedEquals(receipt.RuntimeDigest, reviewed.RuntimeDigest)
            && FixedEquals(receipt.GmReviewDigest, reviewed.GmReviewDigest)
            && FixedEquals(receipt.OwnerReviewDigest, reviewed.OwnerReviewDigest);

    private static CharacterAfterRunSettlementResult Success(
        CharacterAfterRunSettlementCommand command,
        string commandDigest,
        CharacterAfterRunSettlementServiceOutcome outcome,
        long currentRevision,
        CharacterAfterRunSettlementQuote quote,
        CharacterAfterRunSettlementReceipt receipt)
    {
        var unsigned = new CharacterAfterRunSettlementResult(
            CharacterAfterRunSettlementServiceSchemas.ResultV1,
            outcome,
            command.WorkspaceId,
            command.ExpectedWorkspaceRevision,
            currentRevision,
            command.Identity,
            command.TransactionId,
            commandDigest,
            quote,
            receipt,
            [],
            string.Empty);
        if (!CharacterAfterRunSettlementServiceIntegrity.TryComputeResultDigest(
                unsigned,
                out string resultDigest))
        {
            return unsigned with
            {
                Outcome = CharacterAfterRunSettlementServiceOutcome.Corrupt,
                Receipt = null,
                Blockers = ["result_digest_failed"]
            };
        }
        return unsigned with { ResultDigest = resultDigest };
    }

    private static CharacterAfterRunSettlementResult Failure(
        CharacterAfterRunSettlementCommand command,
        CharacterAfterRunSettlementServiceOutcome outcome,
        string blocker,
        string commandDigest = "",
        long currentRevision = 0,
        CharacterAfterRunSettlementQuote? quote = null)
        => new(
            CharacterAfterRunSettlementServiceSchemas.ResultV1,
            outcome,
            command.WorkspaceId,
            command.ExpectedWorkspaceRevision,
            currentRevision,
            command.Identity,
            command.TransactionId,
            commandDigest,
            quote,
            null,
            [blocker],
            string.Empty);

    private static CharacterAfterRunSettlementQuoteResult QuoteFailure(
        CharacterAfterRunSettlementServiceOutcome outcome,
        string blocker)
        => new(outcome, null, [blocker]);

    private static CharacterAfterRunSettlementServiceOutcome MapReadOutcome(
        CharacterAfterRunSettlementWorkspaceOutcome outcome)
        => outcome switch
        {
            CharacterAfterRunSettlementWorkspaceOutcome.Missing
                => CharacterAfterRunSettlementServiceOutcome.Missing,
            CharacterAfterRunSettlementWorkspaceOutcome.Corrupt
                => CharacterAfterRunSettlementServiceOutcome.Corrupt,
            _ => CharacterAfterRunSettlementServiceOutcome.Unavailable
        };

    private static string ReadBlocker(
        CharacterAfterRunSettlementWorkspaceReadResult read)
        => read.Error ?? read.Outcome switch
        {
            CharacterAfterRunSettlementWorkspaceOutcome.Missing
                => "workspace_missing",
            CharacterAfterRunSettlementWorkspaceOutcome.Corrupt
                => "workspace_corrupt",
            _ => "workspace_unavailable"
        };

    private static bool IsValidQuoteRequest(
        CharacterAfterRunSettlementQuoteRequest request)
        => CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(
                request.WorkspaceId)
            && request.Identity is
            {
                ProposalId: var proposalId,
                RunId: var runId,
                CharacterId: var characterId
            }
            && proposalId != Guid.Empty
            && runId != Guid.Empty
            && characterId != Guid.Empty;

    private static bool FixedEquals(string? left, string? right)
    {
        if (left is null || right is null)
            return false;
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
