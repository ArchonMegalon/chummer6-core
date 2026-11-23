using System.Globalization;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext : ICharacterCareerMartialAcquisitionSourceContext
    {
        private static readonly IReadOnlySet<string> MartialSavedEffectFields2421 = new HashSet<string>(StringComparer.Ordinal)
        {
            "unique", "target", "improvedname", "sourcename", "min", "max", "aug", "augmax", "val", "rating",
            "exclude", "condition", "improvementttype", "improvementsource", "custom", "customname", "customid",
            "customgroup", "addtorating", "enabled", "order", "notes", "notesColor"
        };

        // This only classifies a contribution to FreeKnowledgeSkills. It grants no
        // source, effect, purchase or write authority. Current admission still executes
        // the proposed graph; capture still binds its complete, unmodified XML.
        private bool IsKnowledgeNeutralMartialImprovement2421(XElement row)
        {
            if (row.Name != "improvement" || row.Elements().Count() != MartialSavedEffectFields2421.Count
                || row.Elements().Any(field => field.Name.Namespace != XNamespace.None)
                || !HasStrictAllowedShape(row, MartialSavedEffectFields2421)) return false;
            string Value(string name) => row.Element(name)!.Value;
            if (new[] { "min", "max", "aug", "augmax", "addtorating", "order" }.Any(name => Value(name) != "0")
                || new[] { "target", "exclude", "condition", "customname", "customid", "customgroup", "notes" }
                    .Any(name => Value(name) != "")
                || Value("rating") != "1" || Value("enabled") != "1" || Value("custom") != "False"
                || Value("notesColor") != "#000000"
                || !Guid.TryParseExact(Value("sourcename"), "D", out var source) || source == Guid.Empty) return false;
            string type = Value("improvementttype");
            bool art = type is "SkillSpecializationOption" or "SkillSpecialization";
            if (!art && type != "UnarmedReach"
                || Value("improvementsource") != (art ? "MartialArt" : "MartialArtTechnique")) return false;
            var arts = _character.Elements("martialarts").Elements("martialart");
            // MartialArt.LoadCoreAsync loads both saved containers. Count their union
            // before classifying neutrality, so a cross-container GUID collision still
            // fails closed. Source membership and exact effects remain admission-owned.
            var owners = (art ? arts : arts.Elements("martialartadvantages").Elements("martialartadvantage")
                .Concat(arts.Elements("martialarttechniques").Elements("martialarttechnique")))
                .Where(item => item.Elements("guid").ToArray() is [var guid]
                    && !guid.HasAttributes && !guid.HasElements && guid.Value == source.ToString("D")).Take(2).ToArray();
            if (owners.Length != 1) return false;
            if (!art) return Value("unique") == "" && Value("improvedname") == ""
                && int.TryParse(Value("val"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            if (Value("val") != "0" || Value("improvedname").Length == 0 || Value("unique").Length == 0) return false;
            var skills = _character.Elements("newskills").Elements("skills").Elements("skill")
                .Where(skill => (string?)skill.Element("name") == Value("improvedname")).Take(2).ToArray();
            if (skills.Length != 1) return false;
            return type == "SkillSpecializationOption"
                || Guid.TryParseExact(Value("unique"), "D", out var specialization) && specialization != Guid.Empty
                    && skills[0].Elements("specs").Elements("spec")
                        .Count(spec => (string?)spec.Element("guid") == specialization.ToString("D")) == 1;
        }

        private bool CaptureMartialDependencyInputs2193()
        {
            // Absence is retained too; it does not authorize installed/default-source fallback.
            bool present = SourceFileExists(Path.Combine(_catalog.BaseDataPath, "martialarts.xml"));
            // Retained inventories are keyed by the exact query, not just its results.
            // Both current catalog projection and identity-sensitive target resolution
            // enumerate this pattern; the digest's *.xml inventory cannot stand in for it.
            foreach (CustomDirectory directory in _customDirectories)
                _ = EnumerateSourceFiles(directory.Path, "*_martialarts.xml", SearchOption.AllDirectories);
            return (!present || TryComputeEffectiveInputDigest(_catalog, "martialarts.xml", out _))
                && TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, "martialarts.xml", out _)
                && !_sourceInputs.HasSourceDrift;
        }

        public bool TryResolveCareerMartialAcquisitionSource(Guid sourceId, CharacterCareerMartialAcquisitionKind kind,
            out CharacterCareerMartialAcquisitionSource? source)
        {
            using IDisposable scope = _sourceInputs.Enter();
            source = null;
            if (sourceId == Guid.Empty || !Enum.IsDefined(kind) || !TryMartialInputs(out _, out var authority)) return false;
            bool art = kind == CharacterCareerMartialAcquisitionKind.Art;
            string tag = art ? "martialart" : "technique";
            if (!TryResolveTarget("martialarts.xml", [art ? "martialarts" : "techniques"], tag,
                    sourceId.ToString("D"), "", out var row, out bool ambiguous, inspectIdentity: true)
                || row is null || ambiguous || !CareerPowerIdentity(row, tag, sourceId, out _, out var book)
                || !IsEnabledSource(book) || _sourceInputs.HasSourceDrift) return false;
            string xml = row.ToString(SaveOptions.DisableFormatting);
            if (xml.Length > 262144) return false;
            source = new(xml, string.Join('\0', authority, xml));
            return true;
        }

        public bool TryResolveCareerMartialAcquisitionCatalog(out CharacterCareerMartialAcquisitionSourceCatalog? catalog)
        {
            using IDisposable scope = _sourceInputs.Enter();
            catalog = null;
            if (!TryMartialInputs(out var document, out var authority)) return false;
            var entries = new List<CharacterCareerMartialAcquisitionCatalogSource>();
            long bytes = 0;
            foreach (var kind in new[] { CharacterCareerMartialAcquisitionKind.Art, CharacterCareerMartialAcquisitionKind.Technique })
            {
                bool art = kind == CharacterCareerMartialAcquisitionKind.Art;
                string tag = art ? "martialart" : "technique";
                var ids = new HashSet<Guid>();
                foreach (var row in document!.Root!.Element(art ? "martialarts" : "techniques")!.Elements())
                {
                    if (!Guid.TryParse(CatalogLiteral(row, "id"), out var id) || id == Guid.Empty || !ids.Add(id)
                        || !CareerPowerIdentity(row, tag, id, out _, out var book)) return false;
                    if (!IsEnabledSource(book)) continue;
                    if (!TryResolveCareerMartialAcquisitionSource(id, kind, out var exact) || exact is null) return false;
                    bytes += System.Text.Encoding.UTF8.GetByteCount(exact.Xml);
                    if (entries.Count >= 4096 || bytes > 8 * 1024 * 1024) return false;
                    entries.Add(new(id, kind, exact.Xml));
                }
            }
            if (_sourceInputs.HasSourceDrift) return false;
            catalog = new(Array.AsReadOnly(entries.ToArray()), authority);
            return true;
        }

        private bool TryMartialInputs(out XDocument? document, out string authority)
        {
            authority = ""; document = null;
            if (_sourceInputs.HasSourceDrift || !TryLoadEffectiveDocument(_catalog, "martialarts.xml", out document)
                || document?.Root?.Name != "chummer") return false;
            if (_sourceInputs.HasMartialKnowledgeRecipe2421)
            {
                // MartialArt.cs retains a cost of 7 when the source omits this optional
                // scalar. Materialize that typed default on the detached effective base
                // before ordered amendments, so REPLACE targets its real effective value.
                // Raw retained bytes, input digests and ambiguous-contributor checks stay exact.
                foreach (var art in document.Root.Elements("martialarts").Elements("martialart"))
                    if (!art.Elements().Any(field => field.Name.LocalName == "cost"))
                        art.Add(new XElement("cost", 7));
                _sourceInputs.SetEffectiveDocument(CreateSourceCacheKey("effective-document", "martialarts.xml"), document);
            }
            if (!TryProjectCurrentSourceDocument("martialarts.xml", [("martialarts", "martialart"), ("techniques", "technique")], ref document)) return false;
            foreach (var (box, tag) in new[] { ("martialarts", "martialart"), ("techniques", "technique") })
            {
                var containers = document.Root.Elements().Where(e => e.Name.LocalName == box).Take(2).ToArray();
                if (containers.Length != 1 || containers[0].Name != box || containers[0].HasAttributes
                    || containers[0].Elements().Any(e => e.Name != tag)) return false;
            }
            if (!TryComputeEffectiveInputDigest(_catalog, "martialarts.xml", out var digest)
                || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, "martialarts.xml", out var custom)
                || _sourceInputs.HasSourceDrift) return false;
            authority = string.Join('\0', "sr5-career-martial-sources/v1", digest, custom, _rawProfileInputsDigest);
            return true;
        }
    }
}
