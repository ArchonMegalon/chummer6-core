using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Content;

public sealed class DefaultBuildKitRegistryService : IBuildKitRegistryService
{
    private static readonly BuildKitRegistryEntry[] BuiltInEntries =
    [
        new(
            Manifest: new BuildKitManifest(
                BuildKitId: "street-sam-starter",
                Version: "1.0.0",
                Title: "Street Sam Starter",
                Description: "Guided combat-focused starter path with a grounded runtime handoff.",
                Targets: [RulesetDefaults.Sr5],
                RuntimeRequirements:
                [
                    new BuildKitRuntimeRequirement(
                        RulesetId: RulesetDefaults.Sr5,
                        RequiredRuntimeFingerprints: ["sha256:core"],
                        RequiredRulePacks: [new ArtifactVersionReference("house-rules", "1.0.0")])
                ],
                Prompts:
                [
                    new BuildKitPromptDescriptor(
                        PromptId: "combat-focus",
                        Kind: BuildKitPromptKinds.Choice,
                        Label: "Combat Focus",
                        Options:
                        [
                            new BuildKitPromptOption("street-sam", "Street Sam", "Balanced direct-combat entry path."),
                            new BuildKitPromptOption("enforcer", "Enforcer", "Heavier armor and suppression-first loadout.")
                        ],
                        Required: true)
                ],
                Actions:
                [
                    new BuildKitActionDescriptor(
                        ActionId: "starter-bundle",
                        Kind: BuildKitActionKinds.AddBundle,
                        TargetId: "street-sam.bundle"),
                    new BuildKitActionDescriptor(
                        ActionId: "career-queue",
                        Kind: BuildKitActionKinds.QueueCareerUpdate,
                        TargetId: "career.street-sam",
                        PromptId: "combat-focus",
                        Notes: "Queue the next advancement checkpoint directly into the living dossier.")
                ],
                Visibility: ArtifactVisibilityModes.Public,
                TrustTier: ArtifactTrustTiers.Curated),
            Owner: new OwnerScope("system"),
            Visibility: ArtifactVisibilityModes.Public,
            PublicationStatus: BuildKitPublicationStatuses.Published,
            UpdatedAtUtc: DateTimeOffset.Parse("2026-03-28T12:00:00+00:00")),
        new(
            Manifest: new BuildKitManifest(
                BuildKitId: "matrix-operator",
                Version: "1.1.0",
                Title: "Matrix Operator",
                Description: "Decking-first starter path that stays honest about required runtime posture.",
                Targets: [RulesetDefaults.Sr5],
                RuntimeRequirements:
                [
                    new BuildKitRuntimeRequirement(
                        RulesetId: RulesetDefaults.Sr5,
                        RequiredRuntimeFingerprints: ["sha256:campaign-a"],
                        RequiredRulePacks: [new ArtifactVersionReference("official-errata", "1.2.0")])
                ],
                Prompts:
                [
                    new BuildKitPromptDescriptor(
                        PromptId: "matrix-lane",
                        Kind: BuildKitPromptKinds.Choice,
                        Label: "Matrix lane",
                        Options:
                        [
                            new BuildKitPromptOption("deck", "Cyberdeck", "Full decker shell with deeper matrix actions."),
                            new BuildKitPromptOption("rig", "Rig support", "Keep the matrix lane lighter and vehicle-aware.")
                        ],
                        Required: true)
                ],
                Actions:
                [
                    new BuildKitActionDescriptor(
                        ActionId: "matrix-bundle",
                        Kind: BuildKitActionKinds.AddBundle,
                        TargetId: "matrix-operator.bundle"),
                    new BuildKitActionDescriptor(
                        ActionId: "metadata-role",
                        Kind: BuildKitActionKinds.SetMetadata,
                        TargetId: "role.matrix-operator",
                        PromptId: "matrix-lane",
                        Notes: "Stamp the selected matrix lane into the build receipt.")
                ],
                Visibility: ArtifactVisibilityModes.Public,
                TrustTier: ArtifactTrustTiers.Curated),
            Owner: new OwnerScope("system"),
            Visibility: ArtifactVisibilityModes.Public,
            PublicationStatus: BuildKitPublicationStatuses.Published,
            UpdatedAtUtc: DateTimeOffset.Parse("2026-03-28T12:05:00+00:00")),
        new(
            Manifest: new BuildKitManifest(
                BuildKitId: "edge-runner-starter",
                Version: "1.0.0",
                Title: "Edge Runner Starter",
                Description: "Fast-start SR6 preview path that hands off into the active approved preview runtime.",
                Targets: [RulesetDefaults.Sr6],
                RuntimeRequirements:
                [
                    new BuildKitRuntimeRequirement(
                        RulesetId: RulesetDefaults.Sr6,
                        RequiredRuntimeFingerprints: ["sr6.preview.v1"],
                        RequiredRulePacks: [])
                ],
                Prompts:
                [
                    new BuildKitPromptDescriptor(
                        PromptId: "tempo",
                        Kind: BuildKitPromptKinds.Toggle,
                        Label: "Keep the quick-start lane?",
                        Options:
                        [
                            new BuildKitPromptOption("yes", "Yes", "Stay on the fastest legal starter path."),
                            new BuildKitPromptOption("no", "No", "Open the broader planning lane before you commit.")
                        ],
                        Required: true)
                ],
                Actions:
                [
                    new BuildKitActionDescriptor(
                        ActionId: "edge-bundle",
                        Kind: BuildKitActionKinds.AddBundle,
                        TargetId: "edge-runner.bundle")
                ],
                Visibility: ArtifactVisibilityModes.Public,
                TrustTier: ArtifactTrustTiers.Curated),
            Owner: new OwnerScope("system"),
            Visibility: ArtifactVisibilityModes.Public,
            PublicationStatus: BuildKitPublicationStatuses.Published,
            UpdatedAtUtc: DateTimeOffset.Parse("2026-03-28T12:10:00+00:00"))
    ];

    public IReadOnlyList<BuildKitRegistryEntry> List(OwnerScope owner, string? rulesetId = null)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        IEnumerable<BuildKitRegistryEntry> entries = BuiltInEntries;
        if (normalizedRulesetId is not null)
        {
            entries = entries.Where(entry => entry.Manifest.Targets.Contains(normalizedRulesetId, StringComparer.Ordinal));
        }

        return entries
            .OrderBy(static entry => entry.Manifest.Targets.FirstOrDefault() ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Manifest.Title, StringComparer.Ordinal)
            .ToArray();
    }

    public BuildKitRegistryEntry? Get(OwnerScope owner, string buildKitId, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildKitId);

        return List(owner, rulesetId)
            .FirstOrDefault(entry => string.Equals(entry.Manifest.BuildKitId, buildKitId, StringComparison.Ordinal));
    }
}
