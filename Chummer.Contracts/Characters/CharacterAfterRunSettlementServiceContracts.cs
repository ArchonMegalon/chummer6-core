using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public static class CharacterAfterRunSettlementServiceSchemas
{
    public const string QuoteV1 = "chummer.core.sr5-after-run-settlement-quote/v1";
    public const string CommandV1 = "chummer.core.sr5-after-run-settlement-command/v1";
    public const string ResultV1 = "chummer.core.sr5-after-run-settlement-result/v1";
    public const int MaximumWorkspaceIdLength = 200;
}

public sealed record CharacterAfterRunSettlementQuoteRequest(
    CharacterWorkspaceId WorkspaceId,
    CharacterAfterRunSettlementIdentity Identity);

public sealed record CharacterAfterRunSettlementQuoteBinding(
    string ContractName,
    CharacterWorkspaceId WorkspaceId,
    long WorkspaceRevision,
    CharacterAfterRunSettlementIdentity Identity,
    CharacterAfterRunSettlementQuote Quote,
    string BindingDigest);

public sealed record CharacterAfterRunSettlementCommand(
    string ContractName,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedWorkspaceRevision,
    CharacterAfterRunSettlementIdentity Identity,
    string ExpectedSourceDigest,
    string ExpectedCustomDataDigest,
    string ExpectedGmPolicyDigest,
    string ExpectedRuntimeDigest,
    string ExpectedLogicalDigest,
    string ExpectedBindingDigest,
    Guid TransactionId,
    bool ExplicitlyConfirmed);

public enum CharacterAfterRunSettlementServiceOutcome
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

public sealed record CharacterAfterRunSettlementQuoteResult(
    CharacterAfterRunSettlementServiceOutcome Outcome,
    CharacterAfterRunSettlementQuoteBinding? Binding,
    IReadOnlyList<string> Blockers);

public sealed record CharacterAfterRunSettlementResult(
    string ContractName,
    CharacterAfterRunSettlementServiceOutcome Outcome,
    CharacterWorkspaceId WorkspaceId,
    long ExpectedWorkspaceRevision,
    long CurrentWorkspaceRevision,
    CharacterAfterRunSettlementIdentity Identity,
    Guid TransactionId,
    string CommandDigest,
    CharacterAfterRunSettlementQuote? ReviewedQuote,
    CharacterAfterRunSettlementReceipt? Receipt,
    IReadOnlyList<string> Blockers,
    string ResultDigest);

public static class CharacterAfterRunSettlementServiceIntegrity
{
    public static bool TryComputeBindingDigest(
        CharacterWorkspaceId workspaceId,
        long workspaceRevision,
        CharacterAfterRunSettlementQuote? quote,
        out string digest)
    {
        digest = string.Empty;
        if (!IsValidWorkspaceId(workspaceId)
            || workspaceRevision <= 0
            || !CharacterAfterRunSettlementRules.IsCoherent(quote))
        {
            return false;
        }

        digest = Sha256(Canonical(
            CharacterAfterRunSettlementServiceSchemas.QuoteV1,
            workspaceId.Value,
            workspaceRevision.ToString(CultureInfo.InvariantCulture),
            IdentityText(quote!.Identity),
            quote.SourceDigest,
            quote.CustomDataDigest,
            quote.GmPolicyDigest,
            quote.RuntimeDigest,
            quote.LogicalDigest));
        return true;
    }

    public static bool TryComputeCommandDigest(
        CharacterAfterRunSettlementCommand? command,
        out string digest)
    {
        digest = string.Empty;
        if (command is null
            || !string.Equals(
                command.ContractName,
                CharacterAfterRunSettlementServiceSchemas.CommandV1,
                StringComparison.Ordinal)
            || !IsValidWorkspaceId(command.WorkspaceId)
            || command.ExpectedWorkspaceRevision <= 0
            || command.ExpectedWorkspaceRevision == long.MaxValue
            || !IsValidIdentity(command.Identity)
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(
                command.ExpectedSourceDigest)
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(
                command.ExpectedCustomDataDigest)
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(
                command.ExpectedGmPolicyDigest)
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(
                command.ExpectedRuntimeDigest)
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(
                command.ExpectedLogicalDigest)
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(
                command.ExpectedBindingDigest)
            || command.TransactionId == Guid.Empty)
        {
            return false;
        }

        digest = Sha256(Canonical(
            CharacterAfterRunSettlementServiceSchemas.CommandV1,
            command.WorkspaceId.Value,
            command.ExpectedWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            IdentityText(command.Identity),
            command.ExpectedSourceDigest,
            command.ExpectedCustomDataDigest,
            command.ExpectedGmPolicyDigest,
            command.ExpectedRuntimeDigest,
            command.ExpectedLogicalDigest,
            command.ExpectedBindingDigest,
            command.TransactionId.ToString("D"),
            command.ExplicitlyConfirmed.ToString(CultureInfo.InvariantCulture)));
        return true;
    }

    public static bool TryComputeResultDigest(
        CharacterAfterRunSettlementResult? result,
        out string digest)
    {
        digest = string.Empty;
        if (result is null
            || !string.Equals(
                result.ContractName,
                CharacterAfterRunSettlementServiceSchemas.ResultV1,
                StringComparison.Ordinal)
            || result.Outcome is not CharacterAfterRunSettlementServiceOutcome.Applied
                and not CharacterAfterRunSettlementServiceOutcome.Replayed
            || !IsValidWorkspaceId(result.WorkspaceId)
            || result.ExpectedWorkspaceRevision <= 0
            || result.CurrentWorkspaceRevision <= result.ExpectedWorkspaceRevision
            || !IsValidIdentity(result.Identity)
            || result.TransactionId == Guid.Empty
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(
                result.CommandDigest)
            || !CharacterAfterRunSettlementRules.IsCoherent(result.ReviewedQuote)
            || !CharacterAfterRunSettlementRules.IsCoherent(result.Receipt)
            || result.ReviewedQuote!.Identity != result.Identity
            || result.Receipt!.Identity != result.Identity
            || result.Receipt.TransactionId != result.TransactionId
            || result.Blockers.Count != 0)
        {
            return false;
        }

        digest = Sha256(Canonical(
            CharacterAfterRunSettlementServiceSchemas.ResultV1,
            result.Outcome.ToString(),
            result.WorkspaceId.Value,
            result.ExpectedWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            result.CurrentWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            IdentityText(result.Identity),
            result.TransactionId.ToString("D"),
            result.CommandDigest,
            result.ReviewedQuote.LogicalDigest,
            result.Receipt.ReceiptDigest));
        return true;
    }

    public static bool IsValidWorkspaceId(CharacterWorkspaceId workspaceId)
        => !string.IsNullOrWhiteSpace(workspaceId.Value)
            && workspaceId.Value.Length
                <= CharacterAfterRunSettlementServiceSchemas.MaximumWorkspaceIdLength
            && workspaceId.Value.All(static character =>
                char.IsLetterOrDigit(character) || character is '-' or '_');

    private static bool IsValidIdentity(CharacterAfterRunSettlementIdentity? identity)
        => identity is not null
            && identity.ProposalId != Guid.Empty
            && identity.RunId != Guid.Empty
            && identity.CharacterId != Guid.Empty;

    private static string IdentityText(CharacterAfterRunSettlementIdentity identity)
        => Canonical(
            identity.ProposalId.ToString("D"),
            identity.RunId.ToString("D"),
            identity.CharacterId.ToString("D"));

    private static string Canonical(params string[] values)
        => string.Join('\0', values.Select(value =>
            string.Concat(
                value.Length.ToString(CultureInfo.InvariantCulture),
                ":",
                value)));

    private static string Sha256(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
