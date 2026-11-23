using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private static readonly IReadOnlySet<string> DenialArmorFields5875 = new HashSet<string>(StringComparer.Ordinal)
        {
            "target", "improvedname", "sourcename", "min", "max", "aug", "augmax", "val", "rating",
            "exclude", "condition", "improvementttype", "improvementsource", "custom", "customname",
            "customid", "customgroup", "addtorating", "enabled", "order", "unique"
        };

        // Recognize only the plain Armor row emitted by CreateFreeSpriteDenial5732's
        // existing owner compiler. This classifies knowledge points, not source or
        // grant authority: complete owner/effect readmission still checks the source
        // and value, and capture retains the original XML without rewriting it.
        private bool IsKnowledgeNeutralDenialArmor5875(XElement row)
        {
            if (row.Name != "improvement" || row.Elements().Count() != DenialArmorFields5875.Count
                || row.Elements().Any(field => field.Name.Namespace != XNamespace.None)
                || row.DescendantNodes().Any(node => node is not XElement && node.GetType() != typeof(XText))
                || row.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
                || !HasStrictAllowedShape(row, DenialArmorFields5875)) return false;
            string Value(string name) => row.Element(name)!.Value;
            if (Value("improvementttype") != "Armor" || Value("improvementsource") != "CritterPower"
                || new[] { "min", "max", "aug", "augmax", "addtorating", "order" }.Any(name => Value(name) != "0")
                || new[] { "target", "improvedname", "exclude", "condition", "customname", "customid", "customgroup", "unique" }
                    .Any(name => Value(name) != "")
                || Value("rating") != "1" || Value("enabled") != "1" || Value("custom") != "false"
                || !decimal.TryParse(Value("val"), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out decimal value)
                || Value("val") != value.ToString(CultureInfo.InvariantCulture)
                || !Guid.TryParseExact(Value("sourcename"), "D", out var ownerId) || ownerId == Guid.Empty
                || Value("sourcename") != ownerId.ToString("D")) return false;

            var containers = _character.Elements().Where(node => node.Name.LocalName == "critterpowers").ToArray();
            if (containers is not [var powers] || powers.Name != "critterpowers" || powers.HasAttributes
                || powers.Elements().Any(node => node.Name != "critterpower")) return false;
            var owners = powers.Elements().Where(power => power.Elements().Any(field => field.Name.LocalName == "guid"
                && Guid.TryParse(field.Value, out var id) && id == ownerId)).Take(2).ToArray();
            bool Identity(XElement owner, string name, string expected)
            {
                var fields = owner.Elements().Where(field => field.Name.LocalName == name).ToArray();
                return fields is [var field] && field.Name == name && !field.HasAttributes && !field.HasElements
                    && field.Nodes().All(node => node.GetType() == typeof(XText)) && field.Value == expected;
            }
            return owners is [var owner] && !owner.HasAttributes
                && Identity(owner, "guid", ownerId.ToString("D"))
                && Identity(owner, "sourceid", CharacterFreeSpriteConversionRules.DenialSourceId.ToString("D"));
        }
    }
}
