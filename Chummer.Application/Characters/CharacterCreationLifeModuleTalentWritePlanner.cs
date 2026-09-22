using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Purchased talent contribution after module/racial grants. Does not
/// grant a free Magic/Resonance rating, spend a different build-method budget,
/// or write a partially finalized runner.</summary>
internal static class CharacterCreationLifeModuleTalentWritePlanner
{
    internal static CharacterCreationLifeModuleTalentWritePlanResult Build(string characterXml,
        CharacterCreationFoundationSequenceWritePlan effects, CharacterCreationLifeModuleMetatypeWritePlan racial,
        CharacterCreationLifeModuleTalentSelection? selection, ICharacterSourceDataContext context)
    {
        CharacterCreationLifeModuleTalentCatalog? catalog = null;
        try
        {
            if (effects.PlanDigest != Digest(effects with { PlanDigest = string.Empty })
                || racial.PlanDigest != Digest(racial with { PlanDigest = string.Empty })
                || racial.EffectPlanDigest != effects.PlanDigest || racial.DraftDigest != effects.DraftDigest
                || effects.RawCharacterXmlDigest != CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(characterXml)
                || !context.TryResolveCreationLifeModuleTalents(out catalog)
                || catalog is not { Schema: CharacterCreationLifeModuleTalentCatalog.SchemaV1, KarmaQuality: > 0,
                    Options.Count: > 0, SourceAnchorIds.Count: > 0 }
                || catalog.AuthorityDigest != Digest(catalog with { AuthorityDigest = string.Empty })
                || catalog.SettingsProfileId != racial.SourceContext.SettingsProfileId
                || catalog.RawProfileInputsDigest != racial.SourceContext.RawProfileInputsDigest
                || catalog.Options.Select(item => item.OptionId).Distinct(StringComparer.Ordinal).Count() != catalog.Options.Count)
                return Failed(null, CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
            if (selection is null) return Failed(catalog, CharacterCreationLifeModuleTalentCatalog.SelectionRequired);
            if (!CharacterCreationKarmaTalentAuthority.IsOptionId(selection.OptionId)
                || catalog.Options.SingleOrDefault(item => item.OptionId == selection.OptionId)
                    is not { IsEnabled: true, Blockers.Count: 0 } talent
                || !catalog.SkillUnlockChoices.TryGetValue(talent.OptionId, out var unlockChoices))
                return Failed(catalog, CharacterCreationLifeModuleTalentCatalog.SelectionInvalid);
            var projected = talent.OptionId == CharacterCreationKarmaTalentCatalog.MundaneOptionId
                ? CharacterCreationKarmaTalentAuthority.Mundane($"settings.xml#setting:{catalog.SettingsProfileId}")
                : CharacterCreationKarmaTalentAuthority.Project(XElement.Parse(talent.SourceNodeXml), catalog.KarmaQuality, true);
            if (!CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(talent, projected)
                || !CharacterCreationLifeModuleTalentAuthority.TryProjectUnlockChoices(talent, out var expectedUnlocks)
                || !unlockChoices.SequenceEqual(expectedUnlocks, StringComparer.Ordinal))
                return Failed(catalog, CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
            if (selection.SkillUnlock is not null && !unlockChoices.Contains(selection.SkillUnlock, StringComparer.Ordinal))
                return Failed(catalog, CharacterCreationLifeModuleTalentCatalog.SelectionInvalid);
            if (unlockChoices.Count > 1 && selection.SkillUnlock is null)
                return Failed(catalog, CharacterCreationLifeModuleTalentCatalog.SkillUnlockRequired);

            XElement root = XDocument.Parse(characterXml).Root ?? throw new InvalidDataException();
            string[] flagNames = ["magenabled", "resenabled", "depenabled", "magician", "adept", "technomancer", "ai"];
            if (root.Name != "character" || root.Element("buildmethod")?.Value != CharacterCreationBuildMethods.LifeModules
                || root.Elements("created").Count() != 1 || !bool.TryParse(root.Element("created")?.Value, out bool created) || created
                || root.Elements("qualities").Any(item => item.HasElements)
                || root.Elements("improvements").Any(item => item.HasElements)
                || flagNames.Any(flag => root.Elements(flag).Count() > 1 || root.Element(flag) is { } node
                    && (!bool.TryParse(node.Value, out bool enabled) || enabled)))
                return Failed(catalog, CharacterCreationFoundationBlockers.PendingDraftConflict);
            var before = effects.QualityXml.Concat(racial.QualityXml).Select(xml => XElement.Parse(xml)).ToArray();
            if (!CharacterCreationKarmaTalentAuthority.IsCompatible(talent, racial.Metatype,
                    before.Select(item => item.Element("name")?.Value ?? string.Empty).ToArray())
                || before.Any(item => item.Element("sourceid")?.Value == talent.OptionId)
                || racial.Flags.Count != 0
                || effects.ImprovementXml.Select(xml => XElement.Parse(xml)).Any(item =>
                    item.Element("improvementttype")?.Value is "SpecialTab" or "SpecialSkills"
                    || item.Element("unique")?.Value == "enableattribute"))
                return Failed(catalog, CharacterCreationLifeModuleTalentCatalog.QualityConflict);
            if (!context.TryResolveCreationLifeModuleTalentSource(talent.OptionId, out var source))
                return Failed(catalog, CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);

            var qualities = new List<XElement>();
            var improvements = new List<XElement>();
            var gear = new List<(XElement Saved, CharacterCreationTalentGearSource Source)>();
            var flags = new HashSet<string>(StringComparer.Ordinal);
            if (talent.OptionId == CharacterCreationKarmaTalentCatalog.MundaneOptionId)
            {
                if (source is not null || selection.SkillUnlock is not null || talent.KarmaCost != 0 || talent.EnabledAttribute is not null)
                    return Failed(catalog, CharacterCreationLifeModuleTalentCatalog.SelectionInvalid);
            }
            else
            {
                if (!CharacterCreationTalentQualitySourceRules.IsValidSource(source)
                    || source!.SourceId != talent.OptionId || source.ForcedSelection != string.Empty
                    || source.EffectiveSourceDigest != catalog.SourceInputsDigest || source.CanonicalSourceXml != talent.SourceNodeXml
                    || talent.SourceNodeDigest != CharacterCreationQualitiesRules.ComputeSourceNodeDigest(talent.SourceNodeXml))
                    return Failed(catalog, CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
                string id = CharacterCreationFinalizationProjector.StableGuid(Digest(new
                    { Semantics = "life-module-talent-owner/v1", effects.PlanDigest, Racial = racial.PlanDigest, selection, source })).ToString("D");
                string extra = CharacterCreationAwakenedLegacyProjector.CompileBonus(XElement.Parse(source.CanonicalSourceXml).Element("bonus"),
                    string.Empty, selection.SkillUnlock is null ? [] : [selection.SkillUnlock], source, id, flags, improvements, gear);
                if (!CharacterCreationLegacySourceProjector.TryBuildGrantedQualityInstance(source, id, extra, "Selected", out var saved))
                    return Failed(catalog, CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
                qualities.Add(saved);
            }
            foreach (var (attribute, flag) in new[] { ("MAG", "magenabled"), ("RES", "resenabled"), ("DEP", "depenabled") })
                if ((talent.EnabledAttribute == attribute) != flags.Contains(flag))
                    return Failed(catalog, CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);

            if (!context.TryResolveCreationFoundationEffectSources(out var effectSources) || effectSources is null
                || !effectSources.TryCreateAuthorities(out _, out var qualityAuthority, out _, out var contextDigest)
                || qualityAuthority is null || contextDigest != effects.SourceContextDigest)
                return Failed(catalog, CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
            var prior = new XElement("character", new XElement("qualities", before));
            try
            {
                if (source is not null)
                    CharacterCreationAwakenedLegacyProjector.CheckRestrictions(XElement.Parse(source.CanonicalSourceXml), prior, qualities, flags);
                foreach (var quality in before.Where(item => item.Element("qualitysource")?.Value != "LifeModule"))
                {
                    if (!qualityAuthority.TryResolveExact(quality.Element("name")!.Value, out var binding)
                        || binding!.SourceId != quality.Element("sourceid")?.Value
                        || !qualityAuthority.TryGetDefinition(binding, out var definition, out _) || definition is null)
                        return Failed(catalog, CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
                    // Check the reverse direction as well as the new talent's own exclusions.
                    CharacterCreationAwakenedLegacyProjector.CheckRestrictions(definition, prior, qualities, flags);
                }
            }
            catch (InvalidDataException)
            {
                return Failed(catalog, CharacterCreationLifeModuleTalentCatalog.QualityConflict);
            }
            if (!context.TryResolveCreationLifeModuleTalents(out var finalCatalog)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(catalog, finalCatalog)
                || !context.TryResolveCreationLifeModuleTalentSource(talent.OptionId, out var finalSource)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(source, finalSource))
                return Failed(catalog, CharacterCreationFoundationBlockers.SourceDigestConflict);
            var plan = new CharacterCreationLifeModuleTalentWritePlan("life-module-talent-contribution/v1",
                effects.PlanDigest, racial.PlanDigest, catalog, selection, talent, source,
                qualities.Select(item => item.ToString(SaveOptions.DisableFormatting)).ToArray(),
                improvements.Select(item => item.ToString(SaveOptions.DisableFormatting)).ToArray(),
                gear.Select(item => item.Saved.ToString(SaveOptions.DisableFormatting)).ToArray(),
                flags.Order(StringComparer.Ordinal).ToArray(), string.Empty);
            return new(catalog, plan with { PlanDigest = Digest(plan) }, []);
        }
        catch (Exception error) when (error is XmlException or InvalidDataException or ArgumentException
            or InvalidOperationException or OverflowException or FormatException)
        {
            return Failed(catalog, CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Failed(catalog, CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);
        }
    }

    private static string Digest<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
    private static CharacterCreationLifeModuleTalentWritePlanResult Failed(CharacterCreationLifeModuleTalentCatalog? catalog, string blocker)
        => new(catalog, null, [blocker]);
}

internal sealed record CharacterCreationLifeModuleTalentWritePlanResult(CharacterCreationLifeModuleTalentCatalog? Catalog,
    CharacterCreationLifeModuleTalentWritePlan? Plan, IReadOnlyList<string> Blockers);
