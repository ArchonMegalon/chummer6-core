using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestOwners = Chummer.Tests.CharacterCreationAttributesServiceTests.AllocationOwners;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceContinuationReadIntegrityTests
{
    [TestMethod]
    [DataRow("legacy-content")]
    [DataRow("legacy-xml")]
    [DataRow("legacy-ruleset")]
    [DataRow("missing-envelope")]
    [DataRow("normalized-ruleset")]
    [DataRow("defaulted-schema")]
    [DataRow("defaulted-kind")]
    [DataRow("auxiliary-typed-field-collision")]
    [DataRow("malformed-typed-auxiliary")]
    [DataRow("duplicate-envelope-field")]
    public void Export_rejects_ambiguous_or_normalization_dependent_current_records(string corruption)
    {
        string directory = Path.Combine(Path.GetTempPath(), "chummer-continuation-integrity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            FileWorkspaceStore store = new(directory);
            CharacterWorkspaceId id = new("integrity-runner");
            Assert.IsTrue(store.CreateWorkspaceDocument(id,
                new WorkspaceDocument("<character><name>Original</name></character>", "sr5")).Success);
            string path = Path.Combine(directory, "workspaces", id.Value + ".json");
            JsonObject record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            switch (corruption)
            {
                case "legacy-content": record["Content"] = "<character><name>Other legacy content</name></character>"; break;
                case "legacy-xml": record["Xml"] = "<character><name>Other legacy XML</name></character>"; break;
                case "legacy-ruleset": record["RulesetId"] = "sr6"; break;
                case "missing-envelope":
                    record.Remove("Envelope");
                    record["Content"] = "<character><name>Fallback</name></character>";
                    record["RulesetId"] = "sr5";
                    break;
                case "normalized-ruleset": record["Envelope"]!["RulesetId"] = " SR5 "; break;
                case "defaulted-schema": record["Envelope"]!["SchemaVersion"] = 0; break;
                case "defaulted-kind": record["Envelope"]!["PayloadKind"] = " "; break;
                case "auxiliary-typed-field-collision":
                    record["AuxiliaryState"] = new JsonObject { ["characterCreationSkillsReceipts"] = new JsonArray() };
                    break;
                case "malformed-typed-auxiliary":
                    record["AuxiliaryState"] = new JsonObject { ["CharacterCreationSkillsReceipts"] = new JsonObject() };
                    break;
            }
            string damaged = record.ToJsonString();
            if (corruption == "duplicate-envelope-field")
                damaged = damaged.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":1,\"SchemaVersion\":1", StringComparison.Ordinal);
            File.WriteAllText(path, damaged);
            byte[] before = File.ReadAllBytes(path);
            DateTime timestamp = File.GetLastWriteTimeUtc(path);

            CommandResult<WorkspaceContinuationSnapshot> result = store.ReadContinuation(id);

            Assert.IsFalse(result.Success, corruption);
            Assert.IsNull(result.Value, corruption);
            Assert.AreEqual(WorkspaceOperationOutcome.Corrupt, result.Outcome, corruption);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Unknown_auxiliary_data_is_preserved_by_read_export_and_codec_without_rewriting_the_record()
    {
        string directory = Path.Combine(Path.GetTempPath(), "chummer-continuation-opaque-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            FileWorkspaceStore store = new(directory);
            CharacterWorkspaceId id = new("opaque-runner");
            Assert.IsTrue(store.CreateWorkspaceDocument(id,
                new WorkspaceDocument("<character><name>Original</name></character>", "sr5")).Success);
            var baseline = store.ReadContinuation(id);
            Assert.IsTrue(baseline.Success, baseline.Error);
            Assert.IsNotNull(baseline.Value);
            JsonElement unknown = JsonSerializer.SerializeToElement(new
            {
                keep = "must not be silently dropped",
                values = new object[] { "Zoë 東京", 7, true }
            });
            var auxiliary = WorkspaceDocumentAuxiliaryState.Empty with
            {
                ExtensionData = new Dictionary<string, JsonElement> { ["UnknownWizardHistory"] = unknown }
            };
            WorkspaceContinuationSnapshot expected = baseline.Value with
            {
                Workspace = baseline.Value.Workspace with
                {
                    Document = baseline.Value.Workspace.Document with
                    {
                        State = baseline.Value.Workspace.Document.State with { AuxiliaryState = auxiliary }
                    }
                }
            };
            string path = Path.Combine(directory, "workspaces", id.Value + ".json");
            JsonObject record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            // Model a future extension in this test-owned record, not a typed ledger or an authority grant.
            record["AuxiliaryState"] = new JsonObject { ["UnknownWizardHistory"] = JsonNode.Parse(unknown.GetRawText()) };
            File.WriteAllText(path, record.ToJsonString());
            byte[] before = File.ReadAllBytes(path);
            DateTime timestamp = File.GetLastWriteTimeUtc(path);
            expected = expected with
            {
                Workspace = expected.Workspace with { LastUpdatedUtc = new DateTimeOffset(timestamp) }
            };
            var baselineAtObservedTime = baseline.Value with
            {
                Workspace = baseline.Value.Workspace with { LastUpdatedUtc = new DateTimeOffset(timestamp) }
            };

            var read = new FileWorkspaceStore(directory).ReadContinuation(id);
            Assert.IsTrue(read.Success, read.Error);
            Assert.AreEqual(WorkspaceOperationOutcome.Success, read.Outcome);
            Assert.IsNotNull(read.Value);
            AssertPreserved(read.Value);

            TestOwners owners = new();
            var exported = new WorkspaceContinuationExportService(new FileWorkspaceStore(directory), owners)
                .Export(owners.Capture(), id);
            Assert.IsTrue(exported.Success, exported.Error);
            Assert.AreEqual(WorkspaceOperationOutcome.Success, exported.Outcome);
            Assert.IsNotNull(exported.Value);
            Assert.AreEqual(0, owners.ActiveLeases);
            Assert.AreEqual(WorkspaceContinuationSnapshotDigest.Compute(expected), exported.Value.SnapshotDigest);
            Assert.AreNotEqual(WorkspaceContinuationSnapshotDigest.Compute(baselineAtObservedTime), exported.Value.SnapshotDigest);
            AssertPreserved(exported.Value.Snapshot);

            // Codec success proves transport fidelity only; the result is an untrusted candidate.
            byte[] encoded = WorkspaceContinuationCodec.Encode(exported.Value, 64 * 1024);
            Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(encoded, encoded.Length, out var candidate));
            Assert.IsNotNull(candidate);
            Assert.AreEqual(exported.Value.SnapshotDigest, candidate.SnapshotDigest);
            AssertPreserved(candidate.Snapshot);
            CollectionAssert.AreEqual(encoded, WorkspaceContinuationCodec.Encode(candidate, encoded.Length));

            void AssertPreserved(WorkspaceContinuationSnapshot actual)
            {
                Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
                var state = actual.Workspace.Document.AuxiliaryState;
                Assert.IsFalse(state.IsEmpty);
                Assert.IsNotNull(state.ExtensionData);
                Assert.HasCount(1, state.ExtensionData);
                Assert.IsTrue(state.ExtensionData.TryGetValue("UnknownWizardHistory", out JsonElement retained));
                Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(unknown.GetRawText()), JsonNode.Parse(retained.GetRawText())));
                CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
                Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(path));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
