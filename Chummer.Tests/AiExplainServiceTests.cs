#nullable enable annotations

using System;
using System.Collections.Generic;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiExplainServiceTests
{
    [TestMethod]
    public void Default_ai_explain_service_projects_capability_backed_explain_details()
    {
        DefaultAiExplainService service = new(
            new StubAiDigestService(),
            new StubRulesetPluginRegistry(new Sr5RulesetPlugin()));

        AiExplainValueProjection? projection = service.GetExplainValue(
            OwnerScope.LocalSingleUser,
            new AiExplainValueQuery(
                RuntimeFingerprint: "sha256:coach",
                CharacterId: "char-7",
                CapabilityId: RulePackCapabilityIds.SessionQuickActions,
                RulesetId: "sr5"));

        Assert.IsNotNull(projection);
        Assert.AreEqual(RulePackCapabilityIds.SessionQuickActions, projection.CapabilityId);
        Assert.AreEqual("sha256:coach", projection.RuntimeFingerprint);
        Assert.AreEqual("sr5", projection.RulesetId);
        Assert.AreEqual(AiExplainEntryKinds.QuickActionAvailability, projection.Kind);
        Assert.AreEqual($"ruleset.capability.{RulePackCapabilityIds.SessionQuickActions}.title", projection.TitleKey);
        Assert.AreEqual(RulesetCapabilityInvocationKinds.Script, projection.InvocationKind);
        Assert.IsTrue(projection.Explainable);
        Assert.IsTrue(projection.SessionSafe);
        Assert.IsGreaterThanOrEqualTo(4, projection.Fragments?.Count ?? 0);
        Assert.IsGreaterThanOrEqualTo(1, projection.Diagnostics?.Count ?? 0);
        Assert.IsFalse(string.IsNullOrWhiteSpace(projection.SummaryKey));
    }

    [TestMethod]
    public void Default_ai_explain_service_returns_null_for_missing_runtime_or_capability()
    {
        DefaultAiExplainService service = new(
            new StubAiDigestService(runtimeSummary: null, characterDigest: null),
            new StubRulesetPluginRegistry(new Sr5RulesetPlugin()));

        Assert.IsNull(service.GetExplainValue(
            OwnerScope.LocalSingleUser,
            new AiExplainValueQuery(RuntimeFingerprint: "sha256:missing", CapabilityId: RulePackCapabilityIds.SessionQuickActions)));

        DefaultAiExplainService missingCapabilityService = new(
            new StubAiDigestService(),
            new StubRulesetPluginRegistry(new Sr5RulesetPlugin()));

        Assert.IsNull(missingCapabilityService.GetExplainValue(
            OwnerScope.LocalSingleUser,
            new AiExplainValueQuery(RuntimeFingerprint: "sha256:coach", CapabilityId: "missing.capability")));
    }

    private sealed class StubAiDigestService : IAiDigestService
    {
        private readonly AiRuntimeSummaryProjection? _runtimeSummary;
        private readonly AiCharacterDigestProjection? _characterDigest;

        public StubAiDigestService(
            AiRuntimeSummaryProjection? runtimeSummary = null,
            AiCharacterDigestProjection? characterDigest = null)
        {
            _runtimeSummary = runtimeSummary ?? new AiRuntimeSummaryProjection(
                RuntimeFingerprint: "sha256:coach",
                RulesetId: "sr5",
                Title: "Street-Level Runtime Lock",
                CatalogKind: RuntimeLockCatalogKinds.Saved,
                EngineApiVersion: "1.0.0",
                ContentBundles: ["official.sr5.core@1.0.0"],
                RulePacks: ["campaign.street-level@2.0.0"],
                ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [RulePackCapabilityIds.DeriveStat] = "campaign.street-level:derive.stat"
                },
                Visibility: ArtifactVisibilityModes.LocalOnly,
                Description: "Street-level runtime lock");
            _characterDigest = characterDigest ?? new AiCharacterDigestProjection(
                CharacterId: "char-7",
                DisplayName: "Cipher (Ghostwire)",
                RulesetId: "sr5",
                RuntimeFingerprint: "sha256:coach",
                Summary: new CharacterFileSummary(
                    Name: "Cipher",
                    Alias: "Ghostwire",
                    Metatype: "Human",
                    BuildMethod: "Priority",
                    CreatedVersion: "5",
                    AppVersion: "10",
                    Karma: 18m,
                    Nuyen: 1500m,
                    Created: true),
                LastUpdatedUtc: new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero),
                HasSavedWorkspace: true);
        }

        public AiRuntimeSummaryProjection? GetRuntimeSummary(OwnerScope owner, string runtimeFingerprint, string? rulesetId = null)
            => _runtimeSummary is not null && string.Equals(_runtimeSummary.RuntimeFingerprint, runtimeFingerprint, StringComparison.Ordinal)
                ? _runtimeSummary
                : null;

        public AiCharacterDigestProjection? GetCharacterDigest(OwnerScope owner, string characterId)
            => _characterDigest is not null && string.Equals(_characterDigest.CharacterId, characterId, StringComparison.Ordinal)
                ? _characterDigest
                : null;

        public AiSessionDigestProjection? GetSessionDigest(OwnerScope owner, string characterId) => null;
    }

    private sealed class StubRulesetPluginRegistry : IRulesetPluginRegistry
    {
        private readonly IRulesetPlugin[] _plugins;

        public StubRulesetPluginRegistry(params IRulesetPlugin[] plugins)
        {
            _plugins = plugins;
        }

        public IReadOnlyList<IRulesetPlugin> All => _plugins;

        public IRulesetPlugin? Resolve(string? rulesetId)
        {
            string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            foreach (IRulesetPlugin plugin in _plugins)
            {
                if (string.Equals(plugin.Id.NormalizedValue, normalizedRulesetId, StringComparison.Ordinal))
                {
                    return plugin;
                }
            }

            return null;
        }
    }
}
