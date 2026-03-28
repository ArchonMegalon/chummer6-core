using Chummer.Contracts.Content;

namespace Chummer.Application.Hub;

internal static class BuildKitHandoffNarrator
{
    public static string SummarizeRuntimeRequirement(BuildKitRuntimeRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        string runtimeText = requirement.RequiredRuntimeFingerprints.Count == 0
            ? "any grounded runtime fingerprint"
            : string.Join(", ", requirement.RequiredRuntimeFingerprints.OrderBy(static item => item, StringComparer.Ordinal));
        string packText = requirement.RequiredRulePacks.Count == 0
            ? "no extra rule packs"
            : string.Join(", ", requirement.RequiredRulePacks
                .OrderBy(static item => item.Id, StringComparer.Ordinal)
                .ThenBy(static item => item.Version, StringComparer.Ordinal)
                .Select(static item => $"{item.Id}@{item.Version}"));

        return $"{requirement.RulesetId}: {runtimeText}; {packText}";
    }

    public static string DescribeRuntimeRequirements(BuildKitManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        string stagingSummary = DescribeReceiptStaging(manifest);
        if (manifest.RuntimeRequirements.Count == 0)
        {
            return $"No extra runtime fingerprint or rule pack is pinned yet; hand the emitted build receipt into the grounded campaign/profile runtime and rule environment already approved for the workspace. The migration oracle stays shallow because no extra compatibility bridge is required. {stagingSummary}";
        }

        string runtimeSummary = string.Join(
            " | ",
            manifest.RuntimeRequirements
                .OrderBy(static requirement => requirement.RulesetId, StringComparer.Ordinal)
                .Select(SummarizeRuntimeRequirement));

        return $"Requires a compatible campaign/profile runtime and rule environment before handoff: {runtimeSummary}. The migration oracle and campaign receipts should reuse the same contract during restore and support closure. {stagingSummary}";
    }

    public static string DescribeSessionRuntimeHandoff(BuildKitManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        string stagingSummary = DescribeReceiptStaging(manifest);
        if (manifest.RuntimeRequirements.Count == 0)
        {
            return $"Apply this build path in the workbench first, then hand the emitted build receipt into the grounded campaign/profile runtime and rule environment. {stagingSummary}";
        }

        string runtimeSummary = string.Join(
            " | ",
            manifest.RuntimeRequirements
                .OrderBy(static requirement => requirement.RulesetId, StringComparer.Ordinal)
                .Select(SummarizeRuntimeRequirement));

        return $"Apply this build path in the workbench first, then hand the emitted build receipt into a compatible runtime and rule environment that match: {runtimeSummary}. {stagingSummary}";
    }

    public static string DescribeCampaignReturn(BuildKitManifest manifest, RuleProfileApplyTarget target)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(target);

        string targetKind = string.IsNullOrWhiteSpace(target.TargetKind)
            ? "workspace"
            : target.TargetKind;

        if (manifest.RuntimeRequirements.Count == 0)
        {
            return $"The emitted build receipt can return through the selected {targetKind} once the grounded campaign/profile runtime and rule environment are attached.";
        }

        string runtimeSummary = string.Join(
            " | ",
            manifest.RuntimeRequirements
                .OrderBy(static requirement => requirement.RulesetId, StringComparer.Ordinal)
                .Select(SummarizeRuntimeRequirement));

        return $"The emitted build receipt can return through the selected {targetKind} after the target matches: {runtimeSummary}. Keep the migration oracle aligned with the same rule-environment contract before reopening play.";
    }

    public static string DescribeCampaignReturnContract(BuildKitManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.RuntimeRequirements.Count == 0)
        {
            return "The emitted build receipt can return through the selected workspace or campaign lane once the grounded campaign/profile runtime and rule environment are attached.";
        }

        string runtimeSummary = string.Join(
            " | ",
            manifest.RuntimeRequirements
                .OrderBy(static requirement => requirement.RulesetId, StringComparer.Ordinal)
                .Select(SummarizeRuntimeRequirement));

        return $"The emitted build receipt can return through the selected workspace or campaign lane after the target matches: {runtimeSummary}. Keep the migration oracle aligned with the same rule-environment contract before reopening play.";
    }

    public static string DescribeSupportClosure(BuildKitManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.RuntimeRequirements.Count == 0)
        {
            return "Support closure can cite the same grounded runtime and rule environment once this build receipt lands.";
        }

        string runtimeSummary = string.Join(
            " | ",
            manifest.RuntimeRequirements
                .OrderBy(static requirement => requirement.RulesetId, StringComparer.Ordinal)
                .Select(SummarizeRuntimeRequirement));

        return $"Support closure can reuse the same runtime, rule-pack, and migration-oracle contract after handoff: {runtimeSummary}.";
    }

    public static string DescribeReceiptStaging(BuildKitManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return (manifest.Prompts.Count, manifest.Actions.Count) switch
        {
            (0, 0) => "No extra prompt resolution or grounded action staging is required.",
            (0, > 0) => $"{manifest.Actions.Count} grounded action(s) will be staged into the emitted build receipt.",
            (> 0, 0) => $"{manifest.Prompts.Count} prompt(s) must be resolved before the build receipt can be emitted.",
            _ => $"{manifest.Prompts.Count} prompt(s) must be resolved and {manifest.Actions.Count} grounded action(s) will be staged into the emitted build receipt."
        };
    }
}
