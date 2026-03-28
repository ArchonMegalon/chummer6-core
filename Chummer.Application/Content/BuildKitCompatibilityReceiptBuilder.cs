using Chummer.Application.Hub;
using Chummer.Contracts.Content;

namespace Chummer.Application.Content;

public static class BuildKitCompatibilityReceiptBuilder
{
    public static BuildKitCompatibilityReceipt Create(BuildKitManifest manifest, RuleProfileApplyTarget? target = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        BuildKitRuntimeRequirementReceipt[] runtimeRequirements = manifest.RuntimeRequirements
            .OrderBy(static requirement => requirement.RulesetId, StringComparer.Ordinal)
            .Select(requirement => new BuildKitRuntimeRequirementReceipt(
                RulesetId: requirement.RulesetId,
                RequiredRuntimeFingerprints: requirement.RequiredRuntimeFingerprints
                    .OrderBy(static fingerprint => fingerprint, StringComparer.Ordinal)
                    .ToArray(),
                RequiredRulePacks: requirement.RequiredRulePacks
                    .OrderBy(static pack => pack.Id, StringComparer.Ordinal)
                    .ThenBy(static pack => pack.Version, StringComparer.Ordinal)
                    .ToArray(),
                Summary: BuildKitHandoffNarrator.SummarizeRuntimeRequirement(requirement)))
            .ToArray();

        bool requiresRuntimeReview = runtimeRequirements.Length > 0;
        bool requiresPromptResolution = manifest.Prompts.Count > 0;
        bool stagesGroundedActions = manifest.Actions.Count > 0;

        return new BuildKitCompatibilityReceipt(
            BuildKitId: manifest.BuildKitId,
            RequiresRuntimeReview: requiresRuntimeReview,
            RequiresPromptResolution: requiresPromptResolution,
            StagesGroundedActions: stagesGroundedActions,
            PromptCount: manifest.Prompts.Count,
            ActionCount: manifest.Actions.Count,
            RuntimeRequirements: runtimeRequirements,
            RuntimeCompatibilitySummary: BuildKitHandoffNarrator.DescribeRuntimeRequirements(manifest),
            SessionRuntimeSummary: BuildKitHandoffNarrator.DescribeSessionRuntimeHandoff(manifest),
            CampaignReturnSummary: target is null
                ? BuildKitHandoffNarrator.DescribeCampaignReturnContract(manifest)
                : BuildKitHandoffNarrator.DescribeCampaignReturn(manifest, target),
            SupportClosureSummary: BuildKitHandoffNarrator.DescribeSupportClosure(manifest),
            NextSafeActionSummary: BuildNextSafeActionSummary(
                manifest,
                target,
                requiresRuntimeReview,
                requiresPromptResolution,
                stagesGroundedActions));
    }

    private static string BuildNextSafeActionSummary(
        BuildKitManifest manifest,
        RuleProfileApplyTarget? target,
        bool requiresRuntimeReview,
        bool requiresPromptResolution,
        bool stagesGroundedActions)
    {
        string targetSummary = string.IsNullOrWhiteSpace(target?.TargetKind)
            ? "the selected workspace or campaign lane"
            : $"the selected {target.TargetKind}";

        if (requiresRuntimeReview)
        {
            string runtimeSummary = string.Join(
                " | ",
                manifest.RuntimeRequirements
                    .OrderBy(static requirement => requirement.RulesetId, StringComparer.Ordinal)
                    .Select(BuildKitHandoffNarrator.SummarizeRuntimeRequirement));

            return $"Resolve the build path in the workbench, then attach the emitted receipt to {targetSummary} only after the runtime and rule environment match: {runtimeSummary}.";
        }

        if (requiresPromptResolution && stagesGroundedActions)
        {
            return $"Resolve {manifest.Prompts.Count} prompt(s), emit the build receipt, and stage {manifest.Actions.Count} grounded action(s) before handing the result into {targetSummary}.";
        }

        if (requiresPromptResolution)
        {
            return $"Resolve {manifest.Prompts.Count} prompt(s) in the workbench, emit the build receipt, and hand it into {targetSummary}.";
        }

        if (stagesGroundedActions)
        {
            return $"Apply this build path in the workbench, stage {manifest.Actions.Count} grounded action(s), and then hand the emitted receipt into {targetSummary}.";
        }

        return $"Apply this build path in the workbench, emit the build receipt, and hand it into {targetSummary}.";
    }
}
