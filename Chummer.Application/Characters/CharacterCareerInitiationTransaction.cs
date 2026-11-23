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
            var acquiredBenefits = new HashSet<Guid>();
            string previous = LedgerRoot;
            long lastRevision = 0, bytes = 0;
            foreach (var entry in entries)
            {
                if (entry is null || !CharacterCareerInitiationRules.IsValid(entry.Plan)
                    || !ValidRecompilations(entry.Plan)
                    || entry.Plan.WorkspaceId != workspace.Value
                    || entry.Plan.TargetWorkspaceRevision > revision || entry.Plan.TargetWorkspaceRevision <= lastRevision
                    || !ids.Add(entry.Plan.TransactionId) || entry.PreviousEntryDigest != previous
                    || !ValidGradeSnapshot(entry.Plan.Quote)
                    || entry.GradeId != GradeIdentity(entry.Plan)
                    || entry.BenefitId != BenefitIdentity(entry.Plan)
                    || entry.KarmaExpenseId != KarmaIdentity(entry.Plan)
                    || entry.Plan.Quote.Selection.Mode != CharacterCareerInitiationMode.GradeOnly
                        && !acquiredBenefits.Add(entry.GradeId)
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
        catch (Exception e) when (e is ArgumentException or NullReferenceException or InvalidOperationException or OverflowException
            or XmlException or InvalidDataException or FormatException)
        { return false; }
    }

    private static CharacterCareerSubmersionGrade DeferredGrade(CharacterCareerInitiationQuote quote)
        => quote.SubmersionGrades!.Single(g => g.GradeId == quote.Selection.TargetGradeId);
    private static Guid GradeIdentity(CharacterCareerInitiationPlan plan)
        => plan.Quote.Selection.Mode == CharacterCareerInitiationMode.DeferredBenefit
            ? DeferredGrade(plan.Quote).GradeId : CharacterCareerInitiationRules.Id(plan.TransactionId, "grade");
    private static Guid BenefitIdentity(CharacterCareerInitiationPlan plan)
        => plan.Quote.Selection.Mode == CharacterCareerInitiationMode.GradeOnly
            ? Guid.Empty : CharacterCareerInitiationRules.Id(plan.TransactionId, "benefit");
    private static Guid KarmaIdentity(CharacterCareerInitiationPlan plan)
        => plan.Quote.Selection.Mode == CharacterCareerInitiationMode.DeferredBenefit
            ? Guid.Empty : CharacterCareerInitiationRules.Id(plan.TransactionId, "karma");

    private static bool ValidGradeSnapshot(CharacterCareerInitiationQuote quote)
    {
        foreach (var grade in quote.SubmersionGrades ?? [])
        {
            var row = CharacterCareerInitiationProjection.Parse(grade.GradeXml, "initiationgrade");
            if (Guid.Parse(CharacterCareerInitiationProjection.Scalar(row, "guid")) != grade.GradeId
                || CharacterCareerInitiationProjection.Integer(row, "grade") != grade.Grade
                || !CharacterCareerComplexFormLearningTransaction.Flag(row, "res")) return false;
        }
        return true;
    }

    // XML-only imports have no local ledger to prove historical payment. Authenticate structural/source
    // state separately; where local receipts do exist, never ignore their persisted objects.
    public static bool ObserveHistory(WorkspaceStoredDocument saved)
    {
        var ledger = saved.Document.AuxiliaryState.CharacterCareerInitiationTransactions;
        return IsValidLedger(saved.Id, saved.ContentRevision, ledger)
            && (ledger ?? []).All(entry => Observe(saved.Document.Content, entry, ledger));
    }

    private static bool ValidRecompilations(CharacterCareerInitiationPlan plan)
    {
        foreach (var update in plan.Quote.RecompiledMetamagics ?? [])
        {
            var source = CharacterCareerInitiationProjection.Parse(update.SourceXml, "metamagic");
            var owned = CharacterCareerInitiationProjection.Parse(update.InstanceXml, "metamagic");
            if (Guid.Parse(CharacterCareerInitiationProjection.Scalar(source, "id")) != update.SourceId
                || Guid.Parse(CharacterCareerInitiationProjection.Scalar(owned, "sourceid")) != update.SourceId
                || Guid.Parse(CharacterCareerInitiationProjection.Scalar(owned, "guid")) != update.InstanceId
                || plan.Quote.Benefits.SingleOrDefault(x => x.SourceId == update.SourceId)?.SourceXml != update.SourceXml
                || !CharacterCareerInitiationProjection.MatchesMetamagicBonus(CharacterCareerInitiationProjection.Container(owned, "bonus"),
                    CharacterCareerInitiationProjection.Container(source, "bonus"))
                || !CharacterCareerInitiationProjection.TryCompileMetamagicEffects(source, update.InstanceId,
                    plan.Quote.CurrentGrade, out var before)
                || !CharacterCareerInitiationProjection.TryCompileMetamagicEffects(source, update.InstanceId,
                    plan.Quote.NextGrade, out var after)
                || !CharacterCareerInitiationProjection.EquivalentMetamagicEffects(
                    update.EffectsBefore.Select(x => CharacterCareerInitiationProjection.Parse(x, "improvement")), before)
                || !after.Select(x => x.ToString(SaveOptions.DisableFormatting)).SequenceEqual(update.EffectsAfter))
                return false;
        }
        return true;
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
        if (!ObserveHistory(saved)
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
            || !ObserveHistory(saved)
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
                GradeIdentity(plan), BenefitIdentity(plan), KarmaIdentity(plan),
                plan.Quote.NuyenCost > 0 ? CharacterCareerInitiationRules.Id(plan.TransactionId, "nuyen") : null,
                CharacterCareerInitiationRules.Hash(xml), ledger.Count > 0 ? ledger[^1].EntryDigest : LedgerRoot, "");
            candidate = candidate with { EntryDigest = EntryDigest(candidate) };
            var appended = ledger.Append(candidate).ToArray();
            if (!appended.All(item => Observe(xml, item, appended)) || !IsValidLedger(saved.Id, plan.TargetWorkspaceRevision, appended)
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
        // Validate first, then retain whitespace/unknown values while the typed edit mutates its owned nodes.
        root = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
        var q = plan.Quote;
        var ids = new[] { "grade", "benefit", "karma", "nuyen" }.Select(x => CharacterCareerInitiationRules.Id(plan.TransactionId, x)).ToHashSet();
        if (root.Descendants("guid").Any(x => Guid.TryParse(x.Value, out var id) && ids.Contains(id)))
            throw new InvalidDataException("initiation_identity_collision");
        foreach (var update in q.RecompiledMetamagics ?? [])
        {
            var owned = CharacterCareerInitiationProjection.Container(root, "metamagics").Elements("metamagic")
                .Single(x => Guid.TryParse(x.Element("guid")?.Value, out var id) && id == update.InstanceId);
            XElement[] before = CharacterCareerInitiationProjection.OwnedEffects(root, update.InstanceId);
            if (!XNode.DeepEquals(CharacterCareerInitiationProjection.Parse(owned.ToString(SaveOptions.DisableFormatting), "metamagic"),
                    CharacterCareerInitiationProjection.Parse(update.InstanceXml, "metamagic"))
                || !before.Select(x => CharacterCareerInitiationProjection.Parse(x.ToString(SaveOptions.DisableFormatting), "improvement")
                    .ToString(SaveOptions.DisableFormatting)).SequenceEqual(update.EffectsBefore))
                throw new InvalidDataException("initiation_metamagic_replacement_mismatch");
            foreach (var effect in before) effect.Remove();
            foreach (string effect in update.EffectsAfter)
                Append(root, "improvements", CharacterCareerInitiationProjection.Parse(effect, "improvement"));
        }
        bool resonance = q.Family == CharacterCareerInitiationFamily.Resonance;
        bool deferred = q.Selection.Mode == CharacterCareerInitiationMode.DeferredBenefit;
        bool gradeOnly = q.Selection.Mode == CharacterCareerInitiationMode.GradeOnly;
        if (deferred)
        {
            if (CharacterCareerResonanceEvaluator.ValidateSubmersionState(root) != q.CurrentGrade)
                throw new InvalidDataException("submersion_grade_changed");
            var target = DeferredGrade(q);
            var current = CharacterCareerResonanceEvaluator.SubmersionGrades(root, [])
                .SingleOrDefault(g => g.GradeId == target.GradeId);
            if (current is not { FirstBenefitPending: true } || current.Blockers.Count != 0
                || current.Grade != target.Grade
                || !XNode.DeepEquals(CharacterCareerInitiationProjection.Parse(current.GradeXml, "initiationgrade"),
                    CharacterCareerInitiationProjection.Parse(target.GradeXml, "initiationgrade")))
                throw new InvalidDataException("submersion_target_grade_changed");
        }
        else
        {
            if (resonance) CharacterCareerResonanceEvaluator.AdvanceSubmersionCap(root, q.CurrentGrade, q.NextGrade);
            else CharacterCareerMagicEvaluator.AdvanceInitiationCaps(root, q.CurrentGrade, q.NextGrade);
            root.SetElementValue(resonance ? "submersiongrade" : "initiategrade", q.NextGrade);
            root.SetElementValue("karma", q.KarmaBefore - q.KarmaCost);
            root.SetElementValue("nuyen", (q.NuyenBefore - q.NuyenCost).ToString(CultureInfo.InvariantCulture));
            Append(root, "initiationgrades", Grade(plan));
        }
        if (!gradeOnly)
        {
            Append(root, "metamagics", Benefit(plan));
            foreach (var effect in Effects(plan)) Append(root, "improvements", effect);
        }
        if (!deferred)
        {
            Append(root, "expenses", Expense(plan, false));
            if (q.NuyenCost > 0) Append(root, "expenses", Expense(plan, true));
        }
        // MAG/MAGAdept, adeptpowerpoints (purchased pool) and the existing powers remain byte-semantically unchanged.
        return CharacterCareerLearningDocument5703.PreserveEnvelope(xml, root.ToString(SaveOptions.DisableFormatting));
    }

    private static CharacterCareerInitiationBenefit Selected(CharacterCareerInitiationPlan plan)
        => plan.Quote.Benefits.Single(x => x.SourceId == plan.Quote.Selection.BenefitSourceId);
    private static string Id(CharacterCareerInitiationPlan plan, string purpose)
        => CharacterCareerInitiationRules.Id(plan.TransactionId, purpose).ToString("D");

    private static XElement Grade(CharacterCareerInitiationPlan plan)
        => plan.Quote.Selection.Mode == CharacterCareerInitiationMode.DeferredBenefit
            ? CharacterCareerInitiationProjection.Parse(DeferredGrade(plan.Quote).GradeXml, "initiationgrade")
            : new("initiationgrade",
            new XElement("guid", Id(plan, "grade")), new XElement("res", plan.Quote.Family == CharacterCareerInitiationFamily.Resonance),
            new XElement("grade", plan.Quote.NextGrade), new XElement("group", plan.Quote.Selection.Group),
            new XElement("ordeal", plan.Quote.Selection.Ordeal), new XElement("schooling", plan.Quote.Selection.Schooling),
            new XElement("notes", string.Join("\n", plan.Quote.Obligations)), new XElement("notesColor", "#000000"));

    private static XElement Benefit(CharacterCareerInitiationPlan plan)
    {
        var benefit = Selected(plan);
        XElement source = CharacterCareerInitiationProjection.Parse(benefit.SourceXml,
            benefit.Kind == CharacterCareerInitiationBenefitKind.Echo ? "echo" : "metamagic");
        return new XElement("metamagic",
            new XElement("sourceid", benefit.SourceId.ToString("D")), new XElement("guid", Id(plan, "benefit")),
            new XElement("name", benefit.Name), new XElement("source", benefit.Book), new XElement("page", benefit.Page),
            new XElement("grade", plan.Quote.Selection.Mode == CharacterCareerInitiationMode.DeferredBenefit
                ? DeferredGrade(plan.Quote).Grade : plan.Quote.NextGrade), new XElement("paidwithkarma", false),
            source.Element("bonus") is { } bonus ? new XElement(bonus) : new XElement("bonus"),
            new XElement("improvementsource", benefit.Kind == CharacterCareerInitiationBenefitKind.Echo ? "Echo" : "Metamagic"), new XElement("notes", ""),
            new XElement("notesColor", "#000000"));
    }

    private static XElement[] Effects(CharacterCareerInitiationPlan plan)
    {
        var benefit = Selected(plan);
        if (benefit.Kind == CharacterCareerInitiationBenefitKind.Echo) return [];
        if (benefit.Kind != CharacterCareerInitiationBenefitKind.PowerPoint)
        {
            if (!CharacterCareerInitiationProjection.TryCompileMetamagicEffects(
                CharacterCareerInitiationProjection.Parse(benefit.SourceXml, "metamagic"),
                CharacterCareerInitiationRules.Id(plan.TransactionId, "benefit"), plan.Quote.NextGrade, out var effects))
                throw new InvalidDataException("initiation_benefit_effect_unsupported");
            return effects;
        }
        // Source-scoped grant, deliberately not the mystic adept's purchased PP scalar.
        return [new XElement("improvement",
            new XElement("target", ""), new XElement("improvedname", ""), new XElement("sourcename", Id(plan, "benefit")),
            new XElement("min", 0), new XElement("max", 0), new XElement("aug", 0), new XElement("augmax", 0),
            new XElement("val", benefit.PowerPoints.ToString(CultureInfo.InvariantCulture)), new XElement("rating", 1),
            new XElement("exclude", ""), new XElement("condition", ""), new XElement("improvementttype", "AdeptPowerPoints"),
            new XElement("improvementsource", "Metamagic"), new XElement("custom", false), new XElement("customname", ""),
            new XElement("customid", ""), new XElement("customgroup", ""), new XElement("addtorating", 0),
            new XElement("enabled", 1), new XElement("order", 0), new XElement("notes", ""))];
    }

    private static XElement Expense(CharacterCareerInitiationPlan plan, bool nuyen)
        => new("expense", new XElement("guid", Id(plan, nuyen ? "nuyen" : "karma")),
            new XElement("date", XmlConvert.ToString(plan.ExpenseDateLocal, XmlDateTimeSerializationMode.Unspecified)),
            new XElement("amount", (nuyen ? -plan.Quote.NuyenCost : -plan.Quote.KarmaCost).ToString(CultureInfo.InvariantCulture)),
            new XElement("reason", (plan.Quote.Family == CharacterCareerInitiationFamily.Resonance ? "Submersion " : "Initiation ") + plan.Quote.CurrentGrade + " -> " + plan.Quote.NextGrade
                + (plan.Quote.Selection.Mode == CharacterCareerInitiationMode.GradeOnly ? "" : ": " + Selected(plan).Name)),
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

    private static bool Observe(string xml, CharacterCareerInitiationEntry entry,
        IReadOnlyList<CharacterCareerInitiationEntry>? ledger)
    {
        try
        {
            var root = CharacterCareerInitiationProjection.Parse(xml, "character");
            var plan = entry.Plan;
            if ((plan.Quote.Family == CharacterCareerInitiationFamily.Resonance
                ? CharacterCareerResonanceEvaluator.ValidateSubmersionState(root)
                : CharacterCareerMagicEvaluator.ValidateInitiationState(root)) < plan.Quote.NextGrade) return false;
            bool Match(string container, string row, Guid id, XElement expected, bool allowParsedSourceBonus = false)
            {
                var nodes = CharacterCareerInitiationProjection.Container(root, container).Elements(row)
                    .Where(x => Guid.TryParse(x.Element("guid")?.Value, out Guid value) && value == id).ToArray();
                if (nodes.Length != 1 || root.Descendants("guid").Count(x => Guid.TryParse(x.Value, out var value) && value == id) != 1)
                    return false;
                if (XNode.DeepEquals(nodes[0], expected)) return true;
                if (!allowParsedSourceBonus) return false;
                // A native save can discard indentation in the originally copied
                // source bonus. Recovery still compares the complete owned row,
                // with only that exact source-derived representation as an alternate.
                var parsedExpected = new XElement(expected);
                var bonus = CharacterCareerInitiationProjection.Container(parsedExpected, "bonus");
                bonus.ReplaceWith(XElement.Parse(bonus.ToString(SaveOptions.DisableFormatting)));
                return XNode.DeepEquals(nodes[0], parsedExpected);
            }
            if (!Match("initiationgrades", "initiationgrade", entry.GradeId, Grade(plan))
                || entry.BenefitId != Guid.Empty && !Match("metamagics", "metamagic", entry.BenefitId, Benefit(plan), allowParsedSourceBonus: true)
                || entry.KarmaExpenseId != Guid.Empty && !Match("expenses", "expense", entry.KarmaExpenseId, Expense(plan, false))
                || entry.NuyenExpenseId is { } nuyen && !Match("expenses", "expense", nuyen, Expense(plan, true))) return false;
            IEnumerable<XElement> LatestEffects(Guid id, IEnumerable<XElement> original)
            {
                var latest = (ledger ?? []).Where(x => x.Plan.TargetWorkspaceRevision > plan.TargetWorkspaceRevision)
                    .SelectMany(x => x.Plan.Quote.RecompiledMetamagics ?? []).LastOrDefault(x => x.InstanceId == id);
                return latest is null ? original : latest.EffectsAfter.Select(x => CharacterCareerInitiationProjection.Parse(x, "improvement"));
            }
            // Native saves may change Magic effect encodings without changing their meaning.
            // Compare history semantically; keep the exact quote snapshots for replacement and receipts.
            bool MatchesEffects(Guid id, IEnumerable<XElement> expected)
                => plan.Quote.Family == CharacterCareerInitiationFamily.Magic
                    ? CharacterCareerInitiationProjection.EquivalentMetamagicEffects(
                        CharacterCareerInitiationProjection.OwnedEffects(root, id), LatestEffects(id, expected))
                    : CharacterCareerInitiationProjection.OwnedEffects(root, id).Select(x => x.ToString(SaveOptions.DisableFormatting))
                        .SequenceEqual(LatestEffects(id, expected).Select(x => x.ToString(SaveOptions.DisableFormatting)));
            if (entry.BenefitId != Guid.Empty && !MatchesEffects(entry.BenefitId, Effects(plan))) return false;
            // Grade-only receipts continue to describe their original purchase after a later fill.
            // The later benefit (if present) is validated by canonical state and its own receipt.
            if (entry.BenefitId == Guid.Empty && root.Descendants("guid").Any(x => Guid.TryParse(x.Value, out var id)
                && id == CharacterCareerInitiationRules.Id(plan.TransactionId, "benefit"))) return false;
            if (entry.KarmaExpenseId == Guid.Empty && root.Descendants("guid").Any(x => Guid.TryParse(x.Value, out var id)
                && id == CharacterCareerInitiationRules.Id(plan.TransactionId, "karma"))) return false;
            foreach (var update in plan.Quote.RecompiledMetamagics ?? [])
            {
                if (!Match("metamagics", "metamagic", update.InstanceId,
                        CharacterCareerInitiationProjection.Parse(update.InstanceXml, "metamagic"))
                    || !MatchesEffects(update.InstanceId, update.EffectsAfter.Select(x =>
                        CharacterCareerInitiationProjection.Parse(x, "improvement")))) return false;
            }
            return true;
        }
        catch (Exception e) when (e is XmlException or InvalidDataException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException)
        { return false; }
    }
}
