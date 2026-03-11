namespace Chummer.Contracts.Content;

public static class ArtifactVisibilityModes
{
    public const string Private = "private";
    public const string Shared = "shared";
    public const string CampaignShared = "campaign-shared";
    public const string Public = "public";
    public const string LocalOnly = "local-only";
}

public static class ArtifactTrustTiers
{
    public const string Official = "official";
    public const string Curated = "curated";
    public const string Private = "private";
    public const string LocalOnly = "local-only";
}

public static class ArtifactInstallStates
{
    public const string Available = "available";
    public const string Installed = "installed";
    public const string Pinned = "pinned";
}

public sealed record ArtifactInstallState(
    string State,
    DateTimeOffset? InstalledAtUtc = null,
    string? InstalledTargetKind = null,
    string? InstalledTargetId = null,
    string? RuntimeFingerprint = null);

public static class ArtifactInstallHistoryOperations
{
    public const string Install = "install";
    public const string Update = "update";
    public const string Pin = "pin";
    public const string Unpin = "unpin";
    public const string Remove = "remove";
}

public sealed record ArtifactInstallHistoryEntry(
    string Operation,
    ArtifactInstallState Install,
    DateTimeOffset AppliedAtUtc,
    string? Notes = null);

public static class RulePackAssetKinds
{
    public const string Xml = "xml";
    public const string Localization = "localization";
    public const string DeclarativeRules = "declarative-rules";
    public const string Lua = "lua";
    public const string Tests = "tests";
}

public static class RulePackAssetModes
{
    public const string ReplaceFile = "replace-file";
    public const string MergeCatalog = "merge-catalog";
    public const string AppendCatalog = "append-catalog";
    public const string RemoveNode = "remove-node";
    public const string PatchNode = "patch-node";
    public const string SetConstant = DeclarativeRuleOverrideModes.SetConstant;
    public const string OverrideThreshold = DeclarativeRuleOverrideModes.OverrideThreshold;
    public const string EnableOption = DeclarativeRuleOverrideModes.EnableOption;
    public const string DisableOption = DeclarativeRuleOverrideModes.DisableOption;
    public const string ReplaceCreationProfile = DeclarativeRuleOverrideModes.ReplaceCreationProfile;
    public const string ModifyCap = DeclarativeRuleOverrideModes.ModifyCap;
    public const string RenameLabel = DeclarativeRuleOverrideModes.RenameLabel;
    public const string AddProvider = "add-provider";
    public const string WrapProvider = "wrap-provider";
    public const string ReplaceProvider = "replace-provider";
    public const string DisableProvider = "disable-provider";
}

public static class RulePackCapabilityIds
{
    public const string ContentCatalog = "content.catalog";
    public const string Localization = "localization";
    public const string DeriveStat = "derive.stat";
    public const string DeriveAttributeLimit = "derive.attribute-limit";
    public const string DeriveInitiative = "derive.initiative";
    public const string ValidateCharacter = "validate.character";
    public const string ValidateChoice = "validate.choice";
    public const string AvailabilityItem = "availability.item";
    public const string PriceItem = "price.item";
    public const string FilterChoices = "filter.choices";
    public const string EffectApply = "effect.apply";
    public const string BuildLabRecommendation = "buildlab.recommendation";
    public const string CreationProfile = "creation.profile";
    public const string SessionQuickActions = "session.quick-actions";
}

public static class RulePackExecutionEnvironments
{
    public const string DesktopLocal = "desktop-local";
    public const string HostedServer = "hosted-server";
    public const string SessionRuntimeBundle = "session-runtime-bundle";
}

public static class RulePackExecutionPolicyModes
{
    public const string Allow = "allow";
    public const string ReviewRequired = "review-required";
    public const string Deny = "deny";
}

public sealed record ArtifactVersionReference(
    string Id,
    string Version);

public sealed record ContentBundleDescriptor(
    string BundleId,
    string RulesetId,
    string Version,
    string Title,
    string Description,
    IReadOnlyList<string> AssetPaths);

public sealed record RulePackAssetDescriptor(
    string Kind,
    string Mode,
    string RelativePath,
    string Checksum);

public sealed record RulePackCapabilityDescriptor(
    string CapabilityId,
    string AssetKind,
    string AssetMode,
    bool Explainable = false,
    bool SessionSafe = false);

public sealed record RulePackExecutionPolicyHint(
    string Environment,
    string PolicyMode,
    string MinimumTrustTier,
    IReadOnlyList<string> AllowedAssetModes);

public sealed record RulePackManifest(
    string PackId,
    string Version,
    string Title,
    string Author,
    string Description,
    IReadOnlyList<string> Targets,
    string EngineApiVersion,
    IReadOnlyList<ArtifactVersionReference> DependsOn,
    IReadOnlyList<ArtifactVersionReference> ConflictsWith,
    string Visibility,
    string TrustTier,
    IReadOnlyList<RulePackAssetDescriptor> Assets,
    IReadOnlyList<RulePackCapabilityDescriptor> Capabilities,
    IReadOnlyList<RulePackExecutionPolicyHint> ExecutionPolicies,
    string? Signature = null);

public sealed record RulePackCatalog(
    IReadOnlyList<RulePackManifest> InstalledRulePacks);

public static class BuildKitPromptKinds
{
    public const string Choice = "choice";
    public const string Toggle = "toggle";
    public const string Quantity = "quantity";
}

public static class BuildKitActionKinds
{
    public const string AddBundle = "add-bundle";
    public const string ApplyChoice = "apply-choice";
    public const string SetMetadata = "set-metadata";
    public const string QueueCareerUpdate = "queue-career-update";
}

public sealed record BuildKitRuntimeRequirement(
    string RulesetId,
    IReadOnlyList<string> RequiredRuntimeFingerprints,
    IReadOnlyList<ArtifactVersionReference> RequiredRulePacks);

public sealed record BuildKitPromptOption(
    string OptionId,
    string Label,
    string? Description = null);

public sealed record BuildKitPromptDescriptor(
    string PromptId,
    string Kind,
    string Label,
    IReadOnlyList<BuildKitPromptOption> Options,
    bool Required = false);

public sealed record BuildKitActionDescriptor(
    string ActionId,
    string Kind,
    string TargetId,
    string? PromptId = null,
    string? Notes = null);

public sealed record BuildKitManifest(
    string BuildKitId,
    string Version,
    string Title,
    string Description,
    IReadOnlyList<string> Targets,
    IReadOnlyList<BuildKitRuntimeRequirement> RuntimeRequirements,
    IReadOnlyList<BuildKitPromptDescriptor> Prompts,
    IReadOnlyList<BuildKitActionDescriptor> Actions,
    string Visibility,
    string TrustTier);

public sealed record ResolvedRuntimeLock(
    string RulesetId,
    IReadOnlyList<ContentBundleDescriptor> ContentBundles,
    IReadOnlyList<ArtifactVersionReference> RulePacks,
    IReadOnlyDictionary<string, string> ProviderBindings,
    string EngineApiVersion,
    string RuntimeFingerprint);
