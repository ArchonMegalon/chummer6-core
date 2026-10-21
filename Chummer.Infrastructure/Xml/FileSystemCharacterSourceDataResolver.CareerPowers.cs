using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        public bool TryResolveCareerPowerSource(Guid sourceId, out CharacterCareerPowerSource? source)
        {
            using IDisposable scope = _sourceInputs.Enter();
            source = null;
            if (_sourceInputs.HasSourceDrift || sourceId == Guid.Empty
                || !TryResolveTarget("powers.xml", ["powers"], "power", sourceId.ToString("D"), "",
                    out var row, out bool ambiguous, inspectIdentity: true)
                || row is null || ambiguous || !CareerPowerIdentity(row, "power", sourceId, out var name, out var book)
                || !IsEnabledSource(book)
                || !TryComputeEffectiveInputDigest(_catalog, "powers.xml", out var sourceDigest)
                || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, "powers.xml", out var custom)
                || _sourceInputs.HasSourceDrift) return false;
            source = new(sourceId, name, book, row.ToString(SaveOptions.DisableFormatting),
                sourceDigest, _rawProfileInputsDigest, custom);
            return true;
        }

        public bool TryResolveCareerPowerCatalog(out IReadOnlyList<CharacterCareerPowerSource> sources)
        {
            using IDisposable scope = _sourceInputs.Enter();
            sources = [];
            if (_sourceInputs.HasSourceDrift
                || !TryEnumerateTargets("powers.xml", ["powers"], "power", out var rows)
                || rows.Length > 4096) return false;
            // With no custom data or overlays, raw and effective rows are identical.
            // Resolve against one detached raw document for this call instead of
            // copying both full documents for every enabled row. Keep the exact
            // resolver for every custom/amend/overlay case.
            Dictionary<Guid, XElement?>? baseTargets = null;
            if (_customDirectories.Count == 0 && !_catalog.Overlays.Any(pack => pack.Enabled)
                && !_sourceInputs.HasSelectedProfile("powers.xml") && rows.Length != 0)
            {
                if (!TryLoadXml(Path.Combine(_catalog.BaseDataPath, "powers.xml"), out var raw)
                    || raw?.Root is null) return false;
                baseTargets = new();
                // Count across ALL raw containers, as CountTargetRows does. A
                // duplicate outside the first enumerated container still rejects
                // an enabled target. Guid parsing matches LocatorMatches.
                foreach (var row in raw.Root.Elements("powers").Elements("power"))
                    if (Guid.TryParse(ReadValue(row, "id"), out var id)
                        && !baseTargets.TryAdd(id, row)) baseTargets[id] = null;
            }
            var result = new List<CharacterCareerPowerSource>();
            var ids = new HashSet<Guid>();
            foreach (var row in rows)
            {
                if (!Guid.TryParse(row.Element("id")?.Value, out var id)
                    || !ids.Add(id) || !CareerPowerIdentity(row, "power", id, out _, out var book)) return false;
                if (!IsEnabledSource(book)) continue;
                CharacterCareerPowerSource? exact;
                if (baseTargets is null)
                {
                    if (!TryResolveCareerPowerSource(id, out exact) || exact is null) return false;
                }
                else
                {
                    // Enumeration alone is not identity authority. Reject raw
                    // ambiguity and apply the same exact-target source gates and
                    // byte/profile/custom digests as TryResolveCareerPowerSource.
                    if (!baseTargets.TryGetValue(id, out var target) || target is null
                        || !FilterSourcebookTarget800("powers.xml", ref target) || target is null
                        || !CareerPowerIdentity(target, "power", id, out var name, out var exactBook)
                        || !IsEnabledSource(exactBook)
                        || !TryComputeEffectiveInputDigest(_catalog, "powers.xml", out var sourceDigest)
                        || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, "powers.xml", out var custom)
                        || _sourceInputs.HasSourceDrift) return false;
                    exact = new(id, name, exactBook, target.ToString(SaveOptions.DisableFormatting),
                        sourceDigest, _rawProfileInputsDigest, custom);
                }
                result.Add(exact);
            }
            if (_sourceInputs.HasSourceDrift) return false;
            sources = Array.AsReadOnly(result.OrderBy(row => row.SourceId).ToArray());
            return true;
        }

        // Existing grant origins are read, never acquired or modified by this lane.
        public bool TryResolveCareerPowerGrantSource(string kind, Guid sourceId,
            out CharacterCareerPowerGrantSource? source)
        {
            using IDisposable scope = _sourceInputs.Enter();
            source = null;
            var location = kind switch
            {
                "Quality" => ("qualities.xml", "qualities", "quality"),
                "MentorSpirit" => ("mentors.xml", "mentors", "mentor"),
                "Metamagic" => ("metamagic.xml", "metamagics", "metamagic"),
                _ => ("", "", "")
            };
            if (location.Item1.Length == 0 || _sourceInputs.HasSourceDrift
                || !TryResolveTarget(location.Item1, [location.Item2], location.Item3, sourceId.ToString("D"), "",
                    out var row, out bool ambiguous, inspectIdentity: true)
                || row is null || ambiguous
                || !CareerPowerIdentity(row, location.Item3, sourceId, out _, out var book) || !IsEnabledSource(book)
                || !TryComputeEffectiveInputDigest(_catalog, location.Item1, out var digest)
                || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, location.Item1, out var custom)
                || _sourceInputs.HasSourceDrift) return false;
            var xml = row.ToString(SaveOptions.DisableFormatting);
            source = new(kind, sourceId, xml, string.Join('\0', kind, digest, custom, _rawProfileInputsDigest, xml));
            return true;
        }

        private static bool CareerPowerIdentity(XElement row, string element, Guid id,
            out string name, out string book)
        {
            name = book = "";
            if (row.Name != element || row.HasAttributes || id == Guid.Empty
                || row.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)) return false;
            bool Scalar(string key, out string value)
            {
                var node = row.Element(key);
                value = node?.Value ?? "";
                return node is not null && !node.HasAttributes && !node.HasElements
                    && value.Length > 0 && value == value.Trim();
            }
            return Scalar("id", out var text) && Guid.TryParse(text, out var actual) && actual == id
                && Scalar("name", out name) && Scalar("source", out book);
        }
    }
}
