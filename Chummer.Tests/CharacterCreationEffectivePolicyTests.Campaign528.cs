using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow("RetainedIdentity", false)]
    [DataRow("RetainedIdentity", true)]
    [DataRow("Binding", false)]
    [DataRow("Binding", true)]
    public void Campaign528_malformed_committed_identity_returns_corrupt_read_without_breaking_List(
        string member, bool missing)
    {
        using var context = ReputationContext414(preserved: false);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var prepared = Prepare518(context, store);
        Finish518(context, store, prepared);
        Assert.IsTrue(store.Get(context.WorkspaceId).Success);
        Assert.IsTrue(store.ListProjected(value => value.Id).Contains(context.WorkspaceId));

        var record = JsonNode.Parse(File.ReadAllText(Record418(context)))!;
        var history = record["RetainedRuleSources"]!["CommittedCampaign"]!.AsObject();
        var proposal = record["AuxiliaryState"]!["CharacterCreationFinalizationArchive"]!
            ["CompletionAuthority"]!["Proposal"]!.AsObject();
        if (member == "Binding")
        {
            Damage(history, "Binding");
            Damage(proposal, "CampaignAuthority");
        }
        else
        {
            Damage(history["Binding"]!.AsObject(), member);
            Damage(proposal["CampaignAuthority"]!.AsObject(), member);
        }
        File.WriteAllText(Record418(context), record.ToJsonString());
        byte[] corrupted = File.ReadAllBytes(Record418(context));
        Assert.IsFalse(store.Get(context.WorkspaceId).Success);
        Assert.IsFalse(store.ListProjected(value => value.Id).Contains(context.WorkspaceId));
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        Assert.IsFalse(cold.Get(context.WorkspaceId).Success);
        Assert.IsFalse(cold.ListProjected(value => value.Id).Contains(context.WorkspaceId));
        CollectionAssert.AreEqual(corrupted, File.ReadAllBytes(Record418(context)));

        void Damage(JsonObject parent, string property)
        {
            if (missing) Assert.IsTrue(parent.Remove(property));
            else parent[property] = null;
        }
    }

    // Called by the existing TWO-process retained-store proof. No live source,
    // campaign service or in-memory authority survives into phase 2.
    internal static void PublishCommittedCampaignProcessFixture528(string root)
    {
        Assert.IsTrue(Path.IsPathFullyQualified(root));
        Assert.IsFalse(Directory.Exists(root));
        using var context = ReputationContext414(preserved: false);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var prepared = Prepare518(context, store);
        var receipt = Finish518(context, store, prepared);
        var saved = store.Get(context.WorkspaceId).Value!;
        var fixture = new CampaignProcess528(Environment.ProcessId,
            context.WorkspaceId, receipt, Hash518(saved.Document.Content));
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(Path.Combine(context.Directory, "campaign-process.json"), options))
        {
            JsonSerializer.Serialize(stream, fixture);
            stream.Flush(flushToDisk: true);
        }
        Directory.Move(context.Directory, root);
    }

    internal static void RecoverCommittedCampaignProcessFixture528(string root)
    {
        Assert.IsTrue(Path.IsPathFullyQualified(root));
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "source")));
        using var stream = new FileStream(Path.Combine(root, "campaign-process.json"), FileMode.Open, FileAccess.Read);
        Assert.IsTrue(stream.Length is > 0 and <= 65536);
        var fixture = JsonSerializer.Deserialize<CampaignProcess528>(stream)!;
        Assert.AreNotEqual(fixture.ProcessId, Environment.ProcessId);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(root,
            new XmlCharacterFileQueries(new CharacterFileService()));
        var read = store.Get(fixture.WorkspaceId);
        Assert.IsTrue(read.Success);
        var saved = read.Value!;
        Assert.AreEqual(fixture.XmlDigest, Hash518(saved.Document.Content));
        Assert.AreEqual(fixture.Receipt.ContentRevision, saved.ContentRevision);
        Assert.AreEqual(fixture.Receipt.SavedRevision, saved.SavedRevision);
        Assert.AreEqual(JsonSerializer.Serialize(fixture.Receipt), JsonSerializer.Serialize(
            saved.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!.Single().Receipt));
        Assert.IsTrue(store.ListProjected(value => value.Id).Contains(fixture.WorkspaceId));
    }

    private sealed record CampaignProcess528(int ProcessId, CharacterWorkspaceId WorkspaceId,
        CharacterCreationFinalizationReceipt Receipt, string XmlDigest);
}
