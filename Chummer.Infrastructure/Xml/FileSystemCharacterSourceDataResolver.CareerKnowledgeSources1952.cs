using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        public bool TryResolveCareerKnowledgeSkillName(string name,
            out CharacterKnowledgeSkillSource? source, out string authorityState)
        {
            using IDisposable scope = _sourceInputs.Enter();
            source = null;
            authorityState = string.Empty;
            if (!IsNamedKnowledgeText1952(name)
                || !TryReadKnowledgeDocument1952(out var document, out var catalog, out var authority)
                || !TryReadKnowledgeRows1952(document, out var rows)) return false;

            // Inspect the full effective document, not a filtered target/catalog.
            // Every raw identity must be readable before absence can be proven.
            var matches = new List<(XElement Row, Guid Id, string Name)>();
            bool alias = Guid.TryParse(name, out var enteredId);
            foreach (var row in rows)
            {
                if (!TryReadRequiredCanonicalScalar(row, "name", out var rowName)
                    || !TryReadRequiredCanonicalScalar(row, "id", out var rawId)
                    || !Guid.TryParseExact(rawId, "D", out var id) || id == Guid.Empty) return false;
                if (rowName == name || alias && id == enteredId) matches.Add((row, id, rowName));
            }
            if (matches.Count > 1) return false;
            if (matches.Count == 1)
            {
                var match = matches[0];
                if (match.Name != name || match.Row.Elements("hide").Any()) return false;
                var admitted = catalog.KnowledgeSkills.Where(x => x.SourceSkillId == match.Id.ToString("D"))
                    .Take(2).ToArray();
                if (admitted.Length != 1 || admitted[0].Name != name
                    || !TryResolveKnowledgeSkillSource(match.Id.ToString("D"), out var resolved)
                    || resolved.Name != name || resolved.SourceSkillId != admitted[0].SourceSkillId
                    || resolved.SkillCategory != admitted[0].Category || resolved.DefaultAttribute != admitted[0].DefaultAttribute)
                    return false;
                source = resolved;
            }
            if (_sourceInputs.HasSourceDrift) { source = null; return false; }
            authorityState = authority;
            return true;
        }

        public bool TryResolveCareerKnowledgeSkillCategories(
            out IReadOnlyDictionary<string, string> categories, out string authorityState)
        {
            using IDisposable scope = _sourceInputs.Enter();
            categories = new Dictionary<string, string>();
            authorityState = string.Empty;
            if (!TryReadKnowledgeDocument1952(out var document, out var catalog, out var authority)
                || !TryReadKnowledgeRows1952(document, out var rows)
                || document.Root!.Elements("categories").Take(2).ToArray() is not [var container]) return false;
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var category in container.Elements("category").Where(x => (string?)x.Attribute("type") == "knowledge"))
            {
                if (category.HasElements || string.IsNullOrWhiteSpace(category.Value)
                    || category.Value != category.Value.Trim() || !declared.Add(category.Value)) return false;
            }
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (row.Elements("hide").Any()) continue;
                if (!TryReadRequiredCanonicalScalar(row, "category", out var category)
                    || !TryReadRequiredCanonicalScalar(row, "attribute", out var attribute)
                    || !CharacterCreationStandardPrioritySkillsRules.IsSupportedAttribute(attribute)) return false;
                // Same assignment order as SkillsSection.KnowledgeSkillCategoriesMap.
                // Do not sort these rows or infer the map from the sorted catalog.
                map[category] = attribute;
            }
            var result = map.Where(x => declared.Contains(x.Key)
                && catalog.KnowledgeSkills.Any(skill => skill.Category == x.Key))
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            if (result.Count == 0 || _sourceInputs.HasSourceDrift) return false;
            categories = result;
            authorityState = authority;
            return true;
        }

        private static bool IsNamedKnowledgeText1952(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name != name.Trim()
                || name.Length > CharacterCareerKnowledgeSkillAdvanceRules.MaximumNameLength || name.Any(char.IsControl)) return false;
            try { return XmlConvert.VerifyXmlChars(name) == name; }
            catch (XmlException) { return false; }
        }

        private bool TryReadKnowledgeDocument1952(out XDocument document,
            out CharacterCreationSkillsCatalog catalog, out string authority)
        {
            document = null!; catalog = null!; authority = string.Empty;
            // Reuse exact profile/settings/skills currentness and the existing
            // selected-custom-skills-overlay rejection, without broadening it.
            if (_sourceInputs.HasSourceDrift || !TryResolveCreationSkillsCatalog(out var resolved) || resolved is null
                || !TryLoadEffectiveDocument(_catalog, "skills.xml", out var full)
                || full?.Root?.Name != "chummer" || _sourceInputs.HasSourceDrift) return false;
            document = full!; catalog = resolved;
            authority = "career-knowledge-source/v1\n" + _settingsProfileId + "\n"
                + _rawProfileInputsDigest + "\n" + _effectiveSkillsInputsDigest + "\n" + resolved.CatalogDigest;
            return true;
        }

        private static bool TryReadKnowledgeRows1952(XDocument document, out XElement[] rows)
        {
            rows = [];
            if (document.Root!.Elements("knowledgeskills").Take(2).ToArray() is not [var container]
                || container.HasAttributes || container.Elements().Any(x => x.Name != "skill")) return false;
            rows = container.Elements("skill").ToArray();
            return rows.Length != 0;
        }
    }
}
