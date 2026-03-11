using System;
using System.Collections.Generic;
using System.IO;
using Chummer.Contracts.Api;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Files;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class RosterStoreTests
{
    [TestMethod]
    public void Upsert_adds_new_entry_and_avoids_duplicate_name_alias_pairs()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            var store = new FileRosterStore(stateDirectory);
            RosterEntry entry = new("BLUE", "Troy", "Ork", DateTimeOffset.UtcNow.ToString("O"));

            IReadOnlyList<RosterEntry> first = store.Upsert(entry);
            IReadOnlyList<RosterEntry> second = store.Upsert(entry);

            Assert.HasCount(1, first);
            Assert.HasCount(1, second);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Upsert_enforces_maximum_of_fifty_entries()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            var store = new FileRosterStore(stateDirectory);

            for (int i = 0; i < 55; i++)
            {
                store.Upsert(new RosterEntry($"Name-{i}", $"Alias-{i}", "Human", DateTimeOffset.UtcNow.ToString("O")));
            }

            IReadOnlyList<RosterEntry> entries = store.Load();
            Assert.HasCount(50, entries);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Upsert_isolates_owner_scopes_and_preserves_local_single_user_path()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileRosterStore store = new(stateDirectory);
            OwnerScope alice = new("Alice@example.com");

            store.Upsert(new RosterEntry("Global", "Runner", "Human", DateTimeOffset.UtcNow.ToString("O")));
            store.Upsert(alice, new RosterEntry("Alice", "Mage", "Elf", DateTimeOffset.UtcNow.ToString("O")));

            Assert.HasCount(1, store.Load());
            Assert.HasCount(1, store.Load(alice));
            Assert.IsTrue(File.Exists(Path.Combine(stateDirectory, "roster.json")));
            Assert.IsTrue(Directory.Exists(Path.Combine(stateDirectory, "owners")));
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    private static string CreateTempStateDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "chummer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
