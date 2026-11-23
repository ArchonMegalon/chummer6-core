using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Application.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        // The natural calculation stays with its existing projector. This adapter
        // supplies the missing limb part from the SAME input and admitted profile.
        public bool TryEvaluateCareerCyberlimbAttribute(
            CharacterCareerCyberlimbAttributeInput input,
            out CharacterCareerCyberlimbAttributeEvaluation? evaluation)
        {
            using IDisposable scope = _sourceInputs.Enter();
            evaluation = null;
            try
            {
                if (input is null || input.Abbreviation is not ("AGI" or "STR") || _sourceInputs.HasSourceDrift
                    || "sha256:" + input.CharacterXmlDigest.ToLowerInvariant() != _rawCharacterXmlDigest
                    || !LimbBool3921(_character, "created", false)
                    || !TryResolveCareerSettingsProfile(out string xml, out string profileAuthority)) return false;
                XElement settings = XElement.Parse(xml);
                if (input.SettingsXmlDigest != LimbDigest3921(xml)
                    || LimbBool3921(settings, "dontusecyberlimbcalculation", true)
                    || input.NaturalValue < 0 || input.NaturalMaximum < input.NaturalValue
                    || input.TotalAugmentedMaximum < 0
                    || Math.Abs((long)input.NaturalTotalValue) > 10000
                    || input.NaturalMaximum > 10000 || input.TotalAugmentedMaximum > 10000) return false;

                int profileCount = LimbInt3921(settings, "limbcount");
                int bonusCap = LimbInt3921(settings, "cyberlimbattributebonuscap");
                string excluded = LimbScalar3921(settings, "excludelimbslot");
                XElement? redlinerElement = LimbContainer3921(settings, "redlinerexclusion");
                if (redlinerElement is null || redlinerElement.Elements().Any(e => e.Name != "limb")) return false;
                var redlinerExcluded = redlinerElement.Elements("limb").Select(e => e.HasElements || e.HasAttributes
                    ? throw new InvalidDataException() : e.Value).ToHashSet(StringComparer.Ordinal);
                if (profileCount < 0 || profileCount > 1024 || bonusCap < 0 || bonusCap > 10000) return false;

                List<LimbImprovement3921> improvements = ReadLimbImprovements3921();
                int Count(string slot) => PortableCyberlimbAttributeRules.CharacterLimbCount(profileCount, slot,
                    LimbRound3921(SelectLimbImprovements3935(improvements, "AddLimb", slot).Value));
                int characterCount = Count(string.Empty);
                if (characterCount <= 0 || characterCount > 1024) return false;
                int nodeCount = 0;
                var sourceStates = new SortedDictionary<string, string>(StringComparer.Ordinal);
                var instanceIds = new HashSet<Guid>();
                XElement? wareRoot = LimbContainer3921(_character, "cyberwares");
                if (wareRoot is null || wareRoot.Elements().Any(e => e.Name != "cyberware")) return false;
                List<LimbNode3921> roots = wareRoot.Elements("cyberware")
                    .Select(e => ReadLimbNode3921(e, null, Count, sourceStates, instanceIds, ref nodeCount, 0)).ToList();
                int redlinerCount = roots.Sum(n => CountRedliner3921(n, redlinerExcluded));
                int redliner = PortableCyberlimbAttributeRules.RedlinerBonus(redlinerCount,
                    SelectLimbImprovements3935(improvements, "Seeker").Used.Any(i => i.Name is "STR" or "AGI"));
                int improvementBonus = LimbRound3921(SelectLimbImprovements3935(improvements, "CyberlimbAttributeBonus", input.Abbreviation).Value);
                var evidence = new XElement("limbs", new XAttribute("count", characterCount),
                    new XAttribute("redliner", redliner), new XAttribute("bonus", improvementBonus));
                int replaced = 0, pureCyber = 0;
                foreach (var node in roots) Accumulate3921(node);
                int total = input.NaturalTotalValue;
                if (replaced > 0)
                {
                    // Reject overflow instead of returning an apparently authoritative wraparound.
                    _ = checked(pureCyber + Math.Max(total, 0) * Math.Max(characterCount - replaced, 0) + characterCount - 1);
                    total = PortableCyberlimbAttributeRules.WeightedTotal(total, pureCyber, replaced, characterCount);
                }
                total = Math.Min(total, input.TotalAugmentedMaximum);
                if (total < 1) total = input.NaturalZeroAllowed
                    || PortableCyberlimbAttributeRules.CyberlimbsAllowZero(replaced, pureCyber) ? 0 : 1;
                evidence.Add(new XElement("inputs", roots.Select(n => n.Evidence)),
                    new XElement("improvements", improvements.Select(i => i.Evidence)));
                if (_sourceInputs.HasSourceDrift) return false;
                evaluation = new(input.Abbreviation, input.CharacterXmlDigest, input.SettingsXmlDigest,
                    input.NaturalValue, input.NaturalTotalValue, input.TotalAugmentedMaximum, total,
                    evidence.ToString(SaveOptions.DisableFormatting),
                    string.Join('\0', PortableCyberlimbAttributeRules.Revision, profileAuthority,
                        string.Join('\0', sourceStates.Select(s => s.Key + "=" + s.Value))));
                return true;

                void Accumulate3921(LimbNode3921 node)
                {
                    bool equipped = node.Plugs.Length == 0;
                    for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
                        equipped = PortableCyberlimbAttributeRules.ApplyParentMount(equipped,
                            ancestor.Mount.Length != 0, ancestor.Plugs.Length != 0);
                    if (!equipped) return;
                    if (node.IsLimb)
                    {
                        // CharacterSettings.ExcludeLimbSlot is a string in the engine.
                        // Its Contains semantics (including the empty slot) are intentional.
                        if (excluded.Contains(node.Slot, StringComparison.Ordinal)) return;
                        int value = LimbValue3921(node, input, bonusCap, redliner, improvementBonus);
                        if (value < 0) throw new InvalidDataException();
                        replaced = checked(replaced + node.SlotCount);
                        pureCyber = checked(pureCyber + value * node.SlotCount);
                        evidence.Add(new XElement("contribution", new XAttribute("id", node.Id),
                            new XAttribute("slots", node.SlotCount), new XAttribute("value", value)));
                    }
                    else foreach (var child in node.Children) Accumulate3921(child);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException
                or System.Xml.XmlException or ArgumentException)
            {
                evaluation = null;
                return false;
            }
        }

        private sealed class LimbNode3921
        {
            public required string Id, Name, Category, Slot, Mount, Plugs, MinRating, MaxRating;
            public required int SlotCount, Agility, Strength, Rating;
            public required bool Inherit;
            public LimbNode3921? Parent;
            public List<LimbNode3921> Children = [];
            public required XElement Evidence;
            public bool IsLimb => !string.IsNullOrWhiteSpace(Slot)
                || Plugs.ToUpperInvariant() is "WRIST" or "ELBOW" or "SHOULDER" or "ANKLE" or "KNEE" or "HIP"
                || Inherit && Children.Any(c => c.IsLimb);
        }

        private LimbNode3921 ReadLimbNode3921(XElement saved, LimbNode3921? parent, Func<string, int> count,
            SortedDictionary<string, string> sources, HashSet<Guid> ids, ref int nodes, int depth)
        {
            if (++nodes > 512 || depth > 32 || !Guid.TryParse(LimbScalar3921(saved, "guid"), out var instance)
                || instance == Guid.Empty || !ids.Add(instance)
                || !Guid.TryParse(LimbScalar3921(saved, "sourceid"), out var sourceId) || sourceId == Guid.Empty)
                throw new InvalidDataException();
            string kind = LimbScalar3921(saved, "improvementsource", "Cyberware");
            string file = kind == "Cyberware" ? "cyberware.xml" : kind == "Bioware" ? "bioware.xml"
                : throw new InvalidDataException();
            string container = kind == "Cyberware" ? "cyberwares" : "biowares";
            string entry = kind == "Cyberware" ? "cyberware" : "bioware";
            if (!TryResolveTarget(file, [container], entry, sourceId.ToString("D"), string.Empty,
                    out XElement? source, out bool ambiguous, inspectIdentity: true) || source is null || ambiguous
                || !IsEnabledSource(LimbScalar3921(source, "source"))
                || !TryComputeEffectiveInputDigest(_catalog, file, out string digest)
                || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, file, out string customDigest))
                throw new InvalidDataException();
            sources[file] = digest + "\0" + customDigest;

            // Static saved fields must agree with their exact admitted definition. An old
            // or hand-edited variant is unavailable, never silently repaired in this read.
            string Bound(string name, string fallback = "")
            {
                string authoritative = LimbScalar3921(source, name, fallback);
                string value = LimbScalar3921(saved, name, fallback);
                if (value != authoritative) throw new InvalidDataException();
                return value;
            }
            string slot = Bound("limbslot");
            string slotCount = Bound("limbslotcount", "1");
            int slots = string.Equals(slotCount, "all", StringComparison.OrdinalIgnoreCase) ? count(slot)
                : int.Parse(slotCount, NumberStyles.Integer, CultureInfo.InvariantCulture);
            bool inherit = source.Elements("inheritattributes").Any();
            if (source.Elements("inheritattributes").Count() > 1
                || LimbBool3921(saved, "inheritattributes", false) != inherit || slots < 0 || slots > 1024)
                throw new InvalidDataException();
            var node = new LimbNode3921
            {
                Id = instance.ToString("D"), Name = Bound("name"), Category = Bound("category"), Slot = slot,
                SlotCount = slots, Inherit = inherit, Parent = parent, Mount = Mount("hasmodularmount", "modularmount"),
                Plugs = Mount("plugsintomodularmount", "mountsto"), Agility = ParseBound("minagility", "3"),
                Strength = ParseBound("minstrength", "3"), Rating = LimbInt3921(saved, "rating", "0"),
                MinRating = RatingBound("minrating", "minrating"),
                MaxRating = RatingBound("maxrating", "rating"),
                Evidence = new XElement("ware", new XAttribute("instance", instance),
                    new XAttribute("source", sourceId), new XElement("definition", new XElement(source)))
            };
            if (node.Agility < 0 || node.Strength < 0 || node.Rating < 0
                || node.Agility > 10000 || node.Strength > 10000 || node.Rating > 10000) throw new InvalidDataException();
            node.Evidence.Add(new XAttribute("rating", node.Rating));
            XElement? children = LimbContainer3921(saved, "children");
            if (children is not null)
            {
                if (children.Elements().Any(e => e.Name != "cyberware")) throw new InvalidDataException();
                foreach (var child in children.Elements("cyberware"))
                    node.Children.Add(ReadLimbNode3921(child, node, count, sources, ids, ref nodes, depth + 1));
            }
            node.Evidence.Add(node.Children.Select(c => c.Evidence));
            return node;
            string Mount(string savedName, string sourceName)
            {
                string expected = LimbScalar3921(source, sourceName, "");
                // Native saves persist this selection. Historical missing mount fields
                // use different loader fallbacks, so do not invent a source-derived mount.
                if (LimbScalar3921(saved, savedName, "") != expected) throw new InvalidDataException();
                return expected;
            }
            string RatingBound(string savedName, string sourceName)
            {
                string expected = LimbScalar3921(source, sourceName, "");
                string actual = LimbScalar3921(saved, savedName, "");
                if (expected.Length == 0) expected = "0";
                if (actual.Length == 0) actual = "0";
                if (actual != expected) throw new InvalidDataException();
                return expected;
            }
            int ParseBound(string name, string fallback) => int.Parse(Bound(name, fallback), NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static int LimbValue3921(LimbNode3921 node, CharacterCareerCyberlimbAttributeInput input,
            int bonusCap, int redliner, int improvementBonus)
        {
            if (node.Inherit)
            {
                int sum = 0, count = 0;
                // Canonical inheritance considers all children, including detached ones,
                // ignores nonpositive results, and truncates the positive-child average.
                foreach (var child in node.Children)
                {
                    int value = LimbValue3921(child, input, bonusCap, redliner, improvementBonus);
                    if (value <= 0) continue;
                    sum = checked(sum + value); ++count;
                }
                return PortableCyberlimbAttributeRules.InheritedValue(sum, count);
            }
            if (node.Category != "Cyberlimb" && !node.IsLimb) return 0;
            bool agility = input.Abbreviation == "AGI";
            string customization = agility ? "Customized Agility" : "Customized Strength";
            string customization2050 = agility ? "Cyberlimb Customization, Agility (2050)" : "Cyberlimb Customization, Strength (2050)";
            string enhancement = agility ? "Enhanced Agility" : "Enhanced Strength";
            string enhancement2050 = agility ? "Cyberlimb Augmentation, Agility (2050)" : "Cyberlimb Augmentation, Strength (2050)";
            int valueBase = agility ? node.Agility : node.Strength;
            var custom = node.Children.Where(c => c.Name == customization || c.Name == customization2050).ToArray();
            if (custom.Length > 0) valueBase = custom.Max(c => LimbRating3921(c, input));
            var enhancements = node.Children.Where(c => c.Name == enhancement || c.Name == enhancement2050).ToArray();
            int bonus = enhancements.Length > 0 ? enhancements.Max(c => LimbRating3921(c, input)) : 0;
            bonus = checked(bonus + redliner + improvementBonus);
            return PortableCyberlimbAttributeRules.AttributeTotal(
                PortableCyberlimbAttributeRules.AttributeValue(valueBase, input.NaturalMaximum),
                bonus, bonusCap, input.TotalAugmentedMaximum);
        }

        private static int LimbRating3921(LimbNode3921 node, CharacterCareerCyberlimbAttributeInput input)
        {
            int minimum = Bound(node.MinRating), maximum = Bound(node.MaxRating);
            return Math.Max(Math.Min(node.Rating, maximum), minimum);
            int Bound(string expression)
            {
                if (decimal.TryParse(expression, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
                    return LimbRound3921(value);
                if (expression == "{" + input.Abbreviation + "Maximum}") return input.NaturalMaximum;
                var parent = node.Category == "Cyberlimb" || node.IsLimb ? node : node.Parent;
                while (parent is not null && parent.Category != "Cyberlimb" && !parent.IsLimb) parent = parent.Parent;
                if (parent is not null && expression == "{" + input.Abbreviation + "Minimum}+1")
                    return checked((input.Abbreviation == "AGI" ? parent.Agility : parent.Strength) + 1);
                throw new InvalidDataException();
            }
        }

        private static int CountRedliner3921(LimbNode3921 node, HashSet<string> excluded)
            => node.Slot.Length != 0 && !excluded.Contains(node.Slot) ? node.SlotCount
                : node.Children.Sum(c => CountRedliner3921(c, excluded));

        private sealed record LimbImprovement3921(string Type, string Name, string Unique, bool Custom,
            decimal Value, XElement Evidence);

        private static (decimal Value, List<LimbImprovement3921> Used) SelectLimbImprovements3935(
            List<LimbImprovement3921> rows, string type, string name = "")
        {
            var eligible = rows.Where(i => i.Type == type && (name.Length == 0 || i.Name == name)).ToList();
            PortableImprovementSelection.Select(eligible, i => i.Value, i => i.Name, i => i.Unique,
                i => i.Custom, out var values, out var used);
            return (values.Values.Sum(), used.Values.SelectMany(i => i).ToList());
        }
        private List<LimbImprovement3921> ReadLimbImprovements3921()
        {
            var result = new List<LimbImprovement3921>();
            XElement? container = LimbContainer3921(_character, "improvements");
            if (container is null) return result;
            foreach (var row in container.Elements("improvement"))
            {
                string type = LimbScalar3921(row, "improvementttype", "");
                if (type is not ("AddLimb" or "Seeker" or "CyberlimbAttributeBonus")) continue;
                if (!LimbBool3921(row, "enabled", true) || LimbBool3921(row, "addtorating", false)) continue;
                string condition = LimbScalar3921(row, "condition", "");
                if (condition is not ("" or "career")) continue;
                string unique = LimbScalar3921(row, "unique", "");
                bool custom = LimbBool3921(row, "custom", false);
                string name = LimbScalar3921(row, "improvedname", "");
                decimal value = decimal.Parse(LimbScalar3921(row, "val", "0"), NumberStyles.Number, CultureInfo.InvariantCulture);
                result.Add(new(type, name, unique, custom, value, new XElement("improvement", new XAttribute("type", type),
                    new XAttribute("name", name), new XAttribute("unique", unique), new XAttribute("custom", custom),
                    new XAttribute("value", value))));
            }
            return result;
        }

        private static XElement? LimbContainer3921(XElement row, string name)
        {
            var values = row.Elements(name).Take(2).ToArray();
            if (values.Length > 1 || values.Length == 1 && values[0].HasAttributes) throw new InvalidDataException();
            return values.SingleOrDefault();
        }
        private static string LimbScalar3921(XElement row, string name, string? fallback = null)
        {
            var value = LimbContainer3921(row, name);
            if (value is null) return fallback ?? throw new InvalidDataException();
            if (value.HasElements) throw new InvalidDataException();
            return value.Value;
        }
        private static int LimbInt3921(XElement row, string name, string? fallback = null)
            => int.Parse(LimbScalar3921(row, name, fallback), NumberStyles.Integer, CultureInfo.InvariantCulture);
        private static bool LimbBool3921(XElement row, string name, bool fallback)
        {
            string value = LimbScalar3921(row, name, fallback ? "True" : "False").Trim();
            if (value == "1") return true;
            if (value == "0") return false;
            return bool.Parse(value);
        }
        private static int LimbRound3921(decimal value)
            => decimal.ToInt32(value >= 0 ? decimal.Ceiling(value) : decimal.Floor(value));
        private static string LimbDigest3921(string xml)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml)));
    }
}
