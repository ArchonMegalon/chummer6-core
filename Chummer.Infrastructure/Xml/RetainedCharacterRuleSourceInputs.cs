using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Content;
using Chummer.Application.Characters;
using Chummer.Contracts.Content;

namespace Chummer.Infrastructure.Xml;

// Private archive format, deliberately outside the engine's public receipt/contracts plane.
// Only source I/O is replayed. No merged trees, consumer results or authorization are retained.
internal sealed partial class RetainedCharacterRuleSourceInputs
{
    internal const int MaximumFiles = 4096;
    internal const int MaximumFileBytes = 16 * 1024 * 1024;
    internal const long MaximumTotalBytes = 128L * 1024 * 1024;
    internal const int MaximumArchiveBytes = 160 * 1024 * 1024;
    internal const int MaximumObservations = 32768;
    internal const int MaximumTextBytes = 64 * 1024;
    internal const long MaximumXmlCharacters = 16L * 1024 * 1024;
    internal const int MaximumXmlDepth = 128;
    internal const int MaximumPreparedXmlFiles3275 = 32;
    internal const int MaximumPreparedXmlBytes3275 = 4 * 1024 * 1024;
    private const string Format = "chummer-private-rule-inputs-v1";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal readonly record struct InventoryKey(string Directory, string Pattern, SearchOption Search, bool Directories);
    internal sealed record Inventory(InventoryKey Key, bool Exists, string[] Entries);
    internal sealed record Selection(string Name, string Path, string ManifestId, string Version);

    private readonly ContentOverlayCatalog _catalog;
    private readonly Selection[] _selection;
    private readonly Dictionary<string, byte[]> _files;
    private readonly Dictionary<string, bool> _existence;
    private readonly Dictionary<InventoryKey, Inventory> _inventories;
    private readonly PreparedXmlScope4505 _preparedXml3275;
    internal string CharacterXml { get; }
    internal string PolicyDigest { get; }
    internal string ProfileId { get; }
    // Private, digest-bound projection identity, not execution or save authority.
    // Historical formats omit it and retain their exact catalog and serialization.
    internal bool NativeFocusGearCatalog1339 { get; }

    internal RetainedCharacterRuleSourceInputs(ContentOverlayCatalog catalog, string characterXml,
        string policyDigest, string profileId, IEnumerable<Selection> selection,
        IReadOnlyDictionary<string, byte[]> files, IReadOnlyDictionary<string, bool> existence,
        IEnumerable<Inventory> inventories, bool portable = false, char? sourcePathSeparator = null,
        string dependencyRecipe1244 = LegacyDependencyRecipe1244,
        bool nativeFocusGearCatalog1339 = false, string? compatibilityProfile = null,
        PreparedXmlScope4505? preparedXmlScope = null)
    {
        _preparedXml3275 = preparedXmlScope ?? new PreparedXmlScope4505();
        _portable = portable;
        CompatibilityProfile = compatibilityProfile;
        NativeFocusGearCatalog1339 = nativeFocusGearCatalog1339;
        DependencyRecipe1244 = ValidateDependencyRecipe1244(dependencyRecipe1244);
        SourcePathSeparator = sourcePathSeparator ?? Path.DirectorySeparatorChar;
        if (SourcePathSeparator is not ('/' or '\\')
            || (!portable && SourcePathSeparator != Path.DirectorySeparatorChar)) throw Rejected();
        ValidateCharacter(characterXml);
        ValidateCatalog(catalog);
        CharacterXml = characterXml;
        PolicyDigest = policyDigest;
        ProfileId = profileId;
        _catalog = catalog with { Overlays = Array.AsReadOnly(catalog.Overlays.ToArray()) };
        _selection = selection.Take(MaximumFiles + 1).ToArray();
        if (_selection.Length > MaximumFiles || files.Count > MaximumFiles
            || existence.Count > MaximumObservations) throw Rejected();
        long total = 0;
        foreach (var (path, bytes) in files)
        {
            ValidatePath(path);
            ValidateFile(path, bytes, ref total);
        }
        _files = files.ToDictionary(row => row.Key, row => row.Value.ToArray(), StringComparer.Ordinal);
        _existence = new(existence, StringComparer.Ordinal);
        _inventories = new();
        int entries = 0;
        foreach (Inventory inventory in inventories)
        {
            if (_inventories.Count >= MaximumObservations || inventory.Entries.Length > MaximumObservations
                || (entries += inventory.Entries.Length) > MaximumObservations) throw Rejected();
            ValidatePath(inventory.Key.Directory);
            ValidateText(inventory.Key.Pattern);
            if (inventory.Key.Search is not (SearchOption.AllDirectories or SearchOption.TopDirectoryOnly)
                || !inventory.Exists && inventory.Entries.Length != 0) throw Rejected();
            foreach (string path in inventory.Entries) ValidatePath(path);
            if (!inventory.Key.Directories && inventory.Entries.Any(path => !_files.ContainsKey(path))) throw Rejected();
            if (!inventory.Entries.SequenceEqual(inventory.Entries.Distinct(StringComparer.Ordinal)
                    .OrderBy(PathOrderKey, StringComparer.Ordinal), StringComparer.Ordinal)) throw Rejected();
            if (!_inventories.TryAdd(inventory.Key, inventory with { Entries = inventory.Entries.ToArray() })) throw Rejected();
        }
        foreach (var (path, exists) in _existence)
        {
            ValidatePath(path);
            if (exists != _files.ContainsKey(path)) throw Rejected();
        }
        foreach (Selection item in _selection)
        {
            ValidateText(item.Name); ValidatePath(item.Path); ValidateText(item.ManifestId); ValidateText(item.Version);
        }
        ValidateText(policyDigest); ValidateText(profileId);
        if (_portable) ValidatePortablePrivacy();
        if (compatibilityProfile is not null)
        {
            CompatibilityDiagnostics = InspectLegacyImport();
            if (CompatibilityDiagnostics.Any(row => row.Severity == RulePackResolutionSeverityLevels.Error))
                throw new CompatibilityRejection(CompatibilityDiagnostics);
        }
    }

    internal IReadOnlyList<Selection> SelectionInputs => Array.AsReadOnly(_selection.ToArray());

    internal ContentOverlayCatalog Catalog => _catalog with { Overlays = Array.AsReadOnly(_catalog.Overlays.ToArray()) };
    internal bool MatchesSelection(string profileId, IEnumerable<Selection> selection)
        => ProfileId == profileId && _selection.SequenceEqual(selection);

    internal byte[] ReadAllBytes(string path)
        => _files.TryGetValue(path, out byte[]? bytes) ? bytes.ToArray() : throw Rejected();

    // Raw parsing only: no selected profile, merged tree, projection, consumer
    // observation or authority enters this preparation. The sealed capture owns
    // detached bytes; each independent reconstruction still reads/admits its bytes
    // and receives its own mutable tree. A private workspace may lend a bounded
    // parsing scope to its imports; ordinary captures keep an input-local scope.
    // No policy/admission result or parsing preparation enters the archive.
    internal bool TryReadPreparedXml3275(string path, ReadOnlySpan<byte> expectedBytes,
        out XDocument? document, out bool parsed)
    {
        document = null;
        parsed = false;
        if (!_files.TryGetValue(path, out byte[]? bytes)
            || !expectedBytes.SequenceEqual(bytes)) return false;
        return _preparedXml3275.TryRead(path, bytes, out document, out parsed);
    }

    // Owned by one RetainedRuleSourceWorkspace (or one unshared input). The key
    // includes the reader's exact base URI and bytes, not an archive/policy digest.
    // Entries contain only raw parsing results; resolvers and accounting stay fresh.
    // A changed file, full scope or retired scope takes the ordinary parsing path.
    internal sealed class PreparedXmlScope4505 : IDisposable
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, (byte[] Bytes, XDocument Document)> _documents = new(StringComparer.Ordinal);
        private int _bytes;
        private bool _active = true;

        internal bool TryRead(string path, byte[] bytes, out XDocument? document, out bool parsed)
        {
            document = null;
            parsed = false;
            lock (_sync)
            {
                if (!_active) return false;
                if (_documents.TryGetValue(path, out var prepared))
                {
                    if (!bytes.AsSpan().SequenceEqual(prepared.Bytes)) return false;
                    document = new XDocument(prepared.Document);
                    return true;
                }
                if (_documents.Count >= MaximumPreparedXmlFiles3275
                    || bytes.Length > MaximumPreparedXmlBytes3275 - _bytes) return false;

                // Same parser/options as SourceInputSnapshot. Retain an owned byte
                // copy for exact comparisons; neither it nor this tree escapes.
                byte[] owned = bytes.ToArray();
                using var stream = new MemoryStream(owned, writable: false);
                using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                }, path);
                XDocument tree = XDocument.Load(reader, LoadOptions.None);
                if (tree.Root is null) return false;
                _documents.Add(path, (owned, tree));
                _bytes += owned.Length;
                document = new XDocument(tree);
                parsed = true;
                return true;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _active = false;
                _documents.Clear();
                _bytes = 0;
            }
        }
    }

    internal bool FileExists(string path)
        => _existence.TryGetValue(path, out bool exists) ? exists : throw Rejected();

    internal Inventory ReadInventory(InventoryKey key)
        => _inventories.TryGetValue(key, out Inventory? value)
            ? value with { Entries = value.Entries.ToArray() } : throw Rejected();

    internal static InvalidOperationException Rejected() => new("Private rule inputs could not be admitted.");

    internal static void ValidateText(string value)
    {
        if (value is null || value.Length > MaximumTextBytes || Utf8.GetByteCount(value) > MaximumTextBytes) throw Rejected();
    }

    private static void ValidatePath(string value)
    {
        ValidateText(value);
        if (!Path.IsPathFullyQualified(value) || Path.GetFullPath(value) != value) throw Rejected();
    }

    internal static void ValidateCatalog(ContentOverlayCatalog catalog)
    {
        if (catalog.Overlays.Count > MaximumFiles) throw Rejected();
        ValidateText(catalog.BaseDataPath); ValidateText(catalog.BaseLanguagePath);
        foreach (var pack in catalog.Overlays)
        {
            ValidateText(pack.Id); ValidateText(pack.Name); ValidateText(pack.RootPath);
            ValidateText(pack.DataPath); ValidateText(pack.LanguagePath); ValidateText(pack.Mode); ValidateText(pack.Description);
        }
    }

    internal static void ValidateCharacter(string xml)
    {
        if (xml.Length > MaximumXmlCharacters) throw Rejected();
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, XmlSettings());
        ValidateReader(reader, "character", Digest(Utf8.GetBytes(xml)));
    }

    internal static XmlReaderSettings XmlSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
        MaxCharactersInDocument = MaximumXmlCharacters, MaxCharactersFromEntities = MaximumXmlCharacters
    };

    internal static void ValidateFile(string path, byte[] bytes, ref long total)
    {
        if (bytes.Length > MaximumFileBytes || (total += bytes.LongLength) > MaximumTotalBytes) throw Rejected();
        if (!string.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase)) return;
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, XmlSettings());
        ValidateReader(reader, path, Digest(bytes));
    }

    private static void ValidateReader(XmlReader reader, string source, string digest)
    {
        while (reader.Read())
        {
            if (reader.Depth > MaximumXmlDepth) throw Rejected();
            // Capture may invoke the existing profile/skill policy consumers. Do not execute
            // imported XPath or regex as a side effect of admission. Ordinary callers retain
            // their existing behavior; this stricter boundary rejects these captures.
            if (reader.NodeType == XmlNodeType.Element && reader.HasAttributes)
            {
                while (reader.MoveToNextAttribute())
                    if (reader.LocalName is "xpathfilter" or "regexpattern")
                        throw new CompatibilityRejection([CompatibilityDiagnostic("unsupported-" + reader.LocalName,
                            source, digest, reader as IXmlLineInfo,
                            "Retained capture forbids XPath and regex directives; original input was not rewritten.")]);
                reader.MoveToElement();
            }
        }
    }

    internal byte[] Export()
    {
        // Count first, including metadata, before allocating the archive buffer.
        using var counter = new BoundedCountingStream();
        using (var writer = new BinaryWriter(counter, Utf8, leaveOpen: true)) Write(writer);
        byte[] archive = new byte[checked((int)counter.Length)];
        using var stream = new MemoryStream(archive, writable: true);
        using (var writer = new BinaryWriter(stream, Utf8, leaveOpen: true)) Write(writer);
        if (stream.Position != archive.Length) throw Rejected();
        return archive;
    }

    internal static string Digest(ReadOnlySpan<byte> bytes)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private void Write(BinaryWriter writer)
    {
        WriteCaptureHeader1244(writer);
        WriteText(writer, CharacterXml, MaximumArchiveBytes);
        WriteEnvironmentInputs(writer);
    }

    // Reuse the archive's canonical encoding for the environment binding. Character
    // bytes remain separately bound by the existing whole-capture IntegrityDigest.
    private void WriteEnvironmentInputs(BinaryWriter writer)
    {
        WriteText(writer, PolicyDigest); WriteText(writer, ProfileId);
        WriteArchivePath(writer, _catalog.BaseDataPath); WriteArchivePath(writer, _catalog.BaseLanguagePath);
        writer.Write(_catalog.Overlays.Count);
        foreach (var pack in _catalog.Overlays)
        {
            WriteText(writer, pack.Id); WriteText(writer, pack.Name); WriteArchivePath(writer, pack.RootPath);
            WriteArchivePath(writer, pack.DataPath); WriteArchivePath(writer, pack.LanguagePath);
            writer.Write(pack.Priority); writer.Write(pack.Enabled); WriteText(writer, pack.Mode); WriteText(writer, pack.Description);
        }
        writer.Write(_selection.Length);
        foreach (var item in _selection)
        {
            WriteText(writer, item.Name); WriteArchivePath(writer, item.Path); WriteText(writer, item.ManifestId); WriteText(writer, item.Version);
        }
        writer.Write(_files.Count);
        foreach (var (path, bytes) in _files.OrderBy(row => PathOrderKey(row.Key), StringComparer.Ordinal))
        {
            WriteArchivePath(writer, path); writer.Write(bytes.Length); writer.Write(bytes);
        }
        writer.Write(_existence.Count);
        foreach (var (path, exists) in _existence.OrderBy(row => PathOrderKey(row.Key), StringComparer.Ordinal))
        {
            WriteArchivePath(writer, path); writer.Write(exists);
        }
        writer.Write(_inventories.Count);
        foreach (var item in _inventories.Values.OrderBy(row => PathOrderKey(row.Key.Directory), StringComparer.Ordinal)
                     .ThenBy(row => row.Key.Pattern, StringComparer.Ordinal).ThenBy(row => row.Key.Search).ThenBy(row => row.Key.Directories))
        {
            WriteArchivePath(writer, item.Key.Directory); WriteText(writer, item.Key.Pattern);
            writer.Write((int)item.Key.Search); writer.Write(item.Key.Directories); writer.Write(item.Exists);
            writer.Write(item.Entries.Length);
            foreach (string path in item.Entries) WriteArchivePath(writer, path);
        }
    }

    internal static RetainedCharacterRuleSourceInputs Import(ReadOnlySpan<byte> bytes, string expectedDigest,
        PreparedXmlScope4505? preparedXmlScope = null)
    {
        if (bytes.Length > MaximumArchiveBytes) throw Rejected();
        byte[] detached = bytes.ToArray();
        if (Digest(detached) != expectedDigest) throw Rejected();
        using var stream = new MemoryStream(detached, writable: false);
        using var reader = new BinaryReader(stream, Utf8);
        ReadCaptureHeader1244(reader, out bool portable, out char separator, out string recipe1244,
            out bool nativeFocusGearCatalog1339, out string? compatibilityProfile);
        string ReadPath() => portable ? FromAddress(ReadText(reader)) : ReadText(reader);
        string character = ReadText(reader, checked((int)MaximumXmlCharacters * 4));
        ValidateCharacter(character);
        string policy = ReadText(reader), profile = ReadText(reader);
        string data = ReadPath(), language = ReadPath();
        var packs = new ContentOverlayPack[ReadCount(reader, MaximumFiles)];
        for (int i = 0; i < packs.Length; i++)
            packs[i] = new(ReadText(reader), ReadText(reader), ReadPath(), ReadPath(), ReadPath(),
                reader.ReadInt32(), ReadBool(reader), ReadText(reader), ReadText(reader));
        var selection = new Selection[ReadCount(reader, MaximumFiles)];
        for (int i = 0; i < selection.Length; i++) selection[i] = new(ReadText(reader), ReadPath(), ReadText(reader), ReadText(reader));
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        int count = ReadCount(reader, MaximumFiles);
        for (int i = 0; i < count; i++)
        {
            string path = ReadPath();
            int length = ReadCount(reader, MaximumFileBytes);
            if (total + length > MaximumTotalBytes) throw Rejected();
            byte[] content = ReadBytes(reader, length);
            // Keep allocation bounds here; the constructor below validates every detached
            // file's XML before retaining it. Do not parse those same bytes twice.
            total += content.LongLength;
            if (!files.TryAdd(path, content)) throw Rejected();
        }
        var existence = new Dictionary<string, bool>(StringComparer.Ordinal);
        count = ReadCount(reader, MaximumObservations);
        for (int i = 0; i < count; i++) if (!existence.TryAdd(ReadPath(), ReadBool(reader))) throw Rejected();
        var inventories = new Inventory[ReadCount(reader, MaximumObservations)];
        int entries = 0;
        for (int i = 0; i < inventories.Length; i++)
        {
            var key = new InventoryKey(ReadPath(), ReadText(reader), (SearchOption)reader.ReadInt32(), ReadBool(reader));
            bool exists = ReadBool(reader);
            int length = ReadCount(reader, MaximumObservations);
            if ((entries += length) > MaximumObservations) throw Rejected();
            var paths = new string[length];
            for (int j = 0; j < paths.Length; j++) paths[j] = ReadPath();
            inventories[i] = new(key, exists, paths);
        }
        if (stream.Position != stream.Length) throw Rejected();
        var result = new RetainedCharacterRuleSourceInputs(new(data, language, packs), character,
            policy, profile, selection, files, existence, inventories, portable, separator, recipe1244,
            nativeFocusGearCatalog1339, compatibilityProfile, preparedXmlScope);
        // Canonical encoding rejects alternate ordering, duplicate/ambiguous address encodings,
        // trailing fields and alternate spellings. V1 remains byte-compatible for local recovery.
        if (portable || recipe1244 != LegacyDependencyRecipe1244 || nativeFocusGearCatalog1339 || compatibilityProfile is not null)
        {
            // Compare the same canonical writer directly with the owned input.
            // No admission result is cached and alternate encodings still fail.
            using var comparison = new CanonicalComparisonStream(detached);
            using (var writer = new BinaryWriter(comparison, Utf8, leaveOpen: true)) result.Write(writer);
            if (comparison.Position != comparison.Length) throw Rejected();
        }
        return result;
    }

    private static bool ReadBool(BinaryReader reader) => reader.ReadByte() switch { 0 => false, 1 => true, _ => throw Rejected() };
    private static int ReadCount(BinaryReader reader, int maximum)
    {
        int value = reader.ReadInt32();
        return value >= 0 && value <= maximum ? value : throw Rejected();
    }
    private static byte[] ReadBytes(BinaryReader reader, int length)
    {
        if (reader.BaseStream.Length - reader.BaseStream.Position < length) throw Rejected();
        byte[] result = reader.ReadBytes(length);
        return result.Length == length ? result : throw Rejected();
    }
    private static string ReadText(BinaryReader reader, int maximum = MaximumTextBytes)
        => Utf8.GetString(ReadBytes(reader, ReadCount(reader, maximum)));
    private static void WriteText(BinaryWriter writer, string value, int maximum = MaximumTextBytes)
    {
        int length = Utf8.GetByteCount(value);
        if (length > maximum) throw Rejected();
        writer.Write(length);
        // GetByteCount already checks the strict UTF-8 encoding. The sizing pass
        // needs its length, not a second temporary encoded copy of every string.
        if (writer.BaseStream is BoundedCountingStream counter) counter.Advance(length);
        else writer.Write(Utf8.GetBytes(value));
    }

    private sealed class CanonicalComparisonStream(byte[] expected) : Stream
    {
        private int _position;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => expected.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > expected.Length - _position
                || !buffer.SequenceEqual(expected.AsSpan(_position, buffer.Length))) throw Rejected();
            _position += buffer.Length;
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class BoundedCountingStream : Stream
    {
        private long _length;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position { get => _length; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) => Advance(buffer.Length);
        internal void Advance(int count)
        {
            if ((_length += count) > MaximumArchiveBytes) throw Rejected();
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
