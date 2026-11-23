using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// A retained Sprite observation, not a creature-creation API. No caller-supplied
// force or cached total is used as authority. Native AttributeSection.Create
// persists every evaluated bound; their intersection must identify one level.
public static class CharacterSpriteSource5751
{
    internal static readonly string[] Attributes = ["BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL", "EDG", "MAG", "RES", "ESS", "DEP"];
    private static readonly Regex Linear = new(@"^(?:[+-]?[0-9]+|(?:F|[0-9]+\*F|F\*[0-9]+|\((?:F|[0-9]+\*F|F\*[0-9]+)\))(?:[+-][0-9]+)?)$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly HashSet<string> Fields = new(new[]
    {
        "id", "name", "category", "karma", "forcecreature", "forceislevels", "movement",
        "bonus", "powers", "skills", "qualities", "source", "page", "initiativedice"
    }.Concat(Attributes.Append("INI").SelectMany(a => new[] { a.ToLowerInvariant() + "min", a.ToLowerInvariant() + "max", a.ToLowerInvariant() + "aug" })), StringComparer.Ordinal);

    public static bool IsSource(XElement source)
        => source.Element("forcecreature") is not null
            && (source.Element("category")?.Value ?? "").EndsWith("Sprites", StringComparison.Ordinal);

    public static bool SupportsSource(XElement source)
    {
        try { ValidateSource(source); return true; }
        catch (Exception e) when (e is InvalidDataException or FormatException or OverflowException or RegexMatchTimeoutException) { return false; }
    }

    private static void ValidateSource(XElement source)
    {
        Need(source.Name == "metatype" && IsSource(source) && !source.HasAttributes
            && source.Elements().All(e => Fields.Contains(e.Name.LocalName))
            && source.Elements().GroupBy(e => e.Name).All(g => g.Count() == 1)
            && !source.DescendantsAndSelf().Any(e => e.Name.Namespace != XNamespace.None
                || e.Nodes().Any(n => n is not XElement && n is not XComment && n is not XText)
                || e.HasElements && e.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))), "source-shape");
        foreach (string marker in new[] { "forcecreature", "forceislevels" })
            Need(source.Element(marker) is { HasAttributes: false, HasElements: false } node && node.Value.Length == 0, "source-marker");
        Need(Guid.TryParseExact(Scalar(source, "id"), "D", out var id) && id != Guid.Empty
            && Scalar(source, "name").Length > 0 && Scalar(source, "source").Length > 0
            && Integer(source, "page") >= 0 && Integer(source, "karma") >= 0
            && Scalar(source, "movement") == "Special", "source-identity-or-movement");
        foreach (string a in Attributes.Append("INI"))
            foreach (string bound in new[] { "min", "max", "aug" }) Coefficients(Scalar(source, a.ToLowerInvariant() + bound));
        if (source.Element("initiativedice") is not null) Need(Integer(source, "initiativedice") >= 0, "initiative-dice");
        if (source.Element("skills") is { } skills)
        {
            Need(!skills.HasAttributes && skills.Elements().All(s => s.Name == "skill" && !s.HasElements
                && s.Attributes().All(a => a.Name == "rating") && s.Value.Length > 0)
                && skills.Elements().Select(s => s.Value).Distinct(StringComparer.Ordinal).Count() == skills.Elements().Count(), "skill-shape");
            foreach (var skill in skills.Elements()) Coefficients(skill.Attribute("rating")?.Value ?? "");
        }
        if (source.Element("powers") is { } powers)
        {
            Need(!powers.HasAttributes && powers.Elements().All(p => p.Name == "power" && !p.HasElements
                && p.Attributes().All(a => a.Name == "rating" || a.Name == "select") && p.Value.Length > 0), "power-shape");
            foreach (var power in powers.Elements())
                if (power.Attribute("rating") is { } rating && !string.IsNullOrWhiteSpace(rating.Value)) Coefficients(rating.Value);
        }
        // Bonuses and qualities are consumed exhaustively by the existing owner
        // compiler, including source-selected amendments. Shape admission alone
        // never authorizes a sheet, mutation or talent.
    }

    internal sealed record Observation(int Level, XElement Evaluated, string Digest);
    internal static Observation Observe(XElement root, XElement source)
    {
        ValidateSource(source);
        Need(Scalar(root, "created").Equals("true", StringComparison.OrdinalIgnoreCase)
            && Scalar(root, "critter").Equals("true", StringComparison.OrdinalIgnoreCase)
            && (Scalar(root, "metatypecategory") == Scalar(source, "category")
                || Scalar(root, "metatypecategory") == CharacterFreeSpriteConversionRules.FreeSpriteCategory)
            && Scalar(root, "metatype") == Scalar(source, "name")
            && (root.Element("metavariant")?.Value ?? "") is "" or "None"
            && (root.Element("metavariantguid")?.Value ?? "") is "" or "00000000-0000-0000-0000-000000000000", "saved-identity");
        var attributes = Single(root, "attributes");
        Need(!attributes.HasAttributes && attributes.Elements().All(a => a.Name == "attribute"), "attribute-container");
        long lower = 1, upper = int.MaxValue;
        var facts = new List<(string Expression, int Value, int Floor)>();
        foreach (string name in Attributes.Concat(attributes.Elements().Any(a => Scalar(a, "name") == "MAGAdept") ? new[] { "MAGAdept" } : []))
        {
            var rows = attributes.Elements().Where(a => Scalar(a, "name") == name).ToArray();
            Need(rows.Length == 1 && !rows[0].HasAttributes, "attribute-identity");
            foreach (var (saved, bound) in new[] { ("metatypemin", "min"), ("metatypemax", "max"), ("metatypeaugmax", "aug") })
            {
                string expression = Scalar(source, (name == "MAGAdept" ? "mag" : name.ToLowerInvariant()) + bound);
                int observed = Integer(rows[0], saved);
                Constrain(expression, observed, 1);
            }
        }
        foreach (var grant in source.Elements("skills").Elements("skill"))
        {
            var effects = root.Elements("improvements").Elements("improvement").Where(e =>
                Scalar(e, "improvementsource") == "Metatype" && Scalar(e, "sourcename") == ""
                && Scalar(e, "improvementttype") == "SkillLevel" && Scalar(e, "improvedname") == grant.Value).ToArray();
            Need(effects.Length == 1, "generated-skill-witness");
            decimal value = decimal.Parse(Scalar(effects[0], "val"), NumberStyles.Number, CultureInfo.InvariantCulture);
            Need(value == decimal.Truncate(value) && value >= 0 && value <= int.MaxValue, "generated-skill-witness-value");
            Constrain(grant.Attribute("rating")!.Value, (int)value, 0);
        }
        Need(lower == upper && lower >= 1, "level-ambiguous-or-inconsistent");
        int level = checked((int)lower);
        Need(facts.All(f => Evaluate(f.Expression, level, f.Floor) == f.Value), "bound-mismatch");
        var evaluated = new XElement(source);
        foreach (string a in Attributes.Append("INI"))
        {
            string prefix = a.ToLowerInvariant();
            foreach (string bound in new[] { "min", "max", "aug" })
                evaluated.SetElementValue(prefix + bound, Evaluate(Scalar(source, prefix + bound), level, 1));
            Need(Integer(evaluated, prefix + "min") <= Integer(evaluated, prefix + "max")
                && Integer(evaluated, prefix + "max") <= Integer(evaluated, prefix + "aug"), "source-range");
        }
        return new(level, evaluated, CharacterCreationFinalizationDigest.Compute(new
        { Source = source.ToString(SaveOptions.DisableFormatting), Level = level, BoundsAndGeneratedRanks = facts.Select(f => new { f.Expression, f.Value, f.Floor }).ToArray() }));
        void Constrain(string expression, int observed, int floor)
        {
            var (a, b) = Coefficients(expression);
            Need(observed >= floor, "bound-range");
            if (a == 0) Need(observed == Math.Max(floor, b), "constant-bound");
            else if (observed == floor) upper = Math.Min(upper, checked((long)decimal.Floor((floor - b) / a)));
            else
            {
                decimal candidate = (observed - b) / a;
                Need(candidate == decimal.Truncate(candidate) && candidate >= 1 && candidate <= int.MaxValue, "bound-witness");
                lower = Math.Max(lower, (long)candidate); upper = Math.Min(upper, (long)candidate);
            }
            facts.Add((expression, observed, floor));
        }
    }

    // Accepted grammar is affine integer stock syntax (including amended
    // coefficients/offsets). Evaluation/rounding reuse Core's existing kernel.
    // No XPath, new interpreter, random dice, guessed fallback, or arbitrary cap.
    private static (decimal A, decimal B) Coefficients(string expression)
    {
        string compact = string.Concat(expression.Where(c => !char.IsWhiteSpace(c)));
        Need(compact.Length is > 0 and <= 128 && Linear.IsMatch(compact), "expression-unsupported");
        decimal b = CharacterCreationEssenceExpression1222.Evaluate(compact, new Dictionary<string, decimal> { ["F"] = 0 });
        decimal a = CharacterCreationEssenceExpression1222.Evaluate(compact, new Dictionary<string, decimal> { ["F"] = 1 }) - b;
        Need(a >= 0 && a <= int.MaxValue && b >= int.MinValue && b <= int.MaxValue, "expression-range");
        return (a, b);
    }
    internal static int Evaluate(string expression, int level, int floor)
    {
        Coefficients(expression);
        decimal value = CharacterCreationEssenceExpression1222.Evaluate(expression, new Dictionary<string, decimal> { ["F"] = level });
        return Math.Max(floor, CharacterCreationEssenceRules1222.Round(value));
    }
    internal static CharacterCreationMetatypeOptionProjection Project(Observation observed, int multiplier, int dice)
    {
        var source = observed.Evaluated;
        Need(multiplier >= 0 && dice >= 0, "profile-range");
        return new(Scalar(source, "id"), Scalar(source, "name"), Scalar(source, "category"), Scalar(source, "source"),
            Integer(source, "page"), Integer(source, "karma"), checked(Integer(source, "karma") * multiplier),
            Attributes.Select(a => new CharacterCreationMetatypeAttributeProjection(a, Integer(source, a.ToLowerInvariant() + "min"),
                Integer(source, a.ToLowerInvariant() + "max"), Integer(source, a.ToLowerInvariant() + "aug"))).ToArray(),
            new(Integer(source, "inimin"), Integer(source, "inimax"), Integer(source, "iniaug"), dice)
                { DirectDice886 = source.Element("initiativedice") is null ? null : Integer(source, "initiativedice") },
            CharacterCreationMetatypeMovementProjection.Special, [], [], true, [], [$"critters.xml#metatype:{Scalar(source, "id")}"]);
    }
    private static XElement Single(XElement root, string name)
    { var nodes = root.Elements(name).ToArray(); Need(nodes.Length == 1, "duplicate-or-missing-" + name); return nodes[0]; }
    private static string Scalar(XElement root, string name)
    { var node = Single(root, name); Need(!node.HasAttributes && !node.HasElements, "scalar-" + name); return node.Value; }
    private static int Integer(XElement root, string name) => int.Parse(Scalar(root, name), NumberStyles.Integer, CultureInfo.InvariantCulture);
    private static void Need(bool value, string code) { if (!value) throw new InvalidDataException("sprite-source-" + code); }
}
