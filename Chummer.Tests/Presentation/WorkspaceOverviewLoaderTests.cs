#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Content;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests.Presentation;

[TestClass]
public class WorkspaceOverviewLoaderTests
{
    [TestMethod]
    public async Task LoadAsync_returns_expected_sections_from_client()
    {
        WorkspaceOverviewLoader loader = new();
        LoaderClientStub client = new();
        CharacterWorkspaceId workspaceId = new("ws-loader");

        WorkspaceOverviewLoadResult result = await loader.LoadAsync(client, workspaceId, CancellationToken.None);

        Assert.AreEqual("Loader Neo", result.Profile.Name);
        Assert.AreEqual("LOADER", result.Profile.Alias);
        Assert.AreEqual(9m, result.Progress.Karma);
        Assert.AreEqual(1, result.Skills.Count);
        Assert.AreEqual("SR5", result.Rules.GameEdition);
        Assert.AreEqual("Priority", result.Build.BuildMethod);
        Assert.AreEqual("10/25", result.Movement.Walk);
        Assert.IsFalse(result.Awakening.MagEnabled);
    }

    private sealed class LoaderClientStub : IChummerClient
    {
        public Task<ShellPreferences> GetShellPreferencesAsync(CancellationToken ct) => throw new NotImplementedException();

        public Task SaveShellPreferencesAsync(ShellPreferences preferences, CancellationToken ct) => throw new NotImplementedException();

        public Task<ShellSessionState> GetShellSessionAsync(CancellationToken ct) => throw new NotImplementedException();

        public Task SaveShellSessionAsync(ShellSessionState session, CancellationToken ct) => throw new NotImplementedException();

        public Task<ShellBootstrapSnapshot> GetShellBootstrapAsync(string? rulesetId, CancellationToken ct) => throw new NotImplementedException();

        public Task<RuntimeInspectorProjection?> GetRuntimeInspectorProfileAsync(string profileId, string? rulesetId, CancellationToken ct) => throw new NotImplementedException();

        public Task<IReadOnlyList<AppCommandDefinition>> GetCommandsAsync(string? rulesetId, CancellationToken ct) => throw new NotImplementedException();

        public Task<IReadOnlyList<NavigationTabDefinition>> GetNavigationTabsAsync(string? rulesetId, CancellationToken ct) => throw new NotImplementedException();

        public Task<IReadOnlyList<WorkspaceListItem>> ListWorkspacesAsync(CancellationToken ct) => throw new NotImplementedException();

        public Task<WorkspaceImportResult> ImportAsync(WorkspaceImportDocument document, CancellationToken ct) => throw new NotImplementedException();

        public Task<bool> CloseWorkspaceAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<JsonNode> GetSectionAsync(CharacterWorkspaceId id, string sectionId, CancellationToken ct) => throw new NotImplementedException();

        public Task<CharacterFileSummary> GetSummaryAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CharacterValidationResult> ValidateAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CharacterProfileSection> GetProfileAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            return Task.FromResult(new CharacterProfileSection(
                Name: "Loader Neo",
                Alias: "LOADER",
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
                MugshotCount: 0));
        }

        public Task<CharacterProgressSection> GetProgressAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            return Task.FromResult(new CharacterProgressSection(
                Karma: 9m,
                Nuyen: 1000m,
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
                DepEnabled: false));
        }

        public Task<CharacterSkillsSection> GetSkillsAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            return Task.FromResult(new CharacterSkillsSection(
                Count: 1,
                KnowledgeCount: 0,
                Skills:
                [
                    new CharacterSkillSummary(
                        Guid: "skill-1",
                        Suid: string.Empty,
                        Category: "Combat",
                        IsKnowledge: false,
                        BaseValue: 4,
                        KarmaValue: 0,
                        Specializations: ["Pistols"])
                ]));
        }

        public Task<CharacterRulesSection> GetRulesAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            return Task.FromResult(new CharacterRulesSection(
                GameEdition: "SR5",
                Settings: "default.xml",
                GameplayOption: "Standard",
                GameplayOptionQualityLimit: 25,
                MaxNuyen: 10,
                MaxKarma: 25,
                ContactMultiplier: 3,
                BannedWareGrades: []));
        }

        public Task<CharacterBuildSection> GetBuildAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            return Task.FromResult(new CharacterBuildSection(
                BuildMethod: "Priority",
                PriorityMetatype: "C,2",
                PriorityAttributes: "E,0",
                PrioritySpecial: "A,4",
                PrioritySkills: "B,3",
                PriorityResources: "D,1",
                PriorityTalent: "Mundane",
                SumToTen: 10,
                Special: 1,
                TotalSpecial: 4,
                TotalAttributes: 20,
                ContactPoints: 15,
                ContactPointsUsed: 8));
        }

        public Task<CharacterMovementSection> GetMovementAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            return Task.FromResult(new CharacterMovementSection(
                Walk: "10/25",
                Run: "20/50",
                Sprint: "40/100",
                WalkAlt: "10/25",
                RunAlt: "20/50",
                SprintAlt: "40/100",
                PhysicalCmFilled: 0,
                StunCmFilled: 0));
        }

        public Task<CharacterAwakeningSection> GetAwakeningAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            return Task.FromResult(new CharacterAwakeningSection(
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

        public Task<CommandResult<CharacterProfileSection>> UpdateMetadataAsync(CharacterWorkspaceId id, UpdateWorkspaceMetadata command, CancellationToken ct) => throw new NotImplementedException();

        public Task<CommandResult<WorkspaceSaveReceipt>> SaveAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CommandResult<WorkspaceDownloadReceipt>> DownloadAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CommandResult<WorkspaceExportReceipt>> ExportAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();

        public Task<CommandResult<WorkspacePrintReceipt>> PrintAsync(CharacterWorkspaceId id, CancellationToken ct) => throw new NotImplementedException();
    }
}
