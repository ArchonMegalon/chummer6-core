using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal static class CareerSkillSpecializationMutation
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
        CareerSkillSpecializationRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ExpectedQuote);
        ValidateRequestAuthority(request);

        // The transaction reprojects every input under captured current-source custody immediately
        // before this serializer, then repeats that plan at the final atomic replacement fence.
        CharacterCareerSkillSpecializationQuote current = request.ExpectedQuote;
        if (!CharacterCareerSkillSpecializationRules.TryPlanAdd(
                current,
                request.ExpectedCharacterRevision,
                request.ExpectedSourceRevision,
                request.ExpectedRuleDigest,
                request.ExpectedLogicalRevision,
                request.Confirmed,
                request.SpecializationId,
                request.ExpenseId,
                request.ExpenseDateLocal,
                out CharacterCareerSkillSpecializationPlan plan))
        {
            throw new InvalidOperationException(
                "Specialization purchase requires explicit confirmation, an unchanged four-revision quote, sufficient Karma, and valid unique identities/date.");
        }

        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XElement root = SpecializationXmlReader.RequireCharacterRoot(document);
        EnsureSpecializationIdIsUnique(root, plan.SpecializationId);
        XElement target = ResolveSkillElement(root, plan.Identity);
        AddSpecialization(target, plan);
        SetRequiredValue(
            root,
            "karma",
            plan.SavedCharacterKarma.ToString(CultureInfo.InvariantCulture),
            "The saved runner");
        AddExpense(root, plan);

        string serialized = Serialize(document);
        XDocument verification = XDocument.Parse(serialized, LoadOptions.PreserveWhitespace);
        XElement verifiedTarget = ResolveSkillElement(
            SpecializationXmlReader.RequireCharacterRoot(verification),
            plan.Identity);
        XElement[] writtenSpecs = ResolveSpecializationContainer(verifiedTarget)
            .Elements("spec")
            .Where(spec => HasGuid(spec, "guid", plan.SpecializationId))
            .Take(2)
            .ToArray();
        if (writtenSpecs.Length != 1
            || !IsExactPlannedSpecialization(writtenSpecs[0], plan)
            || ResolveSpecializationContainer(verifiedTarget).Elements("spec").Count() != current.ExistingSpecializationCount + 1
            || SpecializationXmlReader.ReadRequiredNonNegativeInt(verification.Root!, "karma") != plan.SavedCharacterKarma)
        {
            throw new InvalidOperationException(
                "The serialized specialization purchase did not preserve exact saved identity, selection, Karma, group, source, and rule authority.");
        }
        return serialized;
    }

    private static XElement ResolveSkillElement(
        XElement root,
        CharacterCareerSkillIdentity identity)
    {
        XElement newSkills = SpecializationXmlReader.RequireSingle(
            root,
            "newskills",
            "The saved runner must have one <newskills> container.");
        string containerName = identity.Kind == CharacterCareerSkillKind.Active
            ? "skills"
            : "knoskills";
        XElement container = SpecializationXmlReader.RequireSingle(
            newSkills,
            containerName,
            $"The saved runner must have one <{containerName}> container.");
        XElement[] matches = container.Elements("skill")
            .Where(candidate => MatchesIdentity(candidate, identity))
            .Take(2)
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                "The selected active/knowledge skill identity is ambiguous.");
    }

    private static bool MatchesIdentity(
        XElement candidate,
        CharacterCareerSkillIdentity identity)
    {
        XElement[] ids = candidate.Elements("guid").Take(2).ToArray();
        XElement[] sourceIds = candidate.Elements("suid").Take(2).ToArray();
        if (ids.Length != 1
            || sourceIds.Length != 1
            || !Guid.TryParse(ids[0].Value.Trim(), out Guid id)
            || !Guid.TryParse(sourceIds[0].Value.Trim(), out Guid sourceId)
            || id != identity.SkillId)
        {
            return false;
        }
        return identity.Kind == CharacterCareerSkillKind.Active
            ? identity.SourceSkillId.HasValue && sourceId == identity.SourceSkillId.Value
            : identity.SourceSkillId.HasValue
                ? sourceId == identity.SourceSkillId.Value
                : sourceId == Guid.Empty;
    }

    private static void AddSpecialization(
        XElement target,
        CharacterCareerSkillSpecializationPlan plan)
    {
        XElement[] containers = target.Elements("specs").Take(2).ToArray();
        if (containers.Length > 1)
        {
            throw new InvalidOperationException(
                "The selected skill has duplicate <specs> containers.");
        }
        XElement specs = containers.SingleOrDefault() ?? new XElement("specs");
        if (specs.Parent is null)
        {
            target.Add(specs);
        }
        specs.Add(new XElement(
            "spec",
            new XElement("guid", plan.SpecializationId.ToString("D")),
            new XElement("name", plan.SpecializationName),
            new XElement("free", plan.SavedFree.ToString(CultureInfo.InvariantCulture)),
            new XElement("expertise", plan.SavedExpertise.ToString(CultureInfo.InvariantCulture))));
    }

    private static XElement ResolveSpecializationContainer(XElement skill)
    {
        XElement[] containers = skill.Elements("specs").Take(2).ToArray();
        return containers.Length == 1
            ? containers[0]
            : throw new InvalidOperationException(
                "The selected skill does not have one exact <specs> container after mutation.");
    }

    private static void EnsureSpecializationIdIsUnique(XElement root, Guid specializationId)
    {
        foreach (XElement spec in root.Descendants("spec"))
        {
            Guid id = SpecializationXmlReader.ReadRequiredGuid(
                spec,
                "guid",
                "A saved skill specialization");
            if (id == specializationId)
            {
                throw new InvalidOperationException(
                    "The requested specialization GUID already exists.");
            }
        }
    }

    private static bool IsExactPlannedSpecialization(
        XElement spec,
        CharacterCareerSkillSpecializationPlan plan)
        => HasGuid(spec, "guid", plan.SpecializationId)
            && HasExactValue(spec, "name", plan.SpecializationName)
            && HasExactValue(spec, "free", plan.SavedFree.ToString(CultureInfo.InvariantCulture))
            && HasExactValue(spec, "expertise", plan.SavedExpertise.ToString(CultureInfo.InvariantCulture))
            && spec.Elements().Count() == 4;

    private static bool HasGuid(XElement parent, string name, Guid expected)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        return values.Length == 1
            && Guid.TryParse(values[0].Value.Trim(), out Guid actual)
            && actual == expected;
    }

    private static bool HasExactValue(XElement parent, string name, string expected)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        return values.Length == 1
            && string.Equals(values[0].Value, expected, StringComparison.Ordinal);
    }

    private static void AddExpense(
        XElement root,
        CharacterCareerSkillSpecializationPlan plan)
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
            Guid id = SpecializationXmlReader.ReadRequiredGuid(
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
        _ = SpecializationXmlReader.ReadRequiredGuid(
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
        if (!SpecializationSettingsResolver.TryParseBoolean(ReadRequiredExpenseText(expense, "refund"), out bool refund)
            || !decimal.TryParse(
                ReadRequiredExpenseText(expense, "amount"),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal amount)
            || !SpecializationSettingsResolver.TryParseBoolean(
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

    private static void ValidateRequestAuthority(CareerSkillSpecializationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WorkspaceId.Value))
        {
            throw new InvalidOperationException(
                "A nonblank dossier identity is required for specialization purchase.");
        }
        if (request.ExpectedContentRevision <= 0)
        {
            throw new InvalidOperationException(
                "A positive dossier revision is required for specialization purchase.");
        }
        if (!string.Equals(
                request.ExpectedCharacterRevision,
                request.ExpectedQuote.CharacterRevision,
                StringComparison.Ordinal)
            || !string.Equals(
                request.ExpectedSourceRevision,
                request.ExpectedQuote.SourceRevision,
                StringComparison.Ordinal)
            || !string.Equals(
                request.ExpectedRuleDigest,
                request.ExpectedQuote.RuleDigest,
                StringComparison.Ordinal)
            || !string.Equals(
                request.ExpectedLogicalRevision,
                request.ExpectedQuote.LogicalRevision,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The request must carry the exact four revisions shown in its reviewed quote.");
        }
    }

    private static string Serialize(XDocument document)
    {
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        document.Save(writer, SaveOptions.DisableFormatting);
        return writer.ToString();
    }
}
