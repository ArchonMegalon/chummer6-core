using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCurrentRuleEnvironmentConsumers
{
    // Process opt-in; the cumulative allocation count belongs to the calling thread only.
    private static readonly bool observeCareerGearGrammarAllocations4559 =
        Environment.GetEnvironmentVariable("CHUMMER_CONTACTS_PHASES4538") == "1";
    [ThreadStatic]
    private static long careerGearGrammarAllocatedBytes4559;

    public static CharacterCurrentRuleCatalogValidation? ValidatePurchasesCatalog(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogInput input,
        ICharacterSourceDataContext source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!current.IsUnchanged || input.ContextDigest != current.BindingDigest || input.Blockers.Count != 0
            || input.Domain is not (CharacterCurrentRuleDomain.Gear or CharacterCurrentRuleDomain.Qualities)) return null;
        var entries = new List<CharacterCurrentRuleCatalogEntry>();
        CharacterCreationGearAuthority? gear = null;
        CharacterCreationQualitiesAuthority? qualities = null;
        string settings = string.Empty;
        CharacterCareerQualitySettings? qualitySettings = null;
        int? qualityArmCount = null;
        bool qualityAnatomyAttempted = false;
        if (current.Stage == CharacterCurrentRuleStage.Creation)
        {
            if (input.Domain == CharacterCurrentRuleDomain.Gear)
            {
                if (!source.TryResolveCreationGearAuthority(out gear) || !CharacterCreationGearRules.IsValidAuthority(gear)) return null;
            }
            else if (!TryCurrentQualityAuthority(current, source, out qualities) || !qualities.IsAuthoritative
                || qualities.Blockers.Count != 0) return null;
        }
        else if (!source.TryResolveCareerSettingsProfile(out settings, out _)
            || input.Domain == CharacterCurrentRuleDomain.Qualities
                && !source.TryResolveCareerQualitySettings(out qualitySettings, out _)) return null;
        CurrentNativeGrantEffects1958? nativeEffects = null;
        CurrentNativeMethod1958? nativeMethod = null;
        if (UsesNativeMethods1958(current) && current.Stage == CharacterCurrentRuleStage.Creation)
        {
            if (!TryCurrentNativeMethod1958(current, source, out nativeMethod, []) || nativeMethod is null
                || !TryCurrentNativeGrantEffects1958(current, source, nativeMethod, out nativeEffects)) return null;
        }
        IReadOnlyList<CharacterCreationTalentQualitySource> nativeGrants = nativeEffects?.Sources ?? [];
        CurrentCareerEffects1960? careerEffects = null;
        if ((UsesNativeMethods1958(current) || UsesOwnedPriorityCareer9569(current)) && current.Stage == CharacterCurrentRuleStage.Career)
        {
            careerEffects = CompileCurrentCareerEffects1960(current, source, []);
            if (careerEffects is null) return null;
        }
        CharacterCreationKarmaQualitiesCatalog? promptCatalog2742 = null;
        bool prompted2742 = input.Domain == CharacterCurrentRuleDomain.Qualities && UsesKarmaQualityPrompts2742(current, source);
        if (prompted2742 && (!((ICurrentKarmaQualityPromptSource2742)source)
                .TryReadKarmaQualityPromptCatalog2742(out promptCatalog2742)
            || !CharacterCreationKarmaQualitiesRules.IsValidCatalog(promptCatalog2742)
            || promptCatalog2742!.Schema != CharacterCreationKarmaQualitiesCatalog.SchemaV2)) return null;
        // settings is the one immutable string resolved above for this catalog operation.
        // Parse lazily after row/source equality, share only typed price data, retain no authority.
        // All three Gear grammar branches use this exact call-local settings value;
        // focus/ammunition still validate and serialize every supported rating.
        CharacterCreationLegacySourceProjector.CareerGearPricePolicyEvaluation? gearPricePolicy = null;
        foreach (var entry in input.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Key == "$global")
            {
                // Category labels and translated display labels have no numeric effect. Other
                // document-level mechanics need a domain consumer and cannot disappear here.
                var global = ParsePurchaseXml(entry.Xml);
                if (global is null || global.Elements().Any(node => node.Name != "categories"
                        && !((UsesNativeMethods1958(current) || UsesPriorityCurrent3027(current)) && input.Domain == CharacterCurrentRuleDomain.Qualities
                            && node.Name == "xpathqueries" && NativeQualitySearchMetadata1958(node)))
                    || global.Elements("categories").Any(node => node.HasAttributes
                        || node.Elements().Any(category => category.Name != "category" || category.HasElements
                            || category.Attributes().Any(attribute => attribute.Name.LocalName is not ("translate" or "blackmarket")))))
                    return null;
                if (global.Descendants().Attributes("blackmarket").Any()
                    && (!(UsesNativeMethods1958(current) || UsesPriorityCurrent3027(current)) || !CurrentBlackMarketCategoryMetadata1960(global))) return null;
                entries.Add(entry);
                continue;
            }
            if (entry.State == CharacterCurrentRuleEntryState.Tombstone) { entries.Add(entry); continue; }
            XElement? xml = ParsePurchaseXml(entry.Xml);
            if (xml is null || !Guid.TryParse(xml.Element("id")?.Value, out Guid id) || id == Guid.Empty) return null;
            // The exact catalog text was successfully parsed above, including
            // DTD prohibition and the document-size limit. Identical immutable
            // source text needs no second syntax read or structural comparison.
            // Every source query still occurs; differing bytes keep the full
            // historical parser/comparator, including whitespace normalization.
            bool MatchesSourceXml8074(string sourceXml) =>
                string.Equals(sourceXml, entry.Xml, StringComparison.Ordinal)
                    || SamePurchaseXml(sourceXml, entry.Xml);
            bool available;
            if (gear is not null)
            {
                var options = gear.Options.Where(option => option.SourceId == id).ToArray();
                available = options.Length == 1 && options[0].IsSelectable && options[0].PricingIsExact
                    && options[0].AvailabilityIsExact && options[0].Blockers.Count == 0
                    && MatchesSourceXml8074(options[0].SourceNodeXml);
                if (!available && nativeEffects is not null && nativeGrants.SelectMany(grant => grant.GrantedGearSources ?? [])
                    .Any(grant => grant.SourceId == id.ToString("D") && MatchesSourceXml8074(grant.CanonicalSourceXml)))
                    available = true;
            }
            else if (qualities is not null)
            {
                var options = qualities.Options.Where(option => option.SourceId == id).ToArray();
                available = options.Length != 0 && options.All(option => option.IsSelectable && option.EligibilityIsExact
                    && MatchesSourceXml8074(option.SourceNodeXml));
                if (!available && nativeEffects is not null && nativeGrants.Any(grant => grant.SourceId == id.ToString("D")
                        && MatchesSourceXml8074(grant.CanonicalSourceXml))) available = true;
                // A grant-only talent/racial row can have exact native grant
                // semantics while remaining forbidden as an ordinary quality purchase.
                if (!available && nativeEffects is not null)
                    available = NativeFutureGrantRow1958(current, source, nativeMethod!, id, entry.Xml);
                if (!available && nativeMethod?.LifeModules?.Effects is not null && nativeEffects is not null)
                    available = IsCurrentLifeModuleDependentQuality1958(nativeMethod, nativeEffects, id)
                        && source.TryResolveCareerQualitySource(id, out var dependent) && dependent is not null
                        && MatchesSourceXml8074(dependent.RawSourceXml);
            }
            else if (input.Domain == CharacterCurrentRuleDomain.Gear)
            {
                available = source.TryResolveCareerGearPurchaseSource(id, out var item) && item is not null
                    && MatchesSourceXml8074(item.SourceXml)
                    && (ValidateCareerGearGrammar(current, item, settings, ref gearPricePolicy, xml)
                        || CharacterCreationLegacySourceProjector.TryReadRetainedCareerFocus6511(item, out var retainedFocus)
                            && retainedFocus!.ValidateCatalog(new(current.Current.Id, Guid.Empty, item.SourceId,
                                0, 1m, DateTime.UnixEpoch), gearPricePolicy ??= new(settings))
                        || TryCurrentRetainedAmmunition(current, source, item, out var ammunition)
                        && ammunition!.ValidateCatalog(new(current.Current.Id, Guid.Empty, id, 0, 1m, DateTime.UnixEpoch),
                            gearPricePolicy ??= new(settings)));
                if (!available && careerEffects is not null)
                    available = careerEffects.NativeGrants.SelectMany(grant => grant.GrantedGearSources ?? [])
                        .Any(grant => grant.SourceId == id.ToString("D") && MatchesSourceXml8074(grant.CanonicalSourceXml));
            }
            else
            {
                available = false;
                if (source.TryResolveCareerQualitySource(id, out var item) && item is not null
                    && MatchesSourceXml8074(item.RawSourceXml))
                {
                    // Resolve only the supported anatomy expression, with the same
                    // source/owner closure used by the native Career compiler.
                    if (!qualityAnatomyAttempted && xml.Element("nolevels") is null
                        && xml.Element("limit") is { HasAttributes: false, HasElements: false } limit
                        && string.Equals(limit.Value, "{arm} - 1", StringComparison.Ordinal))
                    {
                        qualityAnatomyAttempted = true;
                        if (TryResolveCurrentCareerQualityAnatomy1960(current.CharacterXml, source,
                            out int arms, out _)) qualityArmCount = arms;
                    }
                    available = CharacterCreationLegacySourceProjector.TryBuildCareerQualityInstance(item.RawSourceXml, id,
                            id, qualityArmCount, true, out var definition, out _, out _,
                        importedCareerEffects5698: careerEffects?.Imported is not null)
                        && CharacterCareerQualityRules.TryCalculateOrdinaryCurrentPrice(
                            CharacterCareerQualityOperation.AcquireLevel, definition, qualitySettings!, 0, out _, out _);
                }
                if (!available && careerEffects is not null)
                    available = careerEffects.NativeGrants.Any(grant => grant.SourceId == id.ToString("D")
                        && MatchesSourceXml8074(grant.CanonicalSourceXml))
                        || careerEffects.Imported?.GrantedQualities.Any(grant => grant.SourceId == id
                            && MatchesSourceXml8074(grant.SourceXml)) == true;
            }
            if (!available && promptCatalog2742 is not null)
            {
                // Independent CURRENT source projection, never saved Extra or a
                // selected-ID exemption. AdmitCatalog compiled the original XML;
                // IsExactPurchase rechecks its digest/anchors/cost/strict grammar.
                var prompted = promptCatalog2742.Options.Where(option => option.SourceId == id).ToArray();
                available = prompted.Length != 0 && prompted.All(option =>
                    option.SourceExecutionSchema == CharacterCreationKarmaQualityPrompts.SourceSchema
                    && option.FollowUpChoiceLabel is null
                    && CharacterCreationKarmaQualitiesRules.IsExactPurchase(option)
                    && MatchesSourceXml8074(option.SourceNodeXml));
            }
            entries.Add(available ? entry : entry with { State = CharacterCurrentRuleEntryState.Unavailable,
                UnavailableReason = "current-rule-purchase-canonical-source-unsupported" });
        }
        return CharacterCurrentRuleCatalogValidation.CreateSnapshot(input, entries.AsReadOnly(), [], prompted2742
            ? "chummer.current-purchases-karma-source-text.v3" : "chummer.current-purchases-canonical-grammar.v2");
    }

    private static bool UsesKarmaQualityPrompts2742(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source)
        => UsesNativeLifecycleMechanics9569(current)
            && source is ICurrentKarmaQualityPromptSource2742 { HasKarmaQualityPrompts2742: true };

    private static bool CurrentBlackMarketCategoryMetadata1960(XElement global)
    {
        // Category membership is retained catalog metadata, not a selected
        // discount. Saved/requested discounts still require the unchanged
        // purchase compiler and remain unsupported by this current consumer.
        if (global.HasAttributes || DirectSkillText1949(global) || global.Elements("categories").Count() != 1
            || DirectSkillText1949(global.Element("categories")!)) return false;
        var categories = global.Element("categories")!.Elements("category").ToArray();
        if (categories.Any(row => string.IsNullOrWhiteSpace(row.Value) || row.Value != row.Value.Trim()
                || row.Value.Any(char.IsControl) || row.Attributes().Any(attribute => attribute.Name.Namespace != XNamespace.None))
            || categories.Select(row => row.Value).Distinct(StringComparer.Ordinal).Count() != categories.Length) return false;
        foreach (var metadata in categories.Attributes("blackmarket"))
        {
            string[] names = metadata.Value.Split(',');
            if (names.Any(name => name.Length == 0 || name != name.Trim() || name.Any(char.IsControl))
                || names.Distinct(StringComparer.Ordinal).Count() != names.Length) return false;
        }
        return true;
    }

    public static CharacterCurrentRuleDomainExecution? ExecutePurchases(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!current.IsUnchanged || !catalog.IsComplete || catalog.Input.ContextDigest != current.BindingDigest) return null;
        XElement? character = ParsePurchaseXml(current.CharacterXml);
        if (character is null || character.Name != "character") return null;
        if (UsesNativeMethods1958(current) && current.Stage == CharacterCurrentRuleStage.Creation
            && source.TryResolveCreationSourceProfile(out var nativeProfile))
            return nativeProfile.BuildMethod == CharacterCreationBuildMethods.Karma
                ? ExecuteNativeKarmaPurchases1958(current, catalog, source)
                : ExecuteNativeLifeModulePurchases1958(current, catalog, source);
        if (catalog.Domain == CharacterCurrentRuleDomain.Gear)
            return current.Stage == CharacterCurrentRuleStage.Creation
                ? ExecuteCurrentCreationGear(current, catalog, source, character)
                : ExecuteCurrentCareerGear(current, catalog, source, character, cancellationToken);
        if (catalog.Domain != CharacterCurrentRuleDomain.Qualities) return null;
        return current.Stage == CharacterCurrentRuleStage.Creation
            ? ExecuteCurrentCreationQualities(current, catalog, source, character)
            : ExecuteCurrentCareerQualities(current, catalog, source, cancellationToken);
    }

    private static CharacterCurrentRuleDomainExecution? ExecuteCurrentCreationGear(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, XElement character)
    {
        var auxiliary = current.Current.Document.AuxiliaryState;
        var draft = auxiliary.CharacterCreationGearDraft;
        if (!CurrentCreationPrerequisiteGrantsHaveNoGear1954(current, source)
            || auxiliary.CharacterCreationQualitiesDraft?.Selections
                ?.Any(quality => ParsePurchaseXml(quality.SourceNodeXml)?.Descendants("addgear").Any() == true) == true)
            return null; // Explicit pending grant dependency; never a NotConfigured shortcut.
        // Saved canonical gear and a pending draft are different input representations.
        // Imported Creation possessions need their own source-backed reconciliation.
        if (character.Elements("gears").Elements().Any() || draft?.CharacterEffectsApplied == true
            || !source.TryResolveCreationGearAuthority(out var authority) || !CharacterCreationGearRules.IsValidAuthority(authority))
            return null;
        var selections = draft?.Lines.Select(line => new CharacterCreationGearSelection(line.OptionId, line.Quantity)).ToArray() ?? [];
        var selected = new List<string>();
        foreach (var selection in selections)
        {
            var option = authority.Options.SingleOrDefault(option => option.OptionId == selection.OptionId);
            if (option is null || !TryAvailablePurchaseEntry(catalog, option.SourceId, out string key)) return null;
            selected.Add(key);
        }
        decimal funds = auxiliary.CharacterCreationResourcesDraft?.Budget.TotalStartingNuyen ?? 0m;
        if (selections.Length != 0 && auxiliary.CharacterCreationResourcesDraft is null) return null;
        if (!CharacterCreationGearRules.TryProjectBasket(selections, authority, funds, out var lines, out var budget, out _)) return null;
        return IssueRuleEffects2912(current, catalog, new(current, catalog, draft is null ? CharacterCurrentRuleConsumptionState.NotConfigured
            : CharacterCurrentRuleConsumptionState.Evaluated, selected,
            CharacterCreationFinalizationDigest.Compute(new { authority.AuthorityDigest, Lines = lines, Budget = budget }), []), draft is null ? null : TagRuleEffects2912(new object(), CharacterRuleEffectValues2912.Gear(lines, budget), "Purchase values do not establish equipped effects or capacity totals."));
    }

    private static bool CurrentCreationPrerequisiteGrantsHaveNoGear1954(
        CharacterCurrentRuleEnvironmentContext current, ICharacterSourceDataContext source)
    {
        if (UsesNativeMethods1958(current))
        {
            // Native grants are resolved from the genuine selected method state;
            // absence of a Priority ledger is never an absence proof here.
            return TryCurrentNativeGrants1958(current, source, out _);
        }
        var original = current.Current.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft;
        // Absence means only that this ledger contributes no pending grants. It
        // does not admit other creation methods: ValidateCreationAuthority1949's
        // mandatory Profile/Origins consumers reject Karma and Life Modules.
        if (original is null) return true;

        // This ledger owns Priority/Sum-to-Ten selections. Karma's purchased talent
        // and Life Modules' finalization request are different contracts; neither
        // may be substituted for a Priority talent or synthesized to finish a runner.
        // Re-evaluate the actual saved choices against the retained current profile:
        // historical quality names/XML are not current grant authority.
        if (original.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
            || original.WorkspaceId != current.Current.Id || original.BaseContentRevision <= 0
            || original.BaseContentRevision >= current.Current.ContentRevision
            || original.BaseRawCharacterXmlDigest != CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(current.CharacterXml)
            || !source.TryResolveCreationPrerequisiteAuthority(out var prerequisite)
            || !CharacterCreationPrerequisiteService.TryEvaluateCurrentSelection1949(prerequisite, original, out var evaluated, out _)
            || evaluated is null || evaluated.TalentSelection is not { } selected) return false;

        // The evaluated heritage payload is freshly resolved too. Do not trust the
        // original ledger's cached racial sources or silently omit their gear.
        if ((evaluated.HeritageSelection?.RacialQualitySources ?? []).Any(quality => !CurrentPurchaseGrantHasNoGear1954(quality)))
            return false;
        if (selected.GrantedQualities.Count == 0) return true;
        if (!CharacterCreationTalentQualityGrants.TryResolveTalent(evaluated, source, out _, out var talent))
            return false;
        return talent.GrantedQualitySources is { } sources && sources.All(CurrentPurchaseGrantHasNoGear1954);
    }

    private static bool CurrentPurchaseGrantHasNoGear1954(CharacterCreationTalentQualitySource quality)
        => CharacterCreationTalentQualitySourceRules.IsValidSource(quality)
            && quality.GrantedGearSources is not { Count: > 0 }
            && ParsePurchaseXml(quality.CanonicalSourceXml) is { } xml
            && !xml.Descendants().Any(node => node.Name.LocalName is "addgear" or "freegear");

    private static CharacterCurrentRuleDomainExecution? ExecuteCurrentCareerGear(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, XElement character, CancellationToken cancellationToken)
    {
        if (!source.TryResolveCareerSettingsProfile(out string settings, out _)
            || character.Elements("gears").Count() > 1
            || character.Elements("gears").Elements().Any(node => node.Name != "gear")) return null;
        var outcomes = new List<object>();
        var derived = new List<CharacterRuleEffectValue>();
        var selected = new List<string>();
        CurrentCareerEffects1960? native = null;
        if (UsesNativeMethods1958(current) || UsesOwnedPriorityCareer9569(current))
        {
            native = CompileCurrentCareerEffects1960(current, source, []);
            if (native is null) return null;
            outcomes.Add(new { NativeSourceGraph = native.Digest });
        }
        foreach (var node in character.Elements("gears").Elements("gear"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Membership comes from the freshly compiled owner/source graph,
            // whose exact instance, unique GUID/count and parent were reconciled.
            // A saved parent marker or hidden/free catalog row alone is insufficient.
            if (native?.GrantedGearIds.Contains(node.Element("guid")?.Value ?? string.Empty, StringComparer.Ordinal) == true)
            {
                if (!Guid.TryParseExact(node.Element("sourceid")?.Value, "D", out var grantId)
                    || !TryAvailablePurchaseEntry(catalog, grantId, out string grantKey)) return null;
                selected.Add(grantKey);
                outcomes.Add(new { NativeGrantedInstance = node.ToString(SaveOptions.DisableFormatting), CatalogKey = grantKey });
                continue;
            }
            if (!Guid.TryParse(node.Element("sourceid")?.Value, out var id)
                || !Guid.TryParse(node.Element("guid")?.Value, out var instance)
                || !int.TryParse(node.Element("rating")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int rating)
                || !decimal.TryParse(node.Element("qty")?.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal quantity)
                || node.Elements("children").Any(child => child.HasElements)
                || !TryAvailablePurchaseEntry(catalog, id, out string key)
                || !source.TryResolveCareerGearPurchaseSource(id, out var item) || item is null) return null;
            var request = new CharacterCareerGearPurchaseRequest(current.Current.Id, Guid.Empty, id, rating,
                quantity, DateTime.UnixEpoch);
            string projectedXml, legality;
            decimal unit, total;
            int availability;
            string? weaponAuthority = null;
            string? focusAuthority = null;
            XElement[] focusEffects = [];
            if (ParsePurchaseXml(item.SourceXml)?.Element("ammoforweapontype") is not null)
            {
                if (!TryCurrentRetainedAmmunition(current, source, item, out var ammunition)
                    || !ammunition!.TryProjectSaved(node, request, settings, instance,
                        out projectedXml, out unit, out total, out availability, out legality)) return null;
                weaponAuthority = ammunition.WeaponAuthorityDigest;
            }
            else if (CharacterCreationLegacySourceProjector.TryReadRetainedCareerFocus6511(item, out var retainedFocus))
            {
                if (!retainedFocus!.TryProjectSaved(character, node, request, settings, instance,
                    out projectedXml, out unit, out total, out availability, out legality, out focusEffects)) return null;
                focusAuthority = retainedFocus.SourceDigest;
            }
            else if (!CharacterCreationLegacySourceProjector.TryBuildCareerPurchasedGear(item, request, settings, instance,
                out projectedXml, out unit, out total, out availability, out legality, out _)) return null;
            var projected = ParsePurchaseXml(projectedXml);
            if (projected is null || node.HasAttributes
                || node.Elements().GroupBy(child => child.Name).Any(group => group.Count() != 1)
                || node.Elements().Any(child => child.Name != "children" && !projected.Elements(child.Name).Any())
                || node.Element("discountcost") is { } discount
                    && (!bool.TryParse(discount.Value, out bool discounted) || discounted)) return null;
            selected.Add(key);
            derived.AddRange(CharacterRuleEffectValues2912.Purchase(instance.ToString("D"),
                catalog.Entries.Single(entry => entry.Key == key).Name, unit, total, quantity, availability, legality));
            if (focusAuthority is not null)
            {
                outcomes.Add(new { Saved = node.ToString(SaveOptions.DisableFormatting), Unit = unit, Total = total,
                    Availability = availability, Legality = legality, item.CatalogDigest, item.ProfileDigest, item.CustomDataDigest,
                    FocusAuthority = focusAuthority, Effects = focusEffects.Select(e => e.ToString(SaveOptions.DisableFormatting)).ToArray() });
                if (!TryRetainedFocusValues6511(character, node, settings, focusEffects, out var focusValues)) return null;
                derived.AddRange(focusValues);
            }
            else if (weaponAuthority is null)
                outcomes.Add(new { Saved = node.ToString(SaveOptions.DisableFormatting), Unit = unit, Total = total,
                    Availability = availability, Legality = legality, item.CatalogDigest, item.ProfileDigest, item.CustomDataDigest });
            else
                outcomes.Add(new { Saved = node.ToString(SaveOptions.DisableFormatting), Unit = unit, Total = total,
                    Availability = availability, Legality = legality, item.CatalogDigest, item.ProfileDigest, item.CustomDataDigest,
                    WeaponAuthority = weaponAuthority });
        }
        return IssueRuleEffects2912(current, catalog, new(current, catalog, CharacterCurrentRuleConsumptionState.Evaluated, selected,
            CharacterCreationFinalizationDigest.Compute(new { Settings = settings, Outcomes = outcomes }), []), TagRuleEffects2912(new object(), derived, "Current retained-source replacement prices; not historical spend, a refund or an account balance. Granted gear and capacity/equipped totals require their own canonical output."));
    }

    private static CharacterCurrentRuleDomainExecution? ExecuteCurrentCreationQualities(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, XElement character)
    {
        if (UsesNativeMethods1958(current))
        {
            if (character.Elements("qualities").Elements().Any()
                || !TryCurrentNativeGrants1958(current, source, out var grants)) return null;
            var keys = new List<string>();
            foreach (var grant in grants)
            {
                if (!Guid.TryParse(grant.SourceId, out var id) || !TryAvailablePurchaseEntry(catalog, id, out string key)) return null;
                keys.Add(key);
            }
            XElement[] qualities = [], improvements = [];
            if (grants.Count != 0 && !CharacterCreationPriorityRacialQualities.TryProjectCurrentNativeSkillCosts(grants,
                    current.BindingDigest, out qualities, out improvements)) return null;
            return new(current, catalog, grants.Count == 0 ? CharacterCurrentRuleConsumptionState.NotConfigured
                : CharacterCurrentRuleConsumptionState.Evaluated, keys,
                CharacterCreationFinalizationDigest.Compute(new { MethodGrants = grants,
                    Qualities = qualities.Select(row => row.ToString(SaveOptions.DisableFormatting)).ToArray(),
                    Improvements = improvements.Select(row => row.ToString(SaveOptions.DisableFormatting)).ToArray(),
                    RacialPurchaseKarma = 0 }), []);
        }
        var auxiliary = current.Current.Document.AuxiliaryState;
        var draft = auxiliary.CharacterCreationQualitiesDraft;
        if (character.Elements("qualities").Elements().Any() || draft?.CharacterEffectsApplied == true
            || !TryCurrentQualityAuthority(current, source, out var authority) || !authority.IsAuthoritative
            || authority.Blockers.Count != 0) return null;
        if (draft is null && authority.GrantedQualities.Count == 0)
            return new(current, catalog, CharacterCurrentRuleConsumptionState.NotConfigured, [],
                CharacterCreationFinalizationDigest.Compute(new { authority.AuthorityDigest, Selection = "absent-original-draft-and-xml" }), []);
        var prerequisite = auxiliary.CharacterCreationPrerequisiteDraft;
        var attributes = auxiliary.CharacterCreationAttributesDraft;
        if (prerequisite is null || attributes is null) return null;
        var selected = new List<string>();
        foreach (string optionId in draft?.SelectedOptionIds ?? [])
        {
            var option = authority.Options.SingleOrDefault(option => option.OptionId == optionId);
            if (option is null || !TryAvailablePurchaseEntry(catalog, option.SourceId, out string key)) return null;
            selected.Add(key);
        }
        foreach (var grant in authority.GrantedQualities)
        {
            if (!TryAvailablePurchaseEntry(catalog, grant.SourceId, out string grantKey)) return null;
            selected.Add(grantKey);
        }
        var binding = new CharacterCreationQualitiesBinding(current.Current.Id, current.Current.ContentRevision,
            current.Current.SavedRevision, CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(current.CharacterXml),
            current.Current.Document.AuxiliaryStateDigest, prerequisite.DraftRevision, prerequisite.DraftDigest,
            attributes.DraftRevision, attributes.DraftDigest, current.Current.Document.RulesetId,
            prerequisite.BuildMethod, false, prerequisite.CreationKarmaTotal, attributes.CreationKarmaUsed,
            authority.AuthorityDigest, authority.RuntimeDigest);
        var outcome = CharacterCreationQualitiesRules.Evaluate(new(binding, authority, draft?.SelectedOptionIds ?? []));
        if (outcome.Blockers.Count != 0) return null;
        return IssueRuleEffects2912(current, catalog, new(current, catalog, CharacterCurrentRuleConsumptionState.Evaluated, selected,
            CharacterCreationFinalizationDigest.Compute(outcome), []), TagRuleEffects2912(outcome, CharacterRuleEffectValues2912.Qualities(outcome), "Quality selection costs are calculated; their owned sheet effects are reported by the sheet consumer."));
    }

    private static CharacterCurrentRuleDomainExecution? ExecuteCurrentCareerQualities(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, CancellationToken cancellationToken)
    {
        if (!source.TryResolveCareerQualitySettings(out var settings, out string settingsState)) return null;
        string ordinaryQualityXml = current.CharacterXml;
        var outcomes = new List<object>();
        var derived = new List<CharacterRuleEffectValue>();
        var selected = new List<string>();
        CurrentCareerEffects1960? native = null;
        int? qualityArmCount = null;
        if (UsesNativeMethods1958(current) || UsesOwnedPriorityCareer9569(current))
        {
            native = CompileCurrentCareerEffects1960(current, source, []);
            if (native is null) return null;
            outcomes.Add(new { NativeSourceGraph = native.Digest });
            if (native.FinalizedModuleOwners.Count != 0)
            {
                // The authenticated archive owns module and dependent-quality rows.
                // Evaluate ordinary purchases in a detached view, preserving the
                // strict Positive/Negative parser and the original saved graph.
                var ordinary = ParsePurchaseXml(current.CharacterXml);
                if (ordinary?.Element("qualities") is not { } qualities) return null;
                foreach (var module in native.FinalizedModuleOwners)
                {
                    var rows = qualities.Elements("quality").Where(row =>
                        row.Element("guid")?.Value == module.OwnerId.ToString("D")).ToArray();
                    if (rows is not [var row]
                        || row.Element("sourceid")?.Value != module.SourceId.ToString("D")) return null;
                    row.Remove();
                    outcomes.Add(module);
                }
                ordinaryQualityXml = ordinary.ToString(SaveOptions.DisableFormatting);
            }
        }
        if (native?.Imported is { } imported)
        {
            var ordinary = ParsePurchaseXml(ordinaryQualityXml);
            if (ordinary is null) return null;
            foreach (var grant in imported.GrantedQualities)
            {
                var rows = ordinary.Elements("qualities").Elements("quality").Where(row =>
                    row.Element("guid")?.Value == grant.OwnerId.ToString("D")
                    && row.Element("sourceid")?.Value == grant.SourceId.ToString("D")).ToArray();
                if (rows is not [var row] || !TryAvailablePurchaseEntry(catalog, grant.SourceId, out string grantKey)) return null;
                selected.Add(grantKey);
                outcomes.Add(new { GrantedOwner = grant.OwnerId, grant.SourceId, CatalogKey = grantKey });
                row.Remove();
            }
            ordinaryQualityXml = ordinary.ToString(SaveOptions.DisableFormatting);
        }
        if (!CharacterCareerQualityGraph.TryReadIdentities(ordinaryQualityXml, out var identities)) return null;
        foreach (var identity in identities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (native?.NativeQualityIds.Contains(identity.SourceId.ToString("D"), StringComparer.Ordinal) == true
                || native?.Imported?.GrantedQualities.Any(grant => grant.OwnerId == identity.InternalId
                    && grant.SourceId == identity.SourceId) == true)
            {
                if (!TryAvailablePurchaseEntry(catalog, identity.SourceId, out string grantKey)) return null;
                selected.Add(grantKey);
                continue; // Reconciled selected grant; no Career purchase/refund is invented.
            }
            if (!TryAvailablePurchaseEntry(catalog, identity.SourceId, out string key)
                || !source.TryResolveCareerQualitySource(identity.SourceId, out var item) || item is null) return null;
            var qualityDefinition = ParsePurchaseXml(item.RawSourceXml);
            if (qualityDefinition is null) return null;
            if (qualityArmCount is null && qualityDefinition.Element("nolevels") is null
                && qualityDefinition.Element("limit") is { HasAttributes: false, HasElements: false } limit
                && string.Equals(limit.Value, "{arm} - 1", StringComparison.Ordinal))
            {
                if (!TryResolveCurrentCareerQualityAnatomy1960(current.CharacterXml, source,
                    out int arms, out string anatomyDigest)) return null;
                qualityArmCount = arms;
                outcomes.Add(new { AnatomyDigest = anatomyDigest, ResolvedArmCount = arms });
            }
            if (!CharacterCareerQualityGraph.TryProject(ordinaryQualityXml, item, identity,
                CharacterCareerQualityOperation.RemoveLevel, qualityArmCount, out var graph,
                importedCareerEffects5698: native?.Imported is not null) || graph is null) return null;
            if (!CharacterCareerQualityRules.TryCalculateOrdinaryCurrentPrice(
                    CharacterCareerQualityOperation.RemoveLevel, graph.Definition, settings, graph.InstancesBefore.Length,
                    out int ruleKarmaCost, out int karmaDelta)) return null;
            selected.Add(key);
            outcomes.Add(new { Graph = graph, RuleKarmaCost = ruleKarmaCost, KarmaDelta = karmaDelta });
            string label = catalog.Entries.Single(entry => entry.Key == key).Name;
            derived.Add(CharacterRuleEffectValues2912.Number(identity.ToString(), label, CharacterRuleEffectMetric.Rating, graph.InstancesBefore.Length));
            derived.Add(CharacterRuleEffectValues2912.Number(identity.ToString(), label, CharacterRuleEffectMetric.RuleKarmaCost, ruleKarmaCost, "remove-level"));
            derived.Add(CharacterRuleEffectValues2912.Number(identity.ToString(), label, CharacterRuleEffectMetric.KarmaDelta, karmaDelta, "remove-level"));
        }
        return IssueRuleEffects2912(current, catalog, new(current, catalog, CharacterCurrentRuleConsumptionState.Evaluated, selected,
            CharacterCreationFinalizationDigest.Compute(new { settings, settingsState, Outcomes = outcomes }), []), TagRuleEffects2912(new object(), derived, "Ordinary quality removal quotes only; grants do not imply a purchase or refund."));
    }

    internal static bool CanQuoteCurrentOrdinaryQuality1965(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, CharacterCareerQualityIdentity identity)
    {
        if (!UsesNativeMethods1958(current) && !UsesOwnedPriorityCareer9569(current)) return true;
        var native = CompileCurrentCareerEffects1960(current, source, []);
        // Source matching also excludes bulk removal or an acquisition targeting
        // a dependent module quality through a different instance identity.
        return native is not null && !native.FinalizedModuleOwners.Any(owner =>
            owner.OwnerId == identity.InternalId || owner.SourceId == identity.SourceId)
            && native.Imported?.GrantedQualities.Any(owner => owner.OwnerId == identity.InternalId
                || owner.SourceId == identity.SourceId) != true;
    }

    internal static CharacterCreationQualitiesPreview? QuoteCurrentCreationQualities(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, IReadOnlyList<string> selectedOptionIds)
    {
        if (current.Stage != CharacterCurrentRuleStage.Creation || !current.IsUnchanged
            || !catalog.IsComplete || catalog.Domain != CharacterCurrentRuleDomain.Qualities) return null;
        var auxiliary = current.Current.Document.AuxiliaryState;
        var prerequisite = auxiliary.CharacterCreationPrerequisiteDraft;
        var attributes = auxiliary.CharacterCreationAttributesDraft;
        if (prerequisite is null || attributes is null
            || !TryCurrentQualityAuthority(current, source, out var authority) || !authority.IsAuthoritative) return null;
        foreach (string optionId in selectedOptionIds)
        {
            var option = authority.Options.SingleOrDefault(option => option.OptionId == optionId);
            if (option is null || !TryAvailablePurchaseEntry(catalog, option.SourceId, out _)) return null;
        }
        var binding = new CharacterCreationQualitiesBinding(current.Current.Id, current.Current.ContentRevision,
            current.Current.SavedRevision, CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(current.CharacterXml),
            current.Current.Document.AuxiliaryStateDigest, prerequisite.DraftRevision, prerequisite.DraftDigest,
            attributes.DraftRevision, attributes.DraftDigest, current.Current.Document.RulesetId,
            prerequisite.BuildMethod, false, prerequisite.CreationKarmaTotal, attributes.CreationKarmaUsed,
            authority.AuthorityDigest, authority.RuntimeDigest);
        var outcome = CharacterCreationQualitiesRules.Evaluate(new(binding, authority, selectedOptionIds));
        return outcome.Blockers.Count == 0 && current.IsUnchanged ? outcome : null;
    }

    private static bool TryCurrentQualityAuthority(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, out CharacterCreationQualitiesAuthority authority)
    {
        authority = CharacterCreationQualitiesAuthority.Unavailable;
        var prerequisite = current.Current.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft;
        if (prerequisite is not null && UsesPriorityCurrent3027(current)
            && current.Stage == CharacterCurrentRuleStage.Creation)
        {
            // Current racial/anatomy authority must receive the evaluated selection,
            // while its saved provenance and all selected semantics remain exact.
            if (!current.IsUnchanged || !source.TryResolveCreationPrerequisiteAuthority(out var currentAuthority)
                || !CharacterCreationPrerequisiteDraftIntegrity.TryEvaluateCurrentPending3053(
                    prerequisite, current.Current.Id, current.Current.ContentRevision,
                    CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(current.CharacterXml),
                    currentAuthority, source, out var evaluated) || evaluated is null)
                return false;
            return source.TryResolveCreationQualitiesAuthority(evaluated, out authority) && current.IsUnchanged;
        }
        return prerequisite is not null
            ? source.TryResolveCreationQualitiesAuthority(prerequisite, out authority)
            : source.TryResolveCreationQualitiesAuthority(out authority);
    }

    // Profile's cross-domain closure can admit only the exact leaves claimed here,
    // together with this canonical policy observation. Unknown leaves remain its blockers.
    internal static bool IsCurrentPurchaseProfileSetting1950(string parent, string name)
        => parent == "karmacost" ? name == "karmaquality"
            : parent == "setting" && name is "nuyenformat" or "qualitykarmalimit"
                or "exceedpositivequalities" or "exceednegativequalities"
                or "exceedpositivequalitiescostdoubled" or "exceednegativequalitiesnobonus"
                or "exceednegativequalitieslimit" or "dontdoublequalities" or "dontdoublequalityrefunds"
                or "multiplyrestrictedcost" or "multiplyforbiddencost"
                or "restrictedcostmultiplier" or "forbiddencostmultiplier";

    internal static bool TryObserveCurrentPurchaseProfileSettings1950(
        CharacterCurrentRuleEnvironmentContext current, ICharacterSourceDataContext source,
        XElement profile, out string canonicalPolicyDigest)
    {
        canonicalPolicyDigest = string.Empty;
        if (!current.IsUnchanged || profile.Name != "setting" || profile.HasAttributes
            || !source.TryResolveCareerSettingsProfile(out string settings, out string settingsState)
            || !SamePurchaseXml(settings, new XElement("settings", profile.Nodes()).ToString(SaveOptions.DisableFormatting))
            || !CharacterCareerGearPurchaseRules.TryReadPricePolicy(string.Empty, settings, out decimal ordinaryMultiplier,
                out int? nuyenDecimals) || nuyenDecimals is null) return false;
        decimal? restrictedMultiplier = null, forbiddenMultiplier = null;
        if (profile.Element("multiplyrestrictedcost") is not null || profile.Element("restrictedcostmultiplier") is not null)
        {
            if (!CharacterCareerGearPurchaseRules.TryReadPricePolicy("R", settings, out decimal multiplier, out int? precision)
                || precision != nuyenDecimals) return false;
            restrictedMultiplier = multiplier;
        }
        if (profile.Element("multiplyforbiddencost") is not null || profile.Element("forbiddencostmultiplier") is not null)
        {
            if (!CharacterCareerGearPurchaseRules.TryReadPricePolicy("F", settings, out decimal multiplier, out int? precision)
                || precision != nuyenDecimals) return false;
            forbiddenMultiplier = multiplier;
        }
        foreach (var parent in profile.DescendantsAndSelf())
        {
            var owned = parent.Elements().Where(child => IsCurrentPurchaseProfileSetting1950(
                parent.Name.LocalName, child.Name.LocalName)).ToArray();
            if (owned.Any(child => child.Name.Namespace != XNamespace.None || child.HasAttributes || child.HasElements)
                || owned.GroupBy(child => child.Name).Any(group => group.Count() != 1)) return false;
        }
        if (current.Stage == CharacterCurrentRuleStage.Career)
        {
            // Finalized qualities require Career source policy, not Creation's
            // empty-grant accounting authority. Selected grants/effects still
            // have to close independently in the Qualities consumer.
            if (string.IsNullOrWhiteSpace(settingsState)
                || !TryObserveCurrentCareerQualityPolicy2020(current, source, profile, out var qualityPolicy)) return false;
            canonicalPolicyDigest = CharacterCreationFinalizationDigest.Compute(new
            {
                Schema = "chummer.current-career-purchase-profile-policy.v1", current.BindingDigest,
                Settings = settings, SettingsState = settingsState,
                GearPricePolicy = new { ordinaryMultiplier, restrictedMultiplier, forbiddenMultiplier, nuyenDecimals },
                QualityPolicy = qualityPolicy
            });
            return current.IsUnchanged;
        }
        if (!TryCurrentQualityAuthority(current, source, out var qualities)
            || !qualities.IsAuthoritative || qualities.Blockers.Count != 0) return false;
        CharacterCareerQualitySettings? careerQuality = null;
        if ((current.Stage == CharacterCurrentRuleStage.Career
                || profile.Element("dontdoublequalities") is not null || profile.Element("dontdoublequalityrefunds") is not null)
            && !source.TryResolveCareerQualitySettings(out careerQuality, out _)) return false;
        // Per-entry future grammar and unavailable-row confinement remain with the purchase
        // catalog consumer. A Profile policy observation must not force unsupported unused
        // options to be selected or turn their explicit exclusions into global blockers.
        if (!current.IsUnchanged) return false;
        canonicalPolicyDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = "chummer.current-purchase-profile-policy.v1", current.BindingDigest,
            Settings = settings, SettingsState = settingsState,
            GearPricePolicy = new { ordinaryMultiplier, restrictedMultiplier, forbiddenMultiplier, nuyenDecimals },
            Qualities = new { qualities.AuthorityDigest, qualities.QualityKarmaLimit,
                qualities.MayExceedPositiveQualityLimit, qualities.MayExceedNegativeQualityLimit, qualities.CostPolicy },
            CareerQuality = careerQuality
        });
        return true;
    }

    private static bool TryObserveCurrentCareerQualityPolicy2020(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, XElement profile, out string policyDigest)
    {
        var evaluation = RetainedEvaluationFor2764(current, source);
        policyDigest = string.Empty;
        if (current.Stage != CharacterCurrentRuleStage.Career || !current.IsUnchanged
            || !source.TryResolveCreationSourceProfile(out var selectedProfile)
            || !MatchesSettingsRow5691(source, profile, selectedProfile.SettingsProfileId)
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(selectedProfile.RawProfileInputsDigest)
            || !source.TryResolveCareerQualitySettings(out var settings, out var settingsState)
            || string.IsNullOrWhiteSpace(settingsState)
            || settings is not { KarmaQuality: >= 0 and <= CharacterCareerQualityRules.MaximumKarma }
            || profile.Elements("karmacost").ToArray() is not [var costs] || costs.HasAttributes
            || !ReadSkillInt1949(costs, "karmaquality", null, out int karmaQuality) || karmaQuality != settings.KarmaQuality
            || !ReadSkillBool1949(profile, "dontdoublequalities", null, out bool purchases)
            || purchases != settings.DontDoubleQualityPurchases
            || !ReadSkillBool1949(profile, "dontdoublequalityrefunds", null, out bool refunds)
            || refunds != settings.DontDoubleQualityRefunds
            || !ReadSkillInt1949(profile, "qualitykarmalimit", null, out int creationLimit)
            || !ReadSkillBool1949(profile, "exceedpositivequalities", null, out bool exceedPositive)
            || !ReadSkillBool1949(profile, "exceednegativequalities", null, out bool exceedNegative)) return false;
        // Creation-only limits and cost flags remain exact profile inputs; they
        // do not impose a Creation budget on Career purchases or saved grants.
        // Preserve absence of optional flags instead of inventing policy defaults.
        var creationCostFlags = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (string name in new[] { "exceedpositivequalitiescostdoubled", "exceednegativequalitiesnobonus",
            "exceednegativequalitieslimit" })
        {
            if (profile.Element(name) is null) continue;
            if (!ReadSkillBool1949(profile, name, null, out bool value)) return false;
            creationCostFlags.Add(name, value);
        }
        if (!source.TryResolveCareerQualityCatalog(out var catalog) || catalog.Count > 4096
            || catalog.Any(row => row is null)
            || catalog.Select(row => row.SourceId).Distinct().Count() != catalog.Count) return false;
        // Every source query and its admission checks above still run. Reuse only
        // the pure row proof/digest, after the existing private issuer rechecks
        // this exact source, context, thread and mutable bindings.
        string? retainedInputs = null;
        if (evaluation is not null)
        {
            if (!ReferenceEquals(evaluation, RetainedEvaluationFor2764(current, source))) return false;
            // Exact serialized values, not a digest key. This owns nested profile
            // lists without keeping any caller-writable DTO graph.
            retainedInputs = System.Text.Json.JsonSerializer.Serialize(new
            {
                Profile = profile.ToString(SaveOptions.DisableFormatting),
                selectedProfile.SettingsProfileId, selectedProfile.BuildMethod, selectedProfile.RawProfileInputsDigest,
                selectedProfile.EnabledSourcebooks, selectedProfile.BootstrapSourceAnchors,
                Settings = settings, SettingsState = settingsState
            });
            if (evaluation.ProfileQuality6223 is CareerQualityPolicy6223 saved
                && saved.Matches(retainedInputs, catalog))
            {
                if (!ReferenceEquals(evaluation, RetainedEvaluationFor2764(current, source))) return false;
                policyDigest = saved.Digest;
                return true;
            }
        }
        if (catalog.Count > 0)
        {
            string sourceDigest = catalog[0].SourceInputsDigest, customDigest = catalog[0].CustomDataInputsDigest;
            if (!CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(sourceDigest)
                || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(customDigest)) return false;
            foreach (var row in catalog)
            {
                if (row.SourceId == Guid.Empty || row.ProfileInputsDigest != selectedProfile.RawProfileInputsDigest
                    || row.SourceInputsDigest != sourceDigest || row.CustomDataInputsDigest != customDigest
                    || !selectedProfile.EnabledSourcebooks.Contains(row.SourceBook, StringComparer.OrdinalIgnoreCase)
                    || ParsePurchaseXml(row.RawSourceXml) is not { Name: var name } xml || name != "quality" || xml.HasAttributes
                    || !Guid.TryParseExact(SkillScalar1949(xml, "id"), "D", out var id) || id != row.SourceId
                    || SkillScalar1949(xml, "name") != row.Name || SkillScalar1949(xml, "source") != row.SourceBook) return false;
            }
        }
        // Source identity is not eligibility: unsupported unused rows stay with
        // the purchase catalog, and authentic saved grant graphs with its executor.
        // BindingDigest includes the current revisions, edition, sourcebooks and
        // ordered custom sources; the retained resolver rejects source-byte drift.
        // It also binds the retained archive's exact dependency bytes. Successful
        // resolution hashes quality inputs before filtering, so an empty catalog
        // retains source authority without fabricating first-row source digests.
        policyDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = "chummer.current-career-quality-profile-policy.v1", current.BindingDigest,
            selectedProfile.SettingsProfileId, selectedProfile.BuildMethod, selectedProfile.RawProfileInputsDigest,
            selectedProfile.EnabledSourcebooks, selectedProfile.BootstrapSourceAnchors,
            Settings = settings, SettingsState = settingsState,
            CreationOnly = new { creationLimit, exceedPositive, exceedNegative, CostFlags = creationCostFlags },
            CatalogCount = catalog.Count,
            Sources = catalog.OrderBy(row => row.SourceId).ToArray()
        });
        if (!current.IsUnchanged) return false;
        if (evaluation is not null)
        {
            if (!ReferenceEquals(evaluation, RetainedEvaluationFor2764(current, source))) return false;
            evaluation.ProfileQuality6223 = new CareerQualityPolicy6223(retainedInputs!, catalog, policyDigest);
        }
        return true;
    }

    private sealed class CareerQualityPolicy6223
    {
        private readonly string inputs;
        // These sealed records contain only Guid/string values. Copy the list;
        // no source-owned collection or mutable XML escapes into the memo.
        private readonly CharacterCareerQualitySource[] sources;
        internal string Digest { get; }

        internal CareerQualityPolicy6223(string inputs, IReadOnlyList<CharacterCareerQualitySource> sources,
            string digest)
        {
            this.inputs = inputs;
            this.sources = sources.ToArray();
            Digest = digest;
        }

        internal bool Matches(string candidateInputs, IReadOnlyList<CharacterCareerQualitySource> candidateSources)
            => inputs == candidateInputs && sources.SequenceEqual(candidateSources);
    }


    internal static bool TryAvailablePurchaseEntry(CharacterCurrentRuleCatalogValidation catalog, Guid id, out string key)
    {
        key = string.Empty;
        var rows = catalog.Entries.Where(entry => entry.Key.EndsWith("/id:" + id.ToString("D"), StringComparison.Ordinal)).ToArray();
        if (rows.Length != 1 || rows[0].State != CharacterCurrentRuleEntryState.AvailableNotSelected) return false;
        key = rows[0].Key;
        return true;
    }

    private static bool TryCurrentRetainedAmmunition(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, CharacterCareerGearPurchaseSource item,
        out CharacterCreationLegacySourceProjector.RetainedCareerAmmunition? ammunition)
    {
        ammunition = null;
        if (current.Current.Document.RulesetId != "sr5"
            || !CharacterCreationLegacySourceProjector.HasRetainedCareerAmmunitionSourceShape(item)) return false;
        var evaluation = RetainedEvaluationFor2764(current, source);
        if (evaluation is null)
            return source.TryResolveCreationLoadoutAuthority(out var ordinary)
                && CharacterCreationLegacySourceProjector.TryReadRetainedCareerAmmunition(item, ordinary, out ammunition);
        // Keep the fresh source query and current-binding checks on every row.
        // Only the privately issued, detached loadout validation is shared with
        // the other domains in this evaluation; no write or purchase is admitted.
        if (!TryCurrentLoadoutAuthority6218(current, source, out var loadout)
            || !ReferenceEquals(evaluation, RetainedEvaluationFor2764(current, source))
            || evaluation.Loadout6218 is not { } validated
            || !ReferenceEquals(loadout, validated.Authority)) return false;
        var categories = evaluation.AmmunitionCategories7961;
        if (categories is null || !categories.Matches(validated)) categories = new(validated);
        if (!CharacterCreationLegacySourceProjector.TryReadRetainedCareerAmmunition(item, validated, categories, out ammunition))
            return false;
        evaluation.AmmunitionCategories7961 = categories;
        return true;
    }

    private static bool ValidateCareerGearGrammar(CharacterCurrentRuleEnvironmentContext current,
        CharacterCareerGearPurchaseSource item, string settings,
        ref CharacterCreationLegacySourceProjector.CareerGearPricePolicyEvaluation? pricePolicy, XElement xml)
    {
        if (!observeCareerGearGrammarAllocations4559)
            return ValidateCareerGearGrammarCore4559(current, item, settings, ref pricePolicy, xml);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        try { return ValidateCareerGearGrammarCore4559(current, item, settings, ref pricePolicy, xml); }
        finally
        {
            careerGearGrammarAllocatedBytes4559 += GC.GetAllocatedBytesForCurrentThread() - allocated;
        }
    }

    private static bool ValidateCareerGearGrammarCore4559(CharacterCurrentRuleEnvironmentContext current,
        CharacterCareerGearPurchaseSource item, string settings,
        ref CharacterCreationLegacySourceProjector.CareerGearPricePolicyEvaluation? pricePolicy, XElement xml)
    {
        if (!int.TryParse(xml.Element("rating")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int maximum)
            || maximum < 0 || maximum > CharacterCreationLegacySourceProjector.MaximumCareerGearCatalogRatingChoices) return false;
        int minimum = maximum == 0 ? 0 : 1;
        if (xml.Element("minrating") is { } min
            && (!int.TryParse(min.Value, NumberStyles.None, CultureInfo.InvariantCulture, out minimum) || minimum < 0)) return false;
        if (minimum > maximum) return false;
        pricePolicy ??= new(settings);
        var row = new CharacterCreationLegacySourceProjector.CareerGearRowEvaluation(item);
        for (int rating = minimum; rating <= maximum; rating++)
        {
            var probe = new CharacterCareerGearPurchaseRequest(current.Current.Id, Guid.Empty, item.SourceId,
                rating, 1m, DateTime.UnixEpoch);
            if (!pricePolicy.TryBuildPurchasedGear(row, probe,
                    item.SourceId, out _, out _, out _, out _, out _, out _)) return false;
        }
        return true;
    }

    internal static XElement? ParsePurchaseXml(string xml)
        => ParsePurchaseXml(xml, ignoreWhitespace: false);

    private static XElement? ParsePurchaseXml(string xml, bool ignoreWhitespace)
    {
        try
        {
            using var reader = CreateCurrentRuleXmlReader(xml, new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                    MaxCharactersInDocument = 16 * 1024 * 1024, IgnoreWhitespace = ignoreWhitespace });
            return XElement.Load(reader);
        }
        catch (XmlException) { return null; }
    }

    private static bool SamePurchaseXml(string left, string right)
        // Typed grants use XElement.Parse(..., LoadOptions.None); retained catalog rows can still
        // contain indentation from their XmlReader. Normalize only this structural comparison.
        => ParsePurchaseXml(left, ignoreWhitespace: true) is { } first
            && (string.Equals(left, right, StringComparison.Ordinal)
                || ParsePurchaseXml(right, ignoreWhitespace: true) is { } second && XNode.DeepEquals(first, second));
}
