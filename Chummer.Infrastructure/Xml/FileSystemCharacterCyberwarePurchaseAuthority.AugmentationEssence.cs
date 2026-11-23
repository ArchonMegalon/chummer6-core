using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterCyberwarePurchaseAuthority
{
    private static readonly HashSet<string> PassiveAugmentationEffectTypes = new(StringComparer.Ordinal)
    {
        "Attribute", "Armor", "InitiativeDice", "LifestyleCost", "CMSharedThresholdOffset", "LimitModifier", "Skill", "SkillCategory",
        "SkillGroup", "SkillAttribute", "SkillLinkedAttribute", "UnarmedDVPhysical", "Ambidextrous", "ReflexRecorderOptimization", "AttributeKarmaCost", "BlackMarketDiscount", "SpecialTab", "DisableQuality"
    };

    private string? TryCalculateExistingAugmentationEssence(XElement root, CharacterCyberwarePurchaseSettings settings,
        XElement biowareSource, string baseDataPath, out decimal current, out decimal maximum)
    {
        current = maximum = 0m;
        var essenceAttributes = root.Element("attributes")?.Elements("attribute").Where(a => ReadOptionalScalar(a, "name") == "ESS").Take(2).ToArray() ?? [];
        if (essenceAttributes.Length != 1 || !TryReadDecimal(essenceAttributes[0], "metatypemax", out maximum) || maximum <= 0m)
            return "The saved ESS attribute has no unique positive metatype maximum; cached total Essence is not authority.";
        if (ReadOptionalScalar(root, "metatypecategory") == "Cyberzombie")
            return "Cyberzombie Essence and special-attribute rules require their native authority.";
        if (root.Element("prototypetranshuman") is not null
            && (!TryReadDecimal(root, "prototypetranshuman", out decimal prototype) || prototype != 0m))
            return "Prototype Transhuman allocation requires canonical per-instance Essence allowance authority.";
        foreach (XElement improvement in root.Element("improvements")?.Elements("improvement") ?? [])
        {
            string enabled = ReadOptionalScalar(improvement, "enabled");
            if (enabled is "0" or "False") continue;
            if (enabled is not ("1" or "True")) return "A saved improvement has ambiguous enabled state.";
            string type = ReadOptionalScalar(improvement, "improvementttype");
            string source = ReadOptionalScalar(improvement, "improvementsource");
            if (!PassiveAugmentationEffectTypes.Contains(type) && !BiowareScalarEffects.Values.Contains(type, StringComparer.Ordinal))
                return $"Active improvement '{type}' requires canonical price, Essence, grade, or special-effect authority before purchasing Bioware.";
            if (type == "Attribute" && ReadOptionalScalar(improvement, "improvedname") is "ESS" or "ESSBase")
                return "An active ESS attribute modifier requires the native attribute maximum authority.";
            if (source is "EssenceLossChargen" or "CyberadeptDaemon")
                return $"Existing {source} effects require canonical Career attribute/Daemon reconciliation.";
            if (type == "DisableQuality")
            {
                string target = ReadOptionalScalar(improvement, "improvedname");
                var matching = root.Element("qualities")?.Elements("quality").Where(q => ReadOptionalScalar(q, "name") == target
                    || ReadOptionalScalar(q, "sourceid").Equals(target, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
                foreach (var quality in matching)
                    if (root.Element("improvements")!.Elements("improvement").Any(i => ReadOptionalScalar(i, "improvementsource") == "Quality"
                        && ReadOptionalScalar(i, "sourcename") == ReadOptionalScalar(quality, "guid")
                        && ReadOptionalScalar(i, "enabled") is "1" or "True"))
                        return $"Existing suppression of quality '{target}' has unreconciled active improvements.";
            }
        }

        byte[] cyberwareBytes = _readAllBytes(Path.Combine(baseDataPath, "cyberware.xml"));
        if (!FixedEquals(Convert.ToHexString(SHA256.HashData(cyberwareBytes)).ToLowerInvariant(), CharacterCyberwarePurchaseLegacyAuthority.CyberwareXmlSha256))
            return "Existing mixed augmentation Essence requires the pinned cyberware.xml snapshot.";
        using var stream = new MemoryStream(cyberwareBytes, writable: false);
        var cyberwareSource = XDocument.Load(stream).Root!;
        decimal spent = 0m;
        foreach (XElement ware in root.Element("cyberwares")?.Elements("cyberware") ?? [])
        {
            if (!Guid.TryParseExact(ReadOptionalScalar(ware, "sourceid"), "D", out Guid sourceId) || sourceId == Guid.Empty)
                return "An existing augmentation has no exact source GUID.";
            string id = sourceId.ToString("D");
            if (!TryReadInteger(ware, "rating", out int rating) || rating < 0)
                return "An existing augmentation has no exact non-negative rating.";
            if (id is EssenceHoleSourceId or EssenceAntiHoleSourceId)
            {
                spent += rating / (id == EssenceHoleSourceId ? 100m : -100m);
                continue;
            }
            if (ware.Descendants("cyberware").Any())
                return "Existing augmentation children require canonical parent/capacity/Essence aggregation.";
            string kind = ReadOptionalScalar(ware, "improvementsource");
            if (kind is not ("Cyberware" or "Bioware")) return "An existing augmentation has no exact Cyberware/Bioware source kind.";
            if (ReadOptionalScalar(ware, "prototypetranshuman") == "True" || ReadOptionalScalar(ware, "suite") == "True"
                || ReadOptionalScalar(ware, "plugsintomodularmount").Length != 0)
                return "Existing prototype, suite, or modular augmentation Essence requires its native authority.";
            XElement sourceRoot = kind == "Bioware" ? biowareSource : cyberwareSource;
            var sources = sourceRoot.Element(kind == "Bioware" ? "biowares" : "cyberwares")!.Elements(kind == "Bioware" ? "bioware" : "cyberware")
                .Where(w => ReadOptionalScalar(w, "id").Equals(id, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (sources.Length != 1) return $"Existing {kind} source '{id}' is absent or ambiguous in canonical data.";
            var source = sources[0];
            string gradeName = ReadOptionalScalar(ware, "grade");
            var grades = sourceRoot.Element("grades")!.Elements("grade").Where(g => ReadOptionalScalar(g, "name") == gradeName).Take(2).ToArray();
            if (grades.Length != 1 || !TryReadDecimal(grades[0], "ess", out decimal multiplier)
                || !CharacterCyberwarePurchaseRules.TryEvaluateSourceExpression(ReadOptionalScalar(source, "ess"), rating, out decimal baseEssence))
                return $"Existing {kind} grade or Essence expression cannot be established exactly.";
            if (!TryReadInteger(ware, "essdiscount", out int discount)
                || !TryReadDecimal(ware, "extraessadditivemultiplier", out decimal additive)
                || !TryReadDecimal(ware, "extraessmultiplicativemultiplier", out decimal totalMultiplier))
                return $"Existing {kind} nonretroactive Essence snapshot or discount is missing.";
            decimal cost = checked(baseEssence * Math.Max(0m, (multiplier + additive) * totalMultiplier * (1m - discount / 100m)));
            if (!settings.DoNotRoundEssenceInternally) cost = decimal.Round(cost, settings.EssenceDecimals, MidpointRounding.AwayFromZero);
            spent = checked(spent + cost);
        }
        current = checked(maximum - spent);
        return current <= 0m ? "Existing remaining Essence is non-positive under canonical mixed augmentation costs." : null;
    }

    /// <summary>
    /// Native Character.RefreshEssenceLossImprovements uses StandardRound (away from zero),
    /// not midpoint rounding. This implementation admits ordinary, uninitiated Career
    /// attributes with no chargen/quality modifier stack. Karma/PP burn cases explicitly block.
    /// It preserves base/Karma and replaces the same serialized EssenceLoss improvements as native.
    /// </summary>
    private static string? ProjectCareerSpecialAttributeLoss(XElement root, XElement profile, decimal maximumEssence, decimal nextEssence,
        List<CharacterAugmentationPurchaseEffect> effects)
    {
        var enabled = new List<string>();
        var active = root.Element("improvements")?.Elements("improvement")
            .Where(i => ReadOptionalScalar(i, "enabled") is "1" or "True").ToArray() ?? [];
        foreach ((string field, string attribute) in new[] { ("magenabled", "MAG"), ("resenabled", "RES"), ("depenabled", "DEP") })
        {
            if (!TryReadBoolean(root, field, out bool value)) return $"Saved {field} state is unavailable for Essence/Magic consequences.";
            if (!value && active.Any(i => ReadOptionalScalar(i, "improvementttype") == "Attribute"
                && ReadOptionalScalar(i, "improvedname") == attribute && IsAugmentationAttributeEnableMarker(i)))
                return $"Saved {field} state disagrees with its native enableattribute improvement.";
            if (value) enabled.Add(attribute);
        }
        if (enabled.Count == 0) return null;
        if (!TryReadDecimal(root, "essenceatspecialstart", out decimal acquisition))
            return "Essence at special-attribute acquisition is unavailable; Magic/Resonance loss cannot be inferred.";
        if (acquisition == decimal.MinValue) return null; // Native explicitly exempts this character from Essence burn.
        if (acquisition != maximumEssence)
            return "Special attributes acquired after Essence loss require canonical chargen/Career loss reconciliation.";
        if (!TryReadInteger(root, "initiategrade", out int initiation) || initiation != 0
            || ReadOptionalScalar(root, "submersiongrade") is not ("" or "0"))
            return "Initiation/submersion affects the special-attribute maximum and requires its native authority.";
        if (!TryReadBoolean(root, "adept", out bool adept) || !TryReadBoolean(root, "magician", out bool magician))
            return "The exact adept/magician flags are required for Magic and power-point consequences.";
        if (adept) return "Adept and mystic-adept power-point loss/burn requires the native Career power authority.";
        _ = magician;
        if (active.Any(i => ReadOptionalScalar(i, "improvementttype") == "Attribute"
            && ReadOptionalScalar(i, "improvedname") is "MAG" or "MAGAdept" or "RES" or "DEP" or "MAGBase" or "RESBase" or "DEPBase"
            && ReadOptionalScalar(i, "improvementsource") != "EssenceLoss" && !IsAugmentationAttributeEnableMarker(i)))
            return "An active special-attribute modifier stack requires canonical Magic/Resonance reconciliation.";
        if (active.Any(i => ReadOptionalScalar(i, "improvementsource") == "EssenceLoss"
            && (ReadOptionalScalar(i, "improvementttype") != "Attribute"
                || ReadOptionalScalar(i, "improvedname") is not ("MAG" or "MAGAdept" or "RES" or "DEP"))))
            return "Existing EssenceLoss contains power-point or other non-attribute effects.";
        TryReadBoolean(profile, "specialkarmacostbasedonshownvalue", out bool shown);
        TryReadBoolean(profile, "esslossreducesmaximumonly", out bool maximumOnly);
        int loss = CharacterCyberwarePurchaseRules.StandardRound(maximumEssence - nextEssence);
        foreach (string name in enabled)
        {
            var attributes = root.Element("attributes")?.Elements("attribute").Where(a => ReadOptionalScalar(a, "name") == name).Take(2).ToArray() ?? [];
            if (attributes.Length != 1 || !TryReadInteger(attributes[0], "base", out int basis) || basis < 0
                || !TryReadInteger(attributes[0], "karma", out int karma) || karma < 0
                || !TryReadInteger(attributes[0], "metatypemin", out int minimum)
                || !TryReadInteger(attributes[0], "metatypemax", out int maximum) || minimum < 0 || maximum < minimum)
                return $"Saved {name} base/Karma/metatype bounds are incomplete or ambiguous.";
            var old = active.Where(i => ReadOptionalScalar(i, "improvementsource") == "EssenceLoss"
                && ReadOptionalScalar(i, "improvedname") == name).ToArray();
            if (old.Length > 1 || old.Any(i => ReadOptionalScalar(i, "unique").Length != 0
                || ReadOptionalScalar(i, "condition").Length != 0 || ReadOptionalScalar(i, "rating") != "1"))
                return $"Existing {name} EssenceLoss stacking requires the native attribute authority.";
            int previousReduction = old.Length == 0 ? 0 : -EffectInteger(ReadOptionalScalar(old[0], "min"), 0)
                - EffectInteger(ReadOptionalScalar(old[0], "aug"), 0);
            int minimumLoss = shown ? 0 : maximumOnly ? Math.Max(previousReduction, loss + Math.Min(maximum, basis + minimum + karma) - maximum)
                : Math.Max(previousReduction, loss);
            // Native can irreversibly burn purchased Karma levels below the raw minimum.
            // This lane must not represent that operation as a reversible modifier alone.
            if (!shown && (minimumLoss > basis + minimum || basis + minimum + karma > maximum))
                return $"The {name} purchase consequence requires native Career Karma burn reconciliation.";
            effects.Add(new("Attribute", name, 0m, Minimum: -minimumLoss, Maximum: shown ? 0 : -loss,
                Augmented: shown ? -loss : 0m, ImprovementSource: "EssenceLoss"));
        }
        // Native writes a parallel MAGAdept loss row even when it aliases MAG.
        if (enabled.Contains("MAG", StringComparer.Ordinal))
        {
            var magic = effects.Last(e => e.ImprovementSource == "EssenceLoss" && e.ImprovedName == "MAG");
            effects.Add(magic with { ImprovedName = "MAGAdept" });
        }
        return null;
    }

    private static bool IsAugmentationAttributeEnableMarker(XElement improvement)
        => ReadOptionalScalar(improvement, "unique") == "enableattribute"
            && ReadOptionalScalar(improvement, "rating") == "0"
            && new[] { "min", "max", "aug", "augmax", "val" }.All(f => TryReadDecimal(improvement, f, out decimal value) && value == 0m);
}
