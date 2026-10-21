using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using K = Chummer.Contracts.Characters.CharacterCreationCompleteObligationKind;
using C = Chummer.Contracts.Characters.CharacterCreationCompleteCoverage;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    // PHASE 2 ONLY, after graph integration. Read-only: no Capture, encoding,
    // witness digest recomputation, current source resolution, or fixture writing.
    [TestMethod]
    public void Complete352_History_phase2_replay_exact_pre_graph_V2_bytes()
    {
        string? path = Environment.GetEnvironmentVariable("CHUMMER_GRAPH352_REPLAY_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            Assert.Inconclusive("Controller must supply the phase-1 fixture; never generate it on the graph implementation.");
            return;
        }
        byte[] manifestBytes = File.ReadAllBytes(Path.Combine(path, "sha256.json"));
        var hashes = JsonSerializer.Deserialize<SortedDictionary<string, string>>(manifestBytes)!;
        var originalOptions = new JsonSerializerOptions();
        originalOptions.Converters.Add(new HistoricalWorkspaceDocumentReader352());
        var original = JsonSerializer.Deserialize<WorkspaceStoredDocument>(Bytes("original.json"), originalOptions)!;
        Assert.AreEqual(Bytes("original.json"), JsonSerializer.Serialize(original));
        var inputs = Read<CharacterCreationCompletionReviewInputs>("inputs.json");
        var request = Read<CharacterCreationCompletionReviewRequest>("request.json");
        var archive = Read<CharacterCreationCompletionReplayArchive>("archive.json");
        Assert.AreEqual(CharacterCreationCompleteAssessment.SchemaV2, request.CompleteAssessmentSchema);
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(archive, out var bootstrap, out var witness));
        Assert.AreEqual(Bytes("bootstrap.json"), JsonSerializer.Serialize(bootstrap));
        Assert.AreEqual(Bytes("witness.json"), JsonSerializer.Serialize(witness));
        Assert.AreEqual(Bytes("assessment.json"), JsonSerializer.Serialize(witness.CompleteAssessment));
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(Bytes("review.json"), JsonSerializer.Serialize(full));
        var replay = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, witness, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(Bytes("replay.json"), JsonSerializer.Serialize(replay));
        Assert.AreEqual(Bytes("assessment.json"), JsonSerializer.Serialize(replay.Value!.CompleteAssessment));
        var graph = replay.Value.CompleteAssessment!.Obligations.Where(row => row.Kind == K.ActiveGraph).ToArray();
        Assert.AreEqual(8, graph.Length);
        Assert.IsTrue(graph.All(row => row.Coverage == C.Unresolved && row.EvaluationDigest is null));
        Assert.AreEqual(Bytes("original.json"), JsonSerializer.Serialize(original));
        Assert.AreEqual(Bytes("request.json"), JsonSerializer.Serialize(request));
        Assert.AreEqual(Bytes("witness.json"), JsonSerializer.Serialize(witness));
        foreach (string name in hashes.Keys) _ = Bytes(name);
        CollectionAssert.AreEqual(manifestBytes, File.ReadAllBytes(Path.Combine(path, "sha256.json")));

        T Read<T>(string name) => JsonSerializer.Deserialize<T>(Bytes(name))!;
        string Bytes(string name)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(path, name));
            Assert.AreEqual(hashes[name], Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), name);
            return Encoding.UTF8.GetString(bytes);
        }
    }

    // Test-only materialization of the original serialized state. WorkspaceDocument has
    // several public constructors; this does not change its production serialization contract.
    private sealed class HistoricalWorkspaceDocumentReader352 : JsonConverter<WorkspaceDocument>
    {
        public override WorkspaceDocument Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var json = JsonDocument.ParseValue(ref reader);
            var root = json.RootElement;
            var state = root.GetProperty("State");
            var restored = new WorkspaceDocumentState(state.GetProperty("RulesetId").GetString()!,
                state.GetProperty("SchemaVersion").GetInt32(), state.GetProperty("PayloadKind").GetString()!,
                state.GetProperty("Payload").GetString()!)
            {
                AuxiliaryState = state.GetProperty("AuxiliaryState").Deserialize<WorkspaceDocumentAuxiliaryState>(options)!
            };
            return new(restored, (WorkspaceDocumentFormat)root.GetProperty("Format").GetInt32());
        }

        public override void Write(Utf8JsonWriter writer, WorkspaceDocument value, JsonSerializerOptions options)
            => throw new NotSupportedException("Historical fixture reader must not write or reseal fixtures.");
    }
}
