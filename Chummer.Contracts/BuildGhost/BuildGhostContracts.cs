namespace Chummer.Contracts.BuildGhost;

public static class BuildGhostContractVersions
{
    public const string AnalysisV1 = "chummer.build_ghost_analysis.v1";
    public const string RuleExplanationV1 = "chummer.build_ghost_rule_explanation.v1";
    public const string ProviderAnswerV1 = "chummer.build_ghost_provider_answer.v1";
}

public static class BuildGhostPersonaIds
{
    public const string Rook = "build-ghost-rook-v1";
    public const string StockDefaultAvatar = "build-ghost-tough-tongue-stock-avatar-v1";
    public const string RookAvatar = "build-ghost-rook-avatar-v1";
    public const string RookVoice = "build-ghost-rook-voice-v1";
}

public static class BuildGhostVariantShapes
{
    public const string ConservativeRepair = "conservative-repair";
    public const string RoleFocusedSpecialization = "role-focused-specialization";
    public const string BalancedHybrid = "balanced-hybrid";
}

public static class BuildGhostApplicabilityStatuses
{
    public const string ApplicableNow = "applicable-now";
    public const string RequiresPrerequisite = "requires-prerequisite";
    public const string GmReview = "gm-review";
    public const string FutureOption = "future-option";
    public const string Unresolved = "unresolved";
}

public static class BuildGhostVariantValidationStatuses
{
    public const string Available = "available";
    public const string Rejected = "rejected";
    public const string Unresolved = "unresolved";
}

public static class BuildGhostActionTypes
{
    public const string PreviewBuildVariant = "chummer.preview_build_variant";
    public const string PreviewWizardChoice = "chummer.preview_wizard_choice";
    public const string OpenRuleSource = "chummer.open_rule_source";
    public const string OpenWorkbenchRoute = "chummer.open_workbench_route";
}

public sealed record BuildGhostSourceAnchor(
    string AnchorId,
    string RulesetId,
    string SourceId,
    int? Page,
    IReadOnlyDictionary<string, string> ActiveCharacterSettings,
    IReadOnlyDictionary<string, string> SavedValues,
    IReadOnlyList<string> CalculationTrace,
    string? LocalizedSourceName = null,
    string? RuleId = null,
    bool IsCustomSource = false);

public sealed record BuildGhostFact(
    string FactId,
    string Category,
    string Label,
    string Value,
    decimal Confidence,
    IReadOnlyList<string> SourceAnchorIds,
    bool PlayerVisible = true);

public sealed record BuildGhostRuleEnvironment(
    IReadOnlyList<string> ActiveSourcebookIds,
    string SourcebookFingerprint,
    string CustomDataPosture,
    string CustomDataFingerprint,
    string GmPolicyFingerprint,
    IReadOnlyList<string> GmConstraintIds);

public sealed record BuildGhostRunnerProjection(
    string CharacterId,
    string DisplayName,
    string CreationState,
    IReadOnlyList<string> ExpertiseTags,
    IReadOnlyList<BuildGhostFact> Facts,
    IReadOnlyDictionary<string, decimal> ResourceValues);

public sealed record BuildGhostVariantDelta(
    string DeltaId,
    string Domain,
    string TargetId,
    string? BeforeValue,
    string? AfterValue,
    decimal? NumericDelta,
    string? Unit,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record BuildGhostDrugStrategyProjection(
    string ItemId,
    string SourceId,
    string Dose,
    string Onset,
    string Duration,
    string CrashAndAfterEffects,
    string AddictionTest,
    int AddictionThreshold,
    string StackingInteraction,
    string Legality,
    string Availability,
    decimal Price,
    string Currency,
    string ToleranceAndDependency,
    IReadOnlyList<string> ActiveGmRestrictionIds,
    IReadOnlyList<string> BaselineCalculationTrace,
    IReadOnlyList<string> BoostedCalculationTrace);

public sealed record OptimizationStrategyProjection(
    string StrategyId,
    string StrategyType,
    IReadOnlyList<string> ExpertiseTags,
    string Applicability,
    IReadOnlyList<string> TriggerFactIds,
    string ExpectedBenefit,
    string OpportunityCost,
    string Risk,
    IReadOnlyList<string> Assumptions,
    string Counterfactual,
    string ShortTermBenefit,
    string LongTermCeiling,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> GmPolicyConflicts,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<BuildGhostVariantDelta> Deltas,
    BuildGhostDrugStrategyProjection? DrugProjection = null,
    int Priority = 0);

public sealed record BuildGhostRuleExplanationInput(
    string ExplanationId,
    string RuleId,
    string Question,
    string DeterministicExplanation,
    IReadOnlyList<string> SourceAnchorIds,
    bool Resolved,
    string? UncertaintyReason = null,
    string? SourceLookupRoute = null);

public sealed record BuildGhostGroupCapabilityBand(
    string CapabilityId,
    string LocalizedDisplayName,
    string RatingBand,
    decimal Confidence);

public sealed record BuildGhostVisibleGroupMember(
    string MemberRef,
    IReadOnlyList<BuildGhostGroupCapabilityBand> VisibleCapabilities);

public sealed record BuildGhostGroupInput(
    bool ConsentGranted,
    string? GroupId,
    long? GroupRevision,
    string? MembershipDigest,
    IReadOnlyList<BuildGhostVisibleGroupMember> VisibleMembers,
    IReadOnlyList<string> RequiredCapabilityIds,
    IReadOnlyDictionary<string, string> RequiredCapabilityDisplayNames);

public sealed record BuildGhostAnalysisRequest(
    string OwnerId,
    string? CampaignId,
    string RulesetId,
    string RuntimeFingerprint,
    string WorkspaceId,
    long WorkspaceRevision,
    string SourceDigest,
    string Locale,
    IReadOnlyList<string> LocaleFallbackChain,
    IReadOnlyList<string> SupportedLocales,
    BuildGhostRuleEnvironment RuleEnvironment,
    BuildGhostRunnerProjection Runner,
    string RequestedGoal,
    IReadOnlyList<BuildGhostSourceAnchor> SourceAnchors,
    IReadOnlyList<OptimizationStrategyProjection> Strategies,
    IReadOnlyList<BuildGhostRuleExplanationInput> RuleExplanations,
    BuildGhostGroupInput? Group,
    string DeterministicFallbackText);

public sealed record BuildGhostTip(
    string TipId,
    string Category,
    string Severity,
    IReadOnlyList<string> TriggerFactIds,
    string Explanation,
    IReadOnlyList<string> SourceAnchorIds,
    string ExpectedBenefit,
    string OpportunityCost,
    string WorkbenchRoute,
    string Applicability,
    string StrategyId,
    string Risk,
    IReadOnlyList<string> Assumptions,
    string Counterfactual);

public sealed record BuildGhostRuleExplanation(
    string Schema,
    string ExplanationId,
    string RuleId,
    string Question,
    string Status,
    string Explanation,
    IReadOnlyList<string> SourceAnchorIds,
    string? UncertaintyReason,
    string? SourceLookupRoute);

public sealed record BuildGhostVariantValidation(
    string Status,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings);

public sealed record BuildGhostApplyPreviewPlan(
    string ActionId,
    string ActionType,
    string VariantId,
    bool PreviewOnly,
    bool RequiresExplicitReview,
    long ExpectedWorkspaceRevision,
    string ExpectedSourceDigest,
    string ExpectedInputDigest);

public sealed record BuildGhostBuildVariant(
    string VariantId,
    string Shape,
    string InputDigest,
    IReadOnlyList<string> TargetExpertiseTags,
    IReadOnlyList<string> StrategyIds,
    IReadOnlyList<BuildGhostVariantDelta> Deltas,
    BuildGhostVariantValidation Validation,
    string ShortTermBenefit,
    string LongTermCeiling,
    IReadOnlyList<string> CostsAndLostAlternatives,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> GmPolicyConflicts,
    IReadOnlyList<string> GroupGapsClosed,
    IReadOnlyList<string> GroupRedundanciesCreated,
    BuildGhostApplyPreviewPlan? ApplyPreview);

public sealed record BuildGhostGroupCapabilityConclusion(
    string ConclusionId,
    string CapabilityId,
    string LocalizedDisplayName,
    string Status,
    string Wording,
    decimal Confidence,
    int VisibleMemberCount);

public sealed record GroupBuildCapabilityProjection(
    string VisibilityPosture,
    string? GroupId,
    long? GroupRevision,
    string? MembershipDigest,
    IReadOnlyList<BuildGhostVisibleGroupMember> VisibleMembers,
    IReadOnlyList<BuildGhostGroupCapabilityConclusion> Conclusions,
    IReadOnlyList<string> MissingCapabilityIds,
    IReadOnlyList<string> RedundantCapabilityIds);

public sealed record BuildGhostAllowedAction(
    string ActionId,
    string ActionType,
    string? VariantId,
    bool RequiresExplicitReview,
    long WorkspaceRevision,
    string SourceDigest);

public sealed record BuildGhostAnalysisPacket(
    string Schema,
    string PersonaId,
    string AvatarId,
    string VoiceId,
    string DisplayName,
    string OwnerId,
    string? CampaignId,
    string RulesetId,
    string RuntimeFingerprint,
    string WorkspaceId,
    long WorkspaceRevision,
    string SourceDigest,
    string Locale,
    IReadOnlyList<string> LocaleFallbackChain,
    IReadOnlyList<string> SupportedLocales,
    BuildGhostRuleEnvironment RuleEnvironment,
    IReadOnlyList<BuildGhostSourceAnchor> SourceAnchors,
    BuildGhostRunnerProjection Runner,
    IReadOnlyList<BuildGhostFact> Strengths,
    IReadOnlyList<BuildGhostFact> Blockers,
    IReadOnlyList<BuildGhostFact> Warnings,
    IReadOnlyList<string> ExpertiseTags,
    IReadOnlyList<OptimizationStrategyProjection> OptimizationStrategies,
    IReadOnlyList<BuildGhostTip> Tips,
    IReadOnlyList<BuildGhostRuleExplanation> RuleExplanations,
    IReadOnlyList<BuildGhostBuildVariant> Variants,
    GroupBuildCapabilityProjection? GroupCapabilityPosture,
    IReadOnlyList<BuildGhostAllowedAction> AllowedSuggestedActions,
    IReadOnlyList<string> ForbiddenClaimsAndActions,
    string DeterministicFallbackText,
    string InputDigest,
    string PacketDigest);

public sealed record BuildGhostProviderAnswer(
    string Schema,
    string RequestId,
    string PacketDigest,
    string Locale,
    string Text,
    IReadOnlyList<string> ReferencedFactIds,
    IReadOnlyList<string> ReferencedStrategyIds,
    IReadOnlyList<string> ReferencedRuleExplanationIds,
    IReadOnlyList<string> ReferencedVariantIds,
    IReadOnlyList<string> ReferencedMemberRefs,
    IReadOnlyList<string> ReferencedSourceAnchorIds,
    IReadOnlyList<string> SuggestedActionIds,
    IReadOnlyList<string> Links);

public sealed record BuildGhostProviderValidationResult(
    bool Accepted,
    string OutcomeStatus,
    string SafeText,
    IReadOnlyList<string> RejectionReasons);
