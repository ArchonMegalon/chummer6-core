using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Microsoft.Extensions.DependencyInjection;

// Intentionally RED diagnostic of the old API's applicability to linked work.
// This is NOT a regression test: the explicit trusted-local APIs remain local.
// Compile independently with -p:RunLegacyProbe=true before adding companions.
string root = Check.Root(args);
using var fixture = new CreationFixture(root, CreationFixture.AccountA);
Check.That(fixture.Authority.Current == CreationFixture.AccountA, "Linked owner control is not active.");
if (args.Contains("--contacts", StringComparer.Ordinal))
{
    fixture.SeedContacts();
    string localBefore = fixture.Snapshot(OwnerScope.LocalSingleUser, CreationFixture.ContactWorkspace);
    string linkedBefore = fixture.Snapshot(CreationFixture.AccountA, CreationFixture.ContactWorkspace);
    var service = fixture.Provider.GetRequiredService<ICharacterCreationContactsService>();
    var state = service.Load(new(CreationFixture.ContactWorkspace)).Value
        ?? throw new InvalidOperationException("Legacy contact fixture did not load.");
    var edit = new CharacterCreationContactEdit(CreationFixture.ContactId, Free: true);
    var preview = service.Preview(new(state.Binding, edit)).Value
        ?? throw new InvalidOperationException("Legacy contact fixture did not preview.");
    var result = service.Confirm(new(state.Binding, edit, preview.PreviewDigest,
        "legacy-local-routing-probe", ExplicitlyConfirmed: true));
    Check.That(result.Outcome == CharacterCreationContactOutcomes.Applied,
        "Legacy control failed before the storage-routing assertion.");
    Check.That(fixture.Snapshot(OwnerScope.LocalSingleUser, CreationFixture.ContactWorkspace) != localBefore
        && fixture.Snapshot(CreationFixture.AccountA, CreationFixture.ContactWorkspace) == linkedBefore,
        "Legacy local-write control no longer matches the reviewed baseline.");
    Check.That(fixture.Store.Get(CreationFixture.AccountA, CreationFixture.ContactWorkspace).Value!.ContentRevision == 2,
        "EXPECTED SEMANTIC RED: linked A Contacts confirmation used the local partition, not A.");
}
else
{
    var service = fixture.Provider.GetRequiredService<ICharacterCreationBootstrapService>();
    var created = service.Create(CreationFixture.Request());
    Check.That(created.Outcome == CharacterCreationBootstrapOutcomes.Success && created.Value is not null,
        "Legacy bootstrap control failed before the storage-routing assertion.");
    Check.That(fixture.Store.Get(created.Value!.WorkspaceId).Success,
        "Legacy trusted-local control did not create its local workspace.");
    Check.That(fixture.Store.Get(CreationFixture.AccountA, created.Value.WorkspaceId).Success,
        "EXPECTED SEMANTIC RED: linked A bootstrap used the local partition, not A.");
}
