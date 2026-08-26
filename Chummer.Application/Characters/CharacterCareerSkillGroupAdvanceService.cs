using System.Security.Cryptography;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterCareerSkillGroupAdvanceService :
    ICharacterCareerSkillGroupAdvanceService
{
    private readonly ICharacterCareerSkillGroupAdvanceWorkspace _workspace;

    public CharacterCareerSkillGroupAdvanceService(
        ICharacterCareerSkillGroupAdvanceWorkspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    }

    public CharacterCareerSkillGroupQuoteResult Quote(
        CharacterCareerSkillGroupQuoteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidQuoteRequest(request))
        {
            return QuoteFailure(
                CharacterCareerSkillGroupAdvanceServiceOutcome.Invalid,
                "invalid_quote_request");
        }

        CharacterCareerSkillGroupWorkspaceReadResult read =
            _workspace.Read(request.WorkspaceId, request.Identity);
        if (read.Outcome != CharacterCareerSkillGroupWorkspaceOutcome.Available
            || read.CurrentWorkspaceRevision <= 0
            || read.Input is null)
        {
            return QuoteFailure(MapReadOutcome(read.Outcome), ReadBlocker(read));
        }

        if (read.Input.Identity != request.Identity
            || !CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(
                read.Input,
                out CharacterCareerSkillGroupAdvanceQuote quote)
            || !CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeBindingDigest(
                request.WorkspaceId,
                read.CurrentWorkspaceRevision,
                quote,
                out string bindingDigest))
        {
            return QuoteFailure(
                CharacterCareerSkillGroupAdvanceServiceOutcome.Corrupt,
                "authoritative_projection_invalid");
        }

        return new CharacterCareerSkillGroupQuoteResult(
            CharacterCareerSkillGroupAdvanceServiceOutcome.Available,
            new CharacterCareerSkillGroupQuoteBinding(
                CharacterCareerSkillGroupAdvanceServiceSchemas.QuoteV1,
                request.WorkspaceId,
                read.CurrentWorkspaceRevision,
                request.Identity,
                quote,
                bindingDigest),
            quote.CanAdvance ? [] : [quote.Blocker.ToString()]);
    }

    public CharacterCareerSkillGroupAdvanceResult Advance(
        CharacterCareerSkillGroupAdvanceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeCommandDigest(
                command,
                out string commandDigest))
        {
            return Failure(
                command,
                CharacterCareerSkillGroupAdvanceServiceOutcome.Invalid,
                "invalid_command");
        }

        CharacterCareerSkillGroupWorkspaceLookupResult lookup = _workspace.Lookup(
            command.WorkspaceId,
            command.TransactionId,
            commandDigest);
        CharacterCareerSkillGroupAdvanceResult? replay = MapLookup(
            command,
            commandDigest,
            lookup);
        if (replay is not null)
        {
            return replay;
        }

        CharacterCareerSkillGroupWorkspaceReadResult read = _workspace.Read(
            command.WorkspaceId,
            command.Identity);
        if (read.Outcome != CharacterCareerSkillGroupWorkspaceOutcome.Available
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
                CharacterCareerSkillGroupAdvanceServiceOutcome.Conflict,
                "stale_workspace_revision",
                commandDigest,
                read.CurrentWorkspaceRevision);
        }
        if (read.Input.Identity != command.Identity
            || !CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(
                read.Input,
                out CharacterCareerSkillGroupAdvanceQuote quote))
        {
            return Failure(
                command,
                CharacterCareerSkillGroupAdvanceServiceOutcome.Corrupt,
                "authoritative_projection_invalid",
                commandDigest,
                read.CurrentWorkspaceRevision);
        }
        if (!CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeBindingDigest(
                command.WorkspaceId,
                read.CurrentWorkspaceRevision,
                quote,
                out string bindingDigest)
            || !FixedEquals(bindingDigest, command.ExpectedBindingDigest)
            || !FixedEquals(quote.LogicalRevision, command.ExpectedLogicalRevision)
            || !FixedEquals(quote.SourceRevision, command.ExpectedSourceRevision)
            || !FixedEquals(quote.RuleDigest, command.ExpectedRuleDigest))
        {
            return Failure(
                command,
                CharacterCareerSkillGroupAdvanceServiceOutcome.Conflict,
                "stale_quote_binding",
                commandDigest,
                read.CurrentWorkspaceRevision,
                quote);
        }
        if (!CharacterCareerSkillGroupAdvanceRules.TryPlanAdvance(
                quote,
                command.ExpectedLogicalRevision,
                command.ExpectedSourceRevision,
                command.ExpectedRuleDigest,
                command.ExplicitlyConfirmed,
                transactionIdAlreadyExists: false,
                command.TransactionId,
                command.ExpenseDateLocal,
                out CharacterCareerSkillGroupAdvancePlan plan))
        {
            return Failure(
                command,
                CharacterCareerSkillGroupAdvanceServiceOutcome.Blocked,
                quote.CanAdvance
                    ? "explicit_confirmation_required"
                    : quote.Blocker.ToString(),
                commandDigest,
                read.CurrentWorkspaceRevision,
                quote);
        }

        CharacterCareerSkillGroupWorkspaceCommitResult committed = _workspace.Commit(
            new CharacterCareerSkillGroupWorkspaceCommitRequest(
                command.WorkspaceId,
                command.ExpectedWorkspaceRevision,
                commandDigest,
                quote,
                plan));
        return MapCommit(command, commandDigest, quote, committed);
    }

    private static CharacterCareerSkillGroupAdvanceResult? MapLookup(
        CharacterCareerSkillGroupAdvanceCommand command,
        string commandDigest,
        CharacterCareerSkillGroupWorkspaceLookupResult lookup)
    {
        switch (lookup.Outcome)
        {
            case CharacterCareerSkillGroupWorkspaceOutcome.NotFound:
                return null;
            case CharacterCareerSkillGroupWorkspaceOutcome.Replayed:
                if (!FixedEquals(lookup.ExistingCommandDigest, commandDigest)
                    || lookup.CurrentWorkspaceRevision <= command.ExpectedWorkspaceRevision
                    || !IsValidReceipt(command, lookup.ReviewedQuote, lookup.Receipt))
                {
                    return Failure(
                        command,
                        CharacterCareerSkillGroupAdvanceServiceOutcome.Corrupt,
                        "replay_receipt_invalid",
                        commandDigest,
                        lookup.CurrentWorkspaceRevision);
                }
                return Success(
                    command,
                    commandDigest,
                    CharacterCareerSkillGroupAdvanceServiceOutcome.Replayed,
                    lookup.CurrentWorkspaceRevision,
                    lookup.ReviewedQuote!,
                    lookup.Receipt!);
            case CharacterCareerSkillGroupWorkspaceOutcome.IdempotencyConflict:
                return Failure(
                    command,
                    CharacterCareerSkillGroupAdvanceServiceOutcome.IdempotencyConflict,
                    "transaction_id_conflict",
                    commandDigest,
                    lookup.CurrentWorkspaceRevision);
            case CharacterCareerSkillGroupWorkspaceOutcome.Missing:
            case CharacterCareerSkillGroupWorkspaceOutcome.Corrupt:
            case CharacterCareerSkillGroupWorkspaceOutcome.Unavailable:
                return Failure(
                    command,
                    MapReadOutcome(lookup.Outcome),
                    lookup.Error ?? "workspace_lookup_failed",
                    commandDigest,
                    lookup.CurrentWorkspaceRevision);
            default:
                return Failure(
                    command,
                    CharacterCareerSkillGroupAdvanceServiceOutcome.Corrupt,
                    "workspace_lookup_outcome_invalid",
                    commandDigest,
                    lookup.CurrentWorkspaceRevision);
        }
    }

    private static CharacterCareerSkillGroupAdvanceResult MapCommit(
        CharacterCareerSkillGroupAdvanceCommand command,
        string commandDigest,
        CharacterCareerSkillGroupAdvanceQuote quote,
        CharacterCareerSkillGroupWorkspaceCommitResult commit)
    {
        if (commit.Outcome is CharacterCareerSkillGroupWorkspaceOutcome.Applied
            or CharacterCareerSkillGroupWorkspaceOutcome.Replayed)
        {
            CharacterCareerSkillGroupAdvanceServiceOutcome outcome =
                commit.Outcome == CharacterCareerSkillGroupWorkspaceOutcome.Applied
                    ? CharacterCareerSkillGroupAdvanceServiceOutcome.Applied
                    : CharacterCareerSkillGroupAdvanceServiceOutcome.Replayed;
            bool validRevision = outcome == CharacterCareerSkillGroupAdvanceServiceOutcome.Applied
                ? commit.CurrentWorkspaceRevision
                    == command.ExpectedWorkspaceRevision + 1
                : commit.CurrentWorkspaceRevision
                    > command.ExpectedWorkspaceRevision;
            CharacterCareerSkillGroupAdvanceQuote? reviewed =
                commit.ReviewedQuote ?? quote;
            if (!validRevision
                || !FixedEquals(commit.ExistingCommandDigest, commandDigest)
                || !IsValidReceipt(command, reviewed, commit.Receipt))
            {
                return Failure(
                    command,
                    CharacterCareerSkillGroupAdvanceServiceOutcome.Corrupt,
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

        return Failure(
            command,
            commit.Outcome switch
            {
                CharacterCareerSkillGroupWorkspaceOutcome.Conflict
                    => CharacterCareerSkillGroupAdvanceServiceOutcome.Conflict,
                CharacterCareerSkillGroupWorkspaceOutcome.IdempotencyConflict
                    => CharacterCareerSkillGroupAdvanceServiceOutcome.IdempotencyConflict,
                CharacterCareerSkillGroupWorkspaceOutcome.Missing
                    => CharacterCareerSkillGroupAdvanceServiceOutcome.Missing,
                CharacterCareerSkillGroupWorkspaceOutcome.Corrupt
                    => CharacterCareerSkillGroupAdvanceServiceOutcome.Corrupt,
                _ => CharacterCareerSkillGroupAdvanceServiceOutcome.Unavailable
            },
            commit.Error ?? "atomic_commit_failed",
            commandDigest,
            commit.CurrentWorkspaceRevision,
            quote);
    }

    private static bool IsValidReceipt(
        CharacterCareerSkillGroupAdvanceCommand command,
        CharacterCareerSkillGroupAdvanceQuote? reviewed,
        CharacterCareerSkillGroupAdvanceReceipt? receipt)
        => CharacterCareerSkillGroupAdvanceRules.IsCoherent(reviewed)
            && CharacterCareerSkillGroupAdvanceRules.IsCoherent(receipt)
            && reviewed!.Identity == command.Identity
            && receipt!.Identity == command.Identity
            && receipt.TransactionId == command.TransactionId
            && FixedEquals(
                reviewed.LogicalRevision,
                command.ExpectedLogicalRevision)
            && FixedEquals(
                reviewed.SourceRevision,
                command.ExpectedSourceRevision)
            && FixedEquals(reviewed.RuleDigest, command.ExpectedRuleDigest)
            && FixedEquals(
                receipt.LogicalRevisionBefore,
                reviewed.LogicalRevision)
            && FixedEquals(
                receipt.SourceRevisionBefore,
                reviewed.SourceRevision)
            && FixedEquals(receipt.RuleDigestBefore, reviewed.RuleDigest);

    private static CharacterCareerSkillGroupAdvanceResult Success(
        CharacterCareerSkillGroupAdvanceCommand command,
        string commandDigest,
        CharacterCareerSkillGroupAdvanceServiceOutcome outcome,
        long currentRevision,
        CharacterCareerSkillGroupAdvanceQuote quote,
        CharacterCareerSkillGroupAdvanceReceipt receipt)
    {
        var unsigned = new CharacterCareerSkillGroupAdvanceResult(
            CharacterCareerSkillGroupAdvanceServiceSchemas.ResultV1,
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
        if (!CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeResultDigest(
                unsigned,
                out string resultDigest))
        {
            return unsigned with
            {
                Outcome = CharacterCareerSkillGroupAdvanceServiceOutcome.Corrupt,
                Receipt = null,
                Blockers = ["result_digest_failed"]
            };
        }
        return unsigned with { ResultDigest = resultDigest };
    }

    private static CharacterCareerSkillGroupAdvanceResult Failure(
        CharacterCareerSkillGroupAdvanceCommand command,
        CharacterCareerSkillGroupAdvanceServiceOutcome outcome,
        string blocker,
        string commandDigest = "",
        long currentRevision = 0,
        CharacterCareerSkillGroupAdvanceQuote? quote = null)
        => new(
            CharacterCareerSkillGroupAdvanceServiceSchemas.ResultV1,
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

    private static CharacterCareerSkillGroupQuoteResult QuoteFailure(
        CharacterCareerSkillGroupAdvanceServiceOutcome outcome,
        string blocker)
        => new(outcome, null, [blocker]);

    private static CharacterCareerSkillGroupAdvanceServiceOutcome MapReadOutcome(
        CharacterCareerSkillGroupWorkspaceOutcome outcome)
        => outcome switch
        {
            CharacterCareerSkillGroupWorkspaceOutcome.Missing
                => CharacterCareerSkillGroupAdvanceServiceOutcome.Missing,
            CharacterCareerSkillGroupWorkspaceOutcome.Corrupt
                => CharacterCareerSkillGroupAdvanceServiceOutcome.Corrupt,
            _ => CharacterCareerSkillGroupAdvanceServiceOutcome.Unavailable
        };

    private static string ReadBlocker(
        CharacterCareerSkillGroupWorkspaceReadResult read)
        => read.Error ?? read.Outcome switch
        {
            CharacterCareerSkillGroupWorkspaceOutcome.Missing
                => "workspace_missing",
            CharacterCareerSkillGroupWorkspaceOutcome.Corrupt
                => "workspace_corrupt",
            _ => "workspace_unavailable"
        };

    private static bool IsValidQuoteRequest(
        CharacterCareerSkillGroupQuoteRequest request)
        => !string.IsNullOrWhiteSpace(request.WorkspaceId.Value)
            && request.WorkspaceId.Value.Length
                <= CharacterCareerSkillGroupAdvanceServiceSchemas.MaximumWorkspaceIdLength
            && request.WorkspaceId.Value.All(static character =>
                char.IsLetterOrDigit(character) || character is '-' or '_')
            && request.Identity is { InternalId: var internalId }
            && internalId != Guid.Empty;

    private static bool FixedEquals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }
        byte[] leftBytes = System.Text.Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = System.Text.Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
