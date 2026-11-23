// Existing typed adapter from the paired Presentation snapshot; Core executes it
// only after store admission. Rules remain in Chummer.Contracts.Characters.
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Workspaces.TypedWizard;

internal static class CareerAttributeAdvanceMutation
{
    internal sealed record ReceiptRecovery(
        IReadOnlyList<CharacterCareerAttributeAdvanceReceipt> Receipts,
        int OmittedCount);

    private sealed record ExpenseSortRow(
        XElement Expense,
        DateTime Date,
        int Type,
        string Reason,
        bool Refund,
        decimal Amount,
        bool ForceCareerVisible);

    public static CareerAttributeAdvanceMutationResult Apply(
        string xml,
        CareerAttributeAdvanceRequest request,
        string? settingsCatalogJson,
        ICharacterSourceDataResolver? sourceDataResolver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ExpectedAttribute);
        ValidateRequestAuthority(request.WorkspaceId.Value, request.ExpectedContentRevision);
        ValidateExpectedQuote(request);

        (IReadOnlyList<CareerAttributeAdvanceEditorProjector.AttributeFacts> current, _) =
            CareerAttributeAdvanceEditorProjector.ProjectFacts(
                xml,
                settingsCatalogJson,
                requireSavedTotalValueMatch: true, sourceDataResolver);
        CareerAttributeAdvanceEditorProjector.AttributeFacts reviewed = ResolveFacts(
            current,
            request.ExpectedAttribute.Identity,
            "The selected attribute changed or disappeared while advancement was open.");
        if (!QuoteMatches(reviewed.Quote, request.ExpectedAttribute))
        {
            throw new InvalidOperationException(
                "The selected attribute changed while advancement was open.");
        }
        if (!CharacterCareerAttributeAdvanceRules.TryPlanAdvance(
                reviewed.Quote,
                request.ExpectedLogicalRevision,
                request.ExpectedSourceRevision,
                request.ExpectedRuleDigest,
                request.Confirmed,
                request.ExpenseId,
                request.ExpenseDateLocal,
                out CharacterCareerAttributeAdvancePlan plan))
        {
            throw new InvalidOperationException(
                "Attribute advancement requires explicit confirmation, unchanged source and rule digests, sufficient Karma, and a fresh expense identity.");
        }

        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XElement root = CareerActiveSkillAdvanceEditorProjector.RequireCharacterRoot(document);
        XElement target = CareerAttributeAdvanceEditorProjector.ResolveExactAttribute(
            root,
            plan.Identity);
        SetRequiredValue(target, "karma", plan.SavedAttributeKarmaPoints, "The selected attribute");
        SetRequiredValue(root, "karma", plan.SavedCharacterKarma, "The saved runner");
        if (plan.BurnedEdgePointsBefore > 0)
        {
            ReplaceBurnedEdge(root, plan.SavedBurnedEdgePoints);
        }
        AddExpense(root, plan);
        RefreshCalculatedTotalValue(document, target, plan.Identity, settingsCatalogJson, sourceDataResolver);

        string serialized = Serialize(document);
        (IReadOnlyList<CareerAttributeAdvanceEditorProjector.AttributeFacts> afterFacts, _) =
            CareerAttributeAdvanceEditorProjector.ProjectFacts(
                serialized,
                settingsCatalogJson,
                requireSavedTotalValueMatch: true, sourceDataResolver);
        CareerAttributeAdvanceEditorProjector.AttributeFacts after = ResolveFacts(
            afterFacts,
            plan.Identity,
            "The advanced attribute no longer resolves from exact saved authority.");
        XDocument verifiedDocument = XDocument.Parse(serialized, LoadOptions.PreserveWhitespace);
        XElement verifiedRoot = CareerActiveSkillAdvanceEditorProjector.RequireCharacterRoot(
            verifiedDocument);
        bool expenseExactlyOnce = FindExpenses(verifiedRoot, plan.ExpenseId).Length == 1
            && ExpenseMatches(FindExpenses(verifiedRoot, plan.ExpenseId)[0], plan);
        if (after.Quote.BasePoints != reviewed.Quote.BasePoints
            || after.Quote.KarmaPoints != plan.SavedAttributeKarmaPoints
            || after.Quote.EffectiveValue != reviewed.Quote.TargetValue
            || after.Quote.AvailableKarma != plan.SavedCharacterKarma
            || after.Quote.NaturalMaximum != reviewed.Quote.NaturalMaximum
            || after.Quote.BurnedEdgePoints != plan.SavedBurnedEdgePoints
            || !expenseExactlyOnce)
        {
            throw new InvalidOperationException(
                "The serialized attribute advancement did not preserve its reviewed rating, Karma, Burned Edge, and expense authority.");
        }
        if (!CharacterCareerAttributeAdvanceRules.TryCreateReceipt(
                plan.ExpenseId,
                reviewed.Quote,
                plan,
                after.Quote.KarmaPoints,
                after.Quote.AvailableKarma,
                after.Quote.BurnedEdgePoints,
                expenseExactlyOnce,
                out CharacterCareerAttributeAdvanceReceipt receipt))
        {
            throw new InvalidOperationException(
                "The persisted attribute advancement could not be bound to an exact receipt.");
        }
        AddReceiptAudit(verifiedRoot, receipt, after.Quote);
        serialized = Serialize(verifiedDocument);
        XDocument recoveredDocument = XDocument.Parse(
            serialized,
            LoadOptions.PreserveWhitespace);
        XElement recoveredRoot = CareerActiveSkillAdvanceEditorProjector.RequireCharacterRoot(
            recoveredDocument);
        XElement[] persistedReceipts = FindReceiptAudits(recoveredRoot, receipt.TransactionId);
        if (persistedReceipts.Length != 1
            || !ReceiptAuditMatches(recoveredRoot, persistedReceipts[0], receipt, after.Quote))
        {
            throw new InvalidOperationException(
                "The attribute advancement receipt was not persisted exactly once.");
        }

        return new CareerAttributeAdvanceMutationResult(serialized, receipt);
    }

    public static CareerAttributeCorrectionMutationResult Correct(
        string xml,
        CareerAttributeCorrectionRequest request,
        string? settingsCatalogJson,
        ICharacterSourceDataResolver? sourceDataResolver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.OriginalReceipt);
        ValidateRequestAuthority(request.WorkspaceId.Value, request.ExpectedContentRevision);
        if (!request.Confirmed)
        {
            throw new InvalidOperationException(
                "A compensating attribute correction requires explicit confirmation.");
        }

        (IReadOnlyList<CareerAttributeAdvanceEditorProjector.AttributeFacts> current, _) =
            CareerAttributeAdvanceEditorProjector.ProjectFacts(
                xml,
                settingsCatalogJson,
                requireSavedTotalValueMatch: true, sourceDataResolver);
        CareerAttributeAdvanceEditorProjector.AttributeFacts observed = ResolveFacts(
            current,
            request.OriginalReceipt.Identity,
            "The corrected attribute no longer resolves from exact saved authority.");
        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XElement root = CareerActiveSkillAdvanceEditorProjector.RequireCharacterRoot(document);
        bool originalTransactionAlreadyCorrected = ReadCorrectedTransactionIds(root)
            .Contains(request.OriginalReceipt.TransactionId);
        if (originalTransactionAlreadyCorrected)
        {
            throw new InvalidOperationException(
                "The attribute transaction already has a validated compensating correction.");
        }
        XElement[] expenses = FindExpenses(root, request.OriginalReceipt.ExpenseId);
        XElement[] receiptAudits = FindReceiptAudits(
            root,
            request.OriginalReceipt.TransactionId);
        bool persistedReceiptMatches = receiptAudits.Length == 1
            && ReceiptAuditMatches(
                root,
                receiptAudits[0],
                request.OriginalReceipt,
                observed.Quote);
        bool correctionIdAlreadyExists = HasCorrectionId(root, request.CorrectionId)
            || FindExpenses(root, request.CorrectionId).Length != 0
            || FindReceiptAudits(root, request.CorrectionId).Length != 0;
        if (!CharacterCareerAttributeAdvanceRules.TryPlanCorrection(
                request.OriginalReceipt,
                request.CorrectionId,
                request.Reason,
                observed.Quote.KarmaPoints,
                observed.Quote.AvailableKarma,
                observed.Quote.BurnedEdgePoints,
                expenses.Length == 1
                    && persistedReceiptMatches
                    && ExpenseMatchesReceipt(expenses[0], request.OriginalReceipt),
                correctionIdAlreadyExists,
                request.ExpectedReceiptDigest,
                out CharacterCareerAttributeCorrectionPlan correction))
        {
            throw new InvalidOperationException(
                "The attribute correction is stale, foreign, already applied, or no longer matches its exact receipt and expense.");
        }

        XElement target = CareerAttributeAdvanceEditorProjector.ResolveExactAttribute(
            root,
            correction.Identity);
        SetRequiredValue(
            target,
            "karma",
            correction.SavedAttributeKarmaPoints,
            "The selected attribute");
        SetRequiredValue(
            root,
            "karma",
            correction.SavedCharacterKarma,
            "The saved runner");
        if (request.OriginalReceipt.RepairsBurnedEdge)
        {
            ReplaceBurnedEdge(root, correction.SavedBurnedEdgePoints);
        }
        expenses[0].Remove();
        AddCorrectionAudit(root, correction);
        RefreshCalculatedTotalValue(document, target, correction.Identity, settingsCatalogJson, sourceDataResolver);

        string serialized = Serialize(document);
        (IReadOnlyList<CareerAttributeAdvanceEditorProjector.AttributeFacts> revertedFacts, _) =
            CareerAttributeAdvanceEditorProjector.ProjectFacts(
                serialized,
                settingsCatalogJson,
                requireSavedTotalValueMatch: true, sourceDataResolver);
        CareerAttributeAdvanceEditorProjector.AttributeFacts reverted = ResolveFacts(
            revertedFacts,
            correction.Identity,
            "The corrected attribute no longer resolves from exact saved authority.");
        XDocument verifiedDocument = XDocument.Parse(serialized, LoadOptions.PreserveWhitespace);
        XElement verifiedRoot = CareerActiveSkillAdvanceEditorProjector.RequireCharacterRoot(
            verifiedDocument);
        if (reverted.Quote.KarmaPoints != correction.SavedAttributeKarmaPoints
            || reverted.Quote.AvailableKarma != correction.SavedCharacterKarma
            || reverted.Quote.BurnedEdgePoints != correction.SavedBurnedEdgePoints
            || FindExpenses(verifiedRoot, correction.ExpenseIdToRemove).Length != 0
            || CountCorrections(verifiedRoot, correction.CorrectionId) != 1
            || !ReadCorrectedTransactionIds(verifiedRoot)
                .Contains(correction.OriginalTransactionId))
        {
            throw new InvalidOperationException(
                "The serialized attribute correction did not exactly restore saved Karma and Burned Edge authority.");
        }

        return new CareerAttributeCorrectionMutationResult(serialized, correction);
    }

    internal static ReceiptRecovery RecoverReceipts(
        string xml,
        IReadOnlyList<CareerAttributeAdvanceEditorProjector.AttributeFacts> current)
    {
        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XElement root = CareerActiveSkillAdvanceEditorProjector.RequireCharacterRoot(document);
        XElement[] ledgers = root.Elements("careerattributeadvancementreceipts")
            .Take(2)
            .ToArray();
        if (ledgers.Length > 1)
        {
            throw new InvalidOperationException(
                "The saved runner has duplicate attribute-receipt ledgers.");
        }
        XElement[] elements = ledgers.SingleOrDefault()?.Elements("receipt").ToArray() ?? [];
        HashSet<Guid> correctedTransactions = ReadCorrectedTransactionIds(root);
        Dictionary<Guid, int> counts = [];
        foreach (XElement element in elements)
        {
            try
            {
                Guid id = ReadRequiredGuidAttribute(element, "transactionId");
                counts[id] = counts.GetValueOrDefault(id) + 1;
            }
            catch (InvalidOperationException)
            {
                // Counted as omitted below; malformed ledger rows never become recovery authority.
            }
        }

        List<CharacterCareerAttributeAdvanceReceipt> recovered = [];
        int omitted = 0;
        foreach (XElement element in elements)
        {
            try
            {
                CharacterCareerAttributeAdvanceReceipt receipt = ParseReceiptAudit(element);
                if (correctedTransactions.Contains(receipt.TransactionId))
                {
                    continue;
                }
                if (counts.GetValueOrDefault(receipt.TransactionId) != 1)
                {
                    omitted++;
                    continue;
                }
                CareerAttributeAdvanceEditorProjector.AttributeFacts observed = ResolveFacts(
                    current,
                    receipt.Identity,
                    "The receipt target no longer resolves from exact saved authority.");
                XElement[] expenses = FindExpenses(root, receipt.ExpenseId);
                if (!CharacterCareerAttributeAdvanceRules.IsCoherent(receipt)
                    || expenses.Length != 1
                    || !ExpenseMatchesReceipt(expenses[0], receipt)
                    || !ReceiptAuditMatches(root, element, receipt, observed.Quote))
                {
                    omitted++;
                    continue;
                }
                recovered.Add(receipt);
            }
            catch (InvalidOperationException)
            {
                omitted++;
            }
        }
        return new ReceiptRecovery(recovered, omitted);
    }

    private static void ValidateExpectedQuote(CareerAttributeAdvanceRequest request)
    {
        if (!string.Equals(
                request.ExpectedLogicalRevision,
                request.ExpectedAttribute.LogicalRevision,
                StringComparison.Ordinal)
            || !string.Equals(
                request.ExpectedSourceRevision,
                request.ExpectedAttribute.SourceRevision,
                StringComparison.Ordinal)
            || !string.Equals(
                request.ExpectedRuleDigest,
                request.ExpectedAttribute.RuleDigest,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The requested attribute digests do not match the reviewed quote.");
        }
    }

    private static CareerAttributeAdvanceEditorProjector.AttributeFacts ResolveFacts(
        IReadOnlyList<CareerAttributeAdvanceEditorProjector.AttributeFacts> facts,
        CharacterCareerAttributeIdentity identity,
        string error)
    {
        CareerAttributeAdvanceEditorProjector.AttributeFacts[] matches = facts
            .Where(candidate => candidate.Quote.Identity == identity)
            .Take(2)
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(error);
    }

    private static bool QuoteMatches(
        CharacterCareerAttributeAdvanceQuote current,
        CharacterCareerAttributeAdvanceQuote expected)
        => current.Identity == expected.Identity
            && string.Equals(current.DisplayName, expected.DisplayName, StringComparison.Ordinal)
            && current.BasePoints == expected.BasePoints
            && current.KarmaPoints == expected.KarmaPoints
            && current.EffectiveValue == expected.EffectiveValue
            && current.TargetValue == expected.TargetValue
            && current.NaturalMaximum == expected.NaturalMaximum
            && current.MetatypeMinimum == expected.MetatypeMinimum
            && current.AvailableKarma == expected.AvailableKarma
            && current.KarmaCost == expected.KarmaCost
            && current.RepairsBurnedEdge == expected.RepairsBurnedEdge
            && current.BurnedEdgePoints == expected.BurnedEdgePoints
            && current.ApplicationDuration == expected.ApplicationDuration
            && current.TimeAuthority == expected.TimeAuthority
            && current.Prerequisites.SequenceEqual(expected.Prerequisites)
            && current.CanAdvance == expected.CanAdvance
            && current.Blocker == expected.Blocker
            && string.Equals(
                current.LogicalRevision,
                expected.LogicalRevision,
                StringComparison.Ordinal)
            && string.Equals(
                current.SourceRevision,
                expected.SourceRevision,
                StringComparison.Ordinal)
            && string.Equals(current.RuleDigest, expected.RuleDigest, StringComparison.Ordinal);

    private static void RefreshCalculatedTotalValue(
        XDocument document,
        XElement target,
        CharacterCareerAttributeIdentity identity,
        string? settingsCatalogJson,
        ICharacterSourceDataResolver? sourceDataResolver)
    {
        string staged = Serialize(document);
        (IReadOnlyList<CareerAttributeAdvanceEditorProjector.AttributeFacts> stagedFacts, _) =
            CareerAttributeAdvanceEditorProjector.ProjectFacts(
                staged,
                settingsCatalogJson,
                requireSavedTotalValueMatch: false, sourceDataResolver);
        CareerAttributeAdvanceEditorProjector.AttributeFacts calculated = ResolveFacts(
            stagedFacts,
            identity,
            "The mutated attribute cannot be recalculated from exact saved authority.");
        SetRequiredValue(
            target,
            "totalvalue",
            calculated.CalculatedTotalValue,
            "The selected attribute");
    }

    private static void ReplaceBurnedEdge(XElement root, int remaining)
    {
        XElement? improvements = CareerAttributeAdvanceEditorProjector.ResolveSingleImprovements(root);
        // Match the projector's active burn authority; dormant rows are not part
        // of the reviewed penalty and must survive both repair and correction.
        XElement[] burned = improvements?.Elements("improvement")
            .Where(candidate => string.Equals(
                CareerAttributeAdvanceEditorProjector.ReadOptionalText(
                    candidate,
                    "improvementsource",
                    string.Empty),
                "BurnedEdge",
                StringComparison.Ordinal)
                && CareerAttributeAdvanceEditorProjector.ReadOptionalBool(candidate, "enabled", true)
                && !CareerAttributeAdvanceEditorProjector.ReadOptionalBool(candidate, "addtorating", false)
                && (CareerAttributeAdvanceEditorProjector.ReadOptionalText(
                    candidate, "condition", string.Empty) is "" or "career"))
            .ToArray() ?? [];
        foreach (XElement row in burned)
        {
            row.Remove();
        }
        if (remaining == 0)
        {
            return;
        }
        if (remaining < 0)
        {
            throw new InvalidOperationException("Burned Edge authority cannot be negative.");
        }
        if (improvements is null)
        {
            improvements = new XElement("improvements");
            root.Add(improvements);
        }
        improvements.Add(CreateBurnedEdgeImprovement(remaining));
    }

    private static XElement CreateBurnedEdgeImprovement(int remaining)
        => new(
            "improvement",
            new XElement("unique", string.Empty),
            new XElement("target", string.Empty),
            new XElement("improvedname", "EDG"),
            new XElement("sourcename", "BurnedEdge"),
            new XElement("min", (-remaining).ToString(CultureInfo.InvariantCulture)),
            new XElement("max", "0"),
            new XElement("aug", "0"),
            new XElement("augmax", "0"),
            new XElement("val", "0"),
            new XElement("rating", "1"),
            new XElement("exclude", string.Empty),
            new XElement("condition", string.Empty),
            new XElement("improvementttype", "Attribute"),
            new XElement("improvementsource", "BurnedEdge"),
            new XElement("custom", "False"),
            new XElement("customname", string.Empty),
            new XElement("customid", Guid.Empty.ToString("D")),
            new XElement("customgroup", string.Empty),
            new XElement("addtorating", "False"),
            new XElement("enabled", "True"),
            new XElement("order", "0"),
            new XElement("notes", string.Empty),
            new XElement("notesColor", string.Empty));

    private static void AddExpense(XElement root, CharacterCareerAttributeAdvancePlan plan)
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
        if (FindExpenses(root, plan.ExpenseId).Length != 0)
        {
            throw new InvalidOperationException(
                "The requested career-expense GUID already exists.");
        }
        if (FindReceiptAudits(root, plan.ExpenseId).Length != 0
            || HasCorrectionId(root, plan.ExpenseId))
        {
            throw new InvalidOperationException(
                "The requested transaction GUID already exists in the attribute ledger.");
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
        _ = ReadExpenseId(expense);
        int typeOrder = ReadExactText(expense, "type") switch
        {
            "Karma" => 0,
            "Nuyen" => 1,
            _ => throw new InvalidOperationException(
                "A career expense has an invalid <type> value.")
        };
        if (!DateTime.TryParse(
                ReadExactText(expense, "date"),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime date)
            || !CareerSettingsProfileResolver.TryParseBoolean(ReadExactText(expense, "refund"), out bool refund)
            || !decimal.TryParse(
                ReadExactText(expense, "amount"),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal amount)
            || !CareerSettingsProfileResolver.TryParseBoolean(
                ReadExactText(expense, "forcecareervisible"),
                out bool forceCareerVisible))
        {
            throw new InvalidOperationException(
                "A career expense has invalid sorting authority.");
        }
        return new ExpenseSortRow(
            expense,
            date,
            typeOrder,
            ReadExactText(expense, "reason"),
            refund,
            amount,
            forceCareerVisible);
    }

    private static XElement[] FindExpenses(XElement root, Guid id)
    {
        XElement[] containers = root.Elements("expenses").Take(2).ToArray();
        if (containers.Length > 1)
        {
            throw new InvalidOperationException(
                "The saved runner has duplicate <expenses> containers.");
        }
        return containers.SingleOrDefault()?.Elements("expense")
            .Where(candidate => ReadExpenseId(candidate) == id)
            .Take(2)
            .ToArray() ?? [];
    }

    private static Guid ReadExpenseId(XElement expense)
        => CareerActiveSkillAdvanceEditorProjector.ReadRequiredGuid(
            expense,
            "guid",
            "A career expense");

    private static bool ExpenseMatches(
        XElement expense,
        CharacterCareerAttributeAdvancePlan plan)
    {
        XElement[] undo = expense.Elements("undo").Take(2).ToArray();
        return ReadExactText(expense, "date")
                == plan.ExpenseDateLocal.ToString("s", CultureInfo.InvariantCulture)
            && ReadExactText(expense, "amount")
                == plan.ExpenseAmount.ToString(CultureInfo.InvariantCulture)
            && ReadExactText(expense, "reason") == plan.ExpenseReason
            && ReadExactText(expense, "type") == "Karma"
            && ReadExactText(expense, "refund") == "False"
            && ReadExactText(expense, "forcecareervisible") == "False"
            && undo.Length == 1
            && ReadExactText(undo[0], "karmatype") == plan.KarmaUndoType
            && ReadExactText(undo[0], "nuyentype") == plan.NuyenUndoType
            && ReadExactText(undo[0], "objectid") == plan.UndoObjectId
            && ReadExactText(undo[0], "qty")
                == plan.UndoQuantity.ToString(CultureInfo.InvariantCulture)
            && ReadExactText(undo[0], "extra") == plan.UndoExtra;
    }

    private static bool ExpenseMatchesReceipt(
        XElement expense,
        CharacterCareerAttributeAdvanceReceipt receipt)
    {
        XElement[] undo = expense.Elements("undo").Take(2).ToArray();
        return ReadExpenseId(expense) == receipt.ExpenseId
            && ReadExactText(expense, "amount")
                == receipt.ExpenseAmount.ToString(CultureInfo.InvariantCulture)
            && ReadExactText(expense, "type") == "Karma"
            && ReadExactText(expense, "refund") == "False"
            && undo.Length == 1
            && ReadExactText(undo[0], "karmatype") == "ImproveAttribute"
            && ReadExactText(undo[0], "nuyentype") == "AddCyberware"
            && ReadExactText(undo[0], "objectid") == receipt.Identity.Abbreviation
            && ReadExactText(undo[0], "qty") == "0"
            && ReadExactText(undo[0], "extra") == string.Empty;
    }

    private static string ReadExactText(XElement parent, string name)
    {
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        return matches.Length == 1
            ? matches[0].Value
            : throw new InvalidOperationException(
                $"Saved authority has a missing or duplicate <{name}> value.");
    }

    private static bool HasCorrectionId(XElement root, Guid correctionId)
        => CountCorrections(root, correctionId) != 0;

    private static XElement[] FindReceiptAudits(XElement root, Guid transactionId)
    {
        XElement[] containers = root.Elements("careerattributeadvancementreceipts")
            .Take(2)
            .ToArray();
        if (containers.Length > 1)
        {
            throw new InvalidOperationException(
                "The saved runner has duplicate attribute-receipt ledgers.");
        }
        return containers.SingleOrDefault()?.Elements("receipt")
            .Where(candidate => ReadRequiredGuidAttribute(candidate, "transactionId")
                == transactionId)
            .Take(2)
            .ToArray() ?? [];
    }

    private static CharacterCareerAttributeAdvanceReceipt ParseReceiptAudit(XElement element)
    {
        string abbreviation = ReadAttribute(element, "target");
        if (!CharacterCareerAttributeAdvanceRules.TryCreateIdentity(
                abbreviation,
                out CharacterCareerAttributeIdentity identity)
            || ReadAttribute(element, "kind") != identity.Kind.ToString())
        {
            throw new InvalidOperationException(
                "Attribute receipt authority has a foreign target identity.");
        }
        CharacterCareerAttributeAdvanceReceipt receipt = new(
            ReadRequiredGuidAttribute(element, "transactionId"),
            identity,
            ReadRequiredBoolAttribute(element, "repairsBurnedEdge"),
            ReadRequiredIntAttribute(element, "attributeKarmaBefore"),
            ReadRequiredIntAttribute(element, "attributeKarmaAfter"),
            ReadRequiredIntAttribute(element, "characterKarmaBefore"),
            ReadRequiredIntAttribute(element, "characterKarmaAfter"),
            ReadRequiredIntAttribute(element, "burnedEdgeBefore"),
            ReadRequiredIntAttribute(element, "burnedEdgeAfter"),
            ReadRequiredGuidAttribute(element, "expenseId"),
            ReadRequiredIntAttribute(element, "expenseAmount"),
            ReadAttribute(element, "logicalRevision"),
            ReadAttribute(element, "sourceRevision"),
            ReadAttribute(element, "ruleDigest"),
            ReadAttribute(element, "receiptDigest"));
        return CharacterCareerAttributeAdvanceRules.IsCoherent(receipt)
            ? receipt
            : throw new InvalidOperationException(
                "Attribute receipt authority is not coherent with the Core contract.");
    }

    private static HashSet<Guid> ReadCorrectedTransactionIds(XElement root)
    {
        XElement[] ledgers = root.Elements("careerattributeadvancementcorrections")
            .Take(2)
            .ToArray();
        if (ledgers.Length > 1)
        {
            throw new InvalidOperationException(
                "The saved runner has duplicate attribute-correction ledgers.");
        }
        HashSet<Guid> corrected = [];
        HashSet<Guid> correctionIds = [];
        foreach (XElement element in ledgers.SingleOrDefault()?.Elements("correction") ?? [])
        {
            Guid correctionId = ReadRequiredGuidAttribute(element, "id");
            Guid transactionId = ReadRequiredGuidAttribute(
                element,
                "originalTransactionId");
            XElement[] receiptAudits = FindReceiptAudits(root, transactionId);
            if (element.Attributes().Count() != 5
                || element.Elements().Count() != 1
                || !correctionIds.Add(correctionId)
                || receiptAudits.Length != 1)
            {
                throw new InvalidOperationException(
                    "Attribute correction authority is duplicate, malformed, or missing its original receipt.");
            }

            CharacterCareerAttributeAdvanceReceipt receipt = ParseReceiptAudit(receiptAudits[0]);
            CharacterCareerAttributeCorrectionPlan correction = new(
                correctionId,
                transactionId,
                receipt.ExpenseId,
                receipt.Identity,
                receipt.AttributeKarmaBefore,
                receipt.CharacterKarmaBefore,
                receipt.BurnedEdgePointsBefore,
                ReadExactText(element, "reason"),
                ReadAttribute(element, "receiptDigest"),
                ReadAttribute(element, "correctionDigest"));
            if (ReadAttribute(element, "target") != receipt.Identity.Abbreviation
                || correction.OriginalReceiptDigest != receipt.ReceiptDigest
                || !CharacterCareerAttributeAdvanceRules.IsCoherent(correction)
                || !corrected.Add(transactionId))
            {
                throw new InvalidOperationException(
                    "Attribute correction authority is foreign, incoherent, or duplicated for one transaction.");
            }
        }
        return corrected;
    }

    private static void AddReceiptAudit(
        XElement root,
        CharacterCareerAttributeAdvanceReceipt receipt,
        CharacterCareerAttributeAdvanceQuote postState)
    {
        XElement[] containers = root.Elements("careerattributeadvancementreceipts")
            .Take(2)
            .ToArray();
        if (containers.Length > 1)
        {
            throw new InvalidOperationException(
                "The saved runner has duplicate attribute-receipt ledgers.");
        }
        XElement ledger = containers.SingleOrDefault()
            ?? new XElement("careerattributeadvancementreceipts");
        if (ledger.Parent is null)
        {
            root.Add(ledger);
        }
        if (FindReceiptAudits(root, receipt.TransactionId).Length != 0)
        {
            throw new InvalidOperationException(
                "The attribute transaction already has a persisted receipt.");
        }
        string expenseDigest = CalculateExpenseDigest(root, receipt.ExpenseId);
        ledger.Add(new XElement(
            "receipt",
            new XAttribute("transactionId", receipt.TransactionId.ToString("D")),
            new XAttribute("target", receipt.Identity.Abbreviation),
            new XAttribute("kind", receipt.Identity.Kind.ToString()),
            new XAttribute("repairsBurnedEdge", receipt.RepairsBurnedEdge),
            new XAttribute("attributeKarmaBefore", receipt.AttributeKarmaBefore),
            new XAttribute("attributeKarmaAfter", receipt.AttributeKarmaAfter),
            new XAttribute("characterKarmaBefore", receipt.CharacterKarmaBefore),
            new XAttribute("characterKarmaAfter", receipt.CharacterKarmaAfter),
            new XAttribute("burnedEdgeBefore", receipt.BurnedEdgePointsBefore),
            new XAttribute("burnedEdgeAfter", receipt.BurnedEdgePointsAfter),
            new XAttribute("expenseId", receipt.ExpenseId.ToString("D")),
            new XAttribute("expenseAmount", receipt.ExpenseAmount),
            new XAttribute("logicalRevision", receipt.LogicalRevision),
            new XAttribute("sourceRevision", receipt.SourceRevision),
            new XAttribute("ruleDigest", receipt.RuleDigest),
            new XAttribute("receiptDigest", receipt.ReceiptDigest),
            new XAttribute("expenseDigest", expenseDigest),
            new XAttribute("postLogicalRevision", postState.LogicalRevision),
            new XAttribute("postSourceRevision", postState.SourceRevision),
            new XAttribute("postRuleDigest", postState.RuleDigest),
            new XAttribute(
                "projectionDigest",
                CalculateProjectionDigest(receipt, postState, expenseDigest))));
    }

    private static bool ReceiptAuditMatches(
        XElement root,
        XElement element,
        CharacterCareerAttributeAdvanceReceipt receipt,
        CharacterCareerAttributeAdvanceQuote postState)
    {
        string expenseDigest = CalculateExpenseDigest(root, receipt.ExpenseId);
        return element.Attributes().Count() == 21
            && ReadAttribute(element, "transactionId") == receipt.TransactionId.ToString("D")
            && ReadAttribute(element, "target") == receipt.Identity.Abbreviation
            && ReadAttribute(element, "kind") == receipt.Identity.Kind.ToString()
            && ReadAttribute(element, "repairsBurnedEdge")
                == (receipt.RepairsBurnedEdge ? "true" : "false")
            && ReadAttribute(element, "attributeKarmaBefore")
                == receipt.AttributeKarmaBefore.ToString(CultureInfo.InvariantCulture)
            && ReadAttribute(element, "attributeKarmaAfter")
                == receipt.AttributeKarmaAfter.ToString(CultureInfo.InvariantCulture)
            && ReadAttribute(element, "characterKarmaBefore")
                == receipt.CharacterKarmaBefore.ToString(CultureInfo.InvariantCulture)
            && ReadAttribute(element, "characterKarmaAfter")
                == receipt.CharacterKarmaAfter.ToString(CultureInfo.InvariantCulture)
            && ReadAttribute(element, "burnedEdgeBefore")
                == receipt.BurnedEdgePointsBefore.ToString(CultureInfo.InvariantCulture)
            && ReadAttribute(element, "burnedEdgeAfter")
                == receipt.BurnedEdgePointsAfter.ToString(CultureInfo.InvariantCulture)
            && ReadAttribute(element, "expenseId") == receipt.ExpenseId.ToString("D")
            && ReadAttribute(element, "expenseAmount")
                == receipt.ExpenseAmount.ToString(CultureInfo.InvariantCulture)
            && ReadAttribute(element, "logicalRevision") == receipt.LogicalRevision
            && ReadAttribute(element, "sourceRevision") == receipt.SourceRevision
            && ReadAttribute(element, "ruleDigest") == receipt.RuleDigest
            && ReadAttribute(element, "receiptDigest") == receipt.ReceiptDigest
            && ReadAttribute(element, "expenseDigest") == expenseDigest
            && ReadAttribute(element, "postLogicalRevision") == postState.LogicalRevision
            && ReadAttribute(element, "postSourceRevision") == postState.SourceRevision
            && ReadAttribute(element, "postRuleDigest") == postState.RuleDigest
            && ReadAttribute(element, "projectionDigest")
                == CalculateProjectionDigest(receipt, postState, expenseDigest);
    }

    private static string CalculateProjectionDigest(
        CharacterCareerAttributeAdvanceReceipt receipt,
        CharacterCareerAttributeAdvanceQuote postState,
        string expenseDigest)
    {
        string canonical = string.Join('\0',
            "chummer6.presentation.career-attribute-receipt/v1",
            receipt.ReceiptDigest,
            expenseDigest,
            postState.LogicalRevision,
            postState.SourceRevision,
            postState.RuleDigest);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static string CalculateExpenseDigest(XElement root, Guid expenseId)
    {
        XElement[] expenses = FindExpenses(root, expenseId);
        if (expenses.Length != 1)
        {
            throw new InvalidOperationException(
                "Attribute receipt authority requires exactly one bound expense.");
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                expenses[0].ToString(SaveOptions.DisableFormatting))))
            .ToLowerInvariant();
    }

    private static Guid ReadRequiredGuidAttribute(XElement element, string name)
    {
        XAttribute[] matches = element.Attributes(name).Take(2).ToArray();
        return matches.Length == 1
            && Guid.TryParse(matches[0].Value, out Guid value)
            && value != Guid.Empty
            ? value
            : throw new InvalidOperationException(
                $"Attribute ledger authority has an invalid or duplicate '{name}' value.");
    }

    private static int ReadRequiredIntAttribute(XElement element, string name)
    {
        string value = ReadAttribute(element, name);
        return int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Attribute ledger authority has an invalid '{name}' value.");
    }

    private static bool ReadRequiredBoolAttribute(XElement element, string name)
    {
        string value = ReadAttribute(element, name);
        return CareerSettingsProfileResolver.TryParseBoolean(value, out bool parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Attribute ledger authority has an invalid '{name}' value.");
    }

    private static string ReadAttribute(XElement element, string name)
    {
        XAttribute[] matches = element.Attributes(name).Take(2).ToArray();
        return matches.Length == 1
            ? matches[0].Value
            : throw new InvalidOperationException(
                $"Attribute receipt authority has a missing or duplicate '{name}' value.");
    }

    private static int CountCorrections(XElement root, Guid correctionId)
    {
        XElement[] containers = root.Elements("careerattributeadvancementcorrections")
            .Take(2)
            .ToArray();
        if (containers.Length > 1)
        {
            throw new InvalidOperationException(
                "The saved runner has duplicate attribute-correction ledgers.");
        }
        return containers.SingleOrDefault()?.Elements("correction")
            .Count(candidate => ReadRequiredGuidAttribute(candidate, "id") == correctionId) ?? 0;
    }

    private static void AddCorrectionAudit(
        XElement root,
        CharacterCareerAttributeCorrectionPlan correction)
    {
        XElement[] containers = root.Elements("careerattributeadvancementcorrections")
            .Take(2)
            .ToArray();
        if (containers.Length > 1)
        {
            throw new InvalidOperationException(
                "The saved runner has duplicate attribute-correction ledgers.");
        }
        XElement ledger = containers.SingleOrDefault()
            ?? new XElement("careerattributeadvancementcorrections");
        if (ledger.Parent is null)
        {
            root.Add(ledger);
        }
        ledger.Add(new XElement(
            "correction",
            new XAttribute("id", correction.CorrectionId.ToString("D")),
            new XAttribute(
                "originalTransactionId",
                correction.OriginalTransactionId.ToString("D")),
            new XAttribute("target", correction.Identity.Abbreviation),
            new XAttribute("receiptDigest", correction.OriginalReceiptDigest),
            new XAttribute("correctionDigest", correction.CorrectionDigest),
            new XElement("reason", correction.Reason)));
    }

    private static void SetRequiredValue(
        XElement parent,
        string name,
        int value,
        string owner)
    {
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"{owner} has a missing or duplicate <{name}> value.");
        }
        matches[0].Value = value.ToString(CultureInfo.InvariantCulture);
    }

    private static void ValidateRequestAuthority(string workspaceId, long contentRevision)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new InvalidOperationException(
                "A nonblank dossier identity is required for attribute advancement.");
        }
        if (contentRevision <= 0)
        {
            throw new InvalidOperationException(
                "A positive dossier revision is required for attribute advancement.");
        }
    }

    private static string Serialize(XDocument document)
    {
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        document.Save(writer, SaveOptions.DisableFormatting);
        return writer.ToString();
    }
}
