using Chummer.Application.BuildGhost;
using Chummer.Contracts.BuildGhost;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace Chummer.BuildGhost.Tests;

[TestClass]
[DoNotParallelize]
public sealed class BuildGhostAnalysisServiceTests
{
    private static readonly IReadOnlyDictionary<string, string> LocalizedFallbacks =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["en-US"] = "Rook is using Chummer's grounded fallback.",
            ["de-DE"] = "Rook verwendet Chummers belegte Rückfallantwort.",
            ["fr-FR"] = "Rook utilise la réponse de repli fondée de Chummer.",
            ["ja-JP"] = "ルークは Chummer の根拠付きフォールバックを使用しています。",
            ["pt-BR"] = "Rook está usando a resposta alternativa fundamentada do Chummer.",
            ["zh-CN"] = "Rook 正在使用 Chummer 的有依据后备答复。"
        };

    [TestMethod]
    public void Same_semantic_input_produces_the_same_packet_and_digest()
    {
        DefaultBuildGhostAnalysisService service = new();
        BuildGhostAnalysisRequest request = CreateRequest();

        BuildGhostAnalysisPacket first = service.Analyze(request);
        BuildGhostAnalysisPacket second = service.Analyze(request with
        {
            SupportedLocales = request.SupportedLocales.Reverse().ToArray(),
            Strategies = request.Strategies.Reverse().ToArray(),
            SourceAnchors = request.SourceAnchors.Reverse().ToArray(),
            Runner = request.Runner with { Facts = request.Runner.Facts.Reverse().ToArray() }
        });

        Assert.AreEqual(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        StringAssert.StartsWith(first.InputDigest, "sha256:");
        StringAssert.StartsWith(first.PacketDigest, "sha256:");
        Assert.AreNotEqual(first.InputDigest, first.PacketDigest);
    }

    [TestMethod]
    public void Every_authority_binding_changes_the_packet_digest()
    {
        DefaultBuildGhostAnalysisService service = new();
        BuildGhostAnalysisRequest request = CreateRequest();
        string baseline = service.Analyze(request).PacketDigest;
        BuildGhostFact changedFact = request.Runner.Facts[0] with { Value = "15" };

        BuildGhostAnalysisRequest[] changed =
        [
            request with { RuntimeFingerprint = "runtime:changed" },
            request with { WorkspaceRevision = request.WorkspaceRevision + 1 },
            request with { SourceDigest = "sha256:source-changed" },
            request with { Locale = "de-DE", DeterministicFallbackText = LocalizedFallbacks["de-DE"] },
            request with { Runner = request.Runner with { Facts = [changedFact, .. request.Runner.Facts.Skip(1)] } },
            request with { RuleEnvironment = request.RuleEnvironment with { ActiveSourcebookIds = ["core", "data-trails"] } },
            request with { RuleEnvironment = request.RuleEnvironment with { CustomDataFingerprint = "custom:changed" } },
            request with { RuleEnvironment = request.RuleEnvironment with { GmPolicyFingerprint = "gm:changed" } },
            request with { Group = request.Group! with { GroupRevision = request.Group!.GroupRevision + 1 } },
            request with { Group = request.Group! with { MembershipDigest = "sha256:membership-changed" } }
        ];

        foreach (BuildGhostAnalysisRequest mutation in changed)
        {
            Assert.AreNotEqual(baseline, service.Analyze(mutation).PacketDigest);
        }
    }

    [TestMethod]
    public void Advice_changes_when_the_triggering_build_fact_changes()
    {
        DefaultBuildGhostAnalysisService service = new();
        BuildGhostAnalysisRequest request = CreateRequest();
        BuildGhostAnalysisPacket baseline = service.Analyze(request);
        BuildGhostAnalysisPacket changed = service.Analyze(request with
        {
            Runner = request.Runner with
            {
                Facts = request.Runner.Facts.Where(static fact => fact.FactId != "fact:matrix-pool").ToArray()
            }
        });

        Assert.IsTrue(baseline.Tips.Any(static tip => tip.StrategyId == "strategy:matrix-breakpoint"));
        Assert.IsFalse(changed.Tips.Any(static tip => tip.StrategyId == "strategy:matrix-breakpoint"));
        Assert.IsTrue(changed.Warnings.Any(static warning => warning.FactId.Contains("matrix-breakpoint", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Three_grounded_variant_shapes_are_preview_only_and_preserve_exact_deltas()
    {
        BuildGhostAnalysisPacket packet = new DefaultBuildGhostAnalysisService().Analyze(CreateRequest());

        CollectionAssert.AreEqual(
            new[]
            {
                BuildGhostVariantShapes.ConservativeRepair,
                BuildGhostVariantShapes.RoleFocusedSpecialization,
                BuildGhostVariantShapes.BalancedHybrid
            },
            packet.Variants.Select(static variant => variant.Shape).ToArray());
        Assert.IsTrue(packet.Variants.All(static variant => variant.Validation.Status == BuildGhostVariantValidationStatuses.Available));
        Assert.IsTrue(packet.Variants.All(static variant => variant.ApplyPreview is { PreviewOnly: true, RequiresExplicitReview: true }));
        Assert.IsTrue(packet.AllowedSuggestedActions.All(static action => action.ActionType == BuildGhostActionTypes.PreviewBuildVariant));
        Assert.IsTrue(packet.Variants.SelectMany(static variant => variant.Deltas)
            .Any(static delta => delta.DeltaId == "delta:cracking" && delta.BeforeValue == "12" && delta.AfterValue == "14"));
    }

    [TestMethod]
    public void Invalid_variants_remain_visible_and_incomplete_drug_advice_is_omitted()
    {
        DefaultBuildGhostAnalysisService service = new();
        BuildGhostAnalysisRequest request = CreateRequest();
        OptimizationStrategyProjection incompleteDrug = request.Strategies.Single(static strategy => strategy.DrugProjection is not null) with
        {
            DrugProjection = request.Strategies.Single(static strategy => strategy.DrugProjection is not null).DrugProjection! with { Duration = string.Empty }
        };
        BuildGhostAnalysisPacket packet = service.Analyze(request with
        {
            Strategies = [request.Strategies[0], incompleteDrug]
        });

        Assert.HasCount(3, packet.Variants);
        Assert.IsTrue(packet.Variants.Any(static variant => variant.Validation.Status == BuildGhostVariantValidationStatuses.Rejected));
        Assert.IsTrue(packet.Variants.Where(static variant => variant.Validation.Status != BuildGhostVariantValidationStatuses.Available)
            .All(static variant => variant.ApplyPreview is null));
        Assert.IsFalse(packet.OptimizationStrategies.Any(static strategy => strategy.DrugProjection is not null));
        Assert.IsTrue(packet.Warnings.Any(static warning => warning.Value.Contains("drug or temporary-buff mechanics are incomplete", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Complete_drug_projection_keeps_benefit_cost_reliability_and_every_required_risk_fact()
    {
        BuildGhostAnalysisPacket packet = new DefaultBuildGhostAnalysisService().Analyze(CreateRequest());
        BuildGhostDrugStrategyProjection drug = packet.OptimizationStrategies
            .Single(static strategy => strategy.DrugProjection is not null)
            .DrugProjection!;

        Assert.AreEqual("drug:psyche", drug.ItemId);
        Assert.AreEqual("core", drug.SourceId);
        Assert.AreEqual("1 dose", drug.Dose);
        Assert.AreEqual("10 minutes", drug.Onset);
        Assert.AreEqual("(12 - Body) hours, minimum 1", drug.Duration);
        Assert.AreEqual("-1 Logic and -1 Intuition during crash", drug.CrashAndAfterEffects);
        Assert.AreEqual("Psychological Addiction Test (Logic + Willpower)", drug.AddictionTest);
        Assert.AreEqual(2, drug.AddictionThreshold);
        Assert.AreEqual("does not stack with itself", drug.StackingInteraction);
        Assert.AreEqual("Restricted", drug.Legality);
        Assert.AreEqual("4R", drug.Availability);
        Assert.AreEqual(200m, drug.Price);
        Assert.AreEqual("¥", drug.Currency);
        Assert.AreEqual("track tolerance and dependency after repeated use", drug.ToleranceAndDependency);
        Assert.AreEqual("pool=12", drug.BaselineCalculationTrace.Single());
        Assert.AreEqual("pool=14", drug.BoostedCalculationTrace.Single());
    }

    [TestMethod]
    public void Authorized_group_projection_detects_first_aid_and_canonical_language_gaps_with_visible_scope_wording()
    {
        DefaultBuildGhostAnalysisService service = new();
        BuildGhostAnalysisRequest request = CreateRequest();
        BuildGhostAnalysisPacket packet = service.Analyze(request);
        GroupBuildCapabilityProjection group = packet.GroupCapabilityPosture!;

        CollectionAssert.Contains(group.MissingCapabilityIds.ToArray(), "capability:first-aid");
        CollectionAssert.Contains(group.MissingCapabilityIds.ToArray(), "language:sperethiel");
        StringAssert.Contains(group.Conclusions.Single(static conclusion => conclusion.CapabilityId == "capability:first-aid").Wording, "No visible member");
        StringAssert.Contains(group.Conclusions.Single(static conclusion => conclusion.CapabilityId == "language:sperethiel").Wording, "Sperethiel speaker");
        CollectionAssert.Contains(group.RedundantCapabilityIds.ToArray(), "capability:matrix");

        BuildGhostAnalysisPacket denied = service.Analyze(request with { Group = request.Group! with { ConsentGranted = false } });
        Assert.AreEqual("consent-required", denied.GroupCapabilityPosture!.VisibilityPosture);
        Assert.IsEmpty(denied.GroupCapabilityPosture.VisibleMembers);
        Assert.IsNull(denied.GroupCapabilityPosture.GroupId);
        Assert.IsFalse(denied.PacketDigest.Contains("member-visible", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Group_projection_requires_consent_revision_membership_digest_and_distinct_visible_members()
    {
        DefaultBuildGhostAnalysisService service = new();
        BuildGhostAnalysisRequest request = CreateRequest();
        BuildGhostGroupInput group = request.Group!;
        BuildGhostGroupInput[] invalidBindings =
        [
            group with { GroupId = null },
            group with { GroupRevision = null },
            group with { GroupRevision = -1 },
            group with { MembershipDigest = " " },
            group with { VisibleMembers = [group.VisibleMembers[0], group.VisibleMembers[0]] }
        ];

        foreach (BuildGhostGroupInput invalid in invalidBindings)
        {
            GroupBuildCapabilityProjection projection = service.Analyze(request with { Group = invalid }).GroupCapabilityPosture!;
            Assert.AreEqual("binding-required", projection.VisibilityPosture);
            Assert.IsNull(projection.GroupId);
            Assert.IsNull(projection.GroupRevision);
            Assert.IsNull(projection.MembershipDigest);
            Assert.IsEmpty(projection.VisibleMembers);
            Assert.IsEmpty(projection.Conclusions);
        }

        BuildGhostAnalysisPacket deniedA = service.Analyze(request with
        {
            Group = group with { ConsentGranted = false }
        });
        BuildGhostAnalysisPacket deniedB = service.Analyze(request with
        {
            Group = group with
            {
                ConsentGranted = false,
                GroupId = "different-hidden-group",
                GroupRevision = 99,
                MembershipDigest = "sha256:different-hidden-membership",
                VisibleMembers = [new("different-hidden-member", [])]
            }
        });
        Assert.AreEqual(deniedA.InputDigest, deniedB.InputDigest);
        Assert.AreEqual(deniedA.PacketDigest, deniedB.PacketDigest);
    }

    [TestMethod]
    public void Provider_cannot_introduce_unpacketized_facts_rules_strategies_variants_members_sources_actions_or_links()
    {
        DefaultBuildGhostAnalysisService service = new();
        BuildGhostAnalysisPacket packet = service.Analyze(CreateRequest());
        BuildGhostProviderAnswer malicious = new(
            Schema: BuildGhostContractVersions.ProviderAnswerV1,
            RequestId: "request-1",
            PacketDigest: packet.PacketDigest,
            Locale: packet.Locale,
            Text: "Invented answer",
            ReferencedFactIds: ["fact:not-present"],
            ReferencedStrategyIds: ["strategy:not-present"],
            ReferencedRuleExplanationIds: ["rule:not-present"],
            ReferencedVariantIds: ["variant:not-present"],
            ReferencedMemberRefs: ["member:hidden"],
            ReferencedSourceAnchorIds: ["source:not-present"],
            SuggestedActionIds: ["apply-now"],
            Links: ["https://unsupported.invalid"]);

        BuildGhostProviderValidationResult rejected = service.ValidateProviderAnswer(packet, malicious);

        Assert.IsFalse(rejected.Accepted);
        Assert.AreEqual("deterministic-fallback", rejected.OutcomeStatus);
        Assert.AreEqual(packet.DeterministicFallbackText, rejected.SafeText);
        Assert.HasCount(8, rejected.RejectionReasons);

        BuildGhostProviderAnswer grounded = malicious with
        {
            Text = "Grounded answer",
            ReferencedFactIds = [packet.Runner.Facts[0].FactId],
            ReferencedStrategyIds = [packet.OptimizationStrategies[0].StrategyId],
            ReferencedRuleExplanationIds = [packet.RuleExplanations[0].ExplanationId],
            ReferencedVariantIds = [packet.Variants[0].VariantId],
            ReferencedMemberRefs = [packet.GroupCapabilityPosture!.VisibleMembers[0].MemberRef],
            ReferencedSourceAnchorIds = [packet.SourceAnchors[0].AnchorId],
            SuggestedActionIds = [packet.AllowedSuggestedActions[0].ActionId],
            Links = [packet.RuleExplanations[0].SourceLookupRoute!]
        };
        BuildGhostProviderValidationResult accepted = service.ValidateProviderAnswer(packet, grounded);
        Assert.IsTrue(accepted.Accepted);
        Assert.AreEqual("Grounded answer", accepted.SafeText);
    }

    [TestMethod]
    [DataRow("en-US")]
    [DataRow("de-DE")]
    [DataRow("fr-FR")]
    [DataRow("ja-JP")]
    [DataRow("pt-BR")]
    [DataRow("zh-CN")]
    public void Every_canonical_locale_keeps_requested_locale_and_localized_deterministic_fallback(string locale)
    {
        DefaultBuildGhostAnalysisService service = new();
        BuildGhostAnalysisPacket packet = service.Analyze(CreateRequest(locale));
        BuildGhostProviderValidationResult fallback = service.ValidateProviderAnswer(packet, new BuildGhostProviderAnswer(
            Schema: BuildGhostContractVersions.ProviderAnswerV1,
            RequestId: "request-locale",
            PacketDigest: "wrong",
            Locale: locale,
            Text: "provider text",
            ReferencedFactIds: [],
            ReferencedStrategyIds: [],
            ReferencedRuleExplanationIds: [],
            ReferencedVariantIds: [],
            ReferencedMemberRefs: [],
            ReferencedSourceAnchorIds: [],
            SuggestedActionIds: [],
            Links: []));

        Assert.AreEqual(locale, packet.Locale);
        Assert.AreEqual(locale, packet.LocaleFallbackChain[0]);
        CollectionAssert.Contains(packet.SupportedLocales.ToArray(), locale);
        using JsonDocument serialized = JsonDocument.Parse(JsonSerializer.Serialize(
            packet,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.IsTrue(serialized.RootElement.GetProperty("supportedLocales")
            .EnumerateArray()
            .Any(value => string.Equals(value.GetString(), locale, StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(LocalizedFallbacks[locale], fallback.SafeText);
        Assert.IsFalse(packet.Warnings.Any(static warning => warning.FactId == "buildghost.locale.unsupported"));
    }

    [TestMethod]
    public void Current_workspace_sections_materialize_exact_affordable_variants_and_rule_explanations()
    {
        BuildGhostAnalysisPacket packet = BuildGhostWorkspaceProjectionFactory.Analyze(
            CreateWorkspaceContext(),
            CreateProfile(),
            CreateProgress(),
            CreateRules(),
            CreateBuild(),
            CreateSkills(),
            CreateAttributes(),
            CreateAwakening());

        Assert.AreEqual("workspace-runner", packet.WorkspaceId);
        Assert.AreEqual(12, packet.WorkspaceRevision);
        Assert.AreEqual("sha256:workspace-source", packet.SourceDigest);
        Assert.IsTrue(packet.Runner.Facts.Any(static fact => fact.FactId == "fact:skill:skill-hacking" && fact.Value == "6"));
        Assert.IsTrue(packet.ExpertiseTags.Contains("matrix-specialist", StringComparer.Ordinal));
        Assert.HasCount(3, packet.Variants);
        Assert.AreEqual(1, packet.Variants.Count(static variant => variant.Validation.Status == BuildGhostVariantValidationStatuses.Available));
        Assert.AreEqual(1, packet.Variants.Count(static variant => variant.ApplyPreview is { PreviewOnly: true, RequiresExplicitReview: true }));
        Assert.HasCount(1, packet.AllowedSuggestedActions);
        Assert.IsTrue(packet.Variants.SelectMany(static variant => variant.Deltas)
            .Any(static delta => delta.DeltaId == "delta:attribute:logic"
                && delta.BeforeValue == "6"
                && delta.AfterValue == "7"
                && delta.NumericDelta == 1m));
        BuildGhostBuildVariant focused = packet.Variants
            .Single(static variant => variant.Shape == BuildGhostVariantShapes.RoleFocusedSpecialization);
        BuildGhostVariantDelta composedKarma = focused.Deltas
            .Single(static delta => delta.DeltaId == "delta:composed:karma:resource:karma");
        Assert.AreEqual("50", composedKarma.BeforeValue);
        Assert.AreEqual("-10", composedKarma.AfterValue);
        Assert.AreEqual(-60m, composedKarma.NumericDelta);
        Assert.IsTrue(focused.Validation.Blockers.Contains("resource:karma would fall below zero", StringComparer.Ordinal));
        Assert.IsNull(focused.ApplyPreview);
        BuildGhostRuleExplanation explanation = packet.RuleExplanations
            .Single(static item => item.ExplanationId == "explain:workspace:attribute-upgrade:logic");
        Assert.AreEqual("resolved", explanation.Status);
        StringAssert.Contains(explanation.Explanation, "35 Karma");
    }

    [TestMethod]
    public void Workspace_strategy_generation_checks_the_canonical_progress_balance_as_well_as_attribute_projection()
    {
        BuildGhostAnalysisPacket packet = BuildGhostWorkspaceProjectionFactory.Analyze(
            CreateWorkspaceContext(),
            CreateProfile(),
            CreateProgress() with { Karma = 10m },
            CreateRules(),
            CreateBuild(),
            CreateSkills(),
            CreateAttributes(),
            CreateAwakening());

        Assert.IsEmpty(packet.OptimizationStrategies);
        Assert.IsEmpty(packet.AllowedSuggestedActions);
        Assert.IsTrue(packet.Variants.All(static variant => variant.ApplyPreview is null));
    }

    [TestMethod]
    public void Workspace_variants_compose_shared_resource_deltas_when_the_total_is_affordable()
    {
        BuildGhostAnalysisPacket packet = BuildGhostWorkspaceProjectionFactory.Analyze(
            CreateWorkspaceContext(),
            CreateProfile(),
            CreateProgress() with { Karma = 100m },
            CreateRules(),
            CreateBuild(),
            CreateSkills(),
            CreateAttributes(),
            CreateAwakening());

        Assert.IsTrue(packet.Variants.All(static variant =>
            variant.Validation.Status == BuildGhostVariantValidationStatuses.Available
            && variant.ApplyPreview is { PreviewOnly: true, RequiresExplicitReview: true }));
        BuildGhostVariantDelta focusedKarma = packet.Variants
            .Single(static variant => variant.Shape == BuildGhostVariantShapes.RoleFocusedSpecialization)
            .Deltas.Single(static delta => delta.DeltaId == "delta:composed:karma:resource:karma");
        BuildGhostVariantDelta balancedKarma = packet.Variants
            .Single(static variant => variant.Shape == BuildGhostVariantShapes.BalancedHybrid)
            .Deltas.Single(static delta => delta.DeltaId == "delta:composed:karma:resource:karma");
        Assert.AreEqual("40", focusedKarma.AfterValue);
        Assert.AreEqual("20", balancedKarma.AfterValue);
        Assert.HasCount(3, packet.AllowedSuggestedActions);
    }

    [TestMethod]
    public void Current_workspace_projection_digest_changes_with_exact_saved_attribute_truth()
    {
        BuildGhostWorkspaceAnalysisContext context = CreateWorkspaceContext();
        CharacterAttributeDetailsSection attributes = CreateAttributes();
        BuildGhostAnalysisPacket baseline = BuildGhostWorkspaceProjectionFactory.Analyze(
            context,
            CreateProfile(),
            CreateProgress(),
            CreateRules(),
            CreateBuild(),
            CreateSkills(),
            attributes,
            CreateAwakening());
        CharacterAttributeDetailSummary changedLogic = attributes.Attributes
            .Single(static attribute => attribute.Name == "Logic") with
        {
            BaseValue = 5,
            TotalValue = 5,
            UpgradeKarmaCost = 30
        };
        CharacterAttributeDetailsSection changedAttributes = attributes with
        {
            Attributes = attributes.Attributes
                .Select(attribute => attribute.Name == "Logic" ? changedLogic : attribute)
                .ToArray()
        };

        BuildGhostAnalysisPacket changed = BuildGhostWorkspaceProjectionFactory.Analyze(
            context,
            CreateProfile(),
            CreateProgress(),
            CreateRules(),
            CreateBuild(),
            CreateSkills(),
            changedAttributes,
            CreateAwakening());

        Assert.AreNotEqual(baseline.PacketDigest, changed.PacketDigest);
        Assert.AreEqual("5", changed.Runner.Facts.Single(static fact => fact.FactId == "fact:attribute:logic").Value);
    }

    private static BuildGhostWorkspaceAnalysisContext CreateWorkspaceContext()
        => new(
            OwnerId: "owner-current",
            CampaignId: null,
            RulesetId: "sr5",
            RuntimeFingerprint: "runtime:sr5:current",
            WorkspaceId: "workspace-runner",
            WorkspaceRevision: 12,
            SourceDigest: "sha256:workspace-source",
            Locale: "en-US",
            LocaleFallbackChain: ["en-US"],
            SupportedLocales: LocalizedFallbacks.Keys.ToArray(),
            RuleEnvironment: new BuildGhostRuleEnvironment(
                ActiveSourcebookIds: ["core"],
                SourcebookFingerprint: "sha256:books",
                CustomDataPosture: "none",
                CustomDataFingerprint: "sha256:custom-none",
                GmPolicyFingerprint: "sha256:gm-default",
                GmConstraintIds: []),
            RequestedGoal: "Compare exact current-runner improvements.",
            Group: null,
            DeterministicFallbackText: LocalizedFallbacks["en-US"]);

    private static CharacterProfileSection CreateProfile()
        => new(
            Name: "Workspace Runner",
            Alias: "Current",
            PlayerName: "Player",
            Metatype: "Ork",
            Metavariant: string.Empty,
            Sex: string.Empty,
            Age: string.Empty,
            Height: string.Empty,
            Weight: string.Empty,
            Hair: string.Empty,
            Eyes: string.Empty,
            Skin: string.Empty,
            Concept: "Decker",
            Description: string.Empty,
            Background: string.Empty,
            CreatedVersion: "5.0",
            AppVersion: "5.0",
            BuildMethod: "Priority",
            GameplayOption: "Standard",
            Created: true,
            Adept: false,
            Magician: false,
            Technomancer: false,
            AI: false,
            MainMugshotIndex: 0,
            MugshotCount: 0);

    private static CharacterProgressSection CreateProgress()
        => new(
            Karma: 50m,
            Nuyen: 9000m,
            StartingNuyen: 5000m,
            StreetCred: 0,
            Notoriety: 0,
            PublicAwareness: 0,
            BurntStreetCred: 0,
            BuildKarma: 0,
            TotalAttributes: 0,
            TotalSpecial: 0,
            PhysicalCmFilled: 0,
            StunCmFilled: 0,
            TotalEssence: 5.4m,
            InitiateGrade: 0,
            SubmersionGrade: 0,
            MagEnabled: false,
            ResEnabled: false,
            DepEnabled: false);

    private static CharacterRulesSection CreateRules()
        => new(
            GameEdition: "SR5",
            Settings: "Standard",
            GameplayOption: "Standard",
            GameplayOptionQualityLimit: 25,
            MaxNuyen: 10,
            MaxKarma: 7,
            ContactMultiplier: 3,
            BannedWareGrades: []);

    private static CharacterBuildSection CreateBuild()
        => new(
            BuildMethod: "Priority",
            PriorityMetatype: "C",
            PriorityAttributes: "B",
            PrioritySpecial: "E",
            PrioritySkills: "A",
            PriorityResources: "D",
            PriorityTalent: string.Empty,
            SumToTen: 0,
            Special: 0,
            TotalSpecial: 0,
            TotalAttributes: 0,
            ContactPoints: 0,
            ContactPointsUsed: 0);

    private static CharacterSkillsSection CreateSkills()
        => new(
            Count: 2,
            KnowledgeCount: 0,
            Skills:
            [
                new CharacterSkillSummary(
                    Guid: "skill-hacking-guid",
                    Suid: "skill-hacking",
                    Category: "Cracking",
                    IsKnowledge: false,
                    BaseValue: 5,
                    KarmaValue: 1,
                    Specializations: [],
                    Name: "Hacking"),
                new CharacterSkillSummary(
                    Guid: "skill-first-aid-guid",
                    Suid: "skill-first-aid",
                    Category: "Technical",
                    IsKnowledge: false,
                    BaseValue: 1,
                    KarmaValue: 0,
                    Specializations: [],
                    Name: "First Aid")
            ]);

    private static CharacterAttributeDetailsSection CreateAttributes()
        => new(
            Count: 3,
            Attributes:
            [
                Attribute("Body", 3, 20),
                Attribute("Intuition", 4, 25),
                Attribute("Logic", 6, 35)
            ]);

    private static CharacterAttributeDetailSummary Attribute(string name, int total, int cost)
        => new(
            Name: name,
            MetatypeMin: 1,
            MetatypeMax: 9,
            MetatypeAugMax: 13,
            BaseValue: total,
            KarmaValue: 0,
            TotalValue: total,
            MetatypeCategory: "standard")
        {
            Created = true,
            AvailableKarma = 50,
            UpgradeKarmaCost = cost,
            CanCareerUpgrade = true
        };

    private static CharacterAwakeningSection CreateAwakening()
        => new(
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
            AiAdvancedProgramLimit: 0);

    private static BuildGhostAnalysisRequest CreateRequest(string locale = "en-US")
    {
        BuildGhostSourceAnchor matrixAnchor = new(
            AnchorId: "anchor:matrix-pool",
            RulesetId: "sr5",
            SourceId: "core",
            Page: 226,
            ActiveCharacterSettings: new Dictionary<string, string> { ["maxAvailability"] = "12" },
            SavedValues: new Dictionary<string, string> { ["logic"] = "6", ["cracking"] = "6" },
            CalculationTrace: ["Logic 6 + Cracking 6 = 12"],
            LocalizedSourceName: "Core Rulebook",
            RuleId: "matrix-test");
        BuildGhostSourceAnchor firstAidAnchor = new(
            AnchorId: "anchor:first-aid",
            RulesetId: "sr5",
            SourceId: "core",
            Page: 205,
            ActiveCharacterSettings: new Dictionary<string, string>(),
            SavedValues: new Dictionary<string, string> { ["firstAid"] = "0" },
            CalculationTrace: ["First Aid rating 0"],
            LocalizedSourceName: "Core Rulebook",
            RuleId: "first-aid");
        BuildGhostSourceAnchor drugAnchor = new(
            AnchorId: "anchor:psyche",
            RulesetId: "sr5",
            SourceId: "core",
            Page: 412,
            ActiveCharacterSettings: new Dictionary<string, string> { ["drugRestrictions"] = "standard" },
            SavedValues: new Dictionary<string, string> { ["body"] = "3" },
            CalculationTrace: ["baseline pool=12", "boosted pool=14"],
            LocalizedSourceName: "Core Rulebook",
            RuleId: "drug:psyche");

        BuildGhostFact[] facts =
        [
            new("fact:matrix-pool", "strength", "Matrix pool", "12", 1m, [matrixAnchor.AnchorId]),
            new("fact:first-aid", "warning", "First Aid", "0", 1m, [firstAidAnchor.AnchorId]),
            new("fact:essence", "warning", "Essence", "5.2", 1m, [matrixAnchor.AnchorId])
        ];
        OptimizationStrategyProjection[] strategies =
        [
            new(
                StrategyId: "strategy:matrix-breakpoint",
                StrategyType: "dice-pool-breakpoint",
                ExpertiseTags: ["matrix-specialist"],
                Applicability: BuildGhostApplicabilityStatuses.ApplicableNow,
                TriggerFactIds: ["fact:matrix-pool"],
                ExpectedBenefit: "+2 Matrix dice",
                OpportunityCost: "12 Karma not available for secondary skills",
                Risk: "narrower non-Matrix coverage",
                Assumptions: ["active SR5 runtime"],
                Counterfactual: "keep the current 12-die pool and spend on a backup role",
                ShortTermBenefit: "reach a reliable 14-die Matrix pool",
                LongTermCeiling: "supports later specialization without changing persona",
                Dependencies: [],
                GmPolicyConflicts: [],
                SourceAnchorIds: [matrixAnchor.AnchorId],
                Deltas: [new("delta:cracking", "skill", "skill:cracking", "12", "14", 2m, "dice", [matrixAnchor.AnchorId])],
                Priority: 30),
            new(
                StrategyId: "strategy:datajack-efficiency",
                StrategyType: "ware-efficiency",
                ExpertiseTags: ["matrix-specialist"],
                Applicability: BuildGhostApplicabilityStatuses.GmReview,
                TriggerFactIds: ["fact:essence"],
                ExpectedBenefit: "stable direct neural interface",
                OpportunityCost: "0.1 Essence and 1,000¥",
                Risk: "less Essence headroom",
                Assumptions: ["standard-grade ware"],
                Counterfactual: "retain the current wireless-only path",
                ShortTermBenefit: "secure the required interface",
                LongTermCeiling: "preserves upgrade compatibility",
                Dependencies: ["GM confirms availability"],
                GmPolicyConflicts: ["gm:ware-review"],
                SourceAnchorIds: [matrixAnchor.AnchorId],
                Deltas: [new("delta:datajack", "ware", "ware:datajack", null, "standard", null, null, [matrixAnchor.AnchorId])],
                Priority: 20),
            new(
                StrategyId: "strategy:first-aid-backup",
                StrategyType: "party-coverage",
                ExpertiseTags: ["support", "capability:first-aid"],
                Applicability: BuildGhostApplicabilityStatuses.ApplicableNow,
                TriggerFactIds: ["fact:first-aid"],
                ExpectedBenefit: "visible First Aid backup coverage",
                OpportunityCost: "skill points leave Matrix specialization",
                Risk: "shallower primary-role ceiling",
                Assumptions: ["group projection is consented"],
                Counterfactual: "leave recovery coverage visibly open",
                ShortTermBenefit: "close the visible First Aid gap",
                LongTermCeiling: "reliable backup support",
                Dependencies: [],
                GmPolicyConflicts: [],
                SourceAnchorIds: [firstAidAnchor.AnchorId],
                Deltas: [new("delta:first-aid", "skill", "capability:first-aid", "0", "2", 2m, "rating", [firstAidAnchor.AnchorId])],
                Priority: 10),
            new(
                StrategyId: "strategy:psyche-bounded",
                StrategyType: "drug-temporary-buff",
                ExpertiseTags: ["matrix-specialist"],
                Applicability: BuildGhostApplicabilityStatuses.GmReview,
                TriggerFactIds: ["fact:matrix-pool"],
                ExpectedBenefit: "temporary pool 12 to 14",
                OpportunityCost: "200¥ per dose",
                Risk: "crash and addiction exposure",
                Assumptions: ["no conflicting active effect"],
                Counterfactual: "use the unboosted 12-die baseline",
                ShortTermBenefit: "temporary 14-die Matrix pool",
                LongTermCeiling: "no permanent advancement ceiling change",
                Dependencies: ["dose available", "GM permits drug use"],
                GmPolicyConflicts: ["gm:drug-review"],
                SourceAnchorIds: [drugAnchor.AnchorId],
                Deltas: [new("delta:psyche", "drug", "drug:psyche", "pool=12", "pool=14", 2m, "dice", [drugAnchor.AnchorId])],
                DrugProjection: new BuildGhostDrugStrategyProjection(
                    ItemId: "drug:psyche",
                    SourceId: "core",
                    Dose: "1 dose",
                    Onset: "10 minutes",
                    Duration: "(12 - Body) hours, minimum 1",
                    CrashAndAfterEffects: "-1 Logic and -1 Intuition during crash",
                    AddictionTest: "Psychological Addiction Test (Logic + Willpower)",
                    AddictionThreshold: 2,
                    StackingInteraction: "does not stack with itself",
                    Legality: "Restricted",
                    Availability: "4R",
                    Price: 200m,
                    Currency: "¥",
                    ToleranceAndDependency: "track tolerance and dependency after repeated use",
                    ActiveGmRestrictionIds: ["gm:drug-review"],
                    BaselineCalculationTrace: ["pool=12"],
                    BoostedCalculationTrace: ["pool=14"]),
                Priority: 5)
        ];

        return new BuildGhostAnalysisRequest(
            OwnerId: "owner-1",
            CampaignId: "campaign-1",
            RulesetId: "sr5",
            RuntimeFingerprint: "runtime:sr5:test",
            WorkspaceId: "workspace-1",
            WorkspaceRevision: 42,
            SourceDigest: "sha256:source",
            Locale: locale,
            LocaleFallbackChain: ["en-US"],
            SupportedLocales: LocalizedFallbacks.Keys.ToArray(),
            RuleEnvironment: new BuildGhostRuleEnvironment(
                ActiveSourcebookIds: ["core"],
                SourcebookFingerprint: "books:core",
                CustomDataPosture: "none",
                CustomDataFingerprint: "custom:none",
                GmPolicyFingerprint: "gm:test",
                GmConstraintIds: ["gm:ware-review", "gm:drug-review"]),
            Runner: new BuildGhostRunnerProjection(
                CharacterId: "rook-test-runner",
                DisplayName: "Test Runner",
                CreationState: "career",
                ExpertiseTags: ["matrix-specialist"],
                Facts: facts,
                ResourceValues: new Dictionary<string, decimal> { ["essence"] = 5.2m, ["karma"] = 18m, ["nuyen"] = 8000m }),
            RequestedGoal: "Improve Matrix reliability without losing all team flexibility.",
            SourceAnchors: [matrixAnchor, firstAidAnchor, drugAnchor],
            Strategies: strategies,
            RuleExplanations:
            [
                new BuildGhostRuleExplanationInput(
                    ExplanationId: "explain:matrix-test",
                    RuleId: "matrix-test",
                    Question: "How is the Matrix pool calculated?",
                    DeterministicExplanation: "Logic 6 plus Cracking 6 produces 12 dice.",
                    SourceAnchorIds: [matrixAnchor.AnchorId],
                    Resolved: true,
                    SourceLookupRoute: "/rules/sr5/core/226")
            ],
            Group: new BuildGhostGroupInput(
                ConsentGranted: true,
                GroupId: "group-1",
                GroupRevision: 7,
                MembershipDigest: "sha256:membership",
                VisibleMembers:
                [
                    new("member-visible-1", [new("capability:matrix", "Matrix", "strong", 0.9m)]),
                    new("member-visible-2", [new("capability:matrix", "Matrix", "backup", 0.8m)])
                ],
                RequiredCapabilityIds: ["capability:first-aid", "capability:matrix", "language:sperethiel"],
                RequiredCapabilityDisplayNames: new Dictionary<string, string>
                {
                    ["capability:first-aid"] = "First Aid",
                    ["capability:matrix"] = "Matrix",
                    ["language:sperethiel"] = "Sperethiel"
                }),
            DeterministicFallbackText: LocalizedFallbacks[locale]);
    }
}
