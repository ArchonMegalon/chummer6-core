using System.Text.Json.Nodes;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
    [DataRow("unknown-auxiliary")]
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
                case "unknown-auxiliary":
                    record["AuxiliaryState"] = new JsonObject { ["UnknownWizardHistory"] = "must not be silently dropped" };
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
}
