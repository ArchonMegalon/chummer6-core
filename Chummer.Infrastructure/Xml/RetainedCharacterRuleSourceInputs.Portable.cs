using System.Globalization;
using Chummer.Application.Content;

namespace Chummer.Infrastructure.Xml;

internal sealed partial class RetainedCharacterRuleSourceInputs
{
    internal const string PortableFormat = Chummer.Contracts.Workspaces.WorkspacePrivateRuleSourceClosureBinding.RuleInputsV3;
    private readonly bool _portable;
    internal bool IsPortable => _portable;
    internal char SourcePathSeparator { get; }

    // This recipe is serialized inside the archive's integrity digest. The receiver
    // recomputes policy and the entire observation closure using it; a changed recipe
    // cannot borrow the sender's policy or archive identity.
    internal string PathOrderKey(string path) =>
        path.Replace(Path.DirectorySeparatorChar, SourcePathSeparator);

    internal bool TryCreatePortable(out RetainedCharacterRuleSourceInputs? portable)
    {
        try { portable = ToPortable(); return true; }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException
            or System.Xml.XmlException)
        {
            // A source-copy boundary must not narrow lawful local capture. The
            // original, bounded V1 inputs remain exact and locally recoverable.
            // Private transfer still refuses V1; no sanitization or migration occurs.
            portable = null;
            return false;
        }
    }

    // These are resolver-only addresses, never opened on disk. Every retained read
    // is serviced by the sealed dictionaries; unobserved reads throw.
    private static string VirtualRoot => OperatingSystem.IsWindows()
        ? @"C:\__chummer_rule_closure__" : "/__chummer_rule_closure__";

    internal RetainedCharacterRuleSourceInputs ToPortable()
        => ToPortable(logicalRootOrder: false);

    private RetainedCharacterRuleSourceInputs ToPortable(bool logicalRootOrder)
    {
        if (_portable && !logicalRootOrder) return this;
        var candidates = new List<string>();
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            ValidatePath(path);
            path = Path.TrimEndingDirectorySeparator(path);
            if (path == Path.GetPathRoot(path)) throw Rejected();
            if (!candidates.Contains(path, StringComparer.Ordinal)) candidates.Add(path);
        }
        Add(Path.GetDirectoryName(_catalog.BaseDataPath));
        if (!string.IsNullOrEmpty(_catalog.BaseLanguagePath)) Add(Path.GetDirectoryName(_catalog.BaseLanguagePath));
        foreach (var pack in _catalog.Overlays)
        {
            Add(pack.RootPath);
            if (!string.IsNullOrEmpty(pack.DataPath)) Add(Path.GetDirectoryName(pack.DataPath));
            if (!string.IsNullOrEmpty(pack.LanguagePath)) Add(Path.GetDirectoryName(pack.LanguagePath));
        }
        // Keep shared-root relationships. Historical archives retain their physical
        // root ordering; the new profile assigns each surviving root its first catalog
        // role (base data, base language, then overlays in their retained order).
        // An ancestor inherits its earliest descendant role, even if added later.
        var independentRoots = candidates.Where(path => !candidates.Any(parent =>
            parent != path && Within(parent, path)));
        string[] roots = (logicalRootOrder
            ? independentRoots.OrderBy(root => candidates.FindIndex(path => Within(root, path)))
            : independentRoots.Order(StringComparer.Ordinal)).ToArray();
        if (roots.Length is 0 or > MaximumFiles) throw Rejected();
        string Map(string path)
        {
            if (path.Length == 0) return path;
            int slot = Array.FindIndex(roots, root => Within(root, path));
            if (slot < 0) throw Rejected();
            string relative = Path.GetRelativePath(roots[slot], path).Replace('\\', '/');
            string address = "r" + slot.ToString("D6", CultureInfo.InvariantCulture);
            if (relative != ".") address += "/" + relative;
            return FromAddress(address);
        }
        // Preserve source bytes exactly. Sources or character text containing a
        // configured sender root are not transferable; never redact/rewrite them
        // while retaining their original identity.
        foreach (string root in roots)
        {
            string[] spellings = [root, root.Replace('\\', '/'), root.Replace("\\", "\\\\"),
                System.Security.SecurityElement.Escape(root)!];
            foreach (string spelling in spellings.Distinct(StringComparer.Ordinal))
            {
                byte[] needle = Utf8.GetBytes(spelling);
                if (CharacterXml.Contains(spelling, StringComparison.Ordinal)
                    || _files.Values.Any(bytes => bytes.AsSpan().IndexOf(needle) >= 0)
                    || new[] { ProfileId, PolicyDigest }.Any(value => value.Contains(spelling, StringComparison.Ordinal))
                    || _selection.Any(s => s.Name.Contains(spelling, StringComparison.Ordinal)
                        || s.ManifestId.Contains(spelling, StringComparison.Ordinal) || s.Version.Contains(spelling, StringComparison.Ordinal))
                    || _catalog.Overlays.Any(p => new[] { p.Id, p.Name, p.Mode, p.Description }
                        .Any(value => value.Contains(spelling, StringComparison.Ordinal))))
                    throw Rejected();
            }
        }
        if (_selection.Select(item => item.Path).Distinct(StringComparer.Ordinal).Count() != _selection.Length)
            throw Rejected();
        var catalog = new ContentOverlayCatalog(Map(_catalog.BaseDataPath), Map(_catalog.BaseLanguagePath),
            _catalog.Overlays.Select(pack => pack with
            { RootPath = Map(pack.RootPath), DataPath = Map(pack.DataPath), LanguagePath = Map(pack.LanguagePath) }).ToArray());
        return new(catalog, CharacterXml, PolicyDigest, ProfileId,
            _selection.Select(item => item with { Path = Map(item.Path) }),
            _files.ToDictionary(row => Map(row.Key), row => row.Value, StringComparer.Ordinal),
            _existence.ToDictionary(row => Map(row.Key), row => row.Value, StringComparer.Ordinal),
            _inventories.Values.Select(item => new Inventory(item.Key with { Directory = Map(item.Key.Directory) },
                item.Exists, item.Entries.Select(Map).OrderBy(PathOrderKey, StringComparer.Ordinal).ToArray())),
            portable: true, sourcePathSeparator: SourcePathSeparator, dependencyRecipe1244: DependencyRecipe1244,
            nativeFocusGearCatalog1339: NativeFocusGearCatalog1339, compatibilityProfile: CompatibilityProfile);
    }

    private static bool Within(string root, string path)
        => path == root || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private void WriteArchivePath(BinaryWriter writer, string path)
        => WriteText(writer, _portable ? ToAddress(path) : path);

    private static string ToAddress(string path)
    {
        if (path.Length == 0) return "";
        if (!Within(VirtualRoot, path) || path == VirtualRoot) throw Rejected();
        string address = path[(VirtualRoot.Length + 1)..].Replace('\\', '/');
        if (FromAddress(address) != path) throw Rejected();
        return address;
    }

    private static string FromAddress(string address)
    {
        if (address.Length == 0) return "";
        ValidateText(address);
        string[] parts = address.Split('/');
        if (parts[0].Length != 7 || parts[0][0] != 'r'
            || parts[0].AsSpan(1).IndexOfAnyExcept("0123456789") >= 0
            || int.Parse(parts[0].AsSpan(1), CultureInfo.InvariantCulture) >= MaximumFiles
            || parts.Any(part => part.Length == 0 || part is "." or ".."
                || part.EndsWith(' ') || part.EndsWith('.')
                || part.Any(c => char.IsControl(c) || c is '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')))
            throw Rejected();
        return Path.Combine(VirtualRoot, Path.Combine(parts));
    }

    internal static void ValidatePortableTransferText(string value)
    {
        static bool PathLiteral(string value)
        {
            // Every rejected drive, UNC, file URI or Unix path contains at
            // least one of these characters. Skip only this literal scan;
            // the caller still decodes entities and checks the decoded value.
            if (value.Length == 0 || value.AsSpan().IndexOfAny(':', '/', '\\') < 0) return false;
            for (int i = 0; i < value.Length; i++)
            {
                if (i + 2 < value.Length && (i == 0 || !char.IsLetterOrDigit(value[i - 1]))
                    && char.IsAsciiLetter(value[i]) && value[i + 1] == ':'
                    && value[i + 2] is '/' or '\\') return true;
                if (i + 1 < value.Length && value[i] == '\\' && value[i + 1] == '\\') return true;
                if ((i == 0 || !char.IsLetterOrDigit(value[i - 1]))
                    && value.AsSpan(i).StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return true;
                // Absolute Unix literals at text/token boundaries. URLs and ordinary
                // relative logical names do not begin at such a boundary.
                if (value[i] == '/' && (i + 1 == value.Length || !char.IsWhiteSpace(value[i + 1]))
                    && (i == 0 || char.IsWhiteSpace(value[i - 1])
                        || value[i - 1] is '"' or '\'' or '(' or ')' or '[' or ']' or '|'
                            or '=' or '!' or '<' or '>' or ',' or '+' or '-' or '*')) return true;
            }
            return false;
        }
        if (PathLiteral(value)) throw Rejected();
        string decoded = System.Net.WebUtility.HtmlDecode(value);
        // Equal decoded text has already passed this pure scan in this call.
        // Encoded values still receive the same second check, without retaining state.
        if (decoded != value && PathLiteral(decoded)) throw Rejected();
    }

    // Continuations also carry XML as JSON strings. Inspect decoded XML values,
    // not markup such as the slash in an ordinary <expenses /> element.
    internal static void ValidatePortableTransferDocumentText(string value)
    {
        if (!value.AsSpan().TrimStart().StartsWith("<", StringComparison.Ordinal))
        {
            ValidatePortableTransferText(value);
            return;
        }
        try
        {
            ValidateCharacter(value);
            using var text = new StringReader(value);
            using var reader = System.Xml.XmlReader.Create(text, XmlSettings());
            ValidatePortableXmlValues(reader);
        }
        catch (System.Xml.XmlException) when (!value.Contains("<!DOCTYPE", StringComparison.Ordinal))
        {
            // Ordinary JSON prose can also begin with '<' (for example, "<3").
            // It gains no XML interpretation; the original literal gate still applies.
            ValidatePortableTransferText(value);
        }
    }

    // A saved Quality retains its original answered selecttext bonus. Its XPath
    // is document syntax, not a filesystem address. This scope is reached only
    // from retained CharacterXml or a decoded SR5 workspace payload; ordinary
    // strings, comments and embedded XML remain on the strict text path.
    internal static void ValidatePortableCharacterDocumentXml(string value)
    {
        ValidateCharacter(value);
        using var text = new StringReader(value);
        using var reader = System.Xml.XmlReader.Create(text, XmlSettings());
        ValidatePortableXmlValues(reader, sourceElement: "character", characterDocument: true);
    }

    // Retained files and digest-bound nodes reached through actual decoded source
    // contracts may use the rule schema. Prose stays strict.
    internal static void ValidatePortableRuleSourceXml(System.Xml.XmlReader reader)
        => ValidatePortableXmlValues(reader, ruleSource: true);

    internal static void ValidatePortableRuleSourceNodeXml(string value, string container, string element)
    {
        ValidateCharacter(value);
        using var text = new StringReader(value);
        using var reader = System.Xml.XmlReader.Create(text, XmlSettings());
        // Virtual ancestry is supplied by the typed contract, never the XML itself.
        // It changes classification only; the source string and archive stay exact.
        ValidatePortableXmlValues(reader, ruleSource: true, sourceContainer: container, sourceElement: element);
    }

    private static void ValidatePortableXmlComment(string value)
    {
        if (value.Contains('<'))
        {
            try
            {
                using var text = new StringReader(value);
                var settings = XmlSettings();
                settings.ConformanceLevel = System.Xml.ConformanceLevel.Fragment;
                using var reader = System.Xml.XmlReader.Create(text, settings);
                // Shipped comments contain disabled XML snippets and closing tags.
                // Parse markup only; even source-looking snippets get the strict gate.
                ValidatePortableXmlValues(reader);
                return;
            }
            catch (System.Xml.XmlException) when (!value.Contains("<!DOCTYPE", StringComparison.Ordinal)) { }
        }
        ValidatePortableTransferText(value);
    }

    private static void ValidatePortableXmlValues(System.Xml.XmlReader reader, bool ruleSource = false,
        string? sourceContainer = null, string? sourceElement = null, bool characterDocument = false)
    {
        var ancestry = sourceContainer is null ? new List<string>() : new List<string> { "chummer", sourceContainer };
        int ancestryOffset = ancestry.Count;
        bool sourceRootSeen = false;
        string? plainText = null;
        System.Text.StringBuilder? joinedPlainText = null;
        void AppendPlainText(string value)
        {
            if (value.Length == 0) return;
            if (plainText is null) plainText = value;
            else (joinedPlainText ??= new System.Text.StringBuilder(plainText)).Append(value);
        }
        void FlushText()
        {
            if (plainText is null) return;
            // Keep adjacent XML text/CDATA in one token context, while a single
            // reader value can be checked without copying it through a builder.
            ValidatePortableTransferText(joinedPlainText?.ToString() ?? plainText);
            plainText = null;
            joinedPlainText = null;
        }
        System.Text.StringBuilder? schemaText = null;
        bool ammunitionCapacity = false;
        bool savedFocusCondition = false;
        int schemaTextDepth = -1;
        while (reader.Read())
        {
            if (reader.NodeType == System.Xml.XmlNodeType.Element)
            {
                FlushText();
                if (schemaText is not null || reader.Depth > MaximumXmlDepth) throw Rejected();
                if (sourceElement is not null && reader.Depth == 0)
                {
                    if (sourceRootSeen || reader.NamespaceURI.Length != 0 || reader.LocalName != sourceElement)
                        throw Rejected();
                    sourceRootSeen = true;
                }
                while (ancestry.Count > reader.Depth + ancestryOffset) ancestry.RemoveAt(ancestry.Count - 1);
                bool unqualified = reader.NamespaceURI.Length == 0;
                bool selector = unqualified && reader.LocalName == "selecttext"
                    && (ruleSource && IsPortableRuleBonusPath(string.Join("/", ancestry))
                        || characterDocument && string.Join("/", ancestry) == "character/qualities/quality/bonus");
                if (characterDocument && unqualified && reader.LocalName == "condition" && !reader.IsEmptyElement
                    && string.Join("/", ancestry) is "character/gears/gear/bonus/spellcategory"
                        or "character/improvements/improvement")
                {
                    schemaText = new();
                    schemaTextDepth = reader.Depth;
                    savedFocusCondition = true;
                }
                if (ruleSource && unqualified && reader.LocalName == "condition" && !reader.IsEmptyElement
                    && ancestry.Count > 0 && IsPortableRuleCondition(ancestry[^1])
                    && IsPortableRuleBonusPath(string.Join("/", ancestry.Take(ancestry.Count - 1))))
                {
                    schemaText = new();
                    schemaTextDepth = reader.Depth;
                }
                if (ruleSource && unqualified && reader.LocalName == "ammo" && !reader.IsEmptyElement
                    && string.Join("/", ancestry) == "chummer/weapons/weapon")
                {
                    schemaText = new();
                    schemaTextDepth = reader.Depth;
                    ammunitionCapacity = true;
                }
                ancestry.Add(unqualified ? reader.LocalName : "");
                if (!reader.HasAttributes) continue;
                while (reader.MoveToNextAttribute())
                {
                    if (selector && reader.NamespaceURI.Length == 0 && reader.LocalName == "xpath")
                        ValidatePortableRuleExpression(reader.Value);
                    else ValidatePortableTransferText(reader.Value);
                }
                reader.MoveToElement();
            }
            else
            {
                if (reader.HasValue)
                {
                    // XML text/CDATA fragments share one XPath quote/token context.
                    if (schemaText is not null && reader.NodeType is System.Xml.XmlNodeType.Text
                        or System.Xml.XmlNodeType.CDATA or System.Xml.XmlNodeType.Whitespace
                        or System.Xml.XmlNodeType.SignificantWhitespace)
                        schemaText.Append(reader.Value);
                    else if (reader.NodeType is System.Xml.XmlNodeType.Text or System.Xml.XmlNodeType.CDATA
                        or System.Xml.XmlNodeType.Whitespace or System.Xml.XmlNodeType.SignificantWhitespace)
                        AppendPlainText(reader.Value);
                    else if (reader.NodeType == System.Xml.XmlNodeType.Comment)
                        ValidatePortableXmlComment(reader.Value);
                    else ValidatePortableTransferText(reader.Value);
                }
                if (reader.NodeType == System.Xml.XmlNodeType.EndElement)
                {
                    FlushText();
                    if (schemaText is not null && reader.Depth == schemaTextDepth)
                    {
                        if (ammunitionCapacity) ValidatePortableRuleAmmunitionCapacity(schemaText.ToString());
                        else if (savedFocusCondition) ValidatePortableSavedFocusCondition7520(schemaText.ToString());
                        else ValidatePortableRuleExpression(schemaText.ToString(), allowConditionProse: true);
                        schemaText = null;
                        ammunitionCapacity = false;
                        savedFocusCondition = false;
                    }
                    while (ancestry.Count > reader.Depth + ancestryOffset) ancestry.RemoveAt(ancestry.Count - 1);
                }
            }
        }
        FlushText();
        if (sourceElement is not null && !sourceRootSeen) throw Rejected();
    }

    private static void ValidatePortableSavedFocusCondition7520(string value)
    {
        // The exact ordinary-spell condition retained by the catalog bonus and
        // emitted by CharacterCareerFocusBondingTransaction.BuiltinEffects.
        // This is privacy classification, not XPath evaluation or source admission.
        // All other text keeps the existing path guard. Do not admit general
        // rule expressions in saved characters, notes, comments or unknown XML.
        if (value != "not(/spell/alchemical)") ValidatePortableTransferText(value);
    }

    private void ValidatePortablePrivacy()
    {
        static void Text(string value) => ValidatePortableTransferText(value);
        ValidatePortableCharacterDocumentXml(CharacterXml);
        foreach (var (path, bytes) in _files)
        {
            if (string.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase))
            {
                // XmlReader understands UTF-8/UTF-16/BOMs; an encoded absolute path
                // cannot bypass the gate while exact original bytes are retained.
                using var stream = new MemoryStream(bytes, writable: false);
                using var xml = System.Xml.XmlReader.Create(stream, XmlSettings());
                ValidatePortableRuleSourceXml(xml);
            }
            else Text(Utf8.GetString(bytes)); // Unsupported non-text opaque inputs fail closed.
        }
        Text(ProfileId); Text(PolicyDigest);
        foreach (var pack in _catalog.Overlays) { Text(pack.Id); Text(pack.Name); Text(pack.Mode); Text(pack.Description); }
        foreach (var item in _selection) { Text(item.Name); Text(item.ManifestId); Text(item.Version); }
        foreach (var inventory in _inventories.Values) Text(inventory.Key.Pattern);
    }
}
