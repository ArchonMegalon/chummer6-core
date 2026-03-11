using System;
using System.IO;
using System.Text.Json.Nodes;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Files;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class SettingsStoreTests
{
    [TestMethod]
    public void Load_returns_empty_json_when_scope_file_is_missing()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            var store = new FileSettingsStore(stateDirectory);

            JsonObject settings = store.Load("global");

            Assert.IsEmpty(settings);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Save_and_load_roundtrip_preserves_values()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            var store = new FileSettingsStore(stateDirectory);
            JsonObject expected = new()
            {
                ["uiScale"] = 120,
                ["theme"] = "classic",
                ["compactMode"] = true
            };

            store.Save("global", expected);
            JsonObject actual = store.Load("global");

            Assert.AreEqual(120, actual["uiScale"]?.GetValue<int>());
            Assert.AreEqual("classic", actual["theme"]?.GetValue<string>());
            Assert.IsTrue(actual["compactMode"]?.GetValue<bool>() ?? false);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Owner_scope_methods_preserve_local_single_user_path_and_isolate_other_owners()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileSettingsStore store = new(stateDirectory);
            OwnerScope alice = new("Alice@example.com");
            JsonObject globalSettings = new()
            {
                ["theme"] = "classic"
            };
            JsonObject aliceSettings = new()
            {
                ["theme"] = "neon"
            };

            store.Save(OwnerScope.LocalSingleUser, "global", globalSettings);
            store.Save(alice, "global", aliceSettings);

            Assert.AreEqual("classic", store.Load("global")["theme"]?.GetValue<string>());
            Assert.AreEqual("classic", store.Load(OwnerScope.LocalSingleUser, "global")["theme"]?.GetValue<string>());
            Assert.AreEqual("neon", store.Load(alice, "global")["theme"]?.GetValue<string>());
            Assert.IsTrue(File.Exists(Path.Combine(stateDirectory, "global-settings.json")));
            Assert.IsTrue(File.Exists(Path.Combine(
                stateDirectory,
                "owners",
                Uri.EscapeDataString(alice.NormalizedValue),
                "settings",
                "global-settings.json")));
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
