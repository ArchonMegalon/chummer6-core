using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// Creation-only arithmetic over the admitted compiled graph. No public DTO, saved
// total, observation name, or caller-supplied diagnostic can manufacture execution.
internal static partial class CharacterCreationRetainedMagicKarma1221
{
    internal sealed record Branch(string[] Fields, int Cost, string Detail);
    internal sealed record Result(int Cost, IReadOnlyList<Branch> Branches);
    internal const string Unavailable = "creation_retained_magic_karma_unavailable1221";

    internal static bool TryEvaluate(XElement profile, XElement graph, out Result? result)
    {
        result = null;
        try
        {
            if (profile.Name != "setting" || profile.HasAttributes || graph.Name != "character"
                || Boolean(graph, "created", true)) return false;
            var costs = Container(profile, "karmacost", required: true);
            var branches = new List<Branch>();
            void Add(string[] fields, int cost, object detail)
                => branches.Add(new(fields, cost, JsonSerializer.Serialize(detail)));
            int Price(string field) => Nonnegative(Scalar(costs, field));

            // CharacterCreate.cs:14445. Sprites pay services, never fettering.
            int serviceRate = Price("karmaspirit"), fetteringRate = Price("karmaspiritfettering");
            var spirits = Rows(graph, "spirits", "spirit").Select(row => new
            {
                Id = Identity(row), Type = Scalar(row, "type"),
                Services = Nonnegative(Scalar(row, "services")),
                Force = Nonnegative(Scalar(row, "force")), Fettered = Boolean(row, "fettered", false)
            }).ToArray();
            if (spirits.Any(row => row.Type is not ("Spirit" or "Sprite"))) return false;
            int services = spirits.Sum(row => checked(row.Services * serviceRate));
            int fettering = spirits.Where(row => row.Type == "Spirit" && row.Fettered)
                .Sum(row => checked(row.Force * fetteringRate));
            Add(["karmaspirit"], services, new { serviceRate, spirits });
            Add(["karmaspiritfettering"], fettering, new { fetteringRate, spirits });

            // CharacterCreate.cs:14491; Character.cs:24333. Preserve grants and
            // spend unused advanced allowances on basic programs before charging.
            int basicRate = Price("karmanewaiprogram"), advancedRate = Price("karmanewaiadvancedprogram");
            var programs = Rows(graph, "aiprograms", "aiprogram").Select(row => new
            {
                Id = Identity(row), CanDelete = Boolean(row, "candelete", true),
                Advanced = Boolean(row, "isadvancedprogram", false)
            }).ToArray();
            int basicLimit = Nonnegative(Scalar(graph, "ainormalprogramlimit", "0"));
            int advancedLimit = Nonnegative(Scalar(graph, "aiadvancedprogramlimit", "0"));
            int basic = programs.Count(row => row.CanDelete && !row.Advanced);
            int advanced = programs.Count(row => row.CanDelete && row.Advanced);
            int substituted = Math.Min(Math.Max(basic - basicLimit, 0), Math.Max(advancedLimit - advanced, 0));
            int basicUnit = ProgramPrice(graph, "NewAIProgramKarmaCost", basicRate);
            int advancedUnit = ProgramPrice(graph, "NewAIAdvancedProgramKarmaCost", advancedRate);
            Add(["karmanewaiprogram"], checked(Math.Max(basic - substituted - basicLimit, 0) * basicUnit),
                new { basicRate, basicUnit, basicLimit, advancedLimit, substituted, programs });
            Add(["karmanewaiadvancedprogram"], checked(Math.Max(advanced - advancedLimit, 0) * advancedUnit),
                new { advancedRate, advancedUnit, advancedLimit, programs });

            // InitiationGrade.cs:367, CharacterCreate.cs:14545. The rate and flat
            // terms form ONE rounded price per grade, not two rounded charges.
            int flat = Price("karmainitiationflat"), perGrade = Price("karmainitiation");
            int metamagicRate = Price("karmametamagic");
            var metamagics = Rows(graph, "metamagics", "metamagic").Select(row => new
                { Id = Identity(row), Grade = Integer(Scalar(row, "grade")) }).ToArray();
            var grades = Rows(graph, "initiationgrades", "initiationgrade").Select(row =>
            {
                string id = Identity(row);
                int grade = Nonnegative(Scalar(row, "grade"));
                if (grade == 0) throw new InvalidDataException();
                bool resonance = Boolean(row, "res", false);
                decimal multiplier = 1m;
                foreach (string discount in new[] { "group", "ordeal", "schooling" })
                    if (Boolean(row, discount, false))
                        // CharacterSettings.cs:236-241: these six rates are
                        // constructor constants, not XML aliases in Load/Save.
                        multiplier -= resonance && discount == "ordeal" ? 0.2m : 0.1m;
                if (multiplier is < 0 or > 1) throw new InvalidDataException();
                return new { Id = id, Grade = grade, Resonance = resonance, Multiplier = multiplier,
                    Cost = Round(checked((flat + checked(grade * perGrade)) * multiplier)),
                    Extra = Math.Max(metamagics.Count(item => item.Grade == grade) - 1, 0) };
            }).ToArray();
            if (grades.Select(row => row.Grade).Distinct().Count() != grades.Length) return false;
            Add(["karmainitiationflat", "karmainitiation"], grades.Sum(row => row.Cost), new { flat, perGrade, grades });
            Add(["karmametamagic"], grades.Sum(row => checked(row.Extra * metamagicRate)), new { metamagicRate, grades, metamagics });

            // CharacterCreate.cs:14569. A created=False character can be a member.
            int joiningRate = Price("karmajoingroup");
            bool member = Boolean(graph, "groupmember", false), magic = Boolean(graph, "magenabled", false);
            Add(["karmajoingroup"], member && magic ? joiningRate : 0, new { joiningRate, member, magic });

            // CharacterSettings.cs:3793: canonical technique price wins; maneuver
            // is the historical fallback, not an additional cost or blanket alias.
            int maneuverRate = Price("karmamaneuver");
            string? canonical = OptionalScalar(costs, "karmatechnique");
            bool useFallback = canonical is null || !int.TryParse(canonical, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            int techniqueRate = useFallback ? maneuverRate : Nonnegative(canonical!);
            var arts = Rows(graph, "martialarts", "martialart").Select(row => new
            {
                Id = Identity(row), Quality = Boolean(row, "isquality", false),
                Cost = Nonnegative(Scalar(row, "cost")),
                Techniques = Rows(row, "martialarttechniques", "martialarttechnique").Select(Identity).ToArray()
            }).ToArray();
            Add(["karmamaneuver"], arts.Where(row => !row.Quality)
                    .Sum(row => checked(row.Cost + checked(Math.Max(row.Techniques.Length - 1, 0) * techniqueRate))),
                new { maneuverRate, techniqueRate, UsedFallback = useFallback, arts });

            // Enemy points enter the negative-quality subtotal BEFORE its cap.
            // Character.cs:54726/54830/55084, CharacterCreate.cs:13907.
            int enemyRate = Price("karmaenemy");
            bool tracking = Boolean(profile, "enableenemytracking", false);
            var enemies = Rows(graph, "contacts", "contact").Where(row => Scalar(row, "type", "Contact") == "Enemy")
                .Select(row => new { Id = Identity(row), Free = ContactFree(graph, row),
                    Connection = Math.Min(Nonnegative(Scalar(row, "connection")),
                        SelectImprovements(graph, "FriendsInHighPlaces").Length > 0 ? 12 : 6),
                    Loyalty = EnemyLoyalty(graph, row) }).ToArray();
            int enemy = tracking ? enemies.Where(row => !row.Free)
                .Sum(row => checked(checked(row.Connection + row.Loyalty) * enemyRate)) : 0;
            int enemyDelta = 0;
            if (enemy != 0)
            {
                int qualityRate = Price("karmaquality"), limit = Nonnegative(Scalar(profile, "qualitykarmalimit"));
                bool capped = Boolean(profile, "exceednegativequalitiesnobonus",
                    Boolean(profile, "exceednegativequalitieslimit", false));
                var negatives = Rows(graph, "qualities", "quality").Where(row => Scalar(row, "qualitytype") == "Negative").ToArray();
                int free = Round(checked(SelectImprovements(graph, "FreeNegativeQualities").Sum(row => row.Value) * qualityRate));
                int cappedSubtotal = checked(negatives.Where(row => Contributes(graph, row, false)
                        && Contributes(graph, row, true)).Sum(row => Integer(Scalar(row, "bp"))) * qualityRate - free);
                int before = capped ? Math.Max(cappedSubtotal, -limit) : cappedSubtotal;
                int after = capped ? Math.Max(checked(cappedSubtotal + enemy), -limit) : checked(cappedSubtotal + enemy);
                enemyDelta = checked(after - before);
                if (Boolean(profile, "enemykarmaqualitylimit", true))
                {
                    int limitSubtotal = checked(negatives.Where(row => Contributes(graph, row, true))
                        .Sum(row => Integer(Scalar(row, "bp"))) * qualityRate + enemy
                        - Round(SelectImprovements(graph, "FreeNegativeQualities").Sum(row => row.Value)));
                    if (!capped && !Boolean(profile, "exceednegativequalities", false) && checked(-limitSubtotal) > limit)
                        return false;
                }
            }
            Add(["karmaenemy"], enemyDelta, new { enemyRate, tracking, enemies, enemy, enemyDelta });
            result = new(branches.Sum(row => row.Cost), branches.AsReadOnly());
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or FormatException or OverflowException
            or InvalidOperationException or ArgumentException) { return false; }
    }

    private static int ProgramPrice(XElement graph, string type, int price)
    {
        decimal value = checked(price + SelectImprovements(graph, type).Sum(row => row.Value));
        decimal multiplier = 1m;
        foreach (var row in SelectImprovements(graph, type + "Multiplier")) multiplier = checked(multiplier * row.Value / 100m);
        return Math.Max(Round(checked(value * multiplier)), 0);
    }

    // The retained backend's StandardRound is CEILING for positive values and
    // FLOOR for negative values (DecimalExtensions.cs:42), not nearest integer.
    private static int Round(decimal value) => decimal.ToInt32(value >= 0 ? decimal.Ceiling(value) : decimal.Floor(value));
    private sealed record Improvement(string Target, string Unique, bool Custom, decimal Value);
    private static Improvement[] SelectImprovements(XElement graph, string type, string? target = null)
    {
        var all = Rows(graph, "improvements", "improvement")
            .Where(row => Scalar(row, "improvementttype", "") == type && Boolean(row, "enabled", true)
                && !Boolean(row, "addtorating", false) && Scalar(row, "condition", "").Length == 0
                && (target is null || Scalar(row, "improvedname", "") == target))
            .Select(row => new Improvement(Scalar(row, "improvedname", ""), Scalar(row, "unique", ""),
                Boolean(row, "custom", false), Number(Scalar(row, "val", "0")))).ToArray();
        // Retained MetaValueOf's custom-unique cache uses different numeric and
        // selected-list paths (ImprovementManager.cs:1269-1340). Do not silently
        // substitute ordinary unique stacking for an unproven multiplier list.
        if (all.Any(row => row.Custom && row.Unique.Length != 0)) throw new InvalidDataException("custom unique improvement");
        var selected = new List<Improvement>();
        // ImprovementManager.MetaValueOf: unique names compete within an improved
        // target and separately in the custom pool. Ties retain source order.
        foreach (var group in all.GroupBy(row => (row.Target, row.Custom)))
        {
            var plain = group.Where(row => row.Unique.Length == 0).ToArray();
            var unique = group.Where(row => row.Unique.Length != 0).ToArray();
            Improvement[]? precedence = null;
            if (!group.Key.Custom && unique.Any(row => row.Unique == "precedence0"))
                precedence = unique.Where(row => row.Unique == "precedence0").OrderByDescending(row => row.Value).Take(1)
                    .Concat(unique.Where(row => row.Unique == "precedence-1")).ToArray();
            else if (!group.Key.Custom && unique.Any(row => row.Unique == "precedence1"))
                precedence = unique.Where(row => row.Unique is "precedence1" or "precedence-1").ToArray();
            if (precedence is not null)
                selected.AddRange(precedence.Sum(row => row.Value) > plain.Sum(row => row.Value) ? precedence : plain);
            else
            {
                selected.AddRange(plain);
                selected.AddRange(unique.GroupBy(row => row.Unique).Select(rows => rows.OrderByDescending(row => row.Value).First()));
            }
        }
        return selected.ToArray();
    }

    private static bool ContactFree(XElement graph, XElement contact)
        => Boolean(contact, "free", false) || SelectImprovements(graph, "ContactMakeFree", Scalar(contact, "guid")).Length > 0;

    private static int EnemyLoyalty(XElement graph, XElement contact)
    {
        int forced = SelectImprovements(graph, "ContactForcedLoyalty", Scalar(contact, "guid"))
            .Select(row => Round(row.Value)).Prepend(0).Max();
        return forced > 0 ? forced : Boolean(contact, "group", false) ? 1 : Nonnegative(Scalar(contact, "loyalty"));
    }

    private static bool Contributes(XElement graph, XElement quality, bool limit)
    {
        string origin = Scalar(quality, "qualitysource", "Selected");
        if (origin is "Metatype" or "MetatypeRemovable" or "Heritage"
            || limit && origin == "MetatypeRemovedAtChargen") return false;
        if (Boolean(quality, "metagenic", false)
            && Round(SelectImprovements(graph, "MetageneticLimit").Sum(row => row.Value)) > 0) return false;
        return Boolean(quality, limit ? "contributetolimit" : "contributetobp", true)
            && SelectImprovements(graph, "FreeQuality", Scalar(quality, "sourceid")).Length == 0
            && SelectImprovements(graph, "FreeQuality", Scalar(quality, "name")).Length == 0;
    }

    private static XElement Container(XElement parent, string name, bool required = false)
    {
        var nodes = parent.Elements().Where(node => node.Name.LocalName == name).ToArray();
        if (nodes.Length == 0 && !required) return new XElement(name);
        if (nodes is not [var node] || node.Name != name || node.HasAttributes
            || node.Nodes().Any(item => item is XText text && !string.IsNullOrWhiteSpace(text.Value)))
            throw new InvalidDataException(name);
        return node;
    }
    private static XElement[] Rows(XElement parent, string container, string item)
    {
        var rows = Container(parent, container).Elements().ToArray();
        if (rows.Any(row => row.Name != item || row.HasAttributes)) throw new InvalidDataException(container);
        var ids = rows.Select(Identity).ToArray();
        if (item != "improvement" && ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length) throw new InvalidDataException(container);
        return rows;
    }
    private static string Identity(XElement row)
    {
        // Legacy improvements have no GUID. Their ordered full XML is identity.
        if (row.Name == "improvement") return CharacterCreationFinalizationDigest.Compute(row.ToString(SaveOptions.DisableFormatting));
        if (!Guid.TryParse(Scalar(row, "guid"), out var id) || id == Guid.Empty) throw new InvalidDataException("guid");
        return id.ToString("D");
    }
    private static string? OptionalScalar(XElement parent, string name)
    {
        var nodes = parent.Elements().Where(node => node.Name.LocalName == name).ToArray();
        if (nodes.Length == 0) return null;
        if (nodes is not [var node] || node.Name != name || node.HasAttributes || node.HasElements
            || node.Nodes().Any(item => item is not XText || item.GetType() != typeof(XText))) throw new InvalidDataException(name);
        return node.Value;
    }
    private static string Scalar(XElement parent, string name, string? fallback = null)
        => OptionalScalar(parent, name) ?? fallback ?? throw new InvalidDataException(name);
    private static bool Boolean(XElement parent, string name, bool fallback)
    {
        string? value = OptionalScalar(parent, name);
        if (value is null) return fallback;
        return value switch { "0" => false, "1" => true, _ => bool.Parse(value) };
    }
    private static int Integer(string value) => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    private static int Nonnegative(string value) => Integer(value) is var number && number >= 0 ? number : throw new InvalidDataException();
    private static decimal Number(string value) => decimal.Parse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
}
