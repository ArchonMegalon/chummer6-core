using Chummer.Contracts.Content;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Content;

public static class ContentOverlayRulePackCatalogExtensions
{
    public static RulePackCatalog ToRulePackCatalog(
        this ContentOverlayCatalog catalog,
        string rulesetId,
        string engineApiVersion = "rulepack-v1")
    {
        ArgumentNullException.ThrowIfNull(catalog);

        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(rulesetId);
        IReadOnlyList<RulePackManifest> installedRulePacks = catalog.Overlays
            .Select(overlay => overlay.ToRulePackManifest(normalizedRulesetId, engineApiVersion))
            .ToArray();

        return new RulePackCatalog(installedRulePacks);
    }

    public static RulePackManifest ToRulePackManifest(
        this ContentOverlayPack overlay,
        string rulesetId,
        string engineApiVersion = "rulepack-v1")
    {
        ArgumentNullException.ThrowIfNull(overlay);

        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(rulesetId);
        List<RulePackAssetDescriptor> assets = [];
        List<RulePackCapabilityDescriptor> capabilities = [];

        if (!string.IsNullOrWhiteSpace(overlay.DataPath))
        {
            assets.Add(new RulePackAssetDescriptor(
                Kind: RulePackAssetKinds.Xml,
                Mode: overlay.Mode,
                RelativePath: "data/",
                Checksum: string.Empty));
            capabilities.Add(new RulePackCapabilityDescriptor(
                CapabilityId: RulePackCapabilityIds.ContentCatalog,
                AssetKind: RulePackAssetKinds.Xml,
                AssetMode: overlay.Mode,
                Explainable: false,
                SessionSafe: false));
        }

        if (!string.IsNullOrWhiteSpace(overlay.LanguagePath))
        {
            assets.Add(new RulePackAssetDescriptor(
                Kind: RulePackAssetKinds.Localization,
                Mode: RulePackAssetModes.ReplaceFile,
                RelativePath: "lang/",
                Checksum: string.Empty));
            capabilities.Add(new RulePackCapabilityDescriptor(
                CapabilityId: RulePackCapabilityIds.Localization,
                AssetKind: RulePackAssetKinds.Localization,
                AssetMode: RulePackAssetModes.ReplaceFile,
                Explainable: false,
                SessionSafe: true));
        }

        RulePackExecutionPolicyHint[] executionPolicies =
        [
            new(
                Environment: RulePackExecutionEnvironments.DesktopLocal,
                PolicyMode: RulePackExecutionPolicyModes.Allow,
                MinimumTrustTier: ArtifactTrustTiers.LocalOnly,
                AllowedAssetModes: assets.Select(asset => asset.Mode).Distinct(StringComparer.Ordinal).ToArray()),
            new(
                Environment: RulePackExecutionEnvironments.HostedServer,
                PolicyMode: RulePackExecutionPolicyModes.Deny,
                MinimumTrustTier: ArtifactTrustTiers.Curated,
                AllowedAssetModes: []),
            new(
                Environment: RulePackExecutionEnvironments.SessionRuntimeBundle,
                PolicyMode: RulePackExecutionPolicyModes.Deny,
                MinimumTrustTier: ArtifactTrustTiers.Curated,
                AllowedAssetModes: [])
        ];

        return new RulePackManifest(
            PackId: overlay.Id,
            Version: "overlay-v1",
            Title: overlay.Name,
            Author: string.Empty,
            Description: overlay.Description,
            Targets: [normalizedRulesetId],
            EngineApiVersion: engineApiVersion,
            DependsOn: [],
            ConflictsWith: [],
            Visibility: ArtifactVisibilityModes.LocalOnly,
            TrustTier: ArtifactTrustTiers.LocalOnly,
            Assets: assets,
            Capabilities: capabilities,
            ExecutionPolicies: executionPolicies);
    }
}
