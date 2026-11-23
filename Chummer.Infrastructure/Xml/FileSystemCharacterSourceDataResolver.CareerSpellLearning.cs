using System.Xml.Linq;
using Chummer.Application.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext : ICharacterCareerSpellLearningSourceContext,
        ICharacterCareerSpellLearningCatalogSourceContext
    {
        public bool TryResolveCareerSpellLearningCatalog(out CharacterCareerSpellLearningSourceCatalog? catalog)
        {
            using IDisposable scope = _sourceInputs.Enter();
            catalog = null;
            if (_sourceInputs.HasSourceDrift
                || !TryLoadEffectiveDocument(_catalog, "spells.xml", out var document)
                || document?.Root?.Name != "chummer") return false;
            var containers = document.Root.Elements().Where(row => row.Name.LocalName == "spells").Take(2).ToArray();
            if (containers.Length != 1 || containers[0].Name != "spells" || containers[0].HasAttributes
                || containers[0].Elements().Any(row => row.Name != "spell")
                || !TryEnumerateTargets("spells.xml", ["spells"], "spell", out var rows)
                || rows.Length > 4096
                || !TryComputeEffectiveInputDigest(_catalog, "spells.xml", out var spellsDigest)
                || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, "spells.xml", out var spellsCustom)
                || !TryComputeEffectiveInputDigest(_catalog, "traditions.xml", out var traditionsDigest)
                || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, "traditions.xml", out var traditionsCustom))
                return false;
            var sources = new List<CharacterCareerSpellLearningCatalogSource>();
            var ids = new HashSet<Guid>();
            long bytes = 0;
            foreach (var row in rows)
            {
                if (!Guid.TryParse(row.Element("id")?.Value, out var id) || id == Guid.Empty || !ids.Add(id)
                    || !CareerPowerIdentity(row, "spell", id, out _, out var book)) return false;
                if (!IsEnabledSource(book)) continue;
                // Use the exact lookup too: enumeration must not mask duplicate/custom locator ambiguity.
                if (!TryResolveTarget("spells.xml", ["spells"], "spell", id.ToString("D"), "",
                        out var exact, out bool ambiguous, inspectIdentity: true)
                    || exact is null || ambiguous
                    || !CareerPowerIdentity(exact, "spell", id, out var name, out var exactBook)
                    || exactBook != book || !IsEnabledSource(exactBook)) return false;
                // Missing/unknown semantic fields remain visible and are rejected by canonical evaluation.
                string xml = exact.ToString(SaveOptions.DisableFormatting);
                bytes += System.Text.Encoding.UTF8.GetByteCount(xml);
                if (xml.Length > 262144 || bytes > 16 * 1024 * 1024) return false;
                sources.Add(new(id, name, CatalogLiteral(exact, "category"), exactBook, CatalogLiteral(exact, "page"), xml));
            }
            if (_sourceInputs.HasSourceDrift) return false;
            catalog = new(Array.AsReadOnly(sources.OrderBy(row => row.SourceId).ToArray()),
                Array.AsReadOnly(_enabledSourcebooks.OrderBy(book => book, StringComparer.Ordinal).ToArray()),
                string.Join('\0', "sr5-career-spell-learning-catalog-sources/v1", spellsDigest, spellsCustom,
                    traditionsDigest, traditionsCustom, _rawProfileInputsDigest));
            return true;
        }

        private static string CatalogLiteral(XElement row, string field)
        {
            var nodes = row.Elements().Where(node => node.Name.LocalName == field).Take(2).ToArray();
            return nodes.Length == 1 && nodes[0].Name == field && !nodes[0].HasElements && !nodes[0].HasAttributes
                ? nodes[0].Value.Trim() : "";
        }

        public bool TryResolveCareerSpellLearningSource(Guid spellId, Guid traditionId,
            out CharacterCareerSpellLearningSource? source)
        {
            using IDisposable scope = _sourceInputs.Enter();
            source = null;
            if (_sourceInputs.HasSourceDrift || spellId == Guid.Empty || traditionId == Guid.Empty
                || !TryResolveTarget("spells.xml", ["spells"], "spell", spellId.ToString("D"), "",
                    out var spell, out bool ambiguousSpell, inspectIdentity: true)
                || spell is null || ambiguousSpell || !CareerPowerIdentity(spell, "spell", spellId, out _, out var spellBook)
                || !IsEnabledSource(spellBook)
                || !TryResolveTarget("traditions.xml", ["traditions"], "tradition", traditionId.ToString("D"), "",
                    out var tradition, out bool ambiguousTradition, inspectIdentity: true)
                || tradition is null || ambiguousTradition
                || !CareerPowerIdentity(tradition, "tradition", traditionId, out _, out var traditionBook)
                || !IsEnabledSource(traditionBook)
                || !TryComputeEffectiveInputDigest(_catalog, "spells.xml", out var spellsDigest)
                || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, "spells.xml", out var spellsCustom)
                || !TryComputeEffectiveInputDigest(_catalog, "traditions.xml", out var traditionsDigest)
                || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, "traditions.xml", out var traditionsCustom)
                || _sourceInputs.HasSourceDrift) return false;
            string spellXml = spell.ToString(SaveOptions.DisableFormatting);
            string traditionXml = tradition.ToString(SaveOptions.DisableFormatting);
            source = new(spellXml, traditionXml, string.Join('\0', "sr5-career-spell-learning-sources/v1",
                spellsDigest, spellsCustom, traditionsDigest, traditionsCustom, _rawProfileInputsDigest, spellXml, traditionXml));
            return true;
        }
    }
}
