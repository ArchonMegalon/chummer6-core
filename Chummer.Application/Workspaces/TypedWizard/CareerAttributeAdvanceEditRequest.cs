// Existing typed adapter from the paired Presentation snapshot; Core executes it
// only after store admission. Rules remain in Chummer.Contracts.Characters.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces.TypedWizard;

internal sealed record CareerAttributeAdvanceEditorState(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    IReadOnlyList<CharacterCareerAttributeAdvanceQuote> Attributes,
    int OmittedAttributeCount,
    IReadOnlyList<CharacterCareerAttributeAdvanceReceipt> RecoverableReceipts,
    int OmittedReceiptCount);

internal sealed record CareerAttributeAdvanceRequest(
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    CharacterCareerAttributeAdvanceQuote ExpectedAttribute,
    string ExpectedLogicalRevision,
    string ExpectedSourceRevision,
    string ExpectedRuleDigest,
    bool Confirmed,
    Guid ExpenseId,
    DateTime ExpenseDateLocal);

internal sealed record CareerAttributeCorrectionRequest(
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    CharacterCareerAttributeAdvanceReceipt OriginalReceipt,
    string ExpectedReceiptDigest,
    bool Confirmed,
    Guid CorrectionId,
    string Reason);

internal sealed record CareerAttributeAdvanceMutationResult(
    string Xml,
    CharacterCareerAttributeAdvanceReceipt Receipt);

internal sealed record CareerAttributeCorrectionMutationResult(
    string Xml,
    CharacterCareerAttributeCorrectionPlan Correction);

internal static class CareerAttributeAdvanceEditorProjector
{
    internal sealed record AttributeFacts(
        CharacterCareerAttributeAdvanceQuote Quote,
        int CalculatedTotalValue);

    private sealed record ImprovementFacts(
        XElement Element,
        int Ordinal,
        string Type,
        string ImprovedName,
        string Source,
        int Minimum,
        int Maximum,
        decimal Augmented,
        int AugmentedMaximum,
        decimal Value,
        int Rating);

    public static CareerAttributeAdvanceEditorState Project(
        string xml,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        string? settingsCatalogJson,
        ICharacterSourceDataResolver? sourceDataResolver = null)
    {
        if (string.IsNullOrWhiteSpace(workspaceId.Value))
        {
            throw new InvalidOperationException(
                "A nonblank dossier identity is required for attribute advancement.");
        }
        if (contentRevision <= 0)
        {
            throw new InvalidOperationException(
                "Dossier revision is unavailable. Reload before advancing an attribute.");
        }

        (IReadOnlyList<AttributeFacts> facts, int omitted) =
            ProjectFacts(xml, settingsCatalogJson, requireSavedTotalValueMatch: true, sourceDataResolver);
        CareerAttributeAdvanceMutation.ReceiptRecovery recovery =
            CareerAttributeAdvanceMutation.RecoverReceipts(xml, facts);
        return new CareerAttributeAdvanceEditorState(
            workspaceId,
            contentRevision,
            facts.Select(static value => value.Quote).ToArray(),
            omitted,
            recovery.Receipts,
            recovery.OmittedCount);
    }

    internal static (
        IReadOnlyList<AttributeFacts> Attributes,
        int OmittedAttributeCount) ProjectFacts(
        string xml,
        string? settingsCatalogJson,
        bool requireSavedTotalValueMatch,
        ICharacterSourceDataResolver? sourceDataResolver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XElement root = CareerActiveSkillAdvanceEditorProjector.RequireCharacterRoot(document);
        bool created = CareerActiveSkillAdvanceEditorProjector.ReadRequiredBool(root, "created");
        if (!created)
        {
            throw new InvalidOperationException(
                "Attribute advancement is available only for career runners.");
        }

        // Every projection, including staged mutation/correction, gets a context
        // for these exact bytes. An evaluation from the reviewed dossier is stale
        // as soon as its natural attribute rating changes.
        ICharacterSourceDataContext? sourceContext = sourceDataResolver?.TryCreateContext(xml);
        XElement settings = CareerSettingsProfileResolver.Resolve(root, settingsCatalogJson,
            sourceContext);
        XElement karmaCost = CareerActiveSkillAdvanceEditorProjector.RequireSingle(
            settings,
            "karmacost",
            "The active settings profile must have one <karmacost> container.");
        CharacterCareerAttributeAdvanceSettings rules = new(
            CareerActiveSkillAdvanceEditorProjector.ReadRequiredNonNegativeInt(
                karmaCost,
                "karmaattribute"),
            ReadOptionalBool(settings, "alternatemetatypeattributekarma", false));
        bool unclampMinimum = ReadOptionalBool(settings, "unclampattributeminimum", false);
        bool secondMagicAttribute = ReadOptionalBool(
            settings,
            "mysadeptsecondmagattribute",
            false);
        bool dontUseCyberlimbCalculation = ReadOptionalBool(
            settings,
            "dontusecyberlimbcalculation",
            false);
        bool hasApplicableCyberlimb = !dontUseCyberlimbCalculation
            && HasApplicableCyberlimb(root, settings);
        string characterXmlDigest = hasApplicableCyberlimb ? Digest(xml) : string.Empty;
        string settingsXmlDigest = hasApplicableCyberlimb
            ? Digest(settings.ToString(SaveOptions.DisableFormatting)) : string.Empty;

        int availableKarma = CareerActiveSkillAdvanceEditorProjector
            .ReadRequiredNonNegativeInt(root, "karma");
        bool magicEnabled = CareerActiveSkillAdvanceEditorProjector
            .ReadRequiredBool(root, "magenabled");
        bool resonanceEnabled = CareerActiveSkillAdvanceEditorProjector
            .ReadRequiredBool(root, "resenabled");
        bool depthEnabled = ReadOptionalBool(root, "depenabled", false);
        bool mysticAdept = CareerActiveSkillAdvanceEditorProjector
            .ReadRequiredBool(root, "adept")
            && CareerActiveSkillAdvanceEditorProjector.ReadRequiredBool(root, "magician");
        bool critter = ReadOptionalBool(root, "critter", false);
        string metatypeCategory = ReadOptionalText(root, "metatypecategory", string.Empty);

        XElement attributes = CareerActiveSkillAdvanceEditorProjector.RequireSingle(
            root,
            "attributes",
            "The saved runner must have one <attributes> container.");
        XElement[] rows = attributes.Elements("attribute").ToArray();
        Dictionary<string, XElement> catalogRows = new(StringComparer.Ordinal);
        HashSet<string> catalog = CharacterCareerAttributeAdvanceRules.GetTargetCatalog()
            .Select(static identity => identity.Abbreviation)
            .ToHashSet(StringComparer.Ordinal);
        foreach (XElement row in rows)
        {
            string name = CareerActiveSkillAdvanceEditorProjector.ReadRequiredText(
                row,
                "name",
                "An attribute");
            if (catalog.Contains(name) && !catalogRows.TryAdd(name, row))
            {
                throw new InvalidOperationException(
                    $"The saved runner has duplicate {name} attribute rows.");
            }
        }

        XElement[] improvementsContainers = root.Elements("improvements").Take(2).ToArray();
        if (improvementsContainers.Length > 1)
        {
            throw new InvalidOperationException(
                "The saved runner has duplicate <improvements> containers.");
        }
        XElement? improvements = improvementsContainers.SingleOrDefault();
        string rawRuleState = CareerSettingsProfileResolver.RuleState(settings)
            + "\n"
            + (improvements?.ToString(SaveOptions.DisableFormatting) ?? "<improvements />")
            + "\n"
            + string.Join("|", new[]
            {
                magicEnabled.ToString(CultureInfo.InvariantCulture),
                resonanceEnabled.ToString(CultureInfo.InvariantCulture),
                mysticAdept.ToString(CultureInfo.InvariantCulture),
                metatypeCategory,
                critter.ToString(CultureInfo.InvariantCulture),
                depthEnabled.ToString(CultureInfo.InvariantCulture)
            });

        List<AttributeFacts> projected = [];
        int omitted = 0;
        foreach (CharacterCareerAttributeIdentity identity in
                 CharacterCareerAttributeAdvanceRules.GetTargetCatalog())
        {
            if (!catalogRows.TryGetValue(identity.Abbreviation, out XElement? row))
            {
                // Disabled special attributes may legitimately have no saved row.
                // Present rows still require the complete authority projection below.
                bool requiresSavedRow = identity.Abbreviation switch
                {
                    "MAG" => magicEnabled,
                    "MAGAdept" => magicEnabled && mysticAdept && secondMagicAttribute,
                    "RES" => resonanceEnabled,
                    _ => true
                };
                if (requiresSavedRow)
                {
                    omitted++;
                }
                continue;
            }
            if (!TryProjectAttribute(
                    row,
                    identity,
                    availableKarma,
                    magicEnabled,
                    mysticAdept,
                    secondMagicAttribute,
                    resonanceEnabled,
                    depthEnabled,
                    critter,
                    metatypeCategory,
                    unclampMinimum,
                    hasApplicableCyberlimb,
                    sourceContext,
                    characterXmlDigest,
                    settingsXmlDigest,
                    rules,
                    rawRuleState,
                    improvements,
                    requireSavedTotalValueMatch,
                    out AttributeFacts facts))
            {
                omitted++;
                continue;
            }
            projected.Add(facts);
        }

        return (projected, omitted);
    }

    private static bool TryProjectAttribute(
        XElement row,
        CharacterCareerAttributeIdentity identity,
        int availableKarma,
        bool magicEnabled,
        bool mysticAdept,
        bool secondMagicAttribute,
        bool resonanceEnabled,
        bool depthEnabled,
        bool critter,
        string metatypeCategory,
        bool unclampMinimum,
        bool hasApplicableCyberlimb,
        ICharacterSourceDataContext? sourceContext,
        string characterXmlDigest,
        string settingsXmlDigest,
        CharacterCareerAttributeAdvanceSettings rules,
        string rawRuleState,
        XElement? improvements,
        bool requireSavedTotalValueMatch,
        out AttributeFacts facts)
    {
        facts = null!;
        try
        {
            if (identity.Kind == CharacterCareerAttributeKind.Edge && depthEnabled)
            {
                return false;
            }

            int basePoints = ReadRequiredNonNegativeInt(row, "base", "An attribute");
            int karmaPoints = ReadRequiredNonNegativeInt(row, "karma", "An attribute");
            int metatypeMinimum = ReadRequiredNonNegativeInt(row, "metatypemin", "An attribute");
            int metatypeMaximum = ReadRequiredNonNegativeInt(row, "metatypemax", "An attribute");
            int metatypeAugmentedMaximum = ReadRequiredNonNegativeInt(
                row,
                "metatypeaugmax",
                "An attribute");
            int savedTotalValue = ReadRequiredNonNegativeInt(row, "totalvalue", "An attribute");
            if (metatypeMaximum < metatypeMinimum
                || metatypeAugmentedMaximum < metatypeMaximum)
            {
                return false;
            }
            if (!TryResolveImprovementFacts(
                    improvements,
                    identity.Abbreviation,
                    out IReadOnlyList<ImprovementFacts> relevant,
                    out IReadOnlyList<CharacterCareerAttributeKarmaModifier> costModifiers,
                    out int burnedEdgePoints))
            {
                return false;
            }

            ImprovementFacts[] attributeRows = relevant
                .Where(static candidate => candidate.Type == "Attribute")
                .ToArray();
            int minimumModifiers = checked(attributeRows.Sum(candidate =>
                candidate.Minimum * candidate.Rating));
            int maximumModifiers = checked(attributeRows.Sum(candidate =>
                candidate.Maximum * candidate.Rating));
            int augmentedMaximumModifiers = checked(attributeRows
                .Where(candidate => string.Equals(
                    candidate.ImprovedName,
                    identity.Abbreviation,
                    StringComparison.Ordinal))
                .Sum(candidate => candidate.AugmentedMaximum * candidate.Rating));
            decimal freeBaseRaw = relevant
                .Where(static candidate => candidate.Type == "Attributelevel")
                .Sum(static candidate => candidate.Value);
            int freeBase = StandardRound(Math.Min(
                freeBaseRaw,
                metatypeMaximum - metatypeMinimum));
            int rawMinimum = checked(metatypeMinimum + minimumModifiers);
            if (!unclampMinimum)
            {
                rawMinimum = Math.Max(rawMinimum, 0);
            }

            bool cyberzombieMagic = string.Equals(
                    metatypeCategory,
                    "Cyberzombie",
                    StringComparison.Ordinal)
                && identity.Abbreviation is "MAG" or "MAGAdept";
            int naturalMaximum = cyberzombieMagic
                ? 1
                : Math.Max(0, checked(metatypeMaximum + maximumModifiers));
            int totalMinimum = cyberzombieMagic
                ? 1
                : ResolveTotalMinimum(
                    rawMinimum,
                    naturalMaximum,
                    critter,
                    identity.Abbreviation);
            decimal baseValueModifiersRaw = attributeRows
                .Where(candidate => string.Equals(
                    candidate.ImprovedName,
                    identity.Abbreviation + "Base",
                    StringComparison.Ordinal))
                .Sum(static candidate => candidate.Augmented * candidate.Rating);
            int baseValueModifiers = StandardRound(baseValueModifiersRaw);
            int effectiveValue = Math.Min(
                checked(Math.Max(
                    checked(basePoints + freeBase + rawMinimum + baseValueModifiers),
                    totalMinimum) + karmaPoints),
                naturalMaximum);
            int? burnedEdgeRepairValue = null;
            if (identity.Kind == CharacterCareerAttributeKind.Edge
                && burnedEdgePoints > 0)
            {
                int repairedRawMinimum = checked(
                    metatypeMinimum + minimumModifiers + 1);
                if (!unclampMinimum)
                {
                    repairedRawMinimum = Math.Max(repairedRawMinimum, 0);
                }
                int repairedTotalMinimum = ResolveTotalMinimum(
                    repairedRawMinimum,
                    naturalMaximum,
                    critter,
                    identity.Abbreviation);
                burnedEdgeRepairValue = Math.Min(
                    checked(Math.Max(
                        checked(basePoints + freeBase + repairedRawMinimum + baseValueModifiers),
                        repairedTotalMinimum) + karmaPoints),
                    naturalMaximum);
            }

            decimal augmentedRaw = attributeRows
                .Where(candidate => string.Equals(
                    candidate.ImprovedName,
                    identity.Abbreviation,
                    StringComparison.Ordinal))
                .Sum(static candidate => candidate.Augmented * candidate.Rating);
            int attributeModifiers = StandardRound(augmentedRaw);
            int modifiersClamp = checked(
                metatypeAugmentedMaximum - metatypeMaximum + augmentedMaximumModifiers);
            bool maximumClamp = relevant.Any(static candidate =>
                candidate.Type == "AttributeMaxClamp");
            if (maximumClamp)
            {
                modifiersClamp = Math.Min(
                    modifiersClamp,
                    checked(naturalMaximum - effectiveValue));
            }
            attributeModifiers = Math.Min(attributeModifiers, modifiersClamp);
            int totalAugmentedMaximum = cyberzombieMagic
                ? 1
                : maximumClamp
                    ? naturalMaximum
                    : Math.Max(0, checked(
                        metatypeAugmentedMaximum
                        + maximumModifiers
                        + augmentedMaximumModifiers));
            int naturalTotalValue = checked(effectiveValue + attributeModifiers);
            int calculatedTotalValue = Math.Min(naturalTotalValue, totalAugmentedMaximum);
            CharacterCareerCyberlimbAttributeEvaluation? limbEvaluation = null;
            if (hasApplicableCyberlimb && identity.Abbreviation is "AGI" or "STR")
            {
                if (sourceContext is null
                    || !sourceContext.TryEvaluateCareerCyberlimbAttribute(
                        new CharacterCareerCyberlimbAttributeInput(
                        identity.Abbreviation, characterXmlDigest, settingsXmlDigest,
                        effectiveValue, naturalTotalValue, naturalMaximum, totalAugmentedMaximum,
                        critter || metatypeMaximum == 0 || (identity.Abbreviation == "AGI"
                            && attributeRows.Where(candidate => candidate.ImprovedName == identity.Abbreviation
                                && candidate.Source == "ArmorEncumbrance")
                                .Sum(static candidate => candidate.Augmented * candidate.Rating) < 0m)),
                    out limbEvaluation)
                    || limbEvaluation is null
                    || limbEvaluation.Abbreviation != identity.Abbreviation
                    || limbEvaluation.CharacterXmlDigest != characterXmlDigest
                    || limbEvaluation.SettingsXmlDigest != settingsXmlDigest
                    || limbEvaluation.NaturalValue != effectiveValue
                    || limbEvaluation.NaturalTotalValue != naturalTotalValue
                    || limbEvaluation.TotalAugmentedMaximum != totalAugmentedMaximum
                    || limbEvaluation.CalculatedTotalValue < 0
                    || limbEvaluation.CalculatedTotalValue > Math.Max(1, totalAugmentedMaximum)
                    || string.IsNullOrWhiteSpace(limbEvaluation.LimbSourceState)
                    || string.IsNullOrWhiteSpace(limbEvaluation.RuleSourceState))
                {
                    return false;
                }
                // The canonical result already includes averaging, caps and the
                // cyberlimb-specific zero exceptions. Do not apply natural-only
                // minimum handling to it or infer it from the saved average.
                calculatedTotalValue = limbEvaluation.CalculatedTotalValue;
            }
            else if (calculatedTotalValue < 1)
            {
                bool encumbranceAllowsZero = identity.Abbreviation is "AGI" or "REA"
                    && attributeRows
                        .Where(candidate => string.Equals(
                                candidate.ImprovedName,
                                identity.Abbreviation,
                                StringComparison.Ordinal)
                            && string.Equals(
                                candidate.Source,
                                "ArmorEncumbrance",
                                StringComparison.Ordinal))
                        .Sum(static candidate => candidate.Augmented * candidate.Rating) < 0m;
                calculatedTotalValue = critter
                    || metatypeMaximum == 0
                    || identity.Kind is CharacterCareerAttributeKind.Edge
                        or CharacterCareerAttributeKind.Magic
                        or CharacterCareerAttributeKind.Resonance
                    || encumbranceAllowsZero
                    ? 0
                    : 1;
            }
            if (requireSavedTotalValueMatch && savedTotalValue != calculatedTotalValue)
            {
                return false;
            }

            CharacterCareerAttributeAdvanceInput input = new(
                identity,
                Created: true,
                RulesetId: CharacterCareerAttributeAdvanceRules.RulesetId,
                DisplayName: identity.Abbreviation,
                basePoints,
                karmaPoints,
                effectiveValue,
                naturalMaximum,
                metatypeMinimum,
                availableKarma,
                magicEnabled,
                mysticAdept,
                secondMagicAttribute,
                resonanceEnabled,
                burnedEdgePoints,
                rules,
                costModifiers,
                RawSourceState: row.ToString(SaveOptions.DisableFormatting)
                    + (limbEvaluation is null ? string.Empty
                        : "\ncyberlimb-input:" + Digest(limbEvaluation.LimbSourceState)),
                rawRuleState + (limbEvaluation is null ? string.Empty
                    : "\ncyberlimb-rule:" + Digest(limbEvaluation.RuleSourceState)))
            {
                BurnedEdgeRepairValue = burnedEdgeRepairValue
            };
            if (!CharacterCareerAttributeAdvanceRules.TryCreateQuote(input, out var quote))
            {
                return false;
            }

            facts = new AttributeFacts(quote, calculatedTotalValue);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool HasApplicableCyberlimb(XElement root, XElement settings)
    {
        XElement[] containers = root.Elements("cyberwares").Take(2).ToArray();
        if (containers.Length > 1)
        {
            throw new InvalidOperationException(
                "The saved runner has duplicate <cyberwares> containers.");
        }

        string excludedLimbSlots = ReadOptionalText(
            settings,
            "excludelimbslot",
            string.Empty);
        return containers.SingleOrDefault()?.Elements("cyberware")
            .Any(candidate => HasApplicableCyberlimb(
                candidate,
                [],
                excludedLimbSlots)) == true;
    }

    private static bool HasApplicableCyberlimb(
        XElement cyberware,
        IReadOnlyList<XElement> parents,
        string excludedLimbSlots)
    {
        if (!IsModularCurrentlyEquipped(cyberware, parents))
        {
            return false;
        }

        if (IsCyberlimb(cyberware))
        {
            string limbSlot = ReadOptionalText(cyberware, "limbslot", string.Empty);
            return !excludedLimbSlots.Contains(limbSlot, StringComparison.Ordinal);
        }

        XElement[] containers = cyberware.Elements("children").Take(2).ToArray();
        if (containers.Length > 1)
        {
            throw new InvalidOperationException(
                "Saved cyberware has duplicate <children> containers.");
        }
        if (containers.Length == 0)
        {
            return false;
        }

        XElement[] lineage = [cyberware, .. parents];
        return containers[0].Elements("cyberware")
            .Any(candidate => HasApplicableCyberlimb(
                candidate,
                lineage,
                excludedLimbSlots));
    }

    private static bool IsCyberlimb(XElement cyberware)
    {
        string limbSlot = ReadOptionalText(cyberware, "limbslot", string.Empty);
        string modularMount = ReadOptionalText(
            cyberware,
            "plugsintomodularmount",
            string.Empty);
        if (!string.IsNullOrWhiteSpace(limbSlot)
            || modularMount.Equals("WRIST", StringComparison.OrdinalIgnoreCase)
            || modularMount.Equals("ELBOW", StringComparison.OrdinalIgnoreCase)
            || modularMount.Equals("SHOULDER", StringComparison.OrdinalIgnoreCase)
            || modularMount.Equals("ANKLE", StringComparison.OrdinalIgnoreCase)
            || modularMount.Equals("KNEE", StringComparison.OrdinalIgnoreCase)
            || modularMount.Equals("HIP", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!ReadOptionalBool(cyberware, "inheritattributes", false))
        {
            return false;
        }
        XElement[] containers = cyberware.Elements("children").Take(2).ToArray();
        if (containers.Length > 1)
        {
            throw new InvalidOperationException(
                "Saved cyberware has duplicate <children> containers.");
        }
        return containers.SingleOrDefault()?.Elements("cyberware")
            .Any(IsCyberlimb) == true;
    }

    private static bool IsModularCurrentlyEquipped(
        XElement cyberware,
        IReadOnlyList<XElement> parents)
    {
        bool equipped = string.IsNullOrEmpty(ReadOptionalText(
            cyberware,
            "plugsintomodularmount",
            string.Empty));
        foreach (XElement parent in parents)
        {
            if (!string.IsNullOrEmpty(ReadOptionalText(
                    parent,
                    "hasmodularmount",
                    string.Empty)))
            {
                equipped = true;
            }
            if (!string.IsNullOrEmpty(ReadOptionalText(
                    parent,
                    "plugsintomodularmount",
                    string.Empty)))
            {
                equipped = false;
            }
        }
        return equipped;
    }

    private static bool TryResolveImprovementFacts(
        XElement? improvements,
        string abbreviation,
        out IReadOnlyList<ImprovementFacts> relevant,
        out IReadOnlyList<CharacterCareerAttributeKarmaModifier> costModifiers,
        out int burnedEdgePoints)
    {
        List<ImprovementFacts> resolved = [];
        List<CharacterCareerAttributeKarmaModifier> costs = [];
        burnedEdgePoints = 0;
        int ordinal = 0;
        foreach (XElement improvement in improvements?.Elements("improvement") ?? [])
        {
            string type = ReadOptionalText(improvement, "improvementttype", string.Empty);
            string target = ReadOptionalText(improvement, "improvedname", string.Empty);
            string source = ReadOptionalText(improvement, "improvementsource", string.Empty);
            bool enabled = ReadOptionalBool(improvement, "enabled", true);
            bool addToRating = ReadOptionalBool(improvement, "addtorating", false);
            string condition = ReadOptionalText(improvement, "condition", string.Empty);
            bool careerActive = enabled
                && !addToRating
                && (condition.Length == 0
                    || string.Equals(condition, "career", StringComparison.Ordinal));
            bool targetExact = string.Equals(target, abbreviation, StringComparison.Ordinal);
            bool targetBase = string.Equals(
                target,
                abbreviation + "Base",
                StringComparison.Ordinal);
            bool costType = type is "AttributeKarmaCost" or "AttributeKarmaCostMultiplier";
            bool relevantCost = costType
                && careerActive
                && (target.Length == 0
                    || string.Equals(target, abbreviation, StringComparison.Ordinal));
            bool relevantRating = careerActive
                && (type == "Attribute" && (targetExact || targetBase)
                    || type == "Attributelevel" && targetExact
                    || type == "AttributeMaxClamp" && targetExact);
            bool unsupportedReplacement = enabled
                && type == "ReplaceAttribute"
                && targetExact;
            bool burnedSource = string.Equals(source, "BurnedEdge", StringComparison.Ordinal);
            if (!relevantCost && !relevantRating && !burnedSource && !unsupportedReplacement)
            {
                ordinal++;
                continue;
            }
            if (unsupportedReplacement)
            {
                relevant = [];
                costModifiers = [];
                return false;
            }
            if (burnedSource && abbreviation != "EDG")
            {
                ordinal++;
                continue;
            }
            if (burnedSource && !careerActive)
            {
                ordinal++;
                continue;
            }
            if (burnedSource
                && (type != "Attribute"
                    || target != "EDG"))
            {
                relevant = [];
                costModifiers = [];
                return false;
            }
            if (!string.IsNullOrEmpty(ReadOptionalText(improvement, "unique", string.Empty))
                || ReadOptionalBool(improvement, "custom", false))
            {
                relevant = [];
                costModifiers = [];
                return false;
            }

            int minimum = ReadRequiredInt(improvement, "min", "An attribute improvement");
            int maximum = ReadRequiredInt(improvement, "max", "An attribute improvement");
            decimal augmented = ReadRequiredDecimal(improvement, "aug", "An attribute improvement");
            int augmentedMaximum = ReadRequiredInt(
                improvement,
                "augmax",
                "An attribute improvement");
            decimal value = ReadRequiredDecimal(improvement, "val", "An attribute improvement");
            int rating = ReadRequiredNonNegativeInt(
                improvement,
                "rating",
                "An attribute improvement");
            ImprovementFacts facts = new(
                improvement,
                ordinal,
                type,
                target,
                source,
                minimum,
                maximum,
                augmented,
                augmentedMaximum,
                value,
                rating);
            if (relevantRating)
            {
                resolved.Add(facts);
            }
            if (relevantCost)
            {
                if (minimum < 0 || maximum < 0 || maximum != 0 && maximum < minimum)
                {
                    relevant = [];
                    costModifiers = [];
                    return false;
                }
                string raw = ordinal.ToString(CultureInfo.InvariantCulture)
                    + "\0"
                    + improvement.ToString(SaveOptions.DisableFormatting);
                costs.Add(new CharacterCareerAttributeKarmaModifier(
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))
                        .ToLowerInvariant(),
                    type == "AttributeKarmaCost"
                        ? CharacterCareerAttributeKarmaModifierKind.AttributeKarmaCost
                        : CharacterCareerAttributeKarmaModifierKind.AttributeKarmaCostMultiplier,
                    target,
                    minimum,
                    maximum,
                    value));
            }
            if (burnedSource)
            {
                int burned = checked(-(minimum * rating));
                // Replacing a burn changes only its minimum penalty. Reject
                // mixed effects rather than silently discarding other authority.
                if (burned <= 0 || maximum != 0 || augmented != 0m
                    || augmentedMaximum != 0 || value != 0m)
                {
                    relevant = [];
                    costModifiers = [];
                    return false;
                }
                burnedEdgePoints = checked(burnedEdgePoints + burned);
            }
            ordinal++;
        }

        relevant = resolved;
        costModifiers = costs;
        return true;
    }

    internal static XElement ResolveExactAttribute(
        XElement root,
        CharacterCareerAttributeIdentity identity)
    {
        XElement attributes = CareerActiveSkillAdvanceEditorProjector.RequireSingle(
            root,
            "attributes",
            "The saved runner must have one <attributes> container.");
        XElement[] matches = attributes.Elements("attribute")
            .Where(candidate => string.Equals(
                ReadOptionalText(candidate, "name", string.Empty),
                identity.Abbreviation,
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                "The selected attribute identity is ambiguous or missing.");
    }

    internal static XElement? ResolveSingleImprovements(XElement root)
    {
        XElement[] matches = root.Elements("improvements").Take(2).ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                "The saved runner has duplicate <improvements> containers.")
        };
    }

    private static int ResolveTotalMinimum(
        int rawMinimum,
        int totalMaximum,
        bool critter,
        string abbreviation)
    {
        if (rawMinimum >= 1)
        {
            return rawMinimum;
        }
        return critter
            || totalMaximum == 0
            || abbreviation is "EDG" or "MAG" or "MAGAdept" or "RES" or "DEP"
            ? 0
            : 1;
    }


    internal static int ReadRequiredNonNegativeInt(
        XElement parent,
        string name,
        string owner)
    {
        int value = ReadRequiredInt(parent, name, owner);
        return value >= 0
            ? value
            : throw new InvalidOperationException(
                $"{owner} has a negative <{name}> value.");
    }

    internal static int ReadRequiredInt(XElement parent, string name, string owner)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        return values.Length == 1
            && int.TryParse(
                values[0].Value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
            ? value
            : throw new InvalidOperationException(
                $"{owner} has an invalid or duplicate <{name}> value.");
    }

    internal static decimal ReadRequiredDecimal(XElement parent, string name, string owner)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        return values.Length == 1
            && decimal.TryParse(
                values[0].Value.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal value)
            ? value
            : throw new InvalidOperationException(
                $"{owner} has an invalid or duplicate <{name}> value.");
    }

    internal static bool ReadOptionalBool(XElement parent, string name, bool fallback)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length == 0
            || values.Length == 1 && string.IsNullOrWhiteSpace(values[0].Value))
        {
            return fallback;
        }
        return values.Length == 1
            && CareerSettingsProfileResolver.TryParseBoolean(values[0].Value.Trim(), out bool value)
            ? value
            : throw new InvalidOperationException(
                $"The saved authority has an invalid or duplicate <{name}> value.");
    }

    internal static string ReadOptionalText(XElement parent, string name, string fallback)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        return values.Length switch
        {
            0 => fallback,
            1 => values[0].Value.Trim(),
            _ => throw new InvalidOperationException(
                $"The saved authority has duplicate <{name}> values.")
        };
    }

    private static int StandardRound(decimal value)
        => decimal.ToInt32(value >= 0m ? decimal.Ceiling(value) : decimal.Floor(value));

    private static string Digest(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
