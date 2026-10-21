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
    {
        if (_portable) return this;
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            ValidatePath(path);
            path = Path.TrimEndingDirectorySeparator(path);
            if (path == Path.GetPathRoot(path)) throw Rejected();
            candidates.Add(path);
        }
        Add(Path.GetDirectoryName(_catalog.BaseDataPath));
        if (!string.IsNullOrEmpty(_catalog.BaseLanguagePath)) Add(Path.GetDirectoryName(_catalog.BaseLanguagePath));
        foreach (var pack in _catalog.Overlays)
        {
            Add(pack.RootPath);
            if (!string.IsNullOrEmpty(pack.DataPath)) Add(Path.GetDirectoryName(pack.DataPath));
            if (!string.IsNullOrEmpty(pack.LanguagePath)) Add(Path.GetDirectoryName(pack.LanguagePath));
        }
        // Keep shared-root relationships and physical discovery order. Assign opaque
        // slots only after eliminating descendants; never serialize the mapping.
        string[] roots = candidates.Where(path => !candidates.Any(parent =>
            parent != path && Within(parent, path))).Order(StringComparer.Ordinal).ToArray();
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
            portable: true, sourcePathSeparator: SourcePathSeparator);
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
        if (PathLiteral(value) || PathLiteral(System.Net.WebUtility.HtmlDecode(value))) throw Rejected();
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

    // Retained files and digest-bound nodes reached through actual decoded source
    // contracts may use the rule schema. Character XML and prose stay strict.
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
        string? sourceContainer = null, string? sourceElement = null)
    {
        var ancestry = sourceContainer is null ? new List<string>() : new List<string> { "chummer", sourceContainer };
        int ancestryOffset = ancestry.Count;
        bool sourceRootSeen = false;
        var plainText = new System.Text.StringBuilder();
        void FlushText()
        {
            if (plainText.Length == 0) return;
            ValidatePortableTransferText(plainText.ToString());
            plainText.Clear();
        }
        System.Text.StringBuilder? schemaText = null;
        bool ammunitionCapacity = false;
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
                bool selector = ruleSource && unqualified && reader.LocalName == "selecttext"
                    && IsPortableRuleBonusPath(string.Join("/", ancestry));
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
                        plainText.Append(reader.Value);
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
                        else ValidatePortableRuleExpression(schemaText.ToString(), allowConditionProse: true);
                        schemaText = null;
                        ammunitionCapacity = false;
                    }
                    while (ancestry.Count > reader.Depth + ancestryOffset) ancestry.RemoveAt(ancestry.Count - 1);
                }
            }
        }
        FlushText();
        if (sourceElement is not null && !sourceRootSeen) throw Rejected();
    }

    private void ValidatePortablePrivacy()
    {
        static void Text(string value) => ValidatePortableTransferText(value);
        using (var text = new StringReader(CharacterXml))
        using (var xml = System.Xml.XmlReader.Create(text, XmlSettings())) ValidatePortableXmlValues(xml);
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
