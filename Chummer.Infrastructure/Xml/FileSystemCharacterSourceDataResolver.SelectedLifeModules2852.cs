using System.Xml.Linq;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        // Live marked sources and explicit retained V7 must project the same
        // selected inputs. An older retained reader never inherits this grammar
        // from the character marker (including the observation-only V1 reader).
        private bool UsesSelectedV7Sources2852 => _sourceInputs.Accounting is { } accounting
            ? accounting.HasLifestyleMechanics2684 : HasLifestyleMechanics2684;

        private bool TryReadSelectedLifeModulesDocument2852(out XDocument? document)
        {
            if (!TryLoadEffectiveDocument(_catalog, "lifemodules.xml", out document)
                || document?.Root is not { } root) return false;
            if (!UsesSelectedV7Sources2852) return true;
            if (!TryHasSelectedCustomDataInputFor(_customDirectories, "lifemodules.xml", out bool custom)) return false;
            if (!custom) return true; // Preserve the original raw catalog bytes/digest.

            if (root.Elements("modules").ToArray() is not [var modules] || modules.HasAttributes
                || HasDirectScopeText(modules) || modules.Elements().Any(row => row.Name != "module")) return false;
            var baseRows = modules.Elements("module").ToArray();
            if (baseRows.Any(row => !TryModuleId2852(row, out _))
                || baseRows.Select(row => ReadValue(row, "id")).Distinct(StringComparer.OrdinalIgnoreCase).Count() != baseRows.Length)
                return false;

            var locators = new List<TargetLocator>();
            AddLocators(root, ["modules"], "module", locators);
            var fragments = new List<(string Prefix, XElement Root)>();
            foreach (CustomDirectory directory in _customDirectories)
            {
                string[] paths = EnumerateSourceFiles(directory.Path, "*_lifemodules.xml", SearchOption.AllDirectories)
                    .Where(path => IsLegacyCustomDataInputFor(path, "lifemodules.xml"))
                    .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(path => path, StringComparer.Ordinal).ToArray();
                foreach (string prefix in new[] { "override_", "custom_", "amend_" })
                foreach (string path in paths.Where(path => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    // This bridge projects module rows, not stage/story headers.
                    // Refuse unsupported siblings rather than silently dropping them.
                    if (!TryLoadXml(path, out var fragment) || fragment?.Root is not { } selected
                        || selected.Name != "chummer" || selected.HasAttributes || HasDirectScopeText(selected)
                        || selected.Elements().ToArray() is not [var rows] || rows.Name != "modules"
                        || rows.HasAttributes || HasDirectScopeText(rows)
                        || rows.Elements().Any(row => row.Name != "module"
                            || row.Attributes().Any(attribute => attribute.Name.LocalName is not ("amendoperation" or "addifnotfound")
                                || attribute.Name.NamespaceName.Length != 0
                                || attribute.Name == "addifnotfound" && !bool.TryParse(attribute.Value, out _))
                            || row.Elements("id").Any() && !TryModuleId2852(row, out _)
                            || !row.Elements("id").Any() && string.IsNullOrWhiteSpace(ReadValue(row, "name")))) return false;
                    AddLocators(selected, ["modules"], "module", locators);
                    fragments.Add((prefix, selected));
                }
            }

            // Resolve each alias from an immutable base row. A GUID/name locator
            // and an ID-only amendment must not apply an APPEND twice. Do not use
            // purchase target enumeration here: the full catalog retains disabled
            // books for the native module parser's own eligibility checks.
            var resolved = new Dictionary<Guid, XElement>();
            foreach (TargetLocator locator in locators.Distinct())
            {
                XElement? original = FindTarget(root, ["modules"], "module", locator);
                XElement? target = original is null ? null : new XElement(original);
                Guid? identity = original is null ? locator.SourceId : Guid.Parse(ReadValue(original, "id"));
                bool ExactIdentity(XElement? candidate)
                {
                    if (candidate is null) return true;
                    if (!TryModuleId2852(candidate, out Guid candidateId)
                        || identity is { } bound && bound != candidateId) return false;
                    identity ??= candidateId;
                    return true;
                }
                foreach (var fragment in fragments)
                    if (!TryApplyCustomFile(fragment.Root, fragment.Prefix, ["modules"], "module", locator,
                        ref target, validateTarget: ExactIdentity, currentSemantics: true)) return false;
                if (target is null) continue;
                if (!TryModuleId2852(target, out Guid id)
                    || original is not null && ReadValue(original, "id") != ReadValue(target, "id")
                    || locator.SourceId is { } selectedId && selectedId != id
                    || resolved.TryGetValue(id, out var previous) && !XNode.DeepEquals(previous, target)) return false;
                resolved[id] = target;
            }

            // Keep existing module order and untouched XML nodes. Append genuinely
            // new rows deterministically; never reorder stock rows to obtain a digest.
            foreach (XElement original in baseRows)
            {
                if (!TryModuleId2852(original, out Guid id)) return false;
                if (!resolved.Remove(id, out var effective)) original.Remove();
                else if (!XNode.DeepEquals(original, effective)) original.ReplaceWith(effective);
            }
            foreach (var row in resolved.OrderBy(pair => pair.Key)) modules.Add(row.Value);
            return !_sourceInputs.HasSourceDrift;
        }

        private static bool TryModuleId2852(XElement row, out Guid id)
        {
            id = Guid.Empty;
            return row.Elements("id").ToArray() is [var field] && !field.HasAttributes && !field.HasElements
                && Guid.TryParseExact(field.Value.Trim(), "D", out id) && id != Guid.Empty;
        }
    }
}
