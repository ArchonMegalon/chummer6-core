using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static partial class CharacterCareerLifestyleMaintenanceProjection
{
    // Identity admission only. Prices and enabled-source decisions always come from the resolved catalog.
    private static readonly HashSet<Guid> StandardIds = new(new[]
    {
        "559653df-a9af-44e2-9e04-3044c1d1b421", "9367556e-f82f-4f9d-840e-24c8f824ca68",
        "451eef87-d18e-4bee-a972-1ee165b08522", "9cb0222c-14c1-4bea-bf83-055513a1f33e",
        "4a37d519-c9be-4ecc-97bb-e9d78708c374", "4b513ac9-9eb3-471b-931b-839a04873b84"
    }.Select(Guid.Parse));
    private static readonly HashSet<string> PassiveFields = new(StringComparer.Ordinal)
        { "notes", "notesColor", "customname", "sortorder", "print" };

    internal static bool IsStandard(CharacterCreationLifestyleCatalogOption option)
        => StandardIds.Contains(option.SourceId) && option.SourceBook == "SR5"
            && option.DefaultIncrementId == CharacterCreationLifestyleIncrementIds.Month
            && option.IsSelectable && option.EligibilityIsExact;

    internal static XDocument ReadDocument(WorkspaceStoredDocument saved)
    {
        var d = saved.Document;
        if (d.Format != WorkspaceDocumentFormat.NativeXml || d.RulesetId != "sr5" || d.SchemaVersion != 1
            || d.PayloadKind is not ("workspace" or "sr5/chum5-xml") || saved.ContentRevision <= 0
            || saved.ContentRevision == long.MaxValue || saved.SavedRevision != saved.ContentRevision
            || string.IsNullOrEmpty(d.Content) || d.Content.Length > CharacterCareerLifestyleMaintenanceTransaction.MaximumContentLength)
            throw new InvalidDataException("lifestyle_maintenance_requires_saved_sr5_career");
        using var text = new StringReader(d.Content);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = CharacterCareerLifestyleMaintenanceTransaction.MaximumContentLength });
        var doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (doc.Root?.Name != "character" || doc.Root.HasAttributes
            || !bool.TryParse(Scalar(doc.Root, "created"), out bool career) || !career)
            throw new InvalidDataException("lifestyle_maintenance_requires_saved_sr5_career");
        var containers = doc.Root.Elements("lifestyles").ToArray();
        if (containers.Length > 1 || containers.Any(x => x.HasAttributes)
            || containers.Sum(x => x.Elements().Count()) > 100_000)
            throw new InvalidDataException("lifestyle_maintenance_lifestyle_container_invalid");
        return doc;
    }

    internal static bool TryAuthority(string content, ICharacterSourceDataResolver sources,
        out CharacterCreationLifestylesAuthority authority, out string digest)
        => TryAuthority(content, sources, out authority, out digest, out _);

    internal static bool TryAuthority(string content, ICharacterSourceDataResolver sources,
        out CharacterCreationLifestylesAuthority authority, out string digest, out string error)
        => TryAuthorityCore2719(content, sources, out authority, out digest, out error, historical: false);

    // V1–V6 consume the original authority and digest, including its original admission rules.
    internal static bool TryHistoricalAuthority2719(string content, ICharacterSourceDataResolver sources,
        out CharacterCreationLifestylesAuthority authority, out string digest, out string error)
        => TryAuthorityCore2719(content, sources, out authority, out digest, out error, historical: true);

    private static bool TryAuthorityCore2719(string content, ICharacterSourceDataResolver sources,
        out CharacterCreationLifestylesAuthority authority, out string digest, out string error, bool historical)
    {
        authority = CharacterCreationLifestylesAuthority.Unavailable;
        digest = string.Empty;
        error = "lifestyle_maintenance_source_or_effect_authority_unavailable";
        // Freeze the historical Creation-based authority for old consumers. Only
        // explicit V7 source capability selects the shared engine's Career mode.
        var root = XDocument.Parse(content).Root!;
        var context = sources.TryCreateContext(content);
        bool careerMode = !historical && context is ICurrentLifestyleMechanicsSource2684 { HasLifestyleMechanics2684: true };
        if (!historical) foreach (string container in new[] { "improvements", "qualities" })
        {
            var nodes = root.Elements(container).ToArray();
            if (nodes.Length > 1 || nodes.Any(node => node.HasAttributes
                || node.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))) return false;
        }
        foreach (var effect in root.Elements("improvements").SelectMany(c => c.Elements()))
        {
            if (!effect.Elements().Any(e => e.Name.LocalName == "improvementttype"
                && e.Value.Trim() is "LifestyleCost" or "BasicLifestyleCost" or "TrustFund")) continue;
            error = "lifestyle_maintenance_cost_effect_shape_unresolved";
            if (effect.Name != "improvement" || effect.HasAttributes
                || effect.Elements().Any(e => e.Name.Namespace != XNamespace.None || e.HasAttributes || e.HasElements)
                || effect.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)) return false;
            string enabled = effect.Element("enabled")?.Value.Trim() ?? "";
            if (historical)
            {
                if (enabled == "0") continue;
            }
            else
            {
                if (enabled.Length != 0 && (!int.TryParse(enabled, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int flag) || flag < 0)) return false;
                if (!careerMode && enabled.Length != 0 && int.Parse(enabled, CultureInfo.InvariantCulture) == 0) continue;
            }
            error = "lifestyle_maintenance_conditional_cost_effect_read_only";
            if (!careerMode && !string.IsNullOrEmpty(effect.Element("condition")?.Value.Trim())) return false;
        }
        error = "lifestyle_maintenance_source_or_effect_authority_unavailable";
        string sourceXml = string.Empty, sourceAuthority = string.Empty;
        if (context is null || !historical && (context is not ICurrentLifestyleSettingsSource2678 lifestyleSource
            || !lifestyleSource.TryReadLifestyleSource(out sourceXml, out sourceAuthority)
            || string.IsNullOrWhiteSpace(sourceAuthority))
            || !context.TryResolveCareerSettingsProfile(out string profile, out string profileAuthority)
            || string.IsNullOrWhiteSpace(profile) || string.IsNullOrWhiteSpace(profileAuthority)
            || !(careerMode
                ? context is ICharacterLifestyleSourceMode2719 career && career.TryResolveLifestylesAuthority(CharacterLifestyleSourceMode.Career, out authority)
                : context.TryResolveCreationLifestylesAuthority(out authority))
            || !CharacterCreationLifestylesRules.IsValidAuthority(authority)
            || !CharacterCreationLifestyleImprovementRules.IsValidPurchasedQualityCosts(authority.PurchasedQualityCosts)) return false;
        error = "lifestyle_maintenance_metatype_or_trust_fund_source_unresolved";
        if (!TryCareerEffectSources(root, context, authority, out string effectSources, historical, careerMode)) return false;
        digest = historical
            ? CharacterCareerLifestyleMaintenanceTransaction.Hash(JsonSerializer.Serialize(new
                { profile, profileAuthority, authority.AuthorityDigest, effectSources }))
            : CharacterCareerLifestyleMaintenanceTransaction.Hash(JsonSerializer.Serialize(new
                { profile, profileAuthority, authority.AuthorityDigest, effectSources, sourceAuthority,
                    LifestyleSourceDigest = CharacterCareerLifestyleMaintenanceTransaction.Hash(sourceXml) }));
        error = string.Empty;
        return true;
    }

    // Used by the explicit source capability as well as maintenance/current admission.
    public static bool TryValidateSourceEffects2719(XElement root, ICharacterSourceDataContext context,
        CharacterCreationLifestylesAuthority authority, out string digest)
    {
        digest = string.Empty;
        try
        {
            foreach (string container in new[] { "improvements", "qualities" })
            {
                var nodes = root.Elements(container).ToArray();
                if (nodes.Length > 1 || nodes.Any(node => node.HasAttributes
                    || node.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))) return false;
            }
            foreach (var effect in root.Elements("improvements").SelectMany(container => container.Elements()))
            {
                if (!effect.Elements().Any(field => field.Name.LocalName == "improvementttype"
                    && field.Value.Trim() is "LifestyleCost" or "BasicLifestyleCost" or "TrustFund")) continue;
                if (effect.Name != "improvement" || effect.HasAttributes
                    || effect.Elements().Any(field => field.Name.Namespace != XNamespace.None || field.HasAttributes || field.HasElements)
                    || effect.Elements().GroupBy(field => field.Name).Any(group => group.Count() != 1)) return false;
            }
            return TryCareerEffectSources(root, context, authority, out digest, historical: false, careerMode: true);
        }
        catch (Exception error) when (IsInputException(error)) { return false; }
    }

    private static bool TryCareerEffectSources(XElement root, ICharacterSourceDataContext context,
        CharacterCreationLifestylesAuthority authority, out string digest, bool historical = false, bool careerMode = false)
    {
        digest = string.Empty;
        // The shared creation resolver admits a saved racial percentage by shape. Career also
        // requires its exact enabled base-metatype source, so missing/altered surcharges never become zero.
        if (!context.TryResolveCareerQualityMetatype(out string metatypeXml, out string metatypeAuthority)
            || string.IsNullOrWhiteSpace(metatypeAuthority)) return false;
        var metatype = XElement.Parse(metatypeXml);
        var racialCosts = metatype.Elements("bonus").SelectMany(b => b.Elements())
            .Where(e => e.Name.LocalName is "lifestylecost" or "basiclifestylecost").ToArray();
        decimal racialPercent = 0;
        if (racialCosts.Length > 1 || racialCosts.Any(e => e.Name != "lifestylecost" || e.HasAttributes || e.HasElements)
            || racialCosts.Length == 1 && (!decimal.TryParse(racialCosts[0].Value,
                NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out racialPercent) || racialPercent < 0)
            || authority.MetatypeCostPercent != racialPercent) return false;
        var improvements = root.Elements("improvements").SelectMany(c => c.Elements("improvement")).ToArray();
        bool Active(XElement e) => historical ? (e.Element("enabled")?.Value.Trim() ?? "") != "0"
            : string.IsNullOrWhiteSpace(e.Element("enabled")?.Value)
            || int.TryParse(e.Element("enabled")!.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int flag) && flag > 0;
        var racial = improvements.Where(e => Active(e) && e.Element("improvementttype")?.Value.Trim() == "LifestyleCost"
            && e.Element("improvementsource")?.Value.Trim() is "Metatype" or "Metavariant" or "Heritage").ToArray();
        if (racial.Length != racialCosts.Length || racial.Any(e => e.Element("improvementsource")?.Value.Trim() != "Metatype")) return false;

        if (careerMode)
        {
            // Native metatype effects identify their owner by the source metatype ID.
            // Disabled/foreign/extra racial caches cannot substitute for that exact effect.
            var allRacial = improvements.Where(e => e.Element("improvementsource")?.Value.Trim() is "Metatype" or "Metavariant" or "Heritage"
                && e.Element("improvementttype")?.Value.Trim() is "LifestyleCost" or "BasicLifestyleCost").ToArray();
            if (allRacial.Length != racialCosts.Length || allRacial.Any(e =>
                e.Element("enabled")?.Value != "1" || e.Element("condition")?.Value is not (null or "")
                || !Guid.TryParse(e.Element("sourcename")?.Value, out var owner)
                || !Guid.TryParse(metatype.Element("id")?.Value, out var sourceId) || owner != sourceId)) return false;
        }
        if (careerMode)
            return TryCompilerTrustFundSources2749(root, improvements, context, authority, metatypeAuthority, out digest);
        // Trust Fund's saved value is checked against the live source and its saved owner.
        // Keep all quality/improvement XML unchanged; this grants no quality purchase authority.
        var trust = improvements.Where(e => e.Element("improvementttype")?.Value.Trim() == "TrustFund").ToArray();
        var remaining = trust.ToList();
        var sourcePins = new List<string>();
        int expectedLevel = 0;
        var qualities = root.Elements("qualities").SelectMany(c => c.Elements("quality")).ToArray();
        if (qualities.Length > 4096) return false;
        foreach (var quality in qualities)
        {
            bool hint = quality.Descendants().Any(e => e.Name.LocalName == "trustfund");
            if (!Guid.TryParse(quality.Element("sourceid")?.Value, out var sourceId)
                || !context.TryResolveCareerQualitySource(sourceId, out var source) || source is null)
            { if (hint) return false; continue; }
            var sourceNode = XElement.Parse(source.RawSourceXml);
            var levels = sourceNode.Elements("bonus").SelectMany(b => b.Elements("trustfund")).ToArray();
            if (levels.Length == 0) { if (hint) return false; continue; }
            if (levels.Length != 1 || levels[0].HasAttributes || levels[0].HasElements
                || !int.TryParse(levels[0].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int level)
                || level is < 1 or > 4 || quality.HasAttributes
                || quality.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)
                || !Guid.TryParse(quality.Element("guid")?.Value, out var instance) || instance == Guid.Empty
                || quality.Element("bonus") is not { } savedBonus || sourceNode.Element("bonus") is not { } sourceBonus
                || !XNode.DeepEquals(WithoutWhitespace(savedBonus), WithoutWhitespace(sourceBonus))) return false;
            var effects = remaining.Where(e => e.Element("improvementsource")?.Value.Trim() == "Quality"
                && Guid.TryParse(e.Element("sourcename")?.Value, out var owner) && owner == instance).ToArray();
            if (effects.Length != 1 || !int.TryParse(effects[0].Element("val")?.Value, out int savedLevel)
                || savedLevel != level) return false;
            remaining.Remove(effects[0]);
            if (Active(effects[0])) { if (expectedLevel != 0) return false; expectedLevel = level; }
            sourcePins.Add(JsonSerializer.Serialize(source));
        }
        if (remaining.Count != 0 || authority.TrustFundLevel != expectedLevel) return false;
        digest = CharacterCareerLifestyleMaintenanceTransaction.Hash(JsonSerializer.Serialize(new { metatypeAuthority, sourcePins }));
        return true;
    }

    private static bool TryCompilerTrustFundSources2749(XElement root, XElement[] improvements,
        ICharacterSourceDataContext context, CharacterCreationLifestylesAuthority authority,
        string metatypeAuthority, out string digest)
    {
        digest = string.Empty;
        var qualities = root.Elements("qualities").SelectMany(container => container.Elements("quality")).ToArray();
        if (qualities.Length > 4096) return false;
        var instances = new List<CharacterCreationLifestyleQualityInstance>();
        var sourcePins = new Dictionary<Guid, string>();
        bool HasTrustFund(XElement node) => node.Descendants().Any(element => element.Name.LocalName == "trustfund");
        foreach (var group in qualities.GroupBy(quality =>
            Guid.TryParse(quality.Element("sourceid")?.Value, out var id) ? id : Guid.Empty))
        {
            bool savedHint = group.Any(HasTrustFund);
            if (!context.TryResolveCareerQualitySource(group.Key, out var source) || source is null)
            { if (savedHint) return false; continue; }
            var sourceNode = XElement.Parse(source.RawSourceXml);
            if (!savedHint && !HasTrustFund(sourceNode)) continue;
            if (!int.TryParse(sourceNode.Element("karma")?.Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int karma)) return false;
            bool Flag(string name) => bool.TryParse(sourceNode.Element(name)?.Value, out bool value) && value;
            int rating = group.Count();
            long totalCost = (long)karma * rating;
            if (totalCost is < int.MinValue or > int.MaxValue) return false;
            int cost = (int)totalCost;
            string optionId = $"quality:{source.SourceId:D}:rating:{rating}";
            string[] anchors = [$"qualities.xml#quality:{source.SourceId:D}"];
            string nodeDigest = CharacterCreationQualitiesRules.ComputeSourceNodeDigest(source.RawSourceXml);
            var type = sourceNode.Element("category")?.Value == "Positive"
                ? CharacterCreationQualityType.Positive : CharacterCreationQualityType.Negative;
            bool metagenic = Flag("metagenic") || Flag("metagenetic");
            bool countsAgainstLimit = sourceNode.Element("contributetolimit") is null || Flag("contributetolimit");
            bool countsAgainstKarma = sourceNode.Element("contributetobp") is null || Flag("contributetobp");
            var option = new CharacterCreationQualityCatalogOption(optionId, source.SourceId,
                source.SourceId.ToString("D"), source.Name, type, rating, cost, 1, metagenic,
                countsAgainstLimit, countsAgainstKarma, false, true, true, null, null, null,
                anchors, source.RawSourceXml, nodeDigest, string.Empty);
            var selection = new CharacterCreationQualitySelection(optionId, source.SourceId,
                option.SelectionKey, source.Name, type, rating, cost, metagenic, countsAgainstLimit,
                countsAgainstKarma, false, null, null, anchors, source.RawSourceXml, nodeDigest,
                CharacterCreationQualitiesRules.ComputeOptionDigest(option));
            int level = 0;
            foreach (var quality in group)
                instances.Add(new(quality, ++level, selection));
            sourcePins.Add(group.Key, JsonSerializer.Serialize(source));
        }
        // Reuse the explicit compiler/matcher, including its first-level occurrence
        // rules and complete native owner/value/enabled checks. Other quality costs
        // were already matched by the authority resolver; retain every TrustFund so
        // an orphan or extra effect cannot disappear from this narrower graph.
        var owners = instances.Select(instance => instance.Quality.Element("guid")?.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bound = improvements.Where(effect => effect.Element("improvementttype")?.Value.Trim() == "TrustFund"
            || effect.Element("improvementsource")?.Value.Trim() == "Quality"
                && effect.Element("improvementttype")?.Value.Trim() is "LifestyleCost" or "BasicLifestyleCost"
                && owners.Contains(effect.Element("sourcename")?.Value)).ToArray();
        if (!CharacterCreationLifestyleImprovementRules.TryResolveSourceBackedCosts(bound, instances, 0,
            out int trustFundLevel, out _, out _, out _, CharacterLifestyleSourceMode.Career)
            || trustFundLevel != authority.TrustFundLevel) return false;
        // Preserve saved occurrence order in the digest, including later quality
        // instances that retain firstlevelbonus XML without emitting another effect.
        var pins = qualities.Where(quality => Guid.TryParse(quality.Element("sourceid")?.Value, out var id)
            && sourcePins.ContainsKey(id)).Select(quality => sourcePins[Guid.Parse(quality.Element("sourceid")!.Value)]).ToList();
        digest = CharacterCareerLifestyleMaintenanceTransaction.Hash(JsonSerializer.Serialize(new { metatypeAuthority, sourcePins = pins }));
        return true;
    }

    public static CharacterCareerLifestyleMaintenanceLoadResult Load(WorkspaceStoredDocument saved,
        ICharacterSourceDataResolver sources)
    {
        try
        {
            var root = ReadDocument(saved).Root!;
            if (!TryAuthority(saved.Document.Content, sources, out var authority, out string digest, out string error))
                return new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: error);
            if (!TrySourcePeriods(saved.Document.Content, sources, out var periods))
                return new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_maintenance_source_period_unresolved");
            var rows = root.Element("lifestyles")?.Elements("lifestyle").ToArray() ?? [];
            var entries = new List<CharacterCareerLifestyleMaintenanceEntry>();
            foreach (var row in rows)
            {
                Guid.TryParse(row.Element("guid")?.Value, out var id);
                bool unique = id != Guid.Empty && rows.Count(x => Guid.TryParse(x.Element("guid")?.Value, out var other) && other == id) == 1;
                CharacterCreationLifestyleProjection? projected = null;
                string reason = "lifestyle_maintenance_identity_ambiguous";
                bool supported = unique && TryExistingForMaintenance(row, id, authority, out projected, out reason);
                if (supported && (!periods.TryGetValue(projected!.SourceId, out string? period)
                    || period != projected.Configuration.IncrementId))
                { supported = false; reason = "lifestyle_maintenance_source_period_unresolved"; }
                entries.Add(new(id, row.Element("name")?.Value ?? row.Element("baselifestyle")?.Value ?? "Saved lifestyle",
                    supported ? projected!.Configuration : null, supported ? projected!.Economics.CostPerIncrement : null,
                    supported ? null : reason));
            }
            if (!TryAuthority(saved.Document.Content, sources, out _, out string final) || digest != final)
                return new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_maintenance_source_changed");
            return new(CharacterCareerLifestyleMaintenanceOutcome.Available,
                new(AdmittedOptions(authority).Where(option => periods.TryGetValue(option.SourceId, out string? period)
                    && period == option.DefaultIncrementId).ToArray(), entries, authority.TrustFundLevel) { Authority = authority }, saved.ContentRevision);
        }
        catch (Exception ex) when (IsInputException(ex))
        { return new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_maintenance_input_unresolved"); }
    }

    internal static bool TryExisting(XElement row, Guid id, CharacterCreationLifestylesAuthority authority,
        out CharacterCreationLifestyleProjection? projection, out string error)
    {
        try { return TryExistingCore(row, id, authority, out projection, out error); }
        catch (Exception ex) when (IsInputException(ex))
        { projection = null; error = "lifestyle_maintenance_custom_or_advanced_read_only"; return false; }
    }

    private static bool TryExistingCore(XElement row, Guid id, CharacterCreationLifestylesAuthority authority,
        out CharacterCreationLifestyleProjection? projection, out string error)
    {
        projection = null;
        error = "lifestyle_maintenance_custom_or_advanced_read_only";
        if (row.HasAttributes || row.Elements().GroupBy(x => x.Name).Any(g => g.Count() != 1)
            || row.Elements().Any(x => x.Name.Namespace != XNamespace.None || x.HasAttributes)
            || row.Element("type")?.Value != "Standard" || row.Element("increment")?.Value != "Month"
            || !CharacterCreationLifestylesService.TryReadConfiguration(row, id, authority, out var config, out _)
            || config.StyleId != CharacterCreationLifestyleStyleIds.Standard || config.Area != 0 || config.Comforts != 0
            || config.Security != 0 || config.BonusLifestylePoints != 0 || config.Qualities.Any(q => !q.IsBuiltIn)
            || config.Increments is < 0 or > 10_000
            || !authority.LifestyleOptions.Any(o => o.OptionId == config.BaseLifestyleOptionId && IsStandard(o))) return false;
        if (!TryProject(config, authority, out var candidate, out _)) return false;
        // Admit only known standard serialization. Unknown target data stays readable but is never rewritten.
        var canonical = CharacterCreationLifestylesService.BuildLifestyleElement(candidate!, null, authority);
        foreach (var node in row.Elements())
        {
            string name = node.Name.LocalName;
            if (PassiveFields.Contains(name) && !node.HasElements) continue;
            if (name is "totalcost" or "totalmonthlycost")
            { if (node.HasElements || !decimal.TryParse(node.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) || n < 0) return false; continue; }
            var expected = canonical.Element(node.Name);
            if (expected is null || (name != "lifestylequalities" && node.HasElements)) return false;
            if (name == "lifestylequalities")
            {
                // No normalization of custom quality fields or effects, including within built-ins.
                if (!XNode.DeepEquals(WithoutWhitespace(node), WithoutWhitespace(expected))) return false;
            }
            else if (node.Value != expected.Value
                && !(decimal.TryParse(node.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var actualNumber)
                    && decimal.TryParse(expected.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var expectedNumber)
                    && actualNumber == expectedNumber)) return false;
        }
        projection = candidate;
        error = string.Empty;
        return true;
    }

    private static XElement WithoutWhitespace(XElement value)
    {
        var copy = new XElement(value);
        copy.DescendantNodes().OfType<XText>().Where(x => string.IsNullOrWhiteSpace(x.Value)).Remove();
        return copy;
    }

    internal static bool TryProject(CharacterCreationLifestyleConfiguration configuration,
        CharacterCreationLifestylesAuthority authority, out CharacterCreationLifestyleProjection? projection, out string error)
    {
        projection = null;
        // Creation's count >= 1 is not Career's count rule. Project the canonical per-interval price
        // with one interval, then retain the actual Career count (including zero after a decrease).
        if (!CharacterCreationLifestylesRules.TryProject(configuration with { Increments = 1 }, authority,
            out var priced, out var blockers))
        { error = string.Join(",", blockers); return false; }
        var candidate = priced with { Configuration = priced.Configuration with { Increments = configuration.Increments },
            Economics = priced.Economics with { TotalCost = checked(priced.Economics.CostPerIncrement * configuration.Increments) } };
        projection = candidate with { LifestyleDigest = CharacterCreationLifestylesRules.ComputeProjectionDigest(candidate) };
        error = string.Empty;
        return true;
    }

    internal static string Scalar(XElement parent, string name)
    {
        var nodes = parent.Elements(name).ToArray();
        if (nodes.Length != 1 || nodes[0].HasAttributes || nodes[0].HasElements) throw new InvalidDataException();
        return nodes[0].Value;
    }
    internal static bool IsInputException(Exception ex) => ex is XmlException or InvalidDataException
        or FormatException or OverflowException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException;
}
