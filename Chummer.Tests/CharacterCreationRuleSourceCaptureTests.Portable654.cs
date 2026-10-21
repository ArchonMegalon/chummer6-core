using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Application.Content;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationRuleSourceCaptureTests
{
    [TestMethod]
    public void PrivateWire670_embedded_sender_paths_remain_local_without_rewriting_source_bytes()
    {
        using var fixture = new Fixture();
        // UTF-16 must not hide a path from the privacy check.
        byte[] bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(
            "<?xml version=\"1.0\" encoding=\"utf-16\"?><!-- " + fixture.Root + " -->" + Gear(10))).ToArray();
        File.WriteAllBytes(fixture.Data("gear.xml"), bytes);
        Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
        byte[] archive = capture!.ExportPrivateInputs();
        var retained = RetainedCharacterRuleSourceInputs.Import(archive, capture.IntegrityDigest);
        Assert.IsFalse(retained.IsPortable);
        Assert.ThrowsExactly<InvalidOperationException>(() => retained.ToPortable());
        Assert.IsTrue(archive.AsSpan().IndexOf(bytes) >= 0);
        Directory.Delete(fixture.Root, recursive: true);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            archive, capture.IntegrityDigest, out var restored));
        CollectionAssert.AreEqual(archive, restored!.ExportPrivateInputs());
        Assert.IsTrue(restored.CreateReplayResolver().TryCreateContext(fixture.Character)!
            .TryResolveCreationGearAuthority(out _));
    }

    [TestMethod]
    public void PrivateWire654_new_capture_is_path_free_and_root_relocation_preserves_identity()
    {
        using var first = new Fixture(selected: true);
        using var second = new Fixture(selected: true);
        foreach (string source in Directory.EnumerateFiles(first.Root, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(second.Root, Path.GetRelativePath(first.Root, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, File.ReadAllBytes(source));
        }
        Assert.IsTrue(first.Resolver().TryCaptureRuleSources(first.Character, out var original));
        Assert.IsTrue(second.Resolver().TryCaptureRuleSources(second.Character, out var relocated));
        byte[] bytes = original!.ExportPrivateInputs();
        Assert.IsTrue(RetainedCharacterRuleSourceInputs.Import(bytes, original.IntegrityDigest).IsPortable);
        Assert.IsFalse(Encoding.UTF8.GetString(bytes).Contains(first.Root, StringComparison.Ordinal));
        Assert.IsFalse(Encoding.UTF8.GetString(bytes).Contains("__chummer_rule_closure__", StringComparison.Ordinal));
        Assert.AreEqual(original.IntegrityDigest, relocated!.IntegrityDigest);
        Directory.Delete(first.Root, true);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(bytes, original.IntegrityDigest, out _));
        File.AppendAllText(second.Data("gear.xml"), "\n");
        Assert.IsTrue(second.Resolver().TryCaptureRuleSources(second.Character, out var changed));
        Assert.AreNotEqual(original.IntegrityDigest, changed!.IntegrityDigest);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("extra")]
    [DataRow("missing-existence")]
    [DataRow("extra-existence")]
    [DataRow("extra-inventory")]
    public void PrivateWire654_recomputed_digest_does_not_admit_incomplete_or_extra_observations(string damage)
    {
        using var fixture = new Fixture();
        Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
        var decoded = Decode654(capture!.ExportPrivateInputs());
        string gear = Path.Combine(decoded.Catalog.BaseDataPath, "gear.xml");
        string extra = Path.Combine(decoded.Catalog.BaseDataPath, "unused-extra.xml");
        if (damage == "missing") { decoded.Files.Remove(gear); decoded.Existence.Remove(gear); }
        if (damage == "extra") { decoded.Files.Add(extra, Encoding.UTF8.GetBytes("<chummer/>")); decoded.Existence.Add(extra, true); }
        if (damage == "missing-existence") decoded.Existence.Remove(gear);
        if (damage == "extra-existence") decoded.Existence.Add(extra, false);
        if (damage == "extra-inventory") decoded.Inventories.Add(new(
            new(decoded.Catalog.BaseDataPath, "unused-*.xml", SearchOption.TopDirectoryOnly, false), false, []));
        byte[] changed = decoded.Build(portable: true).Export();
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(changed, Digest(changed), out _));
    }

    [TestMethod]
    public void PrivateWire654_v1_remains_locally_recoverable_without_relabeling_its_bytes()
    {
        using var fixture = new Fixture();
        Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
        var decoded = Decode654(capture!.ExportPrivateInputs());
        byte[] legacy = decoded.Build(portable: false).Export();
        string legacyDigest = Digest(legacy);
        Assert.AreNotEqual(capture.IntegrityDigest, legacyDigest);
        Assert.IsFalse(RetainedCharacterRuleSourceInputs.Import(legacy, legacyDigest).IsPortable);
        Directory.Delete(fixture.Root, true);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(legacy, legacyDigest, out var local));
        CollectionAssert.AreEqual(legacy, local!.ExportPrivateInputs());
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            capture.ExportPrivateInputs(), legacyDigest, out _));
        byte[] interrupted = legacy[..^1];
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(interrupted, Digest(interrupted), out _));
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(legacy, legacyDigest, out _));
    }

    [TestMethod]
    public void PrivateWire654_path_traversal_trailing_bytes_and_duplicate_file_are_rejected()
    {
        using var fixture = new Fixture();
        Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
        byte[] bytes = capture!.ExportPrivateInputs();
        byte[] traversal = bytes.ToArray();
        int position = traversal.AsSpan().IndexOf(Encoding.UTF8.GetBytes("r000000/data"));
        Assert.IsTrue(position > 0);
        Encoding.UTF8.GetBytes("r000000/../x").CopyTo(traversal, position);
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(traversal, Digest(traversal), out _));
        byte[] trailing = [.. bytes, 0];
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(trailing, Digest(trailing), out _));
        // Duplicate keys are rejected before materializing a retained dictionary.
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        SkipText654(reader);
        reader.ReadByte(); // V3 sender path-order recipe
        for (int i = 0; i < 5; i++) SkipText654(reader);
        Assert.AreEqual(0, reader.ReadInt32()); // no overlay packs
        Assert.AreEqual(0, reader.ReadInt32()); // no selected custom directories
        int countPosition = checked((int)stream.Position), count = reader.ReadInt32();
        int first = checked((int)stream.Position);
        SkipText654(reader); SkipText654(reader);
        int firstEnd = checked((int)stream.Position);
        for (int i = 1; i < count; i++) { SkipText654(reader); SkipText654(reader); }
        int filesEnd = checked((int)stream.Position);
        byte[] duplicate = [.. bytes[..filesEnd], .. bytes[first..firstEnd], .. bytes[filesEnd..]];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(duplicate.AsSpan(countPosition), count + 1);
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(duplicate, Digest(duplicate), out _));
    }

    [TestMethod]
    public void PrivateWire662_empty_XML_markup_is_not_a_filesystem_path()
    {
        RetainedCharacterRuleSourceInputs.ValidatePortableTransferDocumentText(
            "<character><expenses /><improvements /><contacts /></character>");
        RetainedCharacterRuleSourceInputs.ValidatePortableTransferDocumentText("ordinary note");
        RetainedCharacterRuleSourceInputs.ValidatePortableTransferDocumentText("<3");
        foreach (string xml in new[]
        {
            "<character><notes>/home/runner/private</notes></character>",
            "<character note=\"C:\\Users\\runner\\private\"><expenses /></character>",
            "<character><!-- /home/runner/private --><expenses /></character>",
            "<character><notes>&#47;home/runner/private</notes></character>",
            "<!DOCTYPE character [<!ENTITY p 'private'>]><character>&p;</character>"
        })
        {
            bool rejected = false;
            try { RetainedCharacterRuleSourceInputs.ValidatePortableTransferDocumentText(xml); }
            catch (Exception error) when (error is InvalidOperationException or System.Xml.XmlException)
            { rejected = true; }
            Assert.IsTrue(rejected, "XML markup admission must still reject private paths and DTDs.");
        }
    }

    [TestMethod]
    [DataRow("/", "nested0.xml")]
    [DataRow("/", "nestedA.xml")]
    [DataRow("\\", "nested0.xml")]
    [DataRow("\\", "nestedA.xml")]
    public void PrivateWire670_sender_order_replays_nested_trees_and_relocation(string separator, string sibling)
    {
        using var first = new Fixture(selected: true);
        using var second = new Fixture(selected: true);
        string nested = first.Custom("First", Path.Combine("nested", "unused.xml"));
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        File.WriteAllText(nested, "<chummer><!-- nested bytes --></chummer>");
        File.WriteAllText(first.Custom("First", sibling), "<chummer><!-- sibling bytes --></chummer>");
        foreach (string source in Directory.EnumerateFiles(first.Root, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(second.Root, Path.GetRelativePath(first.Root, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, File.ReadAllBytes(source));
        }
        var live = first.Resolver().TryCreateContext(first.Character)!;
        Assert.IsTrue(live.TryResolveCreationEffectivePolicy(out var policy));
        Assert.IsTrue(live.TryResolveCreationGearAuthority(out var gear));
        Assert.IsTrue(first.Resolver().TryCaptureRuleSources(first.Character, out var capture));
        Assert.IsTrue(second.Resolver().TryCaptureRuleSources(second.Character, out var moved));
        Assert.AreEqual(capture!.IntegrityDigest, moved!.IntegrityDigest);
        var decoded = Decode654(capture.ExportPrivateInputs());
        // Independent reproduction of the existing policy recipe: native relative
        // ordering, then slash-delimited names and exact bytes. Never relabel a hash.
        Assert.AreEqual(policy!.PolicyDigest, OrderedPolicy670(policy, decoded, Path.DirectorySeparatorChar));
        string expectedPolicy = OrderedPolicy670(policy, decoded, separator[0]);
        string otherPolicy = OrderedPolicy670(policy, decoded, separator == "/" ? '\\' : '/');
        Assert.AreNotEqual(expectedPolicy, otherPolicy);
        var sender = decoded with { Separator = separator[0], Policy = expectedPolicy };
        byte[] archive = sender.Build(portable: true).Export();
        byte[] relocated = (Decode654(moved.ExportPrivateInputs()) with {
            Separator = separator[0], Policy = expectedPolicy }).Build(portable: true).Export();
        CollectionAssert.AreEqual(archive, relocated);
        string digest = Digest(archive);
        Directory.Delete(first.Root, recursive: true);
        Directory.Delete(second.Root, recursive: true);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(archive, digest, out var restored));
        Assert.AreEqual(expectedPolicy, restored!.EffectivePolicyDigest);
        CollectionAssert.AreEqual(archive, restored.ExportPrivateInputs());
        var replayResolver = (FileSystemCharacterSourceDataResolver)restored.CreateReplayResolver();
        var replay = replayResolver.TryCreateContext(first.Character)!;
        Assert.IsTrue(replay.TryResolveCreationGearAuthority(out var replayGear));
        Assert.AreEqual(JsonSerializer.Serialize(gear), JsonSerializer.Serialize(replayGear));
        Assert.IsTrue(replayResolver.TryCaptureRuleSources(first.Character, out var observed));
        CollectionAssert.AreEqual(archive, observed!.ExportPrivateInputs());

        using var stream = new MemoryStream(archive);
        using var reader = new BinaryReader(stream);
        SkipText654(reader);
        int recipeOffset = checked((int)stream.Position);
        byte[] wrongRecipe = archive.ToArray();
        wrongRecipe[recipeOffset] = (byte)(separator == "/" ? '\\' : '/');
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            wrongRecipe, digest, out _)); // archive identity binding
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            wrongRecipe, Digest(wrongRecipe), out _)); // independent policy/closure replay
        byte[] canonicallyForged = (sender with { Separator = separator == "/" ? '\\' : '/' })
            .Build(portable: true).Export();
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            canonicallyForged, Digest(canonicallyForged), out _)); // canonical, but the old policy is false
        wrongRecipe[recipeOffset] = (byte)'?';
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            wrongRecipe, Digest(wrongRecipe), out _));
    }

    private static string OrderedPolicy670(CharacterCreationEffectivePolicyContext policy,
        DecodedSource654 inputs, char separator)
    {
        var sources = policy.Sources.Select(source =>
        {
            if (source.Kind != CharacterCreationPolicySourceKind.SelectedAmend) return source;
            string directory = inputs.Selected[source.Order].Path;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            void Frame(byte[] value)
            {
                Span<byte> size = stackalloc byte[8];
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(size, value.LongLength);
                hash.AppendData(size); hash.AppendData(value);
            }
            Frame(Encoding.UTF8.GetBytes(source.IdentityDigest));
            foreach (var row in inputs.Files.Where(row => row.Key.StartsWith(
                directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && Path.GetExtension(row.Key) == ".xml")
                .OrderBy(row => Path.GetRelativePath(directory, row.Key)
                    .Replace(Path.DirectorySeparatorChar, separator), StringComparer.Ordinal))
            {
                Frame(Encoding.UTF8.GetBytes(Path.GetRelativePath(directory, row.Key).Replace('\\', '/')));
                Frame(row.Value);
            }
            return source with { ContentDigest = "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() };
        }).ToArray();
        return CharacterCreationEffectivePolicyContext.ComputeDigest(policy with { Sources = sources });
    }

    [TestMethod]
    [DataRow("character")]
    [DataRow("catalog")]
    public void PrivateWire670_path_bearing_local_inputs_capture_and_restore_but_cannot_be_portable(string location)
    {
        using var fixture = new Fixture();
        string pathText = "/home/runner/private C:\\Users\\runner\\private file:///private/source";
        string character = fixture.Character;
        if (location == "character")
            character = character.Replace("</character>", "<notes>" + pathText + "</notes></character>",
                StringComparison.Ordinal);
        else
            fixture.Catalog.Value = fixture.Catalog.Value with { Overlays = [
                new ContentOverlayPack("disabled", "Local metadata", fixture.Root, fixture.Data(string.Empty),
                    string.Empty, 0, false, "merge", pathText)] };
        var resolver = fixture.Resolver();
        Assert.IsTrue(resolver.TryCreateContext(character)!.TryResolveCreationEffectivePolicy(out var before));
        Assert.IsTrue(resolver.TryCaptureRuleSources(character, out var capture));
        byte[] archive = capture!.ExportPrivateInputs();
        Assert.IsTrue(archive.AsSpan().IndexOf(Encoding.UTF8.GetBytes(pathText)) >= 0);
        var retained = RetainedCharacterRuleSourceInputs.Import(archive, capture.IntegrityDigest);
        Assert.IsFalse(retained.IsPortable);
        Assert.AreEqual(character, retained.CharacterXml);
        Assert.ThrowsExactly<InvalidOperationException>(() => retained.ToPortable());
        Directory.Delete(fixture.Root, recursive: true);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            archive, capture.IntegrityDigest, out var restored));
        CollectionAssert.AreEqual(archive, restored!.ExportPrivateInputs());
        Assert.IsTrue(restored.CreateReplayResolver().TryCreateContext(character)!
            .TryResolveCreationEffectivePolicy(out var after));
        Assert.AreEqual(before!.PolicyDigest, after!.PolicyDigest);
    }

    [TestMethod]
    public void PrivateWire662_trailing_separator_capture_replays_without_remapping_virtual_roots()
    {
        using var fixture = new Fixture();
        fixture.Catalog.Value = fixture.Catalog.Value with
        { BaseDataPath = fixture.Data(string.Empty) + Path.DirectorySeparatorChar };
        Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
        byte[] bytes = capture!.ExportPrivateInputs();
        Directory.Delete(fixture.Root, true);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            bytes, capture.IntegrityDigest, out var restored));
        CollectionAssert.AreEqual(bytes, restored!.ExportPrivateInputs());
    }

    private static void SkipText654(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        reader.BaseStream.Position += length;
    }

    // Test decoder only: reconstruct the same genuine source capture as historical
    // local V1 bytes. No receipt, authorization or production history is fabricated.
    internal static DecodedSource654 Decode654(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var r = new BinaryReader(stream, Encoding.UTF8);
        string Text() => Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
        string root = OperatingSystem.IsWindows() ? @"C:\__chummer_rule_closure__" : "/__chummer_rule_closure__";
        string Address() { string value = Text(); return value.Length == 0 ? "" : Path.Combine(root, value.Replace('/', Path.DirectorySeparatorChar)); }
        Assert.AreEqual(RetainedCharacterRuleSourceInputs.PortableFormat, Text());
        char separator = (char)r.ReadByte();
        string character = Text(), policy = Text(), profile = Text(), data = Address(), language = Address();
        var packs = new ContentOverlayPack[r.ReadInt32()];
        for (int i = 0; i < packs.Length; i++)
            packs[i] = new(Text(), Text(), Address(), Address(), Address(), r.ReadInt32(), r.ReadBoolean(), Text(), Text());
        var selected = new RetainedCharacterRuleSourceInputs.Selection[r.ReadInt32()];
        for (int i = 0; i < selected.Length; i++) selected[i] = new(Text(), Address(), Text(), Text());
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++) { string path = Address(); files.Add(path, r.ReadBytes(r.ReadInt32())); }
        var existence = new Dictionary<string, bool>(StringComparer.Ordinal);
        count = r.ReadInt32();
        for (int i = 0; i < count; i++) existence.Add(Address(), r.ReadBoolean());
        var inventories = new List<RetainedCharacterRuleSourceInputs.Inventory>();
        count = r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var key = new RetainedCharacterRuleSourceInputs.InventoryKey(Address(), Text(), (SearchOption)r.ReadInt32(), r.ReadBoolean());
            bool exists = r.ReadBoolean();
            var entries = new string[r.ReadInt32()];
            for (int j = 0; j < entries.Length; j++) entries[j] = Address();
            inventories.Add(new(key, exists, entries));
        }
        Assert.AreEqual(stream.Length, stream.Position);
        return new(new(data, language, packs), character, policy, profile, selected, files, existence, inventories, separator);
    }

    internal sealed record DecodedSource654(ContentOverlayCatalog Catalog, string Character, string Policy, string Profile,
        RetainedCharacterRuleSourceInputs.Selection[] Selected, Dictionary<string, byte[]> Files,
        Dictionary<string, bool> Existence, List<RetainedCharacterRuleSourceInputs.Inventory> Inventories, char Separator)
    {
        internal RetainedCharacterRuleSourceInputs Build(bool portable)
            => new(Catalog, Character, Policy, Profile, Selected, Files, Existence,
                Inventories.Select(item => item with { Entries = item.Entries.OrderBy(
                    path => path.Replace(Path.DirectorySeparatorChar, portable ? Separator : Path.DirectorySeparatorChar),
                    StringComparer.Ordinal).ToArray() }), portable,
                portable ? Separator : Path.DirectorySeparatorChar);
    }
}
