using System.Reflection;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;

// Actual Core/file-store tests; no Android/UI source, release or cleanup grant.
using var fixture = new CreationFixture(Check.Root(args), CreationFixture.AccountA);
fixture.SeedContacts();
FileWorkspaceStore store = fixture.Store;
IWorkspaceStoreInventory inventory = store;
int cases = 0;
void Assert(bool result, string description)
{
    Check.That(result, description);
    cases++;
    Console.WriteLine("PASS Core strict inventory: " + description);
}

Assert(inventory.Inspect() is { Success: true, Value.Count: 1 }, "trusted Local");
Assert(inventory.Inspect(CreationFixture.AccountA) is { Success: true, Value.Count: 1 }, "linked A");
Assert(inventory.Inspect(CreationFixture.AccountB) is { Success: true, Value.Count: 1 }, "linked B");
foreach (OwnerScope invalid in new[] { default(OwnerScope), OwnerScope.LocalSingleUser,
             new OwnerScope("local-single-user") })
    Assert(!inventory.Inspect(invalid).Success, "invalid/reserved scoped owner rejected");
OwnerScope missing = new("strict-inventory-missing");
Assert(inventory.Inspect(missing) is { Success: true, Value.Count: 0 }, "confirmed absence");
var id = new CharacterWorkspaceId("strict-inventory-target");
Check.That(store.CreateWorkspaceDocument(CreationFixture.AccountA, id,
    CreationFixture.ContactDocument()).Success, "Scratch runner creation failed.");
string path = Directory.EnumerateFiles(fixture.StateDirectory, id.Value + ".json",
    SearchOption.AllDirectories).Single();
byte[] original = File.ReadAllBytes(path);
File.WriteAllText(path, "{");
Assert(store.List(CreationFixture.AccountA).Count == 1 && inventory.Inspect(CreationFixture.AccountA)
    is { Success: false, Outcome: WorkspaceOperationOutcome.Corrupt, Value.Count: 1 },
    "display omission cannot establish complete inventory");
File.WriteAllBytes(path, original);
foreach (string name in new[] { "invalid.name.json", "unexpected-extension.JSON" })
{
    string invalid = Path.Combine(Path.GetDirectoryName(path)!, name);
    File.WriteAllBytes(invalid, original);
    Assert(!inventory.Inspect(CreationFixture.AccountA).Success, "invalid/noncanonical member rejected");
    File.Delete(invalid);
}
string retained = path + ".retained";
File.Move(path, retained);
Directory.CreateDirectory(path);
Assert(!inventory.Inspect(CreationFixture.AccountA).Success, "directory in place of runner");
Directory.Delete(path);
File.CreateSymbolicLink(path, retained);
Assert(!inventory.Inspect(CreationFixture.AccountA).Success, "symlink in place of runner");
File.Delete(path);
File.Move(retained, path);

string missingPath = (string)typeof(FileWorkspaceStore).GetMethod("GetWorkspaceDirectory",
    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(store, [missing])!;
Check.That(missingPath.StartsWith(fixture.StateDirectory + Path.DirectorySeparatorChar,
    StringComparison.Ordinal), "Scratch directory escaped the fixture.");
Directory.CreateDirectory(Path.GetDirectoryName(missingPath)!);
File.WriteAllText(missingPath, "not a directory");
Assert(!inventory.Inspect(missing).Success, "file obstruction is not absence");
File.Delete(missingPath);
Assert(inventory.Inspect(missing) is { Success: true, Value.Count: 0 }, "restored confirmed absence");
var cold = new FileWorkspaceStore(fixture.StateDirectory);
Assert(cold.Inspect(CreationFixture.AccountA) is { Success: true, Value.Count: 2 }
    && cold.Inspect(CreationFixture.AccountB) is { Success: true, Value.Count: 1 }
    && cold.Inspect() is { Success: true, Value.Count: 1 }
    && File.ReadAllBytes(path).SequenceEqual(original), "cold inspection preserves exact partitions and bytes");
Console.WriteLine($"PASS {cases} Core-owned strict inventory cases");
