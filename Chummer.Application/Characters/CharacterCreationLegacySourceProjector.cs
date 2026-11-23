using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Reproduces the bounded, prompt-free Chummer5 Quality.Create/Save and Gear.Create/Save
/// shapes accepted by the typed SR5 creation authorities. Unsupported source semantics fail
/// closed; callers never synthesize a partial legacy instance from display-only catalog data.
/// </summary>
public static partial class CharacterCreationLegacySourceProjector
{
    private const string DefaultNotesColor = "#000000";

    private static readonly IReadOnlySet<string> s_QualitySourceChildren =
        new HashSet<string>(
        [
            "id", "name", "translate", "karma", "category", "limit", "nolevels",
            "metagenic", "metagenetic", "altnotes", "notes", "notesColor",
            "doublecareer", "canbuywithspellpoints", "print", "implemented",
            "contributetobp", "contributetolimit", "stagedpurchase", "source", "page",
            "altpage", "mutant", "bonus", "firstlevelbonus", "naturalweapons",
            "careeronly", "onlyprioritygiven"
        ],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> s_GearSourceChildren =
        new HashSet<string>(
        [
            "id", "name", "translate", "category", "avail", "capacity", "armorcapacity",
            "costfor", "cost", "weight", "ratinglabel", "rating", "minrating",
            "altnotes", "notes", "notesColor", "devicerating", "matrixcmbonus", "source",
            "page", "altpage", "canformpersona", "ammoforweapontype",
            "childcostmultiplier", "childavailmodifier", "allowrename", "stolen",
            "isflechetteammo", "attributearray", "attack", "sleaze", "dataprocessing",
            "firewall", "modattack", "modsleaze", "moddataprocessing", "modfirewall",
            "modattributearray", "programs"
        ],
        StringComparer.Ordinal);

    public static bool IsQualitySourceProjectable(string sourceNodeXml)
        => IsQualitySourceProjectable(sourceNodeXml, armCount: null);

    /// <summary>
    /// Checks an original quality source against an explicitly resolved creation
    /// anatomy. Callers must bind the arm count to the selected metatype and all
    /// active limb effects; absent anatomy never means two arms.
    /// </summary>
    public static bool IsQualitySourceProjectable(string sourceNodeXml, int? armCount)
        => IsQualitySourceProjectable(sourceNodeXml, armCount, null);

    public static bool IsQualitySourceProjectable(string sourceNodeXml, int? armCount, string? sourceExecutionSchema)
    {
        if (sourceNodeXml is not
            { Length: > 0 and <= CharacterCreationQualitiesRules.MaximumSourceNodeLength })
            return false;
        XElement? source = ParseBoundedSource(
            sourceNodeXml,
            "quality",
            s_QualitySourceChildren);
        return source is not null && TryReadQualityDefinition(source, out var definition, armCount, sourceExecutionSchema)
            // Ordinary creation has no selected-quality point/wallet consumer.
            && !definition.BonusEffects.Concat(definition.FirstLevelEffects).Any(IsKnowledgeCategoryEffect);
    }

    public static bool IsGearSourceProjectable(string sourceNodeXml)
    {
        if (sourceNodeXml is not
            { Length: > 0 and <= CharacterCreationGearRules.MaximumSourceNodeLength })
            return false;
        XElement? source = ParseBoundedSource(sourceNodeXml, "gear", s_GearSourceChildren);
        return source is not null && TryReadGearDefinition(source, out _);
    }

    internal static bool TryBuildQualityGraph(
        CharacterCreationQualitySelection selection,
        string draftDigest,
        out XElement[] qualities,
        out XElement[] improvements)
        => TryBuildQualityGraph(selection, draftDigest, out qualities, out improvements, selection?.ResolvedArmCount);

    internal static bool TryBuildQualityGraph(
        CharacterCreationQualitySelection selection,
        string draftDigest,
        out XElement[] qualities,
        out XElement[] improvements,
        int? armCount, string? sourceExecutionSchema = null)
        => TryBuildQualityGraph(selection, draftDigest, out qualities, out improvements, armCount, sourceExecutionSchema, false);

    internal static bool TryBuildQualityGraph(
        CharacterCreationQualitySelection selection,
        string draftDigest,
        out XElement[] qualities,
        out XElement[] improvements,
        int? armCount, string? sourceExecutionSchema, bool lifestyleSourceMode)
    {
        qualities = [];
        improvements = [];
        if (selection?.SourceExecutionSchema == CharacterCreationKarmaQualityPrompts.SourceSchema)
        {
            if (!CharacterCreationKarmaQualityPrompts.TryPrepare(selection, out var plain, out var answer)
                || !TryBuildQualityGraph(plain, draftDigest, out qualities, out improvements, armCount, sourceExecutionSchema, lifestyleSourceMode)) return false;
            var originalSource = XElement.Parse(selection.SourceNodeXml);
            foreach (var quality in qualities)
            {
                quality.SetElementValue("extra", answer);
                // Quality.Save retains the original bonus, including its answered selecttext.
                quality.Element("bonus")!.ReplaceWith(new XElement(originalSource.Element("bonus")!));
            }
            return true;
        }
        if (selection?.SourceExecutionSchema is not null && selection.SourceExecutionSchema != sourceExecutionSchema
            || !CharacterCreationQualitiesRules.IsCanonicalDigest(draftDigest)
            || !TryReadSelectedQualityDefinition(selection, armCount,
                out XElement source, out QualityDefinition definition, lifestyleSourceMode))
            return false;

        var savedQualities = new List<XElement>(selection.Rating);
        var savedImprovements = new List<XElement>();
        for (int level = 1; level <= selection.Rating; level++)
        {
            string qualityId = StableGuid(
                $"quality:{selection.OptionId}:{selection.SourceNodeDigest}:{draftDigest}:{level}")
                .ToString("D", CultureInfo.InvariantCulture);
            savedQualities.Add(BuildSavedQuality(source, definition, qualityId));
            foreach (CompiledEffect effect in definition.BonusEffects)
                savedImprovements.Add(BuildImprovement(effect, qualityId, definition.NotesColor));
            if (level == 1)
            {
                foreach (CompiledEffect effect in definition.FirstLevelEffects)
                    savedImprovements.Add(BuildImprovement(effect, qualityId, definition.NotesColor));
            }
        }

        qualities = savedQualities.ToArray();
        improvements = savedImprovements.ToArray();
        return true;
    }

    internal static bool TryBuildGear(
        CharacterCreationGearLine line,
        string draftDigest,
        out XElement gear)
    {
        gear = new XElement("gear");
        if (line?.Focus is not null)
            return CharacterCreationGearRules.IsCanonicalDigest(draftDigest) && TryBuildFocusGear1233(line, out gear);
        if (line is null
            || line.SourceNodeXml is not
                { Length: > 0 and <= CharacterCreationGearRules.MaximumSourceNodeLength }
            || !CharacterCreationGearRules.DigestsEqual(
                line.SourceNodeDigest,
                CharacterCreationGearRules.ComputeSourceNodeDigest(line.SourceNodeXml))
            || !CharacterCreationGearRules.IsCanonicalDigest(draftDigest))
            return false;
        XElement? source = ParseBoundedSource(line.SourceNodeXml, "gear", s_GearSourceChildren);
        if (source is null
            || !TryReadGearDefinition(source, out GearDefinition definition)
            || definition.SourceId != line.SourceId
            || !string.Equals(definition.Name, line.Name, StringComparison.Ordinal)
            || !string.Equals(definition.Category, line.Category, StringComparison.Ordinal)
            || definition.PackageCost != line.PackageCost
            || definition.PackageQuantity != line.PackageQuantity
            || definition.Availability != line.Availability
            || !string.Equals(definition.Legality, line.Legality, StringComparison.Ordinal)
            || !string.Equals(definition.SourceBook, line.SourceBook, StringComparison.Ordinal)
            || !string.Equals(definition.Page, line.Page, StringComparison.Ordinal)
            || line.Quantity <= 0)
            return false;

        decimal expectedTotal;
        try
        {
            expectedTotal = checked(line.PackageCost * line.Quantity / line.PackageQuantity);
        }
        catch (OverflowException)
        {
            return false;
        }
        if (expectedTotal != line.TotalCost)
            return false;

        string gearId = StableGuid(
            $"gear:{line.OptionId}:{line.SourceNodeDigest}:{draftDigest}")
            .ToString("D", CultureInfo.InvariantCulture);
        gear = BuildSavedGear(definition, gearId, line.Quantity);
        return true;
    }

    internal static bool TryBuildGrantedGearInstance(CharacterCreationTalentGearSource input,
        string gearId, string qualityId, out XElement saved)
    {
        saved = new XElement("gear");
        if (!CharacterCreationTalentQualitySourceRules.IsValidGearSource(input)) return false;
        XElement source = XElement.Parse(input.CanonicalSourceXml);
        // Catalog visibility and permitted accessory categories are not saved
        // instance fields. Validate them before removing them from this projection.
        if (source.Nodes().OfType<XText>().Any(item => !string.IsNullOrWhiteSpace(item.Value))
            || source.Elements("hide").Any(item => item.HasAttributes || item.HasElements || !string.IsNullOrWhiteSpace(item.Value))
            || source.Elements("hide").Count() > 1
            || source.Elements("addoncategory").Any(item => item.HasAttributes || item.HasElements || string.IsNullOrWhiteSpace(item.Value)))
            return false;
        source.Elements("hide").Remove();
        source.Elements("addoncategory").Remove();
        if (ParseBoundedSource(source.ToString(SaveOptions.DisableFormatting), "gear", s_GearSourceChildren) is null
            || !TryReadGearDefinition(source, out var definition))
            return false;
        saved = BuildSavedGear(definition, gearId, 1);
        // addgear without fullcost owns a free instance; retain original cost
        // only in its independently validated source payload, not purchase budget.
        saved.Element("cost")!.Value = "0";
        saved.Element("parentid")!.Value = qualityId;
        return true;
    }

    // Instance serialization only. The caller must first compile and validate every
    // original bonus and restriction. Keep the purchased-quality path unchanged.
    internal static bool TryBuildHeritageQualityInstance(CharacterCreationTalentQualitySource input,
        string qualityId, string extra, out XElement saved)
        => TryBuildGrantedQualityInstance(input, qualityId, extra, "Heritage", out saved);

    // SR5 Career only. This closure covers the ordinary quality compiler and the
    // complete literal grant compiler (including Adept). Neither currently compiles
    // AddLimb. Unknown contributors remain unresolved, rather than being dropped.
    internal static bool TryResolveCareerArmCount(string characterXml,
        CharacterCareerQualityAnatomySources evidence, out int armCount)
    {
        armCount = 0;
        try
        {
            if (evidence.Schema != CharacterCareerQualityAnatomySources.Sr5CareerV1
                || evidence.Digest != CharacterCareerQualityAnatomySources.ComputeDigest(evidence)
                || evidence.Qualities is null || evidence.Qualities.Any(source =>
                    !CharacterCreationTalentQualitySourceRules.IsValidSource(source))
                || evidence.Qualities.Select(source => source.SourceId).Distinct(StringComparer.Ordinal).Count()
                    != evidence.Qualities.Count) return false;
            using var reader = XmlReader.Create(new StringReader(characterXml), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = CharacterCareerReputationProjector.MaximumCharacterXmlLength });
            var root = XElement.Load(reader);
            if (root.Name != "character" || root.HasAttributes
                || evidence.CharacterDigest != CharacterCreationQualitiesRules.ComputeSourceNodeDigest(root.ToString(SaveOptions.DisableFormatting))
                || !bool.TryParse(Scalar(root, "created"), out bool created) || !created
                || Scalar(root, "gameedition") != "SR5"
                || Scalar(root, "buildmethod") != evidence.BuildMethod || Scalar(root, "settings") != evidence.SettingsProfileId
                || Scalar(root, "metatypeguid") != evidence.MetatypeId
                || Scalar(root, "metavariantguid", false) != evidence.MetavariantId
                || Scalar(root, "metavariant", false) != evidence.MetavariantName
                || evidence.MetavariantName is not ("" or "None")
                || evidence.MetavariantId.Length != 0 && (!Guid.TryParseExact(evidence.MetavariantId, "D", out var variant)
                    || variant != Guid.Empty)) return false;
            var metatype = XElement.Parse(evidence.MetatypeXml);
            if (metatype.Name != "metatype" || metatype.HasAttributes
                || Scalar(metatype, "id") != evidence.MetatypeId
                || Scalar(metatype, "name") != Scalar(root, "metatype")) return false;
            // Nested metavariants are not the selected base metatype. Intrinsic/racial
            // grants require their own full graph closure; do not infer their absence.
            foreach (string name in new[] { "bonus", "qualities", "powers" })
                if (!Empty(metatype, name)) return false;
            foreach (string name in new[] { "cyberwares", "biowares", "gears", "weapons", "armors", "vehicles",
                "powers", "spells", "complexforms", "critterpowers", "martialarts", "programs", "aiprograms",
                "initiationgrades", "metamagics", "arts", "enhancements", "drugs", "lifemodules", "lifestyles" })
                if (!Empty(root, name)) return false;
            var qualities = Container(root, "qualities", "quality");
            var improvements = Container(root, "improvements", "improvement");
            if (qualities.Length > CharacterCareerReputationProjector.MaximumRowsPerContainer
                || improvements.Length > CharacterCareerReputationProjector.MaximumRowsPerContainer) return false;
            var ids = new HashSet<Guid>();
            var usedSources = new HashSet<string>(StringComparer.Ordinal);
            var expectedEffects = new List<XElement>();
            var flags = new HashSet<string>(StringComparer.Ordinal);
            var firstLevels = new Dictionary<string, int>(StringComparer.Ordinal);
            var requiresFirstLevel = new HashSet<string>(StringComparer.Ordinal);
            // Character.LimbCount("arm"): 1 + StandardRound(ValueOf(AddLimb, false, "arm")) + 1.
            // The candidate is released only after proving the complete supported graph
            // contains no AddLimb. No raw saved values/precedence rows are summed here.
            int candidate = 1 + (int)Math.Round(0m, MidpointRounding.AwayFromZero) + 1;
            foreach (var row in qualities)
            {
                if (!Guid.TryParseExact(Scalar(row, "guid"), "D", out var id) || id == Guid.Empty || !ids.Add(id)) return false;
                string sourceId = Scalar(row, "sourceid");
                var source = evidence.Qualities.SingleOrDefault(item => item.SourceId == sourceId);
                if (source is null || source.ForcedSelection.Length != 0 || source.GrantedGearSources is not { Count: 0 }) return false;
                usedSources.Add(sourceId);
                string origin = Scalar(row, "qualitysource");
                if (!Enum.TryParse<CharacterCareerQualityOrigin>(origin, out var parsedOrigin)
                    || !Enum.IsDefined(parsedOrigin) || parsedOrigin.ToString() != origin) return false;
                var owned = improvements.Where(effect => Scalar(effect, "sourcename") == id.ToString("D")).ToArray();
                XElement expected;
                if (TryBuildCareerQualityInstance(source.CanonicalSourceXml, Guid.Parse(sourceId), id, candidate,
                        false, out _, out expected, out var regular))
                {
                    if (!TryBuildCareerQualityInstance(source.CanonicalSourceXml, Guid.Parse(sourceId), id, candidate,
                            true, out _, out _, out var first)) return false;
                    if (first.Length != regular.Length) requiresFirstLevel.Add(sourceId);
                    if (SameEffects(owned, regular)) expectedEffects.AddRange(regular);
                    else if (SameEffects(owned, first))
                    {
                        firstLevels[sourceId] = firstLevels.GetValueOrDefault(sourceId) + 1;
                        expectedEffects.AddRange(first);
                    }
                    else return false;
                }
                else
                {
                    var effects = new List<XElement>();
                    var gears = new List<(XElement Saved, CharacterCreationTalentGearSource Source)>();
                    var definition = XElement.Parse(source.CanonicalSourceXml);
                    string extra = CharacterCreationAwakenedLegacyProjector.CompileBonus(definition.Element("bonus"),
                        string.Empty, [], source, id.ToString("D"), flags, effects, gears);
                    if (gears.Count != 0 || !TryBuildGrantedQualityInstance(source, id.ToString("D"), extra, origin, out expected)
                        || !SameEffects(owned, effects)) return false;
                    expectedEffects.AddRange(effects);
                }
                // Same cosmetic allowances as CareerQualityGraph; every rule-bearing
                // field is compared, and duplicates/unknown payloads are not ignored.
                if (row.HasAttributes || row.Elements().Any(node => node.Name.Namespace != XNamespace.None)
                    || row.Elements().GroupBy(node => node.Name).Any(group => group.Count() != 1)
                    || row.Elements().Any(node => expected.Element(node.Name) is null)) return false;
                foreach (var node in expected.Elements().Where(node => node.Name.LocalName is not
                    ("notes" or "notesColor" or "print" or "qualitysource" or "contributetolimit")))
                    if (!XNode.DeepEquals(Normalize(Single(row, node.Name.LocalName)), Normalize(node))) return false;
            }
            if (usedSources.Count != evidence.Qualities.Count
                || requiresFirstLevel.Any(source => firstLevels.GetValueOrDefault(source) != 1)
                || !SameEffects(improvements, expectedEffects)
                || expectedEffects.Any(effect => Scalar(effect, "improvementttype") == "AddLimb")) return false;
            foreach (string name in new[] { "magenabled", "resenabled", "depenabled", "adept", "magician", "technomancer", "ai" })
            {
                string value = Scalar(root, name, false);
                if (value.Length == 0 ? flags.Contains(name) : !bool.TryParse(value, out bool enabled) || enabled != flags.Contains(name))
                    return false;
            }
            armCount = candidate;
            return true;
        }
        catch (Exception error) when (error is XmlException or InvalidDataException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException) { return false; }

        static XElement Single(XElement parent, string name)
        {
            var nodes = parent.Elements().Where(node => node.Name.LocalName == name).Take(2).ToArray();
            return nodes.Length == 1 && nodes[0].Name == name ? nodes[0] : throw new InvalidDataException();
        }
        static string Scalar(XElement parent, string name, bool required = true)
        {
            if (!required && !parent.Elements().Any(node => node.Name.LocalName == name)) return string.Empty;
            var node = Single(parent, name);
            if (node.HasAttributes || node.HasElements || node.Value != node.Value.Trim()) throw new InvalidDataException();
            return node.Value;
        }
        static bool Empty(XElement parent, string name)
        {
            if (!parent.Elements().Any(node => node.Name.LocalName == name)) return true;
            var node = Single(parent, name);
            return !node.HasAttributes && !node.HasElements && string.IsNullOrWhiteSpace(node.Value);
        }
        static XElement[] Container(XElement parent, string name, string child)
        {
            var node = Single(parent, name);
            if (node.HasAttributes || node.Elements().Any(row => row.Name != child)
                || node.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))) throw new InvalidDataException();
            return node.Elements().ToArray();
        }
        static XElement Normalize(XElement node)
        {
            var copy = new XElement(node);
            foreach (var container in copy.DescendantsAndSelf().Where(item => item.HasElements))
                container.Nodes().OfType<XText>().Where(text => string.IsNullOrWhiteSpace(text.Value)).Remove();
            return copy;
        }
        static bool SameEffects(IEnumerable<XElement> actual, IEnumerable<XElement> expected)
        {
            string Key(XElement node)
            {
                var copy = Normalize(node);
                copy.Elements("notes").Remove(); copy.Elements("notesColor").Remove();
                return copy.ToString(SaveOptions.DisableFormatting);
            }
            return actual.Select(Key).Order(StringComparer.Ordinal).SequenceEqual(expected.Select(Key).Order(StringComparer.Ordinal));
        }
    }

    internal static bool TryBuildGrantedQualityInstance(CharacterCreationTalentQualitySource input,
        string qualityId, string extra, string origin, out XElement saved)
        => TryBuildGrantedQualityInstanceCore8590(input, qualityId, extra, origin, out saved, null);

    internal static bool TryBuildLifeModuleRacialQualityInstance8590(CharacterCreationTalentQualitySource input,
        string qualityId, CharacterCreationMetatypeOptionProjection metatype, out XElement saved)
        => TryBuildGrantedQualityInstanceCore8590(input, qualityId, string.Empty, "Metatype", out saved, metatype);

    private static bool TryBuildGrantedQualityInstanceCore8590(CharacterCreationTalentQualitySource input,
        string qualityId, string extra, string origin, out XElement saved,
        CharacterCreationMetatypeOptionProjection? lifeModuleRacialMetatype)
    {
        saved = new XElement("quality");
        if (origin is not ("Heritage" or "Metatype" or "Selected")
            || !CharacterCreationTalentQualitySourceRules.IsValidSource(input)) return false;
        XElement source = XElement.Parse(input.CanonicalSourceXml);
        XElement metadata = new(source);
        metadata.Elements("bonus").Remove();
        metadata.Elements("forbidden").Remove();
        metadata.Elements("nameonpage").Remove();
        if (metadata.Elements("required").Any())
        {
            // Only the bound automatic-grant path can separate the exact SURGE
            // purchase gate from saved metadata. Purchased/Heritage callers keep
            // the original strict behavior; the original source stays intact.
            if (origin != "Metatype" || lifeModuleRacialMetatype is null
                || !CharacterCreationLifeModuleRacialGrants8590.IsPurchaseOnlySurgeGrant(lifeModuleRacialMetatype, input))
                return false;
            metadata.Elements("required").Remove();
        }
        // Hidden catalog rows can be compulsory racial qualities. Hiding is
        // display metadata, never a reason to discard the original bonus.
        if (metadata.Elements("hide").Count() > 1 || metadata.Elements("hide").Any(item =>
                item.HasAttributes || item.HasElements || !string.IsNullOrWhiteSpace(item.Value)))
            return false;
        metadata.Elements("hide").Remove();
        if (source.Elements("bonus").Count() > 1 || source.Elements("forbidden").Count() > 1
            || source.Elements("nameonpage").Count() > 1
            || ParseBoundedSource(metadata.ToString(SaveOptions.DisableFormatting), "quality", s_QualitySourceChildren) is null
            || !TryReadQualityDefinition(metadata, out var definition)
            || definition.FirstLevelEffects.Count != 0)
            return false;
        saved = BuildSavedQuality(source, definition, qualityId);
        saved.Element("qualitysource")!.Value = origin;
        // Character.Create explicitly removes racial grants from quality limits;
        // BP and removal/career identity still retain the source's original value.
        if (origin == "Metatype") saved.Element("contributetolimit")!.Value = "False";
        saved.Element("extra")!.Value = extra;
        return true;
    }

    private static XElement? ParseBoundedSource(
        string sourceNodeXml,
        string expectedName,
        IReadOnlySet<string> allowedChildren)
    {
        try
        {
            XElement source = XElement.Parse(sourceNodeXml ?? string.Empty, LoadOptions.None);
            if (source.Name.NamespaceName.Length != 0
                || !string.Equals(source.Name.LocalName, expectedName, StringComparison.Ordinal)
                || source.HasAttributes
                || source.Elements().Any(child => child.Name.NamespaceName.Length != 0)
                || source.Elements().Any(child => !allowedChildren.Contains(child.Name.LocalName))
                || source.Elements().Any(child => child.Name.LocalName
                       is not ("bonus" or "firstlevelbonus" or "naturalweapons")
                       && (child.HasAttributes || child.Elements().Any()))
                || source.Elements().GroupBy(child => child.Name.LocalName, StringComparer.Ordinal)
                    .Any(group => group.Count() > 1))
                return null;
            return source;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or InvalidOperationException
                                          or XmlException)
        {
            return null;
        }
    }

    private static bool TryReadQualityDefinition(
        XElement source,
        out QualityDefinition definition,
        int? armCount = null, string? sourceExecutionSchema = null, bool lifestyleSourceMode = false,
        bool importedCareerEffects5698 = false)
    {
        definition = null!;
        if (!TryReadGuid(source, "id", out Guid sourceId)
            || !TryReadRequiredScalar(source, "name", out string name)
            || !TryReadRequiredScalar(source, "category", out string category)
            || category is not ("Positive" or "Negative")
            || !TryReadRequiredInt(source, "karma", out int karma)
            || category == "Positive" && karma < 0
            || category == "Negative" && karma > 0
            || !TryReadRequiredScalar(source, "source", out string sourceBook)
            || !TryReadRequiredScalar(source, "page", out string page)
            || !TryReadBoolean(source, "implemented", true, out bool implemented)
            || !TryReadBoolean(source, "contributetobp", true, out bool contributeToBp)
            || !TryReadBoolean(source, "contributetolimit", true, out bool contributeToLimit)
            || !TryReadBoolean(source, "stagedpurchase", false, out bool stagedPurchase)
            || !TryReadBoolean(source, "doublecareer", true, out bool doubleCareer)
            || !TryReadBoolean(source, "canbuywithspellpoints", false, out bool spellPoints)
            || !TryReadBoolean(source, "print", true, out bool print)
            || !TryReadBooleanAlias(source, "metagenic", "metagenetic", out bool metagenic)
            || !TryReadMaximumRating(source, out int maximumRating, armCount)
            || !TryReadEffectContainer(source.Element("bonus"), out CompiledEffect[] bonusEffects, sourceExecutionSchema,
                lifestyleSourceMode, importedCareerEffects5698)
            || !TryReadEffectContainer(
                source.Element("firstlevelbonus"),
                out CompiledEffect[] firstLevelEffects, sourceExecutionSchema, lifestyleSourceMode, importedCareerEffects5698)
            || source.Element("naturalweapons") is XElement naturalWeapons
               && (naturalWeapons.HasAttributes
                   || naturalWeapons.Elements().Any()
                   || !string.IsNullOrWhiteSpace(naturalWeapons.Value)))
            return false;

        string notes = ReadOptionalScalar(source, "altnotes")
                       ?? ReadOptionalScalar(source, "notes")
                       ?? string.Empty;
        string notesColor = ReadOptionalScalar(source, "notesColor") ?? DefaultNotesColor;
        if (string.IsNullOrWhiteSpace(notesColor)
            || !string.Equals(notesColor, notesColor.Trim(), StringComparison.Ordinal))
            return false;
        definition = new QualityDefinition(
            sourceId,
            name,
            category == "Positive"
                ? CharacterCreationQualityType.Positive
                : CharacterCreationQualityType.Negative,
            karma,
            maximumRating,
            implemented,
            contributeToBp,
            contributeToLimit,
            stagedPurchase,
            doubleCareer,
            spellPoints,
            metagenic,
            print,
            source.Element("mutant") is not null,
            sourceBook,
            page,
            notes,
            notesColor,
            bonusEffects,
            firstLevelEffects);
        return true;
    }

    private static bool TryReadGearDefinition(XElement source, out GearDefinition definition)
    {
        definition = null!;
        if (!TryReadGuid(source, "id", out Guid sourceId)
            || !TryReadRequiredScalar(source, "name", out string name)
            || !TryReadRequiredScalar(source, "category", out string category)
            || !string.Equals(ReadOptionalScalar(source, "rating"), "0", StringComparison.Ordinal)
            || source.Element("minrating") is not null
            || !TryReadRequiredDecimal(source, "cost", out decimal cost)
            || cost < 0m
            || !TryReadAvailability(source, out string availabilityExpression, out int availability,
                out string legality)
            || !TryReadRequiredScalar(source, "source", out string sourceBook)
            || !TryReadRequiredScalar(source, "page", out string page)
            || !TryReadOptionalPositiveInt(source, "costfor", 1, out int packageQuantity)
            || !TryReadOptionalInt(source, "matrixcmbonus", 0, out int matrixCmBonus)
            || !TryReadOptionalInt(source, "childcostmultiplier", 1,
                out int childCostMultiplier)
            || childCostMultiplier <= 0
            || !TryReadOptionalInt(source, "childavailmodifier", 0,
                out int childAvailabilityModifier)
            || !TryReadBoolean(source, "allowrename", false, out bool allowRename)
            || !TryReadBoolean(source, "stolen", false, out bool stolen)
            || !TryReadBoolean(source, "isflechetteammo", false, out bool isFlechetteAmmo))
            return false;

        string attributeArray = ReadOptionalScalar(source, "attributearray") ?? string.Empty;
        string attack = ReadOptionalScalar(source, "attack") ?? string.Empty;
        string sleaze = ReadOptionalScalar(source, "sleaze") ?? string.Empty;
        string dataProcessing = ReadOptionalScalar(source, "dataprocessing") ?? string.Empty;
        string firewall = ReadOptionalScalar(source, "firewall") ?? string.Empty;
        bool canSwapAttributes = attributeArray.Length != 0;
        if (canSwapAttributes)
        {
            string[] values = attributeArray.Split(',');
            if (values.Length != 4 || values.Any(string.IsNullOrWhiteSpace))
                return false;
            attack = values[0];
            sleaze = values[1];
            dataProcessing = values[2];
            firewall = values[3];
        }

        string notes = ReadOptionalScalar(source, "altnotes")
                       ?? ReadOptionalScalar(source, "notes")
                       ?? string.Empty;
        string notesColor = ReadOptionalScalar(source, "notesColor") ?? DefaultNotesColor;
        if (string.IsNullOrWhiteSpace(notesColor)
            || !string.Equals(notesColor, notesColor.Trim(), StringComparison.Ordinal))
            return false;

        definition = new GearDefinition(
            sourceId,
            name,
            category,
            ReadOptionalScalar(source, "capacity") ?? string.Empty,
            ReadOptionalScalar(source, "armorcapacity") ?? string.Empty,
            availabilityExpression,
            availability,
            legality,
            packageQuantity,
            cost,
            ReadOptionalScalar(source, "cost")!,
            ReadOptionalScalar(source, "weight") ?? string.Empty,
            sourceBook,
            page,
            isFlechetteAmmo,
            ReadOptionalScalar(source, "ammoforweapontype") ?? string.Empty,
            ReadOptionalScalar(source, "canformpersona") ?? string.Empty,
            ReadOptionalScalar(source, "devicerating") ?? string.Empty,
            matrixCmBonus,
            allowRename,
            stolen,
            childCostMultiplier,
            childAvailabilityModifier,
            notes,
            notesColor,
            ReadOptionalScalar(source, "programs") ?? string.Empty,
            attack,
            sleaze,
            dataProcessing,
            firewall,
            attributeArray,
            ReadOptionalScalar(source, "modattack") ?? string.Empty,
            ReadOptionalScalar(source, "modsleaze") ?? string.Empty,
            ReadOptionalScalar(source, "moddataprocessing") ?? string.Empty,
            ReadOptionalScalar(source, "modfirewall") ?? string.Empty,
            ReadOptionalScalar(source, "modattributearray") ?? string.Empty,
            canSwapAttributes);
        return true;
    }

    private static bool TryReadEffectContainer(
        XElement? container,
        out CompiledEffect[] effects, string? sourceExecutionSchema = null, bool lifestyleSourceMode = false,
        bool importedCareerEffects5698 = false)
    {
        effects = [];
        if (container is null)
            return true;
        if (container.HasAttributes
            || container.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
            return false;
        var compiled = new List<CompiledEffect>();
        foreach (XElement effect in container.Elements())
        {
            if (IsMatrixDiceEffect(effect.Name.LocalName))
            {
                if (sourceExecutionSchema != CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
                    || !TryReadMatrixDiceEffect(effect, out var matrix)) return false;
                compiled.Add(matrix);
                continue;
            }
            if (IsPhysicalInitiativeEffect886(effect.Name.LocalName))
            {
                if (sourceExecutionSchema != CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
                    || !TryReadPhysicalInitiativeEffect886(effect, out var initiative)) return false;
                compiled.Add(initiative);
                continue;
            }
            bool modeCost = lifestyleSourceMode && effect.Name.LocalName is "lifestylecost" or "basiclifestylecost";
            if (effect.Name.NamespaceName.Length != 0
                || effect.HasAttributes && (!modeCost || effect.Attributes().Any(attribute => attribute.Name != "condition")))
                return false;
            string lifestyleCondition = modeCost ? effect.Attribute("condition")?.Value ?? "" : "";
            if (modeCost && !CharacterCreationLifestyleImprovementRules.IsSupportedSourceCondition(lifestyleCondition))
                return false;
            if (IsKnowledgeCategoryElement(effect.Name.LocalName))
            {
                if (container.Elements(effect.Name).Count(other => XElement.DeepEquals(other, effect)) != 1
                    || !TryReadKnowledgeCategoryEffect(effect, out var structured)) return false;
                compiled.Add(structured);
                continue;
            }
            if (effect.Elements().Any()) return false;
            if (effect.Name.LocalName is "lifestylecost" or "basiclifestylecost")
            {
                // One literal purchased-quality adjustment of each type per source container.
                // Only explicit V7 callers admit the two source mode conditions; keep expressions and named effects,
                // reductions below -100 and ambiguous duplicate rows outside this consumer.
                string literal = effect.Value.Trim();
                if (container.Elements(effect.Name).Count() != 1
                    || !decimal.TryParse(literal,
                        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out decimal percentage)
                    || percentage < -100m
                    // Decimal parsing can round an over-precise value below -100 up to -100.
                    || percentage == -100m && literal.TrimStart('-', '0').StartsWith("100.", StringComparison.Ordinal)
                        && literal[(literal.IndexOf('.') + 1)..].Any(digit => digit != '0')) return false;
                compiled.Add(new CompiledEffect(effect.Name.LocalName == "basiclifestylecost"
                    ? "BasicLifestyleCost" : "LifestyleCost", string.Empty, percentage, Condition: lifestyleCondition));
                continue;
            }
            if (effect.Name.LocalName == "trustfund")
            {
                if (!int.TryParse(effect.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out int level) || level is < 1 or > 4) return false;
                compiled.Add(new CompiledEffect("TrustFund", string.Empty, level));
                continue;
            }
            // V9's reconciled imported owner graph already consumes these native
            // effects. Serialize them through the same Career comparison/delta
            // kernel; never strip them to obtain a cost-only quality definition.
            if (importedCareerEffects5698 && effect.Name == "notoriety")
            {
                if (!int.TryParse(effect.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int notoriety)
                    || notoriety is < -CharacterCareerQualityRules.MaximumKarma or > CharacterCareerQualityRules.MaximumKarma)
                    return false;
                compiled.Add(new CompiledEffect("Notoriety", string.Empty, notoriety));
                continue;
            }
            if (importedCareerEffects5698 && effect.Name == "blockskillcategorydefaulting")
            {
                string category = effect.Value;
                if (string.IsNullOrWhiteSpace(category) || category != category.Trim()
                    || category.Length > CharacterCareerQualityRules.MaximumTextLength
                    || container.Elements(effect.Name).Count(row => row.Value == category) != 1) return false;
                compiled.Add(new CompiledEffect("BlockSkillCategoryDefault", category, 0));
                continue;
            }
            if (!string.IsNullOrWhiteSpace(effect.Value)) return false;
            string improvementType = effect.Name.LocalName switch
            {
                "ambidextrous" => "Ambidextrous",
                "friendsinhighplaces" => "FriendsInHighPlaces",
                "erased" => "Erased",
                "overclocker" => "Overclocker",
                _ => string.Empty
            };
            if (improvementType.Length == 0)
                return false;
            compiled.Add(new CompiledEffect(improvementType, string.Empty, 0m));
        }
        effects = compiled.ToArray();
        return true;
    }

    private static XElement BuildSavedQuality(
        XElement source,
        QualityDefinition definition,
        string qualityId) => new(
        "quality",
        new XElement("sourceid", definition.SourceId.ToString("D", CultureInfo.InvariantCulture)),
        new XElement("guid", qualityId),
        new XElement("name", definition.Name),
        new XElement("extra", string.Empty),
        new XElement("bp", definition.Karma.ToString(CultureInfo.InvariantCulture)),
        new XElement("implemented", LegacyBoolean(definition.Implemented)),
        new XElement("contributetobp", LegacyBoolean(definition.ContributeToBuildPoints)),
        new XElement("contributetolimit", LegacyBoolean(definition.ContributeToLimit)),
        new XElement("stagedpurchase", LegacyBoolean(definition.StagedPurchase)),
        new XElement("doublecareer", LegacyBoolean(definition.DoubleCareer)),
        new XElement("canbuywithspellpoints", LegacyBoolean(definition.CanBuyWithSpellPoints)),
        new XElement("metagenic", LegacyBoolean(definition.Metagenic)),
        new XElement("print", LegacyBoolean(definition.Print)),
        new XElement("qualitytype", definition.Type.ToString()),
        new XElement("qualitysource", "Selected"),
        new XElement("mutant", LegacyBoolean(definition.Mutant)),
        new XElement("source", definition.SourceBook),
        new XElement("page", definition.Page),
        new XElement("sourcename", string.Empty),
        CloneContainer(source, "bonus"),
        CloneContainer(source, "firstlevelbonus"),
        CloneContainer(source, "naturalweapons"),
        new XElement("notes", definition.Notes),
        new XElement("notesColor", definition.NotesColor));

    private static XElement BuildSavedGear(
        GearDefinition definition,
        string gearId,
        int quantity)
    {
        var gear = new XElement(
            "gear",
            new XElement("sourceid", definition.SourceId.ToString("D", CultureInfo.InvariantCulture)),
            new XElement("guid", gearId),
            new XElement("name", definition.Name),
            new XElement("category", definition.Category),
            new XElement("capacity", definition.Capacity),
            new XElement("armorcapacity", definition.ArmorCapacity),
            new XElement("minrating", string.Empty),
            new XElement("maxrating", string.Empty),
            new XElement("rating", "0"),
            new XElement("qty", quantity.ToString(CultureInfo.InvariantCulture)),
            new XElement("avail", definition.AvailabilityExpression));
        if (definition.PackageQuantity > 1)
            gear.Add(new XElement("costfor", definition.PackageQuantity.ToString(CultureInfo.InvariantCulture)));
        gear.Add(
            new XElement("cost", definition.CostExpression),
            new XElement("weight", definition.Weight),
            new XElement("extra", string.Empty),
            new XElement("bonded", "False"),
            new XElement("equipped", "True"),
            new XElement("wirelesson", "False"),
            new XElement("stolen", LegacyBoolean(definition.Stolen)),
            new XElement("bonus", string.Empty),
            new XElement("wirelessbonus", string.Empty),
            new XElement("weaponbonus", string.Empty),
            new XElement("flechetteweaponbonus", string.Empty),
            new XElement("source", definition.SourceBook),
            new XElement("page", definition.Page),
            new XElement("isflechetteammo", LegacyBoolean(definition.IsFlechetteAmmo)),
            new XElement("ammoforweapontype", definition.AmmoForWeaponType),
            new XElement("canformpersona", definition.CanFormPersona),
            new XElement("devicerating", definition.DeviceRating),
            new XElement("gearname", string.Empty),
            new XElement("forcedvalue", string.Empty),
            new XElement("matrixcmfilled", "0"),
            new XElement("matrixcmbonus", definition.MatrixCmBonus.ToString(CultureInfo.InvariantCulture)),
            new XElement("parentid", string.Empty),
            new XElement("allowrename", LegacyBoolean(definition.AllowRename)));
        if (definition.ChildCostMultiplier != 1)
        {
            gear.Add(new XElement("childcostmultiplier",
                definition.ChildCostMultiplier.ToString(CultureInfo.InvariantCulture)));
        }
        if (definition.ChildAvailabilityModifier != 0)
        {
            gear.Add(new XElement("childavailmodifier",
                definition.ChildAvailabilityModifier.ToString(CultureInfo.InvariantCulture)));
        }
        gear.Add(
            new XElement("children"),
            new XElement("location", string.Empty),
            new XElement("notes", definition.Notes),
            new XElement("notesColor", definition.NotesColor),
            new XElement("discountedcost", "False"),
            new XElement("programlimit", definition.ProgramLimit),
            new XElement("overclocked", "None"),
            new XElement("attack", definition.Attack),
            new XElement("sleaze", definition.Sleaze),
            new XElement("dataprocessing", definition.DataProcessing),
            new XElement("firewall", definition.Firewall),
            new XElement("attributearray", definition.AttributeArray),
            new XElement("modattack", definition.ModAttack),
            new XElement("modsleaze", definition.ModSleaze),
            new XElement("moddataprocessing", definition.ModDataProcessing),
            new XElement("modfirewall", definition.ModFirewall),
            new XElement("modattributearray", definition.ModAttributeArray),
            new XElement("canswapattributes", LegacyBoolean(definition.CanSwapAttributes)),
            new XElement("active", "False"),
            new XElement("homenode", "False"),
            new XElement("sortorder", "0"));
        return gear;
    }

    private static XElement BuildImprovement(
        CompiledEffect effect,
        string sourceName,
        string notesColor) => new(
        "improvement",
        effect.Unique886 is null ? null : new XElement("unique", effect.Unique886),
        new XElement("target", string.Empty),
        new XElement("improvedname", effect.ImprovedName),
        new XElement("sourcename", sourceName),
        new XElement("min", effect.Minimum.ToString(CultureInfo.InvariantCulture)),
        new XElement("max", effect.Maximum.ToString(CultureInfo.InvariantCulture)),
        new XElement("aug", "0"),
        new XElement("augmax", "0"),
        new XElement("val", effect.Value.ToString(CultureInfo.InvariantCulture)),
        new XElement("rating", "1"),
        new XElement("exclude", string.Empty),
        new XElement("condition", effect.ImprovementType is "LifestyleCost" or "BasicLifestyleCost" ? effect.Condition : NativeCondition(effect)),
        new XElement("improvementttype", effect.ImprovementType),
        new XElement("improvementsource", "Quality"),
        new XElement("custom", "False"),
        new XElement("customname", string.Empty),
        new XElement("customid", string.Empty),
        new XElement("customgroup", string.Empty),
        new XElement("addtorating", "0"),
        new XElement("enabled", "1"),
        new XElement("order", "0"),
        new XElement("notes", string.Empty),
        new XElement("notesColor", notesColor));

    private static XElement CloneContainer(XElement source, string name) =>
        new(name, source.Element(name)?.Nodes());

    private static bool TryReadMaximumRating(XElement source, out int maximumRating, int? armCount)
    {
        maximumRating = 1;
        if (source.Element("nolevels") is not null)
            return IsEmptyMarker(source.Element("nolevels")!);
        string? limit = ReadOptionalScalar(source, "limit");
        if (string.Equals(limit, "{arm} - 1", StringComparison.Ordinal))
        {
            // The supported SR5 source expression, not a general expression
            // evaluator. Keep the existing bounded rating contract and reject
            // missing, impossible or excessive anatomy instead of guessing.
            maximumRating = 0;
            if (armCount is not (>= 2 and <= 101))
                return false;
            maximumRating = armCount.Value - 1;
            return true;
        }
        return limit is null
               || string.Equals(limit, "False", StringComparison.OrdinalIgnoreCase)
               || int.TryParse(limit, NumberStyles.Integer, CultureInfo.InvariantCulture,
                   out maximumRating) && maximumRating is > 0 and <= 100;
    }

    private static bool TryReadAvailability(
        XElement source,
        out string expression,
        out int availability,
        out string legality)
    {
        expression = ReadOptionalScalar(source, "avail") ?? string.Empty;
        availability = 0;
        legality = CharacterCreationGearLegality.Legal;
        if (expression.EndsWith('R'))
        {
            legality = CharacterCreationGearLegality.Restricted;
            expression = expression[..^1] + "R";
        }
        else if (expression.EndsWith('F'))
        {
            legality = CharacterCreationGearLegality.Forbidden;
            expression = expression[..^1] + "F";
        }
        ReadOnlySpan<char> numeric = expression.AsSpan(0, expression.Length
            - (legality == CharacterCreationGearLegality.Legal ? 0 : 1));
        return int.TryParse(numeric, NumberStyles.None, CultureInfo.InvariantCulture,
                   out availability)
               && availability >= 0;
    }

    private static bool TryReadGuid(XElement source, string name, out Guid value)
    {
        value = Guid.Empty;
        return TryReadRequiredScalar(source, name, out string text)
               && Guid.TryParseExact(text, "D", out value)
               && value != Guid.Empty;
    }

    private static bool TryReadRequiredInt(XElement source, string name, out int value)
    {
        value = 0;
        return TryReadRequiredScalar(source, name, out string text)
               && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadRequiredDecimal(XElement source, string name, out decimal value)
    {
        value = 0m;
        return TryReadRequiredScalar(source, name, out string text)
               && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadOptionalInt(
        XElement source,
        string name,
        int defaultValue,
        out int value)
    {
        string? text = ReadOptionalScalar(source, name);
        value = defaultValue;
        return text is null
               || int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadOptionalPositiveInt(
        XElement source,
        string name,
        int defaultValue,
        out int value) =>
        TryReadOptionalInt(source, name, defaultValue, out value) && value > 0;

    private static bool TryReadRequiredScalar(
        XElement source,
        string name,
        out string value)
    {
        value = ReadOptionalScalar(source, name) ?? string.Empty;
        return value.Length != 0;
    }

    private static string? ReadOptionalScalar(XElement source, string name)
    {
        XElement? element = source.Element(name);
        if (element is null)
            return null;
        if (element.HasAttributes || element.Elements().Any())
            return null;
        string value = element.Value;
        return string.Equals(value, value.Trim(), StringComparison.Ordinal) ? value : null;
    }

    private static bool TryReadBoolean(
        XElement source,
        string name,
        bool defaultValue,
        out bool value)
    {
        string? raw = ReadOptionalScalar(source, name);
        value = defaultValue;
        return raw is null || bool.TryParse(raw, out value);
    }

    private static bool TryReadBooleanAlias(
        XElement source,
        string current,
        string legacy,
        out bool value)
    {
        if (source.Element(current) is not null && source.Element(legacy) is not null)
        {
            value = false;
            return false;
        }
        string? raw = ReadOptionalScalar(source, current) ?? ReadOptionalScalar(source, legacy);
        value = false;
        return raw is null || bool.TryParse(raw, out value);
    }

    private static bool IsEmptyMarker(XElement element) =>
        !element.HasAttributes && !element.Elements().Any()
        && string.IsNullOrWhiteSpace(element.Value);

    private static string LegacyBoolean(bool value) => value ? "True" : "False";

    private static Guid StableGuid(string seed)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        Span<byte> guid = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guid);
        guid[7] = (byte)((guid[7] & 0x0f) | 0x50);
        guid[8] = (byte)((guid[8] & 0x3f) | 0x80);
        return new Guid(guid);
    }

    private sealed record QualityDefinition(
        Guid SourceId,
        string Name,
        CharacterCreationQualityType Type,
        int Karma,
        int MaximumRating,
        bool Implemented,
        bool ContributeToBuildPoints,
        bool ContributeToLimit,
        bool StagedPurchase,
        bool DoubleCareer,
        bool CanBuyWithSpellPoints,
        bool Metagenic,
        bool Print,
        bool Mutant,
        string SourceBook,
        string Page,
        string Notes,
        string NotesColor,
        IReadOnlyList<CompiledEffect> BonusEffects,
        IReadOnlyList<CompiledEffect> FirstLevelEffects);

    private sealed record CompiledEffect(
        string ImprovementType,
        string ImprovedName,
        decimal Value, int Minimum = 0, int Maximum = 0, string Condition = "", string? Unique886 = null);

    private sealed record GearDefinition(
        Guid SourceId,
        string Name,
        string Category,
        string Capacity,
        string ArmorCapacity,
        string AvailabilityExpression,
        int Availability,
        string Legality,
        int PackageQuantity,
        decimal PackageCost,
        string CostExpression,
        string Weight,
        string SourceBook,
        string Page,
        bool IsFlechetteAmmo,
        string AmmoForWeaponType,
        string CanFormPersona,
        string DeviceRating,
        int MatrixCmBonus,
        bool AllowRename,
        bool Stolen,
        int ChildCostMultiplier,
        int ChildAvailabilityModifier,
        string Notes,
        string NotesColor,
        string ProgramLimit,
        string Attack,
        string Sleaze,
        string DataProcessing,
        string Firewall,
        string AttributeArray,
        string ModAttack,
        string ModSleaze,
        string ModDataProcessing,
        string ModFirewall,
        string ModAttributeArray,
        bool CanSwapAttributes);
}
