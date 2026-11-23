using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCareerLifestyleMaintenanceProjection
{
    // TryExisting is the historical retained-consumer contract. Do not widen that
    // method: V1–V6 keep their original admission and fingerprints. The new Career
    // route (and an explicitly admitted V7 consumer) use this source-based reader.
    public static bool TryExistingForMaintenance(XElement row, Guid id,
        CharacterCreationLifestylesAuthority authority,
        out CharacterCreationLifestyleProjection? projection, out string error)
    {
        projection = null;
        error = "lifestyle_maintenance_saved_mechanics_unresolved";
        try
        {
            if (row.Name != "lifestyle" || row.HasAttributes
                || row.Elements().GroupBy(x => x.Name).Any(g => g.Count() != 1)
                || row.Element("increment")?.Value is not ("Day" or "Week" or "Month")
                || row.Element("type")?.Value is not ("Standard" or "Advanced" or "BoltHole" or "Safehouse")
                || !Guid.TryParse(Scalar(row, "guid"), out var savedId) || savedId != id
                || !CharacterCreationLifestylesService.TryReadConfiguration(row, id, authority, out var configuration, out _)
                || !authority.LifestyleOptions.Any(option => option.OptionId == configuration.BaseLifestyleOptionId
                    && option.DefaultIncrementId == configuration.IncrementId)) return false;
            // Career intervals are signed counters: expiry may take them below zero.
            // Quote one interval through the existing projector, preserving the saved count.
            if (!TryProject(configuration, authority, out var candidate, out error)) return false;
            var canonical = CharacterCreationLifestylesService.BuildLifestyleElement(candidate!, null, authority);
            error = "lifestyle_maintenance_saved_mechanics_unresolved";
            if (!MatchesSourceFields(row, canonical, lifestyle: true)) return false;
            projection = candidate;
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (IsInputException(ex)) { return false; }
    }

    private static bool MatchesSourceFields(XElement saved, XElement canonical, bool lifestyle)
    {
        if (saved.HasAttributes || saved.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)
            || saved.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))
            || saved.Nodes().OfType<XProcessingInstruction>().Any()) return false;
        foreach (var node in saved.Elements())
        {
            if (node.Name.Namespace != XNamespace.None || node.HasAttributes) return false;
            string name = node.Name.LocalName;
            if (PassiveFields.Contains(name) && !node.HasElements) continue;
            if (lifestyle && name is ("totalcost" or "totalmonthlycost"))
            {
                // Derived caches are never price authority, including a forged zero.
                // Total cost may be negative after expiry; the interval price may not.
                if (node.HasElements || !decimal.TryParse(node.Value, NumberStyles.Number,
                    CultureInfo.InvariantCulture, out var cost) || name == "totalmonthlycost" && cost < 0) return false;
                continue;
            }
            var expected = canonical.Element(node.Name);
            if (expected is null) return false;
            if (lifestyle && name == "lifestylequalities")
            {
                if (node.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))
                    || node.Elements().Any(e => e.Name != "lifestylequality")) return false;
                var rows = node.Elements().ToArray();
                var expectedRows = expected.Elements().ToArray();
                if (rows.Length != expectedRows.Length) return false;
                foreach (var quality in rows)
                {
                    var match = expectedRows.SingleOrDefault(e => e.Element("guid")?.Value == quality.Element("guid")?.Value);
                    if (match is null || !MatchesSourceFields(quality, match, lifestyle: false)) return false;
                }
                continue;
            }
            if (node.HasElements || node.Value != expected.Value
                && !(decimal.TryParse(node.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var actual)
                    && decimal.TryParse(expected.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var source)
                    && actual == source)) return false;
        }
        // Built-ins may not disappear just because the whole container was omitted.
        return !lifestyle || saved.Element("lifestylequalities") is not null
            || !canonical.Element("lifestylequalities")!.HasElements;
    }

    // The legacy source parser defaults unrecognized increment text to Month. Career payments
    // may use only an absent/default period or an explicitly supported source period.
    internal static bool TrySourcePeriods(string content, ICharacterSourceDataResolver sources,
        out IReadOnlyDictionary<Guid, string> periods)
    {
        periods = new Dictionary<Guid, string>();
        if (sources.TryCreateContext(content) is not ICurrentLifestyleSettingsSource2678 source
            || !source.TryReadLifestyleSource(out var xml, out _)) return false;
        var result = new Dictionary<Guid, string>();
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = CharacterCareerLifestyleMaintenanceTransaction.MaximumContentLength });
        var rows = XElement.Load(reader).Element("lifestyles")?.Elements("lifestyle").ToArray() ?? [];
        var identities = rows.Select(row => Guid.TryParse(row.Element("id")?.Value, out var id) ? id : Guid.Empty)
            .GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
        foreach (var row in rows)
        {
            if (row.Elements("id").ToArray() is not [var key] || key.HasAttributes || key.HasElements
                || !Guid.TryParse(key.Value, out var id) || id == Guid.Empty) continue;
            if (identities[id] != 1) continue;
            var units = row.Elements("increment").ToArray();
            if (units.Length > 1 || units.Any(unit => unit.HasAttributes || unit.HasElements)) continue;
            string unit = units.SingleOrDefault()?.Value.Trim().ToLowerInvariant() ?? "";
            if (unit.Length == 0) unit = CharacterCreationLifestyleIncrementIds.Month;
            if (CharacterCreationLifestyleIncrementIds.All.Contains(unit)) result.Add(id, unit);
        }
        periods = result;
        return true;
    }

    internal static bool TrySourcePeriod(string content, ICharacterSourceDataResolver sources,
        CharacterCreationLifestyleCatalogOption option, out string error)
    {
        error = "lifestyle_maintenance_source_period_unresolved";
        return TrySourcePeriods(content, sources, out var periods) && periods.TryGetValue(option.SourceId, out string? period)
            && period == option.DefaultIncrementId;
    }

    internal static IReadOnlyList<CharacterCreationLifestyleCatalogOption> AdmittedOptions(
        CharacterCreationLifestylesAuthority authority)
        => authority.LifestyleOptions.Where(option => option.IsSelectable && option.EligibilityIsExact
            && option.Blockers.Count == 0 && CharacterCreationLifestyleIncrementIds.All.Contains(option.DefaultIncrementId)
            && TryProject(new(option.SourceId, option.OptionId, option.Name,
                CharacterCreationLifestyleStyleIds.Standard, option.DefaultIncrementId, 1, 100m,
                0, false, false, 0, 0, 0, 0, "", "", "", []), authority, out _, out _)).ToArray();
}
