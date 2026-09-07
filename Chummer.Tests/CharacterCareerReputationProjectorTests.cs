using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerReputationProjectorTests
{
    [TestMethod]
    public void Earned_karma_uses_each_expense_rounded_away_from_zero_not_available_or_print_totals()
    {
        var root = Root();
        root.Add(new XElement("karma", 999), new XElement("totalkarma", 9999));
        root.Element("expenses")!.Add(
            Expense("0.1"), Expense("0.1"), Expense("35"), Expense("-2.1", forced: true),
            Expense("-9"), Expense("100", refund: true), Expense("50", "Nuyen"),
            Expense("-100", refund: true, forced: true));
        var snapshot = Read(root);
        Assert.AreEqual(34, snapshot.Reputation.Inputs.CareerKarma);
        CollectionAssert.AreEqual(new[] { 1, 1, 35, -3, 0, 0, 0, 0 },
            snapshot.Expenses.Select(row => row.CareerKarmaContribution).ToArray());
        Assert.AreEqual(4, snapshot.Reputation.TotalStreetCred);
        Assert.AreEqual(3, snapshot.Reputation.TotalPublicAwareness);
    }

    [TestMethod]
    public void Improvements_are_filtered_grouped_selected_and_rounded_only_after_aggregation()
    {
        var root = Root();
        root.Element("improvements")!.Add(
            Improvement("StreetCred", "0.2"), Improvement("StreetCred", "0.2"),
            Improvement("StreetCred", "100", enabled: 0),
            Improvement("StreetCred", "100", addToRating: 1),
            Improvement("StreetCred", "100", condition: "create"),
            Improvement("StreetCred", "0.2", condition: "career", enabled: 2, addToRating: -1),
            Improvement("StreetCred", "0.2", condition: "requires-situation"),
            Improvement("Notoriety", "-0.1"), Improvement("PublicAwareness", "1.1"),
            Improvement("StreetCredMultiplier", "-0.1"));
        root.Element("expenses")!.Add(Expense("27"));
        var snapshot = Read(root);
        Assert.AreEqual(1, snapshot.Reputation.Inputs.StreetCredImprovement);
        Assert.AreEqual(-1, snapshot.Reputation.Inputs.NotorietyImprovement);
        Assert.AreEqual(-1, snapshot.Reputation.Inputs.StreetCredDivisorAdjustment);
        Assert.AreEqual(5, snapshot.Reputation.TotalPublicAwareness);
        CollectionAssert.AreEqual(new[] { 0, 1, 5, 7, 8, 9 },
            snapshot.Improvements.Where(row => row.Selected).Select(row => row.ImprovementIndex).ToArray());
    }

    [TestMethod]
    public void Unique_groups_take_highest_per_improved_name_and_custom_partition_not_custom_display_group()
    {
        var root = Root();
        root.Element("improvements")!.Add(
            Improvement("StreetCred", "-3", unique: "same", name: "a"),
            Improvement("StreetCred", "-2", unique: "same", name: "a"),
            Improvement("StreetCred", "4", unique: "same", name: "b"),
            Improvement("StreetCred", "5", unique: "same", name: "a", custom: true),
            Improvement("StreetCred", "6", unique: "same", name: "a", custom: true));
        root.Element("improvements")!.Elements().Last().Add(new XElement("customgroup", "other display group"));
        var snapshot = Read(root);
        Assert.AreEqual(8, snapshot.Reputation.Inputs.StreetCredImprovement);
        CollectionAssert.AreEqual(new[] { 1, 2, 4 },
            snapshot.Improvements.Where(row => row.Selected).Select(row => row.ImprovementIndex).ToArray());
    }

    [TestMethod]
    [DataRow("precedence0", 8)]
    [DataRow("precedence1", 12)]
    public void Precedence_replaces_ordinary_sum_only_if_higher_and_excludes_other_unique_groups(string kind, int expected)
    {
        var root = Root();
        root.Element("improvements")!.Add(
            Improvement("Notoriety", "3"), Improvement("Notoriety", "4", unique: kind),
            Improvement("Notoriety", "7", unique: kind), Improvement("Notoriety", "1", unique: "precedence-1"),
            Improvement("Notoriety", "100", unique: "unrelated"));
        Assert.AreEqual(expected, Read(root).Reputation.Inputs.NotorietyImprovement);
        root.Element("improvements")!.Elements().First().Element("val")!.Value = "20";
        var strongerOrdinary = Read(root);
        Assert.AreEqual(20, strongerOrdinary.Reputation.Inputs.NotorietyImprovement);
        CollectionAssert.AreEqual(new[] { 0 }, strongerOrdinary.Improvements.Where(row => row.Selected)
            .Select(row => row.ImprovementIndex).ToArray());
    }

    [TestMethod]
    public void Precedence_zero_wins_over_one_and_custom_precedence_is_an_ordinary_unique_name()
    {
        var root = Root();
        root.Element("improvements")!.Add(
            Improvement("StreetCred", "2", unique: "precedence0"),
            Improvement("StreetCred", "100", unique: "precedence1"),
            Improvement("StreetCred", "3", unique: "precedence0", custom: true),
            Improvement("StreetCred", "5", unique: "precedence0", custom: true),
            Improvement("StreetCred", "7", unique: "precedence1", custom: true));
        Assert.AreEqual(14, Read(root).Reputation.Inputs.StreetCredImprovement);
    }

    [TestMethod]
    public void Erased_uses_selected_presence_even_at_zero_and_preserves_precedence_tie_semantics()
    {
        var root = Root();
        root.Element("publicawareness")!.Value = "10";
        root.Element("improvements")!.Add(Improvement("Erased", "0", unique: "precedence0"));
        Assert.IsFalse(Read(root).Reputation.Inputs.Erased, "Zero precedence ties an empty ordinary selection.");
        root.Element("improvements")!.Add(Improvement("Erased", "0"));
        var snapshot = Read(root);
        Assert.IsTrue(snapshot.Reputation.Inputs.Erased);
        Assert.AreEqual(1, snapshot.Reputation.TotalPublicAwareness);
        Assert.IsFalse(snapshot.Improvements[0].Selected);
        Assert.IsTrue(snapshot.Improvements[1].Selected);
        root.Element("improvements")!.Elements().Last().Element("enabled")!.Value = "0";
        Assert.IsFalse(Read(root).Reputation.Inputs.Erased);
    }

    [TestMethod]
    public void Equal_unique_values_choose_first_saved_row_and_do_not_deduplicate_ordinary_instances()
    {
        var root = Root();
        root.Element("improvements")!.Add(Improvement("StreetCred", "2", unique: "same"),
            Improvement("StreetCred", "2", unique: "same"), Improvement("StreetCred", "1"),
            Improvement("StreetCred", "1"));
        var snapshot = Read(root);
        Assert.AreEqual(4, snapshot.Reputation.Inputs.StreetCredImprovement);
        CollectionAssert.AreEqual(new[] { 0, 2, 3 }, snapshot.Improvements.Where(row => row.Selected)
            .Select(row => row.ImprovementIndex).ToArray());
    }

    [TestMethod]
    public void Erased_custom_unique_follows_pinned_contributor_cache_not_numeric_sum_or_any_enabled()
    {
        var root = Root();
        root.Element("improvements")!.Add(Improvement("Erased", "1", unique: "custom", name: "named", custom: true));
        var customOnly = Read(root);
        Assert.IsFalse(customOnly.Reputation.Inputs.Erased);
        Assert.IsFalse(customOnly.Improvements[0].Selected);
        root.Element("improvements")!.Add(Improvement("Erased", "0", unique: "precedence0", name: "named"));
        var withNormalPartition = Read(root);
        Assert.IsTrue(withNormalPartition.Reputation.Inputs.Erased);
        Assert.IsTrue(withNormalPartition.Improvements[0].Selected);
        Assert.IsFalse(withNormalPartition.Improvements[1].Selected);
    }

    [TestMethod]
    public void Legacy_unique_minimum_sentinel_is_not_a_selected_contributor()
    {
        var root = Root();
        string minimum = decimal.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        root.Element("improvements")!.Add(Improvement("StreetCred", minimum, unique: "min"),
            Improvement("Erased", minimum, unique: "min"));
        var snapshot = Read(root);
        Assert.AreEqual(0, snapshot.Reputation.Inputs.StreetCredImprovement);
        Assert.IsFalse(snapshot.Reputation.Inputs.Erased);
        Assert.IsFalse(snapshot.Improvements.Any(row => row.Selected));
    }

    [TestMethod]
    public void Precedence_minus_one_minimum_is_a_real_stacked_contributor_not_the_winner_sentinel()
    {
        var root = Root();
        root.Element("improvements")!.Add(Improvement("Notoriety", "-1"),
            Improvement("Notoriety", decimal.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), unique: "precedence0"),
            Improvement("Notoriety", decimal.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture), unique: "precedence-1"));
        var snapshot = Read(root);
        Assert.AreEqual(0, snapshot.Reputation.Inputs.NotorietyImprovement);
        CollectionAssert.AreEqual(new[] { 1, 2 }, snapshot.Improvements.Where(row => row.Selected)
            .Select(row => row.ImprovementIndex).ToArray());
    }

    [TestMethod]
    public void Normal_and_custom_partitions_combine_before_adding_each_named_group_to_the_total()
    {
        var root = Root();
        root.Element("improvements")!.Add(Improvement("StreetCred", "1", name: "first"),
            Improvement("StreetCred", decimal.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), name: "second"),
            Improvement("StreetCred", decimal.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture), name: "second", custom: true));
        Assert.AreEqual(1, Read(root).Reputation.Inputs.StreetCredImprovement);
    }

    [TestMethod]
    public void Saved_optional_improvement_and_refund_defaults_are_explicit_but_missing_awards_are_not_zero()
    {
        var root = Root();
        root.Element("expenses")!.Add(new XElement("expense", new XElement("type", "Karma"), new XElement("amount", 20)));
        root.Element("improvements")!.Add(new XElement("improvement",
            new XElement("improvementttype", "StreetCred"), new XElement("val", 3)));
        Assert.AreEqual(6, Read(root).Reputation.TotalStreetCred);
        root.Element("streetcred")!.Remove();
        Reject(root);
    }

    [TestMethod]
    [DataRow("award-duplicate")]
    [DataRow("award-nested")]
    [DataRow("award-attribute")]
    [DataRow("award-namespaced")]
    [DataRow("award-empty")]
    [DataRow("award-space")]
    [DataRow("container-duplicate")]
    [DataRow("container-missing")]
    [DataRow("container-text")]
    [DataRow("expense-kind")]
    [DataRow("expense-duplicate-kind")]
    [DataRow("expense-amount")]
    [DataRow("expense-flag")]
    [DataRow("improvement-value")]
    [DataRow("improvement-duplicate")]
    [DataRow("improvement-type-case")]
    [DataRow("improvement-enabled")]
    [DataRow("improvement-custom")]
    [DataRow("row-namespace")]
    public void Ambiguous_or_malformed_relevant_inputs_never_produce_guessed_totals(string mode)
    {
        var root = Root();
        XElement award = root.Element("streetcred")!;
        XElement expense = Expense("20");
        XElement improvement = Improvement("StreetCred", "3");
        root.Element("expenses")!.Add(expense);
        root.Element("improvements")!.Add(improvement);
        switch (mode)
        {
            case "award-duplicate": root.Add(new XElement(award)); break;
            case "award-nested": award.Add(new XElement("value", 1)); break;
            case "award-attribute": award.Add(new XAttribute("value", 1)); break;
            case "award-namespaced": award.Name = XName.Get("streetcred", "urn:foreign"); break;
            case "award-empty": award.Value = ""; break;
            case "award-space": award.Value = " 1 "; break;
            case "container-duplicate": root.Add(new XElement("expenses")); break;
            case "container-missing": root.Element("improvements")!.Remove(); break;
            case "container-text": root.Element("expenses")!.Add("unparsed input"); break;
            case "expense-kind": expense.Element("type")!.Value = "unknown"; break;
            case "expense-duplicate-kind": expense.Add(new XElement("type", "Nuyen")); break;
            case "expense-amount": expense.Element("amount")!.Value = "1,000"; break;
            case "expense-flag": expense.Element("refund")!.Value = "unknown"; break;
            case "improvement-value": improvement.Element("val")!.Value = "Rating * 2"; break;
            case "improvement-duplicate": improvement.Add(new XElement("enabled", 0)); break;
            case "improvement-type-case": improvement.Element("improvementttype")!.Value = "streetcred"; break;
            case "improvement-enabled": improvement.Element("enabled")!.Value = "True"; break;
            case "improvement-custom": improvement.Element("custom")!.Value = "sometimes"; break;
            case "row-namespace": improvement.Name = XName.Get("improvement", "urn:foreign"); break;
        }
        Reject(root);
    }

    [TestMethod]
    [DataRow("expense-int")]
    [DataRow("expense-sum")]
    [DataRow("improvement-decimal")]
    [DataRow("improvement-int")]
    [DataRow("zero-divisor")]
    [DataRow("negative-burn")]
    public void Overflow_and_impossible_arithmetic_fail_closed(string mode)
    {
        var root = Root();
        switch (mode)
        {
            case "expense-int": root.Element("expenses")!.Add(Expense("2147483647.1")); break;
            case "expense-sum": root.Element("expenses")!.Add(Expense("2147483647"), Expense("1")); break;
            case "improvement-decimal": root.Element("improvements")!.Add(Improvement("StreetCred", decimal.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)), Improvement("StreetCred", "1")); break;
            case "improvement-int": root.Element("improvements")!.Add(Improvement("StreetCred", "2147483647.1")); break;
            case "zero-divisor": root.Element("improvements")!.Add(Improvement("StreetCredMultiplier", "-10")); break;
            case "negative-burn": root.Element("burntstreetcred")!.Value = "-1"; break;
        }
        Reject(root);
    }

    [TestMethod]
    public void Document_and_profile_bindings_are_not_interchangeable_and_reads_never_mutate_input()
    {
        var root = Root();
        var saved = Saved(root);
        string original = JsonSerializer.Serialize(saved);
        Assert.IsTrue(CharacterCareerReputationProjector.TryRead(saved, new Resolver(), out var first, out _));
        Assert.IsTrue(CharacterCareerReputationProjector.TryRead(saved, new Resolver(), out var again, out _));
        Assert.AreEqual(JsonSerializer.Serialize(first), JsonSerializer.Serialize(again));
        Assert.AreEqual(original, JsonSerializer.Serialize(saved));
        root.Add(new XElement("notes", "unrelated but still source bound"));
        var different = Read(root);
        Assert.AreNotEqual(first!.SourceDigest, different.SourceDigest);
        Assert.AreEqual(first.Reputation, different.Reputation);
        var reprofiled = Read(Root(), new Resolver { Raw = "different full profile inputs" });
        Assert.AreEqual(first.SourceDigest, reprofiled.SourceDigest);
        Assert.AreNotEqual(first.RuleStateDigest, reprofiled.RuleStateDigest);
        var manual = Read(Root(), new Resolver { Calculated = false });
        Assert.AreEqual(1, manual.Reputation.TotalPublicAwareness);
        Assert.AreNotEqual(first.RuleStateDigest, manual.RuleStateDigest);
    }

    [TestMethod]
    public void Auxiliary_state_and_revision_remain_separate_from_the_complete_payload_binding()
    {
        var saved = Saved(Root());
        Assert.IsTrue(CharacterCareerReputationProjector.TryRead(saved, new Resolver(), out var before, out _));
        var changed = saved with { Document = saved.Document with { State = saved.Document.State with
            { AuxiliaryState = new WorkspaceDocumentAuxiliaryState(CharacterAfterRunRewardReceipts: []) } } };
        Assert.IsTrue(CharacterCareerReputationProjector.TryRead(changed, new Resolver(), out var after, out _));
        Assert.AreEqual(before!.SourceDigest, after!.SourceDigest);
        Assert.AreNotEqual(before.AuxiliaryStateDigest, after.AuxiliaryStateDigest);
        Assert.AreEqual(before.Reputation, after.Reputation);
        Assert.IsTrue(CharacterCareerReputationProjector.TryRead(saved with { ContentRevision = 4, SavedRevision = 4 },
            new Resolver(), out var later, out _));
        Assert.AreEqual(before.SourceDigest, later!.SourceDigest);
        Assert.AreNotEqual(before.ContentRevision, later.ContentRevision);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("empty-binding")]
    [DataRow("drift")]
    [DataRow("policy-drift")]
    [DataRow("binding-drift")]
    public void Missing_or_changed_profile_cannot_produce_a_snapshot(string mode)
    {
        var resolver = new Resolver { Failure = mode };
        Assert.IsFalse(CharacterCareerReputationProjector.TryRead(Saved(Root()), resolver, out var snapshot, out var error));
        Assert.IsNull(snapshot);
        StringAssert.StartsWith(error, "reputation_source_");
    }

    [TestMethod]
    [DataRow("json")]
    [DataRow("ruleset")]
    [DataRow("payload")]
    [DataRow("schema")]
    [DataRow("dirty")]
    [DataRow("revision")]
    [DataRow("creation")]
    [DataRow("dtd")]
    [DataRow("namespace")]
    public void Unsupported_envelopes_or_lifecycle_states_do_not_read_profile_or_write_anything(string mode)
    {
        var saved = Saved(Root());
        saved = mode switch
        {
            "json" => saved with { Document = saved.Document with { Format = WorkspaceDocumentFormat.Json } },
            "ruleset" => saved with { Document = saved.Document with { State = saved.Document.State with { RulesetId = "sr6" } } },
            "payload" => saved with { Document = saved.Document with { State = saved.Document.State with { PayloadKind = "sr6/chum6-xml" } } },
            "schema" => saved with { Document = saved.Document with { State = saved.Document.State with { SchemaVersion = 2 } } },
            "dirty" => saved with { SavedRevision = 1 },
            "revision" => saved with { ContentRevision = long.MaxValue, SavedRevision = long.MaxValue },
            "creation" => ReplaceXml(saved, saved.Document.Content.Replace("<created>True", "<created>False", StringComparison.Ordinal)),
            "dtd" => ReplaceXml(saved, "<!DOCTYPE character [<!ENTITY attack '1'>]>" + saved.Document.Content),
            "namespace" => ReplaceXml(saved, saved.Document.Content.Replace("<character>", "<character xmlns='urn:foreign'>", StringComparison.Ordinal)),
            _ => throw new InvalidOperationException()
        };
        var resolver = new Resolver();
        Assert.IsFalse(CharacterCareerReputationProjector.TryRead(saved, resolver, out var snapshot, out _));
        Assert.IsNull(snapshot);
        Assert.AreEqual(0, resolver.Creations);
    }

    private static void Reject(XElement root)
    {
        Assert.IsFalse(CharacterCareerReputationProjector.TryRead(Saved(root), new Resolver(), out var snapshot, out var error));
        Assert.IsNull(snapshot);
        Assert.IsFalse(string.IsNullOrEmpty(error));
    }

    private static CharacterCareerReputationSnapshot Read(XElement root, Resolver? resolver = null)
    {
        Assert.IsTrue(CharacterCareerReputationProjector.TryRead(Saved(root), resolver ?? new Resolver(), out var snapshot, out var error), error);
        return snapshot!;
    }

    private static XElement Root() => XElement.Parse("""
        <character><settings>test-profile</settings><created>True</created>
        <streetcred>1</streetcred><notoriety>2</notoriety><publicawareness>1</publicawareness>
        <burntstreetcred>0</burntstreetcred><expenses/><improvements/></character>
        """);

    private static XElement Expense(string amount, string type = "Karma", bool refund = false, bool forced = false)
        => new("expense", new XElement("type", type), new XElement("amount", amount),
            new XElement("refund", refund), new XElement("forcecareervisible", forced));

    private static XElement Improvement(string type, string value, string unique = "", string name = "",
        bool custom = false, int enabled = 1, int addToRating = 0, string condition = "")
        => new("improvement", new XElement("improvementttype", type), new XElement("val", value),
            new XElement("unique", unique), new XElement("improvedname", name), new XElement("custom", custom),
            new XElement("enabled", enabled), new XElement("addtorating", addToRating), new XElement("condition", condition));

    private static WorkspaceStoredDocument Saved(XElement root)
        => new(new CharacterWorkspaceId("reputation-test"), new WorkspaceDocument(
            new WorkspaceDocumentState("sr5", 1, "sr5/chum5-xml", root.ToString(SaveOptions.DisableFormatting))),
            3, 3, DateTimeOffset.UnixEpoch);

    private static WorkspaceStoredDocument ReplaceXml(WorkspaceStoredDocument saved, string xml)
        => saved with { Document = saved.Document with { State = saved.Document.State with { Payload = xml } } };

    private sealed class Resolver : ICharacterSourceDataResolver, ICharacterSourceDataContext
    {
        public bool TryResolveCyberwareGradeDeviceRating(string sourceId, string grade, out int deviceRating)
        { deviceRating = 0; return false; }
        public bool TryResolveVehicleModBonuses(string sourceId, string category, out CharacterVehicleModSourceBonuses bonuses)
        { bonuses = default!; return false; }
        public int Creations { get; private set; }
        public int Reads { get; private set; }
        public string Raw { get; init; } = "full captured profile test input";
        public bool Calculated { get; init; } = true;
        public string Failure { get; init; } = "";
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        { Creations++; return Failure == "missing" ? null : this; }

        public bool TryResolveCareerReputationSettings(out CharacterCareerReputationSettings settings, out string rawRuleState)
        {
            Reads++;
            settings = new(Calculated && !(Failure == "policy-drift" && Reads > 1));
            rawRuleState = Failure == "empty-binding" ? "" : Failure == "binding-drift" && Reads > 1 ? "changed" : Raw;
            return !(Failure == "drift" && Reads > 1);
        }
    }
}
