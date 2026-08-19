using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.BuildGhost;

namespace Chummer.Application.BuildGhost;

public sealed class DefaultBuildGhostAnalysisService : IBuildGhostAnalysisService
{
    private static readonly string[] ForbiddenClaimsAndActions =
    [
        "invent-rule-or-legality-claim",
        "invent-source-or-calculation",
        "invent-runner-or-teammate-fact",
        "invent-variant-delta",
        "expose-hidden-group-data",
        "mutate-runner-directly",
        "apply-without-revision-and-digest-review",
        "silently-switch-provider-locale-model-or-voice"
    ];

    private static readonly JsonSerializerOptions DigestSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public BuildGhostAnalysisPacket Analyze(BuildGhostAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateBinding(request);

        BuildGhostAnalysisRequest normalizedRequest = Normalize(request);
        string inputDigest = ComputeDigest(normalizedRequest);
        IReadOnlyList<BuildGhostSourceAnchor> anchors = normalizedRequest.SourceAnchors;
        HashSet<string> anchorIds = anchors
            .Select(static anchor => anchor.AnchorId)
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<BuildGhostFact> visibleFacts = normalizedRequest.Runner.Facts
            .Where(static fact => fact.PlayerVisible)
            .ToArray();
        HashSet<string> factIds = visibleFacts
            .Select(static fact => fact.FactId)
            .ToHashSet(StringComparer.Ordinal);

        List<BuildGhostFact> generatedWarnings = [];
        if (!IsSupportedLocale(normalizedRequest.Locale, normalizedRequest.SupportedLocales))
        {
            generatedWarnings.Add(new BuildGhostFact(
                FactId: "buildghost.locale.unsupported",
                Category: "warning",
                Label: "Provider locale unavailable",
                Value: normalizedRequest.Locale,
                Confidence: 1m,
                SourceAnchorIds: []));
        }

        List<OptimizationStrategyProjection> structurallyValidStrategies = [];
        foreach (OptimizationStrategyProjection strategy in normalizedRequest.Strategies)
        {
            IReadOnlyList<string> failures = ValidateStrategy(strategy, factIds, anchorIds);
            if (failures.Count == 0)
            {
                structurallyValidStrategies.Add(strategy);
                continue;
            }

            generatedWarnings.Add(new BuildGhostFact(
                FactId: $"buildghost.strategy.{strategy.StrategyId}.omitted",
                Category: "warning",
                Label: "Strategy omitted",
                Value: string.Join("; ", failures),
                Confidence: 1m,
                SourceAnchorIds: strategy.SourceAnchorIds.Where(anchorIds.Contains).ToArray()));
        }

        IReadOnlyList<BuildGhostTip> tips = structurallyValidStrategies
            .Where(static strategy => !string.Equals(
                strategy.Applicability,
                BuildGhostApplicabilityStatuses.Unresolved,
                StringComparison.Ordinal))
            .Select(CreateTip)
            .OrderBy(static tip => tip.TipId, StringComparer.Ordinal)
            .ToArray();

        GroupBuildCapabilityProjection? groupProjection = CreateGroupProjection(normalizedRequest.Group);
        IReadOnlyList<BuildGhostBuildVariant> variants = CreateVariants(
            normalizedRequest,
            inputDigest,
            structurallyValidStrategies,
            groupProjection);
        IReadOnlyList<BuildGhostAllowedAction> allowedActions = variants
            .Where(static variant => variant.ApplyPreview is not null)
            .Select(variant => new BuildGhostAllowedAction(
                ActionId: variant.ApplyPreview!.ActionId,
                ActionType: BuildGhostActionTypes.PreviewBuildVariant,
                VariantId: variant.VariantId,
                RequiresExplicitReview: true,
                WorkspaceRevision: normalizedRequest.WorkspaceRevision,
                SourceDigest: normalizedRequest.SourceDigest))
            .OrderBy(static action => action.ActionId, StringComparer.Ordinal)
            .ToArray();
        IReadOnlyList<BuildGhostRuleExplanation> explanations = normalizedRequest.RuleExplanations
            .Select(explanation => CreateRuleExplanation(explanation, anchorIds))
            .OrderBy(static explanation => explanation.ExplanationId, StringComparer.Ordinal)
            .ToArray();
        IReadOnlyList<BuildGhostFact> allWarnings = visibleFacts
            .Where(static fact => string.Equals(fact.Category, "warning", StringComparison.OrdinalIgnoreCase))
            .Concat(generatedWarnings)
            .OrderBy(static fact => fact.FactId, StringComparer.Ordinal)
            .ToArray();

        BuildGhostAnalysisPacket packet = new(
            Schema: BuildGhostContractVersions.AnalysisV1,
            PersonaId: BuildGhostPersonaIds.Rook,
            AvatarId: BuildGhostPersonaIds.RookAvatar,
            VoiceId: BuildGhostPersonaIds.RookVoice,
            DisplayName: "Rook",
            OwnerId: normalizedRequest.OwnerId,
            CampaignId: normalizedRequest.CampaignId,
            RulesetId: normalizedRequest.RulesetId,
            RuntimeFingerprint: normalizedRequest.RuntimeFingerprint,
            WorkspaceId: normalizedRequest.WorkspaceId,
            WorkspaceRevision: normalizedRequest.WorkspaceRevision,
            SourceDigest: normalizedRequest.SourceDigest,
            Locale: normalizedRequest.Locale,
            LocaleFallbackChain: normalizedRequest.LocaleFallbackChain,
            RuleEnvironment: normalizedRequest.RuleEnvironment,
            SourceAnchors: anchors,
            Runner: normalizedRequest.Runner with { Facts = visibleFacts },
            Strengths: visibleFacts
                .Where(static fact => string.Equals(fact.Category, "strength", StringComparison.OrdinalIgnoreCase))
                .OrderBy(static fact => fact.FactId, StringComparer.Ordinal)
                .ToArray(),
            Blockers: visibleFacts
                .Where(static fact => string.Equals(fact.Category, "blocker", StringComparison.OrdinalIgnoreCase))
                .OrderBy(static fact => fact.FactId, StringComparer.Ordinal)
                .ToArray(),
            Warnings: allWarnings,
            ExpertiseTags: normalizedRequest.Runner.ExpertiseTags
                .Concat(structurallyValidStrategies.SelectMany(static strategy => strategy.ExpertiseTags))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static tag => tag, StringComparer.Ordinal)
                .ToArray(),
            OptimizationStrategies: structurallyValidStrategies,
            Tips: tips,
            RuleExplanations: explanations,
            Variants: variants,
            GroupCapabilityPosture: groupProjection,
            AllowedSuggestedActions: allowedActions,
            ForbiddenClaimsAndActions: ForbiddenClaimsAndActions,
            DeterministicFallbackText: normalizedRequest.DeterministicFallbackText,
            InputDigest: inputDigest,
            PacketDigest: string.Empty);

        return packet with { PacketDigest = ComputeDigest(packet) };
    }

    public BuildGhostProviderValidationResult ValidateProviderAnswer(
        BuildGhostAnalysisPacket packet,
        BuildGhostProviderAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(answer);

        List<string> reasons = [];
        if (!string.Equals(answer.Schema, BuildGhostContractVersions.ProviderAnswerV1, StringComparison.Ordinal))
        {
            reasons.Add("provider-answer-schema-mismatch");
        }

        if (!string.Equals(answer.PacketDigest, packet.PacketDigest, StringComparison.Ordinal))
        {
            reasons.Add("packet-digest-mismatch");
        }

        if (!string.Equals(answer.Locale, packet.Locale, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("locale-mismatch");
        }

        if (string.IsNullOrWhiteSpace(answer.RequestId) || string.IsNullOrWhiteSpace(answer.Text))
        {
            reasons.Add("provider-answer-missing-required-content");
        }

        AddUnknownReferences(
            reasons,
            "fact",
            answer.ReferencedFactIds,
            packet.Runner.Facts.Select(static fact => fact.FactId));
        AddUnknownReferences(
            reasons,
            "strategy",
            answer.ReferencedStrategyIds,
            packet.OptimizationStrategies.Select(static strategy => strategy.StrategyId));
        AddUnknownReferences(
            reasons,
            "rule-explanation",
            answer.ReferencedRuleExplanationIds,
            packet.RuleExplanations.Select(static explanation => explanation.ExplanationId));
        AddUnknownReferences(
            reasons,
            "variant",
            answer.ReferencedVariantIds,
            packet.Variants.Select(static variant => variant.VariantId));
        AddUnknownReferences(
            reasons,
            "member",
            answer.ReferencedMemberRefs,
            packet.GroupCapabilityPosture?.VisibleMembers.Select(static member => member.MemberRef) ?? []);
        AddUnknownReferences(
            reasons,
            "source-anchor",
            answer.ReferencedSourceAnchorIds,
            packet.SourceAnchors.Select(static anchor => anchor.AnchorId));
        AddUnknownReferences(
            reasons,
            "action",
            answer.SuggestedActionIds,
            packet.AllowedSuggestedActions.Select(static action => action.ActionId));

        HashSet<string> allowedLinks = packet.RuleExplanations
            .Select(static explanation => explanation.SourceLookupRoute)
            .Where(static route => !string.IsNullOrWhiteSpace(route))
            .Select(static route => route!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string link in answer.Links.Where(static link => !string.IsNullOrWhiteSpace(link)))
        {
            if (!allowedLinks.Contains(link))
            {
                reasons.Add($"unsupported-link:{link}");
            }
        }

        if (reasons.Count > 0)
        {
            return new BuildGhostProviderValidationResult(
                Accepted: false,
                OutcomeStatus: "deterministic-fallback",
                SafeText: packet.DeterministicFallbackText,
                RejectionReasons: reasons.Distinct(StringComparer.Ordinal).OrderBy(static reason => reason, StringComparer.Ordinal).ToArray());
        }

        return new BuildGhostProviderValidationResult(
            Accepted: true,
            OutcomeStatus: "validated-provider-answer",
            SafeText: answer.Text,
            RejectionReasons: []);
    }

    private static BuildGhostAnalysisRequest Normalize(BuildGhostAnalysisRequest request)
    {
        BuildGhostRuleEnvironment environment = request.RuleEnvironment with
        {
            ActiveSourcebookIds = Order(request.RuleEnvironment.ActiveSourcebookIds),
            GmConstraintIds = Order(request.RuleEnvironment.GmConstraintIds)
        };
        BuildGhostRunnerProjection runner = request.Runner with
        {
            ExpertiseTags = Order(request.Runner.ExpertiseTags),
            Facts = request.Runner.Facts
                .Select(fact => fact with { SourceAnchorIds = Order(fact.SourceAnchorIds) })
                .OrderBy(static fact => fact.FactId, StringComparer.Ordinal)
                .ToArray(),
            ResourceValues = OrderDictionary(request.Runner.ResourceValues)
        };
        BuildGhostGroupInput? group = NormalizeGroup(request.Group);

        return request with
        {
            Locale = request.Locale.Trim(),
            LocaleFallbackChain = OrderLocaleFallbacks(request.Locale, request.LocaleFallbackChain),
            SupportedLocales = Order(request.SupportedLocales),
            RuleEnvironment = environment,
            Runner = runner,
            SourceAnchors = request.SourceAnchors
                .Select(anchor => anchor with
                {
                    ActiveCharacterSettings = OrderDictionary(anchor.ActiveCharacterSettings),
                    SavedValues = OrderDictionary(anchor.SavedValues),
                    CalculationTrace = anchor.CalculationTrace.ToArray()
                })
                .OrderBy(static anchor => anchor.AnchorId, StringComparer.Ordinal)
                .ToArray(),
            Strategies = request.Strategies
                .Select(strategy => strategy with
                {
                    ExpertiseTags = Order(strategy.ExpertiseTags),
                    TriggerFactIds = Order(strategy.TriggerFactIds),
                    Assumptions = Order(strategy.Assumptions),
                    Dependencies = Order(strategy.Dependencies),
                    GmPolicyConflicts = Order(strategy.GmPolicyConflicts),
                    SourceAnchorIds = Order(strategy.SourceAnchorIds),
                    Deltas = strategy.Deltas
                        .Select(delta => delta with { SourceAnchorIds = Order(delta.SourceAnchorIds) })
                        .OrderBy(static delta => delta.DeltaId, StringComparer.Ordinal)
                        .ToArray()
                })
                .OrderByDescending(static strategy => strategy.Priority)
                .ThenBy(static strategy => strategy.StrategyId, StringComparer.Ordinal)
                .ToArray(),
            RuleExplanations = request.RuleExplanations
                .Select(explanation => explanation with { SourceAnchorIds = Order(explanation.SourceAnchorIds) })
                .OrderBy(static explanation => explanation.ExplanationId, StringComparer.Ordinal)
                .ToArray(),
            Group = group
        };
    }

    private static void ValidateBinding(BuildGhostAnalysisRequest request)
    {
        Require(request.OwnerId, nameof(request.OwnerId));
        Require(request.RulesetId, nameof(request.RulesetId));
        Require(request.RuntimeFingerprint, nameof(request.RuntimeFingerprint));
        Require(request.WorkspaceId, nameof(request.WorkspaceId));
        Require(request.SourceDigest, nameof(request.SourceDigest));
        Require(request.Locale, nameof(request.Locale));
        Require(request.Runner.CharacterId, "Runner.CharacterId");
        Require(request.RuleEnvironment.SourcebookFingerprint, "RuleEnvironment.SourcebookFingerprint");
        Require(request.RuleEnvironment.CustomDataFingerprint, "RuleEnvironment.CustomDataFingerprint");
        Require(request.RuleEnvironment.GmPolicyFingerprint, "RuleEnvironment.GmPolicyFingerprint");
        Require(request.DeterministicFallbackText, nameof(request.DeterministicFallbackText));
        if (request.WorkspaceRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.WorkspaceRevision));
        }

        try
        {
            _ = CultureInfo.GetCultureInfo(request.Locale);
        }
        catch (CultureNotFoundException exception)
        {
            throw new ArgumentException("Locale must be a valid BCP-47 culture tag.", nameof(request.Locale), exception);
        }
    }

    private static IReadOnlyList<string> ValidateStrategy(
        OptimizationStrategyProjection strategy,
        IReadOnlySet<string> factIds,
        IReadOnlySet<string> anchorIds)
    {
        List<string> failures = [];
        if (string.IsNullOrWhiteSpace(strategy.StrategyId))
        {
            failures.Add("missing strategy id");
        }

        if (strategy.TriggerFactIds.Count == 0 || strategy.TriggerFactIds.Any(id => !factIds.Contains(id)))
        {
            failures.Add("trigger fact is absent from the visible runner projection");
        }

        if (strategy.SourceAnchorIds.Count == 0 || strategy.SourceAnchorIds.Any(id => !anchorIds.Contains(id)))
        {
            failures.Add("source anchor is unresolved");
        }

        if (strategy.Deltas.Any(delta => delta.SourceAnchorIds.Any(id => !anchorIds.Contains(id))))
        {
            failures.Add("variant delta source anchor is unresolved");
        }

        if (strategy.DrugProjection is not null && !IsCompleteDrugProjection(strategy.DrugProjection))
        {
            failures.Add("drug or temporary-buff mechanics are incomplete");
        }

        return failures;
    }

    private static bool IsCompleteDrugProjection(BuildGhostDrugStrategyProjection drug)
    {
        return HasText(drug.ItemId)
            && HasText(drug.SourceId)
            && HasText(drug.Dose)
            && HasText(drug.Onset)
            && HasText(drug.Duration)
            && HasText(drug.CrashAndAfterEffects)
            && HasText(drug.AddictionTest)
            && drug.AddictionThreshold >= 0
            && HasText(drug.StackingInteraction)
            && HasText(drug.Legality)
            && HasText(drug.Availability)
            && drug.Price >= 0m
            && HasText(drug.Currency)
            && HasText(drug.ToleranceAndDependency)
            && drug.BaselineCalculationTrace.Count > 0
            && drug.BoostedCalculationTrace.Count > 0;
    }

    private static BuildGhostTip CreateTip(OptimizationStrategyProjection strategy)
    {
        string route = strategy.Deltas.FirstOrDefault()?.Domain switch
        {
            "attribute" => "/build/attributes",
            "skill" or "language" => "/build/skills",
            "quality" => "/build/qualities",
            "ware" => "/build/ware",
            "gear" or "drug" => "/build/gear",
            "magic" or "resonance" => "/build/magic-resonance",
            _ => "/build/compare"
        };
        string severity = string.Equals(strategy.Applicability, BuildGhostApplicabilityStatuses.GmReview, StringComparison.Ordinal)
            ? "warning"
            : "info";

        return new BuildGhostTip(
            TipId: $"tip:{strategy.StrategyId}",
            Category: strategy.StrategyType,
            Severity: severity,
            TriggerFactIds: strategy.TriggerFactIds,
            Explanation: strategy.ShortTermBenefit,
            SourceAnchorIds: strategy.SourceAnchorIds,
            ExpectedBenefit: strategy.ExpectedBenefit,
            OpportunityCost: strategy.OpportunityCost,
            WorkbenchRoute: route,
            Applicability: strategy.Applicability,
            StrategyId: strategy.StrategyId,
            Risk: strategy.Risk,
            Assumptions: strategy.Assumptions,
            Counterfactual: strategy.Counterfactual);
    }

    private static IReadOnlyList<BuildGhostBuildVariant> CreateVariants(
        BuildGhostAnalysisRequest request,
        string inputDigest,
        IReadOnlyList<OptimizationStrategyProjection> strategies,
        GroupBuildCapabilityProjection? group)
    {
        OptimizationStrategyProjection[] conservative = strategies
            .OrderBy(static strategy => strategy.Deltas.Count)
            .ThenBy(static strategy => strategy.Priority)
            .ThenBy(static strategy => strategy.StrategyId, StringComparer.Ordinal)
            .Take(1)
            .ToArray();
        OptimizationStrategyProjection[] focused = strategies
            .OrderByDescending(static strategy => strategy.Priority)
            .ThenByDescending(static strategy => strategy.Deltas.Count)
            .ThenBy(static strategy => strategy.StrategyId, StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        List<OptimizationStrategyProjection> balanced = [];
        HashSet<string> seenTypes = new(StringComparer.Ordinal);
        foreach (OptimizationStrategyProjection strategy in strategies
            .OrderByDescending(static strategy => strategy.Priority)
            .ThenBy(static strategy => strategy.StrategyId, StringComparer.Ordinal))
        {
            if (seenTypes.Add(strategy.StrategyType) || balanced.Count == 0)
            {
                balanced.Add(strategy);
            }

            if (balanced.Count == 3)
            {
                break;
            }
        }

        if (balanced.Count < 3)
        {
            balanced.AddRange(strategies
                .Except(balanced)
                .OrderBy(static strategy => strategy.StrategyId, StringComparer.Ordinal)
                .Take(3 - balanced.Count));
        }

        return
        [
            CreateVariant(request, inputDigest, BuildGhostVariantShapes.ConservativeRepair, conservative, 1, group),
            CreateVariant(request, inputDigest, BuildGhostVariantShapes.RoleFocusedSpecialization, focused, 2, group),
            CreateVariant(request, inputDigest, BuildGhostVariantShapes.BalancedHybrid, balanced, 3, group)
        ];
    }

    private static BuildGhostBuildVariant CreateVariant(
        BuildGhostAnalysisRequest request,
        string inputDigest,
        string shape,
        IReadOnlyList<OptimizationStrategyProjection> selected,
        int requiredStrategyCount,
        GroupBuildCapabilityProjection? group)
    {
        string variantId = $"{request.Runner.CharacterId}:{shape}:v1";
        List<string> blockers = [];
        List<string> warnings = [];
        if (selected.Count < requiredStrategyCount)
        {
            blockers.Add($"{shape} requires {requiredStrategyCount} grounded strategies; {selected.Count} are available");
        }

        foreach (OptimizationStrategyProjection strategy in selected)
        {
            if (string.Equals(strategy.Applicability, BuildGhostApplicabilityStatuses.RequiresPrerequisite, StringComparison.Ordinal)
                || string.Equals(strategy.Applicability, BuildGhostApplicabilityStatuses.FutureOption, StringComparison.Ordinal))
            {
                blockers.Add($"{strategy.StrategyId}:{strategy.Applicability}");
            }
            else if (string.Equals(strategy.Applicability, BuildGhostApplicabilityStatuses.Unresolved, StringComparison.Ordinal))
            {
                blockers.Add($"{strategy.StrategyId}:unresolved");
            }
            else if (string.Equals(strategy.Applicability, BuildGhostApplicabilityStatuses.GmReview, StringComparison.Ordinal))
            {
                warnings.Add($"{strategy.StrategyId}:gm-review");
            }
        }

        IReadOnlyList<BuildGhostVariantDelta> deltas = ComposeVariantDeltas(selected, blockers);
        ValidateResourceDeltas(request.Runner.ResourceValues, deltas, blockers);
        string validationStatus = blockers.Count == 0
            ? BuildGhostVariantValidationStatuses.Available
            : selected.Any(static strategy => string.Equals(strategy.Applicability, BuildGhostApplicabilityStatuses.Unresolved, StringComparison.Ordinal))
                ? BuildGhostVariantValidationStatuses.Unresolved
                : BuildGhostVariantValidationStatuses.Rejected;
        IReadOnlyList<string> tags = selected
            .SelectMany(static strategy => strategy.ExpertiseTags)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static tag => tag, StringComparer.Ordinal)
            .ToArray();
        IReadOnlyList<string> gapsClosed = MatchCapabilities(group?.MissingCapabilityIds, selected, deltas);
        IReadOnlyList<string> redundancies = MatchCapabilities(group?.RedundantCapabilityIds, selected, deltas);
        BuildGhostApplyPreviewPlan? preview = string.Equals(validationStatus, BuildGhostVariantValidationStatuses.Available, StringComparison.Ordinal)
            ? new BuildGhostApplyPreviewPlan(
                ActionId: $"preview:{variantId}",
                ActionType: BuildGhostActionTypes.PreviewBuildVariant,
                VariantId: variantId,
                PreviewOnly: true,
                RequiresExplicitReview: true,
                ExpectedWorkspaceRevision: request.WorkspaceRevision,
                ExpectedSourceDigest: request.SourceDigest,
                ExpectedInputDigest: inputDigest)
            : null;

        return new BuildGhostBuildVariant(
            VariantId: variantId,
            Shape: shape,
            InputDigest: inputDigest,
            TargetExpertiseTags: tags,
            StrategyIds: selected.Select(static strategy => strategy.StrategyId).OrderBy(static id => id, StringComparer.Ordinal).ToArray(),
            Deltas: deltas,
            Validation: new BuildGhostVariantValidation(
                Status: validationStatus,
                Blockers: blockers.OrderBy(static blocker => blocker, StringComparer.Ordinal).ToArray(),
                Warnings: warnings.OrderBy(static warning => warning, StringComparer.Ordinal).ToArray()),
            ShortTermBenefit: JoinDistinct(selected.Select(static strategy => strategy.ShortTermBenefit)),
            LongTermCeiling: JoinDistinct(selected.Select(static strategy => strategy.LongTermCeiling)),
            CostsAndLostAlternatives: selected
                .SelectMany(static strategy => new[] { strategy.OpportunityCost, strategy.Risk })
                .Where(HasText)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static item => item, StringComparer.Ordinal)
                .ToArray(),
            Dependencies: selected.SelectMany(static strategy => strategy.Dependencies).Distinct(StringComparer.Ordinal).OrderBy(static item => item, StringComparer.Ordinal).ToArray(),
            GmPolicyConflicts: selected.SelectMany(static strategy => strategy.GmPolicyConflicts).Distinct(StringComparer.Ordinal).OrderBy(static item => item, StringComparer.Ordinal).ToArray(),
            GroupGapsClosed: gapsClosed,
            GroupRedundanciesCreated: redundancies,
            ApplyPreview: preview);
    }

    private static GroupBuildCapabilityProjection? CreateGroupProjection(BuildGhostGroupInput? group)
    {
        if (group is null)
        {
            return null;
        }

        if (!group.ConsentGranted)
        {
            return new GroupBuildCapabilityProjection(
                VisibilityPosture: "consent-required",
                GroupId: null,
                GroupRevision: null,
                MembershipDigest: null,
                VisibleMembers: [],
                Conclusions: [],
                MissingCapabilityIds: [],
                RedundantCapabilityIds: []);
        }

        if (!HasValidGroupBinding(group))
        {
            return new GroupBuildCapabilityProjection(
                VisibilityPosture: "binding-required",
                GroupId: null,
                GroupRevision: null,
                MembershipDigest: null,
                VisibleMembers: [],
                Conclusions: [],
                MissingCapabilityIds: [],
                RedundantCapabilityIds: []);
        }

        BuildGhostVisibleGroupMember[] members = group.VisibleMembers
            .OrderBy(static member => member.MemberRef, StringComparer.Ordinal)
            .ToArray();
        string[] required = group.RequiredCapabilityIds
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static capability => capability, StringComparer.Ordinal)
            .ToArray();
        List<BuildGhostGroupCapabilityConclusion> conclusions = [];
        List<string> missing = [];
        List<string> redundant = [];
        foreach (string capabilityId in required)
        {
            BuildGhostGroupCapabilityBand[] visible = members
                .SelectMany(static member => member.VisibleCapabilities)
                .Where(capability => string.Equals(capability.CapabilityId, capabilityId, StringComparison.Ordinal))
                .ToArray();
            string displayName = group.RequiredCapabilityDisplayNames.TryGetValue(capabilityId, out string? localizedName)
                && !string.IsNullOrWhiteSpace(localizedName)
                ? localizedName
                : capabilityId;
            string status;
            string wording;
            if (visible.Length == 0)
            {
                status = "missing-visible-coverage";
                missing.Add(capabilityId);
                wording = capabilityId.StartsWith("language:", StringComparison.Ordinal)
                    ? $"The visible group projection has no {displayName} speaker."
                    : $"No visible member currently covers {displayName}.";
            }
            else if (visible.Length > 1)
            {
                status = "redundant-visible-coverage";
                redundant.Add(capabilityId);
                wording = $"The visible group projection has {visible.Length} members covering {displayName}.";
            }
            else
            {
                status = "covered-visible-scope";
                wording = $"One visible member currently covers {displayName}.";
            }

            conclusions.Add(new BuildGhostGroupCapabilityConclusion(
                ConclusionId: $"group-capability:{capabilityId}",
                CapabilityId: capabilityId,
                LocalizedDisplayName: displayName,
                Status: status,
                Wording: wording,
                Confidence: visible.Length == 0 ? 0m : visible.Min(static item => item.Confidence),
                VisibleMemberCount: visible.Length));
        }

        return new GroupBuildCapabilityProjection(
            VisibilityPosture: "authorized-visible-scope",
            GroupId: group.GroupId,
            GroupRevision: group.GroupRevision,
            MembershipDigest: group.MembershipDigest,
            VisibleMembers: members,
            Conclusions: conclusions,
            MissingCapabilityIds: missing,
            RedundantCapabilityIds: redundant);
    }

    private static BuildGhostRuleExplanation CreateRuleExplanation(
        BuildGhostRuleExplanationInput input,
        IReadOnlySet<string> anchorIds)
    {
        bool anchorsResolved = input.SourceAnchorIds.Count > 0 && input.SourceAnchorIds.All(anchorIds.Contains);
        bool resolved = input.Resolved && anchorsResolved && !string.IsNullOrWhiteSpace(input.DeterministicExplanation);
        return new BuildGhostRuleExplanation(
            Schema: BuildGhostContractVersions.RuleExplanationV1,
            ExplanationId: input.ExplanationId,
            RuleId: input.RuleId,
            Question: input.Question,
            Status: resolved ? "resolved" : "bounded-uncertainty",
            Explanation: resolved
                ? input.DeterministicExplanation
                : "Rook cannot resolve this rule from the active Chummer data and calculation trace.",
            SourceAnchorIds: resolved ? input.SourceAnchorIds : input.SourceAnchorIds.Where(anchorIds.Contains).ToArray(),
            UncertaintyReason: resolved ? null : input.UncertaintyReason ?? "required rule or calculation facts are unresolved",
            SourceLookupRoute: input.SourceLookupRoute);
    }

    private static IReadOnlyList<string> MatchCapabilities(
        IReadOnlyList<string>? capabilityIds,
        IReadOnlyList<OptimizationStrategyProjection> strategies,
        IReadOnlyList<BuildGhostVariantDelta> deltas)
    {
        if (capabilityIds is null || capabilityIds.Count == 0)
        {
            return [];
        }

        string searchable = string.Join('|', strategies.SelectMany(static strategy => strategy.ExpertiseTags)
            .Concat(deltas.Select(static delta => delta.TargetId)));
        return capabilityIds
            .Where(capability => searchable.Contains(capability, StringComparison.OrdinalIgnoreCase)
                || searchable.Contains(capability.Replace("capability:", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static capability => capability, StringComparer.Ordinal)
            .ToArray();
    }

    private static BuildGhostGroupInput? NormalizeGroup(BuildGhostGroupInput? group)
    {
        if (group is null)
        {
            return null;
        }

        if (!group.ConsentGranted || !HasValidGroupBinding(group))
        {
            return group with
            {
                GroupId = null,
                GroupRevision = null,
                MembershipDigest = null,
                VisibleMembers = [],
                RequiredCapabilityIds = [],
                RequiredCapabilityDisplayNames = new Dictionary<string, string>(StringComparer.Ordinal)
            };
        }

        return group with
        {
            VisibleMembers = group.VisibleMembers
                .Select(member => member with
                {
                    VisibleCapabilities = member.VisibleCapabilities
                        .OrderBy(static capability => capability.CapabilityId, StringComparer.Ordinal)
                        .ToArray()
                })
                .OrderBy(static member => member.MemberRef, StringComparer.Ordinal)
                .ToArray(),
            RequiredCapabilityIds = Order(group.RequiredCapabilityIds),
            RequiredCapabilityDisplayNames = OrderDictionary(group.RequiredCapabilityDisplayNames)
        };
    }

    private static bool HasValidGroupBinding(BuildGhostGroupInput group)
        => !string.IsNullOrWhiteSpace(group.GroupId)
            && group.GroupRevision is >= 0
            && !string.IsNullOrWhiteSpace(group.MembershipDigest)
            && group.VisibleMembers.All(static member => !string.IsNullOrWhiteSpace(member.MemberRef))
            && group.VisibleMembers.Select(static member => member.MemberRef).Distinct(StringComparer.Ordinal).Count()
                == group.VisibleMembers.Count;

    private static IReadOnlyList<BuildGhostVariantDelta> ComposeVariantDeltas(
        IReadOnlyList<OptimizationStrategyProjection> strategies,
        ICollection<string> blockers)
    {
        BuildGhostVariantDelta[] supplied = strategies.SelectMany(static strategy => strategy.Deltas).ToArray();
        foreach (IGrouping<string, BuildGhostVariantDelta> duplicateId in supplied
            .GroupBy(static delta => delta.DeltaId, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1))
        {
            blockers.Add($"duplicate variant delta id {duplicateId.Key}");
        }

        List<BuildGhostVariantDelta> result = [];
        foreach (IGrouping<(string Domain, string TargetId), BuildGhostVariantDelta> target in supplied
            .GroupBy(static delta => (delta.Domain, delta.TargetId)))
        {
            BuildGhostVariantDelta[] deltas = target.OrderBy(static delta => delta.DeltaId, StringComparer.Ordinal).ToArray();
            if (deltas.Length == 1)
            {
                result.Add(deltas[0]);
                continue;
            }

            if (CanComposeAdditiveResourceDeltas(deltas, out decimal before, out decimal numericDelta, out string? unit))
            {
                result.Add(new BuildGhostVariantDelta(
                    DeltaId: $"delta:composed:{target.Key.Domain}:{target.Key.TargetId}",
                    Domain: target.Key.Domain,
                    TargetId: target.Key.TargetId,
                    BeforeValue: Format(before),
                    AfterValue: Format(before + numericDelta),
                    NumericDelta: numericDelta,
                    Unit: unit,
                    SourceAnchorIds: deltas
                        .SelectMany(static delta => delta.SourceAnchorIds)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(static id => id, StringComparer.Ordinal)
                        .ToArray()));
                continue;
            }

            blockers.Add($"conflicting variant deltas target {target.Key.Domain}:{target.Key.TargetId}");
            result.AddRange(deltas);
        }

        return result.OrderBy(static delta => delta.DeltaId, StringComparer.Ordinal).ToArray();
    }

    private static bool CanComposeAdditiveResourceDeltas(
        IReadOnlyList<BuildGhostVariantDelta> deltas,
        out decimal before,
        out decimal numericDelta,
        out string? unit)
    {
        before = 0m;
        numericDelta = 0m;
        unit = null;
        BuildGhostVariantDelta first = deltas[0];
        if (!string.Equals(first.TargetId, $"resource:{first.Domain}", StringComparison.Ordinal)
            || first.NumericDelta is null
            || !decimal.TryParse(first.BeforeValue, NumberStyles.Number, CultureInfo.InvariantCulture, out before))
        {
            return false;
        }

        unit = first.Unit;
        foreach (BuildGhostVariantDelta delta in deltas)
        {
            if (delta.NumericDelta is null
                || !decimal.TryParse(delta.BeforeValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal candidateBefore)
                || candidateBefore != before
                || !string.Equals(delta.Unit, unit, StringComparison.Ordinal)
                || !decimal.TryParse(delta.AfterValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal candidateAfter)
                || candidateAfter != before + delta.NumericDelta.Value)
            {
                return false;
            }

            numericDelta += delta.NumericDelta.Value;
        }

        return true;
    }

    private static void ValidateResourceDeltas(
        IReadOnlyDictionary<string, decimal> resources,
        IReadOnlyList<BuildGhostVariantDelta> deltas,
        ICollection<string> blockers)
    {
        foreach (BuildGhostVariantDelta delta in deltas.Where(static delta =>
            string.Equals(delta.TargetId, $"resource:{delta.Domain}", StringComparison.Ordinal)))
        {
            if (delta.NumericDelta is null
                || !decimal.TryParse(delta.BeforeValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal before)
                || !decimal.TryParse(delta.AfterValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal after)
                || after != before + delta.NumericDelta.Value)
            {
                blockers.Add($"{delta.TargetId} delta is not an exact additive projection");
                continue;
            }

            if (!resources.TryGetValue(delta.Domain, out decimal current) || current != before)
            {
                blockers.Add($"{delta.TargetId} delta does not match the current runner balance");
            }

            if (after < 0m)
            {
                blockers.Add($"{delta.TargetId} would fall below zero");
            }
        }
    }

    private static void AddUnknownReferences(
        ICollection<string> reasons,
        string kind,
        IEnumerable<string> referenced,
        IEnumerable<string> allowed)
    {
        HashSet<string> allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (string id in referenced.Where(static id => !string.IsNullOrWhiteSpace(id)))
        {
            if (!allowedSet.Contains(id))
            {
                reasons.Add($"unknown-{kind}:{id}");
            }
        }
    }

    private static string ComputeDigest<T>(T value)
    {
        JsonElement element = JsonSerializer.SerializeToElement(value, DigestSerializerOptions);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(writer, element);
        }

        return $"sha256:{Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant()}";
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException($"Unsupported JSON kind {element.ValueKind}.");
        }
    }

    private static IReadOnlyDictionary<string, TValue> OrderDictionary<TValue>(IReadOnlyDictionary<string, TValue> values)
    {
        SortedDictionary<string, TValue> ordered = new(StringComparer.Ordinal);
        foreach ((string key, TValue value) in values)
        {
            ordered[key] = value;
        }

        return ordered;
    }

    private static string[] Order(IEnumerable<string> values)
        => values
            .Where(HasText)
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

    private static string[] OrderLocaleFallbacks(string locale, IEnumerable<string> fallbacks)
        => new[] { locale.Trim() }
            .Concat(fallbacks.Where(HasText).Select(static value => value.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string JoinDistinct(IEnumerable<string> values)
        => string.Join(" ", values.Where(HasText).Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal));

    private static string Format(decimal value)
        => value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static bool IsSupportedLocale(string locale, IEnumerable<string> supportedLocales)
        => supportedLocales.Any(supported => string.Equals(supported, locale, StringComparison.OrdinalIgnoreCase));

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static void Require(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }
    }
}
