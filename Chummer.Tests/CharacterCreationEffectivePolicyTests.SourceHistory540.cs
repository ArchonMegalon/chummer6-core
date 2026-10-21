using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    // Opt-in, private test transport. No authored expected result is a baseline.
    // Run capture BEFORE source525+532+535, then replay in a new test host AFTER.
    [TestMethod]
    public void SourceHistory540_Capture_pre_purchase_V1()
    {
        string root = Required540("CAPTURE");
        Assert.IsTrue(Path.IsPathFullyQualified(root));
        Assert.IsFalse(Directory.Exists(root), "Never replace historical evidence.");
        Assert.IsNull(typeof(CharacterCreationCompletionReviewSchemas).GetField("RuleSourceExecutionV2"),
            "Capture requires the pre-integration implementation, not a V1 run on V2 code.");
        byte[] sourceManifest = File.ReadAllBytes(Required540("SOURCE_MANIFEST"));
        Assert.AreEqual(Required540("SOURCE_DIGEST"), Hash540(sourceManifest), "Pinned implementation identity.");
        string sourceRoot = Required540("SOURCE_ROOT");
        VerifyImplementation540(sourceManifest, sourceRoot);

        using var context = ReadyContext.Create(true, includeNonEmptyPurchases: true, includeSkillPurchase: true,
            qualityName: "Ambidextrous", prepareSource: _ => { });
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var prepared = Prepare410(context);
        var request = OriginalRequest396(prepared.Inputs) with
        {
            RuleSourceExecutionSchema = CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1,
            RuleSourceCaptureDigest = prepared.Inputs.RuleSourceCaptureDigest
        };
        var full = CharacterCreationCompletionReviewService.Review(prepared.Inputs, request, ruleSources: prepared.Execution);
        Success540(full);
        Assert.IsTrue(full.Value!.Balances.GearCost > 0m);
        Assert.IsTrue(full.Value.UpstreamBudget!.Qualities.Qualities.Selections
            .Single(row => row.Name == "Ambidextrous").KarmaCost > 0);
        Assert.IsTrue(prepared.Inputs.Lifestyles.Gear.State.Authority.Options.Count > 100);
        Assert.IsTrue(prepared.Inputs.Lifestyles.Qualities.Authority.Options.Count > 100);
        Assert.IsFalse(full.Value.CompleteAssessment!.SatisfiesComplete);

        var bootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot
            .Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var captured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, prepared.Inputs,
            request, ruleSources: prepared.Execution);
        Success540(captured);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryEncode(bootstrap, captured.Value!, out var archive));
        byte[] sources = prepared.Execution.ExportPrivateInputs();
        // From here even capture uses the retained resolver, with no live XML tree.
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var bytes = Evaluate540(original, prepared.Inputs, request, archive!, sources);
        Equal540(JsonSerializer.SerializeToUtf8Bytes(full), bytes["full-review.json"], "live/retained full review");
        Equal540(JsonSerializer.SerializeToUtf8Bytes(captured.Value), bytes["witness.json"], "live/retained witness");
        Equal540(JsonSerializer.SerializeToUtf8Bytes(original),
            JsonSerializer.SerializeToUtf8Bytes(context.Store.Get(context.WorkspaceId).Value), "original unchanged");
        bytes.Add("implementation.json", sourceManifest);
        bytes.Add("process.json", JsonSerializer.SerializeToUtf8Bytes(new SourceHistoryProcess540(
            Process540(), Hash540(sourceManifest))));
        VerifyImplementation540(sourceManifest, sourceRoot);

        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(root);
        else Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in bytes)
        {
            Write540(Path.Combine(root, row.Key), row.Value);
            hashes.Add(row.Key, Hash540(row.Value));
        }
        // Last file is the completion marker. Its hash must be pinned OUTSIDE this directory.
        Write540(Path.Combine(root, "sha256.json"), JsonSerializer.SerializeToUtf8Bytes(hashes));
    }

    [TestMethod]
    public void SourceHistory540_Replay_exact_pre_purchase_V1()
    {
        string root = Required540("REPLAY");
        Assert.IsNotNull(typeof(CharacterCreationCompletionReviewSchemas).GetField("RuleSourceExecutionV2"),
            "Compatibility replay must run after integration, not twice on pre-change code.");
        var baseline = Read540(root, Required540("BASELINE_DIGEST"));
        var process = JsonSerializer.Deserialize<SourceHistoryProcess540>(baseline["process.json"])!;
        Assert.AreNotEqual(process.ProcessIdentity, Process540(), "Use a fresh test host.");
        Assert.AreEqual(Required540("SOURCE_DIGEST"), process.ImplementationDigest);
        Assert.AreEqual(process.ImplementationDigest, Hash540(baseline["implementation.json"]));
        var original = Original540(baseline["original.json"]);
        var inputs = JsonSerializer.Deserialize<CharacterCreationCompletionReviewInputs>(baseline["inputs.json"])!;
        var request = JsonSerializer.Deserialize<CharacterCreationCompletionReviewRequest>(baseline["request.json"])!;
        var archive = JsonSerializer.Deserialize<CharacterCreationCompletionReplayArchive>(baseline["archive.json"])!;
        var actual = Evaluate540(original, inputs, request, archive, baseline["sources.bin"]);
        foreach (var row in actual)
            Equal540(baseline[row.Key], row.Value, row.Key);
        // Replay never writes, seals or replaces baseline bytes.
        foreach (var row in baseline)
            Equal540(row.Value, File.ReadAllBytes(Path.Combine(root, row.Key)), row.Key + " unchanged");
        Assert.AreEqual(Required540("BASELINE_DIGEST"), Hash540(File.ReadAllBytes(Path.Combine(root, "sha256.json"))));
    }

    [TestMethod]
    public void SourceHistory540_Reject_mismatched_baseline_and_source_identity()
    {
        string root = Required540("REPLAY");
        var baseline = Read540(root, Required540("BASELINE_DIGEST"));
        // A different REAL payload identity must not be accepted as the manifest identity.
        string wrongManifestIdentity = Hash540(baseline["original.json"]);
        Assert.AreNotEqual(Required540("BASELINE_DIGEST"), wrongManifestIdentity);
        Assert.ThrowsExactly<AssertFailedException>(() => Read540(root, wrongManifestIdentity));
        var inputs = JsonSerializer.Deserialize<CharacterCreationCompletionReviewInputs>(baseline["inputs.json"])!;
        var request = JsonSerializer.Deserialize<CharacterCreationCompletionReviewRequest>(baseline["request.json"])!;
        string otherIdentity = CharacterCreationFinalizationDigest.ComputeUtf8(
            Encoding.UTF8.GetString(baseline["original.json"]));
        Assert.AreNotEqual(request.RuleSourceCaptureDigest, otherIdentity);
        Assert.IsFalse(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            baseline["sources.bin"], otherIdentity, out _));
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            baseline["sources.bin"], request.RuleSourceCaptureDigest!, out var capture));
        var execution = new CharacterCreationRuleSourceExecutionContext(capture!,
            new XmlCharacterFileQueries(new CharacterFileService()));
        var mismatched = request with { RuleSourceCaptureDigest = otherIdentity };
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(inputs, mismatched, ruleSources: execution).Value);
        var archive = JsonSerializer.Deserialize<CharacterCreationCompletionReplayArchive>(baseline["archive.json"])!;
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var bootstrap, out var witness));
        Assert.IsNull(CharacterCreationSelectedCompletionEvaluator.Replay(Original540(baseline["original.json"]),
            bootstrap, witness, mismatched, ruleSources: execution).Value);
    }

    // Common execution only. Capture writes its returned bytes once; replay compares
    // them against the externally pinned, pre-change bytes and never updates them.
    private static SortedDictionary<string, byte[]> Evaluate540(WorkspaceStoredDocument original,
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request,
        CharacterCreationCompletionReplayArchive archive, byte[] sources)
    {
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1, request.RuleSourceExecutionSchema);
        Assert.AreEqual(request.RuleSourceExecutionSchema, inputs.RuleSourceExecutionSchema);
        Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(
            sources, request.RuleSourceCaptureDigest!, out var capture));
        var resolver = capture!.CreateReplayResolver();
        var queries = new XmlCharacterFileQueries(new CharacterFileService());
        var execution = new CharacterCreationRuleSourceExecutionContext(capture, queries);
        // Rebuild authority from the original workspace and retained XML, rather
        // than merely evaluating old serialized authority DTOs on new code.
        var loaded = execution.Resolve(original, request, default);
        Assert.IsNotNull(loaded, "Canonical retained-source input reconstruction.");
        Equal540(JsonSerializer.SerializeToUtf8Bytes(inputs), JsonSerializer.SerializeToUtf8Bytes(loaded.Inputs),
            "reconstructed full inputs");
        inputs = loaded.Inputs;
        var full = CharacterCreationCompletionReviewService.Review(inputs, request, ruleSources: execution);
        Success540(full);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var bootstrap, out var witness));
        var cold = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, witness, request,
            ruleSources: execution);
        Success540(cold);
        // Current capture is compared with the historical witness; it never replaces it.
        var recaptured = CharacterCreationSelectedCompletionEvaluator.Capture(original, bootstrap, inputs, request,
            ruleSources: execution);
        Success540(recaptured);
        Equal540(JsonSerializer.SerializeToUtf8Bytes(witness), JsonSerializer.SerializeToUtf8Bytes(recaptured.Value),
            "selected witness identity");
        var source = resolver.TryCreateContext(original.Document.Content)!;
        CharacterCreationStartingNuyenSource? cash;
        if (request.Selections.StartingLifestyleId is Guid selected)
        {
            var home = request.Selections.Lifestyles.Single(row => row.LifestyleId == selected);
            var option = inputs.Lifestyles.Authority.LifestyleOptions.Single(row => row.OptionId == home.BaseLifestyleOptionId);
            Assert.IsTrue(source.TryResolveCreationPriorityStartingNuyen(option.SourceId, out cash));
        }
        else Assert.IsTrue(source.TryResolveCreationDefaultStartingNuyen(out cash));
        Assert.IsNotNull(cash);
        var choice = new CharacterCreationStartingCashChoice(cash.AuthorityDigest, cash.Dice * 3);
        var fullOutput = CharacterCreationCompletionFinalizationProjector.Project(original, bootstrap, inputs,
            full.Value!, cash, choice, ruleSources: execution);
        var coldOutput = CharacterCreationCompletionTransaction.ProjectSelected(original, bootstrap, witness,
            request, cash, choice, ruleSources: execution);
        Success540(fullOutput);
        Success540(coldOutput);
        Equal540(JsonSerializer.SerializeToUtf8Bytes(fullOutput), JsonSerializer.SerializeToUtf8Bytes(coldOutput),
            "full/cold canonical projection");
        Equal540(JsonSerializer.SerializeToUtf8Bytes(full.Value!.CompleteAssessment),
            JsonSerializer.SerializeToUtf8Bytes(cold.Value!.CompleteAssessment), "full/cold assessment");

        var result = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        Save("original.json", original);
        Save("inputs.json", inputs);
        Save("request.json", request);
        Save("full-authority.json", CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request));
        Save("full-review.json", full);
        Save("bootstrap.json", bootstrap);
        Save("witness.json", witness);
        Save("archive.json", archive);
        Save("cold-evaluation.json", cold);
        Save("cash-source.json", cash);
        Save("cash-choice.json", choice);
        Save("full-projection.json", fullOutput);
        Save("cold-projection.json", coldOutput);
        result.Add("output.xml", Encoding.UTF8.GetBytes(fullOutput.Value!.CharacterXml));
        result.Add("sources.bin", sources);
        return result;
        void Save<T>(string name, T value) => result.Add(name, JsonSerializer.SerializeToUtf8Bytes(value));
    }

    private static SortedDictionary<string, byte[]> Read540(string root, string expectedManifestDigest)
    {
        Assert.IsTrue(Path.IsPathFullyQualified(root));
        byte[] manifest = File.ReadAllBytes(Path.Combine(root, "sha256.json"));
        Assert.AreEqual(expectedManifestDigest, Hash540(manifest), "Pinned baseline identity mismatch.");
        var result = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var row in JsonSerializer.Deserialize<SortedDictionary<string, string>>(manifest)!)
        {
            Assert.AreEqual(Path.GetFileName(row.Key), row.Key);
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, row.Key));
            Assert.AreEqual(row.Value, Hash540(bytes), row.Key);
            result.Add(row.Key, bytes);
        }
        return result;
    }

    private static WorkspaceStoredDocument Original540(byte[] bytes)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new SourceHistoryDocumentReader540());
        var original = JsonSerializer.Deserialize<WorkspaceStoredDocument>(bytes, options)!;
        Equal540(bytes, JsonSerializer.SerializeToUtf8Bytes(original), "original JSON roundtrip");
        return original;
    }

    // Same test-only materialization as GraphHistoryReplay; no production converter change.
    private sealed class SourceHistoryDocumentReader540 : JsonConverter<WorkspaceDocument>
    {
        public override WorkspaceDocument Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            using var json = JsonDocument.ParseValue(ref reader);
            var root = json.RootElement;
            var state = root.GetProperty("State");
            return new(new WorkspaceDocumentState(state.GetProperty("RulesetId").GetString()!,
                state.GetProperty("SchemaVersion").GetInt32(), state.GetProperty("PayloadKind").GetString()!,
                state.GetProperty("Payload").GetString()!)
            {
                AuxiliaryState = state.GetProperty("AuxiliaryState").Deserialize<WorkspaceDocumentAuxiliaryState>(options)!
            }, (WorkspaceDocumentFormat)root.GetProperty("Format").GetInt32());
        }
        public override void Write(Utf8JsonWriter writer, WorkspaceDocument value, JsonSerializerOptions options)
            => throw new NotSupportedException("Read-only historical fixture converter.");
    }

    private static void Success540<T>(CharacterCreationFoundationResult<T> result) where T : class
    {
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.IsNotNull(result.Value);
    }

    private static void Equal540(byte[] expected, byte[] actual, string name)
    {
        // Avoid printing private XML/JSON payloads in test failure messages.
        Assert.AreEqual(Hash540(expected), Hash540(actual), name + " SHA-256");
        Assert.IsTrue(expected.AsSpan().SequenceEqual(actual), name + " exact bytes");
    }

    private static string Required540(string suffix)
    {
        string? value = Environment.GetEnvironmentVariable("CHUMMER_V1_HISTORY540_" + suffix);
        if (string.IsNullOrWhiteSpace(value))
            Assert.Inconclusive("Controller must explicitly supply private history540 " + suffix + ".");
        return value!;
    }

    private static string Hash540(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void VerifyImplementation540(byte[] manifest, string sourceRoot)
    {
        foreach (var row in JsonSerializer.Deserialize<SortedDictionary<string, string>>(manifest)!)
            Assert.AreEqual(row.Value, Hash540(File.ReadAllBytes(Path.Combine(sourceRoot, row.Key))), row.Key);
    }

    private static string Process540()
    {
        using var process = Process.GetCurrentProcess();
        return Environment.ProcessId + ":" + process.StartTime.ToUniversalTime().Ticks;
    }

    private static void Write540(string path, byte[] bytes)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(path, options);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    public sealed record SourceHistoryProcess540(string ProcessIdentity, string ImplementationDigest);
}
