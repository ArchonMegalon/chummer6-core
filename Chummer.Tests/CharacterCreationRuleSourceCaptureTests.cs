using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Content;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed partial class CharacterCreationRuleSourceCaptureTests
{
    private const string ProfileId = "11111111-1111-1111-1111-111111111111";
    private const string GearId = "22222222-2222-2222-2222-222222222222";

    [TestMethod]
    public void RuleSource405_Exact_bytes_policy_digests_and_consumer_rows_survive_cold_restore()
    {
        using var fixture = new Fixture();
        // BOM, CRLF, comments and insignificant whitespace must remain actual digest inputs.
        byte[] bytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("<!-- retained -->\r\n" + Gear(10) + "\r\n")];
        File.WriteAllBytes(fixture.Data("gear.xml"), bytes);
        var resolver = fixture.Resolver();
        var live = resolver.TryCreateContext(fixture.Character)!;
        Assert.IsNotNull(live);
        Assert.IsTrue(live.TryResolveCreationEffectivePolicy(out var policy));
        Assert.IsTrue(live.TryResolveCreationSkillsAuthority(out var skills));
        Assert.IsTrue(live.TryResolveCreationGearAuthority(out var gear));
        Assert.IsTrue(resolver.TryCaptureRuleSources(fixture.Character, out var capture));
        Assert.IsNotNull(capture);
        byte[] archive = capture.ExportPrivateInputs();
        Assert.IsTrue(archive.AsSpan().IndexOf(bytes) >= 0);
        Assert.AreEqual(policy!.PolicyDigest, capture.EffectivePolicyDigest);
        Assert.AreEqual(FramedDigest("base", bytes), policy.Sources.Single(row => row.SourceId == "gear").ContentDigest);
        Assert.IsNull(policy.Sources.Single(row => row.SourceId == "streams").ContentDigest);
        Assert.IsFalse(policy.Domains.Single(row => row.Domain == CharacterCreationPolicyDomain.CharacterMechanics)
            .Disposition == CharacterCreationPolicyDisposition.Effective);
        Directory.Delete(fixture.Root, recursive: true);

        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(archive, capture.IntegrityDigest, out var restored));
        Assert.IsNotNull(restored);
        var replay = restored.CreateReplayResolver().TryCreateContext(fixture.Character);
        Assert.IsNotNull(replay);
        Assert.IsTrue(replay.TryResolveCreationEffectivePolicy(out var replayPolicy));
        Assert.AreEqual(JsonSerializer.Serialize(policy), JsonSerializer.Serialize(replayPolicy));
        Assert.IsTrue(replay.TryResolveCreationSkillsAuthority(out var replaySkills));
        Assert.AreEqual(JsonSerializer.Serialize(skills), JsonSerializer.Serialize(replaySkills));
        Assert.IsTrue(replay.TryResolveCreationGearAuthority(out var replayGear));
        Assert.AreEqual(JsonSerializer.Serialize(gear), JsonSerializer.Serialize(replayGear));
    }

    [TestMethod]
    public void RuleSource405_Returned_archive_buffers_cannot_mutate_a_sealed_capture()
    {
        using var fixture = new Fixture();
        Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
        byte[] before = capture!.ExportPrivateInputs();
        byte[] detached = capture.ExportPrivateInputs();
        Array.Fill(detached, (byte)0);
        CollectionAssert.AreEqual(before, capture.ExportPrivateInputs());
        Assert.IsNotNull(capture.CreateReplayResolver().TryCreateContext(fixture.Character));
    }

    [TestMethod]
    public void RuleSource405_Existing_operation_scope_exposes_capture_of_its_admitted_context()
    {
        using var fixture = new Fixture();
        using var operation = fixture.Resolver().CreateOperationScope();
        var context = operation.TryCreateContext(fixture.Character);
        Assert.IsNotNull(context);
        var capability = operation as ICharacterCreationRuleSourceCapture;
        Assert.IsNotNull(capability);
        Assert.IsTrue(capability.TryCaptureRuleSources(fixture.Character, out var capture));
        Assert.AreSame(context, operation.TryCreateContext(fixture.Character));
        Assert.IsNotNull(capture!.CreateReplayResolver().TryCreateContext(fixture.Character));
        File.AppendAllText(fixture.Data("gear.xml"), "\n");
        Assert.IsFalse(capability.TryCaptureRuleSources(fixture.Character, out _));
    }

    [TestMethod]
    public void RuleSource405_Version_selection_retains_unselected_manifest_candidates()
    {
        using var fixture = new Fixture(selected: true);
        const string id = "77777777-7777-7777-7777-777777777777";
        File.WriteAllText(fixture.Custom("First", "manifest.xml"), $"<manifest><guid>{id}</guid><version>2.0</version></manifest>");
        File.WriteAllText(fixture.Custom("Unselected", "manifest.xml"), $"<manifest><guid>{id}</guid><version>1.0</version></manifest>");
        string settings = File.ReadAllText(fixture.Data("settings.xml"));
        File.WriteAllText(fixture.Data("settings.xml"), settings.Replace("<directoryname>First</directoryname>",
            $"<directoryname>{id}&gt;1.0</directoryname>", StringComparison.Ordinal));
        Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
        byte[] archive = capture!.ExportPrivateInputs();
        byte[] candidate = File.ReadAllBytes(fixture.Custom("Unselected", "manifest.xml"));
        Assert.IsTrue(archive.AsSpan().IndexOf(candidate) >= 0);
        Directory.Delete(fixture.Root, recursive: true);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(archive, capture.IntegrityDigest, out var restored));
        Assert.IsNotNull(restored!.CreateReplayResolver().TryCreateContext(fixture.Character));
    }

    [TestMethod]
    [DataRow("same-length")]
    [DataRow("manifest")]
    [DataRow("membership")]
    [DataRow("absent")]
    [DataRow("catalog")]
    [DataRow("candidate")]
    public void RuleSource405_Live_capture_rejects_changed_observed_inputs(string change)
    {
        using var fixture = new Fixture(selected: true);
        using var reads = fixture.Resolver(weakIdentity: true).CreateValidatedReadScope();
        Assert.IsTrue(reads.TryCaptureRuleSources(fixture.Character, out _));
        switch (change)
        {
            case "same-length": ReplaceSameLength(fixture.Data("gear.xml"), "<cost>10</cost>", "<cost>11</cost>"); break;
            case "manifest": File.AppendAllText(fixture.Custom("First", "manifest.xml"), "\n"); break;
            case "membership": File.WriteAllText(fixture.Custom("First", "custom_new.xml"), "<chummer/>"); break;
            case "absent": File.WriteAllText(fixture.Data("streams.xml"), "<chummer/>"); break;
            case "catalog": fixture.Catalog.Value = fixture.Catalog.Value with { BaseLanguagePath = fixture.Root + "/changed" }; break;
            case "candidate": File.AppendAllText(fixture.Custom("Unselected", "manifest.xml"), "\n"); break;
        }
        Assert.IsFalse(reads.TryCaptureRuleSources(fixture.Character, out var rejected));
        Assert.IsNull(rejected);
    }

    [TestMethod]
    public void RuleSource405_Observed_ABA_stays_poisoned()
    {
        using var fixture = new Fixture();
        using var reads = fixture.Resolver(weakIdentity: true).CreateValidatedReadScope();
        Assert.IsTrue(reads.TryCaptureRuleSources(fixture.Character, out _));
        string path = fixture.Data("gear.xml");
        byte[] original = File.ReadAllBytes(path);
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        ReplaceSameLength(path, "<cost>10</cost>", "<cost>11</cost>");
        Assert.IsFalse(reads.TryCaptureRuleSources(fixture.Character, out _));
        File.WriteAllBytes(path, original);
        File.SetLastWriteTimeUtc(path, timestamp);
        Assert.IsFalse(reads.TryCaptureRuleSources(fixture.Character, out _));
    }

    [TestMethod]
    public void RuleSource405_Synchronous_exit_rejects_drift_before_publication()
    {
        using var fixture = new Fixture();
        // Entry, context catalog, pre-capture fence, post-capture fence, exit fence.
        fixture.Catalog.OnRead = count =>
        {
            if (count == 5) ReplaceSameLength(fixture.Data("gear.xml"), "<cost>10</cost>", "<cost>11</cost>");
        };
        Assert.IsFalse(fixture.Resolver(weakIdentity: true).TryCaptureRuleSources(fixture.Character, out var capture));
        Assert.IsNull(capture);
    }

    [TestMethod]
    public void RuleSource405_Global_and_ordered_selected_overlays_replay_and_settings_apply_once()
    {
        using var fixture = new Fixture(selected: true);
        string global = Path.Combine(fixture.Root, "overlay", "data");
        Directory.CreateDirectory(global);
        File.WriteAllText(Path.Combine(global, "gear.fragment.xml"), Gear(20));
        File.WriteAllText(Path.Combine(fixture.Root, "overlay", "manifest.json"), "{\"id\":\"global\"}");
        fixture.Catalog.Value = fixture.Catalog.Value with { Overlays = [new("global", "Global", Path.GetDirectoryName(global)!, global,
            string.Empty, 1, true, ContentOverlayModes.MergeCatalog, "fixture")] };
        File.WriteAllText(fixture.Custom("First", "override_gear.xml"), Gear(30));
        File.WriteAllText(fixture.Custom("First", "custom_gear.xml"), Gear(40));
        File.WriteAllText(fixture.Custom("First", "amend_gear.xml"), GearAmend(50));
        File.WriteAllText(fixture.Custom("Second", "override_gear.xml"), Gear(60));
        File.WriteAllText(fixture.Custom("Second", "custom_gear.xml"), Gear(70));
        File.WriteAllText(fixture.Custom("Second", "amend_gear.xml"), GearAmend(80));
        File.WriteAllText(fixture.Custom("First", "amend_settings.xml"),
            $"<chummer><settings><setting><id>{ProfileId}</id><nuyenformat amendoperation=\"append\">#</nuyenformat></setting></settings></chummer>");
        File.WriteAllText(fixture.Custom("Second", "amend_settings.xml"),
            $"<chummer><settings><setting><id>{ProfileId}</id><nuyenformat amendoperation=\"append\">#</nuyenformat></setting></settings></chummer>");
        Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
        var live = fixture.Resolver().TryCreateContext(fixture.Character)!;
        Assert.IsTrue(live.TryResolveCreationGearAuthority(out var expected));
        Assert.AreEqual(80m, expected.Options.Single().PackageCost);
        Assert.IsTrue(live.TryResolveMaxNuyenDecimals(out int decimals));
        Assert.AreEqual(3, decimals);
        Directory.Delete(fixture.Root, recursive: true);
        var replay = capture!.CreateReplayResolver().TryCreateContext(fixture.Character)!;
        Assert.IsTrue(replay.TryResolveCreationGearAuthority(out var actual));
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
        Assert.IsTrue(replay.TryResolveMaxNuyenDecimals(out int replayDecimals));
        Assert.AreEqual(decimals, replayDecimals);
        Assert.IsTrue(replay.TryResolveCreationEffectivePolicy(out var first));
        Assert.IsTrue(replay.TryResolveCreationEffectivePolicy(out var second));
        Assert.AreEqual(first!.PolicyDigest, second!.PolicyDigest);
        Assert.AreEqual(capture.EffectivePolicyDigest, second.PolicyDigest);
    }

    [TestMethod]
    public void RuleSource405_Missing_tampered_and_unknown_retained_inputs_fail_closed()
    {
        using var fixture = new Fixture();
        Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
        byte[] archive = capture!.ExportPrivateInputs();
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(archive[..^1], capture.IntegrityDigest, out _));
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(archive[..^1], Digest(archive[..^1]), out _));
        byte[] tampered = archive.ToArray();
        int offset = tampered.AsSpan().IndexOf(Encoding.UTF8.GetBytes("<cost>10</cost>"));
        Assert.IsTrue(offset >= 0);
        tampered[offset + 7] = (byte)'1';
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(tampered, capture.IntegrityDigest, out _));
        // Rehashing the archive does not repair its bound effective-policy source digests.
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(tampered, Digest(tampered), out _));
        var inputs = RetainedCharacterRuleSourceInputs.Import(archive, capture.IntegrityDigest);
        Assert.ThrowsExactly<InvalidOperationException>(() => new RetainedCharacterRuleSourceInputs(fixture.Catalog.Value,
            fixture.Character, capture.EffectivePolicyDigest, ProfileId, [], new Dictionary<string, byte[]>(),
            new Dictionary<string, bool>(), [new(new(fixture.Data(string.Empty).TrimEnd(Path.DirectorySeparatorChar),
                "*.xml", SearchOption.TopDirectoryOnly, false), true, [fixture.Data("gear.xml")])]));
        Assert.ThrowsExactly<InvalidOperationException>(() => inputs.ReadAllBytes(fixture.Data("unknown.xml")));
        Assert.ThrowsExactly<InvalidOperationException>(() => inputs.FileExists(fixture.Data("unknown.xml")));
        Assert.ThrowsExactly<InvalidOperationException>(() => inputs.ReadInventory(new(fixture.Root, "*", SearchOption.AllDirectories, false)));
        File.WriteAllText(fixture.Data("vehicles.xml"), "<chummer/>");
        var replay = capture.CreateReplayResolver().TryCreateContext(fixture.Character)!;
        Assert.ThrowsExactly<InvalidOperationException>(() => replay.TryResolveVehicleWorkshopCatalog(out _));
    }

    [TestMethod]
    public void RuleSource405_File_count_per_file_and_aggregate_bounds_reject_before_retention()
    {
        using var fixture = new Fixture();
        var tooMany = Enumerable.Range(0, RetainedCharacterRuleSourceInputs.MaximumFiles + 1)
            .ToDictionary(i => fixture.Data(i + ".bin"), _ => Array.Empty<byte>(), StringComparer.Ordinal);
        Assert.ThrowsExactly<InvalidOperationException>(() => Retain(fixture, tooMany));
        byte[] file = new byte[RetainedCharacterRuleSourceInputs.MaximumFileBytes];
        var aggregate = Enumerable.Range(0, 9).ToDictionary(i => fixture.Data(i + ".bin"), _ => file, StringComparer.Ordinal);
        Assert.ThrowsExactly<InvalidOperationException>(() => Retain(fixture, aggregate));
        Assert.ThrowsExactly<InvalidOperationException>(() => Retain(fixture,
            new Dictionary<string, byte[]> { [fixture.Data("large.bin")] = new byte[file.Length + 1] }));
        using (var stream = File.Create(fixture.Data("gear.xml"))) stream.SetLength(file.Length + 1L);
        Assert.IsFalse(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out _));
    }

    [TestMethod]
    [DataRow("depth")]
    [DataRow("dtd")]
    [DataRow("xpath")]
    [DataRow("regex")]
    public void RuleSource405_Xml_admission_is_bounded_and_does_not_execute_dynamic_selectors(string kind)
    {
        using var fixture = new Fixture();
        string xml = kind switch
        {
            "depth" => string.Concat(Enumerable.Repeat("<n>", 130)) + string.Concat(Enumerable.Repeat("</n>", 130)),
            "dtd" => "<!DOCTYPE chummer [<!ENTITY external SYSTEM 'file:///never-read'>]><chummer>&external;</chummer>",
            "xpath" => "<chummer xpathfilter=\"//*\"/>",
            _ => "<chummer regexpattern=\"(a+)+$\"/>"
        };
        File.WriteAllText(fixture.Data("gear.xml"), xml);
        Assert.IsFalse(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
        Assert.IsNull(capture);
    }

    [TestMethod]
    public void RuleSource405_Xml_character_and_archive_length_limits_fail_closed()
    {
        using var fixture = new Fixture();
        Assert.ThrowsExactly<InvalidOperationException>(() => RetainedCharacterRuleSourceInputs.ValidateCharacter(
            new string(' ', checked((int)RetainedCharacterRuleSourceInputs.MaximumXmlCharacters + 1))));
        Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
        byte[] archive = capture!.ExportPrivateInputs();
        // The first length prefix is checked before allocation or decoding.
        BinaryPrimitives.WriteInt32LittleEndian(archive, int.MaxValue);
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(archive, Digest(archive), out _));
    }

    [TestMethod]
    public void RuleSource405_Unsupported_priority_and_category_consumers_remain_rejected()
    {
        using var fixture = new Fixture(selected: true);
        File.WriteAllText(fixture.Custom("First", "amend_priorities.xml"), "<chummer><priorities amendoperation=\"replace\"/></chummer>");
        File.WriteAllText(fixture.Custom("First", "amend_weapons.xml"), "<chummer><categories amendoperation=\"remove\"/></chummer>");
        Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
        var live = fixture.Resolver().TryCreateContext(fixture.Character)!;
        Assert.IsTrue(live.TryResolveCreationPrerequisiteAuthority(out var expected));
        CollectionAssert.Contains(expected.Blockers.ToArray(), CharacterCreationPrerequisiteBlockers.PriorityCustomDataUnsupported);
        Assert.IsFalse(live.TryResolveCreationLoadoutAuthority(out _));
        Directory.Delete(fixture.Root, recursive: true);
        var replay = capture!.CreateReplayResolver().TryCreateContext(fixture.Character)!;
        Assert.IsTrue(replay.TryResolveCreationPrerequisiteAuthority(out var actual));
        Assert.IsFalse(actual.IsAuthoritative);
        CollectionAssert.Contains(actual.Blockers.ToArray(), CharacterCreationPrerequisiteBlockers.PriorityCustomDataUnsupported);
        Assert.IsFalse(replay.TryResolveCreationLoadoutAuthority(out _));
    }

    [TestMethod]
    public void RuleSource405_Specialized_priority_weight_amendment_replays()
    {
        using var fixture = new Fixture(selected: true);
        File.WriteAllText(fixture.Custom("First", "amend_priorities.xml"),
            "<chummer><priortysumtotenvalues><A>7</A><B>4</B></priortysumtotenvalues></chummer>");
        Assert.IsTrue(fixture.Resolver().TryCaptureRuleSources(fixture.Character, out var capture));
        var live = fixture.Resolver().TryCreateContext(fixture.Character)!;
        Assert.IsTrue(live.TryResolveCreationPrerequisiteAuthority(out var expected));
        Assert.AreEqual(7, expected.RankWeights.Single(row => row.Rank == "A").Value);
        Assert.AreEqual(4, expected.RankWeights.Single(row => row.Rank == "B").Value);
        Directory.Delete(fixture.Root, recursive: true);
        var replay = capture!.CreateReplayResolver().TryCreateContext(fixture.Character)!;
        Assert.IsTrue(replay.TryResolveCreationPrerequisiteAuthority(out var actual));
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    private static RetainedCharacterRuleSourceInputs Retain(Fixture fixture, Dictionary<string, byte[]> files)
        => new(fixture.Catalog.Value, fixture.Character, "private", ProfileId, [], files,
            files.ToDictionary(row => row.Key, _ => true, StringComparer.Ordinal), []);

    private static string Digest(byte[] bytes) => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string FramedDigest(string label, byte[] bytes)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            byte[] name = Encoding.UTF8.GetBytes(label);
            writer.Write((long)name.Length); writer.Write(name); writer.Write((long)bytes.Length); writer.Write(bytes);
        }
        return Digest(stream.ToArray());
    }
    private static void ReplaceSameLength(string path, string before, string after)
    {
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        string original = File.ReadAllText(path), changed = original.Replace(before, after, StringComparison.Ordinal);
        Assert.AreEqual(original.Length, changed.Length);
        Assert.AreNotEqual(original, changed);
        File.WriteAllText(path, changed); File.SetLastWriteTimeUtc(path, timestamp);
    }
    private static string Gear(int cost) => $"<chummer><gears><gear><id>{GearId}</id><name>Fixture Gear</name>"
        + $"<category>Tools</category><rating>0</rating><cost>{cost}</cost><avail>0</avail><source>SR5</source><page>1</page></gear></gears></chummer>";
    private static string GearAmend(int cost) => $"<chummer><gears><gear><id>{GearId}</id><cost amendoperation=\"replace\">{cost}</cost></gear></gears></chummer>";

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "chummer-rule-source-405-" + Guid.NewGuid().ToString("N"));
        internal CatalogService Catalog { get; }
        internal string Character { get; }
        internal string Data(string name) => Path.Combine(Root, "data", name);
        internal string Custom(string name, string file) => Path.Combine(Root, "customdata", name, file);
        internal FileSystemCharacterSourceDataResolver Resolver(bool weakIdentity = false)
            => new(Catalog, null, useStrongChangeIdentity: !weakIdentity);
        internal Fixture(bool selected = false)
        {
            Directory.CreateDirectory(Path.Combine(Root, "data"));
            Catalog = new(new(Data(string.Empty), string.Empty, []));
            string selection = selected ? string.Concat(new[] { "First", "Second" }.Select((name, index) =>
                $"<customdatadirectoryname><directoryname>{name}</directoryname><order>{index}</order><enabled>True</enabled></customdatadirectoryname>")) : string.Empty;
            Character = $"<character><settings>{ProfileId}</settings><buildmethod>Priority</buildmethod><created>False</created>"
                + (selected ? "<customdatadirectorynames><directoryname>First</directoryname><directoryname>Second</directoryname></customdatadirectorynames>" : string.Empty)
                + "</character>";
            File.WriteAllText(Data("settings.xml"), $"<chummer><settings><setting><id>{ProfileId}</id><buildmethod>Priority</buildmethod>"
                + "<buildpoints>25</buildpoints><priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable><sumtoten>10</sumtoten>"
                + "<books><book>SR5</book></books><nuyenformat>#,0.#</nuyenformat><availability>12</availability><nuyenperbpwftm>2000</nuyenperbpwftm>"
                + "<dronemods>False</dronemods>"
                + "<maxnumbermaxattributescreate>1</maxnumbermaxattributescreate><alternatemetatypeattributekarma>False</alternatemetatypeattributekarma>"
                + "<reverseattributepriorityorder>False</reverseattributepriorityorder><karmacost><karmaattribute>5</karmaattribute></karmacost>"
                + "<nuyenmaxbp>10</nuyenmaxbp><multiplyrestrictedcost>False</multiplyrestrictedcost><multiplyforbiddencost>False</multiplyforbiddencost>"
                + "<knowledgepointsexpression>({INTUnaug} + {LOGUnaug}) * 2</knowledgepointsexpression>"
                + $"<customdatadirectorynames>{selection}</customdatadirectorynames></setting></settings></chummer>");
            string attributes = string.Concat(new[] { "bod", "agi", "rea", "str", "cha", "int", "log", "wil", "mag", "res" }
                .Select(name => $"<{name}min>1</{name}min><{name}max>6</{name}max><{name}aug>10</{name}aug>"));
            File.WriteAllText(Data("metatypes.xml"), "<chummer><metatypes><metatype><id>a53d885d-a4a4-443d-b6a6-b0a55b0a96c7</id>"
                + "<name>Human</name><category>Metahuman</category><karma>0</karma>" + attributes
                + "<edgmin>2</edgmin><edgmax>7</edgmax><edgaug>7</edgaug><essmin>0</essmin><essmax>6</essmax><essaug>6</essaug>"
                + "<depmin>0</depmin><depmax>0</depmax><depaug>0</depaug><bonus/><source>SR5</source></metatype></metatypes></chummer>");
            string[] ranks = ["A", "B", "C", "D", "E"];
            string[] categories = ["Heritage", "Talent", "Attributes", "Skills", "Resources"];
            int number = 0;
            string rows = string.Concat(categories.SelectMany(category => ranks.Select((rank, index) =>
            {
                string body = category switch
                {
                    "Heritage" => "<metatypes><metatype><name>Human</name><value>1</value><karma>0</karma></metatype></metatypes>",
                    "Talent" => "<talents><talent><name>Mundane</name><value>Mundane</value></talent></talents>",
                    "Attributes" => $"<attributes>{new[] { 24, 20, 16, 14, 12 }[index]}</attributes>",
                    "Skills" => $"<skills>{new[] { 46, 36, 28, 22, 18 }[index]}</skills><skillgroups>0</skillgroups>",
                    _ => $"<resources>{new[] { 450000, 275000, 140000, 50000, 6000 }[index]}</resources>"
                };
                return $"<priority><id>00000000-0000-0000-0000-{++number:000000000000}</id><name>{category}-{rank}</name>"
                    + $"<value>{rank}</value><category>{category}</category>{body}</priority>";
            })));
            File.WriteAllText(Data("priorities.xml"), "<chummer><categories>" + string.Concat(categories.Select(category => $"<category>{category}</category>"))
                + "</categories><priortysumtotenvalues><A>4</A><B>3</B><C>2</C><D>1</D><E>0</E></priortysumtotenvalues>"
                + $"<priorities>{rows}</priorities></chummer>");
            File.WriteAllText(Data("skills.xml"), "<chummer><skills><skill><id>30000000-0000-0000-0000-000000000001</id>"
                + "<name>Running</name><attribute>AGI</attribute><category>Physical Active</category><default>True</default>"
                + "<skillgroup>Athletics</skillgroup><exotic>False</exotic><specs/><source>SR5</source></skill></skills>"
                + "<knowledgeskills><skill><id>40000000-0000-0000-0000-000000000001</id><name>English</name><attribute>INT</attribute>"
                + "<category>Language</category><default>False</default><skillgroup/><specs/><source>SR5</source></skill></knowledgeskills></chummer>");
            File.WriteAllText(Data("weapons.xml"), "<chummer><categories/><weapons/></chummer>");
            File.WriteAllText(Data("armor.xml"), "<chummer><categories/><armors/></chummer>");
            File.WriteAllText(Data("gear.xml"), Gear(10));
            if (selected)
                foreach (string name in new[] { "First", "Second", "Unselected" })
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Custom(name, "manifest.xml"))!);
                    File.WriteAllText(Custom(name, "manifest.xml"), $"<manifest><guid>{Guid.NewGuid():D}</guid><version>1.0</version></manifest>");
                }
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }

    private sealed class CatalogService(ContentOverlayCatalog catalog) : IContentOverlayCatalogService
    {
        internal ContentOverlayCatalog Value { get; set; } = catalog;
        internal Action<int>? OnRead { get; set; }
        private int _reads;
        public ContentOverlayCatalog GetCatalog() { OnRead?.Invoke(++_reads); return Value; }
        public IReadOnlyList<string> GetDataDirectories() => [Value.BaseDataPath];
        public IReadOnlyList<string> GetLanguageDirectories() => [];
        public string ResolveDataFile(string fileName) => Path.Combine(Value.BaseDataPath, fileName);
    }
}
