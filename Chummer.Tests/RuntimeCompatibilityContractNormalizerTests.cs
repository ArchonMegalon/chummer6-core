#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class RuntimeCompatibilityContractNormalizerTests
{
    [TestMethod]
    public void NormalizeRuntimeLockInstallCandidate_sorts_diagnostics_and_derives_can_install_and_runtime_fingerprint()
    {
        RuntimeLockInstallCandidate normalized = RuntimeCompatibilityContractNormalizer.NormalizeRuntimeLockInstallCandidate(
            new RuntimeLockInstallCandidate(
                TargetKind: "workspace",
                TargetId: "ws-1",
                Entry: new RuntimeLockRegistryEntry(
                    LockId: "lock-1",
                    Owner: OwnerScope.LocalSingleUser,
                    Title: "Runtime Lock",
                    Visibility: ArtifactVisibilityModes.LocalOnly,
                    CatalogKind: RuntimeLockCatalogKinds.Saved,
                    RuntimeLock: CreateRuntimeLock(),
                    UpdatedAtUtc: DateTimeOffset.UtcNow,
                    Install: new ArtifactInstallState(ArtifactInstallStates.Available)),
                Diagnostics:
                [
                    new RuntimeLockCompatibilityDiagnostic(
                        State: RuntimeLockCompatibilityStates.MissingPack,
                        Message: "missing pack",
                        RequiredRulesetId: "sr6",
                        RequiredRuntimeFingerprint: "sha256:z"),
                    new RuntimeLockCompatibilityDiagnostic(
                        State: RuntimeLockCompatibilityStates.Compatible,
                        Message: "compatible",
                        RequiredRulesetId: "sr5",
                        RequiredRuntimeFingerprint: "sha256:a",
                        MessageKey: " compatibility.ok ",
                        MessageParameters:
                        [
                            new RulesetExplainParameter(" zeta ", RulesetCapabilityBridge.FromObject(2)),
                            new RulesetExplainParameter(" alpha ", RulesetCapabilityBridge.FromObject(1))
                        ])
                ]));

        Assert.IsFalse(normalized.CanInstall);
        Assert.AreEqual("sha256:runtime", normalized.Entry.Install.RuntimeFingerprint);
        CollectionAssert.AreEqual(
            new[] { RuntimeLockCompatibilityStates.Compatible, RuntimeLockCompatibilityStates.MissingPack },
            normalized.Diagnostics.Select(diagnostic => diagnostic.State).ToArray());
        Assert.AreEqual(" compatibility.ok ", normalized.Diagnostics[0].MessageKey);
        CollectionAssert.AreEqual(
            new[] { "alpha", "zeta" },
            normalized.Diagnostics[0].MessageParameters.Select(parameter => parameter.Name).ToArray());
        Assert.AreEqual("missing pack", normalized.Diagnostics[1].MessageKey);
    }

    [TestMethod]
    public void NormalizeBuildKitManifest_trims_sorts_and_deduplicates_manifest_surfaces()
    {
        BuildKitManifest normalized = RuntimeCompatibilityContractNormalizer.NormalizeBuildKitManifest(
            new BuildKitManifest(
                BuildKitId: "build-kit",
                Version: "1.0.0",
                Title: "Build Kit",
                Description: "Desc",
                Targets: [" sr6 ", "sr5", "sr6"],
                RuntimeRequirements:
                [
                    new BuildKitRuntimeRequirement(
                        RulesetId: " sr6 ",
                        RequiredRuntimeFingerprints: [" sha256:b ", "sha256:a", "sha256:a"],
                        RequiredRulePacks:
                        [
                            new ArtifactVersionReference(" z.pack ", " 2.0.0 "),
                            new ArtifactVersionReference(" a.pack ", " 1.0.0 ")
                        ])
                ],
                Prompts:
                [
                    new BuildKitPromptDescriptor(
                        PromptId: " z-prompt ",
                        Kind: " choice ",
                        Label: " Z Prompt ",
                        Options:
                        [
                            new BuildKitPromptOption(" z ", " Z ", " desc "),
                            new BuildKitPromptOption(" a ", " A ")
                        ]),
                    new BuildKitPromptDescriptor(
                        PromptId: " a-prompt ",
                        Kind: " toggle ",
                        Label: " A Prompt ",
                        Options: [])
                ],
                Actions:
                [
                    new BuildKitActionDescriptor(" z-action ", " apply-choice ", " target-z ", Notes: " note "),
                    new BuildKitActionDescriptor(" a-action ", " set-metadata ", " target-a ", PromptId: " a-prompt ")
                ],
                Visibility: " public ",
                TrustTier: " curated "));

        CollectionAssert.AreEqual(new[] { "sr5", "sr6" }, normalized.Targets.ToArray());
        CollectionAssert.AreEqual(new[] { "sha256:a", "sha256:b" }, normalized.RuntimeRequirements[0].RequiredRuntimeFingerprints.ToArray());
        CollectionAssert.AreEqual(new[] { "a.pack", "z.pack" }, normalized.RuntimeRequirements[0].RequiredRulePacks.Select(pack => pack.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "a-prompt", "z-prompt" }, normalized.Prompts.Select(prompt => prompt.PromptId).ToArray());
        CollectionAssert.AreEqual(new[] { "a", "z" }, normalized.Prompts[1].Options.Select(option => option.OptionId).ToArray());
        CollectionAssert.AreEqual(new[] { "a-action", "z-action" }, normalized.Actions.Select(action => action.ActionId).ToArray());
        Assert.AreEqual("public", normalized.Visibility);
        Assert.AreEqual("curated", normalized.TrustTier);
    }

    private static ResolvedRuntimeLock CreateRuntimeLock()
        => new(
            RulesetId: "sr5",
            ContentBundles:
            [
                new ContentBundleDescriptor("bundle-z", "sr5", "2.0.0", "Z", "Z bundle", ["b.xml", "a.xml"]),
                new ContentBundleDescriptor("bundle-a", "sr5", "1.0.0", "A", "A bundle", ["c.xml"])
            ],
            RulePacks:
            [
                new ArtifactVersionReference("z.pack", "2.0.0"),
                new ArtifactVersionReference("a.pack", "1.0.0")
            ],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["z.capability"] = "provider-z",
                ["a.capability"] = "provider-a"
            },
            EngineApiVersion: "1.0.0",
            RuntimeFingerprint: "sha256:runtime");
}
