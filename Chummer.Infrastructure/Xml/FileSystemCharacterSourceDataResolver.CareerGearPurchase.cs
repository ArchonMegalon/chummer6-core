using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        public bool TryResolveCareerGearPurchaseSource(Guid sourceId, out CharacterCareerGearPurchaseSource? source)
        {
            using IDisposable scope = _sourceInputs.Enter();
            source = null;
            if (_sourceInputs.HasSourceDrift || sourceId == Guid.Empty
                || !TryResolveTarget("gear.xml", ["gears"], "gear", sourceId.ToString("D"), "",
                    out var row, out bool ambiguous, inspectIdentity: true)
                || row is null || ambiguous || !CareerGearPurchaseIdentity(row, "gear", sourceId, out var name, out var book)
                || !IsEnabledSource(book)
                || !TryComputeEffectiveInputDigest(_catalog, "gear.xml", out var sourceDigest)
                || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, "gear.xml", out var custom)
                || _sourceInputs.HasSourceDrift) return false;
            source = new(sourceId, name, book, row.ToString(SaveOptions.DisableFormatting),
                sourceDigest, _rawProfileInputsDigest, custom);
            return true;
        }

        public bool TryResolveCareerGearPurchaseCatalog(out IReadOnlyList<CharacterCareerGearPurchaseSource> sources)
        {
            using IDisposable scope = _sourceInputs.Enter();
            sources = [];
            if (_sourceInputs.HasSourceDrift
                || !TryEnumerateTargets("gear.xml", ["gears"], "gear", out var rows)
                || rows.Length > 4096) return false;
            // With no custom data or overlays, raw and effective rows are identical.
            // Resolve against one detached raw document for this call instead of
            // copying both full documents for every enabled row. Keep the exact
            // resolver for every custom/amend/overlay case.
            Dictionary<Guid, XElement?>? baseTargets = null;
            if (_customDirectories.Count == 0 && !_catalog.Overlays.Any(pack => pack.Enabled)
                && !_sourceInputs.HasSelectedProfile("gear.xml") && rows.Length != 0)
            {
                if (!TryLoadXml(Path.Combine(_catalog.BaseDataPath, "gear.xml"), out var raw)
                    || raw?.Root is null) return false;
                baseTargets = new();
                // Count across ALL raw containers, as CountTargetRows does. A
                // duplicate outside the first enumerated container still rejects
                // an enabled target. Guid parsing matches LocatorMatches.
                foreach (var row in raw.Root.Elements("gears").Elements("gear"))
                    if (Guid.TryParse(ReadValue(row, "id"), out var id)
                        && !baseTargets.TryAdd(id, row)) baseTargets[id] = null;
            }
            var result = new List<CharacterCareerGearPurchaseSource>();
            var ids = new HashSet<Guid>();
            // Retain only writer scratch for this catalog, never row values,
            // digests or source authority. Custom resolution remains unchanged.
            using var xmlWriter = new ElementXmlWriter();
            foreach (var row in rows)
            {
                if (!Guid.TryParse(row.Element("id")?.Value, out var id)
                    || !ids.Add(id) || !CareerGearPurchaseIdentity(row, "gear", id, out _, out var book)) return false;
                if (!IsEnabledSource(book)) continue;
                CharacterCareerGearPurchaseSource? exact;
                if (baseTargets is null)
                {
                    if (!TryResolveCareerGearPurchaseSource(id, out exact) || exact is null) return false;
                }
                else
                {
                    // Enumeration alone is not identity authority. Reject raw
                    // ambiguity and apply the same exact-target source gates and
                    // byte/profile/custom digests as TryResolveCareerGearPurchaseSource.
                    if (!baseTargets.TryGetValue(id, out var target) || target is null
                        || !FilterSourcebookTarget800("gear.xml", ref target) || target is null
                        || !CareerGearPurchaseIdentity(target, "gear", id, out var name, out var exactBook)
                        || !IsEnabledSource(exactBook)
                        || !TryComputeEffectiveInputDigest(_catalog, "gear.xml", out var sourceDigest)
                        || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, "gear.xml", out var custom)
                        || _sourceInputs.HasSourceDrift) return false;
                    exact = new(id, name, exactBook, xmlWriter.Serialize(target),
                        sourceDigest, _rawProfileInputsDigest, custom);
                }
                result.Add(exact);
            }
            if (_sourceInputs.HasSourceDrift) return false;
            sources = Array.AsReadOnly(result.OrderBy(row => row.SourceId).ToArray());
            return true;
        }

        private static bool CareerGearPurchaseIdentity(XElement row, string kind, Guid expected,
            out string name, out string book)
        {
            name = book = string.Empty;
            if (row.Name != kind || row.HasAttributes || expected == Guid.Empty) return false;
            string? Scalar(string key) => row.Elements(key).ToArray() is [var field]
                && !field.HasAttributes && !field.HasElements ? field.Value.Trim() : null;
            if (!Guid.TryParse(Scalar("id"), out var actual) || actual != expected) return false;
            name = Scalar("name") ?? ""; book = Scalar("source") ?? "";
            return name.Length > 0 && book.Length > 0;
        }
    }
}
