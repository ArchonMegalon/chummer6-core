using System.Xml.Linq;
using Chummer.Application.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    // Root must compose this partial with the Characters packet. Reuses the same
    // operation-scoped source inputs, enabled books and override/amend resolver.
    private sealed partial class SourceDataContext : ICharacterSheetOwnerSourceContext
    {
        public bool TryResolveSheetOwnerSource(string kind, Guid sourceId, out CharacterSheetOwnerSource? source)
        {
            using IDisposable scope = _sourceInputs.Enter();
            source = null;
            return TryResolveSheetOwnerRow7573(kind, sourceId, out var row, out var name, out var book)
                && SheetReceipt(kind, sourceId, name, book, SheetLocation(kind).File, row!, out source);
        }

        // Source admission is an input to anatomy/skills/policy. Only the sheet
        // receipt adds the downstream effective-policy digest to these same inputs.
        private bool TryResolveSheetOwnerRow7573(string kind, Guid sourceId,
            out XElement? row, out string name, out string book)
        {
            row = null;
            name = book = string.Empty;
            var location = SheetLocation(kind);
            if (location.File.Length == 0 || sourceId == Guid.Empty || _sourceInputs.HasSourceDrift
                || !TryResolveTarget(location.File, [location.Container], location.Entry,
                    sourceId.ToString("D"), "", out row, out bool ambiguous, inspectIdentity: true,
                    sheetMetatypeSource: kind == "Metatype")
                || row is null || ambiguous
                || !SheetOwnerIdentity(kind, row, location.Entry, sourceId, out name, out book)
                || !IsEnabledSource(book)) return false;
            return !_sourceInputs.HasSourceDrift;
        }

        private static bool SheetOwnerIdentity(string kind, XElement row, string element, Guid id,
            out string name, out string book)
        {
            if (kind != "Gear") return CareerPowerIdentity(row, element, id, out name, out book);
            name = book = "";
            // gear.xsd permits repeated addoncategory scalars, also consumed by
            // CharacterSheetOwnerGraph.ValidateSourceShape. Keep the complete row
            // in the receipt; this is no permission to generate or attach a child.
            if (row.Name != element || row.HasAttributes || id == Guid.Empty
                || row.Elements("addoncategory").Any(e => e.HasAttributes || e.HasElements)
                || row.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1 && g.Key != "addoncategory"))
                return false;
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

        private bool SheetMetatypeSourceAvailable(XElement row)
        {
            if (UsesSpriteCatalog5751)
                return CharacterSpriteSource5751.SupportsSource(row)
                    && TryResolveCareerSettingsProfile(out _, out var state) && !string.IsNullOrWhiteSpace(state);
            // An existing owner's source is not a permission to create that owner.
            // Keep live profile/catalog admission and the same strict mechanical
            // shape parser. The sheet compiler separately checks every retained
            // grant, generated parent, suppression link and saved effect.
            if (!TryResolveCreationMetatypeCatalog(out var catalog) || !catalog.IsAuthoritative
                || catalog.Blockers.Count != 0 || !catalog.SourceContext.IsAuthoritative
                || catalog.SourceContext.Blockers.Count != 0
                || catalog.SourceContext.MetatypeKarmaMultiplier is not int multiplier
                || catalog.SourceContext.MinimumInitiativeDiceFallback is not int dice) return false;
            return CharacterCreationMetatypeCatalogProjector.TryProjectCurrentOption(row,
                    multiplier, dice, catalog.SourceContext.EnabledSourcebooks, out var option,
                    _sourceInputs.CreationSourceExecutionSchema, careerCondition5497: UsesCareerCondition5497,
                    importedSprite5732: UsesImportedCareerOwnerSemantics5698)
                && option.IsEnabled && option.Blockers.Count == 0;
        }

        public bool TryResolveSheetNamedSource(string kind, string identity, out CharacterSheetOwnerSource? source)
        {
            using IDisposable scope = _sourceInputs.Enter();
            source = null;
            return TryResolveSheetNamedSourceId7573(kind, identity, out var sourceId)
                && TryResolveSheetOwnerSource(kind, sourceId, out source);
        }

        private bool TryResolveSheetNamedSourceId7573(string kind, string identity, out Guid sourceId)
        {
            sourceId = Guid.Empty;
            var location = SheetLocation(kind);
            if (location.File.Length == 0 || string.IsNullOrWhiteSpace(identity) || _sourceInputs.HasSourceDrift
                || !TryEnumerateTargets(location.File, [location.Container], location.Entry, out var rows)) return false;
            bool byId = Guid.TryParse(identity, out var requested);
            var matches = rows.Where(r => byId
                ? Guid.TryParse(r.Element("id")?.Value, out var id) && id == requested
                : r.Element("name")?.Value == identity).ToArray();
            // Callers must re-enter the identity and enabled-book checked row resolver.
            return matches is [var row] && Guid.TryParse(row.Element("id")?.Value, out sourceId)
                && !_sourceInputs.HasSourceDrift;
        }

        public bool TryResolveSheetGrantSource(string kind, string identity, out CharacterSheetOwnerSource? source)
        {
            using IDisposable scope = _sourceInputs.Enter();
            source = null;
            if (kind is not ("Quality" or "CritterPower")) return false;
            if (Guid.TryParse(identity, out var id)) return TryResolveSheetOwnerSource(kind, id, out source);
            var location = SheetLocation(kind);
            if (_sourceInputs.HasSourceDrift) return false;
            XElement[] rows;
            bool inventory = _readingSourcebooks800;
            try
            {
                // Inspect identity before book filtering, then re-enter the
                // normal enabled-source resolver below. Never admit this inventory.
                _readingSourcebooks800 = true;
                if (!TryEnumerateTargets(location.File, [location.Container], location.Entry, out rows)) return false;
            }
            finally { _readingSourcebooks800 = inventory; }
            // Character.Create -> TryGetNodeByNameOrId uses document order. Never
            // skip a disabled first match to obtain a different enabled power.
            var matches = rows.Where(r => r.Element("name")?.Value == identity).ToArray();
            // Legacy custom enumeration currently sorts identities. It cannot
            // establish document-order precedence for a colliding grant name.
            if (matches.Length > 1 && _customDirectories.Count > 0)
            {
                // Sprite grants still use native first-in-document name lookup.
                // Restore base order only when every colliding identity survives
                // unchanged; new/renamed/removed collisions remain ambiguous.
                if (!UsesSpriteCatalog5751 || !TryLoadCurrentCatalogBaseDocument(location.File, out var original)
                    || original?.Root is null) return false;
                var ordered = original.Root.Elements(location.Container).Elements(location.Entry)
                    .Where(r => r.Element("name")?.Value == identity).ToArray();
                var originalIds = ordered.Select(r => r.Element("id")?.Value).ToArray();
                var effectiveIds = matches.Select(r => r.Element("id")?.Value).ToArray();
                if (originalIds.Any(string.IsNullOrWhiteSpace) || originalIds.Distinct().Count() != originalIds.Length
                    || !originalIds.Order(StringComparer.Ordinal).SequenceEqual(effectiveIds.Order(StringComparer.Ordinal))) return false;
                return Guid.TryParse(originalIds[0], out var first)
                    && TryResolveSheetOwnerSource(kind, first, out source);
            }
            var row = matches.FirstOrDefault();
            return row is not null && Guid.TryParse(row.Element("id")?.Value, out id)
                && TryResolveSheetOwnerSource(kind, id, out source);
        }

        public bool TryResolveSheetMetavariant(Guid parentId, string identity, out CharacterSheetOwnerSource? source)
        {
            using IDisposable scope = _sourceInputs.Enter();
            source = null;
            if (!TryResolveSheetOwnerSource("Metatype", parentId, out var parent) || parent is null
                || string.IsNullOrWhiteSpace(identity)) return false;
            var xml = XElement.Parse(parent.SourceXml);
            var containers = xml.Elements().Where(e => e.Name.LocalName == "metavariants").ToArray();
            if (containers is not [var container] || container.Name != "metavariants" || container.HasAttributes
                || container.Elements().Any(e => e.Name != "metavariant")) return false;
            bool byId = Guid.TryParse(identity, out var requested);
            var matches = container.Elements().Where(r => byId
                ? Guid.TryParse(r.Element("id")?.Value, out var id) && id == requested
                : r.Element("name")?.Value == identity).ToArray();
            if (matches is not [var row] || !Guid.TryParse(row.Element("id")?.Value, out var variantId)
                || !CareerPowerIdentity(row, "metavariant", variantId, out var name, out var book)
                || !IsEnabledSource(book)) return false;
            // Both the parent receipt and this receipt retain the whole effective
            // input digest. The query binds the pair, including parent lineage.
            return SheetReceipt("Metavariant", variantId, name, book, "metatypes.xml", row, out source);
        }

        public bool TryResolveSheetWareGrade(string kind, string name, out CharacterSheetOwnerSource? source)
        {
            using IDisposable scope = _sourceInputs.Enter();
            source = null;
            if (kind is not ("Cyberware" or "Bioware") || _sourceInputs.HasSourceDrift) return false;
            string file = SheetLocation(kind).File;
            if (!TryEnumerateTargets(file, ["grades"], "grade", out var rows)) return false;
            var matches = rows.Where(r => r.Element("name")?.Value == name).ToArray();
            if (matches is not [var row] || row.HasAttributes
                || row.Elements().Any(e => e.HasAttributes || e.HasElements || e.Name.Namespace != XNamespace.None)
                || row.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)
                || !Guid.TryParse(row.Element("id")?.Value, out var id) || id == Guid.Empty) return false;
            string book = row.Element("source")?.Value ?? "";
            if (book.Length > 0 && !IsEnabledSource(book)) return false;
            return SheetReceipt(kind + "Grade", id, name, book, file, row, out source);
        }

        public bool TryResolveSheetSpellCategory(string category, out CharacterSheetOwnerSource? source)
        {
            using IDisposable scope = _sourceInputs.Enter();
            source = null;
            if (string.IsNullOrWhiteSpace(category) || _sourceInputs.HasSourceDrift
                || !TryLoadCurrentCatalogBaseDocument("spells.xml", out var document)
                || document?.Root?.Elements("categories").ToArray() is not [var categories]) return false;
            // Item-only custom/override/amend inputs cannot change the category.
            // Category-level amendments have no supported id/name locator here;
            // refuse them instead of silently reading an unamended base category.
            foreach (var directory in _customDirectories)
            foreach (string path in EnumerateSourceFiles(directory.Path, "*_spells.xml", SearchOption.AllDirectories)
                .Where(path => IsLegacyCustomDataInputFor(path, "spells.xml")))
            {
                string phase = Path.GetFileName(path).StartsWith("amend_", StringComparison.OrdinalIgnoreCase) ? "amend_"
                    : Path.GetFileName(path).StartsWith("override_", StringComparison.OrdinalIgnoreCase) ? "override_" : "custom_";
                if (!TryLoadXml(path, out var fragment) || fragment?.Root is not { } root
                    || !AdmitCurrentFragment(root, phase, [("spells", "spell")], spriteCatalog5751: false)) return false;
            }
            var matches = categories.Elements("category").Where(c => c.Value == category).ToArray();
            if (categories.HasAttributes || matches is not [var row] || row.HasElements
                || row.Attributes().Any(a => a.Name.LocalName is not ("useskill" or "translate" or "alchemicalskill" or "barehandedadeptskill")
                    || a.Name.Namespace != XNamespace.None)) return false;
            return SheetReceipt("SpellCategory", Guid.Empty, category, "", "spells.xml", row, out source);
        }

        private bool SheetReceipt(string kind, Guid id, string name, string book, string file,
            XElement row, out CharacterSheetOwnerSource? source)
        {
            source = null;
            if (!TryComputeEffectiveInputDigest(_catalog, file, out var digest)
                || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, file, out var custom)
                || !TryResolveCreationEffectivePolicy(out var policy) || policy is null
                || _sourceInputs.HasSourceDrift) return false;
            source = new(kind, id, name, book, row.ToString(SaveOptions.DisableFormatting),
                digest, custom, _rawProfileInputsDigest, policy.PolicyDigest);
            return true;
        }

        private (string File, string Container, string Entry) SheetLocation(string kind) => kind switch
        {
            "Metatype" => (UsesSpriteCatalog5751 ? "critters.xml" : "metatypes.xml", "metatypes", "metatype"),
            "Skill" => ("skills.xml", "skills", "skill"),
            "CritterPower" => ("critterpowers.xml", "powers", "power"),
            "Quality" => ("qualities.xml", "qualities", "quality"),
            "Power" => ("powers.xml", "powers", "power"),
            "Cyberware" => ("cyberware.xml", "cyberwares", "cyberware"),
            "Bioware" => ("bioware.xml", "biowares", "bioware"),
            "Gear" => ("gear.xml", "gears", "gear"),
            "WeaponAccessory" => ("weapons.xml", "accessories", "accessory"),
            "Weapon" => ("weapons.xml", "weapons", "weapon"),
            "Armor" => ("armor.xml", "armors", "armor"),
            "Spell" => ("spells.xml", "spells", "spell"),
            "ComplexForm" => ("complexforms.xml", "complexforms", "complexform"),
            "Vehicle" => ("vehicles.xml", "vehicles", "vehicle"),
            "Lifestyle" => ("lifestyles.xml", "lifestyles", "lifestyle"),
            "LifestyleQuality" => ("lifestyles.xml", "qualities", "quality"),
            _ => ("", "", "")
        };
    }
}
