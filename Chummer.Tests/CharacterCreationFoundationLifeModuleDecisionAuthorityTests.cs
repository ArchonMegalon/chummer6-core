using System.Security.Cryptography;
using System.Text;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationFoundationLifeModuleDecisionAuthorityTests
{
    [TestMethod]
    public void Load_projects_only_exact_sr5_life_module_foundation_authority()
    {
        CharacterWorkspaceId workspaceId = new("workspace-origin-authority");
        var store = new InMemoryWorkspaceStore();
        Assert.IsTrue(store.CreateWorkspaceDocument(
            workspaceId,
            new WorkspaceDocument("<character />", RulesetDefaults.Sr5)).Success);
        CharacterCreationFoundationState state = CreateState(workspaceId, RulesetDefaults.Sr5,
            CharacterCreationBuildMethods.LifeModules);
        var foundation = new FakeFoundation(state, CreatePreview(state));
        var authority = new CharacterCreationFoundationLifeModuleDecisionAuthority(
            store,
            foundation,
            new FakeCharacterFiles(),
            () => "en-US");

        LifeModuleDecisionAuthorityResult<LifeModuleDecisionAuthorityStep> result =
            authority.Load(workspaceId.Value);

        Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, result.Outcome);
        Assert.IsNotNull(result.Value);
        Assert.AreEqual(RulesetDefaults.Sr5, result.Value.RulesetId);
        Assert.HasCount(1, result.Value.LegalChoices);
        Assert.IsTrue(result.Value.LegalChoices[0].IsLegal);
        Assert.HasCount(1, result.Value.LegalChoices[0].MechanicsPreview.Items);
        Assert.IsTrue(result.Value.LegalChoices[0].MechanicsPreview.KarmaIsExact);
        Assert.HasCount(1, foundation.PreviewRequests);

        foundation.State = CreateState(workspaceId, RulesetDefaults.Sr6,
            CharacterCreationBuildMethods.LifeModules);
        Assert.AreEqual(
            LifeModuleOriginDossierOutcomes.Blocked,
            authority.Load(workspaceId.Value).Outcome);
        foundation.State = CreateState(workspaceId, RulesetDefaults.Sr5,
            CharacterCreationBuildMethods.Priority);
        Assert.AreEqual(
            LifeModuleOriginDossierOutcomes.Blocked,
            authority.Load(workspaceId.Value).Outcome);
    }

    private static CharacterCreationFoundationState CreateState(
        CharacterWorkspaceId workspaceId,
        string ruleset,
        string buildMethod)
    {
        string anchor = "lifemodules.xml#module:origin-module";
        var binding = new CharacterCreationFoundationBinding(
            workspaceId,
            1,
            0,
            Digest("raw"),
            CharacterCreationFoundationDigestSemantics.RawCharacterXmlSha256,
            Digest("source"),
            CharacterCreationFoundationDigestSemantics.RawSourceInputsSha256,
            false,
            ["RF"]);
        var metatype = new CharacterCreationLegalOption(
            Guid.Parse("11111111-1111-1111-1111-111111111111").ToString("D"),
            "Human",
            true,
            null,
            new Dictionary<string, string>(),
            [],
            [],
            ["metatypes.xml#human"],
            "SR5");
        var effect = new LifeModuleEffectProjectionDto(
            "effect-1",
            "active-skill",
            "Etiquette",
            "0",
            "1",
            null,
            0,
            [anchor],
            new Dictionary<string, string>(),
            "<addskill />",
            true,
            null);
        var module = new LifeModuleLegalOptionDto(
            "origin-module",
            LifeModuleJourneyStageOrders.Nationality,
            "Street origin",
            15,
            "RF",
            66,
            "The streets taught the runner to survive.",
            true,
            [],
            [],
            [effect],
            [],
            [anchor],
            CharacterCreationLifeModuleStageIds.Nationality,
            false,
            "15",
            true,
            "66",
            []);
        return new CharacterCreationFoundationState(
            CharacterCreationFoundationSchemas.SnapshotV1,
            binding,
            ruleset,
            "Human",
            buildMethod,
            false,
            [metatype],
            [module],
            new CharacterCreationBudgetState(
                CharacterCreationBudgetIds.LifeModules,
                "Life Modules",
                100,
                0,
                100,
                true,
                [],
                "karma"),
            null,
            CharacterCreationFoundationResumeStatuses.AuthorityRequired,
            [],
            Digest("snapshot"));
    }

    private static CharacterCreationFoundationPreview CreatePreview(
        CharacterCreationFoundationState state)
    {
        LifeModuleLegalOptionDto module = state.NationalityOptions[0];
        LifeModuleEffectProjectionDto effect = module.Effects[0];
        return new CharacterCreationFoundationPreview(
            CharacterCreationFoundationSchemas.PreviewV1,
            state.Binding,
            "Human",
            new CharacterCreationFoundationSelection(module.ModuleId, null),
            module,
            null,
            [],
            new Dictionary<string, string>(),
            state.LifeModuleBudget,
            new CharacterCreationChoiceCost(CharacterCreationBudgetIds.LifeModules, 15, "karma"),
            state.LifeModuleBudget with { Used = 15, Remaining = 85 },
            [new CharacterCreationFoundationDiffEntry(
                effect.EffectId,
                effect.Domain,
                effect.TargetId,
                effect.BeforeValue,
                effect.AfterValue,
                CharacterCreationFoundationDiffPhases.DraftLedger,
                false,
                true,
                true,
                [],
                effect.SourceAnchorIds)],
            [],
            true,
            true,
            true,
            false,
            Digest("preview"));
    }

    private static string Digest(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class FakeCharacterFiles : ICharacterFileQueries
    {
        public CharacterFileSummary ParseSummary(CharacterDocument document)
            => new("Runner", "Neon", "Human", CharacterCreationBuildMethods.LifeModules,
                string.Empty, string.Empty, 0, 0, false);

        public CharacterValidationResult Validate(CharacterDocument document)
            => new(true, []);
    }

    private sealed class FakeFoundation : ICharacterCreationFoundationService
    {
        private readonly CharacterCreationFoundationPreview _preview;

        public FakeFoundation(
            CharacterCreationFoundationState state,
            CharacterCreationFoundationPreview preview)
        {
            State = state;
            _preview = preview;
        }

        public CharacterCreationFoundationState State { get; set; }

        public List<CharacterCreationFoundationPreviewRequest> PreviewRequests { get; } = [];

        public CharacterCreationFoundationResult<CharacterCreationFoundationState> Load(
            CharacterCreationFoundationLoadRequest request)
            => new(CharacterCreationFoundationOutcomes.Success, State, []);

        public CharacterCreationFoundationResult<CharacterCreationFoundationPreview> Preview(
            CharacterCreationFoundationPreviewRequest request)
        {
            PreviewRequests.Add(request);
            return new(CharacterCreationFoundationOutcomes.Success, _preview with
            {
                Binding = State.Binding
            }, []);
        }

        public CharacterCreationFoundationResult<CharacterCreationFoundationApplyReceipt> Confirm(
            CharacterCreationFoundationConfirmRequest request)
            => new(CharacterCreationFoundationOutcomes.Blocked, null,
                [LifeModuleOriginDossierBlockers.AuthorityInvalid]);

        public CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationPreview> PreviewFinalization(
            CharacterCreationFoundationFinalizationPreviewRequest request)
            => new(CharacterCreationFoundationOutcomes.Blocked, null, []);

        public CharacterCreationFoundationResult<CharacterCreationFoundationFinalizationReceipt> ConfirmFinalization(
            CharacterCreationFoundationFinalizationConfirmRequest request)
            => new(CharacterCreationFoundationOutcomes.Blocked, null, []);
    }
}
