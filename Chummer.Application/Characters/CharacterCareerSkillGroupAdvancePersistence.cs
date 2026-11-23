using Chummer.Application.Workspaces;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
namespace Chummer.Application.Characters;
// The established Android ledger v1 codec is retained, including historical operation digests.
// This projector cannot publish a caller-supplied document. The store owns admission and publication.
public static class CharacterCareerSkillGroupAdvancePersistence
{
    private const string LedgerElementName = "androidcareerskillgroupadvanceledger";
    private const string LedgerVersion = "1";
    private const int MaximumLedgerEntries = 4096;
    private const int MaximumLedgerJsonLength = 1_048_576;
    private static readonly JsonSerializerOptions LedgerJsonOptions = new() { PropertyNameCaseInsensitive = false };
    public static void ValidateHistory(WorkspaceStoredDocument saved)
        => _ = ReadLedger(ParseDocument(saved.Document.Content), saved.Id);

    public static CharacterCareerSkillGroupWorkspaceLookupResult Lookup(WorkspaceStoredDocument saved, Guid transactionId, string digest)
    {
        var entries = ReadLedger(ParseDocument(saved.Document.Content), saved.Id);
        var entry = entries.SingleOrDefault(e => e.TransactionId == transactionId);
        if (entry is null) return new(CharacterCareerSkillGroupWorkspaceOutcome.NotFound, saved.ContentRevision);
        if (!FixedEquals(entry.CommandDigest, digest)) return new(CharacterCareerSkillGroupWorkspaceOutcome.IdempotencyConflict,
            saved.ContentRevision, entry.CommandDigest);
        if (entry.CommittedWorkspaceRevision > saved.ContentRevision) throw new InvalidOperationException("skill_group_future_receipt");
        return new(CharacterCareerSkillGroupWorkspaceOutcome.Replayed, saved.ContentRevision, entry.CommandDigest, entry.ReviewedQuote, entry.Receipt);
    }
    public static void ApplyPlan(
        XDocument document,
        CharacterCareerSkillGroupAdvancePlan plan)
    {
        XElement root = RequireCharacterRoot(document);
        XElement groups = RequireSingle(RequireSingle(root, "newskills"), "groups");
        XElement[] matches = groups.Elements("group")
            .Where(group => ReadRequiredGuid(group, "id") == plan.Identity.InternalId)
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new WorkspaceTargetMissingException("skill_group_missing_during_commit");
        }
        SetRequiredValue(matches[0], "karma", plan.SavedGroupKarmaPoints);
        SetRequiredValue(root, "karma", plan.SavedCharacterKarma);
        AddExpense(root, plan);
    }

    private static void AddExpense(
        XElement root,
        CharacterCareerSkillGroupAdvancePlan plan)
    {
        XElement[] containers = root.Elements("expenses").Take(2).ToArray();
        if (containers.Length > 1)
        {
            throw new InvalidOperationException("duplicate_expenses_container");
        }
        XElement expenses = containers.SingleOrDefault() ?? new XElement("expenses");
        if (expenses.Parent is null)
        {
            root.Add(expenses);
        }
        if (expenses.Elements("expense").Any(
                expense => ReadRequiredGuid(expense, "guid") == plan.ExpenseId))
        {
            throw new InvalidOperationException("duplicate_expense_identity");
        }

        expenses.Add(new XElement(
            "expense",
            new XElement("guid", plan.ExpenseId.ToString("D")),
            new XElement("date", plan.ExpenseDateLocal.ToString("s", CultureInfo.InvariantCulture)),
            new XElement("amount", plan.ExpenseAmount.ToString(CultureInfo.InvariantCulture)),
            new XElement("reason", plan.ExpenseReason),
            new XElement("type", "Karma"),
            new XElement("refund", "False"),
            new XElement("forcecareervisible", "True"),
            new XElement(
                "undo",
                new XElement("karmatype", plan.KarmaUndoType),
                new XElement("nuyentype", plan.NuyenUndoType),
                new XElement("objectid", plan.UndoObjectId),
                new XElement("qty", plan.UndoQuantity.ToString(CultureInfo.InvariantCulture)),
                new XElement("extra", plan.UndoExtra))));
    }

    public static CharacterCareerSkillGroupExpenseObservation ObserveExpense(
        XDocument document,
        Guid expenseId)
    {
        XElement root = RequireCharacterRoot(document);
        XElement[] containers = root.Elements("expenses").Take(2).ToArray();
        if (containers.Length != 1)
        {
            throw new InvalidOperationException("missing_or_duplicate_expenses_container");
        }
        XElement[] matches = containers[0].Elements("expense")
            .Where(expense => ReadRequiredGuid(expense, "guid") == expenseId)
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException("expense_observation_not_unique");
        }
        XElement expense = matches[0];
        XElement undo = RequireSingle(expense, "undo");
        return new CharacterCareerSkillGroupExpenseObservation(
            MatchingEntryCount: 1,
            expenseId,
            ReadRequiredDate(expense, "date"),
            ReadRequiredInt(expense, "amount"),
            ReadRequiredText(expense, "reason"),
            ReadRequiredText(expense, "type"),
            ReadRequiredBool(expense, "refund"),
            ReadRequiredBool(expense, "forcecareervisible"),
            ReadRequiredText(undo, "karmatype"),
            ReadRequiredText(undo, "nuyentype"),
            ReadRequiredText(undo, "objectid"),
            ReadRequiredDecimal(undo, "qty"),
            ReadOptionalText(undo, "extra", string.Empty));
    }

    public static void PersistLedgerEntry(
        XDocument document,
        CharacterWorkspaceId workspaceId,
        long expectedRevision,
        long committedRevision,
        string commandDigest,
        CharacterCareerSkillGroupAdvanceQuote reviewedQuote,
        CharacterCareerSkillGroupAdvanceReceipt receipt)
    {
        XElement root = RequireCharacterRoot(document);
        IReadOnlyList<PersistedLedgerEntry> existing = ReadLedger(document, workspaceId);
        if (existing.Count >= MaximumLedgerEntries) throw new InvalidOperationException("skill_group_receipt_ledger_full");
        if (existing.Any(entry => entry.TransactionId == receipt.TransactionId))
        {
            throw new InvalidOperationException("transaction_already_claimed");
        }
        if (!CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeBindingDigest(
                workspaceId,
                expectedRevision,
                reviewedQuote,
                out string bindingDigest))
        {
            throw new InvalidOperationException("binding_digest_failed");
        }
        var unsigned = new CharacterCareerSkillGroupAdvanceResult(
            CharacterCareerSkillGroupAdvanceServiceSchemas.ResultV1,
            CharacterCareerSkillGroupAdvanceServiceOutcome.Applied,
            workspaceId,
            expectedRevision,
            committedRevision,
            reviewedQuote.Identity,
            receipt.TransactionId,
            commandDigest,
            reviewedQuote,
            receipt,
            [],
            string.Empty);
        if (!CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeResultDigest(
                unsigned,
                out string resultDigest))
        {
            throw new InvalidOperationException("result_digest_failed");
        }

        XElement[] ledgers = root.Elements(LedgerElementName).Take(2).ToArray();
        XElement ledger = ledgers.Length switch
        {
            0 => new XElement(LedgerElementName, new XAttribute("version", LedgerVersion)),
            1 => ledgers[0],
            _ => throw new InvalidOperationException("duplicate_skill_group_receipt_ledger")
        };
        if (ledger.Parent is null)
        {
            root.Add(ledger);
        }
        RequireLedgerVersion(ledger);
        ledger.Add(new XElement(
            "entry",
            new XElement("transactionid", receipt.TransactionId.ToString("D")),
            new XElement("expectedworkspacerevision", expectedRevision.ToString(CultureInfo.InvariantCulture)),
            new XElement("committedworkspacerevision", committedRevision.ToString(CultureInfo.InvariantCulture)),
            new XElement("commanddigest", commandDigest),
            new XElement("bindingdigest", bindingDigest),
            new XElement("appliedresultdigest", resultDigest),
            new XElement("reviewedquotejson", JsonSerializer.Serialize(reviewedQuote, LedgerJsonOptions)),
            new XElement("receiptjson", JsonSerializer.Serialize(receipt, LedgerJsonOptions))));
    }

    private static IReadOnlyList<PersistedLedgerEntry> ReadLedger(
        XDocument document,
        CharacterWorkspaceId workspaceId)
    {
        XElement root = RequireCharacterRoot(document);
        XElement[] ledgers = root.Elements(LedgerElementName).Take(2).ToArray();
        if (ledgers.Length == 0)
        {
            return [];
        }
        if (ledgers.Length != 1)
        {
            throw new InvalidOperationException("duplicate_skill_group_receipt_ledger");
        }
        XElement ledger = ledgers[0];
        RequireLedgerVersion(ledger);
        XElement[] rawEntries = ledger.Elements("entry").ToArray();
        if (rawEntries.Length > MaximumLedgerEntries)
        {
            throw new InvalidOperationException("skill_group_receipt_ledger_too_large");
        }

        List<PersistedLedgerEntry> result = [];
        HashSet<Guid> transactionIds = [];
        foreach (XElement raw in rawEntries)
        {
            RequireExactLedgerEntryShape(raw);
            Guid transactionId = ReadRequiredGuid(raw, "transactionid");
            long expectedRevision = ReadRequiredPositiveLong(raw, "expectedworkspacerevision");
            long committedRevision = ReadRequiredPositiveLong(raw, "committedworkspacerevision");
            string commandDigest = ReadRequiredDigest(raw, "commanddigest");
            string bindingDigest = ReadRequiredDigest(raw, "bindingdigest");
            string resultDigest = ReadRequiredDigest(raw, "appliedresultdigest");
            string quoteJson = ReadRequiredBoundedText(raw, "reviewedquotejson");
            string receiptJson = ReadRequiredBoundedText(raw, "receiptjson");
            CharacterCareerSkillGroupAdvanceQuote reviewed =
                JsonSerializer.Deserialize<CharacterCareerSkillGroupAdvanceQuote>(
                    quoteJson,
                    LedgerJsonOptions)
                ?? throw new InvalidOperationException("reviewed_quote_missing");
            CharacterCareerSkillGroupAdvanceReceipt receipt =
                JsonSerializer.Deserialize<CharacterCareerSkillGroupAdvanceReceipt>(
                    receiptJson,
                    LedgerJsonOptions)
                ?? throw new InvalidOperationException("receipt_missing");
            if (!transactionIds.Add(transactionId)
                || committedRevision != checked(expectedRevision + 1)
                || receipt.TransactionId != transactionId
                || reviewed.Identity != receipt.Identity
                || !CharacterCareerSkillGroupAdvanceRules.IsCoherent(reviewed)
                || !CharacterCareerSkillGroupAdvanceRules.IsCoherent(receipt)
                || !FixedEquals(receipt.LogicalRevisionBefore, reviewed.LogicalRevision)
                || !FixedEquals(receipt.SourceRevisionBefore, reviewed.SourceRevision)
                || !FixedEquals(receipt.RuleDigestBefore, reviewed.RuleDigest)
                || !CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeBindingDigest(
                    workspaceId,
                    expectedRevision,
                    reviewed,
                    out string expectedBindingDigest)
                || !FixedEquals(expectedBindingDigest, bindingDigest))
            {
                throw new InvalidOperationException("skill_group_receipt_binding_invalid");
            }
            var unsigned = new CharacterCareerSkillGroupAdvanceResult(
                CharacterCareerSkillGroupAdvanceServiceSchemas.ResultV1,
                CharacterCareerSkillGroupAdvanceServiceOutcome.Applied,
                workspaceId,
                expectedRevision,
                committedRevision,
                reviewed.Identity,
                transactionId,
                commandDigest,
                reviewed,
                receipt,
                [],
                string.Empty);
            if (!CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeResultDigest(
                    unsigned,
                    out string expectedResultDigest)
                || !FixedEquals(expectedResultDigest, resultDigest))
            {
                throw new InvalidOperationException("skill_group_result_digest_invalid");
            }
            result.Add(new PersistedLedgerEntry(
                transactionId,
                expectedRevision,
                committedRevision,
                commandDigest,
                bindingDigest,
                resultDigest,
                reviewed,
                receipt));
        }
        return result;
    }

    public static XDocument ParseDocument(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        return XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
    }

    private static XElement RequireCharacterRoot(XDocument document)
        => document.Root is { } root && root.Name == XName.Get("character")
            ? root
            : throw new InvalidOperationException("invalid_character_root");

    private static XElement RequireSingle(XElement parent, string name)
    {
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException($"missing_or_duplicate_{name}");
    }

    private static void RequireLedgerVersion(XElement ledger)
    {
        XAttribute[] attributes = ledger.Attributes().ToArray();
        if (attributes.Length != 1
            || attributes[0].Name != XName.Get("version")
            || !string.Equals(attributes[0].Value, LedgerVersion, StringComparison.Ordinal)
            || ledger.Elements().Any(element => element.Name != XName.Get("entry")))
        {
            throw new InvalidOperationException("skill_group_receipt_ledger_schema_invalid");
        }
    }

    private static void RequireExactLedgerEntryShape(XElement entry)
    {
        string[] expected =
        [
            "transactionid",
            "expectedworkspacerevision",
            "committedworkspacerevision",
            "commanddigest",
            "bindingdigest",
            "appliedresultdigest",
            "reviewedquotejson",
            "receiptjson"
        ];
        XElement[] children = entry.Elements().ToArray();
        if (entry.HasAttributes
            || children.Length != expected.Length
            || expected.Any(name => children.Count(child => child.Name == XName.Get(name)) != 1)
            || children.Any(child => !expected.Contains(child.Name.LocalName, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException("skill_group_receipt_entry_schema_invalid");
        }
    }

    private static Guid ReadRequiredGuid(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1
            || !Guid.TryParse(values[0].Value.Trim(), out Guid value)
            || value == Guid.Empty)
        {
            throw new InvalidOperationException($"invalid_or_duplicate_{name}");
        }
        return value;
    }

    private static string ReadRequiredText(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0].Value))
        {
            throw new InvalidOperationException($"invalid_or_duplicate_{name}");
        }
        return values[0].Value.Trim();
    }

    private static string ReadRequiredBoundedText(XElement parent, string name)
    {
        string value = ReadRequiredText(parent, name);
        return value.Length <= MaximumLedgerJsonLength
            ? value
            : throw new InvalidOperationException($"{name}_too_large");
    }

    private static string ReadRequiredDigest(XElement parent, string name)
    {
        string value = ReadRequiredText(parent, name);
        return CharacterCareerSkillGroupAdvanceServiceIntegrity.IsCanonicalDigest(value)
            ? value
            : throw new InvalidOperationException($"invalid_{name}");
    }

    private static bool ReadRequiredBool(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1 || !TryReadBoolean(values[0].Value.Trim(), out bool value))
        {
            throw new InvalidOperationException($"invalid_or_duplicate_{name}");
        }
        return value;
    }

    private static int ReadRequiredNonNegativeInt(XElement parent, string name)
        => TryReadNonNegativeInt(parent, name, -1, out int value) && value >= 0
            ? value
            : throw new InvalidOperationException($"invalid_or_duplicate_{name}");

    private static int ReadRequiredNonNegativeInt(
        XElement parent,
        string containerName,
        string name)
        => ReadRequiredNonNegativeInt(RequireSingle(parent, containerName), name);

    private static int ReadOptionalNonNegativeInt(
        XElement parent,
        string name,
        int fallback)
        => TryReadNonNegativeInt(parent, name, fallback, out int value)
            ? value
            : throw new InvalidOperationException($"invalid_or_duplicate_{name}");

    private static int ReadRequiredInt(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1
            || !int.TryParse(
                values[0].Value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value))
        {
            throw new InvalidOperationException($"invalid_or_duplicate_{name}");
        }
        return value;
    }

    private static long ReadRequiredPositiveLong(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1
            || !long.TryParse(
                values[0].Value.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long value)
            || value <= 0)
        {
            throw new InvalidOperationException($"invalid_or_duplicate_{name}");
        }
        return value;
    }

    private static decimal ReadRequiredDecimal(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1
            || !decimal.TryParse(
                values[0].Value.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal value))
        {
            throw new InvalidOperationException($"invalid_or_duplicate_{name}");
        }
        return value;
    }

    private static DateTime ReadRequiredDate(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1
            || !DateTime.TryParse(
                values[0].Value.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime value))
        {
            throw new InvalidOperationException($"invalid_or_duplicate_{name}");
        }
        return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
    }

    private static bool TryReadNonNegativeInt(
        XElement parent,
        string name,
        int fallback,
        out int value)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        value = fallback;
        if (values.Length == 0
            || values.Length == 1 && string.IsNullOrWhiteSpace(values[0].Value))
        {
            return fallback >= 0;
        }
        return values.Length == 1
            && int.TryParse(
                values[0].Value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value)
            && value >= 0;
    }

    private static bool TryReadDecimal(XElement parent, string name, out decimal value)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        value = 0m;
        return values.Length == 1
            && decimal.TryParse(
                values[0].Value.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value);
    }

    private static bool ReadOptionalBool(XElement parent, string name, bool fallback)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length == 0
            || values.Length == 1 && string.IsNullOrWhiteSpace(values[0].Value))
        {
            return fallback;
        }
        if (values.Length != 1 || !TryReadBoolean(values[0].Value.Trim(), out bool value))
        {
            throw new InvalidOperationException($"invalid_or_duplicate_{name}");
        }
        return value;
    }

    private static bool TryReadBoolean(string text, out bool value)
    {
        if (text == "1") { value = true; return true; }
        if (text == "0") { value = false; return true; }
        return bool.TryParse(text, out value);
    }

    private static string ReadOptionalText(
        XElement parent,
        string name,
        string fallback)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        return values.Length switch
        {
            0 => fallback,
            1 => values[0].Value.Trim(),
            _ => throw new InvalidOperationException($"duplicate_{name}")
        };
    }

    private static void SetRequiredValue(XElement parent, string name, int value)
    {
        XElement element = RequireSingle(parent, name);
        element.Value = value.ToString(CultureInfo.InvariantCulture);
    }

    private static bool CanonicalEquals<T>(T left, T right)
    {
        byte[] leftBytes = JsonSerializer.SerializeToUtf8Bytes(left, LedgerJsonOptions);
        byte[] rightBytes = JsonSerializer.SerializeToUtf8Bytes(right, LedgerJsonOptions);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static bool FixedEquals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    public static string Serialize(XDocument document)
    {
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        document.Save(writer, SaveOptions.DisableFormatting);
        return writer.ToString();
    }

    private static bool IsProjectionFailure(Exception error)
        => error is InvalidOperationException
            or ArgumentException
            or JsonException
            or XmlException
            or FormatException
            or OverflowException;

    private static bool IsStoreFailure(Exception error)
        => error is IOException
            or UnauthorizedAccessException
            or InvalidOperationException;

    private sealed record PersistedLedgerEntry(
        Guid TransactionId,
        long ExpectedWorkspaceRevision,
        long CommittedWorkspaceRevision,
        string CommandDigest,
        string BindingDigest,
        string AppliedResultDigest,
        CharacterCareerSkillGroupAdvanceQuote ReviewedQuote,
        CharacterCareerSkillGroupAdvanceReceipt Receipt);

    private readonly record struct WorkspaceFailure(
        CharacterCareerSkillGroupWorkspaceOutcome Outcome,
        long Revision,
        string Error);

    private sealed class WorkspaceTargetMissingException(string message) :
        Exception(message);

    private sealed class WorkspaceAuthorityUnavailableException(string message) :
        Exception(message);
}
