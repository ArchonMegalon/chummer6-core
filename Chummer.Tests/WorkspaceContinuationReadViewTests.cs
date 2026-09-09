using System.Reflection;
using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceContinuationReadViewTests
{
    private static readonly OwnerScope OwnerA = new("candidate-owner-a");
    private static readonly OwnerScope OwnerB = new("candidate-owner-b");
    private static readonly CharacterWorkspaceId Id = new("candidate-runner");

    [TestMethod]
    public void Each_view_reads_only_its_exact_owner_and_candidate_including_legacy_signatures()
    {
        foreach (OwnerScope owner in new[] { OwnerA, OwnerB, OwnerScope.LocalSingleUser })
        {
            WorkspaceStoredDocument candidate = Candidate(owner.Value);
            IWorkspaceStore view = new WorkspaceContinuationReadView(owner, candidate);
            Same(candidate, view.Get(Id).Value);
            Same(candidate, view.Get(owner, Id).Value);
            var expectedEntry = new WorkspaceStoreEntry(
                Id, candidate.LastUpdatedUtc, candidate.ContentRevision, candidate.SavedRevision);
            CollectionAssert.AreEqual(new[] { expectedEntry }, view.List().ToArray());
            CollectionAssert.AreEqual(new[] { expectedEntry }, view.List(owner).ToArray());

            foreach (OwnerScope foreign in new[]
                     {
                         OwnerA, OwnerB, OwnerScope.LocalSingleUser, default,
                         new OwnerScope(OwnerScope.LocalSingleUser.Value),
                         new OwnerScope(owner.Value.ToUpperInvariant()),
                         new OwnerScope(" " + owner.Value + " ")
                     }.Where(scope => scope != owner))
            {
                Missing(view.Get(foreign, Id));
                Assert.IsEmpty(view.List(foreign));
            }
        }
    }

    [TestMethod]
    public void Trusted_local_provenance_is_not_recreated_from_the_sentinel_string()
    {
        IWorkspaceStore view = new WorkspaceContinuationReadView(OwnerScope.LocalSingleUser, Candidate());
        Assert.IsTrue(view.Get(OwnerScope.LocalSingleUser, Id).Success);
        foreach (string value in new[] { "local-single-user", "LOCAL-SINGLE-USER", " local-single-user " })
        {
            OwnerScope forgedLocal = new(value);
            Assert.IsTrue(forgedLocal.UsesLocalSingleUserValue);
            Assert.IsFalse(forgedLocal.IsLocalSingleUser);
            Missing(view.Get(forgedLocal, Id));
            Assert.IsEmpty(view.List(forgedLocal));
            Assert.ThrowsExactly<ArgumentException>(() => new WorkspaceContinuationReadView(forgedLocal, Candidate()));
        }
    }

    [TestMethod]
    public void Wrong_or_normalization_equivalent_workspace_IDs_are_missing()
    {
        IWorkspaceStore view = new WorkspaceContinuationReadView(OwnerA, Candidate());
        foreach (CharacterWorkspaceId wrong in new[]
                 {
                     new CharacterWorkspaceId("other-runner"), new(Id.Value.ToUpperInvariant()),
                     new(" " + Id.Value), new(Id.Value + " "), new(""), default
                 })
        {
            Missing(view.Get(wrong));
            Missing(view.Get(OwnerA, wrong));
            Missing(view.Get(OwnerB, wrong));
        }
        Assert.HasCount(1, view.List());
    }

    [TestMethod]
    public void Missing_identity_or_document_cannot_construct_a_view()
    {
        foreach (string? value in new string?[] { null, "", " \t" })
        {
            Assert.ThrowsExactly<ArgumentException>(() => new WorkspaceContinuationReadView(new(value!), Candidate()));
            Assert.ThrowsExactly<ArgumentException>(() => new WorkspaceContinuationReadView(
                OwnerA, Candidate() with { Id = new(value!) }));
        }
        Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceContinuationReadView(OwnerA, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceContinuationReadView(
            OwnerA, Candidate() with { Document = null! }));
        var candidate = Candidate();
        Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceContinuationReadView(
            OwnerA, candidate with { Document = candidate.Document with { State = null! } }));
        Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceContinuationReadView(
            OwnerA, candidate with { Document = candidate.Document with
            {
                State = candidate.Document.State with { AuxiliaryState = null! }
            } }));
    }

    [TestMethod]
    public void Complete_auxiliary_history_and_exact_envelope_are_preserved_without_normalization()
    {
        WorkspaceStoredDocument candidate = Candidate();
        candidate = candidate with
        {
            Document = candidate.Document with
            {
                Format = WorkspaceDocumentFormat.Json,
                State = candidate.Document.State with
                {
                    RulesetId = " SR5 ", PayloadKind = " candidate-kind ", SchemaVersion = 999
                }
            }
        };
        string before = JsonSerializer.Serialize(candidate);
        IWorkspaceStore view = new WorkspaceContinuationReadView(OwnerA, candidate);
        WorkspaceStoredDocument read = view.Get(OwnerA, Id).Value!;
        Assert.AreEqual(before, JsonSerializer.Serialize(read));
        Assert.AreEqual(before, JsonSerializer.Serialize(candidate));
        Assert.AreEqual(candidate.Document.AuxiliaryStateDigest, read.Document.AuxiliaryStateDigest);
        Assert.HasCount(1, read.Document.AuxiliaryState.CharacterCreationSkillsReceipts!);
        Assert.HasCount(1, read.Document.AuxiliaryState.CharacterCreationFinalizationArchive!
            .State.CharacterCreationSkillsReceipts!);
        Assert.AreEqual("history-is-not-admission", read.Document.AuxiliaryState
            .CharacterCreationSkillsReceipts![0].ReceiptDigest);
        Assert.IsNotNull(read.Document.AuxiliaryState.LifeModuleDecisionAcceptances);
        Assert.IsNotNull(read.Document.AuxiliaryState.CharacterCareerReputationReceipts);
        Assert.IsNotNull(read.Document.AuxiliaryState.CharacterAfterRunRewardReceipts);
    }

    [TestMethod]
    public void Mutable_input_and_returned_receipt_graphs_cannot_change_the_frozen_candidate()
    {
        WorkspaceStoredDocument candidate = Candidate();
        string before = JsonSerializer.Serialize(candidate);
        IWorkspaceStore view = new WorkspaceContinuationReadView(OwnerA, candidate);
        var originalAuxiliary = candidate.Document.AuxiliaryState;
        ((IDictionary<string, string>)originalAuxiliary.CharacterCreationFoundationDraft!.FollowUpValues)["detail"] = "caller-mutated";
        ((IList<CharacterCreationSkillsReceipt>)originalAuxiliary.CharacterCreationSkillsReceipts!).Clear();

        WorkspaceStoredDocument first = view.Get(Id).Value!;
        Assert.AreEqual(before, JsonSerializer.Serialize(first));
        var returnedAuxiliary = first.Document.AuxiliaryState;
        ((IDictionary<string, string>)returnedAuxiliary.CharacterCreationFoundationDraft!.FollowUpValues)["detail"] = "consumer-mutated";
        ((IList<string>)returnedAuxiliary.CharacterCreationFoundationDraft.SourceAnchorIds)[0] = "consumer-anchor";
        ((IList<CharacterCreationSkillsReceipt>)returnedAuxiliary.CharacterCreationSkillsReceipts!).Clear();
        ((IList<CharacterCreationSkillsReceipt>)returnedAuxiliary.CharacterCreationFinalizationArchive!
            .State.CharacterCreationSkillsReceipts!).Clear();
        Assert.AreEqual(before, JsonSerializer.Serialize(view.Get(OwnerA, Id).Value));
        var list = view.List();
        if (list is IList<WorkspaceStoreEntry> mutableList && !mutableList.IsReadOnly)
            mutableList[0] = new(new("injected"), default, 99, 99);
        Assert.AreEqual(Id, view.List().Single().Id);
    }

    [TestMethod]
    public void No_write_bootstrap_inventory_or_continuation_authority_is_advertised()
    {
        IWorkspaceStore view = new WorkspaceContinuationReadView(OwnerA, Candidate());
        CollectionAssert.AreEquivalent(new[] { typeof(IWorkspaceStore) }, view.GetType().GetInterfaces());
        Assert.IsFalse(view is IWorkspaceAuxiliaryStateAtomicCommitCapability);
        Assert.IsFalse(view is IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability);
        Assert.IsFalse(view is ICharacterCreationBootstrapAtomicCreateCapability);
        Assert.IsFalse(view is IOwnerScopedCharacterCreationBootstrapAtomicCreateCapability);
        Assert.IsFalse(view is IWorkspaceStoreInventory);
        Assert.IsFalse(view is IWorkspaceContinuationReadCapability);
    }

    [TestMethod]
    public void Every_interface_mutation_and_GM_lookup_is_unavailable_and_leaves_candidate_unchanged()
    {
        WorkspaceStoredDocument candidate = Candidate();
        string before = JsonSerializer.Serialize(candidate);
        IWorkspaceStore view = new WorkspaceContinuationReadView(OwnerA, candidate);
        // Enumerating the interface includes default implementations, so future
        // mutation additions cannot silently escape this fail-closed coverage.
        MethodInfo[] operations = typeof(IWorkspaceStore).GetMethods().Where(method =>
            method.ReturnType == typeof(WorkspaceStoreMutationResult)
            || method.ReturnType == typeof(DelegatedGmCharacterEditStoreResult)).ToArray();
        Assert.HasCount(16, operations);
        foreach (OwnerScope owner in new[] { OwnerA, OwnerB, OwnerScope.LocalSingleUser, new OwnerScope("local-single-user"), default })
        foreach (CharacterWorkspaceId id in new[] { Id, new CharacterWorkspaceId("other"), default })
        foreach (MethodInfo operation in operations)
        {
            object? Argument(ParameterInfo parameter)
            {
                if (parameter.ParameterType == typeof(OwnerScope)) return owner;
                if (parameter.ParameterType == typeof(CharacterWorkspaceId)) return id;
                if (parameter.ParameterType == typeof(long)) return candidate.ContentRevision;
                if (parameter.ParameterType == typeof(string)) return "untrusted-candidate-digest";
                if (parameter.ParameterType == typeof(WorkspaceDocument)) return new WorkspaceDocument("replacement", "sr5");
                if (parameter.ParameterType == typeof(DelegatedGmCharacterEditLedgerEntry))
                    return new DelegatedGmCharacterEditLedgerEntry("key", "command", null!);
                throw new AssertFailedException("Uncovered interface parameter: " + parameter);
            }

            object? result = operation.Invoke(view, operation.GetParameters().Select(Argument).ToArray());
            if (result is WorkspaceStoreMutationResult mutation)
            {
                Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, mutation.Outcome, operation.ToString());
                Assert.IsFalse(mutation.Success);
                Assert.IsNull(mutation.Entry);
            }
            else if (result is DelegatedGmCharacterEditStoreResult gm)
            {
                Assert.AreEqual(DelegatedGmCharacterEditStoreOutcome.Unavailable, gm.Outcome, operation.ToString());
                Assert.IsNull(gm.Receipt);
                Assert.IsNull(gm.CurrentRevision);
            }
            else Assert.Fail("Uncovered interface operation: " + operation);

            Assert.AreEqual(before, JsonSerializer.Serialize(candidate));
            Assert.AreEqual(before, JsonSerializer.Serialize(view.Get(OwnerA, Id).Value));
            Assert.HasCount(1, view.List());
        }
    }

    private static void Same(WorkspaceStoredDocument expected, WorkspaceStoredDocument? actual)
    {
        Assert.IsNotNull(actual);
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    private static void Missing(WorkspaceStoreReadResult result)
    {
        Assert.AreEqual(WorkspaceOperationOutcome.Missing, result.Outcome);
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Value);
    }

    // Deliberately synthetic data: the read view preserves observations, and
    // never treats a well-shaped receipt or any of these digest strings as an
    // admission decision, proof of rule execution, or GM authorization.
    private static WorkspaceStoredDocument Candidate(string payload = "candidate payload")
    {
        CharacterCreationFoundationDraftLedger foundation = new(
            "foundation-schema", Id, 1, 7, "raw-xml", "source", "Human", new("module", "version"),
            [], [], new Dictionary<string, string> { ["detail"] = "original" }, ["anchor"],
            "candidate", false, "draft-digest");
        CharacterCreationSkillsReceipt receipt = new(
            "skills-schema", Id, 6, 7, 7, 1, "draft", "preview", "key", "command", "previous",
            "authority", "runtime", 1, 2, 3, 0, false, "history-is-not-admission");
        WorkspaceDocumentAuxiliaryState archive = new(
            CharacterCreationFoundationDraft: foundation,
            CharacterCreationSkillsReceipts: new List<CharacterCreationSkillsReceipt> { receipt });
        WorkspaceDocumentAuxiliaryState auxiliary = new(
            CharacterCreationFoundationDraft: foundation,
            CharacterCreationSkillsReceipts: new List<CharacterCreationSkillsReceipt> { receipt },
            CharacterCreationMagicResonanceReceipts: [], CharacterCreationContactReceipts: [],
            CharacterCreationQualitiesReceipts: [], CharacterAfterRunSettlementReceipts: [],
            CharacterCreationLifestyleReceipts: [], CharacterCreationResourcesReceipts: [],
            CharacterCreationGearReceipts: [], LifeModuleDecisionAcceptances: [],
            CharacterCreationFinalizationReceipts: [], CharacterAfterRunRewardReceipts: [],
            CharacterCareerReputationReceipts: [], CharacterCreationFinalizationArchive: new(archive));
        WorkspaceDocument document = new(new WorkspaceDocumentState("sr5", 1, "workspace", payload)
        {
            AuxiliaryState = auxiliary
        });
        return new(Id, document, 7, 6, new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero));
    }
}
