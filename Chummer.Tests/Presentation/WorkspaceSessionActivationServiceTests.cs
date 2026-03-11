using System.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Presentation;

[TestClass]
public class WorkspaceSessionActivationServiceTests
{
    [TestMethod]
    public void Activate_without_seed_and_update_enabled_opens_workspace_with_blank_ruleset_when_context_is_missing()
    {
        WorkspaceSessionPresenter sessionPresenter = new();
        WorkspaceSessionActivationService service = new();
        CharacterWorkspaceId workspaceId = new("ws-1");

        WorkspaceSessionState state = service.Activate(
            sessionPresenter,
            workspaceId,
            CreateProfile("One", "A"),
            sessionSeed: null,
            updateSession: true);

        Assert.AreEqual("ws-1", state.ActiveWorkspaceId?.Value);
        Assert.HasCount(1, state.OpenWorkspaces);
        Assert.AreEqual("ws-1", state.OpenWorkspaces[0].Id.Value);
        Assert.AreEqual(string.Empty, state.OpenWorkspaces[0].RulesetId);
    }

    [TestMethod]
    public void Activate_applies_explicit_ruleset_to_opened_workspace()
    {
        WorkspaceSessionPresenter sessionPresenter = new();
        WorkspaceSessionActivationService service = new();
        CharacterWorkspaceId workspaceId = new("ws-ruleset");

        WorkspaceSessionState state = service.Activate(
            sessionPresenter,
            workspaceId,
            CreateProfile("Ruleset", "RS"),
            sessionSeed: null,
            updateSession: true,
            rulesetId: " SR6 ");

        Assert.AreEqual("ws-ruleset", state.ActiveWorkspaceId?.Value);
        Assert.HasCount(1, state.OpenWorkspaces);
        Assert.AreEqual("sr6", state.OpenWorkspaces[0].RulesetId);
    }

    [TestMethod]
    public void Activate_with_seed_switches_existing_workspace_without_duplicates()
    {
        WorkspaceSessionPresenter sessionPresenter = new();
        WorkspaceSessionActivationService service = new();
        CharacterWorkspaceId workspaceOne = new("ws-1");
        CharacterWorkspaceId workspaceTwo = new("ws-2");

        sessionPresenter.Open(workspaceOne, CreateProfile("One", "A"), RulesetDefaults.Sr5);
        WorkspaceSessionState seeded = sessionPresenter.Open(workspaceTwo, CreateProfile("Two", "B"), RulesetDefaults.Sr5);

        WorkspaceSessionState state = service.Activate(
            sessionPresenter,
            workspaceOne,
            CreateProfile("One Updated", "AX"),
            sessionSeed: seeded,
            updateSession: true);

        Assert.AreEqual("ws-1", state.ActiveWorkspaceId?.Value);
        Assert.HasCount(2, state.OpenWorkspaces);
        Assert.AreEqual(
            2,
            state.OpenWorkspaces.Select(workspace => workspace.Id.Value).Distinct().Count());
    }

    [TestMethod]
    public void Activate_without_update_switches_or_opens_when_missing()
    {
        WorkspaceSessionPresenter sessionPresenter = new();
        WorkspaceSessionActivationService service = new();
        CharacterWorkspaceId workspaceOne = new("ws-1");
        CharacterWorkspaceId workspaceThree = new("ws-3");

        sessionPresenter.Open(workspaceOne, CreateProfile("One", "A"), RulesetDefaults.Sr5);

        WorkspaceSessionState switchedState = service.Activate(
            sessionPresenter,
            workspaceOne,
            CreateProfile("One Updated", "AX"),
            sessionSeed: null,
            updateSession: false);

        Assert.AreEqual("ws-1", switchedState.ActiveWorkspaceId?.Value);
        Assert.HasCount(1, switchedState.OpenWorkspaces);

        WorkspaceSessionState openedState = service.Activate(
            sessionPresenter,
            workspaceThree,
            CreateProfile("Three", "C"),
            sessionSeed: null,
            updateSession: false);

        Assert.AreEqual("ws-3", openedState.ActiveWorkspaceId?.Value);
        Assert.HasCount(2, openedState.OpenWorkspaces);
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
