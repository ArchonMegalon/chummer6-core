using System.Xml.Linq;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        // The shipped quality source uses CSV <allowed>. Keep the older nested
        // representation readable, but never choose one of two competing fields.
        // Both the actual authority compiler and accounting use this same reader.
        private static bool TryReadLifestyleAllowed1000(XElement row, out string[] allowed)
        {
            allowed = [];
            var csv = row.Elements("allowed").ToArray();
            var nested = row.Elements("allowedfreelifestyles").ToArray();
            if (csv.Length + nested.Length > 1) return false;
            if (csv is [var field])
            {
                if (field.HasAttributes || field.HasElements) return false;
                allowed = field.Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim()).ToArray();
                if (allowed.Any(string.IsNullOrEmpty)) return false;
            }
            else if (nested is [var container])
            {
                if (container.HasAttributes || HasDirectScopeText(container)
                    || container.Elements().Any(child => child.Name != "lifestyle"
                        || child.HasAttributes || child.HasElements || string.IsNullOrWhiteSpace(child.Value)))
                    return false;
                allowed = container.Elements().Select(child => child.Value.Trim()).ToArray();
            }
            allowed = allowed.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            return true;
        }

        // Called only after Supporting832 reproduced every paid/fallback
        // projection with the freshly resolved authority and canonical TryProject.
        private static void ExecuteLifestyleAccounting1000(XElement root,
            CharacterCreationLifestylesAuthority authority,
            IReadOnlyList<CharacterCreationLifestyleProjection> projections, SourceScopeLedger ledger)
        {
            var lifestyleRows = root.Element("lifestyles")!.Elements("lifestyle").ToArray();
            var identities = new HashSet<Guid>();
            foreach (var row in lifestyleRows)
            {
                if (row.Elements("id").ToArray() is not [var key] || key.HasAttributes || key.HasElements
                    || !Guid.TryParse(key.Value, out var id) || !identities.Add(id)) return;
            }
            foreach (var row in lifestyleRows)
            {
                var id = Guid.Parse(row.Element("id")!.Value);
                var hides = row.Elements("hide").ToArray();
                bool hidden = hides is [var hide] && !hide.HasAttributes && !hide.HasElements
                    && string.IsNullOrWhiteSpace(hide.Value);
                // The real reader excludes empty identities and hidden rows before
                // creating options. Do not turn a malformed hide marker into proof.
                if ((id != Guid.Empty && !hidden) || hides.Length != 0 && !hidden
                    || authority.LifestyleOptions.Any(option => option.SourceId == id)
                    || !Container718(ledger, row, "TryResolveCreationLifestylesAuthority-excluded-row")) continue;
                object result = new { Operation = "TryResolveCreationLifestylesAuthority-excluded-row",
                    SourceId = id, EmptyIdentity = id == Guid.Empty, Hidden = hidden,
                    Projections = projections.Select(p => p.LifestyleDigest).ToArray() };
                foreach (var field in row.Elements())
                {
                    if (row.Elements(field.Name).Count() != 1 || field.HasAttributes || field.HasElements) continue;
                    bool known = field.Name.ToString() switch
                    {
                        "id" => Guid.TryParse(field.Value, out var value) && value == id,
                        "hide" => hidden,
                        "name" or "source" or "page" => !string.IsNullOrWhiteSpace(field.Value),
                        "cost" or "multiplier" or "costforarea" or "costforcomforts" or "costforsecurity" =>
                            TryReadRequiredNonNegativeDecimal(row, field.Name.LocalName, out _),
                        "dice" or "lp" => TryReadRequiredNonNegativeInt(row, field.Name.LocalName, out _),
                        _ => false
                    };
                    if (known) ledger.Tree(field, D.NotSelected, result);
                }
            }

            var options = projections.Select(p => authority.LifestyleOptions.Single(option =>
                option.OptionId == p.Configuration.BaseLifestyleOptionId)).DistinctBy(option => option.OptionId).ToArray();
            foreach (var option in options)
            {
                var row = lifestyleRows.Single(candidate => Guid.Parse(candidate.Element("id")!.Value) == option.SourceId);
                if (row.Elements("freegrids").ToArray() is not [var grids] || grids.HasAttributes
                    || HasDirectScopeText(grids)) continue;
                var entries = grids.Elements().ToArray();
                if (entries.Any(entry => entry.Name != "freegrid" || entry.HasElements
                        || string.IsNullOrWhiteSpace(entry.Value)
                        || entry.Attributes().Any(attribute => attribute.Name != "select"))
                    || entries.Select(entry => (entry.Value.Trim(), entry.Attribute("select")?.Value.Trim() ?? string.Empty))
                        .Distinct().Count() != entries.Length) continue;
                var actual = projections.Where(p => p.Configuration.BaseLifestyleOptionId == option.OptionId).ToArray();
                if (!authority.FreeGridsEnabled)
                {
                    if (authority.FreeGridsActivation is not { Reason: CharacterCreationFreeGridsActivationReason.Disabled }
                        || option.BuiltInQualities.Count != 0) continue;
                    ledger.Tree(grids, D.NotSelected, new { Operation = "TryResolveCreationLifestylesAuthority-freegrids-disabled",
                        authority.FreeGridsActivation, option.OptionId, Results = actual });
                    continue;
                }
                if (entries.Length != option.BuiltInQualities.Count) continue;
                var resolved = entries.Select(entry => new
                {
                    Entry = entry,
                    Extra = entry.Attribute("select")?.Value.Trim() ?? string.Empty,
                    Matches = authority.QualityOptions.Where(quality => quality.Name == entry.Value.Trim()).ToArray()
                }).ToArray();
                if (resolved.Any(entry => entry.Matches.Length != 1
                    || option.BuiltInQualities.Count(quality => quality.QualityOptionId == entry.Matches[0].OptionId
                        && quality.Extra == entry.Extra) != 1
                    || actual.Any(projection => projection.Configuration.Qualities.Count(quality =>
                        quality.OptionId == entry.Matches[0].OptionId && quality.Extra == entry.Extra
                        && quality.IsBuiltIn && quality.IsFree && quality.UseLifestylePoints) != 1))) continue;
                Container718(ledger, grids, "TryResolveCreationLifestylesAuthority-freegrids");
                foreach (var entry in resolved)
                    ledger.Tree(entry.Entry, D.Compiled, new { Operation = "AddBuiltInQualities-and-TryProject",
                        entry.Matches[0].OptionId, entry.Extra, Results = actual });
            }

            var selected = projections.SelectMany(projection => projection.Configuration.Qualities)
                .Select(quality => quality.OptionId).ToHashSet(StringComparer.Ordinal);
            foreach (var option in authority.QualityOptions.Where(option => selected.Contains(option.OptionId)))
            {
                var rows = root.Element("qualities")!.Elements("quality")
                    .Where(row => Guid.TryParse(row.Element("id")?.Value, out var id) && id == option.SourceId).ToArray();
                if (rows is not [var row] || !TryScopeRowIdentity(row, new("qualities", "quality", true), out var identity)
                    || !Container718(ledger, row, "TryProject-selected-lifestyle-quality")) continue;
                var actual = projections.Where(projection => projection.Configuration.Qualities
                    .Any(quality => quality.OptionId == option.OptionId)).ToArray();
                object result = new { Operation = "TryProject-lifestyle-quality-economics-and-aspects",
                    option.OptionId, Results = actual };
                Scalar718(ledger, row, "id", identity, "lifestyle-quality-source-selection", result);
                Scalar718(ledger, row, "name", option.Name, "lifestyle-quality-source-selection", result);
                Scalar718(ledger, row, "category", option.Category, "ResolveLifestyleQualityType-and-TryProject", result);
                Scalar718(ledger, row, "source", option.SourceBook, "lifestyle-quality-source-anchor", result);
                Scalar718(ledger, row, "page", option.Page, "lifestyle-quality-source-anchor", result);
                Scalar718(ledger, row, "lp", option.LifestylePointCost, "ComputeLifestylePoints", result);
                Scalar718(ledger, row, "cost", option.FlatCost, "ComputeCostPerIncrement", result);
                Scalar718(ledger, row, "multiplier", option.CostMultiplierPercent, "ComputeCostPerIncrement", result);
                Scalar718(ledger, row, "multiplierbaseonly", option.BaseCostMultiplierPercent, "ComputeCostPerIncrement", result);
                Scalar718(ledger, row, "areamaximum", option.AreaMaximumModifier, "TryProject-aspect-maximum", result);
                Scalar718(ledger, row, "comfortsmaximum", option.ComfortsMaximumModifier, "TryProject-aspect-maximum", result);
                Scalar718(ledger, row, "securitymaximum", option.SecurityMaximumModifier, "TryProject-aspect-maximum", result);
                if (TryReadLifestyleAllowed1000(row, out var allowed)
                    && allowed.SequenceEqual(option.AllowedFreeLifestyleNames, StringComparer.Ordinal))
                    foreach (var field in row.Elements().Where(field => field.Name == "allowed" || field.Name == "allowedfreelifestyles"))
                        ledger.Tree(field, D.Compiled, new { Operation = "IsAllowedFreeLifestyle-and-TryProject", Allowed = allowed, Results = actual });
                // bonus/selecttext, allowmultiple and quality area/comforts/
                // security adjustments are not executed by this calculator.
                // Their operations deliberately stay open.
            }
        }
    }
}
