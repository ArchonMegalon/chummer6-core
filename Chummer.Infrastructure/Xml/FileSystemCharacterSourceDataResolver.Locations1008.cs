using System.Xml.Linq;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private bool TryResolveLifestyleCatalogues1008(out CharacterCreationLifestyleLocationCatalog? locations,
            out IReadOnlyList<CharacterCreationLifestyleCategory>? categories)
        {
            locations = null;
            categories = null;
            if (!TryHasSelectedCustomDataInputFor(_customDirectories, "lifestyles.xml", out bool custom)) return false;
            // Legacy custom-directory rows use a separate merge interpreter. Until
            // that interpreter exposes these containers, retain its ordinary text
            // behavior and leave their Complete operations unresolved.
            if (custom) return true;
            return TryLoadEffectiveDocument(_catalog, "lifestyles.xml", out var document)
                && document?.Root is { } root && TryReadLifestyleCatalogues1008(root, out locations, out categories);
        }

        private static bool TryReadLifestyleCatalogues1008(XElement root,
            out CharacterCreationLifestyleLocationCatalog? locations,
            out IReadOnlyList<CharacterCreationLifestyleCategory>? categories)
        {
            locations = null;
            categories = null;
            if (root.Name != "chummer"
                || !OptionalContainer1008(root, "cities", out var cities)
                || !OptionalContainer1008(root, "categories", out var declaration)) return false;
            if (cities is not null)
            {
                var ids = new HashSet<Guid>();
                int remaining = 65_536;
                if (!TryReadLocationRows1008(cities, "city", 0, "lifestyles.xml#/cities", ids, ref remaining, out var rows)) return false;
                locations = new(rows);
                if (!CharacterCreationLifestyleLocationRules.IsValidCatalog(locations)) return false;
            }
            if (declaration is not null)
            {
                var compiled = new List<CharacterCreationLifestyleCategory>();
                foreach (var field in declaration.Elements())
                {
                    if (field.Name != "category" || !Leaf1008(field)) return false;
                    string name = field.Value.Trim();
                    string type = CharacterCreationLifestyleLocationRules.QualityType(name);
                    compiled.Add(new(name, type));
                }
                if (!CharacterCreationLifestyleLocationRules.IsValidCategories(compiled)) return false;
                categories = compiled.ToArray();
            }
            return true;
        }

        private static bool OptionalContainer1008(XElement parent, string name, out XElement? container)
        {
            var matches = parent.Elements().Where(field => field.Name.LocalName == name).ToArray();
            container = null;
            if (matches.Length == 0) return true;
            if (matches is not [var field] || field.Name != name || !ContainerShape1008(field)) return false;
            container = field;
            return true;
        }

        private static bool ContainerShape1008(XElement field) => !field.HasAttributes
            && !HasDirectScopeText(field) && !field.Nodes().OfType<XProcessingInstruction>().Any();

        private static bool Leaf1008(XElement field) => !field.HasAttributes && !field.HasElements
            && !field.Nodes().OfType<XProcessingInstruction>().Any();

        private static bool TryReadLocationRows1008(XElement parent, string rowName, int depth, string parentPath,
            HashSet<Guid> ids, ref int remaining, out CharacterCreationLifestyleLocationNode[] result)
        {
            result = [];
            var rows = new List<CharacterCreationLifestyleLocationNode>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in parent.Elements().Where(field => field.Name != "name" && field.Name != "id" && field.Name != "secRating"))
            {
                if (--remaining < 0 || row.Name != rowName || !ContainerShape1008(row)) return false;
                string path = parentPath + "/" + rowName + "["
                    + (rows.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
                string? childName = depth == 0 ? "district" : depth == 1 ? "borough" : null;
                if (row.Elements().Any(field => field.Name != "name" && field.Name != "id"
                    && !(depth == 2 && field.Name == "secRating") && (childName is null || field.Name != childName))) return false;
                if (row.Elements("name").ToArray() is not [var name] || !Leaf1008(name)) return false;
                string label = name.Value.Trim();
                if (!names.Add(label)) return false;
                Guid? sourceId = null;
                var keys = row.Elements("id").ToArray();
                if (keys.Length != 0)
                {
                    if (keys is not [var key] || !Leaf1008(key) || !Guid.TryParse(key.Value.Trim(), out var id)
                        || id == Guid.Empty || !ids.Add(id)) return false;
                    sourceId = id;
                }
                string? rating = null;
                var ratings = row.Elements("secRating").ToArray();
                if (ratings.Length != 0)
                {
                    if (ratings is not [var security] || !Leaf1008(security) || string.IsNullOrWhiteSpace(security.Value)) return false;
                    rating = security.Value.Trim();
                }
                CharacterCreationLifestyleLocationNode[] children = [];
                if (childName is not null && !TryReadLocationRows1008(row, childName, depth + 1, path, ids, ref remaining, out children)) return false;
                rows.Add(new(sourceId, label, path, rating,
                    CharacterCreationFinalizationDigest.Compute(row.ToString(SaveOptions.DisableFormatting)), children));
            }
            // The top-level container admits rows only (not a stray name/id/rating).
            if (depth == 0 && parent.Elements().Any(field => field.Name != rowName)) return false;
            result = rows.ToArray();
            return true;
        }

        private static bool TryReadLifestyleSelection1008(XElement row,
            out CharacterCreationLifestyleQualitySelectionSemantics? semantics)
        {
            semantics = null;
            if (!OptionalContainer1008(row, "bonus", out var bonus)) return false;
            bool text = false;
            if (bonus is not null && bonus.HasElements)
            {
                if (bonus.Elements().ToArray() is not [var prompt] || prompt.Name != "selecttext"
                    || !Leaf1008(prompt) || !string.IsNullOrWhiteSpace(prompt.Value)) return false;
                text = true;
            }
            var multiple = row.Elements().Where(field => field.Name.LocalName == "allowmultiple").ToArray();
            if (multiple.Length != 0 && (multiple is not [var flag] || flag.Name != "allowmultiple"
                || !Leaf1008(flag) || !string.IsNullOrWhiteSpace(flag.Value))) return false;
            semantics = new(text, multiple.Length == 1);
            return true;
        }

        // Reached only after every actual paid/fallback projection has replayed
        // against the fresh authority in ExecuteLifestyleSupporting832.
        private static void ExecuteLifestyleLocations1008(XElement root, CharacterCreationLifestylesAuthority authority,
            IReadOnlyList<CharacterCreationLifestyleProjection> projections, SourceScopeLedger ledger)
        {
            if (!TryReadLifestyleCatalogues1008(root, out var catalogue, out var categories)
                || CharacterCreationFinalizationDigest.Compute(catalogue) != CharacterCreationFinalizationDigest.Compute(authority.LocationCatalog)
                || CharacterCreationFinalizationDigest.Compute(categories) != CharacterCreationFinalizationDigest.Compute(authority.Categories)) return;
            if (categories is not null && root.Element("categories") is { } declaration)
            {
                // Every declaration participates in the compiler's membership
                // and type lookup, including categories with no selected quality.
                var classified = authority.QualityOptions.Select(option => new { option.OptionId, option.Category, option.QualityType }).ToArray();
                ledger.Tree(declaration, D.Compiled, new { Operation = "lifestyle-category-admission-and-classification", categories, classified });
            }
            if (catalogue is null || root.Element("cities") is not { } cities) return;
            var selected = new HashSet<string>(StringComparer.Ordinal);
            var results = new List<CharacterCreationLifestyleLocationSelection>();
            foreach (var projection in projections)
            {
                if (!CharacterCreationLifestyleLocationRules.TrySelect(projection.Configuration, catalogue, out var location)
                    || CharacterCreationFinalizationDigest.Compute(location) != CharacterCreationFinalizationDigest.Compute(projection.Location)) return;
                results.Add(location);
                foreach (var component in new[] { location.City, location.District, location.Borough })
                    if (component.SourcePath is { } path) selected.Add(path);
            }
            object execution = new { Operation = "TryProject-hierarchical-location-selection", Results = results.ToArray(),
                Projections = projections.Select(projection => projection.LifestyleDigest).ToArray() };
            ledger.Node(cities, D.Structural, execution);
            AccountLocationRows1008(cities.Elements(), "lifestyles.xml#/cities", selected, execution, ledger);
        }

        private static void AccountLocationRows1008(IEnumerable<XElement> rows, string parentPath, HashSet<string> selected,
            object execution, SourceScopeLedger ledger)
        {
            int index = 0;
            foreach (var row in rows)
            {
                string path = parentPath + "/" + row.Name.LocalName + "["
                    + (++index).ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
                if (!selected.Contains(path))
                {
                    ledger.Tree(row, D.NotSelected, execution);
                    continue;
                }
                ledger.Node(row, D.Structural, execution);
                foreach (var field in row.Elements().Where(field => field.Name == "name" || field.Name == "id" || field.Name == "secRating"))
                    ledger.Tree(field, D.Compiled, execution);
                AccountLocationRows1008(row.Elements().Where(field => field.Name == "district" || field.Name == "borough"), path, selected, execution, ledger);
            }
        }

        private static void ExecuteLifestylePrompts1008(XElement root, CharacterCreationLifestylesAuthority authority,
            IReadOnlyList<CharacterCreationLifestyleProjection> projections, SourceScopeLedger ledger)
        {
            foreach (var option in authority.QualityOptions)
            {
                var selected = projections.SelectMany(projection => projection.Configuration.Qualities)
                    .Where(selection => selection.OptionId == option.OptionId).ToArray();
                if (selected.Length == 0 || option.SelectionSemantics is null) continue;
                var rows = root.Element("qualities")!.Elements("quality")
                    .Where(row => Guid.TryParse(row.Element("id")?.Value, out var id) && id == option.SourceId).ToArray();
                if (rows is not [var row] || !TryReadLifestyleSelection1008(row, out var semantics)
                    || semantics != option.SelectionSemantics) continue;
                object execution = new { Operation = "TryProject-quality-prompt-and-multiplicity", option.OptionId, semantics,
                    Selected = selected, Projections = projections.Select(projection => projection.LifestyleDigest).ToArray() };
                foreach (var field in row.Elements().Where(field => field.Name == "bonus" || field.Name == "allowmultiple"))
                    ledger.Tree(field, D.Compiled, execution);
            }
        }
    }
}
