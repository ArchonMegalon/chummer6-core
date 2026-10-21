using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static class CharacterCareerInitiationTransaction
{
    public const int MaximumEntries = 1024;
    public const int MaximumLedgerBytes = 8 * 1024 * 1024;
    public static string LedgerRoot => CharacterCareerInitiationRules.Hash("sr5-career-initiation-ledger/v1");
    public static string EntryDigest(CharacterCareerInitiationEntry entry)
        => CharacterCareerInitiationRules.Digest(entry with { EntryDigest = "" });

    public static bool IsValidLedger(CharacterWorkspaceId workspace, long revision,
        IReadOnlyList<CharacterCareerInitiationEntry>? entries)
    {
        if (entries is null) return true;
        if (revision <= 0 || entries.Count > MaximumEntries) return false;
        try
        {
            var ids = new HashSet<Guid>();
            string previous = LedgerRoot;
            long lastRevision = 0, bytes = 0;
            foreach (var entry in entries)
            {
                if (entry is null || !CharacterCareerInitiationRules.IsValid(entry.Plan)
                    || entry.Plan.WorkspaceId != workspace.Value
                    || entry.Plan.TargetWorkspaceRevision > revision || entry.Plan.TargetWorkspaceRevision <= lastRevision
                    || !ids.Add(entry.Plan.TransactionId) || entry.PreviousEntryDigest != previous
                    || entry.GradeId != CharacterCareerInitiationRules.Id(entry.Plan.TransactionId, "grade")
                    || entry.BenefitId != CharacterCareerInitiationRules.Id(entry.Plan.TransactionId, "benefit")
                    || entry.KarmaExpenseId != CharacterCareerInitiationRules.Id(entry.Plan.TransactionId, "karma")
                    || entry.NuyenExpenseId != (entry.Plan.Quote.NuyenCost > 0
                        ? CharacterCareerInitiationRules.Id(entry.Plan.TransactionId, "nuyen") : (Guid?)null)
                    || !CharacterCareerInitiationRules.IsDigest(entry.CharacterDigestAfter)
                    || entry.EntryDigest != EntryDigest(entry)) return false;
                bytes += JsonSerializer.SerializeToUtf8Bytes(entry).Length;
                if (bytes > MaximumLedgerBytes) return false;
                previous = entry.EntryDigest;
                lastRevision = entry.Plan.TargetWorkspaceRevision;
            }
            return true;
        }
        catch (Exception e) when (e is ArgumentException or NullReferenceException or InvalidOperationException or OverflowException)
        { return false; }
    }

    public static CharacterCareerInitiationResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerInitiationPlan plan)
    {
        if (!CharacterCareerQualityTransaction.IsValidOwner(owner) || !CharacterCareerInitiationRules.IsValid(plan)
            || owner.NormalizedValue != plan.OwnerId || saved.Id.Value != plan.WorkspaceId)
            return new(CharacterCareerInitiationOutcome.Conflict);
        var ledger = saved.Document.AuxiliaryState.CharacterCareerInitiationTransactions;
        if (!IsValidLedger(saved.Id, saved.ContentRevision, ledger)) return new(CharacterCareerInitiationOutcome.Corrupt);
        var entry = ledger?.SingleOrDefault(x => x.Plan.TransactionId == plan.TransactionId);
        if (entry is null) return new(CharacterCareerInitiationOutcome.NotFound, CurrentRevision: saved.ContentRevision);
        if (!CharacterCareerInitiationRules.Same(entry.Plan, plan) || !saved.CanReplayReceipt(plan.TargetWorkspaceRevision))
            return new(CharacterCareerInitiationOutcome.IdempotencyConflict, CurrentRevision: saved.ContentRevision);
        if (!Observe(saved.Document.Content, entry)
            || saved.ContentRevision == plan.TargetWorkspaceRevision
                && CharacterCareerInitiationRules.Hash(saved.Document.Content) != entry.CharacterDigestAfter)
            return new(CharacterCareerInitiationOutcome.Corrupt, CurrentRevision: saved.ContentRevision);
        return new(CharacterCareerInitiationOutcome.Replayed, entry, saved.ContentRevision);
    }

    public static bool TryBuild(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerInitiationPlan plan, ICharacterCareerInitiationAdmission admission, string runtime,
        out WorkspaceDocument? replacement, out CharacterCareerInitiationEntry? entry)
    {
        replacement = null;
        entry = null;
        if (!CharacterCareerInitiationRules.IsValid(plan)
            || Lookup(owner.Owner, saved, plan).Outcome != CharacterCareerInitiationOutcome.NotFound
            || !owner.IsValid || owner.AuthorityInstanceId != plan.Quote.Binding.OwnerAuthorityInstanceId
            || owner.TransitionRevision != plan.Quote.Binding.OwnerTransitionRevision
            || saved.ContentRevision != plan.ExpectedWorkspaceRevision || saved.SavedRevision != plan.ExpectedSavedRevision
            || saved.Document.AuxiliaryStateDigest != plan.Quote.Binding.AuxiliaryDigest
            || CharacterCareerInitiationRules.Hash(saved.Document.Content) != plan.Quote.Binding.DocumentDigest
            || runtime != plan.Quote.Binding.RuntimeFingerprint
            || !CharacterCareerInitiationRules.Same(admission.Quote, plan.Quote) || !admission.IsCurrent()) return false;
        try
        {
            var ledger = saved.Document.AuxiliaryState.CharacterCareerInitiationTransactions ?? [];
            if (ledger.Count >= MaximumEntries) return false;
            string xml = Apply(saved.Document.Content, plan);
            var candidate = new CharacterCareerInitiationEntry(plan,
                CharacterCareerInitiationRules.Id(plan.TransactionId, "grade"),
                CharacterCareerInitiationRules.Id(plan.TransactionId, "benefit"),
                CharacterCareerInitiationRules.Id(plan.TransactionId, "karma"),
                plan.Quote.NuyenCost > 0 ? CharacterCareerInitiationRules.Id(plan.TransactionId, "nuyen") : null,
                CharacterCareerInitiationRules.Hash(xml), ledger.Count > 0 ? ledger[^1].EntryDigest : LedgerRoot, "");
            candidate = candidate with { EntryDigest = EntryDigest(candidate) };
            var appended = ledger.Append(candidate).ToArray();
            if (!Observe(xml, candidate) || !IsValidLedger(saved.Id, plan.TargetWorkspaceRevision, appended)
                || !admission.IsCurrent()) return false;
            replacement = saved.Document with { State = saved.Document.State with
            {
                Payload = xml,
                AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerInitiationTransactions = appended }
            } };
            entry = candidate;
            return true;
        }
        catch (Exception e) when (e is XmlException or InvalidDataException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException)
        { return false; }
    }

    private static string Apply(string xml, CharacterCareerInitiationPlan plan)
    {
        XElement root = CharacterCareerInitiationProjection.Parse(xml, "character");
        var q = plan.Quote;
        var ids = new[] { "grade", "benefit", "karma", "nuyen" }.Select(x => CharacterCareerInitiationRules.Id(plan.TransactionId, x)).ToHashSet();
        if (root.Descendants("guid").Any(x => Guid.TryParse(x.Value, out var id) && ids.Contains(id)))
            throw new InvalidDataException("initiation_identity_collision");
        CharacterCareerMagicEvaluator.AdvanceInitiationCaps(root, q.CurrentGrade, q.NextGrade);
        root.SetElementValue("initiategrade", q.NextGrade);
        root.SetElementValue("karma", q.KarmaBefore - q.KarmaCost);
        root.SetElementValue("nuyen", (q.NuyenBefore - q.NuyenCost).ToString(CultureInfo.InvariantCulture));
        Append(root, "initiationgrades", Grade(plan));
        Append(root, "metamagics", Benefit(plan));
        XElement? effect = Effect(plan);
        if (effect is not null) Append(root, "improvements", effect);
        Append(root, "expenses", Expense(plan, false));
        if (q.NuyenCost > 0) Append(root, "expenses", Expense(plan, true));
        // MAG/MAGAdept, adeptpowerpoints (purchased pool) and the existing powers remain byte-semantically unchanged.
        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static CharacterCareerInitiationBenefit Selected(CharacterCareerInitiationPlan plan)
        => plan.Quote.Benefits.Single(x => x.SourceId == plan.Quote.Selection.BenefitSourceId);
    private static string Id(CharacterCareerInitiationPlan plan, string purpose)
        => CharacterCareerInitiationRules.Id(plan.TransactionId, purpose).ToString("D");

    private static XElement Grade(CharacterCareerInitiationPlan plan)
        => new("initiationgrade",
            new XElement("guid", Id(plan, "grade")), new XElement("res", false),
            new XElement("grade", plan.Quote.NextGrade), new XElement("group", plan.Quote.Selection.Group),
            new XElement("ordeal", plan.Quote.Selection.Ordeal), new XElement("schooling", plan.Quote.Selection.Schooling),
            new XElement("notes", string.Join("\n", plan.Quote.Obligations)), new XElement("notesColor", "#000000"));

    private static XElement Benefit(CharacterCareerInitiationPlan plan)
    {
        var benefit = Selected(plan);
        XElement source = CharacterCareerInitiationProjection.Parse(benefit.SourceXml, "metamagic");
        return new XElement("metamagic",
            new XElement("sourceid", benefit.SourceId.ToString("D")), new XElement("guid", Id(plan, "benefit")),
            new XElement("name", benefit.Name), new XElement("source", benefit.Book), new XElement("page", benefit.Page),
            new XElement("grade", plan.Quote.NextGrade), new XElement("paidwithkarma", false),
            source.Element("bonus") is { } bonus ? new XElement(bonus) : new XElement("bonus"),
            new XElement("improvementsource", "Metamagic"), new XElement("notes", ""),
            new XElement("notesColor", "#000000"));
    }

    private static XElement? Effect(CharacterCareerInitiationPlan plan)
    {
        var benefit = Selected(plan);
        if (benefit.Kind != CharacterCareerInitiationBenefitKind.PowerPoint) return null;
        // Source-scoped grant, deliberately not the mystic adept's purchased PP scalar.
        return new XElement("improvement",
            new XElement("target", ""), new XElement("improvedname", ""), new XElement("sourcename", Id(plan, "benefit")),
            new XElement("min", 0), new XElement("max", 0), new XElement("aug", 0), new XElement("augmax", 0),
            new XElement("val", benefit.PowerPoints.ToString(CultureInfo.InvariantCulture)), new XElement("rating", 1),
            new XElement("exclude", ""), new XElement("condition", ""), new XElement("improvementttype", "AdeptPowerPoints"),
            new XElement("improvementsource", "Metamagic"), new XElement("custom", false), new XElement("customname", ""),
            new XElement("customid", ""), new XElement("customgroup", ""), new XElement("addtorating", 0),
            new XElement("enabled", 1), new XElement("order", 0), new XElement("notes", ""));
    }

    private static XElement Expense(CharacterCareerInitiationPlan plan, bool nuyen)
        => new("expense", new XElement("guid", Id(plan, nuyen ? "nuyen" : "karma")),
            new XElement("date", XmlConvert.ToString(plan.ExpenseDateLocal, XmlDateTimeSerializationMode.Unspecified)),
            new XElement("amount", (nuyen ? -plan.Quote.NuyenCost : -plan.Quote.KarmaCost).ToString(CultureInfo.InvariantCulture)),
            new XElement("reason", "Initiation " + plan.Quote.CurrentGrade + " -> " + plan.Quote.NextGrade + ": " + Selected(plan).Name),
            new XElement("type", nuyen ? "Nuyen" : "Karma"), new XElement("refund", false),
            // Absence of <undo> is the legacy representation of a non-undoable expense.
            // A generic legacy undo cannot reverse this multi-object purchase atomically.
            new XElement("forcecareervisible", true));

    private static void Append(XElement root, string name, XElement row)
    {
        XElement container = CharacterCareerInitiationProjection.Container(root, name);
        if (container.Parent is null) root.Add(container);
        container.Add(row);
    }

    private static bool Observe(string xml, CharacterCareerInitiationEntry entry)
    {
        try
        {
            var root = CharacterCareerInitiationProjection.Parse(xml, "character");
            var plan = entry.Plan;
            if (CharacterCareerMagicEvaluator.ValidateInitiationState(root) < plan.Quote.NextGrade) return false;
            bool Match(string container, string row, Guid id, XElement expected)
            {
                var nodes = CharacterCareerInitiationProjection.Container(root, container).Elements(row)
                    .Where(x => Guid.TryParse(x.Element("guid")?.Value, out Guid value) && value == id).ToArray();
                return nodes.Length == 1 && XNode.DeepEquals(nodes[0], expected);
            }
            if (!Match("initiationgrades", "initiationgrade", entry.GradeId, Grade(plan))
                || !Match("metamagics", "metamagic", entry.BenefitId, Benefit(plan))
                || !Match("expenses", "expense", entry.KarmaExpenseId, Expense(plan, false))
                || entry.NuyenExpenseId is { } nuyen && !Match("expenses", "expense", nuyen, Expense(plan, true))) return false;
            var effects = CharacterCareerInitiationProjection.Container(root, "improvements").Elements("improvement")
                .Where(x => x.Element("sourcename")?.Value == entry.BenefitId.ToString("D")).ToArray();
            XElement? expected = Effect(plan);
            return expected is null ? effects.Length == 0 : effects.Length == 1 && XNode.DeepEquals(effects[0], expected);
        }
        catch (Exception e) when (e is XmlException or InvalidDataException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException)
        { return false; }
    }
}
