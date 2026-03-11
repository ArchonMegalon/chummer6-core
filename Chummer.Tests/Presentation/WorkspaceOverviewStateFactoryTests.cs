using System;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Presentation;

[TestClass]
public class WorkspaceOverviewStateFactoryTests
{
    [TestMethod]
    public void CreateLoadedState_maps_loaded_payload_and_restored_view()
    {
        WorkspaceOverviewStateFactory factory = new();
        CharacterWorkspaceId workspaceId = new("ws-1");
        WorkspaceSessionState session = CreateSession(workspaceId);
        WorkspaceOverviewLoadResult loadedOverview = CreateLoadedOverview("Troy", "BLUE");
        WorkspaceViewState restoredView = new(
            ActiveTabId: "tab-gear",
            ActiveActionId: "tab-gear.armor",
            ActiveSectionId: "armor",
            ActiveSectionJson: "{\"sectionId\":\"armor\"}",
            ActiveSectionRows:
            [
                new SectionRowState("armor[0].name", "Armor Jacket")
            ],
            HasSavedWorkspace: true);

        CharacterOverviewState next = factory.CreateLoadedState(
            CharacterOverviewState.Empty,
            workspaceId,
            session,
            loadedOverview,
            restoredView,
            hasSavedWorkspace: true);

        Assert.IsFalse(next.IsBusy);
        Assert.IsNull(next.Error);
        Assert.AreEqual("ws-1", next.WorkspaceId?.Value);
        Assert.AreEqual("Troy", next.Profile?.Name);
        Assert.AreEqual("BLUE", next.Profile?.Alias);
        Assert.AreEqual("tab-gear", next.ActiveTabId);
        Assert.AreEqual("tab-gear.armor", next.ActiveActionId);
        Assert.AreEqual("armor", next.ActiveSectionId);
        Assert.AreEqual("{\"sectionId\":\"armor\"}", next.ActiveSectionJson);
        Assert.HasCount(1, next.ActiveSectionRows);
        Assert.IsTrue(next.HasSavedWorkspace);
        Assert.IsNull(next.ActiveDialog);
    }

    [TestMethod]
    public void CreateLoadedState_preserves_shell_contract_from_current_state()
    {
        WorkspaceOverviewStateFactory factory = new();
        CharacterWorkspaceId workspaceId = new("ws-2");
        WorkspaceSessionState session = CreateSession(workspaceId);
        WorkspaceOverviewLoadResult loadedOverview = CreateLoadedOverview("Dana", "D");
        CharacterOverviewState current = CharacterOverviewState.Empty with
        {
            LastCommandId = "save_character",
            Notice = "Workspace restored.",
            Commands = [new AppCommandDefinition("save_character", "Save", "file", true, true, RulesetDefaults.Sr5)],
            NavigationTabs = [new NavigationTabDefinition("tab-info", "Info", "profile", "character", true, true, RulesetDefaults.Sr5)],
            Preferences = new DesktopPreferenceState(
                UiScalePercent: 125,
                Theme: "legacy",
                Language: "en-us",
                CompactMode: true,
                CharacterPriority: "SumToTen",
                KarmaNuyenRatio: 2,
                HouseRulesEnabled: false,
                CharacterNotes: "notes")
        };

        CharacterOverviewState next = factory.CreateLoadedState(
            current,
            workspaceId,
            session,
            loadedOverview,
            restoredView: null,
            hasSavedWorkspace: false);

        Assert.AreEqual("save_character", next.LastCommandId);
        Assert.AreEqual("Workspace restored.", next.Notice);
        Assert.HasCount(1, next.Commands);
        Assert.AreEqual("save_character", next.Commands[0].Id);
        Assert.HasCount(1, next.NavigationTabs);
        Assert.AreEqual("tab-info", next.NavigationTabs[0].Id);
        Assert.AreEqual(125, next.Preferences.UiScalePercent);
        Assert.AreEqual("legacy", next.Preferences.Theme);
        Assert.IsEmpty(next.ActiveSectionRows);
        Assert.IsFalse(next.HasSavedWorkspace);
    }

    private static WorkspaceSessionState CreateSession(CharacterWorkspaceId workspaceId)
    {
        return new WorkspaceSessionState(
            ActiveWorkspaceId: workspaceId,
            OpenWorkspaces:
            [
                new OpenWorkspaceState(
                    Id: workspaceId,
                    Name: "Test Workspace",
                    Alias: "TW",
                    LastOpenedUtc: DateTimeOffset.UtcNow,
                    RulesetId: RulesetDefaults.Sr5,
                    HasSavedWorkspace: false)
            ],
            RecentWorkspaceIds: [workspaceId]);
    }

    private static WorkspaceOverviewLoadResult CreateLoadedOverview(string name, string alias)
    {
        return new WorkspaceOverviewLoadResult(
            Profile: new CharacterProfileSection(
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
                CreatedVersion: "1.0",
                AppVersion: "1.0",
                BuildMethod: "Priority",
                GameplayOption: "Standard",
                Created: true,
                Adept: false,
                Magician: false,
                Technomancer: false,
                AI: false,
                MainMugshotIndex: 0,
                MugshotCount: 0),
            Progress: new CharacterProgressSection(
                Karma: 12m,
                Nuyen: 5000m,
                StartingNuyen: 0m,
                StreetCred: 0,
                Notoriety: 0,
                PublicAwareness: 0,
                BurntStreetCred: 0,
                BuildKarma: 0,
                TotalAttributes: 0,
                TotalSpecial: 0,
                PhysicalCmFilled: 0,
                StunCmFilled: 0,
                TotalEssence: 6m,
                InitiateGrade: 0,
                SubmersionGrade: 0,
                MagEnabled: false,
                ResEnabled: false,
                DepEnabled: false),
            Skills: new CharacterSkillsSection(
                Count: 0,
                KnowledgeCount: 0,
                Skills: []),
            Rules: new CharacterRulesSection(
                GameEdition: "SR5",
                Settings: "default.xml",
                GameplayOption: "Standard",
                GameplayOptionQualityLimit: 25,
                MaxNuyen: 0,
                MaxKarma: 0,
                ContactMultiplier: 3,
                BannedWareGrades: []),
            Build: new CharacterBuildSection(
                BuildMethod: "Priority",
                PriorityMetatype: "A",
                PriorityAttributes: "B",
                PrioritySpecial: "C",
                PrioritySkills: "D",
                PriorityResources: "E",
                PriorityTalent: "Mundane",
                SumToTen: 10,
                Special: 0,
                TotalSpecial: 0,
                TotalAttributes: 0,
                ContactPoints: 0,
                ContactPointsUsed: 0),
            Movement: new CharacterMovementSection(
                Walk: "0/0/0",
                Run: "0/0/0",
                Sprint: "0/0/0",
                WalkAlt: "0/0/0",
                RunAlt: "0/0/0",
                SprintAlt: "0/0/0",
                PhysicalCmFilled: 0,
                StunCmFilled: 0),
            Awakening: new CharacterAwakeningSection(
                MagEnabled: false,
                ResEnabled: false,
                DepEnabled: false,
                Adept: false,
                Magician: false,
                Technomancer: false,
                AI: false,
                InitiateGrade: 0,
                SubmersionGrade: 0,
                Tradition: string.Empty,
                TraditionName: string.Empty,
                TraditionDrain: string.Empty,
                SpiritCombat: string.Empty,
                SpiritDetection: string.Empty,
                SpiritHealth: string.Empty,
                SpiritIllusion: string.Empty,
                SpiritManipulation: string.Empty,
                Stream: string.Empty,
                StreamDrain: string.Empty,
                CurrentCounterspellingDice: 0,
                SpellLimit: 0,
                CfpLimit: 0,
                AiNormalProgramLimit: 0,
                AiAdvancedProgramLimit: 0));
    }
}
