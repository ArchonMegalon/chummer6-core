#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chummer.Application.Tools;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;
using Chummer.Infrastructure.Files;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class ShellOwnerScopeTests
{
    [TestMethod]
    public void ShellPreferencesService_no_arg_methods_use_local_single_user_scope()
    {
        RecordingPreferencesStore store = new();
        ShellPreferencesService service = new(store);

        service.Save(new ShellPreferences("sr6"));
        ShellPreferences restored = service.Load();

        Assert.AreEqual(OwnerScope.LocalSingleUser.NormalizedValue, store.LastLoadedOwner?.NormalizedValue);
        Assert.AreEqual(OwnerScope.LocalSingleUser.NormalizedValue, store.LastSavedOwner?.NormalizedValue);
        Assert.AreEqual("sr6", restored.PreferredRulesetId);
    }

    [TestMethod]
    public void ShellSessionService_owner_scope_methods_isolate_and_normalize_state()
    {
        RecordingSessionStore store = new();
        ShellSessionService service = new(store);
        OwnerScope alice = new("Alice@example.com");
        OwnerScope bob = new("bob@example.com");

        service.Save(
            alice,
            new ShellSessionState(
                ActiveWorkspaceId: " ws-a ",
                ActiveTabId: " tab-info ",
                ActiveTabsByWorkspace: new Dictionary<string, string>
                {
                    [" ws-a "] = " tab-rules "
                }));
        service.Save(
            bob,
            new ShellSessionState(
                ActiveWorkspaceId: "ws-b",
                ActiveTabId: "tab-gear"));

        ShellSessionState aliceSession = service.Load(alice);
        ShellSessionState bobSession = service.Load(bob);

        Assert.AreEqual("ws-a", aliceSession.ActiveWorkspaceId);
        Assert.AreEqual("tab-info", aliceSession.ActiveTabId);
        Assert.IsNotNull(aliceSession.ActiveTabsByWorkspace);
        Assert.AreEqual("tab-rules", aliceSession.ActiveTabsByWorkspace["ws-a"]);
        Assert.AreEqual("ws-b", bobSession.ActiveWorkspaceId);
        Assert.AreEqual("tab-gear", bobSession.ActiveTabId);
        Assert.AreEqual(bob.NormalizedValue, store.LastSavedOwner?.NormalizedValue);
    }

    [TestMethod]
    public void SettingsShellPreferencesStore_preserves_global_scope_for_local_single_user_and_isolates_other_owners()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileSettingsStore settingsStore = new(stateDirectory);
            SettingsShellPreferencesStore store = new(settingsStore);
            OwnerScope alice = new("Alice@example.com");

            store.Save(OwnerScope.LocalSingleUser, new ShellPreferences("sr5"));
            store.Save(alice, new ShellPreferences("sr6"));

            Assert.AreEqual("sr5", store.Load(OwnerScope.LocalSingleUser).PreferredRulesetId);
            Assert.AreEqual("sr6", store.Load(alice).PreferredRulesetId);
            Assert.AreEqual(string.Empty, store.Load(new OwnerScope("bob@example.com")).PreferredRulesetId);
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

    [TestMethod]
    public void SettingsShellSessionStore_preserves_global_scope_for_local_single_user_and_isolates_other_owners()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileSettingsStore settingsStore = new(stateDirectory);
            SettingsShellSessionStore store = new(settingsStore);
            OwnerScope alice = new("Alice@example.com");

            store.Save(OwnerScope.LocalSingleUser, new ShellSessionState(ActiveWorkspaceId: "ws-global", ActiveTabId: "tab-info"));
            store.Save(alice, new ShellSessionState(ActiveWorkspaceId: "ws-alice", ActiveTabId: "tab-gear"));

            Assert.AreEqual("ws-global", store.Load(OwnerScope.LocalSingleUser).ActiveWorkspaceId);
            Assert.AreEqual("ws-alice", store.Load(alice).ActiveWorkspaceId);
            Assert.IsNull(store.Load(new OwnerScope("bob@example.com")).ActiveWorkspaceId);
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

    private sealed class RecordingPreferencesStore : IShellPreferencesStore
    {
        private readonly Dictionary<string, ShellPreferences> _preferencesByOwner = new(StringComparer.Ordinal);

        public OwnerScope? LastLoadedOwner { get; private set; }

        public OwnerScope? LastSavedOwner { get; private set; }

        public ShellPreferences Load()
        {
            return Load(OwnerScope.LocalSingleUser);
        }

        public ShellPreferences Load(OwnerScope owner)
        {
            LastLoadedOwner = owner;
            return _preferencesByOwner.GetValueOrDefault(owner.NormalizedValue, ShellPreferences.Default);
        }

        public void Save(ShellPreferences preferences)
        {
            Save(OwnerScope.LocalSingleUser, preferences);
        }

        public void Save(OwnerScope owner, ShellPreferences preferences)
        {
            LastSavedOwner = owner;
            _preferencesByOwner[owner.NormalizedValue] = preferences;
        }
    }

    private sealed class RecordingSessionStore : IShellSessionStore
    {
        private readonly Dictionary<string, ShellSessionState> _sessionsByOwner = new(StringComparer.Ordinal);

        public OwnerScope? LastSavedOwner { get; private set; }

        public ShellSessionState Load()
        {
            return Load(OwnerScope.LocalSingleUser);
        }

        public ShellSessionState Load(OwnerScope owner)
        {
            return _sessionsByOwner.GetValueOrDefault(owner.NormalizedValue, ShellSessionState.Default);
        }

        public void Save(ShellSessionState session)
        {
            Save(OwnerScope.LocalSingleUser, session);
        }

        public void Save(OwnerScope owner, ShellSessionState session)
        {
            LastSavedOwner = owner;
            _sessionsByOwner[owner.NormalizedValue] = session;
        }
    }
}
