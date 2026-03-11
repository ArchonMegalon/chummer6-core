#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Infrastructure.Files;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class ManifestStoreTests
{
    [TestMethod]
    public void File_rulepack_manifest_store_persists_owner_scoped_records()
    {
        string stateDirectory = CreateTempDirectory();

        try
        {
            FileRulePackManifestStore store = new(stateDirectory);
            RulePackManifestRecord record = new(
                new RulePackManifest(
                    PackId: "house-rules",
                    Version: "1.0.0",
                    Title: "House Rules",
                    Author: "alice",
                    Description: "Persisted rulepack.",
                    Targets: [RulesetDefaults.Sr5, RulesetDefaults.Sr6],
                    EngineApiVersion: "rulepack-v1",
                    DependsOn: [],
                    ConflictsWith: [],
                    Visibility: ArtifactVisibilityModes.Private,
                    TrustTier: ArtifactTrustTiers.Private,
                    Assets: [],
                    Capabilities: [],
                    ExecutionPolicies: []));

            store.Upsert(new OwnerScope("alice"), record);

            RulePackManifestRecord? sr5 = store.Get(new OwnerScope("alice"), "house-rules", "1.0.0", RulesetDefaults.Sr5);
            IReadOnlyList<RulePackManifestRecord> sr6 = store.List(new OwnerScope("alice"), RulesetDefaults.Sr6);
            IReadOnlyList<RulePackManifestRecord> hiddenFromBob = store.List(new OwnerScope("bob"), RulesetDefaults.Sr5);

            Assert.IsNotNull(sr5);
            CollectionAssert.AreEquivalent(
                new[] { RulesetDefaults.Sr5, RulesetDefaults.Sr6 },
                sr5.Manifest.Targets.ToArray());
            Assert.HasCount(1, sr6);
            Assert.IsEmpty(hiddenFromBob);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void File_ruleprofile_manifest_store_persists_owner_scoped_records()
    {
        string stateDirectory = CreateTempDirectory();

        try
        {
            FileRuleProfileManifestStore store = new(stateDirectory);
            RuleProfileManifestRecord record = new(
                new RuleProfileManifest(
                    ProfileId: "campaign.seattle.runtime",
                    Title: "Seattle Runtime",
                    Description: "Persisted runtime profile.",
                    RulesetId: RulesetDefaults.Sr5,
                    Audience: RuleProfileAudienceKinds.Campaign,
                    CatalogKind: RuleProfileCatalogKinds.Personal,
                    RulePacks: [],
                    DefaultToggles:
                    [
                        new RuleProfileDefaultToggle(
                            ToggleId: "creation.street-scum",
                            Value: "true",
                            Label: "Street Scum")
                    ],
                    RuntimeLock: new ResolvedRuntimeLock(
                        RulesetId: RulesetDefaults.Sr5,
                        ContentBundles:
                        [
                            new ContentBundleDescriptor(
                                BundleId: "official.sr5.base",
                                RulesetId: RulesetDefaults.Sr5,
                                Version: "schema-5",
                                Title: "SR5 Base Content",
                                Description: "Base content.",
                                AssetPaths: ["data/", "lang/"])
                        ],
                        RulePacks: [],
                        ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal),
                        EngineApiVersion: "rulepack-v1",
                        RuntimeFingerprint: "sha256:runtime"),
                    UpdateChannel: RuleProfileUpdateChannels.Preview,
                    Notes: "Owner-scoped profile."));

            store.Upsert(new OwnerScope("alice"), record);

            RuleProfileManifestRecord? reloaded = store.Get(new OwnerScope("alice"), "campaign.seattle.runtime", RulesetDefaults.Sr5);
            IReadOnlyList<RuleProfileManifestRecord> hiddenFromBob = store.List(new OwnerScope("bob"), RulesetDefaults.Sr5);

            Assert.IsNotNull(reloaded);
            Assert.AreEqual("Seattle Runtime", reloaded.Manifest.Title);
            Assert.AreEqual("sha256:runtime", reloaded.Manifest.RuntimeLock.RuntimeFingerprint);
            Assert.IsEmpty(hiddenFromBob);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"chummer-manifest-store-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
