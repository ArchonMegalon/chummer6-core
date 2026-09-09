using System.Security.Cryptography;
using System.Text;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceContinuationSnapshotDigestTests
{
    [TestMethod]
    public void Digest_commits_to_state_not_an_empty_readonly_wrapper()
    {
        WorkspaceContinuationSnapshot original = Snapshot();
        string digest = WorkspaceContinuationSnapshotDigest.Compute(original);
        Assert.AreNotEqual(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("{}"))), digest);
        Assert.AreEqual(digest, WorkspaceContinuationSnapshotDigest.Compute(Snapshot()));
    }

    [TestMethod]
    public void Digest_binds_owner_document_identity_payload_both_revisions_and_checkpoint_time()
    {
        WorkspaceContinuationSnapshot original = Snapshot();
        WorkspaceDocumentSnapshot workspace = original.Workspace;
        WorkspaceContinuationSnapshot[] changes =
        [
            original with { OwnerId = "owner-b" },
            original with { Workspace = workspace with { Id = new("another-workspace") } },
            original with { Workspace = workspace with { ContentRevision = 8 } },
            original with { Workspace = workspace with { SavedRevision = 7 } },
            original with { Workspace = workspace with { LastUpdatedUtc = workspace.LastUpdatedUtc.AddTicks(1) } },
            original with { Workspace = workspace with
            {
                Document = workspace.Document with
                {
                    State = workspace.Document.State with { Payload = "<character><name>Changed</name></character>" }
                }
            } },
            original with { Workspace = workspace with { Document = workspace.Document with { Format = WorkspaceDocumentFormat.Json } } },
            original with { Workspace = workspace with
            {
                Document = workspace.Document with
                {
                    State = workspace.Document.State with { RulesetId = "sr6" }
                }
            } }
        ];
        string originalDigest = WorkspaceContinuationSnapshotDigest.Compute(original);
        foreach (WorkspaceContinuationSnapshot changed in changes)
            Assert.AreNotEqual(originalDigest, WorkspaceContinuationSnapshotDigest.Compute(changed));
        Assert.AreEqual(changes.Length,
            changes.Select(WorkspaceContinuationSnapshotDigest.Compute).Distinct(StringComparer.Ordinal).Count());
    }

    private static WorkspaceContinuationSnapshot Snapshot() => new("owner-a",
        new(new("continuation-digest-runner"),
            new WorkspaceDocument("<character><name>Original</name></character>", "sr5"),
            DateTimeOffset.Parse("2026-09-09T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), 7, 6), []);
}
