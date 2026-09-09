using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

/// <summary>
/// Private-store fixture injection, NOT a production restore path. Tests begin
/// with real locally committed history and mark it as an imported prefix.
/// </summary>
internal static class WorkspaceImportedHistoryTestFixture
{
    internal static WorkspaceLocalHistory MarkImported(string directory, CharacterWorkspaceId id, OwnerScope? owner = null)
    {
        FileWorkspaceStore store = new(directory);
        var read = owner is { } scope ? store.Get(scope, id) : store.Get(id);
        var snapshot = owner is { } currentOwner ? store.ReadContinuation(currentOwner, id) : store.ReadContinuation(id);
        Assert.IsTrue(read.Success, read.Error);
        Assert.IsTrue(snapshot.Success, snapshot.Error);
        var current = read.Value!;
        var history = new WorkspaceLocalHistory(current.LocalHistory!.IncarnationId, current.ContentRevision,
            WorkspaceContinuationSnapshotDigest.Compute(snapshot.Value!));
        string path = Directory.GetFiles(directory, id.Value + ".json", SearchOption.AllDirectories).Single();
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        JsonObject record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        record["LocalHistory"] = JsonSerializer.SerializeToNode(history);
        File.WriteAllText(path, record.ToJsonString());
        File.SetLastWriteTimeUtc(path, timestamp);
        var after = owner is { } afterOwner ? new FileWorkspaceStore(directory).ReadContinuation(afterOwner, id)
            : new FileWorkspaceStore(directory).ReadContinuation(id);
        Assert.IsTrue(after.Success, after.Error);
        Assert.AreEqual(JsonSerializer.Serialize(snapshot.Value), JsonSerializer.Serialize(after.Value));
        return history;
    }
}
