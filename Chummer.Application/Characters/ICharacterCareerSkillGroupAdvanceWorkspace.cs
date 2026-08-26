using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public enum CharacterCareerSkillGroupWorkspaceOutcome
{
    Available,
    NotFound,
    Applied,
    Replayed,
    Conflict,
    IdempotencyConflict,
    Missing,
    Corrupt,
    Unavailable
}

public sealed record CharacterCareerSkillGroupWorkspaceReadResult(
    CharacterCareerSkillGroupWorkspaceOutcome Outcome,
    long CurrentWorkspaceRevision = 0,
    CharacterCareerSkillGroupAdvanceInput? Input = null,
    string? Error = null);

public sealed record CharacterCareerSkillGroupWorkspaceLookupResult(
    CharacterCareerSkillGroupWorkspaceOutcome Outcome,
    long CurrentWorkspaceRevision = 0,
    string ExistingCommandDigest = "",
    CharacterCareerSkillGroupAdvanceQuote? ReviewedQuote = null,
    CharacterCareerSkillGroupAdvanceReceipt? Receipt = null,
    string? Error = null);

public sealed record CharacterCareerSkillGroupWorkspaceCommitRequest(
    CharacterWorkspaceId WorkspaceId,
    long ExpectedWorkspaceRevision,
    string CommandDigest,
    CharacterCareerSkillGroupAdvanceQuote ReviewedQuote,
    CharacterCareerSkillGroupAdvancePlan Plan);

public sealed record CharacterCareerSkillGroupWorkspaceCommitResult(
    CharacterCareerSkillGroupWorkspaceOutcome Outcome,
    long CurrentWorkspaceRevision = 0,
    string ExistingCommandDigest = "",
    CharacterCareerSkillGroupAdvanceQuote? ReviewedQuote = null,
    CharacterCareerSkillGroupAdvanceReceipt? Receipt = null,
    string? Error = null);

/// <summary>
/// Authoritative persistence seam for Career skill-group advancement. Implementations
/// project input from the saved workspace, never from presentation. Commit must check
/// replay before revision, claim the transaction id, compare the workspace revision,
/// apply the plan, observe the unique expense and post-state, create the Core receipt,
/// and persist both mutation and receipt as one durable transaction.
/// </summary>
public interface ICharacterCareerSkillGroupAdvanceWorkspace
{
    CharacterCareerSkillGroupWorkspaceReadResult Read(
        CharacterWorkspaceId workspaceId,
        CharacterCareerSkillGroupIdentity identity);

    CharacterCareerSkillGroupWorkspaceLookupResult Lookup(
        CharacterWorkspaceId workspaceId,
        Guid transactionId,
        string commandDigest);

    CharacterCareerSkillGroupWorkspaceCommitResult Commit(
        CharacterCareerSkillGroupWorkspaceCommitRequest request);
}
