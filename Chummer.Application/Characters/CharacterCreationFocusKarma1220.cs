using System.Globalization;
using System.Xml.Linq;

using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// Arithmetic only. This type cannot register a source invocation or earn coverage.
// Production callers must supply the graph from the real selected-effects compiler.
internal static partial class CharacterCreationFocusKarma1220
{
    internal const string Unavailable = "creation-focus-karma-source-or-owned-graph-unavailable";
    private static readonly (string Name, string Field)[] types =
    [
        ("Alchemical Focus", "karmaalchemicalfocus"), ("Banishing Focus", "karmabanishingfocus"),
        ("Binding Focus", "karmabindingfocus"), ("Centering Focus", "karmacenteringfocus"),
        ("Counterspelling Focus", "karmacounterspellingfocus"), ("Disenchanting Focus", "karmadisenchantingfocus"),
        ("Flexible Signature Focus", "karmaflexiblesignaturefocus"), ("Masking Focus", "karmamaskingfocus"),
        ("Power Focus", "karmapowerfocus"), ("Qi Focus", "karmaqifocus"),
        ("Ritual Spellcasting Focus", "karmaritualspellcastingfocus"), ("Spellcasting Focus", "karmaspellcastingfocus"),
        ("Spell Shaping Focus", "karmaspellshapingfocus"), ("Summoning Focus", "karmasummoningfocus"),
        ("Sustaining Focus", "karmasustainingfocus"), ("Weapon Focus", "karmaweaponfocus")
    ];

    internal sealed record Price(string Field, int Value, string LeafDigest, string RelativePath, int RelativeOrder, string TextDigest);
    internal sealed record Modifier(int Index, string Type, string Name, string Target, decimal Value,
        bool Enabled, string Source, string SourceName, string XmlDigest);
    internal sealed record Component(string GearId, string SourceId, string Grade, string Name, string Extra,
        int Rating, string Field, int Price, decimal Multiplier, decimal Addition, decimal Unrounded,
        string GearXmlDigest, IReadOnlyList<int> AppliedModifiers, bool Priced);
    internal sealed record Charge(string FocusId, string OwnerGearId, bool Stacked, bool Bonded,
        int Karma, string FocusXmlDigest, IReadOnlyList<Component> Components);
    internal sealed record Calculation(string ProfileXmlDigest, string GraphDigest, IReadOnlyList<Price> Prices,
        IReadOnlyList<Modifier> Modifiers, IReadOnlyList<Charge> Charges, int TotalKarma,
        Inventory1239 Inventory, IReadOnlyList<Applicability1239> Applicability);

    internal static bool TryReadPrices(XElement profile, out IReadOnlyList<Price> prices)
    {
        prices = [];
        if (profile.Name != "setting" || profile.HasAttributes
            || profile.Descendants().Where(n => n.Name.LocalName == "karmacost").ToArray() is not [var costs]
            || costs.Name != "karmacost" || costs.Parent != profile || costs.HasAttributes) return false;
        var result = new List<Price>();
        foreach (var (_, field) in types)
        {
            if (profile.Descendants().Where(n => n.Name.LocalName == field).ToArray() is not [var leaf]
                || leaf.Parent != costs || leaf.Name != field || leaf.HasAttributes
                || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
                || !int.TryParse(leaf.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                || value < 0) return false;
            result.Add(new(field, value, Digest(leaf), RelativePath1239(profile, leaf), RelativeOrder1239(profile, leaf),
                CharacterCreationFinalizationDigest.Compute(leaf.Value)));
        }
        prices = Array.AsReadOnly(result.OrderBy(price => price.RelativeOrder).ToArray());
        return true;
    }

    internal static bool TryCalculate(XElement profile, XElement graph, out Calculation? calculation, string? inventoryAdmission1253 = null)
    {
        calculation = null;
        try
        {
            if (!TryReadPrices(profile, out var prices) || graph.Name != "character" || graph.HasAttributes
                || !bool.TryParse(Scalar(graph, "created"), out bool created) || created) return false;
            var inventory = ReadInventory1239(graph, inventoryAdmission1253);
            var priceMap = prices.ToDictionary(p => p.Field, StringComparer.Ordinal);
            var modifiers = ReadModifiers(graph);
            // These are exactly the owned inventories searched by Focus.Load. A
            // component stored inside stackedfoci is NOT an ordinary owned item.
            var owned = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (string container in new[] { "gears", "armors", "weapons", "cyberwares", "vehicles" })
            {
                var root = Container(graph, container);
                if (root is null) continue; // ReadInventory1239 already requires private observed absence.
                foreach (var gear in root.Descendants().Where(n => n.Name.LocalName == "gear"))
                {
                    if (gear.Name != "gear" || gear.HasAttributes) throw Invalid();
                    owned.Add(Id(gear, "guid"), gear);
                }
            }
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            var focusIds = new HashSet<string>(StringComparer.Ordinal);
            var componentIds = new HashSet<string>(owned.Keys, StringComparer.Ordinal);
            var charges = new List<Charge>();
            foreach (var focus in Rows(graph, "foci", "focus"))
            {
                string id = Id(focus, "guid"), owner = Id(focus, "gearid");
                if (!focusIds.Add(id) || !claimed.Add(owner) || !owned.TryGetValue(owner, out var gear)) throw Invalid();
                if (!bool.TryParse(Scalar(gear, "bonded"), out bool bonded) || !bonded) throw Invalid();
                var component = PriceGear(gear, false, priceMap, modifiers);
                charges.Add(new(id, owner, false, true, StandardRound(component.Unrounded), Digest(focus), [component]));
            }
            foreach (var focus in Rows(graph, "stackedfoci", "stackedfocus"))
            {
                string id = Id(focus, "guid"), owner = Id(focus, "gearid");
                if (!focusIds.Add(id) || !claimed.Add(owner) || !owned.TryGetValue(owner, out var gear)
                    || Scalar(gear, "category") != "Stacked Focus"
                    || !Scalar(gear, "name").StartsWith("Stacked Focus: ", StringComparison.Ordinal)
                    || Scalar(gear, "sourceid") != Guid.Empty.ToString("D")
                    || !bool.TryParse(Scalar(focus, "bonded"), out bool bonded))
                    throw Invalid();
                // Native aggregate Gear.Bonded is independent of StackedFocus.Bonded.
                // Components are marked bonded when moved into the stack.
                if (!bool.TryParse(Scalar(gear, "bonded"), out _)) throw Invalid();
                var components = new List<Component>();
                decimal total = 0;
                foreach (var child in Rows(focus, "gears", "gear"))
                {
                    if (!componentIds.Add(Id(child, "guid")) || !bool.TryParse(Scalar(child, "bonded"), out bool componentBonded) || !componentBonded) throw Invalid();
                    var component = PriceGear(child, true, priceMap, modifiers, execute: bonded);
                    components.Add(component);
                    total = checked(total + component.Unrounded);
                }
                if (components.Count < 2) throw Invalid();
                // Legacy rounds the sum once, and charges only bonded stacks.
                charges.Add(new(id, owner, true, bonded, bonded ? StandardRound(total) : 0,
                    Digest(focus), components.AsReadOnly()));
            }
            foreach (var pair in owned)
            {
                string name = Scalar(pair.Value, "category", optional: true) == "Stacked Focus"
                    ? "Stacked Focus" : Normalize(Scalar(pair.Value, "name"), false).Name;
                if (!types.Any(t => t.Name == name) && name != "Stacked Focus") continue;
                if (name == "Stacked Focus" && !claimed.Contains(pair.Key)) throw Invalid();
                if (name != "Stacked Focus" && (!int.TryParse(Scalar(pair.Value, "rating"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int rating) || rating <= 0)) throw Invalid();
                // A bonded instance without its authoritative focus link must not
                // silently disappear from the wallet. No rate/count reconstruction.
                if (!bool.TryParse(Scalar(pair.Value, "bonded"), out bool bonded)
                    || bonded && !claimed.Contains(pair.Key)) throw Invalid();
            }
            int karma = 0;
            foreach (var charge in charges) karma = checked(karma + charge.Karma);
            calculation = new(Digest(profile), Digest(graph), prices, modifiers, charges.AsReadOnly(), karma, inventory,
                Decide1239(prices, inventory, charges));
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException
            or ArgumentException or OverflowException or FormatException) { return false; }
    }

    private static Component PriceGear(XElement gear, bool stacked, IReadOnlyDictionary<string, Price> prices,
        IReadOnlyList<Modifier> modifiers, bool execute = true)
    {
        string originalName = Scalar(gear, "name"), extra = Scalar(gear, "extra", optional: true);
        var normalized = Normalize(originalName, stacked);
        string? field = types.Where(t => t.Name == normalized.Name).Select(t => t.Field).SingleOrDefault();
        // A missing rated/bound-focus projector is a dependency failure. Never
        // interpret its default rating zero as a successfully priced binding.
        if (field is null || !int.TryParse(Scalar(gear, "rating"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int rating) || rating <= 0) throw Invalid();
        // CharacterCreate skips the binding calculation entirely for an unbonded
        // stack. Retain its identities, but do not apply modifiers or overflow in
        // arithmetic that the oracle never executes. This earns no price proof.
        if (!execute)
            return new(Id(gear, "guid"), Id(gear, "sourceid"), Scalar(gear, "grade", optional: true), originalName,
                extra, rating, field, prices[field].Value, 0, 0, 0, Digest(gear), [], false);
        decimal multiplier = prices[field].Value, addition = normalized.Addition;
        var applied = new List<int>();
        foreach (var modifier in modifiers)
        {
            bool matches = stacked
                ? modifier.Target.Length == 0 || extra.Contains(modifier.Target, StringComparison.Ordinal)
                : string.IsNullOrWhiteSpace(extra) ? modifier.Target.Length == 0
                : modifier.Target.Length == 0 || modifier.Target.Contains(extra, StringComparison.Ordinal);
            if (!modifier.Enabled || modifier.Name != normalized.Name || !matches) continue;
            if (modifier.Type == "FocusBindingKarmaCost") addition = checked(addition + modifier.Value);
            else multiplier = checked(multiplier + modifier.Value); // additive multiplier, not percentage
            applied.Add(modifier.Index);
        }
        return new(Id(gear, "guid"), Id(gear, "sourceid"), Scalar(gear, "grade", optional: true), originalName,
            extra, rating, field, prices[field].Value, multiplier, addition,
            checked(rating * multiplier + addition), Digest(gear), applied.AsReadOnly(), true);
    }

    private static (string Name, decimal Addition) Normalize(string name, bool stacked)
    {
        decimal addition = 0;
        // Preserve the retained StackedFocus oracle's order, even though removing
        // the comma suffix first makes its individualized discount unreachable.
        if (stacked) name = Strip(name, true);
        if (name.EndsWith(", Individualized, Complete", StringComparison.Ordinal))
        {
            addition = -2;
            name = name.Replace(", Individualized, Complete", string.Empty, StringComparison.Ordinal);
        }
        else if (name.EndsWith(", Individualized, Partial", StringComparison.Ordinal))
        {
            addition = -1;
            name = name.Replace(", Individualized, Partial", string.Empty, StringComparison.Ordinal);
        }
        if (!stacked) name = Strip(name, false);
        return (name, addition);
    }

    private static string Strip(string name, bool stacked)
    {
        int position = name.IndexOf('(');
        if (stacked ? position >= 0 : position > 0) name = name[..(position - 1)];
        position = name.IndexOf(',');
        if (position >= 0) name = name[..position];
        return name;
    }

    // DecimalExtensions.StandardRound: ceiling for positive, floor for negative.
    // Math.Round(..., AwayFromZero) is NOT equivalent for non-midpoint fractions.
    private static int StandardRound(decimal value)
        => decimal.ToInt32(value >= 0 ? decimal.Ceiling(value) : decimal.Floor(value));

    internal static Chummer.Contracts.Characters.CharacterCreationCompletionPoolBalances Spend(
        Chummer.Contracts.Characters.CharacterCreationCompletionPoolBalances before, Calculation calculation)
    {
        if (before.RemainingCreationKarma is not int remaining) throw Invalid();
        int spent = checked(remaining - calculation.TotalKarma);
        return before with { RemainingCreationKarma = spent, KarmaOverspend = spent < 0 ? checked(-spent) : 0 };
    }

    private static IReadOnlyList<Modifier> ReadModifiers(XElement graph)
    {
        var result = new List<Modifier>();
        int index = 0;
        foreach (var row in Rows(graph, "improvements", "improvement"))
        {
            int current = index++;
            string type = Scalar(row, "improvementttype");
            if (type is not ("FocusBindingKarmaCost" or "FocusBindingKarmaMultiplier")) continue;
            string enabledText = Scalar(row, "enabled");
            bool enabled;
            if (int.TryParse(enabledText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int flag)) enabled = flag > 0;
            else if (!bool.TryParse(enabledText, out enabled)) throw Invalid();
            if (!decimal.TryParse(Scalar(row, "val"), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)) throw Invalid();
            string source = Scalar(row, "improvementsource"), sourceName = Scalar(row, "sourcename");
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(sourceName)) throw Invalid();
            result.Add(new(current, type, Scalar(row, "improvedname"), Scalar(row, "target", optional: true),
                value, enabled, source, sourceName, Digest(row)));
        }
        return result.AsReadOnly();
    }

    private static XElement? Container(XElement parent, string name)
    {
        var nodes = parent.Elements().Where(n => n.Name.LocalName == name).ToArray();
        if (nodes.Length == 0) return null;
        if (nodes is not [var node] || node.Name != name || node.HasAttributes
            || node.Nodes().Any(n => n is not XElement && (n is not XText text || !string.IsNullOrWhiteSpace(text.Value)))) throw Invalid();
        return node;
    }

    private static IEnumerable<XElement> Rows(XElement parent, string container, string name)
    {
        var node = Container(parent, container);
        if (node is null) return [];
        var rows = node.Elements().ToArray();
        if (rows.Any(r => r.Name != name || r.HasAttributes)) throw Invalid();
        return rows;
    }

    private static string Scalar(XElement parent, string name, bool optional = false)
    {
        var nodes = parent.Elements().Where(n => n.Name.LocalName == name).ToArray();
        if (optional && nodes.Length == 0) return string.Empty;
        if (nodes is not [var node] || node.Name != name || node.HasAttributes || node.HasElements
            || node.Nodes().Any(n => n.GetType() != typeof(XText))) throw Invalid();
        return node.Value;
    }

    private static string Id(XElement parent, string field)
    {
        string text = Scalar(parent, field);
        if (!Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty) throw Invalid();
        return id.ToString("D");
    }

    private static string Digest(XElement node) => CharacterCreationFinalizationDigest.Compute(node.ToString(SaveOptions.DisableFormatting));
    private static InvalidDataException Invalid() => new(Unavailable);
}
