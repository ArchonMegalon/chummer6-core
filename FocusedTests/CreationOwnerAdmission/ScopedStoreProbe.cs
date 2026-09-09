using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Microsoft.Extensions.DependencyInjection;

// Actual store capability proof; not proof of the still-missing owner-bound
// domain companions, native DI/callers, device persistence or package authority.
string root = Check.Root(args);
using (var fixture = new CreationFixture(root, CreationFixture.AccountA))
{
    var legacy = fixture.Provider.GetRequiredService<ICharacterCreationBootstrapService>();
    var created = legacy.Create(CreationFixture.Request()).Value
        ?? throw new InvalidOperationException("Trusted-local bootstrap control failed.");
    WorkspaceStoredDocument original = fixture.Store.Get(created.WorkspaceId).Value!;
    string localBefore = fixture.Snapshot(OwnerScope.LocalSingleUser, created.WorkspaceId);
    var capability = (IOwnerScopedCharacterCreationBootstrapAtomicCreateCapability)fixture.Store;
    Check.That(capability.SupportsOwnerScopedCharacterCreationBootstrapAtomicCreate,
        "The real store must explicitly advertise its scoped capability.");
    Check.That(!fixture.Store.CreateWorkspaceDocument(CreationFixture.AccountA,
        created.WorkspaceId, original.Document).Success,
        "Generic creation accepted authority-bound auxiliary state.");
    Check.That(capability.CreateCharacterCreationBootstrapWorkspaceDocument(CreationFixture.AccountA,
        created.WorkspaceId, original.Document).Success, "Scoped bootstrap failed.");
    var linked = fixture.Store.Get(CreationFixture.AccountA, created.WorkspaceId).Value;
    Check.That(linked is { ContentRevision: 1, SavedRevision: 0 }
        && linked.Document.Content == original.Document.Content
        && linked.Document.AuxiliaryStateDigest == original.Document.AuxiliaryStateDigest,
        "Scoped bootstrap changed document identity or initial revisions.");
    Check.That(fixture.Snapshot(OwnerScope.LocalSingleUser, created.WorkspaceId) == localBefore
        && fixture.Count(CreationFixture.AccountB) == 0, "Scoped bootstrap touched another partition.");
    Check.That(capability.CreateCharacterCreationBootstrapWorkspaceDocument(CreationFixture.AccountA,
        created.WorkspaceId, original.Document).Outcome == WorkspaceOperationOutcome.Conflict,
        "Duplicate scoped bootstrap did not fail atomically.");
    foreach (OwnerScope rejected in new[] { default(OwnerScope), OwnerScope.LocalSingleUser })
        Check.That(!capability.CreateCharacterCreationBootstrapWorkspaceDocument(rejected,
            created.WorkspaceId, original.Document).Success, "Scoped API admitted an invalid/reserved owner.");
    Check.That(!capability.CreateCharacterCreationBootstrapWorkspaceDocument(CreationFixture.AccountB,
        new CharacterWorkspaceId("different-workspace"), original.Document).Success,
        "Bootstrap integrity accepted the wrong workspace binding.");
    Console.WriteLine("PASS scoped bootstrap: explicit capability, exact owner, initial identity, duplicate CAS, reserved owner and binding refusal");
}

using (var fixture = new CreationFixture(root, CreationFixture.AccountA))
{
    fixture.SeedContacts();
    var id = CreationFixture.ContactWorkspace;
    WorkspaceStoredDocument beforeA = fixture.Store.Get(CreationFixture.AccountA, id).Value!;
    string beforeB = fixture.Snapshot(CreationFixture.AccountB, id);
    var legacy = fixture.Provider.GetRequiredService<ICharacterCreationContactsService>();
    var state = legacy.Load(new(id)).Value
        ?? throw new InvalidOperationException("Contacts control did not load.");
    var edit = new CharacterCreationContactEdit(CreationFixture.ContactId, Free: true);
    var preview = legacy.Preview(new(state.Binding, edit)).Value
        ?? throw new InvalidOperationException("Contacts control did not preview.");
    Check.That(legacy.Confirm(new(state.Binding, edit, preview.PreviewDigest,
        "scoped-store-transition-template", ExplicitlyConfirmed: true)).Outcome == CharacterCreationContactOutcomes.Applied,
        "Contacts control did not produce a valid typed transition.");
    var template = fixture.Store.Get(id).Value!;
    string localCommitted = fixture.Snapshot(OwnerScope.LocalSingleUser, id);
    var capability = (IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability)fixture.Store;
    Check.That(capability.SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit,
        "The real store must advertise scoped auxiliary CAS.");
    var result = capability.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(CreationFixture.AccountA,
        id, beforeA.ContentRevision, beforeA.Document.AuxiliaryStateDigest, template.Document);
    Check.That(result.Success && result.Entry is { ContentRevision: 2, SavedRevision: 2 },
        "Scoped typed transition did not atomically replace and checkpoint.");
    string committedA = fixture.Snapshot(CreationFixture.AccountA, id);
    Check.That(fixture.Snapshot(CreationFixture.AccountB, id) == beforeB
        && fixture.Snapshot(OwnerScope.LocalSingleUser, id) == localCommitted,
        "Scoped auxiliary commit touched another owner.");
    Check.That(capability.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(CreationFixture.AccountA,
        id, 1, beforeA.Document.AuxiliaryStateDigest, template.Document).Outcome == WorkspaceOperationOutcome.Conflict,
        "Stale revision was admitted.");
    Check.That(capability.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(CreationFixture.AccountA,
        id, 2, beforeA.Document.AuxiliaryStateDigest, template.Document).Outcome == WorkspaceOperationOutcome.Conflict,
        "Stale auxiliary digest was admitted.");
    foreach (OwnerScope rejected in new[] { default(OwnerScope), OwnerScope.LocalSingleUser })
        Check.That(!capability.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(rejected,
            id, 2, template.Document.AuxiliaryStateDigest, template.Document).Success,
            "Scoped auxiliary API admitted an invalid/reserved owner.");
    Check.That(fixture.Snapshot(CreationFixture.AccountA, id) == committedA,
        "Rejected CAS changed stored bytes.");
    Console.WriteLine("PASS scoped auxiliary CAS: typed transition, owner partition, revision/digest rejection and atomic checkpoint");
}

IOwnerScopedCharacterCreationBootstrapAtomicCreateCapability absentCreate = new UnavailableStore();
IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability absentCommit = new UnavailableStore();
Check.That(!absentCreate.SupportsOwnerScopedCharacterCreationBootstrapAtomicCreate
    && !absentCommit.SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit,
    "Unimplemented capabilities must default unavailable.");
Check.That(absentCreate.CreateCharacterCreationBootstrapWorkspaceDocument(CreationFixture.AccountA,
    CreationFixture.ContactWorkspace, CreationFixture.ContactDocument()).Outcome == WorkspaceOperationOutcome.Unavailable,
    "Missing scoped create implementation did not fail closed.");
Check.That(absentCommit.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(CreationFixture.AccountA,
    CreationFixture.ContactWorkspace, 1, new string('0', 64), CreationFixture.ContactDocument()).Outcome == WorkspaceOperationOutcome.Unavailable,
    "Missing scoped auxiliary implementation did not fail closed.");
Console.WriteLine("PASS unimplemented scoped capabilities default unavailable; no local or generic fallback");

internal sealed class UnavailableStore : IOwnerScopedCharacterCreationBootstrapAtomicCreateCapability,
    IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability;
