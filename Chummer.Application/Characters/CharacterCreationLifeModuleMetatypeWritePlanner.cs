using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Characters;

/// <summary>
/// Source-admitted racial contribution to a whole Life Modules runner. This is
/// not a Karma quote, a talent choice or a partial workspace write. The finalizer
/// must still compose attributes, purchases, finances and lifecycle atomically.
/// </summary>
internal static class CharacterCreationLifeModuleMetatypeWritePlanner
{
    internal static CharacterCreationLifeModuleMetatypeWritePlanResult Build(
        WorkspaceStoredDocument workspace, CharacterCreationFoundationDraftLedger draft,
        CharacterCreationFoundationSequenceWritePlan effects, CharacterCreationLegalOption expectedMetatype,
        ICharacterSourceDataContext context)
    {
        try
        {
            if (workspace.Document.RulesetId != RulesetDefaults.Sr5
                || workspace.Id != draft.WorkspaceId || draft.CharacterEffectsApplied
                || !draft.ModuleSelectionFinished || draft.DraftDigest != CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(draft)
                || draft.BaseRawCharacterXmlDigest != CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(workspace.Document.Content)
                || effects.WorkspaceId != workspace.Id || effects.DraftDigest != draft.DraftDigest
                || effects.DraftRevision != draft.DraftRevision || effects.RawCharacterXmlDigest != draft.BaseRawCharacterXmlDigest
                || effects.SourceDigest != draft.SourceDigest
                || effects.PlanDigest != CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(effects with { PlanDigest = string.Empty })
                || !context.TryResolveCreationMetatypeCatalog(out var catalog) || !catalog.IsAuthoritative
                || catalog.Blockers.Count != 0 || !catalog.SourceContext.IsAuthoritative || catalog.SourceContext.Blockers.Count != 0
                || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(catalog.SourceContext.AuthorityDigest)
                || catalog.Options.SingleOrDefault(option => option.Label == draft.RequestedMetatype)
                    is not { IsEnabled: true, Blockers.Count: 0, GrantedQualities.Count: <= 32 } metatype
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    CharacterCreationFoundationService.MapMetatypeOption(metatype), expectedMetatype)
                || !context.TryResolveCreationLifeModuleMetatypeSources(metatype.OptionId, out var sources)
                || sources.Count != metatype.GrantedQualities.Count
                || sources.Any(source => !CharacterCreationTalentQualitySourceRules.IsValidSource(source))
                || sources.Select(source => source.SourceId).Distinct(StringComparer.Ordinal).Count() != sources.Count)
                return Failed(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);

            XElement root = XDocument.Parse(workspace.Document.Content).Root ?? throw new InvalidDataException();
            if (root.Name != "character" || root.Elements("buildmethod").Count() != 1
                || root.Element("buildmethod")!.Value != CharacterCreationBuildMethods.LifeModules
                || root.Elements("created").Count() != 1 || !bool.TryParse(root.Element("created")!.Value, out bool created) || created
                || root.Elements("qualities").Count() > 1 || root.Elements("improvements").Count() > 1)
                return Failed(CharacterCreationFoundationBlockers.CharacterDocumentInvalid);
            var existing = root.Element("qualities")?.Elements("quality").Select(item => new XElement(item)).ToArray() ?? [];
            var planned = effects.QualityXml.Select(xml => XElement.Parse(xml)).ToArray();
            var occupiedSourceIds = existing.Concat(planned).Select(item => item.Element("sourceid")?.Value ?? string.Empty)
                .Where(value => value.Length > 0).ToHashSet(StringComparer.Ordinal);
            if (sources.Any(source => occupiedSourceIds.Contains(source.SourceId)))
                return Failed(CharacterCreationFoundationBlockers.PendingDraftConflict);

            string seed = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
            { Semantics = "life-module-racial-contribution/v1", draft.DraftDigest, effects.PlanDigest,
                Metatype = metatype, CatalogContext = catalog.SourceContext, Sources = sources });
            var qualities = new List<XElement>();
            var improvements = new List<XElement>();
            var gear = new List<(XElement Saved, CharacterCreationTalentGearSource Source)>();
            var flags = new HashSet<string>(StringComparer.Ordinal);
            if (metatype.BaseBonuses is { } bonuses)
            {
                if (bonuses.Armor < 0 || bonuses.Reach < 0 || bonuses.LifestyleCostPercent < 0)
                    return Failed(CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
                foreach (var (type, value, unique) in new[]
                {
                    ("Armor", bonuses.Armor, "group0"), ("Reach", bonuses.Reach, string.Empty),
                    ("LifestyleCost", bonuses.LifestyleCostPercent, string.Empty)
                })
                    if (value != 0) improvements.Add(CharacterCreationAwakenedLegacyProjector.Improvement(
                        type, string.Empty, metatype.OptionId, "Metatype", value, unique));
            }
            for (int index = 0; index < sources.Count; index++)
            {
                var source = sources[index];
                var declaration = metatype.GrantedQualities[index];
                var definition = XElement.Parse(source.CanonicalSourceXml);
                if (source.Name != declaration.Name || source.Reference != declaration.Name || source.ForcedSelection != string.Empty
                    || !string.Equals(definition.Element("category")?.Value, declaration.Polarity, StringComparison.OrdinalIgnoreCase))
                    return Failed(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
                string id = CharacterCreationFinalizationProjector.StableGuid($"life-module-racial:{seed}:{source.SourceId}").ToString("D");
                string extra = CharacterCreationAwakenedLegacyProjector.CompileBonus(definition.Element("bonus"),
                    string.Empty, [], source, id, flags, improvements, gear);
                if (!CharacterCreationLegacySourceProjector.TryBuildGrantedQualityInstance(source, id, extra, "Metatype", out var saved))
                    return Failed(CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
                qualities.Add(saved);
            }
            var other = new XElement("character", new XElement("qualities", existing.Concat(planned)));
            foreach (var source in sources)
                CharacterCreationAwakenedLegacyProjector.CheckRestrictions(XElement.Parse(source.CanonicalSourceXml), other, qualities, flags);
            // Reverse restrictions matter too: a module quality cannot silently
            // forbid a racial grant simply because the module plan was made first.
            if (!context.TryResolveCreationFoundationEffectSources(out var effectSources) || effectSources is null
                || !effectSources.TryCreateAuthorities(out _, out var qualityAuthority, out _, out var effectContextDigest)
                || effectContextDigest != effects.SourceContextDigest || qualityAuthority is null)
                return Failed(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
            foreach (var plannedQuality in planned.Where(item => item.Element("qualitysource")?.Value != "LifeModule"))
            {
                if (!qualityAuthority.TryResolveExact(plannedQuality.Element("name")!.Value, out var binding)
                    || binding!.SourceId != plannedQuality.Element("sourceid")?.Value
                    || !qualityAuthority.TryGetDefinition(binding, out var definition, out _) || definition is null)
                    return Failed(CharacterCreationFoundationBlockers.FinalizationEffectLedgerConflict);
                // The group writer already checked the quality against its full
                // module graph; add the racial grants without mutating that graph.
                CharacterCreationAwakenedLegacyProjector.CheckRestrictions(definition, other, qualities, flags);
            }

            if (!context.TryResolveCreationMetatypeCatalog(out var finalCatalog)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(catalog, finalCatalog)
                || !context.TryResolveCreationLifeModuleMetatypeSources(metatype.OptionId, out var finalSources)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(sources, finalSources))
                return Failed(CharacterCreationFoundationBlockers.SourceDigestConflict);
            var plan = new CharacterCreationLifeModuleMetatypeWritePlan("life-module-racial-contribution/v1",
                draft.DraftDigest, effects.PlanDigest, metatype, catalog.SourceContext, sources,
                qualities.Select(item => item.ToString(SaveOptions.DisableFormatting)).ToArray(),
                improvements.Select(item => item.ToString(SaveOptions.DisableFormatting)).ToArray(),
                gear.Select(item => item.Saved.ToString(SaveOptions.DisableFormatting)).ToArray(),
                flags.Order(StringComparer.Ordinal).ToArray(), string.Empty);
            return new(plan with { PlanDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(plan) }, []);
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException
            or InvalidDataException or OverflowException or FormatException)
        {
            return Failed(CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Failed(CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);
        }
    }

    private static CharacterCreationLifeModuleMetatypeWritePlanResult Failed(string blocker) => new(null, [blocker]);
}

internal sealed record CharacterCreationLifeModuleMetatypeWritePlanResult(CharacterCreationLifeModuleMetatypeWritePlan? Plan,
    IReadOnlyList<string> Blockers);
