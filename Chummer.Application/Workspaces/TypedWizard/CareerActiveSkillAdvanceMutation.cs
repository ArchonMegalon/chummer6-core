// Existing typed adapter from the paired Presentation snapshot; Core executes it
// only after store admission. Rules remain in Chummer.Contracts.Characters.
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Workspaces.TypedWizard;

internal static class CareerActiveSkillAdvanceMutation
{
    private sealed record ExpenseSortRow(
        XElement Expense,
        DateTime Date,
        int Type,
        string Reason,
        bool Refund,
        decimal Amount,
        bool ForceCareerVisible);

    public static string Apply(
        string xml,
        CareerActiveSkillAdvanceRequest request,
        string? settingsCatalogJson,
        ICharacterSourceDataResolver? sourceDataResolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ExpectedSkill);
        ValidateRequestAuthority(request);

        (IReadOnlyList<CharacterCareerActiveSkillAdvanceQuote> current, _) =
            CareerActiveSkillAdvanceEditorProjector.ProjectState(
                xml,
                settingsCatalogJson,
                sourceDataResolver);
        CharacterCareerActiveSkillAdvanceQuote[] matches = current
            .Where(candidate => candidate.Identity == request.ExpectedSkill.Identity)
            .Take(2)
            .ToArray();
        if (matches.Length != 1 || matches[0] != request.ExpectedSkill)
        {
            throw new InvalidOperationException(
                "The selected active skill changed or disappeared while advancement was open.");
        }
        if (!CharacterCareerActiveSkillAdvanceRules.TryPlanAdvance(
                matches[0],
                request.ExpectedRuleDigest,
                request.Confirmed,
                request.ExpenseId,
                request.ExpenseDateLocal,
                out CharacterCareerActiveSkillAdvancePlan plan))
        {
            throw new InvalidOperationException(
                "Active-skill advancement requires confirmation, unchanged rules, sufficient Karma, and a valid expense identity/date.");
        }

        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XElement root = CareerActiveSkillAdvanceEditorProjector.RequireCharacterRoot(document);
        XElement target = ResolveSkillElement(root, plan.Identity);
        SetRequiredValue(
            target,
            "karma",
            plan.SavedSkillKarmaPoints.ToString(CultureInfo.InvariantCulture),
            "The selected active skill");
        SetRequiredValue(
            root,
            "karma",
            plan.SavedCharacterKarma.ToString(CultureInfo.InvariantCulture),
            "The saved runner");
        AddExpense(root, plan);

        string serialized = Serialize(document);
        (IReadOnlyList<CharacterCareerActiveSkillAdvanceQuote> result, _) =
            CareerActiveSkillAdvanceEditorProjector.ProjectState(
                serialized,
                settingsCatalogJson,
                sourceDataResolver);
        CharacterCareerActiveSkillAdvanceQuote[] advanced = result
            .Where(candidate => candidate.Identity == plan.Identity)
            .Take(2)
            .ToArray();
        if (advanced.Length != 1
            || advanced[0].BasePoints != request.ExpectedSkill.BasePoints
            || advanced[0].KarmaPoints != plan.SavedSkillKarmaPoints
            || advanced[0].TotalBaseRating != request.ExpectedSkill.TotalBaseRating + 1
            || advanced[0].AvailableKarma != plan.SavedCharacterKarma
            || !string.Equals(advanced[0].Name, request.ExpectedSkill.Name, StringComparison.Ordinal)
            || !string.Equals(
                advanced[0].SourceRevision,
                request.ExpectedSkill.SourceRevision,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The serialized active-skill advancement did not preserve exact saved identity and rating authority.");
        }
        return serialized;
    }

    private static XElement ResolveSkillElement(
        XElement root,
        CharacterCareerActiveSkillIdentity identity)
    {
        XElement newSkills = CareerActiveSkillAdvanceEditorProjector.RequireSingle(
            root,
            "newskills",
            "The saved runner must have one <newskills> container.");
        XElement skills = CareerActiveSkillAdvanceEditorProjector.RequireSingle(
            newSkills,
            "skills",
            "The saved runner must have one active <skills> container.");
        XElement[] matches = skills.Elements("skill")
            .Where(candidate => MatchesIdentity(candidate, identity))
            .Take(2)
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                "The selected active-skill identity is ambiguous.");
    }

    private static bool MatchesIdentity(
        XElement candidate,
        CharacterCareerActiveSkillIdentity identity)
    {
        XElement[] ids = candidate.Elements("guid").Take(2).ToArray();
        XElement[] sourceIds = candidate.Elements("suid").Take(2).ToArray();
        return ids.Length == 1
            && sourceIds.Length == 1
            && Guid.TryParse(ids[0].Value.Trim(), out Guid id)
            && Guid.TryParse(sourceIds[0].Value.Trim(), out Guid sourceId)
            && id == identity.SkillId
            && sourceId == identity.SourceSkillId;
    }

    private static void AddExpense(
        XElement root,
        CharacterCareerActiveSkillAdvancePlan plan)
    {
        XElement[] containers = root.Elements("expenses").Take(2).ToArray();
        if (containers.Length > 1)
        {
            throw new InvalidOperationException(
                "The saved runner has duplicate <expenses> containers.");
        }
        XElement expenses = containers.SingleOrDefault() ?? new XElement("expenses");
        if (expenses.Parent is null)
        {
            root.Add(expenses);
        }

        foreach (XElement existing in expenses.Elements("expense"))
        {
            Guid id = CareerActiveSkillAdvanceEditorProjector.ReadRequiredGuid(
                existing,
                "guid",
                "A career expense");
            if (id == plan.ExpenseId)
            {
                throw new InvalidOperationException(
                    "The requested career-expense GUID already exists.");
            }
        }

        expenses.Add(new XElement(
            "expense",
            new XElement("guid", plan.ExpenseId.ToString("D")),
            new XElement("date", plan.ExpenseDateLocal.ToString("s", CultureInfo.InvariantCulture)),
            new XElement("amount", plan.ExpenseAmount.ToString(CultureInfo.InvariantCulture)),
            new XElement("reason", plan.ExpenseReason),
            new XElement("type", "Karma"),
            new XElement("refund", "False"),
            new XElement("forcecareervisible", "False"),
            new XElement(
                "undo",
                new XElement("karmatype", plan.KarmaUndoType),
                new XElement("nuyentype", plan.NuyenUndoType),
                new XElement("objectid", plan.UndoObjectId),
                new XElement("qty", plan.UndoQuantity.ToString(CultureInfo.InvariantCulture)),
                new XElement("extra", plan.UndoExtra))));

        XElement[] ordered = expenses.Elements("expense")
            .Select(ProjectExpenseSortRow)
            .OrderByDescending(static candidate => candidate.Date)
            .ThenByDescending(static candidate => candidate.Type)
            .ThenByDescending(static candidate => candidate.Reason, StringComparer.Ordinal)
            .ThenByDescending(static candidate => candidate.Refund)
            .ThenByDescending(static candidate => candidate.Amount)
            .ThenByDescending(static candidate => candidate.ForceCareerVisible)
            .Select(static candidate => candidate.Expense)
            .ToArray();
        foreach (XElement expense in ordered)
        {
            expense.Remove();
        }
        expenses.Add(ordered);
    }

    private static ExpenseSortRow ProjectExpenseSortRow(XElement expense)
    {
        _ = CareerActiveSkillAdvanceEditorProjector.ReadRequiredGuid(
            expense,
            "guid",
            "A career expense");
        string type = ReadRequiredExpenseText(expense, "type");
        int typeOrder = type switch
        {
            "Karma" => 0,
            "Nuyen" => 1,
            _ => throw new InvalidOperationException(
                "A career expense has an invalid <type> value.")
        };
        if (!CareerSettingsProfileResolver.TryParseBoolean(ReadRequiredExpenseText(expense, "refund"), out bool refund)
            || !decimal.TryParse(
                ReadRequiredExpenseText(expense, "amount"),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal amount)
            || !CareerSettingsProfileResolver.TryParseBoolean(
                ReadRequiredExpenseText(expense, "forcecareervisible"),
                out bool forceCareerVisible))
        {
            throw new InvalidOperationException(
                "A career expense has invalid sorting values.");
        }

        return new ExpenseSortRow(
            expense,
            ReadExpenseDate(expense),
            typeOrder,
            ReadRequiredExpenseText(expense, "reason"),
            refund,
            amount,
            forceCareerVisible);
    }

    private static DateTime ReadExpenseDate(XElement expense)
    {
        XElement[] values = expense.Elements("date").Take(2).ToArray();
        if (values.Length != 1
            || !DateTime.TryParse(
                values[0].Value.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime date))
        {
            throw new InvalidOperationException(
                "A career expense has an invalid or duplicate <date> value.");
        }
        return date;
    }

    private static string ReadRequiredExpenseText(XElement expense, string name)
    {
        XElement[] values = expense.Elements(name).Take(2).ToArray();
        return values.Length == 1
            ? values[0].Value
            : throw new InvalidOperationException(
                $"A career expense has a missing or duplicate <{name}> value.");
    }

    private static void SetRequiredValue(
        XElement parent,
        string name,
        string value,
        string owner)
    {
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"{owner} has a missing or duplicate <{name}> value.");
        }
        matches[0].Value = value;
    }

    private static void ValidateRequestAuthority(CareerActiveSkillAdvanceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WorkspaceId.Value))
        {
            throw new InvalidOperationException(
                "A nonblank dossier identity is required for active-skill advancement.");
        }
        if (request.ExpectedContentRevision <= 0)
        {
            throw new InvalidOperationException(
                "A positive dossier revision is required for active-skill advancement.");
        }
    }

    private static string Serialize(XDocument document)
    {
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        document.Save(writer, SaveOptions.DisableFormatting);
        return writer.ToString();
    }
}
