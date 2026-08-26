using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public enum CharacterAfterRunSettlementWorkspaceOutcome
{
    Available,
    NotFound,
    Applied,
    Replayed,
    Conflict,
    IdempotencyConflict,
    Missing,
    Corrupt,
    Indeterminate,
    Unavailable
}

public sealed record CharacterAfterRunSettlementWorkspaceReadResult(
    CharacterAfterRunSettlementWorkspaceOutcome Outcome,
    long CurrentWorkspaceRevision = 0,
    CharacterAfterRunSettlementInput? Input = null,
    string? Error = null);

public sealed record CharacterAfterRunSettlementWorkspaceLookupResult(
    CharacterAfterRunSettlementWorkspaceOutcome Outcome,
    long CurrentWorkspaceRevision = 0,
    string ExistingCommandDigest = "",
    CharacterAfterRunSettlementQuote? ReviewedQuote = null,
    CharacterAfterRunSettlementReceipt? Receipt = null,
    string? Error = null);

public sealed record CharacterAfterRunSettlementWorkspaceCommitRequest(
    CharacterWorkspaceId WorkspaceId,
    long ExpectedWorkspaceRevision,
    string CommandDigest,
    CharacterAfterRunSettlementQuote ReviewedQuote,
    CharacterAfterRunSettlementPlan Plan);

public sealed record CharacterAfterRunSettlementWorkspaceCommitResult(
    CharacterAfterRunSettlementWorkspaceOutcome Outcome,
    long CurrentWorkspaceRevision = 0,
    string ExistingCommandDigest = "",
    CharacterAfterRunSettlementQuote? ReviewedQuote = null,
    CharacterAfterRunSettlementReceipt? Receipt = null,
    string? Error = null);

/// <summary>
/// Persistence seam for one SR5 After Run settlement. Implementations must check
/// the transaction ledger before revision CAS; claim the transaction, mutate the
/// character, add contacts/expense, observe the exact post-state and persist the
/// receipt in one durable commit. Indeterminate commits are resolved by Lookup.
/// </summary>
public interface ICharacterAfterRunSettlementWorkspace
{
    CharacterAfterRunSettlementWorkspaceReadResult Read(
        CharacterWorkspaceId workspaceId,
        CharacterAfterRunSettlementIdentity identity);

    CharacterAfterRunSettlementWorkspaceLookupResult Lookup(
        CharacterWorkspaceId workspaceId,
        Guid transactionId,
        string commandDigest);

    CharacterAfterRunSettlementWorkspaceCommitResult Commit(
        CharacterAfterRunSettlementWorkspaceCommitRequest request);
}
