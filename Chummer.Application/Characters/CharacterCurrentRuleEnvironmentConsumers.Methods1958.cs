using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Application.LifeModules;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Engine-only optional current-source bridge. No persistence or admission authority.</summary>
public interface ICharacterCurrentNativeMethodSource1958
{
    bool TryResolveCurrentMetatypeGrantSources(string metatypeOptionId,
        out IReadOnlyList<CharacterCreationTalentQualitySource> sources);
    bool TryResolveCurrentLifeModulesCatalog(out ILifeModulesCatalogService? catalog);
}

/// <summary>Live Career source bytes only. Finalized archive and grant ownership must still be rebuilt.</summary>
public interface ICharacterCareerLifeModuleSource
{
    bool TryResolveFinalizedLifeModuleSources(out ILifeModulesCatalogService? catalog,
        out CharacterCreationFoundationEffectSources? effects);
}

/// <summary>Only native choice values. Revision, source and permissions come from the opaque current execution.</summary>
public sealed record CharacterCurrentKarmaChoices1958(string MetatypeOptionId, string? TalentOptionId = null,
    IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? Attributes = null,
    CharacterCreationKarmaSkillsSelection? Skills = null, decimal? ResourceKarma = null,
    IReadOnlyList<string>? QualityOptionIds = null, IReadOnlyList<CharacterCreationGearSelection>? Gear = null,
    IReadOnlyList<CharacterCreationKarmaContactSelection>? Contacts = null,
    IReadOnlyList<CharacterCreationLifestyleConfiguration>? Lifestyles = null, Guid? StartingLifestyleId = null,
    CharacterCreationMagicResonanceSelections? Magic = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? QualityPromptValues { get; init; }
}

public static partial class CharacterCurrentRuleEnvironmentConsumers
{
    internal sealed record CurrentKarmaPromptOwner2726(Guid SourceId, XElement Quality, XElement[] Improvements, string QuoteDigest);

    // A prompted saved instance is readmitted only by rebuilding its confirmed creation archive
    // and matching the current source bytes and generated owner. Extra alone is never authority.
    internal static bool TryCurrentKarmaPromptOwners2726(WorkspaceStoredDocument current, ICharacterSourceDataContext source,
        out IReadOnlyDictionary<string, CurrentKarmaPromptOwner2726> owners)
    {
        var result = new Dictionary<string, CurrentKarmaPromptOwner2726>(StringComparer.Ordinal);
        owners = result;
        var state = current.Document.AuxiliaryState;
        var archive = state.CharacterCreationFinalizationArchive;
        var foundation = archive?.KarmaAuthority?.Foundation;
        if (foundation?.Qualities?.Selections is null
            || !foundation.Qualities.Selections.Any(item => item?.SourceExecutionSchema == CharacterCreationKarmaQualityPrompts.SourceSchema)) return true;
        if (source is not ICurrentKarmaQualityPromptSource2742 { HasKarmaQualityPrompts2742: true } promptSource
            || !promptSource.TryReadKarmaQualityPromptCatalog2742(out var currentCatalog)
            || !CharacterCreationKarmaQualitiesRules.IsValidCatalog(currentCatalog)
            || currentCatalog!.Schema != CharacterCreationKarmaQualitiesCatalog.SchemaV2) return false;
        if (state.CharacterCreationFinalizationReceipts is not { Count: 1 } receipts
            || receipts[0].Receipt.BuildMethod != CharacterCreationBuildMethods.Karma
            || !CharacterCreationKarmaFinalizationTransaction.IsValidArchive(current.Id, archive!, receipts[0].Receipt)) return false;
        var currentCharacter = ParsePurchaseXml(current.Document.Content);
        if (currentCharacter?.Name != "character") return false;
        var selectedNames = currentCharacter.Elements("qualities").Elements("quality")
            .Select(row => ScalarCreation1949(row, "name")).ToHashSet(StringComparer.Ordinal);
        foreach (var option in foundation.Qualities.Selections.Where(item => item.SourceExecutionSchema == CharacterCreationKarmaQualityPrompts.SourceSchema))
        {
            var candidates = currentCatalog.Options.Where(item => item.OptionId == option.OptionId).ToArray();
            if (candidates is not [var currentOption]
                || currentOption.SourceExecutionSchema != CharacterCreationKarmaQualityPrompts.SourceSchema
                || currentOption.FollowUpChoiceLabel is not null
                || !CharacterCreationKarmaQualitiesRules.IsExactPurchase(currentOption)
                || CharacterCreationKarmaQualityPrompts.Conflicts(currentOption, selectedNames)
                || option.FollowUpChoiceLabel is null) return false;
            var rebound = currentOption with { FollowUpChoiceLabel = option.FollowUpChoiceLabel };
            rebound = rebound with { OptionDigest = CharacterCreationQualitiesRules.ComputeOptionDigest(rebound) };
            if (!CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(rebound, option)
                || !source.TryResolveCareerQualitySource(option.SourceId, out var live) || live is null
                || !SamePurchaseXml(option.SourceNodeXml, live.RawSourceXml)
                || !CharacterCreationKarmaQualitiesRules.IsExactPurchase(option)
                || !CharacterCreationLegacySourceProjector.TryBuildQualityGraph(CharacterCreationKarmaQualityPrompts.Selection(option),
                    foundation.QuoteDigest, out var qualities, out var improvements)) return false;
            foreach (var quality in qualities)
            {
                string owner = ScalarCreation1949(quality, "guid");
                if (!result.TryAdd(owner, new(option.SourceId, quality,
                    improvements.Where(row => ScalarCreation1949(row, "sourcename") == owner).ToArray(), foundation.QuoteDigest))) return false;
            }
        }
        return true;
    }

    private sealed record CurrentCareerEffects1960(XElement Graph,
        CharacterCreationMetatypeOptionProjection Metatype, string Digest,
        IReadOnlyList<CharacterCreationTalentQualitySource> NativeGrants,
        IReadOnlyList<string> NativeQualityIds, IReadOnlyList<string> GrantedGearIds,
        CharacterCreationFoundationSequenceWritePlan? FinalizedModules,
        IReadOnlyList<CurrentFinalizedModuleOwner1965> FinalizedModuleOwners)
    {
        [System.Text.Json.Serialization.JsonIgnore]
        internal ImportedCareerEffects5688? Imported { get; init; }
    }

    // Produced only after rebuilding the immutable native archive and matching
    // the complete saved owner/effect graph. These owners are not purchases.
    private sealed record CurrentFinalizedModuleOwner1965(Guid OwnerId, Guid SourceId,
        string Parent, string QualityXml, IReadOnlyList<string> ImprovementXml, string PlanDigest);

    private static CurrentFinalizedModuleOwner1965? FindCurrentFinalizedModuleOwner1965(
        CurrentCareerEffects1960 native, CharacterCareerQualityIdentity identity)
        => native.FinalizedModuleOwners.SingleOrDefault(owner => owner.OwnerId == identity.InternalId
            && owner.SourceId == identity.SourceId);

    // A detached calculation graph. Neither the saved improvement payload nor a
    // catalog sibling can issue an effect: every output has a selected source owner.
    private static CurrentCareerEffects1960? CompileCurrentCareerEffects1960(
        CharacterCurrentRuleEnvironmentContext current, ICharacterSourceDataContext source, List<string> blockers)
    {
        var evaluation = RetainedEvaluationFor2764(current, source);
        if (evaluation?.CareerEffects is CurrentCareerEffects1960 retained)
            // Kernels receive their own calculation XML. No caller can mutate the
            // retained graph used by a later domain in this same evaluation.
            return retained with { Graph = new XElement(retained.Graph) };
        if (UsesOwnedPriorityCareer9569(current))
            return CompileImportedCareerEffects5688(current, source, blockers, evaluation);
        try
        {
            if (!UsesNativeMethods1958(current) || current.Stage != CharacterCurrentRuleStage.Career || !current.IsUnchanged)
                return Fail("context-unavailable");
            var root = ParseCreation1949(current.CharacterXml);
            if (root.Name != "character" || root.HasAttributes || DirectSkillText1949(root)
                || !ReadSkillBool1949(root, "created", null, out bool created) || !created)
                return Fail("character-shape");
            if (!source.TryResolveCreationMetatypeCatalog(out var catalog) || !catalog.IsAuthoritative
                || catalog.Blockers.Count != 0 || !catalog.SourceContext.IsAuthoritative
                || catalog.SourceContext.Blockers.Count != 0
                || CharacterCreationMetatypeSelectionRules.ResolveSaved(catalog, root,
                    ScalarCreation1949(root, "buildmethod") == CharacterCreationBuildMethods.LifeModules) is not { IsEnabled: true, Blockers.Count: 0 } metatype
                || source is not ICharacterCurrentNativeMethodSource1958 bridge
                || !TryCurrentRacialSources8582(source, bridge, metatype, out var racial)
                || racial.Count != metatype.GrantedQualities.Count || !racial.All(CharacterCreationTalentQualitySourceRules.IsValidSource))
                return Fail("metatype-source-unresolved");
            // These selected owners require additional canonical compilers. A
            // missing owner compiler is not equivalent to an empty effect list.
            foreach (string name in new[] { "cyberwares", "biowares", "martialarts", "foci", "stackedfoci",
                         "critterpowers", "drugs", "sustainedspells", "arts", "metamagics" })
                if (root.Elements(name).Any(node => node.HasElements || node.HasAttributes || DirectSkillText1949(node)))
                    return Fail("selected-" + name + "-unsupported");
            foreach (string name in new[] { "burnededge", "initiategrade", "submersiongrade", "specialattburn" })
                if (!ReadSkillInt1949(root, name, 0, out int value) || value != 0)
                    return Fail(name + "-history-unresolved");
            foreach (string name in new[] { "ignorerules", "ai", "critter", "critterenabled", "iscritter" })
                if (!ReadSkillSwitch1950(root, name, false, out bool value) || value) return Fail(name + "-unsupported");
            if (root.Elements("qualities").Count() > 1 || root.Elements("improvements").Count() > 1)
                return Fail("duplicate-container");
            var savedQualities = root.Element("qualities") ?? new XElement("qualities");
            var savedEffects = root.Element("improvements") ?? new XElement("improvements");
            if (savedQualities.HasAttributes || DirectSkillText1949(savedQualities)
                || savedEffects.HasAttributes || DirectSkillText1949(savedEffects)
                || savedQualities.Elements().Any(row => row.Name != "quality")
                || savedEffects.Elements().Any(row => row.Name != "improvement")) return Fail("saved-shape");
            // Recompile the immutable completed sequence from retained sources, then
            // join its native owner IDs to the actual Career graph. The archived
            // Creation record remains historical; the current document stays Career.
            CharacterCreationFoundationSequenceWritePlan? modules = null;
            if (ScalarCreation1949(root, "buildmethod") == CharacterCreationBuildMethods.LifeModules)
            {
                modules = CharacterCreationFoundationService.CompileFinalizedLifeModules1964(current.Current, source);
                if (modules is null) return Fail("finalized-module-sequence-unresolved");
            }
            if (!TryCurrentKarmaPromptOwners2726(current.Current, source, out var karmaPromptOwners))
                return Fail("finalized-karma-quality-prompts-unresolved");
            var moduleQualities = (modules?.QualityXml ?? []).Select(ParseCreation1949).ToArray();
            var moduleEffects = (modules?.ImprovementXml ?? []).Select(ParseCreation1949).ToArray();
            var expected = new List<XElement>();
            var native = new List<CharacterCreationTalentQualitySource>();
            var nativeIds = new List<string>();
            var gears = new List<(XElement Saved, CharacterCreationTalentGearSource Source)>();
            var flags = new HashSet<string>(StringComparer.Ordinal);
            var owners = new HashSet<Guid>();
            var seenRacial = new HashSet<string>(StringComparer.Ordinal);
            var firstLevelOwners = new HashSet<Guid>();
            var firstLevelSources = new HashSet<Guid>();
            var consumed = new List<object>();
            var moduleOwners = new List<CurrentFinalizedModuleOwner1965>();
            int? selectedQualityArms = null;
            int talents = 0;
            foreach (var selected in savedQualities.Elements())
            {
                if (selected.HasAttributes || DirectSkillText1949(selected)
                    || selected.Elements().GroupBy(node => node.Name).Any(group => group.Count() != 1)
                    || !Guid.TryParseExact(ScalarCreation1949(selected, "guid"), "D", out var owner) || owner == Guid.Empty || !owners.Add(owner)
                    || !Guid.TryParseExact(ScalarCreation1949(selected, "sourceid"), "D", out var id) || id == Guid.Empty)
                    return Fail("quality-owner-or-source-unresolved");
                string ownerId = owner.ToString("D");
                var owned = savedEffects.Elements().Where(row => ScalarCreation1949(row, "sourcename") == ownerId).ToArray();
                var moduleQuality = moduleQualities.SingleOrDefault(row => ScalarCreation1949(row, "guid") == ownerId);
                if (moduleQuality is not null)
                {
                    var compiledModuleEffects = moduleEffects.Where(row => ScalarCreation1949(row, "sourcename") == ownerId).ToArray();
                    if (!SameCurrentInstance1960(selected, moduleQuality) || !SameCurrentEffects1960(owned, compiledModuleEffects))
                        return Fail("module-owner-or-parent-mismatch");
                    expected.AddRange(compiledModuleEffects);
                    moduleOwners.Add(new(owner, id, ScalarCreation1949(moduleQuality, "parent"),
                        moduleQuality.ToString(SaveOptions.DisableFormatting),
                        Array.AsReadOnly(compiledModuleEffects.Select(row => row.ToString(SaveOptions.DisableFormatting)).ToArray()),
                        modules!.PlanDigest));
                    consumed.Add(new { Owner = ownerId, ModulePlan = modules!.PlanDigest });
                    continue;
                }
                if (ScalarCreation1949(selected, "qualitysource") == "LifeModule"
                    || !source.TryResolveCareerQualitySource(id, out var quality) || quality is null)
                    return Fail("quality-owner-or-source-unresolved");
                if (karmaPromptOwners.TryGetValue(ownerId, out var prompted))
                {
                    if (prompted.SourceId != id || !SameCurrentInstance1960(selected, prompted.Quality)
                        || !SameCurrentEffects1960(owned, prompted.Improvements))
                        return Fail("selected-quality-prompt-owner-mismatch");
                    nativeIds.Add(id.ToString("D"));
                    expected.AddRange(prompted.Improvements);
                    consumed.Add(new { Owner = ownerId, PromptSource = quality, prompted.QuoteDigest });
                    continue;
                }
                CharacterCreationTalentQualitySource? grant = racial.SingleOrDefault(row => row.SourceId == id.ToString("D"));
                bool isRacial = grant is not null;
                if (isRacial && !seenRacial.Add(grant!.SourceId)) return Fail("racial-owner-duplicate");
                if (grant is null)
                {
                    if (ScalarCreation1949(root, "buildmethod") == CharacterCreationBuildMethods.Karma)
                    {
                        if (source.TryResolveCreationKarmaGrantSources(metatype.OptionId, id.ToString("D"), out var currentRacial, out var talent))
                        {
                            if (CharacterCreationFinalizationDigest.Compute(currentRacial) != CharacterCreationFinalizationDigest.Compute(racial))
                                return Fail("racial-source-drift");
                            grant = talent;
                        }
                    }
                    else if (source.TryResolveCreationLifeModuleTalentSource(id.ToString("D"), out var talent)) grant = talent;
                }
                XElement projected;
                XElement[] compiled;
                if (grant is not null)
                {
                    if (!CharacterCreationTalentQualitySourceRules.IsValidSource(grant)
                        || !SamePurchaseXml(grant.CanonicalSourceXml, quality.RawSourceXml)
                        || !isRacial && ++talents > 1) return Fail("native-quality-source-conflict");
                    var bonus = new List<XElement>();
                    string extra = ScalarCreation1949(selected, "extra");
                    string selectedExtra = CharacterCreationAwakenedLegacyProjector.CompileBonus(
                        ParseCreation1949(grant.CanonicalSourceXml).Element("bonus"), grant.ForcedSelection,
                        extra.Length == 0 ? [] : [extra], grant, ownerId, flags, bonus, gears);
                    if (extra != selectedExtra || !CharacterCreationLegacySourceProjector.TryBuildGrantedQualityInstance(
                            grant, ownerId, extra, isRacial ? "Metatype" : "Selected", out projected))
                        return Fail("native-quality-choice-unsupported");
                    compiled = bonus.ToArray();
                    native.Add(grant); nativeIds.Add(id.ToString("D"));
                }
                else
                {
                    var qualityDefinition = ParseCreation1949(quality.RawSourceXml);
                    // Match TryReadMaximumRating's nolevels precedence and exact scalar grammar.
                    if (selectedQualityArms is null && qualityDefinition.Element("nolevels") is null
                        && qualityDefinition.Element("limit") is { HasAttributes: false, HasElements: false } limit
                        && string.Equals(limit.Value, "{arm} - 1", StringComparison.Ordinal))
                    {
                        if (!TryResolveCurrentCareerQualityAnatomy1960(current.CharacterXml, source,
                                out int arms, out string anatomyDigest)) return Fail("selected-quality-mechanic-unsupported");
                        selectedQualityArms = arms;
                        consumed.Add(new { AnatomyDigest = anatomyDigest, ResolvedArmCount = arms });
                    }
                    if (!CharacterCreationLegacySourceProjector.TryBuildCareerQualityInstance(quality.RawSourceXml,
                            id, owner, selectedQualityArms, false, out _, out projected, out var regular)
                        || !CharacterCreationLegacySourceProjector.TryBuildCareerQualityInstance(quality.RawSourceXml,
                            id, owner, selectedQualityArms, true, out _, out _, out var first)) return Fail("selected-quality-mechanic-unsupported");
                    if (!SameCurrentEffects1960(regular, first)) firstLevelSources.Add(id);
                    compiled = regular;
                    if (!SameCurrentEffects1960(owned, regular))
                    {
                        if (!SameCurrentEffects1960(owned, first) || !firstLevelOwners.Add(id))
                            return Fail("quality-first-level-or-saved-effects-mismatch");
                        compiled = first;
                    }
                }
                if (!SameCurrentInstance1960(selected, projected) || !SameCurrentEffects1960(owned, compiled))
                    return Fail("selected-quality-saved-state-mismatch");
                expected.AddRange(compiled);
                consumed.Add(new { Owner = owner, Source = quality, Native = grant });
            }
            var moduleIds = moduleQualities.Select(row => ScalarCreation1949(row, "guid")).ToHashSet(StringComparer.Ordinal);
            if (moduleQualities.Any(row => !Guid.TryParseExact(ScalarCreation1949(row, "guid"), "D", out var id) || !owners.Contains(id))
                || !SameCurrentEffects1960(moduleEffects, expected.Where(row => moduleIds.Contains(ScalarCreation1949(row, "sourcename")))))
                return Fail("module-owner-missing-or-foreign");
            if (karmaPromptOwners.Keys.Any(id => !Guid.TryParseExact(id, "D", out var owner) || !owners.Contains(owner)))
                return Fail("finalized-karma-quality-prompt-owner-missing");
            if (!firstLevelSources.SetEquals(firstLevelOwners)) return Fail("quality-first-level-owner-missing");
            if (racial.Any(row => !seenRacial.Contains(row.SourceId))) return Fail("racial-grant-missing");
            foreach (string flag in new[] { "magenabled", "resenabled", "depenabled", "magician", "adept", "technomancer" })
                if (!ReadSkillSwitch1950(root, flag, false, out bool enabled) || enabled != flags.Contains(flag))
                    return Fail("talent-flag-source-mismatch");
            // DEP has no SR5 advancement target in the existing contract.
            if (flags.Contains("depenabled")) return Fail("depth-advancement-contract-unsupported");
            if (metatype.BaseBonuses is { } bonuses)
                foreach (var (type, value, unique) in new[] { ("Armor", bonuses.Armor, "group0"),
                             ("Reach", bonuses.Reach, string.Empty), ("LifestyleCost", bonuses.LifestyleCostPercent, string.Empty) })
                {
                    if (value < 0) return Fail("metatype-bonus-unsupported");
                    if (value != 0) expected.Add(CharacterCreationAwakenedLegacyProjector.Improvement(type,
                        string.Empty, metatype.MetatypeIdentity?.ParentMetatypeOptionId ?? metatype.OptionId, "Metatype", value, unique));
                }
            if (metatype.BaseBonuses?.ConditionOverflow5497 is int overflow)
            {
                if (!UsesCareerCondition5497(current)) return Fail("metatype-condition-capability-required");
                // This uses the sheet's Metatype/CMOverflow owner and includes a
                // present zero value. Never replace the actual saved row.
                expected.Add(CharacterCareerMetatypeConditionSource5497.Compile(metatype.OptionId, overflow));
            }
            foreach (var grant in native)
                CharacterCreationAwakenedLegacyProjector.CheckRestrictions(ParseCreation1949(grant.CanonicalSourceXml),
                    root, [], flags);
            // Granted gear has exact native source and parent ownership; ordinary
            // selected equipment still goes through its existing purchase/loadout consumer.
            if (gears.Select(row => ScalarCreation1949(row.Saved, "guid")).Distinct(StringComparer.Ordinal).Count() != gears.Count)
                return Fail("granted-gear-identity-duplicate");
            foreach (var gear in gears)
            {
                var saved = root.Elements("gears").Elements("gear")
                    .Where(row => ScalarCreation1949(row, "guid") == ScalarCreation1949(gear.Saved, "guid")).ToArray();
                if (saved is not [var instance] || !SameCurrentInstance1960(instance, gear.Saved))
                    return Fail("granted-gear-owner-mismatch");
            }
            if (source is not ICharacterCurrentSkillEffectSource reader) return Fail("selected-source-reader-unavailable");
            foreach (var (container, rowName) in new[] { ("gears", "gear"), ("weapons", "weapon"),
                         ("armors", "armor"), ("powers", "power"), ("spells", "spell"), ("complexforms", "complexform") })
            {
                if (root.Elements(container).Count() > 1) return Fail("selected-container-duplicate");
                var box = root.Element(container);
                if (box is null) continue;
                if (box.HasAttributes || DirectSkillText1949(box) || box.Elements().Any(row => row.Name != rowName))
                    return Fail("selected-container-shape");
                foreach (var item in box.Elements())
                {
                    if (gears.Any(row => ScalarCreation1949(row.Saved, "guid") == ScalarCreation1949(item, "guid"))) continue;
                    if (!Guid.TryParse(ScalarCreation1949(item, "sourceid"), out var id)
                        || !reader.TryReadCurrentSkillEffectSource(container, id, out var xml, out var state)
                        || string.IsNullOrEmpty(state)) return Fail("selected-" + container + "-source-unresolved");
                    var definition = ParseCreation1949(xml);
                    if (definition.Name != rowName || ScalarCreation1949(definition, "id") != id.ToString("D")
                        || ScalarCreation1949(definition, "name") != ScalarCreation1949(item, "name"))
                        return Fail("selected-" + container + "-source-mismatch");
                    if (definition.Elements().Any(row => (row.Name.LocalName is "bonus" or "firstlevelbonus" or "wirelessbonus"
                            or "wirelesspairbonus" or "pairbonus") && (row.HasElements || row.HasAttributes || DirectSkillText1949(row)))
                        || item.Elements().Any(row => (row.Name.LocalName is "children" or "gears" or "mods" or "accessories") && row.HasElements))
                        return Fail("selected-" + container + "-effect-compiler-required");
                    consumed.Add(new { Kind = container, Id = id, State = state });
                }
            }
            if (!SameCurrentEffects1960(savedEffects.Elements(), expected)) return Fail("untraceable-or-changed-improvement");
            // Reuse the 5064 owner/occurrence/penalty graph through the current-source
            // bridge shared with Priority movement. Removing the collection exclusion
            // alone would admit unresolved links or trust saved penalty caches.
            if (!TrySustainedMovement5080(current, root, out var sustainedAuthority))
                return Fail("selected-sustainedobjects-unresolved");
            if (sustainedAuthority is not null)
                consumed.Add(new { Kind = "sustainedobjects", Authority = sustainedAuthority });
            if (!source.TryResolveCreationMetatypeCatalog(out var finalCatalog)
                || CharacterCreationFinalizationDigest.Compute(catalog) != CharacterCreationFinalizationDigest.Compute(finalCatalog)
                || !current.IsUnchanged) return Fail("source-drift");
            var graph = new XElement(root);
            // Resolve actual native save aliases only on the detached kernel input.
            graph.SetElementValue("metatypeguid", metatype.MetatypeIdentity?.ParentMetatypeOptionId ?? metatype.OptionId);
            graph.Elements("improvements").Remove();
            graph.Add(new XElement("improvements", expected));
            var result = new CurrentCareerEffects1960(graph, metatype, CharacterCreationFinalizationDigest.Compute(new
            { current.BindingDigest, Catalog = catalog, Sources = consumed, Effects = expected.Select(row => row.ToString(SaveOptions.DisableFormatting)).ToArray() }),
                native, nativeIds, gears.Select(row => ScalarCreation1949(row.Saved, "guid")).ToArray(), modules,
                Array.AsReadOnly(moduleOwners.ToArray()));
            if (evaluation is not null)
            {
                if (!evaluation.Allows(current, source)) return Fail("source-drift");
                // All other members stay within these private read-only consumers.
                // Keep XML separate even from the first domain's returned result.
                evaluation.CareerEffects = result with { Graph = new XElement(result.Graph) };
            }
            return result;
        }
        catch (Exception error) when (CreationInputError1949(error)) { return Fail("selected-graph-malformed-or-unsupported"); }
        CurrentCareerEffects1960? Fail(string reason) { blockers.Add("current-career-" + reason); return null; }
    }

    private static bool TryResolveCurrentCareerQualityAnatomy1960(string characterXml,
        ICharacterSourceDataContext source, out int armCount, out string anatomyDigest)
    {
        armCount = 0;
        anatomyDigest = string.Empty;
        if (!source.TryResolveCareerQualityMetatype(out var metatype, out var metatypeAuthority)
            || !source.TryResolveCareerQualityAnatomySources(out var anatomy) || anatomy is null
            || anatomy.Schema != CharacterCareerQualityAnatomySources.Sr5CareerV1
            || anatomy.MetatypeXml != metatype || anatomy.MetatypeAuthority != metatypeAuthority
            || anatomy.SettingsAuthority.Length == 0 || anatomy.SettingsXml.Length == 0
            || anatomy.QualityInputsAuthority.Length == 0 || anatomy.Qualities is null
            || !anatomy.Qualities.All(typed => typed is not null && Guid.TryParseExact(typed.SourceId, "D", out var id)
                && source.TryResolveCareerQualitySource(id, out var raw) && raw is not null
                && Normalize(raw.RawSourceXml) == typed.CanonicalSourceXml)
            || !CharacterCreationLegacySourceProjector.TryResolveCareerArmCount(
                Normalize(characterXml), anatomy, out int resolved)) return false;
        armCount = resolved;
        anatomyDigest = anatomy.Digest;
        return true;

        // Match the ordinary Career owner's detached anatomy view, including source
        // indentation copied into saved bonuses. Never normalize the saved document.
        static string Normalize(string xml)
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true,
                MaxCharactersInDocument = CharacterCareerReputationProjector.MaximumCharacterXmlLength });
            return XElement.Load(reader).ToString(SaveOptions.DisableFormatting);
        }
    }

    internal static bool SameCurrentInstance1960(XElement actual, XElement expected)
    {
        string[] metadata = ["notes", "notesColor", "print"];
        if (actual.Name != expected.Name || actual.HasAttributes || DirectSkillText1949(actual)
            || actual.Elements().GroupBy(row => row.Name).Any(group => group.Count() != 1)
            || actual.Elements().Any(row => !metadata.Contains(row.Name.LocalName, StringComparer.Ordinal)
                && expected.Element(row.Name) is null)) return false;
        return expected.Elements().Where(row => !metadata.Contains(row.Name.LocalName, StringComparer.Ordinal))
            .All(row => actual.Element(row.Name) is { } saved && SamePurchaseXml(saved.ToString(), row.ToString()));
    }

    internal static bool SameCurrentEffects1960(IEnumerable<XElement> actual, IEnumerable<XElement> expected)
    {
        string Normalize(XElement row)
        {
            if (row.Name != "improvement" || row.HasAttributes || DirectSkillText1949(row)
                || row.Elements().Any(field => field.HasAttributes || field.HasElements)
                || row.Elements().GroupBy(field => field.Name).Any(group => group.Count() != 1)) throw new InvalidDataException();
            var fields = row.Elements().Where(field => field.Name.LocalName is not ("notes" or "notesColor"))
                .Select(field => new XElement(field)).OrderBy(field => field.Name.ToString(), StringComparer.Ordinal).ToArray();
            // Empty scalar elements have the same value with either XML spelling.
            // Normalize only detached copies; keep field presence and all content exact.
            foreach (var field in fields.Where(field => field.IsEmpty))
                field.Value = string.Empty;
            foreach (var field in fields.Where(field => field.Name.LocalName is "enabled" or "custom" or "addtorating"))
            {
                if (field.Value is "0" or "1") field.Value = field.Value == "1" ? "True" : "False";
                else if (bool.TryParse(field.Value, out bool value)) field.Value = value ? "True" : "False";
            }
            return new XElement("improvement", fields).ToString(SaveOptions.DisableFormatting);
        }
        return actual.Select(Normalize).Order(StringComparer.Ordinal)
            .SequenceEqual(expected.Select(Normalize).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static bool TryCurrentRacialSources8582(ICharacterSourceDataContext source,
        ICharacterCurrentNativeMethodSource1958 bridge, CharacterCreationMetatypeOptionProjection metatype,
        out IReadOnlyList<CharacterCreationTalentQualitySource> sources)
        => metatype.MetatypeIdentity is { } identity
            ? source.TryResolveCreationLifeModuleMetatypeSources(identity, out sources)
            : bridge.TryResolveCurrentMetatypeGrantSources(metatype.OptionId, out sources);

    private static string CurrentMetatypeIdentity1960(XElement root)
    {
        string id = ScalarCreation1949(root, "metatypeid"), guid = ScalarCreation1949(root, "metatypeguid");
        if (id.Length != 0 && guid.Length != 0 && id != guid) throw new InvalidDataException();
        return guid.Length == 0 ? id : guid;
    }

    private static CharacterCareerAttributeAdvanceQuote[] EvaluateCareerAttributes1960(
        CharacterCurrentRuleEnvironmentContext current, ICharacterSourceDataContext source,
        CharacterCreationAttributePolicy policy, List<string> blockers)
    {
        var selected = CompileCurrentCareerEffects1960(current, source, blockers);
        if (selected is null) return [];
        var root = selected.Graph;
        // Native Sprites retain source-evaluated bounds and values even while
        // special attributes are disabled. The imported owner closure has proved
        // those bounds against one retained-source level; flags still gate quotes.
        bool retainedSprite = selected.Imported?.Owners.Sprite5751 is not null;
        if (!source.TryResolveCareerSettingsProfile(out var settingsXml, out var settingsState)
            || string.IsNullOrEmpty(settingsState)) return Fail("settings-unresolved");
        var settings = ParseCreation1949(settingsXml);
        if (!ReadSkillInt1949(root, "karma", 0, out int available) || available < 0
            || !ReadSkillSwitch1950(settings, "unclampattributeminimum", false, out bool unclamp)
            || !ReadSkillSwitch1950(settings, "mysadeptsecondmagattribute", false, out bool separate)) return Fail("settings-or-karma-invalid");
        bool mag = Flag("magenabled"), res = Flag("resenabled"), adept = Flag("adept"), magician = Flag("magician");
        CharacterCareerMagicEvaluation? magic = null;
        CharacterCareerResonanceEvaluation? resonance = null;
        if (mag && selected.Imported is null)
        {
            magic = CharacterCareerMagicEvaluator.Evaluate(root, settings, sourceContext: source);
            if (!magic.IsSupported) { blockers.AddRange(magic.Blockers); return Fail("magic-source-graph-unresolved"); }
        }
        if (res && selected.Imported is null)
        {
            resonance = CharacterCareerResonanceEvaluator.Evaluate(root, source);
            if (!resonance.IsSupported) { blockers.AddRange(resonance.Blockers); return Fail("resonance-source-graph-unresolved"); }
        }
        if (root.Elements("attributes").Count() > 1) return Fail("duplicate-container");
        var container = root.Element("attributes");
        if (container is null) return [];
        if (container.HasAttributes || DirectSkillText1949(container)) return Fail("container-shape");
        var result = new List<CharacterCareerAttributeAdvanceQuote>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        // These grants were rebuilt from the finalized sequence and matched to
        // the saved owner/effect graph above; saved totals cannot supply levels.
        var moduleAttributes = (selected.FinalizedModules?.ImprovementXml ?? [])
            .Select(ParseCreation1949)
            .Where(effect => ScalarCreation1949(effect, "improvementttype") == "Attributelevel").ToArray();
        foreach (var row in container.Elements())
        {
            string id = ScalarCreation1949(row, "name");
            string[] fields = ["name", "base", "karma", "metatypemin", "metatypemax", "metatypeaug", "metatypeaugmax",
                "metatypecategory", "totalvalue", "limits", "notes", "notesColor"];
            if (row.Name != "attribute" || row.HasAttributes || DirectSkillText1949(row) || !ids.Add(id)
                || row.Elements().GroupBy(field => field.Name).Any(group => group.Count() != 1)
                || row.Elements().Any(field => field.HasAttributes || field.HasElements || !fields.Contains(field.Name.ToString(), StringComparer.Ordinal))
                || ScalarCreation1949(row, "metatypecategory") is not ("" or "Standard")
                || ScalarCreation1949(row, "limits").Length != 0
                || !ReadSkillInt1949(row, "base", -1, out int basis) || basis < 0
                || !ReadSkillInt1949(row, "karma", -1, out int karma) || karma < 0) return Fail("shape-unresolved");
            var range = selected.Metatype.Attributes.SingleOrDefault(value => value.AttributeId == (id == "MAGAdept" ? "MAG" : id));
            bool enabled = id switch { "MAG" => mag, "MAGAdept" => mag && adept && magician && separate, "RES" => res, "DEP" => false, _ => true };
            if (range is null) return Fail("identity-unresolved");
            var specialInput = selected.Imported?.Owners.SpecialInputs5699.SingleOrDefault(value => value.Id == id);
            var specialResult = selected.Imported?.Owners.SpecialResults5699.SingleOrDefault(value => value.Id == id);
            if (selected.Imported is not null && enabled && (id is "MAG" or "MAGAdept" or "RES")
                && (specialInput is null || specialResult is null)) return Fail("special-source-result-unavailable");
            int minimum = enabled || retainedSprite ? specialInput?.Minimum ?? range.Minimum : 0;
            int maximum = enabled || retainedSprite ? specialInput?.Maximum ?? range.Maximum : 0;
            int augmented = enabled || retainedSprite ? specialInput?.AugmentedMaximum ?? range.AugmentedMaximum : 0;
            if (!ReadSkillInt1949(row, "metatypemin", -1, out int savedMinimum) || savedMinimum != minimum
                || !ReadSkillInt1949(row, "metatypemax", -1, out int savedMaximum) || savedMaximum != maximum
                || row.Element("metatypeaug") is null && row.Element("metatypeaugmax") is null
                || row.Element("metatypeaug") is not null && (!ReadSkillInt1949(row, "metatypeaug", -1, out int savedAugmented) || savedAugmented != augmented)
                || row.Element("metatypeaugmax") is not null && (!ReadSkillInt1949(row, "metatypeaugmax", -1, out int savedAugmentedMax) || savedAugmentedMax != augmented))
                return Fail("source-range-mismatch");
            // Match LifeModule FreeBase: sum the source grants, clamp to the
            // metatype span, then add the selected paid allocation exactly once.
            int freeBase = checked((int)Math.Min(moduleAttributes
                .Where(effect => ScalarCreation1949(effect, "improvedname") == id)
                .Sum(effect => long.Parse(ScalarCreation1949(effect, "val"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)),
                maximum - minimum));
            var natural = selected.Imported is { } imported && (enabled || retainedSprite) && id != "ESS"
                ? imported.Owners.Attribute(id, minimum, maximum, augmented, basis, karma, unclamp)
                : CharacterCreationEssenceRules1222.Native(new(id, enabled, minimum, maximum, augmented, basis, karma, unclamp,
                    FreeBase: freeBase));
            if (enabled && id != "ESS" && (selected.Imported is null
                    ? checked(Math.Max((long)minimum + basis + freeBase, natural.Minimum) + karma) > maximum
                    : checked(minimum + basis + karma) > natural.Maximum))
                return Fail("allocation-exceeds-source-cap");
            int effective = id == "ESS" ? maximum : natural.Value;
            int cap = natural.Maximum;
            if (specialResult is not null)
            { effective = specialResult.Current; cap = specialResult.Maximum; }
            else if ((id is "MAG" or "MAGAdept") && enabled)
            { effective = id == "MAG" ? magic!.Magic : magic!.AdeptMagic!.Value; cap = id == "MAG" ? magic!.MagicMaximum : magic!.AdeptMagicMaximum!.Value; }
            if (id == "RES" && enabled && specialResult is null) effective = resonance!.EffectiveResonance;
            if (!enabled && (basis != 0 || karma != 0) || id == "ESS" && (basis != 0 || karma != 0)
                || row.Element("totalvalue") is not null && (!ReadSkillInt1949(row, "totalvalue", -1, out int total) || total != effective))
                return Fail("saved-value-mismatch");
            if (!enabled || id == "ESS") continue;
            if (!CharacterCareerAttributeAdvanceRules.TryCreateIdentity(id, out var identity)) return Fail("identity-unresolved");
            var input = new CharacterCareerAttributeAdvanceInput(identity, true, "sr5", id, basis, karma, effective, cap,
                minimum, available, mag, adept && magician, separate, res, 0,
                new(policy.KarmaAttribute, policy.AlternateMetatypeAttributeKarma), [],
                row.ToString(SaveOptions.DisableFormatting), CharacterCreationFinalizationDigest.Compute(new { selected.Digest, policy, settingsState }));
            if (!CharacterCareerAttributeAdvanceRules.TryCreateQuote(input, out var quote)) return Fail("canonical-quote-unresolved");
            result.Add(quote);
        }
        if (mag && !ids.Contains("MAG") || res && !ids.Contains("RES")) return Fail("enabled-target-missing");
        return current.IsUnchanged ? result.ToArray() : Fail("revision-changed");
        bool Flag(string name) => ReadSkillSwitch1950(root, name, false, out bool value) ? value : throw new InvalidDataException();
        CharacterCareerAttributeAdvanceQuote[] Fail(string reason) { blockers.Add("current-career-attribute-" + reason); return []; }
    }

    private sealed record CurrentNativeMethod1958(CharacterCreationSourceProfileAuthority Profile,
        CharacterCreationMetatypeCatalogAuthority Metatypes, CharacterCreationAttributePolicy Attributes,
        CharacterCreationKarmaResourcesPolicy Funding, CharacterCreationKarmaMetatypeOpen? Karma,
        CurrentLifeModulesObservation1958? LifeModules, CharacterCreationMetatypeOptionProjection? SelectedMetatype, string OutcomeDigest);

    private static bool UsesNativeMethods1958(CharacterCurrentRuleEnvironmentContext current)
        // V7's persisted lifestyle capability is method-independent. Its admission
        // contract already includes marked Priority/SumToTen. Keep those methods on
        // their source-backed prerequisite/allocation kernels, not Karma funding.
        // All older profile dispatch and accepted Karma/LifeModules bytes stay fixed.
        => CurrentCharacterRuleSourceSemantics.IsNativeExecutionProfile(current.SemanticsProfile, current.SemanticsFingerprint)
            && !UsesPriorityCurrent3027(current);

    private static bool TryCurrentNativeMethod1958(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, out CurrentNativeMethod1958? observed, List<string> blockers)
    {
        var evaluation = RetainedEvaluationFor2764(current, source);
        if (evaluation?.NativeMethod is CurrentNativeMethod1958 saved)
        {
            observed = saved;
            return true;
        }
        bool success = CompileCurrentNativeMethod1958(current, source, out observed, blockers);
        if (success && observed is not null && evaluation is not null
            && ReferenceEquals(evaluation, RetainedEvaluationFor2764(current, source)))
            evaluation.NativeMethod = observed;
        return success;
    }

    private static bool CompileCurrentNativeMethod1958(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, out CurrentNativeMethod1958? observed, List<string> blockers)
    {
        observed = null;
        if (!UsesNativeMethods1958(current) || !current.IsUnchanged
            || !source.TryResolveCreationSourceProfile(out var profile)
            || profile.BuildMethod is not (CharacterCreationBuildMethods.Karma or CharacterCreationBuildMethods.LifeModules)
            || profile.BuildMethod != ScalarCreation1949(ParseCreation1949(current.CharacterXml), "buildmethod")
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(profile.RawProfileInputsDigest)
            || profile.SourceAnchorIds.Count == 0 || profile.BuildPoints is not >= 0
            || (profile.BuildMethod == CharacterCreationBuildMethods.Karma
                ? profile.BudgetBlockers.Any(value => value != CharacterCreationFoundationBlockers.LifeModuleBudgetProfileBuildMethodMismatch)
                : !profile.LifeModuleBudgetIsExact || profile.BudgetBlockers.Count != 0)
            || !source.TryResolveCreationMetatypeCatalog(out var metatypes) || !metatypes.IsAuthoritative
            || metatypes.Blockers.Count != 0 || !metatypes.SourceContext.IsAuthoritative
            || metatypes.SourceContext.Blockers.Count != 0
            || metatypes.SourceContext.SettingsProfileId != profile.SettingsProfileId
            || metatypes.SourceContext.RawProfileInputsDigest != profile.RawProfileInputsDigest
            || !source.TryResolveCreationAttributePolicy(out var attributes) || attributes is null
            || attributes.BuildMethod != profile.BuildMethod || attributes.SettingsProfileId != profile.SettingsProfileId
            || attributes.RawProfileInputsDigest != profile.RawProfileInputsDigest
            || attributes.AuthorityDigest != CharacterCreationAttributePolicyAuthority.ComputeDigest(attributes))
        { blockers.Add("current-native-method-source-authority-unresolved"); return false; }
        CharacterCreationKarmaResourcesPolicy? funding;
        bool resolvedFunding = profile.BuildMethod == CharacterCreationBuildMethods.Karma
            ? source.TryResolveCreationKarmaResourcesPolicy(out funding)
            : source.TryResolveCreationLifeModuleResourcesPolicy(out funding);
        string fundingSchema = profile.BuildMethod == CharacterCreationBuildMethods.Karma
            ? CharacterCreationKarmaResourcesPolicy.SchemaV1 : CharacterCreationKarmaResourcesPolicy.LifeModulesSchemaV1;
        if (!resolvedFunding || funding is null || !CharacterCreationKarmaResourcesRules.IsValidSpendingPolicy(funding, fundingSchema)
            || funding.SettingsProfileId != profile.SettingsProfileId || funding.RawProfileInputsDigest != profile.RawProfileInputsDigest)
        { blockers.Add("current-native-method-funding-authority-unresolved"); return false; }
        var auxiliary = current.Current.Document.AuxiliaryState;
        var root = ParseCreation1949(current.CharacterXml);
        CharacterCreationKarmaMetatypeOpen? karma = null;
        CurrentLifeModulesObservation1958? lifeModules = null;
        CharacterCreationMetatypeOptionProjection? selected = null;
        if (current.Stage == CharacterCurrentRuleStage.Creation)
        {
            if (auxiliary.CharacterCreationPrerequisiteDraft is not null || auxiliary.CharacterCreationAttributesDraft is not null
                || auxiliary.CharacterCreationResourcesDraft is not null || auxiliary.CharacterCreationSkillsDraft is not null
                || auxiliary.CharacterCreationQualitiesDraft is not null || auxiliary.CharacterCreationGearDraft is not null
                || auxiliary.CharacterCreationLifestylesDraft is not null || auxiliary.CharacterCreationMagicResonanceDraft is not null
                || auxiliary.CharacterCreationLoadoutDraft is not null || auxiliary.CharacterCreationCompletionDrafts is { Count: > 0 })
            { blockers.Add("current-native-method-mixed-creation-ledgers"); return false; }
            if (profile.BuildMethod == CharacterCreationBuildMethods.Karma)
            {
                if (auxiliary.CharacterCreationFoundationDraft is not null || auxiliary.LifeModuleDecisionAcceptances is { Count: > 0 })
                { blockers.Add("current-native-method-mixed-creation-ledgers"); return false; }
                // Open evaluates the saved command, not its historical quote, under this
                // exact source context. It supports genuinely incomplete native choices.
                var opened = new CharacterCreationKarmaMetatypeService(new WorkspaceContinuationReadView(current.Current),
                    new CreationExactSource1949(current.CharacterXml, source)).Open(current.Current.Id);
                if (opened.Value is not { } value || opened.Blockers.Count != 0)
                { blockers.AddRange(opened.Blockers); blockers.Add("current-karma-native-selection-unresolved"); return false; }
                karma = value;
                selected = value.Quote?.Metatype;
                if (value.Quote is { CanSelect: false } || value.Quote is { Blockers.Count: > 0 })
                { blockers.Add("current-karma-selected-operation-unavailable"); return false; }
            }
            else if (auxiliary.CharacterCreationKarmaMetatypeDecisions is { Count: > 0 })
            { blockers.Add("current-native-method-mixed-creation-ledgers"); return false; }
            else
            {
                if (source is not ICharacterCurrentNativeMethodSource1958 native
                    || !native.TryResolveCurrentLifeModulesCatalog(out var modules) || modules is null)
                { blockers.Add("current-life-modules-retained-catalog-unresolved"); return false; }
                var view = new WorkspaceContinuationReadView(current.Current);
                var service = new CharacterCreationFoundationService(view, current.Queries,
                    new CreationExactSource1949(current.CharacterXml, source), modules,
                    new CharacterCreationFoundationDraftApplyAuthority(view));
                lifeModules = service.ObserveCurrentLifeModules1958(current.Current);
                // Pending winning-quality prompts are observable only in V7;
                // keep the older retained interpretation profiles unchanged.
                if (lifeModules is null
                    || (current.SemanticsProfile != CurrentCharacterRuleSourceSemantics.ProfileV7
                        || current.SemanticsFingerprint != CurrentCharacterRuleSourceSemantics.FingerprintV7)
                    && lifeModules.Sequence?.QualityLevels.Any(level => level.InstancePrompt is not null
                        && level.InstanceValue is null) == true)
                { blockers.Add("current-life-modules-current-sequence-unresolved"); return false; }
                if (lifeModules.State.PendingDraft is { } foundation)
                {
                    selected = CharacterCreationMetatypeSelectionRules.Resolve(metatypes,
                        foundation.RequestedMetatype, foundation.MetatypeIdentity);
                    if (selected is null)
                    { blockers.Add("current-life-modules-selected-metatype-unresolved"); return false; }
                }
            }
            if (root.Element("attributes")?.Elements().Any() == true)
            { blockers.Add("current-native-method-imported-creation-attributes-unresolved"); return false; }
        }
        string id = ScalarCreation1949(root, "metatypeguid"), name = ScalarCreation1949(root, "metatype");
        if (!string.IsNullOrEmpty(id) || !string.IsNullOrEmpty(name))
        {
            var option = CharacterCreationMetatypeSelectionRules.ResolveSaved(metatypes, root,
                profile.BuildMethod == CharacterCreationBuildMethods.LifeModules);
            if (option is null || selected is not null && (selected.OptionId != option.OptionId
                || selected.MetatypeIdentity != option.MetatypeIdentity))
            { blockers.Add("current-native-method-selected-metatype-unresolved"); return false; }
            selected = option;
        }
        if (!CurrentMetatypeScalars1950(root, selected))
        { blockers.Add("current-native-method-metatype-scalars-unresolved"); return false; }
        // Funding arithmetic receives only current, canonical attribute values. An
        // unchosen attribute does not acquire a made-up value to satisfy a formula.
        decimal? zero = null, maximum = null;
        if (current.Stage == CharacterCurrentRuleStage.Creation)
        {
            var values = karma?.Quote?.Attributes?.Attributes.Select(row => new KeyValuePair<string, int>(row.AttributeId, row.Current))
                ?? lifeModules?.Attributes?.Attributes.Select(row => new KeyValuePair<string, int>(row.AttributeId, row.Current))
                ?? Enumerable.Empty<KeyValuePair<string, int>>();
            if (!CharacterCreationKarmaResourcesRules.TryFundingAmount(funding.FundingExpression, 0, values, out decimal currentZero)
                || !CharacterCreationKarmaResourcesRules.TryFundingAmount(funding.FundingExpression, funding.MaximumKarmaInvestment,
                    values, out decimal currentMaximum))
            { blockers.Add("current-native-method-funding-expression-unresolved"); return false; }
            zero = currentZero; maximum = currentMaximum;
        }
        if (!current.IsUnchanged) return false;
        observed = new(profile, metatypes, attributes, funding, karma, lifeModules, selected,
            CharacterCreationFinalizationDigest.Compute(new { Schema = "chummer.current-native-method.v3", current.BindingDigest,
                profile, metatypes, attributes, funding, ZeroInvestment = zero, MaximumInvestment = maximum,
                CurrentSelection = karma?.Quote, LifeModules = lifeModules, SelectedMetatype = selected }));
        return true;
    }

    private static string ValidateNativeCreationAuthority1958(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleDomain domain, XElement[] rows, ICharacterSourceDataContext source, List<string> blockers)
    {
        if (!TryCurrentNativeMethod1958(current, source, out var method, blockers) || method is null) return string.Empty;
        if (domain == CharacterCurrentRuleDomain.Profile)
        {
            var selected = rows.Where(row => ScalarCreation1949(row, "id") == method.Profile.SettingsProfileId).ToArray();
            if (selected is not [var profile]
                || CharacterCreationAttributePolicyAuthority.ResolveMaximumAttributesAtCap(profile) != method.Attributes.MaxNumberMaxAttributesCreate)
                blockers.Add("current-native-method-selected-profile-unresolved");
        }
        else if (domain == CharacterCurrentRuleDomain.Metatypes
            && method.Metatypes.Options.Any(option => option.IsEnabled && option.Blockers.Count == 0
                && rows.Count(row => ScalarCreation1949(row, "id") == option.OptionId && ScalarCreation1949(row, "name") == option.Label) != 1))
            blockers.Add("current-native-method-metatype-catalog-not-exact");
        return method.OutcomeDigest;
    }

    private static object? ConsumeNativeOrigins1958(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, XElement root,
        HashSet<string> selected, List<string> blockers, ref CharacterCurrentRuleConsumptionState state)
    {
        if (!TryCurrentNativeMethod1958(current, source, out var method, blockers) || method is null) return null;
        if (new[] { "prioritymetatype", "priorityattributes", "priorityspecial", "priorityskills", "priorityresources", "prioritytalent" }
            .Any(field => root.Element(field) is { } node && (node.HasAttributes || node.HasElements || node.Value.Length != 0)))
        { blockers.Add("current-native-method-priority-selection-not-applicable"); return null; }
        if (catalog.Domain == CharacterCurrentRuleDomain.Priorities)
        {
            state = CharacterCurrentRuleConsumptionState.NotConfigured;
            return new { method.Profile.BuildMethod, method.Profile.RawProfileInputsDigest, PrioritySelection = "not-applicable" };
        }
        if (method.SelectedMetatype is not { } metatype)
        {
            state = CharacterCurrentRuleConsumptionState.NotConfigured;
            return new { method.OutcomeDigest, Selection = "not-present" };
        }
        SelectCreationKey1949(catalog, "metatype", metatype.MetatypeIdentity?.ParentMetatypeOptionId ?? metatype.OptionId, selected, blockers);
        var grants = CurrentNativeMetatypeEffects1958(current, catalog, source, metatype.OptionId, blockers);
        return grants is null ? null : TagRuleEffects2912(new { method.OutcomeDigest, Metatype = metatype, Grants = grants },
            [CharacterRuleEffectValues2912.Number(metatype.OptionId, metatype.Label, CharacterRuleEffectMetric.RuleKarmaCost, metatype.KarmaCost, "metatype")],
            "Selected metatype rule price is calculated; granted effects are calculated by their owning sheet or native consumers.");
    }

    private static CharacterCurrentCreationMetatypeQuote? CurrentNativeMetatypeEffects1958(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, string optionId, List<string> blockers)
    {
        if (!TryCurrentNativeMethod1958(current, source, out var method, blockers) || method is null
            || (method.Metatypes.Options.SingleOrDefault(option => option.OptionId == optionId)
                    ?? (method.SelectedMetatype?.OptionId == optionId ? method.SelectedMetatype : null))
                is not { IsEnabled: true, Blockers.Count: 0 } metatype) return null;
        if (source is not ICharacterCurrentNativeMethodSource1958 native
            || !TryCurrentRacialSources8582(source, native, metatype, out var racial)
            || racial.Count != metatype.GrantedQualities.Count
            || racial.Where((grant, index) => grant.Name != metatype.GrantedQualities[index].Name).Any())
        { blockers.Add("current-native-method-metatype-grants-unresolved"); return null; }
        XElement[] qualities = [], effects = [];
        // This is the shared typed racial-source effect compiler. No Priority
        // authority, selection, points, or draft enters the native-method path.
        if (racial.Count != 0 && !CharacterCreationPriorityRacialQualities.TryProjectCurrentNativeSkillCosts(
                racial, method.OutcomeDigest, out qualities, out effects, allowLifeModuleRacialDice: metatype.MetatypeIdentity is not null))
        { blockers.Add("current-native-method-metatype-grant-mechanic-unresolved"); return null; }
        var improvements = effects.ToList();
        if (metatype.BaseBonuses is { } bonus)
        {
            if (bonus.Armor < 0 || bonus.Reach < 0 || bonus.LifestyleCostPercent < 0)
            { blockers.Add("current-native-method-metatype-bonus-unresolved"); return null; }
            foreach (var (type, value, unique) in new[] { ("Armor", bonus.Armor, "group0"),
                ("Reach", bonus.Reach, string.Empty), ("LifestyleCost", bonus.LifestyleCostPercent, string.Empty) })
                if (value != 0) improvements.Add(CharacterCreationAwakenedLegacyProjector.Improvement(
                    type, string.Empty, metatype.MetatypeIdentity?.ParentMetatypeOptionId ?? metatype.OptionId, "Metatype", value, unique));
        }
        if (metatype.BaseBonuses?.ConditionOverflow5497 is int overflow)
        {
            if (!UsesCareerCondition5497(current))
            { blockers.Add("current-native-method-metatype-condition-capability-required"); return null; }
            improvements.Add(CharacterCareerMetatypeConditionSource5497.Compile(metatype.OptionId, overflow));
        }
        return new(current.BindingDigest, catalog.ContentDigest, DetachNativeMetatype2764(metatype),
            qualities.Select(row => row.ToString(SaveOptions.DisableFormatting)).ToArray(),
            improvements.Select(row => row.ToString(SaveOptions.DisableFormatting)).ToArray());
    }

    private static bool IsNativeSheetProfileSetting5130(string name)
        => name is "dicepenaltysustaining" or "maxinitiativedice";

    private static bool TryObserveNativeSheetProfile5130(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, XElement profile, out string? observation)
    {
        observation = null;
        var controls = profile.Elements().Where(field => IsNativeSheetProfileSetting5130(field.Name.LocalName)).ToArray();
        if (controls.Length == 0 && !UsesOwnedPriorityCareer9569(current)) return true;
        if (!(UsesNativeMethods1958(current) || UsesOwnedPriorityCareer9569(current)) || current.Stage != CharacterCurrentRuleStage.Career
            || controls.Any(field => field.Name.Namespace != XNamespace.None || field.HasAttributes || field.HasElements
                || profile.Elements(field.Name).Count() != 1
                || !int.TryParse(field.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0)
            || !source.TryResolveCareerSettingsProfile(out var settings, out var state) || string.IsNullOrWhiteSpace(state)
            || !XNode.DeepEquals(CreationComparisonTree1949(ParseCreation1949(settings)),
                CreationComparisonTree1949(new XElement("settings", profile.Nodes())))) return false;
        // The ordinary retained-source sheet owns initiative bounds and sustained
        // owner/occurrence/penalty calculation, even before the first registration.
        // No saved totals, ambient resolver or substitute authority are used.
        var resolution = current.Capture.CreateCurrentEnvironmentResolution(current.SemanticsProfile, current.SemanticsFingerprint);
        if (resolution is null || !current.IsUnchanged) return false;
        var sheet = UsesOwnedPriorityCareer9569(current)
            ? CompileCurrentCareerEffects1960(current, source, [])?.Imported?.Sheet
            : CharacterSheetDerivedQuery.Parse(current.Current, resolution.Resolver);
        if (sheet is null || sheet.UnavailableReason is not null || sheet.AuthorityDigest is null
            || !source.TryResolveCareerSettingsProfile(out var finalSettings, out var finalState)
            || finalSettings != settings || finalState != state || !current.IsUnchanged) return false;
        observation = CharacterCreationFinalizationDigest.Compute(new
        {
            current.BindingDigest, State = state, Controls = controls.Select(field => new
                { Name = field.Name.LocalName, Value = int.Parse(field.Value, CultureInfo.InvariantCulture) }).ToArray(),
            sheet.AuthorityDigest, sheet.PhysicalInitiative, sheet.SustainedSpellPreparation
        });
        return true;
    }

    private static bool TryObserveNativeReputationProfile5175(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, XElement profile, out string? observation)
    {
        observation = null;
        var fields = profile.Elements().Where(row => row.Name.LocalName == "usecalculatedpublicawareness").ToArray();
        if (fields.Length == 0) return true;
        if (!UsesNativeMethods1958(current) || !current.IsUnchanged
            || fields is not [var field] || field.Name != "usecalculatedpublicawareness"
            || field.HasAttributes || field.HasElements || field.Value != field.Value.Trim()
            || !bool.TryParse(field.Value, out bool configured)
            || !source.TryResolveCareerSettingsProfile(out var settings, out var profileState)
            || string.IsNullOrWhiteSpace(profileState)
            || !XNode.DeepEquals(CreationComparisonTree1949(ParseCreation1949(settings)),
                CreationComparisonTree1949(new XElement("settings", profile.Nodes())))
            || !source.TryResolveCareerReputationSettings(out var reputation, out var ruleState)
            || string.IsNullOrWhiteSpace(ruleState) || reputation.UseCalculatedPublicAwareness != configured
            || !source.TryResolveCareerSettingsProfile(out _, out var afterProfileState) || afterProfileState != profileState
            || !source.TryResolveCareerReputationSettings(out var after, out var afterRuleState)
            || afterRuleState != ruleState || after.UseCalculatedPublicAwareness != configured || !current.IsUnchanged)
            return false;
        observation = CharacterCreationFinalizationDigest.Compute(new
        { current.BindingDigest, ProfileState = profileState, RuleState = ruleState, reputation.UseCalculatedPublicAwareness });
        return true;
    }

    private static object? ConsumeNativeProfile1958(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, XElement root,
        HashSet<string> selected, List<string> blockers, CancellationToken cancellationToken)
    {
        if (!TryCurrentNativeMethod1958(current, source, out var method, blockers) || method is null) return null;
        var profile = CreationRows1949(catalog.Input).Single(row => ScalarCreation1949(row, "id") == method.Profile.SettingsProfileId);
        SelectCreationKey1949(catalog, "setting", method.Profile.SettingsProfileId, selected, blockers);
        if (!TryObserveCurrentMetatypeProfile1951(current, source, profile, out var metatypePolicy))
        { blockers.Add("current-profile-metatype-policy-unresolved"); return null; }
        string? purchasePolicy = null, skillPolicy = null;
        if (profile.Descendants().Any(field => IsCurrentPurchaseProfileSetting1950(field.Parent!.Name.LocalName, field.Name.LocalName))
            && !TryObserveCurrentPurchaseProfileSettings1950(current, source, profile, out purchasePolicy))
        { blockers.Add("current-profile-purchase-policy-unresolved"); return null; }
        if (profile.Descendants().Any(field => IsCurrentSkillProfileSetting1951(field.Parent!.Name.LocalName, field.Name.LocalName))
            && !TryObserveCurrentSkillProfileSettings1951(current, source, profile, out skillPolicy))
        { blockers.Add("current-profile-skill-policy-unresolved"); return null; }
        if (current.Stage == CharacterCurrentRuleStage.Creation && root.Elements("improvements").Any(node => node.Elements().Any()))
        { blockers.Add("current-native-method-character-effects-consumer-required"); return null; }
        if (current.Stage == CharacterCurrentRuleStage.Creation
            && method.SelectedMetatype?.BaseBonuses is { LifestyleCostPercent: not 0 })
        { blockers.Add("current-native-method-lifestyle-modifier-consumer-required"); return null; }
        string? sheetPolicy = null;
        if (!TryObserveNativeReputationProfile5175(current, source, profile, out var reputationPolicy))
        { blockers.Add("current-native-profile-reputation-policy-unresolved"); return null; }
        // Extend only the bounded native Career grammar. Wider preset grammars
        // and Creation retain their existing policy/execution paths.
        if (current.Stage == CharacterCurrentRuleStage.Career
            && CreationSyntax1949(profile, CharacterCurrentRuleDomain.Profile, nativeMagicProfile: true,
                karmaQualityPrompts: UsesKarmaQualityPrompts2742(current, source), nativeSheetProfile: true,
                nativeReputationProfile: true, careerCondition5497: UsesCareerCondition5497(current))
            && !TryObserveNativeSheetProfile5130(current, source, profile, out sheetPolicy))
        { blockers.Add("current-native-profile-sheet-policy-unresolved"); return null; }
        object? vehiclePolicy = null;
        if (UsesCareerCondition5497(current)
            && !TryObserveVehiclePolicy5497(current, source, profile, out vehiclePolicy))
        { blockers.Add("current-native-profile-vehicle-policy-unresolved"); return null; }
        cancellationToken.ThrowIfCancellationRequested();
        var career = current.Stage == CharacterCurrentRuleStage.Career
            ? EvaluateCareerAttributes1960(current, source, method.Attributes, blockers) : [];
        var outcome = new { method.OutcomeDigest, metatypePolicy, purchasePolicy, skillPolicy,
            CreationBudget = current.Stage == CharacterCurrentRuleStage.Creation
                ? method.Karma?.Quote?.KarmaBudget ?? method.Karma?.State.KarmaBudget ?? method.LifeModules?.State.LifeModuleBudget : null,
            NativeKarmaAttributes = method.Karma?.Quote?.Attributes,
            NativeKarmaResources = method.Karma?.Quote?.Resources,
            NativeKarmaContacts = method.Karma?.Quote?.Contacts,
            LifeModulesSequence = method.LifeModules?.Sequence,
            LifeModulesEffects = method.LifeModules?.Effects,
            LifeModulesAttributes = method.LifeModules?.Attributes,
            CareerAttributes = career };
        object boundOutcome = sheetPolicy is null ? (object)outcome : new { Native = outcome, SheetPolicy = sheetPolicy };
        if (reputationPolicy is not null) boundOutcome = new { Native = boundOutcome, ReputationPolicy = reputationPolicy };
        if (vehiclePolicy is not null) boundOutcome = new { Native = boundOutcome, VehiclePolicy = vehiclePolicy };
        return TagRuleEffects2912(boundOutcome,
            NativeProfileEffects2912(method, career, current.Stage),
            "Current native allocations, budgets and advancement quotes are calculated. Pending contacts are not exposed by this projection; absent later selections are not zero.");
    }
    private static bool NativeQualitySearchMetadata1958(XElement queries)
    {
        // These are optional source-catalog search filters. Native operations take
        // exact option IDs, not a query. Their retained XML stays in the catalog
        // digest; it cannot grant eligibility, budget, qualities or effects.
        if (queries.HasAttributes || queries.Elements().Any(row => row.Name != "query" || row.HasAttributes
            || row.Elements().Count() != 3 || row.Elements().Any(node => node.HasAttributes || node.HasElements)
            || row.Elements().GroupBy(node => node.Name).Any(group => group.Count() != 1)
            || row.Elements().Any(node => node.Name.LocalName is not ("id" or "display" or "xpath"))
            || !Guid.TryParseExact(row.Element("id")?.Value, "D", out var id) || id == Guid.Empty
            || string.IsNullOrWhiteSpace(row.Element("display")?.Value)
            || string.IsNullOrWhiteSpace(row.Element("xpath")?.Value))) return false;
        return queries.Elements().Select(row => row.Element("id")!.Value).Distinct(StringComparer.Ordinal).Count()
            == queries.Elements().Count();
    }

    private static bool NativeFutureGrantRow1958(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, CurrentNativeMethod1958 native, Guid id, string xml)
    {
        if (native.Profile.BuildMethod == CharacterCreationBuildMethods.LifeModules
            && source.TryResolveCreationLifeModuleTalents(out var talents) && talents is not null
            && talents.Options.SingleOrDefault(option => option.OptionId == id.ToString("D")) is { IsEnabled: true, Blockers.Count: 0 } moduleTalent
            && SamePurchaseXml(moduleTalent.SourceNodeXml, xml)
            && source.TryResolveCreationLifeModuleTalentSource(moduleTalent.OptionId, out var moduleSource)
            && moduleSource is not null && CharacterCreationTalentQualitySourceRules.IsValidSource(moduleSource)) return true;
        if (native.Karma?.State.Talents?.Options.SingleOrDefault(option => option.OptionId == id.ToString("D"))
                is { IsEnabled: true, Blockers.Count: 0 } talent
            && SamePurchaseXml(talent.SourceNodeXml, xml)) return true;
        string name = ParseCreation1949(xml).Element("name")?.Value ?? string.Empty;
        if (source is not ICharacterCurrentNativeMethodSource1958 bridge) return false;
        foreach (var metatype in native.Metatypes.Options.Concat(native.SelectedMetatype?.MetatypeIdentity is not null
                     ? new[] { native.SelectedMetatype } : []).Where(option => option.IsEnabled && option.Blockers.Count == 0
            && option.GrantedQualities.Any(grant => grant.Name == name)))
        {
            if (TryCurrentRacialSources8582(source, bridge, metatype, out var grants)
                && grants.Any(grant => grant.SourceId == id.ToString("D") && SamePurchaseXml(grant.CanonicalSourceXml, xml))
                && CharacterCreationPriorityRacialQualities.TryProjectCurrentNativeSkillCosts(grants, current.BindingDigest, out _, out _, allowLifeModuleRacialDice: metatype.MetatypeIdentity is not null)) return true;
        }
        return false;
    }

    private sealed record CurrentNativeGrantEffects1958(IReadOnlyList<CharacterCreationTalentQualitySource> Sources,
        string[] QualityXml, string[] ImprovementXml, string[] GearXml, string[] EnabledFlags, string Digest);

    private static bool TryCurrentNativeGrantEffects1958(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, CurrentNativeMethod1958 method, out CurrentNativeGrantEffects1958? effects)
    {
        effects = null;
        if (!TryCurrentNativeGrants1958(current, source, out var sources)) return false;
        if (method.LifeModules is { Effects: { } modules, Racial: { } racial })
        {
            var qualityXml = modules.QualityXml.Concat(racial.QualityXml).ToArray();
            var improvementXml = modules.ImprovementXml.Concat(racial.ImprovementXml).ToArray();
            effects = new(sources, qualityXml, improvementXml, racial.GearXml.ToArray(), racial.Flags.ToArray(),
                CharacterCreationFinalizationDigest.Compute(new { ModulesPlanDigest = modules.PlanDigest, RacialPlanDigest = racial.PlanDigest,
                    qualityXml, improvementXml, racial.GearXml, racial.Flags }));
            return current.IsUnchanged;
        }
        if (method.SelectedMetatype is not { } metatype)
        {
            effects = new([], [], [], [], [], CharacterCreationFinalizationDigest.Compute(new { Grants = "none-selected" }));
            return sources.Count == 0 && current.IsUnchanged;
        }
        return TryCompileNativeGrantEffects1958(current, metatype, method.Karma?.Quote, sources, out effects);
    }

    private static bool TryCompileNativeGrantEffects1958(CharacterCurrentRuleEnvironmentContext current,
        CharacterCreationMetatypeOptionProjection metatype, CharacterCreationKarmaMetatypeQuote? karmaQuote,
        IReadOnlyList<CharacterCreationTalentQualitySource> sources, out CurrentNativeGrantEffects1958? effects)
    {
        effects = null;
        var qualities = new List<XElement>();
        var improvements = new List<XElement>();
        var gear = new List<(XElement Saved, CharacterCreationTalentGearSource Source)>();
        var flags = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var item in sources)
            {
                bool racial = metatype.GrantedQualities.Any(grant => grant.Name == item.Name);
                string id = CharacterCreationFinalizationProjector.StableGuid(
                    $"current-native-grant:{current.BindingDigest}:{item.SourceId}:{item.SourceNodeDigest}").ToString("D");
                var definition = ParseCreation1949(item.CanonicalSourceXml);
                var unlock = karmaQuote?.Skills?.Selection.TalentUnlock;
                string extra = CharacterCreationAwakenedLegacyProjector.CompileBonus(definition.Element("bonus"),
                    string.Empty, unlock is null ? [] : [unlock], item, id, flags, improvements, gear);
                if (!CharacterCreationLegacySourceProjector.TryBuildGrantedQualityInstance(item, id, extra,
                        racial ? "Metatype" : "Selected", out var saved)) return false;
                qualities.Add(saved);
            }
            var purchased = new XElement("character", new XElement("qualities",
                (karmaQuote?.Qualities?.Selections ?? []).Select(item => new XElement("quality",
                    new XElement("name", item.Name), new XElement("sourceid", item.SourceId)))));
            foreach (var item in sources)
                CharacterCreationAwakenedLegacyProjector.CheckRestrictions(ParseCreation1949(item.CanonicalSourceXml), purchased, qualities, flags);
            var qualityXml = qualities.Select(row => row.ToString(SaveOptions.DisableFormatting)).ToArray();
            var improvementXml = improvements.Select(row => row.ToString(SaveOptions.DisableFormatting)).ToArray();
            var gearXml = gear.Select(row => row.Saved.ToString(SaveOptions.DisableFormatting)).ToArray();
            string[] enabled = flags.Order(StringComparer.Ordinal).ToArray();
            effects = new(sources, qualityXml, improvementXml, gearXml, enabled,
                CharacterCreationFinalizationDigest.Compute(new { sources, qualityXml, improvementXml, gearXml, enabled }));
            return current.IsUnchanged;
        }
        catch (Exception error) when (CreationInputError1949(error)) { return false; }
    }

    private static bool IsCurrentLifeModuleOwner1958(CurrentNativeMethod1958 method, XElement quality)
        => method.LifeModules?.Effects?.ModuleOwners.Any(owner =>
            quality.Element("guid")?.Value == owner.QualityId
            && quality.Element("sourceid")?.Value == owner.SourceId
            && quality.Element("qualitysource")?.Value == "LifeModule") == true;

    private static bool IsCurrentLifeModuleDependentQuality1958(CurrentNativeMethod1958 method,
        CurrentNativeGrantEffects1958 grants, Guid sourceId)
        => grants.QualityXml.Select(ParseCreation1949).Any(quality =>
            !IsCurrentLifeModuleOwner1958(method, quality)
            && Guid.TryParse(quality.Element("sourceid")?.Value, out var id) && id == sourceId);

    private static CharacterCurrentRuleDomainExecution? ExecuteNativeLifeModulePurchases1958(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source)
    {
        if (!TryCurrentNativeMethod1958(current, source, out var method, []) || method?.LifeModules is null
            || !TryCurrentNativeGrantEffects1958(current, source, method, out var grants) || grants is null) return null;
        if (catalog.Domain is not (CharacterCurrentRuleDomain.Gear or CharacterCurrentRuleDomain.Qualities)) return null;
        var root = ParseCreation1949(current.CharacterXml);
        if (root.Elements(catalog.Domain == CharacterCurrentRuleDomain.Gear ? "gears" : "qualities").Elements().Any()) return null;
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var rows = catalog.Domain == CharacterCurrentRuleDomain.Gear ? grants.GearXml : grants.QualityXml;
        foreach (var text in rows)
        {
            var row = ParseCreation1949(text);
            // Module owner qualities belong to the retained Life Modules catalog.
            // The canonical sequence plan binds their exact source and effect graph.
            if (catalog.Domain == CharacterCurrentRuleDomain.Qualities && IsCurrentLifeModuleOwner1958(method, row)) continue;
            if (!Guid.TryParse(row.Element("sourceid")?.Value, out var id)
                || !TryAvailablePurchaseEntry(catalog, id, out string key)) return null;
            selected.Add(key);
        }
        return new(current, catalog, rows.Length == 0 ? CharacterCurrentRuleConsumptionState.NotConfigured
            : CharacterCurrentRuleConsumptionState.Evaluated, selected.Order(StringComparer.Ordinal).ToArray(),
            CharacterCreationFinalizationDigest.Compute(new { method.OutcomeDigest, Grants = grants,
                ModuleOwners = method.LifeModules.Effects?.ModuleOwners }), []);
    }

    private static CharacterCurrentRuleDomainExecution? ExecuteNativeKarmaPurchases1958(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source)
    {
        if (!TryCurrentNativeMethod1958(current, source, out var method, []) || method?.Karma is not { } opened
            || !TryCurrentNativeGrantEffects1958(current, source, method, out var grants) || grants is null) return null;
        var root = ParseCreation1949(current.CharacterXml);
        if (root.Elements(catalog.Domain == CharacterCurrentRuleDomain.Gear ? "gears" : "qualities").Elements().Any()) return null;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        bool configured = false;
        object? quote;
        if (catalog.Domain == CharacterCurrentRuleDomain.Gear)
        {
            quote = opened.Quote?.Gear;
            configured = quote is not null || grants.GearXml.Length != 0;
            foreach (var line in opened.Quote?.Gear?.Lines ?? [])
                if (!Add(line.SourceId)) return null;
            foreach (var xml in grants.GearXml)
                if (!Guid.TryParse(ParseCreation1949(xml).Element("sourceid")?.Value, out var id) || !Add(id)) return null;
        }
        else
        {
            quote = opened.Quote?.Qualities;
            configured = quote is not null || grants.Sources.Count != 0;
            foreach (var line in opened.Quote?.Qualities?.Selections ?? [])
                if (!Add(line.SourceId)) return null;
            foreach (var grant in grants.Sources)
                if (!Guid.TryParse(grant.SourceId, out var id) || !Add(id)) return null;
        }
        return IssueRuleEffects2912(current, catalog, new(current, catalog, configured ? CharacterCurrentRuleConsumptionState.Evaluated
            : CharacterCurrentRuleConsumptionState.NotConfigured, keys.Order(StringComparer.Ordinal).ToArray(),
            CharacterCreationFinalizationDigest.Compute(new { method.OutcomeDigest, Quote = quote, Grants = grants }), []), TagRuleEffects2912(new object(), catalog.Domain == CharacterCurrentRuleDomain.Gear
                ? CharacterRuleEffectValues2912.Gear(opened.Quote?.Gear?.Lines ?? [], opened.Quote?.Gear?.Budget)
                : KarmaQualityEffects2912(opened.Quote?.Qualities),
                "Native purchase values are recalculated; grants and equipment capacity/effects are not purchase totals."));
        bool Add(Guid id)
        {
            if (!TryAvailablePurchaseEntry(catalog, id, out string key)) return false;
            keys.Add(key); return true;
        }
    }

    private static object? ConsumeNativeKarmaLifestyles1958(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source,
        HashSet<string> selected, List<string> blockers, ref CharacterCurrentRuleConsumptionState state)
    {
        if (!TryCurrentNativeMethod1958(current, source, out var method, blockers) || method?.Karma is not { } opened) return null;
        if (ParseCreation1949(current.CharacterXml).Elements("lifestyles").Elements().Any()) return null;
        var quote = opened.Quote?.Lifestyles;
        foreach (var line in quote?.Lines ?? [])
            SelectCreationKey1949(catalog, "lifestyle", line.SourceId.ToString("D"), selected, blockers);
        if (quote is null) state = CharacterCurrentRuleConsumptionState.NotConfigured;
        return new { method.OutcomeDigest, NativeLifestyles = quote };
    }

    private static object? ConsumeNativeKarmaMagic1958(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source,
        List<string> selected, ref CharacterCurrentRuleConsumptionState state)
    {
        if (!TryCurrentNativeMethod1958(current, source, out var method, []) || method?.Karma is not { } opened
            || !TryCurrentNativeGrantEffects1958(current, source, method, out var grants) || grants is null) return null;
        if (CurrentMagicRows1949(ParseCreation1949(current.CharacterXml), catalog.Domain).Length != 0) return null;
        string? kind = catalog.Domain switch
        {
            CharacterCurrentRuleDomain.Traditions => "tradition", CharacterCurrentRuleDomain.Streams => "stream",
            CharacterCurrentRuleDomain.Spells => "spell", CharacterCurrentRuleDomain.Powers => "adept-power",
            CharacterCurrentRuleDomain.ComplexForms => "complex-form", _ => null
        };
        if (kind is null) return null;
        var items = (opened.Quote?.Magic?.Sources ?? []).Where(item => item.Identity.Kind == kind).ToArray();
        foreach (var item in items)
        {
            if (!Guid.TryParse(item.Identity.SourceId, out var id) || !TryAvailablePurchaseEntry(catalog, id, out string key)) return null;
            var row = catalog.Entries.Single(entry => entry.Key == key);
            if (!XNode.DeepEquals(ParseCreation1949(row.Xml), ParseCreation1949(item.CanonicalSourceXml))) return null;
            selected.Add(key);
        }
        if (items.Length == 0) state = CharacterCurrentRuleConsumptionState.NotConfigured;
        return TagRuleEffects2912(new { method.OutcomeDigest, Sources = items, opened.Quote?.Magic,
            NativeTalent = opened.Quote?.Talent, Grants = grants.Digest },
            KarmaMagicEffects2912(items, opened.Quote?.Magic),
            "Creation option levels and power point budget are calculated; action resolution requires the selected completed graph.");
    }

    private static CharacterCurrentRuleDomainExecution? ExecuteNativeLifeModuleSkills1958(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source)
    {
        if (!TryCurrentNativeMethod1958(current, source, out var method, []) || method?.LifeModules is not { } native) return null;
        var selected = new HashSet<string>(StringComparer.Ordinal);
        if (native.Skills is { } observed)
        {
            if (!source.TryResolveCreationSkillsCatalog(out var authority)) return null;
            foreach (var skill in observed.Quote.Skills)
                if (!Add(skill.Allocation.SourceSkillId)) return null;
            foreach (var group in observed.Quote.Groups)
            {
                var row = authority.SkillGroups.SingleOrDefault(item => item.GroupId == group.Allocation.GroupId);
                if (row is null) return null;
                foreach (string member in row.MemberSkillSourceIds) if (!Add(member)) return null;
            }
        }
        return IssueRuleEffects2912(current, catalog, new(current, catalog, native.Skills is null ? CharacterCurrentRuleConsumptionState.NotConfigured
            : CharacterCurrentRuleConsumptionState.Evaluated, selected.Order(StringComparer.Ordinal).ToArray(),
            CharacterCreationFinalizationDigest.Compute(new { method.OutcomeDigest, Skills = native.Skills }), []), native.Skills is null ? null : TagRuleEffects2912(new object(), ModuleSkillsEffects2912(native.Skills.Quote), native.Skills.MissingChoices.ToArray()));
        bool Add(string id)
        {
            if (!Guid.TryParse(id, out var sourceId) || !TryAvailablePurchaseEntry(catalog, sourceId, out string key)) return false;
            selected.Add(key); return true;
        }
    }

    private static CharacterCurrentRuleDomainExecution? ExecuteNativeKarmaSkills1958(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source)
    {
        if (!TryCurrentNativeMethod1958(current, source, out var method, []) || method?.Karma is not { } opened
            || !TryCurrentNativeGrantEffects1958(current, source, method, out var grants) || grants is null) return null;
        var root = ParseCreation1949(current.CharacterXml);
        if (root.Elements("newskills").Elements().Any(container => container.HasElements)
            || root.Elements("skills").Any(container => container.HasElements)) return null;
        var selected = new List<string>();
        if (opened.Quote?.Skills is { } quote)
        {
            foreach (var skill in quote.Skills)
            {
                if (!Guid.TryParse(skill.Allocation.SourceSkillId, out var id)
                    || !TryAvailablePurchaseEntry(catalog, id, out string key)) return null;
                selected.Add(key);
            }
            // Group costs and all affected group members are in the source-bound
            // native quote, including purchased-quality cost modifiers and access.
            foreach (var group in quote.Groups)
                foreach (var skill in quote.Basis.Catalog.ActiveSkills.Where(skill => skill.SkillGroup == group.Name))
                {
                    if (!Guid.TryParse(skill.SourceSkillId, out var id) || !TryAvailablePurchaseEntry(catalog, id, out string key)) return null;
                    selected.Add(key);
                }
        }
        return IssueRuleEffects2912(current, catalog, new(current, catalog, opened.Quote?.Skills is null ? CharacterCurrentRuleConsumptionState.NotConfigured
            : CharacterCurrentRuleConsumptionState.Evaluated, selected.Distinct(StringComparer.Ordinal).ToArray(),
            CharacterCreationFinalizationDigest.Compute(new { method.OutcomeDigest, Grants = grants.Digest,
                Skills = opened.Quote?.Skills }), []), opened.Quote?.Skills is null ? null : TagRuleEffects2912(new object(), KarmaSkillsEffects2912(opened.Quote.Skills), "Skill action pools come from the sheet reader when a supported character graph is present."));
    }

    private static bool TryCurrentNativeGrants1958(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, out IReadOnlyList<CharacterCreationTalentQualitySource> grants)
    {
        grants = [];
        if (!TryCurrentNativeMethod1958(current, source, out var method, []) || method is null) return false;
        if (method.SelectedMetatype is not { } metatype) return true;
        if (source is not ICharacterCurrentNativeMethodSource1958 native
            || !TryCurrentRacialSources8582(source, native, metatype, out var racial)
            || racial.Count != metatype.GrantedQualities.Count) return false;
        if (method.Karma?.Quote?.Talent is { } talent)
        {
            if (!source.TryResolveCreationKarmaGrantSources(metatype.OptionId, talent.OptionId,
                    out var currentRacial, out var currentTalent)
                || CharacterCreationFinalizationDigest.Compute(currentRacial) != CharacterCreationFinalizationDigest.Compute(racial)) return false;
            grants = currentTalent is null ? racial : racial.Concat([currentTalent]).ToArray();
        }
        else grants = racial;
        return grants.All(CharacterCreationTalentQualitySourceRules.IsValidSource);
    }

    internal static CharacterCreationFoundationPreview? QuoteCurrentLifeModuleFoundation1958(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation metatypes,
        ICharacterSourceDataContext source, string metatypeOptionId, CharacterCreationFoundationSelection selection,
        IReadOnlyDictionary<string, string>? answers)
    {
        if (current.Stage != CharacterCurrentRuleStage.Creation || !UsesNativeMethods1958(current)
            || FindCurrentCreationMetatype(current, metatypes, source, metatypeOptionId) is not { } metatype
            || !TryCurrentNativeMethod1958(current, source, out var native, []) || native?.LifeModules is not { } observed
            || source is not ICharacterCurrentNativeMethodSource1958 bridge
            || !bridge.TryResolveCurrentLifeModulesCatalog(out var modules) || modules is null) return null;
        var view = new WorkspaceContinuationReadView(current.Current);
        var service = new CharacterCreationFoundationService(view, current.Queries,
            new CreationExactSource1949(current.CharacterXml, source), modules, new CharacterCreationFoundationDraftApplyAuthority(view));
        var quote = service.QuoteCurrentFoundation1958(current.Current,
            new(observed.State.Binding, metatype.Label, selection, answers) { MetatypeIdentity = metatype.MetatypeIdentity });
        return quote is not null && current.IsUnchanged ? quote : null;
    }

    internal static CharacterCreationFoundationFinalizationPreview? QuoteCurrentLifeModuleFinalization1960(
        CharacterCurrentRuleEnvironmentContext current, ICharacterSourceDataContext source,
        CharacterCreationFoundationFinalizationPreviewRequest request)
    {
        if (current.Stage != CharacterCurrentRuleStage.Creation || !UsesNativeMethods1958(current)
            || !TryCurrentNativeMethod1958(current, source, out var native, []) || native?.LifeModules is not { } observed
            || observed.State.PendingDraft is not { } draft
            || request.Binding.WorkspaceId != current.Current.Id || request.Binding.ContentRevision != current.Current.ContentRevision
            || request.Binding.SavedRevision != current.Current.SavedRevision
            || request.DraftRevision != draft.DraftRevision || request.DraftDigest != draft.DraftDigest
            || source is not ICharacterCurrentNativeMethodSource1958 bridge
            || !bridge.TryResolveCurrentLifeModulesCatalog(out var modules) || modules is null) return null;
        var view = new WorkspaceContinuationReadView(current.Current);
        var service = new CharacterCreationFoundationService(view, current.Queries,
            new CreationExactSource1949(current.CharacterXml, source), modules, new CharacterCreationFoundationDraftApplyAuthority(view));
        // The request is passed intact. In particular null talent remains a
        // missing selection, and paid choices never enter the persisted ledger.
        var result = service.PreviewFinalization(request);
        return result.Value is { } preview && current.IsUnchanged ? preview : null;
    }

    internal static bool CurrentKarmaQuoteCatalogs1958(CharacterCreationKarmaMetatypeQuote quote,
        IReadOnlyList<CharacterCurrentRuleCatalogValidation> catalogs, ICharacterSourceDataContext source)
    {
        bool Available(CharacterCurrentRuleDomain domain, string id)
            => Guid.TryParse(id, out var sourceId) && catalogs.SingleOrDefault(item => item.Domain == domain) is { } catalog
                && TryAvailablePurchaseEntry(catalog, sourceId, out _);
        if (!Available(CharacterCurrentRuleDomain.Metatypes, quote.Metatype.OptionId)
            || quote.Talent is { } talent && talent.OptionId != CharacterCreationKarmaTalentCatalog.MundaneOptionId
                && !Available(CharacterCurrentRuleDomain.Qualities, talent.OptionId)) return false;
        if ((quote.Skills?.Skills ?? []).Any(item => !Available(CharacterCurrentRuleDomain.Skills, item.Allocation.SourceSkillId))
            || (quote.Qualities?.Selections ?? []).Any(item => !Available(CharacterCurrentRuleDomain.Qualities, item.SourceId.ToString("D")))
            || (quote.Gear?.Lines ?? []).Any(item => !Available(CharacterCurrentRuleDomain.Gear, item.SourceId.ToString("D")))
            || (quote.Lifestyles?.Lines ?? []).Any(item => !Available(CharacterCurrentRuleDomain.Lifestyles, item.SourceId.ToString("D")))) return false;
        if (quote.Skills?.Groups is { Count: > 0 } groups)
        {
            if (!source.TryResolveCreationSkillsCatalog(out var skills)) return false;
            foreach (var group in groups)
            {
                var entry = skills.SkillGroups.SingleOrDefault(item => item.GroupId == group.Allocation.GroupId);
                if (entry is null || entry.MemberSkillSourceIds.Any(id => !Available(CharacterCurrentRuleDomain.Skills, id))) return false;
            }
        }
        foreach (var item in quote.Magic?.Sources ?? [])
        {
            var domain = item.Identity.Kind switch
            {
                "tradition" => CharacterCurrentRuleDomain.Traditions, "stream" => CharacterCurrentRuleDomain.Streams,
                "spell" => CharacterCurrentRuleDomain.Spells, "adept-power" => CharacterCurrentRuleDomain.Powers,
                "complex-form" => CharacterCurrentRuleDomain.ComplexForms, _ => (CharacterCurrentRuleDomain?)null
            };
            if (domain is null || !Available(domain.Value, item.Identity.SourceId)) return false;
        }
        return true;
    }

    private static bool CurrentFutureKarmaGrants1958(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, CharacterCreationKarmaMetatypeQuote quote)
    {
        if (source is not ICharacterCurrentNativeMethodSource1958 native
            || !native.TryResolveCurrentMetatypeGrantSources(quote.Metatype.OptionId, out var racial)) return false;
        IReadOnlyList<CharacterCreationTalentQualitySource> grants = racial;
        if (quote.Talent is { } talent)
        {
            if (!source.TryResolveCreationKarmaGrantSources(quote.Metatype.OptionId, talent.OptionId,
                out var resolved, out var selected)
                || CharacterCreationFinalizationDigest.Compute(resolved) != CharacterCreationFinalizationDigest.Compute(racial)) return false;
            grants = selected is null ? racial : racial.Concat([selected]).ToArray();
        }
        return grants.All(CharacterCreationTalentQualitySourceRules.IsValidSource)
            && TryCompileNativeGrantEffects1958(current, quote.Metatype, quote, grants, out _);
    }

    internal static CharacterCreationKarmaMetatypeQuote? QuoteCurrentKarmaBuild1958(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation metatypes,
        ICharacterSourceDataContext source, CharacterCurrentKarmaChoices1958 choices)
    {
        if (current.Stage != CharacterCurrentRuleStage.Creation || !UsesNativeMethods1958(current)
            || FindCurrentCreationMetatype(current, metatypes, source, choices.MetatypeOptionId) is null
            || !TryCurrentNativeMethod1958(current, source, out var native, []) || native?.Karma is not { } opened) return null;
        var service = new CharacterCreationKarmaMetatypeService(new WorkspaceContinuationReadView(current.Current),
            new CreationExactSource1949(current.CharacterXml, source));
        // Load the exact native policy/catalog dependencies requested by this future
        // selection. No guessed authority, Priority allocation or persisted quote.
        var state = service.Load(current.Current.Id, includeSkills: choices.Skills is not null,
            includeQualities: choices.QualityOptionIds is not null, includeGear: choices.Gear is not null,
            includeLifestyles: choices.Lifestyles is not null, includeMagic: choices.Magic is not null);
        if (state.Value is null || state.Blockers.Count != 0) return null;
        var result = service.PreviewCommand(new(state.Value.Binding, choices.MetatypeOptionId, string.Empty, Guid.Empty, false,
            choices.TalentOptionId, choices.Attributes, choices.Skills, choices.ResourceKarma, choices.QualityOptionIds, choices.Gear,
            choices.Contacts, choices.Lifestyles, choices.StartingLifestyleId, choices.Magic)
            { QualityPromptValues = choices.QualityPromptValues });
        return result.Value is { CanSelect: true, Blockers.Count: 0 } quote
            && CurrentFutureKarmaGrants1958(current, source, quote) && current.IsUnchanged ? quote : null;
    }

    internal static CharacterCreationKarmaMetatypeQuote? QuoteCurrentKarmaFoundation1958(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation metatypes,
        ICharacterSourceDataContext source, string metatypeOptionId, string? talentOptionId,
        IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? attributes)
    {
        if (current.Stage != CharacterCurrentRuleStage.Creation || !UsesNativeMethods1958(current)
            || FindCurrentCreationMetatype(current, metatypes, source, metatypeOptionId) is null
            || !TryCurrentNativeMethod1958(current, source, out var native, []) || native?.Karma is not { } opened)
            return null;
        var service = new CharacterCreationKarmaMetatypeService(new WorkspaceContinuationReadView(current.Current),
            new CreationExactSource1949(current.CharacterXml, source));
        var preview = service.Preview(opened.State.Binding, metatypeOptionId, talentOptionId, attributes);
        return preview.Value is { CanSelect: true, Blockers.Count: 0 } quote
            && CurrentFutureKarmaGrants1958(current, source, quote) && current.IsUnchanged ? quote : null;
    }

}

// Current observation of the real native journey. The original sequence keeps
// its finalization blockers and cannot be used as a completed write plan.
internal sealed record CurrentLifeModulesObservation1958(CharacterCreationFoundationState State,
    CharacterCreationLifeModuleSequenceCompilation? Sequence,
    CharacterCreationFoundationSequenceWritePlan? Effects = null,
    CharacterCreationLifeModuleMetatypeWritePlan? Racial = null,
    CharacterCreationLifeModuleAttributeQuote? Attributes = null,
    CurrentLifeModulesSkillObservation1958? Skills = null);

// An invocation-local relay used by the existing captured-source read session.
// Implementations must replay the observation against retained and fresh sources.
internal interface ICharacterFinalizedModuleOwnershipSource
{
    bool TryObserveFinalizedModules(WorkspaceStoredDocument saved,
        out CharacterFinalizedLifeModuleOwnership? ownership);
}

/// <summary>Rebuilt native module ownership, never a caller-authored grant or mutation permit.</summary>
internal sealed class CharacterFinalizedLifeModuleOwnership
{
    private readonly IReadOnlyDictionary<Guid, string> qualities;
    private readonly IReadOnlyList<string> effects;

    private CharacterFinalizedLifeModuleOwnership(WorkspaceStoredDocument saved,
        CharacterCreationFoundationSequenceWritePlan plan, Dictionary<Guid, string> qualities,
        string currentSourceContextDigest)
    {
        this.qualities = new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, string>(qualities);
        effects = Array.AsReadOnly(plan.ImprovementXml.ToArray());
        Digest = CharacterCreationFinalizationDigest.Compute(new
        {
            Semantics = "native-finalized-module-ownership/v2", saved.Id,
            saved.ContentRevision, saved.SavedRevision, saved.Document.AuxiliaryStateDigest,
            Character = CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(saved.Document.Content),
            plan.PlanDigest, CurrentSourceContextDigest = currentSourceContextDigest,
            Qualities = plan.QualityXml, Effects = plan.ImprovementXml
        });
    }

    public string Digest { get; }

    internal static bool TryObserve(WorkspaceStoredDocument saved, ICharacterSourceDataContext source,
        out CharacterFinalizedLifeModuleOwnership? ownership)
    {
        ownership = null;
        if (source is ICharacterFinalizedModuleOwnershipSource relay)
            return relay.TryObserveFinalizedModules(saved, out ownership);
        try
        {
            var root = CharacterCurrentRuleEnvironmentConsumers.ParsePurchaseXml(saved.Document.Content);
            if (root is null || root.Name != "character" || root.HasAttributes) return false;
            var methods = root.Elements().Where(row => row.Name.LocalName == "buildmethod").ToArray();
            if (methods.Length > 1 || methods.Any(row => row.Name != "buildmethod" || row.HasAttributes || row.HasElements))
                return false;
            if (methods.SingleOrDefault()?.Value != CharacterCreationBuildMethods.LifeModules) return true;
            if (saved.Document.RulesetId != "sr5"
                || !source.TryResolveCreationSourceProfile(out var profile)
                || profile.BuildMethod != CharacterCreationBuildMethods.LifeModules
                || CharacterCreationFoundationService.CompileFinalizedLifeModulesForCareer(saved, source,
                    out string currentSourceContextDigest) is not { } plan)
                return false;
            var containers = root.Elements("qualities").ToArray();
            var effectContainers = root.Elements("improvements").ToArray();
            if (containers is not [var qualityBox] || effectContainers is not [var effectBox]
                || qualityBox.HasAttributes || effectBox.HasAttributes
                || qualityBox.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
                || effectBox.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
                || qualityBox.Elements().Any(row => row.Name != "quality")
                || effectBox.Elements().Any(row => row.Name != "improvement")) return false;
            var savedOwners = new Dictionary<Guid, XElement>();
            foreach (var row in qualityBox.Elements())
            {
                var ids = row.Elements("guid").ToArray();
                if (ids is not [var idNode] || idNode.HasAttributes || idNode.HasElements
                    || !Guid.TryParseExact(idNode.Value, "D", out var id) || id == Guid.Empty
                    || !savedOwners.TryAdd(id, row)) return false;
            }
            var owned = new Dictionary<Guid, string>();
            var compiledEffects = plan.ImprovementXml.Select(XElement.Parse).ToArray();
            foreach (string xml in plan.QualityXml)
            {
                var expected = XElement.Parse(xml);
                if (!Guid.TryParseExact(expected.Element("guid")?.Value, "D", out var id) || id == Guid.Empty
                    || !owned.TryAdd(id, xml) || !savedOwners.TryGetValue(id, out var actual)
                    || !CharacterCurrentRuleEnvironmentConsumers.SameCurrentInstance1960(actual, expected)) return false;
                string owner = id.ToString("D");
                var savedEffects = effectBox.Elements().Where(row => row.Element("sourcename")?.Value == owner);
                var expectedEffects = compiledEffects.Where(row => row.Element("sourcename")?.Value == owner);
                if (!CharacterCurrentRuleEnvironmentConsumers.SameCurrentEffects1960(savedEffects, expectedEffects)) return false;
            }
            if (savedOwners.Any(row => (row.Value.Element("qualitysource")?.Value == "LifeModule"
                    || row.Value.Element("qualitytype")?.Value == "LifeModule") && !owned.ContainsKey(row.Key))
                || compiledEffects.Any(row => !Guid.TryParseExact(row.Element("sourcename")?.Value, "D", out var id)
                    || !owned.ContainsKey(id))) return false;
            ownership = new(saved, plan, owned, currentSourceContextDigest);
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or XmlException or InvalidOperationException
            or ArgumentException or IOException or OverflowException) { return false; }
    }

    internal bool TryGetModuleDefinition(XElement saved, out XElement? definition)
    {
        definition = null;
        if (!Guid.TryParseExact(saved.Element("guid")?.Value, "D", out var owner)
            || !qualities.TryGetValue(owner, out string? xml)) return false;
        var expected = XElement.Parse(xml);
        if (expected.Element("qualitytype")?.Value != "LifeModule"
            || !CharacterCurrentRuleEnvironmentConsumers.SameCurrentInstance1960(saved, expected)) return false;
        // A module view retains its distinct kind. Its historical selection and
        // selected bonus were reconstructed by the native planner above.
        definition = new XElement("lifemodule",
            new XElement("id", expected.Element("sourceid")!.Value),
            new XElement("name", expected.Element("name")!.Value),
            new XElement("source", expected.Element("source")!.Value),
            new XElement("implemented", expected.Element("implemented")!.Value),
            new XElement(expected.Element("bonus")!));
        return true;
    }

    internal bool OwnsQuality(XElement saved)
        => Guid.TryParseExact(saved.Element("guid")?.Value, "D", out var owner)
            && qualities.TryGetValue(owner, out string? xml)
            && CharacterCurrentRuleEnvironmentConsumers.SameCurrentInstance1960(saved, XElement.Parse(xml));

    // These rows were freshly compiled and matched to the authenticated archive
    // and saved owner graph. They are never read from cached character totals.
    internal IEnumerable<XElement> EffectsForOwner(Guid owner)
        => effects.Select(XElement.Parse).Where(row => row.Element("sourcename")?.Value == owner.ToString("D"));

    internal int AttributeLevels(string name)
    {
        decimal total = effects.Select(XElement.Parse)
            .Where(row => row.Element("improvementttype")?.Value == "Attributelevel"
                && row.Element("improvedname")?.Value == name && Enabled(row))
            .Sum(row => decimal.Parse(row.Element("val")!.Value, CultureInfo.InvariantCulture));
        if (total < 0 || total != decimal.Truncate(total) || total > int.MaxValue)
            throw new InvalidDataException("power_module_attribute_level_unavailable");
        return (int)total;
    }

    internal bool OwnsAttributeLevel(XElement actual, string name)
        => actual.Element("improvementttype")?.Value == "Attributelevel"
            && actual.Element("improvedname")?.Value == name && Enabled(actual)
            && effects.Select(XElement.Parse).Any(expected =>
                CharacterCurrentRuleEnvironmentConsumers.SameCurrentEffects1960([actual], [expected]));

    private static bool Enabled(XElement effect)
        => effect.Element("enabled")?.Value == "1"
            || bool.TryParse(effect.Element("enabled")?.Value, out bool enabled) && enabled;
}


public sealed partial class CharacterCreationFoundationService
{
    internal CharacterCreationFoundationResult<CharacterCreationFoundationState> LoadFinalizationState1966(
        CharacterWorkspaceId workspaceId)
    {
        var read = _workspaceStore.Get(workspaceId);
        if (!read.Success || read.Value is not { } workspace) return ReadFailure<CharacterCreationFoundationState>(read);
        // Like native finalization preparation, a read of the finished draft does
        // not require draft-write authority. All source/draft/native blockers and
        // the display catalog/digest are still produced by the original builder.
        return BuildState(workspace, null, false, out _, out _, out _, requireDraftPersistence: false, foundationDisplayOnly: true);
    }

    internal static CharacterCreationFoundationSequenceWritePlan? CompileFinalizedLifeModules1964(
        WorkspaceStoredDocument current, ICharacterSourceDataContext source)
        => source is ICharacterCurrentNativeMethodSource1958 native
            && native.TryResolveCurrentLifeModulesCatalog(out var modules) && modules is not null
            && source.TryResolveCreationFoundationEffectSources(out var effects) && effects is not null
                ? CompileFinalizedLifeModulePlan(current, modules, effects) : null;

    internal static CharacterCreationFoundationSequenceWritePlan? CompileFinalizedLifeModulesForCareer(
        WorkspaceStoredDocument current, ICharacterSourceDataContext source, out string currentSourceContextDigest)
    {
        currentSourceContextDigest = string.Empty;
        if (source is not ICharacterCareerLifeModuleSource career
            || !career.TryResolveFinalizedLifeModuleSources(out var modules, out var effects)
            || modules is null || effects is null) return null;
        effects = effects.CopyWithDetachedSourcebooks();
        return effects.TryCreateAuthorities(out _, out _, out _, out currentSourceContextDigest)
            ? CompileFinalizedLifeModulePlan(current, modules, effects, replayFinalizedProfile: true) : null;
    }

    private static CharacterCreationFoundationSequenceWritePlan? CompileFinalizedLifeModulePlan(
        WorkspaceStoredDocument current, ILifeModulesCatalogService modules, CharacterCreationFoundationEffectSources inputs,
        bool replayFinalizedProfile = false)
    {
        var state = current.Document.AuxiliaryState;
        if (state.CharacterCreationFoundationDraft is not null
            || state.CharacterCreationFinalizationReceipts is not { Count: 1 } receipts
            || state.CharacterCreationFinalizationArchive is not { } archive
            || archive.LifeModuleAuthority is not { } authority
            || archive.State.CharacterCreationFoundationDraft is not { ModuleSelectionFinished: true, CharacterEffectsApplied: false } draft
            || !CharacterCreationLifeModuleFinalizationTransaction.IsValidArchive(current.Id, archive, receipts[0].Receipt)
            || !inputs.TryCreateAuthorities(out var skills, out var qualities, out var levels, out string currentContextDigest)) return null;
        // Owner GUIDs depend on the original compilation namespace. Authenticate
        // that historical context from current catalog/book bytes before replay;
        // the ownership observation separately binds the actual current context.
        string replayContextDigest = currentContextDigest;
        if (replayFinalizedProfile && (authority.Preview.ModuleSequence is not { } historicalSequence
            || !inputs.TryMatchFinalizedContext(authority.Parts.Talent.Catalog.SettingsProfileId,
                authority.Parts.Talent.Catalog.RawProfileInputsDigest, historicalSequence.SourceContextDigest, out replayContextDigest))) return null;
        var catalog = modules.GetOptionProjections();
        // The catalog lookup and planner verify raw XML; the draft retains its combined binding digest.
        var catalogDigest = modules.GetAuthority().RawXmlDigest;
        var module = catalog.SingleOrDefault(row => row.ModuleId == draft.Selection.ModuleId);
        var version = module?.Versions.SingleOrDefault(row => row.VersionId == draft.Selection.VersionId);
        if (module is null || module.Versions.Count != 0 && version is null) return null;
        var first = CharacterCreationFoundationEffectCompiler.Compile(current.Document.RulesetId,
            draft, module, version, skills, qualities, replayContextDigest, levels);
        var sequence = CompileModuleSequence(current.Document.RulesetId, catalog, draft, first,
            skills, qualities, levels, replayContextDigest, authority.Request.QualityInstanceValues);
        if (!CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(sequence, authority.Preview.ModuleSequence)) return null;
        var rebuilt = CharacterCreationFoundationLifeModuleQualityWritePlanner.BuildSequence(current.Document.RulesetId,
            authority.RawCharacterXml, draft, sequence, catalog, modules.ReadSourceBytes(catalogDigest), catalogDigest,
            skills, qualities, levels);
        return rebuilt is { Plan: { } plan, Blockers.Count: 0 }
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(plan, authority.Parts.Effects) ? plan : null;
    }

    internal CharacterCreationFoundationPreview? QuoteCurrentFoundation1958(WorkspaceStoredDocument workspace,
        CharacterCreationFoundationPreviewRequest request)
    {
        var evaluated = EvaluatePreview(request, workspace);
        if (evaluated.Context is not { } context || evaluated.Result.Value is not { } preview
            || preview.AuthorityBlockers.Any(blocker => blocker is not (CharacterCreationFoundationBlockers.WizardStatePersistenceAuthorityRequired
                or CharacterCreationFoundationBlockers.PendingDraftDuplicate)))
            return null;
        // This is the native operation's actual proposed pending ledger, not a
        // completed draft. It is observed privately and never saved or finalized.
        var proposed = CharacterCreationFoundationDraftApplyAuthority.BuildProposedLedger(context);
        var auxiliary = workspace.Document.AuxiliaryState with { CharacterCreationFoundationDraft = proposed };
        var candidate = workspace with { ContentRevision = workspace.ContentRevision + 1,
            Document = workspace.Document with { State = workspace.Document.State with { AuxiliaryState = auxiliary } } };
        return ObserveCurrentLifeModules1958(candidate) is not null ? preview : null;
    }

    internal CurrentLifeModulesObservation1958? ObserveCurrentLifeModules1958(WorkspaceStoredDocument workspace)
    {
        var loaded = BuildState(workspace, null, false, out var modules, out var catalogDigest, out _,
            requireDraftPersistence: false);
        if (loaded.Value is not { } state || loaded.Blockers.Count != 0 || state.AuthorityBlockers.Count != 0) return null;
        if (state.PendingDraft is not { } draft) return new(state, null);
        if (draft.CharacterEffectsApplied || draft.WorkspaceId != workspace.Id
            || draft.BaseContentRevision <= 0 || draft.BaseContentRevision >= workspace.ContentRevision
            || draft.BaseRawCharacterXmlDigest != CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(workspace.Document.Content)
            || draft.DraftDigest != CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(draft)
            || draft.SourceDigest != state.Binding.SourceDigest || ValidateContinuationDraft(workspace).Count != 0) return null;
        var module = state.NationalityOptions.SingleOrDefault(option => option.ModuleId == draft.Selection.ModuleId);
        var version = module?.Versions.SingleOrDefault(option => option.VersionId == draft.Selection.VersionId);
        if (module is null || (module.Versions.Count == 0 ? !string.IsNullOrEmpty(draft.Selection.VersionId) : version is null)) return null;
        var source = _sourceDataResolver.TryCreateContext(workspace.Document.Content);
        if (source is null || !source.TryResolveCreationFoundationEffectSources(out var inputs) || inputs is null
            || !inputs.TryCreateAuthorities(out var skills, out var qualities, out var levels, out string sourceDigest)) return null;
        var nationality = CharacterCreationFoundationEffectCompiler.Compile(workspace.Document.RulesetId,
            draft, module, version, skills, qualities, sourceDigest, levels);
        var sequence = CompileModuleSequence(workspace.Document.RulesetId, modules, draft,
            nationality, skills, qualities, levels, sourceDigest, null);
        var effects = CharacterCreationFoundationLifeModuleQualityWritePlanner.ObserveCurrentSequence1958(
            workspace.Document.RulesetId, workspace.Document.Content, draft, sequence, modules,
            _lifeModulesCatalog.ReadSourceBytes(catalogDigest), catalogDigest, skills, qualities, levels);
        if (effects.Plan is null || effects.Blockers.Count != 0
            || ResolveMetatypeOption(state.MetatypeOptions, draft.RequestedMetatype, new List<string>(), draft.MetatypeIdentity) is not { } metatype) return null;
        var racial = CharacterCreationLifeModuleMetatypeWritePlanner.ObserveCurrent1958(workspace, draft,
            effects.Plan, metatype, source);
        if (racial.Plan is null || racial.Blockers.Count != 0 || !source.TryResolveCreationAttributePolicy(out var policy) || policy is null) return null;
        var attributes = CharacterCreationLifeModuleAttributeRules.Evaluate(workspace.Document.Content,
            effects.Plan, racial.Plan, policy, purchases: null);
        if (attributes.Quote is null || attributes.Blockers.Count != 0) return null;
        var currentSkills = CharacterCreationLifeModuleSkillsRules.ObserveCurrent1958(workspace.Document.Content,
            effects.Plan, racial.Plan, attributes.Quote, source);
        if (currentSkills is null) return null;
        // Distinct current-observation semantics preserve the original incomplete
        // draft and sequence. Every effect is materialized by the same canonical
        // source/push/quality graph body; no finalization flag is changed.
        return new(state, sequence, effects.Plan, racial.Plan, attributes.Quote, currentSkills);
    }
}

internal sealed record CurrentLifeModulesSkillObservation1958(string Semantics,
    CharacterCreationLifeModuleSkillsQuote Quote, IReadOnlyList<string> MissingChoices);

internal static partial class CharacterCreationLifeModuleSkillsRules
{
    internal static CurrentLifeModulesSkillObservation1958? ObserveCurrent1958(string characterXml,
        CharacterCreationFoundationSequenceWritePlan effects, CharacterCreationLifeModuleMetatypeWritePlan racial,
        CharacterCreationLifeModuleAttributeQuote attributes, ICharacterSourceDataContext source)
    {
        if (!source.TryResolveCreationSkillsCatalog(out var catalog)
            || !source.TryResolveCreationLifeModuleSkillsPolicy(out var policy) || policy is null) return null;
        var current = QuoteCore1958(characterXml, effects, racial, talent: null, attributes,
            selection: null, catalog, policy, currentObservation: true);
        if (current.Quote is null || current.Blockers.Count != 0
            || !source.TryResolveCreationSkillsCatalog(out var finalCatalog)
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(catalog, finalCatalog)
            || !source.TryResolveCreationLifeModuleSkillsPolicy(out var finalPolicy)
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(policy, finalPolicy)) return null;
        return new("life-module-current-skill-observation/v1", current.Quote,
            current.Quote.NativeLanguagesUsed == 0 ? ["talent", "native-language"] : ["talent"]);
    }
}
