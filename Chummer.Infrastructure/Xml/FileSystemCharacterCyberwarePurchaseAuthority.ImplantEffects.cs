using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterCyberwarePurchaseAuthority
{
    // This admission is private to installed commerce. Purchase and historical receipt
    // projection retain their original catalogs and digests.
    private static string? CommerceSourceExclusionReason(XElement source, Guid id, string name, bool exCon)
    {
        var plain = new XElement(source);
        if (plain.Elements("bonus").Count() > 1) return "Ambiguous implant bonus source.";
        plain.Elements("bonus").Remove();
        string? blocker = SourceExclusionReason(plain, id, name, exCon);
        if (blocker is not null) return blocker;
        if (source.Element("bonus") is null) return null;
        TryReadRatingBounds(source, out int minimum, out int maximum);
        for (int rating = minimum; rating <= maximum; rating++)
            if (!TryCommerceEffects(source, rating, out _, out blocker)) return blocker;
        return null;
    }

    private XElement[] ReadCommerceEffectSources(string kind = "Cyberware")
    {
        bool bioware = kind == "Bioware";
        if (!bioware && kind != "Cyberware") throw new InvalidDataException("Unknown installed implant source kind.");
        string file = bioware ? "bioware.xml" : "cyberware.xml";
        byte[] bytes = _readAllBytes(Path.Combine(_overlays.GetCatalog().BaseDataPath, file));
        if (!FixedEquals(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            bioware ? BiowareXmlSha256 : CharacterCyberwarePurchaseLegacyAuthority.CyberwareXmlSha256))
            throw new InvalidDataException($"Installed effects require the pinned {file} source.");
        using var stream = new MemoryStream(bytes, writable: false);
        return XDocument.Load(stream).Root!.Element(bioware ? "biowares" : "cyberwares")!
            .Elements(bioware ? "bioware" : "cyberware").ToArray();
    }

    private static bool TryCommerceEffects(XElement source, int rating,
        out IReadOnlyList<CharacterAugmentationPurchaseEffect> effects, out string? blocker)
    {
        effects = [];
        // The augmentation purchase projector implements native AddImprovementCollection
        // semantics. Generated objects (including LimitModifier identities) need their own
        // removal authority and are deliberately outside this scalar/stat slice.
        if (!TryProjectBiowareEffects(source.Element("bonus"), rating, out var projected, out blocker)) return false;
        if (projected.Any(e => e.Type == "LimitModifier" || e.Type == "Attribute"
            && (e.Minimum != 0 || e.Maximum != 0 || e.AugmentedMaximum != 0
                || e.UniqueName.StartsWith("precedence", StringComparison.Ordinal))))
        { blocker = "Generated objects or attribute bound/precedence effects require additional removal authority."; return false; }
        effects = projected.Select(e => e with { ImprovementSource = source.Name == "bioware" ? "Bioware" : "Cyberware" }).ToArray();
        return true;
    }

    private static string? CommerceImprovementStateBlocker(XElement root)
    {
        if (new[] { "attributes", "qualities" }.Any(name => root.Elements(name).Count() > 1))
            return "The saved attribute or quality container is ambiguous.";
        var containers = root.Elements("improvements").ToArray();
        if (containers.Length > 1 || containers.Any(c => c.HasAttributes || c.Elements().Any(e => e.Name != "improvement")))
            return "The saved improvement container is ambiguous.";
        foreach (var effect in containers.SelectMany(c => c.Elements()))
        {
            if (effect.HasAttributes || effect.Elements().Any(e => e.HasAttributes || e.HasElements || e.Name.NamespaceName.Length != 0)
                || effect.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1))
                return "The saved improvement fields are ambiguous.";
            string enabled = ReadOptionalScalar(effect, "enabled");
            if (enabled is "0" or "False") continue;
            if (enabled is not ("1" or "True")) return "A saved improvement has no exact enabled state.";
            string type = ReadOptionalScalar(effect, "improvementttype");
            // Same passive effect boundary as the augmentation Essence authority. Unknown
            // cost/grade/Essence modifiers must never be treated as harmless unrelated rows.
            if (!PassiveAugmentationEffectTypes.Contains(type) && !BiowareScalarEffects.Values.Contains(type, StringComparer.Ordinal))
                return $"Active improvement '{type}' needs additional price, Essence or effect authority.";
            if (type == "Attribute" && ReadOptionalScalar(effect, "improvedname") is "ESS" or "ESSBase")
                return "An ESS modifier needs the native Essence maximum authority.";
            if (ReadOptionalScalar(effect, "improvementsource") is "EssenceLossChargen" or "CyberadeptDaemon")
                return "Existing chargen/Daemon effects require special-attribute reconciliation.";
        }
        return null;
    }

    private string ValidateCommerceEffects(XElement root, XElement ware, XElement source, int rating)
    {
        if (!TryCommerceEffects(source, rating, out var effects, out string? blocker))
            throw new InvalidDataException(blocker);
        // This also guards dormant DisableQuality markers: removing them must not leave
        // a real quality suppressed or restore it without the quality's own authority.
        if (effects.Any(e => e.Type == "DisableQuality")
            && root.Element("qualities")?.Elements().Any(q => q.Name != "quality"
                || !TryReadScalar(q, "name", out _) || q.Elements("sourceid").Count() > 1) == true)
            throw new InvalidDataException("Quality identities are ambiguous for implant suppression reconciliation.");
        // Installed Bioware is already counted by the native source limit. This is
        // a replacement/removal, not an additional purchase of that source.
        string? admission = BiowareCharacterAdmission(source, root, source.Name == "bioware" ? 0 : 1);
        if (admission is not null) throw new InvalidDataException(admission);
        Guid id = Guid.Parse(ReadOptionalScalar(ware, "guid"));
        var actual = CommerceOwnedEffects(root, id);
        var expected = effects.Select(e => SaveAugmentationImprovement(e, id.ToString("D"))).ToList();
        foreach (var effect in actual)
        {
            int index = expected.FindIndex(e => CommerceEffectEquals(effect, e));
            if (index < 0) throw new InvalidDataException("The installed implant effects do not match their canonical source/rating.");
            expected.RemoveAt(index);
        }
        if (expected.Count != 0) throw new InvalidDataException("The installed implant is missing canonical effects.");
        _ = CommerceAttributeValues(root, effects.Where(e => e.Type == "Attribute").Select(e => e.ImprovedName), out string authority);
        return authority;
    }

    private static XElement[] CommerceOwnedEffects(XElement root, Guid id)
        => root.Element("improvements")?.Elements("improvement")
            .Where(e => Guid.TryParse(ReadOptionalScalar(e, "sourcename"), out Guid source) && source == id).ToArray() ?? [];

    private static bool CommerceEffectEquals(XElement actual, XElement expected)
    {
        if (actual.Name != "improvement" || actual.HasAttributes
            || actual.Elements().Any(e => e.HasElements || e.HasAttributes || expected.Element(e.Name) is null)
            || actual.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)) return false;
        foreach (var field in expected.Elements())
        {
            string name = field.Name.LocalName;
            if (name is "notes" or "notesColor" or "order") continue;
            // Native Improvement.Save omits an empty unique name. Numeric spelling
            // and presentation fields do not change the source-authenticated effect.
            if (name == "unique" && actual.Element(name) is null && field.Value.Length == 0) continue;
            if (!TryReadScalar(actual, name, out string value)) return false;
            if (name is "min" or "max" or "aug" or "augmax" or "val" or "rating" or "addtorating")
            {
                if (!TryReadDecimal(actual, name, out decimal number)
                    || number != decimal.Parse(field.Value, CultureInfo.InvariantCulture)) return false;
            }
            else if (name == "enabled")
            { if (value is not ("1" or "True")) return false; }
            else if (value != field.Value) return false;
        }
        return true;
    }

    private void ReconcileCommerceEffects(XElement root, XElement target, CharacterCareerImplantCommerceQuote quote)
    {
        var source = ReadCommerceEffectSources(ReadOptionalScalar(target, "improvementsource"))
            .Single(s => ReadOptionalScalar(s, "id") == quote.Item.SourceId.ToString("D"));
        ValidateCommerceEffects(root, target, source, quote.Item.Snapshot.CurrentRating);
        TryCommerceEffects(source, quote.Item.Snapshot.CurrentRating, out var original, out _);
        // Grade changes do not rewrite equal effects, preserving their position and bytes.
        if (quote.ReplacementImplantXml is null || quote.Canonical.Rating != quote.Item.Snapshot.CurrentRating)
        {
            var owned = CommerceOwnedEffects(root, quote.Item.InstanceId);
            IReadOnlyList<CharacterAugmentationPurchaseEffect> replacement = [];
            if (quote.ReplacementImplantXml is not null
                && !TryCommerceEffects(source, quote.Canonical.Rating, out replacement, out string? blocker))
                throw new InvalidDataException(blocker);
            // Reuse the purchase serializer; never change the source identity or use a
            // broad improvements clear/rebuild that would destroy unrelated effects.
            var materialized = replacement.Select(e => SaveAugmentationImprovement(e, quote.Item.InstanceId.ToString("D"))).ToArray();
            // Preserve the user's effect annotations when the same effect is replayed
            // at another rating; the mechanical fields always come from the source.
            foreach (var effect in materialized)
            {
                var previous = owned.Where(e => ReadOptionalScalar(e, "improvementttype") == ReadOptionalScalar(effect, "improvementttype")
                    && ReadOptionalScalar(e, "improvedname") == ReadOptionalScalar(effect, "improvedname")
                    && ReadOptionalScalar(e, "unique") == ReadOptionalScalar(effect, "unique")).ToArray();
                if (previous.Length == 1)
                    foreach (string field in new[] { "notes", "notesColor", "order" })
                        if (previous[0].Element(field) is XElement cosmetic) effect.Element(field)!.ReplaceWith(new XElement(cosmetic));
            }
            if (owned.Length > 0) owned[0].AddBeforeSelf(materialized);
            else if (materialized.Length > 0) GetOrCreateUniqueContainer(root, "improvements").Add(materialized);
            foreach (var effect in owned) effect.Remove();
        }
        var values = CommerceAttributeValues(root, original.Where(e => e.Type == "Attribute").Select(e => e.ImprovedName), out _);
        foreach (var (name, value) in values)
        {
            var attribute = root.Element("attributes")!.Elements("attribute").Single(a => ReadOptionalScalar(a, "name") == name);
            attribute.SetElementValue("totalvalue", value.Current.ToString(CultureInfo.InvariantCulture));
            if (attribute.Element("value") is not null) attribute.SetElementValue("value", value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private Dictionary<string, CharacterCreationEssenceRules1222.NativeValue> CommerceAttributeValues(
        XElement root, IEnumerable<string> affected, out string authority)
    {
        authority = string.Empty;
        var result = new Dictionary<string, CharacterCreationEssenceRules1222.NativeValue>(StringComparer.Ordinal);
        string[] names = affected.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (names.Length == 0) return result;
        if (root.Element("cyberwares")?.Descendants("cyberware").Any(w => ReadOptionalScalar(w, "limbslot").Length != 0
            || ReadOptionalScalar(w, "inheritattributes") == "True") == true)
            throw new InvalidDataException("Affected cyberlimb attribute averaging requires its native aggregation authority.");
        var context = _sourceData.TryCreateContext(root.ToString(SaveOptions.DisableFormatting));
        if (context is null || !context.TryResolveCareerQualityMetatype(out string metatypeXml, out string metatypeAuthority)
            || !context.TryResolveCareerSettingsProfile(out string settingsXml, out string settingsAuthority))
            throw new InvalidDataException("Implant attribute recalculation needs exact metatype and settings authority.");
        authority = CharacterCareerImplantCommerceTransaction.Digest(new { metatypeXml, metatypeAuthority, settingsXml, settingsAuthority });
        var metatype = XElement.Parse(metatypeXml);
        var settings = XElement.Parse(settingsXml);
        bool unclamp = false;
        if (settings.Element("unclampattributeminimum") is not null
            && !TryReadBoolean(settings, "unclampattributeminimum", out unclamp))
            throw new InvalidDataException("The attribute minimum setting is ambiguous.");
        foreach (string name in names)
        {
            var attribute = root.Elements("attributes").Single().Elements("attribute").Single(a => ReadOptionalScalar(a, "name") == name);
            string prefix = name.ToLowerInvariant();
            if (!TryReadInteger(metatype, prefix + "min", out int minimum)
                || !TryReadInteger(metatype, prefix + "max", out int maximum)
                || !TryReadInteger(metatype, prefix + "aug", out int augmentedMaximum)
                || !TryReadInteger(attribute, "base", out int basis) || basis < 0
                || !TryReadInteger(attribute, "karma", out int karma) || karma < 0
                || !TryReadInteger(attribute, "metatypemin", out int savedMin) || savedMin != minimum
                || !TryReadInteger(attribute, "metatypemax", out int savedMax) || savedMax != maximum
                || !TryReadInteger(attribute, "metatypeaugmax", out int savedAug) || savedAug != augmentedMaximum
                || minimum < 1 || maximum < minimum || augmentedMaximum < maximum)
                throw new InvalidDataException("The affected attribute allocation or source bounds are unavailable.");
            decimal augmentation = 0m;
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var effect in root.Element("improvements")?.Elements("improvement") ?? [])
            {
                if (ReadOptionalScalar(effect, "enabled") is "0" or "False") continue;
                string type = ReadOptionalScalar(effect, "improvementttype");
                if (!(type.StartsWith("Attribute", StringComparison.Ordinal) || type == "ReplaceAttribute")) continue;
                string target = ReadOptionalScalar(effect, "improvedname");
                if (target != name && target != name + "Base") continue;
                string group = ReadOptionalScalar(effect, "unique");
                if (type != "Attribute" || target != name || ReadOptionalScalar(effect, "condition").Length != 0
                    || ReadOptionalScalar(effect, "exclude").Length != 0 || ReadOptionalScalar(effect, "target").Length != 0
                    || !TryReadBoolean(effect, "custom", out bool custom) || custom
                    || !TryReadInteger(effect, "addtorating", out int add) || add != 0
                    || !TryReadInteger(effect, "rating", out int effectRating) || effectRating != 1
                    || new[] { "min", "max", "augmax", "val" }.Any(f => !TryReadDecimal(effect, f, out decimal v) || v != 0m)
                    || !TryReadDecimal(effect, "aug", out decimal augmented) || augmented < 0m
                    || group.StartsWith("precedence", StringComparison.Ordinal) || group.Length > 0 && !unique.Add(group))
                    throw new InvalidDataException("The affected attribute modifier stack needs additional native reconciliation.");
                augmentation = checked(augmentation + augmented);
            }
            // Shared Core implementation of native allocation/clamping, never a delta
            // applied to an untrusted cached total. Base, Karma and all other stats survive.
            result.Add(name, CharacterCreationEssenceRules1222.Native(new(name, true, minimum, maximum,
                augmentedMaximum, basis, karma, unclamp, AugmentedModifier: augmentation)));
        }
        return result;
    }
}
