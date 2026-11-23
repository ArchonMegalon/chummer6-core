using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using static Chummer.Application.Characters.CharacterCareerComplexFormLearningTransaction;

namespace Chummer.Application.Characters;

public static partial class CharacterCareerResonanceEvaluator
{
    // The same ordinary echo semantics are used for selection and existing-history authority.
    internal static CharacterCareerInitiationBenefit EchoBenefit(CharacterCareerInitiationSourceRow row,
        XElement root, bool adding = true)
    {
        var source = Parse(row.Xml, "echo");
        Guid id = Guid.Parse(Scalar(source, "id"));
        string name = Scalar(source, "name"), book = Scalar(source, "source"), page = Scalar(source, "page");
        var blockers = new List<string>();
        if (!row.Enabled) blockers.Add("submersion_echo_source_disabled");
        if (source.Elements().Any(e => e.Name.LocalName == "hide")) blockers.Add("submersion_echo_hidden");
        string[] fields = ["id", "name", "source", "page", "limit", "bonus", "translate", "altpage"];
        if (id == Guid.Empty || name.Length == 0 || book.Length == 0
            || source.Elements().Any(e => e.Name.NamespaceName.Length != 0 || !fields.Contains(e.Name.LocalName))
            || source.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1))
            blockers.Add("submersion_echo_source_shape_unsupported");
        if (source.Elements().Any(e => e.HasAttributes || e.HasElements
            || e.Name == "bonus" && !string.IsNullOrWhiteSpace(e.Value)))
            blockers.Add("submersion_echo_effects_or_choices_unsupported");
        if (source.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)))
            blockers.Add("submersion_echo_source_shape_unsupported");
        var limitNode = source.Elements("limit").FirstOrDefault();
        if (limitNode is not null && limitNode.Value != bool.FalseString)
        {
            if (!int.TryParse(limitNode.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int limit))
                blockers.Add("submersion_echo_limit_expression_unsupported");
            else
            {
                // Retained SelectionShared counts by source ID OR name, not source ID alone.
                int count = Box(root, "metamagics").Elements().Count(e => Scalar(e, "name", "") == name
                    || Guid.TryParse(Scalar(e, "sourceid", ""), out var ownedId) && ownedId == id);
                if (adding ? count >= limit : count > limit) blockers.Add("submersion_echo_limit_reached");
            }
        }
        return new(id, name, book, page, row.Xml, CharacterCareerInitiationBenefitKind.Echo, 0,
            blockers.Distinct(StringComparer.Ordinal).ToArray());
    }

    internal static string? AuthenticateEchoHistory(XElement root, ICharacterSourceDataContext context, int grade)
    {
        if (grade == 0) return null;
        Need(context is ICharacterCareerInitiationSourceContext, "resonance_echo_source_capability_unavailable");
        Need(((ICharacterCareerInitiationSourceContext)context).TryResolveCareerInitiationSources(out var sources)
            && sources?.Echoes is not null, "resonance_echo_source_unavailable");
        var benefits = sources!.Echoes!.Select(row => EchoBenefit(row, root, adding: false)).ToArray();
        Need(benefits.Length <= 4096 && benefits.Select(b => b.SourceId).Distinct().Count() == benefits.Length,
            "resonance_echo_source_invalid");
        foreach (var owned in Box(root, "metamagics").Elements())
        {
            Guid sourceId = Guid.Parse(Scalar(owned, "sourceid"));
            var benefit = benefits.SingleOrDefault(b => b.SourceId == sourceId);
            Need(benefit is not null && benefit.Blockers.Count == 0, "resonance_owned_echo_source_unsupported");
            Need(Scalar(owned, "name") == benefit!.Name && Scalar(owned, "source") == benefit.Book
                && Scalar(owned, "page") == benefit.Page, "resonance_owned_echo_identity_changed");
        }
        return CharacterCareerInitiationRules.Digest(sources);
    }

    // Source-independent structural validation is also used by cold receipt recovery.
    // A fresh purchase additionally authenticates every echo against retained effective sources.
    internal static int ValidateSubmersionState(XElement root)
    {
        Need(root.Name == "character" && !root.HasAttributes, "resonance_document_invalid");
        int grade = decimal.ToInt32(Number(root, "submersiongrade", 0));
        Need(grade == Number(root, "submersiongrade", 0) && grade is >= 0 and <= CharacterCareerInitiationRules.MaximumGrade,
            "resonance_saved_grade_inconsistent");
        var grades = Box(root, "initiationgrades").Elements().ToArray();
        Need(grades.Length == grade && grades.All(e => e.Name == "initiationgrade" && !e.HasAttributes)
            && grades.Select(e => Integer(e, "grade")).OrderBy(g => g).SequenceEqual(Enumerable.Range(1, grade)),
            "resonance_saved_grade_inconsistent");
        var identities = new HashSet<Guid>();
        void Identity(XElement row)
        {
            Need(Guid.TryParse(Scalar(row, "guid"), out var id) && id != Guid.Empty && identities.Add(id)
                && root.Descendants("guid").Count(e => Guid.TryParse(e.Value, out var other) && other == id) == 1,
                "resonance_saved_identity_inconsistent");
        }
        foreach (var row in grades)
        {
            Identity(row);
            Need(Only(row, "guid", "res", "grade", "group", "ordeal", "schooling", "notes", "notesColor")
                && Flag(row, "res"),
                "resonance_grade_variant_unsupported");
            // Historic selections are not re-priced against today's membership
            // or profile. Still parse each flag strictly, including imported grades.
            _ = Flag(row, "group");
            _ = Flag(row, "ordeal");
            _ = Flag(row, "schooling");
        }
        var echoes = Box(root, "metamagics").Elements().ToArray();
        Need(echoes.Length <= grade && echoes.All(e => e.Name == "metamagic" && !e.HasAttributes)
            && echoes.All(e => Integer(e, "grade") > 0 && Integer(e, "grade") <= grade)
            && echoes.Select(e => Integer(e, "grade")).Distinct().Count() == echoes.Length,
            "resonance_echo_grade_history_unsupported");
        foreach (var row in echoes)
        {
            Identity(row);
            Need(Guid.TryParse(Scalar(row, "sourceid"), out var id) && id != Guid.Empty
                && Scalar(row, "improvementsource") == "Echo" && !Flag(row, "paidwithkarma")
                && Scalar(row, "extra", "").Length == 0, "resonance_owned_echo_identity_invalid");
            Need(row.Elements().GroupBy(e => e.Name).All(g => g.Count() == 1), "resonance_owned_echo_identity_invalid");
            var bonus = Box(row, "bonus");
            Need(!bonus.HasElements && !bonus.HasAttributes && string.IsNullOrWhiteSpace(bonus.Value)
                && row.Elements().All(e => e.Name == "bonus" || Only(new XElement("fields", new XElement(e)),
                    "sourceid", "guid", "name", "source", "page", "grade", "paidwithkarma", "improvementsource",
                    "extra", "notes", "notesColor")), "resonance_owned_echo_effects_unsupported");
        }
        // Career's free-benefit check includes spells as well as every metamagic kind.
        // An ordinary saved spell (grade 0/-1) does not occupy a Submersion grade.
        var occupied = echoes.Select(e => Integer(e, "grade")).ToHashSet();
        foreach (var spell in Box(root, "spells").Elements())
        {
            Need(spell.Name == "spell" && !spell.HasAttributes, "resonance_spell_grade_history_invalid");
            decimal savedSpellGrade = Number(spell, "grade", 0);
            int spellGrade = decimal.ToInt32(savedSpellGrade);
            Need(savedSpellGrade == spellGrade, "resonance_spell_grade_history_invalid");
            if (spellGrade <= 0) continue;
            Identity(spell);
            Need(spellGrade <= grade, "resonance_spell_grade_history_invalid");
            Need(occupied.Add(spellGrade), "resonance_extra_grade_benefit_unsupported");
        }
        var caps = Box(root, "improvements").Elements()
            .Where(e => Scalar(e, "improvementsource", "") == "Submersion").ToArray();
        Need(caps.Length == (grade == 0 ? 0 : 1), "resonance_submersion_cap_invalid");
        foreach (var cap in caps)
        {
            Need(cap.Name == "improvement" && !cap.HasAttributes
                && cap.Elements().All(e => EffectFields.Contains(e.Name.LocalName) && !e.HasElements && !e.HasAttributes
                    && e.Name.NamespaceName.Length == 0)
                && cap.Elements().GroupBy(e => e.Name).All(g => g.Count() == 1)
                && Scalar(cap, "improvementttype") == "Attribute" && Scalar(cap, "improvedname") == "RES"
                && Integer(cap, "max") == 1 && Integer(cap, "rating") == grade && Flag(cap, "enabled")
                && !Flag(cap, "custom", false), "resonance_submersion_cap_invalid");
            foreach (string field in new[] { "min", "aug", "augmax", "val", "order", "addtorating" })
                Need(Number(cap, field, 0) == 0, "resonance_submersion_cap_invalid");
            foreach (string field in new[] { "target", "sourcename", "unique", "exclude", "condition", "customname", "customid", "customgroup" })
                Need(Scalar(cap, field, "").Length == 0, "resonance_submersion_cap_invalid");
        }
        return grade;
    }

    internal static IReadOnlyList<CharacterCareerSubmersionGrade> SubmersionGrades(
        XElement root, IReadOnlyList<string> stateBlockers)
    {
        // Even unsupported source variants may display structural occupancy, but never authorize a fill.
        var result = new List<CharacterCareerSubmersionGrade>();
        foreach (var row in Box(root, "initiationgrades").Elements())
        {
            int grade = Integer(row, "grade");
            Guid id = Guid.Parse(Scalar(row, "guid"));
            bool occupied = Box(root, "metamagics").Elements().Concat(Box(root, "spells").Elements())
                .Any(e => Number(e, "grade", 0) == grade);
            var blockers = stateBlockers.ToList();
            if (occupied) blockers.Add("submersion_grade_occupied_extra_echo_unsupported");
            result.Add(new(id, grade, row.ToString(SaveOptions.DisableFormatting), !occupied,
                blockers.Distinct(StringComparer.Ordinal).ToArray()));
        }
        return result.OrderBy(g => g.Grade).ToArray();
    }

    internal static void AdvanceSubmersionCap(XElement root, int expectedGrade, int nextGrade)
    {
        Need(ValidateSubmersionState(root) == expectedGrade && nextGrade == checked(expectedGrade + 1)
            && nextGrade is > 0 and <= CharacterCareerInitiationRules.MaximumGrade, "resonance_submersion_transition_invalid");
        var improvements = Box(root, "improvements");
        if (improvements.Parent is null) root.Add(improvements);
        if (expectedGrade > 0)
        {
            improvements.Elements().Single(e => Scalar(e, "improvementsource", "") == "Submersion")
                .SetElementValue("rating", nextGrade);
            return;
        }
        improvements.Add(new XElement("improvement",
            new XElement("target", ""), new XElement("improvedname", "RES"), new XElement("sourcename", ""),
            new XElement("min", 0), new XElement("max", 1), new XElement("aug", 0), new XElement("augmax", 0),
            new XElement("val", 0), new XElement("rating", nextGrade), new XElement("exclude", ""),
            new XElement("condition", ""), new XElement("improvementttype", "Attribute"),
            new XElement("improvementsource", "Submersion"), new XElement("custom", false),
            new XElement("customname", ""), new XElement("customid", ""), new XElement("customgroup", ""),
            new XElement("addtorating", 0), new XElement("enabled", 1), new XElement("order", 0),
            new XElement("notes", ""), new XElement("notesColor", "#000000")));
    }
}
