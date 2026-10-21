using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        // Exact pre1008 compiler, with only the entrypoint and cache renamed.
        // Keep this cache separate from the current compiler on the same sources.
        private JsonElement? _creationLifestylesPre1008AuthoritySnapshot;

        public bool TryResolveCreationLifestylesPre1008Authority(
            out CharacterCreationLifestylesAuthority authority)
        {
            using IDisposable sourceInputScope = _sourceInputs.Enter();
            authority = CharacterCreationLifestylesAuthority.Unavailable;
            if (string.IsNullOrWhiteSpace(_settingsProfileId)
                || (!CharacterCreationBuildMethods.IsSupported(_prerequisiteBuildMethod)
                    && _buildMethod is not (CharacterCreationBuildMethods.Karma or CharacterCreationBuildMethods.LifeModules))
                || !TryComputeEffectiveInputDigest(_catalog, "lifestyles.xml", out string sourceDigest)
                || !TryComputeEffectiveInputDigest(_catalog, SettingsFile, out string settingsDigest)
                || !TryHasSelectedCustomDataInputFor(_customDirectories, "lifestyles.xml", out _))
            {
                return false;
            }

            bool profileMatches = string.Equals(
                BindSelectedProfile(settingsDigest, _settingsProfileId),
                _rawProfileInputsDigest, StringComparison.Ordinal);
            // Admit the actual saved owners against live quality inputs before a
            // cached lifestyle projection can be reused. The ordinary quality
            // catalog intentionally cannot authorize an existing saved graph.
            XElement[] improvements = _character.Elements("improvements")
                .SelectMany(container => container.Elements("improvement")).ToArray();
            bool qualitySourcesExact = TryResolveSavedLifestyleQualitySources(improvements,
                out var qualityInstances, out string qualityInputsDigest, out string? qualitySourceBlocker);
            CharacterCreationLifestyleImprovementRules.TryResolveSourceBackedCosts(improvements,
                qualityInstances, 0, out int trustFundLevel, out decimal metatypeCostPercent,
                out var purchasedQualityCosts, out var effectBlockers);
            JsonElement? cached;
            lock (_completionProjectionSync) { cached = _creationLifestylesPre1008AuthoritySnapshot; }
            if (!_sourceInputs.HasSourceDrift && profileMatches && qualitySourcesExact
                && effectBlockers.Length == 0 && cached is { } snapshot)
            {
                var detached = snapshot.Deserialize<CharacterCreationLifestylesAuthority>();
                if (!_sourceInputs.HasSourceDrift && detached is not null)
                {
                    authority = detached;
                    return true;
                }
            }
            // The context owns one exact character/settings/source snapshot.
            // Reuse only after live-byte admission above, and return detached
            // collections. Drift still produces the existing blocked projection.
            if (!TryResolveTarget(
                    SettingsFile,
                    ["settings"],
                    "setting",
                    _settingsProfileId,
                    string.Empty,
                    out XElement? settings)
                || settings is null
                || !TryEnumerateTargets(
                    "lifestyles.xml",
                    ["lifestyles"],
                    "lifestyle",
                    out XElement[] lifestyleRows)
                || !TryEnumerateTargets(
                    "lifestyles.xml",
                    ["qualities"],
                    "quality",
                    out XElement[] qualityRows)
                || !TryEnumerateTargets(
                    "lifestyles.xml",
                    ["comforts"],
                    "comfort",
                    out XElement[] comfortRows)
                || !TryEnumerateTargets(
                    "lifestyles.xml",
                    ["neighborhoods"],
                    "neighborhood",
                    out XElement[] areaRows)
                || !TryEnumerateTargets(
                    "lifestyles.xml",
                    ["securities"],
                    "security",
                    out XElement[] securityRows))
            {
                return false;
            }

            var blockers = new List<string>();
            if (_sourceInputs.HasSourceDrift)
                blockers.Add(CharacterCreationLifestylesBlockers.AuthorityUnavailable);
            if (!profileMatches)
            {
                blockers.Add(CharacterCreationLifestylesBlockers.AuthorityUnavailable);
            }

            string? rawFreeGrids = settings.Element("allowfreegrids")?.Value.Trim();
            bool freeGridsFromSetting = ParseBool(rawFreeGrids);
            bool freeGridsFromBook = _enabledSourcebooks.Contains("HT");
            bool freeGridsEnabled = freeGridsFromSetting || freeGridsFromBook;
            var freeGridsActivation = new CharacterCreationFreeGridsActivation(rawFreeGrids, freeGridsFromBook,
                freeGridsFromSetting ? CharacterCreationFreeGridsActivationReason.Setting
                    : freeGridsFromBook ? CharacterCreationFreeGridsActivationReason.Sourcebook
                    : CharacterCreationFreeGridsActivationReason.Disabled,
                CharacterCreationFinalizationDigest.Compute(settings.ToString(SaveOptions.DisableFormatting)), sourceDigest);
            blockers.AddRange(effectBlockers);
            if (!qualitySourcesExact)
                blockers.Add(qualitySourceBlocker ?? CharacterCreationLifestylesBlockers.AuthorityUnavailable);

            var qualities = new List<CharacterCreationLifestyleQualityCatalogOption>();
            var exactQualityCosts1000 = new HashSet<Guid>();
            foreach (XElement row in qualityRows.OrderBy(
                         item => ReadValue(item, "id"),
                         StringComparer.Ordinal))
            {
                if (!Guid.TryParse(ReadValue(row, "id"), out Guid sourceId)
                    || sourceId == Guid.Empty)
                {
                    continue;
                }
                string name = ReadValue(row, "name");
                string category = ReadValue(row, "category");
                string qualityType = ResolveLifestyleQualityType(category);
                string sourceBook = ReadValue(row, "source");
                string page = ReadValue(row, "page");
                bool sourceEnabled = !string.IsNullOrWhiteSpace(sourceBook)
                    && _enabledSourcebooks.Contains(sourceBook);
                bool implemented = !row.Elements("implemented").Any()
                    || ParseBool(ReadValue(row, "implemented"));
                bool careerOnly = ParseBool(ReadValue(row, "careeronly"));
                bool hasRequirements = row.Elements("required").Any()
                    || row.Elements("forbidden").Any();
                bool hasUnboundedPrompt = row.Descendants().Any(element =>
                    element.Name.LocalName.StartsWith("select", StringComparison.OrdinalIgnoreCase));
                int lp = 0;
                decimal flatCost = 0m;
                decimal multiplier = 0m;
                decimal baseMultiplier = 0m;
                int area = 0;
                int comforts = 0;
                int security = 0;
                int areaMaximum = 0;
                int comfortsMaximum = 0;
                int securityMaximum = 0;
                bool numericExact = TryReadOptionalInt(row, "lp", out lp);
                numericExact &= TryReadOptionalDecimal(row, "cost", out flatCost);
                numericExact &= TryReadOptionalDecimal(row, "multiplier", out multiplier);
                numericExact &= TryReadOptionalDecimal(row, "multiplierbaseonly", out baseMultiplier);
                numericExact &= TryReadOptionalInt(row, "area", out area);
                numericExact &= TryReadOptionalInt(row, "comforts", out comforts);
                numericExact &= TryReadOptionalInt(row, "security", out security);
                numericExact &= TryReadOptionalInt(row, "areamaximum", out areaMaximum);
                numericExact &= TryReadOptionalInt(row, "comfortsmaximum", out comfortsMaximum);
                numericExact &= TryReadOptionalInt(row, "securitymaximum", out securityMaximum);
                bool allowedExact = TryReadLifestyleAllowed1000(row, out var allowedFreeLifestyles);
                if (numericExact && allowedExact) exactQualityCosts1000.Add(sourceId);
                bool eligibilityExact = numericExact
                    && allowedExact
                    && !hasRequirements
                    && !hasUnboundedPrompt
                    && !string.IsNullOrWhiteSpace(page);
                bool selectable = !string.IsNullOrWhiteSpace(name)
                    && !string.IsNullOrWhiteSpace(qualityType)
                    && sourceEnabled
                    && implemented
                    && !careerOnly
                    && eligibilityExact;
                string[] optionBlockers = selectable
                    ? []
                    : !sourceEnabled
                        ? [CharacterCreationLifestylesBlockers.SourceDisabled]
                        : [CharacterCreationLifestylesBlockers.UnsupportedSemantics];
                string optionId = $"lifestyle-quality:{sourceId:D}";
                string anchor = $"lifestyles.xml#quality:{sourceId:D}";
                var candidate = new CharacterCreationLifestyleQualityCatalogOption(
                    optionId,
                    sourceId,
                    name,
                    category,
                    sourceBook,
                    page,
                    qualityType,
                    numericExact ? lp : 0,
                    numericExact ? flatCost : 0m,
                    numericExact ? multiplier : 0m,
                    numericExact ? baseMultiplier : 0m,
                    numericExact ? area : 0,
                    numericExact ? comforts : 0,
                    numericExact ? security : 0,
                    numericExact ? areaMaximum : 0,
                    numericExact ? comfortsMaximum : 0,
                    numericExact ? securityMaximum : 0,
                    allowedFreeLifestyles,
                    selectable,
                    eligibilityExact,
                    optionBlockers,
                    [anchor],
                    string.Empty);
                qualities.Add(candidate with
                {
                    OptionDigest = CharacterCreationLifestylesRules.ComputeQualityOptionDigest(candidate)
                });
            }

            Dictionary<string, CharacterCreationLifestyleQualityCatalogOption[]> qualitiesByName = qualities
                .GroupBy(option => option.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var lifestyles = new List<CharacterCreationLifestyleCatalogOption>();
            foreach (XElement row in lifestyleRows.OrderBy(
                         item => ReadValue(item, "id"),
                         StringComparer.Ordinal))
            {
                if (!Guid.TryParse(ReadValue(row, "id"), out Guid sourceId)
                    || sourceId == Guid.Empty
                    || row.Element("hide") is not null)
                {
                    continue;
                }
                string name = ReadValue(row, "name");
                string sourceBook = ReadValue(row, "source");
                string page = ReadValue(row, "page");
                bool sourceEnabled = !string.IsNullOrWhiteSpace(sourceBook)
                    && _enabledSourcebooks.Contains(sourceBook);
                decimal cost = 0m;
                int dice = 0;
                decimal nuyenMultiplier = 0m;
                int lp = 0;
                decimal costForArea = 0m;
                decimal costForComforts = 0m;
                decimal costForSecurity = 0m;
                bool numericExact = TryReadRequiredNonNegativeDecimal(row, "cost", out cost);
                numericExact &= TryReadRequiredNonNegativeInt(row, "dice", out dice);
                numericExact &= TryReadRequiredNonNegativeDecimal(row, "multiplier", out nuyenMultiplier);
                numericExact &= TryReadOptionalNonNegativeInt(row, "lp", out lp);
                numericExact &= TryReadOptionalNonNegativeDecimal(row, "costforarea", out costForArea);
                numericExact &= TryReadOptionalNonNegativeDecimal(row, "costforcomforts", out costForComforts);
                numericExact &= TryReadOptionalNonNegativeDecimal(row, "costforsecurity", out costForSecurity);
                int baseComfort = 0;
                int maxComfort = 0;
                int baseArea = 0;
                int maxArea = 0;
                int baseSecurity = 0;
                int maxSecurity = 0;
                bool aspectsExact = TryResolveLifestyleAspect(comfortRows, name, out baseComfort, out maxComfort);
                aspectsExact &= TryResolveLifestyleAspect(areaRows, name, out baseArea, out maxArea);
                aspectsExact &= TryResolveLifestyleAspect(securityRows, name, out baseSecurity, out maxSecurity);
                if (!aspectsExact)
                {
                    baseComfort = 0;
                    maxComfort = 0;
                    baseArea = 0;
                    maxArea = 0;
                    baseSecurity = 0;
                    maxSecurity = 0;
                    // Hospital and other fixed standard rows have no advanced aspect rows.
                    aspectsExact = !row.Elements("lp").Any()
                        || ReadValue(row, "lp") == "0";
                }

                var builtIns = new List<CharacterCreationLifestyleBuiltInQuality>();
                bool builtInsExact = true;
                if (freeGridsEnabled)
                {
                    foreach (XElement freeGrid in row.Element("freegrids")?.Elements("freegrid") ?? [])
                    {
                        string qualityName = freeGrid.Value.Trim();
                        string extra = freeGrid.Attribute("select")?.Value.Trim() ?? string.Empty;
                        if (!qualitiesByName.TryGetValue(
                                qualityName,
                                out CharacterCreationLifestyleQualityCatalogOption[]? matches)
                            || matches.Length != 1
                            || !exactQualityCosts1000.Contains(matches[0].SourceId))
                        {
                            builtInsExact = false;
                            continue;
                        }
                        builtIns.Add(new CharacterCreationLifestyleBuiltInQuality(
                            matches[0].OptionId,
                            extra,
                            matches[0].SourceAnchorIds));
                    }
                }

                string increment = ReadValue(row, "increment").ToLowerInvariant() switch
                {
                    "day" => CharacterCreationLifestyleIncrementIds.Day,
                    "week" => CharacterCreationLifestyleIncrementIds.Week,
                    _ => CharacterCreationLifestyleIncrementIds.Month
                };
                bool exact = numericExact
                    && aspectsExact
                    && builtInsExact
                    && !string.IsNullOrWhiteSpace(name)
                    && !string.IsNullOrWhiteSpace(page);
                bool selectable = sourceEnabled && exact;
                string[] optionBlockers = selectable
                    ? []
                    : !sourceEnabled
                        ? [CharacterCreationLifestylesBlockers.SourceDisabled]
                        : [CharacterCreationLifestylesBlockers.UnsupportedSemantics];
                string optionId = $"lifestyle:{sourceId:D}";
                string anchor = $"lifestyles.xml#lifestyle:{sourceId:D}";
                var candidate = new CharacterCreationLifestyleCatalogOption(
                    optionId,
                    sourceId,
                    name,
                    numericExact ? cost : 0m,
                    numericExact ? dice : 0,
                    numericExact ? nuyenMultiplier : 0m,
                    numericExact ? lp : 0,
                    numericExact ? costForArea : 0m,
                    numericExact ? costForComforts : 0m,
                    numericExact ? costForSecurity : 0m,
                    baseArea,
                    maxArea,
                    baseComfort,
                    maxComfort,
                    baseSecurity,
                    maxSecurity,
                    ParseBool(ReadValue(row, "allowbonuslp")),
                    increment,
                    sourceBook,
                    page,
                    builtIns,
                    selectable,
                    exact,
                    optionBlockers,
                    [anchor],
                    string.Empty);
                lifestyles.Add(candidate with
                {
                    OptionDigest = CharacterCreationLifestylesRules.ComputeOptionDigest(candidate)
                });
            }

            if (lifestyles.Count == 0 || qualities.Count == 0)
                blockers.Add(CharacterCreationLifestylesBlockers.AuthorityUnavailable);
            string gmPolicyDigest = CharacterCreationSkillsDigest.Compute(new
            {
                Schema = "chummer.sr5.creation-lifestyles-gm-policy.v1",
                _settingsProfileId,
                EnabledBooks = _enabledSourcebooks.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                FreeGridsEnabled = freeGridsEnabled,
                TrustFundLevel = trustFundLevel
            });
            string runtimeDigest = CharacterCreationSkillsDigest.Compute(new
            {
                CharacterCreationLifestylesSchemas.RuntimeV1,
                StableOptionIdentity = true,
                Chummer5CostLayerOrder = true,
                UnsupportedImprovementPrecedenceFailsClosed = true,
                AtomicCreateEditDelete = true
            });
            if (metatypeCostPercent != 0m)
            {
                gmPolicyDigest = CharacterCreationSkillsDigest.Compute(new
                {
                    PreviousPolicyDigest = gmPolicyDigest,
                    MetatypeCostPercent = metatypeCostPercent
                });
                runtimeDigest = CharacterCreationSkillsDigest.Compute(new
                {
                    PreviousRuntimeDigest = runtimeDigest,
                    MetatypeCostLayer = "unconditional-global-racial-after-split-before-outings-v1"
                });
            }
            string[] lifestyleSourceAnchors = ["lifestyles.xml", SettingsAnchor, "character.xml#improvements"];
            if (!string.IsNullOrEmpty(qualityInputsDigest))
            {
                sourceDigest = CharacterCreationSkillsDigest.Compute(new
                {
                    Lifestyles = sourceDigest,
                    PurchasedQualitySources = qualityInputsDigest
                });
                lifestyleSourceAnchors = lifestyleSourceAnchors
                    .Concat(["qualities.xml", "character.xml#qualities"])
                    .Concat(qualityInstances.SelectMany(instance => instance.Selection.SourceAnchorIds))
                    .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
            }
            if (purchasedQualityCosts is not null)
            {
                gmPolicyDigest = CharacterCreationSkillsDigest.Compute(new
                {
                    PreviousPolicyDigest = gmPolicyDigest,
                    PurchasedQualityCosts = purchasedQualityCosts
                });
                runtimeDigest = CharacterCreationSkillsDigest.Compute(new
                {
                    PreviousRuntimeDigest = runtimeDigest,
                    PurchasedQualityCostLayer = purchasedQualityCosts.Occurrences.Any(effect =>
                        effect.EffectType == CharacterCreationLifestyleCostEffectType.BasicLifestyleCost)
                        ? "unconditional-source-occurrences-min-minus100-standard-only-basic-after-racial-before-outings-v3"
                        : purchasedQualityCosts.Occurrences.Any(effect => effect.Percentage < 0m)
                        ? "unconditional-source-occurrences-min-minus100-after-racial-before-outings-v2"
                        : "unconditional-nonnegative-source-occurrences-after-racial-before-outings-v1"
                });
            }
            string[] normalized = blockers.Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
            var projected = new CharacterCreationLifestylesAuthority(
                CharacterCreationLifestylesSchemas.AuthorityV1,
                "sr5",
                _settingsProfileId,
                lifestyles,
                qualities,
                trustFundLevel,
                freeGridsEnabled,
                lifestyleSourceAnchors,
                normalized,
                normalized.Length == 0,
                sourceDigest,
                _rawProfileInputsDigest,
                gmPolicyDigest,
                runtimeDigest,
                string.Empty)
            {
                MetatypeCostPercent = metatypeCostPercent,
                PurchasedQualityCosts = purchasedQualityCosts,
                FreeGridsActivation = freeGridsActivation
            };
            authority = projected with
            {
                AuthorityDigest = CharacterCreationLifestylesRules.ComputeAuthorityDigest(projected)
            };
            if (authority.IsAuthoritative && !_sourceInputs.HasSourceDrift)
            {
                JsonElement frozen = JsonSerializer.SerializeToElement(authority);
                lock (_completionProjectionSync) { _creationLifestylesPre1008AuthoritySnapshot = frozen; }
            }
            return true;
        }

    }
}
