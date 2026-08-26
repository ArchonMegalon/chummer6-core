using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public static class CharacterCareerSkillGroupAdvanceServiceSchemas
{
    public const string QuoteV1 = "chummer.core.sr5-career-skill-group-quote/v1";
    public const string CommandV1 = "chummer.core.sr5-career-skill-group-command/v1";
    public const string ResultV1 = "chummer.core.sr5-career-skill-group-result/v1";
    public const int MaximumWorkspaceIdLength = 200;
}

public sealed record CharacterCareerSkillGroupQuoteRequest(
    CharacterWorkspaceId WorkspaceId,
    CharacterCareerSkillGroupIdentity Identity);

public sealed record CharacterCareerSkillGroupQuoteBinding(
    string ContractName,
    CharacterWorkspaceId WorkspaceId,
    long WorkspaceRevision,
    CharacterCareerSkillGroupIdentity Identity,
    CharacterCareerSkillGroupAdvanceQuote Quote,
    string BindingDigest);

public sealed record CharacterCareerSkillGroupAdvanceCommand(
    string ContractName,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedWorkspaceRevision,
    CharacterCareerSkillGroupIdentity Identity,
    string ExpectedLogicalRevision,
    string ExpectedSourceRevision,
    string ExpectedRuleDigest,
    string ExpectedBindingDigest,
    Guid TransactionId,
    DateTime ExpenseDateLocal,
    bool ExplicitlyConfirmed);

public enum CharacterCareerSkillGroupAdvanceServiceOutcome
{
    Available,
    Applied,
    Replayed,
    Invalid,
    Blocked,
    Conflict,
    IdempotencyConflict,
    Missing,
    Corrupt,
    Unavailable
}

public sealed record CharacterCareerSkillGroupQuoteResult(
    CharacterCareerSkillGroupAdvanceServiceOutcome Outcome,
    CharacterCareerSkillGroupQuoteBinding? Binding,
    IReadOnlyList<string> Blockers);

public sealed record CharacterCareerSkillGroupAdvanceResult(
    string ContractName,
    CharacterCareerSkillGroupAdvanceServiceOutcome Outcome,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedWorkspaceRevision,
    long CurrentWorkspaceRevision,
    CharacterCareerSkillGroupIdentity Identity,
    Guid TransactionId,
    string CommandDigest,
    CharacterCareerSkillGroupAdvanceQuote? ReviewedQuote,
    CharacterCareerSkillGroupAdvanceReceipt? Receipt,
    IReadOnlyList<string> Blockers,
    string ResultDigest);

/// <summary>
/// Canonical digests for the transport-facing quote/confirm boundary. These digests
/// bind typed workspace and skill-group identity to the exact Core quote and command;
/// they are not persistence authority by themselves.
/// </summary>
public static class CharacterCareerSkillGroupAdvanceServiceIntegrity
{
    public static bool TryComputeBindingDigest(
        CharacterWorkspaceId workspaceId,
        long workspaceRevision,
        CharacterCareerSkillGroupAdvanceQuote? quote,
        out string digest)
    {
        digest = string.Empty;
        if (!IsValidWorkspaceId(workspaceId)
            || workspaceRevision <= 0
            || !CharacterCareerSkillGroupAdvanceRules.IsCoherent(quote))
        {
            return false;
        }

        digest = Sha256(string.Join('\0',
            CharacterCareerSkillGroupAdvanceServiceSchemas.QuoteV1,
            workspaceId.Value,
            workspaceRevision.ToString(CultureInfo.InvariantCulture),
            quote!.Identity.InternalId.ToString("D"),
            quote.LogicalRevision,
            quote.SourceRevision,
            quote.RuleDigest));
        return true;
    }

    public static bool TryComputeCommandDigest(
        CharacterCareerSkillGroupAdvanceCommand? command,
        out string digest)
    {
        digest = string.Empty;
        if (command is null
            || !string.Equals(
                command.ContractName,
                CharacterCareerSkillGroupAdvanceServiceSchemas.CommandV1,
                StringComparison.Ordinal)
            || !IsValidWorkspaceId(command.WorkspaceId)
            || command.ExpectedWorkspaceRevision <= 0
            || command.ExpectedWorkspaceRevision == long.MaxValue
            || command.Identity is not { InternalId: var internalId }
            || internalId == Guid.Empty
            || !IsCanonicalDigest(command.ExpectedLogicalRevision)
            || !IsCanonicalDigest(command.ExpectedSourceRevision)
            || !IsCanonicalDigest(command.ExpectedRuleDigest)
            || !IsCanonicalDigest(command.ExpectedBindingDigest)
            || command.TransactionId == Guid.Empty
            || command.ExpenseDateLocal.Kind != DateTimeKind.Unspecified
            || command.ExpenseDateLocal < CharacterCareerSkillGroupAdvanceRules.MinimumExpenseDate
            || command.ExpenseDateLocal > CharacterCareerSkillGroupAdvanceRules.MaximumExpenseDate)
        {
            return false;
        }

        digest = Sha256(string.Join('\0',
            CharacterCareerSkillGroupAdvanceServiceSchemas.CommandV1,
            command.WorkspaceId.Value,
            command.ExpectedWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            internalId.ToString("D"),
            command.ExpectedLogicalRevision,
            command.ExpectedSourceRevision,
            command.ExpectedRuleDigest,
            command.ExpectedBindingDigest,
            command.TransactionId.ToString("D"),
            command.ExpenseDateLocal.ToString("O", CultureInfo.InvariantCulture),
            command.ExplicitlyConfirmed.ToString(CultureInfo.InvariantCulture)));
        return true;
    }

    public static bool TryComputeResultDigest(
        CharacterCareerSkillGroupAdvanceResult? result,
        out string digest)
    {
        digest = string.Empty;
        if (result is null
            || !string.Equals(
                result.ContractName,
                CharacterCareerSkillGroupAdvanceServiceSchemas.ResultV1,
                StringComparison.Ordinal)
            || result.Outcome is not CharacterCareerSkillGroupAdvanceServiceOutcome.Applied
                and not CharacterCareerSkillGroupAdvanceServiceOutcome.Replayed
            || !IsValidWorkspaceId(result.WorkspaceId)
            || result.ExpectedWorkspaceRevision <= 0
            || result.CurrentWorkspaceRevision <= result.ExpectedWorkspaceRevision
            || result.Identity is not { InternalId: var internalId }
            || internalId == Guid.Empty
            || result.TransactionId == Guid.Empty
            || !IsCanonicalDigest(result.CommandDigest)
            || !CharacterCareerSkillGroupAdvanceRules.IsCoherent(result.ReviewedQuote)
            || !CharacterCareerSkillGroupAdvanceRules.IsCoherent(result.Receipt)
            || result.Receipt!.TransactionId != result.TransactionId
            || result.Receipt.Identity != result.Identity
            || result.ReviewedQuote!.Identity != result.Identity
            || result.Blockers.Count != 0)
        {
            return false;
        }

        digest = Sha256(string.Join('\0',
            CharacterCareerSkillGroupAdvanceServiceSchemas.ResultV1,
            result.Outcome.ToString(),
            result.WorkspaceId.Value,
            result.ExpectedWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            result.CurrentWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            internalId.ToString("D"),
            result.TransactionId.ToString("D"),
            result.CommandDigest,
            result.ReviewedQuote.LogicalRevision,
            result.ReviewedQuote.SourceRevision,
            result.ReviewedQuote.RuleDigest,
            result.Receipt.ReceiptDigest));
        return true;
    }

    public static bool IsCanonicalDigest(string? value)
        => value is { Length: CharacterCareerSkillGroupAdvanceRules.RevisionHexLength }
            && value.All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsValidWorkspaceId(CharacterWorkspaceId workspaceId)
        => !string.IsNullOrWhiteSpace(workspaceId.Value)
            && workspaceId.Value.Length
                <= CharacterCareerSkillGroupAdvanceServiceSchemas.MaximumWorkspaceIdLength
            && workspaceId.Value.All(static character =>
                char.IsLetterOrDigit(character) || character is '-' or '_');

    private static string Sha256(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
