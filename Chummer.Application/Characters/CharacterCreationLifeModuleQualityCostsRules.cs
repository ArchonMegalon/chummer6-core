using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal static class CharacterCreationLifeModuleQualityCostsRules
{
    internal static CharacterCreationLifeModuleQualityCostsQuote? Quote(
        CharacterCreationFoundationSequenceWritePlan effects, CharacterCreationLifeModuleMetatypeWritePlan racial,
        CharacterCreationLifeModuleTalentWritePlan talent, CharacterCreationKarmaQualitiesPolicy policy)
    {
        try
        {
            if (!CharacterCreationKarmaQualitiesRules.IsValidCostPolicy(policy, CharacterCreationKarmaQualitiesPolicy.LifeModulesSchemaV1)
                || effects.PlanDigest != Hash(effects with { PlanDigest = string.Empty })
                || racial.PlanDigest != Hash(racial with { PlanDigest = string.Empty })
                || talent.PlanDigest != Hash(talent with { PlanDigest = string.Empty })
                || racial.EffectPlanDigest != effects.PlanDigest || talent.EffectPlanDigest != effects.PlanDigest
                || talent.MetatypePlanDigest != racial.PlanDigest || talent.Talent.KarmaCost < 0
                || policy.SettingsProfileId != racial.SourceContext.SettingsProfileId
                || policy.RawProfileInputsDigest != racial.SourceContext.RawProfileInputsDigest
                || policy.SettingsProfileId != talent.Catalog.SettingsProfileId
                || policy.RawProfileInputsDigest != talent.Catalog.RawProfileInputsDigest
                || policy.SourceInputsDigest != talent.Catalog.SourceInputsDigest
                || policy.Costs.KarmaMultiplier != talent.Catalog.KarmaQuality)
                return null;

            var qualities = effects.QualityXml.Concat(racial.QualityXml).Concat(talent.QualityXml)
                .Select(xml => Read(xml, "quality")).ToArray();
            if (qualities.Length > 131_072) return null;
            bool freeMentor = qualities.Any(item => item.Element("name")?.Value is "The Beast's Way" or "The Spiritual Way");
            var lines = new List<CharacterCreationLifeModuleQualityCostLine>();
            var ids = new HashSet<Guid>();
            foreach (var quality in qualities)
            {
                string origin = Required(quality, "qualitysource"), type = Required(quality, "qualitytype");
                string id = Required(quality, "guid"), source = Required(quality, "sourceid"), name = Required(quality, "name");
                if (!Guid.TryParse(id, out var parsedId) || parsedId == Guid.Empty || !ids.Add(parsedId)
                    || !Guid.TryParse(source, out var sourceId) || sourceId == Guid.Empty
                    || !int.TryParse(Required(quality, "bp"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int bp))
                    return null;
                // These owners are already charged by ModuleKarmaCost, never a second time.
                if (origin == "LifeModule")
                {
                    if (type != "LifeModule") return null;
                    continue;
                }
                if (origin is not ("Selected" or "Improvement" or "QualityLevelImprovement" or "Metatype"
                    or "MetatypeRemovable" or "MetatypeRemovedAtChargen" or "Heritage")
                    || type is not ("Positive" or "Negative") || type == "Positive" && bp < 0 || type == "Negative" && bp > 0)
                    return null;
                bool metagenic = Boolean(quality, "metagenic");
                bool limit = Boolean(quality, "contributetolimit"), karma = Boolean(quality, "contributetobp");
                bool racialOrigin = origin is "Metatype" or "MetatypeRemovable" or "Heritage";
                if (racialOrigin || origin == "MetatypeRemovedAtChargen") limit = false;
                if (racialOrigin) karma = false;
                if (metagenic && policy.MetagenicLimit > 0 || freeMentor && name == "Mentor Spirit")
                    (limit, karma) = (false, false);
                lines.Add(new(id, source, name, origin, bp, limit, karma,
                    metagenic && policy.MetagenicLimit > 0 && !racialOrigin && origin != "MetatypeRemovedAtChargen"));
            }
            decimal freePositive = 0m, freeNegative = 0m;
            foreach (string xml in effects.ImprovementXml.Concat(racial.ImprovementXml).Concat(talent.ImprovementXml))
            {
                var improvement = Read(xml, "improvement");
                string type = Required(improvement, "improvementttype");
                if (type is not ("FreePositiveQualities" or "FreeNegativeQualities"))
                {
                    if (!IsQualityCostIndependent(type)) return null;
                    continue;
                }
                if (Required(improvement, "enabled") != "1" || Required(improvement, "addtorating") != "0"
                    || new[] { "improvedname", "condition", "unique", "uniquename", "exclude", "target" }
                        .Any(field => !string.IsNullOrEmpty(improvement.Element(field)?.Value))
                    || new[] { "min", "max", "aug", "augmax" }.Any(field => Required(improvement, field) != "0")
                    || !decimal.TryParse(Required(improvement, "val"), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out decimal value))
                    return null;
                if (type == "FreePositiveQualities") freePositive = checked(freePositive + value);
                else freeNegative = checked(freeNegative + value);
            }

            if (!CharacterCreationQualityCostRules.TryCalculate(policy.Costs, policy.QualityKarmaLimit,
                lines.Select(row => new CharacterCreationQualityCostItem(row.SourceKarma, row.CountsAgainstQualityLimit,
                    row.CountsAgainstKarma, row.CountsAgainstMetagenicLimit)).ToArray(), 0, freePositive, freeNegative, out var costs))
                return null;
            var blockers = new HashSet<string>(StringComparer.Ordinal);
            if (costs.PositiveLimitKarma > policy.QualityKarmaLimit && !policy.MayExceedPositiveLimit)
                blockers.Add(CharacterCreationQualitiesBlockers.PositiveLimitExceeded);
            if (costs.NegativeLimitKarma > policy.QualityKarmaLimit && !policy.MayExceedNegativeLimit)
                blockers.Add(CharacterCreationQualitiesBlockers.NegativeLimitExceeded);
            int difference = checked(costs.MetagenicPositiveKarma - costs.MetagenicNegativeKarma);
            if (policy.MetagenicLimit > 0)
            {
                if (costs.MetagenicPositiveKarma > policy.MetagenicLimit || costs.MetagenicNegativeKarma > policy.MetagenicLimit)
                    blockers.Add(CharacterCreationQualitiesBlockers.MetagenicLimitExceeded);
                if (difference is not (0 or 1)) blockers.Add(CharacterCreationQualitiesBlockers.MetagenicImbalanced);
            }
            int balance = difference == 1 ? 1 : 0;
            var result = new CharacterCreationLifeModuleQualityCostsQuote(policy, effects.PlanDigest, racial.PlanDigest,
                talent.PlanDigest, lines.OrderBy(row => row.InstanceId, StringComparer.Ordinal).ToArray(), freePositive,
                freeNegative, costs, balance, checked(costs.NetKarmaSpent + balance - (decimal)talent.Talent.KarmaCost),
                blockers.Order(StringComparer.Ordinal).ToArray(), string.Empty);
            return result with { QuoteDigest = Hash(result) };
        }
        catch (Exception error) when (error is XmlException or InvalidDataException or ArgumentException or InvalidOperationException
            or OverflowException or FormatException)
        { return null; }
    }

    // Other source-admitted effects are retained in their owning plans. Unknown
    // cost modifiers (FreeQuality, mastery spell-point conversion, etc.) are not
    // silently ignored by a future expansion of the source compiler.
    private static bool IsQualityCostIndependent(string type) => type is
        "Attributelevel" or "Attribute" or "SkillLevel" or "SkillGroupLevel" or "FreeKnowledgeSkills"
        or "QualityLevel" or "SpecificQuality" or "Notoriety" or "TrustFund" or "DamageResistance"
        or "BlockSkillCategoryDefault" or "SkillGroupCategoryDisable" or "SkillCategoryKarmaCostMultiplier"
        or "SkillCategorySpecializationKarmaCostMultiplier" or "SkillGroupCategoryKarmaCostMultiplier"
        or "SkillCategoryPointCostMultiplier" or "Armor" or "Reach" or "LifestyleCost" or "Gear" or "Skill"
        or "PathogenContactResist" or "PathogenIngestionResist" or "PathogenInhalationResist" or "PathogenInjectionResist"
        or "ToxinContactResist" or "ToxinIngestionResist" or "ToxinInhalationResist" or "ToxinInjectionResist"
        or "SpecialTab" or "SpecialSkills" or "BlockSpellDescriptor" or "LimitSpellCategory";

    private static XElement Read(string xml, string name)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 131_072 });
        var row = XElement.Load(reader);
        if (row.Name != name || row.Elements().GroupBy(item => item.Name).Any(group => group.Count() != 1))
            throw new InvalidDataException();
        return row;
    }
    private static string Required(XElement row, string name) => row.Element(name)?.Value ?? throw new InvalidDataException();
    private static bool Boolean(XElement row, string name)
        => bool.TryParse(Required(row, name), out bool value) ? value : throw new InvalidDataException();
    private static string Hash<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
}
