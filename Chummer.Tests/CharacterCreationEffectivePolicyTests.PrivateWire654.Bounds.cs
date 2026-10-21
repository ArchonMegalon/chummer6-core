using System.Text;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AllocationOwners654 = Chummer.Tests.CharacterCreationAttributesServiceTests.AllocationOwners;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    [DataRow("missing-source")]
    [DataRow("duplicate-source")]
    [DataRow("extra-source")]
    [DataRow("old-format")]
    [DataRow("missing-campaign")]
    [DataRow("extra-campaign")]
    [DataRow("changed-campaign")]
    [DataRow("oversized-length")]
    [DataRow("interrupted")]
    public void PrivateWire654_import_rejects_bad_closure_even_with_recomputed_transport_identity(string damage)
    {
        using var context = ReputationContext414(preserved: false);
        var owners = new AllocationOwners654();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var export = store.ExportPrivateRuleSourceClosure(owners, owners.Capture(), context.WorkspaceId, 4 * 1024 * 1024);
        byte[] original = export.ExportPrivateBytes(), record = File.ReadAllBytes(Record418(context));
        var parts = ReadWire654(original);
        if (damage == "missing-source") parts.Archives.Clear();
        if (damage == "duplicate-source") parts.Archives.Add(parts.Archives[0]);
        if (damage is "old-format" or "extra-source")
        {
            var inputs = CharacterCreationRuleSourceCaptureTests.Decode654(parts.Archives[0]);
            if (damage == "old-format") parts.Archives[0] = inputs.Build(portable: false).Export();
            else
            {
                string name = Path.Combine(inputs.Catalog.BaseDataPath, "unused-extra.xml");
                inputs.Files.Add(name, Encoding.UTF8.GetBytes("<chummer/>"));
                inputs.Existence.Add(name, true);
                parts.Archives.Add(inputs.Build(portable: true).Export());
            }
        }
        if (damage == "missing-campaign") parts.Campaigns.Clear();
        if (damage == "extra-campaign") parts.Campaigns.Add(parts.Campaigns[0]);
        if (damage == "changed-campaign") parts.Campaigns[0] = parts.Campaigns[0] with
        { Binding = parts.Campaigns[0].Binding with { TransitionDigest = Hash518("private654-other-campaign") } };
        byte[] bytes = WriteWire654(parts);
        if (damage == "oversized-length")
        {
            // The first length prefix must be rejected before allocating a string.
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, int.MaxValue);
        }
        if (damage == "interrupted") bytes = bytes[..^1];
        var expected = export.Binding with
        {
            ClosureDigest = RetainedCharacterRuleSourceInputs.Digest(bytes),
            ArchiveCount = parts.Archives.Count, ArchiveBytes = parts.Archives.Sum(b => (long)b.Length),
            CampaignCount = parts.Campaigns.Count
        };
        if (damage == "old-format")
            Assert.ThrowsExactly<WorkspacePrivateRuleSourceClosureLegacyFormatException>(() => WorkspacePrivateRuleSourceClosure.Import(
                bytes, expected, 4 * 1024 * 1024, context.Queries));
        else
            Assert.ThrowsExactly<InvalidOperationException>(() => WorkspacePrivateRuleSourceClosure.Import(
                bytes, expected, 4 * 1024 * 1024, context.Queries));
        CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));
        Assert.AreEqual(export.Binding, WorkspacePrivateRuleSourceClosure.Import(
            original, export.Binding, 4 * 1024 * 1024, context.Queries).Binding);
        Assert.IsTrue(store.Get(context.WorkspaceId).Success);
    }

    [TestMethod]
    public void PrivateWire654_identity_bounds_owner_cancellation_and_current_source_fences()
    {
        using var context = ReputationContext414(preserved: false);
        var owners = new AllocationOwners654();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var closure = store.ExportPrivateRuleSourceClosure(owners, owners.Capture(), context.WorkspaceId, 4 * 1024 * 1024);
        byte[] bytes = closure.ExportPrivateBytes(), record = File.ReadAllBytes(Record418(context));
        Assert.ThrowsExactly<InvalidOperationException>(() => WorkspacePrivateRuleSourceClosure.Import(bytes,
            closure.Binding with { ContentRevision = closure.Binding.ContentRevision + 1 }, bytes.Length, context.Queries));
        Assert.ThrowsExactly<InvalidOperationException>(() => WorkspacePrivateRuleSourceClosure.Import(bytes,
            closure.Binding, bytes.Length - 1, context.Queries));
        Assert.ThrowsExactly<InvalidOperationException>(() => store.ExportPrivateRuleSourceClosure(
            owners, owners.Capture(), context.WorkspaceId, 64));
        var imported = WorkspacePrivateRuleSourceClosure.Import(bytes, closure.Binding, bytes.Length, context.Queries);
        Array.Fill(bytes, (byte)0);
        var stamp = owners.Capture();
        bool called = false;
        using var cancel = new CancellationTokenSource();
        Assert.ThrowsExactly<OperationCanceledException>(() => imported.EvaluateCompletionInputs(
            owners, stamp, context.Resolver, context.Queries, LifeModules654(),
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2,
            (_, _, _) => { cancel.Cancel(); return true; }, cancel.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => imported.EvaluateCompletionInputs(
            owners, stamp, context.Resolver, context.Queries, LifeModules654(),
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2,
            (_, _, _) => { called = true; return true; }, cancel.Token));
        Assert.IsFalse(called);
        owners.Set(new OwnerScope("private654-other"));
        owners.Set(stamp.Owner);
        Assert.ThrowsExactly<InvalidOperationException>(() => imported.EvaluateCompletionInputs(
            owners, stamp, context.Resolver, context.Queries, LifeModules654(),
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2,
            (_, _, _) => { called = true; return true; }));
        Assert.IsFalse(called);
        Assert.ThrowsExactly<InvalidOperationException>(() => imported.EvaluateCompletionInputs(
            owners, owners.Capture(), context.Resolver, context.Queries, LifeModules654(),
            CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2,
            (_, _, _) => { Directory.Delete(Path.Combine(context.Directory, "source"), true); return true; }));
        Assert.AreEqual(0, owners.ActiveLeases);
        CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));
        // Cold observation custody is still recoverable; no current source is substituted.
        Assert.AreEqual(closure.Binding, WorkspacePrivateRuleSourceClosure.Import(
            closure.ExportPrivateBytes(), closure.Binding, 4 * 1024 * 1024, context.Queries).Binding);
    }

    [TestMethod]
    public void PrivateWire662_normalized_nonlocal_owner_can_observe_its_private_closure()
    {
        using var context = ReputationContext414(preserved: false);
        string directory = Path.Combine(context.Directory, "private662-normalized-owner");
        var (_, owners, id) = CharacterCreationCompletionApplyTests.CreateLinkedPrerequisite(context, directory);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(directory, context.Queries);
        var first = PrepareDraft550(context, store, owners, workspaceId: id);
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        owners.Set(new OwnerScope("  " + owners.Capture().Owner.NormalizedValue.ToUpperInvariant() + "  "));
        var exported = store.ExportPrivateRuleSourceClosure(owners, owners.Capture(), id, 4 * 1024 * 1024);
        var imported = WorkspacePrivateRuleSourceClosure.Import(exported.ExportPrivateBytes(), exported.Binding,
            4 * 1024 * 1024, context.Queries);
        string observedOwner = imported.EvaluateCompletionInputs(owners, owners.Capture(), context.Resolver,
            context.Queries, LifeModules654(), CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2,
            (fresh, sources, binding) =>
            {
                Assert.IsTrue(fresh.Success);
                Assert.IsNotNull(sources);
                Assert.AreEqual(1, owners.ActiveLeases);
                return binding.OwnerId;
            });
        Assert.AreEqual(exported.Binding.OwnerId, observedOwner);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    private sealed record WireParts654(byte[] Continuation, List<byte[]> Archives,
        List<CharacterCreationCommittedCampaign> Campaigns);
    private static WireParts654 ReadWire654(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var r = new BinaryReader(stream);
        byte[] Block() => r.ReadBytes(r.ReadInt32());
        Assert.AreEqual(WorkspacePrivateRuleSourceClosureBinding.SchemaV1, Encoding.UTF8.GetString(Block()));
        byte[] continuation = Block();
        int count = r.ReadInt32();
        var archives = new List<byte[]>();
        for (int i = 0; i < count; i++) { _ = Block(); archives.Add(Block()); }
        var campaigns = JsonSerializer.Deserialize<List<CharacterCreationCommittedCampaign>>(Block())!;
        return new(continuation, archives, campaigns);
    }
    private static byte[] WriteWire654(WireParts654 parts)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        void Block(byte[] bytes) { w.Write(bytes.Length); w.Write(bytes); }
        Block(Encoding.UTF8.GetBytes(WorkspacePrivateRuleSourceClosureBinding.SchemaV1));
        Block(parts.Continuation);
        w.Write(parts.Archives.Count);
        foreach (byte[] archive in parts.Archives.OrderBy(archive => RetainedCharacterRuleSourceInputs.Digest(archive), StringComparer.Ordinal))
        { Block(Encoding.UTF8.GetBytes(RetainedCharacterRuleSourceInputs.Digest(archive))); Block(archive); }
        Block(JsonSerializer.SerializeToUtf8Bytes(parts.Campaigns.OrderBy(c => c.ReceiptDigest, StringComparer.Ordinal).ToArray()));
        w.Flush();
        return stream.ToArray();
    }
}
