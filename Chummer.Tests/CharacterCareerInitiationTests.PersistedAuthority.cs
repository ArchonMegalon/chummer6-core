using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCareerInitiationTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Delegated_history_without_campaign_XML_refuses_fresh_initiation_and_preserves_receipts(bool committed)
    {
        using var f = new Fixture();
        var owner = new OwnerScope("initiation609-character-owner");
        var document = f.Saved().Document;
        f.Owners.Set(owner);
        Assert.IsTrue(f.Store.CreateWorkspaceDocument(owner, f.Id, document).Success);
        Assert.IsTrue(f.Store.SaveCheckpoint(owner, f.Id, 1).Success);
        var selection = new CharacterCareerInitiationSelection(Centering, AcceptObligations: true);
        var quote = f.Service.Read(f.Owners.Capture(), f.Id, selection);
        Assert.IsNotNull(quote);
        Assert.IsTrue(quote!.CanApply);
        var original = new CharacterCareerInitiationPlan(Guid.NewGuid(), quote, Date);
        if (committed)
            Assert.AreEqual(CharacterCareerInitiationOutcome.Applied,
                f.Service.Commit(f.Owners.Capture(), original).Outcome);
        var beforeHistory = f.Store.Get(owner, f.Id).Value!;
        // Capture a real fresh quote before history arrives; the store must recheck under its lease.
        var freshQuote = f.Service.Read(f.Owners.Capture(), f.Id,
            new(Masking, AcceptObligations: true));
        Assert.IsNotNull(freshQuote);
        Assert.IsTrue(freshQuote!.CanApply);
        var fresh = new CharacterCareerInitiationPlan(Guid.NewGuid(), freshQuote, Date);
        AppendDelegatedHistory609(f.Store, owner, beforeHistory);

        var cold = new FileWorkspaceStore(f.StorePath);
        var saved = cold.Get(owner, f.Id);
        Assert.IsTrue(saved.Success, saved.Error);
        Assert.IsNull(saved.Value!.Document.AuxiliaryState.CharacterCreationFinalizationArchive);
        string recordPath = Directory.GetFiles(f.StorePath, f.Id.Value + ".json", SearchOption.AllDirectories)
            .Single(path => JsonNode.Parse(File.ReadAllText(path))!["DelegatedGmCharacterEdits"] is JsonArray { Count: > 0 });
        AssertPersistedAuthorityRefusal609(cold, f.Owners, f.Authority, saved.Value, fresh, recordPath);
        // Source evaluation alone cannot see the private delegated ledger. Even an exact,
        // current source-backed command must pass the store's persisted-authority check.
        using var currentSources = f.Authority.Open(f.Owners.Capture(), saved.Value,
            fresh.Quote.Selection, cold.CareerInitiationRuntimeFingerprint);
        Assert.IsNotNull(currentSources);
        Assert.IsTrue(currentSources!.Quote.CanApply);
        AssertPersistedAuthorityRefusal609(cold, f.Owners, f.Authority, saved.Value,
            new(Guid.NewGuid(), currentSources.Quote, Date), recordPath);
        if (committed)
            AssertOriginalReceipt609(cold, f.Owners, f.Authority, saved.Value, original, recordPath);
    }

    private static void AppendDelegatedHistory609(FileWorkspaceStore store, OwnerScope owner,
        WorkspaceStoredDocument saved)
    {
        const string notes = "Persisted delegated edit; no campaign XML.";
        string key = Sha609("delegated-key"), command = Sha609("delegated-command");
        const string delegation = "delegation609", authorityReceipt = "authority609";
        string receiptId = "gm-edit-" + Sha609(string.Join("\n", command, delegation, authorityReceipt, key))[..24];
        var receipt = new DelegatedGmCharacterEditAuditReceipt(
            DelegatedGmCharacterEditContract.Name, receiptId, "campaign609", delegation,
            "campaign609-owner", owner.NormalizedValue, authorityReceipt, 1,
            "campaign609-gm", DelegatedGmCharacterEditContract.GameMasterRole,
            owner.NormalizedValue, saved.Id, "Seed persisted history through the real store writer.",
            key, command, saved.ContentRevision, saved.ContentRevision + 1,
            new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero),
            ImmutableArray.Create(new DelegatedGmCharacterEditAuditOperation(
                DelegatedGmCharacterPatchOperationKind.Replace,
                DelegatedGmCharacterEditContract.ProfileNotesPath, Sha609(notes), notes.Length)));
        var entry = new DelegatedGmCharacterEditLedgerEntry(key, command, receipt);
        Assert.IsTrue(DelegatedGmCharacterEditLedgerValidator.IsValidForCommit(
            owner, saved.Id, saved.ContentRevision, entry));
        var xml = XElement.Parse(saved.Document.Content);
        xml.SetElementValue("notes", notes);
        var replacement = saved.Document with { State = saved.Document.State with
            { Payload = xml.ToString(SaveOptions.DisableFormatting) } };
        Assert.AreEqual(DelegatedGmCharacterEditStoreOutcome.Applied,
            store.ApplyDelegatedGmCharacterEdit(owner, saved.Id, saved.ContentRevision, replacement, entry).Outcome);
        Assert.IsTrue(store.SaveCheckpoint(owner, saved.Id, saved.ContentRevision + 1).Success);
    }

    internal static void AssertArchiveAuthority609(FileWorkspaceStore store, CharacterWorkspaceId id,
        CharacterCreationAttributesServiceTests.AllocationOwners owners, string recordPath, bool committed,
        Func<FileWorkspaceStore> reopen)
    {
        using var f = new Fixture();
        var saved = store.Get(id).Value!;
        Assert.IsNotNull(saved.Document.AuxiliaryState.CharacterCreationFinalizationArchive?
            .CompletionAuthority?.Proposal.CampaignAuthority);
        // Retain the completed native skill identities required by V4 archive validation.
        var xml = f.Xml();
        var skills = XElement.Parse(saved.Document.Content).Element("newskills");
        Assert.IsNotNull(skills);
        xml.Add(new XElement(skills!));
        Assert.IsTrue(store.ReplaceWorkspaceDocumentAndCheckpoint(id, saved.ContentRevision,
            saved.Document with { State = saved.Document.State with
                { Payload = xml.ToString(SaveOptions.DisableFormatting) } }).Success);
        saved = store.Get(id).Value!;
        AssertNoCampaignXml609(saved);
        Assert.IsNull(JsonNode.Parse(File.ReadAllText(recordPath))!["DelegatedGmCharacterEdits"]);

        // Obtain the historical source-backed quote without treating archived campaign history
        // as live authority. No test admission/price resolver is substituted.
        var historical = saved with { Document = saved.Document with { State = saved.Document.State with
            { AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCreationFinalizationArchive = null } } } };
        using var admission = f.Authority.Open(owners.Capture(), historical,
            new(Centering, AcceptObligations: true), store.CareerInitiationRuntimeFingerprint);
        Assert.IsNotNull(admission);
        Assert.IsTrue(admission!.Quote.CanApply);
        var original = new CharacterCareerInitiationPlan(Guid.NewGuid(), admission.Quote, Date);

        if (committed)
        {
            // Materialize a pre609 historical receipt with the real transaction builder, then restore
            // the separately validated completion archive. Cold read must validate all persisted
            // history normally; this does not bypass the new fresh-operation store admission.
            Assert.IsTrue(CharacterCareerInitiationTransaction.TryBuild(owners.Capture(), historical,
                original, admission, store.CareerInitiationRuntimeFingerprint, out var replacement, out _));
            replacement = replacement! with { State = replacement!.State with
                { AuxiliaryState = replacement.AuxiliaryState with
                    { CharacterCreationFinalizationArchive = saved.Document.AuxiliaryState.CharacterCreationFinalizationArchive } } };
            var record = JsonNode.Parse(File.ReadAllText(recordPath))!;
            record["Envelope"] = JsonSerializer.SerializeToNode(replacement.PayloadEnvelope);
            record["AuxiliaryState"] = JsonSerializer.SerializeToNode(replacement.AuxiliaryState);
            record["ContentRevision"] = original.TargetWorkspaceRevision;
            record["SavedRevision"] = original.TargetSavedRevision;
            File.WriteAllText(recordPath, record.ToJsonString());
            store = reopen();
            var reopened = store.Get(id);
            Assert.IsTrue(reopened.Success, reopened.Error);
            saved = reopened.Value!;
        }

        // A structurally valid fresh command with the exact current revision/content/auxiliary
        // binding must be refused because of metadata, rather than an incidental stale quote.
        historical = saved with { Document = saved.Document with { State = saved.Document.State with
            { AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCreationFinalizationArchive = null } } } };
        using var freshAdmission = f.Authority.Open(owners.Capture(), historical,
            new(Masking, AcceptObligations: true), store.CareerInitiationRuntimeFingerprint);
        Assert.IsNotNull(freshAdmission);
        Assert.IsTrue(freshAdmission!.Quote.CanApply);
        var freshQuote = freshAdmission.Quote with { Binding = freshAdmission.Quote.Binding with
            { AuxiliaryDigest = saved.Document.AuxiliaryStateDigest } };
        var fresh = new CharacterCareerInitiationPlan(Guid.NewGuid(), freshQuote, Date);
        Assert.IsTrue(CharacterCareerInitiationRules.IsValid(fresh));
        Assert.IsNull(f.Authority.Open(owners.Capture(), saved, freshQuote.Selection,
            store.CareerInitiationRuntimeFingerprint));
        AssertPersistedAuthorityRefusal609(store, owners, f.Authority, saved, fresh, recordPath);
        if (committed)
            AssertOriginalReceipt609(store, owners, f.Authority, saved, original, recordPath);
    }

    private static void AssertPersistedAuthorityRefusal609(FileWorkspaceStore store,
        CharacterCreationAttributesServiceTests.AllocationOwners owners,
        CharacterCareerInitiationAuthorityResolver authority, WorkspaceStoredDocument before,
        CharacterCareerInitiationPlan fresh, string recordPath)
    {
        AssertNoCampaignXml609(before);
        byte[] bytes = File.ReadAllBytes(recordPath);
        var read = store.ReadCareerInitiationWorkspace(owners.Capture(), before.Id);
        Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, read.Outcome);
        Assert.AreEqual("initiation_campaign_authority_unavailable", read.Error);
        var service = new OwnerBoundCharacterCareerInitiationService(store, owners, authority);
        Assert.IsNull(service.Read(owners.Capture(), before.Id, fresh.Quote.Selection));
        foreach (var result in new[]
        {
            store.CommitCareerInitiation(owners.Capture(), fresh, authority),
            service.Commit(owners.Capture(), fresh)
        })
        {
            Assert.AreEqual(CharacterCareerInitiationOutcome.Unavailable, result.Outcome);
            Assert.AreEqual("initiation_campaign_authority_unavailable", result.Error);
            Assert.IsNull(result.Entry);
        }
        AssertUnchanged609(store, owners.Capture().Owner, before, bytes, recordPath);
    }

    private static void AssertOriginalReceipt609(FileWorkspaceStore store,
        CharacterCreationAttributesServiceTests.AllocationOwners owners,
        CharacterCareerInitiationAuthorityResolver authority, WorkspaceStoredDocument before,
        CharacterCareerInitiationPlan original, string recordPath)
    {
        byte[] bytes = File.ReadAllBytes(recordPath);
        var entry = before.Document.AuxiliaryState.CharacterCareerInitiationTransactions!
            .Single(x => x.Plan.TransactionId == original.TransactionId);
        var service = new OwnerBoundCharacterCareerInitiationService(store, owners, authority);
        foreach (var result in new[]
        {
            store.CommitCareerInitiation(owners.Capture(), original, authority),
            service.Commit(owners.Capture(), original)
        })
        {
            Assert.AreEqual(CharacterCareerInitiationOutcome.Replayed, result.Outcome);
            Assert.AreEqual(JsonSerializer.Serialize(entry), JsonSerializer.Serialize(result.Entry));
        }
        Assert.AreEqual(CharacterCareerInitiationOutcome.IdempotencyConflict,
            service.Commit(owners.Capture(), original with { ExpenseDateLocal = Date.AddMinutes(1) }).Outcome);
        AssertUnchanged609(store, owners.Capture().Owner, before, bytes, recordPath);
    }

    private static void AssertUnchanged609(FileWorkspaceStore store, OwnerScope owner,
        WorkspaceStoredDocument before, byte[] bytes, string recordPath)
    {
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(recordPath));
        // Scoped reads reject the local sentinel; use each fixture's matching read overload.
        var read = owner.IsLocalSingleUser ? store.Get(before.Id) : store.Get(owner, before.Id);
        Assert.IsTrue(read.Success, read.Error);
        Assert.IsNotNull(read.Value);
        var after = read.Value!;
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
        Assert.AreEqual(before.LastUpdatedUtc, after.LastUpdatedUtc);
        Assert.AreEqual(JsonSerializer.Serialize(before.Document.AuxiliaryState),
            JsonSerializer.Serialize(after.Document.AuxiliaryState));
        var oldXml = XElement.Parse(before.Document.Content);
        var newXml = XElement.Parse(after.Document.Content);
        foreach (string name in new[] { "karma", "nuyen", "attributes", "magsplitadept", "adeptpowerpoints",
            "initiategrade", "initiationgrades", "metamagics", "powers", "improvements", "expenses" })
            Assert.AreEqual(oldXml.Element(name)?.ToString(), newXml.Element(name)?.ToString(), name);
    }

    private static void AssertNoCampaignXml609(WorkspaceStoredDocument saved)
        => Assert.IsFalse(XElement.Parse(saved.Document.Content).Elements()
            .Any(x => x.Name.LocalName.Contains("campaign", StringComparison.OrdinalIgnoreCase)));

    private static string Sha609(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Campaign_archive_without_campaign_XML_refuses_fresh_initiation_and_preserves_receipts(bool committed)
    {
        // Reuse the existing retained-source completion fixture and its campaign boundary setup.
        // The new regression adds no campaign authorizer, fake Magic evaluator or admission stub.
        using var context = ReputationContext414(preserved: false);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var prepared = Prepare518(context, store);
        _ = Finish518(context, store, prepared);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        Assert.IsTrue(cold.Get(context.WorkspaceId).Success);
        CharacterCareerInitiationTests.AssertArchiveAuthority609(cold, context.WorkspaceId,
            prepared.Owners, Record418(context), committed,
            () => FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries));
    }
}
