using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace Chummer.Tests.Presentation;

[TestClass]
public class WorkspaceSessionManagerTests
{
    [TestMethod]
    public void Restore_orders_workspaces_by_last_updated_descending()
    {
        WorkspaceSessionManager manager = new();
        WorkspaceListItem[] input =
        [
            new(
                new CharacterWorkspaceId("ws-old"),
                CreateSummary("Old", "O"),
                DateTimeOffset.UtcNow.AddMinutes(-10),
                RulesetDefaults.Sr5),
            new(
                new CharacterWorkspaceId("ws-new"),
                CreateSummary("New", "N"),
                DateTimeOffset.UtcNow.AddMinutes(-1),
                "sr6")
        ];

        IReadOnlyList<OpenWorkspaceState> restored = manager.Restore(input);

        Assert.HasCount(2, restored);
        Assert.AreEqual("ws-new", restored[0].Id.Value);
        Assert.AreEqual("sr6", restored[0].RulesetId);
        Assert.AreEqual("ws-old", restored[1].Id.Value);
        Assert.AreEqual(RulesetDefaults.Sr5, restored[1].RulesetId);
    }

    [TestMethod]
    public void Activate_upserts_existing_workspace_and_moves_it_to_top()
    {
        WorkspaceSessionManager manager = new();
        OpenWorkspaceState[] existing =
        [
            new(new CharacterWorkspaceId("ws-1"), "One", "A", DateTimeOffset.UtcNow.AddMinutes(-10), RulesetDefaults.Sr5),
            new(new CharacterWorkspaceId("ws-2"), "Two", "B", DateTimeOffset.UtcNow.AddMinutes(-5), RulesetDefaults.Sr5)
        ];

        IReadOnlyList<OpenWorkspaceState> updated = manager.Activate(
            existing,
            new CharacterWorkspaceId("ws-1"),
            CreateProfile("One Updated", "A2"));

        Assert.HasCount(2, updated);
        Assert.AreEqual("ws-1", updated[0].Id.Value);
        Assert.AreEqual("One Updated", updated[0].Name);
        Assert.AreEqual("A2", updated[0].Alias);
        Assert.AreEqual(RulesetDefaults.Sr5, updated[0].RulesetId);
    }

    [TestMethod]
    public void Activate_preserves_existing_ruleset_when_workspace_is_reopened()
    {
        WorkspaceSessionManager manager = new();
        OpenWorkspaceState[] existing =
        [
            new(new CharacterWorkspaceId("ws-sr6"), "Six", "S", DateTimeOffset.UtcNow.AddMinutes(-2), RulesetId: "sr6"),
            new(new CharacterWorkspaceId("ws-sr5"), "Five", "F", DateTimeOffset.UtcNow.AddMinutes(-1), RulesetId: RulesetDefaults.Sr5)
        ];

        IReadOnlyList<OpenWorkspaceState> updated = manager.Activate(
            existing,
            new CharacterWorkspaceId("ws-sr6"),
            CreateProfile("Six Updated", "SX"));

        Assert.AreEqual("ws-sr6", updated[0].Id.Value);
        Assert.AreEqual("sr6", updated[0].RulesetId);
    }

    [TestMethod]
    public void Activate_uses_explicit_ruleset_when_provided()
    {
        WorkspaceSessionManager manager = new();

        IReadOnlyList<OpenWorkspaceState> updated = manager.Activate(
            existing: [],
            id: new CharacterWorkspaceId("ws-new"),
            profile: CreateProfile("New", "N"),
            rulesetId: " sr6 ");

        Assert.AreEqual("ws-new", updated[0].Id.Value);
        Assert.AreEqual("sr6", updated[0].RulesetId);
    }

    [TestMethod]
    public void Activate_uses_existing_workspace_context_when_explicit_ruleset_is_missing()
    {
        WorkspaceSessionManager manager = new();
        OpenWorkspaceState[] existing =
        [
            new(new CharacterWorkspaceId("ws-sr6"), "Six", "S", DateTimeOffset.UtcNow.AddMinutes(-2), RulesetId: "sr6")
        ];

        IReadOnlyList<OpenWorkspaceState> updated = manager.Activate(
            existing,
            id: new CharacterWorkspaceId("ws-new"),
            profile: CreateProfile("New", "N"));

        Assert.AreEqual("ws-new", updated[0].Id.Value);
        Assert.AreEqual("sr6", updated[0].RulesetId);
    }

    [TestMethod]
    public void Activate_returns_blank_ruleset_when_no_context_is_available()
    {
        WorkspaceSessionManager manager = new();

        IReadOnlyList<OpenWorkspaceState> updated = manager.Activate(
            existing: [],
            id: new CharacterWorkspaceId("ws-new"),
            profile: CreateProfile("New", "N"));

        Assert.AreEqual("ws-new", updated[0].Id.Value);
        Assert.AreEqual(string.Empty, updated[0].RulesetId);
    }

    [TestMethod]
    public void Close_removes_workspace_and_select_next_returns_first_remaining()
    {
        WorkspaceSessionManager manager = new();
        OpenWorkspaceState[] existing =
        [
            new(new CharacterWorkspaceId("ws-top"), "Top", "T", DateTimeOffset.UtcNow, RulesetDefaults.Sr5),
            new(new CharacterWorkspaceId("ws-next"), "Next", "N", DateTimeOffset.UtcNow.AddMinutes(-1), RulesetDefaults.Sr5)
        ];

        IReadOnlyList<OpenWorkspaceState> remaining = manager.Close(existing, new CharacterWorkspaceId("ws-top"));
        CharacterWorkspaceId? next = manager.SelectNext(remaining);

        Assert.HasCount(1, remaining);
        Assert.AreEqual("ws-next", remaining[0].Id.Value);
        Assert.IsNotNull(next);
        Assert.AreEqual("ws-next", next.Value.Value);
    }

    private static CharacterFileSummary CreateSummary(string name, string alias)
    {
        return new CharacterFileSummary(
            Name: name,
            Alias: alias,
            Metatype: "Human",
            BuildMethod: "Priority",
            CreatedVersion: "1.0",
            AppVersion: "1.0",
            Karma: 0m,
            Nuyen: 0m,
            Created: true);
    }

    private static CharacterProfileSection CreateProfile(string name, string alias)
    {
        return new CharacterProfileSection(
            Name: name,
            Alias: alias,
            PlayerName: string.Empty,
            Metatype: "Human",
            Metavariant: string.Empty,
            Sex: string.Empty,
            Age: string.Empty,
            Height: string.Empty,
            Weight: string.Empty,
            Hair: string.Empty,
            Eyes: string.Empty,
            Skin: string.Empty,
            Concept: string.Empty,
            Description: string.Empty,
            Background: string.Empty,
            CreatedVersion: string.Empty,
            AppVersion: string.Empty,
            BuildMethod: "Priority",
            GameplayOption: string.Empty,
            Created: true,
            Adept: false,
            Magician: false,
            Technomancer: false,
            AI: false,
            MainMugshotIndex: 0,
            MugshotCount: 0);
    }
}
