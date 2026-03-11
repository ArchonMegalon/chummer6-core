using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Content;

public static class RuntimeCompatibilityContractNormalizer
{
    public static RuntimeLockInstallPreviewReceipt NormalizeRuntimeLockInstallPreview(RuntimeLockInstallPreviewReceipt preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        RuntimeLockInstallPreviewItem[] changes = preview.Changes
            .Select(NormalizePreviewItem)
            .OrderBy(static change => change.Kind, StringComparer.Ordinal)
            .ThenBy(static change => change.SubjectId, StringComparer.Ordinal)
            .ToArray();
        RuntimeInspectorWarning[] warnings = preview.Warnings
            .Select(NormalizeWarning)
            .OrderBy(static warning => warning.Kind, StringComparer.Ordinal)
            .ThenBy(static warning => warning.Severity, StringComparer.Ordinal)
            .ThenBy(static warning => warning.SubjectId, StringComparer.Ordinal)
            .ThenBy(static warning => warning.MessageKey, StringComparer.Ordinal)
            .ToArray();

        return preview with
        {
            RuntimeLock = NormalizeRuntimeLock(preview.RuntimeLock),
            Changes = changes,
            Warnings = warnings,
            RequiresConfirmation = changes.Any(static change => change.RequiresConfirmation)
        };
    }

    public static RuntimeLockInstallCandidate NormalizeRuntimeLockInstallCandidate(RuntimeLockInstallCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        RuntimeLockCompatibilityDiagnostic[] diagnostics = candidate.Diagnostics
            .Select(NormalizeCompatibilityDiagnostic)
            .OrderBy(static diagnostic => diagnostic.State, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.RequiredRulesetId, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.RequiredRuntimeFingerprint, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.MessageKey, StringComparer.Ordinal)
            .ToArray();

        return candidate with
        {
            Entry = candidate.Entry with
            {
                RuntimeLock = NormalizeRuntimeLock(candidate.Entry.RuntimeLock),
                Install = NormalizeInstall(candidate.Entry.Install, candidate.Entry.RuntimeLock.RuntimeFingerprint)
            },
            Diagnostics = diagnostics,
            CanInstall = diagnostics.All(static diagnostic => string.Equals(diagnostic.State, RuntimeLockCompatibilityStates.Compatible, StringComparison.Ordinal))
        };
    }

    public static BuildKitManifest NormalizeBuildKitManifest(BuildKitManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        BuildKitRuntimeRequirement[] requirements = manifest.RuntimeRequirements
            .Select(static requirement => new BuildKitRuntimeRequirement(
                RulesetId: RulesetDefaults.NormalizeRequired(requirement.RulesetId),
                RequiredRuntimeFingerprints: requirement.RequiredRuntimeFingerprints
                    .Where(static fingerprint => !string.IsNullOrWhiteSpace(fingerprint))
                    .Select(static fingerprint => fingerprint.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static fingerprint => fingerprint, StringComparer.Ordinal)
                    .ToArray(),
                RequiredRulePacks: requirement.RequiredRulePacks
                    .Select(static reference => new ArtifactVersionReference(reference.Id.Trim(), reference.Version.Trim()))
                    .OrderBy(static reference => reference.Id, StringComparer.Ordinal)
                    .ThenBy(static reference => reference.Version, StringComparer.Ordinal)
                    .ToArray()))
            .OrderBy(static requirement => requirement.RulesetId, StringComparer.Ordinal)
            .ToArray();

        BuildKitPromptDescriptor[] prompts = manifest.Prompts
            .Select(static prompt => new BuildKitPromptDescriptor(
                PromptId: prompt.PromptId.Trim(),
                Kind: prompt.Kind.Trim(),
                Label: prompt.Label.Trim(),
                Options: prompt.Options
                    .Select(static option => new BuildKitPromptOption(
                        OptionId: option.OptionId.Trim(),
                        Label: option.Label.Trim(),
                        Description: string.IsNullOrWhiteSpace(option.Description) ? null : option.Description.Trim()))
                    .OrderBy(static option => option.OptionId, StringComparer.Ordinal)
                    .ToArray(),
                Required: prompt.Required))
            .OrderBy(static prompt => prompt.PromptId, StringComparer.Ordinal)
            .ToArray();

        BuildKitActionDescriptor[] actions = manifest.Actions
            .Select(static action => new BuildKitActionDescriptor(
                ActionId: action.ActionId.Trim(),
                Kind: action.Kind.Trim(),
                TargetId: action.TargetId.Trim(),
                PromptId: string.IsNullOrWhiteSpace(action.PromptId) ? null : action.PromptId.Trim(),
                Notes: string.IsNullOrWhiteSpace(action.Notes) ? null : action.Notes.Trim()))
            .OrderBy(static action => action.ActionId, StringComparer.Ordinal)
            .ToArray();

        return manifest with
        {
            Targets = manifest.Targets
                .Select(RulesetDefaults.NormalizeRequired)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static target => target, StringComparer.Ordinal)
                .ToArray(),
            RuntimeRequirements = requirements,
            Prompts = prompts,
            Actions = actions,
            Visibility = manifest.Visibility.Trim(),
            TrustTier = manifest.TrustTier.Trim()
        };
    }

    private static RuntimeLockInstallPreviewItem NormalizePreviewItem(RuntimeLockInstallPreviewItem item)
    {
        return item with
        {
            SummaryKey = RuntimeLockContractLocalization.ResolveInstallPreviewSummaryKey(item),
            SummaryParameters = NormalizeParameters(RuntimeLockContractLocalization.ResolveInstallPreviewSummaryParameters(item))
        };
    }

    private static RuntimeInspectorWarning NormalizeWarning(RuntimeInspectorWarning warning)
    {
        return warning with
        {
            MessageKey = RuntimeInspectorContractLocalization.ResolveMessageKey(warning),
            MessageParameters = NormalizeParameters(RuntimeInspectorContractLocalization.ResolveMessageParameters(warning))
        };
    }

    private static RuntimeLockCompatibilityDiagnostic NormalizeCompatibilityDiagnostic(RuntimeLockCompatibilityDiagnostic diagnostic)
    {
        return diagnostic with
        {
            MessageKey = RuntimeLockContractLocalization.ResolveCompatibilityMessageKey(diagnostic),
            MessageParameters = NormalizeParameters(RuntimeLockContractLocalization.ResolveCompatibilityMessageParameters(diagnostic))
        };
    }

    private static ResolvedRuntimeLock NormalizeRuntimeLock(ResolvedRuntimeLock runtimeLock)
    {
        return runtimeLock with
        {
            RulesetId = RulesetDefaults.NormalizeRequired(runtimeLock.RulesetId),
            ContentBundles = runtimeLock.ContentBundles
                .Select(static bundle => bundle with
                {
                    RulesetId = RulesetDefaults.NormalizeRequired(bundle.RulesetId),
                    AssetPaths = bundle.AssetPaths
                        .OrderBy(static path => path, StringComparer.Ordinal)
                        .ToArray()
                })
                .OrderBy(static bundle => bundle.BundleId, StringComparer.Ordinal)
                .ThenBy(static bundle => bundle.Version, StringComparer.Ordinal)
                .ToArray(),
            RulePacks = runtimeLock.RulePacks
                .OrderBy(static reference => reference.Id, StringComparer.Ordinal)
                .ThenBy(static reference => reference.Version, StringComparer.Ordinal)
                .ToArray(),
            ProviderBindings = runtimeLock.ProviderBindings
                .OrderBy(static binding => binding.Key, StringComparer.Ordinal)
                .ThenBy(static binding => binding.Value, StringComparer.Ordinal)
                .ToDictionary(static binding => binding.Key, static binding => binding.Value, StringComparer.Ordinal)
        };
    }

    private static ArtifactInstallState NormalizeInstall(ArtifactInstallState install, string runtimeFingerprint)
    {
        return string.IsNullOrWhiteSpace(install.RuntimeFingerprint)
            ? install with { RuntimeFingerprint = runtimeFingerprint }
            : install;
    }

    private static RulesetExplainParameter[] NormalizeParameters(IReadOnlyList<RulesetExplainParameter> parameters)
    {
        return parameters
            .OrderBy(static parameter => parameter.Name, StringComparer.Ordinal)
            .Select(static parameter => parameter with { Name = parameter.Name.Trim() })
            .ToArray();
    }
}
