using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Foundation-only attribute projection. Module levels remain Improvements;
/// only explicit purchases become paid Karma levels. Does not write XML or
/// silently choose Mundane. Awakened attributes require an explicit bound talent plan.
/// </summary>
internal static class CharacterCreationLifeModuleAttributeRules
{
    private static readonly string[] Attributes = ["BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL", "EDG"];

    internal static CharacterCreationLifeModuleAttributeQuoteResult Evaluate(string characterXml,
        CharacterCreationFoundationSequenceWritePlan effects, CharacterCreationLifeModuleMetatypeWritePlan racial,
        CharacterCreationAttributePolicy policy, IReadOnlyList<CharacterCreationLifeModuleAttributePurchase>? purchases,
        CharacterCreationLifeModuleTalentWritePlan? talent = null)
    {
        try
        {
            if (policy is not { Schema: CharacterCreationAttributePolicy.SchemaV1,
                    BuildMethod: CharacterCreationBuildMethods.LifeModules, KarmaAttribute: > 0,
                    MaxNumberMaxAttributesCreate: >= 0, SourceAnchorIds.Count: > 0 }
                || policy.AuthorityDigest != CharacterCreationAttributePolicyAuthority.ComputeDigest(policy)
                || policy.SettingsProfileId != racial.SourceContext.SettingsProfileId
                || policy.RawProfileInputsDigest != racial.SourceContext.RawProfileInputsDigest
                || effects.PlanDigest != CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(effects with { PlanDigest = string.Empty })
                || racial.PlanDigest != CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(racial with { PlanDigest = string.Empty })
                || racial.EffectPlanDigest != effects.PlanDigest || racial.DraftDigest != effects.DraftDigest
                || effects.RawCharacterXmlDigest != CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(characterXml)
                || racial.Metatype is not { IsEnabled: true, Blockers.Count: 0, Attributes.Count: 13 }
                || racial.Metatype.Attributes.Select(item => item.AttributeId).Distinct(StringComparer.Ordinal).Count() != 13
                || racial.Metatype.Attributes.Any(item => item.Minimum < 0 || item.Maximum < item.Minimum
                    || item.AugmentedMaximum < item.Maximum)
                || Attributes.Any(id => !racial.Metatype.Attributes.Any(item => item.AttributeId == id)))
                return Failed(CharacterCreationAttributesBlockers.AuthorityUnavailable);
            if (talent is not null && (talent.PlanDigest != CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(talent with { PlanDigest = string.Empty })
                || talent.EffectPlanDigest != effects.PlanDigest || talent.MetatypePlanDigest != racial.PlanDigest
                || talent.Catalog.SettingsProfileId != policy.SettingsProfileId
                || talent.Catalog.RawProfileInputsDigest != policy.RawProfileInputsDigest
                || talent.Talent.EnabledAttribute is not (null or "MAG" or "RES")))
                return Failed(CharacterCreationAttributesBlockers.AuthorityUnavailable);
            string[] attributes = talent?.Talent.EnabledAttribute is { } enabled ? [.. Attributes, enabled] : Attributes;
            if (purchases is { Count: > 10 } || purchases?.Any(item => item is null || item.KarmaLevels < 0
                    || !attributes.Contains(item.AttributeId, StringComparer.Ordinal)) == true
                || (purchases is not null && purchases.Select(item => item.AttributeId).Distinct(StringComparer.Ordinal).Count() != purchases.Count))
                return Failed(CharacterCreationAttributesBlockers.AllocationInvalid);
            var requested = purchases?.ToArray() ?? [];
            XElement root = XDocument.Parse(characterXml).Root ?? throw new InvalidDataException();
            // Existing imported mechanics need their own reconciliation. An empty
            // typed Foundation must not ignore a saved modifier or paid allocation.
            if (root.Name != "character" || root.Element("buildmethod")?.Value != CharacterCreationBuildMethods.LifeModules
                || root.Elements("created").Count() != 1 || !bool.TryParse(root.Element("created")?.Value, out bool created) || created
                || root.Elements("qualities").Any(item => item.HasElements)
                || root.Elements("improvements").Any(item => item.HasElements)
                || root.Elements("attributes").Elements("attribute").Any(item =>
                    item.Elements().Where(value => value.Name.LocalName is "base" or "karma").Any(value =>
                        !int.TryParse(value.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int points) || points != 0)))
                return Failed(CharacterCreationAttributesBlockers.LegacyAttributeStateRequiresImport);
            var levels = attributes.ToDictionary(id => id, _ => 0L, StringComparer.Ordinal);
            foreach (string xml in effects.ImprovementXml.Concat(racial.ImprovementXml).Concat(talent?.ImprovementXml ?? []))
            {
                var improvement = XElement.Parse(xml);
                string type = improvement.Element("improvementttype")?.Value ?? string.Empty;
                if (type != "Attributelevel")
                {
                    if (type == "Attribute" && talent?.Talent.EnabledAttribute is { } active
                        && improvement.Element("improvedname")?.Value == active
                        && improvement.Element("unique")?.Value == "enableattribute"
                        && improvement.Element("val")?.Value == "0" && improvement.Element("rating")?.Value == "0"
                        && improvement.Element("min")?.Value == "0" && improvement.Element("max")?.Value == "0"
                        && improvement.Element("aug")?.Value == "0" && improvement.Element("augmax")?.Value == "0"
                        && improvement.Element("enabled")?.Value == "1" && string.IsNullOrEmpty(improvement.Element("condition")?.Value))
                        continue;
                    if (!IsAttributeIndependent(type)) return Failed(CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
                    continue;
                }
                string id = improvement.Element("improvedname")?.Value ?? string.Empty;
                if (!levels.ContainsKey(id) || improvement.Element("enabled")?.Value != "1"
                    || !string.IsNullOrEmpty(improvement.Element("condition")?.Value)
                    || !string.IsNullOrEmpty(improvement.Element("unique")?.Value)
                    || !string.IsNullOrEmpty(improvement.Element("uniquename")?.Value)
                    || !int.TryParse(improvement.Element("val")?.Value, NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out int value))
                    return Failed(CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
                levels[id] = checked(levels[id] + value);
            }
            var rows = new List<CharacterCreationLifeModuleAttributeValue>();
            var blockers = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in attributes)
            {
                var range = racial.Metatype.Attributes.Single(item => item.AttributeId == id);
                int bought = requested.SingleOrDefault(item => item.AttributeId == id)?.KarmaLevels ?? 0;
                int free = checked((int)Math.Min(levels[id], range.Maximum - range.Minimum));
                // Legacy Attribute.FreeBase / TotalBase: sum grants, clamp to
                // the metatype span, then apply the minimum floor. Do not clamp
                // each occurrence separately or charge its levels a second time.
                bool special = id is "EDG" or "MAG" or "RES";
                int minimum = Math.Max(range.Minimum, special || range.Maximum == 0 ? 0 : 1);
                int baseline = checked((int)Math.Max((long)range.Minimum + free, minimum));
                int current = checked(baseline + bought);
                int costBase = policy.AlternateMetatypeAttributeKarma
                    ? checked((int)Math.Max((long)free + 1, special || range.Maximum == 0 ? 0 : 1))
                    : baseline;
                // There are no Priority allocations: ReverseAttributePriorityOrder
                // cannot change this base. Shared triangular arithmetic is exact.
                if (!CharacterCreationAttributeCostRules.TryCalculate(costBase, bought, policy.KarmaAttribute, out int cost))
                    return Failed(CharacterCreationAttributesBlockers.AllocationInvalid);
                if (current > range.Maximum) blockers.Add(CharacterCreationAttributesBlockers.AllocationInvalid);
                // Reuse actual source anchors. Module/version provenance remains
                // in the bound effect plan; do not guess a module path from a
                // version's source ID.
                string[] anchors = racial.Metatype.SourceAnchorIds.Concat(policy.SourceAnchorIds)
                    .Concat(id == talent?.Talent.EnabledAttribute ? talent.Summary.SourceAnchorIds : [])
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                rows.Add(new(id, range.Minimum, range.Maximum, levels[id], free, bought, current, cost, anchors));
            }
            if (rows.Count(row => row.AttributeId is not ("EDG" or "MAG" or "RES") && row.Current == row.Maximum) > policy.MaxNumberMaxAttributesCreate)
                blockers.Add(CharacterCreationAttributesBlockers.MaximumAttributeCountExceeded);
            var quote = new CharacterCreationLifeModuleAttributeQuote(policy, effects.PlanDigest, racial.PlanDigest,
                rows.ToArray(), rows.Sum(row => (decimal)row.KarmaCost), blockers.Order(StringComparer.Ordinal).ToArray(), string.Empty)
                { TalentPlanDigest = talent?.PlanDigest };
            quote = quote with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quote) };
            return new(quote, quote.Blockers);
        }
        catch (Exception error) when (error is XmlException or InvalidDataException or InvalidOperationException
            or ArgumentException or OverflowException or FormatException)
        {
            return Failed(CharacterCreationAttributesBlockers.AllocationInvalid);
        }
    }

    // Explicitly bounded to the source compilers used above. A newly supported
    // attribute/cost modifier must be implemented here, not silently ignored.
    private static bool IsAttributeIndependent(string type) => type is
        "SkillLevel" or "SkillGroupLevel" or "FreeKnowledgeSkills" or "FreePositiveQualities" or "FreeNegativeQualities"
        or "QualityLevel" or "SpecificQuality" or "Notoriety" or "TrustFund" or "DamageResistance"
        or "BlockSkillCategoryDefault" or "SkillGroupCategoryDisable" or "SkillCategoryKarmaCostMultiplier"
        or "SkillCategorySpecializationKarmaCostMultiplier" or "SkillGroupCategoryKarmaCostMultiplier"
        or "SkillCategoryPointCostMultiplier" or "Armor" or "Reach" or "LifestyleCost" or "Gear" or "Skill"
        or "PathogenContactResist" or "PathogenIngestionResist" or "PathogenInhalationResist" or "PathogenInjectionResist"
        or "ToxinContactResist" or "ToxinIngestionResist" or "ToxinInhalationResist" or "ToxinInjectionResist"
        or "SpecialTab" or "SpecialSkills" or "BlockSpellDescriptor" or "LimitSpellCategory";

    private static CharacterCreationLifeModuleAttributeQuoteResult Failed(string blocker) => new(null, [blocker]);
}

internal sealed record CharacterCreationLifeModuleAttributeQuoteResult(CharacterCreationLifeModuleAttributeQuote? Quote,
    IReadOnlyList<string> Blockers);
