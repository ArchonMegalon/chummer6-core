using System.Xml.Linq;
using Chummer.Application.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext : ICharacterCareerComplexFormLearningSourceContext,
        ICharacterCareerComplexFormLearningCatalogSourceContext
    {
        public bool TryResolveCareerComplexFormLearningCatalog(out CharacterCareerComplexFormLearningSourceCatalog? catalog)
        {
            using IDisposable scope = _sourceInputs.Enter();
            catalog = null;
            if (!TryComplexFormInputs(out var authority)
                || !TryEnumerateTargets("complexforms.xml", ["complexforms"], "complexform", out var rows)
                || rows.Length > 4096) return false;
            var sources = new List<CharacterCareerComplexFormLearningCatalogSource>();
            var ids = new HashSet<Guid>();
            long bytes = 0;
            foreach (var row in rows)
            {
                if (!Guid.TryParse(CatalogLiteral(row, "id"), out var id) || id == Guid.Empty || !ids.Add(id)
                    || !CareerPowerIdentity(row, "complexform", id, out _, out var book)) return false;
                if (!IsEnabledSource(book)) continue;
                if (!TryResolveCareerComplexFormLearningSource(id, out var exact) || exact is null) return false;
                var source = XElement.Parse(exact.ComplexFormXml);
                bytes += System.Text.Encoding.UTF8.GetByteCount(exact.ComplexFormXml);
                if (bytes > 16 * 1024 * 1024) return false;
                sources.Add(new(id, CatalogLiteral(source, "name"), book, CatalogLiteral(source, "page"), exact.ComplexFormXml));
            }
            if (_sourceInputs.HasSourceDrift) return false;
            catalog = new(Array.AsReadOnly(sources.OrderBy(row => row.SourceId).ToArray()),
                Array.AsReadOnly(_enabledSourcebooks.OrderBy(book => book, StringComparer.Ordinal).ToArray()), authority);
            return true;
        }

        public bool TryResolveCareerComplexFormLearningSource(Guid sourceId, out CharacterCareerComplexFormLearningSource? source)
        {
            using IDisposable scope = _sourceInputs.Enter();
            source = null;
            if (sourceId == Guid.Empty || !TryComplexFormInputs(out var authority)
                || !TryResolveTarget("complexforms.xml", ["complexforms"], "complexform", sourceId.ToString("D"), "",
                    out var row, out bool ambiguous, inspectIdentity: true)
                || row is null || ambiguous || !CareerPowerIdentity(row, "complexform", sourceId, out _, out var book)
                || !IsEnabledSource(book) || _sourceInputs.HasSourceDrift) return false;
            string xml = row.ToString(SaveOptions.DisableFormatting);
            if (xml.Length > 262144) return false;
            source = new(xml, string.Join('\0', authority, xml));
            return true;
        }

        private bool TryComplexFormInputs(out string authority)
        {
            authority = "";
            if (_sourceInputs.HasSourceDrift || !TryLoadEffectiveDocument(_catalog, "complexforms.xml", out var document)
                || document?.Root?.Name != "chummer") return false;
            // Root records only. Prerequisite references are not catalog entries.
            var containers = document.Root.Elements().Where(e => e.Name.LocalName == "complexforms").Take(2).ToArray();
            if (containers.Length != 1 || containers[0].Name != "complexforms" || containers[0].HasAttributes
                || containers[0].Elements().Any(e => e.Name != "complexform")
                || !TryComputeEffectiveInputDigest(_catalog, "complexforms.xml", out var digest)
                || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, "complexforms.xml", out var custom)
                || _sourceInputs.HasSourceDrift) return false;
            authority = string.Join('\0', "sr5-career-complex-form-sources/v1", digest, custom, _rawProfileInputsDigest);
            return true;
        }

        public bool TryResolveCareerResonanceSource(out CharacterCareerResonanceSource? source)
        {
            using IDisposable scope = _sourceInputs.Enter();
            source = null;
            string metatypeText = CatalogLiteral(_character, "metatypeid"), alternate = CatalogLiteral(_character, "metatypeguid");
            if (metatypeText.Length == 0) metatypeText = alternate;
            if (_sourceInputs.HasSourceDrift || alternate.Length != 0 && alternate != metatypeText
                || !Guid.TryParse(metatypeText, out var metatypeId)
                || metatypeId == Guid.Empty
                || !TryResolveTarget("metatypes.xml", ["metatypes"], "metatype", metatypeId.ToString("D"), "",
                    out var metatype, out bool ambiguousMetatype, inspectIdentity: true)
                || metatype is null || ambiguousMetatype
                || !CareerPowerIdentity(metatype, "metatype", metatypeId, out _, out var metatypeBook) || !IsEnabledSource(metatypeBook)
                || !TryComputeEffectiveInputDigest(_catalog, "metatypes.xml", out var metatypesDigest)
                || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, "metatypes.xml", out var metatypesCustom)
                || !TryComputeEffectiveInputDigest(_catalog, "qualities.xml", out var qualitiesDigest)
                || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, "qualities.xml", out var qualitiesCustom)) return false;
            var boxes = _character.Elements().Where(e => e.Name.LocalName == "qualities").Take(2).ToArray();
            if (boxes.Length != 1 || boxes[0].Name != "qualities" || boxes[0].HasAttributes
                || boxes[0].Elements().Any(e => e.Name != "quality") || boxes[0].Elements().Count() > 4096) return false;
            var entries = new List<CharacterCareerResonanceQualitySource>();
            var ids = new HashSet<Guid>();
            long bytes = 0;
            foreach (var owned in boxes[0].Elements("quality"))
            {
                if (!Guid.TryParse(CatalogLiteral(owned, "guid"), out var instanceId) || instanceId == Guid.Empty || !ids.Add(instanceId)
                    || !Guid.TryParse(CatalogLiteral(owned, "sourceid"), out var id) || id == Guid.Empty
                    || !TryResolveTarget("qualities.xml", ["qualities"], "quality", id.ToString("D"), "",
                        out var row, out bool ambiguous, inspectIdentity: true)
                    || row is null || ambiguous || !CareerPowerIdentity(row, "quality", id, out var name, out var book)
                    || !IsEnabledSource(book) || name != CatalogLiteral(owned, "name") || book != CatalogLiteral(owned, "source")) return false;
                string xml = row.ToString(SaveOptions.DisableFormatting);
                bytes += System.Text.Encoding.UTF8.GetByteCount(xml);
                if (xml.Length > 262144 || bytes > 8 * 1024 * 1024) return false;
                entries.Add(new(instanceId, id, xml));
            }
            string metatypeXml = metatype.ToString(SaveOptions.DisableFormatting);
            if (metatypeXml.Length > 262144 || _sourceInputs.HasSourceDrift) return false;
            source = new(metatypeXml, Array.AsReadOnly(entries.ToArray()), string.Join('\0', "sr5-career-resonance-sources/v1",
                metatypesDigest, metatypesCustom, qualitiesDigest, qualitiesCustom, _rawProfileInputsDigest,
                metatypeXml, System.Text.Json.JsonSerializer.Serialize(entries)));
            return true;
        }
    }
}
