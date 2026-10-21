using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Chummer.Application.Content;

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
    internal string CharacterXml { get; }
    internal string PolicyDigest { get; }
    internal string ProfileId { get; }

    internal RetainedCharacterRuleSourceInputs(ContentOverlayCatalog catalog, string characterXml,
        string policyDigest, string profileId, IEnumerable<Selection> selection,
        IReadOnlyDictionary<string, byte[]> files, IReadOnlyDictionary<string, bool> existence,
        IEnumerable<Inventory> inventories, bool portable = false, char? sourcePathSeparator = null)
    {
        _portable = portable;
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
    }

    internal IReadOnlyList<Selection> SelectionInputs => Array.AsReadOnly(_selection.ToArray());

    internal ContentOverlayCatalog Catalog => _catalog with { Overlays = Array.AsReadOnly(_catalog.Overlays.ToArray()) };
    internal bool MatchesSelection(string profileId, IEnumerable<Selection> selection)
        => ProfileId == profileId && _selection.SequenceEqual(selection);

    internal byte[] ReadAllBytes(string path)
        => _files.TryGetValue(path, out byte[]? bytes) ? bytes.ToArray() : throw Rejected();

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
        ValidateReader(reader);
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
        ValidateReader(reader);
    }

    private static void ValidateReader(XmlReader reader)
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
                    if (reader.LocalName is "xpathfilter" or "regexpattern") throw Rejected();
                reader.MoveToElement();
            }
        }
    }

    internal byte[] Export()
    {
        // Count first, including metadata, before allocating the archive buffer.
        using var counter = new BoundedCountingStream();
        using (var writer = new BinaryWriter(counter, Utf8, leaveOpen: true)) Write(writer);
        using var stream = new MemoryStream(checked((int)counter.Length));
        using (var writer = new BinaryWriter(stream, Utf8, leaveOpen: true)) Write(writer);
        return stream.ToArray();
    }

    internal static string Digest(ReadOnlySpan<byte> bytes)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private void Write(BinaryWriter writer)
    {
        WriteText(writer, _portable ? PortableFormat : Format);
        if (_portable) writer.Write((byte)SourcePathSeparator); WriteText(writer, CharacterXml, MaximumArchiveBytes);
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

    internal static RetainedCharacterRuleSourceInputs Import(ReadOnlySpan<byte> bytes, string expectedDigest)
    {
        if (bytes.Length > MaximumArchiveBytes) throw Rejected();
        byte[] detached = bytes.ToArray();
        if (Digest(detached) != expectedDigest) throw Rejected();
        using var stream = new MemoryStream(detached, writable: false);
        using var reader = new BinaryReader(stream, Utf8);
        string format = ReadText(reader);
        bool portable = format == PortableFormat;
        char separator = portable ? (char)reader.ReadByte() : Path.DirectorySeparatorChar;
        if (separator is not ('/' or '\\')) throw Rejected();
        if (!portable && format != Format) throw Rejected();
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
            ValidateFile(path, content, ref total);
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
            policy, profile, selection, files, existence, inventories, portable, separator);
        // Canonical encoding rejects alternate ordering, duplicate/ambiguous address encodings,
        // trailing fields and alternate spellings. V1 remains byte-compatible for local recovery.
        if (portable && !result.Export().AsSpan().SequenceEqual(detached)) throw Rejected();
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
        writer.Write(length); writer.Write(Utf8.GetBytes(value));
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
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if ((_length += buffer.Length) > MaximumArchiveBytes) throw Rejected();
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
